using Microsoft.Data.SqlClient;
using System.Data;
using TKS_Thuc_Tap_V11_Data_Access.DataLayer;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.DM;

public class CDM_San_Pham_Controller
{
    public List<CDM_San_Pham> F2018_sp_sel_List_San_Pham()
    {
        List<CDM_San_Pham> v_arrRes = new List<CDM_San_Pham>();
        DataTable v_dt = new DataTable();

        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, "F2018_sp_sel_List_San_Pham");

            foreach (DataRow v_row in v_dt.Rows)
            {
                CDM_San_Pham v_objRes = CUtility.Map_Row_To_Entity<CDM_San_Pham>(v_row);
                v_objRes.Ma_San_Pham = Convert.ToString(v_row["Code"]) ?? "";
                v_objRes.Ten_San_Pham = Convert.ToString(v_row["Name"]) ?? "";
                v_objRes.Loai_San_Pham_ID = Convert.ToInt64(v_row["Related_ID"]);
                v_objRes.Don_Vi_Tinh_ID = Convert.ToInt64(v_row["Related_ID_2"]);
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

    public List<CDM_San_Pham> F2018_sp_sel_List_San_Pham_Lookup()
    {
        List<CDM_San_Pham> v_arrRes = new List<CDM_San_Pham>();
        DataTable v_dt = new DataTable();

        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, "F2018_sp_sel_List_San_Pham_Lookup");

            foreach (DataRow v_row in v_dt.Rows)
            {
                CDM_San_Pham v_objRes = CUtility.Map_Row_To_Entity<CDM_San_Pham>(v_row);
                v_objRes.Ma_San_Pham = Convert.ToString(v_row["Code"] ) ?? "";
                v_objRes.Ten_San_Pham = Convert.ToString(v_row["Name"] ) ?? "";
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

    public long F2018_sp_ins_San_Pham(
        CDM_San_Pham p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        long v_iRes = 0;

        try
        {
            Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
            v_iRes = Convert.ToInt64(CSqlHelper.ExecuteScalar(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F2018_sp_ins_San_Pham", p_objData.Auto_ID, p_objData.Ma_San_Pham, p_objData.Ten_San_Pham, p_objData.Loai_San_Pham_ID, p_objData.Don_Vi_Tinh_ID, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function));
        }
        catch (Exception)
        {
            throw;
        }

        return v_iRes;
    }

    public long F2018_sp_ins_San_Pham(
        SqlConnection p_conn,
        SqlTransaction p_trans,
        CDM_San_Pham p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        long v_iRes = 0;

        try
        {
            Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
            v_iRes = Convert.ToInt64(CSqlHelper.ExecuteScalar(p_conn, p_trans, CConfig.TKS_Thuc_Tap_V11_Conn_String, "F2018_sp_ins_San_Pham", p_objData.Auto_ID, p_objData.Ma_San_Pham, p_objData.Ten_San_Pham, p_objData.Loai_San_Pham_ID, p_objData.Don_Vi_Tinh_ID, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function));
        }
        catch (Exception)
        {
            throw;
        }

        return v_iRes;
    }

    public void F2018_sp_upd_San_Pham(
        CDM_San_Pham p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        try
        {
            Set_Audit_Fields(p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
            p_objData.Auto_ID = Convert.ToInt64(CSqlHelper.ExecuteScalar(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F2018_sp_upd_San_Pham", p_objData.Auto_ID, p_objData.Ma_San_Pham, p_objData.Ten_San_Pham, p_objData.Loai_San_Pham_ID, p_objData.Don_Vi_Tinh_ID, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function));
        }
        catch (Exception)
        {
            throw;
        }
    }

    public void F2018_sp_del_San_Pham(
        long p_iAuto_ID,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        try
        {
            CSqlHelper.ExecuteNonquery(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F2018_sp_del_San_Pham", p_iAuto_ID, p_strLast_Updated_By, p_strLast_Updated_By_Function);
        }
        catch (Exception)
        {
            throw;
        }
    }

    private static void Set_Audit_Fields(
        CDM_San_Pham p_objData,
        string p_strLast_Updated_By,
        string p_strLast_Updated_By_Function)
    {
        p_objData.Last_Updated_By = p_strLast_Updated_By;
        p_objData.Last_Updated_By_Function = p_strLast_Updated_By_Function;
        p_objData.Created_By = p_strLast_Updated_By;
        p_objData.Created_By_Function = p_strLast_Updated_By_Function;
    }
}
