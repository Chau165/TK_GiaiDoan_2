using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class ProductImportDatabaseSafetyTests
{
    private const string m_strValidConnectionString =
        @"Data Source=DESKTOP-NHQ7QPL\MSSQLSERVER19;Initial Catalog=TKS_Thuc_Tap_V11_C01C02_Test_20260906;Integrated Security=True;TrustServerCertificate=True";

    [Fact]
    public void Connection_guard_accepts_only_the_owner_approved_test_database()
    {
        var v_strConnectionString = ProductImportTestDatabase.ValidateConnectionString(m_strValidConnectionString);

        Assert.Contains("TKS_Thuc_Tap_V11_C01C02_Test_20260906", v_strConnectionString, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"Data Source=DESKTOP-NHQ7QPL\MSSQLSERVER19;Initial Catalog=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True")]
    [InlineData(@"Data Source=localhost;Initial Catalog=TKS_Thuc_Tap_V11_C01C02_Test_20260906;Integrated Security=True")]
    [InlineData(@"Data Source=DESKTOP-NHQ7QPL\MSSQLSERVER19;Initial Catalog=master;Integrated Security=True")]
    public void Connection_guard_rejects_business_or_unapproved_targets(string p_strConnectionString)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ProductImportTestDatabase.ValidateConnectionString(p_strConnectionString));
    }
}
