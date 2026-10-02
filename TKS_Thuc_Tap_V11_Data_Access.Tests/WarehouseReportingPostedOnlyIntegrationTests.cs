using Microsoft.Data.SqlClient;
using System.Data;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseReportingPostedOnlyIntegrationTests
{
    private static string ConnectionString
    {
        get
        {
            return WarehouseTestDatabase.ConnectionString;
        }
    }

    [Fact]
    public async Task Reports_include_posted_documents_but_exclude_drafts()
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
            var v_Tag = $"TDD-POSTED-{Guid.NewGuid():N}";
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

            var draftReceiptId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, '2026-08-20', 0, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-draft-receipt", 100), BigInt("@WarehouseId", warehouseId), BigInt("@SupplierId", supplierId));
            var postedReceiptId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, '2026-08-20', 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-posted-receipt", 100), BigInt("@WarehouseId", warehouseId), BigInt("@SupplierId", supplierId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@ReceiptId, @ProductId, @Quantity, 100);",
                BigInt("@ReceiptId", draftReceiptId), BigInt("@ProductId", productId), Decimal("@Quantity", 5m));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@ReceiptId, @ProductId, @Quantity, 100);",
                BigInt("@ReceiptId", postedReceiptId), BigInt("@ProductId", productId), Decimal("@Quantity", 10m));

            var draftIssueId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, '2026-08-21', 0, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-draft-issue", 100), BigInt("@WarehouseId", warehouseId));
            var postedIssueId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, '2026-08-21', 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-posted-issue", 100), BigInt("@WarehouseId", warehouseId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@IssueId, @ProductId, @Quantity, 100);",
                BigInt("@IssueId", draftIssueId), BigInt("@ProductId", productId), Decimal("@Quantity", 2m));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@IssueId, @ProductId, @Quantity, 100);",
                BigInt("@IssueId", postedIssueId), BigInt("@ProductId", productId), Decimal("@Quantity", 3m));
            await ExecuteAsync(v_Connection, v_Transaction,
                """
                INSERT dbo.Inventory_Movement_Daily(Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue, IsValid)
                VALUES ('2026-08-20', @WarehouseId, @ProductId, 10, 0, 1), ('2026-08-21', @WarehouseId, @ProductId, 0, 3, 1);
                INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid)
                VALUES ('2026-08-20', @WarehouseId, @ProductId, 0, 10, 0, 10, 10, 0, 1), ('2026-08-21', @WarehouseId, @ProductId, 10, 0, 3, 7, 10, 3, 1);
                INSERT dbo.Inventory_Balance_Daily_Scope(Kho_ID, San_Pham_ID, First_Balance_Date, Last_Balance_Date)
                VALUES (@WarehouseId, @ProductId, '2026-08-20', '2026-08-21');
                """,
                BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId));

            var v_arrReceiptRows = await ReadDetailReportAsync(v_Connection, v_Transaction, "sp_BC_Chi_Tiet_Nhap", v_Login);
            Assert.Single(v_arrReceiptRows);
            Assert.Equal($"{v_Tag}-posted-receipt", v_arrReceiptRows[0].DocumentNumber);
            Assert.Equal(10m, v_arrReceiptRows[0].Quantity);

            var v_arrIssueRows = await ReadDetailReportAsync(v_Connection, v_Transaction, "sp_BC_Chi_Tiet_Xuat", v_Login);
            Assert.Single(v_arrIssueRows);
            Assert.Equal($"{v_Tag}-posted-issue", v_arrIssueRows[0].DocumentNumber);
            Assert.Equal(3m, v_arrIssueRows[0].Quantity);

            var v_arrInventoryRows = await ReadInventoryReportAsync(v_Connection, v_Transaction, "sp_BC_Xuat_Nhap_Ton", v_Login);
            var v_Inventory = Assert.Single(v_arrInventoryRows);
            Assert.Equal(productId, v_Inventory.ProductId);
            Assert.Equal(10m, v_Inventory.Received);
            Assert.Equal(3m, v_Inventory.Issued);
            Assert.Equal(7m, v_Inventory.Closing);

            var v_ReceiptPage = await ReadPagedDetailReportAsync(v_Connection, v_Transaction, "sp_BC_Chi_Tiet_Nhap_Page", v_Login);
            Assert.Equal(1, v_ReceiptPage.TotalCount);
            Assert.Single(v_ReceiptPage.Rows);
            var v_IssuePage = await ReadPagedDetailReportAsync(v_Connection, v_Transaction, "sp_BC_Chi_Tiet_Xuat_Page", v_Login);
            Assert.Equal(1, v_IssuePage.TotalCount);
            Assert.Single(v_IssuePage.Rows);

            var v_InventoryPage = await ReadPagedInventoryReportAsync(v_Connection, v_Transaction, v_Login);
            Assert.Equal(1, v_InventoryPage.TotalCount);
            var v_PagedInventory = Assert.Single(v_InventoryPage.Rows);
            Assert.Equal(10m, v_PagedInventory.Received);
            Assert.Equal(3m, v_PagedInventory.Issued);
            Assert.Equal(7m, v_PagedInventory.Closing);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Document_page_returns_the_persisted_post_status()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Tag = $"TDD-DOCUMENT-STATUS-{Guid.NewGuid():N}";
            var v_Login = $"{v_Tag}-login";
            var supplierId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_NCC(Ma_NCC, Ten_NCC, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{v_Tag}-supplier-code", 100), Text("@Name", $"{v_Tag}-supplier", 200));
            var warehouseId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-warehouse", 255));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", v_Login, 100), BigInt("@WarehouseId", warehouseId));

            await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, '2026-08-25', 0, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-draft-receipt", 100), BigInt("@WarehouseId", warehouseId), BigInt("@SupplierId", supplierId));
            await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, '2026-08-25', 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-posted-receipt", 100), BigInt("@WarehouseId", warehouseId), BigInt("@SupplierId", supplierId));
            await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, '2026-08-25', 0, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-draft-issue", 100), BigInt("@WarehouseId", warehouseId));
            await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, '2026-08-25', 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-posted-issue", 100), BigInt("@WarehouseId", warehouseId));

            var v_arrReceiptRows = await ReadDocumentPageAsync(v_Connection, v_Transaction, true, v_Login);
            Assert.Equal(2, v_arrReceiptRows.Count);
            Assert.False(v_arrReceiptRows.Single(row => row.DocumentNumber == $"{v_Tag}-draft-receipt").IsPosted);
            Assert.True(v_arrReceiptRows.Single(row => row.DocumentNumber == $"{v_Tag}-posted-receipt").IsPosted);

            var v_arrIssueRows = await ReadDocumentPageAsync(v_Connection, v_Transaction, false, v_Login);
            Assert.Equal(2, v_arrIssueRows.Count);
            Assert.False(v_arrIssueRows.Single(row => row.DocumentNumber == $"{v_Tag}-draft-issue").IsPosted);
            Assert.True(v_arrIssueRows.Single(row => row.DocumentNumber == $"{v_Tag}-posted-issue").IsPosted);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
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

    private static async Task<List<DetailRow>> ReadDetailReportAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Procedure, string p_Login)
    {
        await using var v_Command = ReportCommand(p_Connection, p_Transaction, p_Procedure, p_Login);
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        var v_arrRows = new List<DetailRow>();
        while (await v_Reader.ReadAsync())
            v_arrRows.Add(new DetailRow(v_Reader.GetString(1), v_Reader.GetDecimal(5)));
        return v_arrRows;
    }

    private static async Task<List<InventoryRow>> ReadInventoryReportAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Procedure, string p_Login)
    {
        await using var v_Command = ReportCommand(p_Connection, p_Transaction, p_Procedure, p_Login);
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        var v_arrRows = new List<InventoryRow>();
        while (await v_Reader.ReadAsync())
            v_arrRows.Add(new InventoryRow(v_Reader.GetInt64(2), v_Reader.GetDecimal(6), v_Reader.GetDecimal(7), v_Reader.GetDecimal(8)));
        return v_arrRows;
    }

    private static async Task<List<DocumentRow>> ReadDocumentPageAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, bool p_bIsReceipt, string p_Login)
    {
        await using var v_Command = new SqlCommand("sp_XNK_Document_Page", p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure };
        v_Command.Parameters.Add(new SqlParameter("@Is_Receipt", SqlDbType.Bit) { Value = p_bIsReceipt });
        v_Command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
        v_Command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 100 });
        v_Command.Parameters.Add(Text("@Search_Text", "", 100));
        v_Command.Parameters.Add(Text("@Ma_Dang_Nhap", p_Login, 100));

        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        Assert.Equal(2, v_Reader.GetInt32(0));
        Assert.True(await v_Reader.NextResultAsync());
        var v_iDocumentNumberOrdinal = v_Reader.GetOrdinal("So_Phieu");
        var v_iStatusOrdinal = v_Reader.GetOrdinal("Is_Posted");
        var v_arrRows = new List<DocumentRow>();
        while (await v_Reader.ReadAsync())
            v_arrRows.Add(new DocumentRow(v_Reader.GetString(v_iDocumentNumberOrdinal), v_Reader.GetBoolean(v_iStatusOrdinal)));
        return v_arrRows;
    }

    private static async Task<(int TotalCount, List<DetailRow> Rows)> ReadPagedDetailReportAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Procedure, string p_Login)
    {
        await using var v_Command = ReportCommand(p_Connection, p_Transaction, p_Procedure, p_Login, p_bPaged: true);
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        var v_iTotalCount = v_Reader.GetInt32(0);
        Assert.True(await v_Reader.NextResultAsync());
        var v_arrRows = new List<DetailRow>();
        while (await v_Reader.ReadAsync())
            v_arrRows.Add(new DetailRow(v_Reader.GetString(1), v_Reader.GetDecimal(5)));
        return (v_iTotalCount, v_arrRows);
    }

    private static async Task<(int TotalCount, List<InventoryRow> Rows)> ReadPagedInventoryReportAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Login)
    {
        await using var v_Command = ReportCommand(p_Connection, p_Transaction, "sp_BC_Xuat_Nhap_Ton_Page", p_Login, p_bPaged: true);
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        var v_iTotalCount = v_Reader.GetInt32(0);
        Assert.True(await v_Reader.NextResultAsync());
        var v_arrRows = new List<InventoryRow>();
        while (await v_Reader.ReadAsync())
            v_arrRows.Add(new InventoryRow(v_Reader.GetInt64(2), v_Reader.GetDecimal(6), v_Reader.GetDecimal(7), v_Reader.GetDecimal(8)));
        return (v_iTotalCount, v_arrRows);
    }

    private static SqlCommand ReportCommand(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Procedure, string p_Login, bool p_bPaged = false)
    {
        var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure };
        v_Command.Parameters.Add(Date("@Tu_Ngay", new DateTime(2026, 8, 1)));
        v_Command.Parameters.Add(Date("@Den_Ngay", new DateTime(2026, 8, 31)));
        if (p_bPaged)
        {
            v_Command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
            v_Command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 100 });
        }
        v_Command.Parameters.Add(Text("@Ma_Dang_Nhap", p_Login, 100));
        return v_Command;
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

    private sealed record DetailRow(string DocumentNumber, decimal Quantity);
    private sealed record InventoryRow(long ProductId, decimal Received, decimal Issued, decimal Closing);
    private sealed record DocumentRow(string DocumentNumber, bool IsPosted);
}
