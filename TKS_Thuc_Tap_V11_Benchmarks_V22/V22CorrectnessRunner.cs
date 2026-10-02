using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Benchmarks_V22;

internal static class V22CorrectnessRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] Scenarios =
    [
        "MasterPaged", "LookupPaged", "DocumentPaged", "DetailReportPaged",
        "InventoryHistoricalReportPaged", "InventoryCurrentBalancePaged"
    ];

    private sealed record Options(string OutputRoot, string Phase3Path, string ClosurePath, string ParityPath, string SecurityPath);
    private sealed record ActualPage(long TotalCount, List<Dictionary<string, object?>> Rows);
    private sealed record ScenarioEvaluation(string Scenario, string RootProcedure, string OracleConfidence,
        string OracleSource, string Status, long? ExpectedTotalCount, long? ActualTotalCount,
        int? ExpectedItemsCount, int? ActualItemsCount, int? DuplicateMappedKeyCount,
        IReadOnlyDictionary<string, bool> Checks, IReadOnlyList<string> Errors,
        IReadOnlyList<string> ExpectedKeys, IReadOnlyList<string> ActualKeys,
        IReadOnlyList<object> RowComparisons);

    internal static async Task<int> RunAsync(string[] args)
    {
        Options options;
        try
        {
            options = ParseOptions(args);
            if (Directory.Exists(options.OutputRoot) || File.Exists(options.OutputRoot))
                throw new IOException("Correctness output root already exists; run roots are never reused.");
            Directory.CreateDirectory(options.OutputRoot);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(SafeMessage(exception.Message));
            return 64;
        }

        V22SnapshotCapture? pre = null;
        V22SnapshotCapture? post = null;
        var evaluations = new List<ScenarioEvaluation>();
        var oraclePages = new Dictionary<string, V22OraclePage>(StringComparer.Ordinal);
        var actualPages = new Dictionary<string, ActualPage>(StringComparer.Ordinal);
        var failure = (Stage: "INPUT_VALIDATION", Code: "", Message: "");
        var phase3Status = "NOT_VERIFIED";
        var securityDisposition = "NOT_VERIFIED";
        var securitySource = "";
        var securitySourceSha256 = "";
        var connectionSource = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TKS_V22_CONNECTION_STRING"))
            ? "DATA_ACCESS_CCONFIG_EXISTING"
            : "TKS_V22_CONNECTION_STRING_PROCESS_ENVIRONMENT";
        var securityPositive = false;
        var prePostStatus = "NOT_CAPTURED";
        var runStartedUtc = DateTime.UtcNow;
        IReadOnlyList<V22ExpectedModule> modules = Array.Empty<V22ExpectedModule>();

        try
        {
            phase3Status = ValidatePhase3Evidence(options.Phase3Path, options.ClosurePath, options.ParityPath);
            (securityDisposition, securitySource, securitySourceSha256) = ValidateSecurityEvidence(options.SecurityPath);
            modules = V22CorrectnessSnapshot.LoadModules(options.ClosurePath, options.ParityPath);

            var connectionString = CConfig.TKS_Thuc_Tap_V11_Conn_String;
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidDataException("No connection string is configured in the process environment or Data Access configuration.");

            failure = ("DATABASE_PRE_SNAPSHOT", "", "");
            pre = await V22CorrectnessSnapshot.CaptureAsync(connectionString, modules, options.ClosurePath, options.ParityPath);
            if (!pre.ExpectedModuleHashesMatch)
                throw new GateException("SQL_CONTRACT_DRIFT", "Live active SQL module identity/hash differs from Phase 3 parity evidence.");
            if (!string.Equals(pre.DatabaseName, "TKS_Thuc_Tap_V11_Perf_10000000", StringComparison.Ordinal) ||
                pre.DatabaseId != 5 || !string.Equals(pre.DatabaseState, "ONLINE", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(pre.HistoricalReportMode, "LEGACY", StringComparison.Ordinal))
                throw new GateException("DATABASE_PREFLIGHT_MISMATCH", "Current database identity or Historical_Report_Mode does not match the approved benchmark target.");
            if (pre.AllowedWarehouseIds.Count != 100 || pre.AllowedWarehouseIds.Distinct(StringComparer.Ordinal).Count() != 100)
                throw new GateException("SECURITY_FIXTURE_DRIFT", "PERF_USER live warehouse mapping no longer matches the verified 100-warehouse contract.");

            await using var oracleConnection = new SqlConnection(connectionString);
            await oracleConnection.OpenAsync();
            File.WriteAllText(Path.Combine(options.OutputRoot, "V22-Oracle-Queries.sql"), V22CorrectnessOracle.SqlBundle, new UTF8Encoding(false));

            foreach (var scenario in Scenarios)
            {
                try
                {
                    failure = ($"ORACLE_{scenario}", "", "");
                    var oracle = await V22CorrectnessOracle.ReadAsync(oracleConnection, scenario);
                    oraclePages[scenario] = oracle;

                    failure = ($"DATA_ACCESS_{scenario}", "", "");
                    var actual = await RunDataAccessAsync(scenario);
                    actualPages[scenario] = actual;

                    var evaluation = EvaluateScenario(scenario, oracle, actual);
                    evaluations.Add(evaluation);
                }
                catch (Exception exception)
                {
                    failure = (failure.Stage,
                        exception is SqlException sqlException && sqlException.Number == -2
                            ? failure.Stage.StartsWith("DATA_ACCESS_", StringComparison.Ordinal) ? "APPLICATION_PATH_TIMEOUT" : "ORACLE_QUERY_TIMEOUT"
                            : failure.Stage.StartsWith("DATA_ACCESS_", StringComparison.Ordinal) ? "BUSINESS_CORRECTNESS_FINDING" : "CORRECTNESS_EXECUTION_FAILED",
                        SafeMessage(exception.Message));
                    break;
                }
            }

            if (actualPages.TryGetValue("DocumentPaged", out var documentPage))
            {
                var allowed = pre.AllowedWarehouseIds.ToHashSet(StringComparer.Ordinal);
                var securityRows = new[] { "DocumentPaged", "InventoryHistoricalReportPaged", "InventoryCurrentBalancePaged" }
                    .Where(actualPages.ContainsKey)
                    .SelectMany(name => actualPages[name].Rows)
                    .ToArray();
                securityPositive = documentPage.Rows.Count > 0 && securityRows.Length > 0 && securityRows.All(row =>
                    row.TryGetValue("Kho_ID", out var value) && allowed.Contains(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""));
            }
        }
        catch (GateException exception)
        {
            failure = (failure.Stage, exception.Code, SafeMessage(exception.Message));
        }
        catch (Exception exception)
        {
            var code = exception is SqlException sqlException && sqlException.Number == -2
                ? failure.Stage.StartsWith("DATA_ACCESS_", StringComparison.Ordinal) ? "APPLICATION_PATH_TIMEOUT" : "DATABASE_QUERY_TIMEOUT"
                : "CORRECTNESS_HARNESS_FAILED";
            failure = (failure.Stage, code, SafeMessage(exception.Message));
        }
        finally
        {
            if (pre is not null)
            {
                try
                {
                    post = await V22CorrectnessSnapshot.CaptureAsync(
                        CConfig.TKS_Thuc_Tap_V11_Conn_String, modules, options.ClosurePath, options.ParityPath);
                    prePostStatus = pre.SnapshotSha256 == post.SnapshotSha256 && post.ExpectedModuleHashesMatch
                        ? "PASS"
                        : "INVALID_CORRECTNESS_RUN";
                }
                catch (Exception exception)
                {
                    prePostStatus = "POST_SNAPSHOT_FAILED";
                    if (string.IsNullOrEmpty(failure.Code))
                        failure = ("DATABASE_POST_SNAPSHOT", "POST_SNAPSHOT_FAILED", SafeMessage(exception.Message));
                }
            }
        }

        var allScenarioPass = evaluations.Count == 6 && evaluations.All(item => item.Status == "PASS");
        var securityPass = securityDisposition == "NOT_APPLICABLE_BY_CONTRACT" && securityPositive;
        var semanticPass = allScenarioPass && securityPass && prePostStatus == "PASS" && string.IsNullOrEmpty(failure.Code);
        var finalStatus = semanticPass ? "PASS_6_OF_6" :
            failure.Code == "APPLICATION_PATH_TIMEOUT" ? "APPLICATION_PATH_TIMEOUT" :
            prePostStatus == "INVALID_CORRECTNESS_RUN" ? "INVALID_CORRECTNESS_RUN" : "SEMANTIC_CORRECTNESS_FAILED";

        WriteOutput(options.OutputRoot, "V22-Semantic-Fixtures.json", new
        {
            Protocol = "WarehouseBenchmarkV2.2",
            Source = "Independent parameterized SELECTs over base tables and inventory read models; root stored procedures are never called for expected values.",
            PageNumber = V22CorrectnessOracle.PageNumber,
            PageSize = V22CorrectnessOracle.PageSize,
            Search = "empty",
            LoginName = V22CorrectnessOracle.LoginName,
            WarehouseId = (long?)null,
            ReceiptFlag = true,
            FromDate = V22CorrectnessOracle.ReportFrom,
            ToDate = V22CorrectnessOracle.ReportTo,
            HistoricalReportMode = pre?.HistoricalReportMode ?? "NOT_CAPTURED",
            Scenarios = oraclePages.Select(pair => new
            {
                Scenario = pair.Key,
                pair.Value.RootProcedure,
                pair.Value.Confidence,
                ExpectedTotalCount = pair.Value.TotalCount,
                ExpectedPageRows = pair.Value.Rows,
                RawRowsBoundedToPageSize = true
            }).ToArray()
        });

        WriteOutput(options.OutputRoot, "V22-Semantic-Correctness-Results.json", new
        {
            Protocol = "WarehouseBenchmarkV2.2",
            RunStartedUtc = runStartedUtc,
            CompletedUtc = DateTime.UtcNow,
            Mode = "READ_ONLY_SEMANTIC_CORRECTNESS",
            ConnectionStringPersisted = false,
            PerformanceWorkloadRun = false,
            Phase3Status = phase3Status,
            SecurityDisposition = securityDisposition,
            SecurityPositiveStatus = securityPositive ? "PASS" : "FAIL_OR_NOT_RUN",
            ScenariosExpected = 6,
            ScenariosCompleted = evaluations.Count,
            TotalCountPassed = evaluations.Count(item => item.Checks.TryGetValue("Total_Count", out var passed) && passed),
            Status = finalStatus,
            Failure = string.IsNullOrEmpty(failure.Code) ? null : new { failure.Stage, failure.Code, failure.Message },
            Results = evaluations
        });

        WriteOutput(options.OutputRoot, "V22-Inventory-Correctness-Results.json", new
        {
            Protocol = "WarehouseBenchmarkV2.2",
            Historical = evaluations.FirstOrDefault(item => item.Scenario == "InventoryHistoricalReportPaged"),
            Current = evaluations.FirstOrDefault(item => item.Scenario == "InventoryCurrentBalancePaged"),
            Status = evaluations.Count(item => (item.Scenario == "InventoryHistoricalReportPaged" || item.Scenario == "InventoryCurrentBalancePaged") && item.Status == "PASS") == 2
                ? "PASS_HISTORICAL_AND_CURRENT"
                : "NOT_READY",
            ArithmeticContract = "Opening + period inbound - period outbound = ending",
            CurrentNullContract = "NULL SQL quantities are not equivalent to CLR decimal zero; a NULL expected quantity fails mapping validation."
        });

        WriteOutput(options.OutputRoot, "V22-Security-Correctness-Results.json", new
        {
            Protocol = "WarehouseBenchmarkV2.2",
            Disposition = securityDisposition,
            NegativeCaseStatus = securityDisposition == "NOT_APPLICABLE_BY_CONTRACT" ? "NOT_APPLICABLE_BY_CONTRACT" : "BLOCKED",
            PositiveCase = new
            {
                User = V22CorrectnessOracle.LoginName,
                Required = true,
                Status = securityPositive ? "PASS" : "FAIL_OR_NOT_RUN",
                CheckedRows = actualPages.Where(pair => pair.Key is "DocumentPaged" or "DetailReportPaged" or "InventoryHistoricalReportPaged" or "InventoryCurrentBalancePaged")
                    .Sum(pair => pair.Value.Rows.Count),
                AllowedWarehouseMappingCount = pre?.AllowedWarehouseIds.Count ?? 0,
                EvidenceSource = securitySource,
                EvidenceSourceSha256 = securitySourceSha256,
                ContentMatchedAllowedWarehouseMappings = securityPositive
            }
        });

        WriteOutput(options.OutputRoot, "V22-DB-PrePost-Evidence.json", new
        {
            Protocol = "WarehouseBenchmarkV2.2",
            Status = prePostStatus,
            EqualityScope = "database identity, Historical_Report_Mode, approximate relevant-table row counts, bounded value fingerprints, PERF_USER mapping, queue counts, current generation, and active closure object/definition identities",
            FullDatasetEqualityVerified = false,
            Pre = pre?.Evidence,
            Post = post?.Evidence,
            PrePostSha256Equal = pre is not null && post is not null && pre.SnapshotSha256 == post.SnapshotSha256,
            ExpectedActiveModuleHashesMatchPreAndPost = pre?.ExpectedModuleHashesMatch == true && post?.ExpectedModuleHashesMatch == true
        });

        WriteOutput(options.OutputRoot, "V22-Correctness-Run-Index.md", BuildRunIndex(options.OutputRoot, finalStatus, phase3Status, securityDisposition, pre, post, evaluations, failure));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Status = finalStatus,
            Phase3Status = phase3Status,
            SecurityDisposition = securityDisposition,
            SemanticScenarios = $"{evaluations.Count(item => item.Status == "PASS")}/6",
            TotalCountPass = $"{evaluations.Count(item => item.Checks.TryGetValue("Total_Count", out var pass) && pass)}/6",
            DbPrePost = prePostStatus,
            OutputRoot = options.OutputRoot,
            Failure = string.IsNullOrEmpty(failure.Code) ? null : failure.Code
        }, JsonOptions));
        return semanticPass ? 0 : 1;
    }

    private static Options ParseOptions(string[] args)
    {
        string? Get(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var output = Get("--output");
        var phase3 = Get("--phase3");
        var closure = Get("--closure");
        var parity = Get("--parity");
        var security = Get("--security");
        if (new[] { output, phase3, closure, parity, security }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Required: --output <new-root> --phase3 <json> --closure <json> --parity <json> --security <json>.");
        foreach (var path in new[] { phase3!, closure!, parity!, security! })
            if (!File.Exists(path)) throw new FileNotFoundException("Required Phase 3/security evidence is missing.", path);
        return new Options(Path.GetFullPath(output!), Path.GetFullPath(phase3!), Path.GetFullPath(closure!), Path.GetFullPath(parity!), Path.GetFullPath(security!));
    }

    private static string ValidatePhase3Evidence(string phase3Path, string closurePath, string parityPath)
    {
        using var phase3 = JsonDocument.Parse(File.ReadAllText(phase3Path));
        var status = phase3.RootElement.GetProperty("Status").GetString() ?? "";
        if (status != "PHASE3_SQL_CONTRACT_READY" ||
            phase3.RootElement.GetProperty("PassedGateCount").GetInt32() != phase3.RootElement.GetProperty("RequiredGateCount").GetInt32())
            throw new GateException("PHASE3_NOT_READY", "Phase 3 acceptance evidence is not PHASE3_SQL_CONTRACT_READY.");

        var closureValidationPath = Path.Combine(Path.GetDirectoryName(closurePath)!, "V22-SQL-ActiveLegacyClosure-Validation.json");
        using var closureValidation = JsonDocument.Parse(File.ReadAllText(closureValidationPath));
        var validation = closureValidation.RootElement;
        using var closure = JsonDocument.Parse(File.ReadAllText(closurePath));
        var classifications = closure.RootElement.GetProperty("Classifications").EnumerateArray().ToArray();
        if (validation.GetProperty("Status").GetString() != "PASS_CLOSURE_ARTIFACT_VALID" ||
            validation.GetProperty("NullRequiredFieldCount").GetInt32() != 0 ||
            validation.GetProperty("UnknownReachableCount").GetInt32() != 0 ||
            validation.GetProperty("UnresolvedDynamicCount").GetInt32() != 0 ||
            validation.GetProperty("DuplicateCount").GetInt32() != 0 ||
            validation.GetProperty("ActualNodeCount").GetInt32() != 72 ||
            validation.GetProperty("RequiredActiveModuleCount").GetInt32() != 17 ||
            validation.GetProperty("VerifiedRequiredModules").GetInt32() != 17 ||
            validation.GetProperty("KnownEdgeEndpointCount").GetInt32() != validation.GetProperty("ExpectedEdgeCount").GetInt32() ||
            closure.RootElement.GetProperty("Status").GetString() != "COMPLETE_FOR_CAPTURED_ARGUMENTS" ||
            closure.RootElement.GetProperty("NodeCount").GetInt32() != 72 ||
            closure.RootElement.GetProperty("EdgeCount").GetInt32() != 115 ||
            classifications.Length != 72 ||
            classifications.Any(item => string.IsNullOrWhiteSpace(item.GetProperty("ActiveStatus").GetString())))
            throw new GateException("PHASE3_CLOSURE_EVIDENCE_INVALID", "Active legacy closure validation does not satisfy its required-field, identity, or reachability gates.");

        using var parity = JsonDocument.Parse(File.ReadAllText(parityPath));
        var parityStatus = parity.RootElement.GetProperty("Status").GetString() ?? "";
        if (!parityStatus.StartsWith("PASS", StringComparison.Ordinal) ||
            parity.RootElement.GetProperty("VerifiedRequiredModules").GetInt32() != 17)
            throw new GateException("PHASE3_DEFINITION_PARITY_INVALID", "Definition parity evidence does not verify all 17 required active modules.");
        return status;
    }

    private static (string Disposition, string Source, string SourceHash) ValidateSecurityEvidence(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var disposition = root.GetProperty("NegativeCase").GetProperty("Disposition").GetString() ?? "";
        var contract = root.GetProperty("AuthoritativeFullAccessContract");
        var source = contract.GetProperty("Source").GetString() ?? "";
        var hash = contract.GetProperty("SourceSHA256").GetString() ?? "";
        if (disposition != "NOT_APPLICABLE_BY_CONTRACT" ||
            contract.GetProperty("Status").GetString() != "PROVEN_BY_BENCHMARK_SETUP" ||
            string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(hash) ||
            !root.GetProperty("PositiveCase").GetProperty("Required").GetBoolean())
            throw new GateException("SECURITY_DISPOSITION_UNRESOLVED", "Security evidence does not prove the authorized PERF_USER full-access contract and mandatory positive test.");
        return (disposition, source, hash);
    }

    private static async Task<ActualPage> RunDataAccessAsync(string scenario)
    {
        const int page = V22CorrectnessOracle.PageNumber;
        const int size = V22CorrectnessOracle.PageSize;
        const string login = V22CorrectnessOracle.LoginName;
        switch (scenario)
        {
            case "MasterPaged":
            {
                var result = await new CWarehouseMaster_Controller().List_Master_Page_Async("SanPham", page, size, "");
                return new ActualPage(result.Total_Count, result.Items.Select(item => Row(
                    ("Auto_ID", item.Auto_ID), ("Code", item.Code), ("Name", item.Name),
                    ("Related_ID", item.Related_ID), ("Related_ID_2", item.Related_ID_2), ("Ghi_Chu", item.Ghi_Chu))).ToList());
            }
            case "LookupPaged":
            {
                var result = await new CWarehouseMaster_Controller().List_Lookup_Page_Async("SanPham", page, size, "");
                return new ActualPage(result.Total_Count, result.Items.Select(item => Row(
                    ("Auto_ID", item.Auto_ID), ("Code", item.Code), ("Name", item.Name))).ToList());
            }
            case "DocumentPaged":
            {
                var result = await new CWarehouseDocument_Controller().List_Documents_Page_Async(true, page, size, "", login, null);
                return new ActualPage(result.Total_Count, result.Items.Select(item => Row(
                    ("Auto_ID", item.Auto_ID), ("Is_Receipt", item.Is_Receipt), ("So_Phieu", item.So_Phieu),
                    ("Kho_ID", item.Kho_ID), ("Ten_Kho", item.Ten_Kho), ("NCC_ID", item.NCC_ID),
                    ("Ten_NCC", item.Ten_NCC), ("Ngay_Chung_Tu", item.Ngay_Chung_Tu),
                    ("Is_Posted", item.Is_Posted), ("Ghi_Chu", item.Ghi_Chu))).ToList());
            }
            case "DetailReportPaged":
            {
                var result = await new CWarehouseReport_Controller().Detail_Report_Page_Async(
                    true, V22CorrectnessOracle.ReportFrom, V22CorrectnessOracle.ReportTo, page, size, login, null);
                return new ActualPage(result.Total_Count, result.Items.Select(item => Row(
                    ("Ngay", item.Ngay), ("So_Phieu", item.So_Phieu), ("Nha_Cung_Cap", item.Nha_Cung_Cap),
                    ("Ma_San_Pham", item.Ma_San_Pham), ("Ten_San_Pham", item.Ten_San_Pham),
                    ("So_Luong", item.So_Luong), ("Don_Gia", item.Don_Gia), ("Tri_Gia", item.Tri_Gia))).ToList());
            }
            case "InventoryHistoricalReportPaged":
            case "InventoryCurrentBalancePaged":
            {
                var isCurrent = scenario == "InventoryCurrentBalancePaged";
                var result = await new CWarehouseReport_Controller().Inventory_Report_Page_Async(
                    V22CorrectnessOracle.ReportFrom, V22CorrectnessOracle.ReportTo, page, size, login, null,
                    p_bRead_Current_Balance: isCurrent);
                return new ActualPage(result.Total_Count, result.Items.Select(item => Row(
                    ("Kho_ID", item.Kho_ID), ("Ten_Kho", item.Ten_Kho), ("San_Pham_ID", item.San_Pham_ID),
                    ("Ma_San_Pham", item.Ma_San_Pham), ("Ten_San_Pham", item.Ten_San_Pham),
                    ("SL_Dau_Ky", item.SL_Dau_Ky), ("SL_Nhap", item.SL_Nhap), ("SL_Xuat", item.SL_Xuat),
                    ("SL_Cuoi_Ky", item.SL_Cuoi_Ky), ("SL_Ton_Thuc_Te", item.SL_Ton_Thuc_Te),
                    ("SL_Dang_Giu", item.SL_Dang_Giu), ("SL_Kha_Dung", item.SL_Kha_Dung))).ToList());
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }

    private static ScenarioEvaluation EvaluateScenario(string scenario, V22OraclePage oracle, ActualPage actual)
    {
        var errors = new List<string>();
        var checks = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        var expectedItemCount = (int)Math.Min(V22CorrectnessOracle.PageSize, Math.Max(0, oracle.TotalCount));
        checks["Total_Count"] = actual.TotalCount == oracle.TotalCount;
        checks["Items.Count"] = actual.Rows.Count == expectedItemCount;
        checks["PageSize"] = actual.Rows.Count <= V22CorrectnessOracle.PageSize && actual.Rows.Count == expectedItemCount;
        checks["ZeroRowsPolicy"] = oracle.TotalCount > 0 && oracle.Rows.Count > 0 && actual.Rows.Count > 0;

        if (!checks["Total_Count"]) errors.Add($"Total_Count expected {oracle.TotalCount}, actual {actual.TotalCount}.");
        if (!checks["Items.Count"]) errors.Add($"Items.Count expected {expectedItemCount}, actual {actual.Rows.Count}.");
        if (!checks["ZeroRowsPolicy"]) errors.Add("Legacy correctness contract disallows an unexpected zero-row page.");

        var fields = FieldsFor(scenario);
        var comparisons = new List<object>();
        var mappedFieldsPass = true;
        var rowCount = Math.Min(oracle.Rows.Count, actual.Rows.Count);
        for (var index = 0; index < rowCount; index++)
        {
            foreach (var field in fields)
            {
                var hasExpected = oracle.Rows[index].TryGetValue(field, out var expected);
                var hasActual = actual.Rows[index].TryGetValue(field, out var found);
                var equal = hasExpected && hasActual && ValuesEqual(expected, found, field);
                mappedFieldsPass &= equal;
                comparisons.Add(new { Index = index, Field = field, Expected = SafeValue(expected), Actual = SafeValue(found), Match = equal });
                if (!equal) errors.Add($"Row {index} field {field} does not match the independent oracle.");
            }
        }
        checks["RequiredMappedFields"] = rowCount == expectedItemCount && mappedFieldsPass;

        var expectedKeys = oracle.Rows.Select(row => KeyFor(scenario, row)).ToArray();
        var actualKeys = actual.Rows.Select(row => KeyFor(scenario, row)).ToArray();
        checks["StableKeysAndOrder"] = expectedKeys.SequenceEqual(actualKeys, StringComparer.Ordinal);
        if (!checks["StableKeysAndOrder"]) errors.Add("Mapped stable keys/order differ from the independent ordered page.");

        var expectedDuplicateCount = DuplicateCount(expectedKeys);
        var actualDuplicateCount = DuplicateCount(actualKeys);
        checks["DuplicateKeyBehavior"] = expectedDuplicateCount == actualDuplicateCount;
        if (!checks["DuplicateKeyBehavior"]) errors.Add("Duplicate mapped-key count differs from the independent page.");

        checks["IndependentOracle"] = oracle.Confidence is "HIGH" or "MEDIUM" && oracle.Rows.All(row => !row.Keys.Any(key => key.StartsWith("__Root", StringComparison.Ordinal)));
        if (!checks["IndependentOracle"]) errors.Add("Oracle independence contract is invalid.");

        if (scenario == "InventoryHistoricalReportPaged")
        {
            var arithmeticPass = oracle.Rows.All(row => Decimal(row, "SL_Dau_Ky") + Decimal(row, "SL_Nhap") - Decimal(row, "SL_Xuat") == Decimal(row, "SL_Cuoi_Ky"));
            checks["HistoricalInventoryArithmetic"] = arithmeticPass;
            if (!arithmeticPass) errors.Add("Independent daily-balance arithmetic Opening + Inbound - Outbound != Ending.");
        }
        if (scenario == "InventoryCurrentBalancePaged")
        {
            var nonNullQuantities = oracle.Rows.All(row => new[] { "SL_Cuoi_Ky", "SL_Ton_Thuc_Te", "SL_Dang_Giu", "SL_Kha_Dung" }.All(field => row[field] is not null));
            checks["CurrentQuantityNullSemantics"] = nonNullQuantities;
            if (!nonNullQuantities) errors.Add("A SQL NULL current/reserved quantity cannot be accepted as CLR decimal zero.");
        }

        var duplicateCount = actualDuplicateCount;
        return new ScenarioEvaluation(scenario, oracle.RootProcedure, oracle.Confidence,
            "Independent parameterized SELECT over base tables/read models; no expected row comes from the root stored procedure.",
            errors.Count == 0 ? "PASS" : "FAIL", oracle.TotalCount, actual.TotalCount,
            expectedItemCount, actual.Rows.Count, duplicateCount, checks, errors,
            expectedKeys, actualKeys, comparisons);
    }

    private static string[] FieldsFor(string scenario) => scenario switch
    {
        "MasterPaged" => ["Auto_ID", "Code", "Name", "Related_ID", "Related_ID_2", "Ghi_Chu"],
        "LookupPaged" => ["Auto_ID", "Code", "Name"],
        "DocumentPaged" => ["Auto_ID", "Is_Receipt", "So_Phieu", "Kho_ID", "Ten_Kho", "NCC_ID", "Ten_NCC", "Ngay_Chung_Tu", "Is_Posted", "Ghi_Chu"],
        "DetailReportPaged" => ["Ngay", "So_Phieu", "Nha_Cung_Cap", "Ma_San_Pham", "Ten_San_Pham", "So_Luong", "Don_Gia", "Tri_Gia"],
        "InventoryHistoricalReportPaged" or "InventoryCurrentBalancePaged" => ["Kho_ID", "Ten_Kho", "San_Pham_ID", "Ma_San_Pham", "Ten_San_Pham", "SL_Dau_Ky", "SL_Nhap", "SL_Xuat", "SL_Cuoi_Ky", "SL_Ton_Thuc_Te", "SL_Dang_Giu", "SL_Kha_Dung"],
        _ => throw new ArgumentOutOfRangeException(nameof(scenario))
    };

    private static string KeyFor(string scenario, IReadOnlyDictionary<string, object?> row) => scenario switch
    {
        "MasterPaged" or "LookupPaged" or "DocumentPaged" => Convert.ToString(row["Auto_ID"], CultureInfo.InvariantCulture) ?? "<null>",
        "DetailReportPaged" => string.Join("|", Convert.ToDateTime(row["Ngay"], CultureInfo.InvariantCulture).ToString("O", CultureInfo.InvariantCulture), row["So_Phieu"], row["Ma_San_Pham"]),
        _ => string.Join("|", row["Kho_ID"], row["San_Pham_ID"])
    };

    private static int DuplicateCount(IReadOnlyList<string> keys) => keys.Count - keys.Distinct(StringComparer.Ordinal).Count();

    private static decimal Decimal(IReadOnlyDictionary<string, object?> row, string name) =>
        Convert.ToDecimal(row[name], CultureInfo.InvariantCulture);

    private static bool ValuesEqual(object? expected, object? actual, string field)
    {
        if (expected is null)
        {
            var defaultEmptyStringField = field is "Ghi_Chu" or "Ten_Kho" or "Ten_NCC" or "Code" or "Name" or "Ma_San_Pham" or "Ten_San_Pham" or "Nha_Cung_Cap" or "So_Phieu";
            return actual is null || (defaultEmptyStringField && actual is string text && text.Length == 0);
        }
        if (expected is DateTime expectedDate && actual is DateTime actualDate)
            return expectedDate == actualDate;
        if (expected is bool expectedBool && actual is bool actualBool)
            return expectedBool == actualBool;
        if (IsNumeric(expected) && IsNumeric(actual))
            return Convert.ToDecimal(expected, CultureInfo.InvariantCulture) == Convert.ToDecimal(actual, CultureInfo.InvariantCulture);
        return string.Equals(Convert.ToString(expected, CultureInfo.InvariantCulture), Convert.ToString(actual, CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static bool IsNumeric(object? value) => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static object? SafeValue(object? value) => value switch
    {
        null => null,
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        decimal decimalValue => decimalValue.ToString("G29", CultureInfo.InvariantCulture),
        _ => value
    };

    private static Dictionary<string, object?> Row(params (string Name, object? Value)[] fields) =>
        fields.ToDictionary(field => field.Name, field => field.Value, StringComparer.OrdinalIgnoreCase);

    private static void WriteOutput(string root, string name, object value) =>
        File.WriteAllText(Path.Combine(root, name), JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));

    private static string BuildRunIndex(string outputRoot, string status, string phase3, string security,
        V22SnapshotCapture? pre, V22SnapshotCapture? post, IReadOnlyList<ScenarioEvaluation> evaluations,
        (string Stage, string Code, string Message) failure) => $$"""
# V22 Correctness Run Index

- Run root: `{{outputRoot}}`
- Status: **{{status}}**
- Phase 3: `{{phase3}}`
- Security disposition: `{{security}}`
- DB PRE/POST: `{{(pre is not null && post is not null && pre.SnapshotSha256 == post.SnapshotSha256 ? "PASS" : "NOT PASS")}}`
- Completed scenarios: {{evaluations.Count}}/6
- Root cause: `{{(string.IsNullOrEmpty(failure.Code) ? "none" : failure.Code)}}` at `{{failure.Stage}}`
- Connection string persisted: no
- Performance workload: not run

Artifacts: `V22-Semantic-Fixtures.json`, `V22-Semantic-Correctness-Results.json`, `V22-Inventory-Correctness-Results.json`, `V22-Security-Correctness-Results.json`, `V22-DB-PrePost-Evidence.json`, `V22-Oracle-Queries.sql`.
""";

    private static string SafeMessage(string message)
    {
        var safe = System.Text.RegularExpressions.Regex.Replace(message,
            "(?i)(password|pwd|user\\s*id|uid|access\\s*token)\\s*=\\s*[^;\\s]+", "$1=<redacted>");
        return safe.Length <= 1000 ? safe : safe[..1000];
    }

    private sealed class GateException(string code, string message) : Exception(message)
    {
        internal string Code { get; } = code;
    }
}
