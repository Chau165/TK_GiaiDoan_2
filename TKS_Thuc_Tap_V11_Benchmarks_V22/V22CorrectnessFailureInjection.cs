using System.Text.Json;

namespace TKS_Thuc_Tap_V11_Benchmarks_V22;

internal static class V22CorrectnessFailureInjection
{
    internal sealed record GateVector(
        bool RootMatch = true,
        bool ParameterTypeMatch = true,
        bool RequiredResultColumnsPresent = true,
        bool TotalCountMatch = true,
        bool ItemsCountMatch = true,
        bool ExpectedKeyPresent = true,
        bool MappedQuantityMatch = true,
        int ActualItems = 10,
        int ExpectedItems = 10,
        bool AllowZeroRows = false,
        bool SqlDefinitionMatch = true,
        bool NoUnresolvedActiveDependency = true,
        bool SecurityPositivePresent = true,
        bool SecurityNegativeNoLeakage = true,
        bool InventoryArithmeticMatch = true,
        bool HistoricalModeMatch = true,
        bool PrePostMatch = true,
        bool OracleIndependent = true);

    private sealed record Injection(
        string TestId,
        string ExpectedCode,
        GateVector Input);

    internal static IReadOnlyList<string> Validate(GateVector input)
    {
        var failures = new List<string>();
        if (!input.RootMatch) failures.Add("ROOT_SP_MISMATCH");
        if (!input.ParameterTypeMatch) failures.Add("PARAMETER_TYPE_MISMATCH");
        if (!input.RequiredResultColumnsPresent) failures.Add("MISSING_REQUIRED_RESULT_COLUMN");
        if (!input.TotalCountMatch) failures.Add("TOTAL_COUNT_MISMATCH");
        if (!input.ItemsCountMatch || input.ActualItems != input.ExpectedItems)
            failures.Add("ITEMS_COUNT_MISMATCH");
        if (!input.ExpectedKeyPresent) failures.Add("EXPECTED_KEY_MISSING");
        if (!input.MappedQuantityMatch) failures.Add("MAPPED_QUANTITY_MISMATCH");
        if (input.ActualItems == 0 && !input.AllowZeroRows)
            failures.Add("UNEXPECTED_ZERO_ROWS");
        if (!input.SqlDefinitionMatch) failures.Add("SQL_DEFINITION_MISMATCH");
        if (!input.NoUnresolvedActiveDependency) failures.Add("UNRESOLVED_ACTIVE_DEPENDENCY");
        if (!input.SecurityPositivePresent) failures.Add("SECURITY_POSITIVE_MISSING");
        if (!input.SecurityNegativeNoLeakage) failures.Add("SECURITY_NEGATIVE_LEAKAGE");
        if (!input.InventoryArithmeticMatch) failures.Add("INVENTORY_ARITHMETIC_MISMATCH");
        if (!input.HistoricalModeMatch) failures.Add("HISTORICAL_MODE_MISMATCH");
        if (!input.PrePostMatch) failures.Add("PRE_POST_FINGERPRINT_MISMATCH");
        if (!input.OracleIndependent) failures.Add("SELF_CONFIRMING_ORACLE");
        return failures;
    }

    public static int Run(string? outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            Console.Error.WriteLine("failure-injection requires a new output root.");
            return 64;
        }
        if (Directory.Exists(outputRoot) || File.Exists(outputRoot))
            throw new IOException("Failure-injection output root already exists.");

        Directory.CreateDirectory(outputRoot);
        var evidencePath = Path.Combine(outputRoot, "V22-Correctness-FailureInjection-Results.json");
        var cases = new[]
        {
            new Injection("CI01_ROOT_SP_MISMATCH", "ROOT_SP_MISMATCH", new(RootMatch: false)),
            new Injection("CI02_PARAMETER_TYPE_MISMATCH", "PARAMETER_TYPE_MISMATCH", new(ParameterTypeMatch: false)),
            new Injection("CI03_MISSING_RESULT_COLUMN", "MISSING_REQUIRED_RESULT_COLUMN", new(RequiredResultColumnsPresent: false)),
            new Injection("CI04_TOTAL_COUNT_MISMATCH", "TOTAL_COUNT_MISMATCH", new(TotalCountMatch: false)),
            new Injection("CI05_ITEMS_COUNT_MISMATCH", "ITEMS_COUNT_MISMATCH", new(ItemsCountMatch: false)),
            new Injection("CI06_EXPECTED_KEY_MISSING", "EXPECTED_KEY_MISSING", new(ExpectedKeyPresent: false)),
            new Injection("CI07_MAPPED_QUANTITY_MISMATCH", "MAPPED_QUANTITY_MISMATCH", new(MappedQuantityMatch: false)),
            new Injection("CI08_UNEXPECTED_ZERO_ROWS", "UNEXPECTED_ZERO_ROWS", new(ActualItems: 0, ExpectedItems: 0)),
            new Injection("CI09_SQL_DEFINITION_MISMATCH", "SQL_DEFINITION_MISMATCH", new(SqlDefinitionMatch: false)),
            new Injection("CI10_UNRESOLVED_ACTIVE_DEPENDENCY", "UNRESOLVED_ACTIVE_DEPENDENCY", new(NoUnresolvedActiveDependency: false)),
            new Injection("CI11_SECURITY_POSITIVE_MISSING", "SECURITY_POSITIVE_MISSING", new(SecurityPositivePresent: false)),
            new Injection("CI12_SECURITY_NEGATIVE_LEAKAGE", "SECURITY_NEGATIVE_LEAKAGE", new(SecurityNegativeNoLeakage: false)),
            new Injection("CI13_INVENTORY_ARITHMETIC_MISMATCH", "INVENTORY_ARITHMETIC_MISMATCH", new(InventoryArithmeticMatch: false)),
            new Injection("CI14_HISTORICAL_MODE_MISMATCH", "HISTORICAL_MODE_MISMATCH", new(HistoricalModeMatch: false)),
            new Injection("CI15_PRE_POST_MISMATCH", "PRE_POST_FINGERPRINT_MISMATCH", new(PrePostMatch: false)),
            new Injection("CI16_SELF_CONFIRMING_ORACLE", "SELF_CONFIRMING_ORACLE", new(OracleIndependent: false))
        };

        var results = cases.Select(test =>
        {
            var actual = Validate(test.Input);
            var passed = actual.Contains(test.ExpectedCode, StringComparer.Ordinal);
            return new
            {
                test.TestId,
                Input = test.Input,
                Expected = test.ExpectedCode,
                Actual = actual,
                Status = passed ? "PASS" : "FAIL",
                EvidencePath = "V22-Correctness-FailureInjection-Results.json"
            };
        }).ToArray();

        var passedCount = results.Count(result => result.Status == "PASS");
        var report = new
        {
            Protocol = "WarehouseBenchmarkV2.2",
            Mode = "OFFLINE_SYNTHETIC_CORRECTNESS_FAILURE_INJECTION",
            CapturedUtc = DateTime.UtcNow,
            DatabaseAccessed = false,
            PerformanceWorkloadRun = false,
            Results = results,
            Passed = passedCount,
            Total = results.Length,
            Status = passedCount == results.Length ? "PASS_16_OF_16" : "FAIL"
        };
        File.WriteAllText(
            evidencePath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine(JsonSerializer.Serialize(
            new { report.Status, report.Passed, report.Total, EvidencePath = evidencePath }));
        return passedCount == results.Length ? 0 : 1;
    }
}
