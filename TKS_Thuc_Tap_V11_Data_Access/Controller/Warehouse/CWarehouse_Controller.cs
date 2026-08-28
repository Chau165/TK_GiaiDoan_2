using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

// Compatibility facade for existing pages. New warehouse code should use the focused controllers directly.
public sealed class CWarehouse_Controller
{
    private readonly CWarehouseMaster_Controller m_objMasterController = new();
    private readonly CWarehouseDocument_Controller m_objDocumentController = new();
    private readonly CWarehouseReport_Controller m_objReportController = new();

    public Task<List<CWarehouseMaster>> List_Master_Async(string p_strType) => m_objMasterController.List_Master_Async(p_strType);
    public Task<List<CWarehouseLookup>> List_Lookup_Async(string p_strType) => m_objMasterController.List_Lookup_Async(p_strType);
    public Task Save_Master_Async(string p_strType, CWarehouseMaster p_objData, string p_strUser = "", string p_strFunction = "") => m_objMasterController.Save_Master_Async(p_strType, p_objData, p_strUser, p_strFunction);
    public Task Delete_Master_Async(string p_strType, long p_iAuto_ID, string p_strUser = "", string p_strFunction = "") => m_objMasterController.Delete_Master_Async(p_strType, p_iAuto_ID, p_strUser, p_strFunction);

    public Task<List<CWarehouseDocument>> List_Documents_Async(bool p_bReceipt) => m_objDocumentController.List_Documents_Async(p_bReceipt);
    public Task Save_Document_Async(CWarehouseDocument p_objData, string p_strUser = "", string p_strFunction = "") => m_objDocumentController.Save_Document_Async(p_objData, p_strUser, p_strFunction);
    public Task Delete_Document_Async(bool p_bReceipt, long p_iAuto_ID, string p_strUser = "", string p_strFunction = "") => m_objDocumentController.Delete_Document_Async(p_bReceipt, p_iAuto_ID, p_strUser, p_strFunction);
    public Task Post_Document_Async(bool p_bReceipt, long p_iAuto_ID, string p_strUser = "", string p_strFunction = "") => m_objDocumentController.Post_Document_Async(p_bReceipt, p_iAuto_ID, p_strUser, p_strFunction);
    public Task<List<CWarehouseDocumentDetail>> List_Document_Details_Async(bool p_bReceipt, long p_iDocument_ID) => m_objDocumentController.List_Document_Details_Async(p_bReceipt, p_iDocument_ID);
    public Task Save_Document_Detail_Async(bool p_bReceipt, CWarehouseDocumentDetail p_objData, string p_strUser = "", string p_strFunction = "") => m_objDocumentController.Save_Document_Detail_Async(p_bReceipt, p_objData, p_strUser, p_strFunction);
    public Task Delete_Document_Detail_Async(bool p_bReceipt, long p_iAuto_ID, string p_strUser = "", string p_strFunction = "") => m_objDocumentController.Delete_Document_Detail_Async(p_bReceipt, p_iAuto_ID, p_strUser, p_strFunction);

    public Task<List<CWarehouseDetailReport>> Detail_Report_Async(bool p_bReceipt, DateTime p_dtmFrom, DateTime p_dtmTo, string p_strCurrent_Login = "", long? p_iWarehouse_ID = null) => m_objReportController.Detail_Report_Async(p_bReceipt, p_dtmFrom, p_dtmTo, p_strCurrent_Login, p_iWarehouse_ID);
    public Task<List<CWarehouseInventoryReport>> Inventory_Report_Async(DateTime p_dtmFrom, DateTime p_dtmTo, string p_strCurrent_Login = "", long? p_iWarehouse_ID = null) => m_objReportController.Inventory_Report_Async(p_dtmFrom, p_dtmTo, p_strCurrent_Login, p_iWarehouse_ID);
}
