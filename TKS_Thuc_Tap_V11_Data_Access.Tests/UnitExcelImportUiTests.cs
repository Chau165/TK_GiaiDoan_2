using ClosedXML.Excel;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class UnitExcelImportUiTests
{
    [Fact]
    public void Unit_import_action_is_scoped_to_Add_and_refreshes_once_after_success()
    {
        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("m_strMaster_Type != \"DonViTinh\" || !r_objChuc_Nang.Is_Have_Add_Permission", v_strSource);
        Assert.Contains("<FWarehouse_Unit_Import", v_strSource);
        Assert.Contains("On_Unit_Import_Completed", v_strSource);
        Assert.Contains("\"Unit Excel Import\"", v_strSource);
        Assert.Contains("m_grdMaster.Rebind();", v_strSource);
    }

    [Fact]
    public async Task Official_template_has_one_sheet_and_exact_unit_headers()
    {
        var v_strPath = FindRepositoryPath("TKS_Thuc_Tap_V11_Web", "wwwroot", "FileManagement", "Template", "Don_Vi_Tinh_Import_Template.xlsx");
        await using var v_objStream = File.OpenRead(v_strPath);
        var v_objSheet = await new CUnit_Xlsx_Reader().ReadAsync(v_objStream);

        Assert.Equal("DonViTinh", v_objSheet.Name);
        Assert.Equal(new[] { "Ten_Don_Vi_Tinh", "Ghi_Chu" }, v_objSheet.Rows.Single().Cells.Select(v_objCell => v_objCell.Value));
        using var v_objWorkbook = new XLWorkbook(v_strPath);
        Assert.Single(v_objWorkbook.Worksheets);
        Assert.True(v_objWorkbook.Worksheet("DonViTinh").Column(1).Width > 12);
    }

    [Fact]
    public void Unit_import_dialog_exposes_template_preview_errors_and_confirm_gating()
    {
        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_Unit_Import.razor"));

        Assert.Contains("Don_Vi_Tinh_Import_Template.xlsx", v_strSource);
        Assert.Contains("Tải template", v_strSource);
        Assert.Contains("Preview", v_strSource);
        Assert.Contains("PreviewValid", v_strSource);
        Assert.Contains("PreviewInvalid", v_strSource);
        Assert.Contains("disabled=\"@(!Can_Confirm)\"", v_strSource);
        Assert.Contains("Confirm_Async", v_strSource);
    }

    private static string FindRepositoryPath(params string[] p_arrParts)
    {
        for (var v_objDirectory = new DirectoryInfo(AppContext.BaseDirectory); v_objDirectory is not null; v_objDirectory = v_objDirectory.Parent)
        {
            var v_strCandidate = Path.Combine(new[] { v_objDirectory.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_strCandidate))
                return v_strCandidate;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(p_arrParts)}");
    }
}
