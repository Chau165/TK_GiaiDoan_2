using Microsoft.Data.SqlClient;
using System.Data;
using TKS_Thuc_Tap_V11_Data_Access.DataLayer;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.DM;

public class CDM_Loai_San_Pham_Controller
{
    public List<CDM_Loai_San_Pham> F2017_sp_sel_List_Loai_San_Pham()
    {
        List<CDM_Loai_San_Pham> v_arrRes = new List<CDM_Loai_San_Pham>();
        DataTable v_dt = new DataTable();

        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, "F2017_sp_sel_List_Loai_San_Pham");

            foreach (DataRow v_row in v_dt.Rows)
            {
                CDM_Loai_San_Pham v_objRes = CUtility.Map_Row_To_Entity<CDM_Loai_San_Pham>(v_row);
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

    public List<CDM_Loai_San_Pham> F2017_sp_sel_List_Loai_San_Pham_For_Cache()
    {
        List<CDM_Loai_San_Pham> v_arrRes = new List<CDM_Loai_San_Pham>();
        DataTable v_dt = new DataTable();

        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, "F2017_sp_sel_List_Loai_San_Pham_For_Cache");

            foreach (DataRow v_row in v_dt.Rows)
            {
                CDM_Loai_San_Pham v_objRes = CUtility.Map_Row_To_Entity<CDM_Loai_San_Pham>(v_row);
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

    public List<CDM_Loai_San_Pham> F2017_sp_sel_List_Loai_San_Pham_Lookup()
    {
        List<CDM_Loai_San_Pham> v_arrRes = new List<CDM_Loai_San_Pham>();
        DataTable v_dt = new DataTable();

        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, "F2017_sp_sel_List_Loai_San_Pham_Lookup");

            foreach (DataRow v_row in v_dt.Rows)
            {
                CDM_Loai_San_Pham v_objRes = CUtility.Map_Row_To_Entity<CDM_Loai_San_Pham>(v_row);
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

    public long F2017_sp_ins_Loai_San_Pham(
        CDM_Loai_San_Pham p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        long v_iRes = 0;

        try
        {
            Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
            v_iRes = Convert.ToInt64(CSqlHelper.ExecuteScalar(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F2017_sp_ins_Loai_San_Pham", p_objData.Auto_ID, p_objData.Ma_LSP, p_objData.Ten_LSP, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function));
        }
        catch (Exception)
        {
            throw;
        }

        return v_iRes;
    }

    public long F2017_sp_ins_Loai_San_Pham(
        SqlConnection p_conn,
        SqlTransaction p_trans,
        CDM_Loai_San_Pham p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        long v_iRes = 0;

        try
        {
            Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
            v_iRes = Convert.ToInt64(CSqlHelper.ExecuteScalar(p_conn, p_trans, CConfig.TKS_Thuc_Tap_V11_Conn_String, "F2017_sp_ins_Loai_San_Pham", p_objData.Auto_ID, p_objData.Ma_LSP, p_objData.Ten_LSP, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function));
        }
        catch (Exception)
        {
            throw;
        }

        return v_iRes;
    }

    public void F2017_sp_upd_Loai_San_Pham(
        CDM_Loai_San_Pham p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        try
        {
            Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
            p_objData.Auto_ID = Convert.ToInt64(CSqlHelper.ExecuteScalar(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F2017_sp_upd_Loai_San_Pham", p_objData.Auto_ID, p_objData.Ma_LSP, p_objData.Ten_LSP, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function));
        }
        catch (Exception)
        {
            throw;
        }
    }

    public void F2017_sp_del_Loai_San_Pham(
        long p_iAuto_ID,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        try
        {
            CSqlHelper.ExecuteNonquery(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F2017_sp_del_Loai_San_Pham", p_iAuto_ID, p_strLast_Updated_By, p_strLast_Updated_By_Function);
        }
        catch (Exception)
        {
            throw;
        }
    }

    private static void Set_Audit_Fields(
        CDM_Loai_San_Pham p_objData,
        string p_strLast_Updated_By,
        string p_strLast_Updated_By_Function)
    {
        p_objData.Last_Updated_By = p_strLast_Updated_By;
        p_objData.Last_Updated_By_Function = p_strLast_Updated_By_Function;
        p_objData.Created_By = p_strLast_Updated_By;
        p_objData.Created_By_Function = p_strLast_Updated_By_Function;
    }
}
