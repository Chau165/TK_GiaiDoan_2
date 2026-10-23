using ClosedXML.Excel;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class SupplierExcelImportUiTests
{
    [Fact]
    public void Supplier_route_uses_FBase_import_instead_of_legacy_preview_confirm_import()
    {
        var v_strRoute = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Warehouse_Nha_Cung_Cap.razor"));
        var v_strList = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "F2019_1_Nha_Cung_Cap_List.razor"));

        Assert.Contains("<F2019_1_Nha_Cung_Cap_List />", v_strRoute);
        Assert.Contains("Is_Have_Add_Permission", v_strList);
        Assert.Contains("@Layout_Default_Filter(grdData, \"Nha_Cung_Cap_Import_Template.xlsx\")", v_strList);
        Assert.Contains("FCommon_Import_Excel", v_strList);
        Assert.Contains("protected override void Import_Excel_Entry", v_strList);
        Assert.DoesNotContain("<FWarehouse_Supplier_Import", v_strList);
    }

    [Fact]
    public void Official_template_has_one_sheet_exact_headers_and_text_code_column()
    {
        var v_strPath = FindRepositoryPath("TKS_Thuc_Tap_V11_Web", "wwwroot", "FileManagement", "Template", "Nha_Cung_Cap_Import_Template.xlsx");
        using var v_objWorkbook = new XLWorkbook(v_strPath);
        Assert.Single(v_objWorkbook.Worksheets);
        var v_objSheet = v_objWorkbook.Worksheet("NhaCungCap");

        Assert.Equal("Ma_NCC", v_objSheet.Cell(1, 1).GetString());
        Assert.Equal("Ten_NCC", v_objSheet.Cell(1, 2).GetString());
        Assert.Equal("Ghi_Chu", v_objSheet.Cell(1, 3).GetString());
        Assert.Equal(1, v_objSheet.LastRowUsed()!.RowNumber());
        Assert.Equal("@", v_objSheet.Column(1).Style.NumberFormat.Format);
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
