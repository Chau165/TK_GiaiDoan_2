using TKS_Thuc_Tap_V11_Data_Access.Controller.Common;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.DM;

// Temporary owner until the company defines the shared Page Store naming rule.
public class CWarehouseMasterSupport_Controller : CWarehouse_Controller_Base
{
    public Task<CWarehousePagedResult<CWarehouseMaster>> List_Master_Page_Async(string p_strMaster_Type, int p_iPage_Number, int p_iPage_Size, string p_strSearch_Text = "")
    {
        return Task.FromResult(Page_From_Procedure<CWarehouseMaster>("sp_DM_Master_Page", p_strMaster_Type, p_iPage_Number, p_iPage_Size, p_strSearch_Text));
    }

    public Task<CWarehousePagedResult<CWarehouseLookup>> List_Lookup_Page_Async(string p_strMaster_Type, int p_iPage_Number, int p_iPage_Size, string p_strSearch_Text = "")
    {
        return Task.FromResult(Page_From_Procedure<CWarehouseLookup>("sp_DM_Lookup_Page", p_strMaster_Type, p_iPage_Number, p_iPage_Size, p_strSearch_Text));
    }
}
