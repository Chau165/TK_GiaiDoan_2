using System;
using System.IO;
using System.Net;
using ClosedXML.Excel;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class LegacyExcelHeaderValidationTests
{
    [Fact]
    public void Validate_Header_Matches_accepts_the_configured_template_order()
    {
        using var v_objFixture = new WorkbookFixture(
            new[] { new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" } },
            new[] { new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" } });

        CWMS_Excel_Utility.Validate_Header_Matches(v_objFixture.Source, v_objFixture.Template);
    }

    [Fact]
    public void Validate_Header_Matches_ignores_extra_whitespace()
    {
        using var v_objFixture = new WorkbookFixture(
            new[] { new[] { "  Mã   Chủ Hàng  ", "Tên Chủ Hàng" } },
            new[] { new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" } });

        CWMS_Excel_Utility.Validate_Header_Matches(v_objFixture.Source, v_objFixture.Template);
    }

    [Fact]
    public void Validate_Header_Matches_rejects_header_case_changes()
    {
        using var v_objFixture = new WorkbookFixture(
            new[] { new[] { "Mã chủ hàng", "Tên Chủ Hàng" } },
            new[] { new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" } });

        Assert.Throws<InvalidDataException>(() =>
            CWMS_Excel_Utility.Validate_Header_Matches(v_objFixture.Source, v_objFixture.Template));
    }

    [Fact]
    public void Validate_Header_Matches_rejects_reordered_or_shifted_headers()
    {
        using var v_objFixture = new WorkbookFixture(
            new[] { new[] { "Tên Chủ Hàng", "Mã Chủ Hàng" } },
            new[] { new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" } });

        Assert.Throws<InvalidDataException>(() =>
            CWMS_Excel_Utility.Validate_Header_Matches(v_objFixture.Source, v_objFixture.Template));
    }

    [Fact]
    public void Validate_Header_Matches_reports_the_expected_and_actual_header()
    {
        using var v_objFixture = new WorkbookFixture(
            new[] { new[] { "Mã Chủ Hàng", "Ghi Chú" } },
            new[] { new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" } });

        var v_objException = Assert.Throws<InvalidDataException>(() =>
            CWMS_Excel_Utility.Validate_Header_Matches(v_objFixture.Source, v_objFixture.Template));
        string v_strMessage = WebUtility.HtmlDecode(v_objException.Message);

        Assert.Contains("cột B", v_strMessage);
        Assert.Contains("Tên Chủ Hàng", v_strMessage);
        Assert.Contains("Ghi Chú", v_strMessage);
    }

    [Fact]
    public void Validate_Header_Matches_rejects_missing_and_additional_headers()
    {
        using var v_objMissing = new WorkbookFixture(
            new[] { new[] { "Mã Chủ Hàng" } },
            new[] { new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" } });
        using var v_objAdditional = new WorkbookFixture(
            new[] { new[] { "Mã Chủ Hàng", "Tên Chủ Hàng", "Ghi Chú" } },
            new[] { new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" } });

        Assert.Throws<InvalidDataException>(() =>
            CWMS_Excel_Utility.Validate_Header_Matches(v_objMissing.Source, v_objMissing.Template));
        Assert.Throws<InvalidDataException>(() =>
            CWMS_Excel_Utility.Validate_Header_Matches(v_objAdditional.Source, v_objAdditional.Template));
    }

    [Fact]
    public void Validate_Header_Matches_rejects_headers_moved_below_the_expected_row()
    {
        using var v_objFixture = new WorkbookFixture(
            new[] { new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" } },
            new[] { new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" } },
            sourceHeaderRow: 2);

        Assert.Throws<InvalidDataException>(() =>
            CWMS_Excel_Utility.Validate_Header_Matches(v_objFixture.Source, v_objFixture.Template));
    }

    [Fact]
    public void Validate_Header_Matches_rejects_wrong_worksheet_name_order_and_count()
    {
        using var v_objWrongName = new WorkbookFixture(
            new[] { new[] { "Số CT" } },
            new[] { new[] { "Số CT" } },
            sourceSheetNames: new[] { "Phiếu xuất" },
            templateSheetNames: new[] { "Phiếu nhập" });
        using var v_objWrongOrder = new WorkbookFixture(
            new[] { new[] { "Header" }, new[] { "Detail" } },
            new[] { new[] { "Header" }, new[] { "Detail" } },
            sourceSheetNames: new[] { "Chi tiết nhập", "Phiếu nhập" },
            templateSheetNames: new[] { "Phiếu nhập", "Chi tiết nhập" });
        using var v_objWrongCount = new WorkbookFixture(
            new[] { new[] { "Header" }, new[] { "Detail" } },
            new[] { new[] { "Header" } });

        Assert.Throws<InvalidDataException>(() =>
            CWMS_Excel_Utility.Validate_Header_Matches(v_objWrongName.Source, v_objWrongName.Template));
        Assert.Throws<InvalidDataException>(() =>
            CWMS_Excel_Utility.Validate_Header_Matches(v_objWrongOrder.Source, v_objWrongOrder.Template));
        Assert.Throws<InvalidDataException>(() =>
            CWMS_Excel_Utility.Validate_Header_Matches(v_objWrongCount.Source, v_objWrongCount.Template));
    }

    [Fact]
    public void Validate_Header_Matches_ignores_formatted_but_empty_columns()
    {
        using var v_objFixture = new WorkbookFixture(
            new[] { new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" } },
            new[] { new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" } },
            sourceFormattingOnlyColumn: 12);

        CWMS_Excel_Utility.Validate_Header_Matches(v_objFixture.Source, v_objFixture.Template);
    }

    [Fact]
    public void Validate_Import_File_rejects_an_empty_template_without_importing_rows()
    {
        using var v_objFixture = new WorkbookFixture(
            new[] { new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" } },
            new[] { new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" } },
            sourceFormattingOnlyColumn: 12);

        var v_objException = Assert.Throws<InvalidDataException>(() =>
            CWMS_Excel_Utility.Validate_Import_File(v_objFixture.Source, v_objFixture.Template));

        Assert.Contains("không có dòng dữ liệu", v_objException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Import_File_accepts_data_after_the_header()
    {
        using var v_objFixture = new WorkbookFixture(
            new[] { new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" } },
            new[] { new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" } },
            sourceHasData: true);

        CWMS_Excel_Utility.Validate_Import_File(v_objFixture.Source, v_objFixture.Template);
    }

    [Fact]
    public void Existing_import_templates_match_their_own_structure()
    {
        string[] v_arrTemplateNames =
        {
            "Thanh_Vien_Template.xlsx",
            "Chu_Hang_Template.xlsx",
            "Kho_Import_Template.xlsx",
            "Don_Vi_Tinh_Import_Template.xlsx",
            "Loai_San_Pham_Import_Template.xlsx",
            "San_Pham_Import_Template.xlsx",
            "Nha_Cung_Cap_Import_Template.xlsx",
            "Nhap_Kho_Import_Template.xlsx",
            "Xuat_Kho_Import_Template.xlsx",
            "Dinh_Nghia_Mau_Column_Template.xlsx",
            "MultiLanguage_Template.xlsx",
            "Drill_Down_Template.xlsx",
            "Hien_An_Column_Template.xlsx",
            "Dieu_Chinh_Do_Rong_Cot_Template.xlsx",
            "Nhom_PDA_Template.xlsx",
            "Cau_Hinh_Grid_Field_Template.xlsx",
            "Cau_Hinh_Grid_UI_Global_Template.xlsx"
        };

        foreach (string v_strTemplateName in v_arrTemplateNames)
        {
            var v_objTemplate = new FileInfo(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Web", "wwwroot", "FileManagement", "Template", v_strTemplateName));

            Assert.True(v_objTemplate.Exists, $"Template missing: {v_strTemplateName}");
            CWMS_Excel_Utility.Validate_Header_Matches(v_objTemplate, v_objTemplate);
        }
    }

    [Fact]
    public void Member_template_matches_the_current_seven_column_import_mapping()
    {
        var v_strTemplatePath = FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web", "wwwroot", "FileManagement", "Template", "Thanh_Vien_Template.xlsx");
        using var v_objWorkbook = new XLWorkbook(v_strTemplatePath);

        Assert.Single(v_objWorkbook.Worksheets);
        Assert.Equal(
            new[] { "Ma_Dang_Nhap", "Mat_Khau", "Ho_Ten", "Email", "Dien_Thoai", "Trang_Thai_ID", "Ghi_Chu" },
            v_objWorkbook.Worksheet("Sheet1").Row(1).Cells(1, 7).Select(v_objCell => v_objCell.GetString()));
    }

    [Fact]
    public void Kho_and_product_type_templates_are_rejected_for_owner_import()
    {
        var v_objOwnerTemplate = new FileInfo(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web", "wwwroot", "FileManagement", "Template", "Chu_Hang_Template.xlsx"));
        var v_objWarehouseTemplate = new FileInfo(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web", "wwwroot", "FileManagement", "Template", "Kho_Import_Template.xlsx"));
        var v_objProductTypeTemplate = new FileInfo(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web", "wwwroot", "FileManagement", "Template", "Loai_San_Pham_Import_Template.xlsx"));

        Assert.Throws<InvalidDataException>(() =>
            CWMS_Excel_Utility.Validate_Header_Matches(v_objWarehouseTemplate, v_objOwnerTemplate));
        Assert.Throws<InvalidDataException>(() =>
            CWMS_Excel_Utility.Validate_Header_Matches(v_objProductTypeTemplate, v_objOwnerTemplate));
    }

    [Fact]
    public void Distinct_master_templates_are_rejected_when_cross_imported()
    {
        string[] v_arrTemplateNames =
        {
            "Thanh_Vien_Template.xlsx",
            "Chu_Hang_Template.xlsx",
            "Kho_Import_Template.xlsx",
            "Don_Vi_Tinh_Import_Template.xlsx",
            "Loai_San_Pham_Import_Template.xlsx",
            "San_Pham_Import_Template.xlsx",
            "Nha_Cung_Cap_Import_Template.xlsx"
        };
        string[] v_arrTemplatePaths = Array.ConvertAll(v_arrTemplateNames, v_strTemplateName =>
            FindRepositoryPath("TKS_Thuc_Tap_V11_Web", "wwwroot", "FileManagement", "Template", v_strTemplateName));

        for (int v_iSource = 0; v_iSource < v_arrTemplatePaths.Length; v_iSource++)
        {
            for (int v_iTarget = 0; v_iTarget < v_arrTemplatePaths.Length; v_iTarget++)
            {
                if (v_iSource == v_iTarget)
                    continue;

                Assert.Throws<InvalidDataException>(() => CWMS_Excel_Utility.Validate_Header_Matches(
                    new FileInfo(v_arrTemplatePaths[v_iSource]),
                    new FileInfo(v_arrTemplatePaths[v_iTarget])));
            }
        }
    }

    [Fact]
    public void Receipt_and_issue_templates_are_rejected_when_swapped()
    {
        var v_objReceiptTemplate = new FileInfo(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web", "wwwroot", "FileManagement", "Template", "Nhap_Kho_Import_Template.xlsx"));
        var v_objIssueTemplate = new FileInfo(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web", "wwwroot", "FileManagement", "Template", "Xuat_Kho_Import_Template.xlsx"));

        Assert.Throws<InvalidDataException>(() =>
            CWMS_Excel_Utility.Validate_Header_Matches(v_objIssueTemplate, v_objReceiptTemplate));
        Assert.Throws<InvalidDataException>(() =>
            CWMS_Excel_Utility.Validate_Header_Matches(v_objReceiptTemplate, v_objIssueTemplate));
    }

    private static string FindRepositoryPath(params string[] p_arrSegments)
    {
        DirectoryInfo? v_objDirectory = new(AppContext.BaseDirectory);
        while (v_objDirectory != null)
        {
            string v_strCandidate = v_objDirectory.FullName;
            foreach (string v_strSegment in p_arrSegments)
                v_strCandidate = Path.Combine(v_strCandidate, v_strSegment);

            if (File.Exists(v_strCandidate))
                return v_strCandidate;

            v_objDirectory = v_objDirectory.Parent;
        }

        throw new FileNotFoundException("Could not find a repository file.", Path.Combine(p_arrSegments));
    }

    private sealed class WorkbookFixture : IDisposable
    {
        private readonly string m_strDirectory;

        public FileInfo Source { get; }
        public FileInfo Template { get; }

        public WorkbookFixture(
            string[][] p_arrSourceHeaders,
            string[][] p_arrTemplateHeaders,
            int sourceHeaderRow = 1,
            string[]? sourceSheetNames = null,
            string[]? templateSheetNames = null,
            bool sourceHasData = false,
            int sourceFormattingOnlyColumn = 0)
        {
            m_strDirectory = Path.Combine(Path.GetTempPath(), $"excel-template-validation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(m_strDirectory);
            Source = new FileInfo(Path.Combine(m_strDirectory, "source.xlsx"));
            Template = new FileInfo(Path.Combine(m_strDirectory, "template.xlsx"));
            WriteWorkbook(Source.FullName, p_arrSourceHeaders, sourceHeaderRow, sourceSheetNames, sourceHasData, sourceFormattingOnlyColumn);
            WriteWorkbook(Template.FullName, p_arrTemplateHeaders, 1, templateSheetNames, false, 0);
        }

        public void Dispose() => Directory.Delete(m_strDirectory, recursive: true);

        private static void WriteWorkbook(
            string p_strPath,
            string[][] p_arrHeaders,
            int p_iHeaderRow,
            string[]? p_arrSheetNames,
            bool p_bIncludeData,
            int p_iFormattingOnlyColumn)
        {
            using var v_objWorkbook = new XLWorkbook();
            for (int v_iSheet = 0; v_iSheet < p_arrHeaders.Length; v_iSheet++)
            {
                string v_strSheetName = p_arrSheetNames != null && v_iSheet < p_arrSheetNames.Length
                    ? p_arrSheetNames[v_iSheet]
                    : $"Sheet{v_iSheet + 1}";
                var v_objSheet = v_objWorkbook.AddWorksheet(v_strSheetName);
                for (int v_iColumn = 0; v_iColumn < p_arrHeaders[v_iSheet].Length; v_iColumn++)
                    v_objSheet.Cell(p_iHeaderRow, v_iColumn + 1).Value = p_arrHeaders[v_iSheet][v_iColumn];

                if (p_bIncludeData && v_iSheet == 0)
                    v_objSheet.Cell(p_iHeaderRow + 1, 1).Value = "sample";

                if (p_iFormattingOnlyColumn > 0)
                    v_objSheet.Cell(93, p_iFormattingOnlyColumn).Style.Fill.BackgroundColor = XLColor.LightGray;
            }

            v_objWorkbook.SaveAs(p_strPath);
        }
    }
}
