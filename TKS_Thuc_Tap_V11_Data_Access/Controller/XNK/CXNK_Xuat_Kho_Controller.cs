using System.Data;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Common;
using TKS_Thuc_Tap_V11_Data_Access.DataLayer;
using TKS_Thuc_Tap_V11_Data_Access.Entity.XNK;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.XNK;

public class CXNK_Xuat_Kho_Controller : CWarehouse_Controller_Base
{
    public List<CXNK_Xuat_Kho> F3002_sp_sel_List_Xuat_Kho(string p_strCurrent_Login, long? p_iWarehouse_ID = null)
    {
        List<CXNK_Xuat_Kho> v_arrResult = new List<CXNK_Xuat_Kho>();
        DataTable v_dtData = new DataTable();
        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dtData,
                "F3002_sp_sel_List_Xuat_Kho", p_strCurrent_Login, p_iWarehouse_ID);
            foreach (DataRow v_row in v_dtData.Rows)
            {
                CXNK_Xuat_Kho v_objResult = CUtility.Map_Row_To_Entity<CXNK_Xuat_Kho>(v_row);
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

    public List<CXNK_Xuat_Kho_Detail> F3002_sp_sel_List_Xuat_Kho_Detail(long p_iXuat_Kho_ID, string p_strCurrent_Login)
    {
        List<CXNK_Xuat_Kho_Detail> v_arrResult = new List<CXNK_Xuat_Kho_Detail>();
        DataTable v_dtData = new DataTable();
        try
        {
            CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dtData,
                "F3002_sp_sel_List_Xuat_Kho_Detail", p_iXuat_Kho_ID, p_strCurrent_Login);
            foreach (DataRow v_row in v_dtData.Rows)
            {
                CXNK_Xuat_Kho_Detail v_objResult = CUtility.Map_Row_To_Entity<CXNK_Xuat_Kho_Detail>(v_row);
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

    public long F3002_sp_ins_Xuat_Kho_Header(CXNK_Xuat_Kho p_objData)
    {
        p_objData.Auto_ID = Scalar_ID("F3002_sp_ins_Xuat_Kho_Header",
            InputOutput_BigInt("@Auto_ID", p_objData.Auto_ID),
            NVarChar("@So_Phieu_Xuat_Kho", p_objData.So_Phieu_Xuat_Kho, 100),
            BigInt("@Kho_ID", p_objData.Kho_ID), Date("@Ngay_Xuat_Kho", p_objData.Ngay_Xuat_Kho),
            NVarChar("@Ghi_Chu", p_objData.Ghi_Chu, 1000), NVarChar("@Ma_Dang_Nhap", p_objData.Ma_Dang_Nhap, 100),
            NVarChar("@Created_By", p_objData.Created_By, 100), NVarChar("@Created_By_Function", p_objData.Created_By_Function, 100),
            NVarChar("@Last_Updated_By", p_objData.Last_Updated_By, 100), NVarChar("@Last_Updated_By_Function", p_objData.Last_Updated_By_Function, 100));
        return p_objData.Auto_ID;
    }

    public long F3002_sp_ins_Xuat_Kho_Header(SqlConnection p_conn, SqlTransaction p_trans, CXNK_Xuat_Kho p_objData)
    {
        p_objData.Auto_ID = Scalar_ID(p_conn, p_trans, "F3002_sp_ins_Xuat_Kho_Header",
            InputOutput_BigInt("@Auto_ID", p_objData.Auto_ID),
            NVarChar("@So_Phieu_Xuat_Kho", p_objData.So_Phieu_Xuat_Kho, 100),
            BigInt("@Kho_ID", p_objData.Kho_ID), Date("@Ngay_Xuat_Kho", p_objData.Ngay_Xuat_Kho),
            NVarChar("@Ghi_Chu", p_objData.Ghi_Chu, 1000), NVarChar("@Ma_Dang_Nhap", p_objData.Ma_Dang_Nhap, 100),
            NVarChar("@Created_By", p_objData.Created_By, 100), NVarChar("@Created_By_Function", p_objData.Created_By_Function, 100),
            NVarChar("@Last_Updated_By", p_objData.Last_Updated_By, 100), NVarChar("@Last_Updated_By_Function", p_objData.Last_Updated_By_Function, 100));
        return p_objData.Auto_ID;
    }

    public long F3002_sp_upd_Xuat_Kho_Header(CXNK_Xuat_Kho p_objData)
    {
        p_objData.Auto_ID = Scalar_ID("F3002_sp_upd_Xuat_Kho_Header",
            InputOutput_BigInt("@Auto_ID", p_objData.Auto_ID),
            NVarChar("@So_Phieu_Xuat_Kho", p_objData.So_Phieu_Xuat_Kho, 100),
            BigInt("@Kho_ID", p_objData.Kho_ID), Date("@Ngay_Xuat_Kho", p_objData.Ngay_Xuat_Kho),
            NVarChar("@Ghi_Chu", p_objData.Ghi_Chu, 1000), NVarChar("@Ma_Dang_Nhap", p_objData.Ma_Dang_Nhap, 100),
            NVarChar("@Created_By", null, 100), NVarChar("@Created_By_Function", null, 100),
            NVarChar("@Last_Updated_By", p_objData.Last_Updated_By, 100), NVarChar("@Last_Updated_By_Function", p_objData.Last_Updated_By_Function, 100));
        return p_objData.Auto_ID;
    }

    public void F3002_sp_del_Xuat_Kho_Header(long p_iAuto_ID, string p_strCurrent_Login)
    {
        Execute_Procedure("F3002_sp_del_Xuat_Kho_Header", BigInt("@Auto_ID", p_iAuto_ID),
            NVarChar("@Last_Updated_By", null, 100), NVarChar("@Last_Updated_By_Function", null, 100),
            NVarChar("@Ma_Dang_Nhap", p_strCurrent_Login, 100));
    }

    public long F3002_sp_ins_Xuat_Kho_Detail(CXNK_Xuat_Kho_Detail p_objData)
    {
        p_objData.Auto_ID = Scalar_ID("F3002_sp_ins_Xuat_Kho_Detail",
            InputOutput_BigInt("@Auto_ID", p_objData.Auto_ID), BigInt("@Xuat_Kho_ID", p_objData.Xuat_Kho_ID),
            BigInt("@San_Pham_ID", p_objData.San_Pham_ID), Decimal("@SL_Xuat", p_objData.SL_Xuat, 18, 3),
            Decimal("@Don_Gia_Xuat", p_objData.Don_Gia_Xuat, 18, 2), NVarChar("@Ma_Dang_Nhap", p_objData.Ma_Dang_Nhap, 100),
            NVarChar("@Created_By", p_objData.Created_By, 100), NVarChar("@Created_By_Function", p_objData.Created_By_Function, 100),
            NVarChar("@Last_Updated_By", p_objData.Last_Updated_By, 100), NVarChar("@Last_Updated_By_Function", p_objData.Last_Updated_By_Function, 100));
        return p_objData.Auto_ID;
    }

    public long F3002_sp_ins_Xuat_Kho_Detail(SqlConnection p_conn, SqlTransaction p_trans, CXNK_Xuat_Kho_Detail p_objData)
    {
        p_objData.Auto_ID = Scalar_ID(p_conn, p_trans, "F3002_sp_ins_Xuat_Kho_Detail",
            InputOutput_BigInt("@Auto_ID", p_objData.Auto_ID), BigInt("@Xuat_Kho_ID", p_objData.Xuat_Kho_ID),
            BigInt("@San_Pham_ID", p_objData.San_Pham_ID), Decimal("@SL_Xuat", p_objData.SL_Xuat, 18, 3),
            Decimal("@Don_Gia_Xuat", p_objData.Don_Gia_Xuat, 18, 2), NVarChar("@Ma_Dang_Nhap", p_objData.Ma_Dang_Nhap, 100),
            NVarChar("@Created_By", p_objData.Created_By, 100), NVarChar("@Created_By_Function", p_objData.Created_By_Function, 100),
            NVarChar("@Last_Updated_By", p_objData.Last_Updated_By, 100), NVarChar("@Last_Updated_By_Function", p_objData.Last_Updated_By_Function, 100));
        return p_objData.Auto_ID;
    }

    public long F3002_sp_upd_Xuat_Kho_Detail(CXNK_Xuat_Kho_Detail p_objData)
    {
        p_objData.Auto_ID = Scalar_ID("F3002_sp_upd_Xuat_Kho_Detail",
            InputOutput_BigInt("@Auto_ID", p_objData.Auto_ID), BigInt("@Xuat_Kho_ID", p_objData.Xuat_Kho_ID),
            BigInt("@San_Pham_ID", p_objData.San_Pham_ID), Decimal("@SL_Xuat", p_objData.SL_Xuat, 18, 3),
            Decimal("@Don_Gia_Xuat", p_objData.Don_Gia_Xuat, 18, 2), NVarChar("@Ma_Dang_Nhap", p_objData.Ma_Dang_Nhap, 100),
            NVarChar("@Created_By", null, 100), NVarChar("@Created_By_Function", null, 100),
            NVarChar("@Last_Updated_By", p_objData.Last_Updated_By, 100), NVarChar("@Last_Updated_By_Function", p_objData.Last_Updated_By_Function, 100));
        return p_objData.Auto_ID;
    }

    public void F3002_sp_del_Xuat_Kho_Detail(long p_iAuto_ID, string p_strCurrent_Login)
    {
        Execute_Procedure("F3002_sp_del_Xuat_Kho_Detail", BigInt("@Auto_ID", p_iAuto_ID),
            NVarChar("@Last_Updated_By", null, 100), NVarChar("@Last_Updated_By_Function", null, 100),
            NVarChar("@Ma_Dang_Nhap", p_strCurrent_Login, 100));
    }

    private static SqlParameter BigInt(string p_strName, long p_iValue)
    {
        SqlParameter v_objParameter = new SqlParameter(p_strName, SqlDbType.BigInt) { Value = p_iValue };
        return v_objParameter;
    }

    private static SqlParameter InputOutput_BigInt(string p_strName, long p_iValue)
    {
        SqlParameter v_objParameter = new SqlParameter(p_strName, SqlDbType.BigInt)
        {
            Direction = ParameterDirection.InputOutput,
            Value = p_iValue
        };
        return v_objParameter;
    }

    private static SqlParameter Date(string p_strName, DateTime p_dtmValue)
    {
        SqlParameter v_objParameter = new SqlParameter(p_strName, SqlDbType.Date) { Value = p_dtmValue.Date };
        return v_objParameter;
    }

    private static SqlParameter Decimal(string p_strName, decimal p_decValue, byte p_bPrecision, byte p_bScale)
    {
        SqlParameter v_objParameter = new SqlParameter(p_strName, SqlDbType.Decimal)
        {
            Precision = p_bPrecision,
            Scale = p_bScale,
            Value = p_decValue
        };
        return v_objParameter;
    }

    private static SqlParameter NVarChar(string p_strName, string? p_strValue, int p_iSize)
    {
        SqlParameter v_objParameter = new SqlParameter(p_strName, SqlDbType.NVarChar, p_iSize)
        {
            Value = string.IsNullOrEmpty(p_strValue) ? DBNull.Value : p_strValue
        };
        return v_objParameter;
    }
}
