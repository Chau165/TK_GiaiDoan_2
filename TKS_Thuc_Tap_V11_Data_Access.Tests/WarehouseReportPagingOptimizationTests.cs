using Microsoft.Data.SqlClient;
using System.Data;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseReportPagingOptimizationTests
{
    private static string ConnectionString => WarehouseTestDatabase.ConnectionString;

    [Fact]
    public async Task Effective_report_procedures_use_narrow_paging_scopes_and_reuse_authorization()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var inventoryDefinition = await ReadDefinitionAsync(connection, "sp_BC_Xuat_Nhap_Ton_Page");
        var receiptDefinition = await ReadDefinitionAsync(connection, "sp_BC_Chi_Tiet_Nhap_Page");
        var issueDefinition = await ReadDefinitionAsync(connection, "sp_BC_Chi_Tiet_Xuat_Page");

        Assert.Contains("AuthorizedScope", inventoryDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CROSS APPLY", inventoryDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#WarehouseScopeResult_Snapshot", inventoryDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#ReportKeys", inventoryDefinition, StringComparison.OrdinalIgnoreCase);

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
            await ExecuteAsync(connection, transaction,
                "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
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
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, '2026-09-10', 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{tag}-receipt-a", 100), BigInt("@WarehouseId", warehouseA), BigInt("@SupplierId", supplierId));
            var receiptB = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, '2026-09-10', 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{tag}-receipt-b", 100), BigInt("@WarehouseId", warehouseB), BigInt("@SupplierId", supplierId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, 11, 1);",
                BigInt("@DocumentId", receiptA), BigInt("@ProductId", productId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, 22, 1);",
                BigInt("@DocumentId", receiptB), BigInt("@ProductId", productId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.Inventory_Movement_Daily(Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue, IsValid) VALUES ('2026-09-10', @WarehouseA, @ProductId, 11, 0, 1), ('2026-09-10', @WarehouseB, @ProductId, 22, 0, 1); INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES ('2026-09-10', @WarehouseA, @ProductId, 0, 11, 0, 11, 11, 0, 1), ('2026-09-10', @WarehouseB, @ProductId, 0, 22, 0, 22, 22, 0, 1); INSERT dbo.Inventory_Balance_Daily_Scope(Kho_ID, San_Pham_ID, First_Balance_Date, Last_Balance_Date) VALUES (@WarehouseA, @ProductId, '2026-09-10', '2026-09-10'), (@WarehouseB, @ProductId, '2026-09-10', '2026-09-10');",
                BigInt("@WarehouseA", warehouseA), BigInt("@WarehouseB", warehouseB), BigInt("@ProductId", productId));

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

    [Fact]
    public async Task Selecting_a_warehouse_restricts_reports_and_rejects_unassigned_warehouse()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            await ExecuteAsync(connection, transaction,
                "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            var tag = $"TDD-RF-{Guid.NewGuid():N}";
            var login = $"{tag}-login";
            var reportDate = DateTime.Today;
            await InsertIdAsync(connection, transaction,
                "DECLARE @UserId BIGINT; SELECT @UserId = ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX); INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) OUTPUT INSERTED.Auto_ID VALUES (@UserId, @Login, @Name, 0);",
                Text("@Login", login, 100), Text("@Name", $"{tag}-user", 200));
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
            var warehouseC = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-warehouse-c", 255));

            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", login, 100), BigInt("@WarehouseId", warehouseA));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", login, 100), BigInt("@WarehouseId", warehouseB));

            var receiptA = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, @Date, 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{tag}-receipt-a", 100), BigInt("@WarehouseId", warehouseA), BigInt("@SupplierId", supplierId), Date("@Date", reportDate));
            var receiptB = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, @Date, 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{tag}-receipt-b", 100), BigInt("@WarehouseId", warehouseB), BigInt("@SupplierId", supplierId), Date("@Date", reportDate));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, 11, 1);",
                BigInt("@DocumentId", receiptA), BigInt("@ProductId", productId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, 22, 1);",
                BigInt("@DocumentId", receiptB), BigInt("@ProductId", productId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, @Quantity, 0);",
                BigInt("@WarehouseId", warehouseA), BigInt("@ProductId", productId), Decimal("@Quantity", 11));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, @Quantity, 0);",
                BigInt("@WarehouseId", warehouseB), BigInt("@ProductId", productId), Decimal("@Quantity", 22));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.Inventory_Movement_Daily(Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue, IsValid) VALUES (@Date, @WarehouseA, @ProductId, 11, 0, 1), (@Date, @WarehouseB, @ProductId, 22, 0, 1); INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseA, @ProductId, 0, 11, 0, 11, 11, 0, 1), (@Date, @WarehouseB, @ProductId, 0, 22, 0, 22, 22, 0, 1); INSERT dbo.Inventory_Balance_Daily_Scope(Kho_ID, San_Pham_ID, First_Balance_Date, Last_Balance_Date) VALUES (@WarehouseA, @ProductId, @Date, @Date), (@WarehouseB, @ProductId, @Date, @Date);",
                Date("@Date", reportDate), BigInt("@WarehouseA", warehouseA), BigInt("@WarehouseB", warehouseB), BigInt("@ProductId", productId));

            var inventory = await ReadPagedInventoryAsync(connection, transaction, login, warehouseA, reportDate, reportDate);
            Assert.Equal(1, inventory.TotalCount);
            Assert.Equal(warehouseA, Assert.Single(inventory.Rows).WarehouseId);

            var detail = await ReadPagedDetailAsync(connection, transaction, "sp_BC_Chi_Tiet_Nhap_Page", login, warehouseB, reportDate, reportDate);
            Assert.Equal(1, detail.TotalCount);
            Assert.Equal($"{tag}-receipt-b", Assert.Single(detail.Rows).DocumentNumber);

            var error = await Assert.ThrowsAsync<SqlException>(() =>
                ReadPagedInventoryAsync(connection, transaction, login, warehouseC));
            Assert.Equal(51054, error.Number);
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
        SqlConnection connection, SqlTransaction transaction, string login, long? warehouseId = null, DateTime? from = null, DateTime? to = null)
    {
        await using var command = ReportCommand(connection, transaction, "sp_BC_Xuat_Nhap_Ton_Page", login, warehouseId, from, to);
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
        SqlConnection connection, SqlTransaction transaction, string procedure, string login, long? warehouseId = null, DateTime? from = null, DateTime? to = null)
    {
        await using var command = ReportCommand(connection, transaction, procedure, login, warehouseId, from, to);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var totalCount = reader.GetInt32(0);
        Assert.True(await reader.NextResultAsync());
        var rows = new List<DetailRow>();
        while (await reader.ReadAsync())
            rows.Add(new DetailRow(reader.GetString(1), reader.GetDecimal(5)));
        return (totalCount, rows);
    }

    private static SqlCommand ReportCommand(SqlConnection connection, SqlTransaction transaction, string procedure, string login, long? warehouseId = null, DateTime? from = null, DateTime? to = null)
    {
        var command = new SqlCommand(procedure, connection, transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add(Date("@Tu_Ngay", from ?? new DateTime(2026, 9, 1)));
        command.Parameters.Add(Date("@Den_Ngay", to ?? new DateTime(2026, 9, 30)));
        command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
        command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 10 });
        command.Parameters.Add(Text("@Ma_Dang_Nhap", login, 100));
        command.Parameters.Add(new SqlParameter("@Kho_ID", SqlDbType.BigInt) { Value = warehouseId ?? (object)DBNull.Value });
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
    private static SqlParameter Decimal(string name, decimal value) => new(name, SqlDbType.Decimal) { Precision = 18, Scale = 3, Value = value };

    private sealed record DetailRow(string DocumentNumber, decimal Quantity);
    private sealed record InventoryRow(long WarehouseId, decimal Received);
}
