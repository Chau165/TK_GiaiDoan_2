using OfficeOpenXml;

namespace TKS_Thuc_Tap_V11_Data_Access.Utility;

public sealed class CProfiledWorkbook_Xlsx_Reader
{
    private readonly CWorkbook_Xlsx_Reader m_objWorkbookReader = new();
    private readonly CWorkbookStructureDetector m_objDetector;
    private readonly CImportProfileProvider m_objProfileProvider;

    public CProfiledWorkbook_Xlsx_Reader()
        : this(new CImportProfileProvider())
    {
    }

    public CProfiledWorkbook_Xlsx_Reader(CImportProfileProvider p_objProfileProvider)
    {
        ArgumentNullException.ThrowIfNull(p_objProfileProvider);
        m_objProfileProvider = p_objProfileProvider;
        m_objDetector = new CWorkbookStructureDetector(p_objProfileProvider);
    }

    public async Task<CWorkbookStructureDetectionResult> ReadAsync(
        Stream p_objSource,
        string p_strProfileCode,
        CancellationToken p_objCancellationToken = default)
    {
        var v_objProfile = m_objProfileProvider.GetByCode(p_strProfileCode);
        var v_objWorkbook = await m_objWorkbookReader.ReadAsync(
            p_objSource,
            new CWorkbook_Xlsx_Limits(
                v_objProfile.MaxFileBytes,
                v_objProfile.MaxWorksheets,
                checked(v_objProfile.MaxRows + 20),
                v_objProfile.MaxColumns),
            p_objCancellationToken);
        return m_objDetector.Detect(v_objWorkbook, v_objProfile);
    }

    public async Task<CImportDetectedSheet> ReadSingleSheetAsync(
        Stream p_objSource,
        string p_strProfileCode,
        CancellationToken p_objCancellationToken = default)
    {
        var v_objResult = await ReadAsync(p_objSource, p_strProfileCode, p_objCancellationToken);
        if (!v_objResult.IsValid)
            throw new InvalidDataException(string.Join(Environment.NewLine, v_objResult.Errors.Select(v_objError => v_objError.Message)));
        if (v_objResult.SheetMappings.Count != 1)
            throw new InvalidDataException($"Profile '{p_strProfileCode}' did not identify exactly one data sheet.");
        return v_objResult.SheetMappings[0];
    }

    public CImportDetectedSheet ReadSingleSheet(
        ExcelPackage p_objPackage,
        string p_strProfileCode,
        long p_lFileLength)
    {
        ArgumentNullException.ThrowIfNull(p_objPackage);
        var v_objProfile = m_objProfileProvider.GetByCode(p_strProfileCode);
        if (p_lFileLength > v_objProfile.MaxFileBytes)
            throw new InvalidDataException($"XLSX files must not exceed {v_objProfile.MaxFileBytes} bytes.");

        if (p_objPackage.Workbook.Worksheets.Count > v_objProfile.MaxWorksheets)
            throw new InvalidDataException($"XLSX files must not exceed {v_objProfile.MaxWorksheets} worksheets.");

        var v_iMaxRows = checked(v_objProfile.MaxRows + 20);
        var v_arrSheets = new List<CWorkbook_Xlsx_Sheet>(p_objPackage.Workbook.Worksheets.Count);
        foreach (var v_objWorksheet in p_objPackage.Workbook.Worksheets)
        {
            var v_iLastRow = v_objWorksheet.Dimension?.End.Row ?? 0;
            var v_iLastColumn = v_objWorksheet.Dimension?.End.Column ?? 0;
            if (v_iLastRow > v_iMaxRows)
                throw new InvalidDataException($"Worksheet '{v_objWorksheet.Name}' exceeds the row limit.");
            if (v_iLastColumn > v_objProfile.MaxColumns)
                throw new InvalidDataException($"Worksheet '{v_objWorksheet.Name}' exceeds the column limit.");

            var v_arrRows = new List<CWorkbook_Xlsx_Row>(v_iLastRow);
            for (var v_iRow = 1; v_iRow <= v_iLastRow; v_iRow++)
            {
                var v_objRow = v_objWorksheet.Row(v_iRow);
                var v_arrCells = new List<CWorkbook_Xlsx_Cell>(v_iLastColumn);
                for (var v_iColumn = 1; v_iColumn <= v_iLastColumn; v_iColumn++)
                {
                    var v_objCell = v_objWorksheet.Cells[v_iRow, v_iColumn];
                    var v_bHasFormula = !string.IsNullOrWhiteSpace(v_objCell.Formula);
                    var v_objValue = v_bHasFormula ? null : v_objCell.Value;
                    v_arrCells.Add(new CWorkbook_Xlsx_Cell(
                        v_iColumn,
                        v_bHasFormula ? "Formula" : NormalizeCellType(v_objValue),
                        v_objValue,
                        v_bHasFormula ? v_objCell.Formula : null,
                        NormalizeNumberFormat(v_objCell.Style.Numberformat.Format)));
                }

                v_arrRows.Add(new CWorkbook_Xlsx_Row(v_iRow, v_objRow.Hidden, v_arrCells));
            }

            v_arrSheets.Add(new CWorkbook_Xlsx_Sheet(v_objWorksheet.Name, v_arrRows));
        }

        var v_objDetection = m_objDetector.Detect(
            new CWorkbook_Xlsx_Workbook(v_arrSheets),
            v_objProfile);
        if (!v_objDetection.IsValid)
            throw new InvalidDataException(string.Join(Environment.NewLine, v_objDetection.Errors.Select(v_objError => v_objError.Message)));
        if (v_objDetection.SheetMappings.Count != 1)
            throw new InvalidDataException($"Profile '{p_strProfileCode}' did not identify exactly one data sheet.");

        return v_objDetection.SheetMappings[0];
    }

    private static string NormalizeCellType(object? p_objValue) => p_objValue switch
    {
        null => "Blank",
        DateTime => "DateTime",
        TimeSpan => "TimeSpan",
        bool => "Boolean",
        string => "Text",
        _ when p_objValue.GetType().Name == "ExcelErrorValue" => "Error",
        _ => "Number"
    };

    private static string NormalizeNumberFormat(string p_strNumberFormat) =>
        string.Equals(p_strNumberFormat, "General", StringComparison.OrdinalIgnoreCase)
            ? ""
            : p_strNumberFormat;
}
