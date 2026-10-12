using ClosedXML.Excel;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class CategoryExcelImportUiTests
{
    [Fact]
    public void Category_import_action_is_scoped_to_Add_and_refreshes_once_after_success()
    {
        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("m_strMaster_Type != \"LoaiSanPham\" || !r_objChuc_Nang.Is_Have_Add_Permission", v_strSource);
        Assert.Contains("<FWarehouse_Category_Import", v_strSource);
        Assert.Contains("On_Category_Import_Completed", v_strSource);
        Assert.Contains("\"Category Excel Import\"", v_strSource);
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

        Assert.Contains("Loai_San_Pham_Import_Template.xlsx", v_strSource);
        Assert.Contains("Xem trước", v_strSource);
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
