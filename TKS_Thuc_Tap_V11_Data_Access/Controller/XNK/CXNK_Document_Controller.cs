using System.Data;
using System.Text.Json;
using TKS_Thuc_Tap_V11_Data_Access.DataLayer;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.XNK;

// Aggregate boundary: header, lines, Post/correction and revision share one SQL transaction.
public class CXNK_Document_Controller
{
    public CWarehouseDocument sp_XNK_Document_Edit_Get(bool p_bIs_Receipt, long p_iDocument_ID, string p_strCurrent_Login)
    {
        using DataTable v_dtData = new();
        CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dtData,
            "sp_XNK_Document_Edit_Get", p_bIs_Receipt, p_iDocument_ID, p_strCurrent_Login);
        if (v_dtData.Rows.Count != 1)
            throw new InvalidDataException("Không đọc được chứng từ.");
        DataRow v_row = v_dtData.Rows[0];
        CWarehouseDocument v_objData = JsonSerializer.Deserialize<CWarehouseDocument>((string)v_row["Document_State"])
            ?? throw new InvalidDataException("Dữ liệu chứng từ không hợp lệ.");
        v_objData.Document_Version = Convert.ToInt64(v_row["Document_Version"]);
        v_objData.State_Token = Convert.ToString(v_row["State_Token"]) ?? "";
        return v_objData;
    }

    public long sp_XNK_Document_Save_Posted(CWarehouseDocument p_objData, string p_strCurrent_Login)
    {
        Check_Data(p_objData, p_strCurrent_Login);
        string v_strDetails = JsonSerializer.Serialize(p_objData.Details.Select(p_objLine => new
        {
            p_objLine.Auto_ID, p_objLine.San_Pham_ID, p_objLine.So_Luong, p_objLine.Don_Gia
        }));
        using DataTable v_dtData = new();
        CSqlHelper.FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dtData,
            "sp_XNK_Document_Save_Posted", p_objData.Is_Receipt, p_objData.Auto_ID, p_objData.Document_Version,
            p_objData.Request_ID, p_objData.So_Phieu.Trim(), p_objData.Kho_ID, p_objData.NCC_ID,
            p_objData.Ngay_Chung_Tu.Date, p_objData.Ghi_Chu ?? "", v_strDetails, p_strCurrent_Login,
            string.IsNullOrEmpty(p_objData.State_Token) ? DBNull.Value : p_objData.State_Token);
        if (v_dtData.Rows.Count != 1)
            throw new InvalidDataException("Không nhận được kết quả lưu chứng từ.");
        p_objData.Auto_ID = Convert.ToInt64(v_dtData.Rows[0]["Auto_ID"]);
        p_objData.Document_Version = Convert.ToInt64(v_dtData.Rows[0]["Document_Version"]);
        p_objData.Is_Posted = true;
        return p_objData.Auto_ID;
    }

    public static void Check_Data(CWarehouseDocument p_objData, string p_strCurrent_Login)
    {
        ArgumentNullException.ThrowIfNull(p_objData);
        if (string.IsNullOrWhiteSpace(p_strCurrent_Login) || p_strCurrent_Login.Length > 100)
            throw new InvalidDataException("Phiên đăng nhập không hợp lệ.");
        if (p_objData.Auto_ID < 0 || p_objData.Document_Version < 0 || p_objData.Request_ID == Guid.Empty)
            throw new InvalidDataException("Mã phiếu hoặc mã yêu cầu không hợp lệ.");
        if (string.IsNullOrWhiteSpace(p_objData.So_Phieu) || p_objData.So_Phieu.Length > 100)
            throw new InvalidDataException("Số phiếu bắt buộc và tối đa 100 ký tự.");
        if (p_objData.Kho_ID <= 0 || p_objData.Ngay_Chung_Tu == default || p_objData.Ngay_Chung_Tu < new DateTime(1, 1, 1))
            throw new InvalidDataException("Kho và ngày chứng từ bắt buộc.");
        if (p_objData.Is_Receipt && p_objData.NCC_ID <= 0)
            throw new InvalidDataException("Nhà cung cấp bắt buộc.");
        if ((p_objData.Ghi_Chu?.Length ?? 0) > 1000)
            throw new InvalidDataException("Ghi chú tối đa 1000 ký tự.");
        if (p_objData.Auto_ID > 0 && (p_objData.State_Token.Length != 64 || !p_objData.State_Token.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Cần tải lại chứng từ trước khi hiệu chỉnh.");
        if (p_objData.Details == null || p_objData.Details.Count == 0)
            throw new InvalidDataException("Phiếu phải có ít nhất một dòng chi tiết.");
        HashSet<long> v_setIDs = new();
        foreach (CWarehouseDocumentDetail v_objLine in p_objData.Details)
        {
            if (v_objLine == null || v_objLine.Auto_ID < 0 || v_objLine.San_Pham_ID <= 0 ||
                v_objLine.So_Luong <= 0 || v_objLine.So_Luong > 999999999999999.999m ||
                decimal.Round(v_objLine.So_Luong, 3) != v_objLine.So_Luong ||
                v_objLine.Don_Gia <= 0 || v_objLine.Don_Gia > 9999999999999999.99m ||
                decimal.Round(v_objLine.Don_Gia, 2) != v_objLine.Don_Gia)
                throw new InvalidDataException("Chi tiết cần sản phẩm, số lượng dương (3 số lẻ), đơn giá dương (2 số lẻ).");
            if (v_objLine.Auto_ID > 0 && !v_setIDs.Add(v_objLine.Auto_ID))
                throw new InvalidDataException("Chi tiết bị trùng ID.");
        }
    }
}
