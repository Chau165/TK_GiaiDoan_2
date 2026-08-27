using TKS_Thuc_Tap_V11_Benchmarks;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests.Performance;

public sealed class ToolBenchmarkHarnessTests
{
    [Fact]
    public void Settings_parse_database_and_load_options_from_environment()
    {
        var settings = BenchmarkSettings.FromEnvironment(new Dictionary<string, string?>
        {
            ["TKS_PERF_ROWS"] = "1000000",
            ["TKS_PERF_PAGE_SIZE"] = "25",
            ["TKS_NBOMBER_COPIES"] = "12",
            ["TKS_NBOMBER_DURATION_SECONDS"] = "7",
            ["TKS_PERF_LOGIN"] = "PERF_USER",
            ["TKS_PERF_CONNECTION_STRING"] = "Server=(local);Database=Perf;",
            ["TKS_BENCH_REPORT_DIR"] = "C:\\temp\\tks-bench"
        });

        Assert.Equal(1_000_000, settings.RecordCount);
        Assert.Equal(25, settings.PageSize);
        Assert.Equal(12, settings.NBomberCopies);
        Assert.Equal(7, settings.NBomberDurationSeconds);
        Assert.Equal("PERF_USER", settings.LoginName);
        Assert.True(settings.DatabaseConfigured);
        Assert.Equal("C:\\temp\\tks-bench", settings.ReportDirectory);
    }

    [Fact]
    public void Scenario_catalog_exposes_only_read_only_warehouse_paths()
    {
        var names = WarehouseScenarioCatalog.Names;

        Assert.Equal(
            new[] { "MasterPaged", "LookupPaged", "DocumentPaged", "DetailReportPaged", "InventoryReportPaged" },
            names);
    }

    [Fact]
    public void Scenario_selection_can_limit_load_to_named_paths()
    {
        var settings = BenchmarkSettings.FromEnvironment(new Dictionary<string, string?>
        {
            ["TKS_NBOMBER_SCENARIOS"] = "MasterPaged, LookupPaged, DocumentPaged"
        });

        Assert.Equal(
            new[] { "MasterPaged", "LookupPaged", "DocumentPaged" },
            settings.NBomberScenarioNames);
    }
}
