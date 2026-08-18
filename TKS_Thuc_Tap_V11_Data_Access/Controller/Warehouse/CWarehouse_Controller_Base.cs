using System.Data;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.DataLayer;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

public abstract class CWarehouse_Controller_Base
{
    protected static List<T> List_From_Procedure<T>(string p_strProcedure, params object[] p_arrValue) where T : new()
    {
        using var v_dtData = new DataTable();
        CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dtData, p_strProcedure, p_arrValue);

        var v_arrRes = new List<T>();
        foreach (DataRow v_row in v_dtData.Rows)
            v_arrRes.Add(CUtility.Map_Row_To_Entity<T>(v_row));

        return v_arrRes;
    }

    protected static long Scalar_ID(string p_strProcedure, params object[] p_arrValue)
    {
        return Convert.ToInt64(CSqlHelper.ExecuteScalar(CConfig.TKS_Thuc_Tap_V11_Conn_String, p_strProcedure, p_arrValue));
    }

    protected static void Execute_Procedure(string p_strProcedure, params object[] p_arrValue)
    {
        CSqlHelper.ExecuteNonquery(CConfig.TKS_Thuc_Tap_V11_Conn_String, p_strProcedure, p_arrValue);
    }

    protected static long Scalar_ID(SqlConnection p_conn, SqlTransaction p_trans, string p_strProcedure, params object[] p_arrValue)
    {
        return Convert.ToInt64(CSqlHelper.ExecuteScalar(p_conn, p_trans, CConfig.TKS_Thuc_Tap_V11_Conn_String, p_strProcedure, p_arrValue));
    }

    protected static void Execute_Procedure(SqlConnection p_conn, SqlTransaction p_trans, string p_strProcedure, params object[] p_arrValue)
    {
        CSqlHelper.ExecuteNonquery(p_conn, p_trans, CConfig.TKS_Thuc_Tap_V11_Conn_String, p_strProcedure, p_arrValue);
    }
}
