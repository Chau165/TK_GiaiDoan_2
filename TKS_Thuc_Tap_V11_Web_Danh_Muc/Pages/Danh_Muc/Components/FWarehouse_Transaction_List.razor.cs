using System.Data;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;
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
            {
                throw new UnauthorizedAccessException(Get_Language_Data_Field("Bạn không có quyền tạo phiếu nhập/xuất."));
            }

            // Đọc và nhóm các dòng có thể xử lý theo số phiếu trước khi ghi dữ liệu.
            var v_arrDocuments = Read_Import_Documents(v_objCtrlExcel, v_sbError, ref p_iCount_Error);
            var v_arrWarehouses = m_objPermissionController.sp_DM_Kho_User_List_Allowed(r_strActive_User_Name);
            // Lookup SP chỉ trả ID/mã/tên; cần entity đầy đủ để kiểm tra Don_Vi_Tinh_ID.
            var v_arrProducts = new CDM_San_Pham_Controller().F2018_sp_sel_List_San_Pham();
            var v_arrUnits = new CDM_Don_Vi_Tinh_Controller().F2016_sp_sel_List_Don_Vi_Tinh_Lookup();
            List<CDM_NCC> v_arrSuppliers;
            if (m_bIs_Receipt)
            {
                v_arrSuppliers = new CDM_NCC_Controller().F2019_sp_sel_List_NCC_Lookup();
            }
            else
            {
                v_arrSuppliers = new();
            }

            foreach (var v_objRows in v_arrDocuments)
            {
                int v_iCurrentRow = v_objRows.Row;
                string v_strCurrentSheet;
                if (m_bIs_Receipt)
                {
                    v_strCurrentSheet = "Phiếu nhập";
                }
                else
                {
                    v_strCurrentSheet = "Phiếu xuất";
                }

                if (v_objRows.Errors.Count > 0)
                {
                    p_iCount_Error++;
                    foreach (string v_strImportError in v_objRows.Errors)
                    {
                        v_sbError.AppendLine(WebUtility.HtmlEncode(v_strImportError) + "<br/>");
                    }

                    continue;
                }

                try
                {
                    var v_row = v_objRows.Header;
                    var v_strWarehouse = Import_Text(v_row[2]);
                    List<CWarehouseLookup> v_arrWarehouseMatches = new List<CWarehouseLookup>();
                    foreach (CWarehouseLookup v_objWarehouse in v_arrWarehouses)
                    {
                        if (Import_Equals(v_objWarehouse.Code, v_strWarehouse) || Import_Equals(v_objWarehouse.Name, v_strWarehouse))
                        {
                            v_arrWarehouseMatches.Add(v_objWarehouse);
                        }
                    }

                    if (v_strWarehouse.Length == 0 || v_arrWarehouseMatches.Count != 1)
                    {
                        throw new InvalidDataException("Không xác định duy nhất được kho được cấp quyền: " + v_strWarehouse);
                    }

                    int v_iNote_Column = 3;
                    if (m_bIs_Receipt) { v_iNote_Column = 4; }
                    var v_objDocument = new CWarehouseDocument
                    {
                        Is_Receipt = m_bIs_Receipt,
                        So_Phieu = v_objRows.Number,
                        Kho_ID = v_arrWarehouseMatches[0].Auto_ID,
                        Ngay_Chung_Tu = Parse_Import_Date(v_row[1]),
                        Ghi_Chu = Import_Text(v_row[v_iNote_Column]),
                        Created_By = r_strActive_User_Name,
                        Created_By_Function = r_strActive_Function_Code,
                        Last_Updated_By = r_strActive_User_Name,
                        Last_Updated_By_Function = r_strActive_Function_Code
                    };
                    if (v_objDocument.So_Phieu.Length > 100 || v_objDocument.Ghi_Chu.Length > 1000)
                    {
                        throw new InvalidDataException("Số phiếu hoặc ghi chú vượt độ dài tham số SQL.");
                    }

                    if (m_bIs_Receipt)
                    {
                        string v_strSupplier = Import_Text(v_row[3]);
                        List<CDM_NCC> v_arrMatches = new List<CDM_NCC>();
                        foreach (CDM_NCC v_objSupplier in v_arrSuppliers)
                        {
                            if (Import_Equals(v_objSupplier.Ma_NCC, v_strSupplier))
                            {
                                v_arrMatches.Add(v_objSupplier);
                            }
                        }

                        if (v_strSupplier.Length == 0 || v_arrMatches.Count != 1)
                        {
                            throw new InvalidDataException("Không xác định duy nhất được mã nhà cung cấp: " + v_strSupplier);
                        }

                        v_objDocument.NCC_ID = v_arrMatches[0].Auto_ID;
                    }

                    if (v_objRows.Details.Count == 0)
                    {
                        throw new InvalidDataException("Phiếu phải có ít nhất một dòng chi tiết.");
                    }

                    var v_arrDetails = new List<CWarehouseDocumentDetail>();
                    var v_arrDetailErrors = new List<string>();
                    if (m_bIs_Receipt)
                    {
                        v_strCurrentSheet = "Chi tiết nhập";
                    }
                    else
                    {
                        v_strCurrentSheet = "Chi tiết xuất";
                    }

                    foreach (var v_objDetailRow in v_objRows.Details)
                    {
                        v_iCurrentRow = v_objDetailRow.Row;
                        try
                        {
                            var v_detail = v_objDetailRow.Data;
                            string v_strCode = Import_Text(v_detail[1]);
                            List<CDM_San_Pham> v_arrMatches = new List<CDM_San_Pham>();
                            foreach (CDM_San_Pham v_objMatch in v_arrProducts)
                            {
                                if (Import_Equals(v_objMatch.Ma_San_Pham, v_strCode))
                                {
                                    v_arrMatches.Add(v_objMatch);
                                }
                            }

                            if (v_strCode.Length == 0 || v_arrMatches.Count != 1)
                            {
                                throw new InvalidDataException("Không xác định duy nhất được mã sản phẩm: " + v_strCode);
                            }

                            var v_objProduct = v_arrMatches[0];
                            bool v_bUnitFound = false;
                            foreach (CDM_Don_Vi_Tinh v_objUnit in v_arrUnits)
                            {
                                if (v_objUnit.Auto_ID == v_objProduct.Don_Vi_Tinh_ID)
                                {
                                    v_bUnitFound = true;
                                    break;
                                }
                            }
                            if (!v_bUnitFound)
                            {
                                throw new InvalidDataException("Đơn vị tính của sản phẩm không hợp lệ: " + v_strCode);
                            }

                            if (Import_Text(v_detail[2]).Length > 0 && !Import_Equals(v_objProduct.Ten_San_Pham, Import_Text(v_detail[2])))
                            {
                                throw new InvalidDataException("Tên hàng không khớp mã sản phẩm: " + v_strCode);
                            }

                            v_arrDetails.Add(new CWarehouseDocumentDetail { San_Pham_ID = v_objProduct.Auto_ID, So_Luong = Parse_Import_Decimal(v_detail[3], 3), Don_Gia = Parse_Import_Decimal(v_detail[4], 2), Created_By = r_strActive_User_Name, Created_By_Function = r_strActive_Function_Code, Last_Updated_By = r_strActive_User_Name, Last_Updated_By_Function = r_strActive_Function_Code });
                        }
                        catch (InvalidDataException v_objException)
                        {
                            v_arrDetailErrors.Add($"{Get_Language_Data_Field("Dòng")} {v_iCurrentRow} {Get_Language_Data_Field("có lỗi")}: {WebUtility.HtmlEncode(v_objRows.Number)} — {WebUtility.HtmlEncode(v_strCurrentSheet)}: {WebUtility.HtmlEncode(v_objException.Message)}");
                        }
                    }

                    if (v_arrDetailErrors.Count > 0)
                    {
                        p_iCount_Error++;
                        foreach (string v_strDetailError in v_arrDetailErrors)
                        {
                            v_sbError.AppendLine(v_strDetailError + "<br/>");
                        }

                        continue;
                    }

                    using (SqlConnection v_conn = CSqlHelper.CreateConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String))
                    {
                        v_conn.Open();
                        using SqlTransaction v_trans = v_conn.BeginTransaction();
                        try
                        {
                            v_iCurrentRow = v_objRows.Row;
                            if (m_bIs_Receipt)
                            {
                                v_strCurrentSheet = "Phiếu nhập";
                            }
                            else
                            {
                                v_strCurrentSheet = "Phiếu xuất";
                            }

                            if (m_bIs_Receipt)
                            {
                                v_objDocument.Auto_ID = m_objNhapKhoController.F3001_sp_ins_Nhap_Kho_Header(v_conn, v_trans, To_Nhap_Kho(v_objDocument));
                            }
                            else
                            {
                                v_objDocument.Auto_ID = m_objXuatKhoController.F3002_sp_ins_Xuat_Kho_Header(v_conn, v_trans, To_Xuat_Kho(v_objDocument));
                            }

                            for (int v_i = 0; v_i < v_arrDetails.Count; v_i++)
                            {
                                v_iCurrentRow = v_objRows.Details[v_i].Row;
                                if (m_bIs_Receipt)
                                {
                                    v_strCurrentSheet = "Chi tiết nhập";
                                }
                                else
                                {
                                    v_strCurrentSheet = "Chi tiết xuất";
                                }

                                var v_objDetail = v_arrDetails[v_i];
                                v_objDetail.Document_ID = v_objDocument.Auto_ID;
                                if (m_bIs_Receipt)
                                {
                                    m_objNhapKhoController.F3001_sp_ins_Nhap_Kho_Detail(v_conn, v_trans, To_Nhap_Kho_Detail(v_objDetail));
                                }
                                else
                                {
                                    m_objXuatKhoController.F3002_sp_ins_Xuat_Kho_Detail(v_conn, v_trans, To_Xuat_Kho_Detail(v_objDetail));
                                }
                            }

                            v_trans.Commit();
                        }
                        catch
                        {
                            try
                            {
                                v_trans.Rollback();
                            }
                            catch
                            {
                            }

                            throw;
                        }
                    }

                    p_iCount_Success++;
                    try
                    {
                        CCommon_Controller.FCommon_Insert_Action_History(v_objDocument.Auto_ID, Get_Language_Data_Field("Import Excel - Thêm Mới"), "Web", r_strActive_User_Name, r_strActive_Function_Code, r_objChuc_Nang.Ten_Chuc_Nang, "Key: " + v_objDocument.So_Phieu);
                    }
                    catch (Exception v_objException)
                    {
                        v_sbWarning.AppendLine($"{Get_Language_Data_Field("Đã lưu nhưng chưa ghi lịch sử")}: {WebUtility.HtmlEncode(v_objRows.Number)}: {WebUtility.HtmlEncode(v_objException.Message)}<br/>");
                    }
                }
                catch (Exception v_objException) when (
                    v_objException is InvalidDataException ||
                    (v_objException is Microsoft.Data.SqlClient.SqlException v_objSqlException &&
                     (v_objSqlException.Number >= 50000 || v_objSqlException.Number == 2601 || v_objSqlException.Number == 2627 || v_objSqlException.Number == 547 || v_objSqlException.Number == 515)))
                {
                    p_iCount_Error++;
                    v_sbError.AppendLine(Get_Language_Data_Field("Dòng") + " " + v_iCurrentRow + " " + Get_Language_Data_Field("có lỗi") + ": " + WebUtility.HtmlEncode(v_objRows.Number) + " — " + v_strCurrentSheet + ": " + WebUtility.HtmlEncode(v_objException.Message) + "<br/>");
                }
            }

            if (v_arrDocuments.Count == 0)
            {
                v_sbError.AppendLine(Get_Language_Data_Field("File không có chứng từ."));
            }

            m_objSelectedDocument = null;
            m_arrDetail.Clear();
        }
        catch (Exception v_objException)
        {
            p_iCount_Error++;
            if (v_objException is InvalidDataException && !v_objException.Message.Contains("dòng", StringComparison.OrdinalIgnoreCase))
            {
                v_sbError.AppendLine(Get_Language_Data_Field("Dòng") + " 1 " + Get_Language_Data_Field("có lỗi") + ": " + WebUtility.HtmlEncode(v_objException.Message) + "<br/>");
            }
            else
            {
                v_sbError.AppendLine(WebUtility.HtmlEncode(v_objException.Message) + "<br/>");
            }
        }

        string v_strMessage = CCommonFunction.Set_Success_MessageBox(string.Format(Get_Language_Data_Field("Thêm mới {0} phiếu thành công."), p_iCount_Success));
        if (v_sbError.Length > 0)
        {
            v_strMessage += CCommonFunction.Set_Error_MessageBox(v_sbError.ToString());
        }

        if (v_sbWarning.Length > 0)
        {
            v_strMessage += CCommonFunction.Set_Error_MessageBox(v_sbWarning.ToString());
        }

        Open_Message_Info(Get_Language_Data_Field("Thông báo Import Excel"), v_strMessage);
    }

    // Đọc các worksheet nghiệp vụ và mapping theo vị trí cột cố định.
    private List<CDocumentImportRows> Read_Import_Documents(CExcel_Controller p_objExcel, StringBuilder p_sbError, ref int p_iCount_Error)
    {
        string v_strHeaderSheet;
        if (m_bIs_Receipt)
        {
            v_strHeaderSheet = "Phiếu nhập";
        }
        else
        {
            v_strHeaderSheet = "Phiếu xuất";
        }

        string v_strDetailSheet;
        if (m_bIs_Receipt)
        {
            v_strDetailSheet = "Chi tiết nhập";
        }
        else
        {
            v_strDetailSheet = "Chi tiết xuất";
        }

        string v_strHeader_Last_Column;
        int v_iHeader_Column_Count;
        if (m_bIs_Receipt)
        {
            v_strHeader_Last_Column = "E";
            v_iHeader_Column_Count = 5;
        }
        else
        {
            v_strHeader_Last_Column = "D";
            v_iHeader_Column_Count = 4;
        }

        using DataTable v_dtHeader = Read_Import_Sheet(p_objExcel, v_strHeaderSheet, v_strHeader_Last_Column);
        using DataTable v_dtDetail = Read_Import_Sheet(p_objExcel, v_strDetailSheet, "E");
        if (v_dtHeader.Rows.Count > 20000 || v_dtDetail.Rows.Count > 20000)
        {
            throw new InvalidDataException("Worksheet vượt quá 20000 dòng dữ liệu.");
        }

        var v_arrDocuments = new List<CDocumentImportRows>();
        var v_dicDocuments = new Dictionary<string, CDocumentImportRows>(StringComparer.OrdinalIgnoreCase);
        var v_setOrphanDocumentNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int v_iRow = 1;
        foreach (DataRow v_row in v_dtHeader.Rows)
        {
            v_iRow++;
            if (v_row.ItemArray.Length == 0)
            {
                p_iCount_Error++;
                p_sbError.AppendLine(WebUtility.HtmlEncode($"{Get_Language_Data_Field("Dòng")} {v_iRow} {Get_Language_Data_Field("có lỗi")}: {v_strHeaderSheet}: không có cột dữ liệu để đọc số phiếu.") + "<br/>");
                continue;
            }

            bool v_bEmpty_Row = true;
            foreach (object v_objValue in v_row.ItemArray)
            {
                if (Import_Text(v_objValue).Length != 0) { v_bEmpty_Row = false; break; }
            }
            if (v_bEmpty_Row)
            {
                continue;
            }

            string v_strNumber = Import_Text(v_row[0]);
            if (v_strNumber.Length == 0)
            {
                p_iCount_Error++;
                p_sbError.AppendLine(WebUtility.HtmlEncode($"{Get_Language_Data_Field("Dòng")} {v_iRow} {Get_Language_Data_Field("có lỗi")}: {v_strHeaderSheet}: thiếu số phiếu, không thể ghép chi tiết.") + "<br/>");
                continue;
            }

            if (v_dicDocuments.TryGetValue(v_strNumber, out CDocumentImportRows v_objExistingRows))
            {
                v_objExistingRows.Errors.Add($"{Get_Language_Data_Field("Dòng")} {v_iRow} {Get_Language_Data_Field("có lỗi")}: {v_strHeaderSheet}: Header trùng số phiếu '{v_strNumber}' trong file.");
                continue;
            }

            var v_objRows = new CDocumentImportRows(v_row, v_iRow, v_strNumber);
            if (v_row.ItemArray.Length < v_iHeader_Column_Count)
            {
                v_objRows.Errors.Add($"{Get_Language_Data_Field("Dòng")} {v_iRow} {Get_Language_Data_Field("có lỗi")}: {v_strHeaderSheet}: thiếu cột dữ liệu Header cần thiết để mapping.");
            }

            v_dicDocuments.Add(v_strNumber, v_objRows);
            v_arrDocuments.Add(v_objRows);
        }

        v_iRow = 1;
        foreach (DataRow v_row in v_dtDetail.Rows)
        {
            v_iRow++;
            if (v_row.ItemArray.Length == 0)
            {
                p_iCount_Error++;
                p_sbError.AppendLine(WebUtility.HtmlEncode($"{Get_Language_Data_Field("Dòng")} {v_iRow} {Get_Language_Data_Field("có lỗi")}: {v_strDetailSheet}: không có cột dữ liệu để đọc số phiếu.") + "<br/>");
                continue;
            }

            bool v_bEmpty_Row = true;
            foreach (object v_objValue in v_row.ItemArray)
            {
                if (Import_Text(v_objValue).Length != 0) { v_bEmpty_Row = false; break; }
            }
            if (v_bEmpty_Row)
            {
                continue;
            }

            string v_strNumber = Import_Text(v_row[0]);
            if (v_strNumber.Length == 0)
            {
                p_iCount_Error++;
                p_sbError.AppendLine(WebUtility.HtmlEncode($"{Get_Language_Data_Field("Dòng")} {v_iRow} {Get_Language_Data_Field("có lỗi")}: {v_strDetailSheet}: thiếu số phiếu để liên kết Header–Detail.") + "<br/>");
                continue;
            }

            if (!v_dicDocuments.TryGetValue(v_strNumber, out CDocumentImportRows v_objRows))
            {
                if (v_setOrphanDocumentNumbers.Add(v_strNumber))
                {
                    p_iCount_Error++;
                }

                p_sbError.AppendLine(WebUtility.HtmlEncode($"{Get_Language_Data_Field("Dòng")} {v_iRow} {Get_Language_Data_Field("có lỗi")}: {v_strDetailSheet}: không tìm thấy Header cho số phiếu '{v_strNumber}'.") + "<br/>");
                continue;
            }

            if (v_row.ItemArray.Length < 5)
            {
                v_objRows.Errors.Add($"{Get_Language_Data_Field("Dòng")} {v_iRow} {Get_Language_Data_Field("có lỗi")}: {v_strDetailSheet}: thiếu cột dữ liệu chi tiết cần thiết để mapping.");
                continue;
            }

            v_objRows.Details.Add((v_row, v_iRow));
        }

        return v_arrDocuments;
    }

    private static DataTable Read_Import_Sheet(CExcel_Controller p_objExcel, string p_strName, string p_strLastColumn)
    {
        try
        {
            return p_objExcel.List_Range_Value_To_End(p_strName, "A2", p_strLastColumn);
        }
        catch (NullReferenceException v_objException)
        {
            throw new InvalidDataException($"Không thể đọc worksheet '{p_strName}'. Hãy kiểm tra worksheet tồn tại và có vùng dữ liệu.", v_objException);
        }
    }

    private static string Import_Text(object p_objValue)
    {
        return CUtility.Convert_To_String(p_objValue).Trim().Normalize();
    }

    private static bool Import_Equals(string? p_strLeft, string p_strRight)
    {
        return string.Equals(p_strLeft?.Trim().Normalize(), p_strRight, StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime Parse_Import_Date(object p_objValue)
    {
        string v_strValue = Import_Text(p_objValue);
        if (DateTime.TryParseExact(v_strValue, new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v_dtmValue))
        {
            return v_dtmValue.Date;
        }

        // CExcel_Controller lưu DataColumn dạng string, DateTime của Excel được đổi theo culture hiện tại.
        if (DateTime.TryParse(v_strValue, CultureInfo.CurrentCulture, DateTimeStyles.None, out v_dtmValue))
        {
            return v_dtmValue.Date;
        }

        if (double.TryParse(v_strValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double v_dblSerial) &&
            !double.IsNaN(v_dblSerial) &&
            !double.IsInfinity(v_dblSerial) &&
            v_dblSerial >= -657435 &&
            v_dblSerial < 2958466)
        {
            v_dtmValue = DateTime.FromOADate(v_dblSerial);
            if (v_dtmValue.Year >= 1900 && v_dtmValue.Year <= 9999)
            {
                return v_dtmValue.Date;
            }
        }

        throw new InvalidDataException("Ngày chứng từ không hợp lệ: " + v_strValue);
    }

    private static decimal Parse_Import_Decimal(object p_objValue, int p_iScale)
    {
        string v_strValue = Import_Text(p_objValue);
        if (!decimal.TryParse(v_strValue, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out decimal v_decValue) && !decimal.TryParse(v_strValue, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out v_decValue))
        {
            throw new InvalidDataException("Giá trị số không hợp lệ: " + v_strValue);
        }

        decimal v_decLimit = 10000000000000000m;
        if (p_iScale == 3) { v_decLimit = 1000000000000000m; }
        if (decimal.Round(v_decValue, p_iScale) != v_decValue || v_decValue <= -v_decLimit || v_decValue >= v_decLimit)
        {
            throw new InvalidDataException($"Giá trị vượt kiểu SQL decimal(18,{p_iScale}): {v_strValue}");
        }

        // SQL chịu trách nhiệm điều kiện số lượng/đơn giá dương và tồn khả dụng.
        return v_decValue;
    }

    private sealed class CDocumentImportRows
    {
        public DataRow Header { get; }
        public int Row { get; }
        public string Number { get; }
        public List<(DataRow Data, int Row)> Details { get; } = new();
        public List<string> Errors { get; } = new();

        public CDocumentImportRows(DataRow p_objHeader, int p_iRow, string p_strNumber)
        {
            Header = p_objHeader;
            Row = p_iRow;
            Number = p_strNumber;
        }
    }
}
