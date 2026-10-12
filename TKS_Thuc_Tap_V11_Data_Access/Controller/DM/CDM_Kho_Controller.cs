using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.DM;

public class CDM_Kho_Controller : CWarehouse_Controller_Base
{
    public Task<List<CWarehouseMaster>> F2009_sp_sel_List_Kho() =>
        Task.FromResult(List_From_Procedure<CWarehouseMaster>("F2009_sp_sel_List_Kho"));

    public Task<List<CWarehouseLookup>> F2009_sp_sel_List_Kho_Lookup() =>
        Task.FromResult(List_From_Procedure<CWarehouseLookup>("F2009_sp_sel_List_Kho_Lookup"));

    public Task F2009_sp_ins_Kho(
        CWarehouseMaster p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
        p_objData.Auto_ID = Scalar_ID("F2009_sp_ins_Kho", p_objData.Auto_ID, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function);
        return Task.CompletedTask;
    }

    public Task F2009_sp_ins_Kho(
        SqlConnection p_conn,
        SqlTransaction p_trans,
        CWarehouseMaster p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
        p_objData.Auto_ID = Scalar_ID(p_conn, p_trans, "F2009_sp_ins_Kho", p_objData.Auto_ID, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function);
        return Task.CompletedTask;
    }

    public Task F2009_sp_upd_Kho(
        CWarehouseMaster p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
        p_objData.Auto_ID = Scalar_ID("F2009_sp_upd_Kho", p_objData.Auto_ID, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function);
        return Task.CompletedTask;
    }

    public Task F2009_sp_del_Kho(
        long p_iAuto_ID,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        Execute_Procedure("F2009_sp_del_Kho", p_iAuto_ID, p_strLast_Updated_By, p_strLast_Updated_By_Function);
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
