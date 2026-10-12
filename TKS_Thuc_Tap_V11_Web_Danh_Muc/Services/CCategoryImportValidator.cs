using System.Globalization;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;

public static class CCategoryImportValidator
{
    private static readonly string[] m_arrHeaders = { "Ma_LSP", "Ten_LSP", "Ghi_Chu" };
    private static readonly CompareInfo m_objCompareInfo = CultureInfo.GetCultureInfo("en-US").CompareInfo;
    private const CompareOptions m_objCompareOptions = CompareOptions.IgnoreCase;

    public static CCategoryImportPreviewResult Preview(
        CCategory_Xlsx_Worksheet p_objWorksheet,
        IReadOnlyList<CWarehouseMaster> p_arrExistingCategories)
    {
        ArgumentNullException.ThrowIfNull(p_objWorksheet);
        ArgumentNullException.ThrowIfNull(p_arrExistingCategories);
        var v_objResult = new CCategoryImportPreviewResult();
        v_objResult.Unsupported_Columns.AddRange(p_objWorksheet.UnsupportedColumns);
        v_objResult.Mapped_Columns.AddRange(p_objWorksheet.MappedColumns);
        v_objResult.Ignored_Columns.AddRange(p_objWorksheet.IgnoredColumns);
        v_objResult.Unsupported_Capabilities.AddRange(p_objWorksheet.UnsupportedCapabilities);
        v_objResult.Field_Policy_Diagnostics.AddRange(p_objWorksheet.PolicyDiagnostics);
        if (!string.Equals(p_objWorksheet.Name, "LoaiSanPham", StringComparison.Ordinal))
        {
            AddFileError(v_objResult, "Workbook phải có sheet LoaiSanPham.");
            return v_objResult;
        }
        if (!ValidateHeaders(p_objWorksheet, v_objResult))
            return v_objResult;

        var v_arrRows = p_objWorksheet.Rows
            .Where(v_objRow => v_objRow.RowNumber > 1 && !IsBlankRow(v_objRow))
            .Select(v_objRow => CreateInputRow(v_objRow, v_objResult))
            .ToArray();
        if (v_arrRows.Length == 0)
        {
            AddFileError(v_objResult, "Workbook không có dòng dữ liệu loại sản phẩm.");
            return v_objResult;
        }
        return ValidateRows(v_arrRows, p_arrExistingCategories, v_objResult);
    }

    internal static CCategoryImportPreviewResult Revalidate(
        IReadOnlyList<CCategoryImportRow> p_arrRows,
        IReadOnlyList<CWarehouseMaster> p_arrExistingCategories)
    {
        ArgumentNullException.ThrowIfNull(p_arrRows);
        ArgumentNullException.ThrowIfNull(p_arrExistingCategories);
        var v_arrRows = p_arrRows.Select(v_objRow => new CCategoryImportRow
        {
            Row_Number = v_objRow.Row_Number,
            Ma_LSP = v_objRow.Ma_LSP,
            Ten_LSP = v_objRow.Ten_LSP,
            Ghi_Chu = v_objRow.Ghi_Chu
        }).ToArray();
        return ValidateRows(v_arrRows, p_arrExistingCategories, new CCategoryImportPreviewResult());
    }

    private static CCategoryImportPreviewResult ValidateRows(
        IReadOnlyList<CCategoryImportRow> p_arrInputRows,
        IReadOnlyList<CWarehouseMaster> p_arrExistingCategories,
        CCategoryImportPreviewResult p_objResult)
    {
        var v_dicExistingByCode = p_arrExistingCategories
            .Where(v_objCategory => !string.IsNullOrWhiteSpace(v_objCategory.Code))
            .GroupBy(v_objCategory => ComparisonKey(v_objCategory.Code), StringComparer.Ordinal)
            .ToDictionary(v_objGroup => v_objGroup.Key, v_objGroup => v_objGroup.ToArray(), StringComparer.Ordinal);
        var v_dicExistingByName = p_arrExistingCategories
            .Where(v_objCategory => !string.IsNullOrWhiteSpace(v_objCategory.Name))
            .GroupBy(v_objCategory => ComparisonKey(v_objCategory.Name), StringComparer.Ordinal)
            .ToDictionary(v_objGroup => v_objGroup.Key, v_objGroup => v_objGroup.ToArray(), StringComparer.Ordinal);
        var v_dicFirstByCode = new Dictionary<string, CCategoryImportRow>(StringComparer.Ordinal);
        var v_dicFirstByName = new Dictionary<string, CCategoryImportRow>(StringComparer.Ordinal);

        foreach (var v_objInput in p_arrInputRows)
        {
            var v_objRow = CloneInputRow(v_objInput);
            p_objResult.Rows.Add(v_objRow);
            ValidateRequiredAndLength(p_objResult, v_objRow, "Ma_LSP", v_objRow.Ma_LSP, 100);
            ValidateRequiredAndLength(p_objResult, v_objRow, "Ten_LSP", v_objRow.Ten_LSP, 200);
            ValidateRequiredAndLength(p_objResult, v_objRow, "Ghi_Chu", v_objRow.Ghi_Chu, 1000, required: false);
            if (!v_objRow.Is_Valid)
                continue;

            var v_strCodeKey = ComparisonKey(v_objRow.Ma_LSP);
            var v_strNameKey = ComparisonKey(v_objRow.Ten_LSP);
            if (v_dicExistingByCode.TryGetValue(v_strCodeKey, out var v_arrExistingByCode))
            {
                if (v_arrExistingByCode.All(v_objExisting =>
                    ComparisonKey(v_objExisting.Name) == v_strNameKey &&
                    ComparisonKey(v_objExisting.Ghi_Chu) == ComparisonKey(v_objRow.Ghi_Chu)))
                    v_objRow.Is_Skipped = true;
                else
                    AddRowError(p_objResult, v_objRow, "Ma_LSP", $"Mã loại sản phẩm '{v_objRow.Ma_LSP}' trùng khóa nhưng dữ liệu khác trong hệ thống.");
                continue;
            }

            if (v_dicExistingByName.ContainsKey(v_strNameKey))
            {
                AddRowError(p_objResult, v_objRow, "Ten_LSP", $"Tên loại sản phẩm '{v_objRow.Ten_LSP}' trùng khóa nhưng dữ liệu khác trong hệ thống.");
                continue;
            }

            if (v_dicFirstByCode.TryGetValue(v_strCodeKey, out var v_objPreviousByCode))
            {
                if (ComparisonKey(v_objPreviousByCode.Ten_LSP) == v_strNameKey &&
                    ComparisonKey(v_objPreviousByCode.Ghi_Chu) == ComparisonKey(v_objRow.Ghi_Chu))
                    v_objRow.Is_Skipped = true;
                else
                    AddRowError(p_objResult, v_objRow, "Ma_LSP", $"Mã loại sản phẩm '{v_objRow.Ma_LSP}' trùng khóa nhưng dữ liệu khác trong file.");
                continue;
            }

            if (v_dicFirstByName.ContainsKey(v_strNameKey))
            {
                AddRowError(p_objResult, v_objRow, "Ten_LSP", $"Tên loại sản phẩm '{v_objRow.Ten_LSP}' trùng khóa nhưng dữ liệu khác trong file.");
                continue;
            }

            v_dicFirstByCode.Add(v_strCodeKey, v_objRow);
            v_dicFirstByName.Add(v_strNameKey, v_objRow);
        }
        return p_objResult;
    }

    private static bool ValidateHeaders(CCategory_Xlsx_Worksheet p_objWorksheet, CCategoryImportPreviewResult p_objResult)
    {
        var v_objHeaderRow = p_objWorksheet.Rows.FirstOrDefault(v_objRow => v_objRow.RowNumber == 1);
        if (v_objHeaderRow is null)
        {
            foreach (var v_strHeader in m_arrHeaders)
                AddFileError(p_objResult, $"Thiếu tiêu đề cột {v_strHeader}.", v_strHeader);
            return false;
        }

        var v_bIsValid = true;
        var v_arrActualHeaders = new string[m_arrHeaders.Length];
        var v_setSeen = new HashSet<string>(StringComparer.Ordinal);
        for (var v_iColumn = 1; v_iColumn <= v_objHeaderRow.Cells.Count; v_iColumn++)
        {
            var v_objCell = FindCell(v_objHeaderRow, v_iColumn);
            var v_strExpected = v_iColumn <= m_arrHeaders.Length ? m_arrHeaders[v_iColumn - 1] : "";
            var v_strHeader = ReadCellValue(v_objCell);
            if (IsFormula(v_objCell))
            {
                AddFileError(p_objResult, $"Dòng 1, cột {v_strExpected}: Không hỗ trợ công thức Excel.", v_strExpected);
                v_bIsValid = false;
                continue;
            }
            if (v_strHeader.Length == 0)
            {
                AddFileError(p_objResult, "Tiêu đề cột không được để trống.", v_strExpected);
                v_bIsValid = false;
                continue;
            }
            if (!v_setSeen.Add(v_strHeader))
            {
                AddFileError(p_objResult, $"Tiêu đề {v_strHeader} bị lặp.", v_strHeader);
                v_bIsValid = false;
            }
            if (!m_arrHeaders.Contains(v_strHeader, StringComparer.Ordinal))
            {
                AddFileError(p_objResult, $"Không nhận diện tiêu đề {v_strHeader}; hãy dùng template chuẩn.", v_strHeader);
                v_bIsValid = false;
            }
            if (v_iColumn <= m_arrHeaders.Length)
                v_arrActualHeaders[v_iColumn - 1] = v_strHeader;
        }
        foreach (var v_strHeader in m_arrHeaders)
        {
            if (!v_setSeen.Contains(v_strHeader))
            {
                AddFileError(p_objResult, $"Thiếu tiêu đề cột {v_strHeader}.", v_strHeader);
                v_bIsValid = false;
            }
        }
        if (v_bIsValid && !v_arrActualHeaders.SequenceEqual(m_arrHeaders, StringComparer.Ordinal))
        {
            var v_iMismatch = Enumerable.Range(0, m_arrHeaders.Length)
                .First(v_iIndex => !string.Equals(v_arrActualHeaders[v_iIndex], m_arrHeaders[v_iIndex], StringComparison.Ordinal));
            AddFileError(p_objResult, "Thứ tự cột không đúng template Loại sản phẩm chuẩn.", m_arrHeaders[v_iMismatch]);
            v_bIsValid = false;
        }
        return v_bIsValid;
    }

    private static CCategoryImportRow CreateInputRow(CCategory_Xlsx_Row p_objSource, CCategoryImportPreviewResult p_objResult)
    {
        var v_objRow = new CCategoryImportRow { Row_Number = p_objSource.RowNumber };
        var v_objCode = FindCell(p_objSource, 1);
        var v_objName = FindCell(p_objSource, 2);
        var v_objNote = FindCell(p_objSource, 3);
        if (IsFormula(v_objCode)) AddRowError(p_objResult, v_objRow, "Ma_LSP", $"Dòng {p_objSource.RowNumber}, cột Ma_LSP: Không hỗ trợ công thức Excel.");
        if (IsFormula(v_objName)) AddRowError(p_objResult, v_objRow, "Ten_LSP", $"Dòng {p_objSource.RowNumber}, cột Ten_LSP: Không hỗ trợ công thức Excel.");
        if (IsFormula(v_objNote)) AddRowError(p_objResult, v_objRow, "Ghi_Chu", $"Dòng {p_objSource.RowNumber}, cột Ghi_Chu: Không hỗ trợ công thức Excel.");
        v_objRow.Ma_LSP = ReadCellValue(v_objCode);
        v_objRow.Ten_LSP = ReadCellValue(v_objName);
        v_objRow.Ghi_Chu = ReadCellValue(v_objNote);
        return v_objRow;
    }

    private static CCategoryImportRow CloneInputRow(CCategoryImportRow p_objSource)
    {
        var v_objRow = new CCategoryImportRow
        {
            Row_Number = p_objSource.Row_Number,
            Ma_LSP = p_objSource.Ma_LSP.Trim(),
            Ten_LSP = p_objSource.Ten_LSP.Trim(),
            Ghi_Chu = p_objSource.Ghi_Chu.Trim()
        };
        foreach (var v_objError in p_objSource.Errors)
            v_objRow.Errors.Add(v_objError);
        return v_objRow;
    }

    private static void ValidateRequiredAndLength(
        CCategoryImportPreviewResult p_objResult,
        CCategoryImportRow p_objRow,
        string p_strColumn,
        string p_strValue,
        int p_iMaxLength,
        bool required = true)
    {
        if (p_objRow.Errors.Any(v_objError => v_objError.Column_Name == p_strColumn &&
            v_objError.Message.Contains("Không hỗ trợ công thức Excel.", StringComparison.Ordinal)))
            return;
        if (required && p_strValue.Length == 0)
            AddRowError(p_objResult, p_objRow, p_strColumn, $"Dòng {p_objRow.Row_Number}, cột {p_strColumn}: Không được để trống.");
        else if (p_strValue.Length > p_iMaxLength)
            AddRowError(p_objResult, p_objRow, p_strColumn, $"Dòng {p_objRow.Row_Number}, cột {p_strColumn}: Tối đa {p_iMaxLength} ký tự.");
    }

    private static bool IsBlankRow(CCategory_Xlsx_Row p_objRow) => p_objRow.Cells.All(v_objCell =>
        !IsFormula(v_objCell) &&
        (v_objCell.Value is null || string.IsNullOrWhiteSpace(Convert.ToString(v_objCell.Value, CultureInfo.InvariantCulture))));
    private static bool IsFormula(CCategory_Xlsx_Cell? p_objCell) => p_objCell?.Formula is not null || p_objCell?.CellType == "Formula";
    private static CCategory_Xlsx_Cell? FindCell(CCategory_Xlsx_Row p_objRow, int p_iColumn) => p_objRow.Cells.FirstOrDefault(v_objCell => v_objCell.ColumnNumber == p_iColumn);
    private static string ReadCellValue(CCategory_Xlsx_Cell? p_objCell) => Convert.ToString(p_objCell?.Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
    private static string ComparisonKey(string p_strValue) =>
        CImportBusinessValueComparer.Normalize(p_strValue).ToUpperInvariant();

    private static void AddRowError(CCategoryImportPreviewResult p_objResult, CCategoryImportRow p_objRow, string p_strColumn, string p_strMessage)
    {
        var v_objError = new CCategoryImportError(p_objRow.Row_Number, p_strColumn, p_strMessage);
        p_objRow.Errors.Add(v_objError);
        p_objResult.Errors.Add(v_objError);
    }
    private static void AddFileError(CCategoryImportPreviewResult p_objResult, string p_strMessage, string p_strColumn = "") =>
        p_objResult.Errors.Add(new CCategoryImportError(1, p_strColumn, p_strMessage));
}
