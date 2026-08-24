using System.Data;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseCrudContractIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString = "Server=localhost;Database=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True;TrustServerCertificate=True;";

    private readonly string m_strTag = $"TDD-CRUD-{Guid.NewGuid():N}"[..21];
    private long m_iUnitId;
    private long m_iCategoryId;
    private long m_iProductId;
    private long m_iSecondProductId;
    private long m_iSupplierId;
    private long m_iWarehouseAId;
    private long m_iWarehouseBId;

    public async Task InitializeAsync()
    {
        CConfig.TKS_Thuc_Tap_V11_Conn_String = ConnectionString;

        m_iUnitId = await InsertIdAsync(
            "INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
            NVarChar("@Name", $"{m_strTag}-Unit", 200));
        m_iCategoryId = await InsertIdAsync(
            "INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP, Ten_LSP, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
            NVarChar("@Code", $"{m_strTag}-CAT", 100), NVarChar("@Name", $"{m_strTag}-Category", 200));
        m_iProductId = await InsertIdAsync(
            "INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, @CategoryId, @UnitId, N'');",
            NVarChar("@Code", $"{m_strTag}-P1", 100), NVarChar("@Name", $"{m_strTag}-Product-1", 255), BigInt("@CategoryId", m_iCategoryId), BigInt("@UnitId", m_iUnitId));
        m_iSecondProductId = await InsertIdAsync(
            "INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, @CategoryId, @UnitId, N'');",
            NVarChar("@Code", $"{m_strTag}-P2", 100), NVarChar("@Name", $"{m_strTag}-Product-2", 255), BigInt("@CategoryId", m_iCategoryId), BigInt("@UnitId", m_iUnitId));
        m_iSupplierId = await InsertIdAsync(
            "INSERT dbo.tbl_DM_NCC(Ma_NCC, Ten_NCC, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
            NVarChar("@Code", $"{m_strTag}-NCC", 100), NVarChar("@Name", $"{m_strTag}-Supplier", 200));
        m_iWarehouseAId = await InsertIdAsync(
            "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
            NVarChar("@Name", $"{m_strTag}-Warehouse-A", 255));
        m_iWarehouseBId = await InsertIdAsync(
            "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
            NVarChar("@Name", $"{m_strTag}-Warehouse-B", 255));
    }

    public async Task DisposeAsync()
    {
        await ExecuteAsync("DELETE dbo.InventoryBalance_Current WHERE San_Pham_ID IN (@ProductId, @SecondProductId);", BigInt("@ProductId", m_iProductId), BigInt("@SecondProductId", m_iSecondProductId));
        await ExecuteAsync("DELETE dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho LIKE @Tag;", NVarChar("@Tag", $"{m_strTag}%", 100));
        await ExecuteAsync("DELETE dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho LIKE @Tag;", NVarChar("@Tag", $"{m_strTag}%", 100));
        await ExecuteAsync("DELETE dbo.tbl_DM_San_Pham WHERE Auto_ID IN (@ProductId, @SecondProductId);", BigInt("@ProductId", m_iProductId), BigInt("@SecondProductId", m_iSecondProductId));
        await ExecuteAsync("DELETE dbo.tbl_DM_NCC WHERE Auto_ID = @SupplierId;", BigInt("@SupplierId", m_iSupplierId));
        await ExecuteAsync("DELETE dbo.tbl_DM_Kho WHERE Auto_ID IN (@WarehouseAId, @WarehouseBId);", BigInt("@WarehouseAId", m_iWarehouseAId), BigInt("@WarehouseBId", m_iWarehouseBId));
        await ExecuteAsync("DELETE dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID = @CategoryId;", BigInt("@CategoryId", m_iCategoryId));
        await ExecuteAsync("DELETE dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID = @UnitId;", BigInt("@UnitId", m_iUnitId));
    }

    [Fact]
    public async Task Controller_executes_receipt_and_issue_crud_with_returned_ids_and_header_cascade()
    {
        var v_objController = new CWarehouseDocument_Controller();
        var v_objReceipt = Receipt("R1", m_iWarehouseAId, new DateTime(2026, 1, 1));

        await v_objController.Save_Document_Async(v_objReceipt, "tdd-user", "tdd-function");
        Assert.True(v_objReceipt.Auto_ID > 0);
        Assert.Equal(1L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Id;", BigInt("@Id", v_objReceipt.Auto_ID)));

        v_objReceipt.Kho_ID = m_iWarehouseBId;
        v_objReceipt.Ngay_Chung_Tu = new DateTime(2026, 1, 2);
        v_objReceipt.Ghi_Chu = "updated receipt header";
        await v_objController.Save_Document_Async(v_objReceipt, "tdd-user", "tdd-function");
        Assert.Equal(1L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Id AND Kho_ID = @WarehouseId AND Ngay_Nhap_Kho = @Date;", BigInt("@Id", v_objReceipt.Auto_ID), BigInt("@WarehouseId", m_iWarehouseBId), Date("@Date", v_objReceipt.Ngay_Chung_Tu)));

        var v_objReceiptDetail = Detail(v_objReceipt.Auto_ID, m_iProductId, 30m, 100m);
        await v_objController.Save_Document_Detail_Async(true, v_objReceiptDetail, "tdd-user", "tdd-function");
        Assert.True(v_objReceiptDetail.Auto_ID > 0);

        v_objReceiptDetail.So_Luong = 35m;
        v_objReceiptDetail.Don_Gia = 120m;
        await v_objController.Save_Document_Detail_Async(true, v_objReceiptDetail, "tdd-user", "tdd-function");
        Assert.Equal(1L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @Id AND SL_Nhap = 35 AND Don_Gia_Nhap = 120;", BigInt("@Id", v_objReceiptDetail.Auto_ID)));

        var v_objProductChange = Detail(v_objReceipt.Auto_ID, m_iSecondProductId, 35m, 120m);
        v_objProductChange.Auto_ID = v_objReceiptDetail.Auto_ID;
        var v_objProductChangeError = await Assert.ThrowsAsync<SqlException>(async () => await v_objController.Save_Document_Detail_Async(true, v_objProductChange));
        Assert.Equal(51110, v_objProductChangeError.Number);
        Assert.Equal(m_iProductId, await ScalarLongAsync("SELECT San_Pham_ID FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @Id;", BigInt("@Id", v_objReceiptDetail.Auto_ID)));

        var v_objSecondReceiptDetail = Detail(v_objReceipt.Auto_ID, m_iSecondProductId, 15m, 80m);
        await v_objController.Save_Document_Detail_Async(true, v_objSecondReceiptDetail);
        await v_objController.Delete_Document_Detail_Async(true, v_objSecondReceiptDetail.Auto_ID, "tdd-user", "tdd-function");
        Assert.Equal(0L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @Id;", BigInt("@Id", v_objSecondReceiptDetail.Auto_ID)));
        v_objSecondReceiptDetail.Auto_ID = 0;
        await v_objController.Save_Document_Detail_Async(true, v_objSecondReceiptDetail);

        var v_objIssueStockReceipt = Receipt("R-ISSUE", m_iWarehouseAId, new DateTime(2026, 1, 1));
        await v_objController.Save_Document_Async(v_objIssueStockReceipt);
        await v_objController.Save_Document_Detail_Async(true, Detail(v_objIssueStockReceipt.Auto_ID, m_iProductId, 20m, 100m));

        var v_objIssue = Issue("I1", m_iWarehouseAId, new DateTime(2026, 1, 3));
        await v_objController.Save_Document_Async(v_objIssue);
        Assert.True(v_objIssue.Auto_ID > 0);

        var v_objIssueDetail = Detail(v_objIssue.Auto_ID, m_iProductId, 5m, 150m);
        await v_objController.Save_Document_Detail_Async(false, v_objIssueDetail);
        Assert.True(v_objIssueDetail.Auto_ID > 0);

        v_objIssue.Kho_ID = m_iWarehouseBId;
        v_objIssue.Ngay_Chung_Tu = new DateTime(2026, 1, 4);
        await v_objController.Save_Document_Async(v_objIssue);
        Assert.Equal(1L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Id AND Kho_ID = @WarehouseId AND Ngay_Xuat_Kho = @Date;", BigInt("@Id", v_objIssue.Auto_ID), BigInt("@WarehouseId", m_iWarehouseBId), Date("@Date", v_objIssue.Ngay_Chung_Tu)));

        v_objIssueDetail.So_Luong = 6m;
        v_objIssueDetail.Don_Gia = 160m;
        await v_objController.Save_Document_Detail_Async(false, v_objIssueDetail);
        Assert.Equal(1L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @Id AND SL_Xuat = 6 AND Don_Gia_Xuat = 160;", BigInt("@Id", v_objIssueDetail.Auto_ID)));
        await v_objController.Delete_Document_Detail_Async(false, v_objIssueDetail.Auto_ID, "tdd-user", "tdd-function");
        Assert.Equal(0L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @Id;", BigInt("@Id", v_objIssueDetail.Auto_ID)));
        v_objIssueDetail.Auto_ID = 0;
        await v_objController.Save_Document_Detail_Async(false, v_objIssueDetail);

        await v_objController.Delete_Document_Async(false, v_objIssue.Auto_ID, "tdd-user", "tdd-function");
        Assert.Equal(0L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Xuat_Kho_ID = @Id;", BigInt("@Id", v_objIssue.Auto_ID)));

        await v_objController.Delete_Document_Async(true, v_objReceipt.Auto_ID, "tdd-user", "tdd-function");
        Assert.Equal(0L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @Id;", BigInt("@Id", v_objReceipt.Auto_ID)));
    }

    [Fact]
    public async Task Controller_rejects_invalid_detail_and_rolls_back_negative_issue_detail()
    {
        var v_objController = new CWarehouseDocument_Controller();
        var v_objReceipt = Receipt("R2", m_iWarehouseAId, new DateTime(2026, 2, 1));
        await v_objController.Save_Document_Async(v_objReceipt);

        var v_objZeroQuantity = Detail(v_objReceipt.Auto_ID, m_iProductId, 0m, 100m);
        var v_objZeroQuantityError = await Assert.ThrowsAsync<SqlException>(async () => await v_objController.Save_Document_Detail_Async(true, v_objZeroQuantity));
        Assert.Equal(51107, v_objZeroQuantityError.Number);
        Assert.Equal(0L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @Id;", BigInt("@Id", v_objReceipt.Auto_ID)));

        var v_objMissingHeader = Detail(long.MaxValue, m_iProductId, 1m, 100m);
        var v_objMissingHeaderError = await Assert.ThrowsAsync<SqlException>(async () => await v_objController.Save_Document_Detail_Async(true, v_objMissingHeader));
        Assert.Equal(51105, v_objMissingHeaderError.Number);

        var v_objIssue = Issue("I2", m_iWarehouseBId, new DateTime(2026, 2, 2));
        await v_objController.Save_Document_Async(v_objIssue);
        var v_objNegativeIssue = Detail(v_objIssue.Auto_ID, m_iProductId, 1m, 150m);
        await v_objController.Save_Document_Detail_Async(false, v_objNegativeIssue);
        var v_objNegativeIssueError = await Assert.ThrowsAsync<SqlException>(async () => await v_objController.Post_Document_Async(false, v_objIssue.Auto_ID));
        Assert.Equal(51120, v_objNegativeIssueError.Number);
        Assert.Equal(1L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Xuat_Kho_ID = @Id;", BigInt("@Id", v_objIssue.Auto_ID)));
        Assert.Equal(0L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Id AND Is_Posted = 1;", BigInt("@Id", v_objIssue.Auto_ID)));
    }

    [Fact]
    public async Task Delete_draft_does_not_commit_a_caller_owned_transaction()
    {
        var v_objController = new CWarehouseDocument_Controller();
        var v_objReceipt = Receipt("R3", m_iWarehouseAId, new DateTime(2026, 3, 1));
        await v_objController.Save_Document_Async(v_objReceipt);
        await v_objController.Save_Document_Detail_Async(true, Detail(v_objReceipt.Auto_ID, m_iProductId, 5m, 100m));

        var v_objIssue = Issue("I3", m_iWarehouseAId, new DateTime(2026, 3, 2));
        await v_objController.Save_Document_Async(v_objIssue);
        await v_objController.Save_Document_Detail_Async(false, Detail(v_objIssue.Auto_ID, m_iProductId, 5m, 150m));

        using var v_objConnection = new SqlConnection(ConnectionString);
        await v_objConnection.OpenAsync();
        using var v_objTransaction = v_objConnection.BeginTransaction();
        using var v_objDelete = new SqlCommand("dbo.sp_XNK_Nhap_Kho_Delete_Header", v_objConnection, v_objTransaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        v_objDelete.Parameters.Add(BigInt("@Auto_ID", v_objReceipt.Auto_ID));

        await v_objDelete.ExecuteNonQueryAsync();

        using var v_objTransactionCount = new SqlCommand("SELECT @@TRANCOUNT;", v_objConnection, v_objTransaction);
        Assert.Equal(1L, Convert.ToInt64(await v_objTransactionCount.ExecuteScalarAsync()));
        v_objTransaction.Rollback();
        Assert.Equal(1L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Id;", BigInt("@Id", v_objReceipt.Auto_ID)));
    }

    [Fact]
    public async Task Post_makes_a_draft_document_a_movement_and_rolls_back_an_insufficient_issue()
    {
        var v_objController = new CWarehouseDocument_Controller();
        var v_objReceipt = Receipt("POST-R", m_iWarehouseAId, new DateTime(2026, 4, 1));
        await v_objController.Save_Document_Async(v_objReceipt);
        await v_objController.Save_Document_Detail_Async(true, Detail(v_objReceipt.Auto_ID, m_iProductId, 10m, 100m));

        Assert.Equal(0L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", m_iWarehouseAId), BigInt("@ProductId", m_iProductId)));

        await v_objController.Post_Document_Async(true, v_objReceipt.Auto_ID, "tdd-user", "tdd-function");

        Assert.Equal(1L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Id AND Is_Posted = 1;", BigInt("@Id", v_objReceipt.Auto_ID)));
        Assert.Equal(10L, await ScalarLongAsync("SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", m_iWarehouseAId), BigInt("@ProductId", m_iProductId)));

        var v_objIssue = Issue("POST-I", m_iWarehouseAId, new DateTime(2026, 4, 2));
        await v_objController.Save_Document_Async(v_objIssue);
        await v_objController.Save_Document_Detail_Async(false, Detail(v_objIssue.Auto_ID, m_iProductId, 11m, 150m));

        var v_objPostError = await Assert.ThrowsAsync<SqlException>(async () => await v_objController.Post_Document_Async(false, v_objIssue.Auto_ID, "tdd-user", "tdd-function"));
        Assert.Equal(51120, v_objPostError.Number);
        Assert.Equal(0L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Id AND Is_Posted = 1;", BigInt("@Id", v_objIssue.Auto_ID)));
        Assert.Equal(10L, await ScalarLongAsync("SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", m_iWarehouseAId), BigInt("@ProductId", m_iProductId)));
    }

    private CWarehouseDocument Receipt(string p_strSuffix, long p_iWarehouseId, DateTime p_dtmDate) => new()
    {
        Is_Receipt = true,
        So_Phieu = $"{m_strTag}-{p_strSuffix}",
        Kho_ID = p_iWarehouseId,
        NCC_ID = m_iSupplierId,
        Ngay_Chung_Tu = p_dtmDate,
        Ghi_Chu = "warehouse CRUD contract test"
    };

    private CWarehouseDocument Issue(string p_strSuffix, long p_iWarehouseId, DateTime p_dtmDate) => new()
    {
        Is_Receipt = false,
        So_Phieu = $"{m_strTag}-{p_strSuffix}",
        Kho_ID = p_iWarehouseId,
        Ngay_Chung_Tu = p_dtmDate,
        Ghi_Chu = "warehouse CRUD contract test"
    };

    private static CWarehouseDocumentDetail Detail(long p_iDocumentId, long p_iProductId, decimal p_decQuantity, decimal p_decPrice) => new()
    {
        Document_ID = p_iDocumentId,
        San_Pham_ID = p_iProductId,
        So_Luong = p_decQuantity,
        Don_Gia = p_decPrice
    };

    private static SqlParameter BigInt(string p_strName, long p_iValue) => new(p_strName, SqlDbType.BigInt) { Value = p_iValue };
    private static SqlParameter Date(string p_strName, DateTime p_dtmValue) => new(p_strName, SqlDbType.Date) { Value = p_dtmValue.Date };
    private static SqlParameter NVarChar(string p_strName, string p_strValue, int p_iSize) => new(p_strName, SqlDbType.NVarChar, p_iSize) { Value = p_strValue };

    private static async Task<long> InsertIdAsync(string p_strSql, params SqlParameter[] p_arrParameters)
    {
        using var v_objConnection = new SqlConnection(ConnectionString);
        using var v_objCommand = new SqlCommand(p_strSql, v_objConnection);
        v_objCommand.Parameters.AddRange(p_arrParameters);
        await v_objConnection.OpenAsync();
        return Convert.ToInt64(await v_objCommand.ExecuteScalarAsync());
    }

    private static async Task<long> ScalarLongAsync(string p_strSql, params SqlParameter[] p_arrParameters)
    {
        using var v_objConnection = new SqlConnection(ConnectionString);
        using var v_objCommand = new SqlCommand(p_strSql, v_objConnection);
        v_objCommand.Parameters.AddRange(p_arrParameters);
        await v_objConnection.OpenAsync();
        return Convert.ToInt64(await v_objCommand.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(string p_strSql, params SqlParameter[] p_arrParameters)
    {
        using var v_objConnection = new SqlConnection(ConnectionString);
        using var v_objCommand = new SqlCommand(p_strSql, v_objConnection);
        v_objCommand.Parameters.AddRange(p_arrParameters);
        await v_objConnection.OpenAsync();
        await v_objCommand.ExecuteNonQueryAsync();
    }
}
