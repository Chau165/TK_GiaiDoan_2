using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class SupplierExcelImportValidationTests
{
    private static readonly string[] m_arrHeaders = { "Ma_NCC", "Ten_NCC", "Ghi_Chu" };

    [Fact]
    public void Preview_trims_supplier_values_and_preserves_leading_zero_code()
    {
        var v_objResult = Preview(Worksheet(m_arrHeaders, new object?[] { " 00123 ", " Nhà cung cấp ", " Ghi chú " }));

        var v_objRow = Assert.Single(v_objResult.Rows);
        Assert.True(v_objResult.Can_Confirm);
        Assert.Equal("00123", v_objRow.Ma_NCC);
        Assert.Equal("Nhà cung cấp", v_objRow.Ten_NCC);
        Assert.Equal("Ghi chú", v_objRow.Ghi_Chu);
    }

    [Fact]
    public void Preview_rejects_existing_and_in_file_code_or_name_duplicates()
    {
        var v_objInFile = Preview(Worksheet(m_arrHeaders,
            new object?[] { "NCC001", "Nhà cung cấp A", "" },
            new object?[] { "ncc001", "Nhà cung cấp B", "" },
            new object?[] { "NCC003", "nhà cung cấp a", "" }));
        var v_objExistingCode = Preview(Worksheet(m_arrHeaders,
            new object?[] { "ncc-db", "Tên mới", "" }),
            new[] { new CDM_NCC { Ma_NCC = "NCC-DB", Ten_NCC = "Tên cũ" } });
        var v_objExistingName = Preview(Worksheet(m_arrHeaders,
            new object?[] { "NCC-NEW", "tÊN cŨ", "" }),
            new[] { new CDM_NCC { Ma_NCC = "NCC-DB", Ten_NCC = "Tên cũ" } });

        Assert.Contains(v_objInFile.Errors, v_objError => v_objError.Column_Name == "Ma_NCC");
        Assert.Contains(v_objInFile.Errors, v_objError => v_objError.Column_Name == "Ten_NCC");
        Assert.False(v_objExistingCode.Can_Confirm);
        Assert.False(v_objExistingName.Can_Confirm);
    }

    [Fact]
    public void Preview_skips_exact_duplicates_and_rejects_conflicting_code_reuse()
    {
        var v_objExact = Preview(Worksheet(m_arrHeaders,
            new object?[] { "NCC001", "Nhà cung cấp A", "Ghi chú" },
            new object?[] { "ncc001", " nhà cung cấp a ", " Ghi chú " }));
        var v_objConflict = Preview(Worksheet(m_arrHeaders,
            new object?[] { "NCC001", "Nhà cung cấp A", "" },
            new object?[] { "ncc001", "Nhà cung cấp B", "" }));
        var v_objExistingExact = Preview(
            Worksheet(m_arrHeaders, new object?[] { "NCC001", "Nhà cung cấp A", "Ghi chú" }),
            new[] { new CDM_NCC { Ma_NCC = "ncc001", Ten_NCC = " nhà cung cấp a ", Ghi_Chu = " Ghi chú " } });

        Assert.True(v_objExact.Can_Confirm);
        Assert.Equal(1, v_objExact.Skipped_Rows);
        Assert.False(v_objConflict.Can_Confirm);
        Assert.Contains(v_objConflict.Errors, v_objError => v_objError.Message.Contains("trùng khóa nhưng dữ liệu khác", StringComparison.OrdinalIgnoreCase));
        Assert.False(v_objExistingExact.Can_Confirm);
        Assert.Equal(1, v_objExistingExact.Skipped_Rows);
        Assert.Empty(v_objExistingExact.Errors);
    }

    [Fact]
    public void Preview_uses_case_insensitive_accent_sensitive_identity()
    {
        var v_objCaseDuplicate = Preview(Worksheet(m_arrHeaders,
            new object?[] { "NCCÉ", "Cà phê", "" },
            new object?[] { "nccé", "Cà phê rang", "" }));
        var v_objAccentDistinct = Preview(Worksheet(m_arrHeaders,
            new object?[] { "NCCÉ", "Cafe", "" },
            new object?[] { "NCCE", "Café", "" }));

        Assert.False(v_objCaseDuplicate.Can_Confirm);
        Assert.True(v_objAccentDistinct.Can_Confirm);
    }

    [Theory]
    [InlineData("", "Nhà cung cấp", "Ma_NCC")]
    [InlineData("NCC001", "", "Ten_NCC")]
    public void Preview_requires_code_and_name(string p_strCode, string p_strName, string p_strColumn)
    {
        var v_objResult = Preview(Worksheet(m_arrHeaders, new object?[] { p_strCode, p_strName, "" }));

        Assert.Contains(v_objResult.Errors, v_objError => v_objError.Column_Name == p_strColumn);
    }

    [Theory]
    [InlineData(0, 101, "Ma_NCC")]
    [InlineData(1, 256, "Ten_NCC")]
    [InlineData(2, 1001, "Ghi_Chu")]
    public void Preview_rejects_values_over_database_limits(int p_iColumn, int p_iLength, string p_strColumn)
    {
        var v_arrValues = new object?[] { "NCC001", "Nhà cung cấp", "" };
        v_arrValues[p_iColumn] = new string('X', p_iLength);

        var v_objResult = Preview(Worksheet(m_arrHeaders, v_arrValues));

        Assert.Contains(v_objResult.Errors, v_objError => v_objError.Column_Name == p_strColumn);
    }

    [Fact]
    public void Preview_rejects_wrong_sheet_headers_and_formula_cells()
    {
        var v_objWrongSheet = Preview(WorksheetOnSheet(m_arrHeaders, "Suppliers", new object?[] { "NCC001", "Tên", "" }));
        var v_objBadHeaders = Preview(Worksheet(new[] { "Ten_NCC", "Ma_NCC", "Unknown" }, new object?[] { "Tên", "NCC001", "" }));
        var v_objFormula = Preview(WorksheetWithRows("NhaCungCap", m_arrHeaders,
            DataRow(2, ("NCC001", "Text", null), (null, "Formula", "1+1"), ("", "Blank", null))));

        Assert.False(v_objWrongSheet.Can_Confirm);
        Assert.False(v_objBadHeaders.Can_Confirm);
        Assert.False(v_objFormula.Can_Confirm);
        Assert.Contains(v_objFormula.Errors, v_objError =>
            v_objError.Message.Contains("cột Ten_NCC: Không hỗ trợ công thức Excel.", StringComparison.Ordinal));
    }

    private static CSupplierImportPreviewResult Preview(
        CSupplier_Xlsx_Worksheet p_objWorksheet,
        IReadOnlyList<CDM_NCC>? p_arrExisting = null) =>
        CSupplierImportValidator.Preview(p_objWorksheet, p_arrExisting ?? Array.Empty<CDM_NCC>());

    private static CSupplier_Xlsx_Worksheet Worksheet(string?[] p_arrHeaders, params object?[][] p_arrDataRows) =>
        WorksheetOnSheet(p_arrHeaders, "NhaCungCap", p_arrDataRows);

    private static CSupplier_Xlsx_Worksheet WorksheetOnSheet(
        string?[] p_arrHeaders,
        string p_strSheetName,
        params object?[][] p_arrDataRows)
    {
        var v_arrRows = new List<CSupplier_Xlsx_Row>
        {
            DataRow(1, p_arrHeaders.Select(v_strHeader => ((object?)v_strHeader, "Text", (string?)null)).ToArray())
        };
        for (var v_iIndex = 0; v_iIndex < p_arrDataRows.Length; v_iIndex++)
            v_arrRows.Add(DataRow(v_iIndex + 2, p_arrDataRows[v_iIndex]
                .Select(v_objValue => (v_objValue, v_objValue is null ? "Blank" : "Text", (string?)null)).ToArray()));
        return new CSupplier_Xlsx_Worksheet(p_strSheetName, v_arrRows);
    }

    private static CSupplier_Xlsx_Worksheet WorksheetWithRows(
        string p_strSheetName,
        string?[] p_arrHeaders,
        params CSupplier_Xlsx_Row[] p_arrDataRows)
    {
        var v_arrRows = new List<CSupplier_Xlsx_Row>
        {
            DataRow(1, p_arrHeaders.Select(v_strHeader => ((object?)v_strHeader, "Text", (string?)null)).ToArray())
        };
        v_arrRows.AddRange(p_arrDataRows);
        return new CSupplier_Xlsx_Worksheet(p_strSheetName, v_arrRows);
    }

    private static CSupplier_Xlsx_Row DataRow(int p_iRow, params (object? Value, string Type, string? Formula)[] p_arrValues) =>
        new(p_iRow, false, p_arrValues.Select((v_objValue, v_iIndex) =>
            new CSupplier_Xlsx_Cell(v_iIndex + 1, v_objValue.Type, v_objValue.Value, v_objValue.Formula, null)).ToArray());
}
