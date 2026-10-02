using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public class ChuHangPermissionTests
{
    [Fact]
    public async Task Thuctap_kho_has_active_chu_hang_access_with_crud_permissions()
    {
        var v_ConnectionString = WarehouseTestDatabase.ConnectionString;

        await using var v_Connection = new SqlConnection(v_ConnectionString);
        await v_Connection.OpenAsync();

        const string v_Sql = """
            SELECT
                CASE WHEN EXISTS (
                    SELECT 1
                    FROM dbo.view_Sys_Nhom_Thanh_Vien_User AS U
                    JOIN dbo.view_Sys_Nhom_Thanh_Vien AS G ON G.Auto_ID = U.Nhom_Thanh_Vien_ID
                    WHERE U.Ma_Dang_Nhap = N'thuctap_kho' AND G.Ten_Nhom_Thanh_Vien = N'Chủ hàng'
                ) THEN 1 ELSE 0 END,
                COALESCE(MAX(CONVERT(int, P.Is_Have_View_Permission)), 0),
                COALESCE(MAX(CONVERT(int, P.Is_Have_Add_Permission)), 0),
                COALESCE(MAX(CONVERT(int, P.Is_Have_Edit_Permission)), 0),
                COALESCE(MAX(CONVERT(int, P.Is_Have_Delete_Permission)), 0),
                COALESCE(MAX(CONVERT(int, P.Is_Have_Export_Permission)), 0)
            FROM dbo.view_Sys_Phan_Quyen_Chuc_Nang AS P
            JOIN dbo.view_Sys_Nhom_Thanh_Vien AS G ON G.Auto_ID = P.Nhom_Thanh_Vien_ID
            JOIN dbo.view_Sys_Chuc_Nang AS C ON C.Auto_ID = P.Chuc_Nang_ID
            WHERE G.Ten_Nhom_Thanh_Vien = N'Chủ hàng' AND C.Ma_Chuc_Nang = N'2003';
            """;

        await using var v_Command = new SqlCommand(v_Sql, v_Connection);
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        Assert.Equal(1, v_Reader.GetInt32(0));
        Assert.Equal(1, v_Reader.GetInt32(1));
        Assert.Equal(1, v_Reader.GetInt32(2));
        Assert.Equal(1, v_Reader.GetInt32(3));
        Assert.Equal(1, v_Reader.GetInt32(4));
        Assert.Equal(1, v_Reader.GetInt32(5));
    }
}
