using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public class ChuHangPermissionTests
{
    [Fact]
    public async Task Thuctap_kho_has_active_chu_hang_access_with_crud_permissions()
    {
        const string connectionString = "Server=localhost;Database=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True;TrustServerCertificate=True;";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        const string sql = """
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

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2));
        Assert.Equal(1, reader.GetInt32(3));
        Assert.Equal(1, reader.GetInt32(4));
        Assert.Equal(1, reader.GetInt32(5));
    }
}
