using System.Data;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.DataLayer;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Common;
public abstract class CWarehouse_Controller_Base
{
    protected static CWarehousePagedResult<T> Page_From_Procedure<T>(string p_strProcedure, params object[] p_arrValue)
        where T : new()
    {
        using var v_dsData = new DataSet();
        Fill_Page_Data(v_dsData, p_strProcedure, p_arrValue);
        if (v_dsData.Tables.Count < 2 || v_dsData.Tables[0].Rows.Count == 0)
        {
            throw new InvalidOperationException($"{p_strProcedure} must return total count and page data.");
        }

        var v_objResult = new CWarehousePagedResult<T>
        {
            Total_Count = Convert.ToInt32(v_dsData.Tables[0].Rows[0]["Total_Count"])
        };
        foreach (DataRow v_row in v_dsData.Tables[1].Rows)
        {
            v_objResult.Items.Add(CWMS_Utility.Map_Warehouse_Row<T>(v_row));
        }

        return v_objResult;
    }

    // 23/10/2026 - Codex: SP phân trang trả hai result sets; Source A chỉ hỗ trợ DataTable.
    // Giữ phần đọc DataSet tại Controller Warehouse, tái sử dụng connection và parameter cache của công ty.
    public static void Fill_Page_Data(DataSet p_dsData, string p_strProcedure, params object[] p_arrValue)
    {
        SqlParameter[] v_arrParameters = CSqlHelperParameterCache.GetSpParameterSet(CConfig.TKS_Thuc_Tap_V11_Conn_String, p_strProcedure);
        if (v_arrParameters.Length != p_arrValue.Length)
        {
            throw new Exception($"{p_strProcedure}. Parameter count does not match Parameter Value count.");
        }

        for (int v_i = 0; v_i < v_arrParameters.Length; v_i++)
        {
            object v_objValue = p_arrValue[v_i];
            if (v_objValue == null)
            {
                v_objValue = DBNull.Value;
            }

            v_arrParameters[v_i].Value = v_objValue;
        }

        using (SqlConnection v_conn = CSqlHelper.CreateConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String))
        using (SqlCommand v_objCommand = new SqlCommand(p_strProcedure, v_conn))
        using (SqlDataAdapter v_objAdapter = new SqlDataAdapter(v_objCommand))
        {
            v_objCommand.CommandType = CommandType.StoredProcedure;
            v_objCommand.CommandTimeout = 30;
            v_objCommand.Parameters.AddRange(v_arrParameters);
            v_objAdapter.Fill(p_dsData);
        }
    }
}
