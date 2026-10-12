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
            new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" },
            new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" });

        CExcel_Controller.Validate_Header_Matches(v_objFixture.Source, v_objFixture.Template);
    }

    [Fact]
    public void Validate_Header_Matches_rejects_reordered_or_shifted_headers()
    {
        using var v_objFixture = new WorkbookFixture(
            new[] { "Tên Chủ Hàng", "Mã Chủ Hàng" },
            new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" });

        Assert.Throws<InvalidDataException>(() =>
            CExcel_Controller.Validate_Header_Matches(v_objFixture.Source, v_objFixture.Template));
    }

    [Fact]
    public void Validate_Header_Matches_rejects_headers_moved_below_the_expected_row()
    {
        using var v_objFixture = new WorkbookFixture(
            new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" },
            new[] { "Mã Chủ Hàng", "Tên Chủ Hàng" },
            sourceHeaderRow: 2);

        Assert.Throws<InvalidDataException>(() =>
            CExcel_Controller.Validate_Header_Matches(v_objFixture.Source, v_objFixture.Template));
    }

    private sealed class WorkbookFixture : IDisposable
    {
        private readonly string m_strDirectory;

        public FileInfo Source { get; }
        public FileInfo Template { get; }

        public WorkbookFixture(string[] p_arrSourceHeaders, string[] p_arrTemplateHeaders, int sourceHeaderRow = 1)
        {
            m_strDirectory = Path.Combine(Path.GetTempPath(), $"legacy-excel-header-{Guid.NewGuid():N}");
            Directory.CreateDirectory(m_strDirectory);
            Source = new FileInfo(Path.Combine(m_strDirectory, "source.xlsx"));
            Template = new FileInfo(Path.Combine(m_strDirectory, "template.xlsx"));
            WriteWorkbook(Source.FullName, p_arrSourceHeaders, sourceHeaderRow);
            WriteWorkbook(Template.FullName, p_arrTemplateHeaders, 1);
        }

        public void Dispose() => Directory.Delete(m_strDirectory, recursive: true);

        private static void WriteWorkbook(string p_strPath, string[] p_arrHeaders, int p_iHeaderRow)
        {
            using var v_objWorkbook = new XLWorkbook();
            var v_objSheet = v_objWorkbook.AddWorksheet("Sheet1");
            for (var v_i = 0; v_i < p_arrHeaders.Length; v_i++)
                v_objSheet.Cell(p_iHeaderRow, v_i + 1).Value = p_arrHeaders[v_i];
            v_objWorkbook.SaveAs(p_strPath);
        }
    }
}
