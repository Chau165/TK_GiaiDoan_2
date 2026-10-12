namespace TKS_Thuc_Tap_V11_Data_Access.Utility;

public static class CImportLegacyWorksheetAdapter
{
    public static CProduct_Xlsx_Worksheet ToProduct(CImportDetectedSheet p_objSheet) => Create(
        p_objSheet,
        "SanPham",
        new[] { "Ma_San_Pham", "Ten_San_Pham", "Ma_LSP", "Ten_Don_Vi_Tinh", "Ghi_Chu" },
        new[] { "ProductCode", "ProductName", "CategoryCode", "UnitName", "Note", "IssueUnitName" },
        (v_iColumn, v_strType, v_objValue, v_strFormula, v_strFormat) => new CProduct_Xlsx_Cell(v_iColumn, v_strType, v_objValue, v_strFormula, v_strFormat),
        (v_iRow, v_bHidden, v_arrCells) => new CProduct_Xlsx_Row(v_iRow, v_bHidden, v_arrCells),
        (v_strName, v_arrRows, v_arrMapped, v_arrIgnored, v_arrUnsupported, v_arrIssues, v_arrPolicyDiagnostics) => new CProduct_Xlsx_Worksheet(v_strName, v_arrRows)
        {
            MappedColumns = v_arrMapped,
            IgnoredColumns = v_arrIgnored,
            UnsupportedColumns = v_arrUnsupported,
            UnsupportedCapabilities = v_arrIssues,
            PolicyDiagnostics = v_arrPolicyDiagnostics
        });

    public static CUnit_Xlsx_Worksheet ToUnit(CImportDetectedSheet p_objSheet) => Create(
        p_objSheet,
        "DonViTinh",
        new[] { "Ten_Don_Vi_Tinh", "Ghi_Chu" },
        new[] { "UnitName", "Note" },
        (v_iColumn, v_strType, v_objValue, v_strFormula, v_strFormat) => new CUnit_Xlsx_Cell(v_iColumn, v_strType, v_objValue, v_strFormula, v_strFormat),
        (v_iRow, v_bHidden, v_arrCells) => new CUnit_Xlsx_Row(v_iRow, v_bHidden, v_arrCells),
        (v_strName, v_arrRows, v_arrMapped, v_arrIgnored, v_arrUnsupported, v_arrIssues, v_arrPolicyDiagnostics) => new CUnit_Xlsx_Worksheet(v_strName, v_arrRows)
        {
            MappedColumns = v_arrMapped,
            IgnoredColumns = v_arrIgnored,
            UnsupportedColumns = v_arrUnsupported,
            UnsupportedCapabilities = v_arrIssues,
            PolicyDiagnostics = v_arrPolicyDiagnostics
        });

    public static CCategory_Xlsx_Worksheet ToCategory(CImportDetectedSheet p_objSheet) => Create(
        p_objSheet,
        "LoaiSanPham",
        new[] { "Ma_LSP", "Ten_LSP", "Ghi_Chu" },
        new[] { "CategoryCode", "CategoryName", "Note" },
        (v_iColumn, v_strType, v_objValue, v_strFormula, v_strFormat) => new CCategory_Xlsx_Cell(v_iColumn, v_strType, v_objValue, v_strFormula, v_strFormat),
        (v_iRow, v_bHidden, v_arrCells) => new CCategory_Xlsx_Row(v_iRow, v_bHidden, v_arrCells),
        (v_strName, v_arrRows, v_arrMapped, v_arrIgnored, v_arrUnsupported, v_arrIssues, v_arrPolicyDiagnostics) => new CCategory_Xlsx_Worksheet(v_strName, v_arrRows)
        {
            MappedColumns = v_arrMapped,
            IgnoredColumns = v_arrIgnored,
            UnsupportedColumns = v_arrUnsupported,
            UnsupportedCapabilities = v_arrIssues,
            PolicyDiagnostics = v_arrPolicyDiagnostics
        });

    public static CSupplier_Xlsx_Worksheet ToSupplier(CImportDetectedSheet p_objSheet) => Create(
        p_objSheet,
        "NhaCungCap",
        new[] { "Ma_NCC", "Ten_NCC", "Ghi_Chu" },
        new[] { "SupplierCode", "SupplierName", "Note" },
        (v_iColumn, v_strType, v_objValue, v_strFormula, v_strFormat) => new CSupplier_Xlsx_Cell(v_iColumn, v_strType, v_objValue, v_strFormula, v_strFormat),
        (v_iRow, v_bHidden, v_arrCells) => new CSupplier_Xlsx_Row(v_iRow, v_bHidden, v_arrCells),
        (v_strName, v_arrRows, v_arrMapped, v_arrIgnored, v_arrUnsupported, v_arrIssues, v_arrPolicyDiagnostics) => new CSupplier_Xlsx_Worksheet(v_strName, v_arrRows)
        {
            MappedColumns = v_arrMapped,
            IgnoredColumns = v_arrIgnored,
            UnsupportedColumns = v_arrUnsupported,
            UnsupportedCapabilities = v_arrIssues,
            PolicyDiagnostics = v_arrPolicyDiagnostics
        });

    public static CWarehouse_Xlsx_Worksheet ToWarehouse(CImportDetectedSheet p_objSheet) => Create(
        p_objSheet,
        "Kho",
        new[] { "Ten_Kho", "Ghi_Chu" },
        new[] { "WarehouseName", "Note" },
        (v_iColumn, v_strType, v_objValue, v_strFormula, v_strFormat) => new CWarehouse_Xlsx_Cell(v_iColumn, v_strType, v_objValue, v_strFormula, v_strFormat),
        (v_iRow, v_bHidden, v_arrCells) => new CWarehouse_Xlsx_Row(v_iRow, v_bHidden, v_arrCells),
        (v_strName, v_arrRows, v_arrMapped, v_arrIgnored, v_arrUnsupported, v_arrIssues, v_arrPolicyDiagnostics) => new CWarehouse_Xlsx_Worksheet(v_strName, v_arrRows)
        {
            MappedColumns = v_arrMapped,
            IgnoredColumns = v_arrIgnored,
            UnsupportedColumns = v_arrUnsupported,
            UnsupportedCapabilities = v_arrIssues,
            PolicyDiagnostics = v_arrPolicyDiagnostics
        });

    private static TWorksheet Create<TWorksheet, TRow, TCell>(
        CImportDetectedSheet p_objSheet,
        string p_strLegacySheetName,
        IReadOnlyList<string> p_arrHeaders,
        IReadOnlyList<string> p_arrFields,
        Func<int, string, object?, string?, string?, TCell> p_dlgCellFactory,
        Func<int, bool, IReadOnlyList<TCell>, TRow> p_dlgRowFactory,
        Func<string, IReadOnlyList<TRow>, IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<CImportCapabilityIssue>, IReadOnlyList<CImportFieldPolicyDiagnostic>, TWorksheet> p_dlgSheetFactory)
    {
        var v_setProjectedFields = p_objSheet.Columns
            .Where(v_objColumn => v_objColumn.Disposition == EImportColumnDisposition.Mapped && v_objColumn.CanonicalField is not null)
            .Select(v_objColumn => v_objColumn.CanonicalField!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var v_arrRows = new List<TRow>
        {
            CreateRow(1, p_arrHeaders, null, null, null, null, null, false, p_dlgCellFactory, p_dlgRowFactory)
        };

        foreach (var v_objDataRow in p_objSheet.DataRows)
        {
            v_arrRows.Add(CreateRow(
                v_objDataRow.RowNumber,
                p_arrFields,
                v_objDataRow.Values,
                v_objDataRow.FormulaFields,
                v_objDataRow.Formulas,
                v_objDataRow.CellTypes,
                v_objDataRow.NumberFormats,
                v_objDataRow.IsHidden,
                p_dlgCellFactory,
                p_dlgRowFactory,
                v_setProjectedFields));
        }

        return p_dlgSheetFactory(
            p_strLegacySheetName,
            v_arrRows,
            p_objSheet.MappedColumns,
            p_objSheet.IgnoredColumns,
            p_objSheet.UnsupportedColumns,
            p_objSheet.CapabilityIssues,
            p_objSheet.PolicyDiagnostics);
    }

    private static TRow CreateRow<TRow, TCell>(
        int p_iRowNumber,
        IReadOnlyList<string> p_arrFields,
        IReadOnlyDictionary<string, object?>? p_dicValues,
        IReadOnlySet<string>? p_setFormulaFields,
        IReadOnlyDictionary<string, string?>? p_dicFormulas,
        IReadOnlyDictionary<string, string>? p_dicCellTypes,
        IReadOnlyDictionary<string, string?>? p_dicNumberFormats,
        bool p_bIsHidden,
        Func<int, string, object?, string?, string?, TCell> p_dlgCellFactory,
        Func<int, bool, IReadOnlyList<TCell>, TRow> p_dlgRowFactory,
        IReadOnlySet<string>? p_setProjectedFields = null)
    {
        var v_arrCells = new List<TCell>(p_arrFields.Count);
        for (var v_iIndex = 0; v_iIndex < p_arrFields.Count; v_iIndex++)
        {
            var v_strField = p_arrFields[v_iIndex];
            var v_bProjected = p_dicValues is null || p_setProjectedFields?.Contains(v_strField) == true;
            var v_bFormula = v_bProjected && p_setFormulaFields?.Contains(v_strField) == true;
            var v_objValue = p_dicValues is null
                ? v_strField
                : v_bProjected && p_dicValues.TryGetValue(v_strField, out var v_objMappedValue) ? v_objMappedValue : null;
            var v_strCellType = v_bProjected && p_dicCellTypes is not null && p_dicCellTypes.TryGetValue(v_strField, out var v_strMappedType)
                ? v_strMappedType
                : v_objValue is null ? "Blank" : "Text";
            var v_strFormula = v_bProjected && p_dicFormulas is not null && p_dicFormulas.TryGetValue(v_strField, out var v_strMappedFormula)
                ? v_strMappedFormula
                : v_bFormula ? "=" : null;
            var v_strNumberFormat = v_bProjected && p_dicNumberFormats is not null && p_dicNumberFormats.TryGetValue(v_strField, out var v_strMappedFormat)
                ? v_strMappedFormat
                : null;
            v_arrCells.Add(p_dlgCellFactory(
                v_iIndex + 1,
                v_bFormula ? "Formula" : v_strCellType,
                v_bFormula ? null : v_objValue,
                v_bFormula ? v_strFormula : null,
                v_strNumberFormat));
        }

        return p_dlgRowFactory(p_iRowNumber, p_bIsHidden, v_arrCells);
    }
}
