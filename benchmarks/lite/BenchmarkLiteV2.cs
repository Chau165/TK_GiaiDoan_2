using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using NBomber.CSharp;
using TKS_Thuc_Tap_V11_Data_Access.Controller.BC;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Controller.XNK;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

internal static partial class Program
{
    private const int LiteV2PageSize = 10;
    private static readonly DateTime LiteV2From = new(2025, 1, 1);
    private static readonly DateTime LiteV2To = new(2026, 12, 31);
    private static readonly string[] WorkloadProcedures =
    [
        "sp_DM_Master_Page", "sp_DM_Lookup_Page", "sp_XNK_Document_Page",
        "F9002_sp_Chi_Tiet_Nhap_Page", "F9002_sp_Xuat_Nhap_Ton_Page", "F9001_sp_Ton_Kho_Hien_Tai_Page"
    ];
    private static readonly string[] HistoricalSupportProcedures =
    ["sp_Inventory_Movement_Process_RebuildQueue", "sp_Inventory_Movement_Rebuild"];

    private static async Task<int> RunLiteV2Async(string[] args)
    {
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            PrintLiteV2Help();
            return 0;
        }

        var options = ParseLiteV2Options(args);
        var startedUtc = DateTimeOffset.UtcNow;
        var runId = $"WHB-LITE2-{startedUtc:yyyyMMdd'T'HHmmss'Z'}-{Guid.NewGuid():N}"[..47];
        var repositoryRoot = FindRepositoryRoot();
        var outputDirectory = Path.Combine(repositoryRoot, "benchmarks", "lite", "results", runId);
        Directory.CreateDirectory(Path.GetDirectoryName(outputDirectory)!);
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
            throw new IOException("Lite V2 output paths are never reused.");
        Directory.CreateDirectory(outputDirectory);

        var paths = LiteV2Paths.Create(outputDirectory);
        InitializeLiteV2Artifacts(paths);
        var run = new LiteV2RunDocument
        {
            RunId = runId,
            StartedUtc = startedUtc,
            RepositoryPath = repositoryRoot,
            OutputDirectory = outputDirectory,
            Mode = options.Mode,
            Options = options,
            BuildConfiguration = BuildConfiguration,
            ProcessPath = Environment.ProcessPath,
            MachineBefore = CaptureMachineSnapshot(),
            SourceIdentity = CaptureSourceIdentity(repositoryRoot),
            MeasurementBusinessDbWrites = 0,
            FullBatch4CanonicalPath = "STOPPED_BY_OWNER_SCOPE_CHANGE",
            ResultsRepresentThisMachineEnvironment = true,
            ResultsAreNotProductionHardwareGuarantee = true
        };
        var readiness = new HistoricalReadinessDocument { Status = "NOT_EVALUATED" };
        WriteJson(paths.Results, run);
        WriteJson(paths.Readiness, readiness);
        AppendLog(paths.Log, $"Lite V2 run {runId} created; mode={options.Mode}; no existing run directory reused.");

        try
        {
            var configPath = ReadOption(args, "--config") ?? Path.Combine(AppContext.BaseDirectory, "appsettings.Benchmark.json");
            var connectionString = LoadConnectionString(configPath);
            var connection = ValidateConnectionConfiguration(connectionString);
            run.ConfigurationPath = Path.GetFullPath(configPath);
            run.ConfigurationSha256 = HashFile(configPath);
            run.ConfiguredServer = connection.DataSource;
            run.ConfiguredDatabase = connection.InitialCatalog;
            run.Status = "PREFLIGHT";
            WriteJson(paths.Results, run);

            var preflight = await ReadTargetAndDatasetAsync(connectionString).ConfigureAwait(false);
            run.Server = preflight.Identity.ServerName;
            run.Database = preflight.Identity.DatabaseName;
            run.DatabaseId = preflight.Identity.DatabaseId;
            run.DatabaseState = preflight.Identity.DatabaseState;
            run.DatasetScale = preflight.Dataset;
            if (preflight.Identity.DatabaseId != 5)
                throw new TargetPreflightException($"DB_ID() mismatch: expected 5; observed {preflight.Identity.DatabaseId}.");
            if (preflight.Dataset.PrimaryDatasetRowCount <= 0)
                throw new TargetPreflightException("Expected detail-table partition metadata did not return a positive combined row count.");

            CConfig.TKS_Thuc_Tap_V11_Conn_String = connectionString;
            CLogger.Enable_Trace = false;
            WriteJson(paths.Environment, new LiteV2EnvironmentDocument
            {
                RunId = runId,
                MachineBefore = run.MachineBefore,
                Server = run.Server,
                Database = run.Database,
                DatabaseId = run.DatabaseId,
                DatabaseState = run.DatabaseState,
                DatasetScale = run.DatasetScale,
                ConfigurationSha256 = run.ConfigurationSha256,
                SourceIdentity = run.SourceIdentity
            });
            AppendLog(paths.Log, $"Target identity PASS; DB_ID={run.DatabaseId}; dataset metadata rows={preflight.Dataset.PrimaryDatasetRowCount.ToString(CultureInfo.InvariantCulture)}.");

            run.ProcedureParity = (await VerifyProcedureParityAsync(repositoryRoot, connectionString).ConfigureAwait(false)).ToList();
            WriteJson(paths.Results, run);
            var workloadParityFailed = run.ProcedureParity.Where(item => WorkloadProcedures.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
                .Any(item => item.Status != "PASS");
            if (workloadParityFailed)
                throw new TargetPreflightException("One or more live workload stored procedures do not match the repository source; workload execution is blocked.");

            if (options.PreflightOnly)
            {
                run.Status = "PREFLIGHT_ONLY_COMPLETED";
                run.OverallVerdict = "PREFLIGHT_ONLY";
                run.FinishedUtc = DateTimeOffset.UtcNow;
                run.MachineAfter = CaptureMachineSnapshot();
                WriteLiteV2FinalArtifacts(paths, run);
                AppendLog(paths.Log, "Preflight-only completed; no historical worker or workload call was executed.");
                Console.WriteLine($"Preflight PASS; no workload ran. RunId: {run.RunId}");
                return 0;
            }

            readiness = await QualifyHistoricalReadinessAsync(repositoryRoot, connectionString, paths.Readiness, paths.Log, run.ProcedureParity)
                .ConfigureAwait(false);
            run.HistoricalReadiness = readiness;
            WriteJson(paths.Results, run);

            if (options.Mode is "sequential" or "all")
            {
                run.SequentialStatus = "IN_PROGRESS";
                run.SequentialCases = (await RunSequentialSuiteAsync(paths, readiness, options.MeasuredCount, run).ConfigureAwait(false)).ToList();
                run.SequentialStatus = run.SequentialCases.Count == 6 && run.SequentialCases.All(item => item.Verdict == "PASS")
                    ? "PASS" : "PARTIAL";
                WriteJson(paths.Results, run);

                run.MatrixStatus = "IN_PROGRESS";
                run.MatrixCases = (await RunMatrixSuiteAsync(paths, readiness, options.MatrixMeasuredCount, run).ConfigureAwait(false)).ToList();
                run.MatrixStatus = run.MatrixCases.Any(item => item.Verdict == "FAIL" || item.Verdict == "TIMEOUT")
                    ? "PARTIAL" : "COMPLETED_WITH_SKIPS_ALLOWED";
                WriteJson(paths.Results, run);
            }

            if (options.Mode is "load" or "all")
            {
                run.LoadStatus = "IN_PROGRESS";
                run.LoadStages = (await RunLoadSuiteAsync(paths, readiness, options, run).ConfigureAwait(false)).ToList();
                run.LoadStatus = run.LoadStages.Count > 0 && run.LoadStages.All(item => item.Verdict == "PASS")
                    ? "PASS" : "PARTIAL";
                WriteJson(paths.LoadResults, run.LoadStages);
                WriteJson(paths.Results, run);
            }

            run.FinishedUtc = DateTimeOffset.UtcNow;
            run.MachineAfter = CaptureMachineSnapshot();
            run.Status = "COMPLETED";
            run.OverallVerdict = GetLiteV2OverallVerdict(run);
            WriteLiteV2FinalArtifacts(paths, run);
            AppendLog(paths.Log, $"Completed; verdict={run.OverallVerdict}; measurement business DB writes=0; preparation worker calls={readiness.WorkerCalls}.");
            Console.WriteLine($"Lite V2 completed: {run.OverallVerdict}");
            Console.WriteLine($"RunId: {run.RunId}");
            Console.WriteLine($"Report: {paths.Report}");
            return run.OverallVerdict == "PASS" ? 0 : 2;
        }
        catch (TargetPreflightException exception)
        {
            run.Status = "TARGET_PREFLIGHT_FAILED";
            run.HarnessFailureType = nameof(TargetPreflightException);
            run.PreflightFailure = Sanitize(exception.Message);
            run.OverallVerdict = "TARGET_PREFLIGHT_FAILED";
            run.FinishedUtc = DateTimeOffset.UtcNow;
            run.MachineAfter = CaptureMachineSnapshot();
            WriteLiteV2FinalArtifacts(paths, run);
            AppendLog(paths.Log, $"STOP before measurement: {run.PreflightFailure}");
            Console.Error.WriteLine($"Lite V2 stopped before measurement. {run.PreflightFailure}");
            return 2;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            run.Status = "HARNESS_FAILED";
            run.HarnessFailureType = exception.GetType().Name;
            run.PreflightFailure = Sanitize(exception.Message);
            run.OverallVerdict = "HARNESS_FAILED";
            run.FinishedUtc = DateTimeOffset.UtcNow;
            run.MachineAfter = CaptureMachineSnapshot();
            WriteLiteV2FinalArtifacts(paths, run);
            AppendLog(paths.Log, $"Harness failed: {run.HarnessFailureType}: {run.PreflightFailure}");
            Console.Error.WriteLine($"Lite V2 harness failed: {run.HarnessFailureType}: {run.PreflightFailure}");
            return 1;
        }
    }

    private static LiteV2Options ParseLiteV2Options(string[] args)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "--mode", "--config", "--measured-count", "--matrix-measured-count", "--load-seconds", "--load-warmup-seconds", "--users", "--preflight-only", "--help", "-h" };
        for (var index = 0; index < args.Length; index++)
        {
            if (!known.Contains(args[index]))
                throw new ArgumentException($"Unknown argument: {args[index]}");
            if (args[index] is "--help" or "-h" or "--preflight-only")
                continue;
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Option {args[index]} requires a value.");
            index++;
        }

        var mode = ReadOption(args, "--mode") ?? "all";
        if (mode is not ("all" or "sequential" or "load"))
            throw new ArgumentException("--mode must be all, sequential, or load.");
        var measured = ReadIntegerOption(args, "--measured-count", 30, 1, 300);
        var matrixMeasured = ReadIntegerOption(args, "--matrix-measured-count", 10, 1, 100);
        var loadSeconds = ReadIntegerOption(args, "--load-seconds", 30, 1, 300);
        var warmupSeconds = ReadIntegerOption(args, "--load-warmup-seconds", 3, 0, 60);
        var usersText = ReadOption(args, "--users") ?? "1,2,4";
        var users = usersText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed : throw new ArgumentException($"Invalid --users value: {value}"))
            .Distinct().Order().ToArray();
        if (users.Length == 0 || users.Any(value => value is not (1 or 2 or 4)))
            throw new ArgumentException("--users may contain only 1, 2, and/or 4.");
        return new LiteV2Options(mode, measured, matrixMeasured, loadSeconds, warmupSeconds, users,
            args.Contains("--preflight-only", StringComparer.OrdinalIgnoreCase));
    }

    private static int ReadIntegerOption(string[] args, string option, int fallback, int minimum, int maximum)
    {
        var value = ReadOption(args, option);
        if (value is null)
            return fallback;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < minimum || parsed > maximum)
            throw new ArgumentException($"{option} must be an integer in {minimum}..{maximum}.");
        return parsed;
    }

    private static void InitializeLiteV2Artifacts(LiteV2Paths paths)
    {
        File.WriteAllText(paths.SequentialCsv,
            "RunId,Scenario,Case,Phase,Iteration,StartedUtc,DurationMs,CallSucceeded,Valid,ReturnedRows,TotalCount,SqlErrorNumber,ErrorCategory,ExceptionType,ErrorMessage,TimedOut\r\n",
            new UTF8Encoding(false));
        File.WriteAllText(paths.MatrixCsv,
            "RunId,Scenario,Case,Phase,Iteration,PageNumber,PageSize,SearchText,FromDate,ToDate,StartedUtc,DurationMs,CallSucceeded,Valid,ReturnedRows,TotalCount,SqlErrorNumber,ErrorCategory,ExceptionType,ErrorMessage,TimedOut\r\n",
            new UTF8Encoding(false));
        File.WriteAllText(paths.LoadCsv,
            "RunId,Scenario,Case,Users,ConfiguredCopies,StartedUtc,MeasuredSeconds,WarmupSeconds,WarmupOkRequests,WarmupFailedRequests,OkRequests,FailedRequests,Rps,LatencyMeanMs,LatencyP95Ms,LatencyP99Ms,LatencyMaxMs,ErrorCategories,Verdict\r\n",
            new UTF8Encoding(false));
    }

    private static async Task<IReadOnlyList<ProcedureParityDocument>> VerifyProcedureParityAsync(string repositoryRoot, string connectionString)
    {
        var localPath = Path.Combine(repositoryRoot, "Database", "WarehouseModule.Procedures.sql");
        if (!File.Exists(localPath))
            throw new TargetPreflightException("Repository procedure source file is missing; live parity cannot be verified.");
        var sourceText = await File.ReadAllTextAsync(localPath).ConfigureAwait(false);
        var required = WorkloadProcedures.Concat(HistoricalSupportProcedures).ToArray();
        var sourceDefinitions = required.ToDictionary(name => name, name => ExtractProcedureDefinition(sourceText, name), StringComparer.OrdinalIgnoreCase);
        var results = new List<ProcedureParityDocument>(required.Length);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        foreach (var name in required)
        {
            var sourceDefinition = sourceDefinitions[name];
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 10;
            command.CommandText = "SELECT m.definition FROM sys.sql_modules AS m JOIN sys.objects AS o ON o.object_id = m.object_id WHERE o.schema_id = SCHEMA_ID(N'dbo') AND o.name = @Name;";
            command.Parameters.AddWithValue("@Name", name);
            var liveValue = await command.ExecuteScalarAsync().ConfigureAwait(false);
            var liveDefinition = liveValue is DBNull or null ? null : Convert.ToString(liveValue, CultureInfo.InvariantCulture);
            var liveBody = liveDefinition is null ? null : ExtractProcedureBody(liveDefinition, name);
            var sourceNormalized = sourceDefinition is null ? null : NormalizeSqlBody(sourceDefinition);
            var liveNormalized = liveBody is null ? null : NormalizeSqlBody(liveBody);
            var sourceHash = sourceNormalized is null ? null : HashText(sourceNormalized);
            var liveHash = liveNormalized is null ? null : HashText(liveNormalized);
            var difference = sourceNormalized is null || liveNormalized is null ? -1 : FirstDifferenceIndex(sourceNormalized, liveNormalized);
            var firstDifference = difference < 0 ? (int?)null : difference;
            results.Add(new ProcedureParityDocument
            {
                Name = name,
                Role = WorkloadProcedures.Contains(name, StringComparer.OrdinalIgnoreCase) ? "WORKLOAD" : "HISTORICAL_PREPARATION_SUPPORT",
                SourceDefinitionSha256 = sourceHash,
                LiveDefinitionSha256 = liveHash,
                Status = sourceNormalized is not null && liveNormalized is not null &&
                         string.Equals(sourceNormalized, liveNormalized, StringComparison.Ordinal)
                    ? "PASS" : "NOT_VERIFIED",
                SourceNormalizedLength = sourceNormalized?.Length,
                LiveNormalizedLength = liveNormalized?.Length,
                FirstDifferenceIndex = firstDifference,
                SourceDifferenceContext = firstDifference.HasValue ? SqlDifferenceContext(sourceNormalized!, firstDifference.Value) : null,
                LiveDifferenceContext = firstDifference.HasValue ? SqlDifferenceContext(liveNormalized!, firstDifference.Value) : null,
                Note = sourceNormalized is null ? "Procedure was not found in source script." :
                    liveDefinition is null ? "Live definition is not visible or procedure is absent." :
                    liveNormalized is null ? "Live procedure header/body could not be parsed." : null
            });
        }
        return results;
    }

    private static string? ExtractProcedureDefinition(string script, string name)
    {
        var escapedName = Regex.Escape(name);
        foreach (var batch in Regex.Split(script, @"(?im)^[ \t]*GO[ \t]*(?:--[^\r\n]*)?\r?$").Where(value => value.Length > 0))
        {
            var body = ExtractProcedureBody(batch, name);
            if (body is not null)
                return body;
        }
        return null;
    }

    private static string? ExtractProcedureBody(string definition, string name)
    {
        var escapedName = Regex.Escape(name);
        var header = Regex.Match(definition,
            $@"(?im)^\s*CREATE\s+(?:OR\s+ALTER\s+)?PROC(?:EDURE)?\s+(?:\[dbo\]|dbo)\s*\.\s*\[?{escapedName}\]?(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant);
        if (!header.Success)
            return null;
        var asIndex = FindProcedureAsToken(definition, header.Index + header.Length);
        return asIndex < 0 ? null : definition[(asIndex + 2)..];
    }

    private static int FindProcedureAsToken(string sql, int start)
    {
        var parenthesisDepth = 0;
        for (var index = start; index < sql.Length; index++)
        {
            var current = sql[index];
            if (current == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                index += 2;
                while (index < sql.Length && sql[index] is not ('\r' or '\n')) index++;
                continue;
            }
            if (current == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                index += 2;
                while (index + 1 < sql.Length && !(sql[index] == '*' && sql[index + 1] == '/')) index++;
                index++;
                continue;
            }
            if (current == '\'')
            {
                while (++index < sql.Length)
                    if (sql[index] == '\'' && index + 1 < sql.Length && sql[index + 1] == '\'') index++;
                    else if (sql[index] == '\'') break;
                continue;
            }
            if (current == '[')
            {
                while (++index < sql.Length)
                    if (sql[index] == ']' && index + 1 < sql.Length && sql[index + 1] == ']') index++;
                    else if (sql[index] == ']') break;
                continue;
            }
            if (current == '"')
            {
                while (++index < sql.Length)
                    if (sql[index] == '"' && index + 1 < sql.Length && sql[index + 1] == '"') index++;
                    else if (sql[index] == '"') break;
                continue;
            }
            if (current == '(')
            {
                parenthesisDepth++;
                continue;
            }
            if (current == ')')
            {
                parenthesisDepth = Math.Max(0, parenthesisDepth - 1);
                continue;
            }
            if (parenthesisDepth == 0 && (current is 'A' or 'a') && index + 1 < sql.Length && (sql[index + 1] is 'S' or 's') &&
                (index == 0 || !IsSqlWordCharacter(sql[index - 1])) && (index + 2 >= sql.Length || !IsSqlWordCharacter(sql[index + 2])))
                return index;
        }
        return -1;
    }

    private static bool IsSqlWordCharacter(char value) => char.IsLetterOrDigit(value) || value is '_' or '@' or '$' or '#';

    private static string NormalizeSqlBody(string sql)
    {
        var output = new StringBuilder(sql.Length);
        var pendingSpace = false;
        for (var index = 0; index < sql.Length; index++)
        {
            var current = sql[index];
            if (current == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                index += 2;
                while (index < sql.Length && sql[index] is not ('\r' or '\n')) index++;
                pendingSpace = true;
                continue;
            }
            if (current == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                index += 2;
                while (index + 1 < sql.Length && !(sql[index] == '*' && sql[index + 1] == '/')) index++;
                index++;
                pendingSpace = true;
                continue;
            }
            if (char.IsWhiteSpace(current))
            {
                pendingSpace = true;
                continue;
            }
            if (pendingSpace && output.Length > 0 && output[^1] != ' ')
                output.Append(' ');
            pendingSpace = false;
            output.Append(current);
            if (current == '\'')
            {
                while (++index < sql.Length)
                {
                    output.Append(sql[index]);
                    if (sql[index] == '\'' && index + 1 < sql.Length && sql[index + 1] == '\'')
                        output.Append(sql[++index]);
                    else if (sql[index] == '\'')
                        break;
                }
            }
            else if (current == '[')
            {
                while (++index < sql.Length)
                {
                    output.Append(sql[index]);
                    if (sql[index] == ']' && index + 1 < sql.Length && sql[index + 1] == ']')
                        output.Append(sql[++index]);
                    else if (sql[index] == ']')
                        break;
                }
            }
            else if (current == '"')
            {
                while (++index < sql.Length)
                {
                    output.Append(sql[index]);
                    if (sql[index] == '"' && index + 1 < sql.Length && sql[index + 1] == '"')
                        output.Append(sql[++index]);
                    else if (sql[index] == '"')
                        break;
                }
            }
        }
        return output.ToString().Trim().TrimEnd(';').Trim();
    }

    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static int FirstDifferenceIndex(string left, string right)
    {
        var limit = Math.Min(left.Length, right.Length);
        for (var index = 0; index < limit; index++)
            if (left[index] != right[index])
                return index;
        return left.Length == right.Length ? -1 : limit;
    }

    private static string SqlDifferenceContext(string value, int index)
    {
        var start = Math.Max(0, index - 55);
        var length = Math.Min(value.Length - start, 130);
        var excerpt = value.Substring(start, length);
        return Regex.Replace(excerpt, @"'(?:''|[^'])*'", "'<literal>'", RegexOptions.CultureInvariant);
    }

    private static async Task<HistoricalReadinessDocument> QualifyHistoricalReadinessAsync(
        string repositoryRoot, string connectionString, string evidencePath, string logPath,
        IReadOnlyList<ProcedureParityDocument> parity)
    {
        var result = new HistoricalReadinessDocument { Status = "QUALIFYING" };
        try
        {
            result.SourceQueueWorkerParity = ParityPass(parity, "sp_Inventory_Movement_Process_RebuildQueue");
            result.SourceRebuildParity = ParityPass(parity, "sp_Inventory_Movement_Rebuild");
            result.PreState = await ReadHistoricalSnapshotAsync(connectionString).ConfigureAwait(false);
            result.PreStateFingerprint = FingerprintHistoricalSnapshot(result.PreState);
            result.Status = "BLOCKED_BEFORE_PREPARATION";
            result.Reason = EvaluateHistoricalBlock(result);
            WriteJson(evidencePath, result);
            AppendLog(logPath, $"Historical pre-state persisted; ready-check={result.Reason ?? "PASS"}; fingerprint={result.PreStateFingerprint}.");

            if (result.Reason is not null)
                return result;
            var waiting = result.PreState!.NonTerminalRows.SingleOrDefault(row => row.Status == "WAITING");
            if (waiting is null)
            {
                result.Status = "READY_NO_PREPARATION_NEEDED";
                result.Reason = null;
                WriteJson(evidencePath, result);
                return result;
            }

            result.ImmediatePreState = await ReadHistoricalSnapshotAsync(connectionString).ConfigureAwait(false);
            result.ImmediatePreStateFingerprint = FingerprintHistoricalSnapshot(result.ImmediatePreState);
            if (!string.Equals(result.PreStateFingerprint, result.ImmediatePreStateFingerprint, StringComparison.Ordinal))
            {
                result.Status = "BLOCKED_STATE_CHANGED_BEFORE_PREPARATION";
                result.Reason = "Queue or aggregate state changed after the persisted pre-state; worker was not invoked.";
                WriteJson(evidencePath, result);
                return result;
            }

            WriteJson(evidencePath, result);
            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync().ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.CommandTimeout = 60;
                command.CommandText = "EXEC dbo.sp_Inventory_Movement_Process_RebuildQueue @Batch_Size = 1, @Max_Retry_Count = 3, @Base_Retry_Delay_Seconds = 5, @Processing_Lease_Seconds = 300;";
                result.WorkerCalls = 1;
                result.WorkerStartedUtc = DateTimeOffset.UtcNow;
                WriteJson(evidencePath, result);
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                result.WorkerFinishedUtc = DateTimeOffset.UtcNow;
            }

            result.PostState = await ReadHistoricalSnapshotAsync(connectionString, waiting.Id).ConfigureAwait(false);
            var processed = result.PostState.NonTerminalRows.Any(row => row.Id == waiting.Id && row.Status == "COMPLETED");
            var postBlock = EvaluateHistoricalSnapshotBlock(result.PostState);
            result.Status = processed && postBlock is null ? "READY_AFTER_BOUNDED_PREPARATION" : "BLOCKED_AFTER_PREPARATION";
            result.Reason = processed ? postBlock : $"The selected queue row {waiting.Id} did not reach COMPLETED status.";
            result.PostStateFingerprint = FingerprintHistoricalSnapshot(result.PostState);
            WriteJson(evidencePath, result);
            AppendLog(logPath, $"Historical worker call finished; status={result.Status}; queue row={waiting.Id}; post={result.Reason ?? "PASS"}.");
            return result;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            result.Status = result.WorkerCalls == 0 ? "BLOCKED_READINESS_ERROR" : "PREPARATION_FAILED";
            result.Reason = Sanitize($"{exception.GetType().Name}: {exception.Message}");
            if (result.WorkerCalls > 0)
            {
                try
                {
                    result.PostState = await ReadHistoricalSnapshotAsync(connectionString).ConfigureAwait(false);
                    result.PostStateFingerprint = FingerprintHistoricalSnapshot(result.PostState);
                }
                catch (Exception postException) when (postException is not OutOfMemoryException)
                {
                    result.PostStateReadError = Sanitize($"{postException.GetType().Name}: {postException.Message}");
                }
            }
            WriteJson(evidencePath, result);
            AppendLog(logPath, $"Historical readiness/preparation blocked: {result.Status}; {result.Reason}");
            return result;
        }
    }

    private static bool ParityPass(IReadOnlyList<ProcedureParityDocument> parity, string name) =>
        parity.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase) && item.Status == "PASS");

    private static string FingerprintHistoricalSnapshot(HistoricalStateSnapshot snapshot) => HashText(JsonSerializer.Serialize(new
    {
        snapshot.MovementAggregateInitialized,
        snapshot.BalanceAggregateInitialized,
        snapshot.DeadLetterCount,
        snapshot.UnknownStatusCount,
        StatusCounts = snapshot.StatusCounts.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase),
        snapshot.NonTerminalRows,
        snapshot.EarliestAuthorizedBalanceDate
    }));

    private static string? EvaluateHistoricalBlock(HistoricalReadinessDocument result)
    {
        if (!result.SourceQueueWorkerParity || !result.SourceRebuildParity)
            return "Historical worker/rebuild live definitions do not match repository source; preparation and historical workload are blocked.";
        return result.PreState is null ? "Historical state snapshot is unavailable." : EvaluateHistoricalSnapshotBlock(result.PreState);
    }

    private static string? EvaluateHistoricalSnapshotBlock(HistoricalStateSnapshot snapshot)
    {
        if (!snapshot.MovementAggregateInitialized || !snapshot.BalanceAggregateInitialized)
            return "Movement or daily-balance aggregate state is not initialized.";
        if (snapshot.DeadLetterCount != 0)
            return $"Dead-letter rows exist ({snapshot.DeadLetterCount}); do not invoke the global worker.";
        if (snapshot.StatusCounts.GetValueOrDefault("PROCESSING") != 0)
            return "A queue row is PROCESSING; global worker recovery/claim behavior is unsafe for this run.";
        if (snapshot.StatusCounts.GetValueOrDefault("RETRY_WAITING") != 0)
            return "RETRY_WAITING rows exist; the global worker could process unrelated work.";
        if (snapshot.StatusCounts.GetValueOrDefault("FAILED_FINAL") != 0)
            return "FAILED_FINAL rows exist; worker state requires separate operational review.";
        if (snapshot.UnknownStatusCount != 0)
            return $"Unknown queue statuses exist ({snapshot.UnknownStatusCount}).";
        var waiting = snapshot.NonTerminalRows.Where(row => row.Status == "WAITING").ToArray();
        if (waiting.Length > 1)
            return $"More than one global WAITING row exists ({waiting.Length}); bounded worker could process unrelated work.";
        if (waiting.Length == 1 && !waiting[0].RelevantToReport)
            return "The only WAITING row is outside the PERF_USER historical-report scope.";
        return null;
    }

    private static async Task<HistoricalStateSnapshot> ReadHistoricalSnapshotAsync(string connectionString, long? trackedQueueId = null)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        var snapshot = new HistoricalStateSnapshot { CapturedUtc = DateTimeOffset.UtcNow };

        await using (var stateCommand = connection.CreateCommand())
        {
            stateCommand.CommandTimeout = 10;
            stateCommand.CommandText = "SELECT State_ID, IsInitialized FROM dbo.InventoryMovement_AggregateState WHERE State_ID = 1; SELECT State_ID, IsInitialized FROM dbo.InventoryBalance_Daily_AggregateState WHERE State_ID = 1;";
            await using var reader = await stateCommand.ExecuteReaderAsync().ConfigureAwait(false);
            if (await reader.ReadAsync().ConfigureAwait(false))
                snapshot.MovementAggregateInitialized = reader.GetBoolean(1);
            if (await reader.NextResultAsync().ConfigureAwait(false) && await reader.ReadAsync().ConfigureAwait(false))
                snapshot.BalanceAggregateInitialized = reader.GetBoolean(1);
        }

        await using (var queueCommand = connection.CreateCommand())
        {
            queueCommand.CommandTimeout = 10;
            queueCommand.CommandText = """
                SELECT q.Status, COUNT_BIG(*)
                FROM dbo.InventoryMovement_RebuildQueue AS q
                GROUP BY q.Status;

                SELECT q.ID, q.Kho_ID, q.San_Pham_ID, q.From_Date, q.To_Date, q.Status,
                       q.Retry_Count, q.Requested_Version, q.Claimed_Version, q.LastAttemptAt, q.NextRetryAt,
                       CONVERT(bit, CASE WHEN q.From_Date <= @ReportTo AND EXISTS
                           (SELECT 1 FROM dbo.tbl_DM_Kho_User AS ku
                            WHERE ku.Kho_ID = q.Kho_ID AND ku.Ma_Dang_Nhap = @Login)
                           THEN 1 ELSE 0 END) AS RelevantToReport
                FROM dbo.InventoryMovement_RebuildQueue AS q
                WHERE q.Status NOT IN (N'COMPLETED', N'SUPERSEDED') OR q.ID = @TrackedQueueId
                ORDER BY q.ID;

                SELECT COUNT_BIG(*) FROM dbo.InventoryMovement_RebuildDeadLetter;

                SELECT MIN(s.First_Balance_Date)
                FROM dbo.Inventory_Balance_Daily_Scope AS s
                WHERE s.First_Balance_Date <= @ReportTo
                  AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User AS ku
                              WHERE ku.Kho_ID = s.Kho_ID AND ku.Ma_Dang_Nhap = @Login);
                """;
            queueCommand.Parameters.AddWithValue("@ReportTo", LiteV2To.Date);
            queueCommand.Parameters.AddWithValue("@Login", ApplicationLogin);
            queueCommand.Parameters.Add(new SqlParameter("@TrackedQueueId", System.Data.SqlDbType.BigInt)
            {
                Value = trackedQueueId.HasValue ? trackedQueueId.Value : DBNull.Value
            });
            await using var reader = await queueCommand.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
                snapshot.StatusCounts[reader.GetString(0)] = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
            if (await reader.NextResultAsync().ConfigureAwait(false))
            {
                while (await reader.ReadAsync().ConfigureAwait(false))
                    snapshot.NonTerminalRows.Add(new HistoricalQueueRow
                    {
                        Id = reader.GetInt64(0),
                        WarehouseId = reader.GetInt64(1),
                        ProductId = reader.GetInt64(2),
                        FromDate = reader.GetDateTime(3).Date,
                        ToDate = reader.GetDateTime(4).Date,
                        Status = reader.GetString(5),
                        RetryCount = reader.GetInt32(6),
                        RequestedVersion = reader.GetInt32(7),
                        ClaimedVersion = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                        LastAttemptUtc = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                        NextRetryUtc = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                        RelevantToReport = reader.GetBoolean(11)
                    });
            }
            if (await reader.NextResultAsync().ConfigureAwait(false) && await reader.ReadAsync().ConfigureAwait(false))
                snapshot.DeadLetterCount = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
            if (await reader.NextResultAsync().ConfigureAwait(false) && await reader.ReadAsync().ConfigureAwait(false) && !reader.IsDBNull(0))
                snapshot.EarliestAuthorizedBalanceDate = reader.GetDateTime(0).Date;
        }

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "WAITING", "PROCESSING", "RETRY_WAITING", "FAILED_FINAL", "COMPLETED", "SUPERSEDED" };
        snapshot.UnknownStatusCount = snapshot.StatusCounts.Where(item => !known.Contains(item.Key)).Sum(item => item.Value);
        return snapshot;
    }

    private static async Task<IReadOnlyList<LiteScenarioResult>> RunSequentialSuiteAsync(LiteV2Paths paths, HistoricalReadinessDocument readiness, int measuredCount, LiteV2RunDocument run)
    {
        var results = new List<LiteScenarioResult>(6);
        foreach (var baseDefinition in CreateLiteSequentialCases())
        {
            var definition = baseDefinition with { RunId = paths.RunId };
            if (definition.Scenario == "InventoryHistoricalReportPaged" && !HistoricalReady(readiness))
            {
                results.Add(new LiteScenarioResult
                {
                    Scenario = definition.Scenario,
                    ApplicationMethod = definition.ApplicationMethod,
                    StoredProcedure = definition.StoredProcedure,
                    CaseName = definition.CaseName,
                    Parameters = definition.ParametersSummary,
                    Verdict = "SKIPPED_READINESS_BLOCKED",
                    SkipReason = readiness.Reason ?? readiness.Status
                });
                run.SequentialCases = results.ToList();
                WriteJson(paths.Results, run);
                AppendLog(paths.Log, $"{definition.Scenario}: skipped; historical readiness={readiness.Status}.");
                continue;
            }

            var result = await RunMeasuredCaseAsync(definition, paths.SequentialCsv, paths.Log,
                matrix: false, measuredCount, warmupCount: 3).ConfigureAwait(false);
            results.Add(result);
            run.SequentialCases = results.ToList();
            WriteJson(paths.Results, run);
            AppendLog(paths.Log, $"Sequential {definition.Scenario}: {result.Verdict}; {result.SuccessfulMeasuredCalls}/{result.MeasuredCalls}; mean={Format(result.MeanMs)} ms.");
            Console.WriteLine($"Sequential {definition.Scenario}: {result.Verdict}; {result.SuccessfulMeasuredCalls}/{result.MeasuredCalls}; mean={Format(result.MeanMs)} ms.");
        }
        return results;
    }

    private static IReadOnlyList<LiteCaseDefinition> CreateLiteSequentialCases() =>
    [
        new("MasterPaged", "MasterPaged", "CWarehouseMasterSupport_Controller.List_Master_Page_Async", "dbo.sp_DM_Master_Page", 1, LiteV2PageSize, "", LiteV2From, LiteV2To,
            new { MasterType = "SanPham", PageNumber = 1, PageSize = LiteV2PageSize, SearchText = "" }),
        new("LookupPaged", "LookupPaged", "CWarehouseMasterSupport_Controller.List_Lookup_Page_Async", "dbo.sp_DM_Lookup_Page", 1, LiteV2PageSize, "", LiteV2From, LiteV2To,
            new { MasterType = "SanPham", PageNumber = 1, PageSize = LiteV2PageSize, SearchText = "" }),
        new("DocumentPaged", "DocumentPaged", "CWarehouseDocumentSupport_Controller.sp_XNK_Document_Page", "dbo.sp_XNK_Document_Page", 1, LiteV2PageSize, "", LiteV2From, LiteV2To,
            new { IsReceipt = true, PageNumber = 1, PageSize = LiteV2PageSize, SearchText = "", Login = ApplicationLogin, WarehouseId = (long?)null }),
        new("DetailReportPaged", "DetailReportPaged", "CWarehouseReport_Controller.F9002_sp_Chi_Tiet_Nhap_Page", "dbo.F9002_sp_Chi_Tiet_Nhap_Page", 1, LiteV2PageSize, "", LiteV2From, LiteV2To,
            new { From = "2025-01-01", To = "2026-12-31", PageNumber = 1, PageSize = LiteV2PageSize, Login = ApplicationLogin, WarehouseId = (long?)null }),
        new("InventoryHistoricalReportPaged", "InventoryHistoricalReportPaged", "CWarehouseReport_Controller.F9002_sp_Xuat_Nhap_Ton_Page", "dbo.F9002_sp_Xuat_Nhap_Ton_Page", 1, LiteV2PageSize, "", LiteV2From, LiteV2To,
            new { From = "2025-01-01", To = "2026-12-31", PageNumber = 1, PageSize = LiteV2PageSize, Login = ApplicationLogin, WarehouseId = (long?)null }),
        new("InventoryCurrentBalancePaged", "InventoryCurrentBalancePaged", "CWarehouseReport_Controller.F9001_sp_Ton_Kho_Hien_Tai_Page", "dbo.F9001_sp_Ton_Kho_Hien_Tai_Page", 1, LiteV2PageSize, "", LiteV2From, LiteV2To,
            new { PageNumber = 1, PageSize = LiteV2PageSize, Login = ApplicationLogin, WarehouseId = (long?)null })
    ];

    private static async Task<LiteScenarioResult> RunMeasuredCaseAsync(
        LiteCaseDefinition definition, string csvPath, string logPath, bool matrix, int measuredCount, int warmupCount)
    {
        var result = new LiteScenarioResult
        {
            Scenario = definition.Scenario,
            ApplicationMethod = definition.ApplicationMethod,
            StoredProcedure = definition.StoredProcedure,
            CaseName = definition.CaseName,
            Parameters = definition.ParametersSummary,
            WarmupTarget = warmupCount,
            MeasuredTarget = measuredCount
        };
        var smoke = await ExecuteLiteCallAsync(definition, "SMOKE", 1).ConfigureAwait(false);
        AppendLiteCallCsv(csvPath, definition, smoke.Record, matrix);
        result.Smoke = smoke.Record;
        if (!smoke.Record.Valid)
        {
            result.Verdict = smoke.Record.TimedOut ? "TIMEOUT" : "SMOKE_FAIL";
            result.Errors.Add(smoke.Record);
            result.SkipReason = "Smoke call failed or result semantics were invalid; later phases were skipped.";
            return result;
        }

        for (var iteration = 1; iteration <= warmupCount; iteration++)
        {
            var warmup = await ExecuteLiteCallAsync(definition, "WARMUP", iteration).ConfigureAwait(false);
            AppendLiteCallCsv(csvPath, definition, warmup.Record, matrix);
            result.WarmupCalls.Add(warmup.Record);
            if (!warmup.Record.Valid)
            {
                result.Verdict = warmup.Record.TimedOut ? "TIMEOUT" : "WARMUP_FAIL";
                result.Errors.Add(warmup.Record);
                result.SkipReason = "Warmup call failed; measured phase was skipped.";
                return result;
            }
        }

        for (var iteration = 1; iteration <= measuredCount; iteration++)
        {
            var measured = await ExecuteLiteCallAsync(definition, "MEASURED", iteration).ConfigureAwait(false);
            AppendLiteCallCsv(csvPath, definition, measured.Record, matrix);
            result.MeasuredCalls.Add(measured.Record);
            if (!measured.Record.Valid)
                result.Errors.Add(measured.Record);
        }
        CalculateLiteMetrics(result);
        result.Verdict = result.MeasuredCalls.Any(item => item.TimedOut) ? "TIMEOUT" :
            result.SuccessfulMeasuredCalls == measuredCount ? "PASS" : "FAIL";
        return result;
    }

    private static async Task<LiteCallExecution> ExecuteLiteCallAsync(LiteCaseDefinition definition, string phase, int iteration)
    {
        var started = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        try
        {
            var outcome = await InvokeLiteCaseAsync(definition).ConfigureAwait(false);
            timer.Stop();
            var withinPageRange = (long)(definition.PageNumber - 1) * definition.PageSize < outcome.TotalCount;
            var valid = outcome.TotalCount > 0 && outcome.ReturnedRows > 0 && outcome.ReturnedRows <= definition.PageSize &&
                        outcome.TotalCount >= outcome.ReturnedRows && withinPageRange;
            return new LiteCallExecution(outcome, new LiteCallRecord
            {
                RunId = definition.RunId,
                Scenario = definition.Scenario,
                CaseName = definition.CaseName,
                Phase = phase,
                Iteration = iteration,
                PageNumber = definition.PageNumber,
                PageSize = definition.PageSize,
                SearchText = definition.SearchText,
                FromDate = definition.FromDate,
                ToDate = definition.ToDate,
                StartedUtc = started,
                DurationMs = Round(timer.Elapsed.TotalMilliseconds),
                CallSucceeded = true,
                Valid = valid,
                ReturnedRows = outcome.ReturnedRows,
                TotalCount = outcome.TotalCount,
                ErrorCategory = valid ? null : "ResultSemanticsInvalid",
                ExceptionType = null,
                ErrorMessage = valid ? null : $"Expected a non-empty valid page; observed rows={outcome.ReturnedRows}, Total_Count={outcome.TotalCount}, page={definition.PageNumber}, size={definition.PageSize}.",
                TimedOut = false
            });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            timer.Stop();
            var sqlException = FindSqlException(exception);
            var timedOut = sqlException?.Number == -2 || exception is TimeoutException;
            return new LiteCallExecution(null, new LiteCallRecord
            {
                RunId = definition.RunId,
                Scenario = definition.Scenario,
                CaseName = definition.CaseName,
                Phase = phase,
                Iteration = iteration,
                PageNumber = definition.PageNumber,
                PageSize = definition.PageSize,
                SearchText = definition.SearchText,
                FromDate = definition.FromDate,
                ToDate = definition.ToDate,
                StartedUtc = started,
                DurationMs = Round(timer.Elapsed.TotalMilliseconds),
                CallSucceeded = false,
                Valid = false,
                SqlErrorNumber = sqlException?.Number,
                ErrorCategory = ClassifyLiteError(exception, sqlException, timedOut),
                ExceptionType = exception.GetType().Name,
                ErrorMessage = Sanitize(exception.Message),
                TimedOut = timedOut
            });
        }
    }

    private static string ClassifyLiteError(Exception exception, SqlException? sqlException, bool timedOut)
    {
        if (sqlException?.Number == -2) return "SQL_TIMEOUT";
        if (exception is TimeoutException) return "TIMEOUT";
        if (sqlException is not null) return "SQL_ERROR";
        return exception is InvalidOperationException or ArgumentException or NullReferenceException or
            InvalidCastException or FormatException or TargetInvocationException
            ? "APPLICATION_EXCEPTION"
            : "OTHER";
    }

    private static async Task<LiteCallOutcome> InvokeLiteCaseAsync(LiteCaseDefinition definition)
    {
        switch (definition.Scenario)
        {
            case "MasterPaged":
            {
                var page = await new CWarehouseMasterSupport_Controller().List_Master_Page_Async("SanPham", definition.PageNumber, definition.PageSize, definition.SearchText).ConfigureAwait(false);
                return new LiteCallOutcome(page.Items.Count, page.Total_Count, page.Items.Cast<object>().ToArray());
            }
            case "LookupPaged":
            {
                var page = await new CWarehouseMasterSupport_Controller().List_Lookup_Page_Async("SanPham", definition.PageNumber, definition.PageSize, definition.SearchText).ConfigureAwait(false);
                return new LiteCallOutcome(page.Items.Count, page.Total_Count, page.Items.Cast<object>().ToArray());
            }
            case "DocumentPaged":
            {
                var page = new CWarehouseDocumentSupport_Controller().sp_XNK_Document_Page(true, definition.PageNumber, definition.PageSize, definition.SearchText, ApplicationLogin, null);
                return new LiteCallOutcome(page.Items.Count, page.Total_Count, page.Items.Cast<object>().ToArray());
            }
            case "DetailReportPaged":
            {
                var page = new CWarehouseReport_Controller().F9002_sp_Chi_Tiet_Nhap_Page(definition.FromDate, definition.ToDate, definition.PageNumber, definition.PageSize, ApplicationLogin, null);
                return new LiteCallOutcome(page.Items.Count, page.Total_Count, page.Items.Cast<object>().ToArray());
            }
            case "InventoryHistoricalReportPaged":
            {
                var page = new CWarehouseReport_Controller().F9002_sp_Xuat_Nhap_Ton_Page(definition.FromDate, definition.ToDate, definition.PageNumber, definition.PageSize, ApplicationLogin, null);
                return new LiteCallOutcome(page.Items.Count, page.Total_Count, page.Items.Cast<object>().ToArray());
            }
            case "InventoryCurrentBalancePaged":
            {
                var page = new CWarehouseReport_Controller().F9001_sp_Ton_Kho_Hien_Tai_Page(definition.PageNumber, definition.PageSize, ApplicationLogin, null);
                return new LiteCallOutcome(page.Items.Count, page.Total_Count, page.Items.Cast<object>().ToArray());
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(definition), "Only the six application read paths are available in Lite V2.");
        }
    }

    private static void CalculateLiteMetrics(LiteScenarioResult result)
    {
        var successes = result.MeasuredCalls.Where(item => item.Valid).Select(item => item.DurationMs).Order().ToArray();
        result.SuccessfulMeasuredCalls = successes.Length;
        result.FailedMeasuredCalls = result.MeasuredCalls.Count - successes.Length;
        if (successes.Length == 0)
            return;
        result.MinMs = Round(successes[0]);
        result.MeanMs = Round(successes.Average());
        result.StandardDeviationMs = successes.Length < 2 ? 0 : Round(Math.Sqrt(successes.Sum(value => Math.Pow(value - successes.Average(), 2)) / (successes.Length - 1)));
        result.P50Ms = Round(NearestRank(successes, 0.50));
        result.P95Ms = Round(NearestRank(successes, 0.95));
        result.MaxMs = Round(successes[^1]);
    }

    private static void AppendLiteCallCsv(string path, LiteCaseDefinition definition, LiteCallRecord item, bool matrix)
    {
        var fields = matrix
            ? new[] { item.RunId, item.Scenario, item.CaseName, item.Phase, item.Iteration.ToString(CultureInfo.InvariantCulture), item.PageNumber.ToString(CultureInfo.InvariantCulture), item.PageSize.ToString(CultureInfo.InvariantCulture), item.SearchText, item.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), item.ToDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), item.StartedUtc.ToString("O", CultureInfo.InvariantCulture), item.DurationMs.ToString("F3", CultureInfo.InvariantCulture), item.CallSucceeded.ToString(), item.Valid.ToString(), item.ReturnedRows?.ToString(CultureInfo.InvariantCulture) ?? "", item.TotalCount?.ToString(CultureInfo.InvariantCulture) ?? "", item.SqlErrorNumber?.ToString(CultureInfo.InvariantCulture) ?? "", item.ErrorCategory ?? "", item.ExceptionType ?? "", item.ErrorMessage ?? "", item.TimedOut.ToString() }
            : new[] { item.RunId, item.Scenario, item.CaseName, item.Phase, item.Iteration.ToString(CultureInfo.InvariantCulture), item.StartedUtc.ToString("O", CultureInfo.InvariantCulture), item.DurationMs.ToString("F3", CultureInfo.InvariantCulture), item.CallSucceeded.ToString(), item.Valid.ToString(), item.ReturnedRows?.ToString(CultureInfo.InvariantCulture) ?? "", item.TotalCount?.ToString(CultureInfo.InvariantCulture) ?? "", item.SqlErrorNumber?.ToString(CultureInfo.InvariantCulture) ?? "", item.ErrorCategory ?? "", item.ExceptionType ?? "", item.ErrorMessage ?? "", item.TimedOut.ToString() };
        File.AppendAllText(path, string.Join(',', fields.Select(CsvEscape)) + "\r\n", new UTF8Encoding(false));
    }

    private static async Task<IReadOnlyList<LiteScenarioResult>> RunMatrixSuiteAsync(
        LiteV2Paths paths, HistoricalReadinessDocument readiness, int measuredCount, LiteV2RunDocument run)
    {
        var definitions = await BuildMatrixCasesAsync(paths, readiness).ConfigureAwait(false);
        var results = new List<LiteScenarioResult>();
        foreach (var item in definitions)
        {
            if (item.SkipReason is not null)
            {
                results.Add(new LiteScenarioResult
                {
                    Scenario = item.Definition.Scenario,
                    ApplicationMethod = item.Definition.ApplicationMethod,
                    StoredProcedure = item.Definition.StoredProcedure,
                    CaseName = item.Definition.CaseName,
                    Parameters = item.Definition.ParametersSummary,
                    Verdict = item.SkipReason.StartsWith("Historical readiness", StringComparison.Ordinal)
                        ? "SKIPPED_READINESS_BLOCKED" : "SKIPPED_NOT_VALID_FOR_DATA",
                    SkipReason = item.SkipReason
                });
                run.MatrixCases = results.ToList();
                WriteJson(paths.Results, run);
                continue;
            }

            var result = await RunMeasuredCaseAsync(item.Definition, paths.MatrixCsv, paths.Log,
                matrix: true, measuredCount, warmupCount: 1).ConfigureAwait(false);
            results.Add(result);
            run.MatrixCases = results.ToList();
            WriteJson(paths.Results, run);
            AppendLog(paths.Log, $"Matrix {result.Scenario}/{result.CaseName}: {result.Verdict}; {result.SuccessfulMeasuredCalls}/{result.MeasuredCalls}; mean={Format(result.MeanMs)} ms.");
            Console.WriteLine($"Matrix {result.Scenario}/{result.CaseName}: {result.Verdict}; {result.SuccessfulMeasuredCalls}/{result.MeasuredCalls}.");
        }
        return results;
    }

    private static async Task<List<MatrixCasePlan>> BuildMatrixCasesAsync(LiteV2Paths paths, HistoricalReadinessDocument readiness)
    {
        var plans = new List<MatrixCasePlan>();
        var definitions = CreateLiteSequentialCases().ToDictionary(item => item.Scenario, StringComparer.Ordinal);
        var documentSamples = new List<CWarehouseDocument>();

        var documentBase = definitions["DocumentPaged"];
        var documentPageCases = new Dictionary<int, (LiteCaseDefinition Definition, LiteCallOutcome? Outcome)>();
        var page1 = await ProbeLiteCaseAsync(paths, documentBase with { CaseName = "DocumentPage1", RunId = paths.RunId }, "DATA_DISCOVERY").ConfigureAwait(false);
        documentPageCases[1] = (documentBase with { CaseName = "DocumentPage1", RunId = paths.RunId }, page1?.Outcome);
        if (page1?.Outcome is not null)
            documentSamples.AddRange(page1.Outcome.Items.OfType<CWarehouseDocument>());
        var documentTotal = page1?.Outcome?.TotalCount ?? 0;
        var documentLastPage = documentTotal <= 0 ? 0 : (int)Math.Ceiling(documentTotal / (double)LiteV2PageSize);
        foreach (var pageNumber in new[] { 100, 1000, 10000 })
        {
            if (pageNumber > documentLastPage)
                continue;
            var definition = documentBase with
            {
                CaseName = $"DocumentPage{pageNumber}",
                PageNumber = pageNumber,
                ParametersSummary = new { IsReceipt = true, PageNumber = pageNumber, PageSize = LiteV2PageSize, SearchText = "", Login = ApplicationLogin, WarehouseId = (long?)null },
                RunId = paths.RunId
            };
            var probe = await ProbeLiteCaseAsync(paths, definition, "DATA_DISCOVERY").ConfigureAwait(false);
            documentPageCases[pageNumber] = (definition, probe?.Outcome);
            if (probe?.Outcome is not null)
                documentSamples.AddRange(probe.Outcome.Items.OfType<CWarehouseDocument>());
        }
        foreach (var pageCase in documentPageCases.OrderBy(item => item.Key))
        {
            var (definition, outcome) = pageCase.Value;
            if (outcome is null)
                plans.Add(new MatrixCasePlan(definition, "Document page was not valid in the live Data Access discovery call."));
            else
                plans.Add(new MatrixCasePlan(definition, null));
        }

        var commonTerm = documentSamples.Where(item => !string.IsNullOrWhiteSpace(item.Ten_Kho))
            .GroupBy(item => item.Ten_Kho.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key).FirstOrDefault();
        var rareTerm = documentSamples.Where(item => !string.IsNullOrWhiteSpace(item.So_Phieu))
            .GroupBy(item => item.So_Phieu.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Count()).ThenByDescending(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key).FirstOrDefault();
        foreach (var term in new[] { ("DocumentSearchCommon", commonTerm), ("DocumentSearchRare", rareTerm) })
        {
            var definition = documentBase with
            {
                CaseName = term.Item1,
                PageNumber = 1,
                SearchText = term.Item2 ?? "",
                ParametersSummary = new { IsReceipt = true, PageNumber = 1, PageSize = LiteV2PageSize, SearchText = term.Item2 ?? "", SearchTermSource = term.Item1 == "DocumentSearchCommon" ? "most frequent observed Ten_Kho" : "least frequent observed So_Phieu", Login = ApplicationLogin },
                RunId = paths.RunId
            };
            if (string.IsNullOrWhiteSpace(term.Item2))
            {
                plans.Add(new MatrixCasePlan(definition, "No non-empty data-derived document term was observed."));
                continue;
            }
            var probe = await ProbeLiteCaseAsync(paths, definition, "DATA_DISCOVERY").ConfigureAwait(false);
            plans.Add(new MatrixCasePlan(definition, probe?.Outcome is { TotalCount: > 0 }
                ? null : "Data-derived search term did not return a non-empty valid result through Data Access."));
        }

        var detailBase = definitions["DetailReportPaged"];
        var detailPage1 = detailBase with { CaseName = "DetailPage1", RunId = paths.RunId };
        var detailPage1Probe = await ProbeLiteCaseAsync(paths, detailPage1, "DATA_DISCOVERY").ConfigureAwait(false);
        if (detailPage1Probe?.Outcome is null)
        {
            plans.Add(new MatrixCasePlan(detailPage1, "Detail page 1 could not be qualified through Data Access."));
        }
        else
        {
            plans.Add(new MatrixCasePlan(detailPage1, null));
            var detailItems = detailPage1Probe.Outcome.Items.OfType<CWarehouseDetailReport>().ToList();
            var detailLastPage = (int)Math.Ceiling(detailPage1Probe.Outcome.TotalCount / (double)LiteV2PageSize);
            var detailDeepPage = Math.Min(100, detailLastPage);
            if (detailDeepPage > 1)
            {
                var deepDefinition = detailBase with
                {
                    CaseName = $"DetailDeepPage{detailDeepPage}",
                    PageNumber = detailDeepPage,
                    ParametersSummary = new { IsReceipt = true, From = LiteV2From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), To = LiteV2To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PageNumber = detailDeepPage, PageSize = LiteV2PageSize, Login = ApplicationLogin },
                    RunId = paths.RunId
                };
                var deepProbe = await ProbeLiteCaseAsync(paths, deepDefinition, "DATA_DISCOVERY").ConfigureAwait(false);
                plans.Add(new MatrixCasePlan(deepDefinition, deepProbe?.Outcome is null ? "Deep page discovery failed or was outside current result range." : null));
                if (deepProbe?.Outcome is not null)
                    detailItems.AddRange(deepProbe.Outcome.Items.OfType<CWarehouseDetailReport>());
            }

            var observedDates = detailItems.Select(item => item.Ngay.Date).Distinct().Order().ToArray();
            if (observedDates.Length > 0)
            {
                var narrowDate = observedDates[^1];
                var broadFrom = observedDates[0] < LiteV2From ? observedDates[0] : LiteV2From;
                var broadTo = observedDates[^1] > LiteV2To ? observedDates[^1] : LiteV2To;
                var dateCases = new List<LiteCaseDefinition>
                {
                    detailBase with { CaseName = "DetailNarrowObservedDay", FromDate = narrowDate, ToDate = narrowDate,
                        ParametersSummary = new { IsReceipt = true, From = narrowDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), To = narrowDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PageNumber = 1, PageSize = LiteV2PageSize, Login = ApplicationLogin, RangeSource = "observed Ngay in Data Access pages" }, RunId = paths.RunId },
                    detailBase with { CaseName = "DetailBroadObservedEnvelope", FromDate = broadFrom, ToDate = broadTo,
                        ParametersSummary = new { IsReceipt = true, From = broadFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), To = broadTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PageNumber = 1, PageSize = LiteV2PageSize, Login = ApplicationLogin, RangeSource = "observed Ngay plus baseline window" }, RunId = paths.RunId }
                };
                foreach (var dateCase in dateCases)
                {
                    var probe = await ProbeLiteCaseAsync(paths, dateCase, "DATA_DISCOVERY").ConfigureAwait(false);
                    plans.Add(new MatrixCasePlan(dateCase, probe?.Outcome is { TotalCount: > 0 }
                        ? null : "Data-derived detail date range did not return a non-empty valid page."));
                }
            }
        }

        var currentBase = definitions["InventoryCurrentBalancePaged"];
        var currentPage1 = currentBase with { CaseName = "CurrentBalancePage1", RunId = paths.RunId };
        var currentProbe = await ProbeLiteCaseAsync(paths, currentPage1, "DATA_DISCOVERY").ConfigureAwait(false);
        if (currentProbe?.Outcome is null)
        {
            plans.Add(new MatrixCasePlan(currentPage1, "Current-balance page 1 could not be qualified through Data Access."));
        }
        else
        {
            plans.Add(new MatrixCasePlan(currentPage1, null));
            var currentLastPage = (int)Math.Ceiling(currentProbe.Outcome.TotalCount / (double)LiteV2PageSize);
            var currentDeepPage = Math.Min(100, currentLastPage);
            if (currentDeepPage > 1)
            {
                var deepDefinition = currentBase with
                {
                    CaseName = $"CurrentBalanceDeepPage{currentDeepPage}",
                    PageNumber = currentDeepPage,
                    ParametersSummary = new { PageNumber = currentDeepPage, PageSize = LiteV2PageSize, Login = ApplicationLogin, WarehouseId = (long?)null, FilterSupport = "none in current Data Access method" },
                    RunId = paths.RunId
                };
                var deepProbe = await ProbeLiteCaseAsync(paths, deepDefinition, "DATA_DISCOVERY").ConfigureAwait(false);
                plans.Add(new MatrixCasePlan(deepDefinition, deepProbe?.Outcome is null ? "Deep current-balance page discovery failed or was outside current result range." : null));
            }
        }

        var historicalBase = definitions["InventoryHistoricalReportPaged"];
        if (!HistoricalReady(readiness))
        {
            var historicalMarker = historicalBase with { CaseName = "HistoricalMatrix", RunId = paths.RunId };
            plans.Add(new MatrixCasePlan(historicalMarker, $"Historical readiness is blocked: {readiness.Reason ?? readiness.Status}"));
        }
        else
        {
            var historicalNormal = historicalBase with { CaseName = "HistoricalNormalRange", RunId = paths.RunId };
            var normalProbe = await ProbeLiteCaseAsync(paths, historicalNormal, "DATA_DISCOVERY").ConfigureAwait(false);
            plans.Add(new MatrixCasePlan(historicalNormal, normalProbe?.Outcome is null ? "Historical normal range did not pass Data Access discovery." : null));
            var observedEarliest = readiness.PostState?.EarliestAuthorizedBalanceDate ?? readiness.PreState?.EarliestAuthorizedBalanceDate;
            var historicalBroadFrom = observedEarliest.HasValue && observedEarliest.Value < LiteV2From
                ? observedEarliest.Value : new DateTime(1, 1, 1);
            var historicalBroadTo = DateTime.Today > LiteV2To ? DateTime.Today : LiteV2To;
            var historicalBroad = historicalBase with
            {
                CaseName = "HistoricalBroadObservedRange",
                FromDate = historicalBroadFrom,
                ToDate = historicalBroadTo,
                ParametersSummary = new { From = historicalBroadFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), To = historicalBroadTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PageNumber = 1, PageSize = LiteV2PageSize, Login = ApplicationLogin, RangeSource = "authorized balance-scope metadata with SQL DATE lower bound" },
                RunId = paths.RunId
            };
            var broadProbe = await ProbeLiteCaseAsync(paths, historicalBroad, "DATA_DISCOVERY").ConfigureAwait(false);
            plans.Add(new MatrixCasePlan(historicalBroad, broadProbe?.Outcome is null ? "Historical broad range did not pass Data Access discovery." : null));
        }

        return plans;
    }

    private static async Task<LiteCallExecution?> ProbeLiteCaseAsync(LiteV2Paths paths, LiteCaseDefinition definition, string phase)
    {
        var execution = await ExecuteLiteCallAsync(definition with { RunId = paths.RunId }, phase, 0).ConfigureAwait(false);
        AppendLiteCallCsv(paths.MatrixCsv, definition, execution.Record, matrix: true);
        AppendLog(paths.Log, $"Matrix discovery {definition.Scenario}/{definition.CaseName}: valid={execution.Record.Valid}; rows={execution.Record.ReturnedRows?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}; total={execution.Record.TotalCount?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}.");
        return execution.Record.Valid ? execution : null;
    }

    private static bool HistoricalReady(HistoricalReadinessDocument readiness) =>
        readiness.Status is "READY_NO_PREPARATION_NEEDED" or "READY_AFTER_BOUNDED_PREPARATION";

    private static async Task<IReadOnlyList<LiteLoadStageDocument>> RunLoadSuiteAsync(LiteV2Paths paths, HistoricalReadinessDocument readiness, LiteV2Options options, LiteV2RunDocument run)
    {
        var allCases = CreateLiteSequentialCases().ToDictionary(item => item.Scenario, StringComparer.Ordinal);
        var cases = new List<LiteCaseDefinition>
        {
            allCases["DocumentPaged"] with { CaseName = "LoadDocumentPage1", RunId = paths.RunId },
            allCases["DetailReportPaged"] with { CaseName = "LoadDetailPage1", RunId = paths.RunId },
            allCases["InventoryCurrentBalancePaged"] with { CaseName = "LoadCurrentBalancePage1", RunId = paths.RunId }
        };
        if (HistoricalReady(readiness))
            cases.Insert(2, allCases["InventoryHistoricalReportPaged"] with { CaseName = "LoadHistoricalPage1", RunId = paths.RunId });

        var results = new List<LiteLoadStageDocument>();
        foreach (var definition in cases)
        {
            var probe = await ExecuteLiteCallAsync(definition, "LOAD_PREFLIGHT", 0).ConfigureAwait(false);
            var probeResult = probe.Record;
            AppendLog(paths.Log, $"Load preflight {definition.Scenario}: valid={probeResult.Valid}; total={probeResult.TotalCount?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}.");
            foreach (var users in options.Users)
            {
                if (!probeResult.Valid)
                {
                    var skipped = new LiteLoadStageDocument
                    {
                        RunId = paths.RunId,
                        Scenario = definition.Scenario,
                        CaseName = definition.CaseName,
                        Users = users,
                        ConfiguredCopies = users,
                        Verdict = probeResult.TimedOut ? "PREFLIGHT_TIMEOUT" : "PREFLIGHT_FAIL",
                        PreflightProbe = probeResult,
                        ErrorCategories = new Dictionary<string, long> { [probeResult.ErrorCategory ?? "ResultSemanticsInvalid"] = 1 }
                    };
                    results.Add(skipped);
                    AppendLiteLoadCsv(paths.LoadCsv, skipped);
                    run.LoadStages = results.ToList();
                    WriteJson(paths.LoadResults, run.LoadStages);
                    WriteJson(paths.Results, run);
                    continue;
                }

                var stage = await RunOneLoadStageAsync(paths, definition, users, probeResult, options).ConfigureAwait(false);
                results.Add(stage);
                AppendLiteLoadCsv(paths.LoadCsv, stage);
                run.LoadStages = results.ToList();
                WriteJson(paths.LoadResults, run.LoadStages);
                WriteJson(paths.Results, run);
                AppendLog(paths.Log, $"NBomber {definition.Scenario} users={users}: {stage.Verdict}; ok={stage.OkRequests}; fail={stage.FailedRequests}; RPS={stage.RequestsPerSecond?.ToString("F3", CultureInfo.InvariantCulture) ?? "n/a"}.");
                Console.WriteLine($"NBomber {definition.Scenario} users={users}: {stage.Verdict}; ok={stage.OkRequests}; fail={stage.FailedRequests}.");
            }
        }

        if (!HistoricalReady(readiness))
        {
            var historical = allCases["InventoryHistoricalReportPaged"] with { CaseName = "LoadHistoricalPage1", RunId = paths.RunId };
            foreach (var users in options.Users)
            {
                var skipped = new LiteLoadStageDocument
                {
                    RunId = paths.RunId,
                    Scenario = historical.Scenario,
                    CaseName = historical.CaseName,
                    Users = users,
                    ConfiguredCopies = users,
                    Verdict = "SKIPPED_READINESS_BLOCKED",
                    SkipReason = readiness.Reason ?? readiness.Status
                };
                results.Add(skipped);
                AppendLiteLoadCsv(paths.LoadCsv, skipped);
                run.LoadStages = results.ToList();
                WriteJson(paths.LoadResults, run.LoadStages);
                WriteJson(paths.Results, run);
            }
        }
        return results;
    }

    private static Task<LiteLoadStageDocument> RunOneLoadStageAsync(
        LiteV2Paths paths, LiteCaseDefinition definition, int users, LiteCallRecord preflight, LiteV2Options options)
    {
        var stageName = $"lite2_{Regex.Replace(definition.Scenario, "[^A-Za-z0-9]+", "_").ToLowerInvariant()}_u{users}";
        var counts = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        long warmupSuccessful = 0;
        long warmupFailed = 0;
        long measuredSuccessful = 0;
        long measuredFailed = 0;
        long callbackStartedTimestamp = 0;
        var startedUtc = DateTimeOffset.UtcNow;
        var machineBefore = CaptureMachineSnapshot();
        var scenario = Scenario.Create(stageName, async _ =>
        {
            Interlocked.CompareExchange(ref callbackStartedTimestamp, Stopwatch.GetTimestamp(), 0);
            var isWarmup = options.LoadWarmupSeconds > 0 &&
                           Stopwatch.GetElapsedTime(Interlocked.Read(ref callbackStartedTimestamp)).TotalSeconds < options.LoadWarmupSeconds;
            var phasePrefix = isWarmup ? "WARMUP_" : "MEASURED_";
            try
            {
                var outcome = await InvokeLiteCaseAsync(definition).ConfigureAwait(false);
                var valid = outcome.TotalCount > 0 && outcome.ReturnedRows > 0 && outcome.ReturnedRows <= definition.PageSize &&
                            outcome.TotalCount >= outcome.ReturnedRows && (long)(definition.PageNumber - 1) * definition.PageSize < outcome.TotalCount;
                if (!valid)
                {
                    if (isWarmup) Interlocked.Increment(ref warmupFailed); else Interlocked.Increment(ref measuredFailed);
                    counts.AddOrUpdate(phasePrefix + "RESULT_SEMANTICS_INVALID", 1, (_, count) => count + 1);
                    return Response.Fail<string>();
                }
                if (isWarmup) Interlocked.Increment(ref warmupSuccessful); else Interlocked.Increment(ref measuredSuccessful);
                return Response.Ok();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                var sqlException = FindSqlException(exception);
                var timeout = sqlException?.Number == -2 || exception is TimeoutException;
                var category = ClassifyLiteError(exception, sqlException, timeout);
                counts.AddOrUpdate(phasePrefix + category, 1, (_, count) => count + 1);
                if (isWarmup) Interlocked.Increment(ref warmupFailed); else Interlocked.Increment(ref measuredFailed);
                return Response.Fail<string>();
            }
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(options.LoadWarmupSeconds))
        .WithLoadSimulations(Simulation.KeepConstant(copies: users, during: TimeSpan.FromSeconds(options.LoadSeconds)))
        .WithRestartIterationOnFail(false)
        .WithMaxFailCount(1_000_000);

        JsonElement? nativeProjection = null;
        string? nativeStatsError = null;
        try
        {
            var nodeStats = NBomberRunner.RegisterScenarios(scenario)
                .WithTestSuite("Warehouse Benchmark Lite V2")
                .WithTestName($"{paths.RunId}_{stageName}")
                .WithoutReports()
                .Run();
            nativeProjection = ProjectNativeStats(nodeStats);
            if (!HasNativeScenarioStats(nativeProjection.Value, stageName))
                nativeStatsError = "NBomber returned no native statistics row matching this stage.";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            nativeStatsError = Sanitize($"{exception.GetType().Name}: {exception.Message}");
            Interlocked.Increment(ref measuredFailed);
            counts.AddOrUpdate("NBomberRunnerError", 1, (_, count) => count + 1);
        }
        var finishedUtc = DateTimeOffset.UtcNow;
        var machineAfter = CaptureMachineSnapshot();
        var result = new LiteLoadStageDocument
        {
            RunId = paths.RunId,
            Scenario = definition.Scenario,
            CaseName = definition.CaseName,
            ApplicationMethod = definition.ApplicationMethod,
            StoredProcedure = definition.StoredProcedure,
            Users = users,
            ConfiguredCopies = users,
            StartedUtc = startedUtc,
            FinishedUtc = finishedUtc,
            MeasuredSeconds = options.LoadSeconds,
            WarmupSeconds = options.LoadWarmupSeconds,
            WarmupOkRequests = warmupSuccessful,
            WarmupFailedRequests = warmupFailed,
            CallbackMeasuredOkRequests = measuredSuccessful,
            CallbackMeasuredFailedRequests = measuredFailed,
            OkRequests = measuredSuccessful,
            FailedRequests = measuredFailed,
            RequestsPerSecond = Round(measuredSuccessful / (double)options.LoadSeconds),
            ErrorCategories = counts.OrderBy(item => item.Key).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            NativeStats = nativeProjection,
            NativeStatsError = nativeStatsError,
            MachineBefore = machineBefore,
            MachineAfter = machineAfter,
            PreflightProbe = preflight,
            Verdict = warmupFailed == 0 && measuredFailed == 0 && nativeStatsError is null ? "PASS" : "FAIL"
        };
        FillNativeLatencyMetrics(result);
        if (result.NativeOkRequests.HasValue)
            result.OkRequests = result.NativeOkRequests.Value;
        if (result.NativeFailedRequests.HasValue)
            result.FailedRequests = result.NativeFailedRequests.Value;
        result.RequestsPerSecond = result.NativeRequestsPerSecond ?? Round(result.OkRequests / (double)options.LoadSeconds);
        result.NativeCounterReconciliation = !result.NativeOkRequests.HasValue || !result.NativeFailedRequests.HasValue
            ? "NOT_VERIFIED"
            : result.NativeOkRequests == measuredSuccessful && result.NativeFailedRequests == measuredFailed ? "PASS" : "MISMATCH_REVIEW";
        if (result.FailedRequests > 0)
            result.Verdict = "FAIL";
        return Task.FromResult(result);
    }

    private static JsonElement ProjectNativeStats(object nodeStats)
    {
        var scenarioStats = ReadProperty(nodeStats, "ScenarioStats") as System.Collections.IEnumerable;
        if (scenarioStats is null)
            return JsonSerializer.SerializeToElement(new { NodeType = nodeStats.GetType().FullName, ScenarioStats = "NOT_EXPOSED" });
        var projection = new
        {
            NodeType = nodeStats.GetType().FullName,
            ScenarioStats = scenarioStats.Cast<object>().Select(ProjectNativeObject).ToArray()
        };
        return JsonSerializer.SerializeToElement(projection);
    }

    private static bool HasNativeScenarioStats(JsonElement nodeStats, string stageName) =>
        nodeStats.ValueKind == JsonValueKind.Object && nodeStats.TryGetProperty("ScenarioStats", out var items) &&
        items.ValueKind == JsonValueKind.Array && items.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.Object && item.TryGetProperty("ScenarioName", out var name) && name.GetString() == stageName);

    private static object ProjectNativeObject(object value) => ProjectNativeValue(value, 0) ?? new { Type = value.GetType().FullName };

    private static object? ProjectNativeValue(object? value, int depth)
    {
        if (value is null) return null;
        var type = value.GetType();
        if (type.IsEnum) return value.ToString();
        if (value is string text) return text.Length <= 300 ? text : text[..300];
        if (value is DateTime or DateTimeOffset or TimeSpan) return Convert.ToString(value, CultureInfo.InvariantCulture);
        if (type.IsPrimitive || value is decimal) return value;
        if (depth >= 4) return type.FullName;
        if (value is System.Collections.IDictionary dictionary)
        {
            var map = new Dictionary<string, object?>();
            foreach (System.Collections.DictionaryEntry entry in dictionary)
                if (map.Count < 80)
                    map[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "key"] = ProjectNativeValue(entry.Value, depth + 1);
            return map;
        }
        if (value is System.Collections.IEnumerable enumerable)
            return enumerable.Cast<object?>().Take(80).Select(item => ProjectNativeValue(item, depth + 1)).ToArray();
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Take(80);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            try { result[property.Name] = ProjectNativeValue(property.GetValue(value), depth + 1); }
            catch { result[property.Name] = "UNAVAILABLE"; }
        }
        return result;
    }

    private static void FillNativeLatencyMetrics(LiteLoadStageDocument result)
    {
        if (result.NativeStats is not JsonElement native || native.ValueKind != JsonValueKind.Object ||
            !native.TryGetProperty("ScenarioStats", out var scenarios) || scenarios.ValueKind != JsonValueKind.Array || scenarios.GetArrayLength() == 0)
            return;
        var first = scenarios[0];
        if (first.ValueKind != JsonValueKind.Object || !first.TryGetProperty("Ok", out var ok) || ok.ValueKind != JsonValueKind.Object)
            return;
        if (ok.TryGetProperty("Latency", out var latency))
        {
            result.NativeLatencyMeanMs = ReadJsonDouble(latency, "MeanMs");
            result.NativeLatencyP50Ms = ReadJsonDouble(latency, "Percent50");
            result.NativeLatencyP95Ms = ReadJsonDouble(latency, "Percent95");
            result.NativeLatencyP99Ms = ReadJsonDouble(latency, "Percent99");
            result.NativeLatencyMaxMs = ReadJsonDouble(latency, "MaxMs");
        }
        if (ok.TryGetProperty("Request", out var request) && request.ValueKind == JsonValueKind.Object)
            result.NativeRequestsPerSecond = ReadJsonDouble(request, "RPS");
        result.NativeOkRequests = ReadJsonLong(ok, "Request", "Count");
        if (first.TryGetProperty("Fail", out var fail))
            result.NativeFailedRequests = ReadJsonLong(fail, "Request", "Count");
    }

    private static object? ReadProperty(object? value, string name)
    {
        if (value is null) return null;
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property))
            return property;
        return value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
    }

    private static double? ReadJsonDouble(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.TryGetDouble(out var number)
            ? Round(number) : null;

    private static long? ReadJsonLong(JsonElement parent, string parentName, string valueName) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(parentName, out var nested) && nested.ValueKind == JsonValueKind.Object &&
        nested.TryGetProperty(valueName, out var value) && value.TryGetInt64(out var number) ? number : null;

    private static void AppendLiteLoadCsv(string path, LiteLoadStageDocument item)
    {
        var fields = new[]
        {
            item.RunId, item.Scenario, item.CaseName, item.Users.ToString(CultureInfo.InvariantCulture),
            item.ConfiguredCopies.ToString(CultureInfo.InvariantCulture), item.StartedUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            item.MeasuredSeconds.ToString(CultureInfo.InvariantCulture), item.WarmupSeconds.ToString(CultureInfo.InvariantCulture),
            item.WarmupOkRequests.ToString(CultureInfo.InvariantCulture), item.WarmupFailedRequests.ToString(CultureInfo.InvariantCulture),
            item.OkRequests.ToString(CultureInfo.InvariantCulture), item.FailedRequests.ToString(CultureInfo.InvariantCulture),
            (item.NativeRequestsPerSecond ?? item.RequestsPerSecond)?.ToString("F3", CultureInfo.InvariantCulture) ?? "",
            item.NativeLatencyMeanMs?.ToString("F3", CultureInfo.InvariantCulture) ?? "",
            item.NativeLatencyP95Ms?.ToString("F3", CultureInfo.InvariantCulture) ?? "",
            item.NativeLatencyP99Ms?.ToString("F3", CultureInfo.InvariantCulture) ?? "",
            item.NativeLatencyMaxMs?.ToString("F3", CultureInfo.InvariantCulture) ?? "",
            JsonSerializer.Serialize(item.ErrorCategories), item.Verdict
        };
        File.AppendAllText(path, string.Join(',', fields.Select(CsvEscape)) + "\r\n", new UTF8Encoding(false));
    }

    private static string GetLiteV2OverallVerdict(LiteV2RunDocument run)
    {
        if (run.Status is "TARGET_PREFLIGHT_FAILED" or "HARNESS_FAILED")
            return run.Status;
        var sequentialRequired = run.Mode is "sequential" or "all";
        var loadRequired = run.Mode is "load" or "all";
        if (sequentialRequired && (run.SequentialCases.Count != 6 || run.SequentialCases.Any(item => item.Verdict != "PASS")))
            return "PARTIAL";
        if (sequentialRequired && run.MatrixCases.Any(item => item.Verdict != "PASS"))
            return "PARTIAL";
        if (loadRequired && (run.LoadStages.Count == 0 || run.LoadStages.Any(item => item.Verdict != "PASS")))
            return "PARTIAL";
        return "PASS";
    }

    private static void WriteLiteV2FinalArtifacts(LiteV2Paths paths, LiteV2RunDocument run)
    {
        WriteJson(paths.Results, run);
        WriteJson(paths.LoadResults, run.LoadStages);
        WriteJson(paths.Readiness, run.HistoricalReadiness ?? new HistoricalReadinessDocument { Status = "NOT_EVALUATED" });
        WriteJson(paths.Environment, new LiteV2EnvironmentDocument
        {
            RunId = run.RunId,
            MachineBefore = run.MachineBefore,
            MachineAfter = run.MachineAfter,
            Server = run.Server,
            Database = run.Database,
            DatabaseId = run.DatabaseId,
            DatabaseState = run.DatabaseState,
            DatasetScale = run.DatasetScale,
            ConfigurationSha256 = run.ConfigurationSha256,
            SourceIdentity = run.SourceIdentity,
            PreflightFailure = run.PreflightFailure,
            ProcedureParity = run.ProcedureParity
        });
        File.WriteAllText(paths.Report, BuildLiteV2Report(run, paths), new UTF8Encoding(false));
        run.ArtifactIntegrity = VerifyLiteV2Artifacts(paths, run);
        WriteJson(paths.Results, run);
        File.WriteAllText(paths.Report, BuildLiteV2Report(run, paths), new UTF8Encoding(false));
    }

    private static string VerifyLiteV2Artifacts(LiteV2Paths paths, LiteV2RunDocument run)
    {
        var requiredFiles = new[] { paths.Results, paths.Report, paths.SequentialCsv, paths.MatrixCsv, paths.LoadResults, paths.LoadCsv, paths.Environment, paths.Log, paths.Readiness };
        if (requiredFiles.Any(path => !File.Exists(path)))
            return "FAIL_MISSING_FILE";
        try
        {
            using var resultJson = JsonDocument.Parse(File.ReadAllText(paths.Results));
            if (!resultJson.RootElement.TryGetProperty("RunId", out var storedRunId) || storedRunId.GetString() != run.RunId)
                return "FAIL_RUN_ID_MISMATCH";
            using var loadJson = JsonDocument.Parse(File.ReadAllText(paths.LoadResults));
            if (loadJson.RootElement.ValueKind != JsonValueKind.Array)
                return "FAIL_LOAD_RESULT_SHAPE";
            var report = File.ReadAllText(paths.Report);
            if (!report.Contains(run.RunId, StringComparison.Ordinal) || !report.Contains("Limitations", StringComparison.OrdinalIgnoreCase))
                return "FAIL_REPORT_CONTENT";
            if (new FileInfo(paths.SequentialCsv).Length == 0 || new FileInfo(paths.MatrixCsv).Length == 0 || new FileInfo(paths.LoadCsv).Length == 0)
                return "FAIL_RAW_CSV_EMPTY";
            return "PASS";
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return $"FAIL_{exception.GetType().Name}";
        }
    }

    private static string BuildLiteV2Report(LiteV2RunDocument run, LiteV2Paths paths)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Warehouse Benchmark Lite V2");
        builder.AppendLine();
        builder.AppendLine($"- RunId: `{run.RunId}`");
        builder.AppendLine($"- Mode / verdict: **{run.Mode} / {run.OverallVerdict ?? run.Status}**");
        builder.AppendLine($"- Started / finished UTC: {run.StartedUtc:O} / {run.FinishedUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "not finished"}");
        builder.AppendLine($"- Target: `{run.Server ?? "not verified"}` / `{run.Database ?? "not verified"}` / DB_ID `{run.DatabaseId?.ToString(CultureInfo.InvariantCulture) ?? "not verified"}` / `{run.DatabaseState ?? "not verified"}`");
        builder.AppendLine($"- Detail-table partition metadata rows: **{run.DatasetScale?.PrimaryDatasetRowCount.ToString("N0", CultureInfo.InvariantCulture) ?? "not verified"}**");
        builder.AppendLine($"- Repository: `{run.RepositoryPath}`");
        builder.AppendLine($"- Build/runtime: `{run.BuildConfiguration}`; benchmark DLL SHA-256 `{run.SourceIdentity?.BenchmarkAssemblySha256 ?? "not available"}`; Data Access DLL SHA-256 `{run.SourceIdentity?.DataAccessAssemblySha256 ?? "not available"}`");
        builder.AppendLine($"- Business DB writes during measured workload: **{run.MeasurementBusinessDbWrites}**; historical preparation worker calls: **{run.HistoricalReadiness?.WorkerCalls ?? 0}**");
        builder.AppendLine($"- Artifact integrity: **{run.ArtifactIntegrity ?? "not checked"}**");
        builder.AppendLine($"- Full Batch4 canonical path: **{run.FullBatch4CanonicalPath}**");
        builder.AppendLine();
        builder.AppendLine("## Application path");
        builder.AppendLine();
        builder.AppendLine("Every measured request uses the current Data Access controller and Warehouse controller/base path to `CSqlHelper` and its paging stored procedure. Direct SQL is limited to target identity, dataset metadata, procedure parity, and historical readiness/preparation evidence.");
        builder.AppendLine();
        builder.AppendLine("## Procedure source parity");
        builder.AppendLine();
        if (run.ProcedureParity.Count == 0)
            builder.AppendLine("Procedure parity was not evaluated.");
        else
        {
            builder.AppendLine("| Procedure | Role | Status | Source body SHA-256 | Live body SHA-256 |");
            builder.AppendLine("|---|---|---|---|---|");
            foreach (var item in run.ProcedureParity)
                builder.AppendLine($"| `{item.Name}` | {item.Role} | **{item.Status}** | `{item.SourceDefinitionSha256 ?? "n/a"}` | `{item.LiveDefinitionSha256 ?? "n/a"}` |");
        }
        builder.AppendLine();
        builder.AppendLine("## Historical readiness and preparation");
        builder.AppendLine();
        var readiness = run.HistoricalReadiness;
        builder.AppendLine($"- Status: **{readiness?.Status ?? "NOT_EVALUATED"}**; worker calls: **{readiness?.WorkerCalls ?? 0}**");
        builder.AppendLine($"- Readiness reason: {Escape(readiness?.Reason)}");
        builder.AppendLine($"- Persisted pre-state fingerprint: `{readiness?.PreStateFingerprint ?? "n/a"}`; post-state fingerprint: `{readiness?.PostStateFingerprint ?? "n/a"}`");
        builder.AppendLine("- A preparation call, if present, invoked only `sp_Inventory_Movement_Process_RebuildQueue` once with `@Batch_Size=1`; no SQL Agent job was started. Its pre/post queue and aggregate states are in `historical-readiness.json`.");
        builder.AppendLine();
        builder.AppendLine("## Sequential read results");
        builder.AppendLine();
        if (run.SequentialCases.Count == 0)
            builder.AppendLine("Not run in this mode.");
        else
        {
            builder.AppendLine("| Scenario | Smoke | Warmup | Successful measured | Mean ms | SD ms | P50 ms | P95 ms | Max ms | Verdict |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---|");
            foreach (var item in run.SequentialCases)
                builder.AppendLine($"| `{item.Scenario}` | {item.Smoke?.Valid.ToString() ?? "n/a"} | {item.WarmupCalls.Count}/{item.WarmupTarget} | {item.SuccessfulMeasuredCalls}/{item.MeasuredTarget} | {Format(item.MeanMs)} | {Format(item.StandardDeviationMs)} | {Format(item.P50Ms)} | {Format(item.P95Ms)} | {Format(item.MaxMs)} | **{item.Verdict}** |");
        }
        builder.AppendLine();
        builder.AppendLine("## Data-derived matrix");
        builder.AppendLine();
        if (run.MatrixCases.Count == 0)
            builder.AppendLine("Not run in this mode.");
        else
        {
            builder.AppendLine("| Scenario | Case | Parameters | Successful measured | Mean ms | P95 ms | Verdict / skip reason |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---|");
            foreach (var item in run.MatrixCases)
                builder.AppendLine($"| `{item.Scenario}` | `{item.CaseName}` | `{Escape(JsonSerializer.Serialize(item.Parameters))}` | {item.SuccessfulMeasuredCalls}/{item.MeasuredTarget} | {Format(item.MeanMs)} | {Format(item.P95Ms)} | **{item.Verdict}** {Escape(item.SkipReason)} |");
        }
        builder.AppendLine();
        builder.AppendLine("## Bounded NBomber closed-load results");
        builder.AppendLine();
        if (run.LoadStages.Count == 0)
            builder.AppendLine("Not run in this mode.");
        else
        {
            builder.AppendLine("| Scenario | Users/copies | Warmup / measured | Warmup ok/fail | Measured ok/fail | Native RPS | Native mean / P95 / P99 / max ms | Verdict |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---|---|");
            foreach (var item in run.LoadStages)
                builder.AppendLine($"| `{item.Scenario}` | {item.Users}/{item.ConfiguredCopies} | {item.WarmupSeconds}s / {item.MeasuredSeconds}s | {item.WarmupOkRequests}/{item.WarmupFailedRequests} | {item.OkRequests}/{item.FailedRequests} | {Format(item.NativeRequestsPerSecond ?? item.RequestsPerSecond)} | {Format(item.NativeLatencyMeanMs)} / {Format(item.NativeLatencyP95Ms)} / {Format(item.NativeLatencyP99Ms)} / {Format(item.NativeLatencyMaxMs)} | **{item.Verdict}** {Escape(item.SkipReason)} |");
            builder.AppendLine();
            builder.AppendLine("Per-stage RAM snapshots, reflected native NBomber `NodeStats.ScenarioStats`, and the comparison of callback counters with native counters are preserved in `benchmark-lite2-load-results.json`.");
        }
        builder.AppendLine();
        builder.AppendLine("## Hotspots");
        builder.AppendLine();
        var ranked = run.SequentialCases.Where(item => item.P95Ms.HasValue).OrderByDescending(item => item.P95Ms).Take(3).ToArray();
        if (ranked.Length == 0)
            builder.AppendLine("No sequential scenario produced measured samples.");
        else
            foreach (var item in ranked)
                builder.AppendLine($"- `{item.Scenario}`: P95 {Format(item.P95Ms)} ms, mean {Format(item.MeanMs)} ms.");
        builder.AppendLine();
        builder.AppendLine("## Errors and limitations");
        builder.AppendLine();
        builder.AppendLine("- Each phase records SQL timeout, SQL error, application exception, and other error separately; no per-request retries are configured.");
        builder.AppendLine("- No latency SLA is defined; reported timings are observations, not a pass/fail performance target.");
        builder.AppendLine("- One machine and one target database; the report does not guarantee production hardware performance.");
        builder.AppendLine("- Matrix cases are qualified from current Data Access results. Out-of-range pages are omitted; cases that could not be verified remain explicit skips.");
        builder.AppendLine("- Read measurement workload invokes no business write method. If historical preparation ran, it may update the inventory aggregate and queue through the one recorded bounded worker call.");
        builder.AppendLine("- No BDN, mixed workload, 8-user stage, Full Batch4 canonical run, host admission, heavy telemetry, or production write path is included.");
        builder.AppendLine("- Dataset size is partition metadata, not full-dataset value equality. Results do not establish a performance SLA or causal attribution.");
        builder.AppendLine("- `HistoricalTimeoutCause=NOT_VERIFIED`, `HistoricalTelemetrySqlMinus2Cause=NOT_VERIFIED`, `FullDatasetValueEquality=NOT_VERIFIED`, and database data parity with the business database is not verified by this Lite run.");
        if (!string.IsNullOrWhiteSpace(run.PreflightFailure))
            builder.AppendLine($"- Stop/failure: `{run.HarnessFailureType}` — {Escape(run.PreflightFailure)}");
        builder.AppendLine();
        builder.AppendLine("## Artifacts");
        builder.AppendLine();
        foreach (var path in new[] { paths.Results, paths.SequentialCsv, paths.MatrixCsv, paths.LoadResults, paths.LoadCsv, paths.Environment, paths.Readiness, paths.Log })
            builder.AppendLine($"- `{Path.GetFileName(path)}`");
        return builder.ToString();
    }

    private static void PrintLiteV2Help() =>
        Console.WriteLine("Warehouse Benchmark Lite V2: BenchmarkLite.exe [--mode all|sequential|load] [--preflight-only] [--config <path>] [--measured-count 1..300] [--matrix-measured-count 1..100] [--load-seconds 1..300] [--load-warmup-seconds 0..60] [--users 1,2,4]");
}

internal sealed record LiteV2Options(string Mode, int MeasuredCount, int MatrixMeasuredCount, int LoadSeconds, int LoadWarmupSeconds, int[] Users, bool PreflightOnly);

internal sealed record LiteV2Paths(string RunId, string Directory, string Results, string Report, string SequentialCsv,
    string MatrixCsv, string LoadResults, string LoadCsv, string Environment, string Log, string Readiness)
{
    public static LiteV2Paths Create(string directory)
    {
        var runId = new DirectoryInfo(directory).Name;
        return new LiteV2Paths(runId, directory,
            Path.Combine(directory, "benchmark-lite2-results.json"),
            Path.Combine(directory, "benchmark-lite2-report.md"),
            Path.Combine(directory, "benchmark-lite2-sequential-raw.csv"),
            Path.Combine(directory, "benchmark-lite2-matrix-raw.csv"),
            Path.Combine(directory, "benchmark-lite2-load-results.json"),
            Path.Combine(directory, "benchmark-lite2-load-raw.csv"),
            Path.Combine(directory, "benchmark-lite2-environment.json"),
            Path.Combine(directory, "benchmark-lite2-run.log"),
            Path.Combine(directory, "historical-readiness.json"));
    }
}

internal sealed record LiteCaseDefinition(string Scenario, string CaseName, string ApplicationMethod, string StoredProcedure,
    int PageNumber, int PageSize, string SearchText, DateTime FromDate, DateTime ToDate, object ParametersSummary)
{
    public string RunId { get; init; } = "";
}

internal sealed record LiteCallOutcome(int ReturnedRows, int TotalCount, IReadOnlyList<object> Items);
internal sealed record LiteCallExecution(LiteCallOutcome? Outcome, LiteCallRecord Record);
internal sealed record MatrixCasePlan(LiteCaseDefinition Definition, string? SkipReason);

internal sealed class LiteCallRecord
{
    public string RunId { get; set; } = "";
    public string Scenario { get; set; } = "";
    public string CaseName { get; set; } = "";
    public string Phase { get; set; } = "";
    public int Iteration { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public string SearchText { get; set; } = "";
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public double DurationMs { get; set; }
    public bool CallSucceeded { get; set; }
    public bool Valid { get; set; }
    public int? ReturnedRows { get; set; }
    public int? TotalCount { get; set; }
    public int? SqlErrorNumber { get; set; }
    public string? ErrorCategory { get; set; }
    public string? ExceptionType { get; set; }
    public string? ErrorMessage { get; set; }
    public bool TimedOut { get; set; }
}

internal sealed class LiteScenarioResult
{
    public string Scenario { get; set; } = "";
    public string ApplicationMethod { get; set; } = "";
    public string StoredProcedure { get; set; } = "";
    public string CaseName { get; set; } = "";
    public object Parameters { get; set; } = new();
    public int WarmupTarget { get; set; }
    public int MeasuredTarget { get; set; }
    public LiteCallRecord? Smoke { get; set; }
    public List<LiteCallRecord> WarmupCalls { get; set; } = [];
    public List<LiteCallRecord> MeasuredCalls { get; set; } = [];
    public List<LiteCallRecord> Errors { get; set; } = [];
    public int SuccessfulMeasuredCalls { get; set; }
    public int FailedMeasuredCalls { get; set; }
    public double? MinMs { get; set; }
    public double? MeanMs { get; set; }
    public double? StandardDeviationMs { get; set; }
    public double? P50Ms { get; set; }
    public double? P95Ms { get; set; }
    public double? MaxMs { get; set; }
    public string Verdict { get; set; } = "NOT_RUN";
    public string? SkipReason { get; set; }
}

internal sealed class LiteLoadStageDocument
{
    public string RunId { get; set; } = "";
    public string Scenario { get; set; } = "";
    public string CaseName { get; set; } = "";
    public string ApplicationMethod { get; set; } = "";
    public string StoredProcedure { get; set; } = "";
    public int Users { get; set; }
    public int ConfiguredCopies { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }
    public int MeasuredSeconds { get; set; }
    public int WarmupSeconds { get; set; }
    public long OkRequests { get; set; }
    public long FailedRequests { get; set; }
    public long CallbackMeasuredOkRequests { get; set; }
    public long CallbackMeasuredFailedRequests { get; set; }
    public long WarmupOkRequests { get; set; }
    public long WarmupFailedRequests { get; set; }
    public long? NativeOkRequests { get; set; }
    public long? NativeFailedRequests { get; set; }
    public double? RequestsPerSecond { get; set; }
    public double? NativeRequestsPerSecond { get; set; }
    public double? NativeLatencyMeanMs { get; set; }
    public double? NativeLatencyP50Ms { get; set; }
    public double? NativeLatencyP95Ms { get; set; }
    public double? NativeLatencyP99Ms { get; set; }
    public double? NativeLatencyMaxMs { get; set; }
    public string NativeCounterReconciliation { get; set; } = "NOT_VERIFIED";
    public Dictionary<string, long> ErrorCategories { get; set; } = [];
    public JsonElement? NativeStats { get; set; }
    public string? NativeStatsError { get; set; }
    public MachineDocument? MachineBefore { get; set; }
    public MachineDocument? MachineAfter { get; set; }
    public LiteCallRecord? PreflightProbe { get; set; }
    public string Verdict { get; set; } = "NOT_RUN";
    public string? SkipReason { get; set; }
}

internal sealed class LiteV2RunDocument
{
    public string RunId { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }
    public string Status { get; set; } = "CREATED";
    public string RepositoryPath { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public string Mode { get; set; } = "";
    public LiteV2Options Options { get; set; } = new("all", 30, 10, 30, 3, [1, 2, 4], false);
    public string BuildConfiguration { get; set; } = "";
    public string? ProcessPath { get; set; }
    public string? ConfigurationPath { get; set; }
    public string? ConfigurationSha256 { get; set; }
    public string? ConfiguredServer { get; set; }
    public string? ConfiguredDatabase { get; set; }
    public string? Server { get; set; }
    public string? Database { get; set; }
    public int? DatabaseId { get; set; }
    public string? DatabaseState { get; set; }
    public DatasetDocument? DatasetScale { get; set; }
    public MachineDocument MachineBefore { get; set; } = new();
    public MachineDocument? MachineAfter { get; set; }
    public SourceIdentityDocument? SourceIdentity { get; set; }
    public List<ProcedureParityDocument> ProcedureParity { get; set; } = [];
    public HistoricalReadinessDocument? HistoricalReadiness { get; set; }
    public string SequentialStatus { get; set; } = "NOT_RUN";
    public List<LiteScenarioResult> SequentialCases { get; set; } = [];
    public string MatrixStatus { get; set; } = "NOT_RUN";
    public List<LiteScenarioResult> MatrixCases { get; set; } = [];
    public string LoadStatus { get; set; } = "NOT_RUN";
    public List<LiteLoadStageDocument> LoadStages { get; set; } = [];
    public int MeasurementBusinessDbWrites { get; set; }
    public string FullBatch4CanonicalPath { get; set; } = "STOPPED_BY_OWNER_SCOPE_CHANGE";
    public string? OverallVerdict { get; set; }
    public string? ArtifactIntegrity { get; set; }
    public string? HarnessFailureType { get; set; }
    public string? PreflightFailure { get; set; }
    public bool ResultsRepresentThisMachineEnvironment { get; set; }
    public bool ResultsAreNotProductionHardwareGuarantee { get; set; }
}

internal sealed class LiteV2EnvironmentDocument
{
    public string RunId { get; set; } = "";
    public MachineDocument? MachineBefore { get; set; }
    public MachineDocument? MachineAfter { get; set; }
    public string? Server { get; set; }
    public string? Database { get; set; }
    public int? DatabaseId { get; set; }
    public string? DatabaseState { get; set; }
    public DatasetDocument? DatasetScale { get; set; }
    public string? ConfigurationSha256 { get; set; }
    public SourceIdentityDocument? SourceIdentity { get; set; }
    public List<ProcedureParityDocument>? ProcedureParity { get; set; }
    public string? PreflightFailure { get; set; }
}

internal sealed class ProcedureParityDocument
{
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Status { get; set; } = "NOT_VERIFIED";
    public string? SourceDefinitionSha256 { get; set; }
    public string? LiveDefinitionSha256 { get; set; }
    public int? SourceNormalizedLength { get; set; }
    public int? LiveNormalizedLength { get; set; }
    public int? FirstDifferenceIndex { get; set; }
    public string? SourceDifferenceContext { get; set; }
    public string? LiveDifferenceContext { get; set; }
    public string? Note { get; set; }
}

internal sealed class HistoricalReadinessDocument
{
    public string Status { get; set; } = "NOT_EVALUATED";
    public string? Reason { get; set; }
    public bool SourceQueueWorkerParity { get; set; }
    public bool SourceRebuildParity { get; set; }
    public HistoricalStateSnapshot? PreState { get; set; }
    public string? PreStateFingerprint { get; set; }
    public HistoricalStateSnapshot? ImmediatePreState { get; set; }
    public string? ImmediatePreStateFingerprint { get; set; }
    public HistoricalStateSnapshot? PostState { get; set; }
    public string? PostStateFingerprint { get; set; }
    public int WorkerCalls { get; set; }
    public DateTimeOffset? WorkerStartedUtc { get; set; }
    public DateTimeOffset? WorkerFinishedUtc { get; set; }
    public string? PostStateReadError { get; set; }
}

internal sealed class HistoricalStateSnapshot
{
    public DateTimeOffset CapturedUtc { get; set; }
    public bool MovementAggregateInitialized { get; set; }
    public bool BalanceAggregateInitialized { get; set; }
    public Dictionary<string, long> StatusCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<HistoricalQueueRow> NonTerminalRows { get; set; } = [];
    public long DeadLetterCount { get; set; }
    public long UnknownStatusCount { get; set; }
    public DateTime? EarliestAuthorizedBalanceDate { get; set; }
}

internal sealed class HistoricalQueueRow
{
    public long Id { get; set; }
    public long WarehouseId { get; set; }
    public long ProductId { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string Status { get; set; } = "";
    public int RetryCount { get; set; }
    public int RequestedVersion { get; set; }
    public int? ClaimedVersion { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public DateTime? NextRetryUtc { get; set; }
    public bool RelevantToReport { get; set; }
}
