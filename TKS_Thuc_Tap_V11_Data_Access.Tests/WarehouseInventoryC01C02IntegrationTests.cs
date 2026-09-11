using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehouseInventoryC01C02IntegrationTests
{
    private const string DirectDmlProbeUser = "Phase10C01DmlProbe";

    private static string BaseConnectionString =>
        Environment.GetEnvironmentVariable("TKS_INTEGRATION_CONNECTION_STRING")
        ?? throw new InvalidOperationException(
            "TKS_INTEGRATION_CONNECTION_STRING must point to a disposable test database.");

    [Fact]
    public async Task Receipt_save_detail_without_ambient_transaction_owns_and_closes_transaction()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var receiptId = await SaveReceiptHeaderAsync(fixture, new DateTime(2026, 1, 10));
            await using var connection = OpenConnection($"C01-standalone-{fixture.Tag}");
            await connection.OpenAsync();

            await SaveReceiptDetailAsync(fixture, receiptId, 10, fixture.LoginA, connection: connection);

            Assert.Equal(0, await IntScalarAsync(connection, null, "SELECT @@TRANCOUNT;"));
            await AssertReceiptInvariantAsync(fixture, expectedLedger: 0, expectedCurrent: 0, expectedDetailCount: 1);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task Receipt_save_detail_inside_ambient_transaction_preserves_caller_ownership_and_rolls_back()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var receiptId = await SaveReceiptHeaderAsync(fixture, new DateTime(2026, 1, 10));
            await using var connection = OpenConnection($"C01-ambient-{fixture.Tag}");
            await connection.OpenAsync();
            await ExecuteAsync(connection, null, "CREATE TABLE #AmbientMarker(Value INT NOT NULL);");
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

            await ExecuteAsync(connection, transaction, "INSERT #AmbientMarker(Value) VALUES (1);");
            await SaveReceiptDetailAsync(
                fixture,
                receiptId,
                10,
                fixture.LoginA,
                connection: connection,
                transaction: transaction);

            Assert.Equal(1, await IntScalarAsync(connection, transaction, "SELECT @@TRANCOUNT;"));
            Assert.Equal(1, await IntScalarAsync(connection, transaction, "SELECT COUNT(*) FROM #AmbientMarker;"));
            Assert.Equal(1, await IntScalarAsync(connection, transaction, "SELECT COUNT(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @ReceiptId;", BigInt("@ReceiptId", receiptId)));

            await transaction.RollbackAsync();

            Assert.Equal(0, await IntScalarAsync(connection, null, "SELECT @@TRANCOUNT;"));
            Assert.Equal(0, await IntScalarAsync(connection, null, "SELECT COUNT(*) FROM #AmbientMarker;"));
            Assert.Equal(0, await IntScalarAsync(connection, null, "SELECT COUNT(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @ReceiptId;", BigInt("@ReceiptId", receiptId)));
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task Receipt_save_detail_error_inside_ambient_transaction_does_not_commit_caller_work()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var receiptId = await SaveReceiptHeaderAsync(fixture, new DateTime(2026, 1, 10));
            await using var connection = OpenConnection($"C01-ambient-error-{fixture.Tag}");
            await connection.OpenAsync();
            await ExecuteAsync(connection, null, "CREATE TABLE #AmbientMarker(Value INT NOT NULL);");
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await ExecuteAsync(connection, transaction, "INSERT #AmbientMarker(Value) VALUES (1);");

            await AssertSqlNumberAsync(
                51107,
                () => SaveReceiptDetailAsync(
                    fixture,
                    receiptId,
                    0,
                    fixture.LoginA,
                    connection: connection,
                    transaction: transaction));

            Assert.Equal(1, await IntScalarAsync(connection, transaction, "SELECT @@TRANCOUNT;"));
            var transactionState = await IntScalarAsync(connection, transaction, "SELECT XACT_STATE();");
            Assert.Contains(transactionState, new[] { 1, -1 });
            await transaction.RollbackAsync();
            Assert.Equal(0, await IntScalarAsync(connection, null, "SELECT COUNT(*) FROM #AmbientMarker;"));
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task Receipt_draft_post_and_posted_mutations_preserve_inventory_invariants()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var receiptId = await SaveReceiptHeaderAsync(fixture, new DateTime(2026, 1, 10));
            var detailId = await SaveReceiptDetailAsync(fixture, receiptId, 10, fixture.LoginA);

            await AssertReceiptInvariantAsync(fixture, expectedLedger: 0, expectedCurrent: 0, expectedDetailCount: 1);

            await SaveReceiptDetailAsync(fixture, receiptId, 12, fixture.LoginA, detailId);
            await DeleteReceiptDetailAsync(fixture, detailId, fixture.LoginA);
            await AssertReceiptInvariantAsync(fixture, expectedLedger: 0, expectedCurrent: 0, expectedDetailCount: 0);

            detailId = await SaveReceiptDetailAsync(fixture, receiptId, 12, fixture.LoginA);
            await PostReceiptAsync(fixture, receiptId, fixture.LoginA);
            await AssertReceiptInvariantAsync(fixture, expectedLedger: 12, expectedCurrent: 12, expectedDetailCount: 1);

            await AssertSqlNumberAsync(51162, () => PostReceiptAsync(fixture, receiptId, fixture.LoginA));
            await AssertSqlNumberAsync(
                51163,
                () => SaveReceiptDetailAsync(fixture, receiptId, 13, fixture.LoginA, detailId));
            await AssertSqlNumberAsync(51163, () => DeleteReceiptDetailAsync(fixture, detailId, fixture.LoginA));

            await AssertSqlNumberAsync(
                51228,
                () => InsertReceiptDetailDirectlyAsync(fixture, receiptId, 5));
            await AssertReceiptInvariantAsync(fixture, expectedLedger: 12, expectedCurrent: 12, expectedDetailCount: 1);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task Concurrent_save_detail_and_post_never_leaves_ledger_ahead_of_current()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var receiptId = await SaveReceiptHeaderAsync(fixture, new DateTime(2026, 1, 10));
            await SaveReceiptDetailAsync(fixture, receiptId, 100, fixture.LoginA);

            await using var gateConnection = OpenConnection($"C01-gate-{fixture.Tag}");
            await gateConnection.OpenAsync();
            await using var gateTransaction = (SqlTransaction)await gateConnection.BeginTransactionAsync();

            /* This X lock stops Save_Detail after its Is_Posted check and
               before its INSERT.  The old procedure has no parent lock, so
               Post can commit while Save_Detail is paused. */
            await ExecuteAsync(
                gateConnection,
                gateTransaction,
                "SELECT Auto_ID FROM dbo.tbl_DM_San_Pham WITH (XLOCK, HOLDLOCK) WHERE Auto_ID = @ProductId;",
                BigInt("@ProductId", fixture.ProductId));

            await using var saveConnection = OpenConnection($"C01-save-{fixture.Tag}");
            await saveConnection.OpenAsync();
            var saveSessionId = await IntScalarAsync(saveConnection, null, "SELECT @@SPID;");
            var saveOutcomeTask = CaptureAsync(() => SaveReceiptDetailAsync(
                fixture,
                receiptId,
                20,
                fixture.LoginB,
                connection: saveConnection));

            await WaitForSqlLockAsync(saveConnection, saveSessionId, saveOutcomeTask);

            await using var postConnection = OpenConnection($"C01-post-{fixture.Tag}");
            await postConnection.OpenAsync();
            var postOutcomeTask = CaptureAsync(() => PostReceiptAsync(
                fixture,
                receiptId,
                fixture.LoginA,
                connection: postConnection));

            /* Before the fix Post commits while Save_Detail is paused.  After
               the fix Save_Detail owns the parent lock, so Post must wait. */
            var postCompletedBeforeGateRelease = await WaitForCompletionAsync(
                postOutcomeTask,
                TimeSpan.FromMilliseconds(400));

            Assert.False(postCompletedBeforeGateRelease, "Post acquired the parent lock before Save Detail released it.");

            await gateTransaction.CommitAsync();
            var saveOutcome = await saveOutcomeTask;
            var postOutcome = await postOutcomeTask;

            Assert.True(
                saveOutcome.Succeeded || IsExpectedPostedRejection(saveOutcome.Error),
                FormatFailure("Save Detail", saveOutcome.Error));
            Assert.True(postOutcome.Succeeded, FormatFailure("Post", postOutcome.Error));

            var invariant = await ReadReceiptInvariantAsync(fixture);
            Assert.Equal(invariant.Ledger, invariant.Current);
            Assert.True(
                invariant.Ledger is 100m or 120m,
                $"Unexpected posted ledger quantity: {invariant.Ledger}.");

            if (postCompletedBeforeGateRelease)
            {
                /* The old implementation reaches this branch and then
                   produces ledger=120/current=100.  Keep the assertion on the
                   invariant, not on an implementation-specific ordering. */
                Assert.Equal(100m, invariant.Current);
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task Concurrent_post_holding_header_makes_save_detail_wait_and_reject_after_post()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await CreateAndPostReceiptAsync(fixture, new DateTime(2026, 1, 9), 100, "stock");
            var receiptId = await SaveReceiptHeaderAsync(fixture, new DateTime(2026, 1, 10));
            await SaveReceiptDetailAsync(fixture, receiptId, 20, fixture.LoginA);

            await using var gateConnection = OpenConnection($"C01-post-gate-{fixture.Tag}");
            await gateConnection.OpenAsync();
            await using var gateTransaction = (SqlTransaction)await gateConnection.BeginTransactionAsync();
            await ExecuteAsync(
                gateConnection,
                gateTransaction,
                "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WITH (XLOCK, HOLDLOCK) WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId),
                BigInt("@ProductId", fixture.ProductId));

            await using var postConnection = OpenConnection($"C01-post-held-{fixture.Tag}");
            await postConnection.OpenAsync();
            var postSessionId = await IntScalarAsync(postConnection, null, "SELECT @@SPID;");
            var postOutcomeTask = CaptureAsync(() => PostReceiptAsync(fixture, receiptId, fixture.LoginA, postConnection));
            await WaitForSqlLockAsync(postConnection, postSessionId, postOutcomeTask);

            await using var saveConnection = OpenConnection($"C01-save-after-post-{fixture.Tag}");
            await saveConnection.OpenAsync();
            var saveSessionId = await IntScalarAsync(saveConnection, null, "SELECT @@SPID;");
            var saveOutcomeTask = CaptureAsync(() => SaveReceiptDetailAsync(
                fixture,
                receiptId,
                5,
                fixture.LoginB,
                connection: saveConnection));
            await WaitForSqlLockAsync(saveConnection, saveSessionId, saveOutcomeTask);

            await gateTransaction.CommitAsync();
            var postOutcome = await postOutcomeTask;
            var saveOutcome = await saveOutcomeTask;

            Assert.True(postOutcome.Succeeded, FormatFailure("Post", postOutcome.Error));
            Assert.True(IsExpectedPostedRejection(saveOutcome.Error), FormatFailure("Save Detail", saveOutcome.Error));
            await AssertReceiptInvariantAsync(fixture, expectedLedger: 120, expectedCurrent: 120, expectedDetailCount: 2);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task Posting_multiple_details_for_one_scope_does_not_duplicate_invalidation_queue()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var receiptId = await SaveReceiptHeaderAsync(fixture, new DateTime(2026, 1, 10));
            await SaveReceiptDetailAsync(fixture, receiptId, 10, fixture.LoginA);
            await SaveReceiptDetailAsync(fixture, receiptId, 20, fixture.LoginA);
            await PostReceiptAsync(fixture, receiptId, fixture.LoginA);

            await AssertReceiptInvariantAsync(fixture, expectedLedger: 30, expectedCurrent: 30, expectedDetailCount: 2);
            Assert.Equal(1, await IntScalarAsync(
                fixture.ConnectionString,
                null,
                "SELECT COUNT(*) FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND LifecycleStatus IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'INITIALIZE_REQUIRED');",
                BigInt("@WarehouseId", fixture.WarehouseId),
                BigInt("@ProductId", fixture.ProductId)));
            Assert.Equal(1, await IntScalarAsync(
                fixture.ConnectionString,
                null,
                "SELECT COUNT(*) FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING');",
                BigInt("@WarehouseId", fixture.WarehouseId),
                BigInt("@ProductId", fixture.ProductId)));
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task Snapshot_rebuild_includes_movement_between_anchor_and_from_date()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await CreateAndPostReceiptAsync(fixture, new DateTime(2025, 12, 31), 100, "opening");
            await InsertSnapshotAsync(fixture, new DateTime(2026, 1, 1), 100, isValid: true);
            await InsertSnapshotAsync(fixture, new DateTime(2026, 1, 5), 0, isValid: false);

            await CreateAndPostReceiptAsync(fixture, new DateTime(2026, 1, 3), 20, "gap-03");
            await CreateAndPostReceiptAsync(fixture, new DateTime(2026, 1, 5), 30, "target-05");

            await AssertReceiptInvariantAsync(fixture, expectedLedger: 150, expectedCurrent: 150, expectedDetailCount: 3);
            await ExecuteStoredAsync(
                fixture.ConnectionString,
                null,
                "dbo.sp_Inventory_Snapshot_Rebuild",
                BigInt("@Kho_ID", fixture.WarehouseId),
                BigInt("@San_Pham_ID", fixture.ProductId),
                Date("@From_Date", new DateTime(2026, 1, 5)));

            var target = await DecimalScalarAsync(
                fixture.ConnectionString,
                null,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", new DateTime(2026, 1, 5)),
                BigInt("@WarehouseId", fixture.WarehouseId),
                BigInt("@ProductId", fixture.ProductId));
            var anchor = await DecimalScalarAsync(
                fixture.ConnectionString,
                null,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", new DateTime(2026, 1, 1)),
                BigInt("@WarehouseId", fixture.WarehouseId),
                BigInt("@ProductId", fixture.ProductId));

            Assert.Equal(150m, target);
            Assert.Equal(100m, anchor);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task Snapshot_scope_without_anchor_remains_initialize_required()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await CreateAndPostReceiptAsync(fixture, new DateTime(2026, 1, 3), 10, "initialize");

            var queue = await ReadSnapshotQueueAsync(fixture);
            Assert.Equal("INITIALIZE", queue.RequestType);
            Assert.Equal("INITIALIZE_REQUIRED", queue.LifecycleStatus);

            await ExecuteStoredAsync(
                fixture.ConnectionString,
                null,
                "dbo.sp_Inventory_Snapshot_Process_RebuildQueue",
                new SqlParameter("@Batch_Size", SqlDbType.Int) { Value = 10 },
                Text("@Worker_Name", $"C02-initialize-{fixture.Tag}", 128));

            var snapshotCount = await IntScalarAsync(
                fixture.ConnectionString,
                null,
                "SELECT COUNT(*) FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId),
                BigInt("@ProductId", fixture.ProductId));
            Assert.Equal(1, snapshotCount);
            var initializedClosing = await DecimalScalarAsync(
                fixture.ConnectionString,
                null,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId),
                BigInt("@ProductId", fixture.ProductId));
            Assert.Equal(10m, initializedClosing);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task Three_decimal_quantity_survives_inventory_projections_and_period_report()
    {
        var fixture = await CreateFixtureAsync();
        var movementDate = new DateTime(2026, 1, 10);
        try
        {
            // The rebuild contract requires a valid historical anchor for an
            // already-initialized scope; this test is about decimal preservation,
            // not about exercising the no-anchor bootstrap failure path.
            await InsertSnapshotAsync(fixture, new DateTime(2026, 1, 1), 0m, isValid: true);
            await CreateAndPostReceiptAsync(fixture, movementDate, 1.234m, "L02-precision");

            await ExecuteStoredAsync(
                fixture.ConnectionString,
                null,
                "dbo.sp_Inventory_Movement_Process_RebuildQueue",
                new SqlParameter("@Batch_Size", SqlDbType.Int) { Value = 100 });
            await ExecuteStoredAsync(
                fixture.ConnectionString,
                null,
                "dbo.sp_Inventory_Balance_Daily_Rebuild",
                BigInt("@Kho_ID", fixture.WarehouseId),
                BigInt("@San_Pham_ID", fixture.ProductId),
                Date("@From_Date", movementDate));

            await ExecuteAsync(
                fixture.ConnectionString,
                null,
                """
                UPDATE dbo.InventoryBalance_Snapshot_Daily
                SET ClosingQuantity = 0, IsValid = 0
                WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;
                IF @@ROWCOUNT = 0
                    INSERT dbo.InventoryBalance_Snapshot_Daily
                    (Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version])
                    VALUES (@Date, @WarehouseId, @ProductId, 0, 0, 1);
                """,
                Date("@Date", movementDate),
                BigInt("@WarehouseId", fixture.WarehouseId),
                BigInt("@ProductId", fixture.ProductId));
            await ExecuteStoredAsync(
                fixture.ConnectionString,
                null,
                "dbo.sp_Inventory_Snapshot_Rebuild",
                BigInt("@Kho_ID", fixture.WarehouseId),
                BigInt("@San_Pham_ID", fixture.ProductId),
                Date("@From_Date", movementDate));

            var projection = await ReadQuantityProjectionAsync(fixture, movementDate);
            Assert.Equal(1.234m, projection.Ledger);
            Assert.Equal(1.234m, projection.Current);
            Assert.Equal(1.234m, projection.MovementReceived);
            Assert.Equal(1.234m, projection.DailyReceived);
            Assert.Equal(1.234m, projection.DailyClosing);
            Assert.Equal(1.234m, projection.SnapshotClosing);

            var report = await ReadPeriodReportQuantityAsync(fixture, movementDate);
            Assert.Equal(1.234m, report.Received);
            Assert.Equal(1.234m, report.Closing);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task Snapshot_old_claim_cannot_complete_after_a_newer_invalidation()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var anchorDate = new DateTime(2025, 12, 31);
            var rebuildDate = new DateTime(2026, 1, 2);
            await CreateAndPostReceiptAsync(fixture, new DateTime(2026, 1, 1), 100m, "H04-version");
            await InsertSnapshotAsync(fixture, anchorDate, 0m, isValid: true);
            await InsertSnapshotAsync(fixture, rebuildDate, 0m, isValid: false);

            var queueId = await IntScalarAsync(
                fixture.ConnectionString,
                null,
                "SELECT TOP (1) ID FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId ORDER BY ID DESC;",
                BigInt("@WarehouseId", fixture.WarehouseId),
                BigInt("@ProductId", fixture.ProductId));

            await ExecuteAsync(
                fixture.ConnectionString,
                null,
                """
                UPDATE dbo.InventorySnapshot_RebuildQueue
                SET Status = N'PROCESSING', LifecycleStatus = N'PROCESSING', ClaimedBy = N'H04-test',
                    ClaimedAt = SYSUTCDATETIME(), LeaseUntil = DATEADD(MINUTE, 5, SYSUTCDATETIME())
                WHERE ID = @QueueId;
                IF COL_LENGTH(N'dbo.InventorySnapshot_RebuildQueue', N'Claimed_Version') IS NOT NULL
                    EXEC sys.sp_executesql N'UPDATE dbo.InventorySnapshot_RebuildQueue SET Claimed_Version = Requested_Version WHERE ID = @QueueId;', N'@QueueId BIGINT', @QueueId = @QueueId;
                """,
                BigInt("@QueueId", queueId));

            await ExecuteStoredAsync(
                fixture.ConnectionString,
                null,
                "dbo.sp_Inventory_Snapshot_Rebuild",
                BigInt("@Kho_ID", fixture.WarehouseId),
                BigInt("@San_Pham_ID", fixture.ProductId),
                Date("@From_Date", new DateTime(2026, 1, 1)));

            await ExecuteAsync(
                fixture.ConnectionString,
                null,
                """
                DECLARE @Affected dbo.InventorySnapshotAffectedType;
                INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
                VALUES (@WarehouseId, @ProductId, @FromDate, N'BACK_DATE_POST');
                EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
                """,
                BigInt("@WarehouseId", fixture.WarehouseId),
                BigInt("@ProductId", fixture.ProductId),
                Date("@FromDate", new DateTime(2026, 1, 1)));

            await ExecuteAsync(
                fixture.ConnectionString,
                null,
                """
                DECLARE @Affected dbo.InventorySnapshotAffectedType;
                INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
                VALUES (@WarehouseId, @ProductId, @FromDate, N'RECEIPT_EDIT');
                EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
                """,
                BigInt("@WarehouseId", fixture.WarehouseId),
                BigInt("@ProductId", fixture.ProductId),
                Date("@FromDate", new DateTime(2026, 1, 1)));

            await ExecuteAsync(
                fixture.ConnectionString,
                null,
                """
                IF OBJECT_ID(N'dbo.sp_Inventory_Snapshot_Complete_Claim', N'P') IS NULL
                BEGIN
                    UPDATE dbo.InventorySnapshot_RebuildQueue
                    SET Status = N'COMPLETED', LifecycleStatus = N'COMPLETED', CompletedAt = SYSUTCDATETIME(),
                        LeaseUntil = NULL, ClaimedBy = NULL, ClaimedAt = NULL
                    WHERE ID = @QueueId;
                END
                ELSE
                BEGIN
                    DECLARE @ClaimedVersion INT;
                    EXEC sys.sp_executesql
                        N'SELECT @ClaimedVersion = Claimed_Version FROM dbo.InventorySnapshot_RebuildQueue WHERE ID = @QueueId;',
                        N'@QueueId BIGINT, @ClaimedVersion INT OUTPUT',
                        @QueueId = @QueueId,
                        @ClaimedVersion = @ClaimedVersion OUTPUT;
                    EXEC dbo.sp_Inventory_Snapshot_Complete_Claim @Queue_ID = @QueueId, @Claimed_Version = @ClaimedVersion;
                END
                """,
                BigInt("@QueueId", queueId));

            Assert.Equal(
                0,
                await IntScalarAsync(
                    fixture.ConnectionString,
                    null,
                    "SELECT CASE WHEN LifecycleStatus = N'COMPLETED' THEN 1 ELSE 0 END FROM dbo.InventorySnapshot_RebuildQueue WHERE ID = @QueueId;",
                    BigInt("@QueueId", queueId)));
            Assert.Equal(
                1,
                await IntScalarAsync(
                    fixture.ConnectionString,
                    null,
                    "SELECT COUNT(*) FROM dbo.InventorySnapshot_RebuildQueue WHERE ID = @QueueId AND LifecycleStatus IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'INITIALIZE_REQUIRED');",
                    BigInt("@QueueId", queueId)));
            Assert.Equal(
                1,
                await IntScalarAsync(
                    fixture.ConnectionString,
                    null,
                    "SELECT CASE WHEN COL_LENGTH(N'dbo.InventorySnapshot_RebuildQueue', N'Requested_Version') IS NOT NULL AND COL_LENGTH(N'dbo.InventorySnapshot_RebuildQueue', N'Claimed_Version') IS NOT NULL THEN 1 ELSE 0 END;"));
            Assert.Equal(
                1,
                await IntScalarAsync(
                    fixture.ConnectionString,
                    null,
                    "SELECT CASE WHEN Requested_Version = Claimed_Version + 2 THEN 1 ELSE 0 END FROM dbo.InventorySnapshot_RebuildQueue WHERE ID = @QueueId;",
                    BigInt("@QueueId", queueId)));

            await ExecuteStoredAsync(
                fixture.ConnectionString,
                null,
                "dbo.sp_Inventory_Snapshot_Process_RebuildQueue",
                new SqlParameter("@Batch_Size", SqlDbType.Int) { Value = 1 },
                Text("@Worker_Name", $"H04-latest-{fixture.Tag}", 128),
                BigInt("@Kho_ID", fixture.WarehouseId),
                BigInt("@San_Pham_ID", fixture.ProductId));

            Assert.Equal(
                1,
                await IntScalarAsync(
                    fixture.ConnectionString,
                    null,
                    "SELECT CASE WHEN LifecycleStatus = N'COMPLETED' AND Requested_Version = Claimed_Version THEN 1 ELSE 0 END FROM dbo.InventorySnapshot_RebuildQueue WHERE ID = @QueueId;",
                    BigInt("@QueueId", queueId)));
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task Snapshot_waiting_invalidation_coalesces_and_advances_version()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var invalidationDate = new DateTime(2026, 2, 10);
            await CreateAndPostReceiptAsync(fixture, invalidationDate, 10m, "H04-waiting");

            await ExecuteAsync(
                fixture.ConnectionString,
                null,
                """
                DECLARE @Affected dbo.InventorySnapshotAffectedType;
                INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
                VALUES (@WarehouseId, @ProductId, @FromDate, N'BACK_DATE_POST');
                EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
                """,
                BigInt("@WarehouseId", fixture.WarehouseId),
                BigInt("@ProductId", fixture.ProductId),
                Date("@FromDate", invalidationDate));

            Assert.Equal(
                1,
                await IntScalarAsync(
                    fixture.ConnectionString,
                    null,
                    "SELECT COUNT(*) FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND LifecycleStatus = N'INITIALIZE_REQUIRED';",
                    BigInt("@WarehouseId", fixture.WarehouseId),
                    BigInt("@ProductId", fixture.ProductId)));
            Assert.Equal(
                1,
                await IntScalarAsync(
                    fixture.ConnectionString,
                    null,
                    "SELECT CASE WHEN Requested_Version = 2 AND Claimed_Version IS NULL THEN 1 ELSE 0 END FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                    BigInt("@WarehouseId", fixture.WarehouseId),
                    BigInt("@ProductId", fixture.ProductId)));
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var tag = $"C01C02-{Guid.NewGuid():N}";
        var loginA = $"{tag}-a";
        var loginB = $"{tag}-b";

        await using var connection = OpenConnection($"C01C02-setup-{tag}");
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var unitId = await InsertIdAsync(
                connection,
                transaction,
                "INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-unit", 200));
            var categoryId = await InsertIdAsync(
                connection,
                transaction,
                "INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP, Ten_LSP, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{tag}-category-code", 100),
                Text("@Name", $"{tag}-category", 200));
            var productId = await InsertIdAsync(
                connection,
                transaction,
                "INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, @CategoryId, @UnitId, N'');",
                Text("@Code", $"{tag}-product-code", 100),
                Text("@Name", $"{tag}-product", 255),
                BigInt("@CategoryId", categoryId),
                BigInt("@UnitId", unitId));
            var supplierId = await InsertIdAsync(
                connection,
                transaction,
                "INSERT dbo.tbl_DM_NCC(Ma_NCC, Ten_NCC, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{tag}-supplier-code", 100),
                Text("@Name", $"{tag}-supplier", 255));
            var warehouseId = await InsertIdAsync(
                connection,
                transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-warehouse", 255));

            await InsertMemberAsync(connection, transaction, loginA, $"{tag}-member-a");
            await InsertMemberAsync(connection, transaction, loginB, $"{tag}-member-b");
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId), (@LoginB, @WarehouseId);",
                Text("@Login", loginA, 100),
                Text("@LoginB", loginB, 100),
                BigInt("@WarehouseId", warehouseId));

            await transaction.CommitAsync();
            return new Fixture(
                tag,
                loginA,
                loginB,
                unitId,
                categoryId,
                productId,
                supplierId,
                warehouseId,
                new SqlConnectionStringBuilder(BaseConnectionString).ConnectionString);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<long> SaveReceiptHeaderAsync(Fixture fixture, DateTime date)
    {
        await using var connection = OpenConnection($"C01C02-header-{fixture.Tag}");
        await connection.OpenAsync();
        return await ExecuteStoredWithOutputAsync(
            connection,
            null,
            "dbo.sp_XNK_Nhap_Kho_Save_Header",
            Text("@So_Phieu_Nhap_Kho", $"{fixture.Tag}-{Guid.NewGuid():N}", 100),
            BigInt("@Kho_ID", fixture.WarehouseId),
            BigInt("@NCC_ID", fixture.SupplierId),
            Date("@Ngay_Nhap_Kho", date),
            Text("@Ghi_Chu", "", 1000),
            Text("@Ma_Dang_Nhap", fixture.LoginA, 100));
    }

    private static async Task<long> SaveReceiptDetailAsync(
        Fixture fixture,
        long receiptId,
        decimal quantity,
        string login,
        long detailId = 0,
        SqlConnection? connection = null,
        SqlTransaction? transaction = null)
    {
        var ownsConnection = connection is null;
        connection ??= OpenConnection($"C01C02-detail-{fixture.Tag}");
        if (ownsConnection)
            await connection.OpenAsync();

        try
        {
            return await ExecuteStoredWithOutputAsync(
                connection,
                transaction,
                "dbo.sp_XNK_Nhap_Kho_Save_Detail",
                BigInt("@Nhap_Kho_ID", receiptId),
                BigInt("@San_Pham_ID", fixture.ProductId),
                Decimal("@SL_Nhap", quantity),
                Decimal("@Don_Gia_Nhap", 1),
                Text("@Ma_Dang_Nhap", login, 100),
                BigInt("@Auto_ID", detailId));
        }
        finally
        {
            if (ownsConnection)
                await connection.DisposeAsync();
        }
    }

    private static async Task DeleteReceiptDetailAsync(Fixture fixture, long detailId, string login)
    {
        await ExecuteStoredAsync(
            fixture.ConnectionString,
            null,
            "dbo.sp_XNK_Nhap_Kho_Delete_Detail",
            BigInt("@Auto_ID", detailId),
            Text("@Ma_Dang_Nhap", login, 100));
    }

    private static Task PostReceiptAsync(
        Fixture fixture,
        long receiptId,
        string login,
        SqlConnection? connection = null) =>
        connection is null
            ? ExecuteStoredAsync(
                fixture.ConnectionString,
                null,
                "dbo.sp_XNK_Document_Post",
                new SqlParameter("@Is_Receipt", SqlDbType.Bit) { Value = true },
                BigInt("@Document_ID", receiptId),
                Text("@Ma_Dang_Nhap", login, 100))
            : ExecuteStoredAsync(
                connection,
                null,
                "dbo.sp_XNK_Document_Post",
                new SqlParameter("@Is_Receipt", SqlDbType.Bit) { Value = true },
                BigInt("@Document_ID", receiptId),
                Text("@Ma_Dang_Nhap", login, 100));

    private static async Task CreateAndPostReceiptAsync(Fixture fixture, DateTime date, decimal quantity, string suffix)
    {
        var receiptId = await SaveReceiptHeaderAsync(fixture, date);
        await SaveReceiptDetailAsync(fixture, receiptId, quantity, fixture.LoginA);
        await PostReceiptAsync(fixture, receiptId, fixture.LoginA);
    }

    private static async Task InsertReceiptDetailDirectlyAsync(Fixture fixture, long receiptId, decimal quantity)
    {
        await EnsureDirectDmlProbeUserAsync();
        await using var connection = OpenConnection($"C01C02-dml-{fixture.Tag}");
        await connection.OpenAsync();
        var impersonated = false;
        try
        {
            await ExecuteAsync(
                connection,
                null,
                $"EXECUTE AS USER = N'{DirectDmlProbeUser}'; EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            impersonated = true;
            await ExecuteAsync(
                connection,
                null,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@ReceiptId, @ProductId, @Quantity, 1);",
                BigInt("@ReceiptId", receiptId),
                BigInt("@ProductId", fixture.ProductId),
                Decimal("@Quantity", quantity));
        }
        finally
        {
            if (impersonated)
                await ExecuteAsync(connection, null, "REVERT;");

            await DropDirectDmlProbeUserAsync();
        }
    }

    private static async Task EnsureDirectDmlProbeUserAsync()
    {
        await using var connection = OpenConnection("C01C02-probe-admin");
        await connection.OpenAsync();
        await ExecuteAsync(
            connection,
            null,
            $"""
            SET QUOTED_IDENTIFIER ON;
            IF DATABASE_PRINCIPAL_ID(N'{DirectDmlProbeUser}') IS NULL
                CREATE USER [{DirectDmlProbeUser}] WITHOUT LOGIN;
            GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.tbl_XNK_Nhap_Kho_Raw_Data TO [{DirectDmlProbeUser}];
            GRANT SELECT ON OBJECT::dbo.tbl_XNK_Nhap_Kho TO [{DirectDmlProbeUser}];
            """);
    }

    private static async Task DropDirectDmlProbeUserAsync()
    {
        await using var connection = OpenConnection("C01C02-probe-cleanup");
        await connection.OpenAsync();
        await ExecuteAsync(connection, null, $"IF DATABASE_PRINCIPAL_ID(N'{DirectDmlProbeUser}') IS NOT NULL DROP USER [{DirectDmlProbeUser}];");
    }

    private static Task InsertSnapshotAsync(Fixture fixture, DateTime date, decimal closing, bool isValid) =>
        ExecuteAsync(
            fixture.ConnectionString,
            null,
            "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, @Closing, @IsValid, 1);",
            Date("@Date", date),
            BigInt("@WarehouseId", fixture.WarehouseId),
            BigInt("@ProductId", fixture.ProductId),
            Decimal("@Closing", closing),
            new SqlParameter("@IsValid", SqlDbType.Bit) { Value = isValid });

    private static async Task AssertReceiptInvariantAsync(Fixture fixture, decimal expectedLedger, decimal expectedCurrent, int expectedDetailCount)
    {
        var invariant = await ReadReceiptInvariantAsync(fixture);
        Assert.Equal(expectedLedger, invariant.Ledger);
        Assert.Equal(expectedCurrent, invariant.Current);
        Assert.Equal(expectedDetailCount, invariant.DetailCount);
    }

    private static async Task<ReceiptInvariant> ReadReceiptInvariantAsync(Fixture fixture)
    {
        await using var connection = OpenConnection($"C01C02-read-{fixture.Tag}");
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            SELECT
                COALESCE((SELECT SUM(d.SL_Nhap)
                          FROM dbo.tbl_XNK_Nhap_Kho h
                          JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
                          WHERE h.Kho_ID = @WarehouseId AND d.San_Pham_ID = @ProductId AND h.Is_Posted = 1), 0),
                COALESCE((SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId), 0),
                (SELECT COUNT(*)
                 FROM dbo.tbl_XNK_Nhap_Kho h
                 JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
                 WHERE h.So_Phieu_Nhap_Kho LIKE @TagPrefix AND d.San_Pham_ID = @ProductId)
            """,
            connection);
        command.Parameters.Add(BigInt("@WarehouseId", fixture.WarehouseId));
        command.Parameters.Add(BigInt("@ProductId", fixture.ProductId));
        command.Parameters.Add(Text("@TagPrefix", $"{fixture.Tag}%", 100));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new ReceiptInvariant(reader.GetDecimal(0), reader.GetDecimal(1), reader.GetInt32(2));
    }

    private static async Task<SnapshotQueueRow> ReadSnapshotQueueAsync(Fixture fixture)
    {
        await using var connection = OpenConnection($"C02-queue-read-{fixture.Tag}");
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT TOP (1) RequestType, LifecycleStatus FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId ORDER BY ID DESC;",
            connection);
        command.Parameters.Add(BigInt("@WarehouseId", fixture.WarehouseId));
        command.Parameters.Add(BigInt("@ProductId", fixture.ProductId));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new SnapshotQueueRow(reader.GetString(0), reader.GetString(1));
    }

    private static async Task<QuantityProjection> ReadQuantityProjectionAsync(Fixture fixture, DateTime date)
    {
        await using var connection = OpenConnection($"L02-projection-read-{fixture.Tag}");
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            SELECT
                COALESCE((SELECT SUM(d.SL_Nhap)
                          FROM dbo.tbl_XNK_Nhap_Kho h
                          JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
                          WHERE h.Kho_ID = @WarehouseId AND d.San_Pham_ID = @ProductId AND h.Is_Posted = 1), 0),
                COALESCE((SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId), 0),
                COALESCE((SELECT Total_Receipt FROM dbo.Inventory_Movement_Daily WHERE Movement_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND IsValid = 1), 0),
                COALESCE((SELECT TotalReceived FROM dbo.Inventory_Balance_Daily WHERE Balance_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND IsValid = 1), 0),
                COALESCE((SELECT ClosingQuantity FROM dbo.Inventory_Balance_Daily WHERE Balance_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND IsValid = 1), 0),
                COALESCE((SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND IsValid = 1), 0)
            """,
            connection);
        command.Parameters.Add(BigInt("@WarehouseId", fixture.WarehouseId));
        command.Parameters.Add(BigInt("@ProductId", fixture.ProductId));
        command.Parameters.Add(Date("@Date", date));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new QuantityProjection(
            reader.GetDecimal(0),
            reader.GetDecimal(1),
            reader.GetDecimal(2),
            reader.GetDecimal(3),
            reader.GetDecimal(4),
            reader.GetDecimal(5));
    }

    private static async Task<PeriodReportQuantity> ReadPeriodReportQuantityAsync(Fixture fixture, DateTime date)
    {
        await using var connection = OpenConnection($"L02-report-read-{fixture.Tag}");
        await connection.OpenAsync();
        await using var command = new SqlCommand("dbo.sp_BC_Xuat_Nhap_Ton_Page", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add(Date("@Tu_Ngay", date));
        command.Parameters.Add(Date("@Den_Ngay", date));
        command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
        command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 10 });
        command.Parameters.Add(Text("@Ma_Dang_Nhap", fixture.LoginA, 100));
        command.Parameters.Add(BigInt("@Kho_ID", fixture.WarehouseId));

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        return new PeriodReportQuantity(
            reader.GetDecimal(reader.GetOrdinal("SL_Nhap")),
            reader.GetDecimal(reader.GetOrdinal("SL_Cuoi_Ky")));
    }

    private static async Task WaitForSqlLockAsync(SqlConnection saveConnection, int sessionId, Task<OperationOutcome> operation)
    {
        await using var monitor = OpenConnection("C01-monitor");
        await monitor.OpenAsync();
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (operation.IsCompleted)
                throw new Xunit.Sdk.XunitException("Save Detail completed before the controlled TOCTOU lock was reached.");

            await using var command = new SqlCommand(
                "SELECT TOP (1) wait_type, blocking_session_id FROM sys.dm_exec_requests WHERE session_id = @SessionId;",
                monitor)
            {
                CommandTimeout = 1
            };
            command.Parameters.Add(new SqlParameter("@SessionId", SqlDbType.Int) { Value = sessionId });
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var waitType = reader.IsDBNull(0) ? null : reader.GetString(0);
                var blockingSessionId = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
                if (blockingSessionId > 0 && waitType?.StartsWith("LCK_", StringComparison.OrdinalIgnoreCase) == true)
                    return;
            }

            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("Save Detail did not reach the controlled lock wait within five seconds.");
    }

    private static async Task<bool> WaitForCompletionAsync(Task<OperationOutcome> operation, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(operation, Task.Delay(timeout));
        return completed == operation;
    }

    private static async Task<OperationOutcome> CaptureAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return new OperationOutcome(true, null);
        }
        catch (Exception exception)
        {
            return new OperationOutcome(false, exception);
        }
    }

    private static bool IsExpectedPostedRejection(Exception? exception) =>
        exception is SqlException sqlException && sqlException.Number == 51163;

    private static string FormatFailure(string operation, Exception? exception) =>
        exception is null ? $"{operation} did not complete." : $"{operation} failed: {exception.Message}";

    private static async Task AssertSqlNumberAsync(int expectedNumber, Func<Task> operation)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(operation);
        Assert.Equal(expectedNumber, exception.Number);
    }

    private static async Task InsertMemberAsync(SqlConnection connection, SqlTransaction transaction, string login, string name)
    {
        await ExecuteAsync(
            connection,
            transaction,
            "DECLARE @MemberId BIGINT; SELECT @MemberId = ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX); INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, @Name, 0);",
            Text("@Login", login, 100),
            Text("@Name", name, 200));
    }

    private static async Task CleanupFixtureAsync(Fixture fixture)
    {
        await using var connection = OpenConnection($"C01C02-cleanup-{fixture.Tag}");
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await ExecuteAsync(connection, transaction, "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            await ExecuteAsync(connection, transaction, "DELETE d FROM dbo.InventorySnapshot_RebuildDeadLetter d JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId;", BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.InventoryReservation_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction, "DELETE d FROM dbo.InventoryMovement_RebuildDeadLetter d JOIN dbo.InventoryMovement_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId;", BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho LIKE @TagPrefix;", Text("@TagPrefix", $"{fixture.Tag}%", 100));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap IN (@LoginA, @LoginB);", Text("@LoginA", fixture.LoginA, 100), Text("@LoginB", fixture.LoginB, 100));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap IN (@LoginA, @LoginB);", Text("@LoginA", fixture.LoginA, 100), Text("@LoginB", fixture.LoginB, 100));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId; DELETE FROM dbo.tbl_DM_NCC WHERE Auto_ID = @SupplierId; DELETE FROM dbo.tbl_DM_San_Pham WHERE Auto_ID = @ProductId; DELETE FROM dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID = @CategoryId; DELETE FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID = @UnitId;", BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@SupplierId", fixture.SupplierId), BigInt("@ProductId", fixture.ProductId), BigInt("@CategoryId", fixture.CategoryId), BigInt("@UnitId", fixture.UnitId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static SqlConnection OpenConnection(string applicationName)
    {
        var builder = new SqlConnectionStringBuilder(BaseConnectionString)
        {
            ApplicationName = applicationName
        };
        return new SqlConnection(builder.ConnectionString);
    }

    private static async Task<long> InsertIdAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> ExecuteStoredWithOutputAsync(SqlConnection connection, SqlTransaction? transaction, string procedure, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedure, connection, transaction) { CommandType = CommandType.StoredProcedure };
        var output = parameters.SingleOrDefault(parameter => parameter.ParameterName == "@Auto_ID");
        output ??= BigInt("@Auto_ID", 0);
        output.Direction = ParameterDirection.InputOutput;
        command.Parameters.Add(output);
        command.Parameters.AddRange(parameters.Where(parameter => parameter != output).ToArray());
        await command.ExecuteNonQueryAsync();
        return Convert.ToInt64(output.Value);
    }

    private static async Task ExecuteStoredAsync(string connectionString, SqlTransaction? transaction, string procedure, params SqlParameter[] parameters)
    {
        await using var connection = transaction is null ? OpenConnection($"C01C02-sql-{procedure}") : null;
        var activeConnection = transaction is null ? connection! : transaction.Connection!;
        if (transaction is null)
            await activeConnection.OpenAsync();
        await using var command = new SqlCommand(procedure, activeConnection, transaction) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
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

    private static async Task ExecuteAsync(string connectionString, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var connection = transaction is null ? OpenConnection("C01C02-command") : null;
        var activeConnection = transaction is null ? connection! : transaction.Connection!;
        if (transaction is null)
            await activeConnection.OpenAsync();
        await ExecuteAsync(activeConnection, transaction, sql, parameters);
    }

    private static async Task<int> IntScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) =>
        Convert.ToInt32(await ScalarAsync(connection, transaction, sql, parameters));

    private static async Task<int> IntScalarAsync(string connectionString, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var connection = transaction is null ? OpenConnection("C01C02-int") : null;
        var activeConnection = transaction is null ? connection! : transaction.Connection!;
        if (transaction is null)
            await activeConnection.OpenAsync();
        return await IntScalarAsync(activeConnection, transaction, sql, parameters);
    }

    private static async Task<decimal> DecimalScalarAsync(string connectionString, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var connection = transaction is null ? OpenConnection("C01C02-decimal") : null;
        var activeConnection = transaction is null ? connection! : transaction.Connection!;
        if (transaction is null)
            await activeConnection.OpenAsync();
        return Convert.ToDecimal(await ScalarAsync(activeConnection, transaction, sql, parameters));
    }

    private static async Task<object?> ScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    private static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt) { Value = value };

    private static SqlParameter Decimal(string name, decimal value) => new(name, SqlDbType.Decimal)
    {
        Precision = 18,
        Scale = 3,
        Value = value
    };

    private static SqlParameter Date(string name, DateTime value) => new(name, SqlDbType.Date) { Value = value.Date };

    private static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.NVarChar, size) { Value = value };

    private sealed record Fixture(
        string Tag,
        string LoginA,
        string LoginB,
        long UnitId,
        long CategoryId,
        long ProductId,
        long SupplierId,
        long WarehouseId,
        string ConnectionString);

    private sealed record ReceiptInvariant(decimal Ledger, decimal Current, int DetailCount);

    private sealed record SnapshotQueueRow(string RequestType, string LifecycleStatus);

    private sealed record QuantityProjection(
        decimal Ledger,
        decimal Current,
        decimal MovementReceived,
        decimal DailyReceived,
        decimal DailyClosing,
        decimal SnapshotClosing);

    private sealed record PeriodReportQuantity(decimal Received, decimal Closing);

    private sealed record OperationOutcome(bool Succeeded, Exception? Error);
}
