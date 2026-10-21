using ClosedXML.Excel;
using Microsoft.AspNetCore.Components.Forms;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Common.Common;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class ProductExcelImportUiTests
{
    [Fact]
    public async Task Shared_upload_helper_checks_extension_and_enforces_bounded_stream_read()
    {
        var v_arrContent = new byte[] { 1, 2, 3 };
        var v_objFile = new CTestBrowserFile("products.xlsx", v_arrContent);

        var v_arrResult = await FBase.Read_Import_File_Async(v_objFile, ".xlsx", 3);

        Assert.Equal(v_arrContent, v_arrResult);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            FBase.Read_Import_File_Async(new CTestBrowserFile("products.xls", v_arrContent), ".xlsx", 3));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            FBase.Read_Import_File_Async(v_objFile, ".xlsx", 2));
    }

    [Fact]
    public void Product_route_uses_FBase_import_and_keeps_product_lookup_validation()
    {
        var v_strRoute = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Warehouse_San_Pham.razor"));
        var v_strList = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "F2018_1_San_Pham_List.razor"));

        Assert.Contains("<F2018_1_San_Pham_List />", v_strRoute);
        Assert.Contains("@if (r_objChuc_Nang.Is_Have_Add_Permission)", v_strList);
        Assert.Contains("Open_Import_Excel(\"San_Pham_Import_Template.xlsx\")", v_strList);
        Assert.Contains("FCommon_Import_Excel", v_strList);
        Assert.Contains("CProductImportValidator.Preview", v_strList);
        Assert.Contains("F2017_sp_sel_List_Loai_San_Pham_Lookup", v_strList);
        Assert.Contains("F2016_sp_sel_List_Don_Vi_Tinh_Lookup", v_strList);
        Assert.Contains("protected override void Import_Excel_Entry", v_strList);
        Assert.DoesNotContain("<FWarehouse_Product_Import", v_strList);
    }

    [Fact]
    public async Task Official_template_has_only_the_standard_product_contract_and_text_code_format()
    {
        var v_strPath = FindRepositoryPath("TKS_Thuc_Tap_V11_Web", "wwwroot", "FileManagement", "Template", "San_Pham_Import_Template.xlsx");
        await using var v_objStream = File.OpenRead(v_strPath);
        var v_objWorksheet = await new CProduct_Xlsx_Reader().ReadAsync(v_objStream);

        Assert.Equal("SanPham", v_objWorksheet.Name);
        var v_objHeader = Assert.Single(v_objWorksheet.Rows);
        Assert.Equal(new[] { "Ma_San_Pham", "Ten_San_Pham", "Ma_LSP", "Ten_Don_Vi_Tinh", "Ghi_Chu" },
            v_objHeader.Cells.Select(v_objCell => v_objCell.Value));

        using var v_objWorkbook = new XLWorkbook(v_strPath);
        Assert.Equal("@", v_objWorkbook.Worksheet("SanPham").Cell("A2").Style.NumberFormat.Format);
    }

    [Fact]
    public void Product_import_dialog_disables_confirm_until_a_valid_preview_exists()
    {
        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_Product_Import.razor"));
        var v_strShell = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_Import_Preview_Shell.razor"));

        Assert.Contains("<FWarehouse_Import_Preview_Shell", v_strSource);
        Assert.Contains("PreviewValid", v_strSource);
        Assert.Contains("PreviewInvalid", v_strSource);
        Assert.Contains("Importing", v_strSource);
        Assert.Contains("Can_Confirm", v_strSource);
        Assert.Contains("disabled=\"@(!CanConfirm)\"", v_strShell);
    }

    [Fact]
    public void Product_preview_path_does_not_call_database_write_operations()
    {
        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Services", "CWarehouseProductImportService.cs"));
        var v_iStart = v_strSource.IndexOf("public async Task<CProductImportPreviewResult> PreviewAsync", StringComparison.Ordinal);
        var v_iEnd = v_strSource.IndexOf("public async Task<int> ConfirmAsync", v_iStart, StringComparison.Ordinal);

        Assert.True(v_iStart >= 0 && v_iEnd > v_iStart, "Preview and Confirm methods were not found.");
        var v_strPreviewMethod = v_strSource[v_iStart..v_iEnd];
        Assert.DoesNotContain("Import_Products_Async", v_strPreviewMethod);
        Assert.DoesNotContain("Save_Master_Async", v_strPreviewMethod);
        Assert.DoesNotContain("Execute_Procedure", v_strPreviewMethod);
    }

    [Fact]
    public void Product_batch_shares_one_transaction_aware_connection_across_all_inserts()
    {
        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Services", "CWarehouseProductImportService.cs"));
        var v_iStart = v_strSource.IndexOf("internal async Task Import_Products_Async(", StringComparison.Ordinal);
        var v_iEnd = v_strSource.IndexOf("private void Demand_Add_Permission(", v_iStart, StringComparison.Ordinal);

        Assert.True(v_iStart >= 0 && v_iEnd > v_iStart, "Product import batch boundaries were not found.");
        var v_strBatchMethod = v_strSource[v_iStart..v_iEnd];
        Assert.Equal(1, v_strBatchMethod.Split("new SqlConnection(", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, v_strBatchMethod.Split("BeginTransaction(", StringSplitOptions.None).Length - 1);
        Assert.Contains("v_objProductController.F2018_sp_ins_San_Pham(v_objConnection, v_objTransaction, v_objProduct", v_strBatchMethod);
        Assert.Contains("v_objTransaction.Rollback();", v_strBatchMethod);
        var v_strProductController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "DM", "CDM_San_Pham_Controller.cs"));
        Assert.Contains("CSqlHelper.ExecuteScalar(p_conn, p_trans, CConfig.TKS_Thuc_Tap_V11_Conn_String, \"F2018_sp_ins_San_Pham\"", v_strProductController);
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
