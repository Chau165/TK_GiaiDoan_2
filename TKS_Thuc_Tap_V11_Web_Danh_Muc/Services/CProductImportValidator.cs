using System.Globalization;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;

public static class CProductImportValidator
{
    private static readonly CImportValueMapper m_objValueMapper = new(new CImportProfileProvider());
    private static readonly string[] m_arrHeaders =
    {
        "Ma_San_Pham", "Ten_San_Pham", "Ma_LSP", "Ten_Don_Vi_Tinh", "Ghi_Chu"
    };


    public static CProductImportPreviewResult Preview(
        CProduct_Xlsx_Worksheet p_objWorksheet,
        IReadOnlyList<CWarehouseMaster> p_arrCategories,
        IReadOnlyList<CWarehouseMaster> p_arrUnits,
        IEnumerable<string> p_arrExistingProductCodes)
    {
        ArgumentNullException.ThrowIfNull(p_arrExistingProductCodes);
        return Preview(p_objWorksheet, p_arrCategories, p_arrUnits, p_arrExistingProductCodes
            .Where(v_strCode => !string.IsNullOrWhiteSpace(v_strCode))
            .Select(v_strCode => new CWarehouseMaster { Code = v_strCode })
            .ToArray());
    }

    public static CProductImportPreviewResult Preview(
        CProduct_Xlsx_Worksheet p_objWorksheet,
        IReadOnlyList<CWarehouseMaster> p_arrCategories,
        IReadOnlyList<CWarehouseMaster> p_arrUnits,
        IReadOnlyList<CWarehouseMaster> p_arrExistingProducts)
    {
        ArgumentNullException.ThrowIfNull(p_objWorksheet);
        ArgumentNullException.ThrowIfNull(p_arrCategories);
        ArgumentNullException.ThrowIfNull(p_arrUnits);
        ArgumentNullException.ThrowIfNull(p_arrExistingProducts);

        var v_objResult = new CProductImportPreviewResult();
        v_objResult.Unsupported_Columns.AddRange(p_objWorksheet.UnsupportedColumns);
        v_objResult.Mapped_Columns.AddRange(p_objWorksheet.MappedColumns);
        v_objResult.Ignored_Columns.AddRange(p_objWorksheet.IgnoredColumns);
        v_objResult.Unsupported_Capabilities.AddRange(p_objWorksheet.UnsupportedCapabilities);
        v_objResult.Field_Policy_Diagnostics.AddRange(p_objWorksheet.PolicyDiagnostics);
        if (!ValidateHeaders(p_objWorksheet, v_objResult))
            return v_objResult;

        var v_arrRows = p_objWorksheet.Rows
            .Where(v_objRow => v_objRow.RowNumber > 1 && !IsBlankRow(v_objRow))
            .Select(v_objRow => CreateInputRow(v_objRow, v_objResult))
            .ToArray();

        if (v_arrRows.Length == 0)
        {
            AddFileError(v_objResult, "Workbook không có dòng dữ liệu sản phẩm.");
            return v_objResult;
        }

        return ValidateRows(v_arrRows, p_arrCategories, p_arrUnits, p_arrExistingProducts, v_objResult);
    }

    internal static CProductImportPreviewResult Revalidate(
        IReadOnlyList<CProductImportRow> p_arrRows,
        IReadOnlyList<CWarehouseMaster> p_arrCategories,
        IReadOnlyList<CWarehouseMaster> p_arrUnits,
        IEnumerable<string> p_arrExistingProductCodes)
    {
        ArgumentNullException.ThrowIfNull(p_arrExistingProductCodes);
        return Revalidate(p_arrRows, p_arrCategories, p_arrUnits, p_arrExistingProductCodes
            .Where(v_strCode => !string.IsNullOrWhiteSpace(v_strCode))
            .Select(v_strCode => new CWarehouseMaster { Code = v_strCode })
            .ToArray());
    }

    internal static CProductImportPreviewResult Revalidate(
        IReadOnlyList<CProductImportRow> p_arrRows,
        IReadOnlyList<CWarehouseMaster> p_arrCategories,
        IReadOnlyList<CWarehouseMaster> p_arrUnits,
        IReadOnlyList<CWarehouseMaster> p_arrExistingProducts)
    {
        ArgumentNullException.ThrowIfNull(p_arrRows);
        ArgumentNullException.ThrowIfNull(p_arrCategories);
        ArgumentNullException.ThrowIfNull(p_arrUnits);
        ArgumentNullException.ThrowIfNull(p_arrExistingProducts);
        var v_arrRows = p_arrRows.Select(v_objRow => new CProductImportRow
        {
            Row_Number = v_objRow.Row_Number,
            Ma_San_Pham = v_objRow.Ma_San_Pham,
            Ten_San_Pham = v_objRow.Ten_San_Pham,
            Ma_LSP = v_objRow.Ma_LSP,
            Ten_Don_Vi_Tinh = v_objRow.Ten_Don_Vi_Tinh,
            Ghi_Chu = v_objRow.Ghi_Chu,
            Source_Ten_Don_Vi_Tinh = v_objRow.Source_Ten_Don_Vi_Tinh
        }).ToArray();

        return ValidateRows(v_arrRows, p_arrCategories, p_arrUnits, p_arrExistingProducts, new CProductImportPreviewResult());
    }

    private static CProductImportPreviewResult ValidateRows(
        IReadOnlyList<CProductImportRow> p_arrInputRows,
        IReadOnlyList<CWarehouseMaster> p_arrCategories,
        IReadOnlyList<CWarehouseMaster> p_arrUnits,
        IReadOnlyList<CWarehouseMaster> p_arrExistingProducts,
        CProductImportPreviewResult p_objResult)
    {
        var v_dicCategoryByCode = p_arrCategories
            .Where(v_objCategory => !string.IsNullOrWhiteSpace(v_objCategory.Code))
            .GroupBy(v_objCategory => CodeComparisonKey(v_objCategory.Code))
            .ToDictionary(v_objGroup => v_objGroup.Key, v_objGroup => v_objGroup.ToArray());
        var v_dicUnitByName = p_arrUnits
            .Where(v_objUnit => !string.IsNullOrWhiteSpace(v_objUnit.Name))
            .GroupBy(v_objUnit => UnitComparisonKey(v_objUnit.Name))
            .ToDictionary(v_objGroup => v_objGroup.Key, v_objGroup => v_objGroup.ToArray());
        var v_dicExistingByCode = p_arrExistingProducts
            .Where(v_objProduct => !string.IsNullOrWhiteSpace(v_objProduct.Code))
            .GroupBy(v_objProduct => CodeComparisonKey(v_objProduct.Code), StringComparer.Ordinal)
            .ToDictionary(v_objGroup => v_objGroup.Key, v_objGroup => v_objGroup.ToArray(), StringComparer.Ordinal);
        var v_dicFirstByCode = new Dictionary<string, CProductImportRow>(StringComparer.Ordinal);

        foreach (var v_objInput in p_arrInputRows)
        {
            var v_objRow = CloneInputRow(v_objInput);
            p_objResult.Rows.Add(v_objRow);

            ValidateRequiredAndLength(p_objResult, v_objRow, "Ma_San_Pham", v_objRow.Ma_San_Pham, 100);
            ValidateRequiredAndLength(p_objResult, v_objRow, "Ten_San_Pham", v_objRow.Ten_San_Pham, 255);
            ValidateRequiredAndLength(p_objResult, v_objRow, "Ma_LSP", v_objRow.Ma_LSP, 100);
            ValidateRequiredAndLength(p_objResult, v_objRow, "Ten_Don_Vi_Tinh", v_objRow.Ten_Don_Vi_Tinh, 200);
            ValidateRequiredAndLength(p_objResult, v_objRow, "Ghi_Chu", v_objRow.Ghi_Chu, 1000, required: false);

            if (v_objRow.Ma_LSP.Length > 0 && v_dicCategoryByCode.TryGetValue(CodeComparisonKey(v_objRow.Ma_LSP), out var v_arrCategories))
            {
                if (v_arrCategories.Length == 1)
                    v_objRow.Loai_San_Pham_ID = v_arrCategories[0].Auto_ID;
                else
                    AddRowError(p_objResult, v_objRow, "Ma_LSP", $"Mã loại sản phẩm '{v_objRow.Ma_LSP}' không duy nhất.");
            }
            else if (v_objRow.Ma_LSP.Length > 0)
            {
                AddRowError(p_objResult, v_objRow, "Ma_LSP", $"Không tìm thấy mã loại sản phẩm '{v_objRow.Ma_LSP}'.");
            }

            var v_strSourceUnitName = v_objRow.Source_Ten_Don_Vi_Tinh.Length > 0
                ? v_objRow.Source_Ten_Don_Vi_Tinh
                : v_objRow.Ten_Don_Vi_Tinh;
            var v_objUnitMapping = m_objValueMapper.Map("Product", "UnitName", v_strSourceUnitName);
            v_objRow.Ten_Don_Vi_Tinh = v_objUnitMapping.MappedValue;
            if (v_objRow.Ten_Don_Vi_Tinh.Length > 0 && v_dicUnitByName.TryGetValue(UnitComparisonKey(v_objRow.Ten_Don_Vi_Tinh), out var v_arrUnits))
            {
                if (v_arrUnits.Length == 1)
                {
                    v_objRow.Don_Vi_Tinh_ID = v_arrUnits[0].Auto_ID;
                    if (v_objUnitMapping.WasMapped)
                    {
                        p_objResult.Value_Mappings.Add(new CImportValueMappingDiagnostic(
                            v_objRow.Row_Number, "SanPham", "UnitName", v_objUnitMapping.SourceValue,
                            v_objUnitMapping.MappedValue, "RESOLVED", v_arrUnits[0].Auto_ID));
                    }
                }
                else
                    AddRowError(p_objResult, v_objRow, "Ten_Don_Vi_Tinh", $"Tên đơn vị tính '{v_objRow.Ten_Don_Vi_Tinh}' không duy nhất sau chuẩn hóa.");
            }
            else if (v_objRow.Ten_Don_Vi_Tinh.Length > 0)
            {
                AddRowError(p_objResult, v_objRow, "Ten_Don_Vi_Tinh", $"Không tìm thấy đơn vị tính '{v_objRow.Ten_Don_Vi_Tinh}'.");
                if (v_objUnitMapping.WasMapped)
                {
                    p_objResult.Value_Mappings.Add(new CImportValueMappingDiagnostic(
                        v_objRow.Row_Number, "SanPham", "UnitName", v_objUnitMapping.SourceValue,
                        v_objUnitMapping.MappedValue, "REFERENCE_NOT_FOUND"));
                }
            }

            if (!v_objRow.Is_Valid || v_objRow.Ma_San_Pham.Length == 0)
                continue;

            var v_strCodeKey = CodeComparisonKey(v_objRow.Ma_San_Pham);
            if (v_dicExistingByCode.TryGetValue(v_strCodeKey, out var v_arrExistingByCode))
            {
                if (v_arrExistingByCode.All(v_objExisting =>
                    CodeComparisonKey(v_objExisting.Name) == CodeComparisonKey(v_objRow.Ten_San_Pham) &&
                    v_objExisting.Related_ID == v_objRow.Loai_San_Pham_ID &&
                    v_objExisting.Related_ID_2 == v_objRow.Don_Vi_Tinh_ID &&
                    CodeComparisonKey(v_objExisting.Ghi_Chu) == CodeComparisonKey(v_objRow.Ghi_Chu)))
                    v_objRow.Is_Skipped = true;
                else
                    AddRowError(p_objResult, v_objRow, "Ma_San_Pham", $"Mã sản phẩm '{v_objRow.Ma_San_Pham}' trùng khóa nhưng dữ liệu khác trong hệ thống.");
                continue;
            }

            if (v_dicFirstByCode.TryGetValue(v_strCodeKey, out var v_objPreviousByCode))
            {
                if (CodeComparisonKey(v_objPreviousByCode.Ten_San_Pham) == CodeComparisonKey(v_objRow.Ten_San_Pham) &&
                    CodeComparisonKey(v_objPreviousByCode.Ma_LSP) == CodeComparisonKey(v_objRow.Ma_LSP) &&
                    UnitComparisonKey(v_objPreviousByCode.Ten_Don_Vi_Tinh) == UnitComparisonKey(v_objRow.Ten_Don_Vi_Tinh) &&
                    CodeComparisonKey(v_objPreviousByCode.Ghi_Chu) == CodeComparisonKey(v_objRow.Ghi_Chu))
                    v_objRow.Is_Skipped = true;
                else
                    AddRowError(p_objResult, v_objRow, "Ma_San_Pham", $"Mã sản phẩm '{v_objRow.Ma_San_Pham}' trùng khóa nhưng dữ liệu khác trong file.");
                continue;
            }

            v_dicFirstByCode.Add(v_strCodeKey, v_objRow);
        }

        return p_objResult;
    }

    private static bool ValidateHeaders(CProduct_Xlsx_Worksheet p_objWorksheet, CProductImportPreviewResult p_objResult)
    {
        var v_objHeaderRow = p_objWorksheet.Rows.FirstOrDefault(v_objRow => v_objRow.RowNumber == 1);
        if (v_objHeaderRow is null)
        {
            AddFileError(p_objResult, "Không tìm thấy dòng tiêu đề chuẩn của sheet SanPham.");
            foreach (var v_strHeader in m_arrHeaders)
                AddFileError(p_objResult, $"Thiếu tiêu đề cột {v_strHeader}.", v_strHeader);
            return false;
        }

        var v_arrValues = new string[m_arrHeaders.Length];
        var v_setSeen = new HashSet<string>(StringComparer.Ordinal);
        var v_bIsValid = true;
        for (var v_iColumn = 1; v_iColumn <= m_arrHeaders.Length; v_iColumn++)
        {
            var v_objCell = FindCell(v_objHeaderRow, v_iColumn);
            var v_strValue = ReadCellValue(v_objCell);
            v_arrValues[v_iColumn - 1] = v_strValue;

            if (v_objCell?.Formula is not null || v_objCell?.CellType == "Formula")
            {
                AddFileError(p_objResult, $"Dòng 1, cột {m_arrHeaders[v_iColumn - 1]}: Không hỗ trợ công thức Excel.", m_arrHeaders[v_iColumn - 1]);
                v_bIsValid = false;
                continue;
            }

            if (v_strValue.Length == 0)
            {
                AddFileError(p_objResult, "Tiêu đề cột không được để trống.", m_arrHeaders[v_iColumn - 1]);
                v_bIsValid = false;
                continue;
            }

            if (!v_setSeen.Add(v_strValue))
            {
                AddFileError(p_objResult, $"Tiêu đề {v_strValue} bị lặp.", v_strValue);
                v_bIsValid = false;
            }

            if (!m_arrHeaders.Contains(v_strValue, StringComparer.Ordinal))
            {
                AddFileError(p_objResult, $"Không nhận diện tiêu đề {v_strValue}; hãy dùng template chuẩn.", v_strValue);
                v_bIsValid = false;
            }
        }

        foreach (var v_strHeader in m_arrHeaders)
        {
            if (!v_setSeen.Contains(v_strHeader))
            {
                AddFileError(p_objResult, $"Thiếu tiêu đề cột {v_strHeader}.", v_strHeader);
                v_bIsValid = false;
            }
        }

        if (v_bIsValid && !v_arrValues.SequenceEqual(m_arrHeaders, StringComparer.Ordinal))
        {
            AddFileError(p_objResult, "Thứ tự cột không đúng template Product chuẩn.");
            v_bIsValid = false;
        }

        return v_bIsValid;
    }

    private static CProductImportRow CloneInputRow(CProductImportRow p_objSource)
    {
        var v_objResult = new CProductImportRow
        {
            Row_Number = p_objSource.Row_Number,
            Ma_San_Pham = p_objSource.Ma_San_Pham.Trim(),
            Ten_San_Pham = p_objSource.Ten_San_Pham.Trim(),
            Ma_LSP = p_objSource.Ma_LSP.Trim(),
            Ten_Don_Vi_Tinh = p_objSource.Ten_Don_Vi_Tinh.Trim(),
            Source_Ten_Don_Vi_Tinh = p_objSource.Source_Ten_Don_Vi_Tinh,
            Ghi_Chu = p_objSource.Ghi_Chu.Trim()
        };

        foreach (var v_objError in p_objSource.Errors)
            v_objResult.Errors.Add(v_objError);

        return v_objResult;
    }

    private static void ValidateRequiredAndLength(
        CProductImportPreviewResult p_objResult,
        CProductImportRow p_objRow,
        string p_strColumn,
        string p_strValue,
        int p_iMaxLength,
        bool required = true)
    {
        if (p_objRow.Errors.Any(v_objError => v_objError.Column_Name == p_strColumn && v_objError.Message.Contains("Không hỗ trợ công thức Excel.", StringComparison.Ordinal)))
            return;

        if (required && p_strValue.Length == 0)
            AddRowError(p_objResult, p_objRow, p_strColumn, $"Dòng {p_objRow.Row_Number}, cột {p_strColumn}: Không được để trống.");
        else if (p_strValue.Length > p_iMaxLength)
            AddRowError(p_objResult, p_objRow, p_strColumn, $"Dòng {p_objRow.Row_Number}, cột {p_strColumn}: Tối đa {p_iMaxLength} ký tự.");
    }

    private static CProductImportRow CreateInputRow(CProduct_Xlsx_Row p_objSource, CProductImportPreviewResult p_objResult)
    {
        var v_objRow = new CProductImportRow { Row_Number = p_objSource.RowNumber };
        var v_arrValues = new string[m_arrHeaders.Length];
        for (var v_iColumn = 1; v_iColumn <= m_arrHeaders.Length; v_iColumn++)
        {
            var v_objCell = FindCell(p_objSource, v_iColumn);
            var v_strColumn = m_arrHeaders[v_iColumn - 1];
            if (v_objCell?.Formula is not null || v_objCell?.CellType == "Formula")
            {
                AddRowError(p_objResult, v_objRow, v_strColumn, $"Dòng {p_objSource.RowNumber}, cột {v_strColumn}: Không hỗ trợ công thức Excel.");
                continue;
            }

            v_arrValues[v_iColumn - 1] = ReadCellValue(v_objCell);
        }

        v_objRow.Ma_San_Pham = v_arrValues[0] ?? "";
        v_objRow.Ten_San_Pham = v_arrValues[1] ?? "";
        v_objRow.Ma_LSP = v_arrValues[2] ?? "";
        v_objRow.Source_Ten_Don_Vi_Tinh = v_arrValues[3] ?? "";
        v_objRow.Ten_Don_Vi_Tinh = m_objValueMapper.Map("Product", "UnitName", v_objRow.Source_Ten_Don_Vi_Tinh).MappedValue;
        v_objRow.Ghi_Chu = v_arrValues[4] ?? "";
        return v_objRow;
    }

    private static bool IsBlankRow(CProduct_Xlsx_Row p_objRow)
    {
        return p_objRow.Cells.All(v_objCell =>
            v_objCell.Formula is null &&
            (v_objCell.Value is null || string.IsNullOrWhiteSpace(Convert.ToString(v_objCell.Value, CultureInfo.InvariantCulture))));
    }

    private static CProduct_Xlsx_Cell? FindCell(CProduct_Xlsx_Row p_objRow, int p_iColumn)
    {
        return p_objRow.Cells.FirstOrDefault(v_objCell => v_objCell.ColumnNumber == p_iColumn);
    }

    private static string ReadCellValue(CProduct_Xlsx_Cell? p_objCell)
    {
        return Convert.ToString(p_objCell?.Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
    }

    private static string CodeComparisonKey(string p_strValue) =>
        CImportBusinessValueComparer.Normalize(p_strValue).ToUpperInvariant();

    private static string UnitComparisonKey(string p_strValue) =>
        CImportBusinessValueComparer.Normalize(p_strValue).ToUpperInvariant();

    private static void AddRowError(CProductImportPreviewResult? p_objResult, CProductImportRow p_objRow, string p_strColumn, string p_strMessage)
    {
        var v_objError = new CProductImportError(p_objRow.Row_Number, p_strColumn, p_strMessage);
        p_objRow.Errors.Add(v_objError);
        p_objResult?.Errors.Add(v_objError);
    }

    private static void AddFileError(CProductImportPreviewResult p_objResult, string p_strMessage, string p_strColumn = "")
    {
        p_objResult.Errors.Add(new CProductImportError(1, p_strColumn, p_strMessage));
    }
}
