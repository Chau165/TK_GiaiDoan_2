namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

internal static class WarehouseTestDatabase
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("TKS_INTEGRATION_CONNECTION_STRING")
        ?? "Server=localhost;Database=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True;TrustServerCertificate=True;";
}
