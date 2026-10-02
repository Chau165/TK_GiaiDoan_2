using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace TKS_Thuc_Tap_V11_Benchmarks_V22;

internal sealed record V22ExpectedModule(string Schema, string Name, string LiveDefinitionSha256);

internal sealed record V22SnapshotCapture(
    DateTime CapturedUtc,
    string SnapshotSha256,
    bool ExpectedModuleHashesMatch,
    string DatabaseName,
    int DatabaseId,
    string DatabaseState,
    string HistoricalReportMode,
    IReadOnlyList<string> AllowedWarehouseIds,
    object Evidence);

internal static class V22CorrectnessSnapshot
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static IReadOnlyList<V22ExpectedModule> LoadModules(string closurePath, string parityPath)
    {
        using var closure = JsonDocument.Parse(File.ReadAllText(closurePath));
        using var parity = JsonDocument.Parse(File.ReadAllText(parityPath));
        var required = closure.RootElement.GetProperty("RequiredActiveModules")
            .EnumerateArray()
            .Select(item => (Schema: item.GetProperty("Schema").GetString()!, Name: item.GetProperty("Name").GetString()!))
            .ToHashSet();
        var modules = parity.RootElement.GetProperty("Modules")
            .EnumerateArray()
            .Select(item => new V22ExpectedModule(
                item.GetProperty("Schema").GetString()!,
                item.GetProperty("ObjectName").GetString()!,
                item.GetProperty("LiveDefinitionSHA256").GetString()!))
            .Where(item => required.Contains((item.Schema, item.Name)))
            .OrderBy(item => item.Schema, StringComparer.Ordinal)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

        if (modules.Length != required.Count || required.Count == 0)
            throw new InvalidDataException("Active closure module set does not match definition parity evidence.");
        if (modules.Any(item => string.IsNullOrWhiteSpace(item.LiveDefinitionSha256)))
            throw new InvalidDataException("Definition parity evidence contains an empty live definition hash.");
        return modules;
    }

    internal static async Task<V22SnapshotCapture> CaptureAsync(
        string connectionString,
        IReadOnlyList<V22ExpectedModule> expectedModules,
        string closurePath,
        string parityPath)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var identityRows = await ReadRowsAsync(connection, """
SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) AS ServerName,
       DB_NAME() AS DatabaseName, DB_ID() AS DatabaseId, d.state_desc AS DatabaseState,
       fc.Historical_Report_Mode AS HistoricalReportMode
FROM sys.databases d
LEFT JOIN dbo.Inventory_Report_Fence_Config fc ON fc.Config_ID = 1
WHERE d.database_id = DB_ID();
""");
        if (identityRows.Count != 1)
            throw new InvalidDataException("Could not capture one database identity/mode row.");

        var counts = await ReadRowsAsync(connection, """
SELECT t.name AS TableName, COALESCE(SUM(CONVERT(bigint, p.row_count)), 0) AS ApproxRows
FROM (VALUES
    (N'tbl_DM_San_Pham'), (N'tbl_DM_Kho'), (N'tbl_DM_Kho_User'),
    (N'tbl_XNK_Nhap_Kho'), (N'tbl_XNK_Nhap_Kho_Raw_Data'),
    (N'Inventory_Balance_Daily_Scope'), (N'Inventory_Balance_Daily'),
    (N'InventoryBalance_Current'), (N'Inventory_Movement_Daily'),
    (N'Inventory_Report_Scope_Catalog'), (N'InventoryMovement_RebuildQueue'),
    (N'InventorySnapshot_RebuildQueue')
) names(TableName)
LEFT JOIN sys.tables t ON t.name = names.TableName
LEFT JOIN sys.dm_db_partition_stats p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
GROUP BY t.name, names.TableName
ORDER BY names.TableName;
""");

        var productFingerprint = await ReadRowsAsync(connection, """
SELECT TOP (20) Auto_ID, Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID
FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;
""");
        var receiptFingerprint = await ReadRowsAsync(connection, """
SELECT TOP (20) Auto_ID, So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted
FROM dbo.tbl_XNK_Nhap_Kho ORDER BY Auto_ID DESC;
""");
        var receiptDetailFingerprint = await ReadRowsAsync(connection, """
SELECT TOP (20) Auto_ID, Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap
FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data ORDER BY Auto_ID DESC;
""");
        var currentFingerprint = await ReadRowsAsync(connection, """
SELECT TOP (20) Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity
FROM dbo.InventoryBalance_Current ORDER BY Kho_ID, San_Pham_ID;
""");
        var dailyFingerprint = await ReadRowsAsync(connection, """
SELECT TOP (20) Kho_ID, San_Pham_ID, Balance_Date, OpeningQuantity,
       TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid
FROM dbo.Inventory_Balance_Daily
ORDER BY Balance_Date DESC, Kho_ID, San_Pham_ID;
""");
        var allowedRows = await ReadRowsAsync(connection, """
SELECT Kho_ID FROM dbo.tbl_DM_Kho_User
WHERE Ma_Dang_Nhap = @LoginName ORDER BY Kho_ID;
""", command => command.Parameters.Add("@LoginName", System.Data.SqlDbType.NVarChar, 100).Value = V22CorrectnessOracle.LoginName);
        var allowedIds = allowedRows.Select(row => Convert.ToString(row["Kho_ID"], CultureInfo.InvariantCulture)!).ToArray();
        var queues = await ReadRowsAsync(connection, """
SELECT N'InventoryMovement_RebuildQueue' AS QueueName, Status, COUNT_BIG(*) AS QueueRows
FROM dbo.InventoryMovement_RebuildQueue GROUP BY Status
UNION ALL
SELECT N'InventorySnapshot_RebuildQueue', Status, COUNT_BIG(*)
FROM dbo.InventorySnapshot_RebuildQueue GROUP BY Status
ORDER BY QueueName, Status;
""");
        var currentState = await ReadRowsAsync(connection, """
SELECT State_ID, Generation FROM dbo.Inventory_Current_Report_State ORDER BY State_ID;
""");
        var moduleRows = await ReadModuleRowsAsync(connection, expectedModules);
        foreach (var row in moduleRows)
        {
            var definition = Convert.ToString(row["DefinitionText"], CultureInfo.InvariantCulture) ?? "";
            row["DefinitionSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(definition))).ToLowerInvariant();
            row.Remove("DefinitionText");
        }
        var expectedHashMap = expectedModules.ToDictionary(
            item => item.Schema + "." + item.Name,
            item => item.LiveDefinitionSha256,
            StringComparer.OrdinalIgnoreCase);
        var moduleHashMatch = moduleRows.Count == expectedModules.Count && moduleRows.All(row =>
        {
            var key = Convert.ToString(row["SchemaName"], CultureInfo.InvariantCulture) + "." +
                      Convert.ToString(row["ObjectName"], CultureInfo.InvariantCulture);
            var actual = Convert.ToString(row["DefinitionSha256"], CultureInfo.InvariantCulture);
            return actual is not null && expectedHashMap.TryGetValue(key, out var expected) &&
                   string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        });

        var databaseIdentity = identityRows[0];
        var core = new
        {
            DatabaseIdentity = databaseIdentity,
            ApproxTableRowCounts = counts,
            BoundedFingerprints = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["ProductsTop20"] = HashRows(productFingerprint),
                ["ReceiptHeadersTop20"] = HashRows(receiptFingerprint),
                ["ReceiptDetailsTop20"] = HashRows(receiptDetailFingerprint),
                ["CurrentBalancesTop20"] = HashRows(currentFingerprint),
                ["DailyBalancesTop20"] = HashRows(dailyFingerprint),
                ["PERF_USER_WarehouseMappings"] = HashRows(allowedRows)
            },
            PERF_USER_WarehouseMappingCount = allowedIds.Length,
            PERF_USER_WarehouseIds = allowedIds,
            QueueStatusCounts = queues,
            CurrentReportState = currentState,
            ActiveClosureModuleIdentityAndHashes = moduleRows,
            ExpectedActiveModuleHashMatch = moduleHashMatch,
            ClosureArtifactSha256 = Sha256File(closurePath),
            DefinitionParityArtifactSha256 = Sha256File(parityPath),
            SnapshotScope = "bounded fingerprints plus approximate partition row counts; not full data equality"
        };
        var snapshotHash = Sha256(SerializeCanonical(core));
        var evidence = new
        {
            CapturedUtc = DateTime.UtcNow,
            SnapshotSha256 = snapshotHash,
            Data = core
        };
        return new V22SnapshotCapture(
            DateTime.UtcNow,
            snapshotHash,
            moduleHashMatch,
            Convert.ToString(databaseIdentity["DatabaseName"], CultureInfo.InvariantCulture) ?? "",
            Convert.ToInt32(databaseIdentity["DatabaseId"], CultureInfo.InvariantCulture),
            Convert.ToString(databaseIdentity["DatabaseState"], CultureInfo.InvariantCulture) ?? "",
            Convert.ToString(databaseIdentity["HistoricalReportMode"], CultureInfo.InvariantCulture) ?? "",
            allowedIds,
            evidence);
    }

    private static async Task<List<Dictionary<string, object?>>> ReadRowsAsync(
        SqlConnection connection,
        string sql,
        Action<SqlCommand>? configure = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 15;
        command.CommandText = sql;
        configure?.Invoke(command);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var value = reader.GetValue(index);
                row[reader.GetName(index)] = ToStableValue(value is DBNull ? null : value);
            }
            rows.Add(row);
        }
        return rows;
    }

    private static async Task<List<Dictionary<string, object?>>> ReadModuleRowsAsync(
        SqlConnection connection,
        IReadOnlyList<V22ExpectedModule> modules)
    {
        var predicates = modules.Select((_, index) => $"(s.name = @Schema{index} AND o.name = @Name{index})");
        var sql = """
SELECT s.name AS SchemaName, o.name AS ObjectName, o.object_id AS ObjectId,
       o.type_desc AS TypeDescription,
       m.definition AS DefinitionText
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN sys.sql_modules m ON m.object_id = o.object_id
WHERE
""" + string.Join(" OR ", predicates) + " ORDER BY s.name, o.name;";

        return await ReadRowsAsync(connection, sql, command =>
        {
            for (var index = 0; index < modules.Count; index++)
            {
                command.Parameters.Add($"@Schema{index}", System.Data.SqlDbType.NVarChar, 128).Value = modules[index].Schema;
                command.Parameters.Add($"@Name{index}", System.Data.SqlDbType.NVarChar, 128).Value = modules[index].Name;
            }
        });
    }

    private static string HashRows(IReadOnlyList<Dictionary<string, object?>> rows) => Sha256(SerializeCanonical(rows));

    private static string SerializeCanonical(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private static object? ToStableValue(object? value) => value switch
    {
        null => null,
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        decimal number => number.ToString("G29", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToHexString(bytes),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Sha256File(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
