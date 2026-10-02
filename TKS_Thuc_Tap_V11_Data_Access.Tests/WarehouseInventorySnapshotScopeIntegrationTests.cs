using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseInventorySnapshotScopeIntegrationTests
{
    private static string ConnectionString
    {
        get
        {
            return WarehouseTestDatabase.ConnectionString;
        }
    }
    private static readonly DateTime m_dtmReportFrom = new(2099, 2, 21);
    private static readonly DateTime m_dtmReportTo = new(2099, 2, 28);

    [Fact]
    public async Task Reports_choose_the_latest_valid_snapshot_per_warehouse_and_product_scope()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            var v_Tag = $"TDD-SNAPSHOT-SCOPE-{Guid.NewGuid():N}";
            var v_Login = $"{v_Tag}-login";
            var productId = await ScalarLongAsync(v_Connection, v_Transaction,
                "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
            var supplierId = await ScalarLongAsync(v_Connection, v_Transaction,
                "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseA = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-warehouse-a", 255));
            var warehouseB = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-warehouse-b", 255));

            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseA), (@Login, @WarehouseB);",
                Text("@Login", v_Login, 100), BigInt("@WarehouseA", warehouseA), BigInt("@WarehouseB", warehouseB));
            await ExecuteAsync(v_Connection, v_Transaction,
                "UPDATE dbo.InventoryMovement_AggregateState SET IsInitialized = 1 WHERE State_ID = 1;");
            await ExecuteAsync(v_Connection, v_Transaction,
                "UPDATE dbo.InventoryBalance_Daily_AggregateState SET IsInitialized = 1 WHERE State_ID = 1;");

            var receiptAOpening = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, '2099-02-10', 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-receipt-a-opening", 100), BigInt("@WarehouseId", warehouseA), BigInt("@SupplierId", supplierId));
            var receiptAPeriod = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, '2099-02-22', 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-receipt-a-period", 100), BigInt("@WarehouseId", warehouseA), BigInt("@SupplierId", supplierId));
            var receiptBPreSnapshot = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, '2099-02-10', 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-receipt-b-presnapshot", 100), BigInt("@WarehouseId", warehouseB), BigInt("@SupplierId", supplierId));
            var issueBPeriod = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, '2099-02-22', 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-issue-b-period", 100), BigInt("@WarehouseId", warehouseB));

            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
                BigInt("@DocumentId", receiptAOpening), BigInt("@ProductId", productId), Decimal("@Quantity", 10));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
                BigInt("@DocumentId", receiptAPeriod), BigInt("@ProductId", productId), Decimal("@Quantity", 5));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
                BigInt("@DocumentId", receiptBPreSnapshot), BigInt("@ProductId", productId), Decimal("@Quantity", 77));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
                BigInt("@DocumentId", issueBPeriod), BigInt("@ProductId", productId), Decimal("@Quantity", 20));

            await ExecuteAsync(v_Connection, v_Transaction,
                """
                INSERT dbo.InventoryBalance_Snapshot_Daily
                (Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, InvalidatedAt, InvalidReason, [Version])
                VALUES
                    ('2099-01-31', @WarehouseA, @ProductId, 100, 1, NULL, NULL, 1),
                    ('2099-02-20', @WarehouseA, @ProductId, 999, 0, SYSUTCDATETIME(), N'BACK_DATE_POST', 1),
                    ('2099-01-31', @WarehouseB, @ProductId, 100, 1, NULL, NULL, 1),
                    ('2099-02-20', @WarehouseB, @ProductId, 200, 1, NULL, NULL, 1);
                """,
                BigInt("@WarehouseA", warehouseA), BigInt("@WarehouseB", warehouseB), BigInt("@ProductId", productId));

            await ExecuteAsync(v_Connection, v_Transaction,
                """
                INSERT dbo.Inventory_Movement_Daily
                (Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue, IsValid)
                VALUES
                    ('2099-02-10', @WarehouseA, @ProductId, 10, 0, 1),
                    ('2099-02-22', @WarehouseA, @ProductId, 5, 0, 1),
                    ('2099-02-10', @WarehouseB, @ProductId, 77, 0, 1),
                    ('2099-02-22', @WarehouseB, @ProductId, 0, 20, 1);
                """,
                BigInt("@WarehouseA", warehouseA), BigInt("@WarehouseB", warehouseB), BigInt("@ProductId", productId));

            await ExecuteAsync(v_Connection, v_Transaction,
                """
                INSERT dbo.Inventory_Balance_Daily
                (Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid)
                VALUES
                    ('2099-01-31', @WarehouseA, @ProductId, 100, 0, 0, 100, 0, 0, 1),
                    ('2099-02-10', @WarehouseA, @ProductId, 100, 10, 0, 110, 10, 0, 1),
                    ('2099-02-22', @WarehouseA, @ProductId, 110, 5, 0, 115, 15, 0, 1),
                    ('2099-01-31', @WarehouseB, @ProductId, 100, 0, 0, 100, 0, 0, 1),
                    ('2099-02-10', @WarehouseB, @ProductId, 100, 77, 0, 177, 77, 0, 1),
                    ('2099-02-20', @WarehouseB, @ProductId, 177, 23, 0, 200, 100, 0, 1),
                    ('2099-02-22', @WarehouseB, @ProductId, 200, 0, 20, 180, 100, 20, 1);
                INSERT dbo.Inventory_Balance_Daily_Scope
                (Kho_ID, San_Pham_ID, First_Balance_Date, Last_Balance_Date)
                VALUES
                    (@WarehouseA, @ProductId, '2099-01-31', '2099-02-22'),
                    (@WarehouseB, @ProductId, '2099-01-31', '2099-02-22');
                """,
                BigInt("@WarehouseA", warehouseA), BigInt("@WarehouseB", warehouseB), BigInt("@ProductId", productId));

            var v_Paged = await ReadPagedAsync(v_Connection, v_Transaction, v_Login);
            var v_arrNonPaged = await ReadNonPagedAsync(v_Connection, v_Transaction, v_Login);

            Assert.Equal(2, v_Paged.TotalCount);
            AssertScope(v_Paged.Rows, warehouseA, p_Opening: 110m, p_Received: 5m, p_Issued: 0m, p_Closing: 115m);
            AssertScope(v_Paged.Rows, warehouseB, p_Opening: 200m, p_Received: 0m, p_Issued: 20m, p_Closing: 180m);
            AssertScope(v_arrNonPaged, warehouseA, p_Opening: 110m, p_Received: 5m, p_Issued: 0m, p_Closing: 115m);
            AssertScope(v_arrNonPaged, warehouseB, p_Opening: 200m, p_Received: 0m, p_Issued: 20m, p_Closing: 180m);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    private static async Task<PagedReport> ReadPagedAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Login)
    {
        await using var v_Command = new SqlCommand("sp_BC_Xuat_Nhap_Ton_Page", p_Connection, p_Transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        AddReportParameters(v_Command, p_Login);
        v_Command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
        v_Command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 20 });

        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        var v_iTotalCount = v_Reader.GetInt32(0);
        Assert.True(await v_Reader.NextResultAsync());
        return new PagedReport(v_iTotalCount, await ReadRowsAsync(v_Reader));
    }

    private static async Task<List<ReportRow>> ReadNonPagedAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Login)
    {
        await using var v_Command = new SqlCommand("sp_BC_Xuat_Nhap_Ton", p_Connection, p_Transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        AddReportParameters(v_Command, p_Login);

        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        return await ReadRowsAsync(v_Reader);
    }

    private static void AddReportParameters(SqlCommand p_Command, string p_Login)
    {
        p_Command.Parameters.Add(Date("@Tu_Ngay", m_dtmReportFrom));
        p_Command.Parameters.Add(Date("@Den_Ngay", m_dtmReportTo));
        p_Command.Parameters.Add(Text("@Ma_Dang_Nhap", p_Login, 100));
    }

    private static async Task<List<ReportRow>> ReadRowsAsync(SqlDataReader p_Reader)
    {
        var v_arrRows = new List<ReportRow>();
        while (await p_Reader.ReadAsync())
        {
            v_arrRows.Add(new ReportRow(
                p_Reader.GetInt64(0),
                p_Reader.GetDecimal(5),
                p_Reader.GetDecimal(6),
                p_Reader.GetDecimal(7),
                p_Reader.GetDecimal(8)));
        }

        return v_arrRows;
    }

    private static void AssertScope(IEnumerable<ReportRow> p_Rows, long warehouseId, decimal p_Opening, decimal p_Received, decimal p_Issued, decimal p_Closing)
    {
        var v_Row = Assert.Single(p_Rows, x => x.WarehouseId == warehouseId);
        Assert.Equal(p_Opening, v_Row.Opening);
        Assert.Equal(p_Received, v_Row.Received);
        Assert.Equal(p_Issued, v_Row.Issued);
        Assert.Equal(p_Closing, v_Row.Closing);
    }

    private static async Task<long> InsertIdAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        return Convert.ToInt64(await v_Command.ExecuteScalarAsync());
    }

    private static async Task<long> ScalarLongAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Sql)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        return Convert.ToInt64(await v_Command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
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

    private sealed record PagedReport(int TotalCount, List<ReportRow> Rows);
    private sealed record ReportRow(long WarehouseId, decimal Opening, decimal Received, decimal Issued, decimal Closing);
}
