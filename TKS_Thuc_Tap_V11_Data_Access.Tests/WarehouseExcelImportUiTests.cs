using ClosedXML.Excel;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseExcelImportUiTests
{
    [Fact]
    public void Master_routes_use_common_FBase_import_and_do_not_render_legacy_importers()
    {
        var v_arrRoutesAndComponents = new[]
        {
            ("Warehouse.razor", "F2009_1_Kho_List.razor", "<F2009_1_Kho_List />", "<FWarehouse_Kho_Import"),
            ("Warehouse_Loai_San_Pham.razor", "F2017_1_Loai_San_Pham_List.razor", "<F2017_1_Loai_San_Pham_List />", "<FWarehouse_Category_Import"),
            ("Warehouse_Nha_Cung_Cap.razor", "F2019_1_Nha_Cung_Cap_List.razor", "<F2019_1_Nha_Cung_Cap_List />", "<FWarehouse_Supplier_Import"),
            ("Warehouse_San_Pham.razor", "F2018_1_San_Pham_List.razor", "<F2018_1_San_Pham_List />", "<FWarehouse_Product_Import")
        };

        foreach (var (v_strRouteFile, v_strListFile, v_strListComponent, v_strLegacyImporter) in v_arrRoutesAndComponents)
        {
            var v_strRoute = File.ReadAllText(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", v_strRouteFile));
            var v_strList = File.ReadAllText(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", v_strListFile));

            Assert.Contains(v_strListComponent, v_strRoute);
            Assert.Contains("FCommon_Import_Excel", v_strList);
            Assert.Contains("protected override void Import_Excel_Entry", v_strList);
            Assert.DoesNotContain(v_strLegacyImporter, v_strList);
        }
    }

    [Fact]
    public void Warehouse_route_uses_FBase_import_instead_of_legacy_preview_confirm_import()
    {
        var v_strRoute = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Warehouse.razor"));
        var v_strList = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "F2009_1_Kho_List.razor"));

        Assert.Contains("<F2009_1_Kho_List />", v_strRoute);
        Assert.Contains("Is_Have_Add_Permission", v_strList);
        Assert.Contains("@Layout_Default_Filter(grdData, \"Kho_Import_Template.xlsx\")", v_strList);
        Assert.Contains("FCommon_Import_Excel", v_strList);
        Assert.Contains("protected override void Import_Excel_Entry", v_strList);
        Assert.DoesNotContain("<FWarehouse_Kho_Import", v_strList);
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
