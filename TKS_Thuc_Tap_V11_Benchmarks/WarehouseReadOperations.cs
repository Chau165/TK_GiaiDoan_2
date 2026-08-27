using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

namespace TKS_Thuc_Tap_V11_Benchmarks;

public sealed class WarehouseReadOperations
{
    private static readonly DateTime s_fromDate = new(2025, 1, 1);
    private static readonly DateTime s_toDate = new(2026, 12, 31);
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
            .Detail_Report_Page_Async(true, s_fromDate, s_toDate, 1, m_settings.PageSize, m_settings.LoginName)).Items.Count;
    }

    public async Task<int> InventoryReportPagedAsync()
    {
        return (await new CWarehouseReport_Controller()
            .Inventory_Report_Page_Async(s_fromDate, s_toDate, 1, m_settings.PageSize, m_settings.LoginName)).Items.Count;
    }
}
