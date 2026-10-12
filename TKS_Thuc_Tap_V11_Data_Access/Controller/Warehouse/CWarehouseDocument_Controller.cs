using System.Data;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

public class CWarehouseDocument_Controller : CWarehouse_Controller_Base
{
    public Task<List<CWarehouseDocument>> List_Documents_Async(bool p_bIs_Receipt, string p_strCurrent_Login = "", long? p_iWarehouse_ID = null)
    {
        string v_strProcedure;
        if (p_bIs_Receipt)
        {
            v_strProcedure = "F3001_sp_sel_List_Nhap_Kho";
        }
        else
        {
            v_strProcedure = "F3002_sp_sel_List_Xuat_Kho";
        }

        return Task.FromResult(List_From_Procedure<CWarehouseDocument>(v_strProcedure, p_strCurrent_Login, p_iWarehouse_ID));
    }

    public Task<CWarehousePagedResult<CWarehouseDocument>> List_Documents_Page_Async(bool p_bIs_Receipt, int p_iPage_Number, int p_iPage_Size, string p_strSearch_Text = "", string p_strCurrent_Login = "", long? p_iWarehouse_ID = null)
    {
        return Task.FromResult(Page_From_Procedure<CWarehouseDocument>("sp_XNK_Document_Page", p_bIs_Receipt, p_iPage_Number, p_iPage_Size, p_strSearch_Text, p_strCurrent_Login, p_iWarehouse_ID));
    }

    public Task Save_Document_Async(CWarehouseDocument p_objData,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "", string p_strCurrent_Login = "")
    {
        var v_bIsCreate = p_objData.Auto_ID == 0;
        p_objData.Last_Updated_By = p_strLast_Updated_By;
        p_objData.Last_Updated_By_Function = p_strLast_Updated_By_Function;
        if (v_bIsCreate)
        {
            p_objData.Created_By = p_strLast_Updated_By;
            p_objData.Created_By_Function = p_strLast_Updated_By_Function;
        }

        if (p_objData.Is_Receipt)
        {
            string? v_strCreatedBy;
            string? v_strCreatedByFunction;
            if (v_bIsCreate)
            {
                v_strCreatedBy = p_objData.Created_By;
                v_strCreatedByFunction = p_objData.Created_By_Function;
            }
            else
            {
                v_strCreatedBy = null;
                v_strCreatedByFunction = null;
            }

            string v_strProcedure;
            if (v_bIsCreate)
            {
                v_strProcedure = "F3001_sp_ins_Nhap_Kho_Header";
            }
            else
            {
                v_strProcedure = "F3001_sp_upd_Nhap_Kho_Header";
            }

            p_objData.Auto_ID = Scalar_ID(v_strProcedure,
                InputOutput_BigInt("@Auto_ID", p_objData.Auto_ID),
                NVarChar("@So_Phieu_Nhap_Kho", p_objData.So_Phieu, 100),
                BigInt("@Kho_ID", p_objData.Kho_ID),
                BigInt("@NCC_ID", p_objData.NCC_ID),
                Date("@Ngay_Nhap_Kho", p_objData.Ngay_Chung_Tu),
                NVarChar("@Ghi_Chu", p_objData.Ghi_Chu, 1000),
                NVarChar("@Ma_Dang_Nhap", p_strCurrent_Login, 100),
                NVarChar("@Created_By", v_strCreatedBy, 100),
                NVarChar("@Created_By_Function", v_strCreatedByFunction, 100),
                NVarChar("@Last_Updated_By", p_objData.Last_Updated_By, 100),
                NVarChar("@Last_Updated_By_Function", p_objData.Last_Updated_By_Function, 100));
        }
        else
        {
            string? v_strCreatedBy;
            string? v_strCreatedByFunction;
            if (v_bIsCreate)
            {
                v_strCreatedBy = p_objData.Created_By;
                v_strCreatedByFunction = p_objData.Created_By_Function;
            }
            else
            {
                v_strCreatedBy = null;
                v_strCreatedByFunction = null;
            }

            string v_strProcedure;
            if (v_bIsCreate)
            {
                v_strProcedure = "F3002_sp_ins_Xuat_Kho_Header";
            }
            else
            {
                v_strProcedure = "F3002_sp_upd_Xuat_Kho_Header";
            }

            p_objData.Auto_ID = Scalar_ID(v_strProcedure,
                InputOutput_BigInt("@Auto_ID", p_objData.Auto_ID),
                NVarChar("@So_Phieu_Xuat_Kho", p_objData.So_Phieu, 100),
                BigInt("@Kho_ID", p_objData.Kho_ID),
                Date("@Ngay_Xuat_Kho", p_objData.Ngay_Chung_Tu),
                NVarChar("@Ghi_Chu", p_objData.Ghi_Chu, 1000),
                NVarChar("@Ma_Dang_Nhap", p_strCurrent_Login, 100),
                NVarChar("@Created_By", v_strCreatedBy, 100),
                NVarChar("@Created_By_Function", v_strCreatedByFunction, 100),
                NVarChar("@Last_Updated_By", p_objData.Last_Updated_By, 100),
                NVarChar("@Last_Updated_By_Function", p_objData.Last_Updated_By_Function, 100));
        }

        return Task.CompletedTask;
    }

    public Task Delete_Document_Async(bool p_bIs_Receipt, long p_iAuto_ID,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "", string p_strCurrent_Login = "")
    {
        var v_strProcedure = "";
        if (p_bIs_Receipt)
        {
            v_strProcedure = "F3001_sp_del_Nhap_Kho_Header";
        }
        else
        {
            v_strProcedure = "F3002_sp_del_Xuat_Kho_Header";
        }

        Execute_Procedure(v_strProcedure,
            BigInt("@Auto_ID", p_iAuto_ID),
            NVarChar("@Ma_Dang_Nhap", p_strCurrent_Login, 100));
        return Task.CompletedTask;
    }

    public Task Post_Document_Async(bool p_bIs_Receipt, long p_iAuto_ID,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "", string p_strCurrent_Login = "")
    {
        Execute_Procedure("sp_XNK_Document_Post",
            Bit("@Is_Receipt", p_bIs_Receipt),
            BigInt("@Document_ID", p_iAuto_ID),
            NVarChar("@Ma_Dang_Nhap", p_strCurrent_Login, 100),
            NVarChar("@Last_Updated_By", p_strLast_Updated_By, 100),
            NVarChar("@Last_Updated_By_Function", p_strLast_Updated_By_Function, 100));
        return Task.CompletedTask;
    }

    public Task<List<CWarehouseDocumentDetail>> List_Document_Details_Async(bool p_bIs_Receipt, long p_iDocument_ID, string p_strCurrent_Login = "")
    {
        if (p_bIs_Receipt)
        {
            return Task.FromResult(List_From_Procedure<CWarehouseDocumentDetail>("F3001_sp_sel_List_Nhap_Kho_Detail", p_iDocument_ID, p_strCurrent_Login));
        }

        return Task.FromResult(List_From_Procedure<CWarehouseDocumentDetail>("F3002_sp_sel_List_Xuat_Kho_Detail", p_iDocument_ID, p_strCurrent_Login));
    }

    public Task Save_Document_Detail_Async(bool p_bIs_Receipt, CWarehouseDocumentDetail p_objData,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "", string p_strCurrent_Login = "")
    {
        var v_bIsCreate = p_objData.Auto_ID == 0;
        p_objData.Last_Updated_By = p_strLast_Updated_By;
        p_objData.Last_Updated_By_Function = p_strLast_Updated_By_Function;
        if (v_bIsCreate)
        {
            p_objData.Created_By = p_strLast_Updated_By;
            p_objData.Created_By_Function = p_strLast_Updated_By_Function;
        }

        if (p_bIs_Receipt)
        {
            string? v_strCreatedBy;
            string? v_strCreatedByFunction;
            if (v_bIsCreate)
            {
                v_strCreatedBy = p_objData.Created_By;
                v_strCreatedByFunction = p_objData.Created_By_Function;
            }
            else
            {
                v_strCreatedBy = null;
                v_strCreatedByFunction = null;
            }

            string v_strProcedure;
            if (v_bIsCreate)
            {
                v_strProcedure = "F3001_sp_ins_Nhap_Kho_Detail";
            }
            else
            {
                v_strProcedure = "F3001_sp_upd_Nhap_Kho_Detail";
            }

            p_objData.Auto_ID = Scalar_ID(v_strProcedure,
                InputOutput_BigInt("@Auto_ID", p_objData.Auto_ID),
                BigInt("@Nhap_Kho_ID", p_objData.Document_ID),
                BigInt("@San_Pham_ID", p_objData.San_Pham_ID),
                Decimal("@SL_Nhap", p_objData.So_Luong, 18, 3),
                Decimal("@Don_Gia_Nhap", p_objData.Don_Gia, 18, 2),
                NVarChar("@Ma_Dang_Nhap", p_strCurrent_Login, 100),
                NVarChar("@Created_By", v_strCreatedBy, 100),
                NVarChar("@Created_By_Function", v_strCreatedByFunction, 100),
                NVarChar("@Last_Updated_By", p_objData.Last_Updated_By, 100),
                NVarChar("@Last_Updated_By_Function", p_objData.Last_Updated_By_Function, 100));
        }
        else
        {
            string? v_strCreatedBy;
            string? v_strCreatedByFunction;
            if (v_bIsCreate)
            {
                v_strCreatedBy = p_objData.Created_By;
                v_strCreatedByFunction = p_objData.Created_By_Function;
            }
            else
            {
                v_strCreatedBy = null;
                v_strCreatedByFunction = null;
            }

            string v_strProcedure;
            if (v_bIsCreate)
            {
                v_strProcedure = "F3002_sp_ins_Xuat_Kho_Detail";
            }
            else
            {
                v_strProcedure = "F3002_sp_upd_Xuat_Kho_Detail";
            }

            p_objData.Auto_ID = Scalar_ID(v_strProcedure,
                InputOutput_BigInt("@Auto_ID", p_objData.Auto_ID),
                BigInt("@Xuat_Kho_ID", p_objData.Document_ID),
                BigInt("@San_Pham_ID", p_objData.San_Pham_ID),
                Decimal("@SL_Xuat", p_objData.So_Luong, 18, 3),
                Decimal("@Don_Gia_Xuat", p_objData.Don_Gia, 18, 2),
                NVarChar("@Ma_Dang_Nhap", p_strCurrent_Login, 100),
                NVarChar("@Created_By", v_strCreatedBy, 100),
                NVarChar("@Created_By_Function", v_strCreatedByFunction, 100),
                NVarChar("@Last_Updated_By", p_objData.Last_Updated_By, 100),
                NVarChar("@Last_Updated_By_Function", p_objData.Last_Updated_By_Function, 100));
        }

        return Task.CompletedTask;
    }

    public async Task<long> Save_Imported_Document_Atomically_Async(
        CWarehouseDocument p_objDocument,
        IReadOnlyList<CWarehouseDocumentDetail> p_arrDetails,
        string p_strCurrent_User,
        string p_strCurrent_Function,
        CancellationToken p_objCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(p_objDocument);
        ArgumentNullException.ThrowIfNull(p_arrDetails);
        if (p_objDocument.Auto_ID != 0)
            throw new ArgumentException("Import chỉ được tạo chứng từ mới.", nameof(p_objDocument));
        if (p_arrDetails.Count == 0)
            throw new ArgumentException("Chứng từ phải có ít nhất một dòng chi tiết.", nameof(p_arrDetails));

        p_objDocument.Created_By = p_strCurrent_User;
        p_objDocument.Created_By_Function = p_strCurrent_Function;
        p_objDocument.Last_Updated_By = p_strCurrent_User;
        p_objDocument.Last_Updated_By_Function = p_strCurrent_Function;

        await using var v_objConnection = new SqlConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String);
        await v_objConnection.OpenAsync(p_objCancellationToken);
        using var v_objTransaction = v_objConnection.BeginTransaction();

        try
        {
            p_objCancellationToken.ThrowIfCancellationRequested();
            if (p_objDocument.Is_Receipt)
            {
                p_objDocument.Auto_ID = Scalar_ID(v_objConnection, v_objTransaction, "F3001_sp_ins_Nhap_Kho_Header",
                    InputOutput_BigInt("@Auto_ID", 0),
                    NVarChar("@So_Phieu_Nhap_Kho", p_objDocument.So_Phieu, 100),
                    BigInt("@Kho_ID", p_objDocument.Kho_ID),
                    BigInt("@NCC_ID", p_objDocument.NCC_ID),
                    Date("@Ngay_Nhap_Kho", p_objDocument.Ngay_Chung_Tu),
                    NVarChar("@Ghi_Chu", p_objDocument.Ghi_Chu, 1000),
                    NVarChar("@Ma_Dang_Nhap", p_strCurrent_User, 100),
                    NVarChar("@Created_By", p_objDocument.Created_By, 100),
                    NVarChar("@Created_By_Function", p_objDocument.Created_By_Function, 100),
                    NVarChar("@Last_Updated_By", p_objDocument.Last_Updated_By, 100),
                    NVarChar("@Last_Updated_By_Function", p_objDocument.Last_Updated_By_Function, 100));
            }
            else
            {
                p_objDocument.Auto_ID = Scalar_ID(v_objConnection, v_objTransaction, "F3002_sp_ins_Xuat_Kho_Header",
                    InputOutput_BigInt("@Auto_ID", 0),
                    NVarChar("@So_Phieu_Xuat_Kho", p_objDocument.So_Phieu, 100),
                    BigInt("@Kho_ID", p_objDocument.Kho_ID),
                    Date("@Ngay_Xuat_Kho", p_objDocument.Ngay_Chung_Tu),
                    NVarChar("@Ghi_Chu", p_objDocument.Ghi_Chu, 1000),
                    NVarChar("@Ma_Dang_Nhap", p_strCurrent_User, 100),
                    NVarChar("@Created_By", p_objDocument.Created_By, 100),
                    NVarChar("@Created_By_Function", p_objDocument.Created_By_Function, 100),
                    NVarChar("@Last_Updated_By", p_objDocument.Last_Updated_By, 100),
                    NVarChar("@Last_Updated_By_Function", p_objDocument.Last_Updated_By_Function, 100));
            }

            foreach (var v_objDetail in p_arrDetails)
            {
                p_objCancellationToken.ThrowIfCancellationRequested();
                v_objDetail.Auto_ID = 0;
                v_objDetail.Document_ID = p_objDocument.Auto_ID;
                v_objDetail.Created_By = p_strCurrent_User;
                v_objDetail.Created_By_Function = p_strCurrent_Function;
                v_objDetail.Last_Updated_By = p_strCurrent_User;
                v_objDetail.Last_Updated_By_Function = p_strCurrent_Function;

                var v_strProcedure = p_objDocument.Is_Receipt
                    ? "F3001_sp_ins_Nhap_Kho_Detail"
                    : "F3002_sp_ins_Xuat_Kho_Detail";
                v_objDetail.Auto_ID = Scalar_ID(v_objConnection, v_objTransaction, v_strProcedure,
                    InputOutput_BigInt("@Auto_ID", 0),
                    BigInt(p_objDocument.Is_Receipt ? "@Nhap_Kho_ID" : "@Xuat_Kho_ID", v_objDetail.Document_ID),
                    BigInt("@San_Pham_ID", v_objDetail.San_Pham_ID),
                    Decimal(p_objDocument.Is_Receipt ? "@SL_Nhap" : "@SL_Xuat", v_objDetail.So_Luong, 18, 3),
                    Decimal(p_objDocument.Is_Receipt ? "@Don_Gia_Nhap" : "@Don_Gia_Xuat", v_objDetail.Don_Gia, 18, 2),
                    NVarChar("@Ma_Dang_Nhap", p_strCurrent_User, 100),
                    NVarChar("@Created_By", v_objDetail.Created_By, 100),
                    NVarChar("@Created_By_Function", v_objDetail.Created_By_Function, 100),
                    NVarChar("@Last_Updated_By", v_objDetail.Last_Updated_By, 100),
                    NVarChar("@Last_Updated_By_Function", v_objDetail.Last_Updated_By_Function, 100));
            }

            v_objTransaction.Commit();
            return p_objDocument.Auto_ID;
        }
        catch
        {
            try
            {
                v_objTransaction.Rollback();
            }
            catch
            {
                // Preserve the original save error.
            }

            throw;
        }
    }

    public Task Delete_Document_Detail_Async(bool p_bIs_Receipt, long p_iAuto_ID,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "", string p_strCurrent_Login = "")
    {
        var v_strProcedure = "";
        if (p_bIs_Receipt)
        {
            v_strProcedure = "F3001_sp_del_Nhap_Kho_Detail";
        }
        else
        {
            v_strProcedure = "F3002_sp_del_Xuat_Kho_Detail";
        }

        Execute_Procedure(v_strProcedure,
            BigInt("@Auto_ID", p_iAuto_ID),
            NVarChar("@Ma_Dang_Nhap", p_strCurrent_Login, 100));
        return Task.CompletedTask;
    }

    private static SqlParameter BigInt(string p_strName, long p_iValue)
    {
        return new SqlParameter(p_strName, SqlDbType.BigInt) { Value = p_iValue };
    }

    private static SqlParameter Bit(string p_strName, bool p_bValue)
    {
        return new SqlParameter(p_strName, SqlDbType.Bit) { Value = p_bValue };
    }

    private static SqlParameter InputOutput_BigInt(string p_strName, long p_iValue)
    {
        return new SqlParameter(p_strName, SqlDbType.BigInt)
        {
            Direction = ParameterDirection.InputOutput,
            Value = p_iValue
        };
    }

    private static SqlParameter Date(string p_strName, DateTime p_dtmValue)
    {
        return new SqlParameter(p_strName, SqlDbType.Date) { Value = p_dtmValue.Date };
    }

    private static SqlParameter Decimal(string p_strName, decimal p_decValue, byte p_bPrecision, byte p_bScale)
    {
        return new SqlParameter(p_strName, SqlDbType.Decimal)
        {
            Precision = p_bPrecision,
            Scale = p_bScale,
            Value = p_decValue
        };
    }

    private static SqlParameter NVarChar(string p_strName, string? p_strValue, int p_iSize)
    {
        SqlParameter v_objParameter = new SqlParameter(p_strName, SqlDbType.NVarChar, p_iSize);
        if (string.IsNullOrEmpty(p_strValue))
        {
            v_objParameter.Value = DBNull.Value;
        }
        else
        {
            v_objParameter.Value = p_strValue;
        }

        return v_objParameter;
    }
}
