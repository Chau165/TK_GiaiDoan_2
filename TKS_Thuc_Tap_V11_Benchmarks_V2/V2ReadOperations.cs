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
        Task<int> v_ResultTask;
        switch (p_scenario)
        {
            case "MasterPaged":
                v_ResultTask = MasterPagedAsync();
                break;
            case "LookupPaged":
                v_ResultTask = LookupPagedAsync();
                break;
            case "DocumentPaged":
                v_ResultTask = DocumentPagedAsync();
                break;
            case "DetailReportPaged":
                v_ResultTask = DetailReportPagedAsync();
                break;
            case "InventoryHistoricalReportPaged":
                v_ResultTask = InventoryHistoricalReportPagedAsync();
                break;
            case "InventoryCurrentBalancePaged":
                v_ResultTask = InventoryCurrentBalancePagedAsync();
                break;
            default:
                throw new ArgumentException($"Unknown V2 scenario: {p_scenario}", nameof(p_scenario));
        }

        return v_ResultTask;
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

    private async Task<int> InventoryReportPagedAsync(bool p_bReadCurrentBalance)
    {
        return (await new CWarehouseReport_Controller()
            .Inventory_Report_Page_Async(
                m_settings.ReportFromDate,
                m_settings.ReportToDate,
                V2Constants.PageNumber,
                m_settings.PageSize,
                m_settings.LoginName,
                p_bRead_Current_Balance: p_bReadCurrentBalance)).Items.Count;
    }
}
