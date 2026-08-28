using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseInventoryMovementReliabilityIntegrationTests
{
    private const string ConnectionString = "Server=localhost;Database=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True;TrustServerCertificate=True;";

    [Fact]
    public void Deployment_post_script_does_not_override_the_canonical_movement_aware_post_procedure()
    {
        var deploymentScript = File.ReadAllText(FindRepositoryFile("Database/WarehouseDocumentPosting.Procedures.sql"));

        Assert.DoesNotContain("CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Post", deploymentScript);
    }

    [Fact]
    public async Task Duplicate_daily_invalidations_are_merged_and_a_stale_claim_is_requeued()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreateScopeAsync(connection, transaction);
            var day = new DateTime(2099, 3, 10);

            await ApplyInvalidationAsync(connection, transaction, scope, day);
            await ExecuteAsync(connection, transaction,
                "UPDATE dbo.InventoryMovement_RebuildQueue SET Status = N'PROCESSING', Claimed_Version = Requested_Version WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND From_Date = @MovementDate;",
                BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId), Date("@MovementDate", day));

            await ApplyInvalidationAsync(connection, transaction, scope, day);
            var claimed = await ReadQueueStateAsync(connection, transaction, scope, day);

            Assert.Equal("PROCESSING", claimed.Status);
            Assert.Equal(2, claimed.RequestedVersion);
            Assert.Equal(1, claimed.ClaimedVersion.GetValueOrDefault());

            var duplicate = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, transaction,
                "INSERT dbo.InventoryMovement_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, To_Date, Status) VALUES (@WarehouseId, @ProductId, @MovementDate, @MovementDate, N'WAITING');",
                BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId), Date("@MovementDate", day)));
            Assert.Contains(duplicate.Number, new[] { 2601, 2627 });

            await ExecuteStoredAsync(connection, transaction, "dbo.sp_Inventory_Movement_Complete_Claim",
                BigInt("@Queue_ID", claimed.QueueId), Int("@Claimed_Version", claimed.ClaimedVersion.GetValueOrDefault()));

            var superseded = await ReadQueueStateAsync(connection, transaction, scope, day);
            Assert.Equal("WAITING", superseded.Status);
            Assert.Equal(2, superseded.RequestedVersion);
            Assert.Null(superseded.NextRetryAt);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Scope_lock_contention_retries_then_moves_the_queue_to_dead_letter()
    {
        var scope = await CreatePersistentScopeAsync();
        var day = new DateTime(2099, 3, 11);

        try
        {
            await ApplyInvalidationAsync(scope, day);

            await using var lockConnection = new SqlConnection(ConnectionString);
            await lockConnection.OpenAsync();
            await using var lockTransaction = lockConnection.BeginTransaction();
            await AcquireExclusiveLockAsync(lockConnection, lockTransaction, ScopeResource(scope));

            await ProcessQueueAsync(maxRetryCount: 2, retryDelaySeconds: 0);
            var retrying = await ReadQueueStateAsync(scope, day);
            Assert.Equal("RETRY_WAITING", retrying.Status);
            Assert.Equal(1, retrying.RetryCount);
            Assert.NotNull(retrying.LastAttemptAt);
            Assert.NotNull(retrying.NextRetryAt);
            Assert.False(string.IsNullOrWhiteSpace(retrying.LastError));

            await ProcessQueueAsync(maxRetryCount: 2, retryDelaySeconds: 0);
            var deadLettered = await ReadQueueStateAsync(scope, day);
            Assert.Equal("FAILED_FINAL", deadLettered.Status);
            Assert.Equal(2, deadLettered.RetryCount);
            Assert.Equal(1, await CountAsync(scope, day, "dbo.InventoryMovement_RebuildDeadLetter"));
        }
        finally
        {
            await CleanupPersistentScopeAsync(scope);
        }
    }

    [Fact]
    public async Task Expired_processing_claim_is_recovered_by_a_later_worker()
    {
        var scope = await CreatePersistentScopeAsync();
        var day = new DateTime(2099, 3, 15);

        try
        {
            await ApplyInvalidationAsync(scope, day);
            await ExecuteAsync(
                "UPDATE dbo.InventoryMovement_RebuildQueue SET Status = N'PROCESSING', Claimed_Version = Requested_Version, LastAttemptAt = '2000-01-01' WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND From_Date = @MovementDate;",
                BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId), Date("@MovementDate", day));

            await ProcessQueueAsync(maxRetryCount: 2, retryDelaySeconds: 0);
            var recovered = await ReadQueueStateAsync(scope, day);

            Assert.Equal("COMPLETED", recovered.Status);
            Assert.Equal(1, recovered.RetryCount);
        }
        finally
        {
            await CleanupPersistentScopeAsync(scope);
        }
    }

    [Fact]
    public async Task Bootstrap_gate_blocks_worker_claims_and_document_posting()
    {
        var scope = await CreatePostableScopeAsync();
        var day = new DateTime(2099, 3, 12);

        try
        {
            await ApplyInvalidationAsync(scope, day);
            var receiptId = await CreateDraftReceiptAsync(scope, new DateTime(2099, 3, 13), quantity: 5m);

            await using var lockConnection = new SqlConnection(ConnectionString);
            await lockConnection.OpenAsync();
            await using var lockTransaction = lockConnection.BeginTransaction();
            await AcquireExclusiveLockAsync(lockConnection, lockTransaction, "InventoryMovement:Bootstrap");

            await ProcessQueueAsync(maxRetryCount: 3, retryDelaySeconds: 0);
            var retrying = await ReadQueueStateAsync(scope, day);
            Assert.Equal("RETRY_WAITING", retrying.Status);

            var postError = await Assert.ThrowsAsync<SqlException>(() => PostReceiptAsync(scope.Login!, receiptId));
            Assert.Equal(51226, postError.Number);

            await lockTransaction.RollbackAsync();
        }
        finally
        {
            await CleanupPersistentScopeAsync(scope);
        }
    }

    [Fact]
    public async Task Direct_update_of_a_posted_detail_is_rejected()
    {
        var scope = await CreatePostableScopeAsync();

        try
        {
            var receiptId = await CreateDraftReceiptAsync(scope, new DateTime(2099, 3, 14), quantity: 10m);
            await PostReceiptAsync(scope.Login!, receiptId);

            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
                "UPDATE d SET SL_Nhap = SL_Nhap + 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d WHERE d.Nhap_Kho_ID = @DocumentId;",
                BigInt("@DocumentId", receiptId)));

            Assert.Equal(51228, error.Number);
        }
        finally
        {
            await CleanupPersistentScopeAsync(scope);
        }
    }

    private static async Task ApplyInvalidationAsync(SqlConnection connection, SqlTransaction? transaction, Scope scope, DateTime day)
    {
        await ExecuteAsync(connection, transaction,
            """
            DECLARE @Affected dbo.InventoryMovementAffectedType;
            INSERT @Affected(Kho_ID, San_Pham_ID, Movement_Date, InvalidReason)
            VALUES (@WarehouseId, @ProductId, @MovementDate, N'TDD_RELIABILITY');
            EXEC dbo.sp_Inventory_Movement_Apply_Invalidation @Affected = @Affected;
            """,
            BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId), Date("@MovementDate", day));
    }

    private static async Task ApplyInvalidationAsync(Scope scope, DateTime day)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ApplyInvalidationAsync(connection, null, scope, day);
    }

    private static async Task<Scope> CreateScopeAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var productId = await ScalarLongAsync(connection, transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
        var tag = $"TDD-MOVEMENT-{Guid.NewGuid():N}"[..25];
        var warehouseId = await ScalarLongAsync(connection, transaction,
            "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'TDD movement reliability');",
            Text("@Name", tag, 255));
        return new Scope(warehouseId, productId, tag, null, null);
    }

    private static async Task<Scope> CreatePersistentScopeAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var productId = await ScalarLongAsync(connection, null, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
        var tag = $"TDD-MOVEMENT-{Guid.NewGuid():N}"[..25];
        var warehouseId = await ScalarLongAsync(connection, null,
            "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'TDD movement reliability');",
            Text("@Name", tag, 255));
        return new Scope(warehouseId, productId, tag, null, null);
    }

    private static async Task<Scope> CreatePostableScopeAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        var scope = await CreateScopeAsync(connection, transaction);
        var supplierId = await ScalarLongAsync(connection, transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
        var login = $"{scope.Tag}-login";
        var memberId = await ScalarLongAsync(connection, transaction, "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");

        await ExecuteAsync(connection, transaction,
            "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, N'TDD movement reliability', 0);",
            BigInt("@MemberId", memberId), Text("@Login", login, 100));
        await ExecuteAsync(connection, transaction,
            "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
            Text("@Login", login, 100), BigInt("@WarehouseId", scope.WarehouseId));
        await transaction.CommitAsync();
        return scope with { Login = login, SupplierId = supplierId };
    }

    private static async Task<long> CreateDraftReceiptAsync(Scope scope, DateTime day, decimal quantity)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var documentId = await ScalarLongAsync(connection, null,
            "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, @SupplierId, @MovementDate, 0, N'TDD movement reliability');",
            Text("@Number", $"{scope.Tag}-receipt-{Guid.NewGuid():N}", 100), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@SupplierId", scope.SupplierId!.Value), Date("@MovementDate", day));
        await ExecuteAsync(connection, null,
            "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
            BigInt("@DocumentId", documentId), BigInt("@ProductId", scope.ProductId), Decimal("@Quantity", quantity));
        return documentId;
    }

    private static async Task PostReceiptAsync(string login, long receiptId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteStoredAsync(connection, null, "dbo.sp_XNK_Document_Post",
            Bit("@Is_Receipt", true), BigInt("@Document_ID", receiptId), Text("@Ma_Dang_Nhap", login, 100));
    }

    private static async Task ProcessQueueAsync(int maxRetryCount, int retryDelaySeconds)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteStoredAsync(connection, null, "dbo.sp_Inventory_Movement_Process_RebuildQueue",
            Int("@Batch_Size", 1), Int("@Max_Retry_Count", maxRetryCount), Int("@Base_Retry_Delay_Seconds", retryDelaySeconds));
    }

    private static async Task AcquireExclusiveLockAsync(SqlConnection connection, SqlTransaction transaction, string resource)
    {
        await ExecuteAsync(connection, transaction,
            """
            DECLARE @Result INT;
            EXEC @Result = sys.sp_getapplock @Resource = @Resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
            IF @Result < 0 THROW 52901, N'TDD could not acquire its applock.', 1;
            """,
            Text("@Resource", resource, 255));
    }

    private static async Task<QueueState> ReadQueueStateAsync(SqlConnection connection, SqlTransaction? transaction, Scope scope, DateTime day)
    {
        await using var command = new SqlCommand(
            "SELECT TOP (1) ID, Status, Retry_Count, Requested_Version, Claimed_Version, LastAttemptAt, NextRetryAt, LastError FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND From_Date = @MovementDate ORDER BY ID DESC;",
            connection, transaction);
        command.Parameters.Add(BigInt("@WarehouseId", scope.WarehouseId));
        command.Parameters.Add(BigInt("@ProductId", scope.ProductId));
        command.Parameters.Add(Date("@MovementDate", day));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new QueueState(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetDateTime(5),
            reader.IsDBNull(6) ? null : reader.GetDateTime(6),
            reader.IsDBNull(7) ? null : reader.GetString(7));
    }

    private static async Task<QueueState> ReadQueueStateAsync(Scope scope, DateTime day)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await ReadQueueStateAsync(connection, null, scope, day);
    }

    private static async Task<int> CountAsync(Scope scope, DateTime day, string tableName)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return Convert.ToInt32(await ScalarAsync(connection, null,
            $"SELECT COUNT(*) FROM {tableName} WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND From_Date = @MovementDate;",
            BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId), Date("@MovementDate", day)));
    }

    private static async Task CleanupPersistentScopeAsync(Scope scope)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await ExecuteAsync(connection, transaction,
                "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            await ExecuteAsync(connection, transaction,
                "IF OBJECT_ID(N'dbo.InventoryMovement_RebuildDeadLetter', N'U') IS NOT NULL DELETE dl FROM dbo.InventoryMovement_RebuildDeadLetter dl JOIN dbo.InventoryMovement_RebuildQueue q ON q.ID = dl.Queue_ID WHERE q.Kho_ID = @WarehouseId;",
                BigInt("@WarehouseId", scope.WarehouseId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", scope.WarehouseId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", scope.WarehouseId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", scope.WarehouseId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", scope.WarehouseId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", scope.WarehouseId));
            await ExecuteAsync(connection, transaction,
                "DELETE d FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = d.Nhap_Kho_ID WHERE h.Kho_ID = @WarehouseId;",
                BigInt("@WarehouseId", scope.WarehouseId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", scope.WarehouseId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", scope.WarehouseId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", scope.WarehouseId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.tbl_DM_Kho_User WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", scope.WarehouseId));
            if (scope.Login is not null)
                await ExecuteAsync(connection, transaction, "DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Login;", Text("@Login", scope.Login, 100));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;", BigInt("@WarehouseId", scope.WarehouseId));
            await ExecuteAsync(connection, transaction,
                "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = NULL;");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static string ScopeResource(Scope scope) => $"InventoryMovement:{scope.WarehouseId}:{scope.ProductId}";

    private static string FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }

    private static async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, null, sql, parameters);
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteStoredAsync(SqlConnection connection, SqlTransaction? transaction, string procedure, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedure, connection, transaction) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarLongAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) => Convert.ToInt64(await ScalarAsync(connection, transaction, sql, parameters));

    private static async Task<object?> ScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    private static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.NVarChar, size) { Value = value };
    private static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt) { Value = value };
    private static SqlParameter Int(string name, int value) => new(name, SqlDbType.Int) { Value = value };
    private static SqlParameter Bit(string name, bool value) => new(name, SqlDbType.Bit) { Value = value };
    private static SqlParameter Date(string name, DateTime value) => new(name, SqlDbType.Date) { Value = value.Date };
    private static SqlParameter Decimal(string name, decimal value) => new(name, SqlDbType.Decimal) { Precision = 18, Scale = 3, Value = value };

    private sealed record Scope(long WarehouseId, long ProductId, string Tag, string? Login, long? SupplierId);
    private sealed record QueueState(long QueueId, string Status, int RetryCount, int RequestedVersion, int? ClaimedVersion, DateTime? LastAttemptAt, DateTime? NextRetryAt, string? LastError);
}
