namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

internal static class WarehouseTestDatabase
{
    public static string ConnectionString
    {
        get
        {
            return ValidateConnectionString(Environment.GetEnvironmentVariable("TKS_INTEGRATION_CONNECTION_STRING"));
        }
    }

    internal static string ValidateConnectionString(string? p_strConnectionString)
    {
        return ProductImportTestDatabase.ValidateConnectionString(p_strConnectionString ?? string.Empty);
    }
}
