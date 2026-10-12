using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class ProductExcelImportValidationTests
{
    private static readonly string[] m_arrHeaders =
    {
        "Ma_San_Pham", "Ten_San_Pham", "Ma_LSP", "Ten_Don_Vi_Tinh", "Ghi_Chu"
    };

    [Fact]
    public void Preview_trims_values_preserves_text_codes_and_resolves_references()
    {
        var v_objResult = Preview(
            Worksheet(m_arrHeaders, new object?[] { " 00123 ", " Sữa hộp ", " LSP001 ", " Hộp ", " ghi chú " }));

        var v_objRow = Assert.Single(v_objResult.Rows);
        Assert.Equal(1, v_objResult.Valid_Rows);
        Assert.Equal(0, v_objResult.Invalid_Rows);
        Assert.True(v_objResult.Can_Confirm);
        Assert.Equal("00123", v_objRow.Ma_San_Pham);
        Assert.Equal("Sữa hộp", v_objRow.Ten_San_Pham);
        Assert.Equal(10L, v_objRow.Loai_San_Pham_ID);
        Assert.Equal(20L, v_objRow.Don_Vi_Tinh_ID);
        Assert.Equal("ghi chú", v_objRow.Ghi_Chu);
    }

    [Theory]
    [InlineData("", "Thiếu tiêu đề cột Ma_San_Pham")]
    [InlineData("Ma_San_Pham|Ten_San_Pham|Ma_LSP|Ten_Don_Vi_Tinh|Unknown", "Không nhận diện tiêu đề Unknown")]
    [InlineData("Ma_San_Pham|Ten_San_Pham|Ma_LSP|Ten_Don_Vi_Tinh|", "Tiêu đề cột không được để trống")]
    [InlineData("Ma_San_Pham|Ma_San_Pham|Ma_LSP|Ten_Don_Vi_Tinh|Ghi_Chu", "Tiêu đề Ma_San_Pham bị lặp")]
    public void Preview_rejects_nonstandard_headers(string p_strHeaders, string p_strExpectedError)
    {
        var v_arrHeaders = p_strHeaders.Split('|');
        var v_objResult = Preview(Worksheet(v_arrHeaders));

        Assert.False(v_objResult.Can_Confirm);
        Assert.Contains(v_objResult.Errors, v_objError => v_objError.Message.Contains(p_strExpectedError, StringComparison.Ordinal));
    }

    [Fact]
    public void Preview_rejects_formula_cells_with_column_specific_message()
    {
        var v_objRow = DataRow(
            2,
            (null, "Formula", "A1"),
            ("Sữa", "Text", null),
            ("LSP001", "Text", null),
            ("Hộp", "Text", null),
            ("ghi chú", "Text", null));
        var v_objResult = Preview(WorksheetWithRows(m_arrHeaders, v_objRow));

        Assert.Contains(v_objResult.Errors, v_objError =>
            v_objError.Message == "Dòng 2, cột Ma_San_Pham: Không hỗ trợ công thức Excel.");
        Assert.False(v_objResult.Can_Confirm);
    }

    [Fact]
    public void Preview_rejects_formula_in_optional_note()
    {
        var v_objRow = DataRow(
            2,
            ("SP001", "Text", null),
            ("Sữa", "Text", null),
            ("LSP001", "Text", null),
            ("Hộp", "Text", null),
            (null, "Formula", "A1"));
        var v_objResult = Preview(WorksheetWithRows(m_arrHeaders, v_objRow));

        Assert.Contains(v_objResult.Errors, v_objError => v_objError.Message.Contains("cột Ghi_Chu: Không hỗ trợ công thức Excel.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, "", "Ma_San_Pham")]
    [InlineData(1, "", "Ten_San_Pham")]
    [InlineData(2, "", "Ma_LSP")]
    [InlineData(3, "", "Ten_Don_Vi_Tinh")]
    public void Preview_requires_all_business_fields(int p_iColumn, string p_strValue, string p_strExpectedColumn)
    {
        var v_arrValues = new object?[] { "SP001", "Sữa", "LSP001", "Hộp", "" };
        v_arrValues[p_iColumn] = p_strValue;
        var v_objResult = Preview(Worksheet(m_arrHeaders, v_arrValues));

        Assert.Contains(v_objResult.Errors, v_objError => v_objError.Column_Name == p_strExpectedColumn);
        Assert.Equal(0, v_objResult.Valid_Rows);
    }

    [Theory]
    [InlineData(0, 101, "Ma_San_Pham")]
    [InlineData(1, 256, "Ten_San_Pham")]
    [InlineData(3, 201, "Ten_Don_Vi_Tinh")]
    [InlineData(4, 1001, "Ghi_Chu")]
    public void Preview_rejects_values_over_business_limits(int p_iColumn, int p_iLength, string p_strExpectedColumn)
    {
        var v_arrValues = new object?[] { "SP001", "Sữa", "LSP001", "Hộp", "" };
        v_arrValues[p_iColumn] = new string('X', p_iLength);
        var v_objResult = Preview(Worksheet(m_arrHeaders, v_arrValues));

        Assert.Contains(v_objResult.Errors, v_objError => v_objError.Column_Name == p_strExpectedColumn);
    }

    [Fact]
    public void Preview_rejects_duplicate_codes_in_file_and_database_without_updating_existing_products()
    {
        var v_objResult = Preview(
            Worksheet(m_arrHeaders,
                new object?[] { "SP001", "Sữa", "LSP001", "Hộp", "" },
                new object?[] { " sp001 ", "Sữa khác", "LSP001", "Hộp", "" }),
            new[] { "SP-DB" });
        var v_objDatabaseDuplicate = Preview(
            Worksheet(m_arrHeaders, new object?[] { " SP-DB ", "Sữa", "LSP001", "Hộp", "" }),
            new[] { "SP-DB" });

        Assert.Equal(1, v_objResult.Invalid_Rows);
        Assert.Contains(v_objResult.Errors, v_objError => v_objError.Message.Contains("trùng khóa nhưng dữ liệu khác", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(v_objDatabaseDuplicate.Errors, v_objError =>
            v_objError.Message.Contains("trùng khóa nhưng dữ liệu khác", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Preview_skips_exact_product_duplicates_in_file()
    {
        var v_objResult = Preview(Worksheet(m_arrHeaders,
            new object?[] { "SP001", "Sữa", "LSP001", "Hộp", "Lạnh" },
            new object?[] { " sp001 ", " sữa ", "lsp001", "Hộp", " Lạnh " }));

        Assert.True(v_objResult.Can_Confirm);
        Assert.Equal(1, v_objResult.Valid_Rows);
        Assert.Equal(1, v_objResult.Skipped_Rows);
        Assert.True(v_objResult.Rows[1].Is_Skipped);
    }

    [Fact]
    public void Preview_skips_exact_database_product_and_rejects_same_code_with_different_content()
    {
        var v_objExisting = new CDM_San_Pham
        {
            Ma_San_Pham = "SP001",
            Ten_San_Pham = "Sữa",
            Loai_San_Pham_ID = 10,
            Don_Vi_Tinh_ID = 20,
            Ghi_Chu = "Lạnh"
        };
        var v_objExact = CProductImportValidator.Preview(
            Worksheet(m_arrHeaders,
                new object?[] { "sp001", " sữa ", "lsp001", "HỘP", " Lạnh " },
                new object?[] { "SP002", "Nước", "LSP001", "Hộp", "" }),
            new[] { new CDM_Loai_San_Pham { Auto_ID = 10, Ma_LSP = "LSP001", Ten_LSP = "Đồ uống" } },
            new[] { new CDM_Don_Vi_Tinh { Auto_ID = 20, Ten_Don_Vi_Tinh = "Hộp" } },
            new[] { v_objExisting });
        var v_objConflict = CProductImportValidator.Preview(
            Worksheet(m_arrHeaders, new object?[] { "SP001", "Sữa khác", "LSP001", "Hộp", "Lạnh" }),
            new[] { new CDM_Loai_San_Pham { Auto_ID = 10, Ma_LSP = "LSP001", Ten_LSP = "Đồ uống" } },
            new[] { new CDM_Don_Vi_Tinh { Auto_ID = 20, Ten_Don_Vi_Tinh = "Hộp" } },
            new[] { v_objExisting });

        Assert.True(v_objExact.Can_Confirm);
        Assert.Equal(1, v_objExact.Skipped_Rows);
        Assert.Equal(1, v_objExact.Valid_Rows);
        Assert.False(v_objConflict.Can_Confirm);
        Assert.Contains(v_objConflict.Errors, v_objError =>
            v_objError.Message.Contains("trùng khóa nhưng dữ liệu khác", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Preview_keeps_product_category_and_unit_identity_accent_sensitive()
    {
        var v_objProductCode = Preview(
            Worksheet(m_arrHeaders, new object?[] { "SPÉ", "Sữa", "LSP001", "hỘP", "" }),
            new[] { "SPE" });
        var v_objCategoryCode = Preview(
            Worksheet(m_arrHeaders, new object?[] { "SP002", "Sữa", "LSPE", "Hộp", "" }),
            categories: new[] { new CDM_Loai_San_Pham { Auto_ID = 10, Ma_LSP = "LSPÉ", Ten_LSP = "Đồ uống" } });
        var v_objUnitName = Preview(
            Worksheet(m_arrHeaders, new object?[] { "SP003", "Sữa", "LSP001", "BOX_EXTERNAL", "" }));

        Assert.True(v_objProductCode.Can_Confirm);
        Assert.Equal(20L, Assert.Single(v_objProductCode.Rows).Don_Vi_Tinh_ID);
        Assert.Contains(v_objCategoryCode.Errors, v_objError =>
            v_objError.Column_Name == "Ma_LSP" && v_objError.Message.Contains("Không tìm thấy", StringComparison.Ordinal));
        Assert.Contains(v_objUnitName.Errors, v_objError =>
            v_objError.Column_Name == "Ten_Don_Vi_Tinh" && v_objError.Message.Contains("Không tìm thấy", StringComparison.Ordinal));
    }

    [Fact]
    public void Preview_rejects_unknown_category_and_unit()
    {
        var v_objResult = Preview(Worksheet(m_arrHeaders, new object?[] { "SP001", "Sữa", "LSP404", "Không có", "" }));

        Assert.Contains(v_objResult.Errors, v_objError => v_objError.Column_Name == "Ma_LSP");
        Assert.Contains(v_objResult.Errors, v_objError => v_objError.Column_Name == "Ten_Don_Vi_Tinh");
    }

    [Fact]
    public void Preview_maps_customer_unit_code_before_exact_unit_reference_lookup()
    {
        var v_objResult = CProductImportValidator.Preview(
            Worksheet(m_arrHeaders, new object?[] { "SP001", "Sữa", "LSP001", " THUNG ", "" }),
            new[] { new CDM_Loai_San_Pham { Auto_ID = 10, Ma_LSP = "LSP001" } },
            new[] { new CDM_Don_Vi_Tinh { Auto_ID = 77, Ten_Don_Vi_Tinh = "Thùng" } },
            Array.Empty<string>());

        var v_objRow = Assert.Single(v_objResult.Rows);
        Assert.True(v_objRow.Is_Valid);
        Assert.Equal(77L, v_objRow.Don_Vi_Tinh_ID);
        Assert.Equal("Thùng", v_objRow.Ten_Don_Vi_Tinh);
        Assert.Contains(v_objResult.Value_Mappings, v_objMapping =>
            v_objMapping.SourceValue.Trim() == "THUNG" && v_objMapping.MappedValue == "Thùng" && v_objMapping.Status == "RESOLVED");
    }

    [Fact]
    public void Preview_matches_unit_name_case_insensitively_and_rejects_ambiguous_matches()
    {
        var v_objUnique = Preview(Worksheet(m_arrHeaders, new object?[] { "SP001", "Sữa", "LSP001", "HỘP", "" }));
        var v_objAmbiguous = Preview(
            Worksheet(m_arrHeaders, new object?[] { "SP001", "Sữa", "LSP001", "Hộp", "" }),
            units: new[]
            {
                new CDM_Don_Vi_Tinh { Auto_ID = 20, Ten_Don_Vi_Tinh = "Hộp" },
                new CDM_Don_Vi_Tinh { Auto_ID = 21, Ten_Don_Vi_Tinh = "HỘP" }
            });

        Assert.Equal(20L, Assert.Single(v_objUnique.Rows).Don_Vi_Tinh_ID);
        Assert.Contains(v_objAmbiguous.Errors, v_objError => v_objError.Column_Name == "Ten_Don_Vi_Tinh" && v_objError.Message.Contains("không duy nhất", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Cuộn", 205L)]
    [InlineData("Cuốn", 211L)]
    [InlineData("CUỘN", 205L)]
    [InlineData("CUỐN", 211L)]
    public void Preview_resolves_unit_names_case_insensitively_but_accent_sensitively(string p_strUnitName, long p_iExpectedUnitId)
    {
        var v_objResult = Preview(
            Worksheet(m_arrHeaders, new object?[] { "SP001", "Sản phẩm", "LSP001", p_strUnitName, "" }),
            units: new[]
            {
                new CDM_Don_Vi_Tinh { Auto_ID = 205, Ten_Don_Vi_Tinh = "Cuộn" },
                new CDM_Don_Vi_Tinh { Auto_ID = 211, Ten_Don_Vi_Tinh = "Cuốn" }
            });

        Assert.True(v_objResult.Can_Confirm);
        Assert.Equal(p_iExpectedUnitId, Assert.Single(v_objResult.Rows).Don_Vi_Tinh_ID);
    }

    [Fact]
    public void Preview_ignores_blank_rows_and_rejects_a_header_only_workbook()
    {
        var v_objWithBlankRow = Preview(Worksheet(
            m_arrHeaders,
            new object?[] { "SP001", "Sữa", "LSP001", "Hộp", "" },
            new object?[] { null, null, null, null, null }));
        var v_objHeaderOnly = Preview(Worksheet(m_arrHeaders));

        Assert.Equal(1, v_objWithBlankRow.Total_Rows);
        Assert.Equal(1, v_objWithBlankRow.Valid_Rows);
        Assert.False(v_objHeaderOnly.Can_Confirm);
        Assert.Contains(v_objHeaderOnly.Errors, v_objError => v_objError.Message.Contains("không có dòng dữ liệu", StringComparison.OrdinalIgnoreCase));
    }

    private static CProductImportPreviewResult Preview(
        CProduct_Xlsx_Worksheet p_objWorksheet,
        IEnumerable<string>? p_arrExistingCodes = null,
        IReadOnlyList<CDM_Loai_San_Pham>? categories = null,
        IReadOnlyList<CDM_Don_Vi_Tinh>? units = null)
    {
        return CProductImportValidator.Preview(
            p_objWorksheet,
            categories ?? new[] { new CDM_Loai_San_Pham { Auto_ID = 10, Ma_LSP = "LSP001", Ten_LSP = "Đồ uống" } },
            units ?? new[] { new CDM_Don_Vi_Tinh { Auto_ID = 20, Ten_Don_Vi_Tinh = "Hộp" } },
            p_arrExistingCodes ?? Array.Empty<string>());
    }

    private static CProduct_Xlsx_Worksheet Worksheet(string?[] p_arrHeaders, params object?[][] p_arrDataRows)
    {
        var v_arrRows = new List<CProduct_Xlsx_Row>
        {
            DataRow(1, p_arrHeaders.Select(v_strHeader => ((object?)v_strHeader, "Text", (string?)null)).ToArray())
        };

        for (var v_iIndex = 0; v_iIndex < p_arrDataRows.Length; v_iIndex++)
            v_arrRows.Add(DataRow(v_iIndex + 2, p_arrDataRows[v_iIndex].Select(v_objValue => (v_objValue, v_objValue is null ? "Blank" : "Text", (string?)null)).ToArray()));

        return new CProduct_Xlsx_Worksheet("SanPham", v_arrRows);
    }

    private static CProduct_Xlsx_Worksheet WorksheetWithRows(string?[] p_arrHeaders, params CProduct_Xlsx_Row[] p_arrDataRows)
    {
        var v_arrRows = new List<CProduct_Xlsx_Row>
        {
            DataRow(1, p_arrHeaders.Select(v_strHeader => ((object?)v_strHeader, "Text", (string?)null)).ToArray())
        };
        v_arrRows.AddRange(p_arrDataRows);
        return new CProduct_Xlsx_Worksheet("SanPham", v_arrRows);
    }

    private static CProduct_Xlsx_Row DataRow(int p_iRow, params (object? Value, string Type, string? Formula)[] p_arrValues)
    {
        return new CProduct_Xlsx_Row(
            p_iRow,
            false,
            p_arrValues.Select((v_objValue, v_iIndex) => new CProduct_Xlsx_Cell(
                v_iIndex + 1,
                v_objValue.Type,
                v_objValue.Value,
                v_objValue.Formula,
                null)).ToArray());
    }
}
