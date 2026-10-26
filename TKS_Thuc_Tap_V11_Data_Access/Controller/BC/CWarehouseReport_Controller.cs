using System.Data;
using TKS_Thuc_Tap_V11_Data_Access.DataLayer;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.BC;
public class CWarehouseReport_Controller
{
    public List<CWarehouseDetailReport> F9002_sp_Chi_Tiet_Nhap(DateTime p_dtmFrom, DateTime p_dtmTo, string p_strCurrent_Login = "", long? p_iWarehouse_ID = null)
    {
        List<CWarehouseDetailReport> v_arrRes = new List<CWarehouseDetailReport>();
        DataTable v_dt = new DataTable();
        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, "F9002_sp_Chi_Tiet_Nhap", p_dtmFrom.Date, p_dtmTo.Date, p_strCurrent_Login, p_iWarehouse_ID);
            foreach (DataRow v_row in v_dt.Rows)
            {
                CWarehouseDetailReport v_objRes = CWMS_Utility.Map_Warehouse_Row<CWarehouseDetailReport>(v_row);
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

    public List<CWarehouseDetailReport> F9002_sp_Chi_Tiet_Xuat(DateTime p_dtmFrom, DateTime p_dtmTo, string p_strCurrent_Login = "", long? p_iWarehouse_ID = null)
    {
        List<CWarehouseDetailReport> v_arrRes = new List<CWarehouseDetailReport>();
        DataTable v_dt = new DataTable();
        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, "F9002_sp_Chi_Tiet_Xuat", p_dtmFrom.Date, p_dtmTo.Date, p_strCurrent_Login, p_iWarehouse_ID);
            foreach (DataRow v_row in v_dt.Rows)
            {
                CWarehouseDetailReport v_objRes = CWMS_Utility.Map_Warehouse_Row<CWarehouseDetailReport>(v_row);
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

    public CWarehousePagedResult<CWarehouseDetailReport> F9002_sp_Chi_Tiet_Nhap_Page(DateTime p_dtmFrom, DateTime p_dtmTo, int p_iPage_Number, int p_iPage_Size, string p_strCurrent_Login = "", long? p_iWarehouse_ID = null)
    {
        CWarehousePagedResult<CWarehouseDetailReport> v_objRes = new CWarehousePagedResult<CWarehouseDetailReport>();
        DataSet v_ds = new DataSet();
        try
        {
            TKS_Thuc_Tap_V11_Data_Access.Controller.Common.CWarehouse_Controller_Base.Fill_Page_Data(v_ds, "F9002_sp_Chi_Tiet_Nhap_Page", p_dtmFrom.Date, p_dtmTo.Date, p_iPage_Number, p_iPage_Size, p_strCurrent_Login, p_iWarehouse_ID);
            if (v_ds.Tables.Count < 2 || v_ds.Tables[0].Rows.Count == 0)
            {
                throw new InvalidOperationException("sp_BC_Chi_Tiet_Nhap_Page must return total count and page data.");
            }

            v_objRes = Map_Page<CWarehouseDetailReport>(v_ds);
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

    public CWarehousePagedResult<CWarehouseDetailReport> F9002_sp_Chi_Tiet_Xuat_Page(DateTime p_dtmFrom, DateTime p_dtmTo, int p_iPage_Number, int p_iPage_Size, string p_strCurrent_Login = "", long? p_iWarehouse_ID = null)
    {
        CWarehousePagedResult<CWarehouseDetailReport> v_objRes = new CWarehousePagedResult<CWarehouseDetailReport>();
        DataSet v_ds = new DataSet();
        try
        {
            TKS_Thuc_Tap_V11_Data_Access.Controller.Common.CWarehouse_Controller_Base.Fill_Page_Data(v_ds, "F9002_sp_Chi_Tiet_Xuat_Page", p_dtmFrom.Date, p_dtmTo.Date, p_iPage_Number, p_iPage_Size, p_strCurrent_Login, p_iWarehouse_ID);
            if (v_ds.Tables.Count < 2 || v_ds.Tables[0].Rows.Count == 0)
            {
                throw new InvalidOperationException("sp_BC_Chi_Tiet_Xuat_Page must return total count and page data.");
            }

            v_objRes = Map_Page<CWarehouseDetailReport>(v_ds);
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

    public List<CWarehouseInventoryReport> F9002_sp_Xuat_Nhap_Ton(DateTime p_dtmFrom, DateTime p_dtmTo, string p_strCurrent_Login = "", long? p_iWarehouse_ID = null)
    {
        List<CWarehouseInventoryReport> v_arrRes = new List<CWarehouseInventoryReport>();
        DataTable v_dt = new DataTable();
        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, "F9002_sp_Xuat_Nhap_Ton", p_dtmFrom.Date, p_dtmTo.Date, p_strCurrent_Login, p_iWarehouse_ID);
            foreach (DataRow v_row in v_dt.Rows)
            {
                CWarehouseInventoryReport v_objRes = CWMS_Utility.Map_Warehouse_Row<CWarehouseInventoryReport>(v_row);
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

    public CWarehousePagedResult<CWarehouseInventoryReport> F9002_sp_Xuat_Nhap_Ton_Page(DateTime p_dtmFrom, DateTime p_dtmTo, int p_iPage_Number, int p_iPage_Size, string p_strCurrent_Login = "", long? p_iWarehouse_ID = null)
    {
        CWarehousePagedResult<CWarehouseInventoryReport> v_objRes = new CWarehousePagedResult<CWarehouseInventoryReport>();
        DataSet v_ds = new DataSet();
        try
        {
            TKS_Thuc_Tap_V11_Data_Access.Controller.Common.CWarehouse_Controller_Base.Fill_Page_Data(v_ds, "F9002_sp_Xuat_Nhap_Ton_Page", p_dtmFrom.Date, p_dtmTo.Date, p_iPage_Number, p_iPage_Size, p_strCurrent_Login, p_iWarehouse_ID);
            if (v_ds.Tables.Count < 2 || v_ds.Tables[0].Rows.Count == 0)
            {
                throw new InvalidOperationException("sp_BC_Xuat_Nhap_Ton_Page must return total count and page data.");
            }

            v_objRes = Map_Page<CWarehouseInventoryReport>(v_ds);
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

    public CWarehousePagedResult<CWarehouseInventoryReport> F9001_sp_Ton_Kho_Hien_Tai_Page(int p_iPage_Number, int p_iPage_Size, string p_strCurrent_Login, long? p_iWarehouse_ID = null)
    {
        CWarehousePagedResult<CWarehouseInventoryReport> v_objRes = new CWarehousePagedResult<CWarehouseInventoryReport>();
        DataSet v_ds = new DataSet();
        try
        {
            TKS_Thuc_Tap_V11_Data_Access.Controller.Common.CWarehouse_Controller_Base.Fill_Page_Data(v_ds, "F9001_sp_Ton_Kho_Hien_Tai_Page", p_iPage_Number, p_iPage_Size, p_strCurrent_Login, p_iWarehouse_ID);
            if (v_ds.Tables.Count < 2 || v_ds.Tables[0].Rows.Count == 0)
            {
                throw new InvalidOperationException("sp_BC_Ton_Kho_Hien_Tai_Page must return total count and page data.");
            }

            v_objRes = Map_Page<CWarehouseInventoryReport>(v_ds);
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

    private static CWarehousePagedResult<T> Map_Page<T>(DataSet p_ds)
        where T : new()
    {
        CWarehousePagedResult<T> v_objRes = new CWarehousePagedResult<T>
        {
            Total_Count = Convert.ToInt32(p_ds.Tables[0].Rows[0]["Total_Count"])
        };
        foreach (DataRow v_row in p_ds.Tables[1].Rows)
        {
            T v_objItem = CUtility.Map_Row_To_Entity<T>(v_row);
            v_objRes.Items.Add(v_objItem);
        }

        return v_objRes;
    }
}
