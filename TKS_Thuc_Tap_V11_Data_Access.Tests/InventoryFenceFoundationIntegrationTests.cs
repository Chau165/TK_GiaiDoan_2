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

        await using var connection = await OpenScratchConnectionAsync();
        await ExecuteAsync(connection, null, DropFoundationSql);
        await InstallFoundationAsync(connection);
    }

    public async Task DisposeAsync()
    {
        if (!IsScratchOptedIn())
            return;

        await using var connection = await OpenScratchConnectionAsync();
        await ExecuteAsync(connection, null, DropFoundationSql);
    }

    [Fact]
    public void Foundation_schema_uses_duplicate_tolerant_typed_sets()
    {
        var schema = ReadRepositoryFile("Database", "WarehouseModule.Schema.sql");
        var start = schema.IndexOf("/* PERF-06A FOUNDATION TYPES START */", StringComparison.Ordinal);
        var end = schema.IndexOf("/* PERF-06A FOUNDATION TYPES END */", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "PERF-06A schema markers are missing or out of order.");
        var foundation = schema[start..end];
        Assert.Contains("InventoryFenceGroupSetType", foundation, StringComparison.Ordinal);
        Assert.Contains("InventoryFenceScopeSetType", foundation, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIMARY KEY", foundation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Foundation_procedure_source_has_one_canonical_resource_seam()
    {
        var source = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var start = source.IndexOf("/* PERF-06A FOUNDATION START */", StringComparison.Ordinal);
        var end = source.IndexOf("/* PERF-06A FOUNDATION END */", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "PERF-06A procedure markers are missing or out of order.");
        var foundation = source[start..end];

        Assert.Contains("fn_Inventory_Fence_Root_Resource", foundation, StringComparison.Ordinal);
        Assert.Contains("fn_Inventory_Fence_Group_Resource", foundation, StringComparison.Ordinal);
        Assert.Contains("fn_Inventory_Fence_Scope_Resource", foundation, StringComparison.Ordinal);
        Assert.Contains("InventoryMovementGroup:Root", foundation, StringComparison.Ordinal);
        Assert.Contains("InventoryMovementGroup:' + CONVERT", foundation, StringComparison.Ordinal);
        Assert.Contains("InventoryMovement:'", foundation, StringComparison.Ordinal);
        Assert.DoesNotContain("SESSION_CONTEXT(", foundation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@Scope_Lock_Held", foundation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@Bootstrap_Lock_Held", foundation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Canonical_resource_functions_render_decimal_names_and_reject_invalid_ids()
    {
        await using var connection = await OpenInstalledScratchConnectionAsync();

        Assert.Equal(
            "InventoryMovementGroup:Root",
            await ScalarStringAsync(connection, null,
                "SELECT dbo.fn_Inventory_Fence_Root_Resource();"));
        Assert.Equal(
            "InventoryMovementGroup:42",
            await ScalarStringAsync(connection, null,
                "SELECT dbo.fn_Inventory_Fence_Group_Resource(@Kho_ID);",
                "42"));
        Assert.Equal(
            "InventoryMovement:42:9001",
            await ScalarStringAsync(connection, null,
                "SELECT dbo.fn_Inventory_Fence_Scope_Resource(@Kho_ID, @San_Pham_ID);",
                "42",
                "9001"));
        Assert.Equal(
            string.Empty,
            await ScalarStringAsync(connection, null,
                "SELECT dbo.fn_Inventory_Fence_Group_Resource(@Kho_ID);",
                "0"));
    }

    [Fact]
    public async Task Root_shared_acquisition_succeeds_with_transaction_owner()
    {
        await using var connection = await OpenInstalledScratchConnectionAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            await ExecuteAsync(connection, transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';");

            var mode = await ScalarStringAsync(connection, transaction,
                "SELECT APPLOCK_MODE(N'public', dbo.fn_Inventory_Fence_Root_Resource(), N'Transaction');");

            Assert.Equal("Shared", mode);
        }
        finally
        {
            transaction.Rollback();
        }
    }

    [Fact]
    public async Task Root_exclusive_conflict_surfaces_negative_applock_result_as_failure()
    {
        await using var owner = await OpenInstalledScratchConnectionAsync();
        using var ownerTransaction = owner.BeginTransaction();
        await ExecuteAsync(owner, ownerTransaction,
            "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';");

        await using var contender = await OpenScratchConnectionAsync();
        using var contenderTransaction = contender.BeginTransaction();

        try
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
                contender,
                contenderTransaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Exclusive';"));

            Assert.Equal(51403, error.Number);
        }
        finally
        {
            contenderTransaction.Rollback();
            ownerTransaction.Rollback();
        }
    }

    [Fact]
    public async Task Group_and_scope_sets_deduplicate_and_acquire_in_canonical_order()
    {
        await using var connection = await OpenInstalledScratchConnectionAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            await ExecuteAsync(connection, transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';");

            var groupSet = CreateGroupSet(9, 2, 9, 1, 2);
            await ExecuteAsync(
                connection,
                transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Group_Set @GroupSet = @GroupSet, @Mode = N'Exclusive';",
                StructuredParameter("@GroupSet", "dbo.InventoryFenceGroupSetType", groupSet));

            Assert.Equal("Exclusive", await GroupModeAsync(connection, transaction, 1));
            Assert.Equal("Exclusive", await GroupModeAsync(connection, transaction, 2));
            Assert.Equal("Exclusive", await GroupModeAsync(connection, transaction, 9));

            var scopeSet = CreateScopeSet((9, 20), (1, 10), (9, 20), (2, 11));
            await ExecuteAsync(
                connection,
                transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Legacy_Scope_Set @ScopeSet = @ScopeSet, @Mode = N'Shared';",
                StructuredParameter("@ScopeSet", "dbo.InventoryFenceScopeSetType", scopeSet));

            Assert.Equal("Shared", await ScopeModeAsync(connection, transaction, 1, 10));
            Assert.Equal("Shared", await ScopeModeAsync(connection, transaction, 2, 11));
            Assert.Equal("Shared", await ScopeModeAsync(connection, transaction, 9, 20));
        }
        finally
        {
            transaction.Rollback();
        }

        var source = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var foundation = ExtractFoundation(source, "/* PERF-06A FOUNDATION START */", "/* PERF-06A FOUNDATION END */");
        Assert.Contains("GROUP BY Kho_ID\n        ORDER BY Kho_ID", foundation, StringComparison.Ordinal);
        Assert.Contains("GROUP BY Kho_ID, San_Pham_ID\n        ORDER BY Kho_ID, San_Pham_ID", foundation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_identifier_fails_before_any_group_is_acquired()
    {
        await using var connection = await OpenInstalledScratchConnectionAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            await ExecuteAsync(connection, transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';");

            var invalidGroups = CreateGroupSet(8, 0, 7);
            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
                connection,
                transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Group_Set @GroupSet = @GroupSet, @Mode = N'Exclusive';",
                StructuredParameter("@GroupSet", "dbo.InventoryFenceGroupSetType", invalidGroups)));

            Assert.Equal(51409, error.Number);
            Assert.Equal("NoLock", await GroupModeAsync(connection, transaction, 8));
            Assert.Equal("NoLock", await GroupModeAsync(connection, transaction, 7));
        }
        finally
        {
            transaction.Rollback();
        }
    }

    [Fact]
    public async Task Missing_root_and_reverse_scope_order_are_rejected_by_public_api()
    {
        await using var connection = await OpenInstalledScratchConnectionAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            var groupError = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
                connection,
                transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Group @Kho_ID = 4, @Mode = N'Exclusive';"));
            Assert.Equal(51406, groupError.Number);

            var scopeError = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
                connection,
                transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Legacy_Scope @Kho_ID = 4, @San_Pham_ID = 6, @Mode = N'Shared';"));
            Assert.Equal(51406, scopeError.Number);
        }
        finally
        {
            transaction.Rollback();
        }
    }

    [Fact]
    public async Task Acquisition_requires_a_transaction_and_context_requires_same_transaction()
    {
        await using var connection = await OpenInstalledScratchConnectionAsync();

        var noTransactionError = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
            connection,
            null,
            "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';"));
        Assert.Equal(51401, noTransactionError.Number);

        using var transaction = connection.BeginTransaction();
        try
        {
            await ExecuteAsync(connection, transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';");
            await ExecuteAsync(connection, transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Group @Kho_ID = 4, @Mode = N'Shared';");

            await using var otherConnection = await OpenScratchConnectionAsync();
            using var otherTransaction = otherConnection.BeginTransaction();
            try
            {
                var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteContextAsync(
                    otherConnection,
                    otherTransaction,
                    CreateGroupSet(4),
                    CreateScopeSet(),
                    "Shared",
                    "Shared",
                    "Shared"));

                Assert.Equal(51419, error.Number);
            }
            finally
            {
                otherTransaction.Rollback();
            }
        }
        finally
        {
            transaction.Rollback();
        }
    }

    [Fact]
    public async Task Session_owned_lock_is_not_accepted_as_transaction_context()
    {
        await using var connection = await OpenInstalledScratchConnectionAsync();

        try
        {
            await ExecuteAsync(connection, null, "DECLARE @Result INT; EXEC @Result = sys.sp_getapplock @Resource = N'InventoryMovementGroup:Root', @LockMode = N'Shared', @LockOwner = N'Session', @LockTimeout = 0, @DbPrincipal = N'public'; IF @Result < 0 THROW 51490, N'Unable to prepare session-owned lock.', 1;");
            using var transaction = connection.BeginTransaction();

            try
            {
                var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteContextAsync(
                    connection,
                    transaction,
                    CreateGroupSet(),
                    CreateScopeSet(),
                    "Shared",
                    "Shared",
                    "Shared"));

                Assert.Equal(51419, error.Number);
            }
            finally
            {
                transaction.Rollback();
            }
        }
        finally
        {
            await ExecuteAsync(connection, null, "EXEC sys.sp_releaseapplock @Resource = N'InventoryMovementGroup:Root', @LockOwner = N'Session', @DbPrincipal = N'public';");
        }
    }

    [Fact]
    public async Task Context_rejects_missing_group_and_insufficient_mode()
    {
        await using var connection = await OpenInstalledScratchConnectionAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            await ExecuteAsync(connection, transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';");

            var missingGroupError = await Assert.ThrowsAsync<SqlException>(() => ExecuteContextAsync(
                connection,
                transaction,
                CreateGroupSet(4),
                CreateScopeSet((4, 6)),
                    "Shared",
                    "Shared",
                    "Shared"));
            Assert.Equal(51420, missingGroupError.Number);

            await ExecuteAsync(connection, transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Group @Kho_ID = 4, @Mode = N'Shared';");

            var modeError = await Assert.ThrowsAsync<SqlException>(() => ExecuteContextAsync(
                connection,
                transaction,
                CreateGroupSet(4),
                CreateScopeSet(),
                "Shared",
                "Exclusive",
                "Shared"));
            Assert.Equal(51420, modeError.Number);
        }
        finally
        {
            transaction.Rollback();
        }
    }

    [Fact]
    public async Task Transaction_rollback_releases_root_group_and_scope_locks()
    {
        await using var connection = await OpenInstalledScratchConnectionAsync();
        using (var transaction = connection.BeginTransaction())
        {
            await ExecuteAsync(connection, transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared'; EXEC dbo.sp_Inventory_Fence_Acquire_Legacy_Scope @Kho_ID = 4, @San_Pham_ID = 6, @Mode = N'Exclusive';");
            transaction.Rollback();
        }

        using var probeTransaction = connection.BeginTransaction();
        try
        {
            Assert.Equal("NoLock", await RootModeAsync(connection, probeTransaction));
            Assert.Equal("NoLock", await GroupModeAsync(connection, probeTransaction, 4));
            Assert.Equal("NoLock", await ScopeModeAsync(connection, probeTransaction, 4, 6));
        }
        finally
        {
            probeTransaction.Rollback();
        }
    }

    [Fact]
    public async Task Validation_error_does_not_leave_transaction_owned_locks_after_rollback()
    {
        await using var connection = await OpenInstalledScratchConnectionAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            await ExecuteAsync(connection, transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';");

            var invalidScopeSet = CreateScopeSet((4, 6), (0, 8));
            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
                connection,
                transaction,
                "EXEC dbo.sp_Inventory_Fence_Acquire_Legacy_Scope_Set @ScopeSet = @ScopeSet, @Mode = N'Exclusive';",
                StructuredParameter("@ScopeSet", "dbo.InventoryFenceScopeSetType", invalidScopeSet)));
            Assert.Equal(51414, error.Number);
        }
        finally
        {
            transaction.Rollback();
        }

        using var probeTransaction = connection.BeginTransaction();
        try
        {
            Assert.Equal("NoLock", await RootModeAsync(connection, probeTransaction));
            Assert.Equal("NoLock", await GroupModeAsync(connection, probeTransaction, 4));
        }
        finally
        {
            probeTransaction.Rollback();
        }
    }

    [Fact]
    public void Perf06b_wires_writer_paths_but_keeps_historical_report_on_legacy_fence()
    {
        var source = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var migratedWriterNames = new[]
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

        foreach (var procedureName in migratedWriterNames)
        {
            var procedure = ExtractProcedure(source, procedureName);
            Assert.Contains("sp_Inventory_Fence_Acquire_Root", procedure, StringComparison.Ordinal);
        }

        var historicalFence = ExtractProcedure(source, "sp_Inventory_Report_Acquire_Scope_Fence");
        var historicalReport = ExtractProcedure(source, "sp_BC_Xuat_Nhap_Ton_Page");
        Assert.Contains("IF @Fence_Mode = N'LEGACY'", historicalFence, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Fence_Acquire_Root", historicalFence, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Fence_Acquire_Group_Set", historicalFence, StringComparison.Ordinal);
        var groupModeStart = historicalFence.IndexOf("IF @Fence_Mode = N'GROUP'", StringComparison.Ordinal);
        Assert.True(groupModeStart >= 0);
        var groupModePath = historicalFence[groupModeStart..];
        Assert.DoesNotContain("sp_Inventory_Fence_Acquire_Legacy_Scope", groupModePath, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryMovement:'", groupModePath, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Report_Acquire_Scope_Fence", historicalReport, StringComparison.Ordinal);
    }

    [Fact]
    public void Perf06b_identity_changing_master_paths_use_root_exclusive_maintenance()
    {
        var source = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        foreach (var procedureName in new[]
        {
            "sp_DM_Delete",
            "sp_DM_Don_Vi_Tinh_Save",
            "sp_DM_Loai_San_Pham_Save",
            "sp_DM_San_Pham_Save",
            "sp_DM_NCC_Save",
            "sp_DM_Kho_Save"
        })
        {
            var procedure = ExtractProcedure(source, procedureName);
            Assert.Contains("sp_Inventory_Fence_Acquire_Root @Mode = N'Exclusive'", procedure, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Perf06b_scoped_paths_preserve_root_group_scope_order()
    {
        var source = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var scopedProcedures = new[]
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

        foreach (var procedureName in scopedProcedures)
        {
            var procedure = ExtractProcedure(source, procedureName);
            AssertOrdered(
                procedure,
                "sp_Inventory_Fence_Acquire_Root",
                "sp_Inventory_Fence_Acquire_Group_Set",
                "sp_Inventory_Fence_Acquire_Legacy_Scope_Set",
                "sp_Inventory_Fence_Require_Context");
        }
    }

    [Fact]
    public void Perf06b_removes_public_boolean_lock_bypass_and_finalize_has_no_shared_write_fence()
    {
        var source = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        Assert.DoesNotContain("@Scope_Lock_Held", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@Bootstrap_Lock_Held", source, StringComparison.OrdinalIgnoreCase);

        var finalize = ExtractProcedure(source, "sp_Inventory_Snapshot_Finalize_Daily");
        Assert.Contains("sp_Inventory_Fence_Acquire_Root", finalize, StringComparison.Ordinal);
        Assert.Contains("DECLARE @RequiredRootMode NVARCHAR(12) = CASE WHEN @WholeDomain = 1 THEN N'Exclusive' ELSE N'Shared' END", finalize, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Fence_Acquire_Snapshot_Finalize_Singleton", finalize, StringComparison.Ordinal);
        Assert.DoesNotContain("@LockMode = N'Shared'", finalize, StringComparison.Ordinal);
        Assert.DoesNotContain("sp_getapplock", finalize, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Perf06b_whole_domain_paths_take_root_exclusive_before_work()
    {
        var source = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        foreach (var procedureName in new[]
        {
            "sp_Inventory_Movement_Bootstrap_From_Ledger",
            "sp_Inventory_Balance_Daily_Bootstrap_From_Movement",
            "sp_Inventory_Snapshot_Bootstrap_From_Ledger",
            "sp_XNK_InventoryBalance_Rebuild",
            "sp_XNK_Reservation_Rebuild"
        })
        {
            var procedure = ExtractProcedure(source, procedureName);
            var rootIndex = procedure.IndexOf("sp_Inventory_Fence_Acquire_Root", StringComparison.Ordinal);
            Assert.True(rootIndex >= 0, $"{procedureName} does not acquire the foundation root.");
            Assert.True(
                procedure.IndexOf("UPDATE ", rootIndex, StringComparison.OrdinalIgnoreCase) < 0
                    || procedure.IndexOf("UPDATE ", rootIndex, StringComparison.OrdinalIgnoreCase) > rootIndex,
                $"{procedureName} has no verifiable post-root mutation ordering.");
        }
    }

    [Fact]
    public void Foundation_uses_transaction_owner_and_fail_closed_contract()
    {
        var source = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var foundation = ExtractFoundation(source, "/* PERF-06A FOUNDATION START */", "/* PERF-06A FOUNDATION END */");

        Assert.Contains("@LockOwner = N'Transaction'", foundation, StringComparison.Ordinal);
        Assert.Contains("@LockTimeout = 0", foundation, StringComparison.Ordinal);
        Assert.Contains("IF @LockResult < 0", foundation, StringComparison.Ordinal);
        Assert.Contains("APPLOCK_MODE(N'public'", foundation, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Fence_Require_Transaction", foundation, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Fence_Acquire_Group_Set", foundation, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Fence_Require_Context", foundation, StringComparison.Ordinal);
    }

    private static async Task InstallFoundationAsync(SqlConnection connection)
    {
        var schema = ReadRepositoryFile("Database", "WarehouseModule.Schema.sql");
        var procedureSource = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var typeFoundation = ExtractFoundation(schema, "/* PERF-06A FOUNDATION TYPES START */", "/* PERF-06A FOUNDATION TYPES END */");
        var procedureFoundation = ExtractFoundation(procedureSource, "/* PERF-06A FOUNDATION START */", "/* PERF-06A FOUNDATION END */");

        await ExecuteAsync(connection, null, "SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;");
        await ExecuteBatchesAsync(connection, typeFoundation);
        await ExecuteBatchesAsync(connection, procedureFoundation);
    }

    private static async Task<SqlConnection> OpenInstalledScratchConnectionAsync()
    {
        var connection = await OpenScratchConnectionAsync();
        await InstallFoundationAsync(connection);
        return connection;
    }

    private static async Task<SqlConnection> OpenScratchConnectionAsync()
    {
        var connectionString = RequiredScratchConnectionString();
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static string RequiredScratchConnectionString()
    {
        if (!IsScratchOptedIn())
            throw SkipException.ForSkip($"Set {ScratchOptInEnvironmentVariable}=1 and provide {ScratchConnectionEnvironmentVariable} targeting tempdb to run SQL foundation tests.");

        var raw = Environment.GetEnvironmentVariable(ScratchConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
            throw SkipException.ForSkip($"{ScratchConnectionEnvironmentVariable} is not configured.");

        var builder = new SqlConnectionStringBuilder(raw);
        if (string.Equals(builder.InitialCatalog, BusinessDatabaseName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Inventory fence foundation tests must never target the Business DB.");
        if (!string.Equals(builder.InitialCatalog, "tempdb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Inventory fence foundation tests require Initial Catalog=tempdb.");

        return builder.ConnectionString;
    }

    private static bool IsScratchOptedIn() =>
        string.Equals(
            Environment.GetEnvironmentVariable(ScratchOptInEnvironmentVariable),
            "1",
            StringComparison.Ordinal);

    private static async Task ExecuteBatchesAsync(SqlConnection connection, string source)
    {
        foreach (var batch in Regex.Split(source, @"(?im)^\s*GO\s*(?:--[^\r\n]*)?(?:\r?\n|$)"))
        {
            if (!string.IsNullOrWhiteSpace(batch))
                await ExecuteAsync(connection, null, batch);
        }
    }

    private static async Task ExecuteAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string sql,
        params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.CommandTimeout = 30;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ScalarStringAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string sql)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.CommandTimeout = 30;
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }

    private static Task<string> RootModeAsync(SqlConnection connection, SqlTransaction? transaction) =>
        ScalarStringAsync(connection, transaction,
            "SELECT APPLOCK_MODE(N'public', dbo.fn_Inventory_Fence_Root_Resource(), N'Transaction');");

    private static Task<string> GroupModeAsync(SqlConnection connection, SqlTransaction? transaction, long warehouseId) =>
        ScalarStringAsync(connection, transaction,
            "SELECT APPLOCK_MODE(N'public', dbo.fn_Inventory_Fence_Group_Resource(@Kho_ID), N'Transaction');",
            // This overload is not used; kept out of the public helper surface.
            warehouseId.ToString());

    private static Task<string> ScopeModeAsync(SqlConnection connection, SqlTransaction? transaction, long warehouseId, long productId) =>
        ScalarStringAsync(connection, transaction,
            "SELECT APPLOCK_MODE(N'public', dbo.fn_Inventory_Fence_Scope_Resource(@Kho_ID, @San_Pham_ID), N'Transaction');",
            warehouseId.ToString(),
            productId.ToString());

    private static DataTable CreateGroupSet(params long[] warehouseIds)
    {
        var table = new DataTable();
        table.Columns.Add("Kho_ID", typeof(long));
        foreach (var warehouseId in warehouseIds)
            table.Rows.Add(warehouseId);
        return table;
    }

    private static DataTable CreateScopeSet(params (long WarehouseId, long ProductId)[] scopes)
    {
        var table = new DataTable();
        table.Columns.Add("Kho_ID", typeof(long));
        table.Columns.Add("San_Pham_ID", typeof(long));
        foreach (var (warehouseId, productId) in scopes)
            table.Rows.Add(warehouseId, productId);
        return table;
    }

    private static SqlParameter StructuredParameter(string name, string typeName, DataTable value) =>
        new(name, SqlDbType.Structured)
        {
            TypeName = typeName,
            Value = value
        };

    private static async Task ExecuteContextAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DataTable groupSet,
        DataTable scopeSet,
        string requiredRootMode,
        string requiredGroupMode,
        string requiredScopeMode)
    {
        await ExecuteAsync(
            connection,
            transaction,
            "EXEC dbo.sp_Inventory_Fence_Require_Context @GroupSet = @GroupSet, @ScopeSet = @ScopeSet, @RequiredRootMode = @RequiredRootMode, @RequiredGroupMode = @RequiredGroupMode, @RequiredScopeMode = @RequiredScopeMode;",
            StructuredParameter("@GroupSet", "dbo.InventoryFenceGroupSetType", groupSet),
            StructuredParameter("@ScopeSet", "dbo.InventoryFenceScopeSetType", scopeSet),
            new SqlParameter("@RequiredRootMode", SqlDbType.NVarChar, 12) { Value = requiredRootMode },
            new SqlParameter("@RequiredGroupMode", SqlDbType.NVarChar, 12) { Value = requiredGroupMode },
            new SqlParameter("@RequiredScopeMode", SqlDbType.NVarChar, 12) { Value = requiredScopeMode });
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(parts)}");
    }

    private static string ExtractFoundation(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (start < 0 || end < 0 || end <= start)
            throw new InvalidOperationException($"Foundation markers were not found in source: {startMarker}");
        return source[(start + startMarker.Length)..end];
    }

    private static string ExtractProcedure(string source, string procedureName)
    {
        var pattern = $"(?is)CREATE\\s+OR\\s+ALTER\\s+PROCEDURE\\s+dbo\\.{Regex.Escape(procedureName)}\\b(?<body>.*?)(?=^\\s*CREATE\\s+OR\\s+ALTER\\s+PROCEDURE\\s+dbo\\.|\\z)";
        var match = Regex.Match(source, pattern, RegexOptions.Multiline);
        Assert.True(match.Success, $"Procedure was not found: {procedureName}");
        return match.Groups["body"].Value;
    }

    private static void AssertOrdered(string source, params string[] markers)
    {
        var previous = -1;
        foreach (var marker in markers)
        {
            var current = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(current >= 0, $"Expected marker was not found: {marker}");
            Assert.True(current > previous, $"Lock/order marker is out of order: {marker}");
            previous = current;
        }
    }

    private static readonly string[] FoundationProcedureNames =
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

    private static readonly string[] FoundationFunctionNames =
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

    private static readonly string DropFoundationSql = $"{string.Join(Environment.NewLine, FoundationProcedureNames.Select(name => $"DROP PROCEDURE IF EXISTS dbo.{name};"))}{Environment.NewLine}{string.Join(Environment.NewLine, FoundationFunctionNames.Select(name => $"DROP FUNCTION IF EXISTS dbo.{name};"))}{Environment.NewLine}DROP TYPE IF EXISTS dbo.InventoryFenceScopeSetType; DROP TYPE IF EXISTS dbo.InventoryFenceGroupSetType;";

    private static async Task<string> ScalarStringAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string sql,
        params string[] values)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.CommandTimeout = 30;
        for (var index = 0; index < values.Length; index++)
            command.Parameters.AddWithValue(index == 0 ? "@Kho_ID" : "@San_Pham_ID", long.Parse(values[index]));
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }
}
