using System.Data;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

public sealed class CWarehouseMaster_Controller : CWarehouse_Controller_Base
{
    public Task<List<CWarehouseMaster>> List_Master_Async(string p_strType)
    {
        var sql = p_strType switch
        {
            "DonViTinh" => "SELECT Auto_ID, N'' Code, Ten_Don_Vi_Tinh Name, 0 Related_ID, 0 Related_ID_2, N'' Login_Name, ISNULL(Ghi_Chu,N'') Ghi_Chu FROM dbo.tbl_DM_Don_Vi_Tinh ORDER BY Ten_Don_Vi_Tinh",
            "LoaiSanPham" => "SELECT Auto_ID, Ma_LSP Code, Ten_LSP Name, 0 Related_ID, 0 Related_ID_2, N'' Login_Name, ISNULL(Ghi_Chu,N'') Ghi_Chu FROM dbo.tbl_DM_Loai_San_Pham ORDER BY Ma_LSP",
            "SanPham" => "SELECT Auto_ID, Ma_San_Pham Code, Ten_San_Pham Name, Loai_San_Pham_ID Related_ID, Don_Vi_Tinh_ID Related_ID_2, N'' Login_Name, ISNULL(Ghi_Chu,N'') Ghi_Chu FROM dbo.tbl_DM_San_Pham ORDER BY Ma_San_Pham",
            "NCC" => "SELECT Auto_ID, Ma_NCC Code, Ten_NCC Name, 0 Related_ID, 0 Related_ID_2, N'' Login_Name, ISNULL(Ghi_Chu,N'') Ghi_Chu FROM dbo.tbl_DM_NCC ORDER BY Ma_NCC",
            "Kho" => "SELECT Auto_ID, N'' Code, Ten_Kho Name, 0 Related_ID, 0 Related_ID_2, N'' Login_Name, ISNULL(Ghi_Chu,N'') Ghi_Chu FROM dbo.tbl_DM_Kho ORDER BY Ten_Kho",
            "KhoUser" => "SELECT Auto_ID, N'' Code, N'' Name, Kho_ID Related_ID, 0 Related_ID_2, Ma_Dang_Nhap Login_Name, N'' Ghi_Chu FROM dbo.tbl_DM_Kho_User ORDER BY Ma_Dang_Nhap",
            _ => throw new ArgumentOutOfRangeException(nameof(p_strType))
        };

        return ReadAsync(sql, r => new CWarehouseMaster
        {
            Auto_ID = r.GetInt64(0), Code = r.GetString(1), Name = r.GetString(2), Related_ID = Convert.ToInt64(r.GetValue(3)), Related_ID_2 = Convert.ToInt64(r.GetValue(4)), Login_Name = r.GetString(5), Ghi_Chu = r.GetString(6)
        });
    }

    public Task<List<CWarehouseLookup>> List_Lookup_Async(string p_strType)
    {
        var sql = p_strType switch
        {
            "DonViTinh" => "SELECT Auto_ID, N'' Code, Ten_Don_Vi_Tinh Name FROM dbo.tbl_DM_Don_Vi_Tinh ORDER BY Ten_Don_Vi_Tinh",
            "LoaiSanPham" => "SELECT Auto_ID, Ma_LSP Code, Ten_LSP Name FROM dbo.tbl_DM_Loai_San_Pham ORDER BY Ma_LSP",
            "SanPham" => "SELECT Auto_ID, Ma_San_Pham Code, Ten_San_Pham Name FROM dbo.tbl_DM_San_Pham ORDER BY Ma_San_Pham",
            "NCC" => "SELECT Auto_ID, Ma_NCC Code, Ten_NCC Name FROM dbo.tbl_DM_NCC ORDER BY Ma_NCC",
            "Kho" => "SELECT Auto_ID, N'' Code, Ten_Kho Name FROM dbo.tbl_DM_Kho ORDER BY Ten_Kho",
            _ => throw new ArgumentOutOfRangeException(nameof(p_strType))
        };

        return ReadAsync(sql, r => new CWarehouseLookup { Auto_ID = r.GetInt64(0), Code = r.GetString(1), Name = r.GetString(2) });
    }

    public async Task Save_Master_Async(string p_strType, CWarehouseMaster p_objData)
    {
        var procedure = p_strType switch
        {
            "DonViTinh" => "dbo.sp_DM_Don_Vi_Tinh_Save",
            "LoaiSanPham" => "dbo.sp_DM_Loai_San_Pham_Save",
            "SanPham" => "dbo.sp_DM_San_Pham_Save",
            "NCC" => "dbo.sp_DM_NCC_Save",
            "Kho" => "dbo.sp_DM_Kho_Save",
            "KhoUser" => "dbo.sp_DM_Kho_User_Save",
            _ => throw new ArgumentOutOfRangeException(nameof(p_strType))
        };

        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = new SqlCommand(procedure, connection) { CommandType = CommandType.StoredProcedure };
        var id = command.Parameters.Add("@Auto_ID", SqlDbType.BigInt);
        id.Direction = ParameterDirection.InputOutput;
        id.Value = p_objData.Auto_ID;

        switch (p_strType)
        {
            case "DonViTinh": Add(command, "@Ten_Don_Vi_Tinh", p_objData.Name); Add(command, "@Ghi_Chu", p_objData.Ghi_Chu); break;
            case "LoaiSanPham": Add(command, "@Ma_LSP", p_objData.Code); Add(command, "@Ten_LSP", p_objData.Name); Add(command, "@Ghi_Chu", p_objData.Ghi_Chu); break;
            case "SanPham": Add(command, "@Ma_San_Pham", p_objData.Code); Add(command, "@Ten_San_Pham", p_objData.Name); Add(command, "@Loai_San_Pham_ID", p_objData.Related_ID); Add(command, "@Don_Vi_Tinh_ID", p_objData.Related_ID_2); Add(command, "@Ghi_Chu", p_objData.Ghi_Chu); break;
            case "NCC": Add(command, "@Ma_NCC", p_objData.Code); Add(command, "@Ten_NCC", p_objData.Name); Add(command, "@Ghi_Chu", p_objData.Ghi_Chu); break;
            case "Kho": Add(command, "@Ten_Kho", p_objData.Name); Add(command, "@Ghi_Chu", p_objData.Ghi_Chu); break;
            case "KhoUser": Add(command, "@Ma_Dang_Nhap", p_objData.Login_Name); Add(command, "@Kho_ID", p_objData.Related_ID); break;
        }

        await command.ExecuteNonQueryAsync();
        p_objData.Auto_ID = Convert.ToInt64(id.Value);
    }

    public Task Delete_Master_Async(string p_strType, long p_iAuto_ID) =>
        ExecuteAsync("dbo.sp_DM_Delete", p => { Add(p, "@Entity", p_strType); Add(p, "@Auto_ID", p_iAuto_ID); });
}
