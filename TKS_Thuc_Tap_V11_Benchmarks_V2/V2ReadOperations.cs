using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

namespace TKS_Thuc_Tap_V11_Benchmarks_V2;

public sealed class V2ReadOperations
{
    private readonly V2Settings m_settings;

    public V2ReadOperations(V2Settings p_settings)
    {
        m_settings = p_settings;
        m_settings.ConfigureDataAccess();
    }

    public Task<int> ExecuteAsync(string p_scenario)
    {
        return p_scenario switch
        {
            "MasterPaged" => MasterPagedAsync(),
            "LookupPaged" => LookupPagedAsync(),
            "DocumentPaged" => DocumentPagedAsync(),
            "DetailReportPaged" => DetailReportPagedAsync(),
            "InventoryHistoricalReportPaged" => InventoryHistoricalReportPagedAsync(),
            "InventoryCurrentBalancePaged" => InventoryCurrentBalancePagedAsync(),
            _ => throw new ArgumentException($"Unknown V2 scenario: {p_scenario}", nameof(p_scenario))
        };
    }

    private async Task<int> MasterPagedAsync()
    {
        return (await new CWarehouseMaster_Controller()
            .List_Master_Page_Async("SanPham", V2Constants.PageNumber, m_settings.PageSize, "")).Items.Count;
    }

    private async Task<int> LookupPagedAsync()
    {
        return (await new CWarehouseMaster_Controller()
            .List_Lookup_Page_Async("SanPham", V2Constants.PageNumber, m_settings.PageSize, "")).Items.Count;
    }

    private async Task<int> DocumentPagedAsync()
    {
        return (await new CWarehouseDocument_Controller()
            .List_Documents_Page_Async(
                true,
                V2Constants.PageNumber,
                m_settings.PageSize,
                "",
                m_settings.LoginName)).Items.Count;
    }

    private async Task<int> DetailReportPagedAsync()
    {
        return (await new CWarehouseReport_Controller()
            .Detail_Report_Page_Async(
                true,
                m_settings.ReportFromDate,
                m_settings.ReportToDate,
                V2Constants.PageNumber,
                m_settings.PageSize,
                m_settings.LoginName)).Items.Count;
    }

    private Task<int> InventoryHistoricalReportPagedAsync()
    {
        return InventoryReportPagedAsync(false);
    }

    private Task<int> InventoryCurrentBalancePagedAsync()
    {
        return InventoryReportPagedAsync(true);
    }

    private async Task<int> InventoryReportPagedAsync(bool p_readCurrentBalance)
    {
        return (await new CWarehouseReport_Controller()
            .Inventory_Report_Page_Async(
                m_settings.ReportFromDate,
                m_settings.ReportToDate,
                V2Constants.PageNumber,
                m_settings.PageSize,
                m_settings.LoginName,
                p_bRead_Current_Balance: p_readCurrentBalance)).Items.Count;
    }
}
