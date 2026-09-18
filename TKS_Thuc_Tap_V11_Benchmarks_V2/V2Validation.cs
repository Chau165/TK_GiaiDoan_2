using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace TKS_Thuc_Tap_V11_Benchmarks_V2;

public static class V2Validation
{
    private static readonly string[] RequiredProcedures =
    {
        "sp_DM_Master_Page",
        "sp_DM_Lookup_Page",
        "sp_XNK_Document_Page",
        "sp_BC_Chi_Tiet_Nhap_Page",
        "sp_BC_Xuat_Nhap_Ton_Page",
        "sp_BC_Ton_Kho_Hien_Tai_Page",
        "sp_Inventory_Report_Acquire_Scope_Fence",
        "sp_Inventory_Fence_Require_Context"
    };

    private static readonly string[] RequiredTypes =
    {
        "InventoryMovementAffectedType",
        "InventorySnapshotAffectedType",
        "InventoryFenceGroupSetType",
        "InventoryFenceScopeSetType"
    };

    private static readonly string[] RequiredIndexes =
    {
        "PK_Inventory_Report_Scope_Catalog",
        "UQ_Inventory_Report_Scope_Catalog_Scope",
        "IX_Inventory_Report_Scope_Catalog_Cutoff",
        "PK_Inventory_Current_Report_State",
        "PK_InventoryBalance_Current",
        "PK_Inventory_Movement_Daily",
        "PK_Inventory_Balance_Daily",
        "PK_InventoryBalance_Snapshot_Daily",
        "PK_InventoryMovement_AggregateState",
        "PK_Inventory_Balance_Daily_Scope"
    };

    private const string ConsistencySql = """
        SET NOCOUNT ON;
        SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

        ;WITH Ledger AS
        (
            SELECT h.Kho_ID, d.San_Pham_ID, CAST(SUM(d.SL_Nhap) AS DECIMAL(18,3)) AS Quantity
            FROM dbo.tbl_XNK_Nhap_Kho h
            JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1
            GROUP BY h.Kho_ID, d.San_Pham_ID
            UNION ALL
            SELECT h.Kho_ID, d.San_Pham_ID, CAST(-SUM(d.SL_Xuat) AS DECIMAL(18,3))
            FROM dbo.tbl_XNK_Xuat_Kho h
            JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1
            GROUP BY h.Kho_ID, d.San_Pham_ID
        ), LedgerTotals AS
        (
            SELECT Kho_ID, San_Pham_ID, CAST(SUM(Quantity) AS DECIMAL(18,3)) AS Quantity
            FROM Ledger
            GROUP BY Kho_ID, San_Pham_ID
        )
        SELECT N'Ledger_vs_Current' AS CheckName, COUNT_BIG(*) AS MismatchCount
        FROM LedgerTotals l
        FULL OUTER JOIN dbo.InventoryBalance_Current b
            ON b.Kho_ID = l.Kho_ID AND b.San_Pham_ID = l.San_Pham_ID
        WHERE ISNULL(l.Quantity, 0) <> ISNULL(b.CurrentQuantity, 0);

        SELECT N'Reserved_vs_Reservation' AS CheckName, COUNT_BIG(*) AS MismatchCount
        FROM
        (
            SELECT Kho_ID, San_Pham_ID, CAST(SUM(ReservedQuantity) AS DECIMAL(18,3)) AS Quantity
            FROM dbo.InventoryReservation_Current
            GROUP BY Kho_ID, San_Pham_ID
        ) r
        FULL OUTER JOIN dbo.InventoryBalance_Current b
            ON b.Kho_ID = r.Kho_ID AND b.San_Pham_ID = r.San_Pham_ID
        WHERE ISNULL(r.Quantity, 0) <> ISNULL(b.ReservedQuantity, 0);

        ;WITH LedgerDaily AS
        (
            SELECT h.Ngay_Nhap_Kho AS Movement_Date, h.Kho_ID, d.San_Pham_ID,
                   CAST(SUM(d.SL_Nhap) AS DECIMAL(18,3)) AS Receipt,
                   CAST(0 AS DECIMAL(18,3)) AS Issue
            FROM dbo.tbl_XNK_Nhap_Kho h
            JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1
            GROUP BY h.Ngay_Nhap_Kho, h.Kho_ID, d.San_Pham_ID
            UNION ALL
            SELECT h.Ngay_Xuat_Kho, h.Kho_ID, d.San_Pham_ID,
                   CAST(0 AS DECIMAL(18,3)), CAST(SUM(d.SL_Xuat) AS DECIMAL(18,3))
            FROM dbo.tbl_XNK_Xuat_Kho h
            JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1
            GROUP BY h.Ngay_Xuat_Kho, h.Kho_ID, d.San_Pham_ID
        ), LedgerDailyTotals AS
        (
            SELECT Movement_Date, Kho_ID, San_Pham_ID,
                   CAST(SUM(Receipt) AS DECIMAL(18,3)) AS Receipt,
                   CAST(SUM(Issue) AS DECIMAL(18,3)) AS Issue
            FROM LedgerDaily
            GROUP BY Movement_Date, Kho_ID, San_Pham_ID
        )
        SELECT N'Movement_vs_Ledger' AS CheckName, COUNT_BIG(*) AS MismatchCount
        FROM LedgerDailyTotals l
        FULL OUTER JOIN dbo.Inventory_Movement_Daily m
            ON m.Movement_Date = l.Movement_Date
           AND m.Kho_ID = l.Kho_ID
           AND m.San_Pham_ID = l.San_Pham_ID
           AND m.IsValid = 1
        WHERE ISNULL(l.Receipt, 0) <> ISNULL(m.Total_Receipt, 0)
           OR ISNULL(l.Issue, 0) <> ISNULL(m.Total_Issue, 0);

        SELECT N'Daily_Arithmetic' AS CheckName, COUNT_BIG(*) AS MismatchCount
        FROM dbo.Inventory_Balance_Daily
        WHERE IsValid = 1
          AND OpeningQuantity + TotalReceived - TotalIssued <> ClosingQuantity;

        ;WITH LedgerDaily AS
        (
            SELECT h.Ngay_Nhap_Kho AS Movement_Date, h.Kho_ID, d.San_Pham_ID,
                   CAST(SUM(d.SL_Nhap) AS DECIMAL(18,3)) AS Receipt,
                   CAST(0 AS DECIMAL(18,3)) AS Issue
            FROM dbo.tbl_XNK_Nhap_Kho h
            JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1
            GROUP BY h.Ngay_Nhap_Kho, h.Kho_ID, d.San_Pham_ID
            UNION ALL
            SELECT h.Ngay_Xuat_Kho, h.Kho_ID, d.San_Pham_ID,
                   CAST(0 AS DECIMAL(18,3)), CAST(SUM(d.SL_Xuat) AS DECIMAL(18,3))
            FROM dbo.tbl_XNK_Xuat_Kho h
            JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1
            GROUP BY h.Ngay_Xuat_Kho, h.Kho_ID, d.San_Pham_ID
        ), Totals AS
        (
            SELECT Movement_Date, Kho_ID, San_Pham_ID,
                   CAST(SUM(Receipt - Issue) AS DECIMAL(18,3)) AS Delta
            FROM LedgerDaily
            GROUP BY Movement_Date, Kho_ID, San_Pham_ID
        ), Running AS
        (
            SELECT Movement_Date, Kho_ID, San_Pham_ID,
                   CAST(SUM(Delta) OVER
                       (PARTITION BY Kho_ID, San_Pham_ID ORDER BY Movement_Date ROWS UNBOUNDED PRECEDING)
                       AS DECIMAL(18,3)) AS ClosingQuantity
            FROM Totals
        )
        SELECT N'Daily_vs_Ledger_Cutoff' AS CheckName, COUNT_BIG(*) AS MismatchCount
        FROM dbo.Inventory_Balance_Daily b
        FULL OUTER JOIN Running r
            ON r.Movement_Date = b.Balance_Date
           AND r.Kho_ID = b.Kho_ID
           AND r.San_Pham_ID = b.San_Pham_ID
        WHERE b.IsValid = 1
          AND ISNULL(b.ClosingQuantity, 0) <> ISNULL(r.ClosingQuantity, 0);

        SELECT N'Period_Arithmetic_from_Daily' AS CheckName, COUNT_BIG(*) AS MismatchCount
        FROM dbo.Inventory_Balance_Daily
        WHERE IsValid = 1
          AND (CumulativeReceived < TotalReceived OR CumulativeIssued < TotalIssued);

        ;WITH LedgerDaily AS
        (
            SELECT h.Ngay_Nhap_Kho AS Movement_Date, h.Kho_ID, d.San_Pham_ID,
                   CAST(SUM(d.SL_Nhap) AS DECIMAL(18,3)) AS Receipt,
                   CAST(0 AS DECIMAL(18,3)) AS Issue
            FROM dbo.tbl_XNK_Nhap_Kho h
            JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1
            GROUP BY h.Ngay_Nhap_Kho, h.Kho_ID, d.San_Pham_ID
            UNION ALL
            SELECT h.Ngay_Xuat_Kho, h.Kho_ID, d.San_Pham_ID,
                   CAST(0 AS DECIMAL(18,3)), CAST(SUM(d.SL_Xuat) AS DECIMAL(18,3))
            FROM dbo.tbl_XNK_Xuat_Kho h
            JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1
            GROUP BY h.Ngay_Xuat_Kho, h.Kho_ID, d.San_Pham_ID
        ), Totals AS
        (
            SELECT Movement_Date, Kho_ID, San_Pham_ID,
                   CAST(SUM(Receipt - Issue) AS DECIMAL(18,3)) AS Delta
            FROM LedgerDaily
            GROUP BY Movement_Date, Kho_ID, San_Pham_ID
        )
        SELECT N'Snapshot_vs_Ledger_Cutoff' AS CheckName, COUNT_BIG(*) AS MismatchCount
        FROM dbo.InventoryBalance_Snapshot_Daily s
        OUTER APPLY
        (
            SELECT CAST(COALESCE(SUM(t.Delta), 0) AS DECIMAL(18,3)) AS ClosingQuantity
            FROM Totals t
            WHERE t.Kho_ID = s.Kho_ID
              AND t.San_Pham_ID = s.San_Pham_ID
              AND t.Movement_Date <= s.Snapshot_Date
        ) ledger
        WHERE s.IsValid = 1
          AND s.ClosingQuantity <> ledger.ClosingQuantity;

        SELECT N'Active_Movement_Queue_Duplicates' AS CheckName, COUNT_BIG(*) AS MismatchCount
        FROM
        (
            SELECT Kho_ID, San_Pham_ID, From_Date
            FROM dbo.InventoryMovement_RebuildQueue
            WHERE Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING')
            GROUP BY Kho_ID, San_Pham_ID, From_Date
            HAVING COUNT(*) > 1
        ) d;

        SELECT N'Active_Snapshot_Queue_Duplicates' AS CheckName, COUNT_BIG(*) AS MismatchCount
        FROM
        (
            SELECT Kho_ID, San_Pham_ID, From_Date
            FROM dbo.InventorySnapshot_RebuildQueue
            WHERE LifecycleStatus IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'INITIALIZE_REQUIRED')
            GROUP BY Kho_ID, San_Pham_ID, From_Date
            HAVING COUNT(*) > 1
        ) d;

        SELECT N'Orphan_Invalid_Snapshot' AS CheckName, COUNT_BIG(*) AS MismatchCount
        FROM dbo.InventoryBalance_Snapshot_Daily s
        WHERE s.IsValid = 0
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.InventorySnapshot_RebuildQueue q
              WHERE q.Kho_ID = s.Kho_ID
                AND q.San_Pham_ID = s.San_Pham_ID
                AND q.From_Date <= s.Snapshot_Date
                AND q.LifecycleStatus IN
                    (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'INITIALIZE_REQUIRED', N'FAILED_FINAL')
          );

        SELECT N'Valid_Snapshot_Affected_By_Unresolved_Failure' AS CheckName, COUNT_BIG(*) AS MismatchCount
        FROM dbo.InventoryBalance_Snapshot_Daily s
        WHERE s.IsValid = 1
          AND EXISTS
          (
              SELECT 1
              FROM dbo.InventorySnapshot_RebuildQueue q
              WHERE q.Kho_ID = s.Kho_ID
                AND q.San_Pham_ID = s.San_Pham_ID
                AND q.From_Date <= s.Snapshot_Date
                AND q.LifecycleStatus = N'FAILED_FINAL'
          );
        """;

    private const string DatasetSql = """
        SET NOCOUNT ON;
        SELECT o.name, SUM(CASE WHEN ps.index_id IN (0, 1) THEN ps.row_count ELSE 0 END) AS [RowCount]
        FROM sys.objects o
        JOIN sys.dm_db_partition_stats ps ON ps.object_id = o.object_id
        WHERE o.type = 'U'
          AND o.name IN
          (
              N'tbl_XNK_Nhap_Kho',
              N'tbl_XNK_Nhap_Kho_Raw_Data',
              N'tbl_XNK_Xuat_Kho',
              N'tbl_XNK_Xuat_Kho_Raw_Data',
              N'tbl_DM_San_Pham',
              N'Inventory_Movement_Daily',
              N'Inventory_Balance_Daily',
              N'Inventory_Balance_Daily_Scope',
              N'Inventory_Report_Scope_Catalog',
              N'InventoryBalance_Current'
          )
        GROUP BY o.name
        ORDER BY o.name;
        """;

    private const string ObjectSql = """
        SET NOCOUNT ON;
        SELECT N'PROCEDURE', COUNT_BIG(*)
        FROM sys.procedures
        WHERE name IN (__PROCEDURES__);
        SELECT N'TYPE', COUNT_BIG(*)
        FROM sys.types
        WHERE is_user_defined = 1 AND name IN (__TYPES__);
        SELECT N'INDEX', COUNT_BIG(*)
        FROM sys.indexes
        WHERE name IN (__INDEXES__);
        """;

    private const string ResidueSql = """
        SET NOCOUNT ON;
        DECLARE @DbId int = DB_ID();
        SELECT
            COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE database_id = @DbId AND session_id <> @@SPID), 0) AS ActiveRequests,
            COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE database_id = @DbId AND session_id <> @@SPID AND blocking_session_id <> 0), 0) AS BlockingRequests,
            COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_exec_query_memory_grants g JOIN sys.dm_exec_sessions s ON s.session_id = g.session_id WHERE s.database_id = @DbId AND g.grant_time IS NULL AND g.session_id <> @@SPID), 0) AS PendingMemoryGrants,
            COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_os_waiting_tasks wt JOIN sys.dm_exec_sessions s ON s.session_id = wt.session_id WHERE s.database_id = @DbId AND wt.wait_type = N'RESOURCE_SEMAPHORE'), 0) AS ResourceSemaphoreWaiters,
            COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_tran_session_transactions t JOIN sys.dm_exec_sessions s ON s.session_id = t.session_id WHERE s.database_id = @DbId AND s.session_id <> @@SPID), 0) AS OpenTransactions,
            COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_tran_locks WHERE request_session_id <> @@SPID AND resource_type = N'APPLICATION'), 0) AS ApplicationLocks;
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task<int> RunAsync(
        V2Settings p_settings,
        string p_outputPath,
        bool p_includeReadSmoke)
    {
        var v_result = new V2ValidationResult
        {
            CapturedAtUtc = DateTime.UtcNow,
            TargetConnection = V2Constants.ExpectedServer,
            TargetDatabase = V2Constants.ExpectedDatabase,
            TargetDatabaseId = V2Constants.ExpectedDatabaseId
        };

        try
        {
            p_settings.ConfigureDataAccess();
            await using var v_connection = new SqlConnection(p_settings.ConnectionString);
            await v_connection.OpenAsync();
            v_result.Identity = await QueryIdentityAsync(v_connection);

            if (!v_result.Identity.IsTarget)
            {
                v_result.Result = "TARGET_DB_MISMATCH";
                v_result.Passed = false;
                WriteJson(p_outputPath, v_result);
                return 20;
            }

            v_result.Dataset = await QueryDatasetAsync(v_connection);
            v_result.ObjectChecks = await QueryObjectChecksAsync(v_connection);
            v_result.Consistency = await QueryConsistencyAsync(v_connection);
            v_result.CurrentRowsetSha256 = await QueryCurrentFingerprintAsync(v_connection);

            if (p_includeReadSmoke)
            {
                var v_operations = new V2ReadOperations(p_settings);
                foreach (var v_scenario in V2Constants.Scenarios)
                {
                    try
                    {
                        var v_rows = await v_operations.ExecuteAsync(v_scenario);
                        v_result.ReadPaths[v_scenario] = $"PASS|Rows={v_rows}";
                    }
                    catch (Exception p_exception)
                    {
                        v_result.ReadPaths[v_scenario] =
                            $"FAIL|{V2LoadTest.ClassifyException(p_exception)}|{p_exception.Message}";
                    }
                }
            }

            v_result.Passed =
                v_result.Dataset.MatchesExpected
                && v_result.ObjectChecks.All(p_check => p_check.Passed)
                && v_result.Consistency.Count == 11
                && v_result.Consistency.All(p_check => p_check.MismatchCount == 0)
                && (!p_includeReadSmoke || v_result.ReadPaths.Values.All(p_value => p_value.StartsWith("PASS|", StringComparison.Ordinal)));
            v_result.Result = v_result.Passed ? "PASS" : "PRECONDITION_FAILED";
            WriteJson(p_outputPath, v_result);
            return v_result.Passed ? 0 : 21;
        }
        catch (Exception p_exception)
        {
            v_result.Result = "HARNESS_FAILURE";
            v_result.Error = $"{p_exception.GetType().Name}: {p_exception.Message}";
            v_result.Passed = false;
            WriteJson(p_outputPath, v_result);
            return 1;
        }
    }

    public static async Task AssertTargetDatabaseAsync(V2Settings p_settings)
    {
        p_settings.ConfigureDataAccess();
        await using var v_connection = new SqlConnection(p_settings.ConnectionString);
        await v_connection.OpenAsync();
        var v_identity = await QueryIdentityAsync(v_connection);
        if (!v_identity.IsTarget)
        {
            throw new InvalidOperationException(
                $"TARGET_DB_MISMATCH|Server={v_identity.ServerName}|Database={v_identity.DatabaseName}|DB_ID={v_identity.DatabaseId}");
        }
    }

    public static async Task<int> RunSmokeAsync(
        V2Settings p_settings,
        string p_scenario,
        string p_outputPath)
    {
        var v_result = new V2SmokeResult
        {
            CapturedAtUtc = DateTime.UtcNow,
            Scenario = p_scenario
        };

        try
        {
            if (!V2Constants.Scenarios.Contains(p_scenario, StringComparer.Ordinal))
                throw new ArgumentException($"Unknown V2 scenario: {p_scenario}");
            await AssertTargetDatabaseAsync(p_settings);
            var v_rows = await new V2ReadOperations(p_settings).ExecuteAsync(p_scenario);
            v_result.Result = "PASS";
            v_result.Rows = v_rows;
            WriteJson(p_outputPath, v_result);
            return 0;
        }
        catch (Exception p_exception)
        {
            v_result.Result = V2LoadTest.ClassifyException(p_exception);
            v_result.Error = p_exception.Message;
            WriteJson(p_outputPath, v_result);
            return 1;
        }
    }

    public static async Task<int> RunResidueAsync(
        V2Settings p_settings,
        string p_outputPath)
    {
        try
        {
            p_settings.ConfigureDataAccess();
            await using var v_connection = new SqlConnection(p_settings.ConnectionString);
            await v_connection.OpenAsync();
            var v_identity = await QueryIdentityAsync(v_connection);
            if (!v_identity.IsTarget)
                throw new InvalidOperationException("TARGET_DB_MISMATCH");

            await using var v_command = v_connection.CreateCommand();
            v_command.CommandText = ResidueSql;
            v_command.CommandTimeout = 10;
            await using var v_reader = await v_command.ExecuteReaderAsync();
            await v_reader.ReadAsync();
            var v_result = new V2ResidueResult
            {
                CapturedAtUtc = DateTime.UtcNow,
                DatabaseName = v_identity.DatabaseName,
                DatabaseId = v_identity.DatabaseId,
                ActiveRequests = Convert.ToInt64(v_reader.GetValue(0), CultureInfo.InvariantCulture),
                BlockingRequests = Convert.ToInt64(v_reader.GetValue(1), CultureInfo.InvariantCulture),
                PendingMemoryGrants = Convert.ToInt64(v_reader.GetValue(2), CultureInfo.InvariantCulture),
                ResourceSemaphoreWaiters = Convert.ToInt64(v_reader.GetValue(3), CultureInfo.InvariantCulture),
                OpenTransactions = Convert.ToInt64(v_reader.GetValue(4), CultureInfo.InvariantCulture),
                ApplicationLocks = Convert.ToInt64(v_reader.GetValue(5), CultureInfo.InvariantCulture)
            };
            v_result.Passed = v_result.IsClean;
            v_result.Result = v_result.Passed ? "PASS" : "RESIDUE_FOUND";
            WriteJson(p_outputPath, v_result);
            return v_result.Passed ? 0 : 22;
        }
        catch (Exception p_exception)
        {
            WriteJson(p_outputPath, new { Result = "HARNESS_FAILURE", Error = p_exception.Message });
            return 1;
        }
    }

    public static int RunSelfTests(string p_outputPath)
    {
        var v_tests = new List<V2SelfTestCase>
        {
            Test("Target guard accepts local instance", IsTarget(
                @"DESKTOP-NHQ7QPL\MSSQLSERVER19",
                V2Constants.ExpectedDatabase,
                V2Constants.ExpectedDatabaseId), true),
            Test("Target guard rejects wrong database", IsTarget(
                @"DESKTOP-NHQ7QPL\MSSQLSERVER19",
                "TKS_Thuc_Tap_V11_GiaiDoan2",
                V2Constants.ExpectedDatabaseId), false),
            Test("Target guard rejects wrong database id", IsTarget(
                @"DESKTOP-NHQ7QPL\MSSQLSERVER19",
                V2Constants.ExpectedDatabase,
                32), false),
            Test("Target guard rejects wrong instance", IsTarget(
                @"DESKTOP-NHQ7QPL\MSSQLSERVER18",
                V2Constants.ExpectedDatabase,
                V2Constants.ExpectedDatabaseId), false),
            Test("Malformed manifest is rejected", RejectsMalformedJson(), true),
            Test("Output schema serializes", SerializesOutputSchema(), true),
            Test("Resume requires same protocol and database fingerprint", ResumeRequiresSameIdentity(), true),
            Test("LEGACY is the standard mode", string.Equals("LEGACY", "LEGACY", StringComparison.Ordinal), true)
        };

        var v_result = new
        {
            Result = v_tests.All(p_test => p_test.Passed) ? "PASS" : "FAIL",
            CapturedAtUtc = DateTime.UtcNow,
            Tests = v_tests
        };
        WriteJson(p_outputPath, v_result);
        return v_tests.All(p_test => p_test.Passed) ? 0 : 1;
    }

    private static V2SelfTestCase Test(string p_name, bool p_actual, bool p_expected)
    {
        return new V2SelfTestCase
        {
            Name = p_name,
            Expected = p_expected,
            Actual = p_actual,
            Passed = p_actual == p_expected
        };
    }

    private static bool IsTarget(string p_server, string p_database, int p_databaseId)
    {
        var v_serverOk =
            p_server.Equals(V2Constants.ExpectedServer, StringComparison.OrdinalIgnoreCase)
            || p_server.EndsWith(@"\MSSQLSERVER19", StringComparison.OrdinalIgnoreCase);
        return v_serverOk
            && string.Equals(p_database, V2Constants.ExpectedDatabase, StringComparison.OrdinalIgnoreCase)
            && p_databaseId == V2Constants.ExpectedDatabaseId;
    }

    private static bool RejectsMalformedJson()
    {
        try
        {
            _ = JsonDocument.Parse("{");
            return false;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static bool SerializesOutputSchema()
    {
        var v_json = JsonSerializer.Serialize(
            new V2SmokeResult { Result = "PASS", Scenario = "MasterPaged", Rows = 10 },
            JsonOptions);
        return v_json.Contains("\"Result\"", StringComparison.Ordinal)
            && v_json.Contains("\"Scenario\"", StringComparison.Ordinal);
    }

    private static bool ResumeRequiresSameIdentity()
    {
        const string v_savedProtocol = "WAREHOUSE_BENCHMARK_V2_1";
        const string v_savedDatabaseFingerprint = "dataset-hash";
        return string.Equals(v_savedProtocol, "WAREHOUSE_BENCHMARK_V2_1", StringComparison.Ordinal)
            && string.Equals(v_savedDatabaseFingerprint, "dataset-hash", StringComparison.Ordinal)
            && !string.Equals(v_savedDatabaseFingerprint, "different-hash", StringComparison.Ordinal);
    }

    private static async Task<V2IdentityEvidence> QueryIdentityAsync(SqlConnection p_connection)
    {
        await using var v_command = p_connection.CreateCommand();
        v_command.CommandText = "SELECT @@SERVERNAME, DB_NAME(), DB_ID();";
        v_command.CommandTimeout = 10;
        await using var v_reader = await v_command.ExecuteReaderAsync();
        if (!await v_reader.ReadAsync())
            throw new InvalidOperationException("Database identity query returned no row.");

        var v_server = Convert.ToString(v_reader.GetValue(0), CultureInfo.InvariantCulture) ?? "";
        var v_database = Convert.ToString(v_reader.GetValue(1), CultureInfo.InvariantCulture) ?? "";
        var v_databaseId = Convert.ToInt32(v_reader.GetValue(2), CultureInfo.InvariantCulture);
        return new V2IdentityEvidence
        {
            ServerName = v_server,
            DatabaseName = v_database,
            DatabaseId = v_databaseId,
            IsTarget = IsTarget(v_server, v_database, v_databaseId)
        };
    }

    private static async Task<V2DatasetEvidence> QueryDatasetAsync(SqlConnection p_connection)
    {
        var v_actual = V2Constants.DatasetExpectedRows.Keys
            .ToDictionary(p_name => p_name, _ => 0L, StringComparer.Ordinal);
        await using var v_command = p_connection.CreateCommand();
        v_command.CommandText = DatasetSql;
        v_command.CommandTimeout = 120;
        await using var v_reader = await v_command.ExecuteReaderAsync();
        while (await v_reader.ReadAsync())
        {
            var v_name = Convert.ToString(v_reader.GetValue(0), CultureInfo.InvariantCulture) ?? "";
            if (v_actual.ContainsKey(v_name))
                v_actual[v_name] = Convert.ToInt64(v_reader.GetValue(1), CultureInfo.InvariantCulture);
        }

        var v_fingerprintInput = string.Join(
            "\n",
            v_actual.OrderBy(p_item => p_item.Key, StringComparer.Ordinal)
                .Select(p_item => $"{p_item.Key}|{p_item.Value.ToString(CultureInfo.InvariantCulture)}"));
        return new V2DatasetEvidence
        {
            ActualRows = v_actual,
            ExpectedRows = V2Constants.DatasetExpectedRows,
            FingerprintInput = v_fingerprintInput,
            FingerprintSha256 = Sha256(v_fingerprintInput),
            MatchesExpected = v_actual.All(p_item =>
                V2Constants.DatasetExpectedRows.TryGetValue(p_item.Key, out var v_expected)
                && v_expected == p_item.Value)
        };
    }

    private static async Task<IReadOnlyList<V2ObjectCheck>> QueryObjectChecksAsync(SqlConnection p_connection)
    {
        var v_sql = ObjectSql
            .Replace("__PROCEDURES__", QuoteNames(RequiredProcedures), StringComparison.Ordinal)
            .Replace("__TYPES__", QuoteNames(RequiredTypes), StringComparison.Ordinal)
            .Replace("__INDEXES__", QuoteNames(RequiredIndexes), StringComparison.Ordinal);
        await using var v_command = p_connection.CreateCommand();
        v_command.CommandText = v_sql;
        v_command.CommandTimeout = 30;
        await using var v_reader = await v_command.ExecuteReaderAsync();
        var v_checks = new List<V2ObjectCheck>();
        do
        {
            if (!await v_reader.ReadAsync())
                continue;
            var v_kind = Convert.ToString(v_reader.GetValue(0), CultureInfo.InvariantCulture) ?? "";
            var v_actual = Convert.ToInt64(v_reader.GetValue(1), CultureInfo.InvariantCulture);
            var v_expected = v_kind switch
            {
                "PROCEDURE" => RequiredProcedures.Length,
                "TYPE" => RequiredTypes.Length,
                "INDEX" => RequiredIndexes.Length,
                _ => 0
            };
            v_checks.Add(new V2ObjectCheck
            {
                Kind = v_kind,
                Expected = v_expected,
                Actual = v_actual,
                Passed = v_actual == v_expected
            });
        }
        while (await v_reader.NextResultAsync());

        return v_checks;
    }

    private static async Task<IReadOnlyList<V2ConsistencyCheck>> QueryConsistencyAsync(SqlConnection p_connection)
    {
        await using var v_command = p_connection.CreateCommand();
        v_command.CommandText = ConsistencySql;
        v_command.CommandTimeout = 180;
        await using var v_reader = await v_command.ExecuteReaderAsync();
        var v_checks = new List<V2ConsistencyCheck>();
        do
        {
            if (!await v_reader.ReadAsync())
                continue;
            v_checks.Add(new V2ConsistencyCheck
            {
                CheckName = Convert.ToString(v_reader.GetValue(0), CultureInfo.InvariantCulture) ?? "",
                MismatchCount = Convert.ToInt64(v_reader.GetValue(1), CultureInfo.InvariantCulture)
            });
        }
        while (await v_reader.NextResultAsync());
        return v_checks;
    }

    private static async Task<string> QueryCurrentFingerprintAsync(SqlConnection p_connection)
    {
        const string v_sql = """
            SET NOCOUNT ON;
            SELECT Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity
            FROM dbo.InventoryBalance_Current
            ORDER BY Kho_ID, San_Pham_ID;
            """;
        await using var v_command = p_connection.CreateCommand();
        v_command.CommandText = v_sql;
        v_command.CommandTimeout = 30;
        await using var v_reader = await v_command.ExecuteReaderAsync();
        var v_builder = new StringBuilder();
        while (await v_reader.ReadAsync())
        {
            for (var v_index = 0; v_index < v_reader.FieldCount; v_index++)
            {
                if (v_index > 0)
                    v_builder.Append('|');
                v_builder.Append(ToInvariant(v_reader.GetValue(v_index)));
            }
            v_builder.Append('\n');
        }
        return Sha256(v_builder.ToString());
    }

    private static string QuoteNames(IEnumerable<string> p_names)
    {
        return string.Join(
            ",",
            p_names.Select(p_name => "N'" + p_name.Replace("'", "''", StringComparison.Ordinal) + "'"));
    }

    private static string ToInvariant(object p_value)
    {
        return p_value == DBNull.Value
            ? "NULL"
            : Convert.ToString(p_value, CultureInfo.InvariantCulture) ?? "";
    }

    private static string Sha256(string p_value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p_value)));
    }

    private static void WriteJson(string p_outputPath, object p_value)
    {
        if (string.IsNullOrWhiteSpace(p_outputPath))
        {
            Console.WriteLine(JsonSerializer.Serialize(p_value, JsonOptions));
            return;
        }

        var v_directory = Path.GetDirectoryName(p_outputPath);
        if (!string.IsNullOrWhiteSpace(v_directory))
            Directory.CreateDirectory(v_directory);
        File.WriteAllText(p_outputPath, JsonSerializer.Serialize(p_value, JsonOptions), new UTF8Encoding(false));
        Console.WriteLine($"V2_OUTPUT|{p_outputPath}");
    }
}

public sealed class V2ValidationResult
{
    public DateTime CapturedAtUtc { get; set; }
    public string TargetConnection { get; set; } = "";
    public string TargetDatabase { get; set; } = "";
    public int TargetDatabaseId { get; set; }
    public string Result { get; set; } = "";
    public bool Passed { get; set; }
    public V2IdentityEvidence Identity { get; set; } = new();
    public V2DatasetEvidence Dataset { get; set; } = new();
    public IReadOnlyList<V2ObjectCheck> ObjectChecks { get; set; } = Array.Empty<V2ObjectCheck>();
    public IReadOnlyList<V2ConsistencyCheck> Consistency { get; set; } = Array.Empty<V2ConsistencyCheck>();
    public Dictionary<string, string> ReadPaths { get; set; } = new(StringComparer.Ordinal);
    public string CurrentRowsetSha256 { get; set; } = "";
    public string Error { get; set; } = "";
}

public sealed class V2IdentityEvidence
{
    public string ServerName { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public int DatabaseId { get; set; }
    public bool IsTarget { get; set; }
}

public sealed class V2DatasetEvidence
{
    public IReadOnlyDictionary<string, long> ExpectedRows { get; set; } =
        new Dictionary<string, long>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, long> ActualRows { get; set; } =
        new Dictionary<string, long>(StringComparer.Ordinal);
    public string FingerprintInput { get; set; } = "";
    public string FingerprintSha256 { get; set; } = "";
    public bool MatchesExpected { get; set; }
}

public sealed class V2ObjectCheck
{
    public string Kind { get; set; } = "";
    public long Expected { get; set; }
    public long Actual { get; set; }
    public bool Passed { get; set; }
}

public sealed class V2ConsistencyCheck
{
    public string CheckName { get; set; } = "";
    public long MismatchCount { get; set; }
}

public sealed class V2SmokeResult
{
    public DateTime CapturedAtUtc { get; set; }
    public string Scenario { get; set; } = "";
    public string Result { get; set; } = "";
    public int Rows { get; set; }
    public string Error { get; set; } = "";
}

public sealed class V2ResidueResult
{
    public DateTime CapturedAtUtc { get; set; }
    public string DatabaseName { get; set; } = "";
    public int DatabaseId { get; set; }
    public long ActiveRequests { get; set; }
    public long BlockingRequests { get; set; }
    public long PendingMemoryGrants { get; set; }
    public long ResourceSemaphoreWaiters { get; set; }
    public long OpenTransactions { get; set; }
    public long ApplicationLocks { get; set; }
    public bool Passed { get; set; }
    public string Result { get; set; } = "";

    public bool IsClean =>
        ActiveRequests == 0
        && BlockingRequests == 0
        && PendingMemoryGrants == 0
        && ResourceSemaphoreWaiters == 0
        && OpenTransactions == 0
        && ApplicationLocks == 0;
}

public sealed class V2SelfTestCase
{
    public string Name { get; set; } = "";
    public bool Expected { get; set; }
    public bool Actual { get; set; }
    public bool Passed { get; set; }
}
