using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class UnitExcelImportValidationTests
{
    private static readonly string[] m_arrHeaders = { "Ten_Don_Vi_Tinh", "Ghi_Chu" };

    [Fact]
    public void Preview_trims_values_and_accepts_optional_note()
    {
        var v_objResult = Preview(Worksheet(m_arrHeaders, new object?[] { "  Hộp  ", " ghi chú " }));

        var v_objRow = Assert.Single(v_objResult.Rows);
        Assert.True(v_objResult.Can_Confirm);
        Assert.Equal("Hộp", v_objRow.Ten_Don_Vi_Tinh);
        Assert.Equal("ghi chú", v_objRow.Ghi_Chu);
    }

    [Theory]
    [InlineData("Cuộn", "CUỘN", true)]
    [InlineData("Cuốn", "CUỐN", true)]
    [InlineData("Cuộn", "Cuốn", true)]
    [InlineData("Hộp", "HỘP", true)]
    public void Preview_uses_case_insensitive_accent_sensitive_unit_identity(
        string p_strFirst, string p_strSecond, bool p_bExpectedValid)
    {
        var v_objResult = Preview(Worksheet(m_arrHeaders,
            new object?[] { p_strFirst, "" },
            new object?[] { p_strSecond, "" }));

        Assert.Equal(p_bExpectedValid, v_objResult.Can_Confirm);
        if (!p_bExpectedValid)
            Assert.Contains(v_objResult.Errors, v_objError => v_objError.Column_Name == "Ten_Don_Vi_Tinh");
    }

    [Fact]
    public void Preview_skips_exact_duplicates_but_rejects_same_name_with_different_note()
    {
        var v_objExact = Preview(Worksheet(m_arrHeaders,
            new object?[] { "Hộp", "Chai" },
            new object?[] { " hộp ", " Chai " }));
        var v_objConflict = Preview(Worksheet(m_arrHeaders,
            new object?[] { "Hộp", "Chai" },
            new object?[] { "hỘp", "Thùng" }));

        Assert.True(v_objExact.Can_Confirm);
        Assert.Equal(1, v_objExact.Valid_Rows);
        Assert.Equal(1, v_objExact.Skipped_Rows);
        Assert.True(v_objExact.Rows[1].Is_Skipped);
        Assert.False(v_objConflict.Can_Confirm);
        Assert.Contains(v_objConflict.Errors, v_objError => v_objError.Message.Contains("trùng khóa nhưng dữ liệu khác", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Preview_skips_an_exact_existing_database_duplicate()
    {
        var v_objResult = Preview(
            Worksheet(m_arrHeaders, new object?[] { " CUỘN ", "" }),
            new[] { "Cuộn" });

        Assert.False(v_objResult.Can_Confirm);
        Assert.Equal(1, v_objResult.Skipped_Rows);
        Assert.Empty(v_objResult.Errors);
    }

    [Theory]
    [InlineData("", 1, "Ten_Don_Vi_Tinh")]
    [InlineData("Ten_Don_Vi_Tinh", 0, "Ghi_Chu")]
    [InlineData("Ghi_Chu|Ten_Don_Vi_Tinh", 0, "Ten_Don_Vi_Tinh")]
    [InlineData("Ten_Don_Vi_Tinh|Unknown", 0, "Unknown")]
    [InlineData("Ten_Don_Vi_Tinh|Ten_Don_Vi_Tinh", 0, "Ten_Don_Vi_Tinh")]
    public void Preview_rejects_invalid_headers(string p_strHeaders, int p_iDataRows, string p_strExpectedColumn)
    {
        var v_arrHeaders = p_strHeaders.Split('|');
        var v_arrRows = p_iDataRows == 0 ? Array.Empty<object?[]>() : new[] { new object?[] { "Hộp", "" } };
        var v_objResult = Preview(Worksheet(v_arrHeaders, v_arrRows));

        Assert.False(v_objResult.Can_Confirm);
        Assert.Contains(v_objResult.Errors, v_objError => v_objError.Column_Name == p_strExpectedColumn);
    }

    [Fact]
    public void Preview_rejects_wrong_sheet_and_formulas_in_business_fields()
    {
        var v_objWrongSheet = Preview(WorksheetOnSheet(m_arrHeaders, "Units", new object?[] { "Hộp", "" }));
        var v_objFormula = Preview(WorksheetWithRows("DonViTinh", m_arrHeaders,
            DataRow(2, (null, "Formula", "1+1"), ("", "Blank", null))));
        var v_objFormulaNote = Preview(WorksheetWithRows("DonViTinh", m_arrHeaders,
            DataRow(2, ("Hộp", "Text", null), (null, "Formula", "1+1"))));

        Assert.False(v_objWrongSheet.Can_Confirm);
        Assert.False(v_objFormula.Can_Confirm);
        Assert.False(v_objFormulaNote.Can_Confirm);
        Assert.Contains(v_objFormula.Errors, v_objError => v_objError.Message.Contains("cột Ten_Don_Vi_Tinh: Không hỗ trợ công thức Excel.", StringComparison.Ordinal));
        Assert.Contains(v_objFormulaNote.Errors, v_objError => v_objError.Message.Contains("cột Ghi_Chu: Không hỗ trợ công thức Excel.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(201, "Ten_Don_Vi_Tinh")]
    [InlineData(1001, "Ghi_Chu")]
    public void Preview_rejects_values_over_schema_limits(int p_iLength, string p_strColumn)
    {
        var v_objResult = p_strColumn == "Ten_Don_Vi_Tinh"
            ? Preview(Worksheet(m_arrHeaders, new object?[] { new string('X', p_iLength), "" }))
            : Preview(Worksheet(m_arrHeaders, new object?[] { "Hộp", new string('X', p_iLength) }));

        Assert.Contains(v_objResult.Errors, v_objError => v_objError.Column_Name == p_strColumn);
    }

    private static CUnitImportPreviewResult Preview(
        CUnit_Xlsx_Worksheet p_objWorksheet,
        IEnumerable<string>? p_arrExistingNames = null)
    {
        return CUnitImportValidator.Preview(p_objWorksheet, p_arrExistingNames ?? Array.Empty<string>());
    }

    private static CUnit_Xlsx_Worksheet Worksheet(
        string?[] p_arrHeaders,
        params object?[][] p_arrDataRows) => WorksheetOnSheet(p_arrHeaders, "DonViTinh", p_arrDataRows);

    private static CUnit_Xlsx_Worksheet WorksheetOnSheet(
        string?[] p_arrHeaders,
        string p_strSheetName,
        params object?[][] p_arrDataRows)
    {
        var v_arrRows = new List<CUnit_Xlsx_Row>
        {
            DataRow(1, p_arrHeaders.Select(v_strHeader => ((object?)v_strHeader, "Text", (string?)null)).ToArray())
        };
        for (var v_iIndex = 0; v_iIndex < p_arrDataRows.Length; v_iIndex++)
            v_arrRows.Add(DataRow(v_iIndex + 2, p_arrDataRows[v_iIndex].Select(v_objValue => (v_objValue, v_objValue is null ? "Blank" : "Text", (string?)null)).ToArray()));
        return new CUnit_Xlsx_Worksheet(p_strSheetName, v_arrRows);
    }

    private static CUnit_Xlsx_Worksheet WorksheetWithRows(
        string p_strSheetName,
        string?[] p_arrHeaders,
        params CUnit_Xlsx_Row[] p_arrDataRows)
    {
        var v_arrRows = new List<CUnit_Xlsx_Row>
        {
            DataRow(1, p_arrHeaders.Select(v_strHeader => ((object?)v_strHeader, "Text", (string?)null)).ToArray())
        };
        v_arrRows.AddRange(p_arrDataRows);
        return new CUnit_Xlsx_Worksheet(p_strSheetName, v_arrRows);
    }

    private static CUnit_Xlsx_Row DataRow(int p_iRow, params (object? Value, string Type, string? Formula)[] p_arrValues)
    {
        return new CUnit_Xlsx_Row(p_iRow, false, p_arrValues.Select((v_objValue, v_iIndex) =>
            new CUnit_Xlsx_Cell(v_iIndex + 1, v_objValue.Type, v_objValue.Value, v_objValue.Formula, null)).ToArray());
    }
}
