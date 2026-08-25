using Microsoft.Data.SqlClient;
using System.Data;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseReportingPostedOnlyIntegrationTests
{
    private const string ConnectionString = "Server=localhost;Database=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True;TrustServerCertificate=True;";

    [Fact]
    public async Task Reports_include_posted_documents_but_exclude_drafts()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var tag = $"TDD-POSTED-{Guid.NewGuid():N}";
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

            var draftReceiptId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, @SupplierId, '2026-08-20', 0, N'');",
                Text("@Number", $"{tag}-draft-receipt", 100), BigInt("@WarehouseId", warehouseId), BigInt("@SupplierId", supplierId));
            var postedReceiptId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, @SupplierId, '2026-08-20', 1, N'');",
                Text("@Number", $"{tag}-posted-receipt", 100), BigInt("@WarehouseId", warehouseId), BigInt("@SupplierId", supplierId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@ReceiptId, @ProductId, @Quantity, 100);",
                BigInt("@ReceiptId", draftReceiptId), BigInt("@ProductId", productId), Decimal("@Quantity", 5m));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@ReceiptId, @ProductId, @Quantity, 100);",
                BigInt("@ReceiptId", postedReceiptId), BigInt("@ProductId", productId), Decimal("@Quantity", 10m));

            var draftIssueId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, '2026-08-21', 0, N'');",
                Text("@Number", $"{tag}-draft-issue", 100), BigInt("@WarehouseId", warehouseId));
            var postedIssueId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, '2026-08-21', 1, N'');",
                Text("@Number", $"{tag}-posted-issue", 100), BigInt("@WarehouseId", warehouseId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@IssueId, @ProductId, @Quantity, 100);",
                BigInt("@IssueId", draftIssueId), BigInt("@ProductId", productId), Decimal("@Quantity", 2m));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@IssueId, @ProductId, @Quantity, 100);",
                BigInt("@IssueId", postedIssueId), BigInt("@ProductId", productId), Decimal("@Quantity", 3m));

            var receiptRows = await ReadDetailReportAsync(connection, transaction, "sp_BC_Chi_Tiet_Nhap", login);
            Assert.Single(receiptRows);
            Assert.Equal($"{tag}-posted-receipt", receiptRows[0].DocumentNumber);
            Assert.Equal(10m, receiptRows[0].Quantity);

            var issueRows = await ReadDetailReportAsync(connection, transaction, "sp_BC_Chi_Tiet_Xuat", login);
            Assert.Single(issueRows);
            Assert.Equal($"{tag}-posted-issue", issueRows[0].DocumentNumber);
            Assert.Equal(3m, issueRows[0].Quantity);

            var inventoryRows = await ReadInventoryReportAsync(connection, transaction, "sp_BC_Xuat_Nhap_Ton", login);
            var inventory = Assert.Single(inventoryRows);
            Assert.Equal(productId, inventory.ProductId);
            Assert.Equal(10m, inventory.Received);
            Assert.Equal(3m, inventory.Issued);
            Assert.Equal(7m, inventory.Closing);

            var receiptPage = await ReadPagedDetailReportAsync(connection, transaction, "sp_BC_Chi_Tiet_Nhap_Page", login);
            Assert.Equal(1, receiptPage.TotalCount);
            Assert.Single(receiptPage.Rows);
            var issuePage = await ReadPagedDetailReportAsync(connection, transaction, "sp_BC_Chi_Tiet_Xuat_Page", login);
            Assert.Equal(1, issuePage.TotalCount);
            Assert.Single(issuePage.Rows);

            var inventoryPage = await ReadPagedInventoryReportAsync(connection, transaction, login);
            Assert.Equal(1, inventoryPage.TotalCount);
            var pagedInventory = Assert.Single(inventoryPage.Rows);
            Assert.Equal(10m, pagedInventory.Received);
            Assert.Equal(3m, pagedInventory.Issued);
            Assert.Equal(7m, pagedInventory.Closing);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Document_page_returns_the_persisted_post_status()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var tag = $"TDD-DOCUMENT-STATUS-{Guid.NewGuid():N}";
            var login = $"{tag}-login";
            var supplierId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_NCC(Ma_NCC, Ten_NCC, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{tag}-supplier-code", 100), Text("@Name", $"{tag}-supplier", 200));
            var warehouseId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-warehouse", 255));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", login, 100), BigInt("@WarehouseId", warehouseId));

            await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, @SupplierId, '2026-08-25', 0, N'');",
                Text("@Number", $"{tag}-draft-receipt", 100), BigInt("@WarehouseId", warehouseId), BigInt("@SupplierId", supplierId));
            await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, @SupplierId, '2026-08-25', 1, N'');",
                Text("@Number", $"{tag}-posted-receipt", 100), BigInt("@WarehouseId", warehouseId), BigInt("@SupplierId", supplierId));
            await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, '2026-08-25', 0, N'');",
                Text("@Number", $"{tag}-draft-issue", 100), BigInt("@WarehouseId", warehouseId));
            await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, '2026-08-25', 1, N'');",
                Text("@Number", $"{tag}-posted-issue", 100), BigInt("@WarehouseId", warehouseId));

            var receiptRows = await ReadDocumentPageAsync(connection, transaction, true, login);
            Assert.Equal(2, receiptRows.Count);
            Assert.False(receiptRows.Single(row => row.DocumentNumber == $"{tag}-draft-receipt").IsPosted);
            Assert.True(receiptRows.Single(row => row.DocumentNumber == $"{tag}-posted-receipt").IsPosted);

            var issueRows = await ReadDocumentPageAsync(connection, transaction, false, login);
            Assert.Equal(2, issueRows.Count);
            Assert.False(issueRows.Single(row => row.DocumentNumber == $"{tag}-draft-issue").IsPosted);
            Assert.True(issueRows.Single(row => row.DocumentNumber == $"{tag}-posted-issue").IsPosted);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
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

    private static async Task<List<DetailRow>> ReadDetailReportAsync(SqlConnection connection, SqlTransaction transaction, string procedure, string login)
    {
        await using var command = ReportCommand(connection, transaction, procedure, login);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<DetailRow>();
        while (await reader.ReadAsync())
            rows.Add(new DetailRow(reader.GetString(1), reader.GetDecimal(5)));
        return rows;
    }

    private static async Task<List<InventoryRow>> ReadInventoryReportAsync(SqlConnection connection, SqlTransaction transaction, string procedure, string login)
    {
        await using var command = ReportCommand(connection, transaction, procedure, login);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<InventoryRow>();
        while (await reader.ReadAsync())
            rows.Add(new InventoryRow(reader.GetInt64(2), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8)));
        return rows;
    }

    private static async Task<List<DocumentRow>> ReadDocumentPageAsync(SqlConnection connection, SqlTransaction transaction, bool isReceipt, string login)
    {
        await using var command = new SqlCommand("sp_XNK_Document_Page", connection, transaction) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add(new SqlParameter("@Is_Receipt", SqlDbType.Bit) { Value = isReceipt });
        command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
        command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 100 });
        command.Parameters.Add(Text("@Search_Text", "", 100));
        command.Parameters.Add(Text("@Ma_Dang_Nhap", login, 100));

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.True(await reader.NextResultAsync());
        var documentNumberOrdinal = reader.GetOrdinal("So_Phieu");
        var statusOrdinal = reader.GetOrdinal("Is_Posted");
        var rows = new List<DocumentRow>();
        while (await reader.ReadAsync())
            rows.Add(new DocumentRow(reader.GetString(documentNumberOrdinal), reader.GetBoolean(statusOrdinal)));
        return rows;
    }

    private static async Task<(int TotalCount, List<DetailRow> Rows)> ReadPagedDetailReportAsync(SqlConnection connection, SqlTransaction transaction, string procedure, string login)
    {
        await using var command = ReportCommand(connection, transaction, procedure, login, paged: true);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var totalCount = reader.GetInt32(0);
        Assert.True(await reader.NextResultAsync());
        var rows = new List<DetailRow>();
        while (await reader.ReadAsync())
            rows.Add(new DetailRow(reader.GetString(1), reader.GetDecimal(5)));
        return (totalCount, rows);
    }

    private static async Task<(int TotalCount, List<InventoryRow> Rows)> ReadPagedInventoryReportAsync(SqlConnection connection, SqlTransaction transaction, string login)
    {
        await using var command = ReportCommand(connection, transaction, "sp_BC_Xuat_Nhap_Ton_Page", login, paged: true);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var totalCount = reader.GetInt32(0);
        Assert.True(await reader.NextResultAsync());
        var rows = new List<InventoryRow>();
        while (await reader.ReadAsync())
            rows.Add(new InventoryRow(reader.GetInt64(2), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8)));
        return (totalCount, rows);
    }

    private static SqlCommand ReportCommand(SqlConnection connection, SqlTransaction transaction, string procedure, string login, bool paged = false)
    {
        var command = new SqlCommand(procedure, connection, transaction) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add(Date("@Tu_Ngay", new DateTime(2026, 8, 1)));
        command.Parameters.Add(Date("@Den_Ngay", new DateTime(2026, 8, 31)));
        if (paged)
        {
            command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
            command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 100 });
        }
        command.Parameters.Add(Text("@Ma_Dang_Nhap", login, 100));
        return command;
    }

    private static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.NVarChar, size) { Value = value };
    private static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt) { Value = value };
    private static SqlParameter Decimal(string name, decimal value) => new(name, SqlDbType.Decimal) { Precision = 18, Scale = 3, Value = value };
    private static SqlParameter Date(string name, DateTime value) => new(name, SqlDbType.Date) { Value = value.Date };

    private sealed record DetailRow(string DocumentNumber, decimal Quantity);
    private sealed record InventoryRow(long ProductId, decimal Received, decimal Issued, decimal Closing);
    private sealed record DocumentRow(string DocumentNumber, bool IsPosted);
}
