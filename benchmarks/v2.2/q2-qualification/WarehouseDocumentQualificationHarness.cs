using TKS_Thuc_Tap_V11_Data_Access.Controller.XNK;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.XNK;

internal sealed class WarehouseDocumentQualificationHarness
{
    private readonly CXNK_Nhap_Kho_Controller m_objNhapKhoController = new();
    private readonly CXNK_Xuat_Kho_Controller m_objXuatKhoController = new();
    private readonly CWarehouseDocumentSupport_Controller m_objSupportController = new();

    public Task<CWarehousePagedResult<CWarehouseDocument>> List_Documents_Page_Async(
        bool p_bIsReceipt, int p_iPage, int p_iSize, string p_strSearch, string p_strLogin, long? p_iWarehouse) =>
        Task.FromResult(m_objSupportController.sp_XNK_Document_Page(
            p_bIsReceipt, p_iPage, p_iSize, p_strSearch, p_strLogin, p_iWarehouse));

    public Task Save_Document_Async(CWarehouseDocument p_objData,
        string p_strUpdatedBy, string p_strFunction, string p_strLogin)
    {
        p_objData.Last_Updated_By = p_strUpdatedBy;
        p_objData.Last_Updated_By_Function = p_strFunction;
        if (p_objData.Auto_ID == 0)
        {
            p_objData.Created_By = p_strUpdatedBy;
            p_objData.Created_By_Function = p_strFunction;
        }

        if (p_objData.Is_Receipt)
        {
            var v_objData = new CXNK_Nhap_Kho
            {
                Auto_ID = p_objData.Auto_ID, So_Phieu_Nhap_Kho = p_objData.So_Phieu, Kho_ID = p_objData.Kho_ID,
                NCC_ID = p_objData.NCC_ID, Ngay_Nhap_Kho = p_objData.Ngay_Chung_Tu, Ghi_Chu = p_objData.Ghi_Chu,
                Ma_Dang_Nhap = p_strLogin, Created_By = p_objData.Created_By, Created_By_Function = p_objData.Created_By_Function,
                Last_Updated_By = p_objData.Last_Updated_By, Last_Updated_By_Function = p_objData.Last_Updated_By_Function
            };
            p_objData.Auto_ID = p_objData.Auto_ID == 0
                ? m_objNhapKhoController.F3001_sp_ins_Nhap_Kho_Header(v_objData)
                : m_objNhapKhoController.F3001_sp_upd_Nhap_Kho_Header(v_objData);
        }
        else
        {
            var v_objData = new CXNK_Xuat_Kho
            {
                Auto_ID = p_objData.Auto_ID, So_Phieu_Xuat_Kho = p_objData.So_Phieu, Kho_ID = p_objData.Kho_ID,
                Ngay_Xuat_Kho = p_objData.Ngay_Chung_Tu, Ghi_Chu = p_objData.Ghi_Chu,
                Ma_Dang_Nhap = p_strLogin, Created_By = p_objData.Created_By, Created_By_Function = p_objData.Created_By_Function,
                Last_Updated_By = p_objData.Last_Updated_By, Last_Updated_By_Function = p_objData.Last_Updated_By_Function
            };
            p_objData.Auto_ID = p_objData.Auto_ID == 0
                ? m_objXuatKhoController.F3002_sp_ins_Xuat_Kho_Header(v_objData)
                : m_objXuatKhoController.F3002_sp_upd_Xuat_Kho_Header(v_objData);
        }
        return Task.CompletedTask;
    }

    public Task Save_Document_Detail_Async(bool p_bIsReceipt, CWarehouseDocumentDetail p_objData,
        string p_strUpdatedBy, string p_strFunction, string p_strLogin)
    {
        p_objData.Last_Updated_By = p_strUpdatedBy;
        p_objData.Last_Updated_By_Function = p_strFunction;
        if (p_objData.Auto_ID == 0)
        {
            p_objData.Created_By = p_strUpdatedBy;
            p_objData.Created_By_Function = p_strFunction;
        }

        if (p_bIsReceipt)
        {
            var v_objData = new CXNK_Nhap_Kho_Detail
            {
                Auto_ID = p_objData.Auto_ID, Nhap_Kho_ID = p_objData.Document_ID, San_Pham_ID = p_objData.San_Pham_ID,
                SL_Nhap = p_objData.So_Luong, Don_Gia_Nhap = p_objData.Don_Gia, Ma_Dang_Nhap = p_strLogin,
                Created_By = p_objData.Created_By, Created_By_Function = p_objData.Created_By_Function,
                Last_Updated_By = p_objData.Last_Updated_By, Last_Updated_By_Function = p_objData.Last_Updated_By_Function
            };
            p_objData.Auto_ID = p_objData.Auto_ID == 0
                ? m_objNhapKhoController.F3001_sp_ins_Nhap_Kho_Detail(v_objData)
                : m_objNhapKhoController.F3001_sp_upd_Nhap_Kho_Detail(v_objData);
        }
        else
        {
            var v_objData = new CXNK_Xuat_Kho_Detail
            {
                Auto_ID = p_objData.Auto_ID, Xuat_Kho_ID = p_objData.Document_ID, San_Pham_ID = p_objData.San_Pham_ID,
                SL_Xuat = p_objData.So_Luong, Don_Gia_Xuat = p_objData.Don_Gia, Ma_Dang_Nhap = p_strLogin,
                Created_By = p_objData.Created_By, Created_By_Function = p_objData.Created_By_Function,
                Last_Updated_By = p_objData.Last_Updated_By, Last_Updated_By_Function = p_objData.Last_Updated_By_Function
            };
            p_objData.Auto_ID = p_objData.Auto_ID == 0
                ? m_objXuatKhoController.F3002_sp_ins_Xuat_Kho_Detail(v_objData)
                : m_objXuatKhoController.F3002_sp_upd_Xuat_Kho_Detail(v_objData);
        }
        return Task.CompletedTask;
    }

    public Task<List<CWarehouseDocumentDetail>> List_Document_Details_Async(bool p_bIsReceipt, long p_iDocument, string p_strLogin)
    {
        var v_arrResult = p_bIsReceipt
            ? m_objNhapKhoController.F3001_sp_sel_List_Nhap_Kho_Detail(p_iDocument, p_strLogin)
                .Select(v_objData => new CWarehouseDocumentDetail
                {
                    Auto_ID = v_objData.Auto_ID, Document_ID = v_objData.Nhap_Kho_ID, San_Pham_ID = v_objData.San_Pham_ID,
                    Ma_San_Pham = v_objData.Ma_San_Pham, Ten_San_Pham = v_objData.Ten_San_Pham,
                    Ten_Don_Vi_Tinh = v_objData.Ten_Don_Vi_Tinh, So_Luong = v_objData.SL_Nhap, Don_Gia = v_objData.Don_Gia_Nhap
                }).ToList()
            : m_objXuatKhoController.F3002_sp_sel_List_Xuat_Kho_Detail(p_iDocument, p_strLogin)
                .Select(v_objData => new CWarehouseDocumentDetail
                {
                    Auto_ID = v_objData.Auto_ID, Document_ID = v_objData.Xuat_Kho_ID, San_Pham_ID = v_objData.San_Pham_ID,
                    Ma_San_Pham = v_objData.Ma_San_Pham, Ten_San_Pham = v_objData.Ten_San_Pham,
                    Ten_Don_Vi_Tinh = v_objData.Ten_Don_Vi_Tinh, So_Luong = v_objData.SL_Xuat, Don_Gia = v_objData.Don_Gia_Xuat
                }).ToList();
        return Task.FromResult(v_arrResult);
    }

    public Task Delete_Document_Async(bool p_bIsReceipt, long p_iDocument,
        string p_strUpdatedBy, string p_strFunction, string p_strLogin)
    {
        if (p_bIsReceipt) m_objNhapKhoController.F3001_sp_del_Nhap_Kho_Header(p_iDocument, p_strLogin);
        else m_objXuatKhoController.F3002_sp_del_Xuat_Kho_Header(p_iDocument, p_strLogin);
        return Task.CompletedTask;
    }

    public Task Delete_Document_Detail_Async(bool p_bIsReceipt, long p_iDetail,
        string p_strUpdatedBy, string p_strFunction, string p_strLogin)
    {
        if (p_bIsReceipt) m_objNhapKhoController.F3001_sp_del_Nhap_Kho_Detail(p_iDetail, p_strLogin);
        else m_objXuatKhoController.F3002_sp_del_Xuat_Kho_Detail(p_iDetail, p_strLogin);
        return Task.CompletedTask;
    }

    public Task Post_Document_Async(bool p_bIsReceipt, long p_iDocument,
        string p_strUpdatedBy, string p_strFunction, string p_strLogin)
    {
        m_objSupportController.sp_XNK_Document_Post(p_bIsReceipt, p_iDocument, p_strLogin, p_strUpdatedBy, p_strFunction);
        return Task.CompletedTask;
    }
}
