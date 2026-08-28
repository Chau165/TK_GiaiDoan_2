using System.Data;
using System.Text.RegularExpressions;
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
        Assert.Contains("m_grdDocument.Rebind()", source);
        Assert.Contains("Select_Document_Async(p_objData)", source);
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
    public void Warehouse_document_print_uses_a_dedicated_receipt_template()
    {
        var info = File.ReadAllText(FindWarehouseComponent("FWarehouse_2_Warehouse_Info.razor"));
        var css = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web", "wwwroot", "assets", "css", "tks.css"));

        Assert.Contains("warehouse-print-document", info);
        Assert.Contains("PHIẾU NHẬP KHO", info);
        Assert.Contains("PHIẾU XUẤT KHO", info);
        Assert.Contains("Mã hàng", info);
        Assert.Contains("Tổng số lượng", info);
        Assert.Contains("m_arrDetail.Sum(x => x.Tri_Gia)", info);
        Assert.Contains("@media print", css);
        Assert.Contains(".warehouse-print-document", css);
        Assert.Contains("body *", css);
    }

    [Fact]
    public void Warehouse_lists_use_server_side_ten_record_paging()
    {
        var list = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));
        var masterController = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseMaster_Controller.cs"));
        var documentController = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseDocument_Controller.cs"));
        var reportController = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseReport_Controller.cs"));
        var controllerBase = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouse_Controller_Base.cs"));
        var procedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));

        Assert.Contains("OnRead=\"Read_Master_Async\"", list);
        Assert.Contains("OnRead=\"Read_Document_Async\"", list);
        Assert.Contains("OnRead=\"Read_Report_Detail_Async\"", list);
        Assert.Contains("OnRead=\"Read_Inventory_Async\"", list);
        Assert.Contains("PageSize=\"10\"", list);
        Assert.Contains("List_Master_Page_Async", masterController);
        Assert.Contains("List_Lookup_Page_Async", masterController);
        Assert.Contains("List_Documents_Page_Async", documentController);
        Assert.Contains("Detail_Report_Page_Async", reportController);
        Assert.Contains("Inventory_Report_Page_Async", reportController);
        Assert.Contains("Page_From_Procedure", controllerBase);
        Assert.Contains("sp_DM_Master_Page", procedures);
        Assert.Contains("sp_XNK_Document_Page", procedures);
        Assert.Contains("sp_BC_Chi_Tiet_Nhap_Page", procedures);
        Assert.Contains("OFFSET (@Page_Number - 1) * @Page_Size ROWS", procedures);
    }

    [Fact]
    public void Warehouse_server_read_grids_do_not_mix_data_binding_with_on_read()
    {
        var list = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));
        var gridTags = Regex.Matches(list, "<TelerikGrid\\b[^>]*>", RegexOptions.Singleline);

        Assert.NotEmpty(gridTags);
        foreach (Match gridTag in gridTags)
        {
            if (gridTag.Value.Contains("OnRead=", StringComparison.Ordinal))
                Assert.DoesNotContain("Data=", gridTag.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Warehouse_inventory_page_returns_the_warehouse_name_for_the_grid_contract()
    {
        var procedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));

        Assert.Contains("JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=m.Kho_ID", procedures);
        Assert.Contains("k.Ten_Kho AS Ten_Kho", procedures);
        Assert.Contains("GROUP BY m.Kho_ID,m.San_Pham_ID,k.Ten_Kho", procedures);
        Assert.Contains("SELECT Kho_ID,Ten_Kho,San_Pham_ID", procedures);
    }

    [Fact]
    public void Warehouse_report_grid_is_recreated_when_the_report_type_changes()
    {
        var list = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("<option value=\"Receipt\">Chi tiết hàng nhập</option>", list);
        Assert.Contains("<option value=\"Issue\">Chi tiết hàng xuất</option>", list);
        Assert.Contains("@key=\"m_strReport_Type\"", list);
        Assert.Contains("Detail_Report_Page_Async(m_strReport_Type == \"Receipt\"", list);
    }

    [Fact]
    public void Warehouse_reports_filter_by_a_user_authorized_warehouse()
    {
        var list = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));
        var reportController = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseReport_Controller.cs"));
        var procedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));

        Assert.Contains("<label class=\"form-label\">Kho</label>", list);
        Assert.Contains("m_iReport_Warehouse_ID", list);
        Assert.Contains("<option value=\"\">Tất cả kho</option>", list);
        Assert.Contains("Detail_Report_Page_Async(m_strReport_Type == \"Receipt\", m_dtmFrom, m_dtmTo, args.Request.Page, args.Request.PageSize, r_strActive_User_Name, m_iReport_Warehouse_ID)", list);
        Assert.Contains("Inventory_Report_Page_Async(m_dtmFrom, m_dtmTo, args.Request.Page, args.Request.PageSize, r_strActive_User_Name, m_iReport_Warehouse_ID)", list);
        Assert.Contains("long? p_iWarehouse_ID = null", reportController);
        Assert.Contains("List_From_Procedure<CWarehouseDetailReport>(v_strProcedure, p_dtmFrom.Date, p_dtmTo.Date, p_strCurrent_Login, p_iWarehouse_ID)", reportController);
        Assert.Contains("Page_From_Procedure<CWarehouseDetailReport>(v_strProcedure, p_dtmFrom.Date, p_dtmTo.Date, p_iPage_Number, p_iPage_Size, p_strCurrent_Login, p_iWarehouse_ID)", reportController);
        Assert.Contains("List_From_Procedure<CWarehouseInventoryReport>(\"sp_BC_Xuat_Nhap_Ton\", p_dtmFrom.Date, p_dtmTo.Date, p_strCurrent_Login, p_iWarehouse_ID)", reportController);
        Assert.Contains("Page_From_Procedure<CWarehouseInventoryReport>(\"sp_BC_Xuat_Nhap_Ton_Page\", p_dtmFrom.Date, p_dtmTo.Date, p_iPage_Number, p_iPage_Size, p_strCurrent_Login, p_iWarehouse_ID)", reportController);

        foreach (var procedure in new[]
        {
            "sp_BC_Chi_Tiet_Nhap",
            "sp_BC_Chi_Tiet_Xuat",
            "sp_BC_Chi_Tiet_Nhap_Page",
            "sp_BC_Chi_Tiet_Xuat_Page",
            "sp_BC_Xuat_Nhap_Ton",
            "sp_BC_Xuat_Nhap_Ton_Page"
        })
        {
            var v_iProcedureStart = procedures.IndexOf($"CREATE OR ALTER PROCEDURE dbo.{procedure}", StringComparison.Ordinal);
            Assert.True(v_iProcedureStart >= 0, $"Missing procedure {procedure}.");
            var v_iProcedureEnd = procedures.IndexOf("\nGO", v_iProcedureStart, StringComparison.Ordinal);
            var v_strDefinition = procedures.Substring(v_iProcedureStart, v_iProcedureEnd - v_iProcedureStart);
            Assert.Contains("@Kho_ID BIGINT = NULL", v_strDefinition);
            Assert.Contains("sp_DM_Kho_User_Ensure_Access", v_strDefinition);
            Assert.Contains("@Kho_ID", v_strDefinition);
        }
    }

    [Fact]
    public void Warehouse_document_grids_filter_by_a_user_authorized_warehouse()
    {
        var list = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));
        var documentController = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseDocument_Controller.cs"));
        var procedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));

        Assert.Contains("m_iDocument_Warehouse_ID", list);
        Assert.Contains("@bind=\"m_iDocument_Warehouse_ID\"", list);
        Assert.Contains("List_Documents_Page_Async(Is_Receipt, args.Request.Page, args.Request.PageSize, \"\", r_strActive_User_Name, m_iDocument_Warehouse_ID)", list);
        Assert.Contains("long? p_iWarehouse_ID = null", documentController);
        Assert.Contains("p_iWarehouse_ID", documentController);

        foreach (var procedure in new[] { "sp_XNK_Document_List", "sp_XNK_Document_Page" })
        {
            var procedureStart = procedures.IndexOf($"CREATE OR ALTER PROCEDURE dbo.{procedure}", StringComparison.Ordinal);
            Assert.True(procedureStart >= 0, $"Missing procedure {procedure}.");
            var procedureEnd = procedures.IndexOf("\nGO", procedureStart, StringComparison.Ordinal);
            var definition = procedures.Substring(procedureStart, procedureEnd - procedureStart);
            Assert.Contains("@Kho_ID BIGINT = NULL", definition);
            Assert.Contains("sp_DM_Kho_User_Ensure_Access", definition);
            Assert.Contains("@Kho_ID IS NULL OR", definition);
        }
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
