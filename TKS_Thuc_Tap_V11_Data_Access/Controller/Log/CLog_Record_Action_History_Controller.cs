using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TKS_Thuc_Tap_V11_Data_Access.DataLayer;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Log;
using Microsoft.Data.SqlClient;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Log
{
    public class CLog_Record_Action_History_Controller
    {
        public List<CLog_Record_Action_History> FQ_425_RAH_sp_sel_List_By_Created(DateTime? p_dtmFrom, DateTime? p_dtmTo)
        {
            List<CLog_Record_Action_History> v_arrRes = new List<CLog_Record_Action_History>();
            DataTable v_dt = new DataTable();

            try
            {
                p_dtmFrom = CUtility_Date.Convert_To_Dau_Ngay(p_dtmFrom);
                p_dtmTo = CUtility_Date.Convert_To_Cuoi_Ngay(p_dtmTo);

                CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, "FQ_425_RAH_sp_sel_List_By_Created", p_dtmFrom, p_dtmTo);

                foreach (DataRow v_row in v_dt.Rows)
                {
                    CLog_Record_Action_History v_objRes = CUtility.Map_Row_To_Entity<CLog_Record_Action_History>(v_row);
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

        public CLog_Record_Action_History FQ_425_RAH_sp_sel_Get_By_ID(long p_iID)
        {
            CLog_Record_Action_History v_objRes = null;
            DataTable v_dt = new DataTable();

            try
            {
                CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, "FQ_425_RAH_sp_sel_Get_By_ID", p_iID);

                if (v_dt.Rows.Count > 0)
                {
                    v_objRes = CUtility.Map_Row_To_Entity<CLog_Record_Action_History>(v_dt.Rows[0]);
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

            return v_objRes;
        }

        public long FQ_425_RAH_sp_ins_Insert(CLog_Record_Action_History p_objData)
        {
            long v_iRes = CConst.INT_VALUE_NULL;

            try
            {
                v_iRes = Convert.ToInt64(CSqlHelper.ExecuteScalar(CConfig.TKS_Thuc_Tap_V11_Conn_String, "FQ_425_RAH_sp_ins_Insert",
                    p_objData.Ref_ID, p_objData.Ten_Hanh_Dong, p_objData.Ten_Moi_Truong, p_objData.Ma_Chuc_Nang, p_objData.Ten_Chuc_Nang,
                    p_objData.Noi_Dung_Action, p_objData.Created_By));
            }

            catch (Exception)
            {
                throw;
            }

            return v_iRes;
        }

        public long FQ_425_RAH_sp_ins_Insert(SqlConnection p_Conn, SqlTransaction p_Tran, CLog_Record_Action_History p_objData)
        {
            long v_iRes = CConst.INT_VALUE_NULL;

            try
            {
                v_iRes = Convert.ToInt64(CSqlHelper.ExecuteScalar(p_Conn, p_Tran, CConfig.TKS_Thuc_Tap_V11_Conn_String, "FQ_425_RAH_sp_ins_Insert",
                    p_objData.Ref_ID, p_objData.Ten_Hanh_Dong, p_objData.Ten_Moi_Truong, p_objData.Ma_Chuc_Nang, p_objData.Ten_Chuc_Nang,
                    p_objData.Noi_Dung_Action, p_objData.Created_By));
            }

            catch (Exception)
            {
                throw;
            }

            return v_iRes;
        }

        public List<CLog_Record_Action_History> FCommon_Sys_sp_sel_List_Log_Record_Action_History(long p_iRef_ID)
        {
            List<CLog_Record_Action_History> v_arrRes = new List<CLog_Record_Action_History>();
            DataTable v_dt = new DataTable();

            try
            {
                CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, "FCommon_Sys_sp_sel_List_Log_Record_Action_History", p_iRef_ID);

                foreach (DataRow v_row in v_dt.Rows)
                {
                    CLog_Record_Action_History v_objRes = CUtility.Map_Row_To_Entity<CLog_Record_Action_History>(v_row);
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
	}
}
