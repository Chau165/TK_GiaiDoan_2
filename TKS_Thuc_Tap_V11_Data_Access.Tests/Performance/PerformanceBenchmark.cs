using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests.Performance;

public sealed record PerformanceOptions
{
    public int RecordCount { get; init; } = 100_000;
    public int PageSize { get; init; } = 10;
    public int Workers { get; init; } = 8;
    public int Iterations { get; init; } = 5;
    public int WarmupIterations { get; init; } = 1;
    public bool RunDatabase { get; init; }
    public bool AllowFullLoad { get; init; }
    public string ConnectionString { get; init; } = "";
    public string OutputPath { get; init; } = "";

    public static PerformanceOptions FromEnvironment(IReadOnlyDictionary<string, string?>? p_environment = null)
    {
        p_environment ??= Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(v_item => (string)v_item.Key, v_item => v_item.Value?.ToString());

        return new PerformanceOptions
        {
            RecordCount = ReadInt(p_environment, "TKS_PERF_ROWS", 100_000, 1, 10_000_000),
            PageSize = ReadInt(p_environment, "TKS_PERF_PAGE_SIZE", 10, 1, 10_000),
            Workers = ReadInt(p_environment, "TKS_PERF_WORKERS", 8, 1, 256),
            Iterations = ReadInt(p_environment, "TKS_PERF_ITERATIONS", 5, 1, 10_000),
            WarmupIterations = ReadInt(p_environment, "TKS_PERF_WARMUP", 1, 0, 100),
            RunDatabase = ReadBool(p_environment, "TKS_PERF_RUN"),
            AllowFullLoad = ReadBool(p_environment, "TKS_PERF_FULL_LOAD"),
            ConnectionString = ReadString(p_environment, "TKS_PERF_CONNECTION_STRING"),
            OutputPath = ReadString(p_environment, "TKS_PERF_OUTPUT")
        };
    }

    private static int ReadInt(IReadOnlyDictionary<string, string?> p_environment, string p_name, int p_default, int p_min, int p_max)
    {
        return int.TryParse(ReadString(p_environment, p_name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v_value)
            ? Math.Clamp(v_value, p_min, p_max)
            : p_default;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, string?> p_environment, string p_name)
    {
        var v_value = ReadString(p_environment, p_name);
        return v_value is "1" or "true" or "TRUE" or "yes" or "YES";
    }

    private static string ReadString(IReadOnlyDictionary<string, string?> p_environment, string p_name)
    {
        return p_environment.TryGetValue(p_name, out var v_value) ? v_value ?? "" : "";
    }
}

public sealed class PerformanceMetric
{
    public string Scenario { get; init; } = "";
    public int Operations { get; init; }
    public long RowsObserved { get; init; }
    public double WallTimeMs { get; init; }
    public double OperationsPerSecond { get; init; }
    public double P50Ms { get; init; }
    public double P95Ms { get; init; }
    public double P99Ms { get; init; }
    public double MaxMs { get; init; }
    public long AllocatedBytes { get; init; }
    public long ManagedHeapDeltaBytes { get; init; }
    public long WorkingSetBeforeBytes { get; init; }
    public long WorkingSetAfterBytes { get; init; }
    public long WorkingSetDeltaBytes { get; init; }
    public long PeakWorkingSetBytes { get; init; }
    public double CpuTimeMs { get; init; }
    public double CpuPercentOfOneCore { get; init; }
    public double CpuPercentOfMachine { get; init; }
    public string? Error { get; init; }
}

public sealed class DatabaseStorageSnapshot
{
    public long DataAllocatedBytes { get; init; }
    public long DataUsedBytes { get; init; }
    public long LogAllocatedBytes { get; init; }
}

public sealed class PerformanceReport
{
    public DateTime StartedUtc { get; init; }
    public DateTime CompletedUtc { get; init; }
    public string Mode { get; init; } = "";
    public int RecordCount { get; init; }
    public int PageSize { get; init; }
    public int Workers { get; init; }
    public bool AllowFullLoad { get; init; }

    [JsonIgnore]
    public bool ConnectionStringConfigured { get; init; }

    public DatabaseStorageSnapshot? DatabaseBefore { get; init; }
    public DatabaseStorageSnapshot? DatabaseAfter { get; init; }
    public IReadOnlyList<PerformanceMetric> Metrics { get; init; } = Array.Empty<PerformanceMetric>();
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public static class PerformanceBenchmark
{
    private static readonly string[] s_scenarioNames =
    {
        "MasterFullLoad",
        "MasterPaged",
        "LookupFullLoad",
        "LookupPaged",
        "DocumentFullLoad",
        "DocumentPaged",
        "DocumentDetailFullLoad",
        "DetailReportFullLoad",
        "DetailReportPaged",
        "InventoryReportFullLoad",
        "InventoryReportPaged",
        "ConcurrentMixedWorkload",
        "CrudContract"
    };

    public static PerformanceOptions ParseOptions(IReadOnlyDictionary<string, string?>? p_environment = null)
    {
        return PerformanceOptions.FromEnvironment(p_environment);
    }

    public static IReadOnlyList<string> GetScenarioNames(PerformanceOptions _)
    {
        return s_scenarioNames;
    }

    public static double Percentile(IReadOnlyList<double> p_values, double p_percentile)
    {
        if (p_values.Count == 0)
            return 0;

        var v_sorted = p_values.OrderBy(v_value => v_value).ToArray();
        var v_rank = Math.Clamp((int)Math.Ceiling(p_percentile * v_sorted.Length), 1, v_sorted.Length);
        return v_sorted[v_rank - 1];
    }

    public static PerformanceReport RunSynthetic(PerformanceOptions p_options)
    {
        var v_started = DateTime.UtcNow;
        var v_materializationStart = Stopwatch.GetTimestamp();
        var v_process = Process.GetCurrentProcess();
        var v_workingSetBefore = v_process.WorkingSet64;
        var v_cpuBefore = v_process.TotalProcessorTime;
        var v_allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var v_heapBefore = GC.GetTotalMemory(false);
        var v_table = CreateSyntheticTable(p_options.RecordCount);
        var v_materializationMetric = CreateSingleMetric(
            "SyntheticDataTableMaterialization",
            p_options.RecordCount,
            v_table.Rows.Count,
            v_materializationStart,
            v_workingSetBefore,
            v_cpuBefore,
            v_allocatedBefore,
            v_heapBefore);

        var v_mappingStart = Stopwatch.GetTimestamp();
        v_process = Process.GetCurrentProcess();
        v_workingSetBefore = v_process.WorkingSet64;
        v_cpuBefore = v_process.TotalProcessorTime;
        v_allocatedBefore = GC.GetTotalAllocatedBytes(true);
        v_heapBefore = GC.GetTotalMemory(false);
        var v_mappedItems = new List<CWarehouseMaster>(v_table.Rows.Count);
        foreach (DataRow v_row in v_table.Rows)
            v_mappedItems.Add(CUtility.Map_Row_To_Entity<CWarehouseMaster>(v_row));

        var v_mappingMetric = CreateSingleMetric(
            "SyntheticReflectionMapping",
            p_options.RecordCount,
            v_mappedItems.Count,
            v_mappingStart,
            v_workingSetBefore,
            v_cpuBefore,
            v_allocatedBefore,
            v_heapBefore);

        return new PerformanceReport
        {
            StartedUtc = v_started,
            CompletedUtc = DateTime.UtcNow,
            Mode = "synthetic-in-memory",
            RecordCount = p_options.RecordCount,
            PageSize = p_options.PageSize,
            Workers = p_options.Workers,
            AllowFullLoad = p_options.AllowFullLoad,
            Metrics = new[] { v_materializationMetric, v_mappingMetric },
            Notes = new[]
            {
                "Synthetic mode measures DataTable materialization and the application's reflection mapper; it does not represent SQL Server I/O."
            }
        };
    }

    public static async Task<PerformanceReport> RunDatabaseAsync(PerformanceOptions p_options)
    {
        if (!p_options.RunDatabase)
            throw new InvalidOperationException("Set TKS_PERF_RUN=1 to enable the database benchmark.");
        if (string.IsNullOrWhiteSpace(p_options.ConnectionString))
            throw new InvalidOperationException("TKS_PERF_CONNECTION_STRING is required for the database benchmark.");

        var v_started = DateTime.UtcNow;
        CConfig.TKS_Thuc_Tap_V11_Conn_String = p_options.ConnectionString;
        CLogger.Enable_Trace = false;

        await using var v_connection = new SqlConnection(p_options.ConnectionString);
        await v_connection.OpenAsync();
        var v_databaseBefore = await ReadStorageSnapshotAsync(v_connection);
        var v_metrics = new List<PerformanceMetric>();
        var v_notes = new List<string>();

        await AddMetricAsync(v_metrics, "MasterPaged", p_options, () =>
            new CWarehouseMaster_Controller().List_Master_Page_Async("SanPham", 1, p_options.PageSize, "")
                .ContinueWith(v_task => v_task.Result.Items.Count));
        await AddMetricAsync(v_metrics, "LookupPaged", p_options, () =>
            new CWarehouseMaster_Controller().List_Lookup_Page_Async("SanPham", 1, p_options.PageSize, "")
                .ContinueWith(v_task => v_task.Result.Items.Count));
        await AddMetricAsync(v_metrics, "DocumentPaged", p_options, () =>
            new CWarehouseDocument_Controller().List_Documents_Page_Async(true, 1, p_options.PageSize, "")
                .ContinueWith(v_task => v_task.Result.Items.Count));
        await AddMetricAsync(v_metrics, "DetailReportPaged", p_options, () =>
            new CWarehouseReport_Controller().Detail_Report_Page_Async(true, new DateTime(2025, 1, 1), new DateTime(2026, 12, 31), 1, p_options.PageSize)
                .ContinueWith(v_task => v_task.Result.Items.Count));
        await AddMetricAsync(v_metrics, "InventoryReportPaged", p_options, () =>
            new CWarehouseReport_Controller().Inventory_Report_Page_Async(new DateTime(2025, 1, 1), new DateTime(2026, 12, 31), 1, p_options.PageSize)
                .ContinueWith(v_task => v_task.Result.Items.Count));

        if (p_options.AllowFullLoad)
        {
            await AddMetricAsync(v_metrics, "MasterFullLoad", p_options, () =>
                new CWarehouseMaster_Controller().List_Master_Async("SanPham")
                    .ContinueWith(v_task => v_task.Result.Count));
            await AddMetricAsync(v_metrics, "LookupFullLoad", p_options, () =>
                new CWarehouseMaster_Controller().List_Lookup_Async("SanPham")
                    .ContinueWith(v_task => v_task.Result.Count));
            await AddMetricAsync(v_metrics, "DocumentFullLoad", p_options, () =>
                new CWarehouseDocument_Controller().List_Documents_Async(true)
                    .ContinueWith(v_task => v_task.Result.Count));
            await AddMetricAsync(v_metrics, "DocumentDetailFullLoad", p_options, ()
                => new CWarehouseDocument_Controller().List_Document_Details_Async(true, 1)
                    .ContinueWith(v_task => v_task.Result.Count));
            await AddMetricAsync(v_metrics, "DetailReportFullLoad", p_options, () =>
                new CWarehouseReport_Controller().Detail_Report_Async(true, new DateTime(2025, 1, 1), new DateTime(2026, 12, 31))
                    .ContinueWith(v_task => v_task.Result.Count));
            await AddMetricAsync(v_metrics, "InventoryReportFullLoad", p_options, () =>
                new CWarehouseReport_Controller().Inventory_Report_Async(new DateTime(2025, 1, 1), new DateTime(2026, 12, 31))
                    .ContinueWith(v_task => v_task.Result.Count));
        }
        else
        {
            foreach (var v_name in new[]
                     {
                         "MasterFullLoad", "LookupFullLoad", "DocumentFullLoad", "DocumentDetailFullLoad",
                         "DetailReportFullLoad", "InventoryReportFullLoad"
                     })
            {
                v_metrics.Add(new PerformanceMetric
                {
                    Scenario = v_name,
                    Error = "Skipped by safety guard. Set TKS_PERF_FULL_LOAD=1 only on the isolated performance database."
                });
            }
        }

        await AddMetricAsync(v_metrics, "ConcurrentMixedWorkload", p_options, () =>
        {
            var v_sequence = 0;
            return Task.FromResult(0).ContinueWith(async _ =>
            {
                var v_index = Interlocked.Increment(ref v_sequence) % 4;
                return v_index switch
                {
                    0 => (await new CWarehouseMaster_Controller().List_Master_Page_Async("SanPham", 1, p_options.PageSize, "")).Items.Count,
                    1 => (await new CWarehouseDocument_Controller().List_Documents_Page_Async(true, 1, p_options.PageSize, "")).Items.Count,
                    2 => (await new CWarehouseReport_Controller().Detail_Report_Page_Async(true, new DateTime(2025, 1, 1), new DateTime(2026, 12, 31), 1, p_options.PageSize)).Items.Count,
                    _ => (await new CWarehouseReport_Controller().Inventory_Report_Page_Async(new DateTime(2025, 1, 1), new DateTime(2026, 12, 31), 1, p_options.PageSize)).Items.Count
                };
            }).Unwrap();
        }, p_concurrent: true);

        await AddCrudMetricsAsync(v_metrics, p_options, v_notes);

        var v_databaseAfter = await ReadStorageSnapshotAsync(v_connection);
        var v_report = new PerformanceReport
        {
            StartedUtc = v_started,
            CompletedUtc = DateTime.UtcNow,
            Mode = "sql-server-and-current-controllers",
            RecordCount = p_options.RecordCount,
            PageSize = p_options.PageSize,
            Workers = p_options.Workers,
            AllowFullLoad = p_options.AllowFullLoad,
            ConnectionStringConfigured = true,
            DatabaseBefore = v_databaseBefore,
            DatabaseAfter = v_databaseAfter,
            Metrics = v_metrics,
            Notes = v_notes
        };

        WriteReportIfConfigured(v_report, p_options.OutputPath);
        return v_report;
    }

    public static string SerializeReport(PerformanceReport p_report)
    {
        return JsonSerializer.Serialize(p_report, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static DataTable CreateSyntheticTable(int p_recordCount)
    {
        var v_table = new DataTable();
        v_table.Columns.Add("Auto_ID", typeof(long));
        v_table.Columns.Add("Code", typeof(string));
        v_table.Columns.Add("Name", typeof(string));
        v_table.Columns.Add("Related_ID", typeof(long));
        v_table.Columns.Add("Related_ID_2", typeof(long));
        v_table.Columns.Add("Login_Name", typeof(string));
        v_table.Columns.Add("Ghi_Chu", typeof(string));

        for (var v_index = 1; v_index <= p_recordCount; v_index++)
        {
            v_table.Rows.Add(
                (long)v_index,
                $"SP-{v_index:0000000}",
                $"Synthetic product {v_index}",
                (long)((v_index % 100) + 1),
                (long)((v_index % 50) + 1),
                "",
                "");
        }

        return v_table;
    }

    private static PerformanceMetric CreateSingleMetric(
        string p_scenario,
        int p_operations,
        long p_rows,
        long p_startTimestamp,
        long p_workingSetBefore,
        TimeSpan p_cpuBefore,
        long p_allocatedBefore,
        long p_heapBefore)
    {
        var v_process = Process.GetCurrentProcess();
        var v_wallTimeMs = Stopwatch.GetElapsedTime(p_startTimestamp).TotalMilliseconds;
        var v_cpuTimeMs = (v_process.TotalProcessorTime - p_cpuBefore).TotalMilliseconds;
        var v_allocatedBytes = GC.GetTotalAllocatedBytes(true) - p_allocatedBefore;
        var v_workingSetAfter = v_process.WorkingSet64;

        return new PerformanceMetric
        {
            Scenario = p_scenario,
            Operations = p_operations,
            RowsObserved = p_rows,
            WallTimeMs = v_wallTimeMs,
            OperationsPerSecond = p_operations / Math.Max(v_wallTimeMs / 1000d, 0.000001d),
            P50Ms = v_wallTimeMs,
            P95Ms = v_wallTimeMs,
            P99Ms = v_wallTimeMs,
            MaxMs = v_wallTimeMs,
            AllocatedBytes = Math.Max(0, v_allocatedBytes),
            ManagedHeapDeltaBytes = GC.GetTotalMemory(false) - p_heapBefore,
            WorkingSetBeforeBytes = p_workingSetBefore,
            WorkingSetAfterBytes = v_workingSetAfter,
            WorkingSetDeltaBytes = v_workingSetAfter - p_workingSetBefore,
            PeakWorkingSetBytes = Math.Max(v_workingSetAfter, v_process.PeakWorkingSet64),
            CpuTimeMs = v_cpuTimeMs,
            CpuPercentOfOneCore = v_cpuTimeMs / Math.Max(v_wallTimeMs, 0.001d) * 100d,
            CpuPercentOfMachine = v_cpuTimeMs / Math.Max(v_wallTimeMs * Environment.ProcessorCount, 0.001d) * 100d
        };
    }

    private static async Task AddMetricAsync(
        ICollection<PerformanceMetric> p_metrics,
        string p_scenario,
        PerformanceOptions p_options,
        Func<Task<int>> p_operation,
        bool p_concurrent = false)
    {
        try
        {
            p_metrics.Add(await MeasureAsync(p_scenario, p_options, p_operation, p_concurrent));
        }
        catch (Exception v_exception)
        {
            p_metrics.Add(new PerformanceMetric
            {
                Scenario = p_scenario,
                Error = SanitizeError(v_exception)
            });
        }
    }

    private static async Task<PerformanceMetric> MeasureAsync(
        string p_scenario,
        PerformanceOptions p_options,
        Func<Task<int>> p_operation,
        bool p_concurrent)
    {
        for (var v_index = 0; v_index < p_options.WarmupIterations; v_index++)
            await p_operation();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var v_process = Process.GetCurrentProcess();
        var v_workingSetBefore = v_process.WorkingSet64;
        var v_cpuBefore = v_process.TotalProcessorTime;
        var v_allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var v_heapBefore = GC.GetTotalMemory(false);
        var v_durations = new ConcurrentBag<double>();
        long v_rowsObserved = 0;
        var v_startTimestamp = Stopwatch.GetTimestamp();

        async Task RunWorkerAsync(int p_operationCount)
        {
            for (var v_index = 0; v_index < p_operationCount; v_index++)
            {
                var v_start = Stopwatch.GetTimestamp();
                var v_rows = await p_operation();
                v_durations.Add(Stopwatch.GetElapsedTime(v_start).TotalMilliseconds);
                Interlocked.Add(ref v_rowsObserved, v_rows);
            }
        }

        if (p_concurrent)
        {
            var v_workers = Enumerable.Range(0, p_options.Workers)
                .Select(_ => Task.Run(() => RunWorkerAsync(p_options.Iterations)))
                .ToArray();
            await Task.WhenAll(v_workers);
        }
        else
        {
            await RunWorkerAsync(p_options.Iterations);
        }

        var v_wallTimeMs = Stopwatch.GetElapsedTime(v_startTimestamp).TotalMilliseconds;
        v_process.Refresh();
        var v_cpuTimeMs = (v_process.TotalProcessorTime - v_cpuBefore).TotalMilliseconds;
        var v_durationsArray = v_durations.ToArray();
        var v_operations = v_durationsArray.Length;
        var v_workingSetAfter = v_process.WorkingSet64;

        return new PerformanceMetric
        {
            Scenario = p_scenario,
            Operations = v_operations,
            RowsObserved = v_rowsObserved,
            WallTimeMs = v_wallTimeMs,
            OperationsPerSecond = v_operations / Math.Max(v_wallTimeMs / 1000d, 0.000001d),
            P50Ms = Percentile(v_durationsArray, 0.50),
            P95Ms = Percentile(v_durationsArray, 0.95),
            P99Ms = Percentile(v_durationsArray, 0.99),
            MaxMs = v_durationsArray.Length == 0 ? 0 : v_durationsArray.Max(),
            AllocatedBytes = Math.Max(0, GC.GetTotalAllocatedBytes(true) - v_allocatedBefore),
            ManagedHeapDeltaBytes = GC.GetTotalMemory(false) - v_heapBefore,
            WorkingSetBeforeBytes = v_workingSetBefore,
            WorkingSetAfterBytes = v_workingSetAfter,
            WorkingSetDeltaBytes = v_workingSetAfter - v_workingSetBefore,
            PeakWorkingSetBytes = Math.Max(v_workingSetAfter, v_process.PeakWorkingSet64),
            CpuTimeMs = v_cpuTimeMs,
            CpuPercentOfOneCore = v_cpuTimeMs / Math.Max(v_wallTimeMs, 0.001d) * 100d,
            CpuPercentOfMachine = v_cpuTimeMs / Math.Max(v_wallTimeMs * Environment.ProcessorCount, 0.001d) * 100d
        };
    }

    private static async Task AddCrudMetricsAsync(ICollection<PerformanceMetric> p_metrics, PerformanceOptions p_options, ICollection<string> p_notes)
    {
        var v_masterController = new CWarehouseMaster_Controller();
        await AddMetricAsync(p_metrics, "Crud_Master_Save_Update", p_options, async () =>
        {
            var v_name = "PERF_BM_" + Guid.NewGuid().ToString("N");
            var v_item = new CWarehouseMaster { Name = v_name };
            await v_masterController.Save_Master_Async("DonViTinh", v_item, "benchmark", "performance");
            v_item.Name = v_name + "_U";
            await v_masterController.Save_Master_Async("DonViTinh", v_item, "benchmark", "performance");
            await CleanupUnitAsync(v_name, v_item.Name);
            return 2;
        });

        await AddMetricAsync(p_metrics, "Crud_Master_Delete_Controller", p_options, async () =>
        {
            var v_name = "PERF_BM_" + Guid.NewGuid().ToString("N");
            var v_item = new CWarehouseMaster { Name = v_name };
            await v_masterController.Save_Master_Async("DonViTinh", v_item, "benchmark", "performance");
            try
            {
                await v_masterController.Delete_Master_Async("DonViTinh", v_item.Auto_ID, "benchmark", "performance");
            }
            finally
            {
                await CleanupUnitAsync(v_name);
            }

            return 1;
        });

        var v_documentController = new CWarehouseDocument_Controller();
        await AddMetricAsync(p_metrics, "Crud_Document_Save_Controller", p_options, async ()
            => await SaveDocumentAsync(v_documentController));
        await AddMetricAsync(p_metrics, "Crud_Detail_Save_Controller", p_options, async ()
            => await SaveDetailAsync(v_documentController));
        await AddMetricAsync(p_metrics, "Crud_Document_Delete_Controller", p_options, async ()
            =>
            {
                await v_documentController.Delete_Document_Async(true, 1, "benchmark", "performance");
                return 1;
            });

        p_notes.Add("CRUD controller metrics intentionally retain parameter-contract failures; these are correctness findings, not hidden benchmark successes.");
    }

    private static async Task<int> SaveDocumentAsync(CWarehouseDocument_Controller p_controller)
    {
        var v_document = new CWarehouseDocument
        {
            Is_Receipt = true,
            So_Phieu = "PERF_BM_" + Guid.NewGuid().ToString("N"),
            Kho_ID = 1,
            NCC_ID = 1,
            Ngay_Chung_Tu = new DateTime(2025, 1, 1)
        };
        await p_controller.Save_Document_Async(v_document, "benchmark", "performance");
        return v_document.Auto_ID > 0 ? 1 : 0;
    }

    private static async Task<int> SaveDetailAsync(CWarehouseDocument_Controller p_controller)
    {
        var v_detail = new CWarehouseDocumentDetail
        {
            Document_ID = 1,
            San_Pham_ID = 1,
            So_Luong = 1,
            Don_Gia = 1
        };
        await p_controller.Save_Document_Detail_Async(true, v_detail, "benchmark", "performance");
        return v_detail.Auto_ID > 0 ? 1 : 0;
    }

    private static async Task CleanupUnitAsync(params string[] p_names)
    {
        await using var v_connection = new SqlConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String);
        await v_connection.OpenAsync();
        await using var v_command = v_connection.CreateCommand();
        v_command.CommandText = "DELETE FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Ten_Don_Vi_Tinh IN (SELECT value FROM STRING_SPLIT(@Names, N'|'));";
        v_command.Parameters.AddWithValue("@Names", string.Join('|', p_names));
        await v_command.ExecuteNonQueryAsync();
    }

    private static async Task<DatabaseStorageSnapshot> ReadStorageSnapshotAsync(SqlConnection p_connection)
    {
        await using var v_command = p_connection.CreateCommand();
        v_command.CommandText = """
            SELECT
                COALESCE(SUM(CASE WHEN type = 0 THEN CAST(size AS bigint) END), CONVERT(bigint, 0)) * CONVERT(bigint, 8192),
                COALESCE(SUM(CASE WHEN type = 0 THEN CAST(FILEPROPERTY(name, 'SpaceUsed') AS bigint) END), CONVERT(bigint, 0)) * CONVERT(bigint, 8192),
                COALESCE(SUM(CASE WHEN type = 1 THEN CAST(size AS bigint) END), CONVERT(bigint, 0)) * CONVERT(bigint, 8192)
            FROM sys.database_files;
            """;
        await using var v_reader = await v_command.ExecuteReaderAsync();
        await v_reader.ReadAsync();
        return new DatabaseStorageSnapshot
        {
            DataAllocatedBytes = Convert.ToInt64(v_reader.GetValue(0), CultureInfo.InvariantCulture),
            DataUsedBytes = Convert.ToInt64(v_reader.GetValue(1), CultureInfo.InvariantCulture),
            LogAllocatedBytes = Convert.ToInt64(v_reader.GetValue(2), CultureInfo.InvariantCulture)
        };
    }

    private static void WriteReportIfConfigured(PerformanceReport p_report, string p_outputPath)
    {
        if (string.IsNullOrWhiteSpace(p_outputPath))
            return;

        var v_directory = Path.GetDirectoryName(Path.GetFullPath(p_outputPath));
        if (!string.IsNullOrWhiteSpace(v_directory))
            Directory.CreateDirectory(v_directory);
        File.WriteAllText(p_outputPath, SerializeReport(p_report));
    }

    private static string SanitizeError(Exception p_exception)
    {
        var v_message = p_exception.GetBaseException().Message;
        return v_message.Length <= 500 ? v_message : v_message[..500];
    }
}
