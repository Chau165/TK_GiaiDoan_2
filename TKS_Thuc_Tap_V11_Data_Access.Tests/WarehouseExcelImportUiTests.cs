using ClosedXML.Excel;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseExcelImportUiTests
{
    [Fact]
    public void Master_import_is_named_in_vietnamese_and_rendered_inside_the_tools_menu()
    {
        var v_strListSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));
        var v_strBaseSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Common", "Common", "FBase.razor"));

        Assert.DoesNotContain(">Import Excel</button>", v_strListSource);
        Assert.Contains("@Layout_Tool_Button(m_grdMaster, Can_Import_Selected_Master ? Master_Import_Tool_Item : null)", v_strListSource);
        Assert.Contains("m_strMaster_Type is \"SanPham\" or \"DonViTinh\" or \"LoaiSanPham\" or \"NCC\" or \"Kho\"", v_strListSource);
        Assert.Contains("@onclick=\"Open_Selected_Master_Import\"", v_strListSource);
        Assert.Contains("Nhập Excel", v_strListSource);
        Assert.Contains("RenderFragment? p_objAdditional_Tool_Item = null", v_strBaseSource);
        Assert.Contains("@p_objAdditional_Tool_Item", v_strBaseSource);

        foreach (var v_strComponent in new[]
        {
            "FWarehouse_Product_Import.razor",
            "FWarehouse_Unit_Import.razor",
            "FWarehouse_Category_Import.razor",
            "FWarehouse_Supplier_Import.razor",
            "FWarehouse_Kho_Import.razor"
        })
        {
            var v_strDialogSource = File.ReadAllText(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", v_strComponent));
            Assert.Contains("<h5 class=\"modal-title\">Nhập Excel -", v_strDialogSource);
            Assert.DoesNotContain("<h5 class=\"modal-title\">Import Excel -", v_strDialogSource);
        }
    }

    [Fact]
    public void Warehouse_import_action_is_scoped_to_Add_and_refreshes_once_after_success()
    {
        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("m_strMaster_Type != \"Kho\" || !r_objChuc_Nang.Is_Have_Add_Permission", v_strSource);
        Assert.Contains("<FWarehouse_Kho_Import", v_strSource);
        Assert.Contains("On_Warehouse_Import_Completed", v_strSource);
        Assert.Contains("\"Warehouse Excel Import\"", v_strSource);
    }

    [Fact]
    public void Official_template_has_one_sheet_exact_headers_and_no_data_rows()
    {
        var v_strPath = FindRepositoryPath("TKS_Thuc_Tap_V11_Web", "wwwroot", "FileManagement", "Template", "Kho_Import_Template.xlsx");
        using var v_objWorkbook = new XLWorkbook(v_strPath);
        Assert.Single(v_objWorkbook.Worksheets);
        var v_objSheet = v_objWorkbook.Worksheet("Kho");

        Assert.Equal("Ten_Kho", v_objSheet.Cell(1, 1).GetString());
        Assert.Equal("Ghi_Chu", v_objSheet.Cell(1, 2).GetString());
        Assert.Equal(1, v_objSheet.LastRowUsed()!.RowNumber());
    }

    [Fact]
    public void Warehouse_import_dialog_shows_template_preview_errors_and_confirm_gating()
    {
        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_Kho_Import.razor"));

        Assert.Contains("Kho_Import_Template.xlsx", v_strSource);
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
