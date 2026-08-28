using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

public class CWarehouseReport_Controller : CWarehouse_Controller_Base
{
    public Task<List<CWarehouseDetailReport>> Detail_Report_Async(bool p_bIs_Receipt, DateTime p_dtmFrom, DateTime p_dtmTo, string p_strCurrent_Login = "", long? p_iWarehouse_ID = null)
    {
        var v_strProcedure = p_bIs_Receipt ? "sp_BC_Chi_Tiet_Nhap" : "sp_BC_Chi_Tiet_Xuat";
        return Task.FromResult(List_From_Procedure<CWarehouseDetailReport>(v_strProcedure, p_dtmFrom.Date, p_dtmTo.Date, p_strCurrent_Login, p_iWarehouse_ID));
    }

    public Task<CWarehousePagedResult<CWarehouseDetailReport>> Detail_Report_Page_Async(bool p_bIs_Receipt, DateTime p_dtmFrom, DateTime p_dtmTo, int p_iPage_Number, int p_iPage_Size, string p_strCurrent_Login = "", long? p_iWarehouse_ID = null)
    {
        var v_strProcedure = p_bIs_Receipt ? "sp_BC_Chi_Tiet_Nhap_Page" : "sp_BC_Chi_Tiet_Xuat_Page";
        return Task.FromResult(Page_From_Procedure<CWarehouseDetailReport>(v_strProcedure, p_dtmFrom.Date, p_dtmTo.Date, p_iPage_Number, p_iPage_Size, p_strCurrent_Login, p_iWarehouse_ID));
    }

    public Task<List<CWarehouseInventoryReport>> Inventory_Report_Async(DateTime p_dtmFrom, DateTime p_dtmTo, string p_strCurrent_Login = "", long? p_iWarehouse_ID = null)
    {
        var v_arrRes = List_From_Procedure<CWarehouseInventoryReport>("sp_BC_Xuat_Nhap_Ton", p_dtmFrom.Date, p_dtmTo.Date, p_strCurrent_Login, p_iWarehouse_ID);
        var v_arrWarehouse = List_From_Procedure<CWarehouseLookup>("sp_DM_Kho_User_List_Allowed", p_strCurrent_Login).ToDictionary(x => x.Auto_ID, x => x.Name);
        foreach (var v_objData in v_arrRes)
            v_objData.Ten_Kho = v_arrWarehouse.GetValueOrDefault(v_objData.Kho_ID, "");

        return Task.FromResult(v_arrRes);
    }

    public Task<CWarehousePagedResult<CWarehouseInventoryReport>> Inventory_Report_Page_Async(DateTime p_dtmFrom, DateTime p_dtmTo, int p_iPage_Number, int p_iPage_Size, string p_strCurrent_Login = "", long? p_iWarehouse_ID = null)
    {
        return Task.FromResult(Page_From_Procedure<CWarehouseInventoryReport>("sp_BC_Xuat_Nhap_Ton_Page", p_dtmFrom.Date, p_dtmTo.Date, p_iPage_Number, p_iPage_Size, p_strCurrent_Login, p_iWarehouse_ID));
    }
}
