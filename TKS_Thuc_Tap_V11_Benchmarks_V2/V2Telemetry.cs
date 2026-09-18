using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace TKS_Thuc_Tap_V11_Benchmarks_V2;

public static class V2Telemetry
{
    private const string TelemetrySql = """
        SET NOCOUNT ON;
        DECLARE @DbId int = DB_ID();
        SELECT
            CONVERT(char(33), SYSUTCDATETIME(), 126) AS TimestampUtc,
            DB_NAME() AS DatabaseName,
            DB_ID() AS DatabaseId,
            COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE database_id = @DbId AND session_id <> @@SPID), 0) AS ActiveRequests,
            COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE database_id = @DbId AND session_id <> @@SPID AND blocking_session_id <> 0), 0) AS BlockingRequests,
            COALESCE((SELECT SUM(CONVERT(bigint, granted_query_memory)) * 8 FROM sys.dm_exec_requests WHERE database_id = @DbId AND session_id <> @@SPID), 0) AS ActiveRequestGrantKB,
            COALESCE((SELECT SUM(CONVERT(bigint, requested_memory_kb)) FROM sys.dm_exec_query_memory_grants g JOIN sys.dm_exec_sessions s ON s.session_id = g.session_id WHERE s.database_id = @DbId AND g.session_id <> @@SPID), 0) AS RequestedGrantKB,
            COALESCE((SELECT SUM(CONVERT(bigint, granted_memory_kb)) FROM sys.dm_exec_query_memory_grants g JOIN sys.dm_exec_sessions s ON s.session_id = g.session_id WHERE s.database_id = @DbId AND g.session_id <> @@SPID), 0) AS GrantedGrantKB,
            COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_exec_query_memory_grants g JOIN sys.dm_exec_sessions s ON s.session_id = g.session_id WHERE s.database_id = @DbId AND g.grant_time IS NULL AND g.session_id <> @@SPID), 0) AS PendingMemoryGrants,
            COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_os_waiting_tasks wt JOIN sys.dm_exec_sessions s ON s.session_id = wt.session_id WHERE s.database_id = @DbId AND wt.wait_type = N'RESOURCE_SEMAPHORE'), 0) AS ResourceSemaphoreWaiters,
            COALESCE((SELECT SUM(CONVERT(bigint, r.reads)) FROM sys.dm_exec_requests r WHERE r.database_id = @DbId AND r.session_id <> @@SPID), 0) AS LogicalReads,
            COALESCE((SELECT SUM(CONVERT(bigint, fs.user_object_reserved_page_count + fs.internal_object_reserved_page_count + fs.version_store_reserved_page_count + fs.mixed_extent_page_count)) * 8 FROM tempdb.sys.dm_db_file_space_usage fs), 0) AS TempdbUsedKB,
            COALESCE((SELECT TOP (1) CONVERT(bigint, cntr_value) FROM sys.dm_os_performance_counters WHERE counter_name = N'Memory Grants Pending'), 0) AS MemoryGrantsPendingCounter,
            COALESCE((SELECT TOP (1) CONVERT(bigint, cntr_value) FROM sys.dm_os_performance_counters WHERE counter_name = N'Number of Deadlocks/sec' AND instance_name = N'_Total'), 0) AS DeadlockCounter;
        """;

    public static async Task<int> RunAsync(
        V2Settings p_settings,
        int p_targetProcessId,
        string p_outputPath,
        string p_blockId)
    {
        p_settings.ConfigureDataAccess();
        var v_directory = Path.GetDirectoryName(p_outputPath);
        if (!string.IsNullOrWhiteSpace(v_directory))
            Directory.CreateDirectory(v_directory);

        await using var v_connection = new SqlConnection(p_settings.ConnectionString);
        await v_connection.OpenAsync();
        await using var v_command = v_connection.CreateCommand();
        v_command.CommandText = TelemetrySql;
        v_command.CommandTimeout = 5;

        await using var v_writer = new StreamWriter(
            p_outputPath,
            false,
            new UTF8Encoding(false));
        await v_writer.WriteLineAsync(
            "BlockId,TimestampUtc,DatabaseName,DatabaseId,ActiveRequests,BlockingRequests,ActiveRequestGrantKB,RequestedGrantKB,GrantedGrantKB,PendingMemoryGrants,ResourceSemaphoreWaiters,LogicalReads,TempdbUsedKB,MemoryGrantsPendingCounter,DeadlockCounter");

        var v_sampleCount = 0;
        while (true)
        {
            if (v_sampleCount > 0 && !IsProcessAlive(p_targetProcessId))
                break;

            try
            {
                await using var v_reader = await v_command.ExecuteReaderAsync();
                if (await v_reader.ReadAsync())
                {
                    var v_values = new object[v_reader.FieldCount];
                    v_reader.GetValues(v_values);
                    var v_line = string.Join(
                        ",",
                        new[] { p_blockId }
                            .Concat(v_values.Select(ToCsvValue)));
                    await v_writer.WriteLineAsync(v_line);
                    await v_writer.FlushAsync();
                }
            }
            catch (Exception p_exception)
            {
                await v_writer.WriteLineAsync(
                    string.Join(
                        ",",
                        new[]
                        {
                            p_blockId,
                            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                            "TELEMETRY_ERROR",
                            "",
                            "",
                            "",
                            "",
                            "",
                            "",
                            "",
                            "",
                            "",
                            "",
                            "",
                            "",
                            p_exception.GetType().Name
                        }));
                await v_writer.FlushAsync();
            }

            v_sampleCount++;
            await Task.Delay(V2Constants.TelemetryIntervalMilliseconds);
        }

        return 0;
    }

    private static bool IsProcessAlive(int p_processId)
    {
        try
        {
            using var v_process = Process.GetProcessById(p_processId);
            return !v_process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static string ToCsvValue(object? p_value)
    {
        if (p_value is null || p_value == DBNull.Value)
            return "";

        var v_text = Convert.ToString(p_value, CultureInfo.InvariantCulture) ?? "";
        if (v_text.Contains(',') || v_text.Contains('"') || v_text.Contains('\r') || v_text.Contains('\n'))
            return "\"" + v_text.Replace("\"", "\"\"") + "\"";
        return v_text;
    }
}
