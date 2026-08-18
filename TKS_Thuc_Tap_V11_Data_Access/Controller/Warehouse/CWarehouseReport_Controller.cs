using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

public class CWarehouseReport_Controller : CWarehouse_Controller_Base
{
    public Task<List<CWarehouseDetailReport>> Detail_Report_Async(bool p_bIs_Receipt, DateTime p_dtmFrom, DateTime p_dtmTo)
    {
        var v_strProcedure = p_bIs_Receipt ? "sp_BC_Chi_Tiet_Nhap" : "sp_BC_Chi_Tiet_Xuat";
        return Task.FromResult(List_From_Procedure<CWarehouseDetailReport>(v_strProcedure, p_dtmFrom.Date, p_dtmTo.Date));
    }

    public Task<List<CWarehouseInventoryReport>> Inventory_Report_Async(DateTime p_dtmFrom, DateTime p_dtmTo)
    {
        var v_arrRes = List_From_Procedure<CWarehouseInventoryReport>("sp_BC_Xuat_Nhap_Ton", p_dtmFrom.Date, p_dtmTo.Date);
        var v_arrWarehouse = List_From_Procedure<CWarehouseLookup>("sp_DM_Lookup_List", "Kho").ToDictionary(x => x.Auto_ID, x => x.Name);
        foreach (var v_objData in v_arrRes)
            v_objData.Ten_Kho = v_arrWarehouse.GetValueOrDefault(v_objData.Kho_ID, "");

        return Task.FromResult(v_arrRes);
    }
}
