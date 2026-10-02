using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehousePhase5IntegrationTests
{
    private static string ConnectionString
    {
        get
        {
            var v_ConnectionString = Environment.GetEnvironmentVariable("TKS_INTEGRATION_CONNECTION_STRING");
            if (v_ConnectionString == null)
            {
                throw new InvalidOperationException("TKS_INTEGRATION_CONNECTION_STRING must point to a disposable test database.");
            }

            return v_ConnectionString;
        }
    }

    [Fact]
    public void Finalize_job_source_uses_the_canonical_business_timezone()
    {
        var v_Source = File.ReadAllText(FindRepositoryFile("Database/Jobs/WarehouseInventorySnapshotFinalize.SqlAgent.sql"));

        Assert.Contains("SE Asia Standard Time", v_Source, StringComparison.Ordinal);
        Assert.Contains("AT TIME ZONE", v_Source, StringComparison.Ordinal);
        Assert.DoesNotContain("CONVERT(DATE, SYSUTCDATETIME())", v_Source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("2026-09-05T17:15:00", "2026-09-05")]
    [InlineData("2026-09-06T16:30:00", "2026-09-05")]
    [InlineData("2026-09-30T17:15:00", "2026-09-30")]
    [InlineData("2026-12-31T17:15:00", "2026-12-31")]
    public async Task Business_timezone_maps_previous_local_calendar_date(string p_UtcText, string p_ExpectedDate)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();

        var v_objActual = await ScalarAsync(v_Connection, null,
            "SELECT CONVERT(date, DATEADD(DAY, -1, CONVERT(date, (@Utc AT TIME ZONE N'UTC') AT TIME ZONE N'SE Asia Standard Time')));",
            DateTimeParameter("@Utc", DateTime.Parse(p_UtcText)));

        Assert.Equal(DateTime.Parse(p_ExpectedDate).Date, Convert.ToDateTime(v_objActual).Date);
    }

    [Fact]
    public async Task Finalize_publishes_the_exact_previous_local_date_on_test_clone()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
            var v_dtmTargetDate = new DateTime(2026, 9, 5);

            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 100, 0, 100, 100, 0, 1);",
                DateParameter("@Date", v_dtmTargetDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            await ExecuteStoredAsync(v_Connection, v_Transaction, "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                DateParameter("@Snapshot_Date", v_dtmTargetDate), BigInt("@Kho_ID", v_Scope.WarehouseId), BigInt("@San_Pham_ID", v_Scope.ProductId));

            Assert.Equal(1, await IntScalarAsync(v_Connection, v_Transaction,
                "SELECT COUNT(*) FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND IsValid = 1;",
                DateParameter("@Date", v_dtmTargetDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId)));
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Idle_repair_worker_tick_updates_liveness_heartbeat()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.InventorySnapshot_WorkerHeartbeat WHERE Worker_Name = N'SQLAgent:InventorySnapshotRepair';");

            await ExecuteStoredAsync(v_Connection, v_Transaction, "dbo.sp_Inventory_Snapshot_Process_RebuildQueue",
                Int("@Batch_Size", 1), Int("@Max_Retry_Count", 5), Int("@Processing_Lease_Seconds", 300),
                Text("@Worker_Name", "SQLAgent:InventorySnapshotRepair", 128));
            await ExecuteStoredAsync(v_Connection, v_Transaction, "dbo.sp_Inventory_Snapshot_Process_RebuildQueue",
                Int("@Batch_Size", 1), Int("@Max_Retry_Count", 5), Int("@Processing_Lease_Seconds", 300),
                Text("@Worker_Name", "SQLAgent:InventorySnapshotRepair", 128));

            Assert.Equal(1, await IntScalarAsync(v_Connection, v_Transaction,
                "SELECT CASE WHEN LastHeartbeatAt >= DATEADD(SECOND, -30, SYSUTCDATETIME()) THEN 1 ELSE 0 END FROM dbo.InventorySnapshot_WorkerHeartbeat WHERE Worker_Name = N'SQLAgent:InventorySnapshotRepair';"));

            var v_arrMonitor = await ReadMonitorAsync(v_Connection, v_Transaction);
            Assert.Equal("INFO", v_arrMonitor.Single(x => x.CheckName == "SNAPSHOT_WORKER_STALE").Severity);
            Assert.Equal(0, v_arrMonitor.Single(x => x.CheckName == "SNAPSHOT_WORKER_STALE").MetricValue);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Sparse_daily_without_pending_work_is_not_daily_stale()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (DATEADD(DAY, -2, CONVERT(date, SYSUTCDATETIME())), @WarehouseId, @ProductId, 0, 0, 0, 7, 0, 0, 1);",
                BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            var v_arrMonitor = await ReadMonitorAsync(v_Connection, v_Transaction);
            Assert.Equal("INFO", v_arrMonitor.Single(x => x.CheckName == "DAILY_STALE").Severity);
            Assert.Equal(0, v_arrMonitor.Single(x => x.CheckName == "DAILY_STALE").MetricValue);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Actual_movement_backlog_is_reported_as_daily_stale()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
            var v_dtmMovementDate = new DateTime(2099, 12, 31);
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventoryMovement_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, To_Date, Status, CreatedAt) VALUES (@WarehouseId, @ProductId, @Date, @Date, N'WAITING', DATEADD(MINUTE, -61, SYSUTCDATETIME()));",
                DateParameter("@Date", v_dtmMovementDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            var v_arrMonitor = await ReadMonitorAsync(v_Connection, v_Transaction);
            Assert.Equal("CRITICAL", v_arrMonitor.Single(x => x.CheckName == "DAILY_STALE").Severity);
            Assert.True(v_arrMonitor.Single(x => x.CheckName == "DAILY_STALE").MetricValue > 0);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Pending_backlog_remains_visible_to_monitor()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventorySnapshot_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, Status, RequestType, LifecycleStatus, CreatedAt, Requested_Version) VALUES (@WarehouseId, @ProductId, CONVERT(date, SYSUTCDATETIME()), N'WAITING', N'REBUILD', N'WAITING', DATEADD(MINUTE, -61, SYSUTCDATETIME()), 1);",
                BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            var v_arrMonitor = await ReadMonitorAsync(v_Connection, v_Transaction);
            Assert.Equal("CRITICAL", v_arrMonitor.Single(x => x.CheckName == "SNAPSHOT_BACKLOG").Severity);
            Assert.True(v_arrMonitor.Single(x => x.CheckName == "SNAPSHOT_BACKLOG").MetricValue > 0);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Failed_final_remains_visible_as_monitor_failure()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventorySnapshot_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, Status, RequestType, LifecycleStatus, AttemptCount, LastError, Requested_Version) VALUES (@WarehouseId, @ProductId, CONVERT(date, SYSUTCDATETIME()), N'FAILED', N'REBUILD', N'FAILED_FINAL', 1, N'TDD failed final', 1);",
                BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            var v_arrMonitor = await ReadMonitorAsync(v_Connection, v_Transaction);
            Assert.Equal("CRITICAL", v_arrMonitor.Single(x => x.CheckName == "SNAPSHOT_FAILED_FINAL").Severity);
            Assert.True(v_arrMonitor.Single(x => x.CheckName == "SNAPSHOT_FAILED_FINAL").MetricValue > 0);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Failed_final_invalidation_reactivates_work_and_resolves_dead_letter()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
            var v_dtmDate = new DateTime(2099, 11, 10);
            var queueId = await LongScalarAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventorySnapshot_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, Status, RequestType, LifecycleStatus, AttemptCount, LastError, Requested_Version) OUTPUT INSERTED.ID VALUES (@WarehouseId, @ProductId, @Date, N'FAILED', N'REBUILD', N'FAILED_FINAL', 5, N'TDD terminal failure', 3);",
                DateParameter("@Date", v_dtmDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventorySnapshot_RebuildDeadLetter(Queue_ID, Kho_ID, San_Pham_ID, From_Date, RequestType, AttemptCount, LastError) VALUES (@QueueId, @WarehouseId, @ProductId, @Date, N'REBUILD', 5, N'TDD terminal failure');",
                BigInt("@QueueId", queueId), DateParameter("@Date", v_dtmDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            await ExecuteAsync(v_Connection, v_Transaction,
                "DECLARE @Affected dbo.InventorySnapshotAffectedType; INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason) VALUES (@WarehouseId, @ProductId, @Date, N'TDD_RECOVERY'); EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;",
                DateParameter("@Date", v_dtmDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            Assert.Equal(1, await IntScalarAsync(v_Connection, v_Transaction,
                "SELECT CASE WHEN LifecycleStatus IN (N'WAITING', N'INITIALIZE_REQUIRED') AND Status = N'WAITING' AND Requested_Version = 4 THEN 1 ELSE 0 END FROM dbo.InventorySnapshot_RebuildQueue WHERE ID = @QueueId;",
                BigInt("@QueueId", queueId)));
            Assert.Equal(1, await IntScalarAsync(v_Connection, v_Transaction,
                "SELECT CASE WHEN ResolvedAt IS NOT NULL THEN 1 ELSE 0 END FROM dbo.InventorySnapshot_RebuildDeadLetter WHERE Queue_ID = @QueueId;",
                BigInt("@QueueId", queueId)));
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    private static async Task<Scope> CreateScopeAsync(SqlConnection p_Connection, SqlTransaction p_Transaction)
    {
        var productId = await LongScalarAsync(p_Connection, p_Transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
        var v_Tag = $"TDD-P5-{Guid.NewGuid():N}"[..24];
        var warehouseId = await LongScalarAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'TDD Phase 5');",
            Text("@Name", v_Tag, 255));
        return new Scope(warehouseId, productId);
    }

    private static async Task<List<MonitorRow>> ReadMonitorAsync(SqlConnection p_Connection, SqlTransaction p_Transaction)
    {
        var v_arrRows = new List<MonitorRow>();
        await using var v_Command = new SqlCommand("dbo.sp_Inventory_Snapshot_Monitor", p_Connection, p_Transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        v_Command.Parameters.Add(Int("@Snapshot_Backlog_Minutes", 60));
        v_Command.Parameters.Add(Int("@Processing_Lease_Seconds", 300));
        v_Command.Parameters.Add(Int("@Daily_Stale_Days", 1));
        v_Command.Parameters.Add(new SqlParameter("@Throw_On_Critical", SqlDbType.Bit) { Value = false });
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        while (await v_Reader.ReadAsync())
            v_arrRows.Add(new MonitorRow(v_Reader.GetString(0), v_Reader.GetString(1), v_Reader.GetInt64(2)));
        return v_arrRows;
    }

    private static async Task ExecuteStoredAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Procedure, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task<int> IntScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToInt32(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
    }

    private static async Task<long> LongScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToInt64(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
    }

    private static async Task<object?> ScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        return await v_Command.ExecuteScalarAsync();
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
    private static SqlParameter Text(string p_Name, string p_Value, int p_iSize)
    {
        return new(p_Name, SqlDbType.NVarChar, p_iSize)
        {
            Value = p_Value
        };
    }
    private static SqlParameter DateParameter(string p_Name, DateTime p_dtmValue)
    {
        return new(p_Name, SqlDbType.Date)
        {
            Value = p_dtmValue.Date
        };
    }
    private static SqlParameter DateTimeParameter(string p_Name, DateTime p_dtmValue)
    {
        return new(p_Name, SqlDbType.DateTime2)
        {
            Value = p_dtmValue
        };
    }

    private static string FindRepositoryFile(string p_RelativePath)
    {
        for (var v_Directory = new DirectoryInfo(Directory.GetCurrentDirectory()); v_Directory is not null; v_Directory = v_Directory.Parent)
        {
            var v_Candidate = Path.Combine(v_Directory.FullName, p_RelativePath);
            if (File.Exists(v_Candidate))
                return v_Candidate;
        }

        throw new FileNotFoundException($"Could not locate repository file '{p_RelativePath}'.");
    }

    private sealed record Scope(long WarehouseId, long ProductId);
    private sealed record MonitorRow(string CheckName, string Severity, long MetricValue);
}
