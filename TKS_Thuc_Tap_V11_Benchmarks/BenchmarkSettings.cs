using System.Globalization;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Benchmarks;

public sealed record BenchmarkSettings
{
    public int RecordCount { get; init; } = 100_000;
    public int PageSize { get; init; } = 10;
    public int NBomberCopies { get; init; } = 8;
    public int NBomberDurationSeconds { get; init; } = 15;
    public DateTime ReportFromDate { get; init; } = new(2025, 1, 1);
    public DateTime ReportToDate { get; init; } = new(2026, 12, 31);
    public string LoginName { get; init; } = "PERF_USER";
    public string ConnectionString { get; init; } = "";
    public string ReportDirectory { get; init; } = "docs/testing/performance/tool-benchmarks";
    public IReadOnlyList<string> NBomberScenarioNames { get; init; } = WarehouseScenarioCatalog.DefaultNames;
    public bool RunDatabaseBenchmarks { get; init; }
    public bool UseCurrentInventoryBalance { get; init; }

    public bool DatabaseConfigured => !string.IsNullOrWhiteSpace(ConnectionString);

    public static BenchmarkSettings FromEnvironment(IReadOnlyDictionary<string, string?>? p_environment = null)
    {
        p_environment ??= Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(v_item => (string)v_item.Key, v_item => v_item.Value?.ToString());

        return new BenchmarkSettings
        {
            RecordCount = ReadInt(p_environment, "TKS_PERF_ROWS", 100_000, 1, 10_000_000),
            PageSize = ReadInt(p_environment, "TKS_PERF_PAGE_SIZE", 10, 1, 10_000),
            NBomberCopies = ReadInt(p_environment, "TKS_NBOMBER_COPIES", 8, 1, 256),
            NBomberDurationSeconds = ReadInt(p_environment, "TKS_NBOMBER_DURATION_SECONDS", 15, 1, 3_600),
            ReportFromDate = ReadDate(p_environment, "TKS_PERF_FROM_DATE", new DateTime(2025, 1, 1)),
            ReportToDate = ReadDate(p_environment, "TKS_PERF_TO_DATE", new DateTime(2026, 12, 31)),
            LoginName = ReadString(p_environment, "TKS_PERF_LOGIN") is { Length: > 0 } v_loginName
                ? v_loginName
                : "PERF_USER",
            ConnectionString = ReadString(p_environment, "TKS_PERF_CONNECTION_STRING"),
            ReportDirectory = ReadString(p_environment, "TKS_BENCH_REPORT_DIR") is { Length: > 0 } v_reportDirectory
                ? v_reportDirectory
                : "docs/testing/performance/tool-benchmarks",
            NBomberScenarioNames = ReadScenarioNames(p_environment),
            RunDatabaseBenchmarks = ReadBool(p_environment, "TKS_BDN_DATABASE"),
            UseCurrentInventoryBalance = ReadBool(p_environment, "TKS_PERF_USE_CURRENT_BALANCE")
        };
    }

    public void RequireDatabase()
    {
        if (!DatabaseConfigured)
            throw new InvalidOperationException(
                "TKS_PERF_CONNECTION_STRING is required for database benchmarks and NBomber.");

        CConfig.TKS_Thuc_Tap_V11_Conn_String = ConnectionString;
        CLogger.Enable_Trace = false;
    }

    private static int ReadInt(
        IReadOnlyDictionary<string, string?> p_environment,
        string p_name,
        int p_default,
        int p_min,
        int p_max)
    {
        return int.TryParse(ReadString(p_environment, p_name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v_value)
            ? Math.Clamp(v_value, p_min, p_max)
            : p_default;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, string?> p_environment, string p_name)
    {
        return ReadString(p_environment, p_name) is "1" or "true" or "TRUE" or "yes" or "YES";
    }

    private static DateTime ReadDate(
        IReadOnlyDictionary<string, string?> p_environment,
        string p_name,
        DateTime p_default)
    {
        return DateTime.TryParseExact(
            ReadString(p_environment, p_name),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var v_value)
            ? v_value
            : p_default;
    }

    private static string ReadString(IReadOnlyDictionary<string, string?> p_environment, string p_name)
    {
        return p_environment.TryGetValue(p_name, out var v_value) ? v_value ?? "" : "";
    }

    private static IReadOnlyList<string> ReadScenarioNames(IReadOnlyDictionary<string, string?> p_environment)
    {
        var v_rawNames = ReadString(p_environment, "TKS_NBOMBER_SCENARIOS");
        if (string.IsNullOrWhiteSpace(v_rawNames))
            return WarehouseScenarioCatalog.DefaultNames;

        var v_names = v_rawNames
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(WarehouseScenarioCatalog.Canonicalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var v_unknownNames = v_names
            .Except(WarehouseScenarioCatalog.Names, StringComparer.Ordinal)
            .ToArray();
        if (v_unknownNames.Length > 0)
            throw new ArgumentException(
                $"Unknown NBomber scenario name(s): {string.Join(", ", v_unknownNames)}",
                "TKS_NBOMBER_SCENARIOS");

        return v_names;
    }
}

public static class WarehouseScenarioCatalog
{
    public static IReadOnlyList<string> Names { get; } = new[]
    {
        "MasterPaged",
        "LookupPaged",
        "DocumentPaged",
        "DetailReportPaged",
        "InventoryHistoricalReportPaged",
        "InventoryCurrentBalancePaged"
    };

    public static IReadOnlyList<string> DefaultNames { get; } = new[]
    {
        "MasterPaged",
        "LookupPaged",
        "DocumentPaged",
        "DetailReportPaged",
        "InventoryHistoricalReportPaged"
    };

    public static string Canonicalize(string p_name)
    {
        return p_name switch
        {
            "InventoryReportPaged" => "InventoryHistoricalReportPaged",
            _ => p_name
        };
    }
}
