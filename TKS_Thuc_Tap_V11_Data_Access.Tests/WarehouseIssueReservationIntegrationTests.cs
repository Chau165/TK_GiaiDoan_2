using System.Data;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseIssueReservationIntegrationTests : IAsyncLifetime
{
    private static string ConnectionString => WarehouseTestDatabase.ConnectionString;

    private readonly string m_strTag = $"TDD-RES-{Guid.NewGuid():N}"[..20];
    private long m_iProductId;
    private long m_iCategoryId;
    private long m_iUnitId;
    private long m_iSupplierId;
    private long m_iWarehouseId;
    private string m_strLogin = "";
    private long m_iUserId;

    public async Task InitializeAsync()
    {
        CConfig.TKS_Thuc_Tap_V11_Conn_String = ConnectionString;
        m_strLogin = $"{m_strTag}-login";
        m_iUserId = await InsertIdAsync(
            "DECLARE @UserId BIGINT = CONVERT(BIGINT, ABS(CHECKSUM(NEWID()))); INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, Trang_Thai_ID, deleted) OUTPUT INSERTED.Auto_ID VALUES (@UserId, @Login, @Name, 1, 0);",
            NVarChar("@Login", m_strLogin, 100), NVarChar("@Name", $"{m_strTag}-User", 200));
        m_iUnitId = await InsertIdAsync(
            "INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
            NVarChar("@Name", $"{m_strTag}-Unit", 200));
        m_iCategoryId = await InsertIdAsync(
            "INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP, Ten_LSP, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
            NVarChar("@Code", $"{m_strTag}-CAT", 100), NVarChar("@Name", $"{m_strTag}-Category", 200));
        m_iProductId = await InsertIdAsync(
            "INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, @CategoryId, @UnitId, N'');",
            NVarChar("@Code", $"{m_strTag}-P1", 100), NVarChar("@Name", $"{m_strTag}-Product-1", 255), BigInt("@CategoryId", m_iCategoryId), BigInt("@UnitId", m_iUnitId));
        m_iSupplierId = await InsertIdAsync(
            "INSERT dbo.tbl_DM_NCC(Ma_NCC, Ten_NCC, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
            NVarChar("@Code", $"{m_strTag}-NCC", 100), NVarChar("@Name", $"{m_strTag}-Supplier", 200));
        m_iWarehouseId = await InsertIdAsync(
            "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
            NVarChar("@Name", $"{m_strTag}-Warehouse", 255));
        await ExecuteAsync(
            "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
            NVarChar("@Login", m_strLogin, 100), BigInt("@WarehouseId", m_iWarehouseId));
    }

    public async Task DisposeAsync()
    {
        await ExecuteAsync("EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1; DELETE dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho LIKE @Tag;", NVarChar("@Tag", $"{m_strTag}%", 100));
        await ExecuteAsync("EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1; DELETE dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho LIKE @Tag;", NVarChar("@Tag", $"{m_strTag}%", 100));
        await ExecuteAsync("DELETE dbo.InventoryBalance_Current WHERE San_Pham_ID = @ProductId;", BigInt("@ProductId", m_iProductId));
        await ExecuteAsync("SET QUOTED_IDENTIFIER ON; DELETE d FROM dbo.InventoryMovement_RebuildDeadLetter d JOIN dbo.InventoryMovement_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.San_Pham_ID = @ProductId; DELETE d FROM dbo.InventorySnapshot_RebuildDeadLetter d JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE San_Pham_ID = @ProductId; DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE San_Pham_ID = @ProductId;", BigInt("@ProductId", m_iProductId));
        await ExecuteAsync("DELETE dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap = @Login;", NVarChar("@Login", m_strLogin, 100));
        await ExecuteAsync("DELETE dbo.tbl_DM_San_Pham WHERE Auto_ID = @ProductId;", BigInt("@ProductId", m_iProductId));
        await ExecuteAsync("DELETE dbo.tbl_DM_NCC WHERE Auto_ID = @SupplierId;", BigInt("@SupplierId", m_iSupplierId));
        await ExecuteAsync("DELETE dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;", BigInt("@WarehouseId", m_iWarehouseId));
        await ExecuteAsync("DELETE dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID = @CategoryId;", BigInt("@CategoryId", m_iCategoryId));
        await ExecuteAsync("DELETE dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID = @UnitId;", BigInt("@UnitId", m_iUnitId));
        await ExecuteAsync("DELETE dbo.tbl_Sys_Thanh_Vien WHERE Auto_ID = @UserId;", BigInt("@UserId", m_iUserId));
    }

    [Fact]
    public async Task Draft_issue_reserves_available_stock_and_rejects_a_second_overbooking()
    {
        var v_objController = await CreateStockAsync(50m);
        var v_objFirstIssue = await CreateIssueAsync("I1", 30m, v_objController);

        Assert.Equal(50m, await ScalarDecimalAsync("SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;"));
        Assert.Equal(30m, await ScalarDecimalAsync("SELECT ReservedQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;"));
        Assert.Equal(1L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @DetailId;", BigInt("@DetailId", v_objFirstIssue.Auto_ID)));

        var v_objSecondIssue = Issue("I2");
        await v_objController.Save_Document_Async(v_objSecondIssue, "", "", m_strLogin);
        var v_objError = await Assert.ThrowsAsync<SqlException>(async () => await v_objController.Save_Document_Detail_Async(false, Detail(v_objSecondIssue.Auto_ID, 30m), "", "", m_strLogin));

        Assert.Equal(51140, v_objError.Number);
        Assert.Equal(0L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.InventoryReservation_Current r JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Auto_ID = r.Xuat_Kho_Detail_ID WHERE d.Xuat_Kho_ID = @DocumentId;", BigInt("@DocumentId", v_objSecondIssue.Auto_ID)));
        Assert.Equal(30m, await ScalarDecimalAsync("SELECT ReservedQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;"));
    }

    [Fact]
    public async Task Updating_and_deleting_a_draft_issue_releases_reservation_and_post_consumes_it()
    {
        var v_objController = await CreateStockAsync(50m);
        var v_objIssue = await CreateIssueAsync("I3", 30m, v_objController);
        var v_objDetail = Detail(v_objIssue.Document_ID, 30m);

        v_objDetail.Auto_ID = v_objIssue.Auto_ID;
        v_objDetail.So_Luong = 20m;
        await v_objController.Save_Document_Detail_Async(false, v_objDetail, "", "", m_strLogin);
        Assert.Equal(20m, await ScalarDecimalAsync("SELECT ReservedQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;"));

        await v_objController.Delete_Document_Detail_Async(false, v_objDetail.Auto_ID, "", "", m_strLogin);
        Assert.Equal(0m, await ScalarDecimalAsync("SELECT ReservedQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;"));

        var v_objPostIssue = await CreateIssueAsync("I4", 20m, v_objController);
        await v_objController.Post_Document_Async(false, v_objPostIssue.Document_ID, "", "", m_strLogin);

        Assert.Equal(30m, await ScalarDecimalAsync("SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;"));
        Assert.Equal(0m, await ScalarDecimalAsync("SELECT ReservedQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;"));
    }

    [Fact]
    public async Task Deleting_a_draft_issue_header_releases_all_detail_reservations()
    {
        var v_objController = await CreateStockAsync(50m);
        var v_objIssue = await CreateIssueAsync("I5", 30m, v_objController);

        await v_objController.Delete_Document_Async(false, v_objIssue.Document_ID, "", "", m_strLogin);

        Assert.Equal(0m, await ScalarDecimalAsync("SELECT ReservedQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;"));
        Assert.Equal(0L, await ScalarLongAsync("SELECT COUNT_BIG(*) FROM dbo.InventoryReservation_Current r JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Auto_ID = r.Xuat_Kho_Detail_ID WHERE d.Xuat_Kho_ID = @DocumentId;", BigInt("@DocumentId", v_objIssue.Document_ID)));
    }

    private async Task<CWarehouseDocument_Controller> CreateStockAsync(decimal p_decQuantity)
    {
        var v_objController = new CWarehouseDocument_Controller();
        var v_objReceipt = Receipt("R");
        await v_objController.Save_Document_Async(v_objReceipt, "", "", m_strLogin);
        await v_objController.Save_Document_Detail_Async(true, Detail(v_objReceipt.Auto_ID, p_decQuantity), "", "", m_strLogin);
        await v_objController.Post_Document_Async(true, v_objReceipt.Auto_ID, "", "", m_strLogin);
        return v_objController;
    }

    private async Task<(long Document_ID, long Auto_ID)> CreateIssueAsync(string p_strSuffix, decimal p_decQuantity, CWarehouseDocument_Controller p_objController)
    {
        var v_objIssue = Issue(p_strSuffix);
        await p_objController.Save_Document_Async(v_objIssue, "", "", m_strLogin);
        var v_objDetail = Detail(v_objIssue.Auto_ID, p_decQuantity);
        await p_objController.Save_Document_Detail_Async(false, v_objDetail, "", "", m_strLogin);
        return (v_objIssue.Auto_ID, v_objDetail.Auto_ID);
    }

    private CWarehouseDocument Receipt(string p_strSuffix) => new()
    {
        Is_Receipt = true,
        So_Phieu = $"{m_strTag}-{p_strSuffix}",
        Kho_ID = m_iWarehouseId,
        NCC_ID = m_iSupplierId,
        Ngay_Chung_Tu = new DateTime(2026, 8, 25)
    };

    private CWarehouseDocument Issue(string p_strSuffix) => new()
    {
        Is_Receipt = false,
        So_Phieu = $"{m_strTag}-{p_strSuffix}",
        Kho_ID = m_iWarehouseId,
        Ngay_Chung_Tu = new DateTime(2026, 8, 25)
    };

    private CWarehouseDocumentDetail Detail(long p_iDocumentId, decimal p_decQuantity) => new()
    {
        Document_ID = p_iDocumentId,
        San_Pham_ID = m_iProductId,
        So_Luong = p_decQuantity,
        Don_Gia = 100m
    };

    private static SqlParameter BigInt(string p_strName, long p_iValue) => new(p_strName, SqlDbType.BigInt) { Value = p_iValue };
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
        v_objCommand.Parameters.Add(BigInt("@WarehouseId", 0));
        v_objCommand.Parameters.Add(BigInt("@ProductId", 0));
        v_objCommand.Parameters.AddRange(p_arrParameters);
        await v_objConnection.OpenAsync();
        return Convert.ToInt64(await v_objCommand.ExecuteScalarAsync());
    }

    private async Task<decimal> ScalarDecimalAsync(string p_strSql, params SqlParameter[] p_arrParameters)
    {
        using var v_objConnection = new SqlConnection(ConnectionString);
        using var v_objCommand = new SqlCommand(p_strSql, v_objConnection);
        v_objCommand.Parameters.Add(BigInt("@WarehouseId", m_iWarehouseId));
        v_objCommand.Parameters.Add(BigInt("@ProductId", m_iProductId));
        v_objCommand.Parameters.AddRange(p_arrParameters);
        await v_objConnection.OpenAsync();
        return Convert.ToDecimal(await v_objCommand.ExecuteScalarAsync());
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
