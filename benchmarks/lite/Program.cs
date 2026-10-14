using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.BC;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Controller.XNK;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

internal static partial class Program
{
    private const string ExpectedServer = @"DESKTOP-NHQ7QPL\MSSQLSERVER19";
    private const string ExpectedDatabase = "TKS_Thuc_Tap_V11_Perf_10000000";
    private const string ApplicationLogin = "PERF_USER";
    private const int PageNumber = 1;
    private const int PageSize = 10;
    private const int WarmupCount = 3;
    private const int MeasuredCount = 10;
    private const int Concurrency = 1;
    private static readonly DateTime ReportFrom = new(2025, 1, 1);
    private static readonly DateTime ReportTo = new(2026, 12, 31);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex SecretPattern = new(
        @"(?i)(password|pwd|user\s*id|uid)\s*=\s*[^;\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static Task<int> Main(string[] args) => RunLiteV2Async(args);

    private static async Task<int> RunLiteV1Async(string[] args)
    {
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            PrintHelp();
            return 0;
        }

        var runStarted = DateTimeOffset.UtcNow;
        var runId = $"WHB-LITE-{runStarted:yyyyMMdd'T'HHmmss'Z'}-{Guid.NewGuid():N}"[..42];
        var repositoryRoot = FindRepositoryRoot();
        var outputDirectory = Path.Combine(repositoryRoot, "benchmarks", "lite", "results", runId);
        Directory.CreateDirectory(Path.GetDirectoryName(outputDirectory)!);
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
            throw new IOException("Benchmark Lite output paths are never reused.");
        Directory.CreateDirectory(outputDirectory);

        var resultsPath = Path.Combine(outputDirectory, "benchmark-lite-results.json");
        var reportPath = Path.Combine(outputDirectory, "benchmark-lite-report.md");
        var rawCsvPath = Path.Combine(outputDirectory, "benchmark-lite-raw.csv");
        var environmentPath = Path.Combine(outputDirectory, "benchmark-lite-environment.json");
        var logPath = Path.Combine(outputDirectory, "benchmark-lite-run.log");
        File.WriteAllText(rawCsvPath,
            "Scenario,Phase,Iteration,StartedUtc,DurationMs,Success,CallSucceeded,ReturnedRows,TotalCount,SqlErrorNumber,ErrorType,ErrorMessage,TimedOut\r\n",
            new UTF8Encoding(false));

        var document = new RunDocument
        {
            RunId = runId,
            StartedUtc = runStarted,
            RepositoryPath = repositoryRoot,
            OutputDirectory = outputDirectory,
            BuildConfiguration = BuildConfiguration,
            ProcessPath = Environment.ProcessPath,
            Machine = CaptureMachineSnapshot(),
            Protocol = new ProtocolDocument
            {
                WarmupCount = WarmupCount,
                MeasuredCount = MeasuredCount,
                Concurrency = Concurrency,
                TimingBoundary = "Stopwatch around one awaited Data Access controller call",
                PercentileMethod = "Nearest rank: sorted sample at ceil(p * n), one-based",
                CommandTimeoutOverride = "NONE; Data Access source default is used"
            },
            ResultsRepresentThisMachineEnvironment = true,
            ResultsAreNotProductionHardwareGuarantee = true,
            BusinessDBWrites = 0,
            WriteMethodsInvoked = false,
            HostAdmissionUsed = false,
            HeavyTelemetryUsed = false,
            ConnectionStringPersisted = false,
            SourceIdentity = CaptureSourceIdentity(repositoryRoot)
        };

        try
        {
            var configPath = ReadOption(args, "--config") ?? Path.Combine(AppContext.BaseDirectory, "appsettings.Benchmark.json");
            var connectionString = LoadConnectionString(configPath);
            document.ConfigurationPath = Path.GetFullPath(configPath);
            document.ConfigurationSha256 = HashFile(configPath);
            var connection = ValidateConnectionConfiguration(connectionString);
            document.ConfiguredServer = connection.DataSource;
            document.ConfiguredDatabase = connection.InitialCatalog;
            AppendLog(logPath, $"Run {runId} started; config identity loaded without persisting its contents.");

            PreflightDocument preflight;
            try
            {
                preflight = await ReadTargetAndDatasetAsync(connectionString);
            }
            catch (TargetPreflightException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new TargetPreflightException($"Read-only target identity/dataset verification failed ({exception.GetType().Name}): {Sanitize(exception.Message)}");
            }
            document.Server = preflight.Identity.ServerName;
            document.Database = preflight.Identity.DatabaseName;
            document.DatabaseId = preflight.Identity.DatabaseId;
            document.DatabaseState = preflight.Identity.DatabaseState;
            document.DatasetScale = preflight.Dataset;
            if (!string.Equals(preflight.Identity.ServerName, ExpectedServer, StringComparison.OrdinalIgnoreCase))
                throw new TargetPreflightException($"@@SERVERNAME mismatch: expected {ExpectedServer}; observed {preflight.Identity.ServerName}.");
            if (!string.Equals(preflight.Identity.DatabaseName, ExpectedDatabase, StringComparison.OrdinalIgnoreCase))
                throw new TargetPreflightException($"DB_NAME() mismatch: expected {ExpectedDatabase}; observed {preflight.Identity.DatabaseName}.");
            if (!string.Equals(preflight.Identity.DatabaseState, "ONLINE", StringComparison.OrdinalIgnoreCase))
                throw new TargetPreflightException($"Target database state is {preflight.Identity.DatabaseState}; expected ONLINE.");
            if (preflight.Dataset.PrimaryDatasetRowCount <= 0)
                throw new TargetPreflightException("The expected large detail tables did not return a positive metadata row count.");

            CConfig.TKS_Thuc_Tap_V11_Conn_String = connectionString;
            CLogger.Enable_Trace = false;
            document.Status = "IN_PROGRESS";
            WriteJson(environmentPath, new EnvironmentDocument
            {
                RunId = runId,
                Machine = document.Machine,
                Server = document.Server,
                Database = document.Database,
                DatabaseId = document.DatabaseId,
                DatabaseState = document.DatabaseState,
                DatasetScale = document.DatasetScale,
                ResultsRepresentThisMachineEnvironment = true,
                ResultsAreNotProductionHardwareGuarantee = true
            });
            WriteJson(resultsPath, document);
            AppendLog(logPath, $"Target identity PASS; dataset metadata primary rows={preflight.Dataset.PrimaryDatasetRowCount.ToString(CultureInfo.InvariantCulture)}.");
            Console.WriteLine($"Target PASS: {document.Server} / {document.Database} (DB_ID={document.DatabaseId}); primary detail rows={preflight.Dataset.PrimaryDatasetRowCount:N0}.");

            foreach (var definition in CreateScenarioDefinitions())
            {
                document.ActiveScenario = definition.Name;
                var scenarioResult = await RunScenarioAsync(definition, rawCsvPath, logPath);
                document.Scenarios.Add(scenarioResult);
                WriteJson(resultsPath, document);
                AppendLog(logPath, $"{definition.Name}: {scenarioResult.Verdict}; success={scenarioResult.SuccessCount}/{MeasuredCount}; avg={Format(scenarioResult.AverageMs)} ms.");
                Console.WriteLine($"{definition.Name}: {scenarioResult.Verdict}; {scenarioResult.SuccessCount}/{MeasuredCount}; avg={Format(scenarioResult.AverageMs)} ms.");
            }

            document.FinishedUtc = DateTimeOffset.UtcNow;
            document.MachineAfterRun = CaptureMachineSnapshot();
            document.OverallVerdict = GetOverallVerdict(document.Scenarios);
            document.SourceCurrentCanRunOn10MDb = GetCapability(document);
            document.Status = "COMPLETED";
            WriteJson(environmentPath, new EnvironmentDocument
            {
                RunId = runId,
                Machine = document.Machine,
                MachineAfterRun = document.MachineAfterRun,
                Server = document.Server,
                Database = document.Database,
                DatabaseId = document.DatabaseId,
                DatabaseState = document.DatabaseState,
                DatasetScale = document.DatasetScale,
                ResultsRepresentThisMachineEnvironment = true,
                ResultsAreNotProductionHardwareGuarantee = true
            });
            WriteJson(resultsPath, document);
            File.WriteAllText(reportPath, BuildMarkdownReport(document, resultsPath, environmentPath, rawCsvPath), new UTF8Encoding(false));
            AppendLog(logPath, $"Run completed; verdict={document.SourceCurrentCanRunOn10MDb}; BusinessDBWrites=0.");
            Console.WriteLine($"Completed: {document.SourceCurrentCanRunOn10MDb}");
            Console.WriteLine($"Report: {reportPath}");
            return 0;
        }
        catch (TargetPreflightException exception)
        {
            document.FinishedUtc = DateTimeOffset.UtcNow;
            document.Status = "TARGET_PREFLIGHT_FAILED";
            document.HarnessFailureType = nameof(TargetPreflightException);
            document.PreflightFailure = Sanitize(exception.Message);
            document.SourceCurrentCanRunOn10MDb = "NO";
            document.OverallVerdict = "TARGET_PREFLIGHT_FAILED";
            document.MachineAfterRun = CaptureMachineSnapshot();
            WriteJson(resultsPath, document);
            WriteJson(environmentPath, new EnvironmentDocument
            {
                RunId = runId,
                Machine = document.Machine,
                MachineAfterRun = document.MachineAfterRun,
                Server = document.Server,
                Database = document.Database,
                DatabaseId = document.DatabaseId,
                DatabaseState = document.DatabaseState,
                DatasetScale = document.DatasetScale,
                PreflightFailure = document.PreflightFailure,
                ResultsRepresentThisMachineEnvironment = true,
                ResultsAreNotProductionHardwareGuarantee = true
            });
            File.WriteAllText(reportPath, BuildMarkdownReport(document, resultsPath, environmentPath, rawCsvPath), new UTF8Encoding(false));
            AppendLog(logPath, $"STOP before workload: {document.PreflightFailure}");
            Console.Error.WriteLine($"Target preflight failed; no scenario ran. {document.PreflightFailure}");
            return 2;
        }
        catch (Exception exception)
        {
            document.FinishedUtc = DateTimeOffset.UtcNow;
            document.Status = "HARNESS_FAILED";
            document.HarnessFailureType = exception.GetType().Name;
            document.PreflightFailure = Sanitize(exception.Message);
            document.MachineAfterRun = CaptureMachineSnapshot();
            WriteJson(resultsPath, document);
            WriteJson(environmentPath, new EnvironmentDocument
            {
                RunId = runId,
                Machine = document.Machine,
                MachineAfterRun = document.MachineAfterRun,
                Server = document.Server,
                Database = document.Database,
                DatabaseId = document.DatabaseId,
                DatabaseState = document.DatabaseState,
                DatasetScale = document.DatasetScale,
                PreflightFailure = document.PreflightFailure,
                ResultsRepresentThisMachineEnvironment = true,
                ResultsAreNotProductionHardwareGuarantee = true
            });
            File.WriteAllText(reportPath, BuildMarkdownReport(document, resultsPath, environmentPath, rawCsvPath), new UTF8Encoding(false));
            AppendLog(logPath, $"Harness failed: {exception.GetType().Name}: {document.PreflightFailure}");
            Console.Error.WriteLine($"Benchmark Lite harness failed: {exception.GetType().Name}: {document.PreflightFailure}");
            return 1;
        }
    }

    private static async Task<ScenarioResult> RunScenarioAsync(ScenarioDefinition definition, string rawCsvPath, string logPath)
    {
        var result = new ScenarioResult
        {
            Name = definition.Name,
            ApplicationMethod = definition.ApplicationMethod,
            StoredProcedure = definition.StoredProcedure,
            ParametersSummary = definition.ParametersSummary,
            WarmupCount = WarmupCount,
            MeasuredCount = MeasuredCount
        };

        var smoke = await TimeCallAsync(definition, "SMOKE", 1);
        AppendCsv(rawCsvPath, smoke);
        result.SmokeResult = new SmokeDocument { Status = smoke.Success ? "PASS" : "SMOKE_FAIL", Call = smoke };
        if (!smoke.Success)
        {
            result.Errors.Add(ToError(smoke));
            result.Verdict = "SMOKE_FAIL";
            AppendLog(logPath, $"{definition.Name} smoke failed; skipped warmup and measured calls.");
            return result;
        }

        for (var iteration = 1; iteration <= WarmupCount; iteration++)
        {
            var warmup = await TimeCallAsync(definition, "WARMUP", iteration);
            AppendCsv(rawCsvPath, warmup);
            result.WarmupAttempts++;
            if (!warmup.Success)
            {
                result.Errors.Add(ToError(warmup));
                result.WarmupStatus = "FAIL";
                result.Verdict = warmup.TimedOut ? "TIMEOUT" : "FAIL";
                AppendLog(logPath, $"{definition.Name} warmup failed; skipped measured calls for this scenario.");
                return result;
            }
        }
        result.WarmupStatus = "PASS";

        for (var iteration = 1; iteration <= MeasuredCount; iteration++)
        {
            var measured = await TimeCallAsync(definition, "MEASURED", iteration);
            AppendCsv(rawCsvPath, measured);
            result.MeasuredAttempts++;
            result.MeasuredResults.Add(measured);
            if (!measured.Success)
                result.Errors.Add(ToError(measured));
        }

        result.ReturnedRows = result.MeasuredResults.Select(item => item.ReturnedRows).ToList();
        result.TotalCount = result.MeasuredResults.Select(item => item.TotalCount).ToList();

        var successful = result.MeasuredResults.Where(item => item.Success).Select(item => item.DurationMs).Order().ToArray();
        result.SuccessCount = successful.Length;
        result.FailureCount = result.MeasuredAttempts - result.SuccessCount;
        if (successful.Length > 0)
        {
            result.MinMs = Round(successful[0]);
            result.AverageMs = Round(successful.Average());
            result.P50Ms = Round(NearestRank(successful, 0.50));
            result.P95Ms = Round(NearestRank(successful, 0.95));
            result.MaxMs = Round(successful[^1]);
        }

        result.Verdict = result.MeasuredResults.Any(item => item.TimedOut)
            ? "TIMEOUT"
            : result.SuccessCount == MeasuredCount ? "PASS" : "FAIL";
        return result;
    }

    private static async Task<IterationDocument> TimeCallAsync(ScenarioDefinition definition, string phase, int iteration)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var value = await InvokeScenarioAsync(definition.Name).ConfigureAwait(false);
            stopwatch.Stop();
            var valid = value.ReturnedRows > 0 && value.ReturnedRows <= PageSize && value.TotalCount >= value.ReturnedRows && value.TotalCount > 0;
            return new IterationDocument
            {
                Scenario = definition.Name,
                Phase = phase,
                Iteration = iteration,
                StartedUtc = startedUtc,
                DurationMs = Round(stopwatch.Elapsed.TotalMilliseconds),
                Success = valid,
                CallSucceeded = true,
                ReturnedRows = value.ReturnedRows,
                TotalCount = value.TotalCount,
                ErrorType = valid ? null : "ResultSemanticsInvalid",
                ErrorMessage = valid ? null : $"Expected 1..{PageSize} rows and Total_Count >= ReturnedRows > 0; observed rows={value.ReturnedRows}, Total_Count={value.TotalCount}.",
                TimedOut = false
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            stopwatch.Stop();
            var sqlException = FindSqlException(exception);
            var isTimeout = sqlException?.Number == -2 || exception is TimeoutException;
            return new IterationDocument
            {
                Scenario = definition.Name,
                Phase = phase,
                Iteration = iteration,
                StartedUtc = startedUtc,
                DurationMs = Round(stopwatch.Elapsed.TotalMilliseconds),
                Success = false,
                CallSucceeded = false,
                SqlErrorNumber = sqlException?.Number,
                ErrorType = exception.GetType().Name,
                ErrorMessage = Sanitize(exception.Message),
                TimedOut = isTimeout
            };
        }
    }

    private static async Task<CallResult> InvokeScenarioAsync(string scenario)
    {
        switch (scenario)
        {
            case "MasterPaged":
            {
                var page = await new CWarehouseMasterSupport_Controller()
                    .List_Master_Page_Async("SanPham", PageNumber, PageSize, "").ConfigureAwait(false);
                return new CallResult(page.Items.Count, page.Total_Count);
            }
            case "LookupPaged":
            {
                var page = await new CWarehouseMasterSupport_Controller()
                    .List_Lookup_Page_Async("SanPham", PageNumber, PageSize, "").ConfigureAwait(false);
                return new CallResult(page.Items.Count, page.Total_Count);
            }
            case "DocumentPaged":
            {
                var page = new CWarehouseDocumentSupport_Controller()
                    .sp_XNK_Document_Page(true, PageNumber, PageSize, "", ApplicationLogin, null);
                return new CallResult(page.Items.Count, page.Total_Count);
            }
            case "DetailReportPaged":
            {
                var page = new CWarehouseReport_Controller()
                    .F9002_sp_Chi_Tiet_Nhap_Page(ReportFrom, ReportTo, PageNumber, PageSize, ApplicationLogin, null);
                return new CallResult(page.Items.Count, page.Total_Count);
            }
            case "InventoryHistoricalReportPaged":
            {
                var page = new CWarehouseReport_Controller()
                    .F9002_sp_Xuat_Nhap_Ton_Page(ReportFrom, ReportTo, PageNumber, PageSize, ApplicationLogin, null);
                return new CallResult(page.Items.Count, page.Total_Count);
            }
            case "InventoryCurrentBalancePaged":
            {
                var page = new CWarehouseReport_Controller()
                    .F9001_sp_Ton_Kho_Hien_Tai_Page(PageNumber, PageSize, ApplicationLogin, null);
                return new CallResult(page.Items.Count, page.Total_Count);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), "Only the six read-only Warehouse scenarios are supported.");
        }
    }

    private static IReadOnlyList<ScenarioDefinition> CreateScenarioDefinitions() =>
    [
        new("MasterPaged", "CWarehouseMasterSupport_Controller.List_Master_Page_Async", "dbo.sp_DM_Master_Page",
            new { MasterType = "SanPham", PageNumber, PageSize, SearchText = "" }),
        new("LookupPaged", "CWarehouseMasterSupport_Controller.List_Lookup_Page_Async", "dbo.sp_DM_Lookup_Page",
            new { MasterType = "SanPham", PageNumber, PageSize, SearchText = "" }),
        new("DocumentPaged", "CWarehouseDocumentSupport_Controller.sp_XNK_Document_Page", "dbo.sp_XNK_Document_Page",
            new { IsReceipt = true, PageNumber, PageSize, SearchText = "", Login = ApplicationLogin, WarehouseId = (long?)null }),
        new("DetailReportPaged", "CWarehouseReport_Controller.F9002_sp_Chi_Tiet_Nhap_Page", "dbo.F9002_sp_Chi_Tiet_Nhap_Page",
            new { From = ReportFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), To = ReportTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PageNumber, PageSize, Login = ApplicationLogin, WarehouseId = (long?)null }),
        new("InventoryHistoricalReportPaged", "CWarehouseReport_Controller.F9002_sp_Xuat_Nhap_Ton_Page", "dbo.F9002_sp_Xuat_Nhap_Ton_Page",
            new { From = ReportFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), To = ReportTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PageNumber, PageSize, Login = ApplicationLogin, WarehouseId = (long?)null }),
        new("InventoryCurrentBalancePaged", "CWarehouseReport_Controller.F9001_sp_Ton_Kho_Hien_Tai_Page", "dbo.F9001_sp_Ton_Kho_Hien_Tai_Page",
            new { PageNumber, PageSize, Login = ApplicationLogin, WarehouseId = (long?)null })
    ];

    private static async Task<PreflightDocument> ReadTargetAndDatasetAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var identityCommand = connection.CreateCommand();
        identityCommand.CommandTimeout = 10;
        identityCommand.CommandText = "SELECT @@SERVERNAME, DB_NAME(), DB_ID(), d.state_desc FROM sys.databases AS d WHERE d.database_id = DB_ID();";
        await using var identityReader = await identityCommand.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await identityReader.ReadAsync().ConfigureAwait(false))
            throw new TargetPreflightException("The target identity query returned no row.");
        var identity = new DatabaseIdentity(
            identityReader.GetString(0),
            identityReader.GetString(1),
            Convert.ToInt32(identityReader.GetValue(2), CultureInfo.InvariantCulture),
            identityReader.GetString(3));
        await identityReader.DisposeAsync().ConfigureAwait(false);
        if (!string.Equals(identity.ServerName, ExpectedServer, StringComparison.OrdinalIgnoreCase))
            throw new TargetPreflightException($"@@SERVERNAME mismatch: expected {ExpectedServer}; observed {identity.ServerName}.");
        if (!string.Equals(identity.DatabaseName, ExpectedDatabase, StringComparison.OrdinalIgnoreCase))
            throw new TargetPreflightException($"DB_NAME() mismatch: expected {ExpectedDatabase}; observed {identity.DatabaseName}.");
        if (!string.Equals(identity.DatabaseState, "ONLINE", StringComparison.OrdinalIgnoreCase))
            throw new TargetPreflightException($"Target database state is {identity.DatabaseState}; expected ONLINE.");

        await using var datasetCommand = connection.CreateCommand();
        datasetCommand.CommandTimeout = 10;
        datasetCommand.CommandText = """
            SELECT s.name, t.name, SUM(CONVERT(bigint, p.rows))
            FROM sys.tables AS t
            JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            JOIN sys.partitions AS p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
            WHERE s.name = N'dbo'
              AND t.name IN (N'tbl_XNK_Nhap_Kho_Raw_Data', N'tbl_XNK_Xuat_Kho_Raw_Data', N'tbl_XNK_Nhap_Kho', N'tbl_XNK_Xuat_Kho')
            GROUP BY s.name, t.name
            ORDER BY t.name;
            """;
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        await using var datasetReader = await datasetCommand.ExecuteReaderAsync().ConfigureAwait(false);
        while (await datasetReader.ReadAsync().ConfigureAwait(false))
            counts[$"{datasetReader.GetString(0)}.{datasetReader.GetString(1)}"] =
                Convert.ToInt64(datasetReader.GetValue(2), CultureInfo.InvariantCulture);

        var inbound = counts.GetValueOrDefault("dbo.tbl_XNK_Nhap_Kho_Raw_Data");
        var outbound = counts.GetValueOrDefault("dbo.tbl_XNK_Xuat_Kho_Raw_Data");
        return new PreflightDocument(identity, new DatasetDocument
        {
            CountSource = "sys.partitions.rows metadata; index_id 0 or 1 summed per table",
            PrimaryTables = new Dictionary<string, long>
            {
                ["dbo.tbl_XNK_Nhap_Kho_Raw_Data"] = inbound,
                ["dbo.tbl_XNK_Xuat_Kho_Raw_Data"] = outbound
            },
            HeaderTables = new Dictionary<string, long>
            {
                ["dbo.tbl_XNK_Nhap_Kho"] = counts.GetValueOrDefault("dbo.tbl_XNK_Nhap_Kho"),
                ["dbo.tbl_XNK_Xuat_Kho"] = counts.GetValueOrDefault("dbo.tbl_XNK_Xuat_Kho")
            },
            PrimaryDatasetRowCount = checked(inbound + outbound)
        });
    }

    private static SqlConnectionStringBuilder ValidateConnectionConfiguration(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.DataSource, ExpectedServer, StringComparison.OrdinalIgnoreCase))
            throw new TargetPreflightException($"Configured server must be exactly {ExpectedServer}; no connection was opened.");
        if (!string.Equals(builder.InitialCatalog, ExpectedDatabase, StringComparison.OrdinalIgnoreCase))
            throw new TargetPreflightException($"Configured database must be exactly {ExpectedDatabase}; no connection was opened.");
        if (!builder.IntegratedSecurity)
            throw new TargetPreflightException("Benchmark Lite requires the current Integrated Security authentication contract.");
        return builder;
    }

    private static string LoadConnectionString(string configPath)
    {
        if (!File.Exists(configPath))
            throw new TargetPreflightException($"Benchmark-specific configuration was not found: {configPath}");
        using var json = JsonDocument.Parse(File.ReadAllText(configPath));
        if (!json.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings) ||
            !connectionStrings.TryGetProperty("Benchmark", out var benchmark) ||
            string.IsNullOrWhiteSpace(benchmark.GetString()))
            throw new TargetPreflightException("Configuration must define ConnectionStrings:Benchmark.");
        return benchmark.GetString()!;
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(Path.GetFullPath(start));
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "TKS_Thuc_Tap_V11.sln")) &&
                    Directory.Exists(Path.Combine(current.FullName, "benchmarks", "v2.2")))
                    return current.FullName;
                current = current.Parent;
            }
        }
        throw new DirectoryNotFoundException("Could not locate the TKS_Thuc_Tap_V11 repository root.");
    }

    private static SourceIdentityDocument CaptureSourceIdentity(string repositoryRoot)
    {
        var sourcePaths = new[]
        {
            "benchmarks/lite/Program.cs",
            "benchmarks/lite/BenchmarkLiteV2.cs",
            "benchmarks/lite/BenchmarkLite.csproj",
            "benchmarks/lite/appsettings.Benchmark.json",
            "Database/WarehouseModule.Procedures.sql",
            "Database/WarehouseModule.Schema.sql",
            "TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CWarehouseMasterSupport_Controller.cs",
            "TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CWarehouseDocumentSupport_Controller.cs",
            "TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs",
            "TKS_Thuc_Tap_V11_Data_Access/Controller/Common/CWarehouse_Controller_Base.cs",
            "TKS_Thuc_Tap_V11_Data_Access/Entity/Warehouse/CWarehousePagedResult.cs"
        };
        var hashes = sourcePaths.ToDictionary(
            path => path,
            path => File.Exists(Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)))
                ? HashFile(Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)))
                : "NOT_FOUND",
            StringComparer.Ordinal);
        var benchmarkAssembly = Assembly.GetExecutingAssembly().Location;
        var dataAccessAssembly = typeof(CWarehouseMasterSupport_Controller).Assembly.Location;
        return new SourceIdentityDocument
        {
            SourceFilesSha256 = hashes,
            BenchmarkAssemblyPath = benchmarkAssembly,
            BenchmarkAssemblySha256 = HashFile(benchmarkAssembly),
            DataAccessAssemblyPath = dataAccessAssembly,
            DataAccessAssemblySha256 = HashFile(dataAccessAssembly),
            DataAccessAssemblyName = typeof(CWarehouseMasterSupport_Controller).Assembly.GetName().Name
        };
    }

    private static MachineDocument CaptureMachineSnapshot()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        long? totalBytes = null;
        long? availableBytes = null;
        if (OperatingSystem.IsWindows() && GlobalMemoryStatusEx(ref memory))
        {
            totalBytes = checked((long)memory.TotalPhysical);
            availableBytes = checked((long)memory.AvailablePhysical);
        }
        else
        {
            var gcInfo = GC.GetGCMemoryInfo();
            if (gcInfo.TotalAvailableMemoryBytes > 0)
                totalBytes = gcInfo.TotalAvailableMemoryBytes;
        }

        return new MachineDocument
        {
            MachineName = Environment.MachineName,
            OS = RuntimeInformation.OSDescription,
            LogicalCpuCount = Environment.ProcessorCount,
            TotalRamMb = ToMb(totalBytes),
            AvailableRamMb = ToMb(availableBytes),
            CpuUsagePercent = null,
            DotNetRuntime = RuntimeInformation.FrameworkDescription
        };
    }

    private static string BuildMarkdownReport(RunDocument document, string resultsPath, string environmentPath, string rawCsvPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Warehouse Benchmark Lite — 10M Dataset");
        builder.AppendLine();
        builder.AppendLine("## 1. Mục tiêu");
        builder.AppendLine();
        builder.AppendLine("Đo source hiện tại qua các Warehouse Data Access controller trên Perf DB có dataset lớn, theo thứ tự tuần tự, và ghi latency thực tế. Đây là phép đo mô tả, không đặt SLA.");
        builder.AppendLine();
        builder.AppendLine("## 2. Environment");
        builder.AppendLine();
        builder.AppendLine($"- Repository: `{document.RepositoryPath}`");
        builder.AppendLine($"- Server: `{Escape(document.Server)}`; Database: `{Escape(document.Database)}`; DB_ID: `{document.DatabaseId}`; state: `{Escape(document.DatabaseState)}`");
        builder.AppendLine($"- Dataset primary detail rows: **{document.DatasetScale?.PrimaryDatasetRowCount.ToString("N0", CultureInfo.InvariantCulture) ?? "NOT VERIFIED"}**");
        if (document.DatasetScale is not null)
        {
            foreach (var table in document.DatasetScale.PrimaryTables)
                builder.AppendLine($"  - `{table.Key}`: {table.Value.ToString("N0", CultureInfo.InvariantCulture)}");
            foreach (var table in document.DatasetScale.HeaderTables)
                builder.AppendLine($"  - `{table.Key}` headers: {table.Value.ToString("N0", CultureInfo.InvariantCulture)}");
        }
        builder.AppendLine($"- Machine: `{document.Machine.MachineName}`; OS: `{Escape(document.Machine.OS)}`; logical CPU: {document.Machine.LogicalCpuCount}; RAM: {Format(document.Machine.TotalRamMb)} MB; available before run: {Format(document.Machine.AvailableRamMb)} MB; .NET: `{Escape(document.Machine.DotNetRuntime)}`");
        builder.AppendLine($"- Protocol: warmup {WarmupCount}; measured {MeasuredCount}; concurrency {Concurrency}; sequential scenario order; nearest-rank percentiles.");
        builder.AppendLine($"- Host admission: not used; heavy telemetry: not used; application command timeout override: none.");
        builder.AppendLine();
        builder.AppendLine("## 3. Scenario Results");
        builder.AppendLine();
        builder.AppendLine("| Scenario | Smoke | Success | Avg ms | P50 ms | P95 ms | Max ms | Errors | Verdict |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var scenario in document.Scenarios)
            builder.AppendLine($"| {scenario.Name} | {scenario.SmokeResult.Status} | {scenario.SuccessCount}/{MeasuredCount} | {Format(scenario.AverageMs)} | {Format(scenario.P50Ms)} | {Format(scenario.P95Ms)} | {Format(scenario.MaxMs)} | {scenario.Errors.Count} | {scenario.Verdict} |");
        builder.AppendLine();
        builder.AppendLine("## 4. Ranking");
        builder.AppendLine();
        var ranked = document.Scenarios.Where(item => item.AverageMs.HasValue).OrderByDescending(item => item.AverageMs).ToArray();
        if (ranked.Length == 0)
            builder.AppendLine("No scenario produced a successful measured sample.");
        else
            for (var index = 0; index < ranked.Length; index++)
                builder.AppendLine($"{index + 1}. `{ranked[index].Name}` — Average {Format(ranked[index].AverageMs)} ms; P95 {Format(ranked[index].P95Ms)} ms.");
        builder.AppendLine();
        builder.AppendLine("## 5. 10M Capability");
        builder.AppendLine();
        foreach (var scenario in document.Scenarios)
            builder.AppendLine($"- `{scenario.Name}` = **{scenario.Verdict}**");
        builder.AppendLine($"- `SOURCE_CURRENT_CAN_RUN_ON_10M_DB` = **{document.SourceCurrentCanRunOn10MDb ?? "NOT VERIFIED"}**");
        builder.AppendLine();
        builder.AppendLine("## 6. Hotspots");
        builder.AppendLine();
        foreach (var item in ranked.Take(3))
            builder.AppendLine($"- `{item.Name}` — Average {Format(item.AverageMs)} ms, P95 {Format(item.P95Ms)} ms.");
        if (ranked.Length == 0)
            builder.AppendLine("None; no successful measured calls.");
        builder.AppendLine();
        builder.AppendLine("## 7. Errors and scenario call details");
        builder.AppendLine();
        foreach (var scenario in document.Scenarios)
        {
            builder.AppendLine($"### {scenario.Name}");
            builder.AppendLine();
            builder.AppendLine($"- Application method: `{scenario.ApplicationMethod}`");
            builder.AppendLine($"- Stored procedure: `{scenario.StoredProcedure}`");
            builder.AppendLine($"- Parameters: `{JsonSerializer.Serialize(scenario.ParametersSummary)}`");
            if (scenario.Errors.Count == 0)
                builder.AppendLine("- Errors: none.");
            else
                foreach (var error in scenario.Errors)
                    builder.AppendLine($"- {error.Phase} #{error.Iteration}: {error.Type}; SQL={error.SqlErrorNumber?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}; timeout={error.TimedOut}; {Escape(error.Message)}");
            builder.AppendLine();
        }
        builder.AppendLine("## 8. Limitations");
        builder.AppendLine();
        builder.AppendLine("- One machine, one process, concurrency 1, ten measured calls per scenario.");
        builder.AppendLine("- Results represent this machine environment and do not guarantee production hardware performance.");
        builder.AppendLine("- Read workloads only; no writes, concurrency stress, mixed workload, or heavy SQL telemetry.");
        builder.AppendLine("- No performance SLA was defined; timings are reported as measured and are not labeled good or bad.");
        builder.AppendLine("- P50/P95 use nearest-rank with only ten samples; P95 therefore selects the highest successful sample.");
        builder.AppendLine("- Dataset scale uses SQL Server partition row-count metadata, not a full COUNT(*) scan or value-equality proof.");
        builder.AppendLine();
        builder.AppendLine("## 9. Run integrity");
        builder.AppendLine();
        builder.AppendLine($"- RunId: `{document.RunId}`; status: `{document.Status}`; BusinessDBWrites: **{document.BusinessDBWrites}**.");
        if (!string.IsNullOrWhiteSpace(document.HarnessFailureType))
            builder.AppendLine($"- Harness/preflight failure: `{document.HarnessFailureType}`; active scenario: `{Escape(document.ActiveScenario)}`; {Escape(document.PreflightFailure)}");
        builder.AppendLine($"- Results: `{resultsPath}`");
        builder.AppendLine($"- Environment: `{environmentPath}`");
        builder.AppendLine($"- Raw CSV: `{rawCsvPath}`");
        builder.AppendLine($"- Results represent this machine environment: **YES**; production hardware guarantee: **NO**.");
        return builder.ToString();
    }

    private static string GetOverallVerdict(IReadOnlyCollection<ScenarioResult> scenarios)
    {
        if (scenarios.Count == 6 && scenarios.All(item => item.Verdict == "PASS"))
            return "ALL_SCENARIOS_PASS";
        if (scenarios.Any(item => item.Verdict == "PASS"))
            return "PARTIAL";
        return "NO_SCENARIO_PASS";
    }

    private static string GetCapability(RunDocument document)
    {
        if (!string.Equals(document.Database, ExpectedDatabase, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(document.DatabaseState, "ONLINE", StringComparison.OrdinalIgnoreCase) ||
            (document.DatasetScale?.PrimaryDatasetRowCount ?? 0) <= 0)
            return "NO";
        return document.Scenarios.All(item => item.Verdict == "PASS") ? "YES" :
            document.Scenarios.Any(item => item.Verdict == "PASS") ? "PARTIAL" : "NO";
    }

    private static double NearestRank(IReadOnlyList<double> sorted, double percentile)
    {
        var index = Math.Max(0, (int)Math.Ceiling(percentile * sorted.Count) - 1);
        return sorted[index];
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException sqlException)
                return sqlException;
        return null;
    }

    private static ErrorDocument ToError(IterationDocument item) => new()
    {
        Phase = item.Phase,
        Iteration = item.Iteration,
        Type = item.ErrorType ?? "UnknownError",
        SqlErrorNumber = item.SqlErrorNumber,
        Message = item.ErrorMessage ?? "Unspecified call failure.",
        TimedOut = item.TimedOut
    };

    private static void AppendCsv(string path, IterationDocument item)
    {
        var fields = new[]
        {
            item.Scenario, item.Phase, item.Iteration.ToString(CultureInfo.InvariantCulture), item.StartedUtc.ToString("O", CultureInfo.InvariantCulture),
            item.DurationMs.ToString("F3", CultureInfo.InvariantCulture), item.Success.ToString(), item.CallSucceeded.ToString(),
            item.ReturnedRows?.ToString(CultureInfo.InvariantCulture) ?? "", item.TotalCount?.ToString(CultureInfo.InvariantCulture) ?? "",
            item.SqlErrorNumber?.ToString(CultureInfo.InvariantCulture) ?? "", item.ErrorType ?? "", item.ErrorMessage ?? "", item.TimedOut.ToString()
        };
        File.AppendAllText(path, string.Join(',', fields.Select(CsvEscape)) + "\r\n", new UTF8Encoding(false));
    }

    private static string CsvEscape(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static string Sanitize(string message)
    {
        var value = SecretPattern.Replace(message, "$1=<redacted>").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length <= 600 ? value : value[..600];
    }

    private static void AppendLog(string path, string message) =>
        File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {Sanitize(message)}{Environment.NewLine}", new UTF8Encoding(false));

    private static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static long? ToMb(long? bytes) => bytes.HasValue ? (long)Math.Round(bytes.Value / 1024d / 1024d, MidpointRounding.AwayFromZero) : null;
    private static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
    private static string Format(double? value) => value.HasValue ? value.Value.ToString("F3", CultureInfo.InvariantCulture) : "n/a";
    private static string Escape(string? value) => (value ?? "n/a").Replace("|", "\\|").Replace("`", "'");

    private static string? ReadOption(string[] args, string option)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        return null;
    }

    private static void PrintHelp() =>
        Console.WriteLine("Warehouse Benchmark Lite: BenchmarkLite.exe [--config <path>] [--help]. Default config is appsettings.Benchmark.json beside the executable.");

#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}

internal sealed record ScenarioDefinition(string Name, string ApplicationMethod, string StoredProcedure, object ParametersSummary);
internal sealed record CallResult(int ReturnedRows, int TotalCount);
internal sealed record DatabaseIdentity(string ServerName, string DatabaseName, int DatabaseId, string DatabaseState);
internal sealed record PreflightDocument(DatabaseIdentity Identity, DatasetDocument Dataset);

internal sealed class RunDocument
{
    public string RunId { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }
    public string Status { get; set; } = "CREATED";
    public string RepositoryPath { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public string? ConfigurationPath { get; set; }
    public string? ConfigurationSha256 { get; set; }
    public string? ConfiguredServer { get; set; }
    public string? ConfiguredDatabase { get; set; }
    public string? Server { get; set; }
    public string? Database { get; set; }
    public int? DatabaseId { get; set; }
    public string? DatabaseState { get; set; }
    public DatasetDocument? DatasetScale { get; set; }
    public string BuildConfiguration { get; set; } = "";
    public string? ProcessPath { get; set; }
    public MachineDocument Machine { get; set; } = new();
    public MachineDocument? MachineAfterRun { get; set; }
    public ProtocolDocument Protocol { get; set; } = new();
    public SourceIdentityDocument? SourceIdentity { get; set; }
    public List<ScenarioResult> Scenarios { get; set; } = [];
    public string? OverallVerdict { get; set; }
    public string? SourceCurrentCanRunOn10MDb { get; set; }
    public string? PreflightFailure { get; set; }
    public string? HarnessFailureType { get; set; }
    public string? ActiveScenario { get; set; }
    public int BusinessDBWrites { get; set; }
    public bool WriteMethodsInvoked { get; set; }
    public bool HostAdmissionUsed { get; set; }
    public bool HeavyTelemetryUsed { get; set; }
    public bool ConnectionStringPersisted { get; set; }
    public bool ResultsRepresentThisMachineEnvironment { get; set; }
    public bool ResultsAreNotProductionHardwareGuarantee { get; set; }
}

internal sealed class ScenarioResult
{
    public string Name { get; set; } = "";
    public string ApplicationMethod { get; set; } = "";
    public string StoredProcedure { get; set; } = "";
    public object ParametersSummary { get; set; } = new();
    public SmokeDocument SmokeResult { get; set; } = new();
    public int WarmupCount { get; set; }
    public int WarmupAttempts { get; set; }
    public string WarmupStatus { get; set; } = "NOT_RUN";
    public int MeasuredCount { get; set; }
    public int MeasuredAttempts { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public double? MinMs { get; set; }
    public double? AverageMs { get; set; }
    public double? P50Ms { get; set; }
    public double? P95Ms { get; set; }
    public double? MaxMs { get; set; }
    public List<IterationDocument> MeasuredResults { get; set; } = [];
    public List<int?> ReturnedRows { get; set; } = [];
    public List<int?> TotalCount { get; set; } = [];
    public List<ErrorDocument> Errors { get; set; } = [];
    public string Verdict { get; set; } = "NOT_RUN";
}

internal sealed class SmokeDocument
{
    public string Status { get; set; } = "NOT_RUN";
    public IterationDocument? Call { get; set; }
}

internal sealed class IterationDocument
{
    public string Scenario { get; set; } = "";
    public string Phase { get; set; } = "";
    public int Iteration { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public double DurationMs { get; set; }
    public bool Success { get; set; }
    public bool CallSucceeded { get; set; }
    public int? ReturnedRows { get; set; }
    public int? TotalCount { get; set; }
    public int? SqlErrorNumber { get; set; }
    public string? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
    public bool TimedOut { get; set; }
}

internal sealed class ErrorDocument
{
    public string Phase { get; set; } = "";
    public int Iteration { get; set; }
    public string Type { get; set; } = "";
    public int? SqlErrorNumber { get; set; }
    public string Message { get; set; } = "";
    public bool TimedOut { get; set; }
}

internal sealed class DatasetDocument
{
    public string CountSource { get; set; } = "";
    public Dictionary<string, long> PrimaryTables { get; set; } = [];
    public Dictionary<string, long> HeaderTables { get; set; } = [];
    public long PrimaryDatasetRowCount { get; set; }
}

internal sealed class MachineDocument
{
    public string MachineName { get; set; } = "";
    public string OS { get; set; } = "";
    public int LogicalCpuCount { get; set; }
    public long? TotalRamMb { get; set; }
    public long? AvailableRamMb { get; set; }
    public double? CpuUsagePercent { get; set; }
    public string DotNetRuntime { get; set; } = "";
}

internal sealed class ProtocolDocument
{
    public int WarmupCount { get; set; }
    public int MeasuredCount { get; set; }
    public int Concurrency { get; set; }
    public string TimingBoundary { get; set; } = "";
    public string PercentileMethod { get; set; } = "";
    public string CommandTimeoutOverride { get; set; } = "";
}

internal sealed class SourceIdentityDocument
{
    public Dictionary<string, string> SourceFilesSha256 { get; set; } = [];
    public string BenchmarkAssemblyPath { get; set; } = "";
    public string BenchmarkAssemblySha256 { get; set; } = "";
    public string DataAccessAssemblyPath { get; set; } = "";
    public string DataAccessAssemblySha256 { get; set; } = "";
    public string? DataAccessAssemblyName { get; set; }
}

internal sealed class EnvironmentDocument
{
    public string RunId { get; set; } = "";
    public MachineDocument Machine { get; set; } = new();
    public MachineDocument? MachineAfterRun { get; set; }
    public string? Server { get; set; }
    public string? Database { get; set; }
    public int? DatabaseId { get; set; }
    public string? DatabaseState { get; set; }
    public DatasetDocument? DatasetScale { get; set; }
    public string? PreflightFailure { get; set; }
    public bool ResultsRepresentThisMachineEnvironment { get; set; }
    public bool ResultsAreNotProductionHardwareGuarantee { get; set; }
}

internal sealed class TargetPreflightException(string message) : Exception(message);
