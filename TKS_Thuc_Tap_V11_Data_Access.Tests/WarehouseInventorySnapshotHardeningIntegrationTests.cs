using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehouseInventorySnapshotHardeningIntegrationTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable("TKS_INTEGRATION_CONNECTION_STRING")
        ?? "Server=localhost;Database=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True;TrustServerCertificate=True;";

    [Fact]
    public async Task Missing_snapshot_scope_is_enqueued_as_initialize_required()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreateScopeAsync(connection, transaction);
            var movementDate = new DateTime(2099, 1, 3);

            await ApplySnapshotInvalidationAsync(connection, transaction, scope, movementDate);
            var queue = await ReadQueueAsync(connection, transaction, scope);

            Assert.Equal("INITIALIZE", queue.RequestType);
            Assert.Equal("INITIALIZE_REQUIRED", queue.LifecycleStatus);
            Assert.Equal("WAITING", queue.LegacyStatus);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Bootstrap_uses_only_posted_ledger_and_is_idempotent()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreateScopeAsync(connection, transaction);
            await CreatePostedReceiptAsync(connection, transaction, scope, new DateTime(2098, 12, 31), 140m);
            await CreatePostedIssueAsync(connection, transaction, scope, new DateTime(2098, 12, 31), 40m);
            await CreatePostedReceiptAsync(connection, transaction, scope, new DateTime(2099, 1, 3), 20m);

            await ExecuteStoredAsync(connection, transaction, "dbo.sp_Inventory_Snapshot_Bootstrap_From_Ledger",
                Date("@Baseline_Date", new DateTime(2098, 12, 31)),
                Bit("@Opening_Balance_Confirmed", true),
                BigInt("@Kho_ID", scope.WarehouseId),
                BigInt("@San_Pham_ID", scope.ProductId));

            await ExecuteStoredAsync(connection, transaction, "dbo.sp_Inventory_Snapshot_Bootstrap_From_Ledger",
                Date("@Baseline_Date", new DateTime(2098, 12, 31)),
                Bit("@Opening_Balance_Confirmed", true),
                BigInt("@Kho_ID", scope.WarehouseId),
                BigInt("@San_Pham_ID", scope.ProductId));

            Assert.Equal(100m, await DecimalScalarAsync(connection, transaction,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", new DateTime(2098, 12, 31)), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId)));
            Assert.Equal(1, await IntScalarAsync(connection, transaction,
                "SELECT COUNT(*) FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", new DateTime(2098, 12, 31)), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId)));
            Assert.Equal(2, await IntScalarAsync(connection, transaction,
                "SELECT COUNT(*) FROM dbo.InventorySnapshot_BootstrapAudit WHERE Baseline_Date = @Date AND Status = N'COMPLETED';",
                Date("@Date", new DateTime(2098, 12, 31))));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Bootstrap_requires_explicit_opening_balance_confirmation()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreateScopeAsync(connection, transaction);
            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteStoredAsync(connection, transaction,
                "dbo.sp_Inventory_Snapshot_Bootstrap_From_Ledger",
                Date("@Baseline_Date", new DateTime(2098, 12, 31)),
                Bit("@Opening_Balance_Confirmed", false),
                BigInt("@Kho_ID", scope.WarehouseId),
                BigInt("@San_Pham_ID", scope.ProductId)));

            Assert.Equal(51310, error.Number);
            Assert.Contains("OPENING_BALANCE_REQUIRED", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Finalize_daily_uses_balance_daily_closing_not_current_quantity()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreateScopeAsync(connection, transaction);
            var snapshotDate = new DateTime(2099, 2, 15);
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, 999, 0);",
                BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 50, 30, 3, 77, 30, 3, 1);",
                Date("@Date", new DateTime(2099, 2, 10)), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));

            await ExecuteStoredAsync(connection, transaction, "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                Date("@Snapshot_Date", snapshotDate), BigInt("@Kho_ID", scope.WarehouseId), BigInt("@San_Pham_ID", scope.ProductId));

            Assert.Equal(77m, await DecimalScalarAsync(connection, transaction,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId)));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Finalize_blocks_relevant_movement_failed_final_before_publish()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreateScopeAsync(connection, transaction);
            var snapshotDate = new DateTime(2099, 4, 10);
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 100, 0, 100, 100, 0, 1);",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventoryMovement_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, To_Date, Status, Retry_Count, ErrorMessage, LastError) VALUES (@WarehouseId, @ProductId, @Date, @Date, N'FAILED_FINAL', 3, N'failed movement rebuild', N'failed movement rebuild');",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));

            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteStoredAsync(connection, transaction,
                "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                Date("@Snapshot_Date", snapshotDate), BigInt("@Kho_ID", scope.WarehouseId), BigInt("@San_Pham_ID", scope.ProductId)));

            Assert.Equal(51320, error.Number);
            Assert.Equal(0, await IntScalarAsync(connection, transaction,
                "SELECT COUNT(*) FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId)));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Finalize_blocks_relevant_snapshot_failed_final_before_publish()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreateScopeAsync(connection, transaction);
            var snapshotDate = new DateTime(2099, 5, 10);
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 100, 0, 100, 100, 0, 1);",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventorySnapshot_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, Status, RequestType, LifecycleStatus, AttemptCount, LastError) VALUES (@WarehouseId, @ProductId, @Date, N'FAILED', N'REBUILD', N'FAILED_FINAL', 5, N'failed snapshot rebuild');",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));

            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteStoredAsync(connection, transaction,
                "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                Date("@Snapshot_Date", snapshotDate), BigInt("@Kho_ID", scope.WarehouseId), BigInt("@San_Pham_ID", scope.ProductId)));

            Assert.Equal(51321, error.Number);
            Assert.Equal(0, await IntScalarAsync(connection, transaction,
                "SELECT COUNT(*) FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId)));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Theory]
    [InlineData("WAITING")]
    [InlineData("PROCESSING")]
    [InlineData("RETRY_WAITING")]
    public async Task Finalize_still_blocks_active_movement_rebuild_states(string status)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreateScopeAsync(connection, transaction);
            var snapshotDate = new DateTime(2099, 6, 10);
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventoryMovement_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, To_Date, Status, Retry_Count, ErrorMessage, LastError) VALUES (@WarehouseId, @ProductId, @Date, @Date, @Status, 0, N'active movement rebuild', N'active movement rebuild');",
                Date("@Date", snapshotDate), Text("@Status", status, 20), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));

            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteStoredAsync(connection, transaction,
                "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                Date("@Snapshot_Date", snapshotDate), BigInt("@Kho_ID", scope.WarehouseId), BigInt("@San_Pham_ID", scope.ProductId)));

            Assert.Equal(51320, error.Number);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Theory]
    [InlineData("WAITING")]
    [InlineData("PROCESSING")]
    [InlineData("RETRY_WAITING")]
    public async Task Finalize_keeps_snapshot_invalid_while_snapshot_rebuild_is_active(string lifecycleStatus)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreateScopeAsync(connection, transaction);
            var snapshotDate = new DateTime(2099, 7, 10);
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 100, 0, 100, 100, 0, 1);",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventorySnapshot_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, Status, RequestType, LifecycleStatus, AttemptCount, LastError) VALUES (@WarehouseId, @ProductId, @Date, @Status, N'REBUILD', @LifecycleStatus, 0, N'active snapshot rebuild');",
                Date("@Date", snapshotDate), Text("@Status", lifecycleStatus == "PROCESSING" ? "PROCESSING" : "WAITING", 20), Text("@LifecycleStatus", lifecycleStatus, 24), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));

            await ExecuteStoredAsync(connection, transaction,
                "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                Date("@Snapshot_Date", snapshotDate), BigInt("@Kho_ID", scope.WarehouseId), BigInt("@San_Pham_ID", scope.ProductId));

            Assert.Equal(0, await IntScalarAsync(connection, transaction,
                "SELECT IsValid FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId)));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Finalize_allows_checkpoint_after_failed_final_recovery()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreateScopeAsync(connection, transaction);
            var snapshotDate = new DateTime(2099, 8, 10);
            await CreatePostedReceiptAsync(connection, transaction, scope, snapshotDate, 120m);
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 120, 0, 120, 120, 0, 1);",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, 100, 0, 1);",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
            await ExecuteAsync(connection, transaction,
                "DELETE dl FROM dbo.InventorySnapshot_RebuildDeadLetter dl JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = dl.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId; DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
            var queueId = await LongScalarAsync(connection, transaction,
                "INSERT dbo.InventorySnapshot_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, Status, RequestType, LifecycleStatus, AttemptCount, LastError) OUTPUT INSERTED.ID VALUES (@WarehouseId, @ProductId, @Date, N'FAILED', N'REBUILD', N'FAILED_FINAL', 5, N'failed before recovery');",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));

            await ExecuteAsync(connection, transaction,
                "UPDATE dbo.InventorySnapshot_RebuildQueue SET Status = N'WAITING', LifecycleStatus = N'WAITING', AttemptCount = 0, LastError = NULL, ErrorMessage = NULL, CompletedAt = NULL, NextAttemptAt = NULL WHERE ID = @QueueId;",
                BigInt("@QueueId", queueId));
            await ExecuteStoredAsync(connection, transaction,
                "dbo.sp_Inventory_Snapshot_Process_RebuildQueue",
                Int("@Batch_Size", 1), Int("@Max_Retry_Count", 5), Int("@Processing_Lease_Seconds", 300),
                Text("@Worker_Name", "TDD-Snapshot-Recovery", 128), BigInt("@Kho_ID", scope.WarehouseId), BigInt("@San_Pham_ID", scope.ProductId));
            Assert.Equal(120m, await DecimalScalarAsync(connection, transaction,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND IsValid = 1;",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId)));
            await ExecuteStoredAsync(connection, transaction,
                "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                Date("@Snapshot_Date", snapshotDate), BigInt("@Kho_ID", scope.WarehouseId), BigInt("@San_Pham_ID", scope.ProductId));

            Assert.Equal(120m, await DecimalScalarAsync(connection, transaction,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND IsValid = 1;",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId)));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Finalize_scoped_checkpoint_ignores_unrelated_failed_final()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var healthy = await CreateScopeAsync(connection, transaction);
            var failed = await CreateScopeAsync(connection, transaction);
            var snapshotDate = new DateTime(2099, 9, 10);
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 77, 0, 77, 77, 0, 1);",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", healthy.WarehouseId), BigInt("@ProductId", healthy.ProductId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventoryMovement_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, To_Date, Status, Retry_Count, ErrorMessage, LastError) VALUES (@WarehouseId, @ProductId, @Date, @Date, N'FAILED_FINAL', 3, N'unrelated failure', N'unrelated failure');",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", failed.WarehouseId), BigInt("@ProductId", failed.ProductId));

            await ExecuteStoredAsync(connection, transaction,
                "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                Date("@Snapshot_Date", snapshotDate), BigInt("@Kho_ID", healthy.WarehouseId), BigInt("@San_Pham_ID", healthy.ProductId));

            Assert.Equal(77m, await DecimalScalarAsync(connection, transaction,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND IsValid = 1;",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", healthy.WarehouseId), BigInt("@ProductId", healthy.ProductId)));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Back_dated_post_invalidates_and_rebuilds_existing_snapshot()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreatePostableScopeAsync(connection, transaction);
            var snapshotDate = new DateTime(2099, 3, 10);
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, 0, 1, 1);",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
            var receiptId = await CreateDraftReceiptAsync(connection, transaction, scope, new DateTime(2099, 3, 5), 10m);

            await ExecuteStoredAsync(connection, transaction, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", true), BigInt("@Document_ID", receiptId), Text("@Ma_Dang_Nhap", scope.Login!, 100));

            var queued = await ReadQueueAsync(connection, transaction, scope);
            Assert.Equal("REBUILD", queued.RequestType);
            Assert.Equal("WAITING", queued.LifecycleStatus);

            await ExecuteStoredAsync(connection, transaction, "dbo.sp_Inventory_Snapshot_Process_RebuildQueue",
                Int("@Batch_Size", 1), Int("@Max_Retry_Count", 5), Int("@Processing_Lease_Seconds", 300),
                Text("@Worker_Name", "TDD-Snapshot-Backdate", 128), BigInt("@Kho_ID", scope.WarehouseId), BigInt("@San_Pham_ID", scope.ProductId));

            Assert.Equal(10m, await DecimalScalarAsync(connection, transaction,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND IsValid = 1;",
                Date("@Date", snapshotDate), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId)));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Expired_snapshot_claim_is_recovered_with_retry_waiting()
    {
        var scope = await CreatePersistentScopeAsync();
        var date = new DateTime(2099, 4, 10);

        try
        {
            await SeedRebuildScopeAsync(scope, date);
            await ExecuteAsync(
                "UPDATE dbo.InventorySnapshot_RebuildQueue SET Status = N'PROCESSING', LifecycleStatus = N'PROCESSING', LeaseUntil = '2000-01-01', ClaimedAt = '2000-01-01', AttemptCount = 0 WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));

            await ProcessSnapshotQueueAsync(scope, maxRetryCount: 5, workerName: "TDD-Snapshot-Lease");
            var queue = await ReadQueueAsync(scope);

            Assert.Equal("RETRY_WAITING", queue.LifecycleStatus);
            Assert.Equal(1, queue.AttemptCount);
            Assert.NotNull(queue.NextAttemptAt);
        }
        finally
        {
            await CleanupPersistentScopeAsync(scope);
        }
    }

    [Fact]
    public async Task Transient_snapshot_error_uses_one_minute_retry_backoff()
    {
        var scope = await CreatePersistentScopeAsync();
        var date = new DateTime(2099, 5, 10);

        try
        {
            await SeedRebuildScopeAsync(scope, date);
            await using var lockConnection = new SqlConnection(ConnectionString);
            await lockConnection.OpenAsync();
            await using var lockTransaction = lockConnection.BeginTransaction();
            await AcquireLockAsync(lockConnection, lockTransaction, SnapshotScopeResource(scope));

            await ProcessSnapshotQueueAsync(scope, maxRetryCount: 5, workerName: "TDD-Snapshot-Retry");
            var queue = await ReadQueueAsync(scope);

            Assert.Equal("RETRY_WAITING", queue.LifecycleStatus);
            Assert.Equal(1, queue.AttemptCount);
            Assert.NotNull(queue.NextAttemptAt);
            Assert.True(queue.NextAttemptAt >= DateTime.UtcNow.AddSeconds(45));
            await lockTransaction.RollbackAsync();
        }
        finally
        {
            await CleanupPersistentScopeAsync(scope);
        }
    }

    [Fact]
    public async Task Permanent_snapshot_failure_creates_dead_letter_after_retry_limit()
    {
        var scope = await CreatePersistentScopeAsync();
        var date = new DateTime(2099, 6, 10);

        try
        {
            await SeedRebuildScopeAsync(scope, date);
            await using var lockConnection = new SqlConnection(ConnectionString);
            await lockConnection.OpenAsync();
            await using var lockTransaction = lockConnection.BeginTransaction();
            await AcquireLockAsync(lockConnection, lockTransaction, SnapshotScopeResource(scope));

            await ProcessSnapshotQueueAsync(scope, maxRetryCount: 1, workerName: "TDD-Snapshot-DLQ");
            var queue = await ReadQueueAsync(scope);

            Assert.Equal("FAILED_FINAL", queue.LifecycleStatus);
            Assert.Equal("FAILED", queue.LegacyStatus);
            Assert.Equal(1, await IntScalarAsync(
                "SELECT COUNT(*) FROM dbo.InventorySnapshot_RebuildDeadLetter WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId)));
            await lockTransaction.RollbackAsync();
        }
        finally
        {
            await CleanupPersistentScopeAsync(scope);
        }
    }

    [Fact]
    public async Task Reconciliation_detects_drift_without_changing_current_projection()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var scope = await CreateScopeAsync(connection, transaction);
            var date = new DateTime(2099, 7, 10);
            await CreatePostedReceiptAsync(connection, transaction, scope, date, 100m);
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, 100, 0);",
                BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.Inventory_Movement_Daily(Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 100, 0, 1);",
                Date("@Date", date), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 100, 0, 100, 100, 0, 1);",
                Date("@Date", date), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, 100, 1, 1);",
                Date("@Date", date), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));

            var firstRun = await RunReconciliationAsync(connection, transaction, scope, date);
            Assert.Equal(0, await IntScalarAsync(connection, transaction,
                "SELECT COUNT(*) FROM dbo.InventoryReconciliation_Result WHERE Run_ID = @RunId AND Status = N'FAIL';", BigInt("@RunId", firstRun)));

            await ExecuteAsync(connection, transaction,
                "UPDATE dbo.InventoryBalance_Snapshot_Daily SET ClosingQuantity = 99 WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", date), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
            var secondRun = await RunReconciliationAsync(connection, transaction, scope, date);

            Assert.Equal(1, await IntScalarAsync(connection, transaction,
                "SELECT COUNT(*) FROM dbo.InventoryReconciliation_Result WHERE Run_ID = @RunId AND Check_Type = N'SNAPSHOT_CLOSING' AND Status = N'FAIL';", BigInt("@RunId", secondRun)));
            Assert.Equal(100m, await DecimalScalarAsync(connection, transaction,
                "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId)));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task ApplySnapshotInvalidationAsync(SqlConnection connection, SqlTransaction? transaction, Scope scope, DateTime fromDate)
    {
        await ExecuteAsync(connection, transaction,
            """
            DECLARE @Affected dbo.InventorySnapshotAffectedType;
            INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
            VALUES (@WarehouseId, @ProductId, @FromDate, N'TDD_HARDENING');
            EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
            """,
            BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId), Date("@FromDate", fromDate));
    }

    private static async Task SeedRebuildScopeAsync(Scope scope, DateTime date)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, null,
            "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, 0, 1, 1);",
            Date("@Date", date), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@ProductId", scope.ProductId));
        await ApplySnapshotInvalidationAsync(connection, null, scope, date);
    }

    private static async Task ProcessSnapshotQueueAsync(Scope scope, int maxRetryCount, string workerName)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteStoredAsync(connection, null, "dbo.sp_Inventory_Snapshot_Process_RebuildQueue",
            Int("@Batch_Size", 1), Int("@Max_Retry_Count", maxRetryCount), Int("@Processing_Lease_Seconds", 300),
            Text("@Worker_Name", workerName, 128), BigInt("@Kho_ID", scope.WarehouseId), BigInt("@San_Pham_ID", scope.ProductId));
    }

    private static async Task<long> RunReconciliationAsync(SqlConnection connection, SqlTransaction transaction, Scope scope, DateTime date)
    {
        await using var command = new SqlCommand("dbo.sp_Inventory_Reconciliation_Run", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add(Date("@As_Of_Date", date));
        command.Parameters.Add(BigInt("@Kho_ID", scope.WarehouseId));
        command.Parameters.Add(BigInt("@San_Pham_ID", scope.ProductId));
        var runId = new SqlParameter("@Run_ID", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
        command.Parameters.Add(runId);
        await command.ExecuteNonQueryAsync();
        return Convert.ToInt64(runId.Value);
    }

    private static async Task<Scope> CreateScopeAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var productId = await LongScalarAsync(connection, transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
        var supplierId = await LongScalarAsync(connection, transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
        var tag = $"TDD-SNAPSHOT-HARD-{Guid.NewGuid():N}"[..40];
        var warehouseId = await LongScalarAsync(connection, transaction,
            "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'TDD snapshot hardening');",
            Text("@Name", tag, 255));
        return new Scope(warehouseId, productId, supplierId, tag, null);
    }

    private static async Task<Scope> CreatePostableScopeAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var scope = await CreateScopeAsync(connection, transaction);
        var login = $"{scope.Tag}-login";
        var memberId = await LongScalarAsync(connection, transaction, "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
        await ExecuteAsync(connection, transaction,
            "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, N'TDD snapshot hardening', 0);",
            BigInt("@MemberId", memberId), Text("@Login", login, 100));
        await ExecuteAsync(connection, transaction,
            "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
            Text("@Login", login, 100), BigInt("@WarehouseId", scope.WarehouseId));
        return scope with { Login = login };
    }

    private static async Task<Scope> CreatePersistentScopeAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        var scope = await CreateScopeAsync(connection, transaction);
        await transaction.CommitAsync();
        return scope;
    }

    private static async Task<long> CreateDraftReceiptAsync(SqlConnection connection, SqlTransaction transaction, Scope scope, DateTime date, decimal quantity)
    {
        var documentId = await LongScalarAsync(connection, transaction,
            "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, @Date, 0, N'TDD snapshot hardening'); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
            Text("@Number", $"{scope.Tag}-{Guid.NewGuid():N}", 100), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@SupplierId", scope.SupplierId), Date("@Date", date));
        await ExecuteAsync(connection, transaction,
            "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
            BigInt("@DocumentId", documentId), BigInt("@ProductId", scope.ProductId), Decimal("@Quantity", quantity));
        return documentId;
    }

    private static async Task CreatePostedReceiptAsync(SqlConnection connection, SqlTransaction transaction, Scope scope, DateTime date, decimal quantity)
    {
        await ExecuteAsync(connection, transaction, "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
        var documentId = await LongScalarAsync(connection, transaction,
            "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, @Date, 1, N'TDD snapshot hardening'); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
            Text("@Number", $"{scope.Tag}-R-{Guid.NewGuid():N}", 100), BigInt("@WarehouseId", scope.WarehouseId), BigInt("@SupplierId", scope.SupplierId), Date("@Date", date));
        await ExecuteAsync(connection, transaction,
            "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
            BigInt("@DocumentId", documentId), BigInt("@ProductId", scope.ProductId), Decimal("@Quantity", quantity));
        await ExecuteAsync(connection, transaction, "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = NULL;");
    }

    private static async Task CreatePostedIssueAsync(SqlConnection connection, SqlTransaction transaction, Scope scope, DateTime date, decimal quantity)
    {
        await ExecuteAsync(connection, transaction, "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
        var documentId = await LongScalarAsync(connection, transaction,
            "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @Date, 1, N'TDD snapshot hardening'); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
            Text("@Number", $"{scope.Tag}-I-{Guid.NewGuid():N}", 100), BigInt("@WarehouseId", scope.WarehouseId), Date("@Date", date));
        await ExecuteAsync(connection, transaction,
            "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
            BigInt("@DocumentId", documentId), BigInt("@ProductId", scope.ProductId), Decimal("@Quantity", quantity));
        await ExecuteAsync(connection, transaction, "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = NULL;");
    }

    private static async Task AcquireLockAsync(SqlConnection connection, SqlTransaction transaction, string resource)
    {
        await ExecuteAsync(connection, transaction,
            """
            DECLARE @Result INT;
            EXEC @Result = sys.sp_getapplock @Resource = @Resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
            IF @Result < 0 THROW 52911, N'TDD could not acquire snapshot applock.', 1;
            """,
            Text("@Resource", resource, 255));
    }

    private static async Task<QueueRow> ReadQueueAsync(SqlConnection connection, SqlTransaction? transaction, Scope scope)
    {
        await using var command = new SqlCommand(
            "SELECT TOP (1) Status, RequestType, LifecycleStatus, AttemptCount, NextAttemptAt FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId ORDER BY ID DESC;",
            connection, transaction);
        command.Parameters.Add(BigInt("@WarehouseId", scope.WarehouseId));
        command.Parameters.Add(BigInt("@ProductId", scope.ProductId));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new QueueRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetDateTime(4));
    }

    private static async Task<QueueRow> ReadQueueAsync(Scope scope)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await ReadQueueAsync(connection, null, scope);
    }

    private static async Task CleanupPersistentScopeAsync(Scope scope)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await ExecuteAsync(connection, transaction, "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            await ExecuteAsync(connection, transaction,
                "DELETE dl FROM dbo.InventorySnapshot_RebuildDeadLetter dl JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = dl.Queue_ID WHERE q.Kho_ID = @WarehouseId;",
                BigInt("@WarehouseId", scope.WarehouseId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", scope.WarehouseId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", scope.WarehouseId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;", BigInt("@WarehouseId", scope.WarehouseId));
            await ExecuteAsync(connection, transaction, "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = NULL;");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static string SnapshotScopeResource(Scope scope) => $"InventorySnapshot:{scope.WarehouseId}:{scope.ProductId}";

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

    private static async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, null, sql, parameters);
    }

    private static async Task<long> LongScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) => Convert.ToInt64(await ScalarAsync(connection, transaction, sql, parameters));

    private static async Task<int> IntScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) => Convert.ToInt32(await ScalarAsync(connection, transaction, sql, parameters));

    private static async Task<int> IntScalarAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await IntScalarAsync(connection, null, sql, parameters);
    }

    private static async Task<decimal> DecimalScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) => Convert.ToDecimal(await ScalarAsync(connection, transaction, sql, parameters));

    private static async Task<object?> ScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    private static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.NVarChar, size) { Value = value };
    private static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt) { Value = value };
    private static SqlParameter Int(string name, int value) => new(name, SqlDbType.Int) { Value = value };
    private static SqlParameter Bit(string name, bool value) => new(name, SqlDbType.Bit) { Value = value };
    private static SqlParameter Date(string name, DateTime value) => new(name, SqlDbType.Date) { Value = value.Date };
    private static SqlParameter Decimal(string name, decimal value) => new(name, SqlDbType.Decimal) { Precision = 18, Scale = 3, Value = value };

    private sealed record Scope(long WarehouseId, long ProductId, long SupplierId, string Tag, string? Login);
    private sealed record QueueRow(string LegacyStatus, string RequestType, string LifecycleStatus, int AttemptCount, DateTime? NextAttemptAt);
}
