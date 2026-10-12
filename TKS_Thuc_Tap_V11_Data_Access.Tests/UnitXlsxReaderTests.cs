using ClosedXML.Excel;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class UnitXlsxReaderTests
{
    [Fact]
    public async Task Reader_preserves_unicode_values_and_formula_metadata()
    {
        var v_arrWorkbook = CreateWorkbook(("Hộp", "=1+1"));
        await using var v_objStream = new MemoryStream(v_arrWorkbook);

        var v_objSheet = await new CUnit_Xlsx_Reader().ReadAsync(v_objStream);

        Assert.Equal("DonViTinh", v_objSheet.Name);
        Assert.Equal("Hộp", v_objSheet.Rows[1].Cells[0].Value);
        Assert.Equal("Formula", v_objSheet.Rows[1].Cells[1].CellType);
        Assert.Equal("1+1", v_objSheet.Rows[1].Cells[1].Formula);
        Assert.Null(v_objSheet.Rows[1].Cells[1].Value);
    }

    [Fact]
    public async Task Reader_accepts_renamed_sheet_and_rejects_unmapped_data_sheet()
    {
        var v_objRenamed = await new CUnit_Xlsx_Reader()
            .ReadAsync(new MemoryStream(CreateWorkbook(("Hộp", ""), sheetName: "Units")));
        Assert.Equal("DonViTinh", v_objRenamed.Name);
        Assert.Equal("Hộp", v_objRenamed.Rows[1].Cells[0].Value);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CUnit_Xlsx_Reader().ReadAsync(new MemoryStream(CreateWorkbook(includeExtraSheet: true))));
    }

    [Fact]
    public async Task Reader_rejects_malformed_oversized_and_overwide_workbooks()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CUnit_Xlsx_Reader().ReadAsync(new MemoryStream(new byte[] { 1, 2, 3 })));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CUnit_Xlsx_Reader().ReadAsync(new MemoryStream(new byte[1_048_577])));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CUnit_Xlsx_Reader().ReadAsync(new MemoryStream(CreateWorkbook(tooManyColumns: true))));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CUnit_Xlsx_Reader().ReadAsync(new MemoryStream(CreateWorkbook(tooManyRows: true))));
    }

    private static byte[] CreateWorkbook(
        (string Name, string Note)? p_objRow = null,
        string sheetName = "DonViTinh",
        bool includeExtraSheet = false,
        bool tooManyColumns = false,
        bool tooManyRows = false)
    {
        using var v_objWorkbook = new XLWorkbook();
        var v_objWorksheet = v_objWorkbook.Worksheets.Add(sheetName);
        v_objWorksheet.Cell(1, 1).Value = "Ten_Don_Vi_Tinh";
        v_objWorksheet.Cell(1, 2).Value = "Ghi_Chu";
        if (p_objRow is { } v_objValues)
        {
            v_objWorksheet.Cell(2, 1).Value = v_objValues.Name;
            if (v_objValues.Note.StartsWith("=", StringComparison.Ordinal))
                v_objWorksheet.Cell(2, 2).FormulaA1 = v_objValues.Note[1..];
            else
                v_objWorksheet.Cell(2, 2).Value = v_objValues.Note;
        }
        if (includeExtraSheet)
        {
            var v_objExtra = v_objWorkbook.Worksheets.Add("Extra");
            v_objExtra.Cell(1, 1).Value = "Unmapped data";
            v_objExtra.Cell(2, 1).Value = "Value";
        }
        if (tooManyColumns)
            v_objWorksheet.Cell(1, 101).Value = "Unexpected";
        if (tooManyRows)
            v_objWorksheet.Cell(102, 1).Value = "Too many";

        using var v_objStream = new MemoryStream();
        v_objWorkbook.SaveAs(v_objStream);
        return v_objStream.ToArray();
    }
}
