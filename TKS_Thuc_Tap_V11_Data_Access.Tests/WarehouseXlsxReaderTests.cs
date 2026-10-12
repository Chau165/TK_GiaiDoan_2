using ClosedXML.Excel;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseXlsxReaderTests
{
    [Fact]
    public async Task Reader_preserves_warehouse_name_and_formula_metadata()
    {
        await using var v_objStream = new MemoryStream(CreateWorkbook("Kho A", "=1+1"), writable: false);

        var v_objSheet = await new CWarehouse_Xlsx_Reader().ReadAsync(v_objStream);

        Assert.Equal("Kho", v_objSheet.Name);
        Assert.Equal("Kho A", v_objSheet.Rows[1].Cells[0].Value);
        Assert.Equal("Formula", v_objSheet.Rows[1].Cells[1].CellType);
        Assert.Equal("1+1", v_objSheet.Rows[1].Cells[1].Formula);
    }

    [Fact]
    public async Task Reader_accepts_renamed_sheet_and_rejects_unmapped_data_sheet_and_limits()
    {
        var v_objRenamed = await new CWarehouse_Xlsx_Reader()
            .ReadAsync(new MemoryStream(CreateWorkbook("Kho A", sheetName: "Warehouse")));
        Assert.Equal("Kho", v_objRenamed.Name);
        Assert.Equal("Kho A", v_objRenamed.Rows[1].Cells[0].Value);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CWarehouse_Xlsx_Reader().ReadAsync(new MemoryStream(CreateWorkbook(includeExtraSheet: true))));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CWarehouse_Xlsx_Reader().ReadAsync(new MemoryStream(CreateWorkbook(tooManyColumns: true))));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CWarehouse_Xlsx_Reader().ReadAsync(new MemoryStream(CreateWorkbook(tooManyRows: true))));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CWarehouse_Xlsx_Reader().ReadAsync(new MemoryStream(new byte[1_048_577])));
    }

    private static byte[] CreateWorkbook(
        string? p_strName = null,
        string? p_strFormula = null,
        string sheetName = "Kho",
        bool includeExtraSheet = false,
        bool tooManyColumns = false,
        bool tooManyRows = false)
    {
        using var v_objWorkbook = new XLWorkbook();
        var v_objSheet = v_objWorkbook.Worksheets.Add(sheetName);
        v_objSheet.Cell(1, 1).Value = "Ten_Kho";
        v_objSheet.Cell(1, 2).Value = "Ghi_Chu";
        if (p_strName is not null)
        {
            v_objSheet.Cell(2, 1).Value = p_strName;
            if (p_strFormula is not null)
                v_objSheet.Cell(2, 2).FormulaA1 = p_strFormula[1..];
        }
        if (includeExtraSheet)
        {
            var v_objExtra = v_objWorkbook.Worksheets.Add("Other");
            v_objExtra.Cell(1, 1).Value = "Unmapped data";
            v_objExtra.Cell(2, 1).Value = "Value";
        }
        if (tooManyColumns)
            v_objSheet.Cell(1, 101).Value = "Unexpected";
        if (tooManyRows)
            v_objSheet.Cell(102, 1).Value = "Too many";
        using var v_objStream = new MemoryStream();
        v_objWorkbook.SaveAs(v_objStream);
        return v_objStream.ToArray();
    }
}
