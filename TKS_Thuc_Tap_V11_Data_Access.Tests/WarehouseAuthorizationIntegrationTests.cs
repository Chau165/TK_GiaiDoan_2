using System.Data;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseAuthorizationIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString = "Server=localhost;Database=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True;TrustServerCertificate=True;";

    private readonly string m_strTag = $"TDD-AUTH-{Guid.NewGuid():N}"[..21];
    private string m_strLogin = "";
    private long m_iUserId;
    private long m_iWarehouseAId;
    private long m_iWarehouseBId;

    public async Task InitializeAsync()
    {
        CConfig.TKS_Thuc_Tap_V11_Conn_String = ConnectionString;
        m_strLogin = $"{m_strTag}-login";
        m_iUserId = await InsertIdAsync(
            "DECLARE @UserId BIGINT = CONVERT(BIGINT, ABS(CHECKSUM(NEWID()))); INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, Trang_Thai_ID, deleted) OUTPUT INSERTED.Auto_ID VALUES (@UserId, @Login, @Name, 1, 0);",
            NVarChar("@Login", m_strLogin, 100), NVarChar("@Name", $"{m_strTag}-User", 200));
        m_iWarehouseAId = await InsertIdAsync(
            "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
            NVarChar("@Name", $"{m_strTag}-Warehouse-A", 255));
        m_iWarehouseBId = await InsertIdAsync(
            "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
            NVarChar("@Name", $"{m_strTag}-Warehouse-B", 255));
        await ExecuteAsync(
            "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
            NVarChar("@Login", m_strLogin, 100), BigInt("@WarehouseId", m_iWarehouseAId));
    }

    public async Task DisposeAsync()
    {
        await ExecuteAsync("DELETE dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho LIKE @Tag;", NVarChar("@Tag", $"{m_strTag}%", 100));
        await ExecuteAsync("DELETE dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho LIKE @Tag;", NVarChar("@Tag", $"{m_strTag}%", 100));
        await ExecuteAsync("DELETE dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap = @Login;", NVarChar("@Login", m_strLogin, 100));
        await ExecuteAsync("DELETE dbo.tbl_DM_Kho WHERE Auto_ID IN (@WarehouseAId, @WarehouseBId);", BigInt("@WarehouseAId", m_iWarehouseAId), BigInt("@WarehouseBId", m_iWarehouseBId));
        await ExecuteAsync("DELETE dbo.tbl_Sys_Thanh_Vien WHERE Auto_ID = @UserId;", BigInt("@UserId", m_iUserId));
    }

    [Fact]
    public async Task User_sees_only_assigned_warehouse_and_cannot_save_document_in_other_warehouse()
    {
        var v_objMasterController = new CWarehouseMaster_Controller();
        var v_arrAllowedWarehouses = await v_objMasterController.List_Authorized_Warehouses_Async(m_strLogin);

        var v_objAllowedWarehouse = Assert.Single(v_arrAllowedWarehouses);
        Assert.Equal(m_iWarehouseAId, v_objAllowedWarehouse.Auto_ID);
        Assert.DoesNotContain(v_arrAllowedWarehouses, p_objItem => p_objItem.Auto_ID == m_iWarehouseBId);

        var v_objDocumentController = new CWarehouseDocument_Controller();
        var v_objUnauthorizedReceipt = new CWarehouseDocument
        {
            Is_Receipt = true,
            So_Phieu = $"{m_strTag}-UNAUTHORIZED",
            Kho_ID = m_iWarehouseBId,
            Ngay_Chung_Tu = new DateTime(2026, 8, 25)
        };

        var v_objError = await Assert.ThrowsAsync<SqlException>(async () =>
            await v_objDocumentController.Save_Document_Async(v_objUnauthorizedReceipt, "", "", m_strLogin));

        Assert.Equal(51054, v_objError.Number);
        Assert.Equal(0L, await ScalarLongAsync(
            "SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho = @DocumentNumber;",
            NVarChar("@DocumentNumber", v_objUnauthorizedReceipt.So_Phieu, 100)));
    }

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
