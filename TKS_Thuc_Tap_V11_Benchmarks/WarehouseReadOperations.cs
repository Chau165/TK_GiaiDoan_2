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
        return (await new CWarehouseMaster_Controller()
            .List_Master_Page_Async("SanPham", 1, m_settings.PageSize, "")).Items.Count;
    }

    public async Task<int> LookupPagedAsync()
    {
        return (await new CWarehouseMaster_Controller()
            .List_Lookup_Page_Async("SanPham", 1, m_settings.PageSize, "")).Items.Count;
    }

    public async Task<int> DocumentPagedAsync()
    {
        return (await new CWarehouseDocument_Controller()
            .List_Documents_Page_Async(true, 1, m_settings.PageSize, "", m_settings.LoginName)).Items.Count;
    }

    public async Task<int> DetailReportPagedAsync()
    {
        return (await new CWarehouseReport_Controller()
            .Detail_Report_Page_Async(true, m_settings.ReportFromDate, m_settings.ReportToDate, 1, m_settings.PageSize, m_settings.LoginName)).Items.Count;
    }

    public async Task<int> InventoryReportPagedAsync()
    {
        return (await new CWarehouseReport_Controller()
            .Inventory_Report_Page_Async(
                m_settings.ReportFromDate,
                m_settings.ReportToDate,
                1,
                m_settings.PageSize,
                m_settings.LoginName,
                p_bRead_Current_Balance: m_settings.UseCurrentInventoryBalance)).Items.Count;
    }
}
