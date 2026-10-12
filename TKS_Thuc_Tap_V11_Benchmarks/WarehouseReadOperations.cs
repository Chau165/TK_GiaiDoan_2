using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

namespace TKS_Thuc_Tap_V11_Benchmarks;

public sealed class WarehouseReadOperations
{
    private readonly BenchmarkSettings m_settings;

    public WarehouseReadOperations(BenchmarkSettings p_settings)
    {
        m_settings = p_settings;
    }

    public async Task<int> MasterPagedAsync()
    {
        return (await new CWarehouseMasterSupport_Controller()
            .List_Master_Page_Async("SanPham", 1, m_settings.PageSize, "")).Items.Count;
    }

    public async Task<int> LookupPagedAsync()
    {
        return (await new CWarehouseMasterSupport_Controller()
            .List_Lookup_Page_Async("SanPham", 1, m_settings.PageSize, "")).Items.Count;
    }

    public Task<int> DocumentPagedAsync()
    {
        var v_objPage = new CWarehouseDocumentSupport_Controller()
            .sp_XNK_Document_Page(true, 1, m_settings.PageSize, "", m_settings.LoginName);
        return Task.FromResult(v_objPage.Items.Count);
    }

    public Task<int> DetailReportPagedAsync()
    {
        var v_objPage = new CWarehouseReport_Controller()
            .sp_BC_Chi_Tiet_Nhap_Page(m_settings.ReportFromDate, m_settings.ReportToDate, 1, m_settings.PageSize, m_settings.LoginName);
        return Task.FromResult(v_objPage.Items.Count);
    }

    public Task<int> InventoryHistoricalReportPagedAsync()
    {
        var v_objPage = new CWarehouseReport_Controller()
            .sp_BC_Xuat_Nhap_Ton_Page(m_settings.ReportFromDate, m_settings.ReportToDate, 1, m_settings.PageSize, m_settings.LoginName);
        return Task.FromResult(v_objPage.Items.Count);
    }

    public Task<int> InventoryCurrentBalancePagedAsync()
    {
        var v_objPage = new CWarehouseReport_Controller()
            .sp_BC_Ton_Kho_Hien_Tai_Page(1, m_settings.PageSize, m_settings.LoginName);
        return Task.FromResult(v_objPage.Items.Count);
    }

    // Backward-compatible alias. The former name represented the historical
    // report; current balance has a distinct operation and scenario.
    public Task<int> InventoryReportPagedAsync()
    {
        return InventoryHistoricalReportPagedAsync();
    }

}
