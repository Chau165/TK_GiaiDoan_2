using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehousePhase10H03IntegrationTests
{
    private static string ConnectionString
    {
        get
        {
            return WarehouseTestDatabase.ConnectionString;
        }
    }

    [Fact]
    public async Task H03_nonpaged_period_report_uses_period_closing_not_current_quantity()
    {
        var v_Fixture = await CreateNonPagedFixtureAsync();
        try
        {
            var v_ActualClosing = await ReadNonPagedClosingAsync(v_Fixture);

            // A future Posted receipt makes Current=150, but the requested
            // period ends today and its Daily/Snapshot closing is still 100.
            Assert.Equal(100m, v_ActualClosing);
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task H03_paged_report_rejects_when_scope_fence_is_held()
    {
        var v_Fixture = await CreatePagedFixtureAsync();
        await using var v_WriterConnection = new SqlConnection(ConnectionString);
        await v_WriterConnection.OpenAsync();
        await using var v_WriterTransaction = v_WriterConnection.BeginTransaction();

        try
        {
            await AcquireExclusiveScopeLockAsync(v_WriterConnection, v_WriterTransaction, v_Fixture);

            await using var v_ReportConnection = new SqlConnection(ConnectionString);
            await v_ReportConnection.OpenAsync();
            var v_Error = await Assert.ThrowsAsync<SqlException>(() => ExecuteStoredAsync(
                v_ReportConnection,
                null,
                "dbo.sp_BC_Xuat_Nhap_Ton_Page",
                Date("@Tu_Ngay", v_Fixture.BusinessDate),
                Date("@Den_Ngay", v_Fixture.BusinessDate),
                Int("@Page_Number", 1),
                Int("@Page_Size", 10),
                Text("@Ma_Dang_Nhap", v_Fixture.Login, 100),
                BigInt("@Kho_ID", v_Fixture.WarehouseId)));

            Assert.Equal(51323, v_Error.Number);
        }
        finally
        {
            try { await v_WriterTransaction.RollbackAsync(); } catch { }
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task N01_current_reconciliation_includes_future_only_posted_scope()
    {
        var v_Fixture = await CreateFutureOnlyReconciliationFixtureAsync();
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        try
        {
            var v_RunParameter = BigIntOutput("@Run_ID", 0);
            await ExecuteStoredAsync(
                v_Connection,
                null,
                "dbo.sp_Inventory_Reconciliation_Run",
                v_RunParameter,
                BigInt("@Kho_ID", v_Fixture.WarehouseId),
                BigInt("@San_Pham_ID", v_Fixture.ProductId));

            var runId = Convert.ToInt64(v_RunParameter.Value);
            var v_iResultCount = Convert.ToInt32(await ScalarAsync(
                v_Connection,
                null,
                "SELECT COUNT(*) FROM dbo.InventoryReconciliation_Result WHERE Run_ID = @RunId;",
                BigInt("@RunId", runId)));
            var v_iFailedCount = Convert.ToInt32(await ScalarAsync(
                v_Connection,
                null,
                "SELECT COUNT(*) FROM dbo.InventoryReconciliation_Result WHERE Run_ID = @RunId AND Status = N'FAIL';",
                BigInt("@RunId", runId)));

            Assert.Equal(1, v_iResultCount);
            Assert.Equal(1, v_iFailedCount);
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    private static async Task<ReportFixture> CreateNonPagedFixtureAsync()
    {
        var v_Fixture = await CreateBaseFixtureAsync("TDD-H03-NONPAGED", p_bIncludeDaily: false);
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();

        var supplierId = await LongScalarAsync(v_Connection, null, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
        var receiptId = await ExecuteStoredIdAsync(v_Connection, null, "dbo.F2011_sp_ins_Nhap_Kho_Header",
            BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Nhap_Kho", $"{v_Fixture.Tag}-future-receipt", 100), BigInt("@Kho_ID", v_Fixture.WarehouseId),
            BigInt("@NCC_ID", supplierId), Date("@Ngay_Nhap_Kho", v_Fixture.BusinessDate.AddDays(1)), Text("@Ghi_Chu", "", 1000),
            Text("@Ma_Dang_Nhap", v_Fixture.Login, 100), Text("@Created_By", v_Fixture.Login, 100), Text("@Created_By_Function", "TDD-H03", 100),
            Text("@Last_Updated_By", v_Fixture.Login, 100), Text("@Last_Updated_By_Function", "TDD-H03", 100));
        await ExecuteStoredIdAsync(v_Connection, null, "dbo.F2011_sp_ins_Nhap_Kho_Detail",
            BigIntOutput("@Auto_ID", 0), BigInt("@Nhap_Kho_ID", receiptId), BigInt("@San_Pham_ID", v_Fixture.ProductId), Decimal("@SL_Nhap", 50), Decimal("@Don_Gia_Nhap", 1),
            Text("@Ma_Dang_Nhap", v_Fixture.Login, 100), Text("@Created_By", v_Fixture.Login, 100), Text("@Created_By_Function", "TDD-H03", 100),
            Text("@Last_Updated_By", v_Fixture.Login, 100), Text("@Last_Updated_By_Function", "TDD-H03", 100));
        await ExecuteStoredAsync(v_Connection, null, "dbo.sp_XNK_Document_Post",
            Bit("@Is_Receipt", true), BigInt("@Document_ID", receiptId), Text("@Ma_Dang_Nhap", v_Fixture.Login, 100),
            Text("@Last_Updated_By", v_Fixture.Login, 100), Text("@Last_Updated_By_Function", "TDD-H03", 100));

        // Deliberately reproduce a stale-but-marked-valid historical anchor:
        // the report bug must be detected even though Current is newer.
        await ExecuteAsync(v_Connection, null,
            "UPDATE dbo.InventoryBalance_Snapshot_Daily SET ClosingQuantity = 100, IsValid = 1 WHERE Snapshot_Date = @SnapshotDate AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            Date("@SnapshotDate", v_Fixture.SnapshotDate), BigInt("@WarehouseId", v_Fixture.WarehouseId), BigInt("@ProductId", v_Fixture.ProductId));

        return v_Fixture with { ReceiptId = receiptId };
    }

    private static async Task<ReportFixture> CreatePagedFixtureAsync()
    {
        return await CreateBaseFixtureAsync("TDD-H03-PAGED", p_bIncludeDaily: true);
    }

    private static async Task<ReportFixture> CreateFutureOnlyReconciliationFixtureAsync()
    {
        var v_Fixture = await CreateBaseFixtureAsync("TDD-N01-FUTURE", p_bIncludeDaily: false);
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        var supplierId = await LongScalarAsync(v_Connection, null, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
        var receiptId = await ExecuteStoredIdAsync(v_Connection, null, "dbo.F2011_sp_ins_Nhap_Kho_Header",
            BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Nhap_Kho", $"{v_Fixture.Tag}-future-receipt", 100), BigInt("@Kho_ID", v_Fixture.WarehouseId),
            BigInt("@NCC_ID", supplierId), Date("@Ngay_Nhap_Kho", v_Fixture.BusinessDate.AddDays(1)), Text("@Ghi_Chu", "", 1000),
            Text("@Ma_Dang_Nhap", v_Fixture.Login, 100), Text("@Created_By", v_Fixture.Login, 100), Text("@Created_By_Function", "TDD-N01", 100),
            Text("@Last_Updated_By", v_Fixture.Login, 100), Text("@Last_Updated_By_Function", "TDD-N01", 100));
        await ExecuteStoredIdAsync(v_Connection, null, "dbo.F2011_sp_ins_Nhap_Kho_Detail",
            BigIntOutput("@Auto_ID", 0), BigInt("@Nhap_Kho_ID", receiptId), BigInt("@San_Pham_ID", v_Fixture.ProductId), Decimal("@SL_Nhap", 100), Decimal("@Don_Gia_Nhap", 1),
            Text("@Ma_Dang_Nhap", v_Fixture.Login, 100), Text("@Created_By", v_Fixture.Login, 100), Text("@Created_By_Function", "TDD-N01", 100),
            Text("@Last_Updated_By", v_Fixture.Login, 100), Text("@Last_Updated_By_Function", "TDD-N01", 100));
        await ExecuteStoredAsync(v_Connection, null, "dbo.sp_XNK_Document_Post",
            Bit("@Is_Receipt", true), BigInt("@Document_ID", receiptId), Text("@Ma_Dang_Nhap", v_Fixture.Login, 100),
            Text("@Last_Updated_By", v_Fixture.Login, 100), Text("@Last_Updated_By_Function", "TDD-N01", 100));

        // Keep the Posted Ledger future-only while deliberately removing the
        // Current row, which is the missing projection this reconciliation
        // must report rather than silently omit.
        await ExecuteAsync(v_Connection, null,
            "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", v_Fixture.WarehouseId), BigInt("@ProductId", v_Fixture.ProductId));
        return v_Fixture with { ReceiptId = receiptId };
    }

    private static async Task<ReportFixture> CreateBaseFixtureAsync(string p_Prefix, bool p_bIncludeDaily)
    {
        var v_Tag = $"{p_Prefix}-{Guid.NewGuid():N}"[..40];
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        try
        {
            var productId = await LongScalarAsync(v_Connection, v_Transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
            var warehouseId = await LongScalarAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10 H03');",
                Text("@Name", v_Tag, 255));
            var v_Login = $"{v_Tag}-login";
            var memberId = await LongScalarAsync(v_Connection, v_Transaction, "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
            var v_dtmBusinessDate = Convert.ToDateTime(await ScalarAsync(v_Connection, v_Transaction, "SELECT CONVERT(date, SYSDATETIME());"));
            var v_dtmSnapshotDate = v_dtmBusinessDate.AddDays(-1);
            string v_DailySql;
            if (p_bIncludeDaily == true)
            {
                v_DailySql = " INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@BusinessDate, @WarehouseId, @ProductId, 0, 100, 0, 100, 100, 0, 1); INSERT dbo.Inventory_Balance_Daily_Scope(Kho_ID, San_Pham_ID, First_Balance_Date, Last_Balance_Date) VALUES (@WarehouseId, @ProductId, @BusinessDate, @BusinessDate);";
            }
            else
            {
                v_DailySql = "";
            }
            await ExecuteAsync(v_Connection, v_Transaction,
                $"INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, N'Phase 10 H03', 0); INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId); INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, 100, 0); INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@SnapshotDate, @WarehouseId, @ProductId, 100, 1, 1);{v_DailySql}",
                BigInt("@MemberId", memberId), Text("@Login", v_Login, 100), BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId), Date("@SnapshotDate", v_dtmSnapshotDate), Date("@BusinessDate", v_dtmBusinessDate));
            await v_Transaction.CommitAsync();
            return new ReportFixture(v_Tag, v_Login, warehouseId, productId, 0, v_dtmBusinessDate, v_dtmSnapshotDate);
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<decimal> ReadNonPagedClosingAsync(ReportFixture p_Fixture)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Command = new SqlCommand("dbo.sp_BC_Xuat_Nhap_Ton", v_Connection) { CommandType = CommandType.StoredProcedure, CommandTimeout = 10 };
        v_Command.Parameters.Add(Date("@Tu_Ngay", p_Fixture.BusinessDate));
        v_Command.Parameters.Add(Date("@Den_Ngay", p_Fixture.BusinessDate));
        v_Command.Parameters.Add(Text("@Ma_Dang_Nhap", p_Fixture.Login, 100));
        v_Command.Parameters.Add(BigInt("@Kho_ID", p_Fixture.WarehouseId));
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        var v_iProductOrdinal = v_Reader.GetOrdinal("San_Pham_ID");
        var v_iClosingOrdinal = v_Reader.GetOrdinal("SL_Cuoi_Ky");
        while (await v_Reader.ReadAsync())
        {
            if (v_Reader.GetInt64(v_iProductOrdinal) == p_Fixture.ProductId)
                return v_Reader.GetDecimal(v_iClosingOrdinal);
        }
        throw new Xunit.Sdk.XunitException("H03 fixture scope was not returned by the non-paged report.");
    }

    private static async Task AcquireExclusiveScopeLockAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, ReportFixture p_Fixture)
    {
        await ExecuteAsync(p_Connection, p_Transaction,
            "DECLARE @Result INT; EXEC @Result = sys.sp_getapplock @Resource = @Resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0; IF @Result < 0 THROW 51322, N'TDD could not acquire report scope lock.', 1;",
            Text("@Resource", $"InventoryMovement:{p_Fixture.WarehouseId}:{p_Fixture.ProductId}", 255));
    }

    private static async Task CleanupFixtureAsync(ReportFixture p_Fixture)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; IF @ReceiptId <> 0 BEGIN EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1; DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @ReceiptId; EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = NULL; END DELETE r FROM dbo.InventoryReconciliation_Result r JOIN dbo.InventoryReconciliation_Run run ON run.ID = r.Run_ID WHERE run.Kho_ID = @WarehouseId AND run.San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryReconciliation_Run WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.tbl_DM_Kho_User WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId), BigInt("@ReceiptId", p_Fixture.ReceiptId), Text("@Login", p_Fixture.Login, 100));
            await v_Transaction.CommitAsync();
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task ExecuteStoredAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Procedure, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure, CommandTimeout = 10 };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ExecuteStoredIdAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Procedure, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure, CommandTimeout = 10 };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
        return Convert.ToInt64(v_Command.Parameters["@Auto_ID"].Value);
    }

    private static async Task ExecuteAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction) { CommandTimeout = 10 };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction) { CommandTimeout = 10 };
        v_Command.Parameters.AddRange(p_arrParameters);
        return await v_Command.ExecuteScalarAsync();
    }

    private static async Task<long> LongScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToInt64(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
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
    private static SqlParameter BigIntOutput(string p_Name, long value)
    {
        return new(p_Name, SqlDbType.BigInt)
        {
            Direction = ParameterDirection.InputOutput,
            Value = value
        };
    }
    private static SqlParameter Bit(string p_Name, bool p_bValue)
    {
        return new(p_Name, SqlDbType.Bit)
        {
            Value = p_bValue
        };
    }
    private static SqlParameter Int(string p_Name, int p_iValue)
    {
        return new(p_Name, SqlDbType.Int)
        {
            Value = p_iValue
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

    private sealed record ReportFixture(string Tag, string Login, long WarehouseId, long ProductId, long ReceiptId, DateTime BusinessDate, DateTime SnapshotDate);
}
