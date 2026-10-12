using System.Data;
using TKS_Thuc_Tap_V11_Data_Access.DataLayer;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.DM;

public class CDM_Kho_User_Controller
{
    public List<CDM_Kho_User> F2015_sp_sel_List_Kho_User(string p_strSearch_Text = "")
    {
        List<CDM_Kho_User> v_arrRes = new List<CDM_Kho_User>();
        DataTable v_dt = new DataTable();

        try
        {
            CSqlHelper.FillDataTable(
                CConfig.TKS_Thuc_Tap_V11_Conn_String,
                v_dt,
                "F2015_sp_sel_List_Kho_User",
                p_strSearch_Text);

            foreach (DataRow v_row in v_dt.Rows)
            {
                CDM_Kho_User v_objRes = CUtility.Map_Row_To_Entity<CDM_Kho_User>(v_row);
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

    public List<CWarehouseLookup> F2015_sp_sel_List_Sys_Thanh_Vien_Cho_Phan_Quyen_Kho()
    {
        List<CWarehouseLookup> v_arrRes = new List<CWarehouseLookup>();
        DataTable v_dt = new DataTable();

        try
        {
            CSqlHelper.FillDataTable(
                CConfig.TKS_Thuc_Tap_V11_Conn_String,
                v_dt,
                "F2015_sp_sel_List_Sys_Thanh_Vien_Cho_Phan_Quyen_Kho");

            foreach (DataRow v_row in v_dt.Rows)
            {
                CWarehouseLookup v_objRes = CUtility.Map_Row_To_Entity<CWarehouseLookup>(v_row);
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

    public long F2015_sp_ins_Kho_User(
        CDM_Kho_User p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        long v_iRes;

        try
        {
            v_iRes = Convert.ToInt64(CSqlHelper.ExecuteScalar(
                CConfig.TKS_Thuc_Tap_V11_Conn_String,
                "F2015_sp_ins_Kho_User",
                p_objData.Auto_ID,
                p_objData.Ma_Dang_Nhap,
                p_objData.Kho_ID,
                p_strLast_Updated_By,
                p_strLast_Updated_By_Function,
                p_strLast_Updated_By,
                p_strLast_Updated_By_Function));
            p_objData.Auto_ID = v_iRes;
        }
        catch (Exception)
        {
            throw;
        }

        return v_iRes;
    }

    public long F2015_sp_upd_Kho_User(
        CDM_Kho_User p_objData,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        long v_iRes;

        try
        {
            v_iRes = Convert.ToInt64(CSqlHelper.ExecuteScalar(
                CConfig.TKS_Thuc_Tap_V11_Conn_String,
                "F2015_sp_upd_Kho_User",
                p_objData.Auto_ID,
                p_objData.Ma_Dang_Nhap,
                p_objData.Kho_ID,
                p_strLast_Updated_By,
                p_strLast_Updated_By_Function,
                p_strLast_Updated_By,
                p_strLast_Updated_By_Function));
            p_objData.Auto_ID = v_iRes;
        }
        catch (Exception)
        {
            throw;
        }

        return v_iRes;
    }

    public void F2015_sp_del_Kho_User(
        long p_iAuto_ID,
        string p_strLast_Updated_By = "",
        string p_strLast_Updated_By_Function = "")
    {
        try
        {
            CSqlHelper.ExecuteNonquery(
                CConfig.TKS_Thuc_Tap_V11_Conn_String,
                "F2015_sp_del_Kho_User",
                p_iAuto_ID,
                p_strLast_Updated_By,
                p_strLast_Updated_By_Function);
        }
        catch (Exception)
        {
            throw;
        }
    }

    public List<CWarehouseLookup> sp_DM_Kho_User_List_Allowed(string p_strCurrent_Login)
    {
        List<CWarehouseLookup> v_arrRes = new List<CWarehouseLookup>();
        DataTable v_dt = new DataTable();

        try
        {
            CSqlHelper.FillDataTable(
                CConfig.TKS_Thuc_Tap_V11_Conn_String,
                v_dt,
                "sp_DM_Kho_User_List_Allowed",
                p_strCurrent_Login);

            foreach (DataRow v_row in v_dt.Rows)
            {
                CWarehouseLookup v_objRes = CUtility.Map_Row_To_Entity<CWarehouseLookup>(v_row);
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

    public CWarehousePagedResult<CDM_Kho_User> sp_DM_Kho_User_Page(
        int p_iPage_Number,
        int p_iPage_Size,
        string p_strSearch_Text = "")
    {
        CWarehousePagedResult<CDM_Kho_User> v_objRes = new CWarehousePagedResult<CDM_Kho_User>();
        DataSet v_ds = new DataSet();

        try
        {
            CSqlHelper.FillDataSet(
                CConfig.TKS_Thuc_Tap_V11_Conn_String,
                v_ds,
                "sp_DM_Kho_User_Page",
                p_iPage_Number,
                p_iPage_Size,
                p_strSearch_Text);

            if (v_ds.Tables.Count < 2 || v_ds.Tables[0].Rows.Count == 0)
                throw new InvalidOperationException("sp_DM_Kho_User_Page must return total count and page data.");

            v_objRes = new CWarehousePagedResult<CDM_Kho_User>
            {
                Total_Count = Convert.ToInt32(v_ds.Tables[0].Rows[0]["Total_Count"])
            };

            foreach (DataRow v_row in v_ds.Tables[1].Rows)
            {
                CDM_Kho_User v_objItem = CUtility.Map_Row_To_Entity<CDM_Kho_User>(v_row);
                v_objRes.Items.Add(v_objItem);
            }
        }
        catch (Exception)
        {
            throw;
        }
        finally
        {
            v_ds.Dispose();
        }

        return v_objRes;
    }
}
