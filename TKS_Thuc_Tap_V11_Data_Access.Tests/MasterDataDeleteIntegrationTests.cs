using System.Data;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class MasterDataDeleteIntegrationTests
{
    private const string ConnectionString = "Server=localhost;Database=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True;TrustServerCertificate=True;";

    [Fact]
    public async Task Master_data_delete_accepts_the_four_values_passed_by_the_controller()
    {
        CConfig.TKS_Thuc_Tap_V11_Conn_String = ConnectionString;
        var v_strCode = $"TDD-DELETE-{Guid.NewGuid():N}"[..30];
        var v_iCategoryId = await InsertIdAsync(
            "INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP, Ten_LSP, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
            NVarChar("@Code", v_strCode, 100), NVarChar("@Name", $"{v_strCode}-Category", 200));

        try
        {
            await new CWarehouseMaster_Controller().Delete_Master_Async(
                "LoaiSanPham", v_iCategoryId, "tdd-delete-user", "tdd-delete-function");

            Assert.Equal(0L, await ScalarLongAsync(
                "SELECT COUNT_BIG(*) FROM dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID = @Id;",
                BigInt("@Id", v_iCategoryId)));
        }
        finally
        {
            await ExecuteAsync(
                "DELETE dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID = @Id;",
                BigInt("@Id", v_iCategoryId));
        }
    }

    private static SqlParameter BigInt(string p_strName, long p_iValue) => new(p_strName, SqlDbType.BigInt) { Value = p_iValue };
    private static SqlParameter NVarChar(string p_strName, string p_strValue, int p_iSize) => new(p_strName, SqlDbType.NVarChar, p_iSize) { Value = p_strValue };

    private static async Task<long> InsertIdAsync(string p_strSql, params SqlParameter[] p_arrParameters)
    {
        await using var v_objConnection = new SqlConnection(ConnectionString);
        await using var v_objCommand = new SqlCommand(p_strSql, v_objConnection);
        v_objCommand.Parameters.AddRange(p_arrParameters);
        await v_objConnection.OpenAsync();
        return Convert.ToInt64(await v_objCommand.ExecuteScalarAsync());
    }

    private static async Task<long> ScalarLongAsync(string p_strSql, params SqlParameter[] p_arrParameters)
    {
        await using var v_objConnection = new SqlConnection(ConnectionString);
        await using var v_objCommand = new SqlCommand(p_strSql, v_objConnection);
        v_objCommand.Parameters.AddRange(p_arrParameters);
        await v_objConnection.OpenAsync();
        return Convert.ToInt64(await v_objCommand.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(string p_strSql, params SqlParameter[] p_arrParameters)
    {
        await using var v_objConnection = new SqlConnection(ConnectionString);
        await using var v_objCommand = new SqlCommand(p_strSql, v_objConnection);
        v_objCommand.Parameters.AddRange(p_arrParameters);
        await v_objConnection.OpenAsync();
        await v_objCommand.ExecuteNonQueryAsync();
    }
}
