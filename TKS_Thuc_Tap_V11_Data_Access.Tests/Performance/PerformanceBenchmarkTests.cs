using TKS_Thuc_Tap_V11_Data_Access.Tests.Performance;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class PerformanceBenchmarkTests
{
    [Fact]
    public void Options_support_one_million_rows_and_keep_full_load_opt_in()
    {
        var v_Options = PerformanceBenchmark.ParseOptions(new Dictionary<string, string?>
        {
            ["TKS_PERF_ROWS"] = "1000000",
            ["TKS_PERF_PAGE_SIZE"] = "10",
            ["TKS_PERF_LOGIN"] = "PERF_USER"
        });

        Assert.Equal(1_000_000, v_Options.RecordCount);
        Assert.Equal(10, v_Options.PageSize);
        Assert.Equal("PERF_USER", v_Options.LoginName);
        Assert.False(v_Options.AllowFullLoad);
    }

    [Fact]
    public void Benchmark_seed_and_sql_stats_use_the_authorized_benchmark_login()
    {
        var v_Seed = File.ReadAllText(FindRepositoryPath("Database", "Performance", "WarehousePerformance.Seed.sql"));
        var v_SqlStats = File.ReadAllText(FindRepositoryPath("Database", "Performance", "WarehousePerformance.SqlStats.sql"));

        Assert.Contains("tbl_DM_Kho_User", v_Seed);
        Assert.Contains("PERF_USER", v_Seed);
        Assert.DoesNotContain("CREATE UNIQUE CLUSTERED INDEX IX_Perf_Numbers", v_Seed);
        Assert.Contains("MAXDOP 1", v_Seed);
        Assert.Contains("Is_Posted", v_Seed);
        Assert.Contains("@Ma_Dang_Nhap = N'PERF_USER'", v_SqlStats);
    }

    [Fact]
    public void Percentile_uses_sorted_nearest_rank_values()
    {
        var v_arrValues = new[] { 40d, 10d, 30d, 20d };

        Assert.Equal(20d, PerformanceBenchmark.Percentile(v_arrValues, 0.50));
        Assert.Equal(40d, PerformanceBenchmark.Percentile(v_arrValues, 0.99));
    }

    [Fact]
    public void Scenario_catalog_covers_full_paged_join_and_crud_paths()
    {
        var v_arrNames = PerformanceBenchmark.GetScenarioNames(new PerformanceOptions()).ToArray();

        Assert.Contains("MasterFullLoad", v_arrNames);
        Assert.Contains("MasterPaged", v_arrNames);
        Assert.Contains("DetailReportPaged", v_arrNames);
        Assert.Contains("InventoryHistoricalReportPaged", v_arrNames);
        Assert.Contains("ConcurrentMixedWorkload", v_arrNames);
        Assert.Contains("CrudContract", v_arrNames);
    }

    [Fact]
    public void Synthetic_benchmark_materializes_and_maps_one_hundred_thousand_rows()
    {
        var v_Report = PerformanceBenchmark.RunSynthetic(new PerformanceOptions
        {
            RecordCount = 100_000,
            PageSize = 10,
            Workers = 2,
            Iterations = 1,
            WarmupIterations = 0
        });

        Assert.Equal(100_000, v_Report.RecordCount);
        Assert.Contains(v_Report.Metrics, metric => metric.Scenario == "SyntheticReflectionMapping");
        Assert.Equal(100_000, v_Report.Metrics.Single(metric => metric.Scenario == "SyntheticReflectionMapping").RowsObserved);
    }

    [Fact]
    public void Serialized_report_does_not_contain_connection_string_secrets()
    {
        var v_Report = new PerformanceReport
        {
            RecordCount = 100_000,
            ConnectionStringConfigured = true,
            Metrics = Array.Empty<PerformanceMetric>()
        };

        var v_Json = PerformanceBenchmark.SerializeReport(v_Report);

        Assert.DoesNotContain("Password", v_Json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionString", v_Json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Storage_snapshot_query_casts_page_counts_to_bigint_before_multiplication()
    {
        var v_Source = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access.Tests",
            "Performance",
            "PerformanceBenchmark.cs"));

        Assert.Contains("CAST(size AS bigint)", v_Source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "CAST(FILEPROPERTY(name, 'SpaceUsed') AS bigint)",
            v_Source,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Database_benchmark_runs_only_when_explicitly_enabled()
    {
        var v_Options = PerformanceBenchmark.ParseOptions(Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(item => (string)item.Key, item => item.Value?.ToString()));

        if (!v_Options.RunDatabase)
            return;

        var v_Report = await PerformanceBenchmark.RunDatabaseAsync(v_Options);

        Assert.Contains(v_Report.Metrics, metric => metric.Scenario == "MasterPaged" && metric.Error is null);
        Assert.Contains(v_Report.Metrics, metric => metric.Scenario == "InventoryHistoricalReportPaged" && metric.Error is null);
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
