namespace TKS_Thuc_Tap_V11_Data_Access.Utility;

public sealed class CCategory_Xlsx_Reader
{
    public async Task<CCategory_Xlsx_Worksheet> ReadAsync(
        Stream p_objSource,
        CancellationToken p_objCancellationToken = default)
    {
        var v_objSheet = await new CProfiledWorkbook_Xlsx_Reader()
            .ReadSingleSheetAsync(p_objSource, "Category", p_objCancellationToken);
        return CImportLegacyWorksheetAdapter.ToCategory(v_objSheet);
    }
}

public sealed record CCategory_Xlsx_Worksheet(string Name, IReadOnlyList<CCategory_Xlsx_Row> Rows)
{
    public IReadOnlyList<string> MappedColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> IgnoredColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnsupportedColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<CImportCapabilityIssue> UnsupportedCapabilities { get; init; } = Array.Empty<CImportCapabilityIssue>();
    public IReadOnlyList<CImportFieldPolicyDiagnostic> PolicyDiagnostics { get; init; } = Array.Empty<CImportFieldPolicyDiagnostic>();
}
public sealed record CCategory_Xlsx_Row(int RowNumber, bool IsHidden, IReadOnlyList<CCategory_Xlsx_Cell> Cells);
public sealed record CCategory_Xlsx_Cell(int ColumnNumber, string CellType, object? Value, string? Formula, string? NumberFormat);
