using ClosedXML.Excel;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class CategoryExcelImportUiTests
{
    [Fact]
    public void Category_route_uses_FBase_import_instead_of_legacy_preview_confirm_import()
    {
        var v_strRoute = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Warehouse_Loai_San_Pham.razor"));
        var v_strList = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "F2017_1_Loai_San_Pham_List.razor"));

        Assert.Contains("<F2017_1_Loai_San_Pham_List />", v_strRoute);
        Assert.Contains("@if (r_objChuc_Nang.Is_Have_Add_Permission)", v_strList);
        Assert.Contains("Open_Import_Excel(\"Loai_San_Pham_Import_Template.xlsx\")", v_strList);
        Assert.Contains("FCommon_Import_Excel", v_strList);
        Assert.Contains("protected override void Import_Excel_Entry", v_strList);
        Assert.DoesNotContain("<FWarehouse_Category_Import", v_strList);
    }

    [Fact]
    public async Task Official_template_has_one_sheet_exact_headers_and_text_code_column()
    {
        var v_strPath = FindRepositoryPath("TKS_Thuc_Tap_V11_Web", "wwwroot", "FileManagement", "Template", "Loai_San_Pham_Import_Template.xlsx");
        await using var v_objStream = File.OpenRead(v_strPath);
        var v_objSheet = await new CCategory_Xlsx_Reader().ReadAsync(v_objStream);

        Assert.Equal("LoaiSanPham", v_objSheet.Name);
        Assert.Equal(new[] { "Ma_LSP", "Ten_LSP", "Ghi_Chu" }, v_objSheet.Rows.Single().Cells.Select(v_objCell => v_objCell.Value));
        using var v_objWorkbook = new XLWorkbook(v_strPath);
        Assert.Single(v_objWorkbook.Worksheets);
        Assert.Equal("@", v_objWorkbook.Worksheet("LoaiSanPham").Cell("A2").Style.NumberFormat.Format);
    }

    [Fact]
    public void Category_import_dialog_shows_template_preview_errors_and_confirm_gating()
    {
        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_Category_Import.razor"));
        var v_strShell = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_Import_Preview_Shell.razor"));

        Assert.Contains("<FWarehouse_Import_Preview_Shell", v_strSource);
        Assert.Contains("Loai_San_Pham_Import_Template.xlsx", v_strSource);
        Assert.Contains("PreviewValid", v_strSource);
        Assert.Contains("PreviewInvalid", v_strSource);
        Assert.Contains("disabled=\"@(!CanConfirm)\"", v_strShell);
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
