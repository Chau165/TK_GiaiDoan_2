using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehousePhase10H03IntegrationTests
{
    private static string ConnectionString => WarehouseTestDatabase.ConnectionString;

    [Fact]
    public async Task H03_nonpaged_period_report_uses_period_closing_not_current_quantity()
    {
        var fixture = await CreateNonPagedFixtureAsync();
        try
        {
            var actualClosing = await ReadNonPagedClosingAsync(fixture);

            // A future Posted receipt makes Current=150, but the requested
            // period ends today and its Daily/Snapshot closing is still 100.
            Assert.Equal(100m, actualClosing);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task H03_paged_report_rejects_when_scope_fence_is_held()
    {
        var fixture = await CreatePagedFixtureAsync();
        await using var writerConnection = new SqlConnection(ConnectionString);
        await writerConnection.OpenAsync();
        await using var writerTransaction = writerConnection.BeginTransaction();

        try
        {
            await AcquireExclusiveScopeLockAsync(writerConnection, writerTransaction, fixture);

            await using var reportConnection = new SqlConnection(ConnectionString);
            await reportConnection.OpenAsync();
            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteStoredAsync(
                reportConnection,
                null,
                "dbo.sp_BC_Xuat_Nhap_Ton_Page",
                Date("@Tu_Ngay", fixture.BusinessDate),
                Date("@Den_Ngay", fixture.BusinessDate),
                Int("@Page_Number", 1),
                Int("@Page_Size", 10),
                Text("@Ma_Dang_Nhap", fixture.Login, 100),
                BigInt("@Kho_ID", fixture.WarehouseId)));

            Assert.Equal(51323, error.Number);
        }
        finally
        {
            try { await writerTransaction.RollbackAsync(); } catch { }
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task N01_current_reconciliation_includes_future_only_posted_scope()
    {
        var fixture = await CreateFutureOnlyReconciliationFixtureAsync();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        try
        {
            var runParameter = BigIntOutput("@Run_ID", 0);
            await ExecuteStoredAsync(
                connection,
                null,
                "dbo.sp_Inventory_Reconciliation_Run",
                runParameter,
                BigInt("@Kho_ID", fixture.WarehouseId),
                BigInt("@San_Pham_ID", fixture.ProductId));

            var runId = Convert.ToInt64(runParameter.Value);
            var resultCount = Convert.ToInt32(await ScalarAsync(
                connection,
                null,
                "SELECT COUNT(*) FROM dbo.InventoryReconciliation_Result WHERE Run_ID = @RunId;",
                BigInt("@RunId", runId)));
            var failedCount = Convert.ToInt32(await ScalarAsync(
                connection,
                null,
                "SELECT COUNT(*) FROM dbo.InventoryReconciliation_Result WHERE Run_ID = @RunId AND Status = N'FAIL';",
                BigInt("@RunId", runId)));

            Assert.Equal(1, resultCount);
            Assert.Equal(1, failedCount);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    private static async Task<ReportFixture> CreateNonPagedFixtureAsync()
    {
        var fixture = await CreateBaseFixtureAsync("TDD-H03-NONPAGED", includeDaily: false);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var supplierId = await LongScalarAsync(connection, null, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
        var receiptId = await ExecuteStoredIdAsync(connection, null, "dbo.sp_XNK_Nhap_Kho_Save_Header",
            BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Nhap_Kho", $"{fixture.Tag}-future-receipt", 100), BigInt("@Kho_ID", fixture.WarehouseId),
            BigInt("@NCC_ID", supplierId), Date("@Ngay_Nhap_Kho", fixture.BusinessDate.AddDays(1)), Text("@Ghi_Chu", "", 1000),
            Text("@Ma_Dang_Nhap", fixture.Login, 100), Text("@Created_By", fixture.Login, 100), Text("@Created_By_Function", "TDD-H03", 100),
            Text("@Last_Updated_By", fixture.Login, 100), Text("@Last_Updated_By_Function", "TDD-H03", 100));
        await ExecuteStoredIdAsync(connection, null, "dbo.sp_XNK_Nhap_Kho_Save_Detail",
            BigIntOutput("@Auto_ID", 0), BigInt("@Nhap_Kho_ID", receiptId), BigInt("@San_Pham_ID", fixture.ProductId), Decimal("@SL_Nhap", 50), Decimal("@Don_Gia_Nhap", 1),
            Text("@Ma_Dang_Nhap", fixture.Login, 100), Text("@Created_By", fixture.Login, 100), Text("@Created_By_Function", "TDD-H03", 100),
            Text("@Last_Updated_By", fixture.Login, 100), Text("@Last_Updated_By_Function", "TDD-H03", 100));
        await ExecuteStoredAsync(connection, null, "dbo.sp_XNK_Document_Post",
            Bit("@Is_Receipt", true), BigInt("@Document_ID", receiptId), Text("@Ma_Dang_Nhap", fixture.Login, 100),
            Text("@Last_Updated_By", fixture.Login, 100), Text("@Last_Updated_By_Function", "TDD-H03", 100));

        // Deliberately reproduce a stale-but-marked-valid historical anchor:
        // the report bug must be detected even though Current is newer.
        await ExecuteAsync(connection, null,
            "UPDATE dbo.InventoryBalance_Snapshot_Daily SET ClosingQuantity = 100, IsValid = 1 WHERE Snapshot_Date = @SnapshotDate AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            Date("@SnapshotDate", fixture.SnapshotDate), BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));

        return fixture with { ReceiptId = receiptId };
    }

    private static async Task<ReportFixture> CreatePagedFixtureAsync() => await CreateBaseFixtureAsync("TDD-H03-PAGED", includeDaily: true);

    private static async Task<ReportFixture> CreateFutureOnlyReconciliationFixtureAsync()
    {
        var fixture = await CreateBaseFixtureAsync("TDD-N01-FUTURE", includeDaily: false);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var supplierId = await LongScalarAsync(connection, null, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
        var receiptId = await ExecuteStoredIdAsync(connection, null, "dbo.sp_XNK_Nhap_Kho_Save_Header",
            BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Nhap_Kho", $"{fixture.Tag}-future-receipt", 100), BigInt("@Kho_ID", fixture.WarehouseId),
            BigInt("@NCC_ID", supplierId), Date("@Ngay_Nhap_Kho", fixture.BusinessDate.AddDays(1)), Text("@Ghi_Chu", "", 1000),
            Text("@Ma_Dang_Nhap", fixture.Login, 100), Text("@Created_By", fixture.Login, 100), Text("@Created_By_Function", "TDD-N01", 100),
            Text("@Last_Updated_By", fixture.Login, 100), Text("@Last_Updated_By_Function", "TDD-N01", 100));
        await ExecuteStoredIdAsync(connection, null, "dbo.sp_XNK_Nhap_Kho_Save_Detail",
            BigIntOutput("@Auto_ID", 0), BigInt("@Nhap_Kho_ID", receiptId), BigInt("@San_Pham_ID", fixture.ProductId), Decimal("@SL_Nhap", 100), Decimal("@Don_Gia_Nhap", 1),
            Text("@Ma_Dang_Nhap", fixture.Login, 100), Text("@Created_By", fixture.Login, 100), Text("@Created_By_Function", "TDD-N01", 100),
            Text("@Last_Updated_By", fixture.Login, 100), Text("@Last_Updated_By_Function", "TDD-N01", 100));
        await ExecuteStoredAsync(connection, null, "dbo.sp_XNK_Document_Post",
            Bit("@Is_Receipt", true), BigInt("@Document_ID", receiptId), Text("@Ma_Dang_Nhap", fixture.Login, 100),
            Text("@Last_Updated_By", fixture.Login, 100), Text("@Last_Updated_By_Function", "TDD-N01", 100));

        // Keep the Posted Ledger future-only while deliberately removing the
        // Current row, which is the missing projection this reconciliation
        // must report rather than silently omit.
        await ExecuteAsync(connection, null,
            "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
        return fixture with { ReceiptId = receiptId };
    }

    private static async Task<ReportFixture> CreateBaseFixtureAsync(string prefix, bool includeDaily)
    {
        var tag = $"{prefix}-{Guid.NewGuid():N}"[..40];
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            var productId = await LongScalarAsync(connection, transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
            var warehouseId = await LongScalarAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10 H03');",
                Text("@Name", tag, 255));
            var login = $"{tag}-login";
            var memberId = await LongScalarAsync(connection, transaction, "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
            var businessDate = Convert.ToDateTime(await ScalarAsync(connection, transaction, "SELECT CONVERT(date, SYSDATETIME());"));
            var snapshotDate = businessDate.AddDays(-1);
            var dailySql = includeDaily
                ? " INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@BusinessDate, @WarehouseId, @ProductId, 0, 100, 0, 100, 100, 0, 1); INSERT dbo.Inventory_Balance_Daily_Scope(Kho_ID, San_Pham_ID, First_Balance_Date, Last_Balance_Date) VALUES (@WarehouseId, @ProductId, @BusinessDate, @BusinessDate);"
                : "";
            await ExecuteAsync(connection, transaction,
                $"INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, N'Phase 10 H03', 0); INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId); INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, 100, 0); INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@SnapshotDate, @WarehouseId, @ProductId, 100, 1, 1);{dailySql}",
                BigInt("@MemberId", memberId), Text("@Login", login, 100), BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId), Date("@SnapshotDate", snapshotDate), Date("@BusinessDate", businessDate));
            await transaction.CommitAsync();
            return new ReportFixture(tag, login, warehouseId, productId, 0, businessDate, snapshotDate);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<decimal> ReadNonPagedClosingAsync(ReportFixture fixture)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("dbo.sp_BC_Xuat_Nhap_Ton", connection) { CommandType = CommandType.StoredProcedure, CommandTimeout = 10 };
        command.Parameters.Add(Date("@Tu_Ngay", fixture.BusinessDate));
        command.Parameters.Add(Date("@Den_Ngay", fixture.BusinessDate));
        command.Parameters.Add(Text("@Ma_Dang_Nhap", fixture.Login, 100));
        command.Parameters.Add(BigInt("@Kho_ID", fixture.WarehouseId));
        await using var reader = await command.ExecuteReaderAsync();
        var productOrdinal = reader.GetOrdinal("San_Pham_ID");
        var closingOrdinal = reader.GetOrdinal("SL_Cuoi_Ky");
        while (await reader.ReadAsync())
        {
            if (reader.GetInt64(productOrdinal) == fixture.ProductId)
                return reader.GetDecimal(closingOrdinal);
        }
        throw new Xunit.Sdk.XunitException("H03 fixture scope was not returned by the non-paged report.");
    }

    private static async Task AcquireExclusiveScopeLockAsync(SqlConnection connection, SqlTransaction transaction, ReportFixture fixture)
    {
        await ExecuteAsync(connection, transaction,
            "DECLARE @Result INT; EXEC @Result = sys.sp_getapplock @Resource = @Resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0; IF @Result < 0 THROW 51322, N'TDD could not acquire report scope lock.', 1;",
            Text("@Resource", $"InventoryMovement:{fixture.WarehouseId}:{fixture.ProductId}", 255));
    }

    private static async Task CleanupFixtureAsync(ReportFixture fixture)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; IF @ReceiptId <> 0 BEGIN EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1; DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @ReceiptId; EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = NULL; END DELETE r FROM dbo.InventoryReconciliation_Result r JOIN dbo.InventoryReconciliation_Run run ON run.ID = r.Run_ID WHERE run.Kho_ID = @WarehouseId AND run.San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryReconciliation_Run WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.tbl_DM_Kho_User WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId), BigInt("@ReceiptId", fixture.ReceiptId), Text("@Login", fixture.Login, 100));
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task ExecuteStoredAsync(SqlConnection connection, SqlTransaction? transaction, string procedure, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedure, connection, transaction) { CommandType = CommandType.StoredProcedure, CommandTimeout = 10 };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ExecuteStoredIdAsync(SqlConnection connection, SqlTransaction? transaction, string procedure, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedure, connection, transaction) { CommandType = CommandType.StoredProcedure, CommandTimeout = 10 };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
        return Convert.ToInt64(command.Parameters["@Auto_ID"].Value);
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 10 };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 10 };
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<long> LongScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) => Convert.ToInt64(await ScalarAsync(connection, transaction, sql, parameters));

    private static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.NVarChar, size) { Value = value };
    private static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt) { Value = value };
    private static SqlParameter BigIntOutput(string name, long value) => new(name, SqlDbType.BigInt) { Direction = ParameterDirection.InputOutput, Value = value };
    private static SqlParameter Bit(string name, bool value) => new(name, SqlDbType.Bit) { Value = value };
    private static SqlParameter Int(string name, int value) => new(name, SqlDbType.Int) { Value = value };
    private static SqlParameter Date(string name, DateTime value) => new(name, SqlDbType.Date) { Value = value.Date };
    private static SqlParameter Decimal(string name, decimal value) => new(name, SqlDbType.Decimal) { Precision = 18, Scale = 3, Value = value };

    private sealed record ReportFixture(string Tag, string Login, long WarehouseId, long ProductId, long ReceiptId, DateTime BusinessDate, DateTime SnapshotDate);
}
