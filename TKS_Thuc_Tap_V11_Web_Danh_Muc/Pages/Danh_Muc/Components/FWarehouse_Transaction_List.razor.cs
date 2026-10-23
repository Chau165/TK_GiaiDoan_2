using System.Data;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Common;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.DataLayer;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Common.Common;

namespace TKS_Thuc_Tap_V11_Web_Danh_Muc.Pages.Danh_Muc.Components;

public partial class FWarehouse_Transaction_List
{
    protected override void Import_Excel_Entry(CExcel_Controller v_objCtrlExcel, ref int p_iCount_Success, ref int p_iCount_Error)
    {
        var v_sbError = new StringBuilder();
        var v_sbWarning = new StringBuilder();
        try
        {
            if (!r_objChuc_Nang.Is_Have_Add_Permission)
                throw new UnauthorizedAccessException(Get_Language_Data_Field("Bạn không có quyền tạo phiếu nhập/xuất."));

            // Kiểm tra cả hai sheet và quan hệ Header–Detail trước bất kỳ INSERT nào.
            var v_arrDocuments = Read_Import_Documents(v_objCtrlExcel);
            var v_arrWarehouses = m_objPermissionController.sp_DM_Kho_User_List_Allowed(r_strActive_User_Name);
            // Lookup SP chỉ trả ID/mã/tên; cần entity đầy đủ để kiểm tra Don_Vi_Tinh_ID.
            var v_arrProducts = new CDM_San_Pham_Controller().F2018_sp_sel_List_San_Pham();
            var v_arrUnits = new CDM_Don_Vi_Tinh_Controller().F2016_sp_sel_List_Don_Vi_Tinh_Lookup();
            var v_arrSuppliers = m_bIs_Receipt
                ? new CDM_NCC_Controller().F2019_sp_sel_List_NCC_Lookup()
                : new();

            foreach (var v_objRows in v_arrDocuments)
            {
                int v_iCurrentRow = v_objRows.Row;
                string v_strCurrentSheet = m_bIs_Receipt ? "Phiếu nhập" : "Phiếu xuất";
                try
                {
                    var v_row = v_objRows.Header;
                    var v_strWarehouse = Import_Text(v_row[2]);
                    var v_arrWarehouseMatches = v_arrWarehouses.Where(v_objWarehouse =>
                        Import_Equals(v_objWarehouse.Code, v_strWarehouse) || Import_Equals(v_objWarehouse.Name, v_strWarehouse)).ToArray();
                    if (v_strWarehouse.Length == 0 || v_arrWarehouseMatches.Length != 1)
                        throw new InvalidDataException("Không xác định duy nhất được kho được cấp quyền: " + v_strWarehouse);
                    var v_objDocument = new CWarehouseDocument
                    {
                        Is_Receipt = m_bIs_Receipt,
                        So_Phieu = v_objRows.Number,
                        Kho_ID = v_arrWarehouseMatches[0].Auto_ID,
                        Ngay_Chung_Tu = Parse_Import_Date(v_row[1]),
                        Ghi_Chu = Import_Text(v_row[m_bIs_Receipt ? 4 : 3]),
                        Created_By = r_strActive_User_Name,
                        Created_By_Function = r_strActive_Function_Code,
                        Last_Updated_By = r_strActive_User_Name,
                        Last_Updated_By_Function = r_strActive_Function_Code
                    };
                    if (v_objDocument.So_Phieu.Length > 100 || v_objDocument.Ghi_Chu.Length > 1000)
                        throw new InvalidDataException("Số phiếu hoặc ghi chú vượt độ dài tham số SQL.");
                    if (m_bIs_Receipt)
                    {
                        string v_strSupplier = Import_Text(v_row[3]);
                        var v_arrMatches = v_arrSuppliers.Where(v_objSupplier => Import_Equals(v_objSupplier.Ma_NCC, v_strSupplier)).ToArray();
                        if (v_strSupplier.Length == 0 || v_arrMatches.Length != 1)
                            throw new InvalidDataException("Không xác định duy nhất được mã nhà cung cấp: " + v_strSupplier);
                        v_objDocument.NCC_ID = v_arrMatches[0].Auto_ID;
                    }
                    if (v_objRows.Details.Count == 0)
                        throw new InvalidDataException("Phiếu phải có ít nhất một dòng chi tiết.");

                    var v_arrDetails = new List<CWarehouseDocumentDetail>();
                    v_strCurrentSheet = m_bIs_Receipt ? "Chi tiết nhập" : "Chi tiết xuất";
                    foreach (var v_objDetailRow in v_objRows.Details)
                    {
                        v_iCurrentRow = v_objDetailRow.Row;
                        var v_detail = v_objDetailRow.Data;
                        string v_strCode = Import_Text(v_detail[1]);
                        var v_arrMatches = v_arrProducts.Where(v_objProduct => Import_Equals(v_objProduct.Ma_San_Pham, v_strCode)).ToArray();
                        if (v_strCode.Length == 0 || v_arrMatches.Length != 1)
                            throw new InvalidDataException("Không xác định duy nhất được mã sản phẩm: " + v_strCode);
                        var v_objProduct = v_arrMatches[0];
                        if (!v_arrUnits.Any(v_objUnit => v_objUnit.Auto_ID == v_objProduct.Don_Vi_Tinh_ID))
                            throw new InvalidDataException("Đơn vị tính của sản phẩm không hợp lệ: " + v_strCode);
                        if (Import_Text(v_detail[2]).Length > 0 && !Import_Equals(v_objProduct.Ten_San_Pham, Import_Text(v_detail[2])))
                            throw new InvalidDataException("Tên hàng không khớp mã sản phẩm: " + v_strCode);
                        v_arrDetails.Add(new CWarehouseDocumentDetail
                        {
                            San_Pham_ID = v_objProduct.Auto_ID,
                            So_Luong = Parse_Import_Decimal(v_detail[3], 3),
                            Don_Gia = Parse_Import_Decimal(v_detail[4], 2),
                            Created_By = r_strActive_User_Name,
                            Created_By_Function = r_strActive_Function_Code,
                            Last_Updated_By = r_strActive_User_Name,
                            Last_Updated_By_Function = r_strActive_Function_Code
                        });
                    }

                    using (SqlConnection v_conn = CSqlHelper.CreateConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String))
                    {
                        v_conn.Open();
                        using SqlTransaction v_trans = v_conn.BeginTransaction();
                        try
                        {
                            v_iCurrentRow = v_objRows.Row;
                            v_strCurrentSheet = m_bIs_Receipt ? "Phiếu nhập" : "Phiếu xuất";
                            if (m_bIs_Receipt)
                                v_objDocument.Auto_ID = m_objNhapKhoController.F3001_sp_ins_Nhap_Kho_Header(v_conn, v_trans, To_Nhap_Kho(v_objDocument));
                            else
                                v_objDocument.Auto_ID = m_objXuatKhoController.F3002_sp_ins_Xuat_Kho_Header(v_conn, v_trans, To_Xuat_Kho(v_objDocument));
                            for (int v_i = 0; v_i < v_arrDetails.Count; v_i++)
                            {
                                v_iCurrentRow = v_objRows.Details[v_i].Row;
                                v_strCurrentSheet = m_bIs_Receipt ? "Chi tiết nhập" : "Chi tiết xuất";
                                var v_objDetail = v_arrDetails[v_i];
                                v_objDetail.Document_ID = v_objDocument.Auto_ID;
                                if (m_bIs_Receipt)
                                    m_objNhapKhoController.F3001_sp_ins_Nhap_Kho_Detail(v_conn, v_trans, To_Nhap_Kho_Detail(v_objDetail));
                                else
                                    m_objXuatKhoController.F3002_sp_ins_Xuat_Kho_Detail(v_conn, v_trans, To_Xuat_Kho_Detail(v_objDetail));
                            }
                            v_trans.Commit();
                        }
                        catch
                        {
                            try { v_trans.Rollback(); } catch { }
                            throw;
                        }
                    }
                    p_iCount_Success++;
                    try
                    {
                        CCommon_Controller.FCommon_Insert_Action_History(v_objDocument.Auto_ID,
                            Get_Language_Data_Field("Import Excel - Thêm Mới"), "Web", r_strActive_User_Name,
                            r_strActive_Function_Code, r_objChuc_Nang.Ten_Chuc_Nang, "Key: " + v_objDocument.So_Phieu);
                    }
                    catch (Exception ex)
                    {
                        v_sbWarning.AppendLine($"{Get_Language_Data_Field("Đã lưu nhưng chưa ghi lịch sử")}: {WebUtility.HtmlEncode(v_objRows.Number)}: {WebUtility.HtmlEncode(ex.Message)}<br/>");
                    }
                }
                catch (Exception ex)
                {
                    p_iCount_Error++;
                    v_sbError.AppendLine($"{WebUtility.HtmlEncode(v_objRows.Number)} — {v_strCurrentSheet}, {Get_Language_Data_Field("Dòng")} {v_iCurrentRow}: {WebUtility.HtmlEncode(ex.Message)}<br/>");
                }
            }
            if (v_arrDocuments.Count == 0)
                v_sbError.AppendLine(Get_Language_Data_Field("File không có chứng từ."));
            m_objSelectedDocument = null;
            m_arrDetail.Clear();
        }
        catch (Exception ex)
        {
            p_iCount_Error++;
            v_sbError.AppendLine(WebUtility.HtmlEncode(ex.Message));
        }
        string v_strMessage = CCommonFunction.Set_Success_MessageBox(string.Format(
            Get_Language_Data_Field("Thêm mới {0} phiếu thành công."), p_iCount_Success));
        if (v_sbError.Length > 0)
            v_strMessage += CCommonFunction.Set_Error_MessageBox(v_sbError.ToString());
        if (v_sbWarning.Length > 0)
            v_strMessage += CCommonFunction.Set_Error_MessageBox(v_sbWarning.ToString());
        Open_Message_Info(Get_Language_Data_Field("Thông báo Import Excel"), v_strMessage);
    }

    // Chỉ mapping template cố định; không đọc lại workbook bằng profile/reader khác.
    private List<CDocumentImportRows> Read_Import_Documents(CExcel_Controller p_objExcel)
    {
        string v_strHeaderSheet = m_bIs_Receipt ? "Phiếu nhập" : "Phiếu xuất";
        string v_strDetailSheet = m_bIs_Receipt ? "Chi tiết nhập" : "Chi tiết xuất";
        string[] v_arrHeader = m_bIs_Receipt
            ? new[] { "Số CT", "Ngày CT", "Kho nhận", "Mã NCC", "Ghi chú" }
            : new[] { "Số PX", "Ngày xuất", "Kho xuất", "Ghi chú" };
        string[] v_arrDetail = { m_bIs_Receipt ? "Số CT" : "Số PX", "Mã VT", "Tên hàng", m_bIs_Receipt ? "SL thực nhận" : "SL xuất", "Đơn giá" };
        Validate_Import_Sheet(p_objExcel, v_strHeaderSheet, v_arrHeader);
        Validate_Import_Sheet(p_objExcel, v_strDetailSheet, v_arrDetail);
        using DataTable v_dtHeader = p_objExcel.List_Range_Value_To_End(v_strHeaderSheet, "A2", m_bIs_Receipt ? "E" : "D");
        using DataTable v_dtDetail = p_objExcel.List_Range_Value_To_End(v_strDetailSheet, "A2", "E");
        if (v_dtHeader.Rows.Count > 20000 || v_dtDetail.Rows.Count > 20000)
            throw new InvalidDataException("Template vượt quá 20000 dòng trên một sheet.");
        var v_arrDocuments = new List<CDocumentImportRows>();
        var v_dicDocuments = new Dictionary<string, CDocumentImportRows>(StringComparer.OrdinalIgnoreCase);
        int v_iRow = 1;
        foreach (DataRow v_row in v_dtHeader.Rows)
        {
            v_iRow++;
            if (v_row.ItemArray.All(v_objValue => Import_Text(v_objValue).Length == 0))
                continue;
            string v_strNumber = Import_Text(v_row[0]);
            if (v_strNumber.Length == 0)
                throw new InvalidDataException($"{v_strHeaderSheet}, dòng {v_iRow}: thiếu số phiếu, không thể ghép chi tiết.");
            var v_objRows = new CDocumentImportRows(v_row, v_iRow, v_strNumber);
            if (!v_dicDocuments.TryAdd(v_strNumber, v_objRows))
                throw new InvalidDataException($"{v_strHeaderSheet}, dòng {v_iRow}: Header trùng số phiếu {v_strNumber} trong file.");
            v_arrDocuments.Add(v_objRows);
        }
        v_iRow = 1;
        foreach (DataRow v_row in v_dtDetail.Rows)
        {
            v_iRow++;
            if (v_row.ItemArray.All(v_objValue => Import_Text(v_objValue).Length == 0))
                continue;
            string v_strNumber = Import_Text(v_row[0]);
            if (!v_dicDocuments.TryGetValue(v_strNumber, out var v_objRows))
                throw new InvalidDataException($"{v_strDetailSheet}, dòng {v_iRow}: không tìm thấy Header cho số phiếu '{v_strNumber}'.");
            v_objRows.Details.Add((v_row, v_iRow));
        }
        return v_arrDocuments;
    }

    private static void Validate_Import_Sheet(CExcel_Controller p_objExcel, string p_strName, string[] p_arrHeaders)
    {
        // Get_Cell_Value(index) của công ty tìm SheetN; dùng overload theo tên cho template này.
        for (int v_i = 0; v_i < 16384; v_i++)
        {
            string v_strAddress = OfficeOpenXml.ExcelCellBase.GetAddress(1, v_i + 1);
            string v_strExpected = v_i < p_arrHeaders.Length ? p_arrHeaders[v_i] : "";
            if (!Import_Equals(Import_Text(p_objExcel.Get_Cell_Value(p_strName, v_strAddress)), v_strExpected))
                throw new InvalidDataException($"{p_strName}, cột {v_i + 1}: tiêu đề không khớp template.");
        }
    }

    private static string Import_Text(object p_objValue) => CUtility.Convert_To_String(p_objValue).Trim().Normalize();
    private static bool Import_Equals(string? p_strLeft, string p_strRight) =>
        string.Equals(p_strLeft?.Trim().Normalize(), p_strRight, StringComparison.OrdinalIgnoreCase);

    private static DateTime Parse_Import_Date(object p_objValue)
    {
        string v_strValue = Import_Text(p_objValue);
        if (DateTime.TryParseExact(v_strValue, new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var v_dtmValue))
            return v_dtmValue.Date;
        // CExcel_Controller lưu DataColumn dạng string, DateTime của Excel được đổi theo culture hiện tại.
        if (DateTime.TryParse(v_strValue, CultureInfo.CurrentCulture, DateTimeStyles.None, out v_dtmValue))
            return v_dtmValue.Date;
        if (double.TryParse(v_strValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double v_dblSerial))
        {
            v_dtmValue = DateTime.FromOADate(v_dblSerial);
            if (v_dtmValue.Year >= 1900 && v_dtmValue.Year <= 9999)
                return v_dtmValue.Date;
        }
        throw new InvalidDataException("Ngày chứng từ không hợp lệ: " + v_strValue);
    }

    private static decimal Parse_Import_Decimal(object p_objValue, int p_iScale)
    {
        string v_strValue = Import_Text(p_objValue);
        if (!decimal.TryParse(v_strValue, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.CurrentCulture, out decimal v_decValue) &&
            !decimal.TryParse(v_strValue, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out v_decValue))
            throw new InvalidDataException("Giá trị số không hợp lệ: " + v_strValue);
        if (decimal.Round(v_decValue, p_iScale) != v_decValue ||
            Math.Abs(v_decValue) >= (p_iScale == 3 ? 1000000000000000m : 10000000000000000m))
            throw new InvalidDataException($"Giá trị vượt kiểu SQL decimal(18,{p_iScale}): {v_strValue}");
        // SQL chịu trách nhiệm điều kiện số lượng/đơn giá dương và tồn khả dụng.
        return v_decValue;
    }

    private sealed class CDocumentImportRows
    {
        public DataRow Header { get; }
        public int Row { get; }
        public string Number { get; }
        public List<(DataRow Data, int Row)> Details { get; } = new();
        public CDocumentImportRows(DataRow p_objHeader, int p_iRow, string p_strNumber)
        {
            Header = p_objHeader;
            Row = p_iRow;
            Number = p_strNumber;
        }
    }
}
