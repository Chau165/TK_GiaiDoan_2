using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseInventorySnapshotScopeIntegrationTests
{
    private const string ConnectionString = "Server=localhost;Database=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True;TrustServerCertificate=True;";
    private static readonly DateTime ReportFrom = new(2099, 2, 21);
    private static readonly DateTime ReportTo = new(2099, 2, 28);

    [Fact]
    public async Task Reports_choose_the_latest_valid_snapshot_per_warehouse_and_product_scope()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var tag = $"TDD-SNAPSHOT-SCOPE-{Guid.NewGuid():N}";
            var login = $"{tag}-login";
            var productId = await ScalarLongAsync(connection, transaction,
                "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
            var supplierId = await ScalarLongAsync(connection, transaction,
                "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseA = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-warehouse-a", 255));
            var warehouseB = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-warehouse-b", 255));

            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseA), (@Login, @WarehouseB);",
                Text("@Login", login, 100), BigInt("@WarehouseA", warehouseA), BigInt("@WarehouseB", warehouseB));
            await ExecuteAsync(connection, transaction,
                "UPDATE dbo.InventoryMovement_AggregateState SET IsInitialized = 1 WHERE State_ID = 1;");

            var receiptAOpening = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, @SupplierId, '2099-02-10', 1, N'');",
                Text("@Number", $"{tag}-receipt-a-opening", 100), BigInt("@WarehouseId", warehouseA), BigInt("@SupplierId", supplierId));
            var receiptAPeriod = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, @SupplierId, '2099-02-22', 1, N'');",
                Text("@Number", $"{tag}-receipt-a-period", 100), BigInt("@WarehouseId", warehouseA), BigInt("@SupplierId", supplierId));
            var receiptBPreSnapshot = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, @SupplierId, '2099-02-10', 1, N'');",
                Text("@Number", $"{tag}-receipt-b-presnapshot", 100), BigInt("@WarehouseId", warehouseB), BigInt("@SupplierId", supplierId));
            var issueBPeriod = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, '2099-02-22', 1, N'');",
                Text("@Number", $"{tag}-issue-b-period", 100), BigInt("@WarehouseId", warehouseB));

            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
                BigInt("@DocumentId", receiptAOpening), BigInt("@ProductId", productId), Decimal("@Quantity", 10));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
                BigInt("@DocumentId", receiptAPeriod), BigInt("@ProductId", productId), Decimal("@Quantity", 5));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
                BigInt("@DocumentId", receiptBPreSnapshot), BigInt("@ProductId", productId), Decimal("@Quantity", 77));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
                BigInt("@DocumentId", issueBPeriod), BigInt("@ProductId", productId), Decimal("@Quantity", 20));

            await ExecuteAsync(connection, transaction,
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

            await ExecuteAsync(connection, transaction,
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

            var paged = await ReadPagedAsync(connection, transaction, login);
            var nonPaged = await ReadNonPagedAsync(connection, transaction, login);

            Assert.Equal(2, paged.TotalCount);
            AssertScope(paged.Rows, warehouseA, opening: 110m, received: 5m, issued: 0m, closing: 115m);
            AssertScope(paged.Rows, warehouseB, opening: 200m, received: 0m, issued: 20m, closing: 180m);
            AssertScope(nonPaged, warehouseA, opening: 110m, received: 5m, issued: 0m, closing: 115m);
            AssertScope(nonPaged, warehouseB, opening: 200m, received: 0m, issued: 20m, closing: 180m);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task<PagedReport> ReadPagedAsync(SqlConnection connection, SqlTransaction transaction, string login)
    {
        await using var command = new SqlCommand("sp_BC_Xuat_Nhap_Ton_Page", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        AddReportParameters(command, login);
        command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
        command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 20 });

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var totalCount = reader.GetInt32(0);
        Assert.True(await reader.NextResultAsync());
        return new PagedReport(totalCount, await ReadRowsAsync(reader));
    }

    private static async Task<List<ReportRow>> ReadNonPagedAsync(SqlConnection connection, SqlTransaction transaction, string login)
    {
        await using var command = new SqlCommand("sp_BC_Xuat_Nhap_Ton", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        AddReportParameters(command, login);

        await using var reader = await command.ExecuteReaderAsync();
        return await ReadRowsAsync(reader);
    }

    private static void AddReportParameters(SqlCommand command, string login)
    {
        command.Parameters.Add(Date("@Tu_Ngay", ReportFrom));
        command.Parameters.Add(Date("@Den_Ngay", ReportTo));
        command.Parameters.Add(Text("@Ma_Dang_Nhap", login, 100));
    }

    private static async Task<List<ReportRow>> ReadRowsAsync(SqlDataReader reader)
    {
        var rows = new List<ReportRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new ReportRow(
                reader.GetInt64(0),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8)));
        }

        return rows;
    }

    private static void AssertScope(IEnumerable<ReportRow> rows, long warehouseId, decimal opening, decimal received, decimal issued, decimal closing)
    {
        var row = Assert.Single(rows.Where(x => x.WarehouseId == warehouseId));
        Assert.Equal(opening, row.Opening);
        Assert.Equal(received, row.Received);
        Assert.Equal(issued, row.Issued);
        Assert.Equal(closing, row.Closing);
    }

    private static async Task<long> InsertIdAsync(SqlConnection connection, SqlTransaction transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> ScalarLongAsync(SqlConnection connection, SqlTransaction transaction, string sql)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
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
    private static SqlParameter Decimal(string name, decimal value) => new(name, SqlDbType.Decimal) { Precision = 18, Scale = 3, Value = value };
    private static SqlParameter Date(string name, DateTime value) => new(name, SqlDbType.Date) { Value = value.Date };

    private sealed record PagedReport(int TotalCount, List<ReportRow> Rows);
    private sealed record ReportRow(long WarehouseId, decimal Opening, decimal Received, decimal Issued, decimal Closing);
}
