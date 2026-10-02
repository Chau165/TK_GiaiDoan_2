using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

public class CWarehouseMaster_Controller : CWarehouse_Controller_Base
{
    public Task<List<CWarehouseMaster>> List_Master_Async(string p_strMaster_Type)
    {
        string v_strProcedure;
        switch (p_strMaster_Type)
        {
            case "Kho":
                v_strProcedure = "F2009_sp_sel_List_Kho";
                break;

            case "DonViTinh":
                v_strProcedure = "F2016_sp_sel_List_Don_Vi_Tinh";
                break;

            case "LoaiSanPham":
                v_strProcedure = "F2017_sp_sel_List_Loai_San_Pham";
                break;

            case "SanPham":
                v_strProcedure = "F2018_sp_sel_List_San_Pham";
                break;

            case "NCC":
                v_strProcedure = "F2019_sp_sel_List_NCC";
                break;

            default:
                throw new ArgumentException("Loại danh mục không hợp lệ.", nameof(p_strMaster_Type));
        }

        return Task.FromResult(List_From_Procedure<CWarehouseMaster>(v_strProcedure));
    }

    public Task<CWarehousePagedResult<CWarehouseMaster>> List_Master_Page_Async(string p_strMaster_Type, int p_iPage_Number, int p_iPage_Size, string p_strSearch_Text = "")
    {
        return Task.FromResult(Page_From_Procedure<CWarehouseMaster>("sp_DM_Master_Page", p_strMaster_Type, p_iPage_Number, p_iPage_Size, p_strSearch_Text));
    }

    public Task<List<CWarehouseLookup>> List_Lookup_Async(string p_strMaster_Type)
    {
        string v_strProcedure;
        switch (p_strMaster_Type)
        {
            case "Kho":
                v_strProcedure = "F2009_sp_sel_List_Kho_Lookup";
                break;

            case "DonViTinh":
                v_strProcedure = "F2016_sp_sel_List_Don_Vi_Tinh_Lookup";
                break;

            case "LoaiSanPham":
                v_strProcedure = "F2017_sp_sel_List_Loai_San_Pham_Lookup";
                break;

            case "SanPham":
                v_strProcedure = "F2018_sp_sel_List_San_Pham_Lookup";
                break;

            case "NCC":
                v_strProcedure = "F2019_sp_sel_List_NCC_Lookup";
                break;

            default:
                throw new ArgumentException("Loại danh mục không hợp lệ.", nameof(p_strMaster_Type));
        }

        return Task.FromResult(List_From_Procedure<CWarehouseLookup>(v_strProcedure));
    }

    public Task<CWarehousePagedResult<CWarehouseLookup>> List_Lookup_Page_Async(string p_strMaster_Type, int p_iPage_Number, int p_iPage_Size, string p_strSearch_Text = "")
    {
        return Task.FromResult(Page_From_Procedure<CWarehouseLookup>("sp_DM_Lookup_Page", p_strMaster_Type, p_iPage_Number, p_iPage_Size, p_strSearch_Text));
    }

    public Task Save_Master_Async(string p_strMaster_Type, CWarehouseMaster p_objData,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")
    {
        p_objData.Last_Updated_By = p_strLast_Updated_By;
        p_objData.Last_Updated_By_Function = p_strLast_Updated_By_Function;
        p_objData.Created_By = p_strLast_Updated_By;
        p_objData.Created_By_Function = p_strLast_Updated_By_Function;

        var v_bIsCreate = p_objData.Auto_ID == 0;
        string v_strProcedure;
        switch (p_strMaster_Type)
        {
            case "DonViTinh":
                if (v_bIsCreate)
                {
                    v_strProcedure = "F2016_sp_ins_Don_Vi_Tinh";
                }
                else
                {
                    v_strProcedure = "F2016_sp_upd_Don_Vi_Tinh";
                }
                break;

            case "LoaiSanPham":
                if (v_bIsCreate)
                {
                    v_strProcedure = "F2017_sp_ins_Loai_San_Pham";
                }
                else
                {
                    v_strProcedure = "F2017_sp_upd_Loai_San_Pham";
                }
                break;

            case "SanPham":
                if (v_bIsCreate)
                {
                    v_strProcedure = "F2018_sp_ins_San_Pham";
                }
                else
                {
                    v_strProcedure = "F2018_sp_upd_San_Pham";
                }
                break;

            case "NCC":
                if (v_bIsCreate)
                {
                    v_strProcedure = "F2019_sp_ins_NCC";
                }
                else
                {
                    v_strProcedure = "F2019_sp_upd_NCC";
                }
                break;

            case "Kho":
                if (v_bIsCreate)
                {
                    v_strProcedure = "F2009_sp_ins_Kho";
                }
                else
                {
                    v_strProcedure = "F2009_sp_upd_Kho";
                }
                break;

            default:
                throw new ArgumentException("Loại danh mục không hợp lệ.", nameof(p_strMaster_Type));
        }

        object[] v_arrValue;
        switch (p_strMaster_Type)
        {
            case "DonViTinh":
                v_arrValue = new object[] { p_objData.Auto_ID, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function };
                break;

            case "LoaiSanPham":
                v_arrValue = new object[] { p_objData.Auto_ID, p_objData.Code, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function };
                break;

            case "SanPham":
                v_arrValue = new object[] { p_objData.Auto_ID, p_objData.Code, p_objData.Name, p_objData.Related_ID, p_objData.Related_ID_2, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function };
                break;

            case "NCC":
                v_arrValue = new object[] { p_objData.Auto_ID, p_objData.Code, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function };
                break;

            case "Kho":
                v_arrValue = new object[] { p_objData.Auto_ID, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function };
                break;

            default:
                throw new ArgumentException("Loại danh mục không hợp lệ.", nameof(p_strMaster_Type));
        }

        p_objData.Auto_ID = Scalar_ID(v_strProcedure, v_arrValue);
        return Task.CompletedTask;
    }

    public Task Delete_Master_Async(string p_strMaster_Type, long p_iAuto_ID,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")
    {
        string v_strProcedure;
        switch (p_strMaster_Type)
        {
            case "Kho":
                v_strProcedure = "F2009_sp_del_Kho";
                break;

            case "DonViTinh":
                v_strProcedure = "F2016_sp_del_Don_Vi_Tinh";
                break;

            case "LoaiSanPham":
                v_strProcedure = "F2017_sp_del_Loai_San_Pham";
                break;

            case "SanPham":
                v_strProcedure = "F2018_sp_del_San_Pham";
                break;

            case "NCC":
                v_strProcedure = "F2019_sp_del_NCC";
                break;

            default:
                throw new ArgumentException("Loại danh mục không hợp lệ.", nameof(p_strMaster_Type));
        }

        Execute_Procedure(v_strProcedure, p_iAuto_ID, p_strLast_Updated_By, p_strLast_Updated_By_Function);
        return Task.CompletedTask;
    }
}
