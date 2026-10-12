using ClosedXML.Excel;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class SupplierExcelImportUiTests
{
    [Fact]
    public void Supplier_import_action_is_scoped_to_Add_and_refreshes_once_after_success()
    {
        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("m_strMaster_Type != \"NCC\" || !r_objChuc_Nang.Is_Have_Add_Permission", v_strSource);
        Assert.Contains("<FWarehouse_Supplier_Import", v_strSource);
        Assert.Contains("On_Supplier_Import_Completed", v_strSource);
        Assert.Contains("\"Supplier Excel Import\"", v_strSource);
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

    [Fact]
    public void Supplier_import_dialog_shows_template_preview_errors_and_confirm_gating()
    {
        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_Supplier_Import.razor"));

        Assert.Contains("Nha_Cung_Cap_Import_Template.xlsx", v_strSource);
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
