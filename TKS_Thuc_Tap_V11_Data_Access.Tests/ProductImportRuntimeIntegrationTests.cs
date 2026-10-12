using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;
using Xunit;
using Xunit.Abstractions;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class ProductImportRuntimeIntegrationTests
{
    private const string m_strTestUser = "UAT_PI_RUNTIME";
    private const string m_strTestFunction = "ProductImportPhase1B";
    private const string m_strCategoryCode = "TP";
    private const long m_iCategoryId = 96;
    private const long m_iUnitId = 82;
    private readonly ITestOutputHelper m_objOutput;

    public ProductImportRuntimeIntegrationTests(ITestOutputHelper p_objOutput)
    {
        m_objOutput = p_objOutput;
    }

    [Fact]
    public async Task Phase1b_product_import_runtime_gates_pass_on_approved_test_database()
    {
        var v_strConnectionString = ProductImportTestDatabase.ValidateConnectionString(
            Environment.GetEnvironmentVariable("TKS_INTEGRATION_CONNECTION_STRING") ?? "");
        var v_strPreviousConnectionString = CConfig.TKS_Thuc_Tap_V11_Conn_String;
        var v_strRunId = Environment.GetEnvironmentVariable("TKS_PI_PROBE_SUFFIX") ??
                         Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        if (!Regex.IsMatch(v_strRunId, "^[A-F0-9]{12}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("TKS_PI_PROBE_SUFFIX must be a 12-character hexadecimal run ID.");

        var v_strRootPrefix = Environment.GetEnvironmentVariable("TKS_PI_TEST_PREFIX") ??
            $"UAT_PI_{DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture)}_{v_strRunId}_";
        if (!Regex.IsMatch(v_strRootPrefix, $"^UAT_PI_\\d{{8}}_\\d{{6}}_{v_strRunId}_$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("TKS_PI_TEST_PREFIX must use the exact run-specific UAT_PI timestamp prefix.");

        var v_strUnitPrefix = "UAT_PI_UNIT_" + v_strRootPrefix["UAT_PI_".Length..];
        var v_strProbeTable = $"UAT_PI_TransactionProbe_{v_strRunId}";
        var v_strProbeTrigger = $"TR_UAT_PI_TransactionProbe_{v_strRunId}";
        await using var v_objDatabase = await ProductImportTestDatabase.OpenVerifiedConnectionAsync();
        CConfig.TKS_Thuc_Tap_V11_Conn_String = v_strConnectionString;

        try
        {
            await EnsureTargetIdentityAsync(v_objDatabase);
            Assert.Equal(0, await CountProductPrefixAsync(v_objDatabase, v_strRootPrefix));
            Assert.Equal(0, await CountUnitPrefixAsync(v_objDatabase, v_strUnitPrefix));
            m_objOutput.WriteLine($"TEST_DB={await GetDatabaseIdentityAsync(v_objDatabase)}");
            m_objOutput.WriteLine($"TEST_PREFIX={v_strRootPrefix}");

            await VerifyFenceDependenciesAsync(v_objDatabase);

            var v_objService = new CWarehouseProductImportService(new CAllowAddPermission());
            await VerifyUnitResolutionPreviewAsync(v_objDatabase, v_objService, v_strRootPrefix);
            await VerifyUnitCrudAsync(v_objDatabase, v_strUnitPrefix);

            await VerifyOneHundredRowImportAsync(
                v_objDatabase,
                v_objService,
                v_strRootPrefix,
                v_strProbeTable,
                v_strProbeTrigger);

            await VerifyRowFiftyRollbackAsync(v_objDatabase, v_strRootPrefix);
            await VerifyDuplicateRaceAsync(v_objDatabase, v_objService, v_strConnectionString, v_strRootPrefix);

            Assert.Equal(0, await CountProductPrefixAsync(v_objDatabase, v_strRootPrefix));
            Assert.Equal(0, await CountUnitPrefixAsync(v_objDatabase, v_strUnitPrefix));
            m_objOutput.WriteLine("CLEANUP_ALL_TEST_DATA=0");

            await MeasureProductCodePreloadAsync(v_objDatabase, new CWarehouseProductSupport_Service());
            await MeasureImportAsync(v_objDatabase, v_objService, v_strRootPrefix, 10);
            await MeasureImportAsync(v_objDatabase, v_objService, v_strRootPrefix, 100);
        }
        finally
        {
            try
            {
                await EnsureTargetIdentityAsync(v_objDatabase);
                await DeleteProductsByPrefixAsync(v_objDatabase, v_strRootPrefix);
                await DeleteUnitsByPrefixAsync(v_objDatabase, v_strUnitPrefix);
                await DropProbeObjectsAsync(v_objDatabase, v_strProbeTrigger, v_strProbeTable);
                Assert.Equal(0, await CountProductPrefixAsync(v_objDatabase, v_strRootPrefix));
                Assert.Equal(0, await CountUnitPrefixAsync(v_objDatabase, v_strUnitPrefix));
            }
            finally
            {
                CConfig.TKS_Thuc_Tap_V11_Conn_String = v_strPreviousConnectionString;
            }
        }
    }

    private async Task VerifyFenceDependenciesAsync(SqlConnection p_objConnection)
    {
        await EnsureTargetIdentityAsync(p_objConnection);
        await using (var v_objCommand = new SqlCommand(
                         "SELECT dbo.fn_Inventory_Fence_Root_Resource(), " +
                         "dbo.fn_Inventory_Fence_Mode_Satisfies(N'Exclusive', N'Shared');",
                         p_objConnection))
        await using (var v_objReader = await v_objCommand.ExecuteReaderAsync())
        {
            Assert.True(await v_objReader.ReadAsync());
            Assert.Equal("InventoryMovementGroup:Root", v_objReader.GetString(0));
            Assert.True(v_objReader.GetBoolean(1));
        }

        await EnsureTargetIdentityAsync(p_objConnection);
        await using var v_objAcquire = new SqlCommand(
            "BEGIN TRY BEGIN TRANSACTION; " +
            "EXEC dbo.sp_Inventory_Fence_Require_Transaction; " +
            "EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode=N'Exclusive'; " +
            "DECLARE @Mode nvarchar(32)=APPLOCK_MODE(N'public', dbo.fn_Inventory_Fence_Root_Resource(), N'Transaction'); " +
            "ROLLBACK TRANSACTION; SELECT @Mode; " +
            "END TRY BEGIN CATCH IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION; THROW; END CATCH;",
            p_objConnection);
        Assert.Equal("Exclusive", (string?)await v_objAcquire.ExecuteScalarAsync());
        m_objOutput.WriteLine("FENCE_DEPENDENCIES_CALLABLE=PASS");
    }

    private async Task VerifyUnitResolutionPreviewAsync(
        SqlConnection p_objConnection,
        CWarehouseProductImportService p_objService,
        string p_strRootPrefix)
    {
        var v_strPrefix = p_strRootPrefix + "UNIT_";
        var v_iBefore = await CountProductPrefixAsync(p_objConnection, v_strPrefix);
        using var v_objWorkbook = CreateWorkbook(new[]
        {
            new CWorkbookRow(v_strPrefix + "01", "Cuộn check", m_strCategoryCode, "Cuộn", ""),
            new CWorkbookRow(v_strPrefix + "02", "CUỘN check", m_strCategoryCode, "CUỘN", ""),
            new CWorkbookRow(v_strPrefix + "03", "Cuốn check", m_strCategoryCode, "Cuốn", ""),
            new CWorkbookRow(v_strPrefix + "04", "CUỐN check", m_strCategoryCode, "CUỐN", "")
        });

        var v_objPreview = await p_objService.PreviewAsync(
            "unit-runtime.xlsx", v_objWorkbook, m_strTestUser, m_strTestFunction);

        Assert.True(v_objPreview.Can_Confirm, string.Join("; ", v_objPreview.Errors.Select(v_objError => v_objError.Message)));
        Assert.Equal(new long[] { 205, 205, 211, 211 },
            v_objPreview.Rows.Select(v_objRow => v_objRow.Don_Vi_Tinh_ID).ToArray());
        var v_iAfter = await CountProductPrefixAsync(p_objConnection, v_strPrefix);
        Assert.Equal(0, v_iBefore);
        Assert.Equal(0, v_iAfter);
        m_objOutput.WriteLine("UNIT_DISTINCTNESS_RUNTIME=Cuộn:205,CUỘN:205,Cuốn:211,CUỐN:211");
        m_objOutput.WriteLine($"UNIT_PREVIEW_NO_WRITE={v_iBefore}->{v_iAfter}");
    }

    private async Task VerifyUnitCrudAsync(
        SqlConnection p_objConnection,
        string p_strUnitPrefix)
    {
        var v_strCuon = p_strUnitPrefix + "Cuộn";
        var v_strCuonAccent = p_strUnitPrefix + "Cuốn";
        var v_objUnitController = new CDM_Don_Vi_Tinh_Controller();
        await EnsureTargetIdentityAsync(p_objConnection);

        var v_objCuon = new CWarehouseMaster { Name = v_strCuon };
        v_objCuon.Auto_ID = v_objUnitController.F2016_sp_ins_Don_Vi_Tinh(v_objCuon, m_strTestUser, m_strTestFunction);
        var v_objCuonAccent = new CWarehouseMaster { Name = v_strCuonAccent };
        await EnsureTargetIdentityAsync(p_objConnection);
        v_objCuonAccent.Auto_ID = v_objUnitController.F2016_sp_ins_Don_Vi_Tinh(v_objCuonAccent, m_strTestUser, m_strTestFunction);
        Assert.NotEqual(0, v_objCuon.Auto_ID);
        Assert.NotEqual(0, v_objCuonAccent.Auto_ID);
        Assert.NotEqual(v_objCuon.Auto_ID, v_objCuonAccent.Auto_ID);

        await EnsureTargetIdentityAsync(p_objConnection);
        var v_objInsertDuplicate = Record.Exception(() => v_objUnitController.F2016_sp_ins_Don_Vi_Tinh(
            new CWarehouseMaster { Name = p_strUnitPrefix + "CUỘN" }, m_strTestUser, m_strTestFunction));
        Assert.Equal(51002, Assert.IsType<SqlException>(v_objInsertDuplicate).Number);

        await EnsureTargetIdentityAsync(p_objConnection);
        var v_objUpdateDuplicate = Record.Exception(() => v_objUnitController.F2016_sp_upd_Don_Vi_Tinh(
            new CWarehouseMaster { Auto_ID = v_objCuonAccent.Auto_ID, Name = p_strUnitPrefix + "CUỘN" },
            m_strTestUser, m_strTestFunction));
        Assert.Equal(51002, Assert.IsType<SqlException>(v_objUpdateDuplicate).Number);
        Assert.Equal(v_strCuonAccent, await GetUnitNameAsync(p_objConnection, v_objCuonAccent.Auto_ID));

        var v_iBeforeCleanup = await CountUnitPrefixAsync(p_objConnection, p_strUnitPrefix);
        await DeleteUnitsByPrefixAsync(p_objConnection, p_strUnitPrefix);
        var v_iAfterCleanup = await CountUnitPrefixAsync(p_objConnection, p_strUnitPrefix);
        Assert.Equal(2, v_iBeforeCleanup);
        Assert.Equal(0, v_iAfterCleanup);
        m_objOutput.WriteLine("F2016_CI_AS_RUNTIME=accent-distinct inserts pass; case-only insert/update rejected with 51002");
        m_objOutput.WriteLine($"UNIT_CRUD_CLEANUP={v_iBeforeCleanup}->{v_iAfterCleanup}");
    }

    private async Task VerifyOneHundredRowImportAsync(
        SqlConnection p_objConnection,
        CWarehouseProductImportService p_objService,
        string p_strRootPrefix,
        string p_strProbeTable,
        string p_strProbeTrigger)
    {
        var v_strPrefix = p_strRootPrefix + "100_";
        var v_iBefore = await CountProductPrefixAsync(p_objConnection, v_strPrefix);
        Assert.Equal(0, v_iBefore);
        var v_arrRows = CreateWorkbookRows(v_strPrefix, 100);
        using var v_objWorkbook = CreateWorkbook(v_arrRows);
        var v_objPreview = await p_objService.PreviewAsync(
            "product-import-100.xlsx", v_objWorkbook, m_strTestUser, m_strTestFunction);
        var v_iAfterPreview = await CountProductPrefixAsync(p_objConnection, v_strPrefix);
        Assert.True(v_objPreview.Can_Confirm, string.Join("; ", v_objPreview.Errors.Select(v_objError => v_objError.Message)));
        Assert.Equal(100, v_objPreview.Valid_Rows);
        Assert.Equal(0, v_iAfterPreview);
        m_objOutput.WriteLine($"PREVIEW_NO_WRITE={v_iBefore}->{v_iAfterPreview}; valid={v_objPreview.Valid_Rows}; invalid={v_objPreview.Invalid_Rows}");

        await CreateProbeObjectsAsync(p_objConnection, p_strProbeTable, p_strProbeTrigger, v_strPrefix);
        await using (var v_objVerified = await ProductImportTestDatabase.OpenVerifiedConnectionAsync())
            await EnsureTargetIdentityAsync(v_objVerified);

        var v_iImported = await p_objService.ConfirmAsync(v_objPreview, m_strTestUser, m_strTestFunction);
        var v_iAfterConfirm = await CountProductPrefixAsync(p_objConnection, v_strPrefix);
        Assert.Equal(100, v_iImported);
        Assert.Equal(100, v_iAfterConfirm);
        var v_objAudit = await ReadProductSampleAsync(p_objConnection, v_strPrefix + "00123");
        Assert.Equal(v_strPrefix + "00123", v_objAudit.Code);
        Assert.Equal("Sản phẩm 00123", v_objAudit.Name);
        Assert.Equal(m_iCategoryId, v_objAudit.CategoryId);
        Assert.Equal(m_iUnitId, v_objAudit.UnitId);
        Assert.Equal("", v_objAudit.Note);
        Assert.Equal(m_strTestUser, v_objAudit.CreatedBy);
        Assert.Equal(m_strTestFunction, v_objAudit.CreatedByFunction);
        Assert.Equal(m_strTestUser, v_objAudit.LastUpdatedBy);
        Assert.Equal(m_strTestFunction, v_objAudit.LastUpdatedByFunction);

        var v_objProbe = await ReadProbeEvidenceAsync(p_objConnection, p_strProbeTable);
        Assert.Equal(100, v_objProbe.Rows);
        Assert.Equal(1, v_objProbe.DistinctSessions);
        Assert.Equal(1, v_objProbe.DistinctTransactions);
        m_objOutput.WriteLine($"IMPORT_100_ROWS=PASS_{v_iImported}_OF_100; count={v_iAfterConfirm}");
        m_objOutput.WriteLine($"TRANSACTION_PROBE=rows:{v_objProbe.Rows},sessions:{v_objProbe.DistinctSessions},transactions:{v_objProbe.DistinctTransactions},tranCount:{v_objProbe.MinTranCount}-{v_objProbe.MaxTranCount},xactState:{v_objProbe.MinXactState}-{v_objProbe.MaxXactState}");
        m_objOutput.WriteLine($"AUDIT_SAMPLE=code:{v_objAudit.Code};category:{v_objAudit.CategoryId};unit:{v_objAudit.UnitId};createdBy:{v_objAudit.CreatedBy};createdByFunction:{v_objAudit.CreatedByFunction};updatedBy:{v_objAudit.LastUpdatedBy};updatedByFunction:{v_objAudit.LastUpdatedByFunction}");

        var v_iBeforeCleanup = await CountProductPrefixAsync(p_objConnection, v_strPrefix);
        await DeleteProductsByPrefixAsync(p_objConnection, v_strPrefix);
        var v_iAfterCleanup = await CountProductPrefixAsync(p_objConnection, v_strPrefix);
        Assert.Equal(100, v_iBeforeCleanup);
        Assert.Equal(0, v_iAfterCleanup);
        await DropProbeObjectsAsync(p_objConnection, p_strProbeTrigger, p_strProbeTable);
        m_objOutput.WriteLine($"CLEANUP_100_ROWS={v_iBeforeCleanup}->{v_iAfterCleanup}");
    }

    private async Task VerifyRowFiftyRollbackAsync(SqlConnection p_objConnection, string p_strRootPrefix)
    {
        var v_strPrefix = p_strRootPrefix + "ROLLBACK_";
        var v_iBefore = await CountProductPrefixAsync(p_objConnection, v_strPrefix);
        Assert.Equal(0, v_iBefore);
        var v_arrRows = Enumerable.Range(1, 100)
            .Select(v_iRow => new CProductImportRow
            {
                Row_Number = v_iRow,
                Ma_San_Pham = v_strPrefix + v_iRow.ToString("D3"),
                Ten_San_Pham = $"Rollback row {v_iRow:D3}",
                Ma_LSP = m_strCategoryCode,
                Ten_Don_Vi_Tinh = "Cái",
                Loai_San_Pham_ID = v_iRow == 50 ? long.MaxValue : m_iCategoryId,
                Don_Vi_Tinh_ID = m_iUnitId
            })
            .ToArray();

        await EnsureTargetIdentityAsync(p_objConnection);
        var v_objFailure = await Record.ExceptionAsync(() => new CWarehouseProductImportService()
            .Import_Products_Async(v_arrRows, m_strTestUser, m_strTestFunction));
        var v_objImportError = Assert.IsType<InvalidOperationException>(v_objFailure);
        var v_objSqlError = Assert.IsType<SqlException>(v_objImportError.InnerException);
        Assert.Equal(51023, v_objSqlError.Number);
        Assert.Contains("Dòng 50", v_objImportError.Message, StringComparison.Ordinal);
        var v_iAfter = await CountProductPrefixAsync(p_objConnection, v_strPrefix);
        Assert.Equal(0, v_iAfter);
        m_objOutput.WriteLine($"ALL_OR_NOTHING_TRANSACTION=PASS; failureRow=50; sqlNumber={v_objSqlError.Number}; before={v_iBefore}; after={v_iAfter}");
    }

    private async Task VerifyDuplicateRaceAsync(
        SqlConnection p_objConnection,
        CWarehouseProductImportService p_objService,
        string p_strConnectionString,
        string p_strRootPrefix)
    {
        var v_strPrefix = p_strRootPrefix + "RACE_";
        var v_strFirstCode = v_strPrefix + "A";
        var v_strCompetingCode = v_strPrefix + "X";
        var v_strLastCode = v_strPrefix + "C";
        Assert.Equal(0, await CountProductPrefixAsync(p_objConnection, v_strPrefix));
        using var v_objWorkbook = CreateWorkbook(new[]
        {
            new CWorkbookRow(v_strFirstCode, "Race row A", m_strCategoryCode, "Cái", ""),
            new CWorkbookRow(v_strCompetingCode, "Race row X", m_strCategoryCode, "Cái", ""),
            new CWorkbookRow(v_strLastCode, "Race row C", m_strCategoryCode, "Cái", "")
        });
        var v_objPreview = await p_objService.PreviewAsync(
            "product-import-race.xlsx", v_objWorkbook, m_strTestUser, m_strTestFunction);
        Assert.True(v_objPreview.Can_Confirm);

        await using (var v_objCompetitor = new SqlConnection(p_strConnectionString))
        {
            await v_objCompetitor.OpenAsync();
            await EnsureTargetIdentityAsync(v_objCompetitor);
            Assert.NotEqual(p_objConnection.ClientConnectionId, v_objCompetitor.ClientConnectionId);
            await InsertProductAsync(v_objCompetitor, v_strCompetingCode, "Competing Product X");
        }

        await using (var v_objVerified = await ProductImportTestDatabase.OpenVerifiedConnectionAsync())
            await EnsureTargetIdentityAsync(v_objVerified);
        var v_objFailure = await Record.ExceptionAsync(() => p_objService.ConfirmAsync(
            v_objPreview, m_strTestUser, m_strTestFunction));
        var v_objValidationFailure = Assert.IsType<CProductImportValidationException>(v_objFailure);
        Assert.Contains(v_objValidationFailure.PreviewResult.Errors,
            v_objError => v_objError.Message.Contains(v_strCompetingCode, StringComparison.Ordinal));

        var v_iAfterRace = await CountProductPrefixAsync(p_objConnection, v_strPrefix);
        Assert.Equal(1, v_iAfterRace);
        Assert.Equal(0, await CountProductCodeAsync(p_objConnection, v_strFirstCode));
        Assert.Equal(1, await CountProductCodeAsync(p_objConnection, v_strCompetingCode));
        Assert.Equal(0, await CountProductCodeAsync(p_objConnection, v_strLastCode));
        await DeleteProductsByPrefixAsync(p_objConnection, v_strPrefix);
        var v_iAfterCleanup = await CountProductPrefixAsync(p_objConnection, v_strPrefix);
        Assert.Equal(0, v_iAfterCleanup);
        m_objOutput.WriteLine($"DUPLICATE_RACE=PASS; competing={v_strCompetingCode}; batch=CONFIRM_REJECTED; originalRows=0; remainingBeforeCleanup={v_iAfterRace}; afterCleanup={v_iAfterCleanup}");
    }

    private async Task MeasureProductCodePreloadAsync(
        SqlConnection p_objConnection,
        CWarehouseProductSupport_Service p_objController)
    {
        var v_iProductCount = await CountAllProductsAsync(p_objConnection);
        var v_arrRuns = new List<double>();
        for (var v_iRun = 0; v_iRun < 4; v_iRun++)
        {
            await EnsureTargetIdentityAsync(p_objConnection);
            var v_objTimer = Stopwatch.StartNew();
            var v_setCodes = await p_objController.List_Product_Codes_Async();
            v_objTimer.Stop();
            Assert.Equal(v_iProductCount, v_setCodes.Count);
            if (v_iRun > 0)
                v_arrRuns.Add(v_objTimer.Elapsed.TotalMilliseconds);
        }

        WriteTimingSummary("PRODUCT_CODE_PRELOAD", v_arrRuns);
        m_objOutput.WriteLine($"PRODUCT_PRELOAD_COUNT={v_iProductCount}; expectedPages=1 (page size 500)");
    }

    private async Task MeasureImportAsync(
        SqlConnection p_objConnection,
        CWarehouseProductImportService p_objService,
        string p_strRootPrefix,
        int p_iRowCount)
    {
        var v_arrRuns = new List<CImportTiming>();
        for (var v_iRun = 0; v_iRun < 4; v_iRun++)
        {
            var v_strPrefix = $"{p_strRootPrefix}PERF{p_iRowCount}_{v_iRun}_";
            Assert.Equal(0, await CountProductPrefixAsync(p_objConnection, v_strPrefix));
            using var v_objWorkbook = CreateWorkbook(CreateWorkbookRows(v_strPrefix, p_iRowCount));
            var v_arrContent = v_objWorkbook.ToArray();

            using var v_objParseStream = new MemoryStream(v_arrContent, writable: false);
            var v_objParseTimer = Stopwatch.StartNew();
            _ = await new CProduct_Xlsx_Reader().ReadAsync(v_objParseStream);
            v_objParseTimer.Stop();

            using var v_objPreviewStream = new MemoryStream(v_arrContent, writable: false);
            await EnsureTargetIdentityAsync(p_objConnection);
            var v_objPreviewTimer = Stopwatch.StartNew();
            var v_objPreview = await p_objService.PreviewAsync(
                "product-import-performance.xlsx", v_objPreviewStream, m_strTestUser, m_strTestFunction);
            v_objPreviewTimer.Stop();
            Assert.True(v_objPreview.Can_Confirm);
            Assert.Equal(p_iRowCount, v_objPreview.Valid_Rows);

            await EnsureTargetIdentityAsync(p_objConnection);
            var v_objConfirmTimer = Stopwatch.StartNew();
            var v_iImported = await p_objService.ConfirmAsync(v_objPreview, m_strTestUser, m_strTestFunction);
            v_objConfirmTimer.Stop();
            Assert.Equal(p_iRowCount, v_iImported);
            Assert.Equal(p_iRowCount, await CountProductPrefixAsync(p_objConnection, v_strPrefix));

            var v_iBeforeCleanup = await CountProductPrefixAsync(p_objConnection, v_strPrefix);
            await DeleteProductsByPrefixAsync(p_objConnection, v_strPrefix);
            Assert.Equal(0, await CountProductPrefixAsync(p_objConnection, v_strPrefix));

            if (v_iRun > 0)
            {
                var v_dParse = v_objParseTimer.Elapsed.TotalMilliseconds;
                var v_dPreview = v_objPreviewTimer.Elapsed.TotalMilliseconds;
                var v_dConfirm = v_objConfirmTimer.Elapsed.TotalMilliseconds;
                v_arrRuns.Add(new CImportTiming(v_dParse, v_dPreview, v_dConfirm, v_dPreview + v_dConfirm));
                m_objOutput.WriteLine($"PERF_RAW rows={p_iRowCount},run={v_iRun},parseMs={v_dParse:F3},previewMs={v_dPreview:F3},confirmMs={v_dConfirm:F3},totalMs={v_dPreview + v_dConfirm:F3},cleanup={v_iBeforeCleanup}->0");
            }
        }

        WriteTimingSummary($"PERF_{p_iRowCount}_PARSE", v_arrRuns.Select(v_objRun => v_objRun.ParseMs).ToArray());
        WriteTimingSummary($"PERF_{p_iRowCount}_PREVIEW", v_arrRuns.Select(v_objRun => v_objRun.PreviewMs).ToArray());
        WriteTimingSummary($"PERF_{p_iRowCount}_CONFIRM", v_arrRuns.Select(v_objRun => v_objRun.ConfirmMs).ToArray());
        WriteTimingSummary($"PERF_{p_iRowCount}_TOTAL", v_arrRuns.Select(v_objRun => v_objRun.TotalMs).ToArray());
    }

    private void WriteTimingSummary(string p_strName, IEnumerable<double> p_arrValues)
    {
        var v_arrValues = p_arrValues.Order().ToArray();
        Assert.Equal(3, v_arrValues.Length);
        m_objOutput.WriteLine($"{p_strName}_MS=min:{v_arrValues[0]:F3},median:{v_arrValues[1]:F3},max:{v_arrValues[2]:F3}");
    }

    private static async Task CreateProbeObjectsAsync(
        SqlConnection p_objConnection,
        string p_strTable,
        string p_strTrigger,
        string p_strProductPrefix)
    {
        await EnsureTargetIdentityAsync(p_objConnection);
        var v_strTableSql = $"[dbo].[{p_strTable}]";
        await using (var v_objCreateTable = new SqlCommand(
                         $"CREATE TABLE {v_strTableSql} (" +
                         "Probe_ID bigint IDENTITY(1,1) NOT NULL PRIMARY KEY, " +
                         "Product_Code nvarchar(100) NOT NULL, Session_ID int NOT NULL, " +
                         "Transaction_ID bigint NOT NULL, Transaction_Count int NOT NULL, Xact_State int NOT NULL);",
                         p_objConnection))
            await v_objCreateTable.ExecuteNonQueryAsync();

        await EnsureTargetIdentityAsync(p_objConnection);
        var v_strPrefixLiteral = p_strProductPrefix.Replace("'", "''", StringComparison.Ordinal);
        await using var v_objCreateTrigger = new SqlCommand(
            $"CREATE TRIGGER [dbo].[{p_strTrigger}] ON [dbo].[tbl_DM_San_Pham] AFTER INSERT AS " +
            "BEGIN SET NOCOUNT ON; " +
            $"INSERT INTO {v_strTableSql} (Product_Code,Session_ID,Transaction_ID,Transaction_Count,Xact_State) " +
            "SELECT i.Ma_San_Pham,@@SPID,CURRENT_TRANSACTION_ID(),@@TRANCOUNT,XACT_STATE() " +
            "FROM inserted AS i " +
            $"WHERE LEFT(i.Ma_San_Pham,LEN(N'{v_strPrefixLiteral}')) COLLATE Latin1_General_100_BIN2=N'{v_strPrefixLiteral}' COLLATE Latin1_General_100_BIN2; END;",
            p_objConnection);
        await v_objCreateTrigger.ExecuteNonQueryAsync();
    }

    private static async Task<ProbeEvidence> ReadProbeEvidenceAsync(SqlConnection p_objConnection, string p_strTable)
    {
        await EnsureTargetIdentityAsync(p_objConnection);
        await using var v_objCommand = new SqlCommand(
            $"SELECT COUNT_BIG(*),COUNT(DISTINCT Session_ID),COUNT(DISTINCT Transaction_ID)," +
            $"MIN(Transaction_Count),MAX(Transaction_Count),MIN(Xact_State),MAX(Xact_State) FROM [dbo].[{p_strTable}];",
            p_objConnection);
        await using var v_objReader = await v_objCommand.ExecuteReaderAsync();
        Assert.True(await v_objReader.ReadAsync());
        return new ProbeEvidence(
            Convert.ToInt32(v_objReader.GetInt64(0)),
            v_objReader.GetInt32(1),
            v_objReader.GetInt32(2),
            v_objReader.GetInt32(3),
            v_objReader.GetInt32(4),
            v_objReader.GetInt32(5),
            v_objReader.GetInt32(6));
    }

    private static async Task DropProbeObjectsAsync(SqlConnection p_objConnection, string p_strTrigger, string p_strTable)
    {
        await EnsureTargetIdentityAsync(p_objConnection);
        await using var v_objDrop = new SqlCommand(
            $"IF OBJECT_ID(N'dbo.{p_strTrigger}',N'TR') IS NOT NULL DROP TRIGGER [dbo].[{p_strTrigger}]; " +
            $"IF OBJECT_ID(N'dbo.{p_strTable}',N'U') IS NOT NULL DROP TABLE [dbo].[{p_strTable}];",
            p_objConnection);
        await v_objDrop.ExecuteNonQueryAsync();
    }

    private static async Task<ProductSample> ReadProductSampleAsync(SqlConnection p_objConnection, string p_strCode)
    {
        await EnsureTargetIdentityAsync(p_objConnection);
        await using var v_objCommand = new SqlCommand(
            "SELECT Ma_San_Pham,Ten_San_Pham,Loai_San_Pham_ID,Don_Vi_Tinh_ID,ISNULL(Ghi_Chu,N'')," +
            "Created_By,Created_By_Function,Last_Updated_By,Last_Updated_By_Function " +
            "FROM dbo.tbl_DM_San_Pham WHERE Ma_San_Pham=@Code;",
            p_objConnection);
        v_objCommand.Parameters.Add("@Code", SqlDbType.NVarChar, 100).Value = p_strCode;
        await using var v_objReader = await v_objCommand.ExecuteReaderAsync();
        Assert.True(await v_objReader.ReadAsync());
        return new ProductSample(
            v_objReader.GetString(0), v_objReader.GetString(1), v_objReader.GetInt64(2), v_objReader.GetInt64(3),
            v_objReader.GetString(4), v_objReader.GetString(5), v_objReader.GetString(6),
            v_objReader.GetString(7), v_objReader.GetString(8));
    }

    private static async Task<string> GetUnitNameAsync(SqlConnection p_objConnection, long p_iAutoId)
    {
        await EnsureTargetIdentityAsync(p_objConnection);
        await using var v_objCommand = new SqlCommand(
            "SELECT Ten_Don_Vi_Tinh FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID=@Auto_ID;",
            p_objConnection);
        v_objCommand.Parameters.Add("@Auto_ID", SqlDbType.BigInt).Value = p_iAutoId;
        return (string)(await v_objCommand.ExecuteScalarAsync() ?? throw new InvalidOperationException("Temporary Unit row disappeared."));
    }

    private static async Task InsertProductAsync(SqlConnection p_objConnection, string p_strCode, string p_strName)
    {
        await EnsureTargetIdentityAsync(p_objConnection);
        await using var v_objCommand = new SqlCommand("dbo.F2018_sp_ins_San_Pham", p_objConnection)
        {
            CommandType = CommandType.StoredProcedure
        };
        v_objCommand.Parameters.Add(new SqlParameter("@Auto_ID", SqlDbType.BigInt) { Direction = ParameterDirection.Output, Value = 0L });
        v_objCommand.Parameters.Add("@Ma_San_Pham", SqlDbType.NVarChar, 100).Value = p_strCode;
        v_objCommand.Parameters.Add("@Ten_San_Pham", SqlDbType.NVarChar, 255).Value = p_strName;
        v_objCommand.Parameters.Add("@Loai_San_Pham_ID", SqlDbType.BigInt).Value = m_iCategoryId;
        v_objCommand.Parameters.Add("@Don_Vi_Tinh_ID", SqlDbType.BigInt).Value = m_iUnitId;
        v_objCommand.Parameters.Add("@Ghi_Chu", SqlDbType.NVarChar, 1000).Value = "";
        v_objCommand.Parameters.Add("@Created_By", SqlDbType.NVarChar, 100).Value = m_strTestUser;
        v_objCommand.Parameters.Add("@Created_By_Function", SqlDbType.NVarChar, 100).Value = m_strTestFunction;
        v_objCommand.Parameters.Add("@Last_Updated_By", SqlDbType.NVarChar, 100).Value = m_strTestUser;
        v_objCommand.Parameters.Add("@Last_Updated_By_Function", SqlDbType.NVarChar, 100).Value = m_strTestFunction;
        await v_objCommand.ExecuteNonQueryAsync();
    }

    private static MemoryStream CreateWorkbook(IEnumerable<CWorkbookRow> p_arrRows)
    {
        var v_objStream = new MemoryStream();
        using (var v_objWorkbook = new XLWorkbook())
        {
            var v_objSheet = v_objWorkbook.AddWorksheet("SanPham");
            var v_arrHeaders = new[] { "Ma_San_Pham", "Ten_San_Pham", "Ma_LSP", "Ten_Don_Vi_Tinh", "Ghi_Chu" };
            for (var v_iColumn = 0; v_iColumn < v_arrHeaders.Length; v_iColumn++)
                v_objSheet.Cell(1, v_iColumn + 1).Value = v_arrHeaders[v_iColumn];

            var v_iRow = 2;
            foreach (var v_objRow in p_arrRows)
            {
                v_objSheet.Cell(v_iRow, 1).Value = v_objRow.Code;
                v_objSheet.Cell(v_iRow, 2).Value = v_objRow.Name;
                v_objSheet.Cell(v_iRow, 3).Value = v_objRow.CategoryCode;
                v_objSheet.Cell(v_iRow, 4).Value = v_objRow.UnitName;
                v_objSheet.Cell(v_iRow, 5).Value = v_objRow.Note;
                v_iRow++;
            }

            v_objWorkbook.SaveAs(v_objStream);
        }

        v_objStream.Position = 0;
        return v_objStream;
    }

    private static CWorkbookRow[] CreateWorkbookRows(string p_strPrefix, int p_iCount) =>
        Enumerable.Range(1, p_iCount)
            .Select(v_iRow => new CWorkbookRow(
                p_strPrefix + (v_iRow == 1 ? "00123" : $"P{v_iRow:D3}"),
                v_iRow == 1 ? "Sản phẩm 00123" : $"Sản phẩm UAT {v_iRow:D3}",
                m_strCategoryCode,
                "Cái",
                ""))
            .ToArray();

    private static async Task<long> CountProductPrefixAsync(SqlConnection p_objConnection, string p_strPrefix)
    {
        await EnsureTargetIdentityAsync(p_objConnection);
        await using var v_objCommand = new SqlCommand(
            "SELECT COUNT_BIG(*) FROM dbo.tbl_DM_San_Pham WHERE LEFT(Ma_San_Pham,LEN(@Prefix)) COLLATE Latin1_General_100_BIN2=@Prefix COLLATE Latin1_General_100_BIN2;",
            p_objConnection);
        v_objCommand.Parameters.Add("@Prefix", SqlDbType.NVarChar, 100).Value = p_strPrefix;
        return Convert.ToInt64(await v_objCommand.ExecuteScalarAsync());
    }

    private static async Task<long> CountProductCodeAsync(SqlConnection p_objConnection, string p_strCode)
    {
        await EnsureTargetIdentityAsync(p_objConnection);
        await using var v_objCommand = new SqlCommand(
            "SELECT COUNT_BIG(*) FROM dbo.tbl_DM_San_Pham WHERE Ma_San_Pham COLLATE Latin1_General_100_BIN2=@Code COLLATE Latin1_General_100_BIN2;",
            p_objConnection);
        v_objCommand.Parameters.Add("@Code", SqlDbType.NVarChar, 100).Value = p_strCode;
        return Convert.ToInt64(await v_objCommand.ExecuteScalarAsync());
    }

    private static async Task<long> CountUnitPrefixAsync(SqlConnection p_objConnection, string p_strPrefix)
    {
        await EnsureTargetIdentityAsync(p_objConnection);
        await using var v_objCommand = new SqlCommand(
            "SELECT COUNT_BIG(*) FROM dbo.tbl_DM_Don_Vi_Tinh WHERE LEFT(Ten_Don_Vi_Tinh,LEN(@Prefix)) COLLATE Latin1_General_100_BIN2=@Prefix COLLATE Latin1_General_100_BIN2;",
            p_objConnection);
        v_objCommand.Parameters.Add("@Prefix", SqlDbType.NVarChar, 200).Value = p_strPrefix;
        return Convert.ToInt64(await v_objCommand.ExecuteScalarAsync());
    }

    private static async Task<long> CountAllProductsAsync(SqlConnection p_objConnection)
    {
        await EnsureTargetIdentityAsync(p_objConnection);
        await using var v_objCommand = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.tbl_DM_San_Pham;", p_objConnection);
        return Convert.ToInt64(await v_objCommand.ExecuteScalarAsync());
    }

    private static async Task DeleteProductsByPrefixAsync(SqlConnection p_objConnection, string p_strPrefix)
    {
        await EnsureTargetIdentityAsync(p_objConnection);
        await using var v_objCommand = new SqlCommand(
            "DELETE FROM dbo.tbl_DM_San_Pham WHERE LEFT(Ma_San_Pham,LEN(@Prefix)) COLLATE Latin1_General_100_BIN2=@Prefix COLLATE Latin1_General_100_BIN2;",
            p_objConnection);
        v_objCommand.Parameters.Add("@Prefix", SqlDbType.NVarChar, 100).Value = p_strPrefix;
        await v_objCommand.ExecuteNonQueryAsync();
    }

    private static async Task DeleteUnitsByPrefixAsync(SqlConnection p_objConnection, string p_strPrefix)
    {
        await EnsureTargetIdentityAsync(p_objConnection);
        await using var v_objCommand = new SqlCommand(
            "DELETE FROM dbo.tbl_DM_Don_Vi_Tinh WHERE LEFT(Ten_Don_Vi_Tinh,LEN(@Prefix)) COLLATE Latin1_General_100_BIN2=@Prefix COLLATE Latin1_General_100_BIN2;",
            p_objConnection);
        v_objCommand.Parameters.Add("@Prefix", SqlDbType.NVarChar, 200).Value = p_strPrefix;
        await v_objCommand.ExecuteNonQueryAsync();
    }

    private static async Task EnsureTargetIdentityAsync(SqlConnection p_objConnection)
    {
        if (p_objConnection.State != ConnectionState.Open)
            throw new InvalidOperationException("The test SQL connection is not open.");

        await using var v_objCommand = new SqlCommand("SELECT @@SERVERNAME,DB_NAME();", p_objConnection);
        await using var v_objReader = await v_objCommand.ExecuteReaderAsync();
        if (!await v_objReader.ReadAsync() ||
            !string.Equals(v_objReader.GetString(0), @"DESKTOP-NHQ7QPL\MSSQLSERVER19", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(v_objReader.GetString(1), "TKS_Thuc_Tap_V11_C01C02_Test_20260906", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing Product Import integration work outside the approved TEST database.");
        }
    }

    private static async Task<string> GetDatabaseIdentityAsync(SqlConnection p_objConnection)
    {
        await EnsureTargetIdentityAsync(p_objConnection);
        await using var v_objCommand = new SqlCommand("SELECT CONCAT(@@SERVERNAME,N' / ',DB_NAME());", p_objConnection);
        return (string)(await v_objCommand.ExecuteScalarAsync() ?? "");
    }

    private sealed class CAllowAddPermission : IProductImportPermissionResolver
    {
        public bool Has_Add_Permission(string p_strUserName, string p_strFunctionCode) => true;
    }

    private sealed record CWorkbookRow(string Code, string Name, string CategoryCode, string UnitName, string Note);
    private sealed record ProductSample(
        string Code, string Name, long CategoryId, long UnitId, string Note,
        string CreatedBy, string CreatedByFunction, string LastUpdatedBy, string LastUpdatedByFunction);
    private sealed record ProbeEvidence(
        int Rows, int DistinctSessions, int DistinctTransactions,
        int MinTranCount, int MaxTranCount, int MinXactState, int MaxXactState);
    private sealed record CImportTiming(double ParseMs, double PreviewMs, double ConfirmMs, double TotalMs);
}
