using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehousePhase10N04M05IntegrationTests
{
    private static string ConnectionString
    {
        get
        {
            return WarehouseTestDatabase.ConnectionString;
        }
    }

    [Fact]
    public async Task N04_expired_lease_terminal_failure_is_not_reported_as_tick_success()
    {
        var v_Fixture = await CreateFixtureAsync("TDD-N04-EXPIRED", p_bCreateExpiredLease: true);
        try
        {
            await RunWorkerAsync(v_Fixture, p_iMaxRetryCount: 1);
            var v_Heartbeat = await ReadHeartbeatAsync(v_Fixture.WorkerName);

            Assert.NotNull(v_Heartbeat.LastFailureAt);
            Assert.Contains("LEASE_EXPIRED", v_Heartbeat.LastError ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.True(v_Heartbeat.LastSuccessAt is null || v_Heartbeat.LastSuccessAt < v_Heartbeat.LastFailureAt);
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task M05_idle_worker_tick_updates_liveness_without_queue_work()
    {
        var v_Fixture = await CreateFixtureAsync("TDD-M05-IDLE", p_bCreateExpiredLease: false);
        try
        {
            await RunWorkerAsync(v_Fixture, p_iMaxRetryCount: 3);
            var v_Heartbeat = await ReadHeartbeatAsync(v_Fixture.WorkerName);

            Assert.NotNull(v_Heartbeat.LastHeartbeatAt);
            Assert.NotNull(v_Heartbeat.LastSuccessAt);
            Assert.Null(v_Heartbeat.LastFailureAt);
            Assert.Null(v_Heartbeat.LastError);
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task M05_sparse_daily_without_backlog_is_not_reported_as_daily_stale()
    {
        var v_Fixture = await CreateFixtureAsync("TDD-M05-SPARSE", p_bCreateExpiredLease: false);
        try
        {
            await RunWorkerAsync(v_Fixture, p_iMaxRetryCount: 3);
            var v_DailyStale = await ReadMonitorMetricAsync("DAILY_STALE");

            Assert.Equal(0L, v_DailyStale.MetricValue);
            Assert.Equal("INFO", v_DailyStale.Severity);
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task M05_failed_final_remains_degraded_even_after_a_worker_tick()
    {
        var v_Fixture = await CreateFixtureAsync("TDD-M05-FAILED", p_bCreateExpiredLease: true);
        try
        {
            await RunWorkerAsync(v_Fixture, p_iMaxRetryCount: 1);
            var v_FailureMetric = await ReadMonitorMetricAsync("SNAPSHOT_FAILED_FINAL");

            Assert.True(v_FailureMetric.MetricValue >= 1);
            Assert.Equal("CRITICAL", v_FailureMetric.Severity);
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    private static async Task<WorkerFixture> CreateFixtureAsync(string p_Prefix, bool p_bCreateExpiredLease)
    {
        var v_Tag = $"{p_Prefix}-{Guid.NewGuid():N}"[..40];
        var v_WorkerName = $"{v_Tag}-worker";
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        try
        {
            var productId = await LongScalarAsync(v_Connection, v_Transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
            var warehouseId = await LongScalarAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10 N04/M05');",
                Text("@Name", v_Tag, 255));
            if (p_bCreateExpiredLease)
            {
                await ExecuteAsync(v_Connection, v_Transaction,
                    "INSERT dbo.InventorySnapshot_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, Status, RequestType, LifecycleStatus, CreatedAt, LastAttemptAt, NextAttemptAt, LeaseUntil, ClaimedBy, ClaimedAt, AttemptCount, LastError, ErrorMessage, Requested_Version, Claimed_Version) VALUES (@WarehouseId, @ProductId, @FromDate, N'PROCESSING', N'REBUILD', N'PROCESSING', SYSUTCDATETIME(), DATEADD(MINUTE, -10, SYSUTCDATETIME()), NULL, DATEADD(SECOND, -1, SYSUTCDATETIME()), @WorkerName, DATEADD(MINUTE, -10, SYSUTCDATETIME()), 0, N'old claim', N'old claim', 1, 1);",
                    BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId), Date("@FromDate", new DateTime(2099, 8, 1)), Text("@WorkerName", v_WorkerName, 128));
            }
            await v_Transaction.CommitAsync();
            return new WorkerFixture(v_Tag, warehouseId, productId, v_WorkerName);
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task RunWorkerAsync(WorkerFixture p_Fixture, int p_iMaxRetryCount)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await ExecuteStoredAsync(v_Connection, null, "dbo.sp_Inventory_Snapshot_Process_RebuildQueue",
            Int("@Batch_Size", 1), Int("@Max_Retry_Count", p_iMaxRetryCount), Int("@Processing_Lease_Seconds", 1),
            Text("@Worker_Name", p_Fixture.WorkerName, 128), BigInt("@Kho_ID", p_Fixture.WarehouseId), BigInt("@San_Pham_ID", p_Fixture.ProductId));
    }

    private static async Task<Heartbeat> ReadHeartbeatAsync(string p_WorkerName)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Command = new SqlCommand(
            "SELECT LastHeartbeatAt, LastSuccessAt, LastFailureAt, LastError FROM dbo.InventorySnapshot_WorkerHeartbeat WHERE Worker_Name = @WorkerName;", v_Connection);
        v_Command.Parameters.Add(Text("@WorkerName", p_WorkerName, 128));
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        DateTime? v_dtmLastHeartbeatAt;
        if (v_Reader.IsDBNull(0))
        {
            v_dtmLastHeartbeatAt = null;
        }
        else
        {
            v_dtmLastHeartbeatAt = v_Reader.GetDateTime(0);
        }

        DateTime? v_dtmLastSuccessAt;
        if (v_Reader.IsDBNull(1))
        {
            v_dtmLastSuccessAt = null;
        }
        else
        {
            v_dtmLastSuccessAt = v_Reader.GetDateTime(1);
        }

        DateTime? v_dtmLastFailureAt;
        if (v_Reader.IsDBNull(2))
        {
            v_dtmLastFailureAt = null;
        }
        else
        {
            v_dtmLastFailureAt = v_Reader.GetDateTime(2);
        }

        string? v_LastError;
        if (v_Reader.IsDBNull(3))
        {
            v_LastError = null;
        }
        else
        {
            v_LastError = v_Reader.GetString(3);
        }

        return new Heartbeat(
            v_dtmLastHeartbeatAt,
            v_dtmLastSuccessAt,
            v_dtmLastFailureAt,
            v_LastError);
    }

    private static async Task<MonitorMetric> ReadMonitorMetricAsync(string p_CheckName)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Command = new SqlCommand("dbo.sp_Inventory_Snapshot_Monitor", v_Connection) { CommandType = CommandType.StoredProcedure, CommandTimeout = 10 };
        v_Command.Parameters.Add(Int("@Snapshot_Backlog_Minutes", 60));
        v_Command.Parameters.Add(Int("@Processing_Lease_Seconds", 300));
        v_Command.Parameters.Add(Int("@Daily_Stale_Days", 1));
        v_Command.Parameters.Add(new SqlParameter("@Throw_On_Critical", SqlDbType.Bit) { Value = false });
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        while (await v_Reader.ReadAsync())
        {
            if (v_Reader.GetString(0) == p_CheckName)
                return new MonitorMetric(v_Reader.GetString(1), v_Reader.GetInt64(2));
        }
        throw new Xunit.Sdk.XunitException($"Monitor metric not returned: {p_CheckName}");
    }

    private static async Task CleanupFixtureAsync(WorkerFixture p_Fixture)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.InventorySnapshot_RebuildDeadLetter WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventorySnapshot_WorkerHeartbeat WHERE Worker_Name = @WorkerName; DELETE FROM dbo.tbl_DM_Kho_User WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId), Text("@WorkerName", p_Fixture.WorkerName, 128));
            await v_Transaction.CommitAsync();
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task ExecuteStoredAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Procedure, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure, CommandTimeout = 10 };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction) { CommandTimeout = 10 };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction) { CommandTimeout = 10 };
        v_Command.Parameters.AddRange(p_arrParameters);
        return await v_Command.ExecuteScalarAsync();
    }

    private static async Task<long> LongScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToInt64(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
    }

    private static SqlParameter Text(string p_Name, string p_Value, int p_iSize)
    {
        return new(p_Name, SqlDbType.NVarChar, p_iSize)
        {
            Value = p_Value
        };
    }
    private static SqlParameter BigInt(string p_Name, long value)
    {
        return new(p_Name, SqlDbType.BigInt)
        {
            Value = value
        };
    }
    private static SqlParameter Int(string p_Name, int p_iValue)
    {
        return new(p_Name, SqlDbType.Int)
        {
            Value = p_iValue
        };
    }
    private static SqlParameter Date(string p_Name, DateTime p_dtmValue)
    {
        return new(p_Name, SqlDbType.Date)
        {
            Value = p_dtmValue.Date
        };
    }

    private sealed record WorkerFixture(string Tag, long WarehouseId, long ProductId, string WorkerName);
    private sealed record Heartbeat(DateTime? LastHeartbeatAt, DateTime? LastSuccessAt, DateTime? LastFailureAt, string? LastError);
    private sealed record MonitorMetric(string Severity, long MetricValue);
}
