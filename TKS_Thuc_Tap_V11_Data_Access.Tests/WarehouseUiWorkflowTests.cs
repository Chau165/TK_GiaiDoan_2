using System.Data;
using System.Text.RegularExpressions;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Telerik.DataSource;
using TKS_Thuc_Tap_V11_Web_Common.Common;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseUiWorkflowTests
{
    [Fact]
    public void Warehouse_document_product_dropdown_shows_master_unit_next_to_name()
    {
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_Transaction_List.razor"));
        var v_Edit = File.ReadAllText(FindWarehouseComponent("FWarehouse_3_Warehouse_Edit.razor"));

        Assert.Contains("Don_Vi_Tinh_ID", v_List);
        Assert.Contains("v_dicUnitNamesByID.TryGetValue", v_List);
        Assert.Contains("string.IsNullOrWhiteSpace(v_strUnitName)", v_List);
        Assert.Contains("({v_strUnitName})", v_List);
        Assert.Contains("TextField=\"@nameof(CWarehouseLookup.Name)\"", v_Edit);
        Assert.Contains("F2018_sp_sel_List_San_Pham()", v_List);
        Assert.DoesNotContain("F2018_sp_sel_List_San_Pham_Lookup()", v_List);
        Assert.Contains(
            "SELECT Auto_ID, Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu FROM dbo.tbl_DM_San_Pham",
            File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql")));
    }

    [Fact]
    public void Warehouse_page_only_renders_editors_when_the_user_starts_an_editing_action()
    {
        var v_Source = File.ReadAllText(FindWarehouseComponent("FWarehouse_Transaction_List.razor"));
        var v_InfoSource = File.ReadAllText(FindWarehouseComponent("FWarehouse_2_Warehouse_Info.razor"));

        Assert.Contains("@if (m_bDocumentEditing)", v_Source);
        Assert.Contains("@if (m_bDetailEditing)", v_Source);
        Assert.Contains("if (m_objDocument != null)", v_InfoSource);
        Assert.Contains("Thêm sản phẩm", v_InfoSource);
        Assert.Contains("m_grdDocument.Rebind()", v_Source);
        Assert.Contains("Select_Document_Async(p_objData)", v_Source);
        Assert.Contains("p_objData.Document_ID = m_objSelectedDocument.Auto_ID;", v_Source);
    }

    [Fact]
    public void Warehouse_page_routes_to_dedicated_company_style_master_components()
    {
        var v_Page = File.ReadAllText(FindWarehousePage());
        var v_List = File.ReadAllText(FindWarehouseComponent("F2009_1_Kho_List.razor"));

        Assert.Contains("<F2009_1_Kho_List />", v_Page);
        Assert.DoesNotContain("FWarehouse_1_Warehouse_List", v_Page);
        Assert.Contains("@inherits FBase", v_List);
        Assert.Contains("F2009_2_Kho_Info", v_List);
        Assert.Contains("F2009_3_Kho_Edit", v_List);
        Assert.Contains("<TelerikGrid", v_List);
        Assert.Contains("Is_Have_Add_Permission", v_List);
        Assert.Contains("Is_Have_Edit_Permission", v_List);
        Assert.Contains("Is_Have_Delete_Permission", v_List);
        Assert.Contains("@Layout_Default_Filter(grdData, \"Kho_Import_Template.xlsx\")", v_List);
    }

    [Fact]
    public void Warehouse_master_actions_use_the_common_info_edit_modal_lifecycle()
    {
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("r_bIs_Show_Info", v_List);
        Assert.Contains("r_bIs_Show_Edit", v_List);
        Assert.Contains("<FWarehouse_2_Warehouse_Master_Info", v_List);
        Assert.Contains("<FWarehouse_3_Warehouse_Master_Edit", v_List);
        Assert.Contains("r_bIs_Show_Info = true", v_List);
        Assert.Contains("r_bIs_Show_Edit = true", v_List);
        Assert.Contains("data-bs-toggle=\"dropdown\" aria-expanded=\"false\"", v_List);
        Assert.Contains("Open_Master_Info((context as CWarehouseMaster))", v_List);
        Assert.Contains("Edit_Master((context as CWarehouseMaster))", v_List);
        Assert.Contains("Delete_Master_Async((context as CWarehouseMaster).Auto_ID)", v_List);
        Assert.DoesNotContain("Toggle_Master_Actions", v_List);
        Assert.DoesNotContain("m_lngMasterAction_ID", v_List);
    }

    [Fact]
    public void Warehouse_master_forms_follow_the_localized_owner_layout()
    {
        var v_Edit = File.ReadAllText(FindWarehouseComponent("FWarehouse_3_Warehouse_Edit.razor"));
        var v_Info = File.ReadAllText(FindWarehouseComponent("FWarehouse_2_Warehouse_Master_Info.razor"));

        Assert.Contains("m_strEditor_Type == \"Master\" ? \"\" : \"card border mt-3\"", v_Edit);
        Assert.Contains("@Get_Language_Data_Field(\"Tên\")", v_Edit);
        Assert.Contains("col-lg-6", v_Edit);
        Assert.Contains("Get_Language_Data_Field(\"Thông tin\")", v_Info);
        Assert.Contains("Get_Language_Data_Field(\"Lịch sử xử lý\")", v_Info);
        Assert.Contains("col-lg-4", v_Info);
    }

    [Fact]
    public void Warehouse_master_crud_uses_queue_loading_and_protected_delete_confirmation()
    {
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));
        var v_iSaveStart = v_List.IndexOf("private async Task Save_Master_Operation_Async", StringComparison.Ordinal);
        var v_iDeleteStart = v_List.IndexOf("private async Task Delete_Master_Async", StringComparison.Ordinal);
        var v_iNextDelete = v_List.IndexOf("private async Task Delete_Kho_User_Async", v_iDeleteStart, StringComparison.Ordinal);

        Assert.True(v_iSaveStart >= 0 && v_iDeleteStart > v_iSaveStart && v_iNextDelete > v_iDeleteStart);
        var v_SaveMethod = v_List[v_iSaveStart..v_iDeleteStart];
        var v_DeleteMethod = v_List[v_iDeleteStart..v_iNextDelete];
        Assert.Contains("r_objLayout.Start_Loading()", v_SaveMethod);
        Assert.Contains("Process_Queue", v_SaveMethod);
        Assert.Contains("End_Queue", v_SaveMethod);
        Assert.Contains("Is_Have_Delete_Permission", v_DeleteMethod);
        Assert.Contains("confirm", v_DeleteMethod);
    }

    [Fact]
    public void Warehouse_master_import_refreshes_lookup_projections_and_grid_after_commit()
    {
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));
        var v_ImportCallbacks = new[]
        {
            "On_Product_Import_Completed",
            "On_Category_Import_Completed",
            "On_Supplier_Import_Completed",
            "On_Warehouse_Import_Completed"
        };

        foreach (var v_strCallback in v_ImportCallbacks)
        {
            Assert.Matches(
                $"private Task {v_strCallback}\\(int p_iRowCount\\)\\s*=>\\s*Complete_Master_Import_Async",
                v_List);
        }

        var v_iRefreshStart = v_List.IndexOf("private async Task Complete_Master_Import_Async", StringComparison.Ordinal);
        var v_iNextMethod = v_List.IndexOf("\n    private ", v_iRefreshStart + 1, StringComparison.Ordinal);
        Assert.True(v_iRefreshStart >= 0 && v_iNextMethod > v_iRefreshStart);
        var v_RefreshMethod = v_List[v_iRefreshStart..v_iNextMethod];

        Assert.Contains("await Load_Lookups_Async();", v_RefreshMethod);
        Assert.Contains("m_grdMaster.Rebind();", v_RefreshMethod);
        Assert.Contains("r_objLayout.Start_Loading();", v_RefreshMethod);
        Assert.Contains("r_objLayout.Stop_Loading();", v_RefreshMethod);
        Assert.Contains("Set_Error(new InvalidOperationException(", v_RefreshMethod);
        Assert.Contains("string.Format(Get_Language_Data_Field(\"Đã nhập thành công {0} dòng, nhưng không thể làm mới lookup", v_RefreshMethod);
    }

    [Fact]
    public void Warehouse_master_action_column_uses_common_grid_formatting_for_dropdown_visibility()
    {
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("Format_Grid(m_grdMaster);", v_List);
    }

    [Fact]
    public void Warehouse_master_grid_is_reformatted_when_returning_to_master_section()
    {
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));
        var v_BaseComponent = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Common", "Common", "FBase.razor"));

        Assert.Contains("protected override Task After_Render_Async(bool firstRender)", v_List);
        Assert.Contains("m_objFormattedMasterGrid", v_List);
        Assert.Contains("await After_Render_Async(firstRender);", v_BaseComponent);
    }

    [Fact]
    public void Warehouse_document_print_uses_a_dedicated_receipt_template()
    {
        var v_Info = File.ReadAllText(FindWarehouseComponent("FWarehouse_2_Warehouse_Info.razor"));
        var v_Css = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web", "wwwroot", "assets", "css", "tks.css"));

        Assert.Contains("warehouse-print-document", v_Info);
        Assert.Contains("PHIẾU NHẬP KHO", v_Info);
        Assert.Contains("PHIẾU XUẤT KHO", v_Info);
        Assert.Contains("Mã hàng", v_Info);
        Assert.Contains("Tổng số lượng", v_Info);
        Assert.Contains("m_arrDetail.Sum(x => x.Tri_Gia)", v_Info);
        Assert.Contains("@media print", v_Css);
        Assert.Contains(".warehouse-print-document", v_Css);
        Assert.Contains("body *", v_Css);
    }

    [Fact]
    public void Warehouse_lists_use_server_side_ten_record_paging()
    {
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));
        var v_Transaction = File.ReadAllText(FindWarehouseComponent("FWarehouse_Transaction_List.razor"));
        var v_AllLists = v_List + v_Transaction;
        var v_MasterController = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "DM", "CWarehouseMasterSupport_Controller.cs"));
        var v_DocumentController = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "XNK", "CWarehouseDocumentSupport_Controller.cs"));
        var v_ReportController = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "BC", "CWarehouseReport_Controller.cs"));
        var v_ControllerBase = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "Common", "CWarehouse_Controller_Base.cs"));
        var v_Procedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));

        Assert.Contains("OnRead=\"Read_Master_Async\"", v_List);
        Assert.Contains("OnRead=\"Read_Document_Async\"", v_Transaction);
        Assert.Contains("OnRead=\"Read_Report_Detail_Async\"", v_List);
        Assert.Contains("OnRead=\"Read_Inventory_Async\"", v_List);
        Assert.Contains("PageSize=\"10\"", v_AllLists);
        Assert.Contains("List_Master_Page_Async", v_MasterController);
        Assert.Contains("List_Lookup_Page_Async", v_MasterController);
        Assert.Contains("sp_XNK_Document_Page", v_DocumentController);
        Assert.Contains("F9002_sp_Chi_Tiet_Nhap_Page", v_ReportController);
        Assert.Contains("F9001_sp_Ton_Kho_Hien_Tai_Page", v_ReportController);
        Assert.Contains("Page_From_Procedure", v_ControllerBase);
        Assert.Contains("sp_DM_Master_Page", v_Procedures);
        Assert.Contains("sp_XNK_Document_Page", v_Procedures);
        Assert.Contains("F9002_sp_Chi_Tiet_Nhap_Page", v_Procedures);
        Assert.Contains("OFFSET (@Page_Number - 1) * @Page_Size ROWS", v_Procedures);
    }

    [Fact]
    public void Warehouse_server_read_grids_do_not_mix_data_binding_with_on_read()
    {
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor")) +
            File.ReadAllText(FindWarehouseComponent("FWarehouse_Transaction_List.razor"));
        var v_GridTags = Regex.Matches(v_List, "<TelerikGrid\\b[^>]*>", RegexOptions.Singleline);

        Assert.NotEmpty(v_GridTags);
        foreach (Match v_GridTag in v_GridTags)
        {
            if (v_GridTag.Value.Contains("OnRead=", StringComparison.Ordinal))
                Assert.DoesNotContain("Data=", v_GridTag.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Warehouse_excel_export_uses_telerik_all_pages_for_on_read_grids()
    {
        var v_FBase = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Common", "Common", "FBase.razor"));
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));
        var v_Transaction = File.ReadAllText(FindWarehouseComponent("FWarehouse_Transaction_List.razor"));
        var v_AllLists = v_List + v_Transaction;

        Assert.Contains("if (p_grdGrid.OnRead.HasDelegate)", v_FBase);
        Assert.Contains("await Export_Excel_Telerik_Default(p_grdGrid);", v_FBase);
        Assert.Contains("await Export_Excel_Custom(p_grdGrid);", v_FBase);
        Assert.Contains("p_objArgs.Columns.RemoveAll(v_objColumn => string.IsNullOrWhiteSpace(v_objColumn.Field));", v_FBase);
        Assert.Equal(5, Regex.Matches(v_AllLists, "<GridExcelExport\\b[^>]*AllPages=\"true\"[^>]*>").Count);
        Assert.Equal(5, Regex.Matches(v_AllLists, "OnBeforeExport=\"Prepare_Excel_Export\"").Count);
        Assert.Equal(4, Regex.Matches(v_List, "CGrid_DataSource_Utility.Requires_Full_Source\\(args.Request, r_bIs_Excel_OnRead_Export").Count);
    }

    [Fact]
    public void Warehouse_master_excel_export_uses_single_sheet_exporter_for_all_master_types()
    {
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("@Layout_Tool_Button(m_grdMaster, Can_Import_Selected_Master ? Master_Import_Tool_Item : null, Export_Master_Excel_Async)", v_List);
        Assert.Contains("private async Task Export_Master_Excel_Async()", v_List);
        Assert.Contains("CWarehouseMasterExcelExporter.CreateWorkbook", v_List);
        Assert.Contains("Load_Master_Source()", v_List);
        Assert.Contains("PageSize = 0", v_List);
        Assert.Contains("FilterDescriptors", v_List);
        Assert.Contains("SortDescriptors", v_List);
    }

    [Fact]
    public void Warehouse_document_excel_export_includes_detail_rows_and_formats_document_datetime()
    {
        var v_FBase = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Common", "Common", "FBase.razor"));
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_Transaction_List.razor"));
        var v_iExportStart = v_List.IndexOf("private async Task Export_Document_Excel_Async()", StringComparison.Ordinal);

        Assert.True(v_iExportStart >= 0, "Document Excel export must use a detail-aware export handler.");
        var v_ExportMethod = v_List[v_iExportStart..];
        var v_iNextMethod = v_ExportMethod.IndexOf("\n    private ", StringComparison.Ordinal);
        if (v_iNextMethod >= 0)
            v_ExportMethod = v_ExportMethod[..v_iNextMethod];

        Assert.Contains("Export_Document_Excel_Async", v_List);
        Assert.Contains("Export_Excel(p_objGrid, p_objCustom_Excel_Export)", v_FBase);
        Assert.Contains("F3001_sp_sel_List_Nhap_Kho_Detail", v_ExportMethod);
        Assert.Contains("F3002_sp_sel_List_Xuat_Kho_Detail", v_ExportMethod);
        Assert.Contains("CWarehouseDocumentExcelExporter", v_ExportMethod);
    }

    [Fact]
    public void Warehouse_excel_export_registers_telerik_document_processing_license_in_root_app()
    {
        var v_Project = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web", "TKS_Thuc_Tap_V11_Web.csproj"));
        var v_Program = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web", "Program.cs"));
        var v_iRegistration = v_Program.IndexOf("TelerikLicensing.Register();", StringComparison.Ordinal);
        var v_iAppBuilder = v_Program.IndexOf("WebApplication.CreateBuilder(args)", StringComparison.Ordinal);

        Assert.Contains("<PackageReference Include=\"Telerik.Licensing\" Version=\"1.8.2\" />", v_Project);
        Assert.True(v_iRegistration >= 0, "The root app must register Telerik licensing at runtime.");
        Assert.True(v_iAppBuilder >= 0, "The root app builder was not found.");
        Assert.True(v_iRegistration < v_iAppBuilder, "Telerik licensing must register before app initialization.");
    }

    [Fact]
    public void Warehouse_all_pages_read_supports_zero_page_size_without_paging()
    {
        var v_Procedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_iProcStart = v_Procedures.IndexOf("CREATE OR ALTER PROCEDURE dbo.F9001_sp_Ton_Kho_Hien_Tai_Page", StringComparison.Ordinal);
        var v_iProcEnd = v_Procedures.IndexOf("\nGO", v_iProcStart, StringComparison.Ordinal);

        Assert.True(v_iProcStart >= 0, "Current inventory paging procedure was not found.");
        Assert.True(v_iProcEnd > v_iProcStart, "Current inventory paging procedure end was not found.");

        var v_Procedure = v_Procedures[v_iProcStart..v_iProcEnd];
        Assert.Contains("IF @Page_Size = 0", v_Procedure);
        Assert.DoesNotContain("IF @Page_Size < 1 SET @Page_Size = 10;", v_Procedure);
    }

    [Fact]
    public void Warehouse_excel_read_exports_all_filtered_and_sorted_rows_independent_of_current_page()
    {
        var v_arrData = Enumerable.Range(1, 84)
            .Select(v_i => new CWarehouseMaster
            {
                Auto_ID = v_i,
                Code = $"SP-{v_i:000}",
                Name = v_i <= 12 ? $"Thịt {v_i:00}" : $"Hàng {v_i:00}"
            })
            .ToList();
        var v_objRequest = CreateDataSourceRequest(2, 0);
        v_objRequest.Filters.Add(new FilterDescriptor("Name", FilterOperator.Contains, "Thịt"));
        v_objRequest.Sorts.Add(new SortDescriptor("Code", Telerik.DataSource.ListSortDirection.Descending));
        var v_objGridPageRequest = CreateDataSourceRequest(2, 5);
        v_objGridPageRequest.Filters.Add(new FilterDescriptor("Name", FilterOperator.Contains, "Thịt"));
        v_objGridPageRequest.Sorts.Add(new SortDescriptor("Code", Telerik.DataSource.ListSortDirection.Descending));

        var v_objResult = CGrid_DataSource_Utility.Apply(v_arrData, v_objRequest);
        var v_objGridPage = CGrid_DataSource_Utility.Apply(v_arrData, v_objGridPageRequest);

        Assert.Equal(12, v_objResult.Total_Count);
        Assert.Equal(12, v_objResult.Items.Count);
        Assert.Equal("SP-012", v_objResult.Items[0].Code);
        Assert.Equal("SP-001", v_objResult.Items[^1].Code);
        Assert.Equal(12, v_objGridPage.Total_Count);
        Assert.Equal(5, v_objGridPage.Items.Count);
        Assert.Equal("SP-007", v_objGridPage.Items[0].Code);
        Assert.Equal("SP-003", v_objGridPage.Items[^1].Code);
    }

    [Fact]
    public void Warehouse_excel_read_returns_all_rows_and_empty_filtered_results_without_error()
    {
        var v_arrData = Enumerable.Range(1, 84)
            .Select(v_i => new CWarehouseMaster { Auto_ID = v_i, Code = $"SP-{v_i:000}", Name = $"Hàng {v_i:00}" })
            .ToList();
        var v_objAllPagesRequest = CreateDataSourceRequest(3, 0);
        var v_objEmptyRequest = CreateDataSourceRequest(2, 0);
        v_objEmptyRequest.Filters.Add(new FilterDescriptor("Name", FilterOperator.Contains, "không tồn tại"));

        var v_objAllPages = CGrid_DataSource_Utility.Apply(v_arrData, v_objAllPagesRequest);
        var v_objEmpty = CGrid_DataSource_Utility.Apply(v_arrData, v_objEmptyRequest);

        Assert.Equal(84, v_objAllPages.Total_Count);
        Assert.Equal(84, v_objAllPages.Items.Count);
        Assert.Equal(0, v_objEmpty.Total_Count);
        Assert.Empty(v_objEmpty.Items);
    }

    [Fact]
    public void Warehouse_full_source_is_used_only_for_grid_operations_or_excel_all_pages()
    {
        var v_objNormalRequest = CreateDataSourceRequest(2, 10);
        var v_objAllPagesRequest = CreateDataSourceRequest(2, 0);
        var v_objFilteredRequest = CreateDataSourceRequest(1, 10);
        var v_objSortedRequest = CreateDataSourceRequest(1, 10);
        v_objFilteredRequest.Filters.Add(new FilterDescriptor("Name", FilterOperator.Contains, "Thịt"));
        v_objSortedRequest.Sorts.Add(new SortDescriptor("Code", Telerik.DataSource.ListSortDirection.Descending));

        Assert.False(CGrid_DataSource_Utility.Requires_Full_Source(v_objNormalRequest, false));
        Assert.False(CGrid_DataSource_Utility.Requires_Full_Source(v_objAllPagesRequest, false));
        Assert.True(CGrid_DataSource_Utility.Requires_Full_Source(v_objAllPagesRequest, true));
        Assert.True(CGrid_DataSource_Utility.Requires_Full_Source(v_objFilteredRequest, false));
        Assert.True(CGrid_DataSource_Utility.Requires_Full_Source(v_objSortedRequest, false));
    }

    [Fact]
    public void Warehouse_inventory_page_returns_the_warehouse_name_for_the_grid_contract()
    {
        var v_Procedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));

        Assert.Contains("JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = s.Kho_ID", v_Procedures);
        Assert.Contains("k.Ten_Kho", v_Procedures);
        Assert.Contains("FROM AuthorizedScope s", v_Procedures);
    }

    [Fact]
    public void Warehouse_report_grid_is_recreated_when_the_report_type_changes()
    {
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("Select_Report_Type_Async(\"Receipt\")", v_List);
        Assert.Contains("Select_Report_Type_Async(\"Issue\")", v_List);
        Assert.Contains("Select_Report_Type_Async(\"Inventory\")", v_List);
        Assert.Contains("@key=\"m_strReport_Type\"", v_List);
        Assert.Contains("F9002_sp_Chi_Tiet_Nhap_Page", v_List);
        Assert.Contains("F9002_sp_Chi_Tiet_Xuat_Page", v_List);
    }

    [Fact]
    public void Warehouse_period_inventory_grid_hides_current_balance_columns()
    {
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));
        var v_iGridStart = v_List.IndexOf("<TelerikGrid @key=\"m_strReport_Type\" TItem=\"CWarehouseInventoryReport\"", StringComparison.Ordinal);
        var v_iPeriodStart = v_List.IndexOf("@if (!Is_Current_Inventory_Report)", v_iGridStart, StringComparison.Ordinal);
        var v_iCurrentReportBranch = v_List.IndexOf("else", v_iPeriodStart, StringComparison.Ordinal);

        Assert.True(v_iGridStart >= 0, "Inventory grid markup was not found.");
        Assert.True(v_iPeriodStart > v_iGridStart, "Period-report branch was not found inside the inventory grid.");
        Assert.True(v_iCurrentReportBranch > v_iPeriodStart, "Current-inventory branch was not found after the period-report branch.");

        foreach (var v_Field in new[] { "SL_Dau_Ky", "SL_Nhap", "SL_Xuat", "SL_Cuoi_Ky" })
        {
            var v_iFieldIndex = v_List.IndexOf($"Field=\"{v_Field}\"", v_iPeriodStart, StringComparison.Ordinal);
            Assert.True(v_iFieldIndex > v_iPeriodStart && v_iFieldIndex < v_iCurrentReportBranch, $"Period field {v_Field} is not in the period-report branch.");
        }

        foreach (var v_Field in new[] { "SL_Ton_Thuc_Te", "SL_Dang_Giu", "SL_Kha_Dung" })
        {
            var v_iFieldIndex = v_List.IndexOf($"Field=\"{v_Field}\"", v_iPeriodStart, StringComparison.Ordinal);
            Assert.True(v_iFieldIndex > v_iCurrentReportBranch, $"Current-balance field {v_Field} must be outside the period-report branch.");
        }
    }

    [Fact]
    public void Warehouse_inventory_current_report_rebinds_and_exports_the_inventory_grid()
    {
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("m_grdInventory.Rebind();", v_List);
        Assert.Contains("ToolButtons=\"@Layout_Tool_Button(m_grdInventory)\"", v_List);
        Assert.Contains("GridExcelExport FileName=\"Warehouse_Inventory_Export\" AllPages=\"true\"", v_List);
        Assert.Contains("GridCsvExport FileName=\"Warehouse_Inventory_Export\" AllPages=\"true\"", v_List);
    }

    [Fact]
    public void Warehouse_report_tabs_rebind_the_active_grid()
    {
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("private async Task Select_Report_Type_Async(string p_strReport_Type)", v_List);
        Assert.Contains("private Task Load_Report_Async()", v_List);
        Assert.Contains("m_grdInventory.Rebind();", v_List);
        Assert.Contains("m_grdReportDetail.Rebind();", v_List);
    }

    [Fact]
    public void Warehouse_reports_filter_by_a_user_authorized_warehouse()
    {
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));
        var v_ReportController = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "BC", "CWarehouseReport_Controller.cs"));
        var v_Procedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));

        Assert.DoesNotContain("m_iReport_Warehouse_ID", v_List);
        Assert.Contains("Field=\"Ten_Kho\" Title=\"Kho\"", v_List);
        Assert.Contains("F9002_sp_Chi_Tiet_Nhap_Page(m_dtmFrom, m_dtmTo, args.Request.Page, args.Request.PageSize, r_strActive_User_Name, null)", v_List);
        Assert.Contains("F9002_sp_Chi_Tiet_Xuat_Page(m_dtmFrom, m_dtmTo, args.Request.Page, args.Request.PageSize, r_strActive_User_Name, null)", v_List);
        Assert.Contains("F9001_sp_Ton_Kho_Hien_Tai_Page(", v_List);
        Assert.Contains("F9002_sp_Xuat_Nhap_Ton_Page(", v_List);
        Assert.Contains("long? p_iWarehouse_ID = null", v_ReportController);
        Assert.Contains("CUtility.Map_Row_To_Entity<CWarehouseDetailReport>(v_row)", v_ReportController);
        Assert.Contains("CUtility.Map_Row_To_Entity<CWarehouseInventoryReport>(v_row)", v_ReportController);
        Assert.Contains("Map_Page<CWarehouseDetailReport>(v_ds)", v_ReportController);
        Assert.Contains("Map_Page<CWarehouseInventoryReport>(v_ds)", v_ReportController);

        foreach (var v_Procedure in new[]
        {
            "F9002_sp_Chi_Tiet_Nhap",
            "F9002_sp_Chi_Tiet_Xuat",
            "F9002_sp_Chi_Tiet_Nhap_Page",
            "F9002_sp_Chi_Tiet_Xuat_Page",
            "F9002_sp_Xuat_Nhap_Ton",
            "F9002_sp_Xuat_Nhap_Ton_Page"
        })
        {
            var v_iProcedureStart = v_Procedures.IndexOf($"CREATE OR ALTER PROCEDURE dbo.{v_Procedure}", StringComparison.Ordinal);
            Assert.True(v_iProcedureStart >= 0, $"Missing procedure {v_Procedure}.");
            var v_iProcedureEnd = v_Procedures.IndexOf("\nGO", v_iProcedureStart, StringComparison.Ordinal);
            var v_strDefinition = v_Procedures.Substring(v_iProcedureStart, v_iProcedureEnd - v_iProcedureStart);
            Assert.Contains("@Kho_ID BIGINT = NULL", v_strDefinition);
            Assert.Contains("sp_DM_Kho_User_Ensure_Access", v_strDefinition);
            Assert.Contains("@Kho_ID", v_strDefinition);
        }
    }

    [Fact]
    public void Warehouse_document_grids_filter_by_authorized_warehouse_through_FilterRow()
    {
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_Transaction_List.razor"));
        var v_DocumentController = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "XNK", "CWarehouseDocumentSupport_Controller.cs"));
        var v_Procedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));

        Assert.DoesNotContain("m_iDocument_Warehouse_ID", v_List);
        Assert.Contains("Field=\"Ten_Kho\" Title=\"Kho\"", v_List);
        Assert.Contains("F3001_sp_sel_List_Nhap_Kho(r_strActive_User_Name, null)", v_List);
        Assert.Contains("F3002_sp_sel_List_Xuat_Kho(r_strActive_User_Name, null)", v_List);
        Assert.Contains("CGrid_DataSource_Utility.Apply(v_arrSource, args.Request)", v_List);
        Assert.Contains("long? p_iWarehouse_ID = null", v_DocumentController);
        Assert.Contains("p_iWarehouse_ID", v_DocumentController);

        foreach (var v_Procedure in new[]
        {
            "F3001_sp_sel_List_Nhap_Kho",
            "F3002_sp_sel_List_Xuat_Kho",
            "sp_XNK_Document_Page"
        })
        {
            var v_iProcedureStart = v_Procedures.IndexOf($"CREATE OR ALTER PROCEDURE dbo.{v_Procedure}", StringComparison.Ordinal);
            Assert.True(v_iProcedureStart >= 0, $"Missing procedure {v_Procedure}.");
            var v_iProcedureEnd = v_Procedures.IndexOf("\nGO", v_iProcedureStart, StringComparison.Ordinal);
            var v_Definition = v_Procedures.Substring(v_iProcedureStart, v_iProcedureEnd - v_iProcedureStart);
            Assert.Contains("@Kho_ID BIGINT = NULL", v_Definition);
            Assert.Contains("sp_DM_Kho_User_Ensure_Access", v_Definition);
            Assert.Contains("@Kho_ID IS NULL OR", v_Definition);
        }
    }

    [Fact]
    public void Warehouse_data_access_uses_stored_procedures_and_audit_arguments()
    {
        var v_Master = string.Concat(new[]
        {
            "CDM_Kho_Controller.cs",
            "CDM_Don_Vi_Tinh_Controller.cs",
            "CDM_Loai_San_Pham_Controller.cs",
            "CDM_San_Pham_Controller.cs",
            "CDM_NCC_Controller.cs"
        }.Select(v_strFile => File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "DM", v_strFile))));
        var v_Document = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "XNK", "CWarehouseDocumentSupport_Controller.cs")) +
            File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "XNK", "CXNK_Nhap_Kho_Controller.cs")) +
            File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "XNK", "CXNK_Xuat_Kho_Controller.cs"));
        var v_Report = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "BC", "CWarehouseReport_Controller.cs"));
        var v_BaseController = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "Common", "CWarehouse_Controller_Base.cs"));
        var v_Source = v_Master + v_Document + v_Report + v_BaseController;

        Assert.DoesNotContain("SELECT ", v_Source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CSqlHelper.FillDataTable", v_Source);
        Assert.Contains("Last_Updated_By", v_Source);
        Assert.Contains("Last_Updated_By_Function", v_Source);
        Assert.Contains("F2009_sp_sel_List_Kho", v_Source);
        Assert.Contains("F2016_sp_sel_List_Don_Vi_Tinh", v_Source);
        Assert.Contains("F2017_sp_sel_List_Loai_San_Pham", v_Source);
        Assert.Contains("F2018_sp_sel_List_San_Pham", v_Source);
        Assert.Contains("F2019_sp_sel_List_NCC", v_Source);
        Assert.Contains("F3001_sp_sel_List_Nhap_Kho", v_Source);
        Assert.Contains("F3002_sp_sel_List_Xuat_Kho", v_Source);
        Assert.Contains("F3001_sp_sel_List_Nhap_Kho_Detail", v_Source);
        Assert.Contains("F3002_sp_sel_List_Xuat_Kho_Detail", v_Source);
    }

    [Fact]
    public void Warehouse_database_contract_contains_full_audit_columns()
    {
        var v_Schema = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Schema.sql"));
        var v_Procedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));

        foreach (var v_Field in new[] { "Created_By", "Created_By_Function", "Last_Updated_By", "Last_Updated_By_Function" })
        {
            Assert.Contains(v_Field, v_Schema);
            Assert.Contains(v_Field, v_Procedures);
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

    [Fact]
    public void Warehouse_master_edit_forwards_permission_assignments_to_the_editor()
    {
        var v_Wrapper = File.ReadAllText(FindWarehouseComponent("FWarehouse_3_Warehouse_Master_Edit.razor"));

        Assert.Contains("[Parameter] public List<CWarehousePermission> m_arrWarehouseUser", v_Wrapper);
        Assert.Contains("m_arrWarehouseUser=\"@m_arrWarehouseUser\"", v_Wrapper);
    }

    [Fact]
    public void Warehouse_permission_user_dropdown_provides_value_expression_for_edit_form()
    {
        var v_Editor = File.ReadAllText(FindWarehouseComponent("FWarehouse_3_Warehouse_Edit.razor"));

        Assert.Contains("ValueExpression=\"@(() => m_objPermission.Login_Name)\"", v_Editor);
    }

    [Fact]
    public void Warehouse_toolbars_reuse_original_FBase_helpers_without_extending_FBase()
    {
        var v_Base = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Common", "Common", "FBase.razor"));
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_Transaction_List.razor"));
        var v_Report = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));
        var v_Toolbar = File.ReadAllText(FindWarehouseComponent("FWarehouse_Filter_Toolbar.razor"));

        Assert.Contains("Layout_Default_Filter<T>(TelerikGrid<T> p_objGrid, string p_strExcel_Template", v_Base);
        Assert.Contains("Layout_Tool_Button(p_objGrid)", v_Base);
        Assert.DoesNotContain("RenderFragment? p_objFilterContent", v_Base);
        Assert.DoesNotContain("Layout_Date_Range_Filter", v_Base);
        Assert.Contains("<FWarehouse_Filter_Toolbar", v_List);
        Assert.Contains("ToolButtons=\"@Layout_Tool_Button(m_grdDocument, p_objCustom_Excel_Export: Export_Document_Excel_Async)\"", v_List);
        Assert.Contains("Layout_Tool_Button(m_grdInventory)", v_Report);
        Assert.Contains("Layout_Tool_Button(m_grdReportDetail)", v_Report);
        Assert.DoesNotContain("Layout_Default_Filter(", v_List);
        Assert.DoesNotContain("Layout_Date_Range_Filter", v_List);
        Assert.Contains("btn rounded-pill btn-primary waves-effect waves-light", v_Toolbar);
        Assert.Contains("@Localize(\"Tìm kiếm\")", v_Toolbar);
        Assert.Contains("CanAdd && OnAdd.HasDelegate", v_Toolbar);
        Assert.Contains("CanAdd && OnImport.HasDelegate", v_Toolbar);
    }

    [Fact]
    public void Warehouse_document_filterrow_uses_grid_fields_and_drops_the_toolbar_warehouse_selector()
    {
        var v_List = File.ReadAllText(FindWarehouseComponent("FWarehouse_Transaction_List.razor"));

        Assert.DoesNotContain("Quản lý phiếu nhập", v_List);
        Assert.DoesNotContain("Quản lý phiếu xuất", v_List);
        Assert.DoesNotContain("@bind=\"m_iDocument_Warehouse_ID\"", v_List);
        Assert.DoesNotContain("m_iDocument_Warehouse_ID", v_List);
        Assert.Contains("Field=\"So_Phieu\"", v_List);
        Assert.Contains("Field=\"Ten_Kho\"", v_List);
        Assert.Contains("Field=\"Is_Posted\"", v_List);
        Assert.Contains("Field=\"Ghi_Chu\"", v_List);
        Assert.Contains("Format_Grid(m_grdDocument)", v_List);
    }

    [Fact]
    public void Warehouse_grid_filters_apply_all_descriptors_before_paging()
    {
        var v_arrDocuments = new List<CWarehouseDocument>
        {
            new() { So_Phieu = "NK-101", Ten_Kho = "Long Thành", Is_Posted = false, Ghi_Chu = "Đợt A" },
            new() { So_Phieu = "NK-102", Ten_Kho = "Long Thành", Is_Posted = true, Ghi_Chu = "Đợt A" },
            new() { So_Phieu = "NK-103", Ten_Kho = "Bình Dương", Is_Posted = false, Ghi_Chu = "Đợt B" }
        };
        var v_objRequest = CreateDataSourceRequest(1, 1);
        v_objRequest.Filters.Add(new FilterDescriptor("Ten_Kho", FilterOperator.Contains, "Long"));
        v_objRequest.Filters.Add(new FilterDescriptor("So_Phieu", FilterOperator.StartsWith, "NK-10"));
        v_objRequest.Filters.Add(new FilterDescriptor("Is_Posted", FilterOperator.IsEqualTo, false));

        var v_objResult = CGrid_DataSource_Utility.Apply(v_arrDocuments, v_objRequest);

        Assert.Equal(1, v_objResult.Total_Count);
        Assert.Single(v_objResult.Items);
        Assert.Equal("NK-101", v_objResult.Items[0].So_Phieu);
    }

    private static DataSourceRequest CreateDataSourceRequest(int p_iPage, int p_iPage_Size)
    {
        return new DataSourceRequest
        {
            Page = p_iPage,
            PageSize = p_iPage_Size,
            Filters = new List<IFilterDescriptor>(),
            Sorts = new List<SortDescriptor>()
        };
    }

    private static string FindWarehousePage()
    {
        return FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Warehouse.razor");
    }

    private static string FindWarehouseComponent(string p_FileName)
    {
        return FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", p_FileName);
    }

    private static string FindRepositoryDirectory(params string[] p_arrParts)
    {
        for (var v_Directory = new DirectoryInfo(AppContext.BaseDirectory); v_Directory is not null; v_Directory = v_Directory.Parent)
        {
            var v_Candidate = Path.Combine(new[] { v_Directory.FullName }.Concat(p_arrParts).ToArray());
            if (Directory.Exists(v_Candidate))
                return v_Candidate;
        }

        throw new DirectoryNotFoundException($"Repository directory was not found: {Path.Combine(p_arrParts)}");
    }

    private static string FindRepositoryPath(params string[] p_arrParts)
    {
        for (var v_Directory = new DirectoryInfo(AppContext.BaseDirectory); v_Directory is not null; v_Directory = v_Directory.Parent)
        {
            var v_Candidate = Path.Combine(new[] { v_Directory.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_Candidate))
                return v_Candidate;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(p_arrParts)}");
    }
}
