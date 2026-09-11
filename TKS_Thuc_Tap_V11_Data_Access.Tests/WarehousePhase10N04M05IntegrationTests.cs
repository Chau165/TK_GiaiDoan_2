using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehousePhase10N04M05IntegrationTests
{
    private static string ConnectionString => WarehouseTestDatabase.ConnectionString;

    [Fact]
    public async Task N04_expired_lease_terminal_failure_is_not_reported_as_tick_success()
    {
        var fixture = await CreateFixtureAsync("TDD-N04-EXPIRED", createExpiredLease: true);
        try
        {
            await RunWorkerAsync(fixture, maxRetryCount: 1);
            var heartbeat = await ReadHeartbeatAsync(fixture.WorkerName);

            Assert.NotNull(heartbeat.LastFailureAt);
            Assert.Contains("LEASE_EXPIRED", heartbeat.LastError ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.True(heartbeat.LastSuccessAt is null || heartbeat.LastSuccessAt < heartbeat.LastFailureAt);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task M05_idle_worker_tick_updates_liveness_without_queue_work()
    {
        var fixture = await CreateFixtureAsync("TDD-M05-IDLE", createExpiredLease: false);
        try
        {
            await RunWorkerAsync(fixture, maxRetryCount: 3);
            var heartbeat = await ReadHeartbeatAsync(fixture.WorkerName);

            Assert.NotNull(heartbeat.LastHeartbeatAt);
            Assert.NotNull(heartbeat.LastSuccessAt);
            Assert.Null(heartbeat.LastFailureAt);
            Assert.Null(heartbeat.LastError);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task M05_sparse_daily_without_backlog_is_not_reported_as_daily_stale()
    {
        var fixture = await CreateFixtureAsync("TDD-M05-SPARSE", createExpiredLease: false);
        try
        {
            await RunWorkerAsync(fixture, maxRetryCount: 3);
            var dailyStale = await ReadMonitorMetricAsync("DAILY_STALE");

            Assert.Equal(0L, dailyStale.MetricValue);
            Assert.Equal("INFO", dailyStale.Severity);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task M05_failed_final_remains_degraded_even_after_a_worker_tick()
    {
        var fixture = await CreateFixtureAsync("TDD-M05-FAILED", createExpiredLease: true);
        try
        {
            await RunWorkerAsync(fixture, maxRetryCount: 1);
            var failureMetric = await ReadMonitorMetricAsync("SNAPSHOT_FAILED_FINAL");

            Assert.True(failureMetric.MetricValue >= 1);
            Assert.Equal("CRITICAL", failureMetric.Severity);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    private static async Task<WorkerFixture> CreateFixtureAsync(string prefix, bool createExpiredLease)
    {
        var tag = $"{prefix}-{Guid.NewGuid():N}"[..40];
        var workerName = $"{tag}-worker";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            var productId = await LongScalarAsync(connection, transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
            var warehouseId = await LongScalarAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10 N04/M05');",
                Text("@Name", tag, 255));
            if (createExpiredLease)
            {
                await ExecuteAsync(connection, transaction,
                    "INSERT dbo.InventorySnapshot_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, Status, RequestType, LifecycleStatus, CreatedAt, LastAttemptAt, NextAttemptAt, LeaseUntil, ClaimedBy, ClaimedAt, AttemptCount, LastError, ErrorMessage, Requested_Version, Claimed_Version) VALUES (@WarehouseId, @ProductId, @FromDate, N'PROCESSING', N'REBUILD', N'PROCESSING', SYSUTCDATETIME(), DATEADD(MINUTE, -10, SYSUTCDATETIME()), NULL, DATEADD(SECOND, -1, SYSUTCDATETIME()), @WorkerName, DATEADD(MINUTE, -10, SYSUTCDATETIME()), 0, N'old claim', N'old claim', 1, 1);",
                    BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId), Date("@FromDate", new DateTime(2099, 8, 1)), Text("@WorkerName", workerName, 128));
            }
            await transaction.CommitAsync();
            return new WorkerFixture(tag, warehouseId, productId, workerName);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task RunWorkerAsync(WorkerFixture fixture, int maxRetryCount)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteStoredAsync(connection, null, "dbo.sp_Inventory_Snapshot_Process_RebuildQueue",
            Int("@Batch_Size", 1), Int("@Max_Retry_Count", maxRetryCount), Int("@Processing_Lease_Seconds", 1),
            Text("@Worker_Name", fixture.WorkerName, 128), BigInt("@Kho_ID", fixture.WarehouseId), BigInt("@San_Pham_ID", fixture.ProductId));
    }

    private static async Task<Heartbeat> ReadHeartbeatAsync(string workerName)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT LastHeartbeatAt, LastSuccessAt, LastFailureAt, LastError FROM dbo.InventorySnapshot_WorkerHeartbeat WHERE Worker_Name = @WorkerName;", connection);
        command.Parameters.Add(Text("@WorkerName", workerName, 128));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new Heartbeat(
            reader.IsDBNull(0) ? null : reader.GetDateTime(0),
            reader.IsDBNull(1) ? null : reader.GetDateTime(1),
            reader.IsDBNull(2) ? null : reader.GetDateTime(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static async Task<MonitorMetric> ReadMonitorMetricAsync(string checkName)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("dbo.sp_Inventory_Snapshot_Monitor", connection) { CommandType = CommandType.StoredProcedure, CommandTimeout = 10 };
        command.Parameters.Add(Int("@Snapshot_Backlog_Minutes", 60));
        command.Parameters.Add(Int("@Processing_Lease_Seconds", 300));
        command.Parameters.Add(Int("@Daily_Stale_Days", 1));
        command.Parameters.Add(new SqlParameter("@Throw_On_Critical", SqlDbType.Bit) { Value = false });
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.GetString(0) == checkName)
                return new MonitorMetric(reader.GetString(1), reader.GetInt64(2));
        }
        throw new Xunit.Sdk.XunitException($"Monitor metric not returned: {checkName}");
    }

    private static async Task CleanupFixtureAsync(WorkerFixture fixture)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.InventorySnapshot_RebuildDeadLetter WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventorySnapshot_WorkerHeartbeat WHERE Worker_Name = @WorkerName; DELETE FROM dbo.tbl_DM_Kho_User WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId), Text("@WorkerName", fixture.WorkerName, 128));
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task ExecuteStoredAsync(SqlConnection connection, SqlTransaction? transaction, string procedure, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedure, connection, transaction) { CommandType = CommandType.StoredProcedure, CommandTimeout = 10 };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 10 };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 10 };
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<long> LongScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) => Convert.ToInt64(await ScalarAsync(connection, transaction, sql, parameters));

    private static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.NVarChar, size) { Value = value };
    private static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt) { Value = value };
    private static SqlParameter Int(string name, int value) => new(name, SqlDbType.Int) { Value = value };
    private static SqlParameter Date(string name, DateTime value) => new(name, SqlDbType.Date) { Value = value.Date };

    private sealed record WorkerFixture(string Tag, long WarehouseId, long ProductId, string WorkerName);
    private sealed record Heartbeat(DateTime? LastHeartbeatAt, DateTime? LastSuccessAt, DateTime? LastFailureAt, string? LastError);
    private sealed record MonitorMetric(string Severity, long MetricValue);
}
