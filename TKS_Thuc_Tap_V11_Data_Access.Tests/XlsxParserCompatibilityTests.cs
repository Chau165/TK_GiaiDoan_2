using System.Diagnostics;
using System.Globalization;
using ClosedXML.Excel;
using ExcelDataReader;
using OfficeOpenXml;
using Telerik.Documents.SpreadsheetStreaming;
using Xunit;
using Xunit.Abstractions;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class XlsxParserCompatibilityTests
{
    private static readonly string[] m_arrHeaders =
    {
        "Ma_San_Pham", "Ten_San_Pham", "Ma_LSP", "Ten_Don_Vi_Tinh", "Ghi_Chu"
    };

    private readonly ITestOutputHelper m_objOutput;

    public XlsxParserCompatibilityTests(ITestOutputHelper p_objOutput)
    {
        m_objOutput = p_objOutput;
    }

    [Fact]
    public void Parser_matrix_records_results_for_the_same_core_contract()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        var v_arrProducers = new[]
        {
            (Name: "Telerik", Bytes: CreateTelerikWorkbook()),
            (Name: "EPPlus-7.0.5", Bytes: CreateEpplusWorkbook()),
            (Name: "Microsoft-Excel-16.0", Bytes: File.ReadAllBytes(GetMicrosoftExcelFixturePath()))
        };
        var v_arrParsers = new (string Name, Func<byte[], ParsedSheet> Read)[]
        {
            ("Telerik-SpreadImporter", ReadWithTelerik),
            ("ExcelDataReader-3.9.0", ReadWithExcelDataReader),
            ("ClosedXML-0.105.1", ReadWithClosedXml)
        };

        foreach (var v_objProducer in v_arrProducers)
        foreach (var v_objParser in v_arrParsers)
        {
            ParsedSheet? v_objSheet = null;
            var v_objException = Record.Exception(() =>
            {
                v_objSheet = v_objParser.Read(v_objProducer.Bytes);
            });
            var v_bExpectedFailure = v_objProducer.Name == "EPPlus-7.0.5" && v_objParser.Name == "Telerik-SpreadImporter";
            Assert.Equal(v_bExpectedFailure, v_objException is not null);
            if (v_bExpectedFailure)
            {
                var v_objExpectedException = Assert.IsType<InvalidOperationException>(v_objException);
                Assert.Equal("Nullable object must have a value.", v_objExpectedException.Message);
                m_objOutput.WriteLine(
                    $"{v_objProducer.Name} -> {v_objParser.Name}: FAIL_REPRODUCED; {v_objExpectedException.GetType().Name}: {v_objExpectedException.Message}");
                continue;
            }

            AssertCoreContract(v_objSheet!);
            var v_strFormula = v_objSheet!.Get(7, 2)?.Formula;
            var v_bFormulaDetected = !string.IsNullOrEmpty(v_strFormula);
            Assert.Equal(v_objParser.Name != "ExcelDataReader-3.9.0", v_bFormulaDetected);
            m_objOutput.WriteLine(
                $"{v_objProducer.Name} -> {v_objParser.Name}: PASS; formula={(v_bFormulaDetected ? "DETECTED" : "NOT_EXPOSED")}; bytes={v_objProducer.Bytes.Length}");
        }
    }

    [Fact]
    public void ClosedXml_reads_external_excel_cell_types_and_formula_without_using_cached_value()
    {
        var v_objSheet = ReadWithClosedXml(File.ReadAllBytes(GetMicrosoftExcelFixturePath()));
        AssertCoreContract(v_objSheet);

        Assert.Equal("1+1", v_objSheet.Get(7, 2)?.Formula);
        Assert.Equal("Text", v_objSheet.Get(4, 1)?.Kind);
        Assert.Equal("00123", GetText(v_objSheet.Get(4, 1)));
        Assert.Equal("Number", v_objSheet.Get(9, 1)?.Kind);
        Assert.Equal("DateTime", v_objSheet.Get(10, 5)?.Kind);
        Assert.Contains(6, v_objSheet.HiddenRows);
    }

    [Fact]
    public void ExcelDataReader_reads_external_excel_values_but_exposes_no_formula_expression()
    {
        var v_objSheet = ReadWithExcelDataReader(File.ReadAllBytes(GetMicrosoftExcelFixturePath()));
        AssertCoreContract(v_objSheet);

        Assert.Null(v_objSheet.Get(7, 2)?.Formula);
        Assert.Equal("Text", v_objSheet.Get(4, 1)?.Kind);
        Assert.Equal("00123", GetText(v_objSheet.Get(4, 1)));
        Assert.Equal("Number", v_objSheet.Get(9, 1)?.Kind);
        Assert.Equal("DateTime", v_objSheet.Get(10, 5)?.Kind);
        Assert.Contains(6, v_objSheet.HiddenRows);
        m_objOutput.WriteLine($"Formula cell exposed value: {GetText(v_objSheet.Get(7, 2))}");
    }

    [Fact]
    public void Parser_seek_requirements_are_measured_with_a_nonseekable_stream()
    {
        var v_arrBytes = File.ReadAllBytes(GetMicrosoftExcelFixturePath());
        var v_arrParsers = new (string Name, Func<Stream, ParsedSheet> Read)[]
        {
            ("Telerik", ReadWithTelerik),
            ("ExcelDataReader", ReadWithExcelDataReader),
            ("ClosedXML", ReadWithClosedXml)
        };

        foreach (var v_objParser in v_arrParsers)
        {
            using var v_objStream = new NonSeekableReadStream(new MemoryStream(v_arrBytes, writable: false));
            var v_objException = Record.Exception(() => v_objParser.Read(v_objStream));
            m_objOutput.WriteLine(v_objException is null
                ? $"{v_objParser.Name}/nonseekable: PASS"
                : $"{v_objParser.Name}/nonseekable: FAIL; {v_objException.GetType().Name}: {v_objException.Message}");
        }
    }

    [Fact]
    public async Task Product_reader_preserves_raw_excel_values_and_never_trusts_formula_cache()
    {
        var v_arrBytes = File.ReadAllBytes(GetMicrosoftExcelFixturePath());
        await using var v_objStream = new NonSeekableReadStream(new MemoryStream(v_arrBytes, writable: false));
        var v_objWorksheet = await new TKS_Thuc_Tap_V11_Data_Access.Utility.CProduct_Xlsx_Reader()
            .ReadAsync(v_objStream);

        Assert.Equal("SanPham", v_objWorksheet.Name);
        Assert.Contains(v_objWorksheet.Rows, v_objRow => v_objRow.RowNumber == 5);
        Assert.Equal("00123", GetProductCell(v_objWorksheet, 4, 1).Value);
        Assert.Equal("Text", GetProductCell(v_objWorksheet, 4, 1).CellType);
        Assert.Equal(12345d, GetProductCell(v_objWorksheet, 9, 1).Value);
        Assert.Equal("Number", GetProductCell(v_objWorksheet, 9, 1).CellType);
        Assert.IsType<DateTime>(GetProductCell(v_objWorksheet, 10, 5).Value);
        Assert.True(v_objWorksheet.Rows.Single(v_objRow => v_objRow.RowNumber == 6).IsHidden);

        var v_objFormulaCell = GetProductCell(v_objWorksheet, 7, 2);
        Assert.Equal("1+1", v_objFormulaCell.Formula);
        Assert.Null(v_objFormulaCell.Value);
        Assert.Equal("Formula", v_objFormulaCell.CellType);
        Assert.True(v_objStream.CanRead);
    }

    [Fact]
    public async Task Product_reader_reads_all_available_producers_including_the_epplus_failure_fixture()
    {
        var v_arrFixtures = new[]
        {
            (Name: "Telerik", Bytes: CreateTelerikWorkbook()),
            (Name: "EPPlus-7.0.5", Bytes: CreateEpplusWorkbook()),
            (Name: "Microsoft-Excel-16.0", Bytes: File.ReadAllBytes(GetMicrosoftExcelFixturePath()))
        };
        var v_objReader = new TKS_Thuc_Tap_V11_Data_Access.Utility.CProduct_Xlsx_Reader();

        foreach (var v_objFixture in v_arrFixtures)
        {
            await using var v_objStream = new MemoryStream(v_objFixture.Bytes, writable: false);
            var v_objWorksheet = await v_objReader.ReadAsync(v_objStream);

            Assert.Equal("SanPham", v_objWorksheet.Name);
            for (var v_iColumn = 0; v_iColumn < m_arrHeaders.Length; v_iColumn++)
                Assert.Equal(m_arrHeaders[v_iColumn], GetProductCell(v_objWorksheet, 1, v_iColumn + 1).Value);
            Assert.Equal("SP001", GetProductCell(v_objWorksheet, 2, 1).Value);
            Assert.Equal("Sữa hộp", GetProductCell(v_objWorksheet, 3, 2).Value);
            Assert.Equal("00123", GetProductCell(v_objWorksheet, 4, 1).Value);

            var v_objFormula = GetProductCell(v_objWorksheet, 7, 2);
            Assert.Equal("1+1", v_objFormula.Formula);
            Assert.Null(v_objFormula.Value);
            m_objOutput.WriteLine($"Product reader/{v_objFixture.Name}: PASS");
        }
    }

    [Fact]
    public async Task Product_reader_returns_controlled_errors_for_invalid_or_missing_product_sheet()
    {
        var v_objReader = new TKS_Thuc_Tap_V11_Data_Access.Utility.CProduct_Xlsx_Reader();
        await using var v_objMissingSheet = new MemoryStream(CreateEpplusWorkbook(includeProductSheet: false));
        await using var v_objCorrupt = new MemoryStream(new byte[] { 0x01, 0x7f, 0x00, 0xff, 0x42 });
        await using var v_objTruncated = new MemoryStream(File.ReadAllBytes(GetMicrosoftExcelFixturePath())[..128]);
        await using var v_objEmpty = new MemoryStream(Array.Empty<byte>());

        await Assert.ThrowsAsync<InvalidDataException>(() => v_objReader.ReadAsync(v_objMissingSheet));
        await Assert.ThrowsAsync<InvalidDataException>(() => v_objReader.ReadAsync(v_objCorrupt));
        await Assert.ThrowsAsync<InvalidDataException>(() => v_objReader.ReadAsync(v_objTruncated));
        await Assert.ThrowsAsync<InvalidDataException>(() => v_objReader.ReadAsync(v_objEmpty));
    }

    [Fact]
    public async Task Product_reader_rejects_input_larger_than_one_MiB()
    {
        var v_objReader = new TKS_Thuc_Tap_V11_Data_Access.Utility.CProduct_Xlsx_Reader();
        await using var v_objStream = new MemoryStream(new byte[1_048_577], writable: false);

        var v_objException = await Assert.ThrowsAsync<InvalidDataException>(
            () => v_objReader.ReadAsync(v_objStream));
        Assert.Contains("1 MiB", v_objException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Product_reader_rejects_workbooks_over_the_row_or_column_limit()
    {
        var v_objReader = new TKS_Thuc_Tap_V11_Data_Access.Utility.CProduct_Xlsx_Reader();
        await using var v_objTooManyRows = new MemoryStream(CreateBoundedProductWorkbook(row: 102, column: 5));
        await using var v_objTooManyColumns = new MemoryStream(CreateBoundedProductWorkbook(row: 2, column: 101));

        await Assert.ThrowsAsync<InvalidDataException>(() => v_objReader.ReadAsync(v_objTooManyRows));
        await Assert.ThrowsAsync<InvalidDataException>(() => v_objReader.ReadAsync(v_objTooManyColumns));
    }

    [Fact]
    public void Telerik_reads_its_own_producer_fixture_and_epplus_failure_is_reproduced()
    {
        var v_objTelerik = ReadWithTelerik(CreateTelerikWorkbook());
        AssertCoreContract(v_objTelerik);
        Assert.Equal("1+1", v_objTelerik.Get(7, 2)?.Formula);
        Assert.Equal("@", v_objTelerik.Get(4, 1)?.NumberFormat);

        var v_objMicrosoftExcel = ReadWithTelerik(File.ReadAllBytes(GetMicrosoftExcelFixturePath()));
        AssertCoreContract(v_objMicrosoftExcel);
        Assert.Equal("1+1", v_objMicrosoftExcel.Get(7, 2)?.Formula);
        m_objOutput.WriteLine(
            $"Telerik/Microsoft Excel: numeric={v_objMicrosoftExcel.Get(9, 1)?.Kind}; date={v_objMicrosoftExcel.Get(10, 5)?.Kind}; text-format={v_objMicrosoftExcel.Get(4, 1)?.NumberFormat}; hidden-row-included={v_objMicrosoftExcel.Get(6, 1)?.Value}");

        var v_objException = Assert.Throws<InvalidOperationException>(
            () => ReadWithTelerik(CreateEpplusWorkbook()));
        Assert.Equal("Nullable object must have a value.", v_objException.Message);
    }

    [Fact]
    public void All_candidates_reject_corrupt_files_and_product_sheet_shape_errors()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        var v_arrParsers = new (string Name, Func<byte[], ParsedSheet> Read)[]
        {
            ("Telerik", ReadWithTelerik),
            ("ExcelDataReader", ReadWithExcelDataReader),
            ("ClosedXML", ReadWithClosedXml)
        };
        var v_arrCorrupt = new[]
        {
            (Name: "empty", Bytes: Array.Empty<byte>()),
            (Name: "random-binary", Bytes: new byte[] { 0x01, 0x7f, 0x00, 0xff, 0x42 }),
            (Name: "truncated-xlsx", Bytes: File.ReadAllBytes(GetMicrosoftExcelFixturePath())[..128])
        };

        foreach (var v_objParser in v_arrParsers)
        foreach (var v_objFixture in v_arrCorrupt)
        {
            var v_objException = Record.Exception(() => v_objParser.Read(v_objFixture.Bytes));
            Assert.NotNull(v_objException);
            m_objOutput.WriteLine($"{v_objParser.Name}/{v_objFixture.Name}: {v_objException!.GetType().Name}");
        }

        var v_arrBadShapes = new[]
        {
            (Name: "missing-sheet", Bytes: CreateEpplusWorkbook(includeProductSheet: false)),
            (Name: "missing-header", Bytes: CreateEpplusWorkbook(headers: new[] { "Ma_San_Pham", "Ten_San_Pham", "Ma_LSP", "Ten_Don_Vi_Tinh", "" })),
            (Name: "duplicate-header", Bytes: CreateEpplusWorkbook(headers: new[] { "Ma_San_Pham", "Ma_San_Pham", "Ma_LSP", "Ten_Don_Vi_Tinh", "Ghi_Chu" }))
        };

        foreach (var v_objParser in v_arrParsers)
        foreach (var v_objFixture in v_arrBadShapes)
        {
            var v_objException = Record.Exception(() =>
            {
                var v_objSheet = v_objParser.Read(v_objFixture.Bytes);
                ValidateHeaders(v_objSheet);
            });
            Assert.NotNull(v_objException);
            var v_strClassification = v_objException is InvalidDataException ? "CONTROLLED" : "PARSER_EXCEPTION";
            m_objOutput.WriteLine($"{v_objParser.Name}/{v_objFixture.Name}: {v_strClassification}; {v_objException!.GetType().Name}: {v_objException.Message}");
        }
    }

    [Fact]
    public void Parser_allocations_and_elapsed_time_are_observed_for_10_100_and_1000_rows()
    {
        var v_arrParsers = new (string Name, Func<byte[], ParsedSheet> Read)[]
        {
            ("Telerik", ReadWithTelerik),
            ("ExcelDataReader", ReadWithExcelDataReader),
            ("ClosedXML", ReadWithClosedXml)
        };

        var v_arrWarmup = CreateTelerikBenchmarkWorkbook(10);
        foreach (var v_objParser in v_arrParsers)
            _ = v_objParser.Read(v_arrWarmup);

        foreach (var v_iRows in new[] { 10, 100, 1000 })
        {
            var v_arrBytes = CreateTelerikBenchmarkWorkbook(v_iRows);
            foreach (var v_objParser in v_arrParsers)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                var v_lAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var v_objStopwatch = Stopwatch.StartNew();
                var v_objSheet = v_objParser.Read(v_arrBytes);
                v_objStopwatch.Stop();
                var v_lAllocated = GC.GetAllocatedBytesForCurrentThread() - v_lAllocatedBefore;
                var v_iReadRows = v_objSheet.RowsSeen.Count - 1;
                Assert.Equal(v_iRows, v_iReadRows);
                m_objOutput.WriteLine(
                    $"Telerik-generated/{v_objParser.Name}/{v_iRows} rows: {v_objStopwatch.Elapsed.TotalMilliseconds:F2} ms; {v_lAllocated} allocated bytes; {v_arrBytes.Length} file bytes");
            }
        }
    }

    private static ParsedSheet ReadWithTelerik(byte[] p_arrBytes)
    {
        using var v_objStream = new MemoryStream(p_arrBytes, writable: false);
        return ReadWithTelerik(v_objStream);
    }

    private static ParsedSheet ReadWithTelerik(Stream p_objStream)
    {
        using var v_objWorkbook = SpreadImporter.CreateWorkbookImporter(SpreadDocumentFormat.Xlsx, p_objStream);
        foreach (var v_objWorksheet in v_objWorkbook.WorksheetImporters)
        {
            using (v_objWorksheet)
            {
                if (!string.Equals(v_objWorksheet.Name, "SanPham", StringComparison.Ordinal))
                    continue;

                var v_objSheet = new ParsedSheet(v_objWorksheet.Name);
                foreach (var v_objRow in v_objWorksheet.Rows)
                {
                    var v_iRow = v_objRow.RowIndex + 1;
                    v_objSheet.RowsSeen.Add(v_iRow);
                    foreach (var v_objCell in v_objRow.Cells)
                    {
                        v_objSheet.Cells[(v_iRow, v_objCell.ColumnIndex + 1)] = new CellSnapshot(
                            v_objCell.Value,
                            NormalizeTelerikKind(v_objCell.ValueType.ToString()),
                            v_objCell.FormulaValue,
                            v_objCell.Format.NumberFormat,
                            null);
                    }
                }

                return v_objSheet;
            }
        }

        throw new InvalidDataException("Required worksheet 'SanPham' was not found.");
    }

    private static ParsedSheet ReadWithExcelDataReader(byte[] p_arrBytes)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        using var v_objStream = new MemoryStream(p_arrBytes, writable: false);
        return ReadWithExcelDataReader(v_objStream);
    }

    private static ParsedSheet ReadWithExcelDataReader(Stream p_objStream)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        using var v_objReader = ExcelReaderFactory.CreateReader(p_objStream);
        do
        {
            if (!string.Equals(v_objReader.Name, "SanPham", StringComparison.Ordinal))
                continue;

            var v_objSheet = new ParsedSheet(v_objReader.Name);
            var v_iRow = 0;
            while (v_objReader.Read())
            {
                v_iRow++;
                v_objSheet.RowsSeen.Add(v_iRow);
                if (v_objReader.RowHeight == 0)
                    v_objSheet.HiddenRows.Add(v_iRow);

                for (var v_iColumn = 0; v_iColumn < v_objReader.FieldCount; v_iColumn++)
                {
                    var v_objValue = v_objReader.GetValue(v_iColumn);
                    v_objSheet.Cells[(v_iRow, v_iColumn + 1)] = new CellSnapshot(
                        v_objValue,
                        NormalizeRuntimeKind(v_objValue),
                        null,
                        v_objReader.GetNumberFormatString(v_iColumn),
                        v_objReader.RowHeight == 0);
                }
            }

            return v_objSheet;
        } while (v_objReader.NextResult());

        throw new InvalidDataException("Required worksheet 'SanPham' was not found.");
    }

    private static ParsedSheet ReadWithClosedXml(byte[] p_arrBytes)
    {
        using var v_objStream = new MemoryStream(p_arrBytes, writable: false);
        return ReadWithClosedXml(v_objStream);
    }

    private static ParsedSheet ReadWithClosedXml(Stream p_objStream)
    {
        using var v_objWorkbook = new XLWorkbook(p_objStream);
        var v_objWorksheet = v_objWorkbook.Worksheets.SingleOrDefault(
            v_objCandidate => string.Equals(v_objCandidate.Name, "SanPham", StringComparison.Ordinal));
        if (v_objWorksheet is null)
            throw new InvalidDataException("Required worksheet 'SanPham' was not found.");

        var v_objSheet = new ParsedSheet(v_objWorksheet.Name);
        var v_iLastRow = v_objWorksheet.LastRowUsed()?.RowNumber() ?? 0;
        var v_iLastColumn = v_objWorksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var v_iRow = 1; v_iRow <= v_iLastRow; v_iRow++)
        {
            v_objSheet.RowsSeen.Add(v_iRow);
            if (v_objWorksheet.Row(v_iRow).IsHidden)
                v_objSheet.HiddenRows.Add(v_iRow);

            for (var v_iColumn = 1; v_iColumn <= v_iLastColumn; v_iColumn++)
            {
                var v_objCell = v_objWorksheet.Cell(v_iRow, v_iColumn);
                var v_objCached = v_objCell.HasFormula ? v_objCell.CachedValue : v_objCell.Value;
                object? v_objValue = v_objCell.HasFormula
                    ? v_objCached.ToString()
                    : v_objCell.DataType switch
                    {
                        XLDataType.Blank => null,
                        XLDataType.Text => v_objCell.GetString(),
                        XLDataType.Number => v_objCell.GetDouble(),
                        XLDataType.DateTime => v_objCell.GetDateTime(),
                        XLDataType.TimeSpan => v_objCell.GetTimeSpan(),
                        XLDataType.Boolean => v_objCell.GetBoolean(),
                        XLDataType.Error => v_objCell.GetError().ToString(),
                        _ => v_objCell.Value.ToString()
                    };
                v_objSheet.Cells[(v_iRow, v_iColumn)] = new CellSnapshot(
                    v_objValue,
                    NormalizeClosedXmlKind(v_objCell.HasFormula ? v_objCached.Type : v_objCell.DataType),
                    v_objCell.HasFormula ? v_objCell.FormulaA1 : null,
                    v_objCell.Style.NumberFormat.Format,
                    v_objWorksheet.Row(v_iRow).IsHidden);
            }
        }

        return v_objSheet;
    }

    private static void AssertCoreContract(ParsedSheet p_objSheet)
    {
        Assert.Equal("SanPham", p_objSheet.Name);
        for (var v_iColumn = 0; v_iColumn < m_arrHeaders.Length; v_iColumn++)
            Assert.Equal(m_arrHeaders[v_iColumn], GetText(p_objSheet.Get(1, v_iColumn + 1)));

        Assert.Equal("SP001", GetText(p_objSheet.Get(2, 1)));
        Assert.Equal("Coca Cola", GetText(p_objSheet.Get(2, 2)));
        Assert.Equal("Sữa hộp", GetText(p_objSheet.Get(3, 2)));
        Assert.Equal("Hộp", GetText(p_objSheet.Get(3, 4)));
        Assert.Equal("00123", GetText(p_objSheet.Get(4, 1)));
        Assert.Equal("Text", p_objSheet.Get(4, 1)?.Kind);
        Assert.Contains(5, p_objSheet.RowsSeen);
        Assert.True(p_objSheet.Get(5, 1) is null || p_objSheet.Get(5, 1)?.Value is null);
        Assert.Equal("  Tên có khoảng trắng  ", GetText(p_objSheet.Get(6, 2)));
        Assert.Equal(new string('L', 1500), GetText(p_objSheet.Get(8, 5)));
    }

    private static void ValidateHeaders(ParsedSheet p_objSheet)
    {
        var v_arrActual = Enumerable.Range(1, m_arrHeaders.Length)
            .Select(v_iColumn => GetText(p_objSheet.Get(1, v_iColumn)))
            .ToArray();
        if (v_arrActual.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Required product header is missing.");
        if (v_arrActual.Distinct(StringComparer.OrdinalIgnoreCase).Count() != v_arrActual.Length)
            throw new InvalidDataException("Product headers must be unique.");
        if (!v_arrActual.SequenceEqual(m_arrHeaders, StringComparer.Ordinal))
            throw new InvalidDataException("Product headers do not match the required template.");
    }

    private static byte[] CreateTelerikWorkbook()
    {
        using var v_objStream = new MemoryStream();
        using (var v_objExporter = SpreadExporter.CreateWorkbookExporter(SpreadDocumentFormat.Xlsx, v_objStream))
        {
            using (var v_objInstructions = v_objExporter.CreateWorksheetExporter("Instructions"))
                WriteTelerikRow(v_objInstructions, "Parser test fixture");
            using var v_objWorksheet = v_objExporter.CreateWorksheetExporter("SanPham");
            WriteTelerikRows(v_objWorksheet, includeBusinessRows: true);
        }

        return v_objStream.ToArray();
    }

    private static byte[] CreateTelerikBenchmarkWorkbook(int p_iDataRows)
    {
        using var v_objStream = new MemoryStream();
        using (var v_objExporter = SpreadExporter.CreateWorkbookExporter(SpreadDocumentFormat.Xlsx, v_objStream))
        {
            using (var v_objInstructions = v_objExporter.CreateWorksheetExporter("Instructions"))
                WriteTelerikRow(v_objInstructions, "Parser benchmark fixture");
            using var v_objWorksheet = v_objExporter.CreateWorksheetExporter("SanPham");
            WriteTelerikRows(v_objWorksheet, includeBusinessRows: false);
            for (var v_i = 1; v_i <= p_iDataRows; v_i++)
                WriteTelerikRow(v_objWorksheet, $"SP{v_i:00000}", $"Product {v_i:00000}", "TEST", "Cái", $"Generated row {v_i:00000}");
        }

        return v_objStream.ToArray();
    }

    private static void WriteTelerikRows(IWorksheetExporter p_objWorksheet, bool includeBusinessRows)
    {
        WriteTelerikRow(p_objWorksheet, m_arrHeaders);
        if (!includeBusinessRows)
            return;

        WriteTelerikRow(p_objWorksheet, "SP001", "Coca Cola", "NUOC", "Lon", "Test");
        WriteTelerikRow(p_objWorksheet, "SP002", "Sữa hộp", "SUA", "Hộp", "Hàng lạnh");
        WriteTelerikRow(p_objWorksheet, "00123", "Sản phẩm mã số", "TEST", "Cái", "Ghi chú");
        WriteTelerikRow(p_objWorksheet);
        WriteTelerikRow(p_objWorksheet, "SP004", "  Tên có khoảng trắng  ", "TEST", "Cái", null);
        WriteTelerikRow(p_objWorksheet, "SP005", null, "TEST", "Cái", "Formula in business field");
        WriteTelerikRow(p_objWorksheet, "SP006", "Tên dài", "TEST", "Cái", new string('L', 1500));
    }

    private static void WriteTelerikRow(IWorksheetExporter p_objWorksheet, params string?[] p_arrValues)
    {
        using var v_objRow = p_objWorksheet.CreateRowExporter();
        for (var v_iColumn = 0; v_iColumn < p_arrValues.Length; v_iColumn++)
        {
            using var v_objCell = v_objRow.CreateCellExporter();
            if (p_arrValues.Length >= 5 && p_arrValues[0] == "00123" && v_iColumn == 0)
                v_objCell.SetFormat(new SpreadCellFormat { NumberFormat = "@" });
            if (p_arrValues.Length >= 5 && p_arrValues[0] == "SP005" && v_iColumn == 1)
                v_objCell.SetFormula("1+1");
            else if (p_arrValues[v_iColumn] is { } v_strValue)
                v_objCell.SetValue(v_strValue);
        }
    }

    private static byte[] CreateEpplusWorkbook(
        bool includeProductSheet = true,
        string[]? headers = null)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var v_objPackage = new ExcelPackage();
        v_objPackage.Workbook.Worksheets.Add("Instructions").Cells[1, 1].Value = "Parser test fixture";
        if (includeProductSheet)
        {
            var v_objSheet = v_objPackage.Workbook.Worksheets.Add("SanPham");
            var v_arrHeaders = headers ?? m_arrHeaders;
            for (var v_iColumn = 0; v_iColumn < v_arrHeaders.Length; v_iColumn++)
                v_objSheet.Cells[1, v_iColumn + 1].Value = v_arrHeaders[v_iColumn];

            WriteEpplusRow(v_objSheet, 2, "SP001", "Coca Cola", "NUOC", "Lon", "Test");
            WriteEpplusRow(v_objSheet, 3, "SP002", "Sữa hộp", "SUA", "Hộp", "Hàng lạnh");
            WriteEpplusRow(v_objSheet, 4, "00123", "Sản phẩm mã số", "TEST", "Cái", "Ghi chú");
            v_objSheet.Cells[4, 1].Style.Numberformat.Format = "@";
            WriteEpplusRow(v_objSheet, 6, "SP004", "  Tên có khoảng trắng  ", "TEST", "Cái", null);
            WriteEpplusRow(v_objSheet, 7, "SP005", null, "TEST", "Cái", "Formula in business field");
            v_objSheet.Cells[7, 2].Formula = "1+1";
            WriteEpplusRow(v_objSheet, 8, "SP006", "Tên dài", "TEST", "Cái", new string('L', 1500));
        }

        using var v_objStream = new MemoryStream();
        v_objPackage.SaveAs(v_objStream);
        return v_objStream.ToArray();
    }

    private static byte[] CreateBoundedProductWorkbook(int row, int column)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var v_objPackage = new ExcelPackage();
        var v_objSheet = v_objPackage.Workbook.Worksheets.Add("SanPham");
        for (var v_iColumn = 0; v_iColumn < m_arrHeaders.Length; v_iColumn++)
            v_objSheet.Cells[1, v_iColumn + 1].Value = m_arrHeaders[v_iColumn];
        v_objSheet.Cells[row, 1].Value = "SP001";
        v_objSheet.Cells[row, column].Value = "test";

        using var v_objStream = new MemoryStream();
        v_objPackage.SaveAs(v_objStream);
        return v_objStream.ToArray();
    }

    private static void WriteEpplusRow(ExcelWorksheet p_objSheet, int p_iRow, params string?[] p_arrValues)
    {
        for (var v_iColumn = 0; v_iColumn < p_arrValues.Length; v_iColumn++)
            p_objSheet.Cells[p_iRow, v_iColumn + 1].Value = p_arrValues[v_iColumn];
    }

    private static string GetMicrosoftExcelFixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ProductXlsxCompatibility", "MicrosoftExcelDesktop.xlsx");

    private static string? GetText(CellSnapshot? p_objCell) => p_objCell?.Value switch
    {
        null => null,
        string v_strValue => v_strValue,
        DateTime v_dtmValue => v_dtmValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable v_objValue => v_objValue.ToString(null, CultureInfo.InvariantCulture),
        var v_objValue => v_objValue.ToString()
    };

    private static TKS_Thuc_Tap_V11_Data_Access.Utility.CProduct_Xlsx_Cell GetProductCell(
        TKS_Thuc_Tap_V11_Data_Access.Utility.CProduct_Xlsx_Worksheet p_objWorksheet,
        int p_iRowNumber,
        int p_iColumnNumber) => p_objWorksheet.Rows
            .Single(v_objRow => v_objRow.RowNumber == p_iRowNumber)
            .Cells.Single(v_objCell => v_objCell.ColumnNumber == p_iColumnNumber);

    private static string NormalizeRuntimeKind(object? p_objValue) => p_objValue switch
    {
        null => "Blank",
        string => "Text",
        DateTime => "DateTime",
        TimeSpan => "TimeSpan",
        bool => "Boolean",
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => "Number",
        _ => p_objValue.GetType().Name
    };

    private static string NormalizeTelerikKind(string p_strKind) =>
        p_strKind.Contains("String", StringComparison.OrdinalIgnoreCase) ||
        p_strKind.Contains("Text", StringComparison.OrdinalIgnoreCase)
            ? "Text"
            : p_strKind;

    private static string NormalizeClosedXmlKind(XLDataType p_objKind) => p_objKind switch
    {
        XLDataType.Blank => "Blank",
        XLDataType.Text => "Text",
        XLDataType.Number => "Number",
        XLDataType.DateTime => "DateTime",
        XLDataType.TimeSpan => "TimeSpan",
        XLDataType.Boolean => "Boolean",
        XLDataType.Error => "Error",
        _ => p_objKind.ToString()
    };

    private static string NormalizeClosedXmlKind(XLDataType? p_objKind) =>
        p_objKind.HasValue ? NormalizeClosedXmlKind(p_objKind.Value) : "Blank";

    private sealed class ParsedSheet(string p_strName)
    {
        public string Name { get; } = p_strName;
        public Dictionary<(int Row, int Column), CellSnapshot> Cells { get; } = new();
        public HashSet<int> RowsSeen { get; } = new();
        public HashSet<int> HiddenRows { get; } = new();
        public CellSnapshot? Get(int p_iRow, int p_iColumn) =>
            Cells.TryGetValue((p_iRow, p_iColumn), out var v_objCell) ? v_objCell : null;
    }

    private sealed record CellSnapshot(object? Value, string Kind, string? Formula, string? NumberFormat, bool? IsHidden);

    private sealed class NonSeekableReadStream(Stream p_objInner) : Stream
    {
        public override bool CanRead => p_objInner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] p_arrBuffer, int p_iOffset, int p_iCount) =>
            p_objInner.Read(p_arrBuffer, p_iOffset, p_iCount);
        public override int Read(Span<byte> p_arrBuffer) => p_objInner.Read(p_arrBuffer);
        public override long Seek(long p_lOffset, SeekOrigin p_objOrigin) => throw new NotSupportedException();
        public override void SetLength(long p_lValue) => throw new NotSupportedException();
        public override void Write(byte[] p_arrBuffer, int p_iOffset, int p_iCount) => throw new NotSupportedException();

        protected override void Dispose(bool p_bDisposing)
        {
            if (p_bDisposing)
                p_objInner.Dispose();
            base.Dispose(p_bDisposing);
        }
    }
}
