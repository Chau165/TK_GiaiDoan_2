using Microsoft.Data.SqlClient;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

internal static class ProductImportTestDatabase
{
    private const string m_strExpectedServer = @"DESKTOP-NHQ7QPL\MSSQLSERVER19";
    private const string m_strExpectedDatabase = "TKS_Thuc_Tap_V11_C01C02_Test_20260906";

    internal static string ValidateConnectionString(string p_strConnectionString)
    {
        if (string.IsNullOrWhiteSpace(p_strConnectionString))
            throw new InvalidOperationException("Integration tests require TKS_INTEGRATION_CONNECTION_STRING.");

        var v_objBuilder = new SqlConnectionStringBuilder(p_strConnectionString);
        if (!string.Equals(v_objBuilder.DataSource, m_strExpectedServer, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(v_objBuilder.InitialCatalog, m_strExpectedDatabase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Product import integration tests require the owner-approved test server and database.");
        }

        return v_objBuilder.ConnectionString;
    }

    internal static async Task<SqlConnection> OpenVerifiedConnectionAsync(CancellationToken p_objCancellationToken = default)
    {
        var v_strConnectionString = Environment.GetEnvironmentVariable("TKS_INTEGRATION_CONNECTION_STRING");
        var v_objConnection = new SqlConnection(ValidateConnectionString(v_strConnectionString ?? ""));

        try
        {
            await v_objConnection.OpenAsync(p_objCancellationToken);
            await using var v_objCommand = new SqlCommand("SELECT @@SERVERNAME, DB_NAME();", v_objConnection);
            await using var v_objReader = await v_objCommand.ExecuteReaderAsync(p_objCancellationToken);
            if (!await v_objReader.ReadAsync(p_objCancellationToken) ||
                !string.Equals(v_objReader.GetString(0), m_strExpectedServer, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(v_objReader.GetString(1), m_strExpectedDatabase, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The live SQL connection identity does not match the owner-approved test server and database.");
            }

            return v_objConnection;
        }
        catch
        {
            await v_objConnection.DisposeAsync();
            throw;
        }
    }
}
