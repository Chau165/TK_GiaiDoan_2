using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class PerformanceRunnerTests
{
    [Fact]
    public void Runner_supports_an_explicit_database_storage_directory()
    {
        var v_Runner = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access.Tests",
            "Performance",
            "Run-WarehousePerformance.ps1"));

        Assert.Contains("[string]$DatabaseDirectory", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FILENAME", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE DATABASE [$databaseName] ON PRIMARY", v_Runner, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryPath(params string[] p_arrParts)
    {
        for (var v_Directory = new DirectoryInfo(AppContext.BaseDirectory); v_Directory is not null; v_Directory = v_Directory.Parent)
        {
            var v_Candidate = Path.Combine(new[] { v_Directory.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_Candidate))
                return v_Candidate;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(p_arrParts)}");
    }
}
