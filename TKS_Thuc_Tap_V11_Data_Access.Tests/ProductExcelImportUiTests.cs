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
    public void Product_import_action_is_scoped_to_product_add_permission_and_refreshes_once_after_success()
    {
        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("m_strMaster_Type != \"SanPham\" || !r_objChuc_Nang.Is_Have_Add_Permission", v_strSource);
        Assert.Contains("On_Product_Import_Completed", v_strSource);
        Assert.Contains("m_grdMaster.Rebind();", v_strSource);
        Assert.Contains("\"Product Excel Import\"", v_strSource);
        Assert.Contains("Rows: {p_iRowCount}", v_strSource);
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

        Assert.Contains("PreviewValid", v_strSource);
        Assert.Contains("PreviewInvalid", v_strSource);
        Assert.Contains("Importing", v_strSource);
        Assert.Contains("disabled=\"@(!Can_Confirm)\"", v_strSource);
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
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "DM", "CWarehouseMaster_Controller.cs"));
        var v_iStart = v_strSource.IndexOf("public async Task Import_Products_Async(", StringComparison.Ordinal);
        var v_iEnd = v_strSource.IndexOf("private Task Save_Master_Async(", v_iStart, StringComparison.Ordinal);

        Assert.True(v_iStart >= 0 && v_iEnd > v_iStart, "Import batch and transaction-aware save methods were not found.");
        var v_strBatchMethod = v_strSource[v_iStart..v_iEnd];
        Assert.Equal(1, v_strBatchMethod.Split("new SqlConnection(", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, v_strBatchMethod.Split("BeginTransaction(", StringSplitOptions.None).Length - 1);
        Assert.Contains("v_objConnection, v_objTransaction);", v_strBatchMethod);
        Assert.Contains("v_objTransaction.Rollback();", v_strBatchMethod);
        Assert.Contains("F2018_sp_ins_San_Pham", v_strSource);
        Assert.Contains("Scalar_ID(p_objConnection, p_objTransaction, v_objCommand.Procedure", v_strSource);
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
