using TKS_Thuc_Tap_V11_Benchmarks;
using System.Text.Json;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests.Performance;

public sealed class ToolBenchmarkHarnessTests
{
    [Fact]
    public void Settings_parse_database_and_load_options_from_environment()
    {
        var v_Settings = BenchmarkSettings.FromEnvironment(new Dictionary<string, string?>
        {
            ["TKS_PERF_ROWS"] = "1000000",
            ["TKS_PERF_PAGE_SIZE"] = "25",
            ["TKS_NBOMBER_COPIES"] = "12",
            ["TKS_NBOMBER_DURATION_SECONDS"] = "7",
            ["TKS_PERF_LOGIN"] = "PERF_USER",
            ["TKS_PERF_CONNECTION_STRING"] = "Server=(local);Database=Perf;",
            ["TKS_BENCH_REPORT_DIR"] = "C:\\temp\\tks-bench"
        });

        Assert.Equal(1_000_000, v_Settings.RecordCount);
        Assert.Equal(25, v_Settings.PageSize);
        Assert.Equal(12, v_Settings.NBomberCopies);
        Assert.Equal(7, v_Settings.NBomberDurationSeconds);
        Assert.Equal("PERF_USER", v_Settings.LoginName);
        Assert.True(v_Settings.DatabaseConfigured);
        Assert.Equal("C:\\temp\\tks-bench", v_Settings.ReportDirectory);
        Assert.Equal(new DateTime(2025, 1, 1), v_Settings.ReportFromDate);
        Assert.Equal(new DateTime(2026, 12, 31), v_Settings.ReportToDate);
    }

    [Fact]
    public void Scenario_catalog_exposes_only_read_only_warehouse_paths()
    {
        var v_arrNames = WarehouseScenarioCatalog.Names;

        Assert.Equal(
            new[]
            {
                "MasterPaged",
                "LookupPaged",
                "DocumentPaged",
                "DetailReportPaged",
                "InventoryHistoricalReportPaged",
                "InventoryCurrentBalancePaged"
            },
            v_arrNames);
    }

    [Fact]
    public void Legacy_inventory_name_aliases_to_historical_report_only()
    {
        var v_Settings = BenchmarkSettings.FromEnvironment(new Dictionary<string, string?>
        {
            ["TKS_NBOMBER_SCENARIOS"] = "InventoryReportPaged"
        });

        Assert.Equal(new[] { "InventoryHistoricalReportPaged" }, v_Settings.NBomberScenarioNames);
    }

    [Fact]
    public void Default_scenario_selection_preserves_the_fixed_five_path_workload()
    {
        var v_Settings = BenchmarkSettings.FromEnvironment(new Dictionary<string, string?>());

        Assert.Equal(
            new[]
            {
                "MasterPaged",
                "LookupPaged",
                "DocumentPaged",
                "DetailReportPaged",
                "InventoryHistoricalReportPaged"
            },
            v_Settings.NBomberScenarioNames);
    }

    [Fact]
    public void Current_balance_runner_uses_a_distinct_scenario_contract()
    {
        var v_Runner = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Benchmarks",
            "Run-InventoryReportPagedLoadMatrix.ps1"));

        Assert.Contains("InventoryCurrentBalancePaged", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TKS_NBOMBER_SCENARIOS = 'InventoryReportPaged'", v_Runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sp_BC_Ton_Kho_Hien_Tai_Page", v_Runner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Benchmark_manifest_declares_closure_and_two_inventory_names()
    {
        var v_ManifestPath = FindRepositoryPath("TKS_Thuc_Tap_V11_Benchmarks", "benchmark-manifest.json");
        using var v_Document = JsonDocument.Parse(File.ReadAllText(v_ManifestPath));
        var v_Root = v_Document.RootElement;

        Assert.Equal(
            "TKS_Thuc_Tap_V11_Perf_10000000",
            v_Root.GetProperty("database").GetProperty("name").GetString());
        Assert.Contains(
            "InventoryHistoricalReportPaged",
            v_Root.GetProperty("scenarios").GetProperty("canonical").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains(
            "InventoryCurrentBalancePaged",
            v_Root.GetProperty("scenarios").GetProperty("canonical").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(
            "InventoryHistoricalReportPaged",
            v_Root.GetProperty("scenarios").GetProperty("aliases").GetProperty("InventoryReportPaged").GetString());
        Assert.True(v_Root.GetProperty("requiredObjects").GetProperty("tables").GetArrayLength() > 0);
        Assert.True(v_Root.GetProperty("requiredObjects").GetProperty("procedures").GetArrayLength() > 0);
        Assert.True(v_Root.GetProperty("requiredObjects").GetProperty("indexes").GetArrayLength() > 0);
    }

    [Fact]
    public void Settings_parse_inventory_report_dates_from_environment()
    {
        var v_Settings = BenchmarkSettings.FromEnvironment(new Dictionary<string, string?>
        {
            ["TKS_PERF_FROM_DATE"] = "2026-01-01",
            ["TKS_PERF_TO_DATE"] = "2026-12-31"
        });

        Assert.Equal(new DateTime(2026, 1, 1), v_Settings.ReportFromDate);
        Assert.Equal(new DateTime(2026, 12, 31), v_Settings.ReportToDate);
    }

    [Fact]
    public void Load_scenarios_convert_operation_exceptions_into_recorded_failures()
    {
        var v_LoadTest = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Benchmarks",
            "WarehouseLoadTest.cs"));

        Assert.Contains("catch (Exception", v_LoadTest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Response.Fail", v_LoadTest, StringComparison.OrdinalIgnoreCase);
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
