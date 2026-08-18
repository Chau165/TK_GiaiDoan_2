using System.Data;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseUiWorkflowTests
{
    [Fact]
    public void Warehouse_page_only_renders_editors_when_the_user_starts_an_editing_action()
    {
        var source = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));
        var infoSource = File.ReadAllText(FindWarehouseComponent("FWarehouse_2_Warehouse_Info.razor"));

        Assert.Contains("private bool m_bDocumentEditing;", source);
        Assert.Contains("private bool m_bDetailEditing;", source);
        Assert.Contains("@if (r_bIs_Show_Edit)", source);
        Assert.Contains("@if (r_bIs_Show_Info)", source);
        Assert.Contains("@if (m_bDocumentEditing)", source);
        Assert.Contains("@if (m_bDetailEditing)", source);
        Assert.Contains("Thêm sản phẩm", infoSource);
        Assert.Contains("var v_objSaved =", source);
        Assert.Contains("Select_Document_Async(v_objSaved)", source);
        Assert.Contains("p_objData.Document_ID = m_objSelectedDocument.Auto_ID;", source);
    }

    [Fact]
    public void Warehouse_page_uses_the_company_list_info_edit_component_pattern()
    {
        var page = File.ReadAllText(FindWarehousePage());
        var list = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("<FWarehouse_1_Warehouse_List />", page);
        Assert.Contains("@inherits FBase", list);
        Assert.Contains("FWarehouse_2_Warehouse_Info", list);
        Assert.Contains("FWarehouse_3_Warehouse_Edit", list);
        Assert.Contains("<TelerikGrid", list);
        Assert.Contains("Is_Have_Add_Permission", list);
        Assert.Contains("Is_Have_Edit_Permission", list);
        Assert.Contains("Is_Have_Delete_Permission", list);
        Assert.Contains("Is_Have_Export_Permission", list);
    }

    [Fact]
    public void Warehouse_master_actions_use_the_common_info_edit_modal_lifecycle()
    {
        var list = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("r_bIs_Show_Info", list);
        Assert.Contains("r_bIs_Show_Edit", list);
        Assert.Contains("<FWarehouse_2_Warehouse_Master_Info", list);
        Assert.Contains("<FWarehouse_3_Warehouse_Master_Edit", list);
        Assert.Contains("r_bIs_Show_Info = true", list);
        Assert.Contains("r_bIs_Show_Edit = true", list);
        Assert.Contains("data-bs-toggle=\"dropdown\" aria-expanded=\"false\"", list);
        Assert.Contains("Open_Master_Info((context as CWarehouseMaster))", list);
        Assert.Contains("Edit_Master((context as CWarehouseMaster))", list);
        Assert.Contains("Delete_Master_Async((context as CWarehouseMaster).Auto_ID)", list);
        Assert.DoesNotContain("Toggle_Master_Actions", list);
        Assert.DoesNotContain("m_lngMasterAction_ID", list);
    }

    [Fact]
    public void Warehouse_master_action_column_uses_common_grid_formatting_for_dropdown_visibility()
    {
        var list = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("Format_Grid(m_grdMaster);", list);
    }

    [Fact]
    public void Warehouse_master_grid_is_reformatted_when_returning_to_master_section()
    {
        var list = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));
        var baseComponent = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Common", "Common", "FBase.razor"));

        Assert.Contains("protected override Task After_Render_Async(bool firstRender)", list);
        Assert.Contains("m_objFormattedMasterGrid", list);
        Assert.Contains("await After_Render_Async(firstRender);", baseComponent);
    }

    [Fact]
    public void Warehouse_data_access_uses_stored_procedures_and_audit_arguments()
    {
        var controllerDirectory = FindRepositoryDirectory("TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse");
        var master = File.ReadAllText(Path.Combine(controllerDirectory, "CWarehouseMaster_Controller.cs"));
        var document = File.ReadAllText(Path.Combine(controllerDirectory, "CWarehouseDocument_Controller.cs"));
        var report = File.ReadAllText(Path.Combine(controllerDirectory, "CWarehouseReport_Controller.cs"));
        var baseController = File.ReadAllText(Path.Combine(controllerDirectory, "CWarehouse_Controller_Base.cs"));
        var source = master + document + report + baseController;

        Assert.DoesNotContain("SELECT ", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CSqlHelper.FillDataTable", source);
        Assert.Contains("Last_Updated_By", source);
        Assert.Contains("Last_Updated_By_Function", source);
        Assert.Contains("sp_DM_Master_List", source);
        Assert.Contains("sp_XNK_Document_List", source);
        Assert.Contains("sp_XNK_Document_Detail_List", source);
    }

    [Fact]
    public void Warehouse_database_contract_contains_full_audit_columns()
    {
        var schema = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Schema.sql"));
        var procedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));

        foreach (var field in new[] { "Created_By", "Created_By_Function", "Last_Updated_By", "Last_Updated_By_Function" })
        {
            Assert.Contains(field, schema);
            Assert.Contains(field, procedures);
        }
    }

    [Fact]
    public void Warehouse_report_mapping_converts_double_columns_to_decimal_properties()
    {
        var v_dtData = new DataTable();
        v_dtData.Columns.Add("So_Luong", typeof(double));
        v_dtData.Columns.Add("Don_Gia", typeof(double));
        v_dtData.Columns.Add("Tri_Gia", typeof(double));
        var v_row = v_dtData.Rows.Add(1.5d, 12.25d, 18.375d);

        var v_objReport = CUtility.Map_Row_To_Entity<CWarehouseDetailReport>(v_row);

        Assert.Equal(1.5m, v_objReport.So_Luong);
        Assert.Equal(12.25m, v_objReport.Don_Gia);
        Assert.Equal(18.375m, v_objReport.Tri_Gia);
    }

    private static string FindWarehousePage()
    {
        return FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Warehouse.razor");
    }

    private static string FindWarehouseComponent(string fileName)
    {
        return FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", fileName);
    }

    private static string FindRepositoryDirectory(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException($"Repository directory was not found: {Path.Combine(parts)}");
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
