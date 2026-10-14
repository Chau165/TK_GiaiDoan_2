using TKS_Thuc_Tap_V11_Data_Access.Controller.BC;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Controller.XNK;

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
        return (await new CWarehouseMasterSupport_Controller()
            .List_Master_Page_Async("SanPham", V2Constants.PageNumber, m_settings.PageSize, "")).Items.Count;
    }

    private async Task<int> LookupPagedAsync()
    {
        return (await new CWarehouseMasterSupport_Controller()
            .List_Lookup_Page_Async("SanPham", V2Constants.PageNumber, m_settings.PageSize, "")).Items.Count;
    }

    private Task<int> DocumentPagedAsync()
    {
        var v_objPage = new CWarehouseDocumentSupport_Controller()
            .sp_XNK_Document_Page(
                true,
                V2Constants.PageNumber,
                m_settings.PageSize,
                "",
                m_settings.LoginName);
        return Task.FromResult(v_objPage.Items.Count);
    }

    private Task<int> DetailReportPagedAsync()
    {
        var v_objPage = new CWarehouseReport_Controller()
            .F9002_sp_Chi_Tiet_Nhap_Page(
                m_settings.ReportFromDate,
                m_settings.ReportToDate,
                V2Constants.PageNumber,
                m_settings.PageSize,
                m_settings.LoginName);
        return Task.FromResult(v_objPage.Items.Count);
    }

    private Task<int> InventoryHistoricalReportPagedAsync()
    {
        var v_objPage = new CWarehouseReport_Controller()
            .F9002_sp_Xuat_Nhap_Ton_Page(
                m_settings.ReportFromDate,
                m_settings.ReportToDate,
                V2Constants.PageNumber,
                m_settings.PageSize,
                m_settings.LoginName);
        return Task.FromResult(v_objPage.Items.Count);
    }

    private Task<int> InventoryCurrentBalancePagedAsync()
    {
        var v_objPage = new CWarehouseReport_Controller()
            .F9001_sp_Ton_Kho_Hien_Tai_Page(
                V2Constants.PageNumber,
                m_settings.PageSize,
                m_settings.LoginName);
        return Task.FromResult(v_objPage.Items.Count);
    }
}
