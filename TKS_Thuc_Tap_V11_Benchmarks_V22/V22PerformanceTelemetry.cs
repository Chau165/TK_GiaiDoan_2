using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Benchmarks_V22;

internal static class V22PerformanceTelemetry
{
    private const string CsvHeader = "RunId,BlockId,TargetDatabase,SampleUtc,Status,FreeRamMb,CpuPercent,DatabaseId,ActiveRequests,BlockingRequests,ActiveRequestGrantKB,RequestedGrantKB,GrantedGrantKB,PendingMemoryGrants,ResourceSemaphoreWaiters,ActiveRequestLogicalReadsDiagnostic,TempdbServerUsedKBDiagnostic,MemoryGrantsPendingCounterDiagnostic,DeadlockCounterDiagnostic,ErrorCode,TelemetrySchemaVersion,SampleIndex,SampleStartedUtc,SampleCompletedUtc,ElapsedMs,QueryId,QueryPhase,CommandTimeoutSeconds,ConnectionState,ExceptionType,SqlErrorNumber,SqlErrorState,SqlErrorClass,SafeErrorMessage,IsTimeout,CancellationRequested,PreviousSampleStillRunning,TelemetryProcessId,TargetProcessIds";
    private const string TelemetrySchemaVersion = "warehouse-benchmark-v22-telemetry-csv/2";
    private const string SampleQueryId = "SAMPLE_SQL_DMV_AGGREGATE/2";
    private const string ResidueQueryId = "FINAL_SQL_RESIDUE/2";
    private const int CommandTimeoutSeconds = 5;
    private const string SampleSql = """
        SET NOCOUNT ON;
        DECLARE @DbId int = DB_ID();
        DECLARE @Requests TABLE
        (
            session_id int NOT NULL,
            request_id int NOT NULL,
            blocking_session_id int NOT NULL,
            granted_query_memory bigint NOT NULL,
            logical_reads bigint NOT NULL
        );
        INSERT INTO @Requests(session_id, request_id, blocking_session_id, granted_query_memory, logical_reads)
        SELECT session_id, request_id, COALESCE(blocking_session_id, 0),
               COALESCE(CONVERT(bigint, granted_query_memory), 0), COALESCE(CONVERT(bigint, logical_reads), 0)
        FROM sys.dm_exec_requests
        WHERE database_id = @DbId AND session_id <> @@SPID;

        DECLARE @ActiveRequests bigint = 0, @BlockingRequests bigint = 0,
                @ActiveRequestGrantKB bigint = 0, @RequestedGrantKB bigint = 0,
                @GrantedGrantKB bigint = 0, @PendingMemoryGrants bigint = 0,
                @ResourceSemaphoreWaiters bigint = 0, @ActiveRequestLogicalReads bigint = 0,
                @TempdbUsedKB bigint = 0, @MemoryGrantsPendingCounter bigint = 0,
                @DeadlockCounter bigint = 0;

        SELECT @ActiveRequests = COUNT_BIG(*),
               @BlockingRequests = COALESCE(SUM(CONVERT(bigint, CASE WHEN blocking_session_id <> 0 THEN 1 ELSE 0 END)), 0),
               @ActiveRequestGrantKB = COALESCE(SUM(granted_query_memory), 0) * 8,
               @ActiveRequestLogicalReads = COALESCE(SUM(logical_reads), 0)
        FROM @Requests;

        SELECT @RequestedGrantKB = COALESCE(SUM(CONVERT(bigint, g.requested_memory_kb)), 0),
               @GrantedGrantKB = COALESCE(SUM(CONVERT(bigint, g.granted_memory_kb)), 0),
               @PendingMemoryGrants = COALESCE(SUM(CONVERT(bigint, CASE WHEN g.grant_time IS NULL THEN 1 ELSE 0 END)), 0)
        FROM sys.dm_exec_query_memory_grants AS g
        INNER JOIN @Requests AS r ON r.session_id = g.session_id AND r.request_id = g.request_id;

        SELECT @ResourceSemaphoreWaiters = COUNT_BIG(*)
        FROM sys.dm_os_waiting_tasks AS wt
        INNER JOIN sys.dm_exec_requests AS r ON r.session_id = wt.session_id
        WHERE r.database_id = @DbId AND wt.wait_type = N'RESOURCE_SEMAPHORE';

        SELECT @TempdbUsedKB = COALESCE(SUM(CONVERT(bigint, user_object_reserved_page_count + internal_object_reserved_page_count + version_store_reserved_page_count + mixed_extent_page_count)) * 8, 0)
        FROM tempdb.sys.dm_db_file_space_usage;

        SELECT TOP (1) @MemoryGrantsPendingCounter = CONVERT(bigint, cntr_value)
        FROM sys.dm_os_performance_counters WHERE counter_name = N'Memory Grants Pending';
        SELECT TOP (1) @DeadlockCounter = CONVERT(bigint, cntr_value)
        FROM sys.dm_os_performance_counters
        WHERE counter_name = N'Number of Deadlocks/sec' AND instance_name = N'_Total';

        SELECT CONVERT(char(33), SYSUTCDATETIME(), 126) AS SampleUtc,
               DB_NAME() AS DatabaseName, @DbId AS DatabaseId,
               @ActiveRequests AS ActiveRequests, @BlockingRequests AS BlockingRequests,
               @ActiveRequestGrantKB AS ActiveRequestGrantKB,
               @RequestedGrantKB AS RequestedGrantKB, @GrantedGrantKB AS GrantedGrantKB,
               @PendingMemoryGrants AS PendingMemoryGrants,
               @ResourceSemaphoreWaiters AS ResourceSemaphoreWaiters,
               @ActiveRequestLogicalReads AS ActiveRequestLogicalReadsDiagnostic,
               @TempdbUsedKB AS TempdbUsedKB,
               @MemoryGrantsPendingCounter AS MemoryGrantsPendingCounter,
               @DeadlockCounter AS DeadlockCounter;
        """;

    private const string ResidueSql = """
        SET NOCOUNT ON;
        DECLARE @DbId int = DB_ID();
        SELECT
            DB_NAME() AS DatabaseName,
            DB_ID() AS DatabaseId,
            COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE database_id = @DbId AND session_id <> @@SPID), 0) AS ActiveRequests,
            CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) AS SqlServerProductVersion,
            COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_exec_query_memory_grants g INNER JOIN sys.dm_exec_requests r ON r.session_id = g.session_id AND r.request_id = g.request_id WHERE r.database_id = @DbId AND g.grant_time IS NULL AND g.session_id <> @@SPID), 0) AS PendingMemoryGrants,
            COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_os_waiting_tasks wt JOIN sys.dm_exec_requests r ON r.session_id = wt.session_id WHERE r.database_id = @DbId AND wt.wait_type = N'RESOURCE_SEMAPHORE'), 0) AS ResourceSemaphoreWaiters;
        """;

    internal static async Task<int> RunAsync(string[] args)
    {
        var options = ParseOptions(args);
        var runId = Required(options, "run-id");
        var blockId = Required(options, "block-id");
        var targetDatabase = Required(options, "target-database");
        var outputPath = Path.GetFullPath(Required(options, "output"));
        var probe = options.TryGetValue("probe", out var probeValue) && bool.TryParse(probeValue, out var parsedProbe) && parsedProbe;
        var targetPids = probe ? Array.Empty<int>() : Required(options, "target-process-ids").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var childProcessId) && childProcessId > 0
                ? childProcessId : throw new ArgumentException("Target process IDs must be positive integers."))
            .Distinct().ToArray();
        if (!probe && targetPids.Length == 0) throw new ArgumentException("At least one owned target process is required.");
        if (string.IsNullOrWhiteSpace(CConfig.TKS_Thuc_Tap_V11_Conn_String))
            throw new InvalidDataException("TKS_V22_CONNECTION_STRING is not configured for this process.");
        EnsureNewFile(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        await using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false));
        await writer.WriteLineAsync(CsvHeader);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        SystemCpuTimes? systemTimes = null;
        var sampleCount = 0;
        SqlConnection? connection = null;
        using var sampleGate = new SemaphoreSlim(1, 1);
        try
        {
            while (true)
            {
                if (!probe && sampleCount > 0 && targetPids.All(childProcessId => !IsProcessAlive(childProcessId))) break;
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException exception)
                {
                    sampleCount++;
                    var canceledAt = DateTimeOffset.UtcNow;
                    var facts = CreateErrorFacts(exception);
                    var canceledRow = FormatErrorCsvRow(runId, blockId, targetDatabase, sampleCount,
                        canceledAt, canceledAt, 0, "SAMPLE_SQL_DMV_AGGREGATE/2", "SAMPLE_DELAY",
                        CommandTimeoutSeconds, connection?.State.ToString() ?? "NOT_CREATED", facts, false,
                        Environment.ProcessId, string.Join(';', targetPids));
                    await writer.WriteLineAsync(canceledRow).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                    break;
                }

                sampleCount++;
                var sampleStartedUtc = DateTimeOffset.UtcNow;
                var attemptStartedTimestamp = Stopwatch.GetTimestamp();
                double? freeRamMb = null;
                double? cpuPercent = null;
                var queryPhase = "HOST_SNAPSHOT";
                var row = await RunSampleAttemptAsync(
                    sampleGate,
                    async () =>
                    {
                        freeRamMb = ReadFreeRamMb();
                        var cpuBaseline = systemTimes ?? ReadSystemTimes();
                        cpuPercent = ReadCpuPercent(ref cpuBaseline);
                        systemTimes = cpuBaseline;

                        queryPhase = "CREATE_CONNECTION";
                        var activeConnection = connection ??= new SqlConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String);
                        queryPhase = "OPEN_CONNECTION";
                        if (activeConnection.State != System.Data.ConnectionState.Open)
                        {
                            if (activeConnection.State == System.Data.ConnectionState.Broken)
                                activeConnection.Close();
                            await activeConnection.OpenAsync(cancellation.Token).ConfigureAwait(false);
                        }

                        queryPhase = "EXECUTE_READER";
                        await using var command = activeConnection.CreateCommand();
                        command.CommandText = SampleSql;
                        command.CommandTimeout = CommandTimeoutSeconds;
                        await using var reader = await command.ExecuteReaderAsync(cancellation.Token).ConfigureAwait(false);
                        queryPhase = "READ_AND_MAP";
                        if (!await reader.ReadAsync(cancellation.Token).ConfigureAwait(false))
                            throw new InvalidDataException("Telemetry query returned no identity row.");
                        var databaseName = reader.GetString(reader.GetOrdinal("DatabaseName"));
                        var databaseId = Convert.ToInt32(reader["DatabaseId"], CultureInfo.InvariantCulture);
                        if (!string.Equals(databaseName, targetDatabase, StringComparison.Ordinal) || databaseId != 5)
                            throw new InvalidDataException("Telemetry database identity mismatch.");
                        var sqlSampleUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("SampleUtc")), CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                        var cells = new List<string>
                        {
                            runId, blockId, targetDatabase,
                            new DateTimeOffset(sqlSampleUtc).ToString("O", CultureInfo.InvariantCulture),
                            "VALID", FormatNumber(freeRamMb), FormatNumber(cpuPercent, "F2")
                        };
                        foreach (var name in new[] { "DatabaseId", "ActiveRequests", "BlockingRequests", "ActiveRequestGrantKB", "RequestedGrantKB", "GrantedGrantKB", "PendingMemoryGrants", "ResourceSemaphoreWaiters", "ActiveRequestLogicalReadsDiagnostic", "TempdbUsedKB", "MemoryGrantsPendingCounter", "DeadlockCounter" })
                            cells.Add(Convert.ToString(reader[name], CultureInfo.InvariantCulture) ?? "");
                        cells.Add("");
                        queryPhase = "COMPLETE";
                        AppendDiagnosticCells(cells, sampleCount, sampleStartedUtc, DateTimeOffset.UtcNow,
                            Stopwatch.GetElapsedTime(attemptStartedTimestamp).TotalMilliseconds,
                            SampleQueryId, queryPhase, CommandTimeoutSeconds, activeConnection.State.ToString(),
                            null, false, Environment.ProcessId, string.Join(';', targetPids));
                        return string.Join(',', cells.Select(Csv));
                    },
                    (exception, previousSampleStillRunning) => FormatErrorCsvRow(
                        runId, blockId, targetDatabase, sampleCount, sampleStartedUtc, DateTimeOffset.UtcNow,
                        Stopwatch.GetElapsedTime(attemptStartedTimestamp).TotalMilliseconds,
                        SampleQueryId, queryPhase, CommandTimeoutSeconds, connection?.State.ToString() ?? "NOT_CREATED",
                        CreateErrorFacts(exception), previousSampleStillRunning, Environment.ProcessId,
                        string.Join(';', targetPids))).ConfigureAwait(false);

                if (connection?.State == System.Data.ConnectionState.Broken)
                    connection.Close();
                await writer.WriteLineAsync(row).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
                if (probe || cancellation.IsCancellationRequested) break;
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            if (connection is not null)
                await connection.DisposeAsync().ConfigureAwait(false);
        }
        return 0;
    }

    internal static async Task<int> RunResidueAsync(string[] args)
    {
        var options = ParseOptions(args);
        var runId = Required(options, "run-id");
        var blockId = Required(options, "block-id");
        var targetDatabase = Required(options, "target-database");
        var outputPath = Path.GetFullPath(Required(options, "output"));
        if (string.IsNullOrWhiteSpace(CConfig.TKS_Thuc_Tap_V11_Conn_String))
            throw new InvalidDataException("TKS_V22_CONNECTION_STRING is not configured for this process.");
        EnsureNewFile(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var startedUtc = DateTimeOffset.UtcNow;
        var startedTimestamp = Stopwatch.GetTimestamp();
        var queryPhase = "OPEN_CONNECTION";
        var connectionState = "Closed";
        try
        {
            await using var connection = new SqlConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String);
            await connection.OpenAsync().ConfigureAwait(false);
            connectionState = connection.State.ToString();
            queryPhase = "EXECUTE_READER";
            await using var command = connection.CreateCommand();
            command.CommandText = ResidueSql;
            command.CommandTimeout = CommandTimeoutSeconds;
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            if (!await reader.ReadAsync().ConfigureAwait(false)) throw new InvalidDataException("Residue query returned no result.");
            var name = reader.GetString(reader.GetOrdinal("DatabaseName"));
            var databaseId = Convert.ToInt32(reader["DatabaseId"], CultureInfo.InvariantCulture);
            var sqlServerProductVersion = reader.GetString(reader.GetOrdinal("SqlServerProductVersion"));
            var active = reader.GetInt64(reader.GetOrdinal("ActiveRequests"));
            var grants = reader.GetInt64(reader.GetOrdinal("PendingMemoryGrants"));
            var waiters = reader.GetInt64(reader.GetOrdinal("ResourceSemaphoreWaiters"));
            var passed = string.Equals(name, targetDatabase, StringComparison.Ordinal) && databaseId == 5 && active == 0 && grants == 0 && waiters == 0;
            var result = new
            {
                SchemaVersion = "warehouse-benchmark-v22-residue/2", RunId = runId, BlockId = blockId,
                QueryId = ResidueQueryId, QueryPhase = "COMPLETE", TargetDatabase = name, DatabaseId = databaseId,
                SqlServerProductVersion = sqlServerProductVersion,
                ActiveRequests = active, PendingMemoryGrants = grants, ResourceSemaphoreWaiters = waiters,
                Status = passed ? "RESIDUE_CLEAN" : "RESIDUE_PRESENT", CapturedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ElapsedMs = Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds,
                CommandTimeoutSeconds = CommandTimeoutSeconds, ConnectionState = connection.State.ToString(),
                ConnectionStringPersisted = false
            };
            await WriteNewJsonAsync(outputPath, result).ConfigureAwait(false);
            return passed ? 0 : 1;
        }
        catch (Exception exception)
        {
            var failure = new
            {
                SchemaVersion = "warehouse-benchmark-v22-residue/2", RunId = runId, BlockId = blockId,
                QueryId = ResidueQueryId, QueryPhase = queryPhase, TargetDatabase = targetDatabase,
                Status = "HARNESS_FAILURE", Failure = CreateErrorFacts(exception),
                StartedUtc = startedUtc.ToString("O", CultureInfo.InvariantCulture),
                CapturedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ElapsedMs = Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds,
                CommandTimeoutSeconds = CommandTimeoutSeconds, ConnectionState = connectionState,
                ConnectionStringPersisted = false
            };
            await WriteNewJsonAsync(outputPath, failure).ConfigureAwait(false);
            return 1;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException("Options must be supplied as --name value pairs.");
            if (!result.TryAdd(args[index][2..], args[index + 1])) throw new ArgumentException("Duplicate option.");
        }
        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"Required option missing: --{name}");

    private static void EnsureNewFile(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Telemetry evidence paths are never overwritten.");
    }

    private sealed record TelemetryErrorFacts(
        string ErrorCode,
        string ExceptionType,
        string SqlErrorNumber,
        string SqlErrorState,
        string SqlErrorClass,
        string SafeMessage,
        bool IsTimeout,
        bool CancellationRequested);

    private static async Task<string> RunSampleAttemptAsync(
        SemaphoreSlim sampleGate,
        Func<Task<string>> execute,
        Func<Exception, bool, string> formatFailure)
    {
        if (!sampleGate.Wait(0))
            return formatFailure(new InvalidOperationException("A previous telemetry sample is still running."), true);
        try
        {
            return await execute().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return formatFailure(exception, false);
        }
        finally
        {
            sampleGate.Release();
        }
    }

    private static string FormatErrorCsvRow(
        string runId,
        string blockId,
        string targetDatabase,
        int sampleIndex,
        DateTimeOffset sampleStartedUtc,
        DateTimeOffset sampleCompletedUtc,
        double elapsedMs,
        string queryId,
        string queryPhase,
        int commandTimeoutSeconds,
        string connectionState,
        TelemetryErrorFacts error,
        bool previousSampleStillRunning,
        int telemetryProcessId,
        string targetProcessIds)
    {
        var cells = new List<string>
        {
            runId, blockId, targetDatabase,
            sampleStartedUtc.ToString("O", CultureInfo.InvariantCulture), "TELEMETRY_ERROR",
            "", "", "", "", "", "", "", "", "", "", "", "", "", "", error.ErrorCode
        };
        AppendDiagnosticCells(cells, sampleIndex, sampleStartedUtc, sampleCompletedUtc, elapsedMs,
            queryId, queryPhase, commandTimeoutSeconds, connectionState, error,
            previousSampleStillRunning, telemetryProcessId, targetProcessIds);
        return string.Join(',', cells.Select(Csv));
    }

    private static void AppendDiagnosticCells(
        List<string> cells,
        int sampleIndex,
        DateTimeOffset sampleStartedUtc,
        DateTimeOffset sampleCompletedUtc,
        double elapsedMs,
        string queryId,
        string queryPhase,
        int commandTimeoutSeconds,
        string connectionState,
        TelemetryErrorFacts? error,
        bool previousSampleStillRunning,
        int telemetryProcessId,
        string targetProcessIds)
    {
        cells.Add(TelemetrySchemaVersion);
        cells.Add(sampleIndex.ToString(CultureInfo.InvariantCulture));
        cells.Add(sampleStartedUtc.ToString("O", CultureInfo.InvariantCulture));
        cells.Add(sampleCompletedUtc.ToString("O", CultureInfo.InvariantCulture));
        cells.Add(elapsedMs.ToString("F3", CultureInfo.InvariantCulture));
        cells.Add(queryId);
        cells.Add(queryPhase);
        cells.Add(commandTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        cells.Add(connectionState);
        cells.Add(error?.ExceptionType ?? "");
        cells.Add(error?.SqlErrorNumber ?? "");
        cells.Add(error?.SqlErrorState ?? "");
        cells.Add(error?.SqlErrorClass ?? "");
        cells.Add(error?.SafeMessage ?? "");
        cells.Add((error?.IsTimeout ?? false).ToString().ToLowerInvariant());
        cells.Add((error?.CancellationRequested ?? false).ToString().ToLowerInvariant());
        cells.Add(previousSampleStillRunning.ToString().ToLowerInvariant());
        cells.Add(telemetryProcessId.ToString(CultureInfo.InvariantCulture));
        cells.Add(targetProcessIds);
    }

    private static TelemetryErrorFacts CreateErrorFacts(Exception exception)
    {
        var root = exception.GetBaseException();
        var sql = exception as SqlException ?? root as SqlException;
        var detail = sql is { Errors.Count: > 0 } ? sql.Errors[0] : null;
        return CreateErrorFacts(
            sql?.Number,
            detail?.State,
            detail?.Class,
            root.GetType().FullName ?? root.GetType().Name,
            root.Message,
            root is OperationCanceledException,
            root is TimeoutException);
    }

    private static TelemetryErrorFacts CreateErrorFacts(
        int? sqlErrorNumber,
        byte? sqlErrorState,
        byte? sqlErrorClass,
        string exceptionType,
        string message,
        bool cancellationRequested,
        bool timeoutException = false)
    {
        var errorCode = sqlErrorNumber.HasValue
            ? $"SQL_{sqlErrorNumber.Value.ToString(CultureInfo.InvariantCulture)}"
            : cancellationRequested ? "CANCELED"
            : timeoutException ? "TIMEOUT"
            : exceptionType.Split('.').Last();
        return new TelemetryErrorFacts(
            errorCode,
            exceptionType,
            sqlErrorNumber?.ToString(CultureInfo.InvariantCulture) ?? "",
            sqlErrorState?.ToString(CultureInfo.InvariantCulture) ?? "",
            sqlErrorClass?.ToString(CultureInfo.InvariantCulture) ?? "",
            SanitizeErrorMessage(message),
            sqlErrorNumber == -2 || timeoutException,
            cancellationRequested);
    }

    private static string SanitizeErrorMessage(string message)
    {
        var safe = Regex.Replace(message ?? "",
            @"(?i)\b(password|pwd|user\s*id|uid|data\s*source|server|address|addr|initial\s*catalog|database|access\s*token|account\s*key)\s*=\s*([^;,\s]+)",
            "$1=[REDACTED]");
        return safe.Length <= 512 ? safe : safe[..512];
    }

    private static string FormatNumber(double? value, string format = "G17") =>
        value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "";

    private static string Csv(string value) => value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n')
        ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    private static async Task WriteNewJsonAsync(string path, object value)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, value, new JsonSerializerOptions { WriteIndented = true }).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray()).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    internal static int RunCsvSelfTest() => RunCsvSelfTestAsync().GetAwaiter().GetResult();

    private static async Task<int> RunCsvSelfTestAsync()
    {
        var tests = new List<object>();
        void Add(string id, string input, string expected, string actual, bool passed) =>
            tests.Add(new { TestId = id, Input = input, Expected = expected, Actual = actual, Status = passed ? "PASS" : "FAIL" });

        var headerCount = CsvHeader.Split(',').Length;
        Add("TELEMETRY_V2_HEADER_COLUMNS", "current telemetry header", "39",
            headerCount.ToString(CultureInfo.InvariantCulture), headerCount == 39);

        var now = DateTimeOffset.UnixEpoch;
        var timeout = CreateErrorFacts(-2, 1, 11, "Microsoft.Data.SqlClient.SqlException",
            "Execution Timeout Expired. Server=private-host;Password=private-value", false);
        var timeoutRow = FormatErrorCsvRow("offline-run", "offline-block", "offline-db", 1, now, now.AddMilliseconds(5),
            5, SampleQueryId, "EXECUTE_READER", CommandTimeoutSeconds, "Open", timeout, false, 1234, "100;101");
        var timeoutColumnCount = CountCsvFields(timeoutRow);
        Add("TELEMETRY_SQL_MINUS_2_DETAILS", "synthetic SQL error number -2", "SQL_-2|timeout=true|columns=39",
            $"{timeout.ErrorCode}|timeout={timeout.IsTimeout.ToString().ToLowerInvariant()}|columns={timeoutColumnCount}",
            timeout.ErrorCode == "SQL_-2" && timeout.IsTimeout && timeoutColumnCount == headerCount);
        Add("TELEMETRY_ERROR_SECRET_REDACTION", "connection attributes in provider message", "no private-host or private-value",
            timeout.SafeMessage, !timeout.SafeMessage.Contains("private-host", StringComparison.Ordinal) && !timeout.SafeMessage.Contains("private-value", StringComparison.Ordinal));

        var nonTimeoutSql = CreateErrorFacts(208, 2, 16, "Microsoft.Data.SqlClient.SqlException", "Invalid object name.", false);
        Add("TELEMETRY_NON_TIMEOUT_SQL_ERROR", "synthetic SQL error number 208", "SQL_208|timeout=false",
            $"{nonTimeoutSql.ErrorCode}|timeout={nonTimeoutSql.IsTimeout.ToString().ToLowerInvariant()}",
            nonTimeoutSql.ErrorCode == "SQL_208" && !nonTimeoutSql.IsTimeout);

        var connectionFailure = CreateErrorFacts(new IOException("connection refused"));
        Add("TELEMETRY_CONNECTION_FAILURE_CAPTURE", "connection open failure", "IOException|phase metadata retained",
            connectionFailure.ExceptionType.Split('.').Last(), connectionFailure.ExceptionType.EndsWith("IOException", StringComparison.Ordinal));
        var canceled = CreateErrorFacts(new OperationCanceledException("cancelled"));
        Add("TELEMETRY_CANCELLATION_CAPTURE", "canceled SQL attempt", "CANCELED|requested=true",
            $"{canceled.ErrorCode}|requested={canceled.CancellationRequested.ToString().ToLowerInvariant()}",
            canceled.ErrorCode == "CANCELED" && canceled.CancellationRequested);

        using (var overlapGate = new SemaphoreSlim(1, 1))
        {
            overlapGate.Wait();
            var overlap = await RunSampleAttemptAsync(overlapGate, () => Task.FromResult("UNEXPECTED"),
                (exception, previousRunning) => $"ERROR|previous={previousRunning.ToString().ToLowerInvariant()}").ConfigureAwait(false);
            overlapGate.Release();
            Add("TELEMETRY_PREVIOUS_SAMPLE_OVERLAP", "sample gate already held", "ERROR|previous=true", overlap,
                overlap == "ERROR|previous=true");
        }

        using (var recoveryGate = new SemaphoreSlim(1, 1))
        {
            var attempts = 0;
            var outcomes = new List<string>();
            for (var index = 0; index < 2; index++)
            {
                var outcome = await RunSampleAttemptAsync(recoveryGate, async () =>
                {
                    attempts++;
                    await Task.Yield();
                    if (attempts == 1) throw new TimeoutException("synthetic query timeout");
                    return "VALID";
                }, (exception, _) => exception is TimeoutException ? "SQL_-2" : "ERROR").ConfigureAwait(false);
                outcomes.Add(outcome);
            }
            var sequence = string.Join('|', outcomes);
            Add("TELEMETRY_TIMEOUT_THEN_SUCCESS", "first query timeout; next independent sample succeeds", "SQL_-2|VALID", sequence,
                sequence == "SQL_-2|VALID" && attempts == 2);
        }

        var passedCount = tests.Count(test => (string)test.GetType().GetProperty("Status")!.GetValue(test)! == "PASS");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            SchemaVersion = "warehouse-benchmark-v22-telemetry-self-test/2",
            TestCount = tests.Count,
            Passed = passedCount,
            Failed = tests.Count - passedCount,
            Tests = tests
        }, new JsonSerializerOptions { WriteIndented = true }));
        return passedCount == tests.Count ? 0 : 1;
    }

    private static int CountCsvFields(string line)
    {
        var fieldCount = 1;
        var insideQuotes = false;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '"')
            {
                if (insideQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    index++;
                    continue;
                }
                insideQuotes = !insideQuotes;
            }
            else if (line[index] == ',' && !insideQuotes)
            {
                fieldCount++;
            }
        }
        return insideQuotes ? -1 : fieldCount;
    }

    private static bool IsProcessAlive(int childProcessId)
    {
        try { using var process = Process.GetProcessById(childProcessId); return !process.HasExited; }
        catch { return false; }
    }

    private static double ReadFreeRamMb()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status)) throw new InvalidOperationException("Host memory telemetry unavailable.");
        return Math.Round(status.AvailablePhysical / 1048576d, 1);
    }

    private static SystemCpuTimes ReadSystemTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) throw new InvalidOperationException("Host CPU telemetry unavailable.");
        return new SystemCpuTimes(ToUInt64(idle), ToUInt64(kernel), ToUInt64(user));
    }

    private static double ReadCpuPercent(ref SystemCpuTimes previous)
    {
        var current = ReadSystemTimes();
        var idle = current.Idle >= previous.Idle ? current.Idle - previous.Idle : 0;
        var kernel = current.Kernel >= previous.Kernel ? current.Kernel - previous.Kernel : 0;
        var user = current.User >= previous.User ? current.User - previous.User : 0;
        previous = current;
        var total = kernel + user;
        return total <= 0 ? 0 : Math.Clamp((total - idle) * 100d / total, 0, 100);
    }

    private static ulong ToUInt64(System.Runtime.InteropServices.ComTypes.FILETIME value) =>
        ((ulong)(uint)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;

    private struct SystemCpuTimes(ulong idle, ulong kernel, ulong user)
    {
        internal ulong Idle = idle;
        internal ulong Kernel = kernel;
        internal ulong User = user;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        internal uint Length;
        internal uint MemoryLoad;
        internal ulong TotalPhysical;
        internal ulong AvailablePhysical;
        internal ulong TotalPageFile;
        internal ulong AvailablePageFile;
        internal ulong TotalVirtual;
        internal ulong AvailableVirtual;
        internal ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME idle, out System.Runtime.InteropServices.ComTypes.FILETIME kernel, out System.Runtime.InteropServices.ComTypes.FILETIME user);
}
