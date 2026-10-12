namespace TKS_Thuc_Tap_V11_Data_Access.Utility;

public sealed class CUnit_Xlsx_Reader
{
    public async Task<CUnit_Xlsx_Worksheet> ReadAsync(
        Stream p_objSource,
        CancellationToken p_objCancellationToken = default)
    {
        var v_objSheet = await new CProfiledWorkbook_Xlsx_Reader()
            .ReadSingleSheetAsync(p_objSource, "Unit", p_objCancellationToken);
        return CImportLegacyWorksheetAdapter.ToUnit(v_objSheet);
    }
}

public sealed record CUnit_Xlsx_Worksheet(string Name, IReadOnlyList<CUnit_Xlsx_Row> Rows)
{
    public IReadOnlyList<string> MappedColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> IgnoredColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnsupportedColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<CImportCapabilityIssue> UnsupportedCapabilities { get; init; } = Array.Empty<CImportCapabilityIssue>();
    public IReadOnlyList<CImportFieldPolicyDiagnostic> PolicyDiagnostics { get; init; } = Array.Empty<CImportFieldPolicyDiagnostic>();
}

public sealed record CUnit_Xlsx_Row(
    int RowNumber,
    bool IsHidden,
    IReadOnlyList<CUnit_Xlsx_Cell> Cells);

public sealed record CUnit_Xlsx_Cell(
    int ColumnNumber,
    string CellType,
    object? Value,
    string? Formula,
    string? NumberFormat);
