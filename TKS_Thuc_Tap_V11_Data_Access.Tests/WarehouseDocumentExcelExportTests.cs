using ClosedXML.Excel;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseDocumentExcelExportTests
{
    [Fact]
    public void Receipt_workbook_contains_one_detail_row_per_product_with_readable_datetime()
    {
        var v_objDocument = new CWarehouseDocument
        {
            Auto_ID = 12,
            Is_Receipt = true,
            So_Phieu = "PN-001",
            Ten_Kho = "Kho Bình Tân",
            Ten_NCC = "Nhà cung cấp A",
            Ngay_Chung_Tu = new DateTime(2026, 10, 16, 9, 5, 0),
            Is_Posted = true,
            Ghi_Chu = "Nhập hàng"
        };
        var v_arrDetails = new[]
        {
            new CWarehouseDocumentDetail { Document_ID = 12, Ma_San_Pham = "SP-001", Ten_San_Pham = "Nước suối", Ten_Don_Vi_Tinh = "Thùng", So_Luong = 3, Don_Gia = 12000 },
            new CWarehouseDocumentDetail { Document_ID = 12, Ma_San_Pham = "SP-002", Ten_San_Pham = "Nước ngọt", Ten_Don_Vi_Tinh = "Lon", So_Luong = 5, Don_Gia = 8000 }
        };
        var v_arrRows = CWarehouseDocumentExcelExporter.BuildRows(new[] { v_objDocument }, _ => v_arrDetails);
        using var v_objStream = new MemoryStream(CWarehouseDocumentExcelExporter.CreateWorkbook(v_arrRows, isReceipt: true));
        using var v_objWorkbook = new XLWorkbook(v_objStream);
        var v_objSheet = v_objWorkbook.Worksheet("PhieuNhap");

        Assert.Single(v_objWorkbook.Worksheets);
        Assert.Equal(3, v_objSheet.LastRowUsed()!.RowNumber());
        Assert.Equal("SL nhập", v_objSheet.Cell(1, 10).GetString());
        Assert.Equal("PN-001", v_objSheet.Cell(2, 2).GetString());
        Assert.Equal("SP-001", v_objSheet.Cell(2, 7).GetString());
        Assert.Equal("SP-002", v_objSheet.Cell(3, 7).GetString());
        Assert.Equal(3m, v_objSheet.Cell(2, 10).GetValue<decimal>());
        Assert.Equal(12000m, v_objSheet.Cell(2, 11).GetValue<decimal>());
        Assert.Equal(36000m, v_objSheet.Cell(2, 12).GetValue<decimal>());
        Assert.Equal(v_objDocument.Ngay_Chung_Tu, v_objSheet.Cell(2, 1).GetDateTime());
        Assert.Contains("HH:mm", v_objSheet.Cell(2, 1).Style.DateFormat.Format, StringComparison.OrdinalIgnoreCase);
        Assert.True(v_objSheet.Column(1).Width >= 20);
    }

    [Fact]
    public void Issue_workbook_labels_issue_quantity_and_keeps_documents_without_details()
    {
        var v_objDocument = new CWarehouseDocument
        {
            Auto_ID = 20,
            Is_Receipt = false,
            So_Phieu = "PX-001",
            Ten_Kho = "Kho Dĩ An",
            Ngay_Chung_Tu = new DateTime(2026, 10, 15, 14, 30, 0),
            Is_Posted = false
        };
        var v_arrRows = CWarehouseDocumentExcelExporter.BuildRows(
            new[] { v_objDocument },
            _ => Array.Empty<CWarehouseDocumentDetail>());
        using var v_objStream = new MemoryStream(CWarehouseDocumentExcelExporter.CreateWorkbook(v_arrRows, isReceipt: false));
        using var v_objWorkbook = new XLWorkbook(v_objStream);
        var v_objSheet = v_objWorkbook.Worksheet("PhieuXuat");

        Assert.Single(v_arrRows);
        Assert.Equal(2, v_objSheet.LastRowUsed()!.RowNumber());
        Assert.Equal("SL xuất", v_objSheet.Cell(1, 9).GetString());
        Assert.Equal("Nháp", v_objSheet.Cell(2, 4).GetString());
        Assert.Equal("PX-001", v_objSheet.Cell(2, 2).GetString());
        Assert.True(v_objSheet.Cell(2, 9).IsEmpty());
        Assert.True(v_objSheet.Cell(2, 10).IsEmpty());
        Assert.True(v_objSheet.Cell(2, 11).IsEmpty());
    }
}
