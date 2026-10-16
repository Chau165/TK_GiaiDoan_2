using ClosedXML.Excel;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseMasterExcelExportTests
{
    [Fact]
    public void Master_workbook_has_one_data_sheet_and_exports_visible_fields()
    {
        var v_arrMasters = new[]
        {
            new CWarehouseMaster { Auto_ID = 1, Name = "Kho Bình Tân", Ghi_Chu = "Kho chính" },
            new CWarehouseMaster { Auto_ID = 2, Name = "Kho Cát Lái", Ghi_Chu = "Gần cảng" }
        };
        var v_arrColumns = new[]
        {
            new CWarehouseMasterExcelColumn(nameof(CWarehouseMaster.Name), "Tên kho", "240px"),
            new CWarehouseMasterExcelColumn(nameof(CWarehouseMaster.Ghi_Chu), "Ghi chú", "180px")
        };

        using var v_objStream = new MemoryStream(
            CWarehouseMasterExcelExporter.CreateWorkbook(v_arrMasters, v_arrColumns, "Kho"));
        using var v_objWorkbook = new XLWorkbook(v_objStream);
        var v_objSheet = v_objWorkbook.Worksheet("Kho");

        Assert.Single(v_objWorkbook.Worksheets);
        Assert.Equal("Tên kho", v_objSheet.Cell(1, 1).GetString());
        Assert.Equal("Ghi chú", v_objSheet.Cell(1, 2).GetString());
        Assert.Equal("Kho Bình Tân", v_objSheet.Cell(2, 1).GetString());
        Assert.Equal("Gần cảng", v_objSheet.Cell(3, 2).GetString());
        Assert.Equal(3, v_objSheet.LastRowUsed()!.RowNumber());
        Assert.True(v_objSheet.Column(1).Width >= 30);
    }

    [Fact]
    public void Master_workbook_with_no_visible_rows_still_contains_only_the_header_sheet()
    {
        var v_arrColumns = new[]
        {
            new CWarehouseMasterExcelColumn(nameof(CWarehouseMaster.Code), "Mã", "140px")
        };

        using var v_objStream = new MemoryStream(
            CWarehouseMasterExcelExporter.CreateWorkbook([], v_arrColumns, "SanPham"));
        using var v_objWorkbook = new XLWorkbook(v_objStream);
        var v_objSheet = v_objWorkbook.Worksheet("SanPham");

        Assert.Single(v_objWorkbook.Worksheets);
        Assert.Equal(1, v_objSheet.LastRowUsed()!.RowNumber());
        Assert.Equal("Mã", v_objSheet.Cell(1, 1).GetString());
    }
}
