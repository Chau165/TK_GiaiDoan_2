using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class PerformanceRunnerTests
{
    [Fact]
    public void Runner_supports_an_explicit_database_storage_directory()
    {
        var runner = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access.Tests",
            "Performance",
            "Run-WarehousePerformance.ps1"));

        Assert.Contains("[string]$DatabaseDirectory", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FILENAME", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE DATABASE [$databaseName] ON PRIMARY", runner, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryPath(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(parts)}");
    }
}
