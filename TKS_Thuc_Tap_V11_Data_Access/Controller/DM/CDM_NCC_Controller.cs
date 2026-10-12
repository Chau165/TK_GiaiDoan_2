using Microsoft.Data.SqlClient;
using System.Data;
using TKS_Thuc_Tap_V11_Data_Access.DataLayer;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.DM;

public class CDM_NCC_Controller
{
    public List<CDM_NCC> F2019_sp_sel_List_NCC()
    {
        List<CDM_NCC> v_arrRes = new List<CDM_NCC>();
        DataTable v_dt = new DataTable();

        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, "F2019_sp_sel_List_NCC");

            foreach (DataRow v_row in v_dt.Rows)
            {
                CDM_NCC v_objRes = CUtility.Map_Row_To_Entity<CDM_NCC>(v_row);
                v_arrRes.Add(v_objRes);
            }
        }
        catch (Exception)
        {
            throw;
        }
        finally
        {
            v_dt.Dispose();
        }

        return v_arrRes;
    }

    public List<CDM_NCC> F2019_sp_sel_List_NCC_For_Cache()
    {
        List<CDM_NCC> v_arrRes = new List<CDM_NCC>();
        DataTable v_dt = new DataTable();

        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, "F2019_sp_sel_List_NCC_For_Cache");

            foreach (DataRow v_row in v_dt.Rows)
            {
                CDM_NCC v_objRes = CUtility.Map_Row_To_Entity<CDM_NCC>(v_row);
                v_arrRes.Add(v_objRes);
            }
        }
        catch (Exception)
        {
            throw;
        }
        finally
        {
            v_dt.Dispose();
        }

        return v_arrRes;
    }

    public List<CDM_NCC> F2019_sp_sel_List_NCC_Lookup()
    {
        List<CDM_NCC> v_arrRes = new List<CDM_NCC>();
        DataTable v_dt = new DataTable();

        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, "F2019_sp_sel_List_NCC_Lookup");

            foreach (DataRow v_row in v_dt.Rows)
            {
                CDM_NCC v_objRes = CUtility.Map_Row_To_Entity<CDM_NCC>(v_row);
                v_arrRes.Add(v_objRes);
            }
        }
        catch (Exception)
        {
            throw;
        }
        finally
        {
            v_dt.Dispose();
        }

        return v_arrRes;
    }

    public long F2019_sp_ins_NCC(
        CDM_NCC p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        long v_iRes = 0;

        try
        {
            Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
            v_iRes = Convert.ToInt64(CSqlHelper.ExecuteScalar(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F2019_sp_ins_NCC", p_objData.Auto_ID, p_objData.Ma_NCC, p_objData.Ten_NCC, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function));
        }
        catch (Exception)
        {
            throw;
        }

        return v_iRes;
    }

    public long F2019_sp_ins_NCC(
        SqlConnection p_conn,
        SqlTransaction p_trans,
        CDM_NCC p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        long v_iRes = 0;

        try
        {
            Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
            v_iRes = Convert.ToInt64(CSqlHelper.ExecuteScalar(p_conn, p_trans, CConfig.TKS_Thuc_Tap_V11_Conn_String, "F2019_sp_ins_NCC", p_objData.Auto_ID, p_objData.Ma_NCC, p_objData.Ten_NCC, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function));
        }
        catch (Exception)
        {
            throw;
        }

        return v_iRes;
    }

    public void F2019_sp_upd_NCC(
        CDM_NCC p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        try
        {
            Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
            p_objData.Auto_ID = Convert.ToInt64(CSqlHelper.ExecuteScalar(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F2019_sp_upd_NCC", p_objData.Auto_ID, p_objData.Ma_NCC, p_objData.Ten_NCC, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function));
        }
        catch (Exception)
        {
            throw;
        }
    }

    public void F2019_sp_del_NCC(
        long p_iAuto_ID,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        try
        {
            CSqlHelper.ExecuteNonquery(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F2019_sp_del_NCC", p_iAuto_ID, p_strLast_Updated_By, p_strLast_Updated_By_Function);
        }
        catch (Exception)
        {
            throw;
        }
    }

    private static void Set_Audit_Fields(
        CDM_NCC p_objData,
        string p_strLast_Updated_By,
        string p_strLast_Updated_By_Function)
    {
        p_objData.Last_Updated_By = p_strLast_Updated_By;
        p_objData.Last_Updated_By_Function = p_strLast_Updated_By_Function;
        p_objData.Created_By = p_strLast_Updated_By;
        p_objData.Created_By_Function = p_strLast_Updated_By_Function;
    }
}
