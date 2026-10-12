using Microsoft.Data.SqlClient;
using System.Data;
using TKS_Thuc_Tap_V11_Data_Access.DataLayer;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.DM;

public class CDM_Kho_Controller
{
    public List<CDM_Kho> F2009_sp_sel_List_Kho()
    {
        List<CDM_Kho> v_arrRes = new List<CDM_Kho>();
        DataTable v_dt = new DataTable();

        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, "F2009_sp_sel_List_Kho");

            foreach (DataRow v_row in v_dt.Rows)
            {
                CDM_Kho v_objRes = CUtility.Map_Row_To_Entity<CDM_Kho>(v_row);
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

    public List<CDM_Kho> F2009_sp_sel_List_Kho_For_Cache()
    {
        List<CDM_Kho> v_arrRes = new List<CDM_Kho>();
        DataTable v_dt = new DataTable();

        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, "F2009_sp_sel_List_Kho_For_Cache");

            foreach (DataRow v_row in v_dt.Rows)
            {
                CDM_Kho v_objRes = CUtility.Map_Row_To_Entity<CDM_Kho>(v_row);
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

    public List<CDM_Kho> F2009_sp_sel_List_Kho_Lookup()
    {
        List<CDM_Kho> v_arrRes = new List<CDM_Kho>();
        DataTable v_dt = new DataTable();

        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, "F2009_sp_sel_List_Kho_Lookup");

            foreach (DataRow v_row in v_dt.Rows)
            {
                CDM_Kho v_objRes = CUtility.Map_Row_To_Entity<CDM_Kho>(v_row);
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

    public long F2009_sp_ins_Kho(
        CDM_Kho p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        long v_iRes = 0;

        try
        {
            Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
            v_iRes = Convert.ToInt64(CSqlHelper.ExecuteScalar(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F2009_sp_ins_Kho", p_objData.Auto_ID, p_objData.Ten_Kho, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function));
        }
        catch (Exception)
        {
            throw;
        }

        return v_iRes;
    }

    public long F2009_sp_ins_Kho(
        SqlConnection p_conn,
        SqlTransaction p_trans,
        CDM_Kho p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        long v_iRes = 0;

        try
        {
            Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
            v_iRes = Convert.ToInt64(CSqlHelper.ExecuteScalar(p_conn, p_trans, CConfig.TKS_Thuc_Tap_V11_Conn_String, "F2009_sp_ins_Kho", p_objData.Auto_ID, p_objData.Ten_Kho, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function));
        }
        catch (Exception)
        {
            throw;
        }

        return v_iRes;
    }

    public void F2009_sp_upd_Kho(
        CDM_Kho p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        try
        {
            Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
            p_objData.Auto_ID = Convert.ToInt64(CSqlHelper.ExecuteScalar(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F2009_sp_upd_Kho", p_objData.Auto_ID, p_objData.Ten_Kho, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function));
        }
        catch (Exception)
        {
            throw;
        }
    }

    public void F2009_sp_del_Kho(
        long p_iAuto_ID,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        try
        {
            CSqlHelper.ExecuteNonquery(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F2009_sp_del_Kho", p_iAuto_ID, p_strLast_Updated_By, p_strLast_Updated_By_Function);
        }
        catch (Exception)
        {
            throw;
        }
    }

    private static void Set_Audit_Fields(
        CDM_Kho p_objData,
        string p_strLast_Updated_By,
        string p_strLast_Updated_By_Function)
    {
        p_objData.Last_Updated_By = p_strLast_Updated_By;
        p_objData.Last_Updated_By_Function = p_strLast_Updated_By_Function;
        p_objData.Created_By = p_strLast_Updated_By;
        p_objData.Created_By_Function = p_strLast_Updated_By_Function;
    }
}
