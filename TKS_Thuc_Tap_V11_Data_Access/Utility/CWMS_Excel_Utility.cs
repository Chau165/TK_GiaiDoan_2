using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using OfficeOpenXml;

namespace TKS_Thuc_Tap_V11_Data_Access.Utility
{
    public static class CWMS_Excel_Utility
    {
        private const string TEMPLATE_ERROR_MESSAGE = "File Excel không đúng template. Vui lòng sử dụng template được cung cấp cho chức năng này.";

        public static void Validate_Header_Matches(FileInfo p_objSource_File, FileInfo p_objTemplate_File)
        {
            Validate_Template(p_objSource_File, p_objTemplate_File, false);
        }

        public static void Validate_Import_File(FileInfo p_objSource_File, FileInfo p_objTemplate_File)
        {
            Validate_Template(p_objSource_File, p_objTemplate_File, true);
        }

        private static void Validate_Template(FileInfo p_objSource_File, FileInfo p_objTemplate_File, bool p_bRequireDataRows)
        {
            if (p_objSource_File == null || !p_objSource_File.Exists)
            {
                throw new InvalidDataException("Không tìm thấy file Excel cần nhập.");
            }

            if (p_objTemplate_File == null || !p_objTemplate_File.Exists)
            {
                throw new InvalidDataException("Không tìm thấy file template Excel để kiểm tra cấu trúc.");
            }

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using ExcelPackage v_objSource_Package = Open_Package(
                p_objSource_File,
                "Không thể đọc workbook Excel được tải lên. Hãy kiểm tra file và thử lưu lại dưới dạng .xlsx.");
            using ExcelPackage v_objTemplate_Package = Open_Package(
                p_objTemplate_File,
                "Không thể đọc workbook template Excel của chức năng này.");

            var v_arrErrors = new List<string>();
            int v_iExpected_Sheet_Count = v_objTemplate_Package.Workbook.Worksheets.Count;
            int v_iActual_Sheet_Count = v_objSource_Package.Workbook.Worksheets.Count;

            if (v_iExpected_Sheet_Count == 0)
            {
                throw new InvalidDataException("Template Excel không hợp lệ: workbook không có worksheet.");
            }

            if (v_iExpected_Sheet_Count != v_iActual_Sheet_Count)
            {
                v_arrErrors.Add($"Số worksheet: yêu cầu {v_iExpected_Sheet_Count}, thực tế {v_iActual_Sheet_Count}.");
            }

            int v_iShared_Sheet_Count = Math.Min(v_iExpected_Sheet_Count, v_iActual_Sheet_Count);
            for (int v_iSheet = 0; v_iSheet < v_iShared_Sheet_Count; v_iSheet++)
            {
                ExcelWorksheet v_objExpected_Sheet = v_objTemplate_Package.Workbook.Worksheets[v_iSheet];
                ExcelWorksheet v_objActual_Sheet = v_objSource_Package.Workbook.Worksheets[v_iSheet];

                if (!string.Equals(v_objActual_Sheet.Name, v_objExpected_Sheet.Name, StringComparison.Ordinal))
                {
                    v_arrErrors.Add(
                        $"Worksheet {v_iSheet + 1}: yêu cầu '{Encode(v_objExpected_Sheet.Name)}', thực tế '{Encode(v_objActual_Sheet.Name)}'.");
                }

                string[] v_arrExpected_Headers = Read_Header_Row(v_objExpected_Sheet, true);
                string[] v_arrActual_Headers = Read_Header_Row(v_objActual_Sheet, false);
                int v_iCompared_Column_Count = Math.Max(v_arrExpected_Headers.Length, v_arrActual_Headers.Length);

                for (int v_iColumn = 0; v_iColumn < v_iCompared_Column_Count; v_iColumn++)
                {
                    string v_strExpected_Header = v_iColumn < v_arrExpected_Headers.Length
                        ? v_arrExpected_Headers[v_iColumn]
                        : null;
                    string v_strActual_Header = v_iColumn < v_arrActual_Headers.Length
                        ? v_arrActual_Headers[v_iColumn]
                        : null;

                    if (v_strExpected_Header == null && v_strActual_Header != null)
                    {
                        v_arrErrors.Add(
                            $"Worksheet '{Encode(v_objExpected_Sheet.Name)}', cột {Get_Column_Letter(v_iColumn + 1)}: không cho phép thêm header '{Encode(v_strActual_Header)}'.");
                    }
                    else if (v_strExpected_Header != null &&
                             (v_strActual_Header == null ||
                              !string.Equals(v_strActual_Header, v_strExpected_Header, StringComparison.Ordinal)))
                    {
                        string v_strActual_Display = string.IsNullOrEmpty(v_strActual_Header)
                            ? "(trống)"
                            : Encode(v_strActual_Header);
                        v_arrErrors.Add(
                            $"Worksheet '{Encode(v_objExpected_Sheet.Name)}', cột {Get_Column_Letter(v_iColumn + 1)}: yêu cầu '{Encode(v_strExpected_Header)}', thực tế '{v_strActual_Display}'.");
                    }
                }
            }

            for (int v_iSheet = v_iShared_Sheet_Count; v_iSheet < v_iExpected_Sheet_Count; v_iSheet++)
            {
                string v_strExpected_Name = v_objTemplate_Package.Workbook.Worksheets[v_iSheet].Name;
                v_arrErrors.Add($"Thiếu worksheet '{Encode(v_strExpected_Name)}' ở vị trí {v_iSheet + 1}.");
            }

            for (int v_iSheet = v_iShared_Sheet_Count; v_iSheet < v_iActual_Sheet_Count; v_iSheet++)
            {
                string v_strActual_Name = v_objSource_Package.Workbook.Worksheets[v_iSheet].Name;
                v_arrErrors.Add($"Worksheet thừa ở vị trí {v_iSheet + 1}: '{Encode(v_strActual_Name)}'.");
            }

            if (v_arrErrors.Count > 0)
            {
                throw Create_Template_Exception();
            }

            if (p_bRequireDataRows && !Has_Data_Rows(v_objSource_Package, v_objTemplate_Package))
            {
                throw new InvalidDataException("File Excel đúng template nhưng không có dòng dữ liệu để import.");
            }
        }

        private static ExcelPackage Open_Package(FileInfo p_objFile, string p_strError_Message)
        {
            try
            {
                return new ExcelPackage(p_objFile);
            }
            catch (Exception v_objException) when (v_objException is not OutOfMemoryException)
            {
                throw new InvalidDataException(p_strError_Message, v_objException);
            }
        }

        private static string[] Read_Header_Row(ExcelWorksheet p_objWorksheet, bool p_bIsTemplate)
        {
            int v_iLast_Column = p_objWorksheet.Dimension?.End.Column ?? 0;
            int v_iLast_Header_Column = 0;
            string[] v_arrHeaders = new string[v_iLast_Column];

            for (int v_iColumn = 1; v_iColumn <= v_iLast_Column; v_iColumn++)
            {
                string v_strHeader = Normalize_Header(p_objWorksheet.Cells[1, v_iColumn].Text);
                v_arrHeaders[v_iColumn - 1] = v_strHeader;
                if (v_strHeader.Length > 0)
                {
                    v_iLast_Header_Column = v_iColumn;
                }
            }

            if (v_iLast_Header_Column == 0)
            {
                if (p_bIsTemplate)
                {
                    throw new InvalidDataException(
                        $"Template Excel không hợp lệ: worksheet '{Encode(p_objWorksheet.Name)}' không có header ở dòng 1.");
                }

                return Array.Empty<string>();
            }

            string[] v_arrResult = new string[v_iLast_Header_Column];
            Array.Copy(v_arrHeaders, v_arrResult, v_iLast_Header_Column);

            if (p_bIsTemplate)
            {
                for (int v_iColumn = 0; v_iColumn < v_arrResult.Length; v_iColumn++)
                {
                    if (v_arrResult[v_iColumn].Length == 0)
                    {
                        throw new InvalidDataException(
                            $"Template Excel không hợp lệ: worksheet '{Encode(p_objWorksheet.Name)}' thiếu header tại cột {Get_Column_Letter(v_iColumn + 1)}.");
                    }
                }
            }

            return v_arrResult;
        }

        private static bool Has_Data_Rows(ExcelPackage p_objSource_Package, ExcelPackage p_objTemplate_Package)
        {
            for (int v_iSheet = 0; v_iSheet < p_objTemplate_Package.Workbook.Worksheets.Count; v_iSheet++)
            {
                ExcelWorksheet v_objSource_Sheet = p_objSource_Package.Workbook.Worksheets[v_iSheet];
                ExcelWorksheet v_objTemplate_Sheet = p_objTemplate_Package.Workbook.Worksheets[v_iSheet];
                int v_iData_Column_Count = Read_Header_Row(v_objTemplate_Sheet, true).Length;
                int v_iLast_Row = v_objSource_Sheet.Dimension?.End.Row ?? 1;

                for (int v_iRow = 2; v_iRow <= v_iLast_Row; v_iRow++)
                {
                    for (int v_iColumn = 1; v_iColumn <= v_iData_Column_Count; v_iColumn++)
                    {
                        object v_objValue = v_objSource_Sheet.Cells[v_iRow, v_iColumn].Value;
                        if (v_objValue != null &&
                            (v_objValue is not string v_strValue || !string.IsNullOrWhiteSpace(v_strValue)))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static InvalidDataException Create_Template_Exception()
        {
            return new InvalidDataException(TEMPLATE_ERROR_MESSAGE);
        }

        private static string Normalize_Header(string p_strHeader)
        {
            string v_strNormalized = p_strHeader ?? string.Empty;
            var v_sbHeader = new StringBuilder(v_strNormalized.Length);
            bool v_bHasPendingSpace = false;

            foreach (char v_chr in v_strNormalized)
            {
                if (char.IsWhiteSpace(v_chr))
                {
                    v_bHasPendingSpace = v_sbHeader.Length > 0;
                    continue;
                }

                if (v_bHasPendingSpace)
                {
                    v_sbHeader.Append(' ');
                    v_bHasPendingSpace = false;
                }

                v_sbHeader.Append(v_chr);
            }

            return v_sbHeader.ToString();
        }

        private static string Get_Column_Letter(int p_iColumn)
        {
            var v_sbColumn = new StringBuilder();
            while (p_iColumn > 0)
            {
                p_iColumn--;
                v_sbColumn.Insert(0, (char)('A' + p_iColumn % 26));
                p_iColumn /= 26;
            }

            return v_sbColumn.ToString();
        }

        private static string Encode(string p_strValue)
        {
            return WebUtility.HtmlEncode(p_strValue ?? string.Empty);
        }
    }
}
