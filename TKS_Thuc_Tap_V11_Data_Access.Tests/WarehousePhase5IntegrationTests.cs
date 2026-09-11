using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehousePhase5IntegrationTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable("TKS_INTEGRATION_CONNECTION_STRING")
        ?? throw new InvalidOperationException("TKS_INTEGRATION_CONNECTION_STRING must point to a disposable test database.");

    [Fact]
    public void Finalize_job_source_uses_the_canonical_business_timezone()
    {
        var source = File.ReadAllText(FindRepositoryFile("Database/Jobs/WarehouseInventorySnapshotFinalize.SqlAgent.sql"));

        Assert.Contains("SE Asia Standard Time", source, StringComparison.Ordinal);
        Assert.Contains("AT TIME ZONE", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CONVERT(DATE, SYSUTCDATETIME())", source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("2026-09-05T17:15:00", "2026-09-05")]
    [InlineData("2026-09-06T16:30:00", "2026-09-05")]
    [InlineData("2026-09-30T17:15:00", "2026-09-30")]
    [InlineData("2026-12-31T17:15:00", "2026-12-31")]
    public async Task Business_timezone_maps_previous_local_calendar_date(string utcText, string expectedDate)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var actual = await ScalarAsync(connection, null,
            "SELECT CONVERT(date, DATEADD(DAY, -1, CONVERT(date, (@Utc AT TIME ZONE N'UTC') AT TIME ZONE N'SE Asia Standard Time')));",
            DateTimeParameter("@Utc", DateTime.Parse(utcText)));

        Assert.Equal(DateTime.Parse(expectedDate).Date, Convert.ToDateTime(actual).Date);
    }

    [Fact]
    public async Task Finalize_publishes_the_exact_previous_local_date_on_test_clone()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreateScopeAsync(connection, transaction);
            var targetDate = new DateTime(2026, 9, 5);

            await ExecuteAsync(connection, transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 100, 0, 100, 100, 0, 1);",
                DateParameter("@Date", targetDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));

            await ExecuteStoredAsync(connection, transaction, "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                DateParameter("@Snapshot_Date", targetDate), BigInt("@Kho_ID", scope.WarehouseId), BigInt("@San_Pham_ID", scope.ProductId));

            Assert.Equal(1, await IntScalarAsync(connection, transaction,
                "SELECT COUNT(*) FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND IsValid = 1;",
                DateParameter("@Date", targetDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId)));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Idle_repair_worker_tick_updates_liveness_heartbeat()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.InventorySnapshot_WorkerHeartbeat WHERE Worker_Name = N'SQLAgent:InventorySnapshotRepair';");

            await ExecuteStoredAsync(connection, transaction, "dbo.sp_Inventory_Snapshot_Process_RebuildQueue",
                Int("@Batch_Size", 1), Int("@Max_Retry_Count", 5), Int("@Processing_Lease_Seconds", 300),
                Text("@Worker_Name", "SQLAgent:InventorySnapshotRepair", 128));
            await ExecuteStoredAsync(connection, transaction, "dbo.sp_Inventory_Snapshot_Process_RebuildQueue",
                Int("@Batch_Size", 1), Int("@Max_Retry_Count", 5), Int("@Processing_Lease_Seconds", 300),
                Text("@Worker_Name", "SQLAgent:InventorySnapshotRepair", 128));

            Assert.Equal(1, await IntScalarAsync(connection, transaction,
                "SELECT CASE WHEN LastHeartbeatAt >= DATEADD(SECOND, -30, SYSUTCDATETIME()) THEN 1 ELSE 0 END FROM dbo.InventorySnapshot_WorkerHeartbeat WHERE Worker_Name = N'SQLAgent:InventorySnapshotRepair';"));

            var monitor = await ReadMonitorAsync(connection, transaction);
            Assert.Equal("INFO", monitor.Single(x => x.CheckName == "SNAPSHOT_WORKER_STALE").Severity);
            Assert.Equal(0, monitor.Single(x => x.CheckName == "SNAPSHOT_WORKER_STALE").MetricValue);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Sparse_daily_without_pending_work_is_not_daily_stale()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreateScopeAsync(connection, transaction);
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (DATEADD(DAY, -2, CONVERT(date, SYSUTCDATETIME())), @WarehouseId, @ProductId, 0, 0, 0, 7, 0, 0, 1);",
                BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));

            var monitor = await ReadMonitorAsync(connection, transaction);
            Assert.Equal("INFO", monitor.Single(x => x.CheckName == "DAILY_STALE").Severity);
            Assert.Equal(0, monitor.Single(x => x.CheckName == "DAILY_STALE").MetricValue);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Actual_movement_backlog_is_reported_as_daily_stale()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreateScopeAsync(connection, transaction);
            var movementDate = new DateTime(2099, 12, 31);
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventoryMovement_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, To_Date, Status, CreatedAt) VALUES (@WarehouseId, @ProductId, @Date, @Date, N'WAITING', DATEADD(MINUTE, -61, SYSUTCDATETIME()));",
                DateParameter("@Date", movementDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));

            var monitor = await ReadMonitorAsync(connection, transaction);
            Assert.Equal("CRITICAL", monitor.Single(x => x.CheckName == "DAILY_STALE").Severity);
            Assert.True(monitor.Single(x => x.CheckName == "DAILY_STALE").MetricValue > 0);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Pending_backlog_remains_visible_to_monitor()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreateScopeAsync(connection, transaction);
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventorySnapshot_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, Status, RequestType, LifecycleStatus, CreatedAt, Requested_Version) VALUES (@WarehouseId, @ProductId, CONVERT(date, SYSUTCDATETIME()), N'WAITING', N'REBUILD', N'WAITING', DATEADD(MINUTE, -61, SYSUTCDATETIME()), 1);",
                BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));

            var monitor = await ReadMonitorAsync(connection, transaction);
            Assert.Equal("CRITICAL", monitor.Single(x => x.CheckName == "SNAPSHOT_BACKLOG").Severity);
            Assert.True(monitor.Single(x => x.CheckName == "SNAPSHOT_BACKLOG").MetricValue > 0);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Failed_final_remains_visible_as_monitor_failure()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreateScopeAsync(connection, transaction);
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventorySnapshot_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, Status, RequestType, LifecycleStatus, AttemptCount, LastError, Requested_Version) VALUES (@WarehouseId, @ProductId, CONVERT(date, SYSUTCDATETIME()), N'FAILED', N'REBUILD', N'FAILED_FINAL', 1, N'TDD failed final', 1);",
                BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));

            var monitor = await ReadMonitorAsync(connection, transaction);
            Assert.Equal("CRITICAL", monitor.Single(x => x.CheckName == "SNAPSHOT_FAILED_FINAL").Severity);
            Assert.True(monitor.Single(x => x.CheckName == "SNAPSHOT_FAILED_FINAL").MetricValue > 0);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Failed_final_invalidation_reactivates_work_and_resolves_dead_letter()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreateScopeAsync(connection, transaction);
            var date = new DateTime(2099, 11, 10);
            var queueId = await LongScalarAsync(connection, transaction,
                "INSERT dbo.InventorySnapshot_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, Status, RequestType, LifecycleStatus, AttemptCount, LastError, Requested_Version) OUTPUT INSERTED.ID VALUES (@WarehouseId, @ProductId, @Date, N'FAILED', N'REBUILD', N'FAILED_FINAL', 5, N'TDD terminal failure', 3);",
                DateParameter("@Date", date), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventorySnapshot_RebuildDeadLetter(Queue_ID, Kho_ID, San_Pham_ID, From_Date, RequestType, AttemptCount, LastError) VALUES (@QueueId, @WarehouseId, @ProductId, @Date, N'REBUILD', 5, N'TDD terminal failure');",
                BigInt("@QueueId", queueId), DateParameter("@Date", date), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));

            await ExecuteAsync(connection, transaction,
                "DECLARE @Affected dbo.InventorySnapshotAffectedType; INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason) VALUES (@WarehouseId, @ProductId, @Date, N'TDD_RECOVERY'); EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;",
                DateParameter("@Date", date), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));

            Assert.Equal(1, await IntScalarAsync(connection, transaction,
                "SELECT CASE WHEN LifecycleStatus IN (N'WAITING', N'INITIALIZE_REQUIRED') AND Status = N'WAITING' AND Requested_Version = 4 THEN 1 ELSE 0 END FROM dbo.InventorySnapshot_RebuildQueue WHERE ID = @QueueId;",
                BigInt("@QueueId", queueId)));
            Assert.Equal(1, await IntScalarAsync(connection, transaction,
                "SELECT CASE WHEN ResolvedAt IS NOT NULL THEN 1 ELSE 0 END FROM dbo.InventorySnapshot_RebuildDeadLetter WHERE Queue_ID = @QueueId;",
                BigInt("@QueueId", queueId)));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task<Scope> CreateScopeAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var productId = await LongScalarAsync(connection, transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
        var tag = $"TDD-P5-{Guid.NewGuid():N}"[..24];
        var warehouseId = await LongScalarAsync(connection, transaction,
            "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'TDD Phase 5');",
            Text("@Name", tag, 255));
        return new Scope(warehouseId, productId);
    }

    private static async Task<List<MonitorRow>> ReadMonitorAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var rows = new List<MonitorRow>();
        await using var command = new SqlCommand("dbo.sp_Inventory_Snapshot_Monitor", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add(Int("@Snapshot_Backlog_Minutes", 60));
        command.Parameters.Add(Int("@Processing_Lease_Seconds", 300));
        command.Parameters.Add(Int("@Daily_Stale_Days", 1));
        command.Parameters.Add(new SqlParameter("@Throw_On_Critical", SqlDbType.Bit) { Value = false });
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new MonitorRow(reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
        return rows;
    }

    private static async Task ExecuteStoredAsync(SqlConnection connection, SqlTransaction? transaction, string procedure, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedure, connection, transaction) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> IntScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) =>
        Convert.ToInt32(await ScalarAsync(connection, transaction, sql, parameters));

    private static async Task<long> LongScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) =>
        Convert.ToInt64(await ScalarAsync(connection, transaction, sql, parameters));

    private static async Task<object?> ScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    private static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt) { Value = value };
    private static SqlParameter Int(string name, int value) => new(name, SqlDbType.Int) { Value = value };
    private static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.NVarChar, size) { Value = value };
    private static SqlParameter DateParameter(string name, DateTime value) => new(name, SqlDbType.Date) { Value = value.Date };
    private static SqlParameter DateTimeParameter(string name, DateTime value) => new(name, SqlDbType.DateTime2) { Value = value };

    private static string FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }

    private sealed record Scope(long WarehouseId, long ProductId);
    private sealed record MonitorRow(string CheckName, string Severity, long MetricValue);
}
