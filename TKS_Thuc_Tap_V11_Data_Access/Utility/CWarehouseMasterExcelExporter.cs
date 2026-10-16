using System.Globalization;
using ClosedXML.Excel;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Utility;

public static class CWarehouseMasterExcelExporter
{
    public static byte[] CreateWorkbook(
        IEnumerable<CWarehouseMaster> p_arrMasters,
        IEnumerable<CWarehouseMasterExcelColumn> p_arrColumns,
        string p_strWorksheetName)
    {
        ArgumentNullException.ThrowIfNull(p_arrMasters);
        ArgumentNullException.ThrowIfNull(p_arrColumns);
        if (string.IsNullOrWhiteSpace(p_strWorksheetName))
            throw new ArgumentException("A worksheet name is required.", nameof(p_strWorksheetName));

        var v_arrRows = p_arrMasters.ToList();
        var v_arrColumns = p_arrColumns.ToList();
        if (v_arrColumns.Count == 0)
            throw new ArgumentException("At least one exportable column is required.", nameof(p_arrColumns));

        var v_arrProperties = v_arrColumns
            .Select(v_objColumn => typeof(CWarehouseMaster).GetProperty(v_objColumn.FieldName)
                ?? throw new ArgumentException($"Unknown warehouse master field '{v_objColumn.FieldName}'.", nameof(p_arrColumns)))
            .ToList();

        using var v_objWorkbook = new XLWorkbook();
        var v_objWorksheet = v_objWorkbook.Worksheets.Add(p_strWorksheetName);
        v_objWorksheet.Style.Font.FontName = "Calibri";

        for (var v_iColumn = 0; v_iColumn < v_arrColumns.Count; v_iColumn++)
        {
            var v_iExcelColumn = v_iColumn + 1;
            var v_objColumn = v_arrColumns[v_iColumn];
            var v_objProperty = v_arrProperties[v_iColumn];
            v_objWorksheet.Cell(1, v_iExcelColumn).Value = v_objColumn.Title;
            v_objWorksheet.Column(v_iExcelColumn).Width = To_Excel_Column_Width(v_objColumn.Width);

            var v_objPropertyType = Nullable.GetUnderlyingType(v_objProperty.PropertyType) ?? v_objProperty.PropertyType;
            if (v_objPropertyType == typeof(DateTime))
                v_objWorksheet.Column(v_iExcelColumn).Style.DateFormat.Format = "dd/MM/yyyy HH:mm:ss";
            else if (v_objPropertyType != typeof(string) && v_objPropertyType != typeof(bool) && v_objColumn.FieldName != nameof(CWarehouseMaster.Auto_ID))
                v_objWorksheet.Column(v_iExcelColumn).Style.NumberFormat.Format = "#,##0.##;-#,##0.##;-";
        }

        for (var v_iRow = 0; v_iRow < v_arrRows.Count; v_iRow++)
        {
            var v_iExcelRow = v_iRow + 2;
            for (var v_iColumn = 0; v_iColumn < v_arrColumns.Count; v_iColumn++)
            {
                var v_objValue = v_arrProperties[v_iColumn].GetValue(v_arrRows[v_iRow]);
                Set_Cell_Value(v_objWorksheet.Cell(v_iExcelRow, v_iColumn + 1), v_objValue);
            }
        }

        var v_iLastRow = v_arrRows.Count + 1;
        var v_iLastColumn = v_arrColumns.Count;
        var v_objRange = v_objWorksheet.Range(1, 1, v_iLastRow, v_iLastColumn);
        v_objRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        v_objRange.Style.Alignment.WrapText = true;
        v_objWorksheet.Range(1, 1, 1, v_iLastColumn).Style.Font.Bold = true;
        v_objWorksheet.Range(1, 1, 1, v_iLastColumn).Style.Fill.BackgroundColor = XLColor.LightGray;
        v_objRange.SetAutoFilter();
        v_objWorksheet.SheetView.FreezeRows(1);

        using var v_objStream = new MemoryStream();
        v_objWorkbook.SaveAs(v_objStream);
        return v_objStream.ToArray();
    }

    private static double To_Excel_Column_Width(string p_strWidth)
    {
        var v_strWidth = p_strWidth.Trim();
        if (v_strWidth.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            v_strWidth = v_strWidth[..^2];

        return double.TryParse(v_strWidth, NumberStyles.Float, CultureInfo.InvariantCulture, out var v_dblWidth)
            ? Math.Max(10, v_dblWidth / 7)
            : 18;
    }

    private static void Set_Cell_Value(IXLCell p_objCell, object? p_objValue)
    {
        switch (p_objValue)
        {
            case null:
                break;
            case string v_strValue:
                p_objCell.Value = v_strValue;
                break;
            case DateTime v_dtmValue:
                p_objCell.Value = v_dtmValue;
                break;
            case bool v_bValue:
                p_objCell.Value = v_bValue;
                break;
            case byte v_byValue:
                p_objCell.Value = v_byValue;
                break;
            case short v_i16Value:
                p_objCell.Value = v_i16Value;
                break;
            case int v_i32Value:
                p_objCell.Value = v_i32Value;
                break;
            case long v_i64Value:
                p_objCell.Value = v_i64Value;
                break;
            case decimal v_decValue:
                p_objCell.Value = v_decValue;
                break;
            case double v_dblValue:
                p_objCell.Value = v_dblValue;
                break;
            case float v_fValue:
                p_objCell.Value = v_fValue;
                break;
            default:
                p_objCell.Value = Convert.ToString(p_objValue, CultureInfo.CurrentCulture) ?? "";
                break;
        }
    }
}

public sealed record CWarehouseMasterExcelColumn(string FieldName, string Title, string Width);
