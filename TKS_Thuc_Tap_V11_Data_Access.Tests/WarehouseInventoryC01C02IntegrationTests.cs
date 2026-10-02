using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehouseInventoryC01C02IntegrationTests
{
    private const string DirectDmlProbeUser = "Phase10C01DmlProbe";

    private static string BaseConnectionString
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
    public async Task Receipt_save_detail_without_ambient_transaction_owns_and_closes_transaction()
    {
        var v_Fixture = await CreateFixtureAsync();
        try
        {
            var receiptId = await SaveReceiptHeaderAsync(v_Fixture, new DateTime(2026, 1, 10));
            await using var v_Connection = OpenConnection($"C01-standalone-{v_Fixture.Tag}");
            await v_Connection.OpenAsync();

            await SaveReceiptDetailAsync(v_Fixture, receiptId, 10, v_Fixture.LoginA, p_Connection: v_Connection);

            Assert.Equal(0, await IntScalarAsync(v_Connection, null, "SELECT @@TRANCOUNT;"));
            await AssertReceiptInvariantAsync(v_Fixture, p_ExpectedLedger: 0, p_ExpectedCurrent: 0, p_iExpectedDetailCount: 1);
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task Receipt_save_detail_inside_ambient_transaction_preserves_caller_ownership_and_rolls_back()
    {
        var v_Fixture = await CreateFixtureAsync();
        try
        {
            var receiptId = await SaveReceiptHeaderAsync(v_Fixture, new DateTime(2026, 1, 10));
            await using var v_Connection = OpenConnection($"C01-ambient-{v_Fixture.Tag}");
            await v_Connection.OpenAsync();
            await ExecuteAsync(v_Connection, null, "CREATE TABLE #AmbientMarker(Value INT NOT NULL);");
            await using var v_Transaction = (SqlTransaction)await v_Connection.BeginTransactionAsync();

            await ExecuteAsync(v_Connection, v_Transaction, "INSERT #AmbientMarker(Value) VALUES (1);");
            await SaveReceiptDetailAsync(
                v_Fixture,
                receiptId,
                10,
                v_Fixture.LoginA,
                p_Connection: v_Connection,
                p_Transaction: v_Transaction);

            Assert.Equal(1, await IntScalarAsync(v_Connection, v_Transaction, "SELECT @@TRANCOUNT;"));
            Assert.Equal(1, await IntScalarAsync(v_Connection, v_Transaction, "SELECT COUNT(*) FROM #AmbientMarker;"));
            Assert.Equal(1, await IntScalarAsync(v_Connection, v_Transaction, "SELECT COUNT(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @ReceiptId;", BigInt("@ReceiptId", receiptId)));

            await v_Transaction.RollbackAsync();

            Assert.Equal(0, await IntScalarAsync(v_Connection, null, "SELECT @@TRANCOUNT;"));
            Assert.Equal(0, await IntScalarAsync(v_Connection, null, "SELECT COUNT(*) FROM #AmbientMarker;"));
            Assert.Equal(0, await IntScalarAsync(v_Connection, null, "SELECT COUNT(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @ReceiptId;", BigInt("@ReceiptId", receiptId)));
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task Receipt_save_detail_error_inside_ambient_transaction_does_not_commit_caller_work()
    {
        var v_Fixture = await CreateFixtureAsync();
        try
        {
            var receiptId = await SaveReceiptHeaderAsync(v_Fixture, new DateTime(2026, 1, 10));
            await using var v_Connection = OpenConnection($"C01-ambient-error-{v_Fixture.Tag}");
            await v_Connection.OpenAsync();
            await ExecuteAsync(v_Connection, null, "CREATE TABLE #AmbientMarker(Value INT NOT NULL);");
            await using var v_Transaction = (SqlTransaction)await v_Connection.BeginTransactionAsync();
            await ExecuteAsync(v_Connection, v_Transaction, "INSERT #AmbientMarker(Value) VALUES (1);");

            await AssertSqlNumberAsync(
                51107,
                () => SaveReceiptDetailAsync(
                    v_Fixture,
                    receiptId,
                    0,
                    v_Fixture.LoginA,
                    p_Connection: v_Connection,
                    p_Transaction: v_Transaction));

            Assert.Equal(1, await IntScalarAsync(v_Connection, v_Transaction, "SELECT @@TRANCOUNT;"));
            var v_iTransactionState = await IntScalarAsync(v_Connection, v_Transaction, "SELECT XACT_STATE();");
            Assert.Contains(v_iTransactionState, new[] { 1, -1 });
            await v_Transaction.RollbackAsync();
            Assert.Equal(0, await IntScalarAsync(v_Connection, null, "SELECT COUNT(*) FROM #AmbientMarker;"));
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task Receipt_draft_post_and_posted_mutations_preserve_inventory_invariants()
    {
        var v_Fixture = await CreateFixtureAsync();
        try
        {
            var receiptId = await SaveReceiptHeaderAsync(v_Fixture, new DateTime(2026, 1, 10));
            var detailId = await SaveReceiptDetailAsync(v_Fixture, receiptId, 10, v_Fixture.LoginA);

            await AssertReceiptInvariantAsync(v_Fixture, p_ExpectedLedger: 0, p_ExpectedCurrent: 0, p_iExpectedDetailCount: 1);

            await SaveReceiptDetailAsync(v_Fixture, receiptId, 12, v_Fixture.LoginA, detailId);
            await DeleteReceiptDetailAsync(v_Fixture, detailId, v_Fixture.LoginA);
            await AssertReceiptInvariantAsync(v_Fixture, p_ExpectedLedger: 0, p_ExpectedCurrent: 0, p_iExpectedDetailCount: 0);

            detailId = await SaveReceiptDetailAsync(v_Fixture, receiptId, 12, v_Fixture.LoginA);
            await PostReceiptAsync(v_Fixture, receiptId, v_Fixture.LoginA);
            await AssertReceiptInvariantAsync(v_Fixture, p_ExpectedLedger: 12, p_ExpectedCurrent: 12, p_iExpectedDetailCount: 1);

            await AssertSqlNumberAsync(51162, () => PostReceiptAsync(v_Fixture, receiptId, v_Fixture.LoginA));
            await AssertSqlNumberAsync(
                51163,
                () => SaveReceiptDetailAsync(v_Fixture, receiptId, 13, v_Fixture.LoginA, detailId));
            await AssertSqlNumberAsync(51163, () => DeleteReceiptDetailAsync(v_Fixture, detailId, v_Fixture.LoginA));

            await AssertSqlNumberAsync(
                51228,
                () => InsertReceiptDetailDirectlyAsync(v_Fixture, receiptId, 5));
            await AssertReceiptInvariantAsync(v_Fixture, p_ExpectedLedger: 12, p_ExpectedCurrent: 12, p_iExpectedDetailCount: 1);
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task Concurrent_save_detail_and_post_never_leaves_ledger_ahead_of_current()
    {
        var v_Fixture = await CreateFixtureAsync();
        try
        {
            var receiptId = await SaveReceiptHeaderAsync(v_Fixture, new DateTime(2026, 1, 10));
            await SaveReceiptDetailAsync(v_Fixture, receiptId, 100, v_Fixture.LoginA);

            await using var v_GateConnection = OpenConnection($"C01-gate-{v_Fixture.Tag}");
            await v_GateConnection.OpenAsync();
            await using var v_GateTransaction = (SqlTransaction)await v_GateConnection.BeginTransactionAsync();

            /* This X lock stops Save_Detail after its Is_Posted check and
               before its INSERT.  The old procedure has no parent lock, so
               Post can commit while Save_Detail is paused. */
            await ExecuteAsync(
                v_GateConnection,
                v_GateTransaction,
                "SELECT Auto_ID FROM dbo.tbl_DM_San_Pham WITH (XLOCK, HOLDLOCK) WHERE Auto_ID = @ProductId;",
                BigInt("@ProductId", v_Fixture.ProductId));

            await using var v_SaveConnection = OpenConnection($"C01-save-{v_Fixture.Tag}");
            await v_SaveConnection.OpenAsync();
            var v_iSaveSessionId = await IntScalarAsync(v_SaveConnection, null, "SELECT @@SPID;");
            var v_SaveOutcomeTask = CaptureAsync(() => SaveReceiptDetailAsync(
                v_Fixture,
                receiptId,
                20,
                v_Fixture.LoginB,
                p_Connection: v_SaveConnection));

            await WaitForSqlLockAsync(v_SaveConnection, v_iSaveSessionId, v_SaveOutcomeTask);

            await using var v_PostConnection = OpenConnection($"C01-post-{v_Fixture.Tag}");
            await v_PostConnection.OpenAsync();
            var v_PostOutcomeTask = CaptureAsync(() => PostReceiptAsync(
                v_Fixture,
                receiptId,
                v_Fixture.LoginA,
                p_Connection: v_PostConnection));

            /* Save_Detail has already acquired Root -> Group and is waiting
               on the controlled product-row lock.  Post must therefore fail
               closed at the conflicting Group fence; completion here does
               not mean that the parent row lock was acquired. */
            var v_bPostCompletedBeforeGateRelease = await WaitForCompletionAsync(
                v_PostOutcomeTask,
                TimeSpan.FromSeconds(2));

            if (!v_bPostCompletedBeforeGateRelease)
            {
                await v_GateTransaction.RollbackAsync();
                var v_DelayedPostOutcome = await v_PostOutcomeTask;
                throw new Xunit.Sdk.XunitException(
                    $"Post did not fail fast on the Group fence before the gate was released. {FormatFailure("Post", v_DelayedPostOutcome.Error)}");
            }

            var v_PostOutcome = await v_PostOutcomeTask;
            Assert.True(
                v_PostOutcome.Error is SqlException v_SqlException && v_SqlException.Number == 51407,
                $"Post should fail closed with the Group fence conflict, but {FormatFailure("Post", v_PostOutcome.Error)}.");

            await v_GateTransaction.CommitAsync();
            var v_SaveOutcome = await v_SaveOutcomeTask;

            Assert.True(
                v_SaveOutcome.Succeeded,
                FormatFailure("Save Detail", v_SaveOutcome.Error));

            Assert.Equal(
                0,
                await IntScalarAsync(
                    v_Fixture.ConnectionString,
                    null,
                    "SELECT CONVERT(INT, Is_Posted) FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @ReceiptId;",
                    BigInt("@ReceiptId", receiptId)));

            var v_BeforeRetry = await ReadReceiptInvariantAsync(v_Fixture);
            Assert.Equal(0m, v_BeforeRetry.Ledger);
            Assert.Equal(0m, v_BeforeRetry.Current);

            /* Once the conflicting fence is released, the same valid Post
               operation may be retried and must publish atomically. */
            var v_RetryOutcome = await CaptureAsync(() => PostReceiptAsync(
                v_Fixture,
                receiptId,
                v_Fixture.LoginA,
                p_Connection: v_PostConnection));
            Assert.True(v_RetryOutcome.Succeeded, FormatFailure("Post retry", v_RetryOutcome.Error));

            var v_Invariant = await ReadReceiptInvariantAsync(v_Fixture);
            Assert.Equal(v_Invariant.Ledger, v_Invariant.Current);
            Assert.Equal(120m, v_Invariant.Ledger);
            Assert.Equal(2, v_Invariant.DetailCount);
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task Concurrent_post_holding_header_makes_save_detail_wait_and_reject_after_post()
    {
        var v_Fixture = await CreateFixtureAsync();
        try
        {
            await CreateAndPostReceiptAsync(v_Fixture, new DateTime(2026, 1, 9), 100, "stock");
            var receiptId = await SaveReceiptHeaderAsync(v_Fixture, new DateTime(2026, 1, 10));
            await SaveReceiptDetailAsync(v_Fixture, receiptId, 20, v_Fixture.LoginA);

            await using var v_GateConnection = OpenConnection($"C01-post-gate-{v_Fixture.Tag}");
            await v_GateConnection.OpenAsync();
            await using var v_GateTransaction = (SqlTransaction)await v_GateConnection.BeginTransactionAsync();
            await ExecuteAsync(
                v_GateConnection,
                v_GateTransaction,
                "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WITH (XLOCK, HOLDLOCK) WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", v_Fixture.WarehouseId),
                BigInt("@ProductId", v_Fixture.ProductId));

            await using var v_PostConnection = OpenConnection($"C01-post-held-{v_Fixture.Tag}");
            await v_PostConnection.OpenAsync();
            var v_iPostSessionId = await IntScalarAsync(v_PostConnection, null, "SELECT @@SPID;");
            var v_PostOutcomeTask = CaptureAsync(() => PostReceiptAsync(v_Fixture, receiptId, v_Fixture.LoginA, v_PostConnection));
            await WaitForSqlLockAsync(v_PostConnection, v_iPostSessionId, v_PostOutcomeTask);

            await using var v_SaveConnection = OpenConnection($"C01-save-after-post-{v_Fixture.Tag}");
            await v_SaveConnection.OpenAsync();
            var v_iSaveSessionId = await IntScalarAsync(v_SaveConnection, null, "SELECT @@SPID;");
            var v_SaveOutcomeTask = CaptureAsync(() => SaveReceiptDetailAsync(
                v_Fixture,
                receiptId,
                5,
                v_Fixture.LoginB,
                p_Connection: v_SaveConnection));
            await WaitForSqlLockAsync(v_SaveConnection, v_iSaveSessionId, v_SaveOutcomeTask);

            await v_GateTransaction.CommitAsync();
            var v_PostOutcome = await v_PostOutcomeTask;
            var v_SaveOutcome = await v_SaveOutcomeTask;

            Assert.True(v_PostOutcome.Succeeded, FormatFailure("Post", v_PostOutcome.Error));
            Assert.True(IsExpectedPostedRejection(v_SaveOutcome.Error), FormatFailure("Save Detail", v_SaveOutcome.Error));
            await AssertReceiptInvariantAsync(v_Fixture, p_ExpectedLedger: 120, p_ExpectedCurrent: 120, p_iExpectedDetailCount: 2);
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task Posting_multiple_details_for_one_scope_does_not_duplicate_invalidation_queue()
    {
        var v_Fixture = await CreateFixtureAsync();
        try
        {
            var receiptId = await SaveReceiptHeaderAsync(v_Fixture, new DateTime(2026, 1, 10));
            await SaveReceiptDetailAsync(v_Fixture, receiptId, 10, v_Fixture.LoginA);
            await SaveReceiptDetailAsync(v_Fixture, receiptId, 20, v_Fixture.LoginA);
            await PostReceiptAsync(v_Fixture, receiptId, v_Fixture.LoginA);

            await AssertReceiptInvariantAsync(v_Fixture, p_ExpectedLedger: 30, p_ExpectedCurrent: 30, p_iExpectedDetailCount: 2);
            Assert.Equal(1, await IntScalarAsync(
                v_Fixture.ConnectionString,
                null,
                "SELECT COUNT(*) FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND LifecycleStatus IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'INITIALIZE_REQUIRED');",
                BigInt("@WarehouseId", v_Fixture.WarehouseId),
                BigInt("@ProductId", v_Fixture.ProductId)));
            Assert.Equal(1, await IntScalarAsync(
                v_Fixture.ConnectionString,
                null,
                "SELECT COUNT(*) FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING');",
                BigInt("@WarehouseId", v_Fixture.WarehouseId),
                BigInt("@ProductId", v_Fixture.ProductId)));
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task Snapshot_rebuild_includes_movement_between_anchor_and_from_date()
    {
        var v_Fixture = await CreateFixtureAsync();
        try
        {
            await CreateAndPostReceiptAsync(v_Fixture, new DateTime(2025, 12, 31), 100, "opening");
            await InsertSnapshotAsync(v_Fixture, new DateTime(2026, 1, 1), 100, p_bIsValid: true);
            await InsertSnapshotAsync(v_Fixture, new DateTime(2026, 1, 5), 0, p_bIsValid: false);

            await CreateAndPostReceiptAsync(v_Fixture, new DateTime(2026, 1, 3), 20, "gap-03");
            await CreateAndPostReceiptAsync(v_Fixture, new DateTime(2026, 1, 5), 30, "target-05");

            await AssertReceiptInvariantAsync(v_Fixture, p_ExpectedLedger: 150, p_ExpectedCurrent: 150, p_iExpectedDetailCount: 3);
            await ExecuteStoredAsync(
                v_Fixture.ConnectionString,
                null,
                "dbo.sp_Inventory_Snapshot_Rebuild",
                BigInt("@Kho_ID", v_Fixture.WarehouseId),
                BigInt("@San_Pham_ID", v_Fixture.ProductId),
                Date("@From_Date", new DateTime(2026, 1, 5)));

            var v_Target = await DecimalScalarAsync(
                v_Fixture.ConnectionString,
                null,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", new DateTime(2026, 1, 5)),
                BigInt("@WarehouseId", v_Fixture.WarehouseId),
                BigInt("@ProductId", v_Fixture.ProductId));
            var v_Anchor = await DecimalScalarAsync(
                v_Fixture.ConnectionString,
                null,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", new DateTime(2026, 1, 1)),
                BigInt("@WarehouseId", v_Fixture.WarehouseId),
                BigInt("@ProductId", v_Fixture.ProductId));

            Assert.Equal(150m, v_Target);
            Assert.Equal(100m, v_Anchor);
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task Snapshot_scope_without_anchor_initializes_only_after_valid_bootstrap()
    {
        var v_Fixture = await CreateFixtureAsync();
        try
        {
            var v_dtmMovementDate = new DateTime(2026, 1, 3);
            var v_dtmBootstrapDate = new DateTime(2026, 1, 1);
            await CreateAndPostReceiptAsync(v_Fixture, v_dtmMovementDate, 10, "initialize");

            var v_Queue = await ReadSnapshotQueueAsync(v_Fixture);
            Assert.Equal("INITIALIZE", v_Queue.RequestType);
            Assert.Equal("INITIALIZE_REQUIRED", v_Queue.LifecycleStatus);

            /* Use the canonical production bootstrap contract.  It creates
               only the declared baseline anchor from Posted Ledger; it must
               not fabricate the target snapshot for the unanchored date. */
            await ExecuteStoredAsync(
                v_Fixture.ConnectionString,
                null,
                "dbo.sp_Inventory_Snapshot_Bootstrap_From_Ledger",
                Date("@Baseline_Date", v_dtmBootstrapDate),
                new SqlParameter("@Opening_Balance_Confirmed", SqlDbType.Bit) { Value = true },
                BigInt("@Kho_ID", v_Fixture.WarehouseId),
                BigInt("@San_Pham_ID", v_Fixture.ProductId));

            Assert.Equal(
                1,
                await IntScalarAsync(
                    v_Fixture.ConnectionString,
                    null,
                    "SELECT COUNT(*) FROM dbo.InventorySnapshot_BootstrapAudit WHERE Baseline_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND Status = N'COMPLETED';",
                    Date("@Date", v_dtmBootstrapDate),
                    BigInt("@WarehouseId", v_Fixture.WarehouseId),
                    BigInt("@ProductId", v_Fixture.ProductId)));
            Assert.Equal(
                1,
                await IntScalarAsync(
                    v_Fixture.ConnectionString,
                    null,
                    "SELECT COUNT(*) FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND IsValid = 1;",
                    Date("@Date", v_dtmBootstrapDate),
                    BigInt("@WarehouseId", v_Fixture.WarehouseId),
                    BigInt("@ProductId", v_Fixture.ProductId)));
            Assert.Equal(
                0,
                await IntScalarAsync(
                    v_Fixture.ConnectionString,
                    null,
                    "SELECT COUNT(*) FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                    Date("@Date", v_dtmMovementDate),
                    BigInt("@WarehouseId", v_Fixture.WarehouseId),
                    BigInt("@ProductId", v_Fixture.ProductId)));

            await ExecuteStoredAsync(
                v_Fixture.ConnectionString,
                null,
                "dbo.sp_Inventory_Snapshot_Process_RebuildQueue",
                new SqlParameter("@Batch_Size", SqlDbType.Int) { Value = 10 },
                Text("@Worker_Name", $"C02-initialize-{v_Fixture.Tag}", 128));

            var v_CompletedQueue = await ReadSnapshotQueueAsync(v_Fixture);
            Assert.Equal("COMPLETED", v_CompletedQueue.LifecycleStatus);
            var v_iSnapshotCount = await IntScalarAsync(
                v_Fixture.ConnectionString,
                null,
                "SELECT COUNT(*) FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", v_dtmMovementDate),
                BigInt("@WarehouseId", v_Fixture.WarehouseId),
                BigInt("@ProductId", v_Fixture.ProductId));
            Assert.Equal(1, v_iSnapshotCount);
            var v_InitializedClosing = await DecimalScalarAsync(
                v_Fixture.ConnectionString,
                null,
                "SELECT ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                Date("@Date", v_dtmMovementDate),
                BigInt("@WarehouseId", v_Fixture.WarehouseId),
                BigInt("@ProductId", v_Fixture.ProductId));
            Assert.Equal(10m, v_InitializedClosing);
            Assert.Equal(
                0,
                await IntScalarAsync(
                    v_Fixture.ConnectionString,
                    null,
                    "SELECT COUNT(*) FROM dbo.InventoryBalance_Snapshot_Daily s WHERE s.Kho_ID = @WarehouseId AND s.San_Pham_ID = @ProductId AND (s.IsValid = 0 OR NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho k WHERE k.Auto_ID = s.Kho_ID) OR NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham p WHERE p.Auto_ID = s.San_Pham_ID));",
                    BigInt("@WarehouseId", v_Fixture.WarehouseId),
                    BigInt("@ProductId", v_Fixture.ProductId)));
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task Three_decimal_quantity_survives_inventory_projections_and_period_report()
    {
        var v_Fixture = await CreateFixtureAsync();
        var v_dtmMovementDate = new DateTime(2026, 1, 10);
        try
        {
            // The rebuild contract requires a valid historical anchor for an
            // already-initialized scope; this test is about decimal preservation,
            // not about exercising the no-anchor bootstrap failure path.
            await InsertSnapshotAsync(v_Fixture, new DateTime(2026, 1, 1), 0m, p_bIsValid: true);
            await CreateAndPostReceiptAsync(v_Fixture, v_dtmMovementDate, 1.234m, "L02-precision");

            await ExecuteStoredAsync(
                v_Fixture.ConnectionString,
                null,
                "dbo.sp_Inventory_Movement_Process_RebuildQueue",
                new SqlParameter("@Batch_Size", SqlDbType.Int) { Value = 100 });
            await ExecuteStoredAsync(
                v_Fixture.ConnectionString,
                null,
                "dbo.sp_Inventory_Balance_Daily_Rebuild",
                BigInt("@Kho_ID", v_Fixture.WarehouseId),
                BigInt("@San_Pham_ID", v_Fixture.ProductId),
                Date("@From_Date", v_dtmMovementDate));

            await ExecuteAsync(
                v_Fixture.ConnectionString,
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
                Date("@Date", v_dtmMovementDate),
                BigInt("@WarehouseId", v_Fixture.WarehouseId),
                BigInt("@ProductId", v_Fixture.ProductId));
            await ExecuteStoredAsync(
                v_Fixture.ConnectionString,
                null,
                "dbo.sp_Inventory_Snapshot_Rebuild",
                BigInt("@Kho_ID", v_Fixture.WarehouseId),
                BigInt("@San_Pham_ID", v_Fixture.ProductId),
                Date("@From_Date", v_dtmMovementDate));

            var v_Projection = await ReadQuantityProjectionAsync(v_Fixture, v_dtmMovementDate);
            Assert.Equal(1.234m, v_Projection.Ledger);
            Assert.Equal(1.234m, v_Projection.Current);
            Assert.Equal(1.234m, v_Projection.MovementReceived);
            Assert.Equal(1.234m, v_Projection.DailyReceived);
            Assert.Equal(1.234m, v_Projection.DailyClosing);
            Assert.Equal(1.234m, v_Projection.SnapshotClosing);

            var v_Report = await ReadPeriodReportQuantityAsync(v_Fixture, v_dtmMovementDate);
            Assert.Equal(1.234m, v_Report.Received);
            Assert.Equal(1.234m, v_Report.Closing);
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task Snapshot_old_claim_cannot_complete_after_a_newer_invalidation()
    {
        var v_Fixture = await CreateFixtureAsync();
        try
        {
            var v_dtmAnchorDate = new DateTime(2025, 12, 31);
            var v_dtmRebuildDate = new DateTime(2026, 1, 2);
            await CreateAndPostReceiptAsync(v_Fixture, new DateTime(2026, 1, 1), 100m, "H04-version");
            await InsertSnapshotAsync(v_Fixture, v_dtmAnchorDate, 0m, p_bIsValid: true);
            await InsertSnapshotAsync(v_Fixture, v_dtmRebuildDate, 0m, p_bIsValid: false);

            var v_iQueueId = await IntScalarAsync(
                v_Fixture.ConnectionString,
                null,
                "SELECT TOP (1) ID FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId ORDER BY ID DESC;",
                BigInt("@WarehouseId", v_Fixture.WarehouseId),
                BigInt("@ProductId", v_Fixture.ProductId));

            await ExecuteAsync(
                v_Fixture.ConnectionString,
                null,
                """
                UPDATE dbo.InventorySnapshot_RebuildQueue
                SET Status = N'PROCESSING', LifecycleStatus = N'PROCESSING', ClaimedBy = N'H04-test',
                    ClaimedAt = SYSUTCDATETIME(), LeaseUntil = DATEADD(MINUTE, 5, SYSUTCDATETIME())
                WHERE ID = @QueueId;
                IF COL_LENGTH(N'dbo.InventorySnapshot_RebuildQueue', N'Claimed_Version') IS NOT NULL
                    EXEC sys.sp_executesql N'UPDATE dbo.InventorySnapshot_RebuildQueue SET Claimed_Version = Requested_Version WHERE ID = @QueueId;', N'@QueueId BIGINT', @QueueId = @QueueId;
                """,
                BigInt("@QueueId", v_iQueueId));

            await ExecuteStoredAsync(
                v_Fixture.ConnectionString,
                null,
                "dbo.sp_Inventory_Snapshot_Rebuild",
                BigInt("@Kho_ID", v_Fixture.WarehouseId),
                BigInt("@San_Pham_ID", v_Fixture.ProductId),
                Date("@From_Date", new DateTime(2026, 1, 1)));

            await ExecuteAsync(
                v_Fixture.ConnectionString,
                null,
                """
                DECLARE @Affected dbo.InventorySnapshotAffectedType;
                INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
                VALUES (@WarehouseId, @ProductId, @FromDate, N'BACK_DATE_POST');
                EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
                """,
                BigInt("@WarehouseId", v_Fixture.WarehouseId),
                BigInt("@ProductId", v_Fixture.ProductId),
                Date("@FromDate", new DateTime(2026, 1, 1)));

            await ExecuteAsync(
                v_Fixture.ConnectionString,
                null,
                """
                DECLARE @Affected dbo.InventorySnapshotAffectedType;
                INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
                VALUES (@WarehouseId, @ProductId, @FromDate, N'RECEIPT_EDIT');
                EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
                """,
                BigInt("@WarehouseId", v_Fixture.WarehouseId),
                BigInt("@ProductId", v_Fixture.ProductId),
                Date("@FromDate", new DateTime(2026, 1, 1)));

            await ExecuteAsync(
                v_Fixture.ConnectionString,
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
                BigInt("@QueueId", v_iQueueId));

            Assert.Equal(
                0,
                await IntScalarAsync(
                    v_Fixture.ConnectionString,
                    null,
                    "SELECT CASE WHEN LifecycleStatus = N'COMPLETED' THEN 1 ELSE 0 END FROM dbo.InventorySnapshot_RebuildQueue WHERE ID = @QueueId;",
                    BigInt("@QueueId", v_iQueueId)));
            Assert.Equal(
                1,
                await IntScalarAsync(
                    v_Fixture.ConnectionString,
                    null,
                    "SELECT COUNT(*) FROM dbo.InventorySnapshot_RebuildQueue WHERE ID = @QueueId AND LifecycleStatus IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'INITIALIZE_REQUIRED');",
                    BigInt("@QueueId", v_iQueueId)));
            Assert.Equal(
                1,
                await IntScalarAsync(
                    v_Fixture.ConnectionString,
                    null,
                    "SELECT CASE WHEN COL_LENGTH(N'dbo.InventorySnapshot_RebuildQueue', N'Requested_Version') IS NOT NULL AND COL_LENGTH(N'dbo.InventorySnapshot_RebuildQueue', N'Claimed_Version') IS NOT NULL THEN 1 ELSE 0 END;"));
            Assert.Equal(
                1,
                await IntScalarAsync(
                    v_Fixture.ConnectionString,
                    null,
                    "SELECT CASE WHEN Requested_Version = Claimed_Version + 2 THEN 1 ELSE 0 END FROM dbo.InventorySnapshot_RebuildQueue WHERE ID = @QueueId;",
                    BigInt("@QueueId", v_iQueueId)));

            await ExecuteStoredAsync(
                v_Fixture.ConnectionString,
                null,
                "dbo.sp_Inventory_Snapshot_Process_RebuildQueue",
                new SqlParameter("@Batch_Size", SqlDbType.Int) { Value = 1 },
                Text("@Worker_Name", $"H04-latest-{v_Fixture.Tag}", 128),
                BigInt("@Kho_ID", v_Fixture.WarehouseId),
                BigInt("@San_Pham_ID", v_Fixture.ProductId));

            Assert.Equal(
                1,
                await IntScalarAsync(
                    v_Fixture.ConnectionString,
                    null,
                    "SELECT CASE WHEN LifecycleStatus = N'COMPLETED' AND Requested_Version = Claimed_Version THEN 1 ELSE 0 END FROM dbo.InventorySnapshot_RebuildQueue WHERE ID = @QueueId;",
                    BigInt("@QueueId", v_iQueueId)));
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task Snapshot_waiting_invalidation_coalesces_and_advances_version()
    {
        var v_Fixture = await CreateFixtureAsync();
        try
        {
            var v_dtmInvalidationDate = new DateTime(2026, 2, 10);
            await CreateAndPostReceiptAsync(v_Fixture, v_dtmInvalidationDate, 10m, "H04-waiting");

            await ExecuteAsync(
                v_Fixture.ConnectionString,
                null,
                """
                DECLARE @Affected dbo.InventorySnapshotAffectedType;
                INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
                VALUES (@WarehouseId, @ProductId, @FromDate, N'BACK_DATE_POST');
                EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
                """,
                BigInt("@WarehouseId", v_Fixture.WarehouseId),
                BigInt("@ProductId", v_Fixture.ProductId),
                Date("@FromDate", v_dtmInvalidationDate));

            Assert.Equal(
                1,
                await IntScalarAsync(
                    v_Fixture.ConnectionString,
                    null,
                    "SELECT COUNT(*) FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND LifecycleStatus = N'INITIALIZE_REQUIRED';",
                    BigInt("@WarehouseId", v_Fixture.WarehouseId),
                    BigInt("@ProductId", v_Fixture.ProductId)));
            Assert.Equal(
                1,
                await IntScalarAsync(
                    v_Fixture.ConnectionString,
                    null,
                    "SELECT CASE WHEN Requested_Version = 2 AND Claimed_Version IS NULL THEN 1 ELSE 0 END FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                    BigInt("@WarehouseId", v_Fixture.WarehouseId),
                    BigInt("@ProductId", v_Fixture.ProductId)));
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var v_Tag = $"C01C02-{Guid.NewGuid():N}";
        var v_LoginA = $"{v_Tag}-a";
        var v_LoginB = $"{v_Tag}-b";

        await using var v_Connection = OpenConnection($"C01C02-setup-{v_Tag}");
        await v_Connection.OpenAsync();
        await using var v_Transaction = (SqlTransaction)await v_Connection.BeginTransactionAsync();

        try
        {
            var unitId = await InsertIdAsync(
                v_Connection,
                v_Transaction,
                "INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-unit", 200));
            var categoryId = await InsertIdAsync(
                v_Connection,
                v_Transaction,
                "INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP, Ten_LSP, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{v_Tag}-category-code", 100),
                Text("@Name", $"{v_Tag}-category", 200));
            var productId = await InsertIdAsync(
                v_Connection,
                v_Transaction,
                "INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, @CategoryId, @UnitId, N'');",
                Text("@Code", $"{v_Tag}-product-code", 100),
                Text("@Name", $"{v_Tag}-product", 255),
                BigInt("@CategoryId", categoryId),
                BigInt("@UnitId", unitId));
            var supplierId = await InsertIdAsync(
                v_Connection,
                v_Transaction,
                "INSERT dbo.tbl_DM_NCC(Ma_NCC, Ten_NCC, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{v_Tag}-supplier-code", 100),
                Text("@Name", $"{v_Tag}-supplier", 255));
            var warehouseId = await InsertIdAsync(
                v_Connection,
                v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-warehouse", 255));

            await InsertMemberAsync(v_Connection, v_Transaction, v_LoginA, $"{v_Tag}-member-a");
            await InsertMemberAsync(v_Connection, v_Transaction, v_LoginB, $"{v_Tag}-member-b");
            await ExecuteAsync(
                v_Connection,
                v_Transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId), (@LoginB, @WarehouseId);",
                Text("@Login", v_LoginA, 100),
                Text("@LoginB", v_LoginB, 100),
                BigInt("@WarehouseId", warehouseId));

            await v_Transaction.CommitAsync();
            return new Fixture(
                v_Tag,
                v_LoginA,
                v_LoginB,
                unitId,
                categoryId,
                productId,
                supplierId,
                warehouseId,
                new SqlConnectionStringBuilder(BaseConnectionString).ConnectionString);
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<long> SaveReceiptHeaderAsync(Fixture p_Fixture, DateTime p_dtmDate)
    {
        await using var v_Connection = OpenConnection($"C01C02-header-{p_Fixture.Tag}");
        await v_Connection.OpenAsync();
        return await ExecuteStoredWithOutputAsync(
            v_Connection,
            null,
            "dbo.F2011_sp_ins_Nhap_Kho_Header",
            Text("@So_Phieu_Nhap_Kho", $"{p_Fixture.Tag}-{Guid.NewGuid():N}", 100),
            BigInt("@Kho_ID", p_Fixture.WarehouseId),
            BigInt("@NCC_ID", p_Fixture.SupplierId),
            Date("@Ngay_Nhap_Kho", p_dtmDate),
            Text("@Ghi_Chu", "", 1000),
            Text("@Ma_Dang_Nhap", p_Fixture.LoginA, 100));
    }

    private static async Task<long> SaveReceiptDetailAsync(
        Fixture p_Fixture,
        long receiptId,
        decimal p_Quantity,
        string p_Login,
        long detailId = 0,
        SqlConnection? p_Connection = null,
        SqlTransaction? p_Transaction = null)
    {
        var v_bOwnsConnection = p_Connection is null;
        p_Connection ??= OpenConnection($"C01C02-detail-{p_Fixture.Tag}");
        if (v_bOwnsConnection)
            await p_Connection.OpenAsync();

        try
        {
            string v_strProcedure;
            if (detailId == 0)
            {
                v_strProcedure = "dbo.F2011_sp_ins_Nhap_Kho_Detail";
            }
            else
            {
                v_strProcedure = "dbo.F2011_sp_upd_Nhap_Kho_Detail";
            }

            return await ExecuteStoredWithOutputAsync(
                p_Connection,
                p_Transaction,
                v_strProcedure,
                BigInt("@Nhap_Kho_ID", receiptId),
                BigInt("@San_Pham_ID", p_Fixture.ProductId),
                Decimal("@SL_Nhap", p_Quantity),
                Decimal("@Don_Gia_Nhap", 1),
                Text("@Ma_Dang_Nhap", p_Login, 100),
                BigInt("@Auto_ID", detailId));
        }
        finally
        {
            if (v_bOwnsConnection)
                await p_Connection.DisposeAsync();
        }
    }

    private static async Task DeleteReceiptDetailAsync(Fixture p_Fixture, long detailId, string p_Login)
    {
        await ExecuteStoredAsync(
            p_Fixture.ConnectionString,
            null,
            "dbo.F2011_sp_del_Nhap_Kho_Detail",
            BigInt("@Auto_ID", detailId),
            Text("@Ma_Dang_Nhap", p_Login, 100));
    }

    private static Task PostReceiptAsync(
        Fixture p_Fixture,
        long receiptId,
        string p_Login,
        SqlConnection? p_Connection = null)
    {
        if (p_Connection is null)
        {
            return ExecuteStoredAsync(
                p_Fixture.ConnectionString,
                null,
                "dbo.sp_XNK_Document_Post",
                new SqlParameter("@Is_Receipt", SqlDbType.Bit) { Value = true },
                BigInt("@Document_ID", receiptId),
                Text("@Ma_Dang_Nhap", p_Login, 100));
        }
        else
        {
            return ExecuteStoredAsync(
                p_Connection,
                null,
                "dbo.sp_XNK_Document_Post",
                new SqlParameter("@Is_Receipt", SqlDbType.Bit) { Value = true },
                BigInt("@Document_ID", receiptId),
                Text("@Ma_Dang_Nhap", p_Login, 100));
        }
    }

    private static async Task CreateAndPostReceiptAsync(Fixture p_Fixture, DateTime p_dtmDate, decimal p_Quantity, string p_Suffix)
    {
        var receiptId = await SaveReceiptHeaderAsync(p_Fixture, p_dtmDate);
        await SaveReceiptDetailAsync(p_Fixture, receiptId, p_Quantity, p_Fixture.LoginA);
        await PostReceiptAsync(p_Fixture, receiptId, p_Fixture.LoginA);
    }

    private static async Task InsertReceiptDetailDirectlyAsync(Fixture p_Fixture, long receiptId, decimal p_Quantity)
    {
        await EnsureDirectDmlProbeUserAsync();
        await using var v_Connection = OpenConnection($"C01C02-dml-{p_Fixture.Tag}");
        await v_Connection.OpenAsync();
        var v_bImpersonated = false;
        try
        {
            await ExecuteAsync(
                v_Connection,
                null,
                $"EXECUTE AS USER = N'{DirectDmlProbeUser}'; EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            v_bImpersonated = true;
            await ExecuteAsync(
                v_Connection,
                null,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@ReceiptId, @ProductId, @Quantity, 1);",
                BigInt("@ReceiptId", receiptId),
                BigInt("@ProductId", p_Fixture.ProductId),
                Decimal("@Quantity", p_Quantity));
        }
        finally
        {
            if (v_bImpersonated)
                await ExecuteAsync(v_Connection, null, "REVERT;");

            await DropDirectDmlProbeUserAsync();
        }
    }

    private static async Task EnsureDirectDmlProbeUserAsync()
    {
        await using var v_Connection = OpenConnection("C01C02-probe-admin");
        await v_Connection.OpenAsync();
        await ExecuteAsync(
            v_Connection,
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
        await using var v_Connection = OpenConnection("C01C02-probe-cleanup");
        await v_Connection.OpenAsync();
        await ExecuteAsync(v_Connection, null, $"IF DATABASE_PRINCIPAL_ID(N'{DirectDmlProbeUser}') IS NOT NULL DROP USER [{DirectDmlProbeUser}];");
    }

    private static Task InsertSnapshotAsync(Fixture p_Fixture, DateTime p_dtmDate, decimal p_Closing, bool p_bIsValid)
    {
        return ExecuteAsync(p_Fixture.ConnectionString, null, "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, @Closing, @IsValid, 1);", Date("@Date", p_dtmDate), BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId), Decimal("@Closing", p_Closing), new SqlParameter("@IsValid", SqlDbType.Bit) { Value = p_bIsValid });
    }

    private static async Task AssertReceiptInvariantAsync(Fixture p_Fixture, decimal p_ExpectedLedger, decimal p_ExpectedCurrent, int p_iExpectedDetailCount)
    {
        var v_Invariant = await ReadReceiptInvariantAsync(p_Fixture);
        Assert.Equal(p_ExpectedLedger, v_Invariant.Ledger);
        Assert.Equal(p_ExpectedCurrent, v_Invariant.Current);
        Assert.Equal(p_iExpectedDetailCount, v_Invariant.DetailCount);
    }

    private static async Task<ReceiptInvariant> ReadReceiptInvariantAsync(Fixture p_Fixture)
    {
        await using var v_Connection = OpenConnection($"C01C02-read-{p_Fixture.Tag}");
        await v_Connection.OpenAsync();
        await using var v_Command = new SqlCommand(
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
            v_Connection);
        v_Command.Parameters.Add(BigInt("@WarehouseId", p_Fixture.WarehouseId));
        v_Command.Parameters.Add(BigInt("@ProductId", p_Fixture.ProductId));
        v_Command.Parameters.Add(Text("@TagPrefix", $"{p_Fixture.Tag}%", 100));
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        return new ReceiptInvariant(v_Reader.GetDecimal(0), v_Reader.GetDecimal(1), v_Reader.GetInt32(2));
    }

    private static async Task<SnapshotQueueRow> ReadSnapshotQueueAsync(Fixture p_Fixture)
    {
        await using var v_Connection = OpenConnection($"C02-queue-read-{p_Fixture.Tag}");
        await v_Connection.OpenAsync();
        await using var v_Command = new SqlCommand(
            "SELECT TOP (1) RequestType, LifecycleStatus FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId ORDER BY ID DESC;",
            v_Connection);
        v_Command.Parameters.Add(BigInt("@WarehouseId", p_Fixture.WarehouseId));
        v_Command.Parameters.Add(BigInt("@ProductId", p_Fixture.ProductId));
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        return new SnapshotQueueRow(v_Reader.GetString(0), v_Reader.GetString(1));
    }

    private static async Task<QuantityProjection> ReadQuantityProjectionAsync(Fixture p_Fixture, DateTime p_dtmDate)
    {
        await using var v_Connection = OpenConnection($"L02-projection-read-{p_Fixture.Tag}");
        await v_Connection.OpenAsync();
        await using var v_Command = new SqlCommand(
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
            v_Connection);
        v_Command.Parameters.Add(BigInt("@WarehouseId", p_Fixture.WarehouseId));
        v_Command.Parameters.Add(BigInt("@ProductId", p_Fixture.ProductId));
        v_Command.Parameters.Add(Date("@Date", p_dtmDate));
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        return new QuantityProjection(
            v_Reader.GetDecimal(0),
            v_Reader.GetDecimal(1),
            v_Reader.GetDecimal(2),
            v_Reader.GetDecimal(3),
            v_Reader.GetDecimal(4),
            v_Reader.GetDecimal(5));
    }

    private static async Task<PeriodReportQuantity> ReadPeriodReportQuantityAsync(Fixture p_Fixture, DateTime p_dtmDate)
    {
        await using var v_Connection = OpenConnection($"L02-report-read-{p_Fixture.Tag}");
        await v_Connection.OpenAsync();
        await using var v_Command = new SqlCommand("dbo.sp_BC_Xuat_Nhap_Ton_Page", v_Connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        v_Command.Parameters.Add(Date("@Tu_Ngay", p_dtmDate));
        v_Command.Parameters.Add(Date("@Den_Ngay", p_dtmDate));
        v_Command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
        v_Command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 10 });
        v_Command.Parameters.Add(Text("@Ma_Dang_Nhap", p_Fixture.LoginA, 100));
        v_Command.Parameters.Add(BigInt("@Kho_ID", p_Fixture.WarehouseId));

        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        Assert.True(await v_Reader.NextResultAsync());
        Assert.True(await v_Reader.ReadAsync());
        return new PeriodReportQuantity(
            v_Reader.GetDecimal(v_Reader.GetOrdinal("SL_Nhap")),
            v_Reader.GetDecimal(v_Reader.GetOrdinal("SL_Cuoi_Ky")));
    }

    private static async Task WaitForSqlLockAsync(SqlConnection p_SaveConnection, int p_iSessionId, Task<OperationOutcome> p_Operation)
    {
        await using var v_Monitor = OpenConnection("C01-monitor");
        await v_Monitor.OpenAsync();
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (p_Operation.IsCompleted)
                throw new Xunit.Sdk.XunitException("Save Detail completed before the controlled TOCTOU lock was reached.");

            await using var v_Command = new SqlCommand(
                "SELECT TOP (1) wait_type, blocking_session_id FROM sys.dm_exec_requests WHERE session_id = @SessionId;",
                v_Monitor)
            {
                CommandTimeout = 1
            };
            v_Command.Parameters.Add(new SqlParameter("@SessionId", SqlDbType.Int) { Value = p_iSessionId });
            await using var v_Reader = await v_Command.ExecuteReaderAsync();
            if (await v_Reader.ReadAsync())
            {
                string? v_WaitType;
                if (v_Reader.IsDBNull(0))
                {
                    v_WaitType = null;
                }
                else
                {
                    v_WaitType = v_Reader.GetString(0);
                }

                int v_iBlockingSessionId;
                if (v_Reader.IsDBNull(1))
                {
                    v_iBlockingSessionId = 0;
                }
                else
                {
                    v_iBlockingSessionId = Convert.ToInt32(v_Reader.GetValue(1));
                }
                if (v_iBlockingSessionId > 0 && v_WaitType?.StartsWith("LCK_", StringComparison.OrdinalIgnoreCase) == true)
                    return;
            }

            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("Save Detail did not reach the controlled lock wait within five seconds.");
    }

    private static async Task<bool> WaitForCompletionAsync(Task<OperationOutcome> p_Operation, TimeSpan p_tsTimeout)
    {
        var v_Completed = await Task.WhenAny(p_Operation, Task.Delay(p_tsTimeout));
        return v_Completed == p_Operation;
    }

    private static async Task<OperationOutcome> CaptureAsync(Func<Task> p_Operation)
    {
        try
        {
            await p_Operation();
            return new OperationOutcome(true, null);
        }
        catch (Exception v_Exception)
        {
            return new OperationOutcome(false, v_Exception);
        }
    }

    private static bool IsExpectedPostedRejection(Exception? p_Exception)
    {
        return p_Exception is SqlException v_SqlException && v_SqlException.Number == 51163;
    }

    private static string FormatFailure(string p_Operation, Exception? p_Exception)
    {
        if (p_Exception is null)
        {
            return $"{p_Operation} did not complete.";
        }
        else
        {
            return $"{p_Operation} failed: {p_Exception.Message}";
        }
    }

    private static async Task AssertSqlNumberAsync(int p_iExpectedNumber, Func<Task> p_Operation)
    {
        var v_Exception = await Assert.ThrowsAsync<SqlException>(p_Operation);
        Assert.Equal(p_iExpectedNumber, v_Exception.Number);
    }

    private static async Task InsertMemberAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Login, string p_Name)
    {
        await ExecuteAsync(
            p_Connection,
            p_Transaction,
            "DECLARE @MemberId BIGINT; SELECT @MemberId = ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX); INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, @Name, 0);",
            Text("@Login", p_Login, 100),
            Text("@Name", p_Name, 200));
    }

    private static async Task CleanupFixtureAsync(Fixture p_Fixture)
    {
        await using var v_Connection = OpenConnection($"C01C02-cleanup-{p_Fixture.Tag}");
        await v_Connection.OpenAsync();
        await using var v_Transaction = (SqlTransaction)await v_Connection.BeginTransactionAsync();
        try
        {
            await ExecuteAsync(v_Connection, v_Transaction, "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE d FROM dbo.InventorySnapshot_RebuildDeadLetter d JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId;", BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.InventoryReservation_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.InventorySnapshot_BootstrapAudit WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE d FROM dbo.InventoryMovement_RebuildDeadLetter d JOIN dbo.InventoryMovement_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId;", BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho LIKE @TagPrefix;", Text("@TagPrefix", $"{p_Fixture.Tag}%", 100));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap IN (@LoginA, @LoginB);", Text("@LoginA", p_Fixture.LoginA, 100), Text("@LoginB", p_Fixture.LoginB, 100));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap IN (@LoginA, @LoginB);", Text("@LoginA", p_Fixture.LoginA, 100), Text("@LoginB", p_Fixture.LoginB, 100));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId; DELETE FROM dbo.tbl_DM_NCC WHERE Auto_ID = @SupplierId; DELETE FROM dbo.tbl_DM_San_Pham WHERE Auto_ID = @ProductId; DELETE FROM dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID = @CategoryId; DELETE FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID = @UnitId;", BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@SupplierId", p_Fixture.SupplierId), BigInt("@ProductId", p_Fixture.ProductId), BigInt("@CategoryId", p_Fixture.CategoryId), BigInt("@UnitId", p_Fixture.UnitId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;", BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await v_Transaction.CommitAsync();
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static SqlConnection OpenConnection(string p_ApplicationName)
    {
        var v_Builder = new SqlConnectionStringBuilder(BaseConnectionString)
        {
            ApplicationName = p_ApplicationName
        };
        return new SqlConnection(v_Builder.ConnectionString);
    }

    private static async Task<long> InsertIdAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        return Convert.ToInt64(await v_Command.ExecuteScalarAsync());
    }

    private static async Task<long> ExecuteStoredWithOutputAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Procedure, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure };
        var v_Output = p_arrParameters.SingleOrDefault(parameter => parameter.ParameterName == "@Auto_ID");
        v_Output ??= BigInt("@Auto_ID", 0);
        v_Output.Direction = ParameterDirection.InputOutput;
        v_Command.Parameters.Add(v_Output);
        v_Command.Parameters.AddRange(p_arrParameters.Where(parameter => parameter != v_Output).ToArray());
        await v_Command.ExecuteNonQueryAsync();
        return Convert.ToInt64(v_Output.Value);
    }

    private static async Task ExecuteStoredAsync(string p_ConnectionString, SqlTransaction? p_Transaction, string p_Procedure, params SqlParameter[] p_arrParameters)
    {
        SqlConnection? v_Connection = null;
        if (p_Transaction is null)
        {
            v_Connection = OpenConnection($"C01C02-sql-{p_Procedure}");
        }

        await using (v_Connection)
        {
            SqlConnection v_ActiveConnection;
            if (p_Transaction is null)
            {
                v_ActiveConnection = v_Connection!;
            }
            else
            {
                v_ActiveConnection = p_Transaction.Connection!;
            }

            if (p_Transaction is null)
                await v_ActiveConnection.OpenAsync();
            await using var v_Command = new SqlCommand(p_Procedure, v_ActiveConnection, p_Transaction) { CommandType = CommandType.StoredProcedure };
            v_Command.Parameters.AddRange(p_arrParameters);
            await v_Command.ExecuteNonQueryAsync();
        }
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

    private static async Task ExecuteAsync(string p_ConnectionString, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        SqlConnection? v_Connection = null;
        if (p_Transaction is null)
        {
            v_Connection = OpenConnection("C01C02-command");
        }

        await using (v_Connection)
        {
            SqlConnection v_ActiveConnection;
            if (p_Transaction is null)
            {
                v_ActiveConnection = v_Connection!;
            }
            else
            {
                v_ActiveConnection = p_Transaction.Connection!;
            }

            if (p_Transaction is null)
                await v_ActiveConnection.OpenAsync();
            await ExecuteAsync(v_ActiveConnection, p_Transaction, p_Sql, p_arrParameters);
        }
    }

    private static async Task<int> IntScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToInt32(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
    }

    private static async Task<int> IntScalarAsync(string p_ConnectionString, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        SqlConnection? v_Connection = null;
        if (p_Transaction is null)
        {
            v_Connection = OpenConnection("C01C02-int");
        }

        await using (v_Connection)
        {
            SqlConnection v_ActiveConnection;
            if (p_Transaction is null)
            {
                v_ActiveConnection = v_Connection!;
            }
            else
            {
                v_ActiveConnection = p_Transaction.Connection!;
            }

            if (p_Transaction is null)
                await v_ActiveConnection.OpenAsync();
            return await IntScalarAsync(v_ActiveConnection, p_Transaction, p_Sql, p_arrParameters);
        }
    }

    private static async Task<decimal> DecimalScalarAsync(string p_ConnectionString, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        SqlConnection? v_Connection = null;
        if (p_Transaction is null)
        {
            v_Connection = OpenConnection("C01C02-decimal");
        }

        await using (v_Connection)
        {
            SqlConnection v_ActiveConnection;
            if (p_Transaction is null)
            {
                v_ActiveConnection = v_Connection!;
            }
            else
            {
                v_ActiveConnection = p_Transaction.Connection!;
            }

            if (p_Transaction is null)
                await v_ActiveConnection.OpenAsync();
            return Convert.ToDecimal(await ScalarAsync(v_ActiveConnection, p_Transaction, p_Sql, p_arrParameters));
        }
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

    private static SqlParameter Decimal(string p_Name, decimal p_Value)
    {
        return new(p_Name, SqlDbType.Decimal)
        {
            Precision = 18,
            Scale = 3,
            Value = p_Value
        };
    }

    private static SqlParameter Date(string p_Name, DateTime p_dtmValue)
    {
        return new(p_Name, SqlDbType.Date)
        {
            Value = p_dtmValue.Date
        };
    }

    private static SqlParameter Text(string p_Name, string p_Value, int p_iSize)
    {
        return new(p_Name, SqlDbType.NVarChar, p_iSize)
        {
            Value = p_Value
        };
    }

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
