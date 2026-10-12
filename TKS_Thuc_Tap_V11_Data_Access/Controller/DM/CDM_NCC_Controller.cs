using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.DM;

public class CDM_NCC_Controller : CWarehouse_Controller_Base
{
    public Task<List<CWarehouseMaster>> F2019_sp_sel_List_NCC() =>
        Task.FromResult(List_From_Procedure<CWarehouseMaster>("F2019_sp_sel_List_NCC"));

    public Task<List<CWarehouseLookup>> F2019_sp_sel_List_NCC_Lookup() =>
        Task.FromResult(List_From_Procedure<CWarehouseLookup>("F2019_sp_sel_List_NCC_Lookup"));

    public Task F2019_sp_ins_NCC(
        CWarehouseMaster p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
        p_objData.Auto_ID = Scalar_ID("F2019_sp_ins_NCC", p_objData.Auto_ID, p_objData.Code, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function);
        return Task.CompletedTask;
    }

    public Task F2019_sp_ins_NCC(
        SqlConnection p_conn,
        SqlTransaction p_trans,
        CWarehouseMaster p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
        p_objData.Auto_ID = Scalar_ID(p_conn, p_trans, "F2019_sp_ins_NCC", p_objData.Auto_ID, p_objData.Code, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function);
        return Task.CompletedTask;
    }

    public Task F2019_sp_upd_NCC(
        CWarehouseMaster p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
        p_objData.Auto_ID = Scalar_ID("F2019_sp_upd_NCC", p_objData.Auto_ID, p_objData.Code, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function);
        return Task.CompletedTask;
    }

    public Task F2019_sp_del_NCC(
        long p_iAuto_ID,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        Execute_Procedure("F2019_sp_del_NCC", p_iAuto_ID, p_strLast_Updated_By, p_strLast_Updated_By_Function);
        return Task.CompletedTask;
    }

    private static void Set_Audit_Fields(
        CWarehouseMaster p_objData,
        string p_strLast_Updated_By,
        string p_strLast_Updated_By_Function)
    {
        p_objData.Last_Updated_By = p_strLast_Updated_By;
        p_objData.Last_Updated_By_Function = p_strLast_Updated_By_Function;
        p_objData.Created_By = p_strLast_Updated_By;
        p_objData.Created_By_Function = p_strLast_Updated_By_Function;
    }
}
