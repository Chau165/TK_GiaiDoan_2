using System.Data;
using TKS_Thuc_Tap_V11_Data_Access.DataLayer;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

public class CWarehousePermission_Controller : CWarehouse_Controller_Base
{
    public Task<List<CWarehouseLookup>> List_Authorized_Warehouses_Async(string p_strCurrent_Login)
    {
        return Task.FromResult(List_From_Procedure<CWarehouseLookup>("sp_DM_Kho_User_List_Allowed", p_strCurrent_Login));
    }

    public Task<List<CWarehouseLookup>> List_User_Lookup_Async()
    {
        return Task.FromResult(List_From_Procedure<CWarehouseLookup>("F2015_sp_sel_List_Sys_Thanh_Vien_Cho_Phan_Quyen_Kho"));
    }

    public Task<List<CWarehousePermission>> List_Kho_User_Async()
    {
        return Task.FromResult(List_Permission_From_Procedure("F2015_sp_sel_List_Kho_User"));
    }

    public Task<CWarehousePagedResult<CWarehousePermission>> List_Kho_User_Page_Async(
        int p_iPage_Number, int p_iPage_Size, string p_strSearch_Text = "")
    {
        return Task.FromResult(Page_Permission_From_Procedure(
            "sp_DM_Kho_User_Page", p_iPage_Number, p_iPage_Size, p_strSearch_Text));
    }

    public Task Save_Kho_User_Async(CWarehousePermission p_objData,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")
    {
        var v_bIsCreate = p_objData.Permission_ID == 0;
        string v_strProcedure;
        if (v_bIsCreate)
        {
            v_strProcedure = "F2015_sp_ins_Kho_User";
        }
        else
        {
            v_strProcedure = "F2015_sp_upd_Kho_User";
        }

        p_objData.Permission_ID = Scalar_ID(v_strProcedure,
            p_objData.Permission_ID,
            p_objData.Login_Name,
            p_objData.Warehouse_ID,
            p_strLast_Updated_By,
            p_strLast_Updated_By_Function,
            p_strLast_Updated_By,
            p_strLast_Updated_By_Function);

        return Task.CompletedTask;
    }

    public Task Delete_Kho_User_Async(long p_iPermission_ID,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")
    {
        Execute_Procedure("F2015_sp_del_Kho_User", p_iPermission_ID,
            p_strLast_Updated_By, p_strLast_Updated_By_Function);
        return Task.CompletedTask;
    }

    private static List<CWarehousePermission> List_Permission_From_Procedure(
        string p_strProcedure, params object[] p_arrValue)
    {
        using var v_dtData = new DataTable();
        CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dtData, p_strProcedure, p_arrValue);

        var v_arrRes = new List<CWarehousePermission>();
        foreach (DataRow v_row in v_dtData.Rows)
            v_arrRes.Add(Map_Permission(v_row));

        return v_arrRes;
    }

    private static CWarehousePagedResult<CWarehousePermission> Page_Permission_From_Procedure(
        string p_strProcedure, params object[] p_arrValue)
    {
        using var v_dsData = new DataSet();
        CSqlHelper.FillDataSet(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dsData, p_strProcedure, p_arrValue);

        if (v_dsData.Tables.Count < 2 || v_dsData.Tables[0].Rows.Count == 0)
            throw new InvalidOperationException($"{p_strProcedure} must return total count and page data.");

        var v_objResult = new CWarehousePagedResult<CWarehousePermission>
        {
            Total_Count = Convert.ToInt32(v_dsData.Tables[0].Rows[0]["Total_Count"])
        };

        foreach (DataRow v_row in v_dsData.Tables[1].Rows)
            v_objResult.Items.Add(Map_Permission(v_row));

        return v_objResult;
    }

    private static CWarehousePermission Map_Permission(DataRow p_objRow)
    {
        return new CWarehousePermission
        {
            Permission_ID = CUtility.Convert_To_Int64(p_objRow["Auto_ID"]),
            Login_Name = CUtility.Convert_To_String(p_objRow["Login_Name"]),
            User_Name = CUtility.Convert_To_String(p_objRow["Name"]),
            Warehouse_ID = CUtility.Convert_To_Int64(p_objRow["Related_ID"]),
            Warehouse_Name = CUtility.Convert_To_String(p_objRow["Ghi_Chu"])
        };
    }
}
