using ClosedXML.Excel;

namespace TKS_Thuc_Tap_V11_Data_Access.Utility;

public sealed record CWorkbook_Xlsx_Limits(
    int MaxFileBytes = 26_214_400,
    int MaxWorksheets = 10,
    int MaxRowsPerWorksheet = 50_000,
    int MaxColumnsPerWorksheet = 200);

public sealed class CWorkbook_Xlsx_Reader
{
    private const int m_iBufferSize = 81_920;

    public async Task<CWorkbook_Xlsx_Workbook> ReadAsync(
        Stream p_objSource,
        CWorkbook_Xlsx_Limits? p_objLimits = null,
        CancellationToken p_objCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(p_objSource);
        if (!p_objSource.CanRead)
            throw new ArgumentException("The XLSX source stream must be readable.", nameof(p_objSource));

        var v_objLimits = p_objLimits ?? new CWorkbook_Xlsx_Limits();
        if (v_objLimits.MaxFileBytes <= 0 || v_objLimits.MaxWorksheets <= 0 ||
            v_objLimits.MaxRowsPerWorksheet <= 0 || v_objLimits.MaxColumnsPerWorksheet <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(p_objLimits), "XLSX limits must be positive.");
        }

        using var v_objBuffer = new MemoryStream();
        var v_arrBuffer = new byte[m_iBufferSize];
        while (true)
        {
            var v_iRead = await p_objSource.ReadAsync(v_arrBuffer.AsMemory(), p_objCancellationToken);
            if (v_iRead == 0)
                break;
            if (v_objBuffer.Length + v_iRead > v_objLimits.MaxFileBytes)
                throw new InvalidDataException($"XLSX files must not exceed {FormatFileSize(v_objLimits.MaxFileBytes)}.");

            await v_objBuffer.WriteAsync(v_arrBuffer.AsMemory(0, v_iRead), p_objCancellationToken);
        }

        v_objBuffer.Position = 0;
        try
        {
            using var v_objWorkbook = new XLWorkbook(v_objBuffer);
            if (v_objWorkbook.Worksheets.Count > v_objLimits.MaxWorksheets)
                throw new InvalidDataException($"XLSX files must not exceed {v_objLimits.MaxWorksheets} worksheets.");

            var v_arrSheets = v_objWorkbook.Worksheets
                .Select(v_objWorksheet => ReadWorksheet(v_objWorksheet, v_objLimits))
                .ToArray();
            return new CWorkbook_Xlsx_Workbook(v_arrSheets);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception p_objException) when (
            p_objException is not OutOfMemoryException and not OperationCanceledException)
        {
            throw new InvalidDataException("The XLSX file is invalid or cannot be read.", p_objException);
        }
    }

    private static CWorkbook_Xlsx_Sheet ReadWorksheet(IXLWorksheet p_objWorksheet, CWorkbook_Xlsx_Limits p_objLimits)
    {
        var v_iLastRow = p_objWorksheet.LastRowUsed()?.RowNumber() ?? 0;
        var v_iLastColumn = p_objWorksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (v_iLastRow > p_objLimits.MaxRowsPerWorksheet)
            throw new InvalidDataException($"Worksheet '{p_objWorksheet.Name}' exceeds the row limit.");
        if (v_iLastColumn > p_objLimits.MaxColumnsPerWorksheet)
            throw new InvalidDataException($"Worksheet '{p_objWorksheet.Name}' exceeds the column limit.");

        var v_arrRows = new List<CWorkbook_Xlsx_Row>(v_iLastRow);
        for (var v_iRow = 1; v_iRow <= v_iLastRow; v_iRow++)
        {
            var v_objRow = p_objWorksheet.Row(v_iRow);
            var v_arrCells = new List<CWorkbook_Xlsx_Cell>(v_iLastColumn);
            for (var v_iColumn = 1; v_iColumn <= v_iLastColumn; v_iColumn++)
            {
                var v_objCell = p_objWorksheet.Cell(v_iRow, v_iColumn);
                var v_objType = v_objCell.DataType;
                object? v_objValue = v_objCell.HasFormula ? null : v_objType switch
                {
                    XLDataType.Blank => null,
                    XLDataType.Text => v_objCell.GetString(),
                    XLDataType.Number => v_objCell.GetDouble(),
                    XLDataType.DateTime => v_objCell.GetDateTime(),
                    XLDataType.TimeSpan => v_objCell.GetTimeSpan(),
                    XLDataType.Boolean => v_objCell.GetBoolean(),
                    XLDataType.Error => v_objCell.Value.ToString(),
                    _ => v_objCell.Value.ToString()
                };

                v_arrCells.Add(new CWorkbook_Xlsx_Cell(
                    v_iColumn,
                    v_objCell.HasFormula ? "Formula" : NormalizeCellType(v_objType),
                    v_objValue,
                    v_objCell.HasFormula ? v_objCell.FormulaA1 : null,
                    v_objCell.Style.NumberFormat.Format));
            }

            v_arrRows.Add(new CWorkbook_Xlsx_Row(v_iRow, v_objRow.IsHidden, v_arrCells));
        }

        return new CWorkbook_Xlsx_Sheet(p_objWorksheet.Name, v_arrRows);
    }

    private static string NormalizeCellType(XLDataType p_objType) => p_objType switch
    {
        XLDataType.Blank => "Blank",
        XLDataType.Text => "Text",
        XLDataType.Number => "Number",
        XLDataType.DateTime => "DateTime",
        XLDataType.TimeSpan => "TimeSpan",
        XLDataType.Boolean => "Boolean",
        XLDataType.Error => "Error",
        _ => p_objType.ToString()
    };

    private static string FormatFileSize(int p_iBytes) => p_iBytes % 1_048_576 == 0
        ? $"{p_iBytes / 1_048_576} MiB"
        : $"{p_iBytes} bytes";
}

public sealed record CWorkbook_Xlsx_Workbook(IReadOnlyList<CWorkbook_Xlsx_Sheet> Worksheets);

public sealed record CWorkbook_Xlsx_Sheet(string Name, IReadOnlyList<CWorkbook_Xlsx_Row> Rows);

public sealed record CWorkbook_Xlsx_Row(int RowNumber, bool IsHidden, IReadOnlyList<CWorkbook_Xlsx_Cell> Cells);

public sealed record CWorkbook_Xlsx_Cell(
    int ColumnNumber,
    string CellType,
    object? Value,
    string? Formula,
    string? NumberFormat);
