using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[CollectionDefinition("PERF06D rehearsal", DisableParallelization = true)]
public sealed class Perf06DRehearsalCollection
{
}

[Collection("PERF06D rehearsal")]
public sealed class WarehouseHistoricalGroupModeTests
{
    private const string RehearsalDatabase = "TKS_Thuc_Tap_V11_Inventory_Rehearsal_20260907";
    private const string RehearsalLogin = "thuctap_kho";
    private const string FenceConfigSingletonConstraint = "CK_Inventory_Report_Fence_Config_Singleton";
    private const string FenceConfigModeConstraint = "CK_Inventory_Report_Fence_Config_Mode";
    private static readonly DateTime m_dtmReportFrom = new(2026, 1, 1);
    private static readonly DateTime m_dtmReportTo = new(2026, 9, 4);
    private readonly ITestOutputHelper m_Output;

    public WarehouseHistoricalGroupModeTests(ITestOutputHelper p_Output)
    {
        this.m_Output = p_Output;
    }

    [Fact]
    public void Historical_reader_source_declares_a_reversible_group_contract()
    {
        var v_Schema = ReadRepositoryFile("Database", "WarehouseModule.Schema.sql");
        var v_Procedures = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var v_Helper = ExtractProcedure(v_Procedures, "sp_Inventory_Report_Acquire_Scope_Fence");

        Assert.Contains("Inventory_Report_Fence_Config", v_Schema, StringComparison.Ordinal);
        Assert.Contains("LEGACY", v_Schema, StringComparison.Ordinal);
        Assert.Contains("GROUP", v_Schema, StringComparison.Ordinal);
        Assert.Contains("Inventory_Report_Fence_Config", v_Helper, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Fence_Acquire_Root", v_Helper, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Fence_Acquire_Group_Set", v_Helper, StringComparison.Ordinal);
        Assert.Contains("#ReportScopeCandidate", v_Helper, StringComparison.Ordinal);
        Assert.Contains("#ReportScopeProtected", v_Helper, StringComparison.Ordinal);
        Assert.Contains("EXCEPT", v_Helper, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("51451", v_Helper, StringComparison.Ordinal);
        Assert.DoesNotContain("SESSION_CONTEXT", v_Helper, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@Scope_Lock_Held", v_Helper, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@Bootstrap_Lock_Held", v_Helper, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Group_reader_path_has_no_legacy_scope_acquisition()
    {
        var v_Procedures = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var v_Helper = ExtractProcedure(v_Procedures, "sp_Inventory_Report_Acquire_Scope_Fence");
        var v_iGroupModeStart = v_Helper.IndexOf("@Fence_Mode = N'GROUP'", StringComparison.Ordinal);
        Assert.True(v_iGroupModeStart >= 0, "GROUP mode branch is missing.");
        var v_GroupPath = v_Helper[v_iGroupModeStart..];

        Assert.Contains("sp_Inventory_Fence_Acquire_Group_Set", v_GroupPath, StringComparison.Ordinal);
        Assert.DoesNotContain("sp_Inventory_Fence_Acquire_Legacy_Scope", v_GroupPath, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryMovement:'", v_GroupPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stable_historical_result_is_equal_when_switching_legacy_and_group_modes()
    {
        await using var v_Connection = await OpenRehearsalConnectionAsync();

        try
        {
            await SetModeAsync(v_Connection, "LEGACY");
            var v_Legacy = await ReadPagedReportAsync(v_Connection, m_dtmReportFrom, m_dtmReportTo);

            await SetModeAsync(v_Connection, "GROUP");
            var v_Group = await ReadPagedReportAsync(v_Connection, m_dtmReportFrom, m_dtmReportTo);

            Assert.Equal(v_Legacy.TotalCount, v_Group.TotalCount);
            Assert.Equal(v_Legacy.RowKeys, v_Group.RowKeys);
            m_Output.WriteLine($"PARITY|paged|total={v_Legacy.TotalCount}|rows={v_Legacy.RowKeys.Count}");

            await SetModeAsync(v_Connection, "LEGACY");
            var v_EmptyLegacy = await ReadPagedReportAsync(
                v_Connection,
                new DateTime(2025, 1, 1),
                new DateTime(2025, 1, 2));

            await SetModeAsync(v_Connection, "GROUP");
            var v_EmptyGroup = await ReadPagedReportAsync(
                v_Connection,
                new DateTime(2025, 1, 1),
                new DateTime(2025, 1, 2));

            Assert.Equal(0, v_EmptyLegacy.TotalCount);
            Assert.Equal(v_EmptyLegacy.TotalCount, v_EmptyGroup.TotalCount);
            Assert.Equal(v_EmptyLegacy.RowKeys, v_EmptyGroup.RowKeys);
            m_Output.WriteLine("PARITY|paged-empty|total=0|rows=0");
        }
        finally
        {
            await SetModeAsync(v_Connection, "LEGACY");
        }
    }

    [Fact]
    public async Task Stable_nonpaged_historical_result_is_equal_when_switching_modes()
    {
        await using var v_Connection = await OpenRehearsalConnectionAsync();

        try
        {
            await SetModeAsync(v_Connection, "LEGACY");
            var v_arrLegacy = await ReadNonPagedReportAsync(v_Connection, m_dtmReportFrom, m_dtmReportTo);

            await SetModeAsync(v_Connection, "GROUP");
            var v_arrGroup = await ReadNonPagedReportAsync(v_Connection, m_dtmReportFrom, m_dtmReportTo);

            Assert.Equal(v_arrLegacy, v_arrGroup);
            m_Output.WriteLine($"PARITY|nonpaged|rows={v_arrLegacy.Count}");
        }
        finally
        {
            await SetModeAsync(v_Connection, "LEGACY");
        }
    }

    [Fact]
    public async Task Group_report_holds_one_root_and_sorted_distinct_group_locks_without_scope_locks()
    {
        await using var v_ReportConnection = await OpenRehearsalConnectionAsync();
        await using var v_ObserverConnection = await OpenRehearsalConnectionAsync();
        await SetModeAsync(v_ReportConnection, "GROUP");

        var v_iReportSessionId = await ReadSessionIdAsync(v_ReportConnection);
        await using var v_Transaction = v_ReportConnection.BeginTransaction(IsolationLevel.ReadCommitted);

        try
        {
            await AcquireReportFenceAsync(v_ReportConnection, v_Transaction);
            var v_arrLocks = await ReadApplicationLocksAsync(v_ObserverConnection, v_iReportSessionId);
            var v_arrExpectedGroups = await ReadExpectedGroupsAsync(v_ObserverConnection);

            Assert.Contains(v_arrLocks, item => item.Resource == "InventoryMovementGroup:Root");
            var v_arrGroupResources = v_arrLocks
                .Where(item => item.Resource.StartsWith("InventoryMovementGroup:", StringComparison.Ordinal)
                    && !item.Resource.Equals("InventoryMovementGroup:Root", StringComparison.Ordinal))
                .Select(item => item.Resource)
                .ToArray();
            var v_arrExpectedResources = v_arrExpectedGroups
                .Select(id => $"InventoryMovementGroup:{id.ToString(CultureInfo.InvariantCulture)}")
                .ToArray();

            Assert.Equal(v_arrExpectedResources, v_arrGroupResources.OrderBy(value => value, StringComparer.Ordinal).ToArray());
            Assert.All(v_arrLocks, item => Assert.Equal("Transaction", item.Owner, ignoreCase: true));
            Assert.All(v_arrLocks, item => Assert.Equal("Shared", item.Mode, ignoreCase: true));
            Assert.DoesNotContain(v_arrLocks, item => item.Resource.StartsWith("InventoryMovement:", StringComparison.Ordinal));
            m_Output.WriteLine($"LOCKS|mode=GROUP|root={v_arrLocks.Count(item => item.Resource == "InventoryMovementGroup:Root")}|groups={string.Join(',', v_arrGroupResources)}|scopeLocks={v_arrLocks.Count(item => item.Resource.StartsWith("InventoryMovement:", StringComparison.Ordinal))}|owner=Transaction|mode=Shared");
        }
        finally
        {
            await v_Transaction.RollbackAsync();
            await SetModeAsync(v_ReportConnection, "LEGACY");
        }

        Assert.Equal(0, await CountApplicationLocksAsync(v_ObserverConnection, v_iReportSessionId));
        Assert.Equal(0, await CountSessionTransactionsAsync(v_ObserverConnection, v_iReportSessionId));
    }

    [Fact]
    public async Task Legacy_report_holds_one_shared_scope_lock_per_distinct_scope()
    {
        await using var v_ReportConnection = await OpenRehearsalConnectionAsync();
        await using var v_ObserverConnection = await OpenRehearsalConnectionAsync();
        await SetModeAsync(v_ReportConnection, "LEGACY");

        var v_iReportSessionId = await ReadSessionIdAsync(v_ReportConnection);
        await using var v_Transaction = v_ReportConnection.BeginTransaction(IsolationLevel.ReadCommitted);

        try
        {
            await AcquireReportFenceAsync(v_ReportConnection, v_Transaction);
            var v_arrLocks = await ReadApplicationLocksAsync(v_ObserverConnection, v_iReportSessionId);
            var v_iExpectedScopeCount = await ReadExpectedScopeCountAsync(v_ObserverConnection);
            var v_arrScopeLocks = v_arrLocks
                .Where(item => item.Resource.StartsWith("InventoryMovement:", StringComparison.Ordinal))
                .ToArray();

            Assert.Equal(v_iExpectedScopeCount, v_arrScopeLocks.Length);
            Assert.DoesNotContain(v_arrLocks, item => item.Resource.StartsWith("InventoryMovementGroup:", StringComparison.Ordinal));
            Assert.All(v_arrScopeLocks, item => Assert.Equal("Transaction", item.Owner, ignoreCase: true));
            Assert.All(v_arrScopeLocks, item => Assert.Equal("Shared", item.Mode, ignoreCase: true));
            m_Output.WriteLine($"LOCKS|mode=LEGACY|scopeLocks={v_arrScopeLocks.Length}|groups={v_arrLocks.Count(item => item.Resource.StartsWith("InventoryMovementGroup:", StringComparison.Ordinal))}|owner=Transaction|mode=Shared");
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }

        Assert.Equal(0, await CountApplicationLocksAsync(v_ObserverConnection, v_iReportSessionId));
        Assert.Equal(0, await CountSessionTransactionsAsync(v_ObserverConnection, v_iReportSessionId));
    }

    [Fact]
    public async Task New_warehouse_after_group_discovery_fails_closed_without_late_group_acquisition()
    {
        await RunStabilizationRaceAsync(
            m_Output,
            "new-warehouse",
            new DateTime(2026, 9, 4),
            m_dtmReportFrom,
            m_dtmReportTo);
    }

    [Fact]
    public async Task Empty_range_to_non_empty_race_fails_closed()
    {
        await RunStabilizationRaceAsync(
            m_Output,
            "empty-to-non-empty",
            new DateTime(2025, 1, 2),
            new DateTime(2025, 1, 1),
            new DateTime(2025, 1, 2));
    }

    [Fact]
    public async Task Group_conflict_fails_closed_and_public_report_cleans_its_transaction()
    {
        await using var v_HolderConnection = await OpenRehearsalConnectionAsync();
        await using var v_ReportConnection = await OpenRehearsalConnectionAsync();
        await using var v_ObserverConnection = await OpenRehearsalConnectionAsync();
        await SetModeAsync(v_ReportConnection, "GROUP");

        var v_iReportSessionId = await ReadSessionIdAsync(v_ReportConnection);
        await using var v_HolderTransaction = v_HolderConnection.BeginTransaction(IsolationLevel.ReadCommitted);

        try
        {
            await ExecuteProcedureAsync(v_HolderConnection, v_HolderTransaction, "dbo.sp_Inventory_Fence_Acquire_Root",
                new SqlParameter("@Mode", SqlDbType.NVarChar, 12) { Value = "Shared" });
            await ExecuteProcedureAsync(v_HolderConnection, v_HolderTransaction, "dbo.sp_Inventory_Fence_Acquire_Group",
                new SqlParameter("@Kho_ID", SqlDbType.BigInt) { Value = 175L },
                new SqlParameter("@Mode", SqlDbType.NVarChar, 12) { Value = "Exclusive" });

            var v_Error = await Assert.ThrowsAsync<SqlException>(() =>
                ReadPagedReportAsync(v_ReportConnection, m_dtmReportFrom, m_dtmReportTo));
            Assert.Equal(51407, v_Error.Number);
            Assert.Contains("Inventory fence Group acquisition failed", v_Error.Message, StringComparison.Ordinal);
            Assert.Equal(0, await CountApplicationLocksAsync(v_ObserverConnection, v_iReportSessionId));
            Assert.Equal(0, await CountSessionTransactionsAsync(v_ObserverConnection, v_iReportSessionId));
            m_Output.WriteLine("CONFLICT|mode=GROUP|error=51407|reportLocks=0|reportTransactions=0");
        }
        finally
        {
            await v_HolderTransaction.RollbackAsync();
            await SetModeAsync(v_ReportConnection, "LEGACY");
        }

        await SetModeAsync(v_ReportConnection, "GROUP");
        var v_AfterRelease = await ReadPagedReportAsync(v_ReportConnection, m_dtmReportFrom, m_dtmReportTo);
        Assert.Equal(414, v_AfterRelease.TotalCount);
        m_Output.WriteLine($"CONFLICT|afterRelease=SUCCESS|total={v_AfterRelease.TotalCount}");
        await SetModeAsync(v_ReportConnection, "LEGACY");
    }

    [Fact]
    public async Task Missing_fence_config_fails_closed_with_51450_and_restores_state()
    {
        await using var v_Connection = await OpenRehearsalConnectionAsync();
        var v_iSessionId = await ReadSessionIdAsync(v_Connection);
        var v_Baseline = await ReadFenceConfigBaselineAsync(v_Connection);
        AssertCanonicalFenceConfig(v_Baseline);

        try
        {
            await ExecuteAsync(v_Connection, null,
                "DELETE FROM dbo.Inventory_Report_Fence_Config;");

            await AssertFenceConfigurationFailureAsync(
                m_Output,
                "missing-row",
                v_Connection,
                v_iSessionId,
                "Historical report fence mode configuration is invalid.");
        }
        finally
        {
            await RestoreFenceConfigAsync(v_Connection, v_Baseline);
            await AssertFenceConfigRestoredAsync(v_Connection, v_Baseline);
            await AssertFenceSessionCleanAsync(v_Connection, v_iSessionId);
        }
    }

    [Fact]
    public async Task Invalid_fence_config_value_fails_closed_with_51450_and_restores_state()
    {
        await using var v_Connection = await OpenRehearsalConnectionAsync();
        var v_iSessionId = await ReadSessionIdAsync(v_Connection);
        var v_Baseline = await ReadFenceConfigBaselineAsync(v_Connection);
        AssertCanonicalFenceConfig(v_Baseline);

        try
        {
            await ExecuteAsync(v_Connection, null,
                $"ALTER TABLE dbo.Inventory_Report_Fence_Config NOCHECK CONSTRAINT {FenceConfigModeConstraint};");
            await ExecuteAsync(v_Connection, null,
                "UPDATE dbo.Inventory_Report_Fence_Config SET Historical_Report_Mode = N'INVALID', UpdatedAt = SYSUTCDATETIME() WHERE Config_ID = 1;");

            await AssertFenceConfigurationFailureAsync(
                m_Output,
                "invalid-value",
                v_Connection,
                v_iSessionId,
                "Historical report fence mode configuration is invalid.");
        }
        finally
        {
            await RestoreFenceConfigAsync(v_Connection, v_Baseline);
            await AssertFenceConfigRestoredAsync(v_Connection, v_Baseline);
            await AssertFenceSessionCleanAsync(v_Connection, v_iSessionId);
        }
    }

    [Fact]
    public async Task Unknown_fence_config_mode_fails_closed_with_51450_and_restores_state()
    {
        await using var v_Connection = await OpenRehearsalConnectionAsync();
        var v_iSessionId = await ReadSessionIdAsync(v_Connection);
        var v_Baseline = await ReadFenceConfigBaselineAsync(v_Connection);
        AssertCanonicalFenceConfig(v_Baseline);

        try
        {
            await ExecuteAsync(v_Connection, null,
                $"ALTER TABLE dbo.Inventory_Report_Fence_Config NOCHECK CONSTRAINT {FenceConfigModeConstraint};");
            await ExecuteAsync(v_Connection, null,
                "UPDATE dbo.Inventory_Report_Fence_Config SET Historical_Report_Mode = N'UNKNOWN', UpdatedAt = SYSUTCDATETIME() WHERE Config_ID = 1;");

            await AssertFenceConfigurationFailureAsync(
                m_Output,
                "unknown-mode",
                v_Connection,
                v_iSessionId,
                "Historical report fence mode configuration is invalid.");
        }
        finally
        {
            await RestoreFenceConfigAsync(v_Connection, v_Baseline);
            await AssertFenceConfigRestoredAsync(v_Connection, v_Baseline);
            await AssertFenceSessionCleanAsync(v_Connection, v_iSessionId);
        }
    }

    [Fact]
    public async Task Duplicate_fence_config_cardinality_fails_closed_with_51450_and_restores_state()
    {
        await using var v_Connection = await OpenRehearsalConnectionAsync();
        var v_iSessionId = await ReadSessionIdAsync(v_Connection);
        var v_Baseline = await ReadFenceConfigBaselineAsync(v_Connection);
        AssertCanonicalFenceConfig(v_Baseline);

        try
        {
            await ExecuteAsync(v_Connection, null,
                $"ALTER TABLE dbo.Inventory_Report_Fence_Config NOCHECK CONSTRAINT {FenceConfigSingletonConstraint};");
            await ExecuteAsync(v_Connection, null,
                "INSERT dbo.Inventory_Report_Fence_Config(Config_ID, Historical_Report_Mode) VALUES (2, N'LEGACY');");

            await AssertFenceConfigurationFailureAsync(
                m_Output,
                "duplicate-cardinality",
                v_Connection,
                v_iSessionId,
                "Historical report fence mode configuration is invalid.");
        }
        finally
        {
            await RestoreFenceConfigAsync(v_Connection, v_Baseline);
            await AssertFenceConfigRestoredAsync(v_Connection, v_Baseline);
            await AssertFenceSessionCleanAsync(v_Connection, v_iSessionId);
        }
    }

    [Fact]
    public void Group_mode_source_contract_contains_explicit_race_and_final_validation_guards()
    {
        var v_Procedures = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var v_Helper = ExtractProcedure(v_Procedures, "sp_Inventory_Report_Acquire_Scope_Fence");
        var v_iGroupStart = v_Helper.IndexOf("IF @Fence_Mode = N'GROUP'", StringComparison.Ordinal);
        var v_iProtectedStart = v_Helper.IndexOf("#ReportFenceStateProtected", v_iGroupStart, StringComparison.Ordinal);
        var v_iComparisonStart = v_Helper.IndexOf("IF EXISTS", v_iProtectedStart, StringComparison.Ordinal);

        Assert.True(v_iGroupStart >= 0);
        Assert.True(v_iProtectedStart > v_iGroupStart);
        Assert.True(v_iComparisonStart > v_iProtectedStart);
        Assert.Contains("#ReportScopeCandidate", v_Helper, StringComparison.Ordinal);
        Assert.Contains("#ReportScopeProtected", v_Helper, StringComparison.Ordinal);
        Assert.Contains("51451", v_Helper, StringComparison.Ordinal);

        var v_Page = ExtractProcedure(v_Procedures, "sp_BC_Xuat_Nhap_Ton_Page");
        Assert.Contains("@Current_Catalog_Scope_Count", v_Page, StringComparison.Ordinal);
        Assert.Contains("@Current_Catalog_Max_ID", v_Page, StringComparison.Ordinal);
        Assert.Contains("THROW 51324", v_Page, StringComparison.Ordinal);
    }

    private static async Task<SqlConnection> OpenRehearsalConnectionAsync()
    {
        var v_ConnectionString = Environment.GetEnvironmentVariable("TKS_PERF06D_REHEARSAL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(v_ConnectionString))
            throw SkipException.ForSkip("Set TKS_PERF06D_REHEARSAL_CONNECTION_STRING to run isolated PERF-06D integration tests.");

        var v_Connection = new SqlConnection(v_ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Command = new SqlCommand("SELECT DB_NAME();", v_Connection);
        var v_DatabaseName = Convert.ToString(await v_Command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        Assert.Equal(RehearsalDatabase, v_DatabaseName);
        Assert.NotEqual("TKS_Thuc_Tap_V11_GiaiDoan2", v_DatabaseName);
        return v_Connection;
    }

    private static async Task SetModeAsync(SqlConnection p_Connection, string p_Mode)
    {
        await using var v_Command = new SqlCommand(
            "UPDATE dbo.Inventory_Report_Fence_Config SET Historical_Report_Mode = @Mode, UpdatedAt = SYSUTCDATETIME() WHERE Config_ID = 1;",
            p_Connection);
        v_Command.Parameters.Add(new SqlParameter("@Mode", SqlDbType.NVarChar, 8) { Value = p_Mode });
        Assert.Equal(1, await v_Command.ExecuteNonQueryAsync());
    }

    private static async Task AssertFenceConfigurationFailureAsync(
        ITestOutputHelper p_Output,
        string p_CaseName,
        SqlConnection p_Connection,
        int p_iSessionId,
        string p_ExpectedMessage)
    {
        var v_Error = await Assert.ThrowsAsync<SqlException>(() =>
            ReadPagedReportAsync(p_Connection, m_dtmReportFrom, m_dtmReportTo));
        Assert.Equal(51450, v_Error.Number);
        Assert.Contains(p_ExpectedMessage, v_Error.Message, StringComparison.Ordinal);
        await AssertFenceSessionCleanAsync(p_Connection, p_iSessionId);
        p_Output.WriteLine($"CONFIG|case={p_CaseName}|error={v_Error.Number}|transactions=0|applocks=0|blocking=0");
    }

    private static async Task<FenceConfigBaseline> ReadFenceConfigBaselineAsync(SqlConnection p_Connection)
    {
        const string v_Sql = """
            SELECT Config_ID, Historical_Report_Mode, UpdatedAt
            FROM dbo.Inventory_Report_Fence_Config
            ORDER BY Config_ID;

            SELECT name,
                   OBJECTPROPERTYEX(object_id, 'CnstIsDisabled'),
                   OBJECTPROPERTYEX(object_id, 'CnstIsNotTrusted')
            FROM sys.check_constraints
            WHERE parent_object_id = OBJECT_ID(N'dbo.Inventory_Report_Fence_Config')
            ORDER BY name;
            """;
        await using var v_Command = new SqlCommand(v_Sql, p_Connection);
        await using var v_Reader = await v_Command.ExecuteReaderAsync();

        var v_arrRows = new List<FenceConfigRow>();
        while (await v_Reader.ReadAsync())
        {
            v_arrRows.Add(new FenceConfigRow(
                Convert.ToByte(v_Reader.GetValue(0), CultureInfo.InvariantCulture),
                v_Reader.GetString(1),
                v_Reader.GetDateTime(2)));
        }

        Assert.True(await v_Reader.NextResultAsync());
        var v_arrConstraints = new List<FenceConstraintState>();
        while (await v_Reader.ReadAsync())
        {
            v_arrConstraints.Add(new FenceConstraintState(
                v_Reader.GetString(0),
                Convert.ToInt32(v_Reader.GetValue(1), CultureInfo.InvariantCulture) != 0,
                Convert.ToInt32(v_Reader.GetValue(2), CultureInfo.InvariantCulture) != 0));
        }

        return new FenceConfigBaseline(v_arrRows, v_arrConstraints);
    }

    private static void AssertCanonicalFenceConfig(FenceConfigBaseline p_Baseline)
    {
        var v_Row = Assert.Single(p_Baseline.Rows);
        Assert.Equal((byte)1, v_Row.ConfigId);
        Assert.Equal("LEGACY", v_Row.Mode);

        Assert.Equal(2, p_Baseline.Constraints.Count);
        Assert.All(p_Baseline.Constraints, constraint =>
        {
            Assert.False(constraint.IsDisabled);
            Assert.False(constraint.IsNotTrusted);
        });
        Assert.Contains(p_Baseline.Constraints, constraint =>
            constraint.Name == FenceConfigSingletonConstraint);
        Assert.Contains(p_Baseline.Constraints, constraint =>
            constraint.Name == FenceConfigModeConstraint);
    }

    private static async Task RestoreFenceConfigAsync(
        SqlConnection p_Connection,
        FenceConfigBaseline p_Baseline)
    {
        var v_Row = Assert.Single(p_Baseline.Rows);
        await ExecuteAsync(p_Connection, null,
            "DELETE FROM dbo.Inventory_Report_Fence_Config WHERE Config_ID <> 1;");
        await ExecuteAsync(
            p_Connection,
            null,
            """
            IF EXISTS (SELECT 1 FROM dbo.Inventory_Report_Fence_Config WHERE Config_ID = 1)
                UPDATE dbo.Inventory_Report_Fence_Config
                   SET Historical_Report_Mode = @Mode,
                       UpdatedAt = @UpdatedAt
                 WHERE Config_ID = 1;
            ELSE
                INSERT dbo.Inventory_Report_Fence_Config(Config_ID, Historical_Report_Mode, UpdatedAt)
                VALUES (1, @Mode, @UpdatedAt);
            """,
            new SqlParameter("@Mode", SqlDbType.NVarChar, 8) { Value = v_Row.Mode },
            new SqlParameter("@UpdatedAt", SqlDbType.DateTime2) { Value = v_Row.UpdatedAt });
        await ExecuteAsync(p_Connection, null,
            $"ALTER TABLE dbo.Inventory_Report_Fence_Config WITH CHECK CHECK CONSTRAINT {FenceConfigSingletonConstraint};");
        await ExecuteAsync(p_Connection, null,
            $"ALTER TABLE dbo.Inventory_Report_Fence_Config WITH CHECK CHECK CONSTRAINT {FenceConfigModeConstraint};");
    }

    private static async Task AssertFenceConfigRestoredAsync(
        SqlConnection p_Connection,
        FenceConfigBaseline p_Baseline)
    {
        var v_Actual = await ReadFenceConfigBaselineAsync(p_Connection);
        Assert.Equal(p_Baseline.Rows, v_Actual.Rows);
        Assert.Equal(p_Baseline.Constraints, v_Actual.Constraints);
    }

    private static async Task AssertFenceSessionCleanAsync(SqlConnection p_Connection, int p_iSessionId)
    {
        Assert.Equal(0, await CountApplicationLocksAsync(p_Connection, p_iSessionId));
        Assert.Equal(0, await CountSessionTransactionsAsync(p_Connection, p_iSessionId));
        Assert.Equal(0, await CountSessionBlockingResidueAsync(p_Connection, p_iSessionId));
    }

    private static async Task SetModeAfterRaceAsync(string p_Mode)
    {
        await using var v_Connection = await OpenRehearsalConnectionAsync();
        await SetModeAsync(v_Connection, p_Mode);
    }

    private static async Task<ReportResult> ReadPagedReportAsync(
        SqlConnection p_Connection,
        DateTime p_dtmFrom,
        DateTime p_dtmTo)
    {
        await using var v_Command = new SqlCommand("dbo.sp_BC_Xuat_Nhap_Ton_Page", p_Connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 120
        };
        v_Command.Parameters.Add(new SqlParameter("@Tu_Ngay", SqlDbType.Date) { Value = p_dtmFrom.Date });
        v_Command.Parameters.Add(new SqlParameter("@Den_Ngay", SqlDbType.Date) { Value = p_dtmTo.Date });
        v_Command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
        v_Command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 1000 });
        v_Command.Parameters.Add(new SqlParameter("@Ma_Dang_Nhap", SqlDbType.NVarChar, 100) { Value = RehearsalLogin });
        v_Command.Parameters.Add(new SqlParameter("@Kho_ID", SqlDbType.BigInt) { Value = DBNull.Value });

        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        var totalCount = Convert.ToInt64(v_Reader.GetValue(0), CultureInfo.InvariantCulture);
        Assert.True(await v_Reader.NextResultAsync());

        var v_arrRows = new List<string>();
        while (await v_Reader.ReadAsync())
        {
            var v_arrValues = Enumerable.Range(0, v_Reader.FieldCount)
                .Select(index => Normalize(v_Reader.GetValue(index)))
                .ToArray();
            v_arrRows.Add(string.Join('\u001f', v_arrValues));
        }

        return new ReportResult(totalCount, v_arrRows);
    }

    private static async Task<IReadOnlyList<string>> ReadNonPagedReportAsync(
        SqlConnection p_Connection,
        DateTime p_dtmFrom,
        DateTime p_dtmTo)
    {
        await using var v_Command = new SqlCommand("dbo.sp_BC_Xuat_Nhap_Ton", p_Connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 120
        };
        v_Command.Parameters.Add(new SqlParameter("@Tu_Ngay", SqlDbType.Date) { Value = p_dtmFrom.Date });
        v_Command.Parameters.Add(new SqlParameter("@Den_Ngay", SqlDbType.Date) { Value = p_dtmTo.Date });
        v_Command.Parameters.Add(new SqlParameter("@Ma_Dang_Nhap", SqlDbType.NVarChar, 100) { Value = RehearsalLogin });
        v_Command.Parameters.Add(new SqlParameter("@Kho_ID", SqlDbType.BigInt) { Value = DBNull.Value });

        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        var v_arrRows = new List<string>();
        while (await v_Reader.ReadAsync())
        {
            var v_arrValues = Enumerable.Range(0, v_Reader.FieldCount)
                .Select(index => Normalize(v_Reader.GetValue(index)))
                .ToArray();
            v_arrRows.Add(string.Join('\u001f', v_arrValues));
        }

        return v_arrRows;
    }

    private static async Task AcquireReportFenceAsync(SqlConnection p_Connection, SqlTransaction p_Transaction)
    {
        await AcquireReportFenceAsync(p_Connection, p_Transaction, m_dtmReportFrom, m_dtmReportTo);
    }

    private static async Task AcquireReportFenceAsync(
        SqlConnection p_Connection,
        SqlTransaction p_Transaction,
        DateTime p_dtmFrom,
        DateTime p_dtmTo)
    {
        await using var v_Command = new SqlCommand("dbo.sp_Inventory_Report_Acquire_Scope_Fence", p_Connection, p_Transaction)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 120
        };
        v_Command.Parameters.Add(new SqlParameter("@Tu_Ngay", SqlDbType.Date) { Value = p_dtmFrom.Date });
        v_Command.Parameters.Add(new SqlParameter("@Den_Ngay", SqlDbType.Date) { Value = p_dtmTo.Date });
        v_Command.Parameters.Add(new SqlParameter("@Ma_Dang_Nhap", SqlDbType.NVarChar, 100) { Value = RehearsalLogin });
        v_Command.Parameters.Add(new SqlParameter("@Kho_ID", SqlDbType.BigInt) { Value = DBNull.Value });
        v_Command.Parameters.Add(new SqlParameter("@Is_Current_Report", SqlDbType.Bit) { Value = false });
        v_Command.Parameters.Add(new SqlParameter("@Catalog_Scope_Count", SqlDbType.BigInt) { Direction = ParameterDirection.Output });
        v_Command.Parameters.Add(new SqlParameter("@Catalog_Max_ID", SqlDbType.BigInt) { Direction = ParameterDirection.Output });
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task RunStabilizationRaceAsync(
        ITestOutputHelper p_Output,
        string p_RaceName,
        DateTime p_dtmReceiptDate,
        DateTime p_dtmReportFrom,
        DateTime p_dtmReportTo)
    {
        var v_Fixture = await CreateRaceFixtureAsync(p_dtmReceiptDate);
        try
        {
            await InstallGroupSetPauseAsync();
            await using var v_ReportConnection = await OpenRehearsalConnectionAsync();
            await SetModeAsync(v_ReportConnection, "GROUP");
            await using var v_Transaction = v_ReportConnection.BeginTransaction(IsolationLevel.ReadCommitted);
            var v_ReportTask = AcquireReportFenceAsync(v_ReportConnection, v_Transaction, p_dtmReportFrom, p_dtmReportTo);

            try
            {
                await WaitForRaceSignalAsync();
                await PostRaceReceiptAsync(v_Fixture);

                var v_Error = await Assert.ThrowsAsync<SqlException>(() => v_ReportTask);
                Assert.Equal(51451, v_Error.Number);
                Assert.Contains("Phạm vi báo cáo thay đổi", v_Error.Message, StringComparison.Ordinal);
                p_Output.WriteLine($"RACE|case={p_RaceName}|error={v_Error.Number}|lateGroupAcquisition=0|staleSuccess=0");
            }
            finally
            {
                if (!v_ReportTask.IsCompleted)
                {
                    try
                    {
                        await v_ReportTask;
                    }
                    catch (SqlException)
                    {
                    }
                }

                await v_Transaction.RollbackAsync();
            }
        }
        finally
        {
            try
            {
                await RestoreGroupSetPauseAsync();
            }
            finally
            {
                try
                {
                    await CleanupRaceFixtureAsync(v_Fixture);
                }
                finally
                {
                    await SetModeAfterRaceAsync("LEGACY");
                }
            }
        }
    }

    private static async Task InstallGroupSetPauseAsync()
    {
        await using var v_Connection = await OpenRehearsalConnectionAsync();
        await ExecuteAsync(
            v_Connection,
            null,
            """
            IF OBJECT_ID(N'dbo.Perf06D_Race_Signal', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Perf06D_Race_Signal
                (
                    Signal_ID TINYINT NOT NULL CONSTRAINT PK_Perf06D_Race_Signal PRIMARY KEY,
                    SignaledAt DATETIME2 NOT NULL
                );
            END;
            DELETE FROM dbo.Perf06D_Race_Signal;
            IF OBJECT_ID(N'dbo.sp_Inventory_Fence_Acquire_Group_Set_Perf06D_Original', N'P') IS NOT NULL
            BEGIN
                IF OBJECT_ID(N'dbo.sp_Inventory_Fence_Acquire_Group_Set', N'P') IS NOT NULL
                    DROP PROCEDURE dbo.sp_Inventory_Fence_Acquire_Group_Set;
                EXEC sys.sp_rename
                    @objname = N'dbo.sp_Inventory_Fence_Acquire_Group_Set_Perf06D_Original',
                    @newname = N'sp_Inventory_Fence_Acquire_Group_Set';
            END;
            EXEC sys.sp_rename
                @objname = N'dbo.sp_Inventory_Fence_Acquire_Group_Set',
                @newname = N'sp_Inventory_Fence_Acquire_Group_Set_Perf06D_Original';
            """);

        await ExecuteAsync(
            v_Connection,
            null,
            """
            CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Fence_Acquire_Group_Set
                @GroupSet dbo.InventoryFenceGroupSetType READONLY,
                @Mode NVARCHAR(12)
            AS
            BEGIN
                SET NOCOUNT ON;
                IF @Mode = N'Shared'
                   AND NOT EXISTS (SELECT 1 FROM dbo.Perf06D_Race_Signal)
                BEGIN
                    INSERT dbo.Perf06D_Race_Signal(Signal_ID, SignaledAt)
                    VALUES (1, SYSUTCDATETIME());
                    WAITFOR DELAY '00:00:08';
                END;

                EXEC dbo.sp_Inventory_Fence_Acquire_Group_Set_Perf06D_Original
                    @GroupSet = @GroupSet,
                    @Mode = @Mode;
            END;
            """);
    }

    private static async Task RestoreGroupSetPauseAsync()
    {
        await using var v_Connection = await OpenRehearsalConnectionAsync();
        await ExecuteAsync(
            v_Connection,
            null,
            """
            IF OBJECT_ID(N'dbo.sp_Inventory_Fence_Acquire_Group_Set', N'P') IS NOT NULL
                DROP PROCEDURE dbo.sp_Inventory_Fence_Acquire_Group_Set;
            IF OBJECT_ID(N'dbo.sp_Inventory_Fence_Acquire_Group_Set_Perf06D_Original', N'P') IS NOT NULL
                EXEC sys.sp_rename
                    @objname = N'dbo.sp_Inventory_Fence_Acquire_Group_Set_Perf06D_Original',
                    @newname = N'sp_Inventory_Fence_Acquire_Group_Set';
            IF OBJECT_ID(N'dbo.Perf06D_Race_Signal', N'U') IS NOT NULL
                DROP TABLE dbo.Perf06D_Race_Signal;
            """);
    }

    private static async Task WaitForRaceSignalAsync()
    {
        await using var v_Connection = await OpenRehearsalConnectionAsync();
        for (var v_iAttempt = 0; v_iAttempt < 100; v_iAttempt++)
        {
            var v_iCount = Convert.ToInt32(
                await ScalarAsync(v_Connection, null, "SELECT COUNT(*) FROM dbo.Perf06D_Race_Signal WITH (READUNCOMMITTED);"),
                CultureInfo.InvariantCulture);
            if (v_iCount == 1)
                return;

            await Task.Delay(100);
        }

        throw new XunitException("The test-only GroupSet pause signal was not observed.");
    }

    private static async Task PostRaceReceiptAsync(RaceFixture p_Fixture)
    {
        await using var v_Connection = await OpenRehearsalConnectionAsync();
        await ExecuteStoredAsync(
            v_Connection,
            null,
            "dbo.sp_XNK_Document_Post",
            new SqlParameter("@Is_Receipt", SqlDbType.Bit) { Value = true },
            BigInt("@Document_ID", p_Fixture.ReceiptId),
            Text("@Ma_Dang_Nhap", RehearsalLogin, 100));
    }

    private static async Task<RaceFixture> CreateRaceFixtureAsync(DateTime p_dtmReceiptDate)
    {
        var v_Tag = $"PERF06D-RACE-{Guid.NewGuid():N}";
        await using var v_Connection = await OpenRehearsalConnectionAsync();
        await using var v_Transaction = v_Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var v_bCommitted = false;

        try
        {
            var supplierId = Convert.ToInt64(
                await ScalarAsync(
                    v_Connection,
                    v_Transaction,
                    "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;"),
                CultureInfo.InvariantCulture);
            var productId = Convert.ToInt64(
                await ScalarAsync(
                    v_Connection,
                    v_Transaction,
                    "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;"),
                CultureInfo.InvariantCulture);
            var warehouseId = await InsertIdAsync(
                v_Connection,
                v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", v_Tag, 255));
            await ExecuteAsync(
                v_Connection,
                v_Transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", RehearsalLogin, 100),
                BigInt("@WarehouseId", warehouseId));
            await v_Transaction.CommitAsync();
            v_bCommitted = true;

            var receiptId = await ExecuteStoredWithOutputAsync(
                v_Connection,
                null,
                "dbo.F2011_sp_ins_Nhap_Kho_Header",
                Text("@So_Phieu_Nhap_Kho", $"{v_Tag}-{Guid.NewGuid():N}", 100),
                BigInt("@Kho_ID", warehouseId),
                BigInt("@NCC_ID", supplierId),
                Date("@Ngay_Nhap_Kho", p_dtmReceiptDate),
                Text("@Ghi_Chu", "", 1000),
                Text("@Ma_Dang_Nhap", RehearsalLogin, 100));
            await ExecuteStoredWithOutputAsync(
                v_Connection,
                null,
                "dbo.F2011_sp_ins_Nhap_Kho_Detail",
                BigInt("@Nhap_Kho_ID", receiptId),
                BigInt("@San_Pham_ID", productId),
                Decimal("@SL_Nhap", 1),
                Decimal("@Don_Gia_Nhap", 1),
                Text("@Ma_Dang_Nhap", RehearsalLogin, 100));

            return new RaceFixture(v_Tag, warehouseId, productId, receiptId);
        }
        catch
        {
            if (!v_bCommitted && v_Transaction.Connection is not null)
                await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CleanupRaceFixtureAsync(RaceFixture p_Fixture)
    {
        await using var v_Connection = await OpenRehearsalConnectionAsync();
        await ExecuteAsync(
            v_Connection,
            null,
            "SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;");
        await using var v_Transaction = v_Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE d FROM dbo.InventorySnapshot_RebuildDeadLetter d JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.InventoryReservation_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.Inventory_Report_Scope_Catalog WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE d FROM dbo.InventoryMovement_RebuildDeadLetter d JOIN dbo.InventoryMovement_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @ReceiptId;",
                BigInt("@ReceiptId", p_Fixture.ReceiptId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @ReceiptId;",
                BigInt("@ReceiptId", p_Fixture.ReceiptId));
            /* Deleting raw detail while the header is still Posted invokes the
               canonical invalidation trigger and may enqueue the scope again.
               Remove those trigger-produced queue rows only after the document
               cleanup, still inside this isolated fixture transaction. */
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE d FROM dbo.InventorySnapshot_RebuildDeadLetter d JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE d FROM dbo.InventoryMovement_RebuildDeadLetter d JOIN dbo.InventoryMovement_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap = @Login AND Kho_ID = @WarehouseId;",
                Text("@Login", RehearsalLogin, 100), BigInt("@WarehouseId", p_Fixture.WarehouseId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId));
            await v_Transaction.CommitAsync();
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<long> ExecuteStoredWithOutputAsync(
        SqlConnection p_Connection,
        SqlTransaction? p_Transaction,
        string p_Procedure,
        params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        var v_Output = p_arrParameters.SingleOrDefault(parameter => parameter.ParameterName == "@Auto_ID");
        v_Output ??= BigInt("@Auto_ID", 0);
        v_Output.Direction = ParameterDirection.InputOutput;
        v_Command.Parameters.Add(v_Output);
        v_Command.Parameters.AddRange(p_arrParameters.Where(parameter => parameter != v_Output).ToArray());
        await v_Command.ExecuteNonQueryAsync();
        return Convert.ToInt64(v_Output.Value, CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteProcedureAsync(
        SqlConnection p_Connection,
        SqlTransaction p_Transaction,
        string p_ProcedureName,
        params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_ProcedureName, p_Connection, p_Transaction)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 30
        };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ReadSessionIdAsync(SqlConnection p_Connection)
    {
        await using var v_Command = new SqlCommand("SELECT @@SPID;", p_Connection);
        return Convert.ToInt32(await v_Command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<LockSnapshot>> ReadApplicationLocksAsync(
        SqlConnection p_Connection,
        int p_iSessionId)
    {
        const string v_Sql = """
            SELECT resource_description, request_mode, request_owner_type
            FROM sys.dm_tran_locks
            WHERE request_session_id = @SessionId
              AND resource_type = N'APPLICATION'
            ORDER BY resource_description;
            """;
        await using var v_Command = new SqlCommand(v_Sql, p_Connection);
        v_Command.Parameters.Add(new SqlParameter("@SessionId", SqlDbType.Int) { Value = p_iSessionId });
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        var v_arrRows = new List<LockSnapshot>();
        while (await v_Reader.ReadAsync())
        {
            var v_ResourceDescription = v_Reader.GetString(0);
            var v_ResourceMatch = Regex.Match(v_ResourceDescription, @"\[(?<resource>[^\]]+)\]", RegexOptions.CultureInvariant);
            string v_Resource;
            if (v_ResourceMatch.Success)
            {
                v_Resource = v_ResourceMatch.Groups["resource"].Value;
            }
            else
            {
                v_Resource = v_ResourceDescription.Trim();
            }

            string v_Mode;
            switch (v_Reader.GetString(1))
            {
                case "S":
                    v_Mode = "Shared";
                    break;

                case "X":
                    v_Mode = "Exclusive";
                    break;

                default:
                    v_Mode = v_Reader.GetString(1);
                    break;
            }
            v_arrRows.Add(new LockSnapshot(v_Resource, v_Mode, v_Reader.GetString(2)));
        }
        return v_arrRows;
    }

    private static async Task<IReadOnlyList<long>> ReadExpectedGroupsAsync(SqlConnection p_Connection)
    {
        const string v_Sql = """
            SELECT DISTINCT c.Kho_ID
            FROM dbo.Inventory_Report_Scope_Catalog c
            JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = c.Kho_ID
            WHERE ku.Ma_Dang_Nhap = @Login
              AND c.First_Posted_Date <= @ReportTo
            ORDER BY c.Kho_ID;
            """;
        await using var v_Command = new SqlCommand(v_Sql, p_Connection);
        v_Command.Parameters.Add(new SqlParameter("@Login", SqlDbType.NVarChar, 100) { Value = RehearsalLogin });
        v_Command.Parameters.Add(new SqlParameter("@ReportTo", SqlDbType.Date) { Value = m_dtmReportTo.Date });
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        var v_arrGroups = new List<long>();
        while (await v_Reader.ReadAsync())
            v_arrGroups.Add(v_Reader.GetInt64(0));
        return v_arrGroups;
    }

    private static async Task<int> ReadExpectedScopeCountAsync(SqlConnection p_Connection)
    {
        const string v_Sql = """
            SELECT COUNT(*)
            FROM
            (
                SELECT s.Kho_ID, s.San_Pham_ID
                FROM dbo.InventoryBalance_Snapshot_Daily s
                JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = s.Kho_ID
                WHERE ku.Ma_Dang_Nhap = @Login
                  AND s.Snapshot_Date < @ReportFrom
                UNION
                SELECT s.Kho_ID, s.San_Pham_ID
                FROM dbo.Inventory_Balance_Daily_Scope s
                JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = s.Kho_ID
                WHERE ku.Ma_Dang_Nhap = @Login
                  AND s.First_Balance_Date <= @ReportTo
                UNION
                SELECT c.Kho_ID, c.San_Pham_ID
                FROM dbo.Inventory_Report_Scope_Catalog c
                JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = c.Kho_ID
                WHERE ku.Ma_Dang_Nhap = @Login
                  AND c.First_Posted_Date <= @ReportTo
            ) scopes;
            """;
        await using var v_Command = new SqlCommand(v_Sql, p_Connection);
        v_Command.Parameters.Add(new SqlParameter("@Login", SqlDbType.NVarChar, 100) { Value = RehearsalLogin });
        v_Command.Parameters.Add(new SqlParameter("@ReportFrom", SqlDbType.Date) { Value = m_dtmReportFrom.Date });
        v_Command.Parameters.Add(new SqlParameter("@ReportTo", SqlDbType.Date) { Value = m_dtmReportTo.Date });
        return Convert.ToInt32(await v_Command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountApplicationLocksAsync(SqlConnection p_Connection, int p_iSessionId)
    {
        await using var v_Command = new SqlCommand(
            "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE request_session_id = @SessionId AND resource_type = N'APPLICATION';",
            p_Connection);
        v_Command.Parameters.Add(new SqlParameter("@SessionId", SqlDbType.Int) { Value = p_iSessionId });
        return Convert.ToInt32(await v_Command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountSessionTransactionsAsync(SqlConnection p_Connection, int p_iSessionId)
    {
        await using var v_Command = new SqlCommand(
            "SELECT COUNT(*) FROM sys.dm_tran_session_transactions WHERE session_id = @SessionId;",
            p_Connection);
        v_Command.Parameters.Add(new SqlParameter("@SessionId", SqlDbType.Int) { Value = p_iSessionId });
        return Convert.ToInt32(await v_Command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountSessionBlockingResidueAsync(SqlConnection p_Connection, int p_iSessionId)
    {
        await using var v_Command = new SqlCommand(
            "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE (session_id = @SessionId AND blocking_session_id <> 0) OR blocking_session_id = @SessionId;",
            p_Connection);
        v_Command.Parameters.Add(new SqlParameter("@SessionId", SqlDbType.Int) { Value = p_iSessionId });
        return Convert.ToInt32(await v_Command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<long> InsertIdAsync(
        SqlConnection p_Connection,
        SqlTransaction? p_Transaction,
        string p_Sql,
        params SqlParameter[] p_arrParameters)
    {
        return Convert.ToInt64(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters), CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(
        SqlConnection p_Connection,
        SqlTransaction? p_Transaction,
        string p_Sql,
        params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteStoredAsync(
        SqlConnection p_Connection,
        SqlTransaction? p_Transaction,
        string p_Procedure,
        params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 120
        };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(
        SqlConnection p_Connection,
        SqlTransaction? p_Transaction,
        string p_Sql,
        params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        return await v_Command.ExecuteScalarAsync();
    }

    private static string Normalize(object p_objValue)
    {
        if (p_objValue is DBNull)
            return "<NULL>";
        if (p_objValue is IFormattable v_Formattable)
            return v_Formattable.ToString(null, CultureInfo.InvariantCulture) ?? "<NULL>";
        return Convert.ToString(p_objValue, CultureInfo.InvariantCulture) ?? "<NULL>";
    }

    private sealed record ReportResult(long TotalCount, IReadOnlyList<string> RowKeys);
    private sealed record LockSnapshot(string Resource, string Mode, string Owner);
    private sealed record RaceFixture(string Tag, long WarehouseId, long ProductId, long ReceiptId);
    private sealed record FenceConfigRow(byte ConfigId, string Mode, DateTime UpdatedAt);
    private sealed record FenceConstraintState(string Name, bool IsDisabled, bool IsNotTrusted);
    private sealed record FenceConfigBaseline(
        IReadOnlyList<FenceConfigRow> Rows,
        IReadOnlyList<FenceConstraintState> Constraints);

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

    private static string ExtractProcedure(string p_Source, string p_ProcedureName)
    {
        var v_Pattern = $"(?is)CREATE\\s+OR\\s+ALTER\\s+PROCEDURE\\s+dbo\\.{Regex.Escape(p_ProcedureName)}\\b(?<body>.*?)(?=^\\s*CREATE\\s+OR\\s+ALTER\\s+PROCEDURE\\s+dbo\\.|\\z)";
        var v_Match = Regex.Match(p_Source, v_Pattern, RegexOptions.Multiline);
        Assert.True(v_Match.Success, $"Procedure was not found: {p_ProcedureName}");
        return v_Match.Groups["body"].Value;
    }
}
