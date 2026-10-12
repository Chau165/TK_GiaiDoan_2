namespace TKS_Thuc_Tap_V11_Data_Access.Utility;

public sealed class CProduct_Xlsx_Reader
{
    private readonly CProfiledWorkbook_Xlsx_Reader m_objProfiledReader = new();

    public async Task<CProduct_Xlsx_Worksheet> ReadAsync(
        Stream p_objSource,
        CancellationToken p_objCancellationToken = default)
    {
        var v_objSheet = await m_objProfiledReader.ReadSingleSheetAsync(
            p_objSource,
            "Product",
            p_objCancellationToken);
        return CImportLegacyWorksheetAdapter.ToProduct(v_objSheet);
    }
}

public sealed record CProduct_Xlsx_Worksheet(string Name, IReadOnlyList<CProduct_Xlsx_Row> Rows)
{
    public IReadOnlyList<string> MappedColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> IgnoredColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnsupportedColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<CImportCapabilityIssue> UnsupportedCapabilities { get; init; } = Array.Empty<CImportCapabilityIssue>();
    public IReadOnlyList<CImportFieldPolicyDiagnostic> PolicyDiagnostics { get; init; } = Array.Empty<CImportFieldPolicyDiagnostic>();
}

public sealed record CProduct_Xlsx_Row(
    int RowNumber,
    bool IsHidden,
    IReadOnlyList<CProduct_Xlsx_Cell> Cells);

public sealed record CProduct_Xlsx_Cell(
    int ColumnNumber,
    string CellType,
    object? Value,
    string? Formula,
    string? NumberFormat);
