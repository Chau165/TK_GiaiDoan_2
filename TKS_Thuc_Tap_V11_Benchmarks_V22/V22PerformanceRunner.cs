using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using NBomber.CSharp;
using NBomber.Contracts;
using NBomber.Contracts.Stats;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Benchmarks_V22;

internal static class V22PerformanceRunner
{
    private static readonly string[] Scenarios =
    [
        "MasterPaged", "LookupPaged", "DocumentPaged", "DetailReportPaged",
        "InventoryHistoricalReportPaged", "InventoryCurrentBalancePaged"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object ObservedInstanceWriteGate = new();
    private static string? BdnScenario;

    internal static Task<int> RunLoadAsync(string[] args)
    {
        var options = ParseOptions(args);
        var scenarioName = Required(options, "scenario");
        var runId = Required(options, "run-id");
        var blockId = Required(options, "block-id");
        var profile = Required(options, "profile");
        var targetDatabase = Required(options, "target-database");
        var reportDirectory = Path.GetFullPath(Required(options, "report-directory"));
        var metadataPath = Path.GetFullPath(Required(options, "metadata-path"));
        var copies = ParseInteger(Required(options, "copies"), "copies", 1, 8);
        var warmupSeconds = ParseInteger(Required(options, "warmup-seconds"), "warmup-seconds", 3, 3);
        var durationSeconds = ParseInteger(Required(options, "duration-seconds"), "duration-seconds", 15, 15);

        ValidateScenario(scenarioName);
        if (profile is not ("LEGACY_ISOLATED" or "MIXED_REGRESSION"))
            throw new ArgumentOutOfRangeException(nameof(profile), "Only approved Batch 3 performance profiles are supported.");
        if (targetDatabase != "TKS_Thuc_Tap_V11_Perf_10000000")
            throw new InvalidDataException("Performance target database differs from the approved legacy target.");
        if (string.IsNullOrWhiteSpace(CConfig.TKS_Thuc_Tap_V11_Conn_String))
            throw new InvalidDataException("TKS_V22_CONNECTION_STRING is not configured for this process.");
        EnsureNewPath(reportDirectory);
        EnsureNewFile(metadataPath);
        Directory.CreateDirectory(reportDirectory);

        var processStartedUtc = DateTimeOffset.UtcNow;
        var metadata = new Dictionary<string, object?>
        {
            ["SchemaVersion"] = "warehouse-benchmark-v22-performance-block/1",
            ["ProtocolVersion"] = "2.2",
            ["RunId"] = runId,
            ["BlockId"] = blockId,
            ["Profile"] = profile,
            ["Scenario"] = scenarioName,
            ["Copies"] = copies,
            ["WarmupSeconds"] = warmupSeconds,
            ["ConfiguredDurationSeconds"] = durationSeconds,
            ["TargetDatabase"] = targetDatabase,
            ["ProcessStartedUtc"] = processStartedUtc.ToString("O", CultureInfo.InvariantCulture),
            ["ConnectionStringPersisted"] = false,
            ["Status"] = "IN_PROGRESS"
        };
        WriteNewJson(metadataPath, metadata);

        long totalItemsObserved = 0;
        long successfulOperations = 0;
        var observedInstanceNumbers = new ConcurrentDictionary<int, byte>();
        var scenario = Scenario.Create(
            scenarioName,
            async context =>
            {
                try
                {
                    var instanceNumber = context.ScenarioInfo.InstanceNumber;
                    if (observedInstanceNumbers.TryAdd(instanceNumber, 0))
                    {
                        AppendJsonLine(metadataPath + ".observed.jsonl", new
                        {
                            RunId = runId,
                            BlockId = blockId,
                            Scenario = scenarioName,
                            ProcessId = Environment.ProcessId,
                            InstanceNumber = instanceNumber,
                            ObservedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                        });
                    }
                    var itemCount = await ExecuteAsync(scenarioName).ConfigureAwait(false);
                    Interlocked.Add(ref totalItemsObserved, itemCount);
                    Interlocked.Increment(ref successfulOperations);
                    GC.KeepAlive(itemCount);
                    return Response.Ok();
                }
                catch (Exception exception)
                {
                    var classification = ClassifyException(exception);
                    return Response.Fail(classification, SafeMessage(exception.Message), 0L, 0d);
                }
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(warmupSeconds))
            .WithLoadSimulations(Simulation.KeepConstant(copies: copies, during: TimeSpan.FromSeconds(durationSeconds)));

        var runnerInvokedUtc = DateTimeOffset.UtcNow;
        _ = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFormats(ReportFormat.Csv, ReportFormat.Md, ReportFormat.Txt)
            .WithReportFolder(reportDirectory)
            .WithReportFileName("v22-nbomber")
            .WithTestSuite("WAREHOUSE_BENCHMARK_V22_LEGACY")
            .Run();

        metadata["RunnerInvokedUtc"] = runnerInvokedUtc.ToString("O", CultureInfo.InvariantCulture);
        metadata["ProcessFinishedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        metadata["SuccessfulOperations"] = successfulOperations;
        metadata["TotalItemsObserved"] = totalItemsObserved;
        metadata["ObservedInstanceNumbers"] = observedInstanceNumbers.Keys.OrderBy(value => value).ToArray();
        metadata["ObservedCopies"] = observedInstanceNumbers.Count;
        metadata["Status"] = "PROCESS_COMPLETED_REPORT_REQUIRES_EXTERNAL_VALIDATION";
        WriteNewJson(metadataPath + ".completed.json", metadata);
        return Task.FromResult(0);
    }

    internal static int RunBdn(string[] args)
    {
        var options = ParseOptions(args);
        var scenarioName = Required(options, "scenario");
        var runId = Required(options, "run-id");
        var blockId = Required(options, "block-id");
        var outputDirectory = Path.GetFullPath(Required(options, "output-directory"));
        var metadataPath = Path.GetFullPath(Required(options, "metadata-path"));
        ValidateScenario(scenarioName);
        if (string.IsNullOrWhiteSpace(CConfig.TKS_Thuc_Tap_V11_Conn_String))
            throw new InvalidDataException("TKS_V22_CONNECTION_STRING is not configured for this process.");
        EnsureNewPath(outputDirectory);
        EnsureNewFile(metadataPath);
        Directory.CreateDirectory(outputDirectory);
        Directory.SetCurrentDirectory(outputDirectory);
        BdnScenario = scenarioName;
        Environment.SetEnvironmentVariable("TKS_V22_BDN_SCENARIO", scenarioName);

        var metadata = new Dictionary<string, object?>
        {
            ["SchemaVersion"] = "warehouse-benchmark-v22-bdn-block/1",
            ["ProtocolVersion"] = "2.2",
            ["RunId"] = runId,
            ["BlockId"] = blockId,
            ["Scenario"] = scenarioName,
            ["TargetDatabase"] = "TKS_Thuc_Tap_V11_Perf_10000000",
            ["Profile"] = "LEGACY_BDN_SUPPLEMENTAL",
            ["Configuration"] = new { Toolchain = "InProcessNoEmit", LaunchCount = 1, WarmupCount = 2, IterationCount = 5, InvocationCount = 1, UnrollFactor = 1 },
            ["StartedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["ConnectionStringPersisted"] = false,
            ["Status"] = "IN_PROGRESS"
        };
        WriteNewJson(metadataPath, metadata);
        BenchmarkRunner.Run<V22DatabaseBenchmark>(new V22LowMemoryConfig(), Array.Empty<string>());
        metadata["FinishedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        metadata["Status"] = "PROCESS_COMPLETED_ARTIFACT_REQUIRES_EXTERNAL_VALIDATION";
        WriteNewJson(metadataPath + ".completed.json", metadata);
        return 0;
    }

    internal static string CurrentBdnScenario => BdnScenario
        ?? Environment.GetEnvironmentVariable("TKS_V22_BDN_SCENARIO")
        ?? throw new InvalidOperationException("BDN scenario was not configured.");

    internal static Task<int> ExecuteAsync(string scenarioName)
    {
        const int page = 1;
        const int size = 10;
        const string login = "PERF_USER";
        var from = new DateTime(2025, 1, 1);
        var to = new DateTime(2026, 12, 31);

        return scenarioName switch
        {
            "MasterPaged" => ReadMasterAsync(page, size),
            "LookupPaged" => ReadLookupAsync(page, size),
            "DocumentPaged" => ReadDocumentsAsync(page, size, login),
            "DetailReportPaged" => ReadDetailsAsync(page, size, login, from, to),
            "InventoryHistoricalReportPaged" => ReadInventoryAsync(page, size, login, from, to, false),
            "InventoryCurrentBalancePaged" => ReadInventoryAsync(page, size, login, from, to, true),
            _ => throw new ArgumentOutOfRangeException(nameof(scenarioName))
        };
    }

    private static async Task<int> ReadMasterAsync(int page, int size) =>
        (await new CWarehouseMaster_Controller().List_Master_Page_Async("SanPham", page, size, "").ConfigureAwait(false)).Items.Count;

    private static async Task<int> ReadLookupAsync(int page, int size) =>
        (await new CWarehouseMaster_Controller().List_Lookup_Page_Async("SanPham", page, size, "").ConfigureAwait(false)).Items.Count;

    private static async Task<int> ReadDocumentsAsync(int page, int size, string login) =>
        (await new CWarehouseDocument_Controller().List_Documents_Page_Async(true, page, size, "", login, null).ConfigureAwait(false)).Items.Count;

    private static async Task<int> ReadDetailsAsync(int page, int size, string login, DateTime from, DateTime to) =>
        (await new CWarehouseReport_Controller().Detail_Report_Page_Async(true, from, to, page, size, login, null).ConfigureAwait(false)).Items.Count;

    private static async Task<int> ReadInventoryAsync(int page, int size, string login, DateTime from, DateTime to, bool current) =>
        (await new CWarehouseReport_Controller().Inventory_Report_Page_Async(from, to, page, size, login, null,
            p_bRead_Current_Balance: current).ConfigureAwait(false)).Items.Count;

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException("Options must be supplied as --name value pairs.");
            var key = args[index][2..];
            if (!result.TryAdd(key, args[index + 1]))
                throw new ArgumentException($"Duplicate option: --{key}");
        }
        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Required option missing: --{name}");

    private static int ParseInteger(string value, string name, int minimum, int maximum)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < minimum || parsed > maximum)
            throw new ArgumentOutOfRangeException(name, $"--{name} must be from {minimum} through {maximum}.");
        return parsed;
    }

    private static void ValidateScenario(string scenarioName)
    {
        if (!Scenarios.Contains(scenarioName, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(scenarioName), "Only the six approved legacy read scenarios are supported.");
    }

    private static void EnsureNewPath(string path)
    {
        if (Directory.Exists(path) || File.Exists(path))
            throw new IOException("Performance output paths are never reused.");
    }

    private static void EnsureNewFile(string path)
    {
        if (Directory.Exists(path) || File.Exists(path))
            throw new IOException("Performance evidence files are never overwritten.");
    }

    private static void WriteNewJson(string path, object value)
    {
        EnsureNewFile(path);
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, JsonOptions);
        stream.WriteByte((byte)'\n');
        stream.Flush(true);
    }

    private static void AppendJsonLine(string path, object value)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        lock (ObservedInstanceWriteGate)
        {
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
            stream.WriteByte((byte)'\n');
            stream.Flush(true);
        }
    }

    private static string ClassifyException(Exception exception) => exception switch
    {
        Microsoft.Data.SqlClient.SqlException { Number: -2 } => "SQL_TIMEOUT",
        Microsoft.Data.SqlClient.SqlException { Number: 1205 } => "DEADLOCK",
        Microsoft.Data.SqlClient.SqlException { Number: 8645 or 8651 or 8657 } => "RESOURCE_SEMAPHORE",
        _ when exception.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase) => "DEADLOCK",
        _ when exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) => "SQL_TIMEOUT",
        _ => "PRODUCT_ERROR"
    };

    private static string SafeMessage(string message)
    {
        var safe = System.Text.RegularExpressions.Regex.Replace(message,
            "(?i)(password|pwd|user\\s*id|uid|access\\s*token)\\s*=\\s*[^;\\s]+", "$1=<redacted>");
        return safe.Length <= 1000 ? safe : safe[..1000];
    }
}

internal sealed class V22LowMemoryConfig : ManualConfig
{
    internal V22LowMemoryConfig()
    {
        AddJob(Job.Default
            .WithId("V22-LowMemory")
            .WithToolchain(InProcessNoEmitToolchain.Instance)
            .WithLaunchCount(1)
            .WithWarmupCount(2)
            .WithIterationCount(5)
            .WithInvocationCount(1)
            .WithUnrollFactor(1));
        AddExporter(CsvExporter.Default);
        AddColumnProvider(DefaultColumnProviders.Instance);
        AddLogger(ConsoleLogger.Default);
        WithOptions(ConfigOptions.DisableOptimizationsValidator);
    }
}

public class V22DatabaseBenchmark
{
    [GlobalSetup]
    public void Setup() => _ = V22PerformanceRunner.CurrentBdnScenario;

    [Benchmark]
    public Task<int> Execute() => V22PerformanceRunner.ExecuteAsync(V22PerformanceRunner.CurrentBdnScenario);
}
