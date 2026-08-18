using System.Data;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

public sealed class CWarehouseDocument_Controller : CWarehouse_Controller_Base
{
    public Task<List<CWarehouseDocument>> List_Documents_Async(bool p_bReceipt)
    {
        var sql = p_bReceipt
            ? "SELECT h.Auto_ID,h.So_Phieu_Nhap_Kho,h.Kho_ID,k.Ten_Kho,h.NCC_ID,n.Ten_NCC,h.Ngay_Nhap_Kho,ISNULL(h.Ghi_Chu,N'') FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID=h.NCC_ID ORDER BY h.Ngay_Nhap_Kho DESC,h.Auto_ID DESC"
            : "SELECT h.Auto_ID,h.So_Phieu_Xuat_Kho,h.Kho_ID,k.Ten_Kho,CAST(0 AS BIGINT),N'',h.Ngay_Xuat_Kho,ISNULL(h.Ghi_Chu,N'') FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=h.Kho_ID ORDER BY h.Ngay_Xuat_Kho DESC,h.Auto_ID DESC";

        return ReadAsync(sql, r => new CWarehouseDocument
        {
            Auto_ID = r.GetInt64(0), Is_Receipt = p_bReceipt, So_Phieu = r.GetString(1), Kho_ID = r.GetInt64(2), Ten_Kho = r.GetString(3), NCC_ID = r.GetInt64(4), Ten_NCC = r.GetString(5), Ngay_Chung_Tu = r.GetDateTime(6), Ghi_Chu = r.GetString(7)
        });
    }

    public async Task Save_Document_Async(CWarehouseDocument p_objData)
    {
        var procedure = p_objData.Is_Receipt ? "dbo.sp_XNK_Nhap_Kho_Save_Header" : "dbo.sp_XNK_Xuat_Kho_Save_Header";
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = new SqlCommand(procedure, connection) { CommandType = CommandType.StoredProcedure };
        var id = command.Parameters.Add("@Auto_ID", SqlDbType.BigInt);
        id.Direction = ParameterDirection.InputOutput;
        id.Value = p_objData.Auto_ID;

        Add(command, p_objData.Is_Receipt ? "@So_Phieu_Nhap_Kho" : "@So_Phieu_Xuat_Kho", p_objData.So_Phieu);
        Add(command, "@Kho_ID", p_objData.Kho_ID);
        if (p_objData.Is_Receipt)
            Add(command, "@NCC_ID", p_objData.NCC_ID);
        Add(command, p_objData.Is_Receipt ? "@Ngay_Nhap_Kho" : "@Ngay_Xuat_Kho", p_objData.Ngay_Chung_Tu.Date);
        Add(command, "@Ghi_Chu", p_objData.Ghi_Chu);

        await command.ExecuteNonQueryAsync();
        p_objData.Auto_ID = Convert.ToInt64(id.Value);
    }

    public Task Delete_Document_Async(bool p_bReceipt, long p_iAuto_ID) =>
        ExecuteAsync(p_bReceipt ? "dbo.sp_XNK_Nhap_Kho_Delete_Header" : "dbo.sp_XNK_Xuat_Kho_Delete_Header", p => Add(p, "@Auto_ID", p_iAuto_ID));

    public Task<List<CWarehouseDocumentDetail>> List_Document_Details_Async(bool p_bReceipt, long p_iDocument_ID)
    {
        var sql = p_bReceipt
            ? "SELECT d.Auto_ID,d.Nhap_Kho_ID,d.San_Pham_ID,p.Ma_San_Pham,p.Ten_San_Pham,d.SL_Nhap,d.Don_Gia_Nhap FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=d.San_Pham_ID WHERE d.Nhap_Kho_ID=@DocumentId ORDER BY d.Auto_ID"
            : "SELECT d.Auto_ID,d.Xuat_Kho_ID,d.San_Pham_ID,p.Ma_San_Pham,p.Ten_San_Pham,d.SL_Xuat,d.Don_Gia_Xuat FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=d.San_Pham_ID WHERE d.Xuat_Kho_ID=@DocumentId ORDER BY d.Auto_ID";

        return ReadAsync(sql, r => new CWarehouseDocumentDetail
        {
            Auto_ID = r.GetInt64(0), Document_ID = r.GetInt64(1), San_Pham_ID = r.GetInt64(2), Ma_San_Pham = r.GetString(3), Ten_San_Pham = r.GetString(4), So_Luong = r.GetDecimal(5), Don_Gia = r.GetDecimal(6)
        }, c => Add(c, "@DocumentId", p_iDocument_ID));
    }

    public async Task Save_Document_Detail_Async(bool p_bReceipt, CWarehouseDocumentDetail p_objData)
    {
        var procedure = p_bReceipt ? "dbo.sp_XNK_Nhap_Kho_Save_Detail" : "dbo.sp_XNK_Xuat_Kho_Save_Detail";
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = new SqlCommand(procedure, connection) { CommandType = CommandType.StoredProcedure };
        var id = command.Parameters.Add("@Auto_ID", SqlDbType.BigInt);
        id.Direction = ParameterDirection.InputOutput;
        id.Value = p_objData.Auto_ID;

        Add(command, p_bReceipt ? "@Nhap_Kho_ID" : "@Xuat_Kho_ID", p_objData.Document_ID);
        Add(command, "@San_Pham_ID", p_objData.San_Pham_ID);
        Add(command, p_bReceipt ? "@SL_Nhap" : "@SL_Xuat", p_objData.So_Luong);
        Add(command, p_bReceipt ? "@Don_Gia_Nhap" : "@Don_Gia_Xuat", p_objData.Don_Gia);

        await command.ExecuteNonQueryAsync();
        p_objData.Auto_ID = Convert.ToInt64(id.Value);
    }

    public Task Delete_Document_Detail_Async(bool p_bReceipt, long p_iAuto_ID) =>
        ExecuteAsync(p_bReceipt ? "dbo.sp_XNK_Nhap_Kho_Delete_Detail" : "dbo.sp_XNK_Xuat_Kho_Delete_Detail", p => Add(p, "@Auto_ID", p_iAuto_ID));
}
