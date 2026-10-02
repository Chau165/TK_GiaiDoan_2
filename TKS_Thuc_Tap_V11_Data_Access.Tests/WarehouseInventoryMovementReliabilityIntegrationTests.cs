using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseInventoryMovementReliabilityIntegrationTests
{
    private const string DirectDmlProbeUser = "Phase10MovementDmlProbe";
    private static string ConnectionString
    {
        get
        {
            return Environment.GetEnvironmentVariable("TKS_INTEGRATION_CONNECTION_STRING") ?? "Server=localhost;Database=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True;TrustServerCertificate=True;";
        }
    }

    [Fact]
    public void Deployment_post_script_does_not_override_the_canonical_movement_aware_post_procedure()
    {
        var v_DeploymentScript = File.ReadAllText(FindRepositoryFile("Database/WarehouseDocumentPosting.Procedures.sql"));

        Assert.DoesNotContain("CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Post", v_DeploymentScript);
    }

    [Fact]
    public async Task Duplicate_daily_invalidations_are_merged_and_a_stale_claim_is_requeued()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
            var v_dtmDay = new DateTime(2099, 3, 10);

            await ApplyInvalidationAsync(v_Connection, v_Transaction, v_Scope, v_dtmDay);
            await ExecuteAsync(v_Connection, v_Transaction,
                "UPDATE dbo.InventoryMovement_RebuildQueue SET Status = N'PROCESSING', Claimed_Version = Requested_Version WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND From_Date = @MovementDate;",
                BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId), Date("@MovementDate", v_dtmDay));

            await ApplyInvalidationAsync(v_Connection, v_Transaction, v_Scope, v_dtmDay);
            var v_Claimed = await ReadQueueStateAsync(v_Connection, v_Transaction, v_Scope, v_dtmDay);

            Assert.Equal("PROCESSING", v_Claimed.Status);
            Assert.Equal(2, v_Claimed.RequestedVersion);
            Assert.Equal(1, v_Claimed.ClaimedVersion.GetValueOrDefault());

            var v_Duplicate = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventoryMovement_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, To_Date, Status) VALUES (@WarehouseId, @ProductId, @MovementDate, @MovementDate, N'WAITING');",
                BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId), Date("@MovementDate", v_dtmDay)));
            Assert.Contains(v_Duplicate.Number, new[] { 2601, 2627 });

            await ExecuteStoredAsync(v_Connection, v_Transaction, "dbo.sp_Inventory_Movement_Complete_Claim",
                BigInt("@Queue_ID", v_Claimed.QueueId), Int("@Claimed_Version", v_Claimed.ClaimedVersion.GetValueOrDefault()));

            var v_Superseded = await ReadQueueStateAsync(v_Connection, v_Transaction, v_Scope, v_dtmDay);
            Assert.Equal("WAITING", v_Superseded.Status);
            Assert.Equal(2, v_Superseded.RequestedVersion);
            Assert.Null(v_Superseded.NextRetryAt);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Scope_lock_contention_retries_then_moves_the_queue_to_dead_letter()
    {
        var v_Scope = await CreatePersistentScopeAsync();
        var v_dtmDay = new DateTime(2099, 3, 11);

        try
        {
            await ApplyInvalidationAsync(v_Scope, v_dtmDay);

            await using var v_LockConnection = new SqlConnection(ConnectionString);
            await v_LockConnection.OpenAsync();
            await using var v_LockTransaction = v_LockConnection.BeginTransaction();
            await AcquireExclusiveLockAsync(v_LockConnection, v_LockTransaction, ScopeResource(v_Scope));

            await ProcessQueueAsync(p_iMaxRetryCount: 2, p_iRetryDelaySeconds: 0);
            var v_Retrying = await ReadQueueStateAsync(v_Scope, v_dtmDay);
            Assert.Equal("RETRY_WAITING", v_Retrying.Status);
            Assert.Equal(1, v_Retrying.RetryCount);
            Assert.NotNull(v_Retrying.LastAttemptAt);
            Assert.NotNull(v_Retrying.NextRetryAt);
            Assert.False(string.IsNullOrWhiteSpace(v_Retrying.LastError));

            await ProcessQueueAsync(p_iMaxRetryCount: 2, p_iRetryDelaySeconds: 0);
            var v_DeadLettered = await ReadQueueStateAsync(v_Scope, v_dtmDay);
            Assert.Equal("FAILED_FINAL", v_DeadLettered.Status);
            Assert.Equal(2, v_DeadLettered.RetryCount);
            Assert.Equal(1, await CountAsync(v_Scope, v_dtmDay, "dbo.InventoryMovement_RebuildDeadLetter"));
        }
        finally
        {
            await CleanupPersistentScopeAsync(v_Scope);
        }
    }

    [Fact]
    public async Task Scope_lock_contention_is_retried_then_succeeds_after_fence_release()
    {
        var v_Scope = await CreatePersistentScopeAsync();
        var v_dtmDay = new DateTime(2099, 3, 17);

        try
        {
            await SeedValidSnapshotAnchorAsync(v_Scope, new DateTime(2099, 3, 1));
            await ApplyInvalidationAsync(v_Scope, v_dtmDay);

            await using var v_LockConnection = new SqlConnection(ConnectionString);
            await v_LockConnection.OpenAsync();
            await using var v_LockTransaction = v_LockConnection.BeginTransaction();
            await AcquireExclusiveLockAsync(v_LockConnection, v_LockTransaction, ScopeResource(v_Scope));

            await ProcessQueueAsync(p_iMaxRetryCount: 3, p_iRetryDelaySeconds: 0);
            var v_Retrying = await ReadQueueStateAsync(v_Scope, v_dtmDay);
            Assert.Equal("RETRY_WAITING", v_Retrying.Status);
            Assert.Equal(1, v_Retrying.RetryCount);

            await v_LockTransaction.RollbackAsync();
            await ProcessQueueAsync(p_iMaxRetryCount: 3, p_iRetryDelaySeconds: 0);

            var v_Completed = await ReadQueueStateAsync(v_Scope, v_dtmDay);
            Assert.Equal("COMPLETED", v_Completed.Status);
            Assert.Equal(1, v_Completed.RetryCount);
            Assert.Equal(0, await CountAsync(v_Scope, v_dtmDay, "dbo.InventoryMovement_RebuildDeadLetter"));
        }
        finally
        {
            await CleanupPersistentScopeAsync(v_Scope);
        }
    }

    [Theory]
    [InlineData(51403, "Inventory fence Root acquisition failed. Result=-1", true)]
    [InlineData(51403, "Inventory fence Root acquisition failed. Result=-2", true)]
    [InlineData(51403, "Inventory fence Root acquisition failed. Result=-3", true)]
    [InlineData(51407, "Inventory fence Group acquisition failed. Result=-1", true)]
    [InlineData(51412, "Inventory fence legacy Scope acquisition failed. Result=-1", true)]
    [InlineData(51424, "Inventory fence legacy Snapshot acquisition failed. Result=-1", true)]
    [InlineData(51428, "Inventory movement bootstrap compatibility fence is busy.", true)]
    [InlineData(51431, "Legacy snapshot bootstrap compatibility fence is busy.", true)]
    [InlineData(51403, "Inventory fence Root acquisition failed. Result=-999", false)]
    [InlineData(51403, "Inventory fence Root acquisition failed. Result=-10", false)]
    [InlineData(51406, "Inventory fence Group requires Root first.", false)]
    [InlineData(51413, "Inventory fence legacy Scope verification failed.", false)]
    [InlineData(51420, "Inventory fence context is missing required Group mode.", false)]
    public async Task Fence_classifier_retries_only_expected_contention(
        int p_iErrorNumber,
        string p_ErrorMessage,
        bool p_bExpectedTransient)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();

        var v_objActual = await ScalarAsync(v_Connection, null,
            "SELECT dbo.fn_Inventory_Fence_Is_Transient_Contention(@ErrorNumber, @ErrorMessage);",
            Int("@ErrorNumber", p_iErrorNumber), Text("@ErrorMessage", p_ErrorMessage, 4000));

        Assert.Equal(p_bExpectedTransient, Convert.ToBoolean(v_objActual));
    }

    [Fact]
    public async Task Balance_daily_rebuild_is_rejected_while_another_worker_owns_the_scope()
    {
        var v_Scope = await CreatePersistentScopeAsync();

        try
        {
            await using var v_LockConnection = new SqlConnection(ConnectionString);
            await v_LockConnection.OpenAsync();
            await using var v_LockTransaction = v_LockConnection.BeginTransaction();
            await AcquireExclusiveLockAsync(v_LockConnection, v_LockTransaction, ScopeResource(v_Scope));

            await using var v_WorkerConnection = new SqlConnection(ConnectionString);
            await v_WorkerConnection.OpenAsync();

            var v_Error = await Assert.ThrowsAsync<SqlException>(() => ExecuteStoredAsync(
                v_WorkerConnection,
                null,
                "dbo.sp_Inventory_Balance_Daily_Rebuild",
                BigInt("@Kho_ID", v_Scope.WarehouseId),
                BigInt("@San_Pham_ID", v_Scope.ProductId),
                Date("@From_Date", new DateTime(2099, 3, 20))));

            Assert.Equal(51412, v_Error.Number);
            Assert.Contains("Inventory fence legacy Scope acquisition failed", v_Error.Message, StringComparison.Ordinal);
            await v_LockTransaction.RollbackAsync();
        }
        finally
        {
            await CleanupPersistentScopeAsync(v_Scope);
        }
    }

    [Fact]
    public async Task Expired_processing_claim_is_recovered_by_a_later_worker()
    {
        var v_Scope = await CreatePersistentScopeAsync();
        var v_dtmDay = new DateTime(2099, 3, 15);

        try
        {
            await SeedValidSnapshotAnchorAsync(v_Scope, new DateTime(2099, 3, 1));
            await ApplyInvalidationAsync(v_Scope, v_dtmDay);
            await ExecuteAsync(
                "UPDATE dbo.InventoryMovement_RebuildQueue SET Status = N'PROCESSING', Claimed_Version = Requested_Version, LastAttemptAt = '2000-01-01' WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND From_Date = @MovementDate;",
                BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId), Date("@MovementDate", v_dtmDay));

            await ProcessQueueAsync(p_iMaxRetryCount: 2, p_iRetryDelaySeconds: 0);
            var v_Recovered = await ReadQueueStateAsync(v_Scope, v_dtmDay);

            Assert.Equal("COMPLETED", v_Recovered.Status);
            Assert.Equal(1, v_Recovered.RetryCount);
        }
        finally
        {
            await CleanupPersistentScopeAsync(v_Scope);
        }
    }

    [Fact]
    public async Task A_second_idle_movement_worker_tick_does_not_reapply_completed_work()
    {
        var v_Scope = await CreatePersistentScopeAsync();
        var v_dtmDay = new DateTime(2099, 3, 16);

        try
        {
            await SeedValidSnapshotAnchorAsync(v_Scope, new DateTime(2099, 3, 1));
            await ApplyInvalidationAsync(v_Scope, v_dtmDay);
            await ProcessQueueAsync(p_iMaxRetryCount: 2, p_iRetryDelaySeconds: 0);

            var v_FirstQueue = await ReadQueueStateAsync(v_Scope, v_dtmDay);
            Assert.Equal("COMPLETED", v_FirstQueue.Status);
            var v_iFirstMovementRows = await CountProjectionRowsAsync(v_Scope, v_dtmDay, "dbo.Inventory_Movement_Daily", "Movement_Date");
            var v_iFirstBalanceRows = await CountProjectionRowsAsync(v_Scope, v_dtmDay, "dbo.Inventory_Balance_Daily", "Balance_Date");

            await ProcessQueueAsync(p_iMaxRetryCount: 2, p_iRetryDelaySeconds: 0);

            var v_SecondQueue = await ReadQueueStateAsync(v_Scope, v_dtmDay);
            Assert.Equal("COMPLETED", v_SecondQueue.Status);
            Assert.Equal(v_iFirstMovementRows, await CountProjectionRowsAsync(v_Scope, v_dtmDay, "dbo.Inventory_Movement_Daily", "Movement_Date"));
            Assert.Equal(v_iFirstBalanceRows, await CountProjectionRowsAsync(v_Scope, v_dtmDay, "dbo.Inventory_Balance_Daily", "Balance_Date"));
        }
        finally
        {
            await CleanupPersistentScopeAsync(v_Scope);
        }
    }

    [Fact]
    public async Task Bootstrap_gate_blocks_worker_claims_and_document_posting()
    {
        var v_Scope = await CreatePostableScopeAsync();
        var v_dtmDay = new DateTime(2099, 3, 12);

        try
        {
            await ApplyInvalidationAsync(v_Scope, v_dtmDay);
            var receiptId = await CreateDraftReceiptAsync(v_Scope, new DateTime(2099, 3, 13), p_Quantity: 5m);

            await using var v_LockConnection = new SqlConnection(ConnectionString);
            await v_LockConnection.OpenAsync();
            await using var v_LockTransaction = v_LockConnection.BeginTransaction();
            await AcquireExclusiveLockAsync(v_LockConnection, v_LockTransaction, "InventoryMovement:Bootstrap");

            await ProcessQueueAsync(p_iMaxRetryCount: 3, p_iRetryDelaySeconds: 0);
            var v_Retrying = await ReadQueueStateAsync(v_Scope, v_dtmDay);
            Assert.Equal("RETRY_WAITING", v_Retrying.Status);

            var v_PostError = await Assert.ThrowsAsync<SqlException>(() => PostReceiptAsync(v_Scope.Login!, receiptId));
            Assert.Equal(51428, v_PostError.Number);
            Assert.Contains("compatibility fence is busy", v_PostError.Message, StringComparison.Ordinal);

            await v_LockTransaction.RollbackAsync();
        }
        finally
        {
            await CleanupPersistentScopeAsync(v_Scope);
        }
    }

    [Fact]
    public async Task Direct_update_of_a_posted_detail_is_rejected()
    {
        var v_Scope = await CreatePostableScopeAsync();

        try
        {
            var receiptId = await CreateDraftReceiptAsync(v_Scope, new DateTime(2099, 3, 14), p_Quantity: 10m);
            await PostReceiptAsync(v_Scope.Login!, receiptId);

            var v_Error = await Assert.ThrowsAsync<SqlException>(() => ExecutePostedDetailMutationAsProbeAsync(
                "UPDATE d SET SL_Nhap = SL_Nhap + 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d WHERE d.Nhap_Kho_ID = @DocumentId;",
                BigInt("@DocumentId", receiptId)));

            Assert.Equal(51228, v_Error.Number);
        }
        finally
        {
            await CleanupPersistentScopeAsync(v_Scope);
        }
    }

    private static async Task ApplyInvalidationAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, Scope p_Scope, DateTime p_dtmDay)
    {
        await ExecuteAsync(p_Connection, p_Transaction,
            """
            DECLARE @Affected dbo.InventoryMovementAffectedType;
            INSERT @Affected(Kho_ID, San_Pham_ID, Movement_Date, InvalidReason)
            VALUES (@WarehouseId, @ProductId, @MovementDate, N'TDD_RELIABILITY');
            EXEC dbo.sp_Inventory_Movement_Apply_Invalidation @Affected = @Affected;
            """,
            BigInt("@WarehouseId", p_Scope.WarehouseId), BigInt("@ProductId", p_Scope.ProductId), Date("@MovementDate", p_dtmDay));
    }

    private static async Task ApplyInvalidationAsync(Scope p_Scope, DateTime p_dtmDay)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await ApplyInvalidationAsync(v_Connection, null, p_Scope, p_dtmDay);
    }

    private static Task SeedValidSnapshotAnchorAsync(Scope p_Scope, DateTime p_dtmAnchorDate)
    {
        return ExecuteAsync("INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@AnchorDate, @WarehouseId, @ProductId, 0, 1, 1);", Date("@AnchorDate", p_dtmAnchorDate), BigInt("@WarehouseId", p_Scope.WarehouseId), BigInt("@ProductId", p_Scope.ProductId));
    }

    private static async Task<Scope> CreateScopeAsync(SqlConnection p_Connection, SqlTransaction p_Transaction)
    {
        var productId = await ScalarLongAsync(p_Connection, p_Transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
        var v_Tag = $"TDD-MOVEMENT-{Guid.NewGuid():N}"[..25];
        var warehouseId = await ScalarLongAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'TDD movement reliability');",
            Text("@Name", v_Tag, 255));
        return new Scope(warehouseId, productId, v_Tag, null, null);
    }

    private static async Task<Scope> CreatePersistentScopeAsync()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        var productId = await ScalarLongAsync(v_Connection, null, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
        var v_Tag = $"TDD-MOVEMENT-{Guid.NewGuid():N}"[..25];
        var warehouseId = await ScalarLongAsync(v_Connection, null,
            "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'TDD movement reliability');",
            Text("@Name", v_Tag, 255));
        return new Scope(warehouseId, productId, v_Tag, null, null);
    }

    private static async Task<Scope> CreatePostableScopeAsync()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
        var supplierId = await ScalarLongAsync(v_Connection, v_Transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
        var v_Login = $"{v_Scope.Tag}-login";
        var memberId = await ScalarLongAsync(v_Connection, v_Transaction, "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");

        await ExecuteAsync(v_Connection, v_Transaction,
            "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, N'TDD movement reliability', 0);",
            BigInt("@MemberId", memberId), Text("@Login", v_Login, 100));
        await ExecuteAsync(v_Connection, v_Transaction,
            "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
            Text("@Login", v_Login, 100), BigInt("@WarehouseId", v_Scope.WarehouseId));
        await v_Transaction.CommitAsync();
        return v_Scope with { Login = v_Login, SupplierId = supplierId };
    }

    private static async Task<long> CreateDraftReceiptAsync(Scope p_Scope, DateTime p_dtmDay, decimal p_Quantity)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        var documentId = await ScalarLongAsync(v_Connection, null,
            "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, @MovementDate, 0, N'TDD movement reliability'); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
            Text("@Number", $"{p_Scope.Tag}-receipt-{Guid.NewGuid():N}", 100), BigInt("@WarehouseId", p_Scope.WarehouseId), BigInt("@SupplierId", p_Scope.SupplierId!.Value), Date("@MovementDate", p_dtmDay));
        await ExecuteAsync(v_Connection, null,
            "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
            BigInt("@DocumentId", documentId), BigInt("@ProductId", p_Scope.ProductId), Decimal("@Quantity", p_Quantity));
        return documentId;
    }

    private static async Task PostReceiptAsync(string p_Login, long receiptId)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await ExecuteStoredAsync(v_Connection, null, "dbo.sp_XNK_Document_Post",
            Bit("@Is_Receipt", true), BigInt("@Document_ID", receiptId), Text("@Ma_Dang_Nhap", p_Login, 100));
    }

    private static async Task ProcessQueueAsync(int p_iMaxRetryCount, int p_iRetryDelaySeconds)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await ExecuteStoredAsync(v_Connection, null, "dbo.sp_Inventory_Movement_Process_RebuildQueue",
            Int("@Batch_Size", 1), Int("@Max_Retry_Count", p_iMaxRetryCount), Int("@Base_Retry_Delay_Seconds", p_iRetryDelaySeconds));
    }

    private static async Task AcquireExclusiveLockAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Resource)
    {
        await ExecuteAsync(p_Connection, p_Transaction,
            """
            DECLARE @Result INT;
            EXEC @Result = sys.sp_getapplock @Resource = @Resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
            IF @Result < 0 THROW 52901, N'TDD could not acquire its applock.', 1;
            """,
            Text("@Resource", p_Resource, 255));
    }

    private static async Task<QueueState> ReadQueueStateAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, Scope p_Scope, DateTime p_dtmDay)
    {
        await using var v_Command = new SqlCommand(
            "SELECT TOP (1) ID, Status, Retry_Count, Requested_Version, Claimed_Version, LastAttemptAt, NextRetryAt, LastError FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND From_Date = @MovementDate ORDER BY ID DESC;",
            p_Connection, p_Transaction);
        v_Command.Parameters.Add(BigInt("@WarehouseId", p_Scope.WarehouseId));
        v_Command.Parameters.Add(BigInt("@ProductId", p_Scope.ProductId));
        v_Command.Parameters.Add(Date("@MovementDate", p_dtmDay));
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        int? v_iClaimedVersion;
        if (v_Reader.IsDBNull(4))
        {
            v_iClaimedVersion = null;
        }
        else
        {
            v_iClaimedVersion = v_Reader.GetInt32(4);
        }

        DateTime? v_dtmLastAttemptAt;
        if (v_Reader.IsDBNull(5))
        {
            v_dtmLastAttemptAt = null;
        }
        else
        {
            v_dtmLastAttemptAt = v_Reader.GetDateTime(5);
        }

        DateTime? v_dtmNextRetryAt;
        if (v_Reader.IsDBNull(6))
        {
            v_dtmNextRetryAt = null;
        }
        else
        {
            v_dtmNextRetryAt = v_Reader.GetDateTime(6);
        }

        string? v_LastError;
        if (v_Reader.IsDBNull(7))
        {
            v_LastError = null;
        }
        else
        {
            v_LastError = v_Reader.GetString(7);
        }

        return new QueueState(
            v_Reader.GetInt64(0),
            v_Reader.GetString(1),
            v_Reader.GetInt32(2),
            v_Reader.GetInt32(3),
            v_iClaimedVersion,
            v_dtmLastAttemptAt,
            v_dtmNextRetryAt,
            v_LastError);
    }

    private static async Task<QueueState> ReadQueueStateAsync(Scope p_Scope, DateTime p_dtmDay)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        return await ReadQueueStateAsync(v_Connection, null, p_Scope, p_dtmDay);
    }

    private static async Task<int> CountAsync(Scope p_Scope, DateTime p_dtmDay, string p_TableName)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        return Convert.ToInt32(await ScalarAsync(v_Connection, null,
            $"SELECT COUNT(*) FROM {p_TableName} WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND From_Date = @MovementDate;",
            BigInt("@WarehouseId", p_Scope.WarehouseId), BigInt("@ProductId", p_Scope.ProductId), Date("@MovementDate", p_dtmDay)));
    }

    private static async Task<int> CountProjectionRowsAsync(Scope p_Scope, DateTime p_dtmDay, string p_TableName, string p_DateColumn)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        return Convert.ToInt32(await ScalarAsync(v_Connection, null,
            $"SELECT COUNT(*) FROM {p_TableName} WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND {p_DateColumn} = @MovementDate;",
            BigInt("@WarehouseId", p_Scope.WarehouseId), BigInt("@ProductId", p_Scope.ProductId), Date("@MovementDate", p_dtmDay)));
    }

    private static async Task CleanupPersistentScopeAsync(Scope p_Scope)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            await ExecuteAsync(v_Connection, v_Transaction,
                "IF OBJECT_ID(N'dbo.InventoryMovement_RebuildDeadLetter', N'U') IS NOT NULL DELETE dl FROM dbo.InventoryMovement_RebuildDeadLetter dl JOIN dbo.InventoryMovement_RebuildQueue q ON q.ID = dl.Queue_ID WHERE q.Kho_ID = @WarehouseId;",
                BigInt("@WarehouseId", p_Scope.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", p_Scope.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", p_Scope.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction, "IF OBJECT_ID(N'dbo.Inventory_Balance_Daily_Scope', N'U') IS NOT NULL DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", p_Scope.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction, "IF OBJECT_ID(N'dbo.Inventory_Balance_Daily', N'U') IS NOT NULL DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", p_Scope.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", p_Scope.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", p_Scope.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", p_Scope.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE d FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = d.Nhap_Kho_ID WHERE h.Kho_ID = @WarehouseId;",
                BigInt("@WarehouseId", p_Scope.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", p_Scope.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", p_Scope.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", p_Scope.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.tbl_DM_Kho_User WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", p_Scope.WarehouseId));
            if (p_Scope.Login is not null)
                await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Login;", Text("@Login", p_Scope.Login, 100));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;", BigInt("@WarehouseId", p_Scope.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = NULL;");
            await v_Transaction.CommitAsync();
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static string ScopeResource(Scope p_Scope)
    {
        return $"InventoryMovement:{p_Scope.WarehouseId}:{p_Scope.ProductId}";
    }

    private static string FindRepositoryFile(string p_RelativePath)
    {
        for (var v_Directory = new DirectoryInfo(Directory.GetCurrentDirectory()); v_Directory is not null; v_Directory = v_Directory.Parent)
        {
            var v_Candidate = Path.Combine(v_Directory.FullName, p_RelativePath);
            if (File.Exists(v_Candidate))
                return v_Candidate;
        }

        throw new FileNotFoundException($"Could not locate repository file '{p_RelativePath}'.");
    }

    private static async Task ExecuteAsync(string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await ExecuteAsync(v_Connection, null, p_Sql, p_arrParameters);
    }

    private static async Task ExecuteAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteStoredAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Procedure, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task ExecutePostedDetailMutationAsProbeAsync(string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await EnsureDirectDmlProbeUserAsync();
        try
        {
            await using var v_Connection = new SqlConnection(ConnectionString);
            await v_Connection.OpenAsync();
            try
            {
                await ExecuteAsync(
                    v_Connection,
                    null,
                    $"EXECUTE AS USER = N'{DirectDmlProbeUser}'; EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1; {p_Sql}",
                    p_arrParameters);
            }
            finally
            {
                await ExecuteAsync(v_Connection, null, "REVERT;");
            }
        }
        finally
        {
            await DropDirectDmlProbeUserAsync();
        }
    }

    private static async Task EnsureDirectDmlProbeUserAsync()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await ExecuteAsync(
            v_Connection,
            null,
            $"""
            SET QUOTED_IDENTIFIER ON;
            IF DATABASE_PRINCIPAL_ID(N'{DirectDmlProbeUser}') IS NULL
                CREATE USER [{DirectDmlProbeUser}] WITHOUT LOGIN;
            GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.tbl_XNK_Nhap_Kho_Raw_Data TO [{DirectDmlProbeUser}];
            GRANT SELECT ON OBJECT::dbo.tbl_XNK_Nhap_Kho TO [{DirectDmlProbeUser}];
            """);
    }

    private static async Task DropDirectDmlProbeUserAsync()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await ExecuteAsync(v_Connection, null, $"IF DATABASE_PRINCIPAL_ID(N'{DirectDmlProbeUser}') IS NOT NULL DROP USER [{DirectDmlProbeUser}];");
    }

    private static async Task<long> ScalarLongAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToInt64(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
    }

    private static async Task<object?> ScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        return await v_Command.ExecuteScalarAsync();
    }

    private static SqlParameter Text(string p_Name, string p_Value, int p_iSize)
    {
        return new(p_Name, SqlDbType.NVarChar, p_iSize)
        {
            Value = p_Value
        };
    }
    private static SqlParameter BigInt(string p_Name, long value)
    {
        return new(p_Name, SqlDbType.BigInt)
        {
            Value = value
        };
    }
    private static SqlParameter Int(string p_Name, int p_iValue)
    {
        return new(p_Name, SqlDbType.Int)
        {
            Value = p_iValue
        };
    }
    private static SqlParameter Bit(string p_Name, bool p_bValue)
    {
        return new(p_Name, SqlDbType.Bit)
        {
            Value = p_bValue
        };
    }
    private static SqlParameter Date(string p_Name, DateTime p_dtmValue)
    {
        return new(p_Name, SqlDbType.Date)
        {
            Value = p_dtmValue.Date
        };
    }
    private static SqlParameter Decimal(string p_Name, decimal p_Value)
    {
        return new(p_Name, SqlDbType.Decimal)
        {
            Precision = 18,
            Scale = 3,
            Value = p_Value
        };
    }

    private sealed record Scope(long WarehouseId, long ProductId, string Tag, string? Login, long? SupplierId);
    private sealed record QueueState(long QueueId, string Status, int RetryCount, int RequestedVersion, int? ClaimedVersion, DateTime? LastAttemptAt, DateTime? NextRetryAt, string? LastError);
}
