using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Controller.XNK;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.XNK;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

// Integration-test adapter for the UI document projection; production code calls the split controllers directly.
internal sealed class WarehouseDocumentTestHarness
{
    private readonly CXNK_Nhap_Kho_Controller m_objNhapKhoController = new();
    private readonly CXNK_Xuat_Kho_Controller m_objXuatKhoController = new();
    private readonly CWarehouseDocumentSupport_Controller m_objSupportController = new();

    public Task<List<CWarehouseDocument>> List_Documents_Async(bool p_bIs_Receipt, string p_strCurrent_Login = "", long? p_iWarehouse_ID = null)
    {
        var v_arrResult = p_bIs_Receipt
            ? m_objNhapKhoController.F3001_sp_sel_List_Nhap_Kho(p_strCurrent_Login, p_iWarehouse_ID).Select(To_Warehouse_Document).ToList()
            : m_objXuatKhoController.F3002_sp_sel_List_Xuat_Kho(p_strCurrent_Login, p_iWarehouse_ID).Select(To_Warehouse_Document).ToList();
        return Task.FromResult(v_arrResult);
    }

    public Task<CWarehousePagedResult<CWarehouseDocument>> List_Documents_Page_Async(bool p_bIs_Receipt, int p_iPage_Number, int p_iPage_Size, string p_strSearch_Text = "", string p_strCurrent_Login = "", long? p_iWarehouse_ID = null) =>
        Task.FromResult(m_objSupportController.sp_XNK_Document_Page(p_bIs_Receipt, p_iPage_Number, p_iPage_Size,
            p_strSearch_Text, p_strCurrent_Login, p_iWarehouse_ID));

    public Task Save_Document_Async(CWarehouseDocument p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "", string p_strCurrent_Login = "")
    {
        p_objData.Last_Updated_By = p_strLast_Updated_By;
        p_objData.Last_Updated_By_Function = p_strLast_Updated_By_Function;
        if (p_objData.Auto_ID == 0)
        {
            p_objData.Created_By = p_strLast_Updated_By;
            p_objData.Created_By_Function = p_strLast_Updated_By_Function;
        }

        if (p_objData.Is_Receipt)
        {
            var v_objData = To_Nhap_Kho(p_objData, p_strCurrent_Login);
            p_objData.Auto_ID = p_objData.Auto_ID == 0
                ? m_objNhapKhoController.F3001_sp_ins_Nhap_Kho_Header(v_objData)
                : m_objNhapKhoController.F3001_sp_upd_Nhap_Kho_Header(v_objData);
        }
        else
        {
            var v_objData = To_Xuat_Kho(p_objData, p_strCurrent_Login);
            p_objData.Auto_ID = p_objData.Auto_ID == 0
                ? m_objXuatKhoController.F3002_sp_ins_Xuat_Kho_Header(v_objData)
                : m_objXuatKhoController.F3002_sp_upd_Xuat_Kho_Header(v_objData);
        }
        return Task.CompletedTask;
    }

    public Task Delete_Document_Async(bool p_bIs_Receipt, long p_iAuto_ID,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "", string p_strCurrent_Login = "")
    {
        if (p_bIs_Receipt)
            m_objNhapKhoController.F3001_sp_del_Nhap_Kho_Header(p_iAuto_ID, p_strCurrent_Login);
        else
            m_objXuatKhoController.F3002_sp_del_Xuat_Kho_Header(p_iAuto_ID, p_strCurrent_Login);
        return Task.CompletedTask;
    }

    public Task Post_Document_Async(bool p_bIs_Receipt, long p_iAuto_ID,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "", string p_strCurrent_Login = "")
    {
        m_objSupportController.sp_XNK_Document_Post(p_bIs_Receipt, p_iAuto_ID, p_strCurrent_Login,
            p_strLast_Updated_By, p_strLast_Updated_By_Function);
        return Task.CompletedTask;
    }

    public Task<List<CWarehouseDocumentDetail>> List_Document_Details_Async(bool p_bIs_Receipt, long p_iDocument_ID, string p_strCurrent_Login = "")
    {
        var v_arrResult = p_bIs_Receipt
            ? m_objNhapKhoController.F3001_sp_sel_List_Nhap_Kho_Detail(p_iDocument_ID, p_strCurrent_Login).Select(To_Warehouse_Detail).ToList()
            : m_objXuatKhoController.F3002_sp_sel_List_Xuat_Kho_Detail(p_iDocument_ID, p_strCurrent_Login).Select(To_Warehouse_Detail).ToList();
        return Task.FromResult(v_arrResult);
    }

    public Task Save_Document_Detail_Async(bool p_bIs_Receipt, CWarehouseDocumentDetail p_objData,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "", string p_strCurrent_Login = "")
    {
        p_objData.Last_Updated_By = p_strLast_Updated_By;
        p_objData.Last_Updated_By_Function = p_strLast_Updated_By_Function;
        if (p_objData.Auto_ID == 0)
        {
            p_objData.Created_By = p_strLast_Updated_By;
            p_objData.Created_By_Function = p_strLast_Updated_By_Function;
        }
        if (p_bIs_Receipt)
        {
            var v_objData = To_Nhap_Kho_Detail(p_objData, p_strCurrent_Login);
            p_objData.Auto_ID = p_objData.Auto_ID == 0
                ? m_objNhapKhoController.F3001_sp_ins_Nhap_Kho_Detail(v_objData)
                : m_objNhapKhoController.F3001_sp_upd_Nhap_Kho_Detail(v_objData);
        }
        else
        {
            var v_objData = To_Xuat_Kho_Detail(p_objData, p_strCurrent_Login);
            p_objData.Auto_ID = p_objData.Auto_ID == 0
                ? m_objXuatKhoController.F3002_sp_ins_Xuat_Kho_Detail(v_objData)
                : m_objXuatKhoController.F3002_sp_upd_Xuat_Kho_Detail(v_objData);
        }
        return Task.CompletedTask;
    }

    public Task Delete_Document_Detail_Async(bool p_bIs_Receipt, long p_iAuto_ID,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "", string p_strCurrent_Login = "")
    {
        if (p_bIs_Receipt)
            m_objNhapKhoController.F3001_sp_del_Nhap_Kho_Detail(p_iAuto_ID, p_strCurrent_Login);
        else
            m_objXuatKhoController.F3002_sp_del_Xuat_Kho_Detail(p_iAuto_ID, p_strCurrent_Login);
        return Task.CompletedTask;
    }

    private static CXNK_Nhap_Kho To_Nhap_Kho(CWarehouseDocument p_objData, string p_strLogin) => new()
    {
        Auto_ID = p_objData.Auto_ID, So_Phieu_Nhap_Kho = p_objData.So_Phieu, Kho_ID = p_objData.Kho_ID,
        NCC_ID = p_objData.NCC_ID, Ngay_Nhap_Kho = p_objData.Ngay_Chung_Tu, Ghi_Chu = p_objData.Ghi_Chu,
        Ma_Dang_Nhap = p_strLogin, Created_By = p_objData.Created_By, Created_By_Function = p_objData.Created_By_Function,
        Last_Updated_By = p_objData.Last_Updated_By, Last_Updated_By_Function = p_objData.Last_Updated_By_Function
    };

    private static CXNK_Xuat_Kho To_Xuat_Kho(CWarehouseDocument p_objData, string p_strLogin) => new()
    {
        Auto_ID = p_objData.Auto_ID, So_Phieu_Xuat_Kho = p_objData.So_Phieu, Kho_ID = p_objData.Kho_ID,
        Ngay_Xuat_Kho = p_objData.Ngay_Chung_Tu, Ghi_Chu = p_objData.Ghi_Chu,
        Ma_Dang_Nhap = p_strLogin, Created_By = p_objData.Created_By, Created_By_Function = p_objData.Created_By_Function,
        Last_Updated_By = p_objData.Last_Updated_By, Last_Updated_By_Function = p_objData.Last_Updated_By_Function
    };

    private static CXNK_Nhap_Kho_Detail To_Nhap_Kho_Detail(CWarehouseDocumentDetail p_objData, string p_strLogin) => new()
    {
        Auto_ID = p_objData.Auto_ID, Nhap_Kho_ID = p_objData.Document_ID, San_Pham_ID = p_objData.San_Pham_ID,
        SL_Nhap = p_objData.So_Luong, Don_Gia_Nhap = p_objData.Don_Gia, Ma_Dang_Nhap = p_strLogin,
        Created_By = p_objData.Created_By, Created_By_Function = p_objData.Created_By_Function,
        Last_Updated_By = p_objData.Last_Updated_By, Last_Updated_By_Function = p_objData.Last_Updated_By_Function
    };

    private static CXNK_Xuat_Kho_Detail To_Xuat_Kho_Detail(CWarehouseDocumentDetail p_objData, string p_strLogin) => new()
    {
        Auto_ID = p_objData.Auto_ID, Xuat_Kho_ID = p_objData.Document_ID, San_Pham_ID = p_objData.San_Pham_ID,
        SL_Xuat = p_objData.So_Luong, Don_Gia_Xuat = p_objData.Don_Gia, Ma_Dang_Nhap = p_strLogin,
        Created_By = p_objData.Created_By, Created_By_Function = p_objData.Created_By_Function,
        Last_Updated_By = p_objData.Last_Updated_By, Last_Updated_By_Function = p_objData.Last_Updated_By_Function
    };

    private static CWarehouseDocument To_Warehouse_Document(CXNK_Nhap_Kho p_objData) => new()
    {
        Auto_ID = p_objData.Auto_ID, Is_Receipt = true, So_Phieu = p_objData.So_Phieu_Nhap_Kho,
        Kho_ID = p_objData.Kho_ID, Ten_Kho = p_objData.Ten_Kho, NCC_ID = p_objData.NCC_ID, Ten_NCC = p_objData.Ten_NCC,
        Ngay_Chung_Tu = p_objData.Ngay_Nhap_Kho, Is_Posted = p_objData.Is_Posted, Ghi_Chu = p_objData.Ghi_Chu
    };

    private static CWarehouseDocument To_Warehouse_Document(CXNK_Xuat_Kho p_objData) => new()
    {
        Auto_ID = p_objData.Auto_ID, Is_Receipt = false, So_Phieu = p_objData.So_Phieu_Xuat_Kho,
        Kho_ID = p_objData.Kho_ID, Ten_Kho = p_objData.Ten_Kho, NCC_ID = p_objData.NCC_ID, Ten_NCC = p_objData.Ten_NCC,
        Ngay_Chung_Tu = p_objData.Ngay_Xuat_Kho, Is_Posted = p_objData.Is_Posted, Ghi_Chu = p_objData.Ghi_Chu
    };

    private static CWarehouseDocumentDetail To_Warehouse_Detail(CXNK_Nhap_Kho_Detail p_objData) => new()
    {
        Auto_ID = p_objData.Auto_ID, Document_ID = p_objData.Nhap_Kho_ID, San_Pham_ID = p_objData.San_Pham_ID,
        Ma_San_Pham = p_objData.Ma_San_Pham, Ten_San_Pham = p_objData.Ten_San_Pham,
        Ten_Don_Vi_Tinh = p_objData.Ten_Don_Vi_Tinh, So_Luong = p_objData.SL_Nhap, Don_Gia = p_objData.Don_Gia_Nhap
    };

    private static CWarehouseDocumentDetail To_Warehouse_Detail(CXNK_Xuat_Kho_Detail p_objData) => new()
    {
        Auto_ID = p_objData.Auto_ID, Document_ID = p_objData.Xuat_Kho_ID, San_Pham_ID = p_objData.San_Pham_ID,
        Ma_San_Pham = p_objData.Ma_San_Pham, Ten_San_Pham = p_objData.Ten_San_Pham,
        Ten_Don_Vi_Tinh = p_objData.Ten_Don_Vi_Tinh, So_Luong = p_objData.SL_Xuat, Don_Gia = p_objData.Don_Gia_Xuat
    };
}
