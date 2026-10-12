using ClosedXML.Excel;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class ImportStructureDetectionTests
{
    private readonly CWorkbook_Xlsx_Reader m_objReader = new();
    private readonly CWorkbookStructureDetector m_objDetector = new();
    private readonly CImportProfileProvider m_objProfiles = new();

    [Fact]
    public void Header_normalization_ignores_spacing_and_separator_style()
    {
        Assert.Equal("MÃ VT", CImportHeaderNormalizer.Normalize("  Mã_VT  "));
        Assert.Equal("MA SAN PHAM", CImportHeaderNormalizer.Normalize("ma-san   pham"));
    }

    [Fact]
    public void Business_comparer_preserves_vietnamese_diacritics()
    {
        Assert.True(CImportBusinessValueComparer.AreEqual("  Cà   Phê ", "cà phê"));
        Assert.False(CImportBusinessValueComparer.AreEqual("Cuốn", "Cuộn"));
    }

    [Fact]
    public async Task Customer_product_workbook_detects_header_after_title_and_renamed_sheet()
    {
        await using var v_objStream = OpenFixture("04_San_pham_KH.xlsx");
        var v_objWorkbook = await m_objReader.ReadAsync(v_objStream);
        var v_objResult = m_objDetector.Detect(v_objWorkbook, m_objProfiles.GetByCode("Product"));

        Assert.True(v_objResult.IsValid);
        var v_objSheet = Assert.Single(v_objResult.SheetMappings);
        Assert.Equal(3, v_objSheet.HeaderRowNumber);
        Assert.Equal("SP0001", v_objSheet.DataRows[0].Values["ProductCode"]);
        Assert.Equal("Nước suối tinh khiết 500ml", v_objSheet.DataRows[0].Values["ProductName"]);
        Assert.Contains(v_objSheet.Columns, v_objColumn =>
            v_objColumn.Header == "Barcode" && v_objColumn.Disposition == EImportColumnDisposition.Ignored);
    }

    [Fact]
    public async Task Unit_fixture_uses_exact_code_mapping_and_audited_safe_ignores()
    {
        await using var v_objStream = OpenFixture("02_Don_vi_tinh_KH.xlsx");
        var v_objWorkbook = await m_objReader.ReadAsync(v_objStream);
        var v_objResult = m_objDetector.Detect(v_objWorkbook, m_objProfiles.GetByCode("Unit"));

        var v_objSheet = Assert.Single(v_objResult.SheetMappings);
        Assert.Contains("Tên gọi", v_objSheet.MappedColumns);
        Assert.Contains("Mã ĐVT", v_objSheet.MappedColumns);
        Assert.Contains("Hệ số QĐ", v_objSheet.IgnoredColumns);
        Assert.Contains("ĐVT gốc", v_objSheet.IgnoredColumns);
        Assert.Empty(v_objSheet.CapabilityIssues);
        Assert.Contains(v_objSheet.PolicyDiagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "Mã ĐVT" && v_objDiagnostic.SourceValue == "CAI" &&
            v_objDiagnostic.MappedValue == "Cái" && v_objDiagnostic.Status == "MAPPED_AND_MATCHED");
        Assert.Contains(v_objSheet.PolicyDiagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "Ký hiệu in phiếu" && v_objDiagnostic.SourceValue == "Kg" &&
            v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED" && v_objDiagnostic.Status == "IGNORED" &&
            !string.IsNullOrWhiteSpace(v_objDiagnostic.Reason));
        Assert.Contains(v_objSheet.PolicyDiagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "Hệ số QĐ" && v_objDiagnostic.SourceValue == "1" &&
            v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED" && v_objDiagnostic.Status == "IGNORED");
    }

    [Fact]
    public async Task Category_fixture_ignores_lot_and_expiry_fields_with_row_diagnostics()
    {
        await using var v_objStream = OpenFixture("03_Loai_san_pham_KH.xlsx");
        var v_objWorkbook = await m_objReader.ReadAsync(v_objStream);
        var v_objResult = m_objDetector.Detect(v_objWorkbook, m_objProfiles.GetByCode("Category"));

        var v_objSheet = Assert.Single(v_objResult.SheetMappings);
        Assert.Empty(v_objSheet.CapabilityIssues);
        Assert.Contains("Theo dõi LOT?", v_objSheet.IgnoredColumns);
        Assert.Contains("Theo dõi HSD?", v_objSheet.IgnoredColumns);
        Assert.Contains(v_objSheet.PolicyDiagnostics, v_objDiagnostic =>
            v_objDiagnostic.CanonicalField == "LotTracking" && v_objDiagnostic.SourceValue == "Có" &&
            v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED" && v_objDiagnostic.Status == "IGNORED");
        Assert.Contains(v_objSheet.PolicyDiagnostics, v_objDiagnostic =>
            v_objDiagnostic.CanonicalField == "ExpiryTracking" && v_objDiagnostic.SourceValue == "Có" &&
            v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED" && v_objDiagnostic.Status == "IGNORED");
    }

    [Fact]
    public async Task Nonempty_unknown_column_creates_a_domain_capability_issue()
    {
        using var v_objStream = CreateWorkbook(
            "Products",
            new[] { "Mã VT", "Tên hàng", "Nhóm hàng", "Đơn vị tính", "Ghi chú", "Customer extra" },
            new[] { "SP-01", "Bàn", "CAT-01", "Cái", "", "unknown value" });
        var v_objWorkbook = await m_objReader.ReadAsync(v_objStream);
        var v_objResult = m_objDetector.Detect(v_objWorkbook, m_objProfiles.GetByCode("Product"));

        var v_objSheet = Assert.Single(v_objResult.SheetMappings);
        Assert.Contains(v_objSheet.CapabilityIssues, v_objIssue =>
            v_objIssue.ErrorCode == "UNSUPPORTED_DOMAIN_CAPABILITY" && v_objIssue.SourceColumn == "Customer extra");
    }

    [Fact]
    public async Task Customer_master_workbooks_flow_through_profiled_readers_and_current_validators()
    {
        await using var v_objUnitStream = OpenFixture("02_Don_vi_tinh_KH.xlsx");
        var v_objUnit = await new CUnit_Xlsx_Reader().ReadAsync(v_objUnitStream);
        var v_objUnitPreview = CUnitImportValidator.Preview(v_objUnit, Array.Empty<string>());
        Assert.True(v_objUnitPreview.Can_Confirm, string.Join("; ", v_objUnitPreview.Errors.Select(v_objError => v_objError.Message)));
        Assert.NotEmpty(v_objUnitPreview.Field_Policy_Diagnostics);

        await using var v_objCategoryStream = OpenFixture("03_Loai_san_pham_KH.xlsx");
        var v_objCategory = await new CCategory_Xlsx_Reader().ReadAsync(v_objCategoryStream);
        var v_objCategoryPreview = CCategoryImportValidator.Preview(v_objCategory, Array.Empty<CDM_Loai_San_Pham>());
        Assert.True(v_objCategoryPreview.Can_Confirm, string.Join("; ", v_objCategoryPreview.Errors.Select(v_objError => v_objError.Message)));
        Assert.Equal(5, v_objCategoryPreview.Valid_Rows);
        Assert.Empty(v_objCategoryPreview.Unsupported_Capabilities);

        await using var v_objSupplierStream = OpenFixture("05_Nha_cung_cap_KH.xlsx");
        var v_objSupplier = await new CSupplier_Xlsx_Reader().ReadAsync(v_objSupplierStream);
        var v_objSupplierPreview = CSupplierImportValidator.Preview(v_objSupplier, Array.Empty<CDM_NCC>());
        Assert.True(v_objSupplierPreview.Can_Confirm, string.Join("; ", v_objSupplierPreview.Errors.Select(v_objError => v_objError.Message)));
        Assert.Equal(5, v_objSupplierPreview.Valid_Rows);
        Assert.Empty(v_objSupplierPreview.Unsupported_Capabilities);
        Assert.Contains(v_objSupplierPreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "Điện thoại" && v_objDiagnostic.SourceValue == "02873001111" &&
            v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED" && v_objDiagnostic.Status == "IGNORED" &&
            !string.IsNullOrWhiteSpace(v_objDiagnostic.Reason));
        Assert.Contains(v_objSupplierPreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "Còn giao dịch?" && v_objDiagnostic.SourceValue == "Có" &&
            v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED" && v_objDiagnostic.Status == "IGNORED");
        Assert.Contains(v_objSupplierPreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "MST" && v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED");
        Assert.Contains(v_objSupplierPreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "Điều khoản TT" && v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED");

        await using var v_objWarehouseStream = OpenFixture("01_Danh_muc_Kho_KH.xlsx");
        var v_objWarehouse = await new CWarehouse_Xlsx_Reader().ReadAsync(v_objWarehouseStream);
        var v_objWarehousePreview = CWarehouseImportValidator.Preview(v_objWarehouse, Array.Empty<CDM_Kho>());
        Assert.True(v_objWarehousePreview.Can_Confirm, string.Join("; ", v_objWarehousePreview.Errors.Select(v_objError => v_objError.Message)));
        Assert.Equal(4, v_objWarehousePreview.Valid_Rows);
        Assert.Empty(v_objWarehousePreview.Unsupported_Capabilities);
        Assert.Contains(v_objWarehousePreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "Trạng thái" && v_objDiagnostic.SourceValue == "Đang dùng" &&
            v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED" && v_objDiagnostic.Status == "IGNORED");
        Assert.Contains(v_objWarehousePreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "Trạng thái" && v_objDiagnostic.SourceValue == "Tạm ngưng" &&
            v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED" && v_objDiagnostic.Status == "IGNORED");
        Assert.Contains(v_objWarehousePreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "Mã kho" && v_objDiagnostic.Status == "MAPPED_AND_MATCHED");
        Assert.Contains(v_objWarehousePreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "Người phụ trách" && v_objDiagnostic.SourceValue == "Nguyễn Quốc Minh" &&
            v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED");
    }

    [Fact]
    public async Task Customer_product_workbook_reaches_preview_with_sample_category_and_unit_references()
    {
        await using var v_objCategoryStream = OpenFixture("03_Loai_san_pham_KH.xlsx");
        var v_objCategorySheet = await new CCategory_Xlsx_Reader().ReadAsync(v_objCategoryStream);
        var v_arrCategories = v_objCategorySheet.Rows
            .Where(v_objRow => v_objRow.RowNumber > 1)
            .Select((v_objRow, v_iIndex) => new CDM_Loai_San_Pham
            {
                Auto_ID = v_iIndex + 1,
                Ma_LSP = CellText(v_objRow.Cells, 1),
                Ten_LSP = CellText(v_objRow.Cells, 2),
                Ghi_Chu = CellText(v_objRow.Cells, 3)
            })
            .ToArray();

        await using var v_objUnitStream = OpenFixture("02_Don_vi_tinh_KH.xlsx");
        var v_objUnitSheet = await new CUnit_Xlsx_Reader().ReadAsync(v_objUnitStream);
        var v_arrUnits = v_objUnitSheet.Rows
            .Where(v_objRow => v_objRow.RowNumber > 1)
            .Select((v_objRow, v_iIndex) => new CDM_Don_Vi_Tinh
            {
                Auto_ID = v_iIndex + 1,
                Ten_Don_Vi_Tinh = CellText(v_objRow.Cells, 1),
                Ghi_Chu = CellText(v_objRow.Cells, 2)
            })
            .ToArray();

        await using var v_objProductStream = OpenFixture("04_San_pham_KH.xlsx");
        var v_objProductSheet = await new CProduct_Xlsx_Reader().ReadAsync(v_objProductStream);
        var v_objPreview = CProductImportValidator.Preview(
            v_objProductSheet,
            v_arrCategories,
            v_arrUnits,
            Array.Empty<string>());

        Assert.True(v_objPreview.Can_Confirm, string.Join("; ", v_objPreview.Errors.Select(v_objError => v_objError.Message)));
        Assert.Equal(15, v_objPreview.Total_Rows);
        Assert.Equal(15, v_objPreview.Valid_Rows);
        Assert.Equal(0, v_objPreview.Invalid_Rows);
        Assert.DoesNotContain(v_objPreview.Errors, v_objError =>
            v_objError.Message.Contains("Không tìm thấy đơn vị tính 'THUNG'", StringComparison.Ordinal));
        Assert.Empty(v_objPreview.Unsupported_Capabilities);
        Assert.Contains("Barcode", v_objPreview.Ignored_Columns);
        Assert.All(v_objPreview.Rows, v_objRow =>
        {
            Assert.True(v_objRow.Don_Vi_Tinh_ID > 0);
            Assert.Equal(0, v_objRow.Issue_Don_Vi_Tinh_ID);
        });
        Assert.Equal(15, v_objPreview.Field_Policy_Diagnostics.Count(v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "ĐVT xuất" &&
            v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED" &&
            v_objDiagnostic.Status == "IGNORED"));
        Assert.All(v_objPreview.Field_Policy_Diagnostics.Where(v_objDiagnostic => v_objDiagnostic.SourceColumn == "ĐVT xuất"),
            v_objDiagnostic => Assert.Equal(
                "Current Product domain stores one Unit_ID. Business rule defines Product.Unit_ID from Inbound UOM. Outbound UOM is not persisted by the current Product model.",
                v_objDiagnostic.Reason));
        Assert.Contains(v_objPreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "Trọng lượng (kg)" && v_objDiagnostic.SourceValue == "0.5" &&
            v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED" && v_objDiagnostic.Status == "IGNORED");
    }

    [Fact]
    public async Task Supplier_source_only_changes_do_not_conflict_with_target_duplicate()
    {
        using var v_objStream = CreateWorkbook(
            "NCC",
            new[] { "Mã đối tác", "Tên NCC", "MST", "Điều khoản TT" },
            new[] { "NCC-01", "Nhà cung cấp A", "0312345678", "45 ngày" });
        var v_objSheet = await new CSupplier_Xlsx_Reader().ReadAsync(v_objStream);
        var v_objPreview = CSupplierImportValidator.Preview(v_objSheet, new[]
        {
            new CDM_NCC { Ma_NCC = "NCC-01", Ten_NCC = "Nhà cung cấp A" }
        });

        var v_objRow = Assert.Single(v_objPreview.Rows);
        Assert.True(v_objRow.Is_Skipped);
        Assert.Empty(v_objRow.Errors);
        Assert.Contains(v_objPreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "Điều khoản TT" && v_objDiagnostic.SourceValue == "45 ngày" &&
            v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED");
    }

    [Fact]
    public async Task Product_inbound_unit_is_canonical_when_outbound_unit_differs()
    {
        using var v_objStream = CreateWorkbook(
            "Products",
            new[] { "Mã VT", "Tên hàng", "Nhóm hàng", "ĐVT nhập", "ĐVT xuất", "Ghi chú" },
            new[] { "SP-01", "Nước", "CAT-01", "THUNG", "CHAI", "" });
        var v_objWorkbook = await m_objReader.ReadAsync(v_objStream);
        var v_objDetected = m_objDetector.Detect(v_objWorkbook, m_objProfiles.GetByCode("Product"));
        var v_objSheet = Assert.Single(v_objDetected.SheetMappings);
        var v_objProjected = CImportLegacyWorksheetAdapter.ToProduct(v_objSheet);
        var v_objPreview = CProductImportValidator.Preview(
            v_objProjected,
            new[] { new CDM_Loai_San_Pham { Auto_ID = 1, Ma_LSP = "CAT-01" } },
            new[]
            {
                new CDM_Don_Vi_Tinh { Auto_ID = 2, Ten_Don_Vi_Tinh = "Thùng" },
                new CDM_Don_Vi_Tinh { Auto_ID = 3, Ten_Don_Vi_Tinh = "Chai" }
            },
            Array.Empty<string>());

        var v_objRow = Assert.Single(v_objPreview.Rows);
        Assert.True(v_objPreview.Can_Confirm, string.Join("; ", v_objPreview.Errors.Select(v_objError => v_objError.Message)));
        Assert.Equal("Thùng", v_objRow.Ten_Don_Vi_Tinh);
        Assert.Equal(2, v_objRow.Don_Vi_Tinh_ID);
        Assert.Equal(0, v_objRow.Issue_Don_Vi_Tinh_ID);
        Assert.Empty(v_objPreview.Unsupported_Capabilities);
        Assert.Contains(v_objPreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "ĐVT xuất" && v_objDiagnostic.SourceValue == "CHAI" &&
            v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED" && v_objDiagnostic.Status == "IGNORED" &&
            v_objDiagnostic.Reason == "Current Product domain stores one Unit_ID. Business rule defines Product.Unit_ID from Inbound UOM. Outbound UOM is not persisted by the current Product model.");
    }

    [Fact]
    public async Task Product_equal_units_after_exact_value_mapping_project_to_single_target_unit()
    {
        using var v_objStream = CreateWorkbook(
            "Products",
            new[] { "Mã VT", "Tên hàng", "Nhóm hàng", "ĐVT nhập", "ĐVT xuất", "Ghi chú" },
            new[] { "SP-02", "Nước", "CAT-01", "THUNG", "Thùng", "" });
        var v_objSheet = await new CProduct_Xlsx_Reader().ReadAsync(v_objStream);
        var v_objPreview = CProductImportValidator.Preview(
            v_objSheet,
            new[] { new CDM_Loai_San_Pham { Auto_ID = 1, Ma_LSP = "CAT-01" } },
            new[] { new CDM_Don_Vi_Tinh { Auto_ID = 2, Ten_Don_Vi_Tinh = "Thùng" } },
            Array.Empty<string>());

        var v_objRow = Assert.Single(v_objPreview.Rows);
        Assert.True(v_objPreview.Can_Confirm, string.Join("; ", v_objPreview.Errors.Select(v_objError => v_objError.Message)));
        Assert.Equal("Thùng", v_objRow.Ten_Don_Vi_Tinh);
        Assert.Equal(2, v_objRow.Don_Vi_Tinh_ID);
        Assert.Equal(0, v_objRow.Issue_Don_Vi_Tinh_ID);
        Assert.Empty(v_objPreview.Unsupported_Capabilities);
        Assert.Contains(v_objPreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "ĐVT xuất" && v_objDiagnostic.SourceValue == "Thùng" &&
            v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED" && v_objDiagnostic.Status == "IGNORED");
    }

    [Fact]
    public async Task Product_invalid_inbound_unit_does_not_fall_back_to_valid_outbound_unit()
    {
        using var v_objStream = CreateWorkbook(
            "Products",
            new[] { "Mã VT", "Tên hàng", "Nhóm hàng", "ĐVT nhập", "ĐVT xuất", "Ghi chú" },
            new[] { "SP-03", "Nước", "CAT-01", "UNKNOWN_UNIT", "CHAI", "" });
        var v_objSheet = await new CProduct_Xlsx_Reader().ReadAsync(v_objStream);
        var v_objPreview = CProductImportValidator.Preview(
            v_objSheet,
            new[] { new CDM_Loai_San_Pham { Auto_ID = 1, Ma_LSP = "CAT-01" } },
            new[] { new CDM_Don_Vi_Tinh { Auto_ID = 3, Ten_Don_Vi_Tinh = "Chai" } },
            Array.Empty<string>());

        var v_objRow = Assert.Single(v_objPreview.Rows);
        Assert.False(v_objPreview.Can_Confirm);
        Assert.Equal(0, v_objRow.Don_Vi_Tinh_ID);
        Assert.Contains(v_objRow.Errors, v_objError =>
            v_objError.Column_Name == "Ten_Don_Vi_Tinh" && v_objError.Message.Contains("Không tìm thấy đơn vị tính", StringComparison.Ordinal));
        Assert.DoesNotContain(v_objRow.Errors, v_objError => v_objError.Column_Name == "IssueUnitName");
        Assert.Contains(v_objPreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "ĐVT xuất" && v_objDiagnostic.SourceValue == "CHAI" &&
            v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED");
    }

    [Fact]
    public async Task Product_outbound_only_change_keeps_existing_product_as_exact_duplicate()
    {
        using var v_objStream = CreateWorkbook(
            "Products",
            new[] { "Mã VT", "Tên hàng", "Nhóm hàng", "ĐVT nhập", "ĐVT xuất", "Ghi chú" },
            new[] { "SP-03", "Nước", "CAT-01", "THUNG", "CHAI", "" });
        var v_objSheet = await new CProduct_Xlsx_Reader().ReadAsync(v_objStream);
        var v_objPreview = CProductImportValidator.Preview(
            v_objSheet,
            new[] { new CDM_Loai_San_Pham { Auto_ID = 1, Ma_LSP = "CAT-01" } },
            new[]
            {
                new CDM_Don_Vi_Tinh { Auto_ID = 2, Ten_Don_Vi_Tinh = "Thùng" },
                new CDM_Don_Vi_Tinh { Auto_ID = 3, Ten_Don_Vi_Tinh = "Chai" }
            },
            new[]
            {
                new CDM_San_Pham
                {
                    Ma_San_Pham = "SP-03",
                    Ten_San_Pham = "Nước",
                    Loai_San_Pham_ID = 1,
                    Don_Vi_Tinh_ID = 2
                }
            });

        var v_objRow = Assert.Single(v_objPreview.Rows);
        Assert.True(v_objRow.Is_Skipped);
        Assert.Empty(v_objRow.Errors);
        Assert.Equal(1, v_objPreview.Skipped_Rows);
        Assert.Contains(v_objPreview.Field_Policy_Diagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceColumn == "ĐVT xuất" && v_objDiagnostic.SourceValue == "CHAI");
    }

    [Fact]
    public async Task Product_inbound_unit_change_conflicts_with_existing_product()
    {
        using var v_objStream = CreateWorkbook(
            "Products",
            new[] { "Mã VT", "Tên hàng", "Nhóm hàng", "ĐVT nhập", "ĐVT xuất", "Ghi chú" },
            new[] { "SP-03", "Nước", "CAT-01", "CHAI", "THUNG", "" });
        var v_objSheet = await new CProduct_Xlsx_Reader().ReadAsync(v_objStream);
        var v_objPreview = CProductImportValidator.Preview(
            v_objSheet,
            new[] { new CDM_Loai_San_Pham { Auto_ID = 1, Ma_LSP = "CAT-01" } },
            new[]
            {
                new CDM_Don_Vi_Tinh { Auto_ID = 2, Ten_Don_Vi_Tinh = "Thùng" },
                new CDM_Don_Vi_Tinh { Auto_ID = 3, Ten_Don_Vi_Tinh = "Chai" }
            },
            new[]
            {
                new CDM_San_Pham
                {
                    Ma_San_Pham = "SP-03",
                    Ten_San_Pham = "Nước",
                    Loai_San_Pham_ID = 1,
                    Don_Vi_Tinh_ID = 2
                }
            });

        var v_objRow = Assert.Single(v_objPreview.Rows);
        Assert.False(v_objRow.Is_Skipped);
        Assert.Equal(3, v_objRow.Don_Vi_Tinh_ID);
        Assert.Contains(v_objRow.Errors, v_objError =>
            v_objError.Column_Name == "Ma_San_Pham" && v_objError.Message.Contains("trùng khóa nhưng dữ liệu khác", StringComparison.Ordinal));
    }

    [Fact]
    public void Legacy_adapter_does_not_project_a_canonical_field_marked_ignored()
    {
        var v_objSheet = new CImportDetectedSheet(
            "Products",
            "Data",
            1,
            new[] { new CImportColumnMapping(1, "ĐVT nhập", "UnitName", EImportColumnDisposition.Ignored) },
            new[]
            {
                new CImportDetectedRow(
                    2,
                    false,
                    new Dictionary<string, object?> { ["UnitName"] = "Thùng" },
                    new HashSet<string>(),
                    new Dictionary<string, string?>(),
                    new Dictionary<string, string>(),
                    new Dictionary<string, string?>())
            });

        var v_objProjected = CImportLegacyWorksheetAdapter.ToProduct(v_objSheet);
        var v_objUnitCell = v_objProjected.Rows[1].Cells[3];

        Assert.Null(v_objUnitCell.Value);
        Assert.Equal("Blank", v_objUnitCell.CellType);
        Assert.Null(v_objUnitCell.Formula);
    }

    [Theory]
    [InlineData("06_Nhap_kho_Thang_09_2026_KH.xlsx", "Inbound")]
    [InlineData("07_Xuat_kho_Thang_09_2026_KH.xlsx", "Outbound")]
    public async Task Customer_transaction_workbooks_report_ignored_fields_and_missing_price_without_reference_data(
        string p_strFileName,
        string p_strProfile)
    {
        await using var v_objStream = OpenFixture(p_strFileName);
        var v_objPreview = await new CTransactionImportPreviewService(m_objProfiles)
            .PreviewAsync(v_objStream, p_strProfile);

        Assert.True(v_objPreview.StructureValid,
            string.Join("; ", v_objPreview.Errors.Select(v_objError => v_objError.Message)));
        Assert.NotEmpty(v_objPreview.Documents);
        Assert.Contains(v_objPreview.Documents, v_objDocument => v_objDocument.Lines.Count > 0);
        Assert.False(v_objPreview.PersistenceBlocked);
        Assert.False(v_objPreview.ReferenceDataLoaded);
        Assert.False(v_objPreview.Can_Confirm);
        Assert.NotEmpty(v_objPreview.Field_Policy_Diagnostics);
        if (p_strProfile == "Inbound")
        {
            Assert.Contains(v_objPreview.Field_Policy_Diagnostics, v_objDiagnostic =>
                v_objDiagnostic.SourceColumn == "SL chứng từ" && v_objDiagnostic.SourceValue == "120" &&
                v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED");
            Assert.DoesNotContain(v_objPreview.Errors, v_objError => v_objError.Error_Code == "UNSUPPORTED_DOMAIN_CAPABILITY");
        }
        else
        {
            Assert.Contains(v_objPreview.Errors, v_objError => v_objError.Error_Code == "MISSING_REQUIRED_PRICE");
            Assert.Contains(v_objPreview.Field_Policy_Diagnostics, v_objDiagnostic =>
                v_objDiagnostic.SourceColumn == "SL yêu cầu" && v_objDiagnostic.SourceValue == "25" &&
                v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED");
            Assert.Contains(v_objPreview.Field_Policy_Diagnostics, v_objDiagnostic =>
                v_objDiagnostic.SourceColumn == "Đơn vị nhận" && v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED");
            Assert.DoesNotContain(v_objPreview.Errors, v_objError => v_objError.Error_Code == "UNSUPPORTED_DOMAIN_CAPABILITY");
        }
    }

    [Theory]
    [InlineData("06_Nhap_kho_Thang_09_2026_KH.xlsx", "Inbound")]
    [InlineData("07_Xuat_kho_Thang_09_2026_KH.xlsx", "Outbound")]
    public async Task Customer_document_workbooks_detect_header_and_detail_roles(string p_strFileName, string p_strProfile)
    {
        await using var v_objStream = OpenFixture(p_strFileName);
        var v_objWorkbook = await m_objReader.ReadAsync(v_objStream);
        var v_objResult = m_objDetector.Detect(v_objWorkbook, m_objProfiles.GetByCode(p_strProfile));

        Assert.True(v_objResult.IsValid, DescribeFailure(v_objWorkbook, v_objResult));
        Assert.Equal(new[] { "Detail", "Header" }, v_objResult.SheetMappings.Select(v_objSheet => v_objSheet.Role).OrderBy(v_strRole => v_strRole));
        var v_objHeader = Assert.Single(v_objResult.SheetMappings, v_objSheet => v_objSheet.Role == "Header");
        Assert.Equal(3, v_objHeader.HeaderRowNumber);
        var v_strExpectedDocumentNumber = p_strProfile == "Inbound" ? "PN260901" : "PX260901";
        Assert.Contains(v_objHeader.DataRows, v_objRow => v_objRow.Values.Values.Contains(v_strExpectedDocumentNumber));
    }

    [Fact]
    public async Task Reordered_columns_and_aliases_are_detected_and_unknown_columns_are_classified()
    {
        using var v_objStream = CreateWorkbook(
            "Renamed sheet",
            new[] { "Ghi chú", "SKU", "Tên hàng", "Mã nhóm", "Đơn vị tính", "Customer extra" },
            new[] { "ghi chú", "SP-01", "Bàn", "CAT-01", "Cái", "x" });
        var v_objWorkbook = await m_objReader.ReadAsync(v_objStream);
        var v_objResult = m_objDetector.Detect(v_objWorkbook, m_objProfiles.GetByCode("Product"));

        Assert.True(v_objResult.IsValid);
        var v_objSheet = Assert.Single(v_objResult.SheetMappings);
        Assert.Equal("SP-01", v_objSheet.DataRows[0].Values["ProductCode"]);
        Assert.Contains(v_objSheet.Columns, v_objColumn =>
            v_objColumn.Header == "Customer extra" && v_objColumn.Disposition == EImportColumnDisposition.Unsupported);

        var v_objLegacySheet = CImportLegacyWorksheetAdapter.ToProduct(v_objSheet);
        var v_objPreview = CProductImportValidator.Preview(
            v_objLegacySheet,
            new[] { new CDM_Loai_San_Pham { Auto_ID = 10, Ma_LSP = "CAT-01" } },
            new[] { new CDM_Don_Vi_Tinh { Auto_ID = 20, Ten_Don_Vi_Tinh = "Cái" } },
            Array.Empty<string>());
        Assert.Contains("Customer extra", v_objPreview.Unsupported_Columns);
    }

    [Fact]
    public async Task Unit_code_mapping_must_exactly_match_the_supplied_unit_name()
    {
        using var v_objStream = CreateWorkbook(
            "Units",
            new[] { "Mã ĐVT", "Tên gọi" },
            new[] { "THUNG", "Hộp" });
        var v_objWorkbook = await m_objReader.ReadAsync(v_objStream);

        var v_objResult = m_objDetector.Detect(v_objWorkbook, m_objProfiles.GetByCode("Unit"));

        var v_objSheet = Assert.Single(v_objResult.SheetMappings);
        Assert.Contains(v_objSheet.CapabilityIssues, v_objIssue =>
            v_objIssue.ErrorCode == "REFERENCE_MAPPING_TARGET_MISMATCH" && v_objIssue.CanonicalField == "UnitCode");
        Assert.Contains(v_objSheet.PolicyDiagnostics, v_objDiagnostic =>
            v_objDiagnostic.SourceValue == "THUNG" && v_objDiagnostic.MappedValue == "Thùng" &&
            v_objDiagnostic.Status == "BLOCKED_MISMATCH");
    }

    [Fact]
    public async Task Unit_conversion_factor_is_ignored_even_when_non_identity()
    {
        using var v_objStream = CreateWorkbook(
            "Units",
            new[] { "Tên gọi", "Hệ số QĐ" },
            new[] { "Thùng", "24" });
        var v_objWorkbook = await m_objReader.ReadAsync(v_objStream);

        var v_objResult = m_objDetector.Detect(v_objWorkbook, m_objProfiles.GetByCode("Unit"));

        var v_objSheet = Assert.Single(v_objResult.SheetMappings);
        Assert.Empty(v_objSheet.CapabilityIssues);
        Assert.Contains("Hệ số QĐ", v_objSheet.IgnoredColumns);
        Assert.Contains(v_objSheet.PolicyDiagnostics, v_objDiagnostic =>
            v_objDiagnostic.CanonicalField == "UnitConversionFactor" && v_objDiagnostic.SourceValue == "24" &&
            v_objDiagnostic.Policy == "SOURCE_FIELD_IGNORED" && v_objDiagnostic.Status == "IGNORED");
    }

    [Fact]
    public async Task Duplicate_aliases_for_one_field_are_rejected_as_ambiguous()
    {
        using var v_objStream = CreateWorkbook(
            "Products",
            new[] { "Mã VT", "SKU", "Tên hàng", "Nhóm hàng", "ĐVT nhập" },
            new[] { "SP-01", "SP-01", "Bàn", "CAT-01", "Cái" });
        var v_objWorkbook = await m_objReader.ReadAsync(v_objStream);

        var v_objResult = m_objDetector.Detect(v_objWorkbook, m_objProfiles.GetByCode("Product"));

        Assert.Equal("AMBIGUOUS_MAPPING", v_objResult.ErrorCode);
    }

    [Fact]
    public async Task Missing_required_mapping_is_reported()
    {
        using var v_objStream = CreateWorkbook(
            "Products",
            new[] { "Mã VT", "Tên hàng", "Nhóm hàng" },
            new[] { "SP-01", "Bàn", "CAT-01" });
        var v_objWorkbook = await m_objReader.ReadAsync(v_objStream);

        var v_objResult = m_objDetector.Detect(v_objWorkbook, m_objProfiles.GetByCode("Product"));

        Assert.Equal("MISSING_REQUIRED_MAPPING", v_objResult.ErrorCode);
    }

    [Fact]
    public async Task Multiple_candidate_product_sheets_are_rejected_as_unsupported_structure()
    {
        using var v_objStream = CreateWorkbook(
            "Products",
            new[] { "Mã VT", "Tên hàng", "Nhóm hàng", "ĐVT nhập" },
            new[] { "SP-01", "Bàn", "CAT-01", "Cái" },
            duplicateSheet: true);
        var v_objWorkbook = await m_objReader.ReadAsync(v_objStream);

        var v_objResult = m_objDetector.Detect(v_objWorkbook, m_objProfiles.GetByCode("Product"));

        Assert.Equal("UNSUPPORTED_FILE_STRUCTURE", v_objResult.ErrorCode);
    }

    private static Stream OpenFixture(string p_strFileName) =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "CustomerImport", p_strFileName));

    private static string DescribeFailure(CWorkbook_Xlsx_Workbook p_objWorkbook, CWorkbookStructureDetectionResult p_objResult)
    {
        var v_strErrors = string.Join("; ", p_objResult.Errors.Select(v_objError => v_objError.Message));
        var v_strRows = string.Join(Environment.NewLine, p_objWorkbook.Worksheets.SelectMany(v_objSheet =>
            v_objSheet.Rows.Take(5).Select(v_objRow =>
                $"{v_objSheet.Name} row {v_objRow.RowNumber}: {string.Join(" | ", v_objRow.Cells.Select(v_objCell => v_objCell.Value))}")));
        return $"{v_strErrors}{Environment.NewLine}{v_strRows}";
    }

    private static string CellText(IReadOnlyList<CCategory_Xlsx_Cell> p_arrCells, int p_iColumn) =>
        Convert.ToString(p_arrCells.FirstOrDefault(v_objCell => v_objCell.ColumnNumber == p_iColumn)?.Value) ?? "";

    private static string CellText(IReadOnlyList<CUnit_Xlsx_Cell> p_arrCells, int p_iColumn) =>
        Convert.ToString(p_arrCells.FirstOrDefault(v_objCell => v_objCell.ColumnNumber == p_iColumn)?.Value) ?? "";

    private static MemoryStream CreateWorkbook(string p_strSheetName, string[] p_arrHeaders, string[] p_arrValues, bool duplicateSheet = false)
    {
        using var v_objWorkbook = new XLWorkbook();
        AddSheet(v_objWorkbook, p_strSheetName, p_arrHeaders, p_arrValues);
        if (duplicateSheet)
            AddSheet(v_objWorkbook, "Second product sheet", p_arrHeaders, p_arrValues);

        var v_objStream = new MemoryStream();
        v_objWorkbook.SaveAs(v_objStream);
        v_objStream.Position = 0;
        return v_objStream;
    }

    private static void AddSheet(XLWorkbook p_objWorkbook, string p_strName, string[] p_arrHeaders, string[] p_arrValues)
    {
        var v_objSheet = p_objWorkbook.Worksheets.Add(p_strName);
        for (var v_iColumn = 0; v_iColumn < p_arrHeaders.Length; v_iColumn++)
        {
            v_objSheet.Cell(1, v_iColumn + 1).Value = p_arrHeaders[v_iColumn];
            v_objSheet.Cell(2, v_iColumn + 1).Value = p_arrValues[v_iColumn];
        }
    }
}
