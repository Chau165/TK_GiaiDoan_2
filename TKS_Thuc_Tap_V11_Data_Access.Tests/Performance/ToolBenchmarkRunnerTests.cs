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

    [Fact]
    public void Tool_runner_can_reuse_the_verified_database_and_skip_memory_heavy_synthetic_load()
    {
        var runner = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Benchmarks",
            "Run-WarehouseToolBenchmarks.ps1"));

        Assert.Contains("[switch]$ReuseDatabase", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[switch]$SkipSynthetic", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SkipSynthetic", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ReuseDatabase", runner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tool_runner_preserves_report_generation_when_nbomber_records_failures()
    {
        var runner = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Benchmarks",
            "Run-WarehouseToolBenchmarks.ps1"));

        Assert.Contains("$nbomberExitCode = $LASTEXITCODE", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NBomber exit code", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Write-Warning", runner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("throw \"NBomber failed with exit code $LASTEXITCODE\"", runner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tool_runner_expands_values_inside_the_consolidated_report()
    {
        var runner = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Benchmarks",
            "Run-WarehouseToolBenchmarks.ps1"));

        Assert.Contains("- Database: $databaseName", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$($datasetVerification.Trim())", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$($bootstrapOutput.Trim())", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("~~~", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("## Measured summary", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Import-Csv", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ok_95_percent", runner, StringComparison.OrdinalIgnoreCase);
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
