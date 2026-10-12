using ClosedXML.Excel;

namespace TKS_Thuc_Tap_V11_Data_Access.Utility;

internal static class CMaster_Xlsx_Reader
{
    private const int m_iMaxFileSize = 1_048_576;
    private const int m_iMaxDataRows = 100;

    internal static async Task<CMaster_Xlsx_Worksheet> ReadAsync(
        Stream p_objSource,
        string p_strWorksheetName,
        int p_iMaxColumns,
        string p_strEntityName,
        CancellationToken p_objCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(p_objSource);
        if (!p_objSource.CanRead)
            throw new ArgumentException("The XLSX source stream must be readable.", nameof(p_objSource));

        using var v_objBuffer = new MemoryStream();
        var v_arrBuffer = new byte[81920];
        while (true)
        {
            var v_iRead = await p_objSource.ReadAsync(v_arrBuffer.AsMemory(), p_objCancellationToken);
            if (v_iRead == 0)
                break;
            if (v_objBuffer.Length + v_iRead > m_iMaxFileSize)
                throw new InvalidDataException($"{p_strEntityName} XLSX files must not exceed 1 MiB.");

            await v_objBuffer.WriteAsync(v_arrBuffer.AsMemory(0, v_iRead), p_objCancellationToken);
        }

        v_objBuffer.Position = 0;
        try
        {
            using var v_objWorkbook = new XLWorkbook(v_objBuffer);
            if (v_objWorkbook.Worksheets.Count != 1 ||
                !string.Equals(v_objWorkbook.Worksheets.First().Name, p_strWorksheetName, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Workbook must contain only the '{p_strWorksheetName}' worksheet.");
            }

            return ReadWorksheet(v_objWorkbook.Worksheets.First(), p_iMaxColumns, p_strEntityName);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception p_objException) when (
            p_objException is not OutOfMemoryException and not OperationCanceledException)
        {
            throw new InvalidDataException($"The {p_strEntityName} XLSX file is invalid or cannot be read.", p_objException);
        }
    }

    private static CMaster_Xlsx_Worksheet ReadWorksheet(
        IXLWorksheet p_objWorksheet,
        int p_iMaxColumns,
        string p_strEntityName)
    {
        var v_iLastRow = p_objWorksheet.LastRowUsed()?.RowNumber() ?? 0;
        var v_iLastColumn = p_objWorksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (v_iLastRow > m_iMaxDataRows + 1)
            throw new InvalidDataException($"{p_strEntityName} XLSX files must not exceed 100 data rows.");
        if (v_iLastColumn > p_iMaxColumns)
            throw new InvalidDataException($"{p_strEntityName} XLSX files must not exceed {p_iMaxColumns} columns.");

        var v_arrRows = new List<CMaster_Xlsx_Row>(v_iLastRow);
        for (var v_iRow = 1; v_iRow <= v_iLastRow; v_iRow++)
        {
            var v_objRow = p_objWorksheet.Row(v_iRow);
            var v_arrCells = new List<CMaster_Xlsx_Cell>(v_iLastColumn);
            for (var v_iColumn = 1; v_iColumn <= v_iLastColumn; v_iColumn++)
            {
                var v_objCell = p_objWorksheet.Cell(v_iRow, v_iColumn);
                var v_objType = v_objCell.DataType;
                var v_objValue = v_objCell.HasFormula ? null : v_objType switch
                {
                    XLDataType.Blank => null,
                    XLDataType.Text => (object)v_objCell.GetString(),
                    XLDataType.Number => v_objCell.GetDouble(),
                    XLDataType.DateTime => v_objCell.GetDateTime(),
                    XLDataType.TimeSpan => v_objCell.GetTimeSpan(),
                    XLDataType.Boolean => v_objCell.GetBoolean(),
                    XLDataType.Error => v_objCell.Value.ToString(),
                    _ => v_objCell.Value.ToString()
                };

                v_arrCells.Add(new CMaster_Xlsx_Cell(
                    v_iColumn,
                    v_objCell.HasFormula ? "Formula" : NormalizeCellType(v_objType),
                    v_objValue,
                    v_objCell.HasFormula ? v_objCell.FormulaA1 : null,
                    v_objCell.Style.NumberFormat.Format));
            }

            v_arrRows.Add(new CMaster_Xlsx_Row(v_iRow, v_objRow.IsHidden, v_arrCells));
        }

        return new CMaster_Xlsx_Worksheet(p_objWorksheet.Name, v_arrRows);
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
}

internal sealed record CMaster_Xlsx_Worksheet(string Name, IReadOnlyList<CMaster_Xlsx_Row> Rows);
internal sealed record CMaster_Xlsx_Row(int RowNumber, bool IsHidden, IReadOnlyList<CMaster_Xlsx_Cell> Cells);
internal sealed record CMaster_Xlsx_Cell(int ColumnNumber, string CellType, object? Value, string? Formula, string? NumberFormat);
