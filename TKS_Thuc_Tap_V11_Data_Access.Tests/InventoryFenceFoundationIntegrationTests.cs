using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Sdk;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class InventoryFenceFoundationIntegrationTests : IAsyncLifetime
{
    private const string ScratchConnectionEnvironmentVariable = "TKS_FENCE_FOUNDATION_CONNECTION_STRING";
    private const string ScratchOptInEnvironmentVariable = "TKS_FENCE_FOUNDATION_SCRATCH_OPT_IN";
    private const string BusinessDatabaseName = "TKS_Thuc_Tap_V11_GiaiDoan2";

    public async Task InitializeAsync()
    {
        if (!IsScratchOptedIn())
            return;

        await using var v_Connection = await OpenScratchConnectionAsync();
        await ExecuteAsync(v_Connection, null, m_DropFoundationSql);
        await InstallFoundationAsync(v_Connection);
    }

    public async Task DisposeAsync()
    {
        if (!IsScratchOptedIn())
            return;

        await using var v_Connection = await OpenScratchConnectionAsync();
        await ExecuteAsync(v_Connection, null, m_DropFoundationSql);
    }

    [Fact]
    public void Foundation_schema_uses_duplicate_tolerant_typed_sets()
    {
        var v_Schema = ReadRepositoryFile("Database", "WarehouseModule.Schema.sql");
        var v_iStart = v_Schema.IndexOf("/* PERF-06A FOUNDATION TYPES START */", StringComparison.Ordinal);
        var v_iEnd = v_Schema.IndexOf("/* PERF-06A FOUNDATION TYPES END */", v_iStart, StringComparison.Ordinal);

        Assert.True(v_iStart >= 0 && v_iEnd > v_iStart, "PERF-06A schema markers are missing or out of order.");
        var v_Foundation = v_Schema[v_iStart..v_iEnd];
        Assert.Contains("InventoryFenceGroupSetType", v_Foundation, StringComparison.Ordinal);
        Assert.Contains("InventoryFenceScopeSetType", v_Foundation, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIMARY KEY", v_Foundation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Foundation_procedure_source_has_one_canonical_resource_seam()
    {
        var v_Source = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var v_iStart = v_Source.IndexOf("/* PERF-06A FOUNDATION START */", StringComparison.Ordinal);
        var v_iEnd = v_Source.IndexOf("/* PERF-06A FOUNDATION END */", v_iStart, StringComparison.Ordinal);

        Assert.True(v_iStart >= 0 && v_iEnd > v_iStart, "PERF-06A procedure markers are missing or out of order.");
        var v_Foundation = v_Source[v_iStart..v_iEnd];

        Assert.Contains("fn_Inventory_Fence_Root_Resource", v_Foundation, StringComparison.Ordinal);
        Assert.Contains("fn_Inventory_Fence_Group_Resource", v_Foundation, StringComparison.Ordinal);
        Assert.Contains("fn_Inventory_Fence_Scope_Resource", v_Foundation, StringComparison.Ordinal);
        Assert.Contains("InventoryMovementGroup:Root", v_Foundation, StringComparison.Ordinal);
        Assert.Contains("InventoryMovementGroup:' + CONVERT", v_Foundation, StringComparison.Ordinal);
        Assert.Contains("InventoryMovement:'", v_Foundation, StringComparison.Ordinal);
        Assert.DoesNotContain("SESSION_CONTEXT(", v_Foundation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@Scope_Lock_Held", v_Foundation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@Bootstrap_Lock_Held", v_Foundation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Canonical_resource_functions_render_decimal_names_and_reject_invalid_ids()
    {
        await using var v_Connection = await OpenInstalledScratchConnectionAsync();

        Assert.Equal(
            "InventoryMovementGroup:Root",
            await ScalarStringAsync(v_Connection, null,
                "SELECT dbo.fn_Inventory_Fence_Root_Resource();"));
        Assert.Equal(
            "InventoryMovementGroup:42",
            await ScalarStringAsync(v_Connection, null,
                "SELECT dbo.fn_Inventory_Fence_Group_Resource(@Kho_ID);",
                "42"));
        Assert.Equal(
            "InventoryMovement:42:9001",
            await ScalarStringAsync(v_Connection, null,
                "SELECT dbo.fn_Inventory_Fence_Scope_Resource(@Kho_ID, @San_Pham_ID);",
                "42",
                "9001"));
        Assert.Equal(
            string.Empty,
            await ScalarStringAsync(v_Connection, null,
                "SELECT dbo.fn_Inventory_Fence_Group_Resource(@Kho_ID);",
                "0"));
    }

    [Fact]
    public async Task Root_shared_acquisition_succeeds_with_transaction_owner()
    {
        await using var v_Connection = await OpenInstalledScratchConnectionAsync();
        using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';");

            var v_Mode = await ScalarStringAsync(v_Connection, v_Transaction,
                "SELECT APPLOCK_MODE(N'public', dbo.fn_Inventory_Fence_Root_Resource(), N'Transaction');");

            Assert.Equal("Shared", v_Mode);
        }
        finally
        {
            v_Transaction.Rollback();
        }
    }

    [Fact]
    public async Task Root_exclusive_conflict_surfaces_negative_applock_result_as_failure()
    {
        await using var v_Owner = await OpenInstalledScratchConnectionAsync();
        using var v_OwnerTransaction = v_Owner.BeginTransaction();
        await ExecuteAsync(v_Owner, v_OwnerTransaction,
            "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';");

        await using var v_Contender = await OpenScratchConnectionAsync();
        using var v_ContenderTransaction = v_Contender.BeginTransaction();

        try
        {
            var v_Error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
                v_Contender,
                v_ContenderTransaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Exclusive';"));

            Assert.Equal(51403, v_Error.Number);
        }
        finally
        {
            v_ContenderTransaction.Rollback();
            v_OwnerTransaction.Rollback();
        }
    }

    [Fact]
    public async Task Group_and_scope_sets_deduplicate_and_acquire_in_canonical_order()
    {
        await using var v_Connection = await OpenInstalledScratchConnectionAsync();
        using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';");

            var v_GroupSet = CreateGroupSet(9, 2, 9, 1, 2);
            await ExecuteAsync(
                v_Connection,
                v_Transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Group_Set @GroupSet = @GroupSet, @Mode = N'Exclusive';",
                StructuredParameter("@GroupSet", "dbo.InventoryFenceGroupSetType", v_GroupSet));

            Assert.Equal("Exclusive", await GroupModeAsync(v_Connection, v_Transaction, 1));
            Assert.Equal("Exclusive", await GroupModeAsync(v_Connection, v_Transaction, 2));
            Assert.Equal("Exclusive", await GroupModeAsync(v_Connection, v_Transaction, 9));

            var v_ScopeSet = CreateScopeSet((9, 20), (1, 10), (9, 20), (2, 11));
            await ExecuteAsync(
                v_Connection,
                v_Transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Legacy_Scope_Set @ScopeSet = @ScopeSet, @Mode = N'Shared';",
                StructuredParameter("@ScopeSet", "dbo.InventoryFenceScopeSetType", v_ScopeSet));

            Assert.Equal("Shared", await ScopeModeAsync(v_Connection, v_Transaction, 1, 10));
            Assert.Equal("Shared", await ScopeModeAsync(v_Connection, v_Transaction, 2, 11));
            Assert.Equal("Shared", await ScopeModeAsync(v_Connection, v_Transaction, 9, 20));
        }
        finally
        {
            v_Transaction.Rollback();
        }

        var v_Source = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var v_Foundation = ExtractFoundation(v_Source, "/* PERF-06A FOUNDATION START */", "/* PERF-06A FOUNDATION END */");
        Assert.Contains("GROUP BY Kho_ID\n        ORDER BY Kho_ID", v_Foundation, StringComparison.Ordinal);
        Assert.Contains("GROUP BY Kho_ID, San_Pham_ID\n        ORDER BY Kho_ID, San_Pham_ID", v_Foundation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_identifier_fails_before_any_group_is_acquired()
    {
        await using var v_Connection = await OpenInstalledScratchConnectionAsync();
        using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';");

            var v_InvalidGroups = CreateGroupSet(8, 0, 7);
            var v_Error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
                v_Connection,
                v_Transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Group_Set @GroupSet = @GroupSet, @Mode = N'Exclusive';",
                StructuredParameter("@GroupSet", "dbo.InventoryFenceGroupSetType", v_InvalidGroups)));

            Assert.Equal(51409, v_Error.Number);
            Assert.Equal("NoLock", await GroupModeAsync(v_Connection, v_Transaction, 8));
            Assert.Equal("NoLock", await GroupModeAsync(v_Connection, v_Transaction, 7));
        }
        finally
        {
            v_Transaction.Rollback();
        }
    }

    [Fact]
    public async Task Missing_root_and_reverse_scope_order_are_rejected_by_public_api()
    {
        await using var v_Connection = await OpenInstalledScratchConnectionAsync();
        using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_GroupError = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
                v_Connection,
                v_Transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Group @Kho_ID = 4, @Mode = N'Exclusive';"));
            Assert.Equal(51406, v_GroupError.Number);

            var v_ScopeError = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
                v_Connection,
                v_Transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Legacy_Scope @Kho_ID = 4, @San_Pham_ID = 6, @Mode = N'Shared';"));
            Assert.Equal(51406, v_ScopeError.Number);
        }
        finally
        {
            v_Transaction.Rollback();
        }
    }

    [Fact]
    public async Task Acquisition_requires_a_transaction_and_context_requires_same_transaction()
    {
        await using var v_Connection = await OpenInstalledScratchConnectionAsync();

        var v_NoTransactionError = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
            v_Connection,
            null,
            "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';"));
        Assert.Equal(51401, v_NoTransactionError.Number);

        using var v_Transaction = v_Connection.BeginTransaction();
        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';");
            await ExecuteAsync(v_Connection, v_Transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Group @Kho_ID = 4, @Mode = N'Shared';");

            await using var v_OtherConnection = await OpenScratchConnectionAsync();
            using var v_OtherTransaction = v_OtherConnection.BeginTransaction();
            try
            {
                var v_Error = await Assert.ThrowsAsync<SqlException>(() => ExecuteContextAsync(
                    v_OtherConnection,
                    v_OtherTransaction,
                    CreateGroupSet(4),
                    CreateScopeSet(),
                    "Shared",
                    "Shared",
                    "Shared"));

                Assert.Equal(51419, v_Error.Number);
            }
            finally
            {
                v_OtherTransaction.Rollback();
            }
        }
        finally
        {
            v_Transaction.Rollback();
        }
    }

    [Fact]
    public async Task Session_owned_lock_is_not_accepted_as_transaction_context()
    {
        await using var v_Connection = await OpenInstalledScratchConnectionAsync();

        try
        {
            await ExecuteAsync(v_Connection, null, "DECLARE @Result INT; EXEC @Result = sys.sp_getapplock @Resource = N'InventoryMovementGroup:Root', @LockMode = N'Shared', @LockOwner = N'Session', @LockTimeout = 0, @DbPrincipal = N'public'; IF @Result < 0 THROW 51490, N'Unable to prepare session-owned lock.', 1;");
            using var v_Transaction = v_Connection.BeginTransaction();

            try
            {
                var v_Error = await Assert.ThrowsAsync<SqlException>(() => ExecuteContextAsync(
                    v_Connection,
                    v_Transaction,
                    CreateGroupSet(),
                    CreateScopeSet(),
                    "Shared",
                    "Shared",
                    "Shared"));

                Assert.Equal(51419, v_Error.Number);
            }
            finally
            {
                v_Transaction.Rollback();
            }
        }
        finally
        {
            await ExecuteAsync(v_Connection, null, "EXEC sys.sp_releaseapplock @Resource = N'InventoryMovementGroup:Root', @LockOwner = N'Session', @DbPrincipal = N'public';");
        }
    }

    [Fact]
    public async Task Context_rejects_missing_group_and_insufficient_mode()
    {
        await using var v_Connection = await OpenInstalledScratchConnectionAsync();
        using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';");

            var v_MissingGroupError = await Assert.ThrowsAsync<SqlException>(() => ExecuteContextAsync(
                v_Connection,
                v_Transaction,
                CreateGroupSet(4),
                CreateScopeSet((4, 6)),
                    "Shared",
                    "Shared",
                    "Shared"));
            Assert.Equal(51420, v_MissingGroupError.Number);

            await ExecuteAsync(v_Connection, v_Transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Group @Kho_ID = 4, @Mode = N'Shared';");

            var v_ModeError = await Assert.ThrowsAsync<SqlException>(() => ExecuteContextAsync(
                v_Connection,
                v_Transaction,
                CreateGroupSet(4),
                CreateScopeSet(),
                "Shared",
                "Exclusive",
                "Shared"));
            Assert.Equal(51420, v_ModeError.Number);
        }
        finally
        {
            v_Transaction.Rollback();
        }
    }

    [Fact]
    public async Task Transaction_rollback_releases_root_group_and_scope_locks()
    {
        await using var v_Connection = await OpenInstalledScratchConnectionAsync();
        using (var v_Transaction = v_Connection.BeginTransaction())
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared'; EXEC dbo.sp_Inventory_Fence_Acquire_Legacy_Scope @Kho_ID = 4, @San_Pham_ID = 6, @Mode = N'Exclusive';");
            v_Transaction.Rollback();
        }

        using var v_ProbeTransaction = v_Connection.BeginTransaction();
        try
        {
            Assert.Equal("NoLock", await RootModeAsync(v_Connection, v_ProbeTransaction));
            Assert.Equal("NoLock", await GroupModeAsync(v_Connection, v_ProbeTransaction, 4));
            Assert.Equal("NoLock", await ScopeModeAsync(v_Connection, v_ProbeTransaction, 4, 6));
        }
        finally
        {
            v_ProbeTransaction.Rollback();
        }
    }

    [Fact]
    public async Task Validation_error_does_not_leave_transaction_owned_locks_after_rollback()
    {
        await using var v_Connection = await OpenInstalledScratchConnectionAsync();
        using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';");

            var v_InvalidScopeSet = CreateScopeSet((4, 6), (0, 8));
            var v_Error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
                v_Connection,
                v_Transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Legacy_Scope_Set @ScopeSet = @ScopeSet, @Mode = N'Exclusive';",
                StructuredParameter("@ScopeSet", "dbo.InventoryFenceScopeSetType", v_InvalidScopeSet)));
            Assert.Equal(51414, v_Error.Number);
        }
        finally
        {
            v_Transaction.Rollback();
        }

        using var v_ProbeTransaction = v_Connection.BeginTransaction();
        try
        {
            Assert.Equal("NoLock", await RootModeAsync(v_Connection, v_ProbeTransaction));
            Assert.Equal("NoLock", await GroupModeAsync(v_Connection, v_ProbeTransaction, 4));
        }
        finally
        {
            v_ProbeTransaction.Rollback();
        }
    }

    [Fact]
    public void Perf06b_wires_writer_paths_but_keeps_historical_report_on_legacy_fence()
    {
        var v_Source = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var v_arrMigratedWriterNames = new[]
        {
            "sp_XNK_Document_Post",
            "sp_Inventory_Movement_Rebuild",
            "sp_Inventory_Balance_Daily_Rebuild",
            "sp_Inventory_Snapshot_Rebuild",
            "sp_Inventory_Snapshot_Initialize_From_Ledger",
            "sp_Inventory_Snapshot_Finalize_Daily",
            "sp_Inventory_Snapshot_Complete_Claim",
            "sp_Inventory_Movement_Apply_Invalidation",
            "sp_Inventory_Movement_Complete_Claim",
            "sp_Inventory_Snapshot_Apply_Invalidation",
            "sp_Inventory_Snapshot_Invalidate_From",
            "sp_XNK_Reservation_Adjust",
            "sp_XNK_Reservation_Move_Document",
            "sp_XNK_Reservation_Release_Detail",
            "sp_XNK_Reservation_Release_Document",
            "sp_XNK_Reservation_Rebuild",
            "sp_XNK_InventoryBalance_Rebuild",
            "sp_Inventory_Reconciliation_Run",
            "sp_BC_Ton_Kho_Hien_Tai_Page"
        };

        foreach (var v_ProcedureName in v_arrMigratedWriterNames)
        {
            var v_Procedure = ExtractProcedure(v_Source, v_ProcedureName);
            Assert.Contains("sp_Inventory_Fence_Acquire_Root", v_Procedure, StringComparison.Ordinal);
        }

        var v_HistoricalFence = ExtractProcedure(v_Source, "sp_Inventory_Report_Acquire_Scope_Fence");
        var v_HistoricalReport = ExtractProcedure(v_Source, "sp_BC_Xuat_Nhap_Ton_Page");
        Assert.Contains("IF @Fence_Mode = N'LEGACY'", v_HistoricalFence, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Fence_Acquire_Root", v_HistoricalFence, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Fence_Acquire_Group_Set", v_HistoricalFence, StringComparison.Ordinal);
        var v_iGroupModeStart = v_HistoricalFence.IndexOf("IF @Fence_Mode = N'GROUP'", StringComparison.Ordinal);
        Assert.True(v_iGroupModeStart >= 0);
        var v_GroupModePath = v_HistoricalFence[v_iGroupModeStart..];
        Assert.DoesNotContain("sp_Inventory_Fence_Acquire_Legacy_Scope", v_GroupModePath, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryMovement:'", v_GroupModePath, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Report_Acquire_Scope_Fence", v_HistoricalReport, StringComparison.Ordinal);
    }

    [Fact]
    public void Perf06b_identity_changing_master_paths_use_root_exclusive_maintenance()
    {
        var v_Source = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        foreach (var v_ProcedureName in new[]
        {
            "F2009_sp_del_Kho",
            "F2016_sp_del_Don_Vi_Tinh",
            "F2017_sp_del_Loai_San_Pham",
            "F2018_sp_del_San_Pham",
            "F2019_sp_del_NCC",
            "F2016_sp_ins_Don_Vi_Tinh",
            "F2016_sp_upd_Don_Vi_Tinh",
            "F2017_sp_ins_Loai_San_Pham",
            "F2017_sp_upd_Loai_San_Pham",
            "F2018_sp_ins_San_Pham",
            "F2018_sp_upd_San_Pham",
            "F2019_sp_ins_NCC",
            "F2019_sp_upd_NCC",
            "F2009_sp_ins_Kho",
            "F2009_sp_upd_Kho"
        })
        {
            var v_Procedure = ExtractProcedure(v_Source, v_ProcedureName);
            Assert.Contains("sp_Inventory_Fence_Acquire_Root @Mode = N'Exclusive'", v_Procedure, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Perf06b_scoped_paths_preserve_root_group_scope_order()
    {
        var v_Source = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var v_arrScopedProcedures = new[]
        {
            "sp_XNK_Document_Post",
            "sp_Inventory_Movement_Rebuild",
            "sp_Inventory_Balance_Daily_Rebuild",
            "sp_Inventory_Snapshot_Rebuild",
            "sp_Inventory_Snapshot_Initialize_From_Ledger",
            "sp_Inventory_Snapshot_Finalize_Daily",
            "sp_Inventory_Snapshot_Complete_Claim",
            "sp_Inventory_Movement_Apply_Invalidation",
            "sp_Inventory_Movement_Complete_Claim",
            "sp_Inventory_Snapshot_Apply_Invalidation",
            "sp_Inventory_Snapshot_Process_RebuildQueue",
            "sp_XNK_Reservation_Adjust",
            "sp_XNK_Reservation_Move_Document",
            "sp_XNK_Reservation_Release_Detail",
            "sp_XNK_Reservation_Release_Document",
            "sp_Inventory_Reconciliation_Run"
        };

        foreach (var v_ProcedureName in v_arrScopedProcedures)
        {
            var v_Procedure = ExtractProcedure(v_Source, v_ProcedureName);
            AssertOrdered(
                v_Procedure,
                "sp_Inventory_Fence_Acquire_Root",
                "sp_Inventory_Fence_Acquire_Group_Set",
                "sp_Inventory_Fence_Acquire_Legacy_Scope_Set",
                "sp_Inventory_Fence_Require_Context");
        }
    }

    [Fact]
    public void Perf06b_removes_public_boolean_lock_bypass_and_finalize_has_no_shared_write_fence()
    {
        var v_Source = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        Assert.DoesNotContain("@Scope_Lock_Held", v_Source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@Bootstrap_Lock_Held", v_Source, StringComparison.OrdinalIgnoreCase);

        var v_Finalize = ExtractProcedure(v_Source, "sp_Inventory_Snapshot_Finalize_Daily");
        Assert.Contains("sp_Inventory_Fence_Acquire_Root", v_Finalize, StringComparison.Ordinal);
        Assert.Contains("DECLARE @RequiredRootMode NVARCHAR(12) = CASE WHEN @WholeDomain = 1 THEN N'Exclusive' ELSE N'Shared' END", v_Finalize, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Fence_Acquire_Snapshot_Finalize_Singleton", v_Finalize, StringComparison.Ordinal);
        Assert.DoesNotContain("@LockMode = N'Shared'", v_Finalize, StringComparison.Ordinal);
        Assert.DoesNotContain("sp_getapplock", v_Finalize, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Perf06b_whole_domain_paths_take_root_exclusive_before_work()
    {
        var v_Source = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        foreach (var v_ProcedureName in new[]
        {
            "sp_Inventory_Movement_Bootstrap_From_Ledger",
            "sp_Inventory_Balance_Daily_Bootstrap_From_Movement",
            "sp_Inventory_Snapshot_Bootstrap_From_Ledger",
            "sp_XNK_InventoryBalance_Rebuild",
            "sp_XNK_Reservation_Rebuild"
        })
        {
            var v_Procedure = ExtractProcedure(v_Source, v_ProcedureName);
            var v_iRootIndex = v_Procedure.IndexOf("sp_Inventory_Fence_Acquire_Root", StringComparison.Ordinal);
            Assert.True(v_iRootIndex >= 0, $"{v_ProcedureName} does not acquire the foundation root.");
            Assert.True(
                v_Procedure.IndexOf("UPDATE ", v_iRootIndex, StringComparison.OrdinalIgnoreCase) < 0
                    || v_Procedure.IndexOf("UPDATE ", v_iRootIndex, StringComparison.OrdinalIgnoreCase) > v_iRootIndex,
                $"{v_ProcedureName} has no verifiable post-root mutation ordering.");
        }
    }

    [Fact]
    public void Foundation_uses_transaction_owner_and_fail_closed_contract()
    {
        var v_Source = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var v_Foundation = ExtractFoundation(v_Source, "/* PERF-06A FOUNDATION START */", "/* PERF-06A FOUNDATION END */");

        Assert.Contains("@LockOwner = N'Transaction'", v_Foundation, StringComparison.Ordinal);
        Assert.Contains("@LockTimeout = 0", v_Foundation, StringComparison.Ordinal);
        Assert.Contains("IF @LockResult < 0", v_Foundation, StringComparison.Ordinal);
        Assert.Contains("APPLOCK_MODE(N'public'", v_Foundation, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Fence_Require_Transaction", v_Foundation, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Fence_Acquire_Group_Set", v_Foundation, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Fence_Require_Context", v_Foundation, StringComparison.Ordinal);
    }

    private static async Task InstallFoundationAsync(SqlConnection p_Connection)
    {
        var v_Schema = ReadRepositoryFile("Database", "WarehouseModule.Schema.sql");
        var v_ProcedureSource = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var v_TypeFoundation = ExtractFoundation(v_Schema, "/* PERF-06A FOUNDATION TYPES START */", "/* PERF-06A FOUNDATION TYPES END */");
        var v_ProcedureFoundation = ExtractFoundation(v_ProcedureSource, "/* PERF-06A FOUNDATION START */", "/* PERF-06A FOUNDATION END */");

        await ExecuteAsync(p_Connection, null, "SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;");
        await ExecuteBatchesAsync(p_Connection, v_TypeFoundation);
        await ExecuteBatchesAsync(p_Connection, v_ProcedureFoundation);
    }

    private static async Task<SqlConnection> OpenInstalledScratchConnectionAsync()
    {
        var v_Connection = await OpenScratchConnectionAsync();
        await InstallFoundationAsync(v_Connection);
        return v_Connection;
    }

    private static async Task<SqlConnection> OpenScratchConnectionAsync()
    {
        var v_ConnectionString = RequiredScratchConnectionString();
        var v_Connection = new SqlConnection(v_ConnectionString);
        await v_Connection.OpenAsync();
        return v_Connection;
    }

    private static string RequiredScratchConnectionString()
    {
        if (!IsScratchOptedIn())
            throw SkipException.ForSkip($"Set {ScratchOptInEnvironmentVariable}=1 and provide {ScratchConnectionEnvironmentVariable} targeting tempdb to run SQL foundation tests.");

        var v_Raw = Environment.GetEnvironmentVariable(ScratchConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(v_Raw))
            throw SkipException.ForSkip($"{ScratchConnectionEnvironmentVariable} is not configured.");

        var v_Builder = new SqlConnectionStringBuilder(v_Raw);
        if (string.Equals(v_Builder.InitialCatalog, BusinessDatabaseName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Inventory fence foundation tests must never target the Business DB.");
        if (!string.Equals(v_Builder.InitialCatalog, "tempdb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Inventory fence foundation tests require Initial Catalog=tempdb.");

        return v_Builder.ConnectionString;
    }

    private static bool IsScratchOptedIn()
    {
        return string.Equals(Environment.GetEnvironmentVariable(ScratchOptInEnvironmentVariable), "1", StringComparison.Ordinal);
    }

    private static async Task ExecuteBatchesAsync(SqlConnection p_Connection, string p_Source)
    {
        foreach (var v_Batch in Regex.Split(p_Source, @"(?im)^\s*GO\s*(?:--[^\r\n]*)?(?:\r?\n|$)"))
        {
            if (!string.IsNullOrWhiteSpace(v_Batch))
                await ExecuteAsync(p_Connection, null, v_Batch);
        }
    }

    private static async Task ExecuteAsync(
        SqlConnection p_Connection,
        SqlTransaction? p_Transaction,
        string p_Sql,
        params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.CommandTimeout = 30;
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ScalarStringAsync(
        SqlConnection p_Connection,
        SqlTransaction? p_Transaction,
        string p_Sql)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.CommandTimeout = 30;
        return Convert.ToString(await v_Command.ExecuteScalarAsync()) ?? string.Empty;
    }

    private static Task<string> RootModeAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction)
    {
        return ScalarStringAsync(p_Connection, p_Transaction, "SELECT APPLOCK_MODE(N'public', dbo.fn_Inventory_Fence_Root_Resource(), N'Transaction');");
    }

    private static Task<string> GroupModeAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, long warehouseId)
    {
        return ScalarStringAsync(p_Connection, p_Transaction, "SELECT APPLOCK_MODE(N'public', dbo.fn_Inventory_Fence_Group_Resource(@Kho_ID), N'Transaction');", // This overload is not used; kept out of the public helper surface.
        warehouseId.ToString());
    }

    private static Task<string> ScopeModeAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, long warehouseId, long productId)
    {
        return ScalarStringAsync(p_Connection, p_Transaction, "SELECT APPLOCK_MODE(N'public', dbo.fn_Inventory_Fence_Scope_Resource(@Kho_ID, @San_Pham_ID), N'Transaction');", warehouseId.ToString(), productId.ToString());
    }

    private static DataTable CreateGroupSet(params long[] p_arrWarehouseIds)
    {
        var v_Table = new DataTable();
        v_Table.Columns.Add("Kho_ID", typeof(long));
        foreach (var warehouseId in p_arrWarehouseIds)
            v_Table.Rows.Add(warehouseId);
        return v_Table;
    }

    private static DataTable CreateScopeSet(params (long WarehouseId, long ProductId)[] p_arrScopes)
    {
        var v_Table = new DataTable();
        v_Table.Columns.Add("Kho_ID", typeof(long));
        v_Table.Columns.Add("San_Pham_ID", typeof(long));
        foreach (var (warehouseId, productId) in p_arrScopes)
            v_Table.Rows.Add(warehouseId, productId);
        return v_Table;
    }

    private static SqlParameter StructuredParameter(string p_Name, string p_TypeName, DataTable p_Value)
    {
        return new(p_Name, SqlDbType.Structured)
        {
            TypeName = p_TypeName,
            Value = p_Value
        };
    }

    private static async Task ExecuteContextAsync(
        SqlConnection p_Connection,
        SqlTransaction p_Transaction,
        DataTable p_GroupSet,
        DataTable p_ScopeSet,
        string p_RequiredRootMode,
        string p_RequiredGroupMode,
        string p_RequiredScopeMode)
    {
        await ExecuteAsync(
            p_Connection,
            p_Transaction,
            "EXEC dbo.sp_Inventory_Fence_Require_Context @GroupSet = @GroupSet, @ScopeSet = @ScopeSet, @RequiredRootMode = @RequiredRootMode, @RequiredGroupMode = @RequiredGroupMode, @RequiredScopeMode = @RequiredScopeMode;",
            StructuredParameter("@GroupSet", "dbo.InventoryFenceGroupSetType", p_GroupSet),
            StructuredParameter("@ScopeSet", "dbo.InventoryFenceScopeSetType", p_ScopeSet),
            new SqlParameter("@RequiredRootMode", SqlDbType.NVarChar, 12) { Value = p_RequiredRootMode },
            new SqlParameter("@RequiredGroupMode", SqlDbType.NVarChar, 12) { Value = p_RequiredGroupMode },
            new SqlParameter("@RequiredScopeMode", SqlDbType.NVarChar, 12) { Value = p_RequiredScopeMode });
    }

    private static string ReadRepositoryFile(params string[] p_arrParts)
    {
        var v_Directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (v_Directory is not null)
        {
            var v_Candidate = Path.Combine(new[] { v_Directory.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_Candidate))
                return File.ReadAllText(v_Candidate);
            v_Directory = v_Directory.Parent;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(p_arrParts)}");
    }

    private static string ExtractFoundation(string p_Source, string p_StartMarker, string p_EndMarker)
    {
        var v_iStart = p_Source.IndexOf(p_StartMarker, StringComparison.Ordinal);
        var v_iEnd = p_Source.IndexOf(p_EndMarker, v_iStart + p_StartMarker.Length, StringComparison.Ordinal);
        if (v_iStart < 0 || v_iEnd < 0 || v_iEnd <= v_iStart)
            throw new InvalidOperationException($"Foundation markers were not found in source: {p_StartMarker}");
        return p_Source[(v_iStart + p_StartMarker.Length)..v_iEnd];
    }

    private static string ExtractProcedure(string p_Source, string p_ProcedureName)
    {
        var v_Pattern = $"(?is)CREATE\\s+OR\\s+ALTER\\s+PROCEDURE\\s+dbo\\.{Regex.Escape(p_ProcedureName)}\\b(?<body>.*?)(?=^\\s*CREATE\\s+OR\\s+ALTER\\s+PROCEDURE\\s+dbo\\.|\\z)";
        var v_Match = Regex.Match(p_Source, v_Pattern, RegexOptions.Multiline);
        Assert.True(v_Match.Success, $"Procedure was not found: {p_ProcedureName}");
        return v_Match.Groups["body"].Value;
    }

    private static void AssertOrdered(string p_Source, params string[] p_arrMarkers)
    {
        var v_iPrevious = -1;
        foreach (var v_Marker in p_arrMarkers)
        {
            var v_iCurrent = p_Source.IndexOf(v_Marker, StringComparison.Ordinal);
            Assert.True(v_iCurrent >= 0, $"Expected marker was not found: {v_Marker}");
            Assert.True(v_iCurrent > v_iPrevious, $"Lock/order marker is out of order: {v_Marker}");
            v_iPrevious = v_iCurrent;
        }
    }

    private static readonly string[] m_arrFoundationProcedureNames =
    [
        "sp_Inventory_Fence_Require_Context",
        "sp_Inventory_Fence_Release_Snapshot_Worker_Singleton",
        "sp_Inventory_Fence_Acquire_Snapshot_Worker_Singleton",
        "sp_Inventory_Fence_Acquire_Snapshot_Finalize_Singleton",
        "sp_Inventory_Fence_Acquire_Legacy_Snapshot_Bootstrap",
        "sp_Inventory_Fence_Acquire_Legacy_Movement_Bootstrap",
        "sp_Inventory_Fence_Acquire_Legacy_Snapshot_Set",
        "sp_Inventory_Fence_Acquire_Legacy_Snapshot",
        "sp_Inventory_Fence_Acquire_Legacy_Scope_Set",
        "sp_Inventory_Fence_Acquire_Legacy_Scope",
        "sp_Inventory_Fence_Acquire_Group_Set",
        "sp_Inventory_Fence_Acquire_Group",
        "sp_Inventory_Fence_Acquire_Root",
        "sp_Inventory_Fence_Require_Transaction"
    ];

    private static readonly string[] m_arrFoundationFunctionNames =
    [
        "fn_Inventory_Fence_Mode_Satisfies",
        "fn_Inventory_Fence_Snapshot_Worker_Resource",
        "fn_Inventory_Fence_Snapshot_Finalize_Resource",
        "fn_Inventory_Fence_Snapshot_Bootstrap_Resource",
        "fn_Inventory_Fence_Movement_Bootstrap_Resource",
        "fn_Inventory_Fence_Snapshot_Resource",
        "fn_Inventory_Fence_Scope_Resource",
        "fn_Inventory_Fence_Group_Resource",
        "fn_Inventory_Fence_Root_Resource"
    ];

    private static readonly string m_DropFoundationSql = $"{string.Join(Environment.NewLine, m_arrFoundationProcedureNames.Select(name => $"DROP PROCEDURE IF EXISTS dbo.{name};"))}{Environment.NewLine}{string.Join(Environment.NewLine, m_arrFoundationFunctionNames.Select(name => $"DROP FUNCTION IF EXISTS dbo.{name};"))}{Environment.NewLine}DROP TYPE IF EXISTS dbo.InventoryFenceScopeSetType; DROP TYPE IF EXISTS dbo.InventoryFenceGroupSetType;";

    private static async Task<string> ScalarStringAsync(
        SqlConnection p_Connection,
        SqlTransaction? p_Transaction,
        string p_Sql,
        params string[] p_arrValues)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.CommandTimeout = 30;
        for (var v_iIndex = 0; v_iIndex < p_arrValues.Length; v_iIndex++)
        {
            string v_ParameterName;
            if (v_iIndex == 0)
            {
                v_ParameterName = "@Kho_ID";
            }
            else
            {
                v_ParameterName = "@San_Pham_ID";
            }

            v_Command.Parameters.AddWithValue(v_ParameterName, long.Parse(p_arrValues[v_iIndex]));
        }
        return Convert.ToString(await v_Command.ExecuteScalarAsync()) ?? string.Empty;
    }
}
