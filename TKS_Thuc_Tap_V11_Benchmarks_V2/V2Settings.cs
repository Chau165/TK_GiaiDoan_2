using System.Globalization;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Benchmarks_V2;

public static class V2Constants
{
    public const string ExpectedServer = @"localhost\MSSQLSERVER19";
    public const string ExpectedDatabase = "TKS_Thuc_Tap_V11_Perf_10000000";
    public const int ExpectedDatabaseId = 5;
    public const int PageNumber = 1;
    public const int PageSize = 10;
    public const int ApplicationTimeoutSeconds = 30;
    public const int BdnLaunchCount = 1;
    public const int BdnWarmupCount = 2;
    public const int BdnIterationCount = 5;
    public const int BdnInvocationCount = 1;
    public const int BdnUnrollFactor = 1;
    public const int NbomberWarmupSeconds = 3;
    public const int NbomberDurationSeconds = 15;
    public const int HostHardStopMb = 512;
    public const int HostBlockStartMb = 1024;
    public const int CooldownMinimumSeconds = 5;
    public const int CooldownMaximumSeconds = 60;
    public const int TelemetryIntervalMilliseconds = 1000;

    public static readonly IReadOnlyList<string> Scenarios = new[]
    {
        "MasterPaged",
        "LookupPaged",
        "DocumentPaged",
        "DetailReportPaged",
        "InventoryHistoricalReportPaged",
        "InventoryCurrentBalancePaged"
    };

    public static readonly IReadOnlyDictionary<string, long> DatasetExpectedRows =
        new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["tbl_XNK_Nhap_Kho"] = 500_000,
            ["tbl_XNK_Nhap_Kho_Raw_Data"] = 5_000_000,
            ["tbl_XNK_Xuat_Kho"] = 500_000,
            ["tbl_XNK_Xuat_Kho_Raw_Data"] = 5_000_000,
            ["tbl_DM_San_Pham"] = 10_000,
            ["Inventory_Movement_Daily"] = 730_000,
            ["Inventory_Balance_Daily"] = 730_000,
            ["Inventory_Balance_Daily_Scope"] = 10_000,
            ["Inventory_Report_Scope_Catalog"] = 10_000,
            ["InventoryBalance_Current"] = 10_000
        };

    public static string CanonicalizeScenario(string p_name)
    {
        return p_name switch
        {
            "InventoryReportPaged" => "InventoryHistoricalReportPaged",
            _ => p_name
        };
    }
}

public sealed record V2Settings
{
    public string ConnectionString { get; init; } = "";
    public string LoginName { get; init; } = "PERF_USER";
    public int PageSize { get; init; } = V2Constants.PageSize;
    public DateTime ReportFromDate { get; init; } = new(2025, 1, 1);
    public DateTime ReportToDate { get; init; } = new(2026, 12, 31);
    public int BdnWarmupCount { get; init; } = V2Constants.BdnWarmupCount;
    public int BdnIterationCount { get; init; } = V2Constants.BdnIterationCount;
    public int NbomberWarmupSeconds { get; init; } = V2Constants.NbomberWarmupSeconds;
    public int NbomberDurationSeconds { get; init; } = V2Constants.NbomberDurationSeconds;
    public string Scenario { get; init; } = "";
    public string OutputDirectory { get; init; } = "";

    public bool DatabaseConfigured => !string.IsNullOrWhiteSpace(ConnectionString);

    public static V2Settings FromEnvironment()
    {
        return new V2Settings
        {
            ConnectionString = ReadString("TKS_V2_CONNECTION_STRING"),
            LoginName = ReadString("TKS_V2_LOGIN") is { Length: > 0 } v_login ? v_login : "PERF_USER",
            PageSize = ReadInt("TKS_V2_PAGE_SIZE", V2Constants.PageSize, 1, 10_000),
            ReportFromDate = ReadDate("TKS_V2_FROM_DATE", new DateTime(2025, 1, 1)),
            ReportToDate = ReadDate("TKS_V2_TO_DATE", new DateTime(2026, 12, 31)),
            BdnWarmupCount = ReadInt("TKS_V2_BDN_WARMUP", V2Constants.BdnWarmupCount, 1, 10),
            BdnIterationCount = ReadInt("TKS_V2_BDN_ITERATIONS", V2Constants.BdnIterationCount, 1, 50),
            NbomberWarmupSeconds = ReadInt("TKS_V2_NBOMBER_WARMUP_SECONDS", V2Constants.NbomberWarmupSeconds, 0, 600),
            NbomberDurationSeconds = ReadInt("TKS_V2_NBOMBER_DURATION_SECONDS", V2Constants.NbomberDurationSeconds, 1, 3_600),
            Scenario = ReadString("TKS_V2_SCENARIO"),
            OutputDirectory = ReadString("TKS_V2_OUTPUT_DIRECTORY")
        };
    }

    public void ConfigureDataAccess()
    {
        if (!DatabaseConfigured)
            throw new InvalidOperationException(
                "TKS_V2_CONNECTION_STRING is required for database modes.");

        CConfig.TKS_Thuc_Tap_V11_Conn_String = ConnectionString;
        CLogger.Enable_Trace = false;
    }

    public V2Settings WithScenario(string p_scenario)
    {
        var v_canonical = V2Constants.CanonicalizeScenario(p_scenario);
        if (!V2Constants.Scenarios.Contains(v_canonical, StringComparer.Ordinal))
            throw new ArgumentException($"Unknown V2 scenario: {p_scenario}", nameof(p_scenario));
        return this with { Scenario = v_canonical };
    }

    private static string ReadString(string p_name)
    {
        return Environment.GetEnvironmentVariable(p_name) ?? "";
    }

    private static int ReadInt(string p_name, int p_default, int p_min, int p_max)
    {
        return int.TryParse(ReadString(p_name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v_value)
            ? Math.Clamp(v_value, p_min, p_max)
            : p_default;
    }

    private static DateTime ReadDate(string p_name, DateTime p_default)
    {
        return DateTime.TryParseExact(
            ReadString(p_name),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var v_value)
            ? v_value
            : p_default;
    }
}
