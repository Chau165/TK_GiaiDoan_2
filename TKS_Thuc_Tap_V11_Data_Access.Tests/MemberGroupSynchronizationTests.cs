using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public class MemberGroupSynchronizationTests
{
    [Fact]
    public async Task Deleting_a_member_group_mapping_refreshes_the_member_group_summary()
    {
        var v_ConnectionString = WarehouseTestDatabase.ConnectionString;

        await using var v_Connection = new SqlConnection(v_ConnectionString);
        await v_Connection.OpenAsync();

        await using var v_Command = new SqlCommand("SELECT OBJECT_DEFINITION(OBJECT_ID(N'dbo.FQ_526_NTVU_sp_del_Delete_By_ID'));", v_Connection);
        var v_Definition = (string?)await v_Command.ExecuteScalarAsync();

        Assert.NotNull(v_Definition);
        Assert.Contains("FTotal_sp_upd_Thanh_Vien @Ma_Dang_Nhap", v_Definition, StringComparison.OrdinalIgnoreCase);
    }
}
