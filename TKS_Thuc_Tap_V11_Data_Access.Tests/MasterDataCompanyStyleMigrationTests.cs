using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class MasterDataCompanyStyleMigrationTests
{
    [Fact]
    public void Legacy_master_import_modals_are_not_exposed_by_the_report_and_permission_host()
    {
        var v_strWarehouseHost = ReadComponentFile("FWarehouse_1_Warehouse_List.razor");

        Assert.DoesNotContain("Master_Import_Tool_Item", v_strWarehouseHost);
        Assert.DoesNotContain("<FWarehouse_Product_Import", v_strWarehouseHost);
        Assert.DoesNotContain("<FWarehouse_Category_Import", v_strWarehouseHost);
        Assert.DoesNotContain("<FWarehouse_Supplier_Import", v_strWarehouseHost);
        Assert.DoesNotContain("<FWarehouse_Kho_Import", v_strWarehouseHost);
    }

    [Fact]
    public void All_master_info_components_render_the_created_timestamp_with_localized_label()
    {
        string[] v_arrInfoFiles =
        [
            "F2009_2_Kho_Info.razor",
            "FWarehouse_Unit_Info.razor",
            "F2017_2_Loai_San_Pham_Info.razor",
            "F2018_2_San_Pham_Info.razor",
            "F2019_2_Nha_Cung_Cap_Info.razor"
        ];

        foreach (string v_strInfoFile in v_arrInfoFiles)
        {
            string v_strInfo = ReadComponentFile(v_strInfoFile);
            Assert.Contains("Get_Language_Data_Field(\"Ngày giờ tạo\")", v_strInfo);
            Assert.Contains("CUtility.Get_Label_Text(m_objData.Created, false)", v_strInfo);
            Assert.Equal(1, CountOccurrences(v_strInfo, "m_objData.Created,"));
        }
    }

    [Fact]
    public void Warehouse_uses_company_style_components_and_active_FBase_import()
    {
        var v_strRoute = ReadModuleFile("Warehouse.razor");
        var v_strList = ReadComponentFile("F2009_1_Kho_List.razor");
        var v_strInfo = ReadComponentFile("F2009_2_Kho_Info.razor");
        var v_strEdit = ReadComponentFile("F2009_3_Kho_Edit.razor");

        Assert.Contains("<F2009_1_Kho_List />", v_strRoute);
        Assert.DoesNotContain("FWarehouse_1_Warehouse_List", v_strRoute);

        Assert.Contains("@inherits FBase", v_strList);
        Assert.Contains("@Layout_Default_Filter(grdData, \"Kho_Import_Template.xlsx\")", v_strList);
        Assert.DoesNotContain("Layout_Tool_Button(grdData)", v_strList);
        Assert.Contains("FCommon_Import_Excel", v_strList);
        Assert.DoesNotContain("Open_Kho_Import_Excel", v_strList);
        Assert.Contains("protected override void Import_Excel_Entry(CExcel_Controller", v_strList);
        Assert.DoesNotContain("Read_Profiled_Import_Sheet", v_strList);
        Assert.DoesNotContain("CImportLegacyWorksheetAdapter", v_strList);
        Assert.DoesNotContain("CWarehouse_Xlsx_Reader", v_strList);
        Assert.DoesNotContain("Row_Number - 2", v_strList);
        Assert.Contains("foreach (DataRow v_row", v_strList);
        Assert.DoesNotContain("CWarehouseImportValidator.Revalidate", v_strList);
        Assert.Contains("F2009_sp_ins_Kho", v_strList);
        Assert.Contains("v_trans.Commit()", v_strList);
        Assert.Contains("CCache_Kho.Add_Data", v_strList);
        Assert.DoesNotContain("<FWarehouse_Kho_Import", v_strList);
        Assert.Contains("<GridExcelExport", v_strList);
        Assert.Contains("<GridCsvExport", v_strList);

        Assert.Contains("@inherits FBase", v_strInfo);
        Assert.Contains("Get_Language_Data_Field", v_strInfo);
        Assert.Contains("FCommon_Sys_sp_sel_List_Log_Record_Action_History(r_iAuto_ID)", v_strInfo);
        Assert.Contains("FCommon_Record_Action_History_List v_arrData=\"@m_arrRAH\"", v_strInfo);

        Assert.Contains("@inherits FBase", v_strEdit);
        Assert.Contains("<EditForm", v_strEdit);
        Assert.Contains("private void Check_Data()", v_strEdit);
        Assert.Contains("protected override void Add_Data()", v_strEdit);
        Assert.Contains("protected override void Update_Data()", v_strEdit);
        Assert.Contains("await Save_Data()", v_strEdit);
        Assert.Contains("CUtility.Clone_Entity", v_strEdit);
        Assert.Contains("r_objSource", v_strEdit);
        Assert.Contains("r_objDes", v_strEdit);
        Assert.Contains("CCache_Kho", v_strEdit);
    }

    [Fact]
    public void Supplier_uses_company_style_components_and_active_FBase_import()
    {
        var v_strRoute = ReadModuleFile("Warehouse_Nha_Cung_Cap.razor");
        var v_strList = ReadComponentFile("F2019_1_Nha_Cung_Cap_List.razor");
        var v_strInfo = ReadComponentFile("F2019_2_Nha_Cung_Cap_Info.razor");
        var v_strEdit = ReadComponentFile("F2019_3_Nha_Cung_Cap_Edit.razor");

        Assert.Contains("<F2019_1_Nha_Cung_Cap_List />", v_strRoute);
        Assert.DoesNotContain("FWarehouse_1_Warehouse_List", v_strRoute);

        Assert.Contains("@inherits FBase", v_strList);
        Assert.Contains("@Layout_Default_Filter(grdData, \"Nha_Cung_Cap_Import_Template.xlsx\")", v_strList);
        Assert.DoesNotContain("Layout_Tool_Button(grdData)", v_strList);
        Assert.Contains("FCommon_Import_Excel", v_strList);
        Assert.DoesNotContain("Open_Supplier_Import_Excel", v_strList);
        Assert.Contains("protected override void Import_Excel_Entry(CExcel_Controller", v_strList);
        Assert.DoesNotContain("Read_Profiled_Import_Sheet", v_strList);
        Assert.DoesNotContain("CImportLegacyWorksheetAdapter", v_strList);
        Assert.DoesNotContain("CSupplier_Xlsx_Reader", v_strList);
        Assert.DoesNotContain("Row_Number - 2", v_strList);
        Assert.Contains("foreach (DataRow v_row", v_strList);
        Assert.DoesNotContain("CSupplierImportValidator.Revalidate", v_strList);
        Assert.Contains("F2019_sp_ins_NCC", v_strList);
        Assert.Contains("v_trans.Commit()", v_strList);
        Assert.Contains("CCache_NCC.Add_Data", v_strList);
        Assert.DoesNotContain("<FWarehouse_Supplier_Import", v_strList);
        Assert.Contains("<GridExcelExport", v_strList);
        Assert.Contains("<GridCsvExport", v_strList);

        Assert.Contains("@inherits FBase", v_strInfo);
        Assert.Contains("Get_Language_Data_Field", v_strInfo);
        Assert.Contains("FCommon_Sys_sp_sel_List_Log_Record_Action_History(r_iAuto_ID)", v_strInfo);
        Assert.Contains("FCommon_Record_Action_History_List v_arrData=\"@m_arrRAH\"", v_strInfo);

        Assert.Contains("@inherits FBase", v_strEdit);
        Assert.Contains("<EditForm", v_strEdit);
        Assert.Contains("private void Check_Data()", v_strEdit);
        Assert.Contains("protected override void Add_Data()", v_strEdit);
        Assert.Contains("protected override void Update_Data()", v_strEdit);
        Assert.Contains("await Save_Data()", v_strEdit);
        Assert.Contains("CUtility.Clone_Entity", v_strEdit);
        Assert.Contains("r_objSource", v_strEdit);
        Assert.Contains("r_objDes", v_strEdit);
        Assert.Contains("CCache_NCC", v_strEdit);
    }

    [Fact]
    public void Category_uses_company_style_components_and_active_FBase_import()
    {
        var v_strRoute = ReadModuleFile("Warehouse_Loai_San_Pham.razor");
        var v_strList = ReadComponentFile("F2017_1_Loai_San_Pham_List.razor");
        var v_strInfo = ReadComponentFile("F2017_2_Loai_San_Pham_Info.razor");
        var v_strEdit = ReadComponentFile("F2017_3_Loai_San_Pham_Edit.razor");

        Assert.Contains("<F2017_1_Loai_San_Pham_List />", v_strRoute);
        Assert.DoesNotContain("FWarehouse_1_Warehouse_List", v_strRoute);

        Assert.Contains("@inherits FBase", v_strList);
        Assert.Contains("@Layout_Default_Filter(grdData, \"Loai_San_Pham_Import_Template.xlsx\")", v_strList);
        Assert.DoesNotContain("Layout_Tool_Button(grdData)", v_strList);
        Assert.Contains("FCommon_Import_Excel", v_strList);
        Assert.DoesNotContain("Open_Category_Import_Excel", v_strList);
        Assert.Contains("protected override void Import_Excel_Entry(CExcel_Controller", v_strList);
        Assert.DoesNotContain("Read_Profiled_Import_Sheet", v_strList);
        Assert.DoesNotContain("CImportLegacyWorksheetAdapter", v_strList);
        Assert.DoesNotContain("CCategory_Xlsx_Reader", v_strList);
        Assert.DoesNotContain("Row_Number - 2", v_strList);
        Assert.Contains("foreach (DataRow v_row", v_strList);
        Assert.DoesNotContain("CCategoryImportValidator.Revalidate", v_strList);
        Assert.Contains("F2017_sp_ins_Loai_San_Pham", v_strList);
        Assert.Contains("v_trans.Commit()", v_strList);
        Assert.Contains("CCache_Loai_San_Pham.Add_Data", v_strList);
        Assert.DoesNotContain("<FWarehouse_Category_Import", v_strList);
        Assert.Contains("<GridExcelExport", v_strList);
        Assert.Contains("<GridCsvExport", v_strList);

        Assert.Contains("@inherits FBase", v_strInfo);
        Assert.Contains("Get_Language_Data_Field", v_strInfo);
        Assert.Contains("FCommon_Sys_sp_sel_List_Log_Record_Action_History(r_iAuto_ID)", v_strInfo);
        Assert.Contains("FCommon_Record_Action_History_List v_arrData=\"@m_arrRAH\"", v_strInfo);

        Assert.Contains("@inherits FBase", v_strEdit);
        Assert.Contains("<EditForm", v_strEdit);
        Assert.Contains("private void Check_Data()", v_strEdit);
        Assert.Contains("protected override void Add_Data()", v_strEdit);
        Assert.Contains("protected override void Update_Data()", v_strEdit);
        Assert.Contains("await Save_Data()", v_strEdit);
        Assert.Contains("CUtility.Clone_Entity", v_strEdit);
        Assert.Contains("r_objSource", v_strEdit);
        Assert.Contains("r_objDes", v_strEdit);
        Assert.Contains("CCache_Loai_San_Pham", v_strEdit);
    }

    [Fact]
    public void Product_uses_company_style_components_and_preserves_lookup_import_validation()
    {
        var v_strRoute = ReadModuleFile("Warehouse_San_Pham.razor");
        var v_strList = ReadComponentFile("F2018_1_San_Pham_List.razor");
        var v_strInfo = ReadComponentFile("F2018_2_San_Pham_Info.razor");
        var v_strEdit = ReadComponentFile("F2018_3_San_Pham_Edit.razor");

        Assert.Contains("<F2018_1_San_Pham_List />", v_strRoute);
        Assert.DoesNotContain("FWarehouse_1_Warehouse_List", v_strRoute);

        Assert.Contains("@inherits FBase", v_strList);
        Assert.Contains("@Layout_Default_Filter(grdData, \"San_Pham_Import_Template.xlsx\")", v_strList);
        Assert.DoesNotContain("Layout_Tool_Button(grdData)", v_strList);
        Assert.Contains("FCommon_Import_Excel", v_strList);
        Assert.DoesNotContain("Open_Product_Import_Excel", v_strList);
        Assert.Contains("protected override void Import_Excel_Entry(CExcel_Controller", v_strList);
        Assert.DoesNotContain("Read_Profiled_Import_Sheet", v_strList);
        Assert.DoesNotContain("CImportLegacyWorksheetAdapter", v_strList);
        Assert.Contains("List_Range_Value_To_End(0, \"A2\", \"E\")", v_strList);
        Assert.DoesNotContain("CExcel_Import_DataTable_Adapter", v_strList);
        Assert.DoesNotContain("CProduct_Xlsx_Reader", v_strList);
        Assert.DoesNotContain("Row_Number - 2", v_strList);
        Assert.Contains("foreach (DataRow v_row", v_strList);
        Assert.Contains("F2018_sp_ins_San_Pham", v_strList);
        Assert.Contains("v_trans.Commit()", v_strList);
        Assert.Contains("CCache_San_Pham.Add_Data", v_strList);
        Assert.DoesNotContain("<FWarehouse_Product_Import", v_strList);
        Assert.Contains("<GridExcelExport", v_strList);
        Assert.Contains("<GridCsvExport", v_strList);
        Assert.Contains("F2017_sp_sel_List_Loai_San_Pham_Lookup", v_strList);
        Assert.Contains("F2016_sp_sel_List_Don_Vi_Tinh_Lookup", v_strList);
        Assert.Contains("Code = v_objItem.Ma_San_Pham", v_strList);
        Assert.Contains("Related_ID = v_objItem.Loai_San_Pham_ID", v_strList);

        Assert.Contains("@inherits FBase", v_strInfo);
        Assert.Contains("Get_Language_Data_Field", v_strInfo);
        Assert.Contains("FCommon_Sys_sp_sel_List_Log_Record_Action_History(r_iAuto_ID)", v_strInfo);
        Assert.Contains("FCommon_Record_Action_History_List v_arrData=\"@m_arrRAH\"", v_strInfo);

        Assert.Contains("@inherits FBase", v_strEdit);
        Assert.Contains("<EditForm", v_strEdit);
        Assert.Contains("private void Check_Data()", v_strEdit);
        Assert.Contains("protected override void Add_Data()", v_strEdit);
        Assert.Contains("protected override void Update_Data()", v_strEdit);
        Assert.Contains("await Save_Data()", v_strEdit);
        Assert.Contains("CUtility.Clone_Entity", v_strEdit);
        Assert.Contains("r_objSource", v_strEdit);
        Assert.Contains("r_objDes", v_strEdit);
        Assert.Contains("CCache_San_Pham", v_strEdit);
    }

    [Theory]
    [InlineData("F2009_1_Kho_List.razor", "B")]
    [InlineData("FWarehouse_Unit_List.razor", "B")]
    [InlineData("F2017_1_Loai_San_Pham_List.razor", "C")]
    [InlineData("F2018_1_San_Pham_List.razor", "E")]
    [InlineData("F2019_1_Nha_Cung_Cap_List.razor", "C")]
    public void Master_data_import_maps_entities_from_the_company_positional_data_rows(
        string p_strComponentFile,
        string p_strLastColumn)
    {
        var v_strList = ReadComponentFile(p_strComponentFile);

        Assert.Contains($"List_Range_Value_To_End(0, \"A2\", \"{p_strLastColumn}\")", v_strList);
        Assert.DoesNotContain("CExcel_Import_DataTable_Adapter", v_strList);
        Assert.Contains("CUtility.Convert_To_String(v_row[", v_strList);
    }

    private static string ReadModuleFile(string p_strFileName) => File.ReadAllText(FindRepositoryPath(
        "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", p_strFileName));

    private static string ReadComponentFile(string p_strFileName) => File.ReadAllText(FindRepositoryPath(
        "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", p_strFileName));

    private static int CountOccurrences(string p_strSource, string p_strValue) =>
        p_strSource.Split(p_strValue, StringSplitOptions.None).Length - 1;

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
