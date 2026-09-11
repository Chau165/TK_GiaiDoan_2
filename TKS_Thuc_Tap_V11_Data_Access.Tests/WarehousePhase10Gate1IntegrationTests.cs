using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehousePhase10Gate1IntegrationTests
{
    private const string DirectDmlProbeUser = "Phase10Gate1DmlProbe";
    private static string ConnectionString => WarehouseTestDatabase.ConnectionString;

    [Theory]
    [InlineData("INSERT")]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    public async Task N02_posted_issue_detail_is_immutable_at_database_boundary(string mutation)
    {
        await EnsureDirectDmlProbeUserAsync();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        var scope = await CreatePostedIssueScopeAsync(connection, transaction);
        var impersonated = false;

        try
        {
            await ExecuteAsync(connection, transaction,
                $"EXECUTE AS USER = N'{DirectDmlProbeUser}'; EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            impersonated = true;

            var sql = mutation switch
            {
                "INSERT" => "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@IssueId, @ProductId, 1, 1);",
                "UPDATE" => "UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data SET SL_Xuat = SL_Xuat + 1 WHERE Auto_ID = @DetailId;",
                "DELETE" => "DELETE dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @DetailId;",
                _ => throw new ArgumentOutOfRangeException(nameof(mutation))
            };

            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, transaction, sql,
                BigInt("@IssueId", scope.IssueId), BigInt("@ProductId", scope.ProductId), BigInt("@DetailId", scope.DetailId)));

            Assert.Equal(51228, error.Number);
        }
        finally
        {
            if (impersonated)
                await ExecuteAsync(connection, transaction, "REVERT;");

            await transaction.RollbackAsync();
            await DropDirectDmlProbeUserAsync();
        }
    }

    [Fact]
    public async Task N03_daily_rebuild_includes_snapshot_to_from_date_bridge()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        var scope = await CreateBasicScopeAsync(connection, transaction, "TDD-N03-RECEIPT");
        var anchorDate = new DateTime(2099, 1, 1);
        var bridgeDate = new DateTime(2099, 1, 3);
        var fromDate = new DateTime(2099, 1, 5);

        try
        {
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, 100, 1, 1);",
                Date("@Date", anchorDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.Inventory_Movement_Daily(Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 20, 0, 1), (@FromDate, @WarehouseId, @ProductId, 30, 0, 1);",
                Date("@Date", bridgeDate), Date("@FromDate", fromDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));

            await ExecuteStoredAsync(connection, transaction, "dbo.sp_Inventory_Balance_Daily_Rebuild",
                BigInt("@Kho_ID", scope.WarehouseId), BigInt("@San_Pham_ID", scope.ProductId), Date("@From_Date", fromDate), Bit("@Scope_Lock_Held", false));

            var row = await ReadDailyAsync(connection, transaction, scope, fromDate);
            Assert.Equal(120m, row.OpeningQuantity);
            Assert.Equal(30m, row.TotalReceived);
            Assert.Equal(0m, row.TotalIssued);
            Assert.Equal(150m, row.ClosingQuantity);
            Assert.Equal(50m, row.CumulativeReceived);
            Assert.Equal(0m, row.CumulativeIssued);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task N03_daily_rebuild_includes_issue_bridge_without_double_counting_from_date()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        var scope = await CreateBasicScopeAsync(connection, transaction, "TDD-N03-ISSUE");
        var anchorDate = new DateTime(2099, 2, 1);
        var bridgeDate = new DateTime(2099, 2, 3);
        var fromDate = new DateTime(2099, 2, 5);

        try
        {
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, 100, 1, 1);",
                Date("@Date", anchorDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.Inventory_Movement_Daily(Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 20, 1), (@FromDate, @WarehouseId, @ProductId, 30, 0, 1);",
                Date("@Date", bridgeDate), Date("@FromDate", fromDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));

            await ExecuteStoredAsync(connection, transaction, "dbo.sp_Inventory_Balance_Daily_Rebuild",
                BigInt("@Kho_ID", scope.WarehouseId), BigInt("@San_Pham_ID", scope.ProductId), Date("@From_Date", fromDate), Bit("@Scope_Lock_Held", false));

            var row = await ReadDailyAsync(connection, transaction, scope, fromDate);
            Assert.Equal(80m, row.OpeningQuantity);
            Assert.Equal(30m, row.TotalReceived);
            Assert.Equal(0m, row.TotalIssued);
            Assert.Equal(110m, row.ClosingQuantity);
            Assert.Equal(30m, row.CumulativeReceived);
            Assert.Equal(20m, row.CumulativeIssued);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task N05_delete_header_releases_reservation_when_save_detail_writes_after_delete_waits()
    {
        var scope = await CreatePersistentIssueScopeAsync("TDD-N05-DELETE-WINS");
        await using var saveConnection = new SqlConnection(ConnectionString);
        await using var deleteConnection = new SqlConnection(ConnectionString);
        await saveConnection.OpenAsync();
        await deleteConnection.OpenAsync();
        await using var saveTransaction = saveConnection.BeginTransaction();
        await using var deleteTransaction = deleteConnection.BeginTransaction();

        try
        {
            await ExecuteAsync(saveConnection, saveTransaction,
                "SELECT Auto_ID FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @IssueId;",
                BigInt("@IssueId", scope.IssueId));

            var deleteSpid = await IntScalarAsync(deleteConnection, deleteTransaction, "SELECT @@SPID;");
            var deleteTask = ExecuteStoredAsync(deleteConnection, deleteTransaction, "dbo.sp_XNK_Xuat_Kho_Delete_Header",
                BigInt("@Auto_ID", scope.IssueId), Text("@Last_Updated_By", scope.Login, 100),
                Text("@Last_Updated_By_Function", "TDD", 100), Text("@Ma_Dang_Nhap", scope.Login, 100));

            Assert.True(await WaitForLockWaitAsync(deleteSpid), "Delete Header did not expose the expected parent-lock wait.");

            await SaveIssueDetailAsync(saveConnection, saveTransaction, scope, quantity: 1m);
            await saveTransaction.CommitAsync();
            await deleteTask;
            await deleteTransaction.CommitAsync();

            await AssertReservationInvariantAsync(scope, headerShouldExist: false);
        }
        finally
        {
            if (saveTransaction.Connection is not null)
            {
                try { await saveTransaction.RollbackAsync(); } catch { }
            }
            if (deleteTransaction.Connection is not null)
            {
                try { await deleteTransaction.RollbackAsync(); } catch { }
            }
            await CleanupPersistentIssueScopeAsync(scope);
        }
    }

    [Fact]
    public async Task N05_save_detail_wins_before_delete_and_delete_releases_all_reservations()
    {
        var scope = await CreatePersistentIssueScopeAsync("TDD-N05-SAVE-WINS");
        try
        {
            await using (var connection = new SqlConnection(ConnectionString))
            {
                await connection.OpenAsync();
                await using var transaction = connection.BeginTransaction();
                await SaveIssueDetailAsync(connection, transaction, scope, quantity: 1m);
                await transaction.CommitAsync();
            }

            await using (var connection = new SqlConnection(ConnectionString))
            {
                await connection.OpenAsync();
                await ExecuteStoredAsync(connection, null, "dbo.sp_XNK_Xuat_Kho_Delete_Header",
                    BigInt("@Auto_ID", scope.IssueId), Text("@Last_Updated_By", scope.Login, 100),
                    Text("@Last_Updated_By_Function", "TDD", 100), Text("@Ma_Dang_Nhap", scope.Login, 100));
            }

            await AssertReservationInvariantAsync(scope, headerShouldExist: false);
        }
        finally
        {
            await CleanupPersistentIssueScopeAsync(scope);
        }
    }

    private static async Task<Scope> CreatePostedIssueScopeAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var scope = await CreateBasicScopeAsync(connection, transaction, "TDD-N02");
        await ExecuteAsync(connection, transaction,
            "INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, 8, 0);",
            BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
        await ExecuteAsync(connection, transaction,
            "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1; INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted) VALUES (@ReceiptNumber, @WarehouseId, @SupplierId, '2099-01-01', 1); DECLARE @ReceiptId BIGINT = SCOPE_IDENTITY(); INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@ReceiptId, @ProductId, 10, 1); INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted) VALUES (@IssueNumber, @WarehouseId, '2099-01-02', 1); DECLARE @IssueId BIGINT = SCOPE_IDENTITY(); INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@IssueId, @ProductId, 2, 1); EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = NULL;",
            Text("@ReceiptNumber", $"{scope.Tag}-receipt", 100), Text("@IssueNumber", $"{scope.Tag}-issue", 100),
            BigInt("@WarehouseId", scope.WarehouseId), BigInt("@SupplierId", scope.SupplierId), BigInt("@ProductId", scope.ProductId));
        return scope with { IssueId = await LongScalarAsync(connection, transaction,
            "SELECT TOP (1) Auto_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho = @Number;",
            Text("@Number", $"{scope.Tag}-issue", 100)), DetailId = await LongScalarAsync(connection, transaction,
            "SELECT TOP (1) d.Auto_ID FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID WHERE h.So_Phieu_Xuat_Kho = @Number;",
            Text("@Number", $"{scope.Tag}-issue", 100))};
    }

    private static async Task<Scope> CreatePersistentIssueScopeAsync(string prefix)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        var scope = await CreateBasicScopeAsync(connection, transaction, prefix);
        await ExecuteAsync(connection, transaction,
            "INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, 10, 0); INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted) VALUES (@Number, @WarehouseId, '2099-03-01', 0);",
            Text("@Number", $"{scope.Tag}-issue", 100), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
        var issueId = await LongScalarAsync(connection, transaction,
            "SELECT Auto_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho = @Number;",
            Text("@Number", $"{scope.Tag}-issue", 100));
        await transaction.CommitAsync();
        return scope with { IssueId = issueId, DetailId = 0 };
    }

    private static async Task<long> SaveIssueDetailAsync(SqlConnection connection, SqlTransaction transaction, Scope scope, decimal quantity)
    {
        return await ExecuteStoredIdAsync(connection, transaction, "dbo.sp_XNK_Xuat_Kho_Save_Detail",
            BigIntOutput("@Auto_ID", 0), BigInt("@Xuat_Kho_ID", scope.IssueId), BigInt("@San_Pham_ID", scope.ProductId),
            Decimal("@SL_Xuat", quantity), Decimal("@Don_Gia_Xuat", 1), Text("@Ma_Dang_Nhap", scope.Login, 100),
            Text("@Created_By", scope.Login, 100), Text("@Created_By_Function", "TDD", 100),
            Text("@Last_Updated_By", scope.Login, 100), Text("@Last_Updated_By_Function", "TDD", 100));
    }

    private static async Task<Scope> CreateBasicScopeAsync(SqlConnection connection, SqlTransaction transaction, string prefix)
    {
        var productId = await LongScalarAsync(connection, transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
        var supplierId = await LongScalarAsync(connection, transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
        var tag = $"{prefix}-{Guid.NewGuid():N}"[..40];
        var warehouseId = await LongScalarAsync(connection, transaction,
            "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10 Gate 1');",
            Text("@Name", tag, 255));
        var login = $"{tag}-login";
        var memberId = await LongScalarAsync(connection, transaction,
            "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
        await ExecuteAsync(connection, transaction,
            "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, N'Phase 10 Gate 1', 0); INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
            BigInt("@MemberId", memberId), Text("@Login", login, 100), BigInt("@WarehouseId", warehouseId));
        return new Scope(warehouseId, productId, supplierId, tag, login, 0, 0);
    }

    private static async Task EnsureDirectDmlProbeUserAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(
            connection,
            null,
            $"""
            SET QUOTED_IDENTIFIER ON;
            IF DATABASE_PRINCIPAL_ID(N'{DirectDmlProbeUser}') IS NULL
                CREATE USER [{DirectDmlProbeUser}] WITHOUT LOGIN;
            GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.tbl_XNK_Xuat_Kho_Raw_Data TO [{DirectDmlProbeUser}];
            GRANT SELECT ON OBJECT::dbo.tbl_XNK_Xuat_Kho TO [{DirectDmlProbeUser}];
            """);
    }

    private static async Task DropDirectDmlProbeUserAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, null, $"IF DATABASE_PRINCIPAL_ID(N'{DirectDmlProbeUser}') IS NOT NULL DROP USER [{DirectDmlProbeUser}];");
    }

    private static async Task AssertReservationInvariantAsync(Scope scope, bool headerShouldExist)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var headerCount = await IntScalarAsync(connection, null,
            "SELECT COUNT(*) FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @IssueId;", BigInt("@IssueId", scope.IssueId));
        var reservationSum = await DecimalScalarAsync(connection, null,
            "SELECT COALESCE(SUM(ReservedQuantity), 0) FROM dbo.InventoryReservation_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
        var currentReserved = await DecimalScalarAsync(connection, null,
            "SELECT ReservedQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
        Assert.Equal(headerShouldExist ? 1 : 0, headerCount);
        Assert.Equal(reservationSum, currentReserved);
        Assert.Equal(0m, reservationSum);
    }

    private static async Task CleanupPersistentIssueScopeAsync(Scope scope)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await ExecuteAsync(connection, transaction,
                "DELETE r FROM dbo.InventoryReservation_Current r JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Auto_ID = r.Xuat_Kho_Detail_ID JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID WHERE h.Auto_ID = @IssueId; DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @IssueId; DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.tbl_DM_Kho_User WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;",
                BigInt("@IssueId", scope.IssueId), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId), Text("@Login", scope.Login, 100));
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<bool> WaitForLockWaitAsync(int sessionId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var waitType = await ScalarAsync(connection, null,
                "SELECT wait_type FROM sys.dm_exec_requests WHERE session_id = @SessionId AND wait_type LIKE N'LCK_M_%';",
                Int("@SessionId", sessionId));
            if (waitType is string text && text.StartsWith("LCK_M_", StringComparison.Ordinal))
                return true;
            await Task.Delay(25);
        }
        return false;
    }

    private static async Task<DailyRow> ReadDailyAsync(SqlConnection connection, SqlTransaction transaction, Scope scope, DateTime date)
    {
        await using var command = new SqlCommand(
            "SELECT OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued FROM dbo.Inventory_Balance_Daily WHERE Balance_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            connection, transaction);
        command.Parameters.Add(Date("@Date", date));
        command.Parameters.Add(BigInt("@WarehouseId", scope.WarehouseId));
        command.Parameters.Add(BigInt("@ProductId", scope.ProductId));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new DailyRow(reader.GetDecimal(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5));
    }

    private static async Task ExecuteStoredAsync(SqlConnection connection, SqlTransaction? transaction, string procedure, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedure, connection, transaction) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ExecuteStoredIdAsync(SqlConnection connection, SqlTransaction? transaction, string procedure, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedure, connection, transaction) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
        return Convert.ToInt64(command.Parameters["@Auto_ID"].Value);
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> IntScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) => Convert.ToInt32(await ScalarAsync(connection, transaction, sql, parameters));
    private static async Task<long> LongScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) => Convert.ToInt64(await ScalarAsync(connection, transaction, sql, parameters));
    private static async Task<decimal> DecimalScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) => Convert.ToDecimal(await ScalarAsync(connection, transaction, sql, parameters));

    private static async Task<object?> ScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    private static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.NVarChar, size) { Value = value };
    private static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt) { Value = value };
    private static SqlParameter BigIntOutput(string name, long value) => new(name, SqlDbType.BigInt) { Direction = ParameterDirection.InputOutput, Value = value };
    private static SqlParameter Int(string name, int value) => new(name, SqlDbType.Int) { Value = value };
    private static SqlParameter Bit(string name, bool value) => new(name, SqlDbType.Bit) { Value = value };
    private static SqlParameter Date(string name, DateTime value) => new(name, SqlDbType.Date) { Value = value.Date };
    private static SqlParameter Decimal(string name, decimal value) => new(name, SqlDbType.Decimal) { Precision = 18, Scale = 3, Value = value };

    private sealed record Scope(long WarehouseId, long ProductId, long SupplierId, string Tag, string Login, long IssueId, long DetailId);
    private sealed record DailyRow(decimal OpeningQuantity, decimal TotalReceived, decimal TotalIssued, decimal ClosingQuantity, decimal CumulativeReceived, decimal CumulativeIssued);
}
