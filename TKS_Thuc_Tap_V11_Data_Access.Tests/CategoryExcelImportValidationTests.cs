using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class CategoryExcelImportValidationTests
{
    private static readonly string[] m_arrHeaders = { "Ma_LSP", "Ten_LSP", "Ghi_Chu" };

    [Fact]
    public void Preview_trims_code_name_and_note()
    {
        var v_objResult = Preview(Worksheet(m_arrHeaders, new object?[] { " 00123 ", " Đồ uống ", " lưu ý " }));

        var v_objRow = Assert.Single(v_objResult.Rows);
        Assert.True(v_objResult.Can_Confirm);
        Assert.Equal("00123", v_objRow.Ma_LSP);
        Assert.Equal("Đồ uống", v_objRow.Ten_LSP);
        Assert.Equal("lưu ý", v_objRow.Ghi_Chu);
    }

    [Fact]
    public void Preview_rejects_duplicate_codes_and_names_in_file_or_database()
    {
        var v_objInFile = Preview(Worksheet(m_arrHeaders,
            new object?[] { "LSP001", "Đồ uống", "" },
            new object?[] { "lsp001", "Hàng khô", "" },
            new object?[] { "LSP003", "đồ UỐNG", "" }));
        var v_objDatabaseCode = Preview(
            Worksheet(m_arrHeaders, new object?[] { "lsp-db", "Tên mới", "" }),
            new[] { new CWarehouseMaster { Code = "LSP-DB", Name = "Tên cũ" } });
        var v_objDatabaseName = Preview(
            Worksheet(m_arrHeaders, new object?[] { "LSP-NEW", "tÊN cŨ", "" }),
            new[] { new CWarehouseMaster { Code = "LSP-DB", Name = "Tên cũ" } });

        Assert.Contains(v_objInFile.Errors, v_objError => v_objError.Column_Name == "Ma_LSP");
        Assert.Contains(v_objInFile.Errors, v_objError => v_objError.Column_Name == "Ten_LSP");
        Assert.False(v_objDatabaseCode.Can_Confirm);
        Assert.False(v_objDatabaseName.Can_Confirm);
    }

    [Fact]
    public void Preview_skips_exact_duplicates_and_rejects_conflicting_code_reuse()
    {
        var v_objExact = Preview(Worksheet(m_arrHeaders,
            new object?[] { "LSP001", "Đồ uống", "Nước" },
            new object?[] { "lsp001", " đồ uống ", " Nước " }));
        var v_objConflict = Preview(Worksheet(m_arrHeaders,
            new object?[] { "LSP001", "Đồ uống", "Nước" },
            new object?[] { "lsp001", "Hàng khô", "Khô" }));
        var v_objExistingExact = Preview(
            Worksheet(m_arrHeaders, new object?[] { "LSP001", "Đồ uống", "Nước" }),
            new[] { new CWarehouseMaster { Code = "lsp001", Name = "đồ uống", Ghi_Chu = "Nước" } });

        Assert.True(v_objExact.Can_Confirm);
        Assert.Equal(1, v_objExact.Skipped_Rows);
        Assert.False(v_objConflict.Can_Confirm);
        Assert.Contains(v_objConflict.Errors, v_objError => v_objError.Message.Contains("trùng khóa nhưng dữ liệu khác", StringComparison.OrdinalIgnoreCase));
        Assert.False(v_objExistingExact.Can_Confirm);
        Assert.Equal(1, v_objExistingExact.Skipped_Rows);
        Assert.Empty(v_objExistingExact.Errors);
    }

    [Fact]
    public void Preview_uses_case_insensitive_accent_sensitive_identity_for_code_and_name()
    {
        var v_objCaseDuplicate = Preview(Worksheet(m_arrHeaders,
            new object?[] { "LSPÉ", "Cuộn", "" },
            new object?[] { "lspé", "Cuốn", "" }));
        var v_objAccentDistinct = Preview(Worksheet(m_arrHeaders,
            new object?[] { "LSPÉ", "Cuộn", "" },
            new object?[] { "LSPE", "Cuốn", "" }));

        Assert.False(v_objCaseDuplicate.Can_Confirm);
        Assert.True(v_objAccentDistinct.Can_Confirm);
    }

    [Theory]
    [InlineData("", "Loại", "Ma_LSP")]
    [InlineData("LSP001", "", "Ten_LSP")]
    public void Preview_requires_code_and_name(string p_strCode, string p_strName, string p_strColumn)
    {
        var v_objResult = Preview(Worksheet(m_arrHeaders, new object?[] { p_strCode, p_strName, "" }));

        Assert.Contains(v_objResult.Errors, v_objError => v_objError.Column_Name == p_strColumn);
    }

    [Theory]
    [InlineData(0, 101, "Ma_LSP")]
    [InlineData(1, 201, "Ten_LSP")]
    [InlineData(2, 1001, "Ghi_Chu")]
    public void Preview_rejects_values_over_database_limits(int p_iColumn, int p_iLength, string p_strColumn)
    {
        var v_arrValues = new object?[] { "LSP001", "Loại", "" };
        v_arrValues[p_iColumn] = new string('X', p_iLength);
        var v_objResult = Preview(Worksheet(m_arrHeaders, v_arrValues));

        Assert.Contains(v_objResult.Errors, v_objError => v_objError.Column_Name == p_strColumn);
    }

    [Fact]
    public void Preview_rejects_wrong_sheet_headers_and_formula_cells()
    {
        var v_objWrongSheet = Preview(WorksheetOnSheet(m_arrHeaders, "Categories", new object?[] { "LSP001", "Loại", "" }));
        var v_objBadHeaders = Preview(Worksheet(new[] { "Ten_LSP", "Ma_LSP", "Unknown" }, new object?[] { "Loại", "LSP001", "" }));
        var v_objFormula = Preview(WorksheetWithRows("LoaiSanPham", m_arrHeaders,
            DataRow(2, ("LSP001", "Text", null), (null, "Formula", "1+1"), ("", "Blank", null))));

        Assert.False(v_objWrongSheet.Can_Confirm);
        Assert.False(v_objBadHeaders.Can_Confirm);
        Assert.False(v_objFormula.Can_Confirm);
        Assert.Contains(v_objFormula.Errors, v_objError => v_objError.Message.Contains("cột Ten_LSP: Không hỗ trợ công thức Excel.", StringComparison.Ordinal));
    }

    private static CCategoryImportPreviewResult Preview(
        CCategory_Xlsx_Worksheet p_objWorksheet,
        IReadOnlyList<CWarehouseMaster>? p_arrExisting = null) =>
        CCategoryImportValidator.Preview(p_objWorksheet, p_arrExisting ?? Array.Empty<CWarehouseMaster>());

    private static CCategory_Xlsx_Worksheet Worksheet(string?[] p_arrHeaders, params object?[][] p_arrDataRows) =>
        WorksheetOnSheet(p_arrHeaders, "LoaiSanPham", p_arrDataRows);

    private static CCategory_Xlsx_Worksheet WorksheetOnSheet(
        string?[] p_arrHeaders,
        string p_strSheetName,
        params object?[][] p_arrDataRows)
    {
        var v_arrRows = new List<CCategory_Xlsx_Row>
        {
            DataRow(1, p_arrHeaders.Select(v_strHeader => ((object?)v_strHeader, "Text", (string?)null)).ToArray())
        };
        for (var v_iIndex = 0; v_iIndex < p_arrDataRows.Length; v_iIndex++)
            v_arrRows.Add(DataRow(v_iIndex + 2, p_arrDataRows[v_iIndex].Select(v_objValue => (v_objValue, v_objValue is null ? "Blank" : "Text", (string?)null)).ToArray()));
        return new CCategory_Xlsx_Worksheet(p_strSheetName, v_arrRows);
    }

    private static CCategory_Xlsx_Worksheet WorksheetWithRows(
        string p_strSheetName,
        string?[] p_arrHeaders,
        params CCategory_Xlsx_Row[] p_arrDataRows)
    {
        var v_arrRows = new List<CCategory_Xlsx_Row>
        {
            DataRow(1, p_arrHeaders.Select(v_strHeader => ((object?)v_strHeader, "Text", (string?)null)).ToArray())
        };
        v_arrRows.AddRange(p_arrDataRows);
        return new CCategory_Xlsx_Worksheet(p_strSheetName, v_arrRows);
    }

    private static CCategory_Xlsx_Row DataRow(int p_iRow, params (object? Value, string Type, string? Formula)[] p_arrValues) =>
        new(p_iRow, false, p_arrValues.Select((v_objValue, v_iIndex) =>
            new CCategory_Xlsx_Cell(v_iIndex + 1, v_objValue.Type, v_objValue.Value, v_objValue.Formula, null)).ToArray());
}
