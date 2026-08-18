using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

// Compatibility facade for existing pages. New warehouse code should use the focused controllers directly.
public sealed class CWarehouse_Controller
{
    private readonly CWarehouseMaster_Controller m_objMasterController = new();
    private readonly CWarehouseDocument_Controller m_objDocumentController = new();
    private readonly CWarehouseReport_Controller m_objReportController;

    public CWarehouse_Controller()
    {
        m_objReportController = new CWarehouseReport_Controller(m_objMasterController);
    }

    public Task<List<CWarehouseMaster>> List_Master_Async(string p_strType) => m_objMasterController.List_Master_Async(p_strType);
    public Task<List<CWarehouseLookup>> List_Lookup_Async(string p_strType) => m_objMasterController.List_Lookup_Async(p_strType);
    public Task Save_Master_Async(string p_strType, CWarehouseMaster p_objData) => m_objMasterController.Save_Master_Async(p_strType, p_objData);
    public Task Delete_Master_Async(string p_strType, long p_iAuto_ID) => m_objMasterController.Delete_Master_Async(p_strType, p_iAuto_ID);

    public Task<List<CWarehouseDocument>> List_Documents_Async(bool p_bReceipt) => m_objDocumentController.List_Documents_Async(p_bReceipt);
    public Task Save_Document_Async(CWarehouseDocument p_objData) => m_objDocumentController.Save_Document_Async(p_objData);
    public Task Delete_Document_Async(bool p_bReceipt, long p_iAuto_ID) => m_objDocumentController.Delete_Document_Async(p_bReceipt, p_iAuto_ID);
    public Task<List<CWarehouseDocumentDetail>> List_Document_Details_Async(bool p_bReceipt, long p_iDocument_ID) => m_objDocumentController.List_Document_Details_Async(p_bReceipt, p_iDocument_ID);
    public Task Save_Document_Detail_Async(bool p_bReceipt, CWarehouseDocumentDetail p_objData) => m_objDocumentController.Save_Document_Detail_Async(p_bReceipt, p_objData);
    public Task Delete_Document_Detail_Async(bool p_bReceipt, long p_iAuto_ID) => m_objDocumentController.Delete_Document_Detail_Async(p_bReceipt, p_iAuto_ID);

    public Task<List<CWarehouseDetailReport>> Detail_Report_Async(bool p_bReceipt, DateTime p_dtmFrom, DateTime p_dtmTo) => m_objReportController.Detail_Report_Async(p_bReceipt, p_dtmFrom, p_dtmTo);
    public Task<List<CWarehouseInventoryReport>> Inventory_Report_Async(DateTime p_dtmFrom, DateTime p_dtmTo) => m_objReportController.Inventory_Report_Async(p_dtmFrom, p_dtmTo);
}
