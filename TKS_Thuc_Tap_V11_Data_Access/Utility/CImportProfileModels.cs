using System.Text;

namespace TKS_Thuc_Tap_V11_Data_Access.Utility;

public enum EImportWorkbookStructure
{
    SingleTable,
    HeaderDetail
}

public enum EImportColumnDisposition
{
    Mapped,
    Ignored,
    Unsupported
}

public enum EImportFieldPolicy
{
    Map,
    Required,
    SafeIgnore,
    BlockIfNonEmpty,
    IgnoreSourceOnly
}

public sealed class CImportProfile
{
    public string Code { get; set; } = "";
    public EImportWorkbookStructure Structure { get; set; }
    public int MaxFileBytes { get; set; } = 1_048_576;
    public int MaxRows { get; set; } = 100;
    public int MaxWorksheets { get; set; } = 5;
    public int MaxColumns { get; set; } = 100;
    public List<CImportSheetProfile> Sheets { get; set; } = new();
}

public sealed class CImportProfileConfiguration
{
    public List<CImportReferenceMap> ReferenceMaps { get; set; } = new();
    public List<CImportProfile> Profiles { get; set; } = new();
}

public sealed class CImportReferenceMap
{
    public string Name { get; set; } = "";
    public string EntityType { get; set; } = "";
    public List<CImportValueMappingDefinition> Mappings { get; set; } = new();
}

public sealed record CImportValueMappingDefinition(string SourceValue, string TargetValue);

public sealed class CImportSheetProfile
{
    public string Role { get; set; } = "";
    public bool Required { get; set; } = true;
    public List<string> NameHints { get; set; } = new();
    public List<CImportFieldProfile> Fields { get; set; } = new();
    public List<CImportSafeIgnoreHeader> SafeIgnoreHeaders { get; set; } = new();
    public List<string> UnsupportedHeaders { get; set; } = new();
}

public sealed record CImportSafeIgnoreHeader(string Header, string Reason);

public sealed class CImportFieldProfile
{
    public string Field { get; set; } = "";
    public bool Required { get; set; }
    public List<string> Aliases { get; set; } = new();
    public string? ReferenceMap { get; set; }
    public string? ReferenceEntityType { get; set; }
    public string? MapToField { get; set; }
    public EImportFieldPolicy? Policy { get; set; }
    public string? PolicyReason { get; set; }
    public string? UnsupportedReason { get; set; }
    public List<string> SafeValues { get; set; } = new();
    public string? SafeValueReason { get; set; }
    public string? SafeWhenEqualToField { get; set; }
}

public sealed record CImportValueMappingResult(string SourceValue, string MappedValue, bool WasMapped);

public sealed record CImportValueMappingDiagnostic(
    int? RowNumber,
    string SheetName,
    string CanonicalField,
    string SourceValue,
    string MappedValue,
    string Status,
    long? ResolvedId = null);

public sealed record CImportFieldPolicyDiagnostic(
    int? RowNumber,
    string SheetName,
    string SheetRole,
    string SourceColumn,
    string CanonicalField,
    string SourceValue,
    string Policy,
    string Status,
    string Reason,
    string? MappedValue = null);

public sealed record CImportCapabilityIssue(
    int? RowNumber,
    string SheetName,
    string SheetRole,
    string SourceColumn,
    string CanonicalField,
    string SourceValue,
    string ErrorCode,
    string Message);

public sealed record CImportColumnMapping(
    int ColumnNumber,
    string Header,
    string? CanonicalField,
    EImportColumnDisposition Disposition);

public sealed record CImportDetectedRow(
    int RowNumber,
    bool IsHidden,
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlySet<string> FormulaFields,
    IReadOnlyDictionary<string, string?> Formulas,
    IReadOnlyDictionary<string, string> CellTypes,
    IReadOnlyDictionary<string, string?> NumberFormats);

public sealed record CImportDetectedSheet(
    string Name,
    string Role,
    int HeaderRowNumber,
    IReadOnlyList<CImportColumnMapping> Columns,
    IReadOnlyList<CImportDetectedRow> DataRows)
{
    public IReadOnlyList<CImportCapabilityIssue> CapabilityIssues { get; init; } = Array.Empty<CImportCapabilityIssue>();
    public IReadOnlyList<CImportFieldPolicyDiagnostic> PolicyDiagnostics { get; init; } = Array.Empty<CImportFieldPolicyDiagnostic>();
    public IReadOnlyList<string> MappedColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> IgnoredColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnsupportedColumns { get; init; } = Array.Empty<string>();
}

public sealed class CWorkbookStructureDetectionResult
{
    public List<CImportDetectedSheet> SheetMappings { get; } = new();
    public List<CImportStructureError> Errors { get; } = new();
    public string? ErrorCode => Errors.FirstOrDefault()?.Code;
    public bool IsValid => Errors.Count == 0 && SheetMappings.Count > 0;

    internal void AddError(string p_strCode, string p_strMessage, string? p_strSheet = null, int? p_iRow = null) =>
        Errors.Add(new CImportStructureError(p_strCode, p_strMessage, p_strSheet, p_iRow));
}

public sealed record CImportStructureError(string Code, string Message, string? SheetName, int? RowNumber);

public static class CImportHeaderNormalizer
{
    public static string Normalize(string? p_strHeader)
    {
        if (string.IsNullOrWhiteSpace(p_strHeader))
            return "";

        var v_strSource = p_strHeader.Normalize(NormalizationForm.FormC);
        var v_objBuilder = new StringBuilder(v_strSource.Length);
        var v_bPendingSpace = false;
        foreach (var v_ch in v_strSource.Trim())
        {
            if (char.IsWhiteSpace(v_ch) || v_ch is '_' or '-')
            {
                v_bPendingSpace = v_objBuilder.Length > 0;
                continue;
            }

            if (v_bPendingSpace)
                v_objBuilder.Append(' ');
            v_objBuilder.Append(char.ToUpperInvariant(v_ch));
            v_bPendingSpace = false;
        }

        return v_objBuilder.ToString();
    }
}

public static class CImportBusinessValueComparer
{
    public static bool AreEqual(string? p_strLeft, string? p_strRight) =>
        string.Equals(Normalize(p_strLeft), Normalize(p_strRight), StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? p_strValue)
    {
        if (string.IsNullOrWhiteSpace(p_strValue))
            return "";

        var v_strSource = p_strValue.Normalize(NormalizationForm.FormC);
        var v_objBuilder = new StringBuilder(v_strSource.Length);
        var v_bPendingSpace = false;
        foreach (var v_ch in v_strSource.Trim())
        {
            if (char.IsWhiteSpace(v_ch))
            {
                v_bPendingSpace = v_objBuilder.Length > 0;
                continue;
            }

            if (v_bPendingSpace)
                v_objBuilder.Append(' ');
            v_objBuilder.Append(v_ch);
            v_bPendingSpace = false;
        }

        return v_objBuilder.ToString();
    }
}
