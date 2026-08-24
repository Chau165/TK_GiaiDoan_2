using System.Data;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

public class CWarehouseDocument_Controller : CWarehouse_Controller_Base
{
    public Task<List<CWarehouseDocument>> List_Documents_Async(bool p_bIs_Receipt)
    {
        return Task.FromResult(List_From_Procedure<CWarehouseDocument>("sp_XNK_Document_List", p_bIs_Receipt));
    }

    public Task<CWarehousePagedResult<CWarehouseDocument>> List_Documents_Page_Async(bool p_bIs_Receipt, int p_iPage_Number, int p_iPage_Size, string p_strSearch_Text = "")
    {
        return Task.FromResult(Page_From_Procedure<CWarehouseDocument>("sp_XNK_Document_Page", p_bIs_Receipt, p_iPage_Number, p_iPage_Size, p_strSearch_Text));
    }

    public Task Save_Document_Async(CWarehouseDocument p_objData,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")
    {
        p_objData.Last_Updated_By = p_strLast_Updated_By;
        p_objData.Last_Updated_By_Function = p_strLast_Updated_By_Function;
        p_objData.Created_By = p_strLast_Updated_By;
        p_objData.Created_By_Function = p_strLast_Updated_By_Function;

        if (p_objData.Is_Receipt)
        {
            p_objData.Auto_ID = Scalar_ID("sp_XNK_Nhap_Kho_Save_Header",
                InputOutput_BigInt("@Auto_ID", p_objData.Auto_ID),
                NVarChar("@So_Phieu_Nhap_Kho", p_objData.So_Phieu, 100),
                BigInt("@Kho_ID", p_objData.Kho_ID),
                BigInt("@NCC_ID", p_objData.NCC_ID),
                Date("@Ngay_Nhap_Kho", p_objData.Ngay_Chung_Tu),
                NVarChar("@Ghi_Chu", p_objData.Ghi_Chu, 1000));
        }
        else
        {
            p_objData.Auto_ID = Scalar_ID("sp_XNK_Xuat_Kho_Save_Header",
                InputOutput_BigInt("@Auto_ID", p_objData.Auto_ID),
                NVarChar("@So_Phieu_Xuat_Kho", p_objData.So_Phieu, 100),
                BigInt("@Kho_ID", p_objData.Kho_ID),
                Date("@Ngay_Xuat_Kho", p_objData.Ngay_Chung_Tu),
                NVarChar("@Ghi_Chu", p_objData.Ghi_Chu, 1000));
        }

        return Task.CompletedTask;
    }

    public Task Delete_Document_Async(bool p_bIs_Receipt, long p_iAuto_ID,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")
    {
        Execute_Procedure(p_bIs_Receipt ? "sp_XNK_Nhap_Kho_Delete_Header" : "sp_XNK_Xuat_Kho_Delete_Header",
            BigInt("@Auto_ID", p_iAuto_ID));
        return Task.CompletedTask;
    }

    public Task Post_Document_Async(bool p_bIs_Receipt, long p_iAuto_ID,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")
    {
        Execute_Procedure("sp_XNK_Document_Post", Bit("@Is_Receipt", p_bIs_Receipt), BigInt("@Document_ID", p_iAuto_ID));
        return Task.CompletedTask;
    }

    public Task<List<CWarehouseDocumentDetail>> List_Document_Details_Async(bool p_bIs_Receipt, long p_iDocument_ID)
    {
        return Task.FromResult(List_From_Procedure<CWarehouseDocumentDetail>("sp_XNK_Document_Detail_List", p_bIs_Receipt, p_iDocument_ID));
    }

    public Task Save_Document_Detail_Async(bool p_bIs_Receipt, CWarehouseDocumentDetail p_objData,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")
    {
        p_objData.Last_Updated_By = p_strLast_Updated_By;
        p_objData.Last_Updated_By_Function = p_strLast_Updated_By_Function;
        p_objData.Created_By = p_strLast_Updated_By;
        p_objData.Created_By_Function = p_strLast_Updated_By_Function;

        if (p_bIs_Receipt)
        {
            p_objData.Auto_ID = Scalar_ID("sp_XNK_Nhap_Kho_Save_Detail",
                InputOutput_BigInt("@Auto_ID", p_objData.Auto_ID),
                BigInt("@Nhap_Kho_ID", p_objData.Document_ID),
                BigInt("@San_Pham_ID", p_objData.San_Pham_ID),
                Decimal("@SL_Nhap", p_objData.So_Luong, 18, 3),
                Decimal("@Don_Gia_Nhap", p_objData.Don_Gia, 18, 2));
        }
        else
        {
            p_objData.Auto_ID = Scalar_ID("sp_XNK_Xuat_Kho_Save_Detail",
                InputOutput_BigInt("@Auto_ID", p_objData.Auto_ID),
                BigInt("@Xuat_Kho_ID", p_objData.Document_ID),
                BigInt("@San_Pham_ID", p_objData.San_Pham_ID),
                Decimal("@SL_Xuat", p_objData.So_Luong, 18, 3),
                Decimal("@Don_Gia_Xuat", p_objData.Don_Gia, 18, 2));
        }

        return Task.CompletedTask;
    }

    public Task Delete_Document_Detail_Async(bool p_bIs_Receipt, long p_iAuto_ID,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")
    {
        Execute_Procedure(p_bIs_Receipt ? "sp_XNK_Nhap_Kho_Delete_Detail" : "sp_XNK_Xuat_Kho_Delete_Detail",
            BigInt("@Auto_ID", p_iAuto_ID));
        return Task.CompletedTask;
    }

    private static SqlParameter BigInt(string p_strName, long p_iValue) => new(p_strName, SqlDbType.BigInt) { Value = p_iValue };

    private static SqlParameter Bit(string p_strName, bool p_bValue) => new(p_strName, SqlDbType.Bit) { Value = p_bValue };

    private static SqlParameter InputOutput_BigInt(string p_strName, long p_iValue) => new(p_strName, SqlDbType.BigInt)
    {
        Direction = ParameterDirection.InputOutput,
        Value = p_iValue
    };

    private static SqlParameter Date(string p_strName, DateTime p_dtmValue) => new(p_strName, SqlDbType.Date) { Value = p_dtmValue.Date };

    private static SqlParameter Decimal(string p_strName, decimal p_decValue, byte p_bPrecision, byte p_bScale) => new(p_strName, SqlDbType.Decimal)
    {
        Precision = p_bPrecision,
        Scale = p_bScale,
        Value = p_decValue
    };

    private static SqlParameter NVarChar(string p_strName, string? p_strValue, int p_iSize) => new(p_strName, SqlDbType.NVarChar, p_iSize)
    {
        Value = string.IsNullOrEmpty(p_strValue) ? DBNull.Value : p_strValue
    };
}
