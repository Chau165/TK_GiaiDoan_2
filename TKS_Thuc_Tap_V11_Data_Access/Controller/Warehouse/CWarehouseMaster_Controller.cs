using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

public class CWarehouseMaster_Controller : CWarehouse_Controller_Base
{
    public Task<List<CWarehouseMaster>> List_Master_Async(string p_strMaster_Type)
    {
        if (p_strMaster_Type == "KhoUser")
            return Task.FromResult(List_From_Procedure<CWarehouseMaster>("sp_DM_Kho_User_List"));

        return Task.FromResult(List_From_Procedure<CWarehouseMaster>("sp_DM_Master_List", p_strMaster_Type));
    }

    public Task<CWarehousePagedResult<CWarehouseMaster>> List_Master_Page_Async(string p_strMaster_Type, int p_iPage_Number, int p_iPage_Size, string p_strSearch_Text = "")
    {
        if (p_strMaster_Type == "KhoUser")
            return Task.FromResult(Page_From_Procedure<CWarehouseMaster>("sp_DM_Kho_User_Page", p_iPage_Number, p_iPage_Size, p_strSearch_Text));

        return Task.FromResult(Page_From_Procedure<CWarehouseMaster>("sp_DM_Master_Page", p_strMaster_Type, p_iPage_Number, p_iPage_Size, p_strSearch_Text));
    }

    public Task<List<CWarehouseLookup>> List_Lookup_Async(string p_strMaster_Type)
    {
        return Task.FromResult(List_From_Procedure<CWarehouseLookup>("sp_DM_Lookup_List", p_strMaster_Type));
    }

    public Task<List<CWarehouseLookup>> List_Authorized_Warehouses_Async(string p_strCurrent_Login)
    {
        return Task.FromResult(List_From_Procedure<CWarehouseLookup>("sp_DM_Kho_User_List_Allowed", p_strCurrent_Login));
    }

    public Task<List<CWarehouseLookup>> List_User_Lookup_Async()
    {
        return Task.FromResult(List_From_Procedure<CWarehouseLookup>("sp_DM_Kho_User_User_List"));
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

        var v_strProcedure = p_strMaster_Type switch
        {
            "DonViTinh" => "sp_DM_Don_Vi_Tinh_Save",
            "LoaiSanPham" => "sp_DM_Loai_San_Pham_Save",
            "SanPham" => "sp_DM_San_Pham_Save",
            "NCC" => "sp_DM_NCC_Save",
            "Kho" => "sp_DM_Kho_Save",
            "KhoUser" => "sp_DM_Kho_User_Save",
            _ => throw new ArgumentException("Loại danh mục không hợp lệ.", nameof(p_strMaster_Type))
        };

        var v_arrValue = p_strMaster_Type switch
        {
            "DonViTinh" => new object[] { p_objData.Auto_ID, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function },
            "LoaiSanPham" => new object[] { p_objData.Auto_ID, p_objData.Code, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function },
            "SanPham" => new object[] { p_objData.Auto_ID, p_objData.Code, p_objData.Name, p_objData.Related_ID, p_objData.Related_ID_2, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function },
            "NCC" => new object[] { p_objData.Auto_ID, p_objData.Code, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function },
            "Kho" => new object[] { p_objData.Auto_ID, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function },
            "KhoUser" => new object[] { p_objData.Auto_ID, p_objData.Login_Name, p_objData.Related_ID, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function },
            _ => throw new ArgumentException("Loại danh mục không hợp lệ.", nameof(p_strMaster_Type))
        };

        p_objData.Auto_ID = Scalar_ID(v_strProcedure, v_arrValue);
        return Task.CompletedTask;
    }

    public Task Delete_Master_Async(string p_strMaster_Type, long p_iAuto_ID,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")
    {
        Execute_Procedure("sp_DM_Delete", p_strMaster_Type, p_iAuto_ID, p_strLast_Updated_By, p_strLast_Updated_By_Function);
        return Task.CompletedTask;
    }
}
