using System.Data;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.DataLayer;
using TKS_Thuc_Tap_V11_Data_Access.Entity.XNK;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Common;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.XNK;

public class CXNK_Nhap_Kho_Controller
{
    public List<CXNK_Nhap_Kho> F3001_sp_sel_List_Nhap_Kho(string p_strCurrent_Login, long? p_iWarehouse_ID = null)
    {
        List<CXNK_Nhap_Kho> v_arrResult = new List<CXNK_Nhap_Kho>();
        DataTable v_dtData = new DataTable();
        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dtData,
                "F3001_sp_sel_List_Nhap_Kho", p_strCurrent_Login, p_iWarehouse_ID);
            foreach (DataRow v_row in v_dtData.Rows)
            {
                CXNK_Nhap_Kho v_objResult = CWMS_Utility.Map_Warehouse_Row<CXNK_Nhap_Kho>(v_row);
                v_arrResult.Add(v_objResult);
            }
        }
        catch (Exception)
        {
            throw;
        }
        finally
        {
            v_dtData.Dispose();
        }
        return v_arrResult;
    }

    public List<CXNK_Nhap_Kho_Detail> F3001_sp_sel_List_Nhap_Kho_Detail(long p_iNhap_Kho_ID, string p_strCurrent_Login)
    {
        List<CXNK_Nhap_Kho_Detail> v_arrResult = new List<CXNK_Nhap_Kho_Detail>();
        DataTable v_dtData = new DataTable();
        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dtData,
                "F3001_sp_sel_List_Nhap_Kho_Detail", p_iNhap_Kho_ID, p_strCurrent_Login);
            foreach (DataRow v_row in v_dtData.Rows)
            {
                CXNK_Nhap_Kho_Detail v_objResult = CWMS_Utility.Map_Warehouse_Row<CXNK_Nhap_Kho_Detail>(v_row);
                v_arrResult.Add(v_objResult);
            }
        }
        catch (Exception)
        {
            throw;
        }
        finally
        {
            v_dtData.Dispose();
        }
        return v_arrResult;
    }

    public long F3001_sp_ins_Nhap_Kho_Header(CXNK_Nhap_Kho p_objData)
    {
        p_objData.Auto_ID = Convert.ToInt64(CSqlHelper.ExecuteScalar(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F3001_sp_ins_Nhap_Kho_Header",
            p_objData.Auto_ID,
            Normalize_SQL_Text(p_objData.So_Phieu_Nhap_Kho),
            p_objData.Kho_ID, p_objData.NCC_ID,
            p_objData.Ngay_Nhap_Kho.Date, Normalize_SQL_Text(p_objData.Ghi_Chu),
            Normalize_SQL_Text(p_objData.Ma_Dang_Nhap),
            Normalize_SQL_Text(p_objData.Created_By),
            Normalize_SQL_Text(p_objData.Created_By_Function),
            Normalize_SQL_Text(p_objData.Last_Updated_By),
            Normalize_SQL_Text(p_objData.Last_Updated_By_Function)));
        return p_objData.Auto_ID;
    }

    public long F3001_sp_ins_Nhap_Kho_Header(SqlConnection p_conn, SqlTransaction p_trans, CXNK_Nhap_Kho p_objData)
    {
        p_objData.Auto_ID = Convert.ToInt64(CSqlHelper.ExecuteScalar(p_conn, p_trans, CConfig.TKS_Thuc_Tap_V11_Conn_String, "F3001_sp_ins_Nhap_Kho_Header",
            p_objData.Auto_ID,
            Normalize_SQL_Text(p_objData.So_Phieu_Nhap_Kho),
            p_objData.Kho_ID, p_objData.NCC_ID,
            p_objData.Ngay_Nhap_Kho.Date, Normalize_SQL_Text(p_objData.Ghi_Chu),
            Normalize_SQL_Text(p_objData.Ma_Dang_Nhap),
            Normalize_SQL_Text(p_objData.Created_By),
            Normalize_SQL_Text(p_objData.Created_By_Function),
            Normalize_SQL_Text(p_objData.Last_Updated_By),
            Normalize_SQL_Text(p_objData.Last_Updated_By_Function)));
        return p_objData.Auto_ID;
    }

    public long F3001_sp_upd_Nhap_Kho_Header(CXNK_Nhap_Kho p_objData)
    {
        p_objData.Auto_ID = Convert.ToInt64(CSqlHelper.ExecuteScalar(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F3001_sp_upd_Nhap_Kho_Header",
            p_objData.Auto_ID,
            Normalize_SQL_Text(p_objData.So_Phieu_Nhap_Kho),
            p_objData.Kho_ID, p_objData.NCC_ID,
            p_objData.Ngay_Nhap_Kho.Date, Normalize_SQL_Text(p_objData.Ghi_Chu),
            Normalize_SQL_Text(p_objData.Ma_Dang_Nhap),
            Normalize_SQL_Text(null), Normalize_SQL_Text(null),
            Normalize_SQL_Text(p_objData.Last_Updated_By),
            Normalize_SQL_Text(p_objData.Last_Updated_By_Function)));
        return p_objData.Auto_ID;
    }

    public void F3001_sp_del_Nhap_Kho_Header(long p_iAuto_ID, string p_strCurrent_Login)
    {
        CSqlHelper.ExecuteNonquery(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F3001_sp_del_Nhap_Kho_Header", p_iAuto_ID,
            Normalize_SQL_Text(null), Normalize_SQL_Text(null),
            Normalize_SQL_Text(p_strCurrent_Login));
    }

    public long F3001_sp_ins_Nhap_Kho_Detail(CXNK_Nhap_Kho_Detail p_objData)
    {
        p_objData.Auto_ID = Convert.ToInt64(CSqlHelper.ExecuteScalar(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F3001_sp_ins_Nhap_Kho_Detail",
            p_objData.Auto_ID, p_objData.Nhap_Kho_ID,
            p_objData.San_Pham_ID, p_objData.SL_Nhap,
            p_objData.Don_Gia_Nhap, Normalize_SQL_Text(p_objData.Ma_Dang_Nhap),
            Normalize_SQL_Text(p_objData.Created_By), Normalize_SQL_Text(p_objData.Created_By_Function),
            Normalize_SQL_Text(p_objData.Last_Updated_By), Normalize_SQL_Text(p_objData.Last_Updated_By_Function)));
        return p_objData.Auto_ID;
    }

    public long F3001_sp_ins_Nhap_Kho_Detail(SqlConnection p_conn, SqlTransaction p_trans, CXNK_Nhap_Kho_Detail p_objData)
    {
        p_objData.Auto_ID = Convert.ToInt64(CSqlHelper.ExecuteScalar(p_conn, p_trans, CConfig.TKS_Thuc_Tap_V11_Conn_String, "F3001_sp_ins_Nhap_Kho_Detail",
            p_objData.Auto_ID, p_objData.Nhap_Kho_ID,
            p_objData.San_Pham_ID, p_objData.SL_Nhap,
            p_objData.Don_Gia_Nhap, Normalize_SQL_Text(p_objData.Ma_Dang_Nhap),
            Normalize_SQL_Text(p_objData.Created_By), Normalize_SQL_Text(p_objData.Created_By_Function),
            Normalize_SQL_Text(p_objData.Last_Updated_By), Normalize_SQL_Text(p_objData.Last_Updated_By_Function)));
        return p_objData.Auto_ID;
    }

    public long F3001_sp_upd_Nhap_Kho_Detail(CXNK_Nhap_Kho_Detail p_objData)
    {
        p_objData.Auto_ID = Convert.ToInt64(CSqlHelper.ExecuteScalar(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F3001_sp_upd_Nhap_Kho_Detail",
            p_objData.Auto_ID, p_objData.Nhap_Kho_ID,
            p_objData.San_Pham_ID, p_objData.SL_Nhap,
            p_objData.Don_Gia_Nhap, Normalize_SQL_Text(p_objData.Ma_Dang_Nhap),
            Normalize_SQL_Text(null), Normalize_SQL_Text(null),
            Normalize_SQL_Text(p_objData.Last_Updated_By), Normalize_SQL_Text(p_objData.Last_Updated_By_Function)));
        return p_objData.Auto_ID;
    }

    public void F3001_sp_del_Nhap_Kho_Detail(long p_iAuto_ID, string p_strCurrent_Login)
    {
        CSqlHelper.ExecuteNonquery(CConfig.TKS_Thuc_Tap_V11_Conn_String, "F3001_sp_del_Nhap_Kho_Detail", p_iAuto_ID,
            Normalize_SQL_Text(null), Normalize_SQL_Text(null),
            Normalize_SQL_Text(p_strCurrent_Login));
    }

    // 23/10/2026 - Codex: dùng parameter metadata của CSqlHelper gốc, giữ quy tắc chuỗi rỗng -> SQL NULL.
    private static object Normalize_SQL_Text(string? p_strValue)
    {
        if (string.IsNullOrEmpty(p_strValue))
        {
            return DBNull.Value;
        }
        return p_strValue;
    }
}
