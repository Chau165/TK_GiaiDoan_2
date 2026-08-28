using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests.Performance;

public sealed class ToolBenchmarkRunnerTests
{
    [Fact]
    public void Tool_runner_uses_release_build_and_isolated_database_name()
    {
        var runner = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Benchmarks",
            "Run-WarehouseToolBenchmarks.ps1"));

        Assert.Contains("--configuration Release", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TKS_Thuc_Tap_V11_Perf_$RecordCount", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP DATABASE [$databaseName]", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--filter '*WarehouseSyntheticBenchmarks*'", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--filter '*WarehouseDatabaseBenchmarks*'", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-v', 'PageSize=10'", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TKS_NBOMBER_SCENARIOS", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WarehousePerformance.SnapshotBaseline.sql", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SnapshotDate", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--nbomber", runner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tool_runner_bootstraps_movement_aggregate_before_inventory_load()
    {
        var runner = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Benchmarks",
            "Run-WarehouseToolBenchmarks.ps1"));

        Assert.Contains("sp_Inventory_Movement_Bootstrap_From_Ledger", runner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tool_runner_writes_a_consolidated_markdown_report()
    {
        var runner = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Benchmarks",
            "Run-WarehouseToolBenchmarks.ps1"));

        Assert.Contains("performance-report.md", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Write-ConsolidatedReport", runner, StringComparison.OrdinalIgnoreCase);
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
