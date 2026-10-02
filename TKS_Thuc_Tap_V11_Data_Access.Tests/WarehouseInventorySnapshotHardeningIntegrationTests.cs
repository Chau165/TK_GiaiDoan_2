using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehouseInventorySnapshotHardeningIntegrationTests
{
    private static string ConnectionString
    {
        get
        {
            return Environment.GetEnvironmentVariable("TKS_INTEGRATION_CONNECTION_STRING") ?? "Server=localhost;Database=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True;TrustServerCertificate=True;";
        }
    }

    [Fact]
    public async Task Missing_snapshot_scope_is_enqueued_as_initialize_required()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
            var v_dtmMovementDate = new DateTime(2099, 1, 3);

            await ApplySnapshotInvalidationAsync(v_Connection, v_Transaction, v_Scope, v_dtmMovementDate);
            var v_Queue = await ReadQueueAsync(v_Connection, v_Transaction, v_Scope);

            Assert.Equal("INITIALIZE", v_Queue.RequestType);
            Assert.Equal("INITIALIZE_REQUIRED", v_Queue.LifecycleStatus);
            Assert.Equal("WAITING", v_Queue.LegacyStatus);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Bootstrap_uses_only_posted_ledger_and_is_idempotent()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
            await CreatePostedReceiptAsync(v_Connection, v_Transaction, v_Scope, new DateTime(2098, 12, 31), 140m);
            await CreatePostedIssueAsync(v_Connection, v_Transaction, v_Scope, new DateTime(2098, 12, 31), 40m);
            await CreatePostedReceiptAsync(v_Connection, v_Transaction, v_Scope, new DateTime(2099, 1, 3), 20m);

            await ExecuteStoredAsync(v_Connection, v_Transaction, "dbo.sp_Inventory_Snapshot_Bootstrap_From_Ledger",
                Date("@Baseline_Date", new DateTime(2098, 12, 31)),
                Bit("@Opening_Balance_Confirmed", true),
                BigInt("@Kho_ID", v_Scope.WarehouseId),
                BigInt("@San_Pham_ID", v_Scope.ProductId));

            await ExecuteStoredAsync(v_Connection, v_Transaction, "dbo.sp_Inventory_Snapshot_Bootstrap_From_Ledger",
                Date("@Baseline_Date", new DateTime(2098, 12, 31)),
                Bit("@Opening_Balance_Confirmed", true),
                BigInt("@Kho_ID", v_Scope.WarehouseId),
                BigInt("@San_Pham_ID", v_Scope.ProductId));

            Assert.Equal(100m, await DecimalScalarAsync(v_Connection, v_Transaction,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", new DateTime(2098, 12, 31)), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId)));
            Assert.Equal(1, await IntScalarAsync(v_Connection, v_Transaction,
                "SELECT COUNT(*) FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", new DateTime(2098, 12, 31)), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId)));
            Assert.Equal(2, await IntScalarAsync(v_Connection, v_Transaction,
                "SELECT COUNT(*) FROM dbo.InventorySnapshot_BootstrapAudit WHERE Baseline_Date = @Date AND Status = N'COMPLETED';",
                Date("@Date", new DateTime(2098, 12, 31))));
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Bootstrap_requires_explicit_opening_balance_confirmation()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
            var v_Error = await Assert.ThrowsAsync<SqlException>(() => ExecuteStoredAsync(v_Connection, v_Transaction,
                "dbo.sp_Inventory_Snapshot_Bootstrap_From_Ledger",
                Date("@Baseline_Date", new DateTime(2098, 12, 31)),
                Bit("@Opening_Balance_Confirmed", false),
                BigInt("@Kho_ID", v_Scope.WarehouseId),
                BigInt("@San_Pham_ID", v_Scope.ProductId)));

            Assert.Equal(51310, v_Error.Number);
            Assert.Contains("OPENING_BALANCE_REQUIRED", v_Error.Message, StringComparison.Ordinal);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Finalize_daily_uses_balance_daily_closing_not_current_quantity()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
            var v_dtmSnapshotDate = new DateTime(2099, 2, 15);
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, 999, 0);",
                BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 50, 30, 3, 77, 30, 3, 1);",
                Date("@Date", new DateTime(2099, 2, 10)), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            await ExecuteStoredAsync(v_Connection, v_Transaction, "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                Date("@Snapshot_Date", v_dtmSnapshotDate), BigInt("@Kho_ID", v_Scope.WarehouseId), BigInt("@San_Pham_ID", v_Scope.ProductId));

            Assert.Equal(77m, await DecimalScalarAsync(v_Connection, v_Transaction,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId)));
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Finalize_blocks_relevant_movement_failed_final_before_publish()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
            var v_dtmSnapshotDate = new DateTime(2099, 4, 10);
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 100, 0, 100, 100, 0, 1);",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventoryMovement_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, To_Date, Status, Retry_Count, ErrorMessage, LastError) VALUES (@WarehouseId, @ProductId, @Date, @Date, N'FAILED_FINAL', 3, N'failed movement rebuild', N'failed movement rebuild');",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            var v_Error = await Assert.ThrowsAsync<SqlException>(() => ExecuteStoredAsync(v_Connection, v_Transaction,
                "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                Date("@Snapshot_Date", v_dtmSnapshotDate), BigInt("@Kho_ID", v_Scope.WarehouseId), BigInt("@San_Pham_ID", v_Scope.ProductId)));

            Assert.Equal(51320, v_Error.Number);
            Assert.Equal(0, await IntScalarAsync(v_Connection, v_Transaction,
                "SELECT COUNT(*) FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId)));
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Finalize_blocks_relevant_snapshot_failed_final_before_publish()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
            var v_dtmSnapshotDate = new DateTime(2099, 5, 10);
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 100, 0, 100, 100, 0, 1);",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventorySnapshot_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, Status, RequestType, LifecycleStatus, AttemptCount, LastError) VALUES (@WarehouseId, @ProductId, @Date, N'FAILED', N'REBUILD', N'FAILED_FINAL', 5, N'failed snapshot rebuild');",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            var v_Error = await Assert.ThrowsAsync<SqlException>(() => ExecuteStoredAsync(v_Connection, v_Transaction,
                "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                Date("@Snapshot_Date", v_dtmSnapshotDate), BigInt("@Kho_ID", v_Scope.WarehouseId), BigInt("@San_Pham_ID", v_Scope.ProductId)));

            Assert.Equal(51321, v_Error.Number);
            Assert.Equal(0, await IntScalarAsync(v_Connection, v_Transaction,
                "SELECT COUNT(*) FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId)));
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Theory]
    [InlineData("WAITING")]
    [InlineData("PROCESSING")]
    [InlineData("RETRY_WAITING")]
    public async Task Finalize_still_blocks_active_movement_rebuild_states(string p_Status)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
            var v_dtmSnapshotDate = new DateTime(2099, 6, 10);
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventoryMovement_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, To_Date, Status, Retry_Count, ErrorMessage, LastError) VALUES (@WarehouseId, @ProductId, @Date, @Date, @Status, 0, N'active movement rebuild', N'active movement rebuild');",
                Date("@Date", v_dtmSnapshotDate), Text("@Status", p_Status, 20), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            var v_Error = await Assert.ThrowsAsync<SqlException>(() => ExecuteStoredAsync(v_Connection, v_Transaction,
                "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                Date("@Snapshot_Date", v_dtmSnapshotDate), BigInt("@Kho_ID", v_Scope.WarehouseId), BigInt("@San_Pham_ID", v_Scope.ProductId)));

            Assert.Equal(51320, v_Error.Number);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Theory]
    [InlineData("WAITING")]
    [InlineData("PROCESSING")]
    [InlineData("RETRY_WAITING")]
    public async Task Finalize_keeps_snapshot_invalid_while_snapshot_rebuild_is_active(string p_LifecycleStatus)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
            var v_dtmSnapshotDate = new DateTime(2099, 7, 10);
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 100, 0, 100, 100, 0, 1);",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));
            string v_Status;
            if (p_LifecycleStatus == "PROCESSING")
            {
                v_Status = "PROCESSING";
            }
            else
            {
                v_Status = "WAITING";
            }

            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventorySnapshot_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, Status, RequestType, LifecycleStatus, AttemptCount, LastError) VALUES (@WarehouseId, @ProductId, @Date, @Status, N'REBUILD', @LifecycleStatus, 0, N'active snapshot rebuild');",
                Date("@Date", v_dtmSnapshotDate), Text("@Status", v_Status, 20), Text("@LifecycleStatus", p_LifecycleStatus, 24), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            await ExecuteStoredAsync(v_Connection, v_Transaction,
                "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                Date("@Snapshot_Date", v_dtmSnapshotDate), BigInt("@Kho_ID", v_Scope.WarehouseId), BigInt("@San_Pham_ID", v_Scope.ProductId));

            Assert.Equal(0, await IntScalarAsync(v_Connection, v_Transaction,
                "SELECT IsValid FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId)));
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Finalize_allows_checkpoint_after_failed_final_recovery()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
            var v_dtmSnapshotDate = new DateTime(2099, 8, 10);
            await CreatePostedReceiptAsync(v_Connection, v_Transaction, v_Scope, v_dtmSnapshotDate, 120m);
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 120, 0, 120, 120, 0, 1);",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, 100, 0, 1);",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE dl FROM dbo.InventorySnapshot_RebuildDeadLetter dl JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = dl.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId; DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));
            var queueId = await LongScalarAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventorySnapshot_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, Status, RequestType, LifecycleStatus, AttemptCount, LastError) OUTPUT INSERTED.ID VALUES (@WarehouseId, @ProductId, @Date, N'FAILED', N'REBUILD', N'FAILED_FINAL', 5, N'failed before recovery');",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            await ExecuteAsync(v_Connection, v_Transaction,
                "UPDATE dbo.InventorySnapshot_RebuildQueue SET Status = N'WAITING', LifecycleStatus = N'WAITING', AttemptCount = 0, LastError = NULL, ErrorMessage = NULL, CompletedAt = NULL, NextAttemptAt = NULL WHERE ID = @QueueId;",
                BigInt("@QueueId", queueId));
            await ExecuteStoredAsync(v_Connection, v_Transaction,
                "dbo.sp_Inventory_Snapshot_Process_RebuildQueue",
                Int("@Batch_Size", 1), Int("@Max_Retry_Count", 5), Int("@Processing_Lease_Seconds", 300),
                Text("@Worker_Name", "TDD-Snapshot-Recovery", 128), BigInt("@Kho_ID", v_Scope.WarehouseId), BigInt("@San_Pham_ID", v_Scope.ProductId));
            Assert.Equal(120m, await DecimalScalarAsync(v_Connection, v_Transaction,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND IsValid = 1;",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId)));
            await ExecuteStoredAsync(v_Connection, v_Transaction,
                "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                Date("@Snapshot_Date", v_dtmSnapshotDate), BigInt("@Kho_ID", v_Scope.WarehouseId), BigInt("@San_Pham_ID", v_Scope.ProductId));

            Assert.Equal(120m, await DecimalScalarAsync(v_Connection, v_Transaction,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND IsValid = 1;",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId)));
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Finalize_scoped_checkpoint_ignores_unrelated_failed_final()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Healthy = await CreateScopeAsync(v_Connection, v_Transaction);
            var v_Failed = await CreateScopeAsync(v_Connection, v_Transaction);
            var v_dtmSnapshotDate = new DateTime(2099, 9, 10);
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 77, 0, 77, 77, 0, 1);",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Healthy.WarehouseId), BigInt("@ProductId", v_Healthy.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventoryMovement_RebuildQueue(Kho_ID, San_Pham_ID, From_Date, To_Date, Status, Retry_Count, ErrorMessage, LastError) VALUES (@WarehouseId, @ProductId, @Date, @Date, N'FAILED_FINAL', 3, N'unrelated failure', N'unrelated failure');",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Failed.WarehouseId), BigInt("@ProductId", v_Failed.ProductId));

            await ExecuteStoredAsync(v_Connection, v_Transaction,
                "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                Date("@Snapshot_Date", v_dtmSnapshotDate), BigInt("@Kho_ID", v_Healthy.WarehouseId), BigInt("@San_Pham_ID", v_Healthy.ProductId));

            Assert.Equal(77m, await DecimalScalarAsync(v_Connection, v_Transaction,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND IsValid = 1;",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Healthy.WarehouseId), BigInt("@ProductId", v_Healthy.ProductId)));
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Back_dated_post_invalidates_and_rebuilds_existing_snapshot()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreatePostableScopeAsync(v_Connection, v_Transaction);
            var v_dtmSnapshotDate = new DateTime(2099, 3, 10);
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, 0, 1, 1);",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));
            var receiptId = await CreateDraftReceiptAsync(v_Connection, v_Transaction, v_Scope, new DateTime(2099, 3, 5), 10m);

            await ExecuteStoredAsync(v_Connection, v_Transaction, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", true), BigInt("@Document_ID", receiptId), Text("@Ma_Dang_Nhap", v_Scope.Login!, 100));

            var v_Queued = await ReadQueueAsync(v_Connection, v_Transaction, v_Scope);
            Assert.Equal("REBUILD", v_Queued.RequestType);
            Assert.Equal("WAITING", v_Queued.LifecycleStatus);

            await ExecuteStoredAsync(v_Connection, v_Transaction, "dbo.sp_Inventory_Snapshot_Process_RebuildQueue",
                Int("@Batch_Size", 1), Int("@Max_Retry_Count", 5), Int("@Processing_Lease_Seconds", 300),
                Text("@Worker_Name", "TDD-Snapshot-Backdate", 128), BigInt("@Kho_ID", v_Scope.WarehouseId), BigInt("@San_Pham_ID", v_Scope.ProductId));

            Assert.Equal(10m, await DecimalScalarAsync(v_Connection, v_Transaction,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND IsValid = 1;",
                Date("@Date", v_dtmSnapshotDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId)));
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Expired_snapshot_claim_is_recovered_with_retry_waiting()
    {
        var v_Scope = await CreatePersistentScopeAsync();
        var v_dtmDate = new DateTime(2099, 4, 10);

        try
        {
            await SeedRebuildScopeAsync(v_Scope, v_dtmDate);
            await ExecuteAsync(
                "UPDATE dbo.InventorySnapshot_RebuildQueue SET Status = N'PROCESSING', LifecycleStatus = N'PROCESSING', LeaseUntil = '2000-01-01', ClaimedAt = '2000-01-01', AttemptCount = 0 WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            await ProcessSnapshotQueueAsync(v_Scope, p_iMaxRetryCount: 5, p_WorkerName: "TDD-Snapshot-Lease");
            var v_Queue = await ReadQueueAsync(v_Scope);

            Assert.Equal("RETRY_WAITING", v_Queue.LifecycleStatus);
            Assert.Equal(1, v_Queue.AttemptCount);
            Assert.NotNull(v_Queue.NextAttemptAt);
        }
        finally
        {
            await CleanupPersistentScopeAsync(v_Scope);
        }
    }

    [Fact]
    public async Task Transient_snapshot_error_uses_one_minute_retry_backoff()
    {
        var v_Scope = await CreatePersistentScopeAsync();
        var v_dtmDate = new DateTime(2099, 5, 10);

        try
        {
            await SeedRebuildScopeAsync(v_Scope, v_dtmDate);
            await using var v_LockConnection = new SqlConnection(ConnectionString);
            await v_LockConnection.OpenAsync();
            await using var v_LockTransaction = v_LockConnection.BeginTransaction();
            await AcquireLockAsync(v_LockConnection, v_LockTransaction, SnapshotScopeResource(v_Scope));

            await ProcessSnapshotQueueAsync(v_Scope, p_iMaxRetryCount: 5, p_WorkerName: "TDD-Snapshot-Retry");
            var v_Queue = await ReadQueueAsync(v_Scope);

            Assert.Equal("RETRY_WAITING", v_Queue.LifecycleStatus);
            Assert.Equal(1, v_Queue.AttemptCount);
            Assert.NotNull(v_Queue.NextAttemptAt);
            Assert.True(v_Queue.NextAttemptAt >= DateTime.UtcNow.AddSeconds(45));
            await v_LockTransaction.RollbackAsync();
            await ExecuteAsync(
                "UPDATE dbo.InventorySnapshot_RebuildQueue SET NextAttemptAt = '2000-01-01' WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            await ProcessSnapshotQueueAsync(v_Scope, p_iMaxRetryCount: 5, p_WorkerName: "TDD-Snapshot-Retry-Released");
            var v_Completed = await ReadQueueAsync(v_Scope);
            Assert.Equal("COMPLETED", v_Completed.LifecycleStatus);
            Assert.Equal(1, v_Completed.AttemptCount);
        }
        finally
        {
            await CleanupPersistentScopeAsync(v_Scope);
        }
    }

    [Fact]
    public async Task Permanent_snapshot_failure_creates_dead_letter_after_retry_limit()
    {
        var v_Scope = await CreatePersistentScopeAsync();
        var v_dtmDate = new DateTime(2099, 6, 10);

        try
        {
            await SeedRebuildScopeAsync(v_Scope, v_dtmDate);
            await using var v_LockConnection = new SqlConnection(ConnectionString);
            await v_LockConnection.OpenAsync();
            await using var v_LockTransaction = v_LockConnection.BeginTransaction();
            await AcquireLockAsync(v_LockConnection, v_LockTransaction, SnapshotScopeResource(v_Scope));

            await ProcessSnapshotQueueAsync(v_Scope, p_iMaxRetryCount: 1, p_WorkerName: "TDD-Snapshot-DLQ");
            var v_Queue = await ReadQueueAsync(v_Scope);

            Assert.Equal("FAILED_FINAL", v_Queue.LifecycleStatus);
            Assert.Equal("FAILED", v_Queue.LegacyStatus);
            Assert.Equal(1, await IntScalarAsync(
                "SELECT COUNT(*) FROM dbo.InventorySnapshot_RebuildDeadLetter WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId)));
            await v_LockTransaction.RollbackAsync();
        }
        finally
        {
            await CleanupPersistentScopeAsync(v_Scope);
        }
    }

    [Fact]
    public async Task Reconciliation_detects_drift_without_changing_current_projection()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
            var v_dtmDate = new DateTime(2099, 7, 10);
            await CreatePostedReceiptAsync(v_Connection, v_Transaction, v_Scope, v_dtmDate, 100m);
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, 100, 0);",
                BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Movement_Daily(Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 100, 0, 1);",
                Date("@Date", v_dtmDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 100, 0, 100, 100, 0, 1);",
                Date("@Date", v_dtmDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, 100, 1, 1);",
                Date("@Date", v_dtmDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            var firstRun = await RunReconciliationAsync(v_Connection, v_Transaction, v_Scope, v_dtmDate);
            Assert.Equal(0, await IntScalarAsync(v_Connection, v_Transaction,
                "SELECT COUNT(*) FROM dbo.InventoryReconciliation_Result WHERE Run_ID = @RunId AND Status = N'FAIL';", BigInt("@RunId", firstRun)));

            await ExecuteAsync(v_Connection, v_Transaction,
                "UPDATE dbo.InventoryBalance_Snapshot_Daily SET ClosingQuantity = 99 WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", v_dtmDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));
            var secondRun = await RunReconciliationAsync(v_Connection, v_Transaction, v_Scope, v_dtmDate);

            Assert.Equal(1, await IntScalarAsync(v_Connection, v_Transaction,
                "SELECT COUNT(*) FROM dbo.InventoryReconciliation_Result WHERE Run_ID = @RunId AND Check_Type = N'SNAPSHOT_CLOSING' AND Status = N'FAIL';", BigInt("@RunId", secondRun)));
            Assert.Equal(100m, await DecimalScalarAsync(v_Connection, v_Transaction,
                "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId)));
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    private static async Task ApplySnapshotInvalidationAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, Scope p_Scope, DateTime p_dtmFromDate)
    {
        await ExecuteAsync(p_Connection, p_Transaction,
            """
            DECLARE @Affected dbo.InventorySnapshotAffectedType;
            INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
            VALUES (@WarehouseId, @ProductId, @FromDate, N'TDD_HARDENING');
            EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
            """,
            BigInt("@WarehouseId", p_Scope.WarehouseId), BigInt("@ProductId", p_Scope.ProductId), Date("@FromDate", p_dtmFromDate));
    }

    private static async Task SeedRebuildScopeAsync(Scope p_Scope, DateTime p_dtmDate)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await ExecuteAsync(v_Connection, null,
            "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, 0, 1, 1);",
            Date("@Date", p_dtmDate), BigInt("@WarehouseId", p_Scope.WarehouseId), BigInt("@ProductId", p_Scope.ProductId));
        await ApplySnapshotInvalidationAsync(v_Connection, null, p_Scope, p_dtmDate);
    }

    private static async Task ProcessSnapshotQueueAsync(Scope p_Scope, int p_iMaxRetryCount, string p_WorkerName)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await ExecuteStoredAsync(v_Connection, null, "dbo.sp_Inventory_Snapshot_Process_RebuildQueue",
            Int("@Batch_Size", 1), Int("@Max_Retry_Count", p_iMaxRetryCount), Int("@Processing_Lease_Seconds", 300),
            Text("@Worker_Name", p_WorkerName, 128), BigInt("@Kho_ID", p_Scope.WarehouseId), BigInt("@San_Pham_ID", p_Scope.ProductId));
    }

    private static async Task<long> RunReconciliationAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, Scope p_Scope, DateTime p_dtmDate)
    {
        await using var v_Command = new SqlCommand("dbo.sp_Inventory_Reconciliation_Run", p_Connection, p_Transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        v_Command.Parameters.Add(Date("@As_Of_Date", p_dtmDate));
        v_Command.Parameters.Add(BigInt("@Kho_ID", p_Scope.WarehouseId));
        v_Command.Parameters.Add(BigInt("@San_Pham_ID", p_Scope.ProductId));
        var v_RunId = new SqlParameter("@Run_ID", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
        v_Command.Parameters.Add(v_RunId);
        await v_Command.ExecuteNonQueryAsync();
        return Convert.ToInt64(v_RunId.Value);
    }

    private static async Task<Scope> CreateScopeAsync(SqlConnection p_Connection, SqlTransaction p_Transaction)
    {
        var productId = await LongScalarAsync(p_Connection, p_Transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
        var supplierId = await LongScalarAsync(p_Connection, p_Transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
        var v_Tag = $"TDD-SNAPSHOT-HARD-{Guid.NewGuid():N}"[..40];
        var warehouseId = await LongScalarAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'TDD snapshot hardening');",
            Text("@Name", v_Tag, 255));
        return new Scope(warehouseId, productId, supplierId, v_Tag, null);
    }

    private static async Task<Scope> CreatePostableScopeAsync(SqlConnection p_Connection, SqlTransaction p_Transaction)
    {
        var v_Scope = await CreateScopeAsync(p_Connection, p_Transaction);
        var v_Login = $"{v_Scope.Tag}-login";
        var memberId = await LongScalarAsync(p_Connection, p_Transaction, "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
        await ExecuteAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, N'TDD snapshot hardening', 0);",
            BigInt("@MemberId", memberId), Text("@Login", v_Login, 100));
        await ExecuteAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
            Text("@Login", v_Login, 100), BigInt("@WarehouseId", v_Scope.WarehouseId));
        return v_Scope with { Login = v_Login };
    }

    private static async Task<Scope> CreatePersistentScopeAsync()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        var v_Scope = await CreateScopeAsync(v_Connection, v_Transaction);
        await v_Transaction.CommitAsync();
        return v_Scope;
    }

    private static async Task<long> CreateDraftReceiptAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, Scope p_Scope, DateTime p_dtmDate, decimal p_Quantity)
    {
        var documentId = await LongScalarAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, @Date, 0, N'TDD snapshot hardening'); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
            Text("@Number", $"{p_Scope.Tag}-{Guid.NewGuid():N}", 100), BigInt("@WarehouseId", p_Scope.WarehouseId), BigInt("@SupplierId", p_Scope.SupplierId), Date("@Date", p_dtmDate));
        await ExecuteAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
            BigInt("@DocumentId", documentId), BigInt("@ProductId", p_Scope.ProductId), Decimal("@Quantity", p_Quantity));
        return documentId;
    }

    private static async Task CreatePostedReceiptAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, Scope p_Scope, DateTime p_dtmDate, decimal p_Quantity)
    {
        await ExecuteAsync(p_Connection, p_Transaction, "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
        var documentId = await LongScalarAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, @Date, 1, N'TDD snapshot hardening'); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
            Text("@Number", $"{p_Scope.Tag}-R-{Guid.NewGuid():N}", 100), BigInt("@WarehouseId", p_Scope.WarehouseId), BigInt("@SupplierId", p_Scope.SupplierId), Date("@Date", p_dtmDate));
        await ExecuteAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
            BigInt("@DocumentId", documentId), BigInt("@ProductId", p_Scope.ProductId), Decimal("@Quantity", p_Quantity));
        await ExecuteAsync(p_Connection, p_Transaction, "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = NULL;");
    }

    private static async Task CreatePostedIssueAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, Scope p_Scope, DateTime p_dtmDate, decimal p_Quantity)
    {
        await ExecuteAsync(p_Connection, p_Transaction, "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
        var documentId = await LongScalarAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @Date, 1, N'TDD snapshot hardening'); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
            Text("@Number", $"{p_Scope.Tag}-I-{Guid.NewGuid():N}", 100), BigInt("@WarehouseId", p_Scope.WarehouseId), Date("@Date", p_dtmDate));
        await ExecuteAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
            BigInt("@DocumentId", documentId), BigInt("@ProductId", p_Scope.ProductId), Decimal("@Quantity", p_Quantity));
        await ExecuteAsync(p_Connection, p_Transaction, "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = NULL;");
    }

    private static async Task AcquireLockAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Resource)
    {
        await ExecuteAsync(p_Connection, p_Transaction,
            """
            DECLARE @Result INT;
            EXEC @Result = sys.sp_getapplock @Resource = @Resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
            IF @Result < 0 THROW 52911, N'TDD could not acquire snapshot applock.', 1;
            """,
            Text("@Resource", p_Resource, 255));
    }

    private static async Task<QueueRow> ReadQueueAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, Scope p_Scope)
    {
        await using var v_Command = new SqlCommand(
            "SELECT TOP (1) Status, RequestType, LifecycleStatus, AttemptCount, NextAttemptAt FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId ORDER BY ID DESC;",
            p_Connection, p_Transaction);
        v_Command.Parameters.Add(BigInt("@WarehouseId", p_Scope.WarehouseId));
        v_Command.Parameters.Add(BigInt("@ProductId", p_Scope.ProductId));
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        DateTime? v_dtmNextAttemptAt;
        if (v_Reader.IsDBNull(4))
        {
            v_dtmNextAttemptAt = null;
        }
        else
        {
            v_dtmNextAttemptAt = v_Reader.GetDateTime(4);
        }

        return new QueueRow(
            v_Reader.GetString(0),
            v_Reader.GetString(1),
            v_Reader.GetString(2),
            v_Reader.GetInt32(3),
            v_dtmNextAttemptAt);
    }

    private static async Task<QueueRow> ReadQueueAsync(Scope p_Scope)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        return await ReadQueueAsync(v_Connection, null, p_Scope);
    }

    private static async Task CleanupPersistentScopeAsync(Scope p_Scope)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        try
        {
            await ExecuteAsync(v_Connection, v_Transaction, "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE dl FROM dbo.InventorySnapshot_RebuildDeadLetter dl JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = dl.Queue_ID WHERE q.Kho_ID = @WarehouseId;",
                BigInt("@WarehouseId", p_Scope.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", p_Scope.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId;", BigInt("@WarehouseId", p_Scope.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;", BigInt("@WarehouseId", p_Scope.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction, "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = NULL;");
            await v_Transaction.CommitAsync();
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static string SnapshotScopeResource(Scope p_Scope)
    {
        return $"InventorySnapshot:{p_Scope.WarehouseId}:{p_Scope.ProductId}";
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

    private static async Task ExecuteAsync(string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await ExecuteAsync(v_Connection, null, p_Sql, p_arrParameters);
    }

    private static async Task<long> LongScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToInt64(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
    }

    private static async Task<int> IntScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToInt32(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
    }

    private static async Task<int> IntScalarAsync(string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        return await IntScalarAsync(v_Connection, null, p_Sql, p_arrParameters);
    }

    private static async Task<decimal> DecimalScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToDecimal(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
    }

    private static async Task<object?> ScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        return await v_Command.ExecuteScalarAsync();
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
    private static SqlParameter Bit(string p_Name, bool p_bValue)
    {
        return new(p_Name, SqlDbType.Bit)
        {
            Value = p_bValue
        };
    }
    private static SqlParameter Date(string p_Name, DateTime p_dtmValue)
    {
        return new(p_Name, SqlDbType.Date)
        {
            Value = p_dtmValue.Date
        };
    }
    private static SqlParameter Decimal(string p_Name, decimal p_Value)
    {
        return new(p_Name, SqlDbType.Decimal)
        {
            Precision = 18,
            Scale = 3,
            Value = p_Value
        };
    }

    private sealed record Scope(long WarehouseId, long ProductId, long SupplierId, string Tag, string? Login);
    private sealed record QueueRow(string LegacyStatus, string RequestType, string LifecycleStatus, int AttemptCount, DateTime? NextAttemptAt);
}
