using ClosedXML.Excel;
using Microsoft.AspNetCore.Components.Forms;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Common.Common;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class ProductExcelImportUiTests
{

    [Fact]
    public void Product_route_uses_FBase_import_and_keeps_product_lookup_validation()
    {
        var v_strRoute = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Warehouse_San_Pham.razor"));
        var v_strList = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "F2018_1_San_Pham_List.razor"));

        Assert.Contains("<F2018_1_San_Pham_List />", v_strRoute);
        Assert.Contains("Is_Have_Add_Permission", v_strList);
        Assert.Contains("@Layout_Default_Filter(grdData, \"San_Pham_Import_Template.xlsx\")", v_strList);
        Assert.Contains("FCommon_Import_Excel", v_strList);
        Assert.Contains("v_arrCategoryMatches", v_strList);
        Assert.Contains("v_arrUnitMatches", v_strList);
        Assert.Contains("F2017_sp_sel_List_Loai_San_Pham_Lookup", v_strList);
        Assert.Contains("F2016_sp_sel_List_Don_Vi_Tinh_Lookup", v_strList);
        Assert.Contains("protected override void Import_Excel_Entry", v_strList);
        Assert.DoesNotContain("<FWarehouse_Product_Import", v_strList);
    }

    [Fact]
    public void Official_template_has_only_the_standard_product_contract_and_text_code_format()
    {
        var v_strPath = FindRepositoryPath("TKS_Thuc_Tap_V11_Web", "wwwroot", "FileManagement", "Template", "San_Pham_Import_Template.xlsx");
        using var v_objWorkbook = new XLWorkbook(v_strPath);
        Assert.Equal("@", v_objWorkbook.Worksheet("SanPham").Cell("A2").Style.NumberFormat.Format);
    }




    private static string FindRepositoryPath(params string[] p_arrParts)
    {
        for (var v_objDirectory = new DirectoryInfo(AppContext.BaseDirectory); v_objDirectory is not null; v_objDirectory = v_objDirectory.Parent)
        {
            var v_strCandidate = Path.Combine(new[] { v_objDirectory.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_strCandidate))
                return v_strCandidate;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(p_arrParts)}");
    }

    private sealed class CTestBrowserFile : IBrowserFile
    {
        private readonly byte[] m_arrContent;

        public CTestBrowserFile(string p_strName, byte[] p_arrContent)
        {
            Name = p_strName;
            m_arrContent = p_arrContent;
        }

        public string Name { get; }
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => m_arrContent.LongLength;
        public string ContentType => "application/octet-stream";

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            if (Size > maxAllowedSize)
                throw new IOException("File exceeds the configured upload limit.");

            return new MemoryStream(m_arrContent, writable: false);
        }
    }
}
