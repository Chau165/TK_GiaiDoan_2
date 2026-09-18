using TKS_Thuc_Tap_V11_Data_Access.Tests.Performance;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class PerformanceBenchmarkTests
{
    [Fact]
    public void Options_support_one_million_rows_and_keep_full_load_opt_in()
    {
        var options = PerformanceBenchmark.ParseOptions(new Dictionary<string, string?>
        {
            ["TKS_PERF_ROWS"] = "1000000",
            ["TKS_PERF_PAGE_SIZE"] = "10",
            ["TKS_PERF_LOGIN"] = "PERF_USER"
        });

        Assert.Equal(1_000_000, options.RecordCount);
        Assert.Equal(10, options.PageSize);
        Assert.Equal("PERF_USER", options.LoginName);
        Assert.False(options.AllowFullLoad);
    }

    [Fact]
    public void Benchmark_seed_and_sql_stats_use_the_authorized_benchmark_login()
    {
        var seed = File.ReadAllText(FindRepositoryPath("Database", "Performance", "WarehousePerformance.Seed.sql"));
        var sqlStats = File.ReadAllText(FindRepositoryPath("Database", "Performance", "WarehousePerformance.SqlStats.sql"));

        Assert.Contains("tbl_DM_Kho_User", seed);
        Assert.Contains("PERF_USER", seed);
        Assert.DoesNotContain("CREATE UNIQUE CLUSTERED INDEX IX_Perf_Numbers", seed);
        Assert.Contains("MAXDOP 1", seed);
        Assert.Contains("Is_Posted", seed);
        Assert.Contains("@Ma_Dang_Nhap = N'PERF_USER'", sqlStats);
    }

    [Fact]
    public void Percentile_uses_sorted_nearest_rank_values()
    {
        var values = new[] { 40d, 10d, 30d, 20d };

        Assert.Equal(20d, PerformanceBenchmark.Percentile(values, 0.50));
        Assert.Equal(40d, PerformanceBenchmark.Percentile(values, 0.99));
    }

    [Fact]
    public void Scenario_catalog_covers_full_paged_join_and_crud_paths()
    {
        var names = PerformanceBenchmark.GetScenarioNames(new PerformanceOptions()).ToArray();

        Assert.Contains("MasterFullLoad", names);
        Assert.Contains("MasterPaged", names);
        Assert.Contains("DetailReportPaged", names);
        Assert.Contains("InventoryHistoricalReportPaged", names);
        Assert.Contains("ConcurrentMixedWorkload", names);
        Assert.Contains("CrudContract", names);
    }

    [Fact]
    public void Synthetic_benchmark_materializes_and_maps_one_hundred_thousand_rows()
    {
        var report = PerformanceBenchmark.RunSynthetic(new PerformanceOptions
        {
            RecordCount = 100_000,
            PageSize = 10,
            Workers = 2,
            Iterations = 1,
            WarmupIterations = 0
        });

        Assert.Equal(100_000, report.RecordCount);
        Assert.Contains(report.Metrics, metric => metric.Scenario == "SyntheticReflectionMapping");
        Assert.Equal(100_000, report.Metrics.Single(metric => metric.Scenario == "SyntheticReflectionMapping").RowsObserved);
    }

    [Fact]
    public void Serialized_report_does_not_contain_connection_string_secrets()
    {
        var report = new PerformanceReport
        {
            RecordCount = 100_000,
            ConnectionStringConfigured = true,
            Metrics = Array.Empty<PerformanceMetric>()
        };

        var json = PerformanceBenchmark.SerializeReport(report);

        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionString", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Storage_snapshot_query_casts_page_counts_to_bigint_before_multiplication()
    {
        var source = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access.Tests",
            "Performance",
            "PerformanceBenchmark.cs"));

        Assert.Contains("CAST(size AS bigint)", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "CAST(FILEPROPERTY(name, 'SpaceUsed') AS bigint)",
            source,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Database_benchmark_runs_only_when_explicitly_enabled()
    {
        var options = PerformanceBenchmark.ParseOptions(Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(item => (string)item.Key, item => item.Value?.ToString()));

        if (!options.RunDatabase)
            return;

        var report = await PerformanceBenchmark.RunDatabaseAsync(options);

        Assert.Contains(report.Metrics, metric => metric.Scenario == "MasterPaged" && metric.Error is null);
        Assert.Contains(report.Metrics, metric => metric.Scenario == "InventoryHistoricalReportPaged" && metric.Error is null);
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
