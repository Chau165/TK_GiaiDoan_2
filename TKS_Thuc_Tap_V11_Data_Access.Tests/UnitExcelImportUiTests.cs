using ClosedXML.Excel;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class UnitExcelImportUiTests
{
    [Fact]
    public void Unit_route_uses_the_FBase_company_import_flow()
    {
        var v_strRoute = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Warehouse_Don_Vi_Tinh.razor"));
        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_Unit_List.razor"));
        var v_strLegacyParent = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));
        var v_strBase = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Common", "Common", "FBase.razor"));

        Assert.Contains("<FWarehouse_Unit_List />", v_strRoute);
        Assert.DoesNotContain("FWarehouse_Unit_Import", v_strLegacyParent);
        Assert.Contains("@inherits FBase", v_strSource);
        Assert.Contains("if (r_objChuc_Nang.Is_Have_Add_Permission == true)", v_strBase);
        Assert.Contains("@Layout_Default_Filter(grdData, \"Don_Vi_Tinh_Import_Template.xlsx\")", v_strSource);
        Assert.DoesNotContain("Open_Unit_Import_Excel", v_strSource);
        Assert.DoesNotContain("@Layout_Tool_Button(grdData)", v_strSource);
        Assert.DoesNotContain("Unit_Import_Tool_Item", v_strSource);
        Assert.DoesNotContain("Danh sách đơn vị tính", v_strSource);
        Assert.Contains("<FCommon_Import_Excel", v_strSource);
        Assert.Contains("Localize=\"@Get_Language_Data_Field\"", v_strSource);
        Assert.Contains("OnClose=\"@Close_Import_Excel\"", v_strSource);
        Assert.Contains("protected override void Import_Excel_Entry", v_strSource);
        Assert.DoesNotContain("<FWarehouse_Unit_Import", v_strSource);
        Assert.Contains("Validate_Header_Matches(fileInfo, v_objTemplate_File)", v_strBase);

        var v_strCommonImport = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Common", "Common", "FCommon_Import_Excel.razor"));
        Assert.Contains("public Func<string, string> Localize { get; set; }", v_strCommonImport);
        Assert.Contains("@Localize(\"Download template\")", v_strCommonImport);
        Assert.Contains("Set_Error_MessageBox(Localize(ex.Message))", v_strCommonImport);
    }

    [Fact]
    public void Unit_import_maps_the_template_and_commits_each_row_with_cache_and_history_updates()
    {
        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_Unit_List.razor"));

        Assert.DoesNotContain("Read_Profiled_Import_Sheet", v_strSource);
        Assert.DoesNotContain("CImportLegacyWorksheetAdapter", v_strSource);
        Assert.DoesNotContain("CUnitImportValidator", v_strSource);
        Assert.Contains("Ten_Don_Vi_Tinh = CUtility.Convert_To_String(v_row[0]).Trim()", v_strSource);
        Assert.Contains("Ghi_Chu = CUtility.Convert_To_String(v_row[1]).Trim()", v_strSource);
        Assert.Contains("List_Range_Value_To_End", v_strSource);
        Assert.Contains("v_objCtrData.F2016_sp_ins_Don_Vi_Tinh", v_strSource);
        Assert.Contains("v_trans.Commit()", v_strSource);
        Assert.Contains("v_trans.Rollback()", v_strSource);
        Assert.Contains("CCache_Don_Vi_Tinh.Add_Data(v_objData)", v_strSource);
        Assert.Contains("FCommon_Insert_Action_History", v_strSource);
        Assert.DoesNotContain("PreviewAsync", v_strSource);
        Assert.DoesNotContain("ConfirmAsync", v_strSource);
    }

    [Fact]
    public void Unit_crud_uses_FBase_hooks_cache_clones_and_record_history()
    {
        var v_strList = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_Unit_List.razor"));
        var v_strEdit = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_Unit_Edit.razor"));
        var v_strInfo = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_Unit_Info.razor"));
        var v_strBase = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Common", "Common", "FBase.razor"));

        Assert.Contains("protected override void Add_Data()", v_strEdit);
        Assert.Contains("protected override void Update_Data()", v_strEdit);
        Assert.Contains("await Save_Data()", v_strEdit);
        Assert.Contains("r_objSource = v_objClone", v_strEdit);
        Assert.Contains("r_objDes = v_objClone", v_strEdit);
        Assert.Contains("CCache_Don_Vi_Tinh.Add_Data", v_strEdit);
        Assert.Contains("CCache_Don_Vi_Tinh.Update_Data", v_strEdit);
        Assert.Contains("Is_Have_Add_Permission", v_strEdit);
        Assert.Contains("Is_Have_Edit_Permission", v_strEdit);
        Assert.Contains("protected override void Delete_Data_Entry", v_strList);
        Assert.Contains("CCache_Don_Vi_Tinh.Delete_Data", v_strList);
        Assert.Contains("Is_Have_Add_Permission", v_strList);
        Assert.Contains("Is_Have_Edit_Permission", v_strList);
        Assert.Contains("Is_Have_Delete_Permission", v_strList);
        Assert.Contains("return Delete_Data(p_iAuto_ID)", v_strList);
        Assert.Contains("InvokeAsync<bool>(\"confirm\"", v_strBase);
        Assert.Contains("Process_Queue(\"Delete Data\")", v_strBase);
        Assert.Contains("End_Queue(v_iQueue_ID)", v_strBase);
        Assert.Contains("FCommon_Record_Action_History_List", v_strInfo);
    }

    [Fact]
    public void Unit_grid_uses_the_FBase_export_dispatcher_and_gates_export_permission()
    {
        var v_strList = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_Unit_List.razor"));
        var v_strBase = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Common", "Common", "FBase.razor"));

        Assert.Contains("<TelerikGrid Data=\"@v_arrData\"", v_strList);
        Assert.DoesNotContain("OnRead=", v_strList);
        Assert.Contains("@Layout_Default_Filter(grdData, \"Don_Vi_Tinh_Import_Template.xlsx\")", v_strList);
        Assert.DoesNotContain("@Layout_Tool_Button(grdData", v_strList);
        Assert.Contains("<GridExcelExport FileName=\"Don_Vi_Tinh_Export\" AllPages=\"true\" OnBeforeExport=\"@Prepare_Excel_Export\" />", v_strList);
        Assert.Contains("<GridCsvExport FileName=\"Don_Vi_Tinh_Export\" AllPages=\"true\" />", v_strList);
        Assert.Contains("if (p_grdGrid.OnRead.HasDelegate)", v_strBase);
        Assert.Contains("await Export_Excel_Telerik_Default(p_grdGrid)", v_strBase);
        Assert.Contains("await Export_Excel_Custom(p_grdGrid)", v_strBase);
        Assert.Contains("Is_Have_Export_Permission == true", v_strBase);
    }

    [Fact]
    public void Official_template_has_one_sheet_and_exact_unit_headers()
    {
        var v_strPath = FindRepositoryPath("TKS_Thuc_Tap_V11_Web", "wwwroot", "FileManagement", "Template", "Don_Vi_Tinh_Import_Template.xlsx");
        using var v_objWorkbook = new XLWorkbook(v_strPath);
        Assert.Single(v_objWorkbook.Worksheets);
        Assert.Equal(new[] { "Ten_Don_Vi_Tinh", "Ghi_Chu" }, v_objWorkbook.Worksheet("DonViTinh").Row(1).Cells(1, 2).Select(v_objCell => v_objCell.GetString()));
        Assert.True(v_objWorkbook.Worksheet("DonViTinh").Column(1).Width > 12);
    }

    [Fact]
    public void Legacy_unit_preview_component_is_not_rendered_by_the_migrated_route()
    {
        var v_strRoute = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Warehouse_Don_Vi_Tinh.razor"));
        Assert.Contains("<FWarehouse_Unit_List />", v_strRoute);
        Assert.DoesNotContain("FWarehouse_Unit_Import", v_strRoute);
        string v_strActiveList = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_Unit_List.razor"));
        Assert.Contains("<FCommon_Import_Excel", v_strActiveList);
        Assert.DoesNotContain("FWarehouse_Import_Preview_Shell", v_strActiveList);
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
