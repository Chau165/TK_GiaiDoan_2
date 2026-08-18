using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public class MemberGroupSynchronizationTests
{
    [Fact]
    public async Task Deleting_a_member_group_mapping_refreshes_the_member_group_summary()
    {
        const string connectionString = "Server=localhost;Database=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True;TrustServerCertificate=True;";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("SELECT OBJECT_DEFINITION(OBJECT_ID(N'dbo.FQ_526_NTVU_sp_del_Delete_By_ID'));", connection);
        var definition = (string?)await command.ExecuteScalarAsync();

        Assert.NotNull(definition);
        Assert.Contains("FTotal_sp_upd_Thanh_Vien @Ma_Dang_Nhap", definition, StringComparison.OrdinalIgnoreCase);
    }
}
