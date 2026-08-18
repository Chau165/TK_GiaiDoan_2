using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

public sealed class CWarehouseReport_Controller : CWarehouse_Controller_Base
{
    private readonly CWarehouseMaster_Controller m_objMasterController;

    public CWarehouseReport_Controller(CWarehouseMaster_Controller? p_objMasterController = null)
    {
        m_objMasterController = p_objMasterController ?? new CWarehouseMaster_Controller();
    }

    public Task<List<CWarehouseDetailReport>> Detail_Report_Async(bool p_bReceipt, DateTime p_dtmFrom, DateTime p_dtmTo)
    {
        var procedure = p_bReceipt ? "dbo.sp_BC_Chi_Tiet_Nhap" : "dbo.sp_BC_Chi_Tiet_Xuat";
        return ReadProcedureAsync(procedure, r => new CWarehouseDetailReport
        {
            Ngay = r.GetDateTime(0),
            So_Phieu = r.GetString(1),
            Nha_Cung_Cap = p_bReceipt ? r.GetString(3) : "",
            Ma_San_Pham = p_bReceipt ? r.GetString(4) : r.GetString(2),
            Ten_San_Pham = p_bReceipt ? r.GetString(5) : r.GetString(3),
            So_Luong = p_bReceipt ? r.GetDecimal(6) : r.GetDecimal(4),
            Don_Gia = p_bReceipt ? r.GetDecimal(7) : r.GetDecimal(5),
            Tri_Gia = p_bReceipt ? r.GetDecimal(8) : r.GetDecimal(6)
        }, c => { Add(c, "@Tu_Ngay", p_dtmFrom.Date); Add(c, "@Den_Ngay", p_dtmTo.Date); });
    }

    public async Task<List<CWarehouseInventoryReport>> Inventory_Report_Async(DateTime p_dtmFrom, DateTime p_dtmTo)
    {
        var warehouses = await m_objMasterController.List_Lookup_Async("Kho");
        var names = warehouses.ToDictionary(x => x.Auto_ID, x => x.Name);
        var data = await ReadProcedureAsync("dbo.sp_BC_Xuat_Nhap_Ton", r => new CWarehouseInventoryReport
        {
            Kho_ID = r.GetInt64(0), San_Pham_ID = r.GetInt64(1), Ma_San_Pham = r.GetString(2), Ten_San_Pham = r.GetString(3), SL_Dau_Ky = r.GetDecimal(4), SL_Nhap = r.GetDecimal(5), SL_Xuat = r.GetDecimal(6), SL_Cuoi_Ky = r.GetDecimal(7)
        }, c => { Add(c, "@Tu_Ngay", p_dtmFrom.Date); Add(c, "@Den_Ngay", p_dtmTo.Date); });

        foreach (var item in data)
            item.Ten_Kho = names.TryGetValue(item.Kho_ID, out var name) ? name : "";

        return data;
    }
}
