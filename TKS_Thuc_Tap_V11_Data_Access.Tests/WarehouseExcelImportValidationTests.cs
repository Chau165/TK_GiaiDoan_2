using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseExcelImportValidationTests
{
    private static readonly string[] m_arrHeaders = { "Ten_Kho", "Ghi_Chu" };

    [Fact]
    public void Preview_trims_warehouse_name_and_note()
    {
        var v_objResult = Preview(Worksheet(m_arrHeaders, new object?[] { " Kho chính ", " Ghi chú " }));

        var v_objRow = Assert.Single(v_objResult.Rows);
        Assert.True(v_objResult.Can_Confirm);
        Assert.Equal("Kho chính", v_objRow.Ten_Kho);
        Assert.Equal("Ghi chú", v_objRow.Ghi_Chu);
    }

    [Fact]
    public void Preview_rejects_duplicate_names_in_file_and_database()
    {
        var v_objInFile = Preview(Worksheet(m_arrHeaders,
            new object?[] { "Kho chính", "" },
            new object?[] { "kHO CHÍNH", "" }));
        var v_objExisting = Preview(Worksheet(m_arrHeaders, new object?[] { "Kho hiện hữu", "" }),
            new[] { new CDM_Kho { Ten_Kho = "KHO HIỆN HỮU" } });

        Assert.True(v_objInFile.Can_Confirm);
        Assert.Equal(1, v_objInFile.Skipped_Rows);
        Assert.False(v_objExisting.Can_Confirm);
        Assert.Equal(1, v_objExisting.Skipped_Rows);
    }

    [Fact]
    public void Preview_rejects_same_warehouse_name_with_different_note()
    {
        var v_objResult = Preview(Worksheet(m_arrHeaders,
            new object?[] { "Kho A", "Nhận hàng" },
            new object?[] { "kho a", "Xuất hàng" }));

        Assert.False(v_objResult.Can_Confirm);
        Assert.Contains(v_objResult.Errors, v_objError => v_objError.Message.Contains("trùng khóa nhưng dữ liệu khác", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Preview_uses_case_insensitive_accent_sensitive_name_identity()
    {
        var v_objCaseDuplicate = Preview(Worksheet(m_arrHeaders,
            new object?[] { "Kho É", "" },
            new object?[] { "kho é", "" }));
        var v_objAccentDistinct = Preview(Worksheet(m_arrHeaders,
            new object?[] { "Kho É", "" },
            new object?[] { "Kho E", "" }));

        Assert.True(v_objCaseDuplicate.Can_Confirm);
        Assert.Equal(1, v_objCaseDuplicate.Skipped_Rows);
        Assert.True(v_objAccentDistinct.Can_Confirm);
    }

    [Theory]
    [InlineData("", "note", "Ten_Kho")]
    [InlineData("   ", "note", "Ten_Kho")]
    public void Preview_requires_warehouse_name(string p_strName, string p_strNote, string p_strColumn)
    {
        var v_objResult = Preview(Worksheet(m_arrHeaders, new object?[] { p_strName, p_strNote }));

        Assert.Contains(v_objResult.Errors, v_objError => v_objError.Column_Name == p_strColumn);
    }

    [Theory]
    [InlineData(0, 256, "Ten_Kho")]
    [InlineData(1, 1001, "Ghi_Chu")]
    public void Preview_rejects_values_over_database_limits(int p_iColumn, int p_iLength, string p_strColumn)
    {
        var v_arrValues = new object?[] { "Kho", "" };
        v_arrValues[p_iColumn] = new string('X', p_iLength);

        var v_objResult = Preview(Worksheet(m_arrHeaders, v_arrValues));

        Assert.Contains(v_objResult.Errors, v_objError => v_objError.Column_Name == p_strColumn);
    }

    [Fact]
    public void Preview_rejects_wrong_sheet_headers_and_formula_cells()
    {
        var v_objWrongSheet = Preview(WorksheetOnSheet(m_arrHeaders, "Warehouses", new object?[] { "Kho", "" }));
        var v_objBadHeaders = Preview(Worksheet(new[] { "Ghi_Chu", "Unknown" }, new object?[] { "Ghi chú", "Kho" }));
        var v_objFormula = Preview(WorksheetWithRows("Kho", m_arrHeaders,
            DataRow(2, (null, "Formula", "1+1"), ("", "Blank", null))));

        Assert.False(v_objWrongSheet.Can_Confirm);
        Assert.False(v_objBadHeaders.Can_Confirm);
        Assert.False(v_objFormula.Can_Confirm);
        Assert.Contains(v_objFormula.Errors, v_objError =>
            v_objError.Message.Contains("cột Ten_Kho: Không hỗ trợ công thức Excel.", StringComparison.Ordinal));
    }

    private static CWarehouseImportPreviewResult Preview(
        CWarehouse_Xlsx_Worksheet p_objWorksheet,
        IReadOnlyList<CDM_Kho>? p_arrExisting = null) =>
        CWarehouseImportValidator.Preview(p_objWorksheet, p_arrExisting ?? Array.Empty<CDM_Kho>());

    private static CWarehouse_Xlsx_Worksheet Worksheet(string?[] p_arrHeaders, params object?[][] p_arrDataRows) =>
        WorksheetOnSheet(p_arrHeaders, "Kho", p_arrDataRows);

    private static CWarehouse_Xlsx_Worksheet WorksheetOnSheet(
        string?[] p_arrHeaders,
        string p_strSheetName,
        params object?[][] p_arrDataRows)
    {
        var v_arrRows = new List<CWarehouse_Xlsx_Row>
        {
            DataRow(1, p_arrHeaders.Select(v_strHeader => ((object?)v_strHeader, "Text", (string?)null)).ToArray())
        };
        for (var v_iIndex = 0; v_iIndex < p_arrDataRows.Length; v_iIndex++)
            v_arrRows.Add(DataRow(v_iIndex + 2, p_arrDataRows[v_iIndex]
                .Select(v_objValue => (v_objValue, v_objValue is null ? "Blank" : "Text", (string?)null)).ToArray()));
        return new CWarehouse_Xlsx_Worksheet(p_strSheetName, v_arrRows);
    }

    private static CWarehouse_Xlsx_Worksheet WorksheetWithRows(
        string p_strSheetName,
        string?[] p_arrHeaders,
        params CWarehouse_Xlsx_Row[] p_arrDataRows)
    {
        var v_arrRows = new List<CWarehouse_Xlsx_Row>
        {
            DataRow(1, p_arrHeaders.Select(v_strHeader => ((object?)v_strHeader, "Text", (string?)null)).ToArray())
        };
        v_arrRows.AddRange(p_arrDataRows);
        return new CWarehouse_Xlsx_Worksheet(p_strSheetName, v_arrRows);
    }

    private static CWarehouse_Xlsx_Row DataRow(int p_iRow, params (object? Value, string Type, string? Formula)[] p_arrValues) =>
        new(p_iRow, false, p_arrValues.Select((v_objValue, v_iIndex) =>
            new CWarehouse_Xlsx_Cell(v_iIndex + 1, v_objValue.Type, v_objValue.Value, v_objValue.Formula, null)).ToArray());
}
