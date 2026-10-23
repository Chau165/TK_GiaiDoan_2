using System.Collections;
using System.Data;
using System.Globalization;
using System.Reflection;
using OfficeOpenXml;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Danh_Muc.Pages.Danh_Muc.Components;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class CompanyOriginalExcelImportTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Documents_keep_physical_rows_and_group_details_after_blank_rows(bool p_bReceipt)
    {
        WithWorkbook(p_bReceipt, v_objPackage =>
        {
            var v_header = v_objPackage.Workbook.Worksheets[0];
            var v_detail = v_objPackage.Workbook.Worksheets[1];
            v_header.Cells[2, 1].Value = "0001";
            v_header.Cells[4, 1].Value = "0002";
            v_detail.Cells[2, 1].Value = "0002";
            v_detail.Cells[2, 2].Value = "SP02";
            v_detail.Cells[4, 1].Value = "0001";
            v_detail.Cells[4, 2].Value = "SP01";
            v_detail.Cells[5, 1].Value = "0001";
            v_detail.Cells[5, 2].Value = "SP03";
            v_detail.Cells[7, 2].Style.Numberformat.Format = "@";
        }, v_objExcel =>
        {
            var v_arrRows = ReadDocuments(p_bReceipt, v_objExcel);
            Assert.Equal(2, v_arrRows.Count);
            Assert.Equal(2, Property<int>(v_arrRows[0]!, "Row"));
            Assert.Equal(4, Property<int>(v_arrRows[1]!, "Row"));
            var v_arrDetails = Property<List<(DataRow Data, int Row)>>(v_arrRows[0]!, "Details");
            Assert.Equal(new[] { 4, 5 }, v_arrDetails.Select(v_objRow => v_objRow.Row));
            Assert.Equal(new[] { "SP01", "SP03" }, v_arrDetails.Select(v_objRow => v_objRow.Data[1]));
            Assert.Equal("0001", Property<string>(v_arrRows[0]!, "Number"));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Header_only_template_has_zero_documents(bool p_bReceipt) =>
        WithWorkbook(p_bReceipt, _ => { }, v_objExcel => Assert.Empty(ReadDocuments(p_bReceipt, v_objExcel)));

    [Theory]
    [InlineData("orphan")]
    [InlineData("duplicate-header")]
    [InlineData("empty-header-key")]
    [InlineData("empty-detail-key")]
    [InlineData("wrong-detail-header")]
    [InlineData("extra-detail-column")]
    [InlineData("wrong-sheet")]
    public void Ambiguous_structure_is_rejected_before_any_persistence(string p_strCase)
    {
        WithWorkbook(true, v_objPackage =>
        {
            var v_header = v_objPackage.Workbook.Worksheets[0];
            var v_detail = v_objPackage.Workbook.Worksheets[1];
            v_header.Cells[2, 1].Value = "PN01";
            v_detail.Cells[4, 1].Value = "PN01";
            v_detail.Cells[4, 2].Value = "SP01";
            switch (p_strCase)
            {
                case "orphan": v_detail.Cells[4, 1].Value = "PN99"; break;
                case "duplicate-header": v_header.Cells[4, 1].Value = "pn01"; break;
                case "empty-header-key": v_header.Cells[2, 1].Value = null; v_header.Cells[2, 5].Value = "Có ghi chú"; break;
                case "empty-detail-key": v_detail.Cells[4, 1].Value = null; break;
                case "wrong-detail-header": v_detail.Cells[1, 4].Value = "Sai số lượng"; break;
                case "extra-detail-column": v_detail.Cells[1, 100].Value = "LOT"; break;
                case "wrong-sheet": v_detail.Name = "Không đúng sheet"; break;
            }
        }, v_objExcel =>
        {
            Exception v_objError = Assert.Throws<TargetInvocationException>(() => ReadDocuments(true, v_objExcel)).InnerException!;
            Assert.NotNull(v_objError);
            if (p_strCase == "orphan" || p_strCase == "duplicate-header" || p_strCase == "empty-detail-key")
                Assert.Contains("4", v_objError.Message);
        });
    }

    [Fact]
    public void Company_reader_preserves_unicode_blank_key_rows_and_cached_formula_values()
    {
        WithWorkbook(true, v_objPackage =>
        {
            var v_sheet = v_objPackage.Workbook.Worksheets[0];
            v_sheet.Cells[2, 1].Value = "0001";
            v_sheet.Cells[2, 5].Value = "Tiếng Việt: Đơn vị tính";
            v_sheet.Cells[4, 5].Value = "Khóa rỗng vẫn có dữ liệu";
            v_sheet.Cells[5, 1].Formula = "1+2";
            v_objPackage.Workbook.Calculate();
        }, v_objExcel =>
        {
            using DataTable v_dt = v_objExcel.List_Range_Value_To_End(0, "A2", "E");
            Assert.Equal("0001", v_dt.Rows[0][0]);
            Assert.Equal("Tiếng Việt: Đơn vị tính", v_dt.Rows[0][4]);
            Assert.Equal("Khóa rỗng vẫn có dữ liệu", v_dt.Rows[2][4]);
            Assert.Equal("3", v_dt.Rows[3][0]);
        });
    }

    [Theory]
    [InlineData("21/10/2026", "2026-10-21")]
    [InlineData("2026-10-21", "2026-10-21")]
    [InlineData("46316", "2026-10-21")]
    public void Document_dates_accept_template_text_and_excel_serials(string p_strValue, string p_strExpected)
    {
        var v_dtmDate = (DateTime)InvokeStatic("Parse_Import_Date", p_strValue)!;
        Assert.Equal(p_strExpected, v_dtmDate.ToString("yyyy-MM-dd"));
    }

    [Theory]
    [InlineData("1.234", 3)]
    [InlineData("1234.56", 2)]
    [InlineData("0", 3)]
    [InlineData("-1", 3)]
    public void Numeric_mapping_preserves_values_for_sql_business_validation(string p_strValue, int p_iScale)
    {
        var v_oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal(decimal.Parse(p_strValue, CultureInfo.InvariantCulture), InvokeStatic("Parse_Import_Decimal", p_strValue, p_iScale));
        }
        finally { CultureInfo.CurrentCulture = v_oldCulture; }
    }

    [Theory]
    [InlineData("1.2345", 3)]
    [InlineData("1.234", 2)]
    [InlineData("1000000000000000", 3)]
    [InlineData("1,234", 2)]
    [InlineData("", 2)]
    public void Invalid_numeric_mapping_cannot_be_silently_rounded_or_defaulted(string p_strValue, int p_iScale)
    {
        var v_oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.IsType<InvalidDataException>(Assert.Throws<TargetInvocationException>(() =>
                InvokeStatic("Parse_Import_Decimal", p_strValue, p_iScale)).InnerException);
        }
        finally { CultureInfo.CurrentCulture = v_oldCulture; }
    }

    [Theory]
    [InlineData("F2009_1_Kho_List.razor", "CCache_Kho")]
    [InlineData("FWarehouse_Unit_List.razor", "CCache_Don_Vi_Tinh")]
    [InlineData("F2017_1_Loai_San_Pham_List.razor", "CCache_Loai_San_Pham")]
    [InlineData("F2018_1_San_Pham_List.razor", "CCache_San_Pham")]
    [InlineData("F2019_1_Nha_Cung_Cap_List.razor", "CCache_NCC")]
    public void Master_import_dispatches_every_nonblank_row_and_isolates_post_commit_side_effects(string p_strFile, string p_strCache)
    {
        string v_strSource = File.ReadAllText(RepositoryFile("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", p_strFile));
        v_strSource = v_strSource[v_strSource.IndexOf("protected override void Import_Excel_Entry", StringComparison.Ordinal)..];
        Assert.Contains("foreach (DataRow v_row", v_strSource);
        Assert.Contains("p_iCount_Error++", v_strSource);
        Assert.Contains("Is_Have_Add_Permission", v_strSource);
        Assert.DoesNotContain("Is_Skipped", v_strSource);
        Assert.DoesNotContain("ImportValidator", v_strSource);
        Assert.DoesNotContain("Read_Profiled", v_strSource);
        Assert.DoesNotContain("sp_sel_List", v_strSource.Replace("sp_sel_List_Loai_San_Pham_Lookup", "").Replace("sp_sel_List_Don_Vi_Tinh_Lookup", ""));
        int v_iCommit = v_strSource.IndexOf("v_trans.Commit()", StringComparison.Ordinal);
        Assert.True(v_iCommit < v_strSource.IndexOf("p_iCount_Success++", StringComparison.Ordinal));
        Assert.True(v_iCommit < v_strSource.IndexOf(p_strCache + ".Add_Data", StringComparison.Ordinal));
        Assert.True(v_iCommit < v_strSource.IndexOf("FCommon_Insert_Action_History", StringComparison.Ordinal));
    }

    [Fact]
    public void Transactions_use_one_FBase_dispatcher_and_one_atomic_header_detail_boundary()
    {
        string v_strList = File.ReadAllText(RepositoryFile("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_Transaction_List.razor"));
        string v_strImport = File.ReadAllText(RepositoryFile("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_Transaction_List.razor.cs"));
        Assert.Contains("<FCommon_Import_Excel", v_strList);
        Assert.Contains("OnClose=\"@Close_Import_Excel\"", v_strList);
        Assert.DoesNotContain("FWarehouse_Transaction_Import", v_strList);
        Assert.DoesNotContain("FQ_422_IE_sp_ins_Insert", v_strList + v_strImport);
        Assert.Contains("m_grdDocument.Rebind()", v_strList);
        Assert.Contains("F2018_sp_sel_List_San_Pham()", v_strImport);
        Assert.DoesNotContain("F2018_sp_sel_List_San_Pham_Lookup()", v_strImport);
        Assert.Contains("F3001_sp_ins_Nhap_Kho_Header(v_conn, v_trans", v_strImport);
        Assert.Contains("F3001_sp_ins_Nhap_Kho_Detail(v_conn, v_trans", v_strImport);
        Assert.Contains("F3002_sp_ins_Xuat_Kho_Header(v_conn, v_trans", v_strImport);
        Assert.Contains("F3002_sp_ins_Xuat_Kho_Detail(v_conn, v_trans", v_strImport);
        Assert.True(v_strImport.IndexOf("v_trans.Commit()", StringComparison.Ordinal) < v_strImport.IndexOf("p_iCount_Success++", StringComparison.Ordinal));
        Assert.DoesNotContain("Post_", v_strImport);
        Assert.Contains("FCommon_Insert_Action_History(v_objDocument.Auto_ID", v_strImport);
    }

    internal static string RepositoryFile(params string[] p_arrParts)
    {
        for (DirectoryInfo? v_dir = new(AppContext.BaseDirectory); v_dir != null; v_dir = v_dir.Parent)
        {
            string v_strPath = Path.Combine(new[] { v_dir.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_strPath)) return v_strPath;
        }
        throw new FileNotFoundException(Path.Combine(p_arrParts));
    }

    private static T Property<T>(object p_objValue, string p_strProperty) =>
        (T)p_objValue.GetType().GetProperty(p_strProperty)!.GetValue(p_objValue)!;
    private static object? InvokeStatic(string p_strMethod, params object[] p_arrArgs) =>
        typeof(FWarehouse_Transaction_List).GetMethod(p_strMethod, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, p_arrArgs);
    private static IList ReadDocuments(bool p_bReceipt, CExcel_Controller p_objExcel)
    {
        var v_objComponent = new FWarehouse_Transaction_List();
        typeof(FWarehouse_Transaction_List).GetProperty("m_bIs_Receipt")!.SetValue(v_objComponent, p_bReceipt);
        return (IList)typeof(FWarehouse_Transaction_List).GetMethod("Read_Import_Documents", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(v_objComponent, new object[] { p_objExcel })!;
    }

    private static void WithWorkbook(bool p_bReceipt, Action<ExcelPackage> p_funcArrange, Action<CExcel_Controller> p_funcAssert)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        string v_strPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
        try
        {
            using (var v_objPackage = new ExcelPackage(new FileInfo(RepositoryFile("TKS_Thuc_Tap_V11_Web", "wwwroot", "FileManagement", "Template", p_bReceipt ? "Nhap_Kho_Import_Template.xlsx" : "Xuat_Kho_Import_Template.xlsx"))))
            {
                p_funcArrange(v_objPackage);
                v_objPackage.SaveAs(new FileInfo(v_strPath));
            }
            var v_objExcel = new CExcel_Controller(new FileInfo(v_strPath));
            try { p_funcAssert(v_objExcel); }
            finally { v_objExcel.Close(); }
        }
        finally { File.Delete(v_strPath); }
    }
}
