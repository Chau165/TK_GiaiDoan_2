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
    private static readonly DateTime ReportFrom = new(2026, 1, 1);
    private static readonly DateTime ReportTo = new(2026, 9, 4);
    private readonly ITestOutputHelper output;

    public WarehouseHistoricalGroupModeTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void Historical_reader_source_declares_a_reversible_group_contract()
    {
        var schema = ReadRepositoryFile("Database", "WarehouseModule.Schema.sql");
        var procedures = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var helper = ExtractProcedure(procedures, "sp_Inventory_Report_Acquire_Scope_Fence");

        Assert.Contains("Inventory_Report_Fence_Config", schema, StringComparison.Ordinal);
        Assert.Contains("LEGACY", schema, StringComparison.Ordinal);
        Assert.Contains("GROUP", schema, StringComparison.Ordinal);
        Assert.Contains("Inventory_Report_Fence_Config", helper, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Fence_Acquire_Root", helper, StringComparison.Ordinal);
        Assert.Contains("sp_Inventory_Fence_Acquire_Group_Set", helper, StringComparison.Ordinal);
        Assert.Contains("#ReportScopeCandidate", helper, StringComparison.Ordinal);
        Assert.Contains("#ReportScopeProtected", helper, StringComparison.Ordinal);
        Assert.Contains("EXCEPT", helper, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("51451", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("SESSION_CONTEXT", helper, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@Scope_Lock_Held", helper, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@Bootstrap_Lock_Held", helper, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Group_reader_path_has_no_legacy_scope_acquisition()
    {
        var procedures = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var helper = ExtractProcedure(procedures, "sp_Inventory_Report_Acquire_Scope_Fence");
        var groupModeStart = helper.IndexOf("@Fence_Mode = N'GROUP'", StringComparison.Ordinal);
        Assert.True(groupModeStart >= 0, "GROUP mode branch is missing.");
        var groupPath = helper[groupModeStart..];

        Assert.Contains("sp_Inventory_Fence_Acquire_Group_Set", groupPath, StringComparison.Ordinal);
        Assert.DoesNotContain("sp_Inventory_Fence_Acquire_Legacy_Scope", groupPath, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryMovement:'", groupPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stable_historical_result_is_equal_when_switching_legacy_and_group_modes()
    {
        await using var connection = await OpenRehearsalConnectionAsync();

        try
        {
            await SetModeAsync(connection, "LEGACY");
            var legacy = await ReadPagedReportAsync(connection, ReportFrom, ReportTo);

            await SetModeAsync(connection, "GROUP");
            var group = await ReadPagedReportAsync(connection, ReportFrom, ReportTo);

            Assert.Equal(legacy.TotalCount, group.TotalCount);
            Assert.Equal(legacy.RowKeys, group.RowKeys);
            output.WriteLine($"PARITY|paged|total={legacy.TotalCount}|rows={legacy.RowKeys.Count}");

            await SetModeAsync(connection, "LEGACY");
            var emptyLegacy = await ReadPagedReportAsync(
                connection,
                new DateTime(2025, 1, 1),
                new DateTime(2025, 1, 2));

            await SetModeAsync(connection, "GROUP");
            var emptyGroup = await ReadPagedReportAsync(
                connection,
                new DateTime(2025, 1, 1),
                new DateTime(2025, 1, 2));

            Assert.Equal(0, emptyLegacy.TotalCount);
            Assert.Equal(emptyLegacy.TotalCount, emptyGroup.TotalCount);
            Assert.Equal(emptyLegacy.RowKeys, emptyGroup.RowKeys);
            output.WriteLine("PARITY|paged-empty|total=0|rows=0");
        }
        finally
        {
            await SetModeAsync(connection, "LEGACY");
        }
    }

    [Fact]
    public async Task Stable_nonpaged_historical_result_is_equal_when_switching_modes()
    {
        await using var connection = await OpenRehearsalConnectionAsync();

        try
        {
            await SetModeAsync(connection, "LEGACY");
            var legacy = await ReadNonPagedReportAsync(connection, ReportFrom, ReportTo);

            await SetModeAsync(connection, "GROUP");
            var group = await ReadNonPagedReportAsync(connection, ReportFrom, ReportTo);

            Assert.Equal(legacy, group);
            output.WriteLine($"PARITY|nonpaged|rows={legacy.Count}");
        }
        finally
        {
            await SetModeAsync(connection, "LEGACY");
        }
    }

    [Fact]
    public async Task Group_report_holds_one_root_and_sorted_distinct_group_locks_without_scope_locks()
    {
        await using var reportConnection = await OpenRehearsalConnectionAsync();
        await using var observerConnection = await OpenRehearsalConnectionAsync();
        await SetModeAsync(reportConnection, "GROUP");

        var reportSessionId = await ReadSessionIdAsync(reportConnection);
        await using var transaction = reportConnection.BeginTransaction(IsolationLevel.ReadCommitted);

        try
        {
            await AcquireReportFenceAsync(reportConnection, transaction);
            var locks = await ReadApplicationLocksAsync(observerConnection, reportSessionId);
            var expectedGroups = await ReadExpectedGroupsAsync(observerConnection);

            Assert.Contains(locks, item => item.Resource == "InventoryMovementGroup:Root");
            var groupResources = locks
                .Where(item => item.Resource.StartsWith("InventoryMovementGroup:", StringComparison.Ordinal)
                    && !item.Resource.Equals("InventoryMovementGroup:Root", StringComparison.Ordinal))
                .Select(item => item.Resource)
                .ToArray();
            var expectedResources = expectedGroups
                .Select(id => $"InventoryMovementGroup:{id.ToString(CultureInfo.InvariantCulture)}")
                .ToArray();

            Assert.Equal(expectedResources, groupResources.OrderBy(value => value, StringComparer.Ordinal).ToArray());
            Assert.All(locks, item => Assert.Equal("Transaction", item.Owner, ignoreCase: true));
            Assert.All(locks, item => Assert.Equal("Shared", item.Mode, ignoreCase: true));
            Assert.DoesNotContain(locks, item => item.Resource.StartsWith("InventoryMovement:", StringComparison.Ordinal));
            output.WriteLine($"LOCKS|mode=GROUP|root={locks.Count(item => item.Resource == "InventoryMovementGroup:Root")}|groups={string.Join(',', groupResources)}|scopeLocks={locks.Count(item => item.Resource.StartsWith("InventoryMovement:", StringComparison.Ordinal))}|owner=Transaction|mode=Shared");
        }
        finally
        {
            await transaction.RollbackAsync();
            await SetModeAsync(reportConnection, "LEGACY");
        }

        Assert.Equal(0, await CountApplicationLocksAsync(observerConnection, reportSessionId));
        Assert.Equal(0, await CountSessionTransactionsAsync(observerConnection, reportSessionId));
    }

    [Fact]
    public async Task Legacy_report_holds_one_shared_scope_lock_per_distinct_scope()
    {
        await using var reportConnection = await OpenRehearsalConnectionAsync();
        await using var observerConnection = await OpenRehearsalConnectionAsync();
        await SetModeAsync(reportConnection, "LEGACY");

        var reportSessionId = await ReadSessionIdAsync(reportConnection);
        await using var transaction = reportConnection.BeginTransaction(IsolationLevel.ReadCommitted);

        try
        {
            await AcquireReportFenceAsync(reportConnection, transaction);
            var locks = await ReadApplicationLocksAsync(observerConnection, reportSessionId);
            var expectedScopeCount = await ReadExpectedScopeCountAsync(observerConnection);
            var scopeLocks = locks
                .Where(item => item.Resource.StartsWith("InventoryMovement:", StringComparison.Ordinal))
                .ToArray();

            Assert.Equal(expectedScopeCount, scopeLocks.Length);
            Assert.DoesNotContain(locks, item => item.Resource.StartsWith("InventoryMovementGroup:", StringComparison.Ordinal));
            Assert.All(scopeLocks, item => Assert.Equal("Transaction", item.Owner, ignoreCase: true));
            Assert.All(scopeLocks, item => Assert.Equal("Shared", item.Mode, ignoreCase: true));
            output.WriteLine($"LOCKS|mode=LEGACY|scopeLocks={scopeLocks.Length}|groups={locks.Count(item => item.Resource.StartsWith("InventoryMovementGroup:", StringComparison.Ordinal))}|owner=Transaction|mode=Shared");
        }
        finally
        {
            await transaction.RollbackAsync();
        }

        Assert.Equal(0, await CountApplicationLocksAsync(observerConnection, reportSessionId));
        Assert.Equal(0, await CountSessionTransactionsAsync(observerConnection, reportSessionId));
    }

    [Fact]
    public async Task New_warehouse_after_group_discovery_fails_closed_without_late_group_acquisition()
    {
        await RunStabilizationRaceAsync(
            output,
            "new-warehouse",
            new DateTime(2026, 9, 4),
            ReportFrom,
            ReportTo);
    }

    [Fact]
    public async Task Empty_range_to_non_empty_race_fails_closed()
    {
        await RunStabilizationRaceAsync(
            output,
            "empty-to-non-empty",
            new DateTime(2025, 1, 2),
            new DateTime(2025, 1, 1),
            new DateTime(2025, 1, 2));
    }

    [Fact]
    public async Task Group_conflict_fails_closed_and_public_report_cleans_its_transaction()
    {
        await using var holderConnection = await OpenRehearsalConnectionAsync();
        await using var reportConnection = await OpenRehearsalConnectionAsync();
        await using var observerConnection = await OpenRehearsalConnectionAsync();
        await SetModeAsync(reportConnection, "GROUP");

        var reportSessionId = await ReadSessionIdAsync(reportConnection);
        await using var holderTransaction = holderConnection.BeginTransaction(IsolationLevel.ReadCommitted);

        try
        {
            await ExecuteProcedureAsync(holderConnection, holderTransaction, "dbo.sp_Inventory_Fence_Acquire_Root",
                new SqlParameter("@Mode", SqlDbType.NVarChar, 12) { Value = "Shared" });
            await ExecuteProcedureAsync(holderConnection, holderTransaction, "dbo.sp_Inventory_Fence_Acquire_Group",
                new SqlParameter("@Kho_ID", SqlDbType.BigInt) { Value = 175L },
                new SqlParameter("@Mode", SqlDbType.NVarChar, 12) { Value = "Exclusive" });

            var error = await Assert.ThrowsAsync<SqlException>(() =>
                ReadPagedReportAsync(reportConnection, ReportFrom, ReportTo));
            Assert.Equal(51407, error.Number);
            Assert.Contains("Inventory fence Group acquisition failed", error.Message, StringComparison.Ordinal);
            Assert.Equal(0, await CountApplicationLocksAsync(observerConnection, reportSessionId));
            Assert.Equal(0, await CountSessionTransactionsAsync(observerConnection, reportSessionId));
            output.WriteLine("CONFLICT|mode=GROUP|error=51407|reportLocks=0|reportTransactions=0");
        }
        finally
        {
            await holderTransaction.RollbackAsync();
            await SetModeAsync(reportConnection, "LEGACY");
        }

        await SetModeAsync(reportConnection, "GROUP");
        var afterRelease = await ReadPagedReportAsync(reportConnection, ReportFrom, ReportTo);
        Assert.Equal(414, afterRelease.TotalCount);
        output.WriteLine($"CONFLICT|afterRelease=SUCCESS|total={afterRelease.TotalCount}");
        await SetModeAsync(reportConnection, "LEGACY");
    }

    [Fact]
    public async Task Missing_fence_config_fails_closed_with_51450_and_restores_state()
    {
        await using var connection = await OpenRehearsalConnectionAsync();
        var sessionId = await ReadSessionIdAsync(connection);
        var baseline = await ReadFenceConfigBaselineAsync(connection);
        AssertCanonicalFenceConfig(baseline);

        try
        {
            await ExecuteAsync(connection, null,
                "DELETE FROM dbo.Inventory_Report_Fence_Config;");

            await AssertFenceConfigurationFailureAsync(
                output,
                "missing-row",
                connection,
                sessionId,
                "Historical report fence mode configuration is invalid.");
        }
        finally
        {
            await RestoreFenceConfigAsync(connection, baseline);
            await AssertFenceConfigRestoredAsync(connection, baseline);
            await AssertFenceSessionCleanAsync(connection, sessionId);
        }
    }

    [Fact]
    public async Task Invalid_fence_config_value_fails_closed_with_51450_and_restores_state()
    {
        await using var connection = await OpenRehearsalConnectionAsync();
        var sessionId = await ReadSessionIdAsync(connection);
        var baseline = await ReadFenceConfigBaselineAsync(connection);
        AssertCanonicalFenceConfig(baseline);

        try
        {
            await ExecuteAsync(connection, null,
                $"ALTER TABLE dbo.Inventory_Report_Fence_Config NOCHECK CONSTRAINT {FenceConfigModeConstraint};");
            await ExecuteAsync(connection, null,
                "UPDATE dbo.Inventory_Report_Fence_Config SET Historical_Report_Mode = N'INVALID', UpdatedAt = SYSUTCDATETIME() WHERE Config_ID = 1;");

            await AssertFenceConfigurationFailureAsync(
                output,
                "invalid-value",
                connection,
                sessionId,
                "Historical report fence mode configuration is invalid.");
        }
        finally
        {
            await RestoreFenceConfigAsync(connection, baseline);
            await AssertFenceConfigRestoredAsync(connection, baseline);
            await AssertFenceSessionCleanAsync(connection, sessionId);
        }
    }

    [Fact]
    public async Task Unknown_fence_config_mode_fails_closed_with_51450_and_restores_state()
    {
        await using var connection = await OpenRehearsalConnectionAsync();
        var sessionId = await ReadSessionIdAsync(connection);
        var baseline = await ReadFenceConfigBaselineAsync(connection);
        AssertCanonicalFenceConfig(baseline);

        try
        {
            await ExecuteAsync(connection, null,
                $"ALTER TABLE dbo.Inventory_Report_Fence_Config NOCHECK CONSTRAINT {FenceConfigModeConstraint};");
            await ExecuteAsync(connection, null,
                "UPDATE dbo.Inventory_Report_Fence_Config SET Historical_Report_Mode = N'UNKNOWN', UpdatedAt = SYSUTCDATETIME() WHERE Config_ID = 1;");

            await AssertFenceConfigurationFailureAsync(
                output,
                "unknown-mode",
                connection,
                sessionId,
                "Historical report fence mode configuration is invalid.");
        }
        finally
        {
            await RestoreFenceConfigAsync(connection, baseline);
            await AssertFenceConfigRestoredAsync(connection, baseline);
            await AssertFenceSessionCleanAsync(connection, sessionId);
        }
    }

    [Fact]
    public async Task Duplicate_fence_config_cardinality_fails_closed_with_51450_and_restores_state()
    {
        await using var connection = await OpenRehearsalConnectionAsync();
        var sessionId = await ReadSessionIdAsync(connection);
        var baseline = await ReadFenceConfigBaselineAsync(connection);
        AssertCanonicalFenceConfig(baseline);

        try
        {
            await ExecuteAsync(connection, null,
                $"ALTER TABLE dbo.Inventory_Report_Fence_Config NOCHECK CONSTRAINT {FenceConfigSingletonConstraint};");
            await ExecuteAsync(connection, null,
                "INSERT dbo.Inventory_Report_Fence_Config(Config_ID, Historical_Report_Mode) VALUES (2, N'LEGACY');");

            await AssertFenceConfigurationFailureAsync(
                output,
                "duplicate-cardinality",
                connection,
                sessionId,
                "Historical report fence mode configuration is invalid.");
        }
        finally
        {
            await RestoreFenceConfigAsync(connection, baseline);
            await AssertFenceConfigRestoredAsync(connection, baseline);
            await AssertFenceSessionCleanAsync(connection, sessionId);
        }
    }

    [Fact]
    public void Group_mode_source_contract_contains_explicit_race_and_final_validation_guards()
    {
        var procedures = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        var helper = ExtractProcedure(procedures, "sp_Inventory_Report_Acquire_Scope_Fence");
        var groupStart = helper.IndexOf("IF @Fence_Mode = N'GROUP'", StringComparison.Ordinal);
        var protectedStart = helper.IndexOf("#ReportFenceStateProtected", groupStart, StringComparison.Ordinal);
        var comparisonStart = helper.IndexOf("IF EXISTS", protectedStart, StringComparison.Ordinal);

        Assert.True(groupStart >= 0);
        Assert.True(protectedStart > groupStart);
        Assert.True(comparisonStart > protectedStart);
        Assert.Contains("#ReportScopeCandidate", helper, StringComparison.Ordinal);
        Assert.Contains("#ReportScopeProtected", helper, StringComparison.Ordinal);
        Assert.Contains("51451", helper, StringComparison.Ordinal);

        var page = ExtractProcedure(procedures, "sp_BC_Xuat_Nhap_Ton_Page");
        Assert.Contains("@Current_Catalog_Scope_Count", page, StringComparison.Ordinal);
        Assert.Contains("@Current_Catalog_Max_ID", page, StringComparison.Ordinal);
        Assert.Contains("THROW 51324", page, StringComparison.Ordinal);
    }

    private static async Task<SqlConnection> OpenRehearsalConnectionAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("TKS_PERF06D_REHEARSAL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw SkipException.ForSkip("Set TKS_PERF06D_REHEARSAL_CONNECTION_STRING to run isolated PERF-06D integration tests.");

        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT DB_NAME();", connection);
        var databaseName = Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        Assert.Equal(RehearsalDatabase, databaseName);
        Assert.NotEqual("TKS_Thuc_Tap_V11_GiaiDoan2", databaseName);
        return connection;
    }

    private static async Task SetModeAsync(SqlConnection connection, string mode)
    {
        await using var command = new SqlCommand(
            "UPDATE dbo.Inventory_Report_Fence_Config SET Historical_Report_Mode = @Mode, UpdatedAt = SYSUTCDATETIME() WHERE Config_ID = 1;",
            connection);
        command.Parameters.Add(new SqlParameter("@Mode", SqlDbType.NVarChar, 8) { Value = mode });
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task AssertFenceConfigurationFailureAsync(
        ITestOutputHelper output,
        string caseName,
        SqlConnection connection,
        int sessionId,
        string expectedMessage)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() =>
            ReadPagedReportAsync(connection, ReportFrom, ReportTo));
        Assert.Equal(51450, error.Number);
        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
        await AssertFenceSessionCleanAsync(connection, sessionId);
        output.WriteLine($"CONFIG|case={caseName}|error={error.Number}|transactions=0|applocks=0|blocking=0");
    }

    private static async Task<FenceConfigBaseline> ReadFenceConfigBaselineAsync(SqlConnection connection)
    {
        const string sql = """
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
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<FenceConfigRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new FenceConfigRow(
                Convert.ToByte(reader.GetValue(0), CultureInfo.InvariantCulture),
                reader.GetString(1),
                reader.GetDateTime(2)));
        }

        Assert.True(await reader.NextResultAsync());
        var constraints = new List<FenceConstraintState>();
        while (await reader.ReadAsync())
        {
            constraints.Add(new FenceConstraintState(
                reader.GetString(0),
                Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture) != 0,
                Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture) != 0));
        }

        return new FenceConfigBaseline(rows, constraints);
    }

    private static void AssertCanonicalFenceConfig(FenceConfigBaseline baseline)
    {
        var row = Assert.Single(baseline.Rows);
        Assert.Equal((byte)1, row.ConfigId);
        Assert.Equal("LEGACY", row.Mode);

        Assert.Equal(2, baseline.Constraints.Count);
        Assert.All(baseline.Constraints, constraint =>
        {
            Assert.False(constraint.IsDisabled);
            Assert.False(constraint.IsNotTrusted);
        });
        Assert.Contains(baseline.Constraints, constraint =>
            constraint.Name == FenceConfigSingletonConstraint);
        Assert.Contains(baseline.Constraints, constraint =>
            constraint.Name == FenceConfigModeConstraint);
    }

    private static async Task RestoreFenceConfigAsync(
        SqlConnection connection,
        FenceConfigBaseline baseline)
    {
        var row = Assert.Single(baseline.Rows);
        await ExecuteAsync(connection, null,
            "DELETE FROM dbo.Inventory_Report_Fence_Config WHERE Config_ID <> 1;");
        await ExecuteAsync(
            connection,
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
            new SqlParameter("@Mode", SqlDbType.NVarChar, 8) { Value = row.Mode },
            new SqlParameter("@UpdatedAt", SqlDbType.DateTime2) { Value = row.UpdatedAt });
        await ExecuteAsync(connection, null,
            $"ALTER TABLE dbo.Inventory_Report_Fence_Config WITH CHECK CHECK CONSTRAINT {FenceConfigSingletonConstraint};");
        await ExecuteAsync(connection, null,
            $"ALTER TABLE dbo.Inventory_Report_Fence_Config WITH CHECK CHECK CONSTRAINT {FenceConfigModeConstraint};");
    }

    private static async Task AssertFenceConfigRestoredAsync(
        SqlConnection connection,
        FenceConfigBaseline baseline)
    {
        var actual = await ReadFenceConfigBaselineAsync(connection);
        Assert.Equal(baseline.Rows, actual.Rows);
        Assert.Equal(baseline.Constraints, actual.Constraints);
    }

    private static async Task AssertFenceSessionCleanAsync(SqlConnection connection, int sessionId)
    {
        Assert.Equal(0, await CountApplicationLocksAsync(connection, sessionId));
        Assert.Equal(0, await CountSessionTransactionsAsync(connection, sessionId));
        Assert.Equal(0, await CountSessionBlockingResidueAsync(connection, sessionId));
    }

    private static async Task SetModeAfterRaceAsync(string mode)
    {
        await using var connection = await OpenRehearsalConnectionAsync();
        await SetModeAsync(connection, mode);
    }

    private static async Task<ReportResult> ReadPagedReportAsync(
        SqlConnection connection,
        DateTime from,
        DateTime to)
    {
        await using var command = new SqlCommand("dbo.sp_BC_Xuat_Nhap_Ton_Page", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 120
        };
        command.Parameters.Add(new SqlParameter("@Tu_Ngay", SqlDbType.Date) { Value = from.Date });
        command.Parameters.Add(new SqlParameter("@Den_Ngay", SqlDbType.Date) { Value = to.Date });
        command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
        command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 1000 });
        command.Parameters.Add(new SqlParameter("@Ma_Dang_Nhap", SqlDbType.NVarChar, 100) { Value = RehearsalLogin });
        command.Parameters.Add(new SqlParameter("@Kho_ID", SqlDbType.BigInt) { Value = DBNull.Value });

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var totalCount = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
        Assert.True(await reader.NextResultAsync());

        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            var values = Enumerable.Range(0, reader.FieldCount)
                .Select(index => Normalize(reader.GetValue(index)))
                .ToArray();
            rows.Add(string.Join('\u001f', values));
        }

        return new ReportResult(totalCount, rows);
    }

    private static async Task<IReadOnlyList<string>> ReadNonPagedReportAsync(
        SqlConnection connection,
        DateTime from,
        DateTime to)
    {
        await using var command = new SqlCommand("dbo.sp_BC_Xuat_Nhap_Ton", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 120
        };
        command.Parameters.Add(new SqlParameter("@Tu_Ngay", SqlDbType.Date) { Value = from.Date });
        command.Parameters.Add(new SqlParameter("@Den_Ngay", SqlDbType.Date) { Value = to.Date });
        command.Parameters.Add(new SqlParameter("@Ma_Dang_Nhap", SqlDbType.NVarChar, 100) { Value = RehearsalLogin });
        command.Parameters.Add(new SqlParameter("@Kho_ID", SqlDbType.BigInt) { Value = DBNull.Value });

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            var values = Enumerable.Range(0, reader.FieldCount)
                .Select(index => Normalize(reader.GetValue(index)))
                .ToArray();
            rows.Add(string.Join('\u001f', values));
        }

        return rows;
    }

    private static async Task AcquireReportFenceAsync(SqlConnection connection, SqlTransaction transaction)
        => await AcquireReportFenceAsync(connection, transaction, ReportFrom, ReportTo);

    private static async Task AcquireReportFenceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DateTime from,
        DateTime to)
    {
        await using var command = new SqlCommand("dbo.sp_Inventory_Report_Acquire_Scope_Fence", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 120
        };
        command.Parameters.Add(new SqlParameter("@Tu_Ngay", SqlDbType.Date) { Value = from.Date });
        command.Parameters.Add(new SqlParameter("@Den_Ngay", SqlDbType.Date) { Value = to.Date });
        command.Parameters.Add(new SqlParameter("@Ma_Dang_Nhap", SqlDbType.NVarChar, 100) { Value = RehearsalLogin });
        command.Parameters.Add(new SqlParameter("@Kho_ID", SqlDbType.BigInt) { Value = DBNull.Value });
        command.Parameters.Add(new SqlParameter("@Is_Current_Report", SqlDbType.Bit) { Value = false });
        command.Parameters.Add(new SqlParameter("@Catalog_Scope_Count", SqlDbType.BigInt) { Direction = ParameterDirection.Output });
        command.Parameters.Add(new SqlParameter("@Catalog_Max_ID", SqlDbType.BigInt) { Direction = ParameterDirection.Output });
        await command.ExecuteNonQueryAsync();
    }

    private static async Task RunStabilizationRaceAsync(
        ITestOutputHelper output,
        string raceName,
        DateTime receiptDate,
        DateTime reportFrom,
        DateTime reportTo)
    {
        var fixture = await CreateRaceFixtureAsync(receiptDate);
        try
        {
            await InstallGroupSetPauseAsync();
            await using var reportConnection = await OpenRehearsalConnectionAsync();
            await SetModeAsync(reportConnection, "GROUP");
            await using var transaction = reportConnection.BeginTransaction(IsolationLevel.ReadCommitted);
            var reportTask = AcquireReportFenceAsync(reportConnection, transaction, reportFrom, reportTo);

            try
            {
                await WaitForRaceSignalAsync();
                await PostRaceReceiptAsync(fixture);

                var error = await Assert.ThrowsAsync<SqlException>(() => reportTask);
                Assert.Equal(51451, error.Number);
                Assert.Contains("Phạm vi báo cáo thay đổi", error.Message, StringComparison.Ordinal);
                output.WriteLine($"RACE|case={raceName}|error={error.Number}|lateGroupAcquisition=0|staleSuccess=0");
            }
            finally
            {
                if (!reportTask.IsCompleted)
                {
                    try
                    {
                        await reportTask;
                    }
                    catch (SqlException)
                    {
                    }
                }

                await transaction.RollbackAsync();
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
                    await CleanupRaceFixtureAsync(fixture);
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
        await using var connection = await OpenRehearsalConnectionAsync();
        await ExecuteAsync(
            connection,
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
            connection,
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
        await using var connection = await OpenRehearsalConnectionAsync();
        await ExecuteAsync(
            connection,
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
        await using var connection = await OpenRehearsalConnectionAsync();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var count = Convert.ToInt32(
                await ScalarAsync(connection, null, "SELECT COUNT(*) FROM dbo.Perf06D_Race_Signal WITH (READUNCOMMITTED);"),
                CultureInfo.InvariantCulture);
            if (count == 1)
                return;

            await Task.Delay(100);
        }

        throw new XunitException("The test-only GroupSet pause signal was not observed.");
    }

    private static async Task PostRaceReceiptAsync(RaceFixture fixture)
    {
        await using var connection = await OpenRehearsalConnectionAsync();
        await ExecuteStoredAsync(
            connection,
            null,
            "dbo.sp_XNK_Document_Post",
            new SqlParameter("@Is_Receipt", SqlDbType.Bit) { Value = true },
            BigInt("@Document_ID", fixture.ReceiptId),
            Text("@Ma_Dang_Nhap", RehearsalLogin, 100));
    }

    private static async Task<RaceFixture> CreateRaceFixtureAsync(DateTime receiptDate)
    {
        var tag = $"PERF06D-RACE-{Guid.NewGuid():N}";
        await using var connection = await OpenRehearsalConnectionAsync();
        await using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var committed = false;

        try
        {
            var supplierId = Convert.ToInt64(
                await ScalarAsync(
                    connection,
                    transaction,
                    "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;"),
                CultureInfo.InvariantCulture);
            var productId = Convert.ToInt64(
                await ScalarAsync(
                    connection,
                    transaction,
                    "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;"),
                CultureInfo.InvariantCulture);
            var warehouseId = await InsertIdAsync(
                connection,
                transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", tag, 255));
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", RehearsalLogin, 100),
                BigInt("@WarehouseId", warehouseId));
            await transaction.CommitAsync();
            committed = true;

            var receiptId = await ExecuteStoredWithOutputAsync(
                connection,
                null,
                "dbo.sp_XNK_Nhap_Kho_Save_Header",
                Text("@So_Phieu_Nhap_Kho", $"{tag}-{Guid.NewGuid():N}", 100),
                BigInt("@Kho_ID", warehouseId),
                BigInt("@NCC_ID", supplierId),
                Date("@Ngay_Nhap_Kho", receiptDate),
                Text("@Ghi_Chu", "", 1000),
                Text("@Ma_Dang_Nhap", RehearsalLogin, 100));
            await ExecuteStoredWithOutputAsync(
                connection,
                null,
                "dbo.sp_XNK_Nhap_Kho_Save_Detail",
                BigInt("@Nhap_Kho_ID", receiptId),
                BigInt("@San_Pham_ID", productId),
                Decimal("@SL_Nhap", 1),
                Decimal("@Don_Gia_Nhap", 1),
                Text("@Ma_Dang_Nhap", RehearsalLogin, 100));

            return new RaceFixture(tag, warehouseId, productId, receiptId);
        }
        catch
        {
            if (!committed && transaction.Connection is not null)
                await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CleanupRaceFixtureAsync(RaceFixture fixture)
    {
        await using var connection = await OpenRehearsalConnectionAsync();
        await ExecuteAsync(
            connection,
            null,
            "SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;");
        await using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            await ExecuteAsync(connection, transaction,
                "DELETE d FROM dbo.InventorySnapshot_RebuildDeadLetter d JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.InventoryReservation_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.Inventory_Report_Scope_Catalog WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction,
                "DELETE d FROM dbo.InventoryMovement_RebuildDeadLetter d JOIN dbo.InventoryMovement_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @ReceiptId;",
                BigInt("@ReceiptId", fixture.ReceiptId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @ReceiptId;",
                BigInt("@ReceiptId", fixture.ReceiptId));
            /* Deleting raw detail while the header is still Posted invokes the
               canonical invalidation trigger and may enqueue the scope again.
               Remove those trigger-produced queue rows only after the document
               cleanup, still inside this isolated fixture transaction. */
            await ExecuteAsync(connection, transaction,
                "DELETE d FROM dbo.InventorySnapshot_RebuildDeadLetter d JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction,
                "DELETE d FROM dbo.InventoryMovement_RebuildDeadLetter d JOIN dbo.InventoryMovement_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap = @Login AND Kho_ID = @WarehouseId;",
                Text("@Login", RehearsalLogin, 100), BigInt("@WarehouseId", fixture.WarehouseId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;",
                BigInt("@WarehouseId", fixture.WarehouseId));
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<long> ExecuteStoredWithOutputAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string procedure,
        params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedure, connection, transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        var output = parameters.SingleOrDefault(parameter => parameter.ParameterName == "@Auto_ID");
        output ??= BigInt("@Auto_ID", 0);
        output.Direction = ParameterDirection.InputOutput;
        command.Parameters.Add(output);
        command.Parameters.AddRange(parameters.Where(parameter => parameter != output).ToArray());
        await command.ExecuteNonQueryAsync();
        return Convert.ToInt64(output.Value, CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteProcedureAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string procedureName,
        params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedureName, connection, transaction)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 30
        };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ReadSessionIdAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand("SELECT @@SPID;", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<LockSnapshot>> ReadApplicationLocksAsync(
        SqlConnection connection,
        int sessionId)
    {
        const string sql = """
            SELECT resource_description, request_mode, request_owner_type
            FROM sys.dm_tran_locks
            WHERE request_session_id = @SessionId
              AND resource_type = N'APPLICATION'
            ORDER BY resource_description;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@SessionId", SqlDbType.Int) { Value = sessionId });
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<LockSnapshot>();
        while (await reader.ReadAsync())
        {
            var resourceDescription = reader.GetString(0);
            var resourceMatch = Regex.Match(resourceDescription, @"\[(?<resource>[^\]]+)\]", RegexOptions.CultureInvariant);
            var resource = resourceMatch.Success ? resourceMatch.Groups["resource"].Value : resourceDescription.Trim();
            var mode = reader.GetString(1) switch
            {
                "S" => "Shared",
                "X" => "Exclusive",
                _ => reader.GetString(1)
            };
            rows.Add(new LockSnapshot(resource, mode, reader.GetString(2)));
        }
        return rows;
    }

    private static async Task<IReadOnlyList<long>> ReadExpectedGroupsAsync(SqlConnection connection)
    {
        const string sql = """
            SELECT DISTINCT c.Kho_ID
            FROM dbo.Inventory_Report_Scope_Catalog c
            JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = c.Kho_ID
            WHERE ku.Ma_Dang_Nhap = @Login
              AND c.First_Posted_Date <= @ReportTo
            ORDER BY c.Kho_ID;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@Login", SqlDbType.NVarChar, 100) { Value = RehearsalLogin });
        command.Parameters.Add(new SqlParameter("@ReportTo", SqlDbType.Date) { Value = ReportTo.Date });
        await using var reader = await command.ExecuteReaderAsync();
        var groups = new List<long>();
        while (await reader.ReadAsync())
            groups.Add(reader.GetInt64(0));
        return groups;
    }

    private static async Task<int> ReadExpectedScopeCountAsync(SqlConnection connection)
    {
        const string sql = """
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
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@Login", SqlDbType.NVarChar, 100) { Value = RehearsalLogin });
        command.Parameters.Add(new SqlParameter("@ReportFrom", SqlDbType.Date) { Value = ReportFrom.Date });
        command.Parameters.Add(new SqlParameter("@ReportTo", SqlDbType.Date) { Value = ReportTo.Date });
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountApplicationLocksAsync(SqlConnection connection, int sessionId)
    {
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE request_session_id = @SessionId AND resource_type = N'APPLICATION';",
            connection);
        command.Parameters.Add(new SqlParameter("@SessionId", SqlDbType.Int) { Value = sessionId });
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountSessionTransactionsAsync(SqlConnection connection, int sessionId)
    {
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM sys.dm_tran_session_transactions WHERE session_id = @SessionId;",
            connection);
        command.Parameters.Add(new SqlParameter("@SessionId", SqlDbType.Int) { Value = sessionId });
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountSessionBlockingResidueAsync(SqlConnection connection, int sessionId)
    {
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE (session_id = @SessionId AND blocking_session_id <> 0) OR blocking_session_id = @SessionId;",
            connection);
        command.Parameters.Add(new SqlParameter("@SessionId", SqlDbType.Int) { Value = sessionId });
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<long> InsertIdAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string sql,
        params SqlParameter[] parameters)
    {
        return Convert.ToInt64(await ScalarAsync(connection, transaction, sql, parameters), CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string sql,
        params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteStoredAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string procedure,
        params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedure, connection, transaction)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 120
        };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string sql,
        params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    private static string Normalize(object value)
    {
        if (value is DBNull)
            return "<NULL>";
        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? "<NULL>";
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<NULL>";
    }

    private sealed record ReportResult(long TotalCount, IReadOnlyList<string> RowKeys);
    private sealed record LockSnapshot(string Resource, string Mode, string Owner);
    private sealed record RaceFixture(string Tag, long WarehouseId, long ProductId, long ReceiptId);
    private sealed record FenceConfigRow(byte ConfigId, string Mode, DateTime UpdatedAt);
    private sealed record FenceConstraintState(string Name, bool IsDisabled, bool IsNotTrusted);
    private sealed record FenceConfigBaseline(
        IReadOnlyList<FenceConfigRow> Rows,
        IReadOnlyList<FenceConstraintState> Constraints);

    private static SqlParameter BigInt(string name, long value) =>
        new(name, SqlDbType.BigInt) { Value = value };

    private static SqlParameter Decimal(string name, decimal value) => new(name, SqlDbType.Decimal)
    {
        Precision = 18,
        Scale = 3,
        Value = value
    };

    private static SqlParameter Date(string name, DateTime value) =>
        new(name, SqlDbType.Date) { Value = value.Date };

    private static SqlParameter Text(string name, string value, int size) =>
        new(name, SqlDbType.NVarChar, size) { Value = value };


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

    private static string ExtractProcedure(string source, string procedureName)
    {
        var pattern = $"(?is)CREATE\\s+OR\\s+ALTER\\s+PROCEDURE\\s+dbo\\.{Regex.Escape(procedureName)}\\b(?<body>.*?)(?=^\\s*CREATE\\s+OR\\s+ALTER\\s+PROCEDURE\\s+dbo\\.|\\z)";
        var match = Regex.Match(source, pattern, RegexOptions.Multiline);
        Assert.True(match.Success, $"Procedure was not found: {procedureName}");
        return match.Groups["body"].Value;
    }
}
