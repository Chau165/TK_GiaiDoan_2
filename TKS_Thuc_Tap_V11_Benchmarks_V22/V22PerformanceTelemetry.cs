using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Benchmarks_V22;

internal static class V22PerformanceTelemetry
{
    private const string SampleSql = """
        SET NOCOUNT ON;
        DECLARE @DbId int = DB_ID();
        SELECT
            CONVERT(char(33), SYSUTCDATETIME(), 126) AS SampleUtc,
            DB_NAME() AS DatabaseName,
            DB_ID() AS DatabaseId,
            COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE database_id = @DbId AND session_id <> @@SPID), 0) AS ActiveRequests,
            COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE database_id = @DbId AND session_id <> @@SPID AND blocking_session_id <> 0), 0) AS BlockingRequests,
            COALESCE((SELECT SUM(CONVERT(bigint, granted_query_memory)) * 8 FROM sys.dm_exec_requests WHERE database_id = @DbId AND session_id <> @@SPID), 0) AS ActiveRequestGrantKB,
            COALESCE((SELECT SUM(CONVERT(bigint, g.requested_memory_kb)) FROM sys.dm_exec_query_memory_grants g INNER JOIN sys.dm_exec_requests r ON r.session_id = g.session_id AND r.request_id = g.request_id WHERE r.database_id = @DbId AND g.session_id <> @@SPID), 0) AS RequestedGrantKB,
            COALESCE((SELECT SUM(CONVERT(bigint, g.granted_memory_kb)) FROM sys.dm_exec_query_memory_grants g INNER JOIN sys.dm_exec_requests r ON r.session_id = g.session_id AND r.request_id = g.request_id WHERE r.database_id = @DbId AND g.session_id <> @@SPID), 0) AS GrantedGrantKB,
            COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_exec_query_memory_grants g INNER JOIN sys.dm_exec_requests r ON r.session_id = g.session_id AND r.request_id = g.request_id WHERE r.database_id = @DbId AND g.grant_time IS NULL AND g.session_id <> @@SPID), 0) AS PendingMemoryGrants,
            COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_os_waiting_tasks wt JOIN sys.dm_exec_requests r ON r.session_id = wt.session_id WHERE r.database_id = @DbId AND wt.wait_type = N'RESOURCE_SEMAPHORE'), 0) AS ResourceSemaphoreWaiters,
            COALESCE((SELECT SUM(CONVERT(bigint, r.logical_reads)) FROM sys.dm_exec_requests r WHERE r.database_id = @DbId AND r.session_id <> @@SPID), 0) AS ActiveRequestLogicalReadsDiagnostic,
            COALESCE((SELECT SUM(CONVERT(bigint, fs.user_object_reserved_page_count + fs.internal_object_reserved_page_count + fs.version_store_reserved_page_count + fs.mixed_extent_page_count)) * 8 FROM tempdb.sys.dm_db_file_space_usage fs), 0) AS TempdbUsedKB,
            COALESCE((SELECT TOP (1) CONVERT(bigint, cntr_value) FROM sys.dm_os_performance_counters WHERE counter_name = N'Memory Grants Pending'), 0) AS MemoryGrantsPendingCounter,
            COALESCE((SELECT TOP (1) CONVERT(bigint, cntr_value) FROM sys.dm_os_performance_counters WHERE counter_name = N'Number of Deadlocks/sec' AND instance_name = N'_Total'), 0) AS DeadlockCounter;
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
        await writer.WriteLineAsync("RunId,BlockId,TargetDatabase,SampleUtc,Status,FreeRamMb,CpuPercent,DatabaseId,ActiveRequests,BlockingRequests,ActiveRequestGrantKB,RequestedGrantKB,GrantedGrantKB,PendingMemoryGrants,ResourceSemaphoreWaiters,ActiveRequestLogicalReadsDiagnostic,TempdbServerUsedKBDiagnostic,MemoryGrantsPendingCounterDiagnostic,DeadlockCounterDiagnostic,ErrorCode");
        var systemTimes = ReadSystemTimes();
        var sampleCount = 0;
        await using var connection = new SqlConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SampleSql;
        command.CommandTimeout = 5;

        while (true)
        {
            if (!probe && sampleCount > 0 && targetPids.All(childProcessId => !IsProcessAlive(childProcessId))) break;
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            var sampleUtc = DateTimeOffset.UtcNow;
            var freeRamMb = ReadFreeRamMb();
            var cpuPercent = ReadCpuPercent(ref systemTimes);
            try
            {
                await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                if (!await reader.ReadAsync().ConfigureAwait(false)) throw new InvalidDataException("Telemetry query returned no identity row.");
                var databaseName = reader.GetString(reader.GetOrdinal("DatabaseName"));
                var databaseId = Convert.ToInt32(reader["DatabaseId"], CultureInfo.InvariantCulture);
                if (!string.Equals(databaseName, targetDatabase, StringComparison.Ordinal) || databaseId != 5)
                    throw new InvalidDataException("Telemetry database identity mismatch.");
                var sqlSampleUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("SampleUtc")), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                var cells = new List<string>
                {
                    runId, blockId, targetDatabase, new DateTimeOffset(sqlSampleUtc).ToString("O", CultureInfo.InvariantCulture),
                    "VALID", freeRamMb.ToString(CultureInfo.InvariantCulture), cpuPercent.ToString("F2", CultureInfo.InvariantCulture)
                };
                foreach (var name in new[] { "DatabaseId", "ActiveRequests", "BlockingRequests", "ActiveRequestGrantKB", "RequestedGrantKB", "GrantedGrantKB", "PendingMemoryGrants", "ResourceSemaphoreWaiters", "ActiveRequestLogicalReadsDiagnostic", "TempdbUsedKB", "MemoryGrantsPendingCounter", "DeadlockCounter" })
                    cells.Add(Convert.ToString(reader[name], CultureInfo.InvariantCulture) ?? "");
                cells.Add("");
                await writer.WriteLineAsync(string.Join(',', cells.Select(Csv))).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                var cells = new[]
                {
                    runId, blockId, targetDatabase, sampleUtc.ToString("O", CultureInfo.InvariantCulture), "TELEMETRY_ERROR",
                    freeRamMb.ToString(CultureInfo.InvariantCulture), cpuPercent.ToString("F2", CultureInfo.InvariantCulture),
                    "", "", "", "", "", "", "", "", "", "", "", SafeErrorCode(exception)
                };
                await writer.WriteLineAsync(string.Join(',', cells.Select(Csv))).ConfigureAwait(false);
            }
            await writer.FlushAsync().ConfigureAwait(false);
            sampleCount++;
            if (probe) break;
        }
        return 0;
    }

    internal static async Task<int> RunResidueAsync(string[] args)
    {
        var options = ParseOptions(args);
        var targetDatabase = Required(options, "target-database");
        var outputPath = Path.GetFullPath(Required(options, "output"));
        if (string.IsNullOrWhiteSpace(CConfig.TKS_Thuc_Tap_V11_Conn_String))
            throw new InvalidDataException("TKS_V22_CONNECTION_STRING is not configured for this process.");
        EnsureNewFile(outputPath);
        await using var connection = new SqlConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = ResidueSql;
        command.CommandTimeout = 5;
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
            SchemaVersion = "warehouse-benchmark-v22-residue/1", TargetDatabase = name, DatabaseId = databaseId,
            SqlServerProductVersion = sqlServerProductVersion,
            ActiveRequests = active, PendingMemoryGrants = grants, ResourceSemaphoreWaiters = waiters,
            Status = passed ? "RESIDUE_CLEAN" : "RESIDUE_PRESENT", CapturedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ConnectionStringPersisted = false
        };
        await using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, result, new JsonSerializerOptions { WriteIndented = true }).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray()).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
        return passed ? 0 : 1;
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

    private static string Csv(string value) => value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n')
        ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    private static string SafeErrorCode(Exception exception) => exception is SqlException sql ? $"SQL_{sql.Number}" : exception.GetType().Name;

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
