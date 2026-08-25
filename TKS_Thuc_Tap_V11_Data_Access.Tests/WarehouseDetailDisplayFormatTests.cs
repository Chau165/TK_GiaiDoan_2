using System.ComponentModel.DataAnnotations;
using System.Globalization;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseDetailDisplayFormatTests
{
    [Theory]
    [InlineData(6200, "6.200")]
    [InlineData(372000, "372.000")]
    [InlineData(0, "0")]
    [InlineData(123456789, "123.456.789")]
    [InlineData(-6200, "-6.200")]
    public void Format_So_Tien_uses_vnd_thousands_separator_without_decimals(decimal p_decValue, string p_strExpected)
    {
        Assert.Equal(p_strExpected, CUtility.Format_So_Tien(p_decValue));
    }

    [Theory]
    [InlineData(60, "60.00")]
    [InlineData(1.5, "1.50")]
    [InlineData(1234.567, "1234.57")]
    public void Format_So_Luong_shows_two_decimals_with_dot_separator(decimal p_decValue, string p_strExpected)
    {
        Assert.Equal(p_strExpected, CUtility.Format_So_Luong(p_decValue));
    }

    [Fact]
    public void Warehouse_document_detail_exposes_the_product_unit_name()
    {
        var v_objDetail = new CWarehouseDocumentDetail { Ten_Don_Vi_Tinh = "Chai" };

        Assert.Equal("Chai", v_objDetail.Ten_Don_Vi_Tinh);
    }

    [Fact]
    public void Detail_quantity_validation_accepts_decimal_value_under_vietnamese_culture()
    {
        var v_objPrevious_Culture = CultureInfo.CurrentCulture;
        var v_objPrevious_Ui_Culture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("vi-VN");

            var v_objDetail = new CWarehouseDocumentDetail { So_Luong = 0.0001m };
            var v_arrValidation_Result = new List<ValidationResult>();
            var v_objValidation_Context = new ValidationContext(v_objDetail)
            {
                MemberName = nameof(CWarehouseDocumentDetail.So_Luong)
            };

            var v_bIs_Valid = Validator.TryValidateProperty(
                v_objDetail.So_Luong,
                v_objValidation_Context,
                v_arrValidation_Result);

            Assert.True(v_bIs_Valid, string.Join("; ", v_arrValidation_Result.Select(it => it.ErrorMessage)));
        }
        finally
        {
            CultureInfo.CurrentCulture = v_objPrevious_Culture;
            CultureInfo.CurrentUICulture = v_objPrevious_Ui_Culture;
        }
    }

    [Fact]
    public void Warehouse_detail_stored_procedure_returns_the_product_unit_name()
    {
        var procedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));

        Assert.Contains("sp_XNK_Document_Detail_List", procedures);
        Assert.Contains("dv.Ten_Don_Vi_Tinh", procedures);
        Assert.Contains("JOIN dbo.tbl_DM_Don_Vi_Tinh dv", procedures);
    }

    [Fact]
    public void Warehouse_detail_view_shows_unit_next_to_quantity_and_formats_money_with_vnd_style()
    {
        var infoSource = File.ReadAllText(FindWarehouseComponent("FWarehouse_2_Warehouse_Info.razor"));

        Assert.Contains("CUtility.Format_So_Luong", infoSource);
        Assert.Contains("v_objDetail.Ten_Don_Vi_Tinh", infoSource);
        Assert.Contains("CUtility.Format_So_Tien", infoSource);
    }

    [Fact]
    public void Warehouse_report_grids_format_quantity_and_money_with_shared_helpers()
    {
        var listSource = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("CUtility.Format_So_Luong", listSource);
        Assert.Contains("CUtility.Format_So_Tien", listSource);
    }

    [Fact]
    public void Warehouse_detail_editor_formats_the_total_value_with_vnd_style()
    {
        var editSource = File.ReadAllText(FindWarehouseComponent("FWarehouse_3_Warehouse_Edit.razor"));

        Assert.Contains("CUtility.Format_So_Tien", editSource);
    }

    private static string FindWarehouseComponent(string fileName)
    {
        return FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", fileName);
    }

    private static string FindRepositoryPath(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(parts)}");
    }
}
