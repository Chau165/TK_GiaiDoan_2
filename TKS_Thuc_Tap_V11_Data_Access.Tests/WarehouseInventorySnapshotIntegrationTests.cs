using Microsoft.Data.SqlClient;
using System.Data;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseInventorySnapshotIntegrationTests
{
    private const string ConnectionString = "Server=localhost;Database=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True;TrustServerCertificate=True;";

    [Fact]
    public async Task Inventory_report_uses_the_latest_daily_snapshot_and_only_later_movements()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var tag = $"TDD-SNAPSHOT-{Guid.NewGuid():N}";
            var login = $"{tag}-login";
            var unitId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-unit", 200));
            var categoryId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP, Ten_LSP, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{tag}-category-code", 100), Text("@Name", $"{tag}-category", 200));
            var productId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, @CategoryId, @UnitId, N'');",
                Text("@Code", $"{tag}-product-code", 100), Text("@Name", $"{tag}-product", 255), BigInt("@CategoryId", categoryId), BigInt("@UnitId", unitId));
            var supplierId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_NCC(Ma_NCC, Ten_NCC, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{tag}-supplier-code", 100), Text("@Name", $"{tag}-supplier", 200));
            var warehouseId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-warehouse", 255));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", login, 100), BigInt("@WarehouseId", warehouseId));

            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, 100, 0);",
                BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId));
            await CreateSnapshotAsync(connection, transaction, new DateTime(2026, 1, 31));

            var receiptId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, @SupplierId, '2026-02-10', 1, N'');",
                Text("@Number", $"{tag}-receipt", 100), BigInt("@WarehouseId", warehouseId), BigInt("@SupplierId", supplierId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, 20, 1);",
                BigInt("@DocumentId", receiptId), BigInt("@ProductId", productId));
            var issueId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, '2026-02-20', 1, N'');",
                Text("@Number", $"{tag}-issue", 100), BigInt("@WarehouseId", warehouseId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@DocumentId, @ProductId, 5, 1);",
                BigInt("@DocumentId", issueId), BigInt("@ProductId", productId));
            await ExecuteAsync(connection, transaction,
                "UPDATE dbo.InventoryBalance_Current SET CurrentQuantity = 115 WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId));

            var report = await ReadInventoryReportAsync(connection, transaction, login);

            Assert.Equal(1, report.TotalCount);
            Assert.Equal(100m, report.Opening);
            Assert.Equal(20m, report.Received);
            Assert.Equal(5m, report.Issued);
            Assert.Equal(115m, report.Closing);
            Assert.Equal(115m, report.CurrentQuantity);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task CreateSnapshotAsync(SqlConnection connection, SqlTransaction transaction, DateTime snapshotDate)
    {
        await using var command = new SqlCommand("sp_Inventory_Snapshot_Create_Daily", connection, transaction) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add(Date("@Snapshot_Date", snapshotDate));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<InventoryReportRow> ReadInventoryReportAsync(SqlConnection connection, SqlTransaction transaction, string login)
    {
        await using var command = new SqlCommand("sp_BC_Xuat_Nhap_Ton_Page", connection, transaction) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add(Date("@Tu_Ngay", new DateTime(2026, 2, 1)));
        command.Parameters.Add(Date("@Den_Ngay", new DateTime(2026, 2, 28)));
        command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
        command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 10 });
        command.Parameters.Add(Text("@Ma_Dang_Nhap", login, 100));

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var totalCount = reader.GetInt32(0);
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        return new InventoryReportRow(totalCount, reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9));
    }

    private static async Task<long> InsertIdAsync(SqlConnection connection, SqlTransaction transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.NVarChar, size) { Value = value };
    private static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt) { Value = value };
    private static SqlParameter Date(string name, DateTime value) => new(name, SqlDbType.Date) { Value = value.Date };

    private sealed record InventoryReportRow(int TotalCount, decimal Opening, decimal Received, decimal Issued, decimal Closing, decimal CurrentQuantity);
}
