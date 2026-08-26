using Microsoft.Data.SqlClient;
using System.Data;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseReportPagingOptimizationTests
{
    private const string ConnectionString = "Server=localhost;Database=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True;TrustServerCertificate=True;";

    [Fact]
    public async Task Effective_report_procedures_use_narrow_paging_scopes_and_reuse_authorization()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var inventoryDefinition = await ReadDefinitionAsync(connection, "sp_BC_Xuat_Nhap_Ton_Page");
        var receiptDefinition = await ReadDefinitionAsync(connection, "sp_BC_Chi_Tiet_Nhap_Page");
        var issueDefinition = await ReadDefinitionAsync(connection, "sp_BC_Chi_Tiet_Xuat_Page");

        Assert.Contains("#ReportKeys", inventoryDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SELECT COUNT(*) AS Total_Count FROM #ReportKeys", inventoryDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#WarehouseScopeResult_Snapshot", inventoryDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#MovementAggregate", inventoryDefinition, StringComparison.OrdinalIgnoreCase);

        foreach (var definition in new[] { receiptDefinition, issueDefinition })
        {
            Assert.Contains("#AuthorizedWarehouse", definition, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("#DetailScope", definition, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SELECT COUNT(*) AS Total_Count", definition, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("OFFSET", definition, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("EXISTS", definition, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Detail_report_procedures_page_from_covering_indexes_without_full_scope_materialization()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var receiptDefinition = await ReadDefinitionAsync(connection, "sp_BC_Chi_Tiet_Nhap_Page");
        var issueDefinition = await ReadDefinitionAsync(connection, "sp_BC_Chi_Tiet_Xuat_Page");

        Assert.DoesNotContain("#DetailScope", receiptDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#DetailScope", issueDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", receiptDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", issueDefinition, StringComparison.OrdinalIgnoreCase);

        Assert.True(await IndexExistsAsync(connection, "IX_tbl_XNK_Nhap_Kho_Report_Page"));
        Assert.True(await IndexExistsAsync(connection, "IX_tbl_XNK_Xuat_Kho_Report_Page"));
    }

    [Fact]
    public async Task Paged_inventory_and_detail_reports_preserve_count_rows_and_warehouse_scope()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var tag = $"TDD-PAGING-{Guid.NewGuid():N}";
            var loginA = $"{tag}-A";
            var loginB = $"{tag}-B";
            var productId = await InsertIdAsync(connection, transaction,
                "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
            var supplierId = await InsertIdAsync(connection, transaction,
                "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseA = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-warehouse-a", 255));
            var warehouseB = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-warehouse-b", 255));

            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", loginA, 100), BigInt("@WarehouseId", warehouseA));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", loginB, 100), BigInt("@WarehouseId", warehouseB));

            var receiptA = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, @SupplierId, '2026-09-10', 1, N'');",
                Text("@Number", $"{tag}-receipt-a", 100), BigInt("@WarehouseId", warehouseA), BigInt("@SupplierId", supplierId));
            var receiptB = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, @SupplierId, '2026-09-10', 1, N'');",
                Text("@Number", $"{tag}-receipt-b", 100), BigInt("@WarehouseId", warehouseB), BigInt("@SupplierId", supplierId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, 11, 1);",
                BigInt("@DocumentId", receiptA), BigInt("@ProductId", productId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, 22, 1);",
                BigInt("@DocumentId", receiptB), BigInt("@ProductId", productId));

            var inventoryA = await ReadPagedInventoryAsync(connection, transaction, loginA);
            Assert.Equal(1, inventoryA.TotalCount);
            Assert.Single(inventoryA.Rows);
            Assert.Equal(warehouseA, inventoryA.Rows[0].WarehouseId);
            Assert.Equal(11m, inventoryA.Rows[0].Received);

            var inventoryB = await ReadPagedInventoryAsync(connection, transaction, loginB);
            Assert.Equal(1, inventoryB.TotalCount);
            Assert.Single(inventoryB.Rows);
            Assert.Equal(warehouseB, inventoryB.Rows[0].WarehouseId);
            Assert.Equal(22m, inventoryB.Rows[0].Received);

            var detailA = await ReadPagedDetailAsync(connection, transaction, "sp_BC_Chi_Tiet_Nhap_Page", loginA);
            Assert.Equal(1, detailA.TotalCount);
            Assert.Single(detailA.Rows);
            Assert.Equal($"{tag}-receipt-a", detailA.Rows[0].DocumentNumber);

            var detailB = await ReadPagedDetailAsync(connection, transaction, "sp_BC_Chi_Tiet_Nhap_Page", loginB);
            Assert.Equal(1, detailB.TotalCount);
            Assert.Single(detailB.Rows);
            Assert.Equal($"{tag}-receipt-b", detailB.Rows[0].DocumentNumber);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task<string> ReadDefinitionAsync(SqlConnection connection, string procedureName)
    {
        await using var command = new SqlCommand(
            "SELECT OBJECT_DEFINITION(OBJECT_ID(@ProcedureName));", connection);
        command.Parameters.Add(Text("@ProcedureName", $"dbo.{procedureName}", 256));
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? "";
    }

    private static async Task<bool> IndexExistsAsync(SqlConnection connection, string indexName)
    {
        await using var command = new SqlCommand(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = @IndexName) THEN 1 ELSE 0 END;", connection);
        command.Parameters.Add(Text("@IndexName", indexName, 256));
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<(int TotalCount, List<InventoryRow> Rows)> ReadPagedInventoryAsync(
        SqlConnection connection, SqlTransaction transaction, string login)
    {
        await using var command = ReportCommand(connection, transaction, "sp_BC_Xuat_Nhap_Ton_Page", login);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var totalCount = reader.GetInt32(0);
        Assert.True(await reader.NextResultAsync());
        var rows = new List<InventoryRow>();
        while (await reader.ReadAsync())
            rows.Add(new InventoryRow(reader.GetInt64(0), reader.GetDecimal(6)));
        return (totalCount, rows);
    }

    private static async Task<(int TotalCount, List<DetailRow> Rows)> ReadPagedDetailAsync(
        SqlConnection connection, SqlTransaction transaction, string procedure, string login)
    {
        await using var command = ReportCommand(connection, transaction, procedure, login);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var totalCount = reader.GetInt32(0);
        Assert.True(await reader.NextResultAsync());
        var rows = new List<DetailRow>();
        while (await reader.ReadAsync())
            rows.Add(new DetailRow(reader.GetString(1), reader.GetDecimal(5)));
        return (totalCount, rows);
    }

    private static SqlCommand ReportCommand(SqlConnection connection, SqlTransaction transaction, string procedure, string login)
    {
        var command = new SqlCommand(procedure, connection, transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add(Date("@Tu_Ngay", new DateTime(2026, 9, 1)));
        command.Parameters.Add(Date("@Den_Ngay", new DateTime(2026, 9, 30)));
        command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
        command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 10 });
        command.Parameters.Add(Text("@Ma_Dang_Nhap", login, 100));
        return command;
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

    private sealed record DetailRow(string DocumentNumber, decimal Quantity);
    private sealed record InventoryRow(long WarehouseId, decimal Received);
}
