using Microsoft.Data.SqlClient;
using System.Data;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehouseInventorySnapshotIntegrationTests
{
    private static string ConnectionString
    {
        get
        {
            return WarehouseTestDatabase.ConnectionString;
        }
    }

    [Fact]
    public async Task Inventory_report_uses_the_latest_daily_snapshot_and_only_later_movements()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            await ExecuteAsync(v_Connection, v_Transaction,
                "UPDATE dbo.InventoryMovement_AggregateState SET IsInitialized = 1 WHERE State_ID = 1; UPDATE dbo.InventoryBalance_Daily_AggregateState SET IsInitialized = 1 WHERE State_ID = 1;");
            var v_Tag = $"TDD-SNAPSHOT-{Guid.NewGuid():N}";
            var v_Login = $"{v_Tag}-login";
            var unitId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-unit", 200));
            var categoryId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP, Ten_LSP, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{v_Tag}-category-code", 100), Text("@Name", $"{v_Tag}-category", 200));
            var productId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, @CategoryId, @UnitId, N'');",
                Text("@Code", $"{v_Tag}-product-code", 100), Text("@Name", $"{v_Tag}-product", 255), BigInt("@CategoryId", categoryId), BigInt("@UnitId", unitId));
            var supplierId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_NCC(Ma_NCC, Ten_NCC, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{v_Tag}-supplier-code", 100), Text("@Name", $"{v_Tag}-supplier", 200));
            var warehouseId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-warehouse", 255));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", v_Login, 100), BigInt("@WarehouseId", warehouseId));

            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, 100, 0);",
                BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 100, 0, 0, 100, 0, 0, 1);",
                Date("@Date", new DateTime(2026, 1, 31)), BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId));
            await CreateSnapshotAsync(v_Connection, v_Transaction, new DateTime(2026, 1, 31));

            var receiptId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, '2026-02-10', 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-receipt", 100), BigInt("@WarehouseId", warehouseId), BigInt("@SupplierId", supplierId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, 20, 1);",
                BigInt("@DocumentId", receiptId), BigInt("@ProductId", productId));
            var issueId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, '2026-02-20', 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-issue", 100), BigInt("@WarehouseId", warehouseId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@DocumentId, @ProductId, 5, 1);",
                BigInt("@DocumentId", issueId), BigInt("@ProductId", productId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "UPDATE dbo.InventoryBalance_Current SET CurrentQuantity = 115 WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Movement_Daily(Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue, IsValid) VALUES ('2026-02-10', @WarehouseId, @ProductId, 20, 0, 1), ('2026-02-20', @WarehouseId, @ProductId, 0, 5, 1);",
                BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES ('2026-02-10', @WarehouseId, @ProductId, 100, 20, 0, 120, 20, 0, 1), ('2026-02-20', @WarehouseId, @ProductId, 120, 0, 5, 115, 20, 5, 1);",
                BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Balance_Daily_Scope(Kho_ID, San_Pham_ID, First_Balance_Date, Last_Balance_Date) VALUES (@WarehouseId, @ProductId, '2026-01-31', '2026-02-20');",
                BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId));

            var v_Report = await ReadInventoryReportAsync(v_Connection, v_Transaction, v_Login);

            Assert.Equal(1, v_Report.TotalCount);
            Assert.Equal(100m, v_Report.Opening);
            Assert.Equal(20m, v_Report.Received);
            Assert.Equal(5m, v_Report.Issued);
            Assert.Equal(115m, v_Report.Closing);
            Assert.Equal(115m, v_Report.CurrentQuantity);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    private static async Task CreateSnapshotAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, DateTime p_dtmSnapshotDate)
    {
        await using var v_Command = new SqlCommand("sp_Inventory_Snapshot_Create_Daily", p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure };
        v_Command.Parameters.Add(Date("@Snapshot_Date", p_dtmSnapshotDate));
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task<InventoryReportRow> ReadInventoryReportAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Login)
    {
        await using var v_Command = new SqlCommand("sp_BC_Xuat_Nhap_Ton_Page", p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure };
        v_Command.Parameters.Add(Date("@Tu_Ngay", new DateTime(2026, 2, 1)));
        v_Command.Parameters.Add(Date("@Den_Ngay", new DateTime(2026, 2, 28)));
        v_Command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
        v_Command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 10 });
        v_Command.Parameters.Add(Text("@Ma_Dang_Nhap", p_Login, 100));

        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        var v_iTotalCount = v_Reader.GetInt32(0);
        Assert.True(await v_Reader.NextResultAsync());
        Assert.True(await v_Reader.ReadAsync());
        return new InventoryReportRow(v_iTotalCount, v_Reader.GetDecimal(5), v_Reader.GetDecimal(6), v_Reader.GetDecimal(7), v_Reader.GetDecimal(8), v_Reader.GetDecimal(9));
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
    private static SqlParameter Date(string p_Name, DateTime p_dtmValue)
    {
        return new(p_Name, SqlDbType.Date)
        {
            Value = p_dtmValue.Date
        };
    }

    private sealed record InventoryReportRow(int TotalCount, decimal Opening, decimal Received, decimal Issued, decimal Closing, decimal CurrentQuantity);
}
