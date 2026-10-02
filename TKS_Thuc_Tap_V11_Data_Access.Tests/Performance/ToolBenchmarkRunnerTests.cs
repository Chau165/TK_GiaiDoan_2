using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests.Performance;

public sealed class ToolBenchmarkRunnerTests
{
    [Fact]
    public void Tool_runner_uses_release_build_and_isolated_database_name()
    {
        var v_Runner = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Benchmarks",
            "Run-WarehouseToolBenchmarks.ps1"));

        Assert.Contains("--configuration Release", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TKS_Thuc_Tap_V11_Perf_$RecordCount", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP DATABASE [$databaseName]", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--filter '*WarehouseSyntheticBenchmarks*'", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--filter '*WarehouseDatabaseBenchmarks*'", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-v', 'PageSize=10'", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TKS_NBOMBER_SCENARIOS", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WarehousePerformance.SnapshotBaseline.sql", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SnapshotDate", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--nbomber", v_Runner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tool_runner_bootstraps_movement_aggregate_before_inventory_load()
    {
        var v_Runner = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Benchmarks",
            "Run-WarehouseToolBenchmarks.ps1"));

        Assert.Contains("sp_Inventory_Movement_Bootstrap_From_Ledger", v_Runner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tool_runner_writes_a_consolidated_markdown_report()
    {
        var v_Runner = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Benchmarks",
            "Run-WarehouseToolBenchmarks.ps1"));

        Assert.Contains("performance-report.md", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Write-ConsolidatedReport", v_Runner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tool_runner_can_reuse_the_verified_database_and_skip_memory_heavy_synthetic_load()
    {
        var v_Runner = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Benchmarks",
            "Run-WarehouseToolBenchmarks.ps1"));

        Assert.Contains("[switch]$ReuseDatabase", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[switch]$SkipSynthetic", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SkipSynthetic", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ReuseDatabase", v_Runner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tool_runner_preserves_report_generation_when_nbomber_records_failures()
    {
        var v_Runner = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Benchmarks",
            "Run-WarehouseToolBenchmarks.ps1"));

        Assert.Contains("$nbomberExitCode = $LASTEXITCODE", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NBomber exit code", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Write-Warning", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("throw \"NBomber failed with exit code $LASTEXITCODE\"", v_Runner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tool_runner_expands_values_inside_the_consolidated_report()
    {
        var v_Runner = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Benchmarks",
            "Run-WarehouseToolBenchmarks.ps1"));

        Assert.Contains("- Database: $databaseName", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$($datasetVerification.Trim())", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$($bootstrapOutput.Trim())", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("~~~", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("## Measured summary", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Import-Csv", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ok_95_percent", v_Runner, StringComparison.OrdinalIgnoreCase);
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
