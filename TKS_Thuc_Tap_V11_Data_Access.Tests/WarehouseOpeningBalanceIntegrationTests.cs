using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehouseOpeningBalanceIntegrationTests
{
    private static string ConnectionString
    {
        get
        {
            return WarehouseTestDatabase.ConnectionString;
        }
    }

    [Fact]
    public async Task Period_reports_use_the_last_balance_before_start_and_match_each_other()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            var v_Tag = $"TDD-OPEN-{Guid.NewGuid():N}";
            var v_Login = $"{v_Tag}-login";
            var v_arrProductIds = await ReadProductIdsAsync(v_Connection, v_Transaction, 3);
            var supplierId = await ReadIdAsync(v_Connection, v_Transaction,
                "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Opening balance integration test');",
                Text("@Name", $"{v_Tag}-warehouse", 255));
            await InsertIdAsync(v_Connection, v_Transaction,
                "DECLARE @MemberId BIGINT; SELECT @MemberId = ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX); INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) OUTPUT INSERTED.Auto_ID VALUES (@MemberId, @Login, @Name, 0);",
                Text("@Login", v_Login, 100), Text("@Name", $"{v_Tag}-member", 200));

            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", v_Login, 100), BigInt("@WarehouseId", warehouseId));

            /* Case 1: no balance before 2026-01-01.  The latest row has
               OpeningQuantity=5, which must not become the report opening. */
            var noHistoryProduct = v_arrProductIds[0];
            await InsertReceiptAsync(v_Connection, v_Transaction, warehouseId, supplierId, noHistoryProduct, new DateTime(2026, 1, 3), 15, v_Tag);
            await InsertIssueAsync(v_Connection, v_Transaction, warehouseId, noHistoryProduct, new DateTime(2026, 2, 10), 3, v_Tag);
            await InsertIssueAsync(v_Connection, v_Transaction, warehouseId, noHistoryProduct, new DateTime(2026, 2, 24), 2, v_Tag);
            await InsertIssueAsync(v_Connection, v_Transaction, warehouseId, noHistoryProduct, new DateTime(2026, 3, 24), 3, v_Tag);
            await InsertIssueAsync(v_Connection, v_Transaction, warehouseId, noHistoryProduct, new DateTime(2026, 5, 24), 2, v_Tag);
            await InsertReceiptAsync(v_Connection, v_Transaction, warehouseId, supplierId, noHistoryProduct, new DateTime(2026, 6, 3), 17, v_Tag);
            await InsertDailyAsync(v_Connection, v_Transaction, warehouseId, noHistoryProduct,
                new DateTime(2026, 6, 3), 5, 17, 0, 22, 32, 10);
            await InsertScopeAsync(v_Connection, v_Transaction, warehouseId, noHistoryProduct,
                new DateTime(2026, 6, 3), new DateTime(2026, 6, 3));

            var v_NoHistoryPaged = await ReadPagedReportAsync(v_Connection, v_Transaction, v_Login, warehouseId, noHistoryProduct,
                new DateTime(2026, 1, 1), new DateTime(2026, 9, 30));
            var v_NoHistoryFull = await ReadFullReportAsync(v_Connection, v_Transaction, v_Login, warehouseId, noHistoryProduct,
                new DateTime(2026, 1, 1), new DateTime(2026, 9, 30));
            AssertReport(v_NoHistoryPaged, 0, 32, 10, 22);
            AssertReport(v_NoHistoryFull, 0, 32, 10, 22);

            /* Case 2: a balance exists on 2025-12-31 and must seed 2026-01-01. */
            var priorPeriodProduct = v_arrProductIds[1];
            await InsertSnapshotAsync(v_Connection, v_Transaction, warehouseId, priorPeriodProduct,
                new DateTime(2025, 12, 31), 100);
            await InsertDailyAsync(v_Connection, v_Transaction, warehouseId, priorPeriodProduct,
                new DateTime(2025, 12, 31), 100, 0, 0, 100, 0, 0);
            await InsertReceiptAsync(v_Connection, v_Transaction, warehouseId, supplierId, priorPeriodProduct,
                new DateTime(2026, 1, 10), 20, v_Tag);
            await InsertDailyAsync(v_Connection, v_Transaction, warehouseId, priorPeriodProduct,
                new DateTime(2026, 1, 10), 100, 20, 0, 120, 20, 0);
            await InsertScopeAsync(v_Connection, v_Transaction, warehouseId, priorPeriodProduct,
                new DateTime(2025, 12, 31), new DateTime(2026, 1, 10));

            var v_PriorPeriodPaged = await ReadPagedReportAsync(v_Connection, v_Transaction, v_Login, warehouseId, priorPeriodProduct,
                new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
            var v_PriorPeriodFull = await ReadFullReportAsync(v_Connection, v_Transaction, v_Login, warehouseId, priorPeriodProduct,
                new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
            AssertReport(v_PriorPeriodPaged, 100, 20, 0, 120);
            AssertReport(v_PriorPeriodFull, 100, 20, 0, 120);

            /* Case 3: a mid-period snapshot on 2026-03-10 seeds a report
               beginning on 2026-03-15. */
            var midPeriodProduct = v_arrProductIds[2];
            await InsertSnapshotAsync(v_Connection, v_Transaction, warehouseId, midPeriodProduct,
                new DateTime(2026, 3, 10), 100);
            await InsertDailyAsync(v_Connection, v_Transaction, warehouseId, midPeriodProduct,
                new DateTime(2026, 3, 10), 100, 0, 0, 100, 0, 0);
            await InsertReceiptAsync(v_Connection, v_Transaction, warehouseId, supplierId, midPeriodProduct,
                new DateTime(2026, 3, 20), 20, v_Tag);
            await InsertDailyAsync(v_Connection, v_Transaction, warehouseId, midPeriodProduct,
                new DateTime(2026, 3, 20), 100, 20, 0, 120, 20, 0);
            await InsertScopeAsync(v_Connection, v_Transaction, warehouseId, midPeriodProduct,
                new DateTime(2026, 3, 10), new DateTime(2026, 3, 20));

            var v_MidPeriodPaged = await ReadPagedReportAsync(v_Connection, v_Transaction, v_Login, warehouseId, midPeriodProduct,
                new DateTime(2026, 3, 15), new DateTime(2026, 3, 31));
            var v_MidPeriodFull = await ReadFullReportAsync(v_Connection, v_Transaction, v_Login, warehouseId, midPeriodProduct,
                new DateTime(2026, 3, 15), new DateTime(2026, 3, 31));
            AssertReport(v_MidPeriodPaged, 100, 20, 0, 120);
            AssertReport(v_MidPeriodFull, 100, 20, 0, 120);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    private static void AssertReport(ReportRow p_Row, decimal p_Opening, decimal p_Received, decimal p_Issued, decimal p_Closing)
    {
        Assert.Equal(p_Opening, p_Row.Opening);
        Assert.Equal(p_Received, p_Row.Received);
        Assert.Equal(p_Issued, p_Row.Issued);
        Assert.Equal(p_Closing, p_Row.Closing);
    }

    private static async Task<ReportRow> ReadPagedReportAsync(
        SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Login, long warehouseId, long productId,
        DateTime p_dtmFrom, DateTime p_dtmTo)
    {
        await using var v_Command = ReportCommand(p_Connection, p_Transaction, "sp_BC_Xuat_Nhap_Ton_Page", p_Login, warehouseId, p_dtmFrom, p_dtmTo);
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        Assert.True(await v_Reader.NextResultAsync());
        while (await v_Reader.ReadAsync())
        {
            if (v_Reader.GetInt64(2) == productId)
                return ReadReportRow(v_Reader);
        }

        throw new Xunit.Sdk.XunitException($"Product {productId} was not returned by the paged report.");
    }

    private static async Task<ReportRow> ReadFullReportAsync(
        SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Login, long warehouseId, long productId,
        DateTime p_dtmFrom, DateTime p_dtmTo)
    {
        await using var v_Command = ReportCommand(p_Connection, p_Transaction, "sp_BC_Xuat_Nhap_Ton", p_Login, warehouseId, p_dtmFrom, p_dtmTo);
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        while (await v_Reader.ReadAsync())
        {
            if (v_Reader.GetInt64(2) == productId)
                return ReadReportRow(v_Reader);
        }

        throw new Xunit.Sdk.XunitException($"Product {productId} was not returned by the full report.");
    }

    private static ReportRow ReadReportRow(SqlDataReader p_Reader)
    {
        return new(p_Reader.GetDecimal(5), p_Reader.GetDecimal(6), p_Reader.GetDecimal(7), p_Reader.GetDecimal(8));
    }

    private static SqlCommand ReportCommand(
        SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Procedure, string p_Login, long warehouseId,
        DateTime p_dtmFrom, DateTime p_dtmTo)
    {
        var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        v_Command.Parameters.Add(Date("@Tu_Ngay", p_dtmFrom));
        v_Command.Parameters.Add(Date("@Den_Ngay", p_dtmTo));
        if (p_Procedure.EndsWith("_Page", StringComparison.Ordinal))
        {
            v_Command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
            v_Command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 20 });
        }
        v_Command.Parameters.Add(Text("@Ma_Dang_Nhap", p_Login, 100));
        v_Command.Parameters.Add(BigInt("@Kho_ID", warehouseId));
        return v_Command;
    }

    private static async Task<List<long>> ReadProductIdsAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, int p_iCount)
    {
        await using var v_Command = new SqlCommand(
            "SELECT TOP (@Count) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;", p_Connection, p_Transaction);
        v_Command.Parameters.Add(new SqlParameter("@Count", SqlDbType.Int) { Value = p_iCount });
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        var v_arrResult = new List<long>();
        while (await v_Reader.ReadAsync())
            v_arrResult.Add(v_Reader.GetInt64(0));
        Assert.Equal(p_iCount, v_arrResult.Count);
        return v_arrResult;
    }

    private static async Task<long> ReadIdAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Sql)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        var v_objValue = await v_Command.ExecuteScalarAsync();
        Assert.NotNull(v_objValue);
        return Convert.ToInt64(v_objValue);
    }

    private static async Task<long> InsertIdAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        return Convert.ToInt64(await v_Command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task InsertReceiptAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, long warehouseId, long supplierId, long productId, DateTime p_dtmDate, decimal p_Quantity, string p_Tag)
    {
        var documentId = await InsertIdAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, @Date, 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
            Text("@Number", $"{p_Tag}-receipt-{Guid.NewGuid():N}", 100), BigInt("@WarehouseId", warehouseId), BigInt("@SupplierId", supplierId), Date("@Date", p_dtmDate));
        await ExecuteAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
            BigInt("@DocumentId", documentId), BigInt("@ProductId", productId), Decimal("@Quantity", p_Quantity));
    }

    private static async Task InsertIssueAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, long warehouseId, long productId, DateTime p_dtmDate, decimal p_Quantity, string p_Tag)
    {
        var documentId = await InsertIdAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @Date, 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
            Text("@Number", $"{p_Tag}-issue-{Guid.NewGuid():N}", 100), BigInt("@WarehouseId", warehouseId), Date("@Date", p_dtmDate));
        await ExecuteAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
            BigInt("@DocumentId", documentId), BigInt("@ProductId", productId), Decimal("@Quantity", p_Quantity));
    }

    private static Task InsertSnapshotAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, long warehouseId, long productId, DateTime p_dtmDate, decimal p_Closing)
    {
        return ExecuteAsync(p_Connection, p_Transaction, "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, @Closing, 1, 1);", Date("@Date", p_dtmDate), BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId), Decimal("@Closing", p_Closing));
    }

    private static Task InsertDailyAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, long warehouseId, long productId, DateTime p_dtmDate, decimal p_Opening, decimal p_Received, decimal p_Issued, decimal p_Closing, decimal p_CumulativeReceived, decimal p_CumulativeIssued)
    {
        return ExecuteAsync(p_Connection, p_Transaction, "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, @Opening, @Received, @Issued, @Closing, @CumulativeReceived, @CumulativeIssued, 1);", Date("@Date", p_dtmDate), BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId), Decimal("@Opening", p_Opening), Decimal("@Received", p_Received), Decimal("@Issued", p_Issued), Decimal("@Closing", p_Closing), Decimal("@CumulativeReceived", p_CumulativeReceived), Decimal("@CumulativeIssued", p_CumulativeIssued));
    }

    private static Task InsertScopeAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, long warehouseId, long productId, DateTime p_dtmFirstDate, DateTime p_dtmLastDate)
    {
        return ExecuteAsync(p_Connection, p_Transaction, "INSERT dbo.Inventory_Balance_Daily_Scope(Kho_ID, San_Pham_ID, First_Balance_Date, Last_Balance_Date) VALUES (@WarehouseId, @ProductId, @FirstDate, @LastDate);", BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId), Date("@FirstDate", p_dtmFirstDate), Date("@LastDate", p_dtmLastDate));
    }

    private static SqlParameter BigInt(string p_Name, long value)
    {
        return new(p_Name, SqlDbType.BigInt)
        {
            Value = value
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

    private static SqlParameter Date(string p_Name, DateTime p_dtmValue)
    {
        return new(p_Name, SqlDbType.Date)
        {
            Value = p_dtmValue.Date
        };
    }

    private static SqlParameter Text(string p_Name, string p_Value, int p_iSize)
    {
        return new(p_Name, SqlDbType.NVarChar, p_iSize)
        {
            Value = p_Value
        };
    }

    private sealed record ReportRow(decimal Opening, decimal Received, decimal Issued, decimal Closing);
}
