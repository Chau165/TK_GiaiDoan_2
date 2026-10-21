using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseTestDatabaseSafetyTests
{
    private const string m_strApprovedConnectionString =
        @"Data Source=DESKTOP-NHQ7QPL\MSSQLSERVER19;Initial Catalog=TKS_Thuc_Tap_V11_C01C02_Test_20260906;Integrated Security=True;TrustServerCertificate=True";

    [Fact]
    public void Warehouse_test_connection_requires_explicit_configuration()
    {
        Assert.Throws<InvalidOperationException>(() =>
            WarehouseTestDatabase.ValidateConnectionString(null));
    }

    [Theory]
    [InlineData(@"Data Source=DESKTOP-NHQ7QPL\MSSQLSERVER19;Initial Catalog=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True")]
    [InlineData(@"Data Source=localhost;Initial Catalog=TKS_Thuc_Tap_V11_C01C02_Test_20260906;Integrated Security=True")]
    [InlineData(@"Data Source=DESKTOP-NHQ7QPL\MSSQLSERVER19;Initial Catalog=master;Integrated Security=True")]
    public void Warehouse_test_connection_rejects_unapproved_targets(string p_strConnectionString)
    {
        Assert.Throws<InvalidOperationException>(() =>
            WarehouseTestDatabase.ValidateConnectionString(p_strConnectionString));
    }

    [Fact]
    public void Warehouse_test_connection_accepts_only_the_approved_target()
    {
        var v_strConnectionString = WarehouseTestDatabase.ValidateConnectionString(m_strApprovedConnectionString);

        Assert.Contains("TKS_Thuc_Tap_V11_C01C02_Test_20260906", v_strConnectionString, StringComparison.Ordinal);
    }
}
