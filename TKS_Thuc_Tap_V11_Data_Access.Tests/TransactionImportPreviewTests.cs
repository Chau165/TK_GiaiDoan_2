using ClosedXML.Excel;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class TransactionImportPreviewTests
{
    private readonly CImportProfileProvider m_objProfiles = new();

    [Fact]
    public async Task Inbound_preview_uses_received_quantity_and_product_master_unit_without_blocking_on_source_only_fields()
    {
        var v_objPreview = await PreviewAsync(
            "Inbound",
            new[] { "Số CT", "Ngày CT", "Kho nhận", "Mã NCC" },
            new object?[][] { new object?[] { "PN-01", new DateTime(2026, 9, 1), "WH-01", "SUP-01" } },
            new[] { "Số CT", "Mã VT", "Tên hàng", "ĐVT", "SL chứng từ", "SL thực nhận", "Đơn giá", "Loại sản phẩm", "Số lô" },
            new object?[][] { new object?[] { "PN-01", "SP-01", "Sản phẩm 01", "CHAI", 40d, 39d, 215000d, "Sai loại", "LOT-01" } },
            References());

        Assert.True(v_objPreview.StructureValid);
        var v_objDocument = Assert.Single(v_objPreview.Documents);
        Assert.Equal("PN-01", v_objDocument.DocumentNumber);
        Assert.Equal(new DateTime(2026, 9, 1), v_objDocument.DocumentDate);
        Assert.Equal("WH-01", v_objDocument.WarehouseCode);
        Assert.Single(v_objDocument.Lines);
        Assert.Equal(39m, v_objDocument.Lines[0].Quantity);
        Assert.Equal(215000m, v_objDocument.Lines[0].UnitPrice);
        Assert.Equal(50L, v_objDocument.Lines[0].Unit_ID);
        Assert.Empty(v_objPreview.Errors);
        Assert.Contains(v_objPreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "ĐVT" && v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED");
        Assert.Contains(v_objPreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "Loại sản phẩm" && v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED");
        Assert.Contains(v_objPreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "SL chứng từ" && v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED");
        Assert.Contains(v_objPreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "Số lô" && v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED");
        Assert.False(v_objPreview.PersistenceBlocked);
        Assert.True(v_objPreview.Can_Confirm);
    }

    [Fact]
    public async Task Header_detail_preview_reports_orphans_empty_documents_and_invalid_quantity()
    {
        var v_objPreview = await PreviewAsync(
            "Inbound",
            new[] { "Số CT", "Ngày CT", "Kho nhận", "Mã NCC" },
            new object?[][] { new object?[] { "PN-EMPTY", new DateTime(2026, 9, 1), "WH-01", "SUP-01" } },
            new[] { "Số CT", "Mã VT", "SL thực nhận", "Đơn giá" },
            new object?[][] { new object?[] { "PN-ORPHAN", "SP-01", 0d, 1d } },
            References());

        Assert.Contains(v_objPreview.Errors, v_objError => v_objError.Error_Code == "ORPHAN_DETAIL");
        Assert.Contains(v_objPreview.Errors, v_objError => v_objError.Error_Code == "MISSING_DETAIL");
        Assert.Contains(v_objPreview.Errors, v_objError => v_objError.Error_Code == "INVALID_QUANTITY");
    }

    [Fact]
    public async Task Inbound_preview_reports_product_reference_not_found()
    {
        var v_objPreview = await PreviewAsync(
            "Inbound",
            new[] { "Số CT", "Ngày CT", "Kho nhận", "Mã NCC" },
            new object?[][] { new object?[] { "PN-01", new DateTime(2026, 9, 1), "WH-01", "SUP-01" } },
            new[] { "Số CT", "Mã VT", "SL thực nhận", "Đơn giá" },
            new object?[][] { new object?[] { "PN-01", "SP-MISSING", 10d, 1d } },
            References());

        Assert.Contains(v_objPreview.Errors, v_objError =>
            v_objError.Error_Code == "REFERENCE_NOT_FOUND" && v_objError.CanonicalField == "ProductCode");
    }

    [Fact]
    public async Task Inbound_preview_uses_product_master_unit_even_when_excel_uom_differs()
    {
        var v_objReferences = new CTransactionImportReferences
        {
            Warehouses = new[] { new CWarehouseMaster { Auto_ID = 10, Name = "Kho Bình Tân" } },
            Suppliers = new[] { new CWarehouseMaster { Auto_ID = 20, Code = "NCC001", Name = "Nhà cung cấp 1" } },
            Products = new[] { new CWarehouseMaster { Auto_ID = 30, Code = "SP0001", Name = "Nước suối 500ml", Related_ID = 60, Related_ID_2 = 50 } },
            Units = new[]
            {
                new CWarehouseMaster { Auto_ID = 50, Name = "Thùng" },
                new CWarehouseMaster { Auto_ID = 51, Code = "CHAI", Name = "Chai" }
            }
        };
        var v_objPreview = await PreviewAsync(
            "Inbound",
            new[] { "Số CT", "Ngày CT", "Kho nhận", "Mã NCC" },
            new object?[][] { new object?[] { "PN-01", new DateTime(2026, 9, 1), " KHO-HCM-01 ", "NCC001" } },
            new[] { "Số CT", "Mã VT", "Tên hàng", "ĐVT", "SL thực nhận", "Đơn giá" },
            new object?[][] { new object?[] { "PN-01", "SP0001", "Nước suối 500ml", " CHAI ", 10d, 1d } },
            v_objReferences);

        var v_objDocument = Assert.Single(v_objPreview.Documents);
        var v_objLine = Assert.Single(v_objDocument.Lines);
        Assert.Equal(10L, v_objDocument.Warehouse_ID);
        Assert.Equal(30L, v_objLine.Product_ID);
        Assert.Equal(50L, v_objLine.Unit_ID);
        Assert.Contains(v_objPreview.Value_Mappings, v_objMapping =>
            v_objMapping.SourceValue.Trim() == "KHO-HCM-01" && v_objMapping.MappedValue == "Kho Bình Tân" && v_objMapping.Status == "RESOLVED");
        Assert.DoesNotContain(v_objPreview.Errors, v_objError =>
            v_objError.Error_Code is "REFERENCE_NOT_FOUND" or "VALUE_MAPPING_NOT_FOUND" or "PRODUCT_NAME_MISMATCH" or "UNSUPPORTED_DOMAIN_CAPABILITY");
        Assert.False(v_objPreview.PersistenceBlocked);
    }

    [Fact]
    public async Task Transaction_preview_rejects_product_code_name_mismatch()
    {
        var v_objPreview = await PreviewAsync(
            "Inbound",
            new[] { "Số CT", "Ngày CT", "Kho nhận", "Mã NCC" },
            new object?[][] { new object?[] { "PN-01", new DateTime(2026, 9, 1), "WH-01", "SUP-01" } },
            new[] { "Số CT", "Mã VT", "Tên hàng", "SL thực nhận", "Đơn giá" },
            new object?[][] { new object?[] { "PN-01", "SP-01", "Tên khác", 10d, 1d } },
            References());

        Assert.Contains(v_objPreview.Errors, v_objError =>
            v_objError.Error_Code == "PRODUCT_NAME_MISMATCH" && v_objError.CanonicalField == "ProductName");
    }

    [Theory]
    [InlineData(90d, false)]
    [InlineData(110d, true)]
    public async Task Outbound_preview_compares_aggregate_quantity_with_available_inventory(decimal p_decQuantity, bool p_bInsufficient)
    {
        var v_objPreview = await PreviewAsync(
            "Outbound",
            new[] { "Số PX", "Ngày xuất", "Kho xuất" },
            new object?[][] { new object?[] { "PX-01", new DateTime(2026, 9, 2), "WH-01" } },
            new[] { "Số PX", "Mã hàng", "SL xuất", "Đơn giá" },
            new object?[][] { new object?[] { "PX-01", "SP-01", p_decQuantity, 1d } },
            References(100m));

        Assert.Equal(p_bInsufficient,
            v_objPreview.Errors.Any(v_objError => v_objError.Error_Code == "INSUFFICIENT_STOCK"));
    }

    [Fact]
    public async Task Outbound_preview_aggregates_same_inventory_key_across_documents()
    {
        var v_objPreview = await PreviewAsync(
            "Outbound",
            new[] { "Số PX", "Ngày xuất", "Kho xuất" },
            new object?[][]
            {
                new object?[] { "PX-01", new DateTime(2026, 9, 2), "WH-01" },
                new object?[] { "PX-02", new DateTime(2026, 9, 2), "WH-01" }
            },
            new[] { "Số PX", "Mã hàng", "SL xuất", "Đơn giá" },
            new object?[][]
            {
                new object?[] { "PX-01", "SP-01", 60d, 1d },
                new object?[] { "PX-02", "SP-01", 60d, 1d }
            },
            References(100m));

        Assert.Contains(v_objPreview.Errors, v_objError => v_objError.Error_Code == "INSUFFICIENT_STOCK");
    }

    [Fact]
    public async Task Transaction_preview_requires_a_positive_unit_price_for_current_document_detail()
    {
        var v_objPreview = await PreviewAsync(
            "Outbound",
            new[] { "Số PX", "Ngày xuất", "Kho xuất" },
            new object?[][] { new object?[] { "PX-01", new DateTime(2026, 9, 2), "WH-01" } },
            new[] { "Số PX", "Mã hàng", "SL xuất", "Đơn giá" },
            new object?[][] { new object?[] { "PX-01", "SP-01", 2d, null } },
            References());

        Assert.Contains(v_objPreview.Errors, v_objError => v_objError.Error_Code == "MISSING_REQUIRED_PRICE");
        Assert.False(v_objPreview.Can_Confirm);
    }

    [Fact]
    public async Task Transaction_preview_rejects_zero_unit_price()
    {
        var v_objPreview = await PreviewAsync(
            "Inbound",
            new[] { "Số CT", "Ngày CT", "Kho nhận", "Mã NCC" },
            new object?[][] { new object?[] { "PN-01", new DateTime(2026, 9, 1), "WH-01", "SUP-01" } },
            new[] { "Số CT", "Mã VT", "SL thực nhận", "Đơn giá" },
            new object?[][] { new object?[] { "PN-01", "SP-01", 2d, 0d } },
            References());

        Assert.Contains(v_objPreview.Errors, v_objError => v_objError.Error_Code == "INVALID_UNIT_PRICE");
        Assert.False(v_objPreview.Can_Confirm);
    }

    [Fact]
    public async Task Transaction_preview_skips_an_identical_existing_document()
    {
        var v_objPreview = await PreviewAsync(
            "Inbound",
            new[] { "Số CT", "Ngày CT", "Kho nhận", "Mã NCC" },
            new object?[][] { new object?[] { "PN-01", new DateTime(2026, 9, 1), "WH-01", "SUP-01" } },
            new[] { "Số CT", "Mã VT", "SL thực nhận", "Đơn giá" },
            new object?[][] { new object?[] { "PN-01", "SP-01", 10d, 2d } },
            References(existingDocuments: new[]
            {
                ExistingDocument("PN-01", 10m, 2m)
            }));

        var v_objDocument = Assert.Single(v_objPreview.Documents);
        Assert.Equal("SKIPPED_DUPLICATE_DOCUMENT", v_objDocument.Import_Status);
        Assert.Empty(v_objPreview.Errors);
        Assert.True(v_objPreview.Can_Confirm);
    }

    [Fact]
    public async Task Transaction_preview_blocks_an_existing_document_with_different_target_data()
    {
        var v_objPreview = await PreviewAsync(
            "Inbound",
            new[] { "Số CT", "Ngày CT", "Kho nhận", "Mã NCC" },
            new object?[][] { new object?[] { "PN-01", new DateTime(2026, 9, 1), "WH-01", "SUP-01" } },
            new[] { "Số CT", "Mã VT", "SL thực nhận", "Đơn giá" },
            new object?[][] { new object?[] { "PN-01", "SP-01", 11d, 2d } },
            References(existingDocuments: new[]
            {
                ExistingDocument("PN-01", 10m, 2m)
            }));

        Assert.Contains(v_objPreview.Errors, v_objError => v_objError.Error_Code == "DUPLICATE_DOCUMENT_CONFLICT");
        Assert.False(v_objPreview.Can_Confirm);
    }

    [Fact]
    public async Task Transaction_import_reloads_references_skips_identical_document_and_saves_only_new_document()
    {
        var v_objStore = new CTestTransactionImportStore(References(existingDocuments: new[]
        {
            ExistingDocument("PN-01", 10m, 2m)
        }));
        var v_objService = new CWarehouseTransactionImportService(
            new CTransactionImportPreviewService(m_objProfiles),
            v_objStore,
            new CTestTransactionImportPermissionResolver(true));
        var v_arrContent = CreateWorkbookBytes(
            new[] { "Số CT", "Ngày CT", "Kho nhận", "Mã NCC" },
            new object?[][]
            {
                new object?[] { "PN-01", new DateTime(2026, 9, 1), "WH-01", "SUP-01" },
                new object?[] { "PN-02", new DateTime(2026, 9, 1), "WH-01", "SUP-01" }
            },
            new[] { "Số CT", "Mã VT", "SL thực nhận", "Đơn giá" },
            new object?[][]
            {
                new object?[] { "PN-01", "SP-01", 10d, 2d },
                new object?[] { "PN-02", "SP-01", 5d, 3d }
            });

        var v_objResult = await v_objService.ConfirmAsync(
            "transaction.xlsx", v_arrContent, "Inbound", "user", "receipt-function");

        Assert.Equal(1, v_objStore.Reference_Load_Count);
        var v_objSaved = Assert.Single(v_objStore.Saved_Documents);
        Assert.Equal("PN-02", v_objSaved.Header.So_Phieu);
        Assert.Equal(5m, Assert.Single(v_objSaved.Details).So_Luong);
        Assert.Equal(1, v_objResult.Imported_Documents);
        Assert.Equal(1, v_objResult.Skipped_Documents);
    }

    [Fact]
    public async Task Transaction_import_rejects_fresh_duplicate_conflict_before_any_save()
    {
        var v_objStore = new CTestTransactionImportStore(References(existingDocuments: new[]
        {
            ExistingDocument("PN-01", 11m, 2m)
        }));
        var v_objService = new CWarehouseTransactionImportService(
            new CTransactionImportPreviewService(m_objProfiles),
            v_objStore,
            new CTestTransactionImportPermissionResolver(true));
        var v_arrContent = CreateWorkbookBytes(
            new[] { "Số CT", "Ngày CT", "Kho nhận", "Mã NCC" },
            new object?[][] { new object?[] { "PN-01", new DateTime(2026, 9, 1), "WH-01", "SUP-01" } },
            new[] { "Số CT", "Mã VT", "SL thực nhận", "Đơn giá" },
            new object?[][] { new object?[] { "PN-01", "SP-01", 10d, 2d } });

        var v_objException = await Assert.ThrowsAsync<CTransactionImportValidationException>(() =>
            v_objService.ConfirmAsync("transaction.xlsx", v_arrContent, "Inbound", "user", "receipt-function"));

        Assert.Contains(v_objException.PreviewResult.Errors, v_objError =>
            v_objError.Error_Code == "DUPLICATE_DOCUMENT_CONFLICT");
        Assert.Empty(v_objStore.Saved_Documents);
    }

    [Fact]
    public async Task Transaction_import_checks_add_permission_before_loading_references()
    {
        var v_objStore = new CTestTransactionImportStore(References());
        var v_objService = new CWarehouseTransactionImportService(
            new CTransactionImportPreviewService(m_objProfiles),
            v_objStore,
            new CTestTransactionImportPermissionResolver(false));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            v_objService.PreviewAsync("transaction.xlsx", new byte[] { 1 }, "Inbound", "user", "receipt-function"));

        Assert.Equal(0, v_objStore.Reference_Load_Count);
        Assert.Empty(v_objStore.Saved_Documents);
    }

    private async Task<CTransactionImportPreviewResult> PreviewAsync(
        string p_strProfile,
        string[] p_arrHeaderHeaders,
        object?[][] p_arrHeaderRows,
        string[] p_arrDetailHeaders,
        object?[][] p_arrDetailRows,
        CTransactionImportReferences p_objReferences)
    {
        using var v_objStream = new MemoryStream(CreateWorkbookBytes(
            p_arrHeaderHeaders, p_arrHeaderRows, p_arrDetailHeaders, p_arrDetailRows));
        var v_objParsedWorkbook = await new CWorkbook_Xlsx_Reader().ReadAsync(v_objStream);

        return new CTransactionImportPreviewService(m_objProfiles)
            .Preview(v_objParsedWorkbook, p_strProfile, p_objReferences);
    }

    private static byte[] CreateWorkbookBytes(
        string[] p_arrHeaderHeaders,
        object?[][] p_arrHeaderRows,
        string[] p_arrDetailHeaders,
        object?[][] p_arrDetailRows)
    {
        using var v_objWorkbook = new XLWorkbook();
        AddSheet(v_objWorkbook, "Arbitrary header tab", p_arrHeaderHeaders, p_arrHeaderRows);
        AddSheet(v_objWorkbook, "Arbitrary detail tab", p_arrDetailHeaders, p_arrDetailRows);
        using var v_objStream = new MemoryStream();
        v_objWorkbook.SaveAs(v_objStream);
        return v_objStream.ToArray();
    }

    private static CTransactionImportReferences References(
        decimal p_decAvailable = 1_000m,
        IReadOnlyList<CTransactionImportExistingDocument>? existingDocuments = null) => new()
    {
        Warehouses = new[] { new CWarehouseMaster { Auto_ID = 10, Code = "WH-01", Name = "Kho 01" } },
        Suppliers = new[] { new CWarehouseMaster { Auto_ID = 20, Code = "SUP-01", Name = "NCC 01" } },
        Products = new[] { new CWarehouseMaster { Auto_ID = 30, Code = "SP-01", Name = "Sản phẩm 01", Related_ID = 60, Related_ID_2 = 50 } },
        Units = new[] { new CWarehouseMaster { Auto_ID = 50, Name = "Thùng" } },
        ExistingDocuments = existingDocuments ?? Array.Empty<CTransactionImportExistingDocument>(),
        InventoryBalances = new[]
        {
            new CTransactionImportInventoryBalance(10, 30, p_decAvailable, 0m)
        }
    };

    private static CTransactionImportExistingDocument ExistingDocument(string p_strDocumentNumber, decimal p_decQuantity, decimal p_decPrice) => new(
        new CWarehouseDocument
        {
            Auto_ID = 100,
            Is_Receipt = true,
            So_Phieu = p_strDocumentNumber,
            Kho_ID = 10,
            NCC_ID = 20,
            Ngay_Chung_Tu = new DateTime(2026, 9, 1),
            Ghi_Chu = ""
        },
        new[]
        {
            new CWarehouseDocumentDetail
            {
                Document_ID = 100,
                San_Pham_ID = 30,
                So_Luong = p_decQuantity,
                Don_Gia = p_decPrice
            }
        });

    private static void AddSheet(
        XLWorkbook p_objWorkbook,
        string p_strName,
        string[] p_arrHeaders,
        object?[][] p_arrRows)
    {
        var v_objSheet = p_objWorkbook.Worksheets.Add(p_strName);
        for (var v_iColumn = 0; v_iColumn < p_arrHeaders.Length; v_iColumn++)
            v_objSheet.Cell(1, v_iColumn + 1).Value = p_arrHeaders[v_iColumn];
        for (var v_iRow = 0; v_iRow < p_arrRows.Length; v_iRow++)
            for (var v_iColumn = 0; v_iColumn < p_arrRows[v_iRow].Length; v_iColumn++)
                v_objSheet.Cell(v_iRow + 2, v_iColumn + 1).Value = XLCellValue.FromObject(p_arrRows[v_iRow][v_iColumn]);
    }

    private sealed class CTestTransactionImportStore(CTransactionImportReferences p_objReferences) : ITransactionImportStore
    {
        public int Reference_Load_Count { get; private set; }
        public List<(CWarehouseDocument Header, IReadOnlyList<CWarehouseDocumentDetail> Details)> Saved_Documents { get; } = new();

        public Task<CTransactionImportReferences> Load_References_Async(
            string p_strProfileCode,
            IReadOnlyList<string> p_arrDocumentNumbers,
            string p_strCurrent_Login,
            CancellationToken p_objCancellationToken)
        {
            Reference_Load_Count++;
            return Task.FromResult(p_objReferences);
        }

        public Task Save_Document_Atomically_Async(
            CWarehouseDocument p_objDocument,
            IReadOnlyList<CWarehouseDocumentDetail> p_arrDetails,
            string p_strCurrent_User,
            string p_strCurrent_Function,
            CancellationToken p_objCancellationToken)
        {
            Saved_Documents.Add((p_objDocument, p_arrDetails));
            return Task.CompletedTask;
        }
    }

    private sealed class CTestTransactionImportPermissionResolver(bool p_bHasPermission) : ITransactionImportPermissionResolver
    {
        public bool Has_Add_Permission(string p_strUserName, string p_strFunctionCode) => p_bHasPermission;
    }
}
