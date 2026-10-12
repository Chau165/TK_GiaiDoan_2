using OfficeOpenXml;
using Telerik.Documents.SpreadsheetStreaming;
using Xunit;
using Xunit.Abstractions;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class TelerikSpreadImporterRuntimeTests
{
    private readonly ITestOutputHelper m_objOutput;

    public TelerikSpreadImporterRuntimeTests(ITestOutputHelper p_objOutput)
    {
        m_objOutput = p_objOutput;
    }

    [Fact]
    public void SpreadImporter_reads_xlsx_workbook_cells_unicode_and_formula_metadata()
    {
        var v_strPath = Path.Combine(Path.GetTempPath(), $"product-import-phase0-{Guid.NewGuid():N}.xlsx");

        try
        {
            using (var v_objFileStream = File.Create(v_strPath))
            using (var v_objExporter = SpreadExporter.CreateWorkbookExporter(SpreadDocumentFormat.Xlsx, v_objFileStream))
            using (var v_objWorksheetExporter = v_objExporter.CreateWorksheetExporter("SanPham"))
            {
                var v_arrHeaders = new[] { "Ma_San_Pham", "Ten_San_Pham", "Ma_LSP", "Ten_Don_Vi_Tinh", "Ghi_Chu" };
                void WriteRow(params string[] p_arrValues)
                {
                    using var v_objRow = v_objWorksheetExporter.CreateRowExporter();
                    for (var v_i = 0; v_i < p_arrValues.Length; v_i++)
                    {
                        using var v_objCell = v_objRow.CreateCellExporter();
                        if (p_arrValues[v_i] == "00123")
                            v_objCell.SetFormat(new SpreadCellFormat { NumberFormat = "@" });
                        if (p_arrValues[v_i] == "=1+1")
                            v_objCell.SetFormula("1+1");
                        else
                            v_objCell.SetValue(p_arrValues[v_i]);
                    }
                }

                WriteRow(v_arrHeaders);
                WriteRow("SP001", "Coca Cola", "NUOC", "Lon");
                WriteRow("SP002", "Sữa hộp", "SUA", "Hộp", "Hàng lạnh; Đơn vị tính Hộp");
                using (v_objWorksheetExporter.CreateRowExporter()) { }
                WriteRow("00123", "Sản phẩm", "NUOC", "Hộp");
                WriteRow("SP004", "Formula sample", "NUOC", "Lon", "=1+1");
            }

            Assert.True(new FileInfo(v_strPath).Length > 0);
            using var v_objStream = File.OpenRead(v_strPath);
            using var v_objWorkbook = SpreadImporter.CreateWorkbookImporter(SpreadDocumentFormat.Xlsx, v_objStream);
            var v_objAssembly = typeof(SpreadImporter).Assembly;
            Assert.Equal("Telerik.Documents.SpreadsheetStreaming.dll", Path.GetFileName(v_objAssembly.Location));
            m_objOutput.WriteLine($"Parser assembly: {v_objAssembly.GetName().Name}, {v_objAssembly.GetName().Version}");

            using var v_objWorksheet = v_objWorkbook.WorksheetImporters.Single();
            Assert.Equal("SanPham", v_objWorksheet.Name);

            var v_dicRows = new Dictionary<int, Dictionary<int, ImportedCell>>();
            foreach (var v_objRow in v_objWorksheet.Rows)
            {
                try
                {
                    v_dicRows.Add(v_objRow.RowIndex, v_objRow.Cells.ToDictionary(
                        v_objCell => v_objCell.ColumnIndex,
                        v_objCell => new ImportedCell(
                            v_objCell.Value,
                            v_objCell.FormulaValue,
                            v_objCell.ValueType,
                            v_objRow.RowIndex == 4 && v_objCell.ColumnIndex == 0 ? v_objCell.Format.NumberFormat : string.Empty)));
                }
                catch (Exception v_objException)
                {
                    m_objOutput.WriteLine($"Cell import failed at worksheet row {v_objRow.RowIndex + 1}: {v_objException.GetType().Name}");
                    throw;
                }
            }

            var v_arrExpectedHeaders = new[] { "Ma_San_Pham", "Ten_San_Pham", "Ma_LSP", "Ten_Don_Vi_Tinh", "Ghi_Chu" };
            for (var v_i = 0; v_i < v_arrExpectedHeaders.Length; v_i++)
                Assert.Equal(v_arrExpectedHeaders[v_i], v_dicRows[0][v_i].Value);

            Assert.Equal("SP001", v_dicRows[1][0].Value);
            Assert.Equal("Coca Cola", v_dicRows[1][1].Value);
            Assert.False(v_dicRows[1].ContainsKey(4));
            Assert.Equal("SP002", v_dicRows[2][0].Value);
            Assert.Equal("Sữa hộp", v_dicRows[2][1].Value);
            Assert.Equal("Hộp", v_dicRows[2][3].Value);
            Assert.Equal("Hàng lạnh; Đơn vị tính Hộp", v_dicRows[2][4].Value);

            var v_bHasEmptyRow = v_dicRows.TryGetValue(3, out var v_dicEmptyRow);
            Assert.True(v_bHasEmptyRow);
            Assert.Empty(v_dicEmptyRow!);

            var v_objTextCode = v_dicRows[4][0];
            Assert.Equal("00123", v_objTextCode.Value);
            Assert.Equal("Sản phẩm", v_dicRows[4][1].Value);
            Assert.Equal("Hộp", v_dicRows[4][3].Value);
            Assert.Equal("@", v_objTextCode.NumberFormat);
            Assert.DoesNotContain("Number", v_objTextCode.ValueType.ToString(), StringComparison.OrdinalIgnoreCase);

            var v_objFormula = v_dicRows[5][4];
            m_objOutput.WriteLine($"Formula value={v_objFormula.Value}; formula={v_objFormula.Formula}; type={v_objFormula.ValueType}");
            Assert.Equal(CellValueType.Number, v_objFormula.ValueType);
            Assert.Null(v_objFormula.Value);
            Assert.Equal("1+1", v_objFormula.Formula);
            m_objOutput.WriteLine($"Empty row surfaced: {v_bHasEmptyRow}");
        }
        finally
        {
            if (File.Exists(v_strPath))
                File.Delete(v_strPath);
        }
    }

    [Fact]
    public void SpreadImporter_reproduces_epplus_style_metadata_failure()
    {
        var v_strPath = Path.Combine(Path.GetTempPath(), $"product-import-epplus-{Guid.NewGuid():N}.xlsx");

        try
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using (var v_objPackage = new ExcelPackage())
            {
                var v_objSheet = v_objPackage.Workbook.Worksheets.Add("SanPham");
                v_objSheet.Cells[1, 1].Value = "Ma_San_Pham";
                v_objSheet.Cells[2, 1].Value = "SP001";
                v_objPackage.SaveAs(new FileInfo(v_strPath));
            }

            using var v_objStream = File.OpenRead(v_strPath);
            using var v_objWorkbook = SpreadImporter.CreateWorkbookImporter(SpreadDocumentFormat.Xlsx, v_objStream);
            using var v_objWorksheet = v_objWorkbook.WorksheetImporters.Single();
            var v_objException = Assert.Throws<InvalidOperationException>(
                () => v_objWorksheet.Rows.SelectMany(v_objRow => v_objRow.Cells).ToArray());

            Assert.Equal("Nullable object must have a value.", v_objException.Message);
        }
        finally
        {
            if (File.Exists(v_strPath))
                File.Delete(v_strPath);
        }
    }

    private sealed record ImportedCell(string Value, string Formula, CellValueType ValueType, string NumberFormat);
}
