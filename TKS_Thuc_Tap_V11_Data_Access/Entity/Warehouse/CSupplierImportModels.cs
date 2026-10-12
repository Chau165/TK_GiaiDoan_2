using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

public sealed class CSupplierImportRow
{
    public int Row_Number { get; set; }
    public string Ma_NCC { get; set; } = "";
    public string Ten_NCC { get; set; } = "";
    public string Ghi_Chu { get; set; } = "";
    public bool Is_Skipped { get; set; }
    public List<CSupplierImportError> Errors { get; } = new();
    public bool Is_Valid => !Is_Skipped && Errors.Count == 0;
}

public sealed record CSupplierImportError(int Row_Number, string Column_Name, string Message);

public sealed class CSupplierImportPreviewResult
{
    public List<CSupplierImportRow> Rows { get; } = new();
    public List<CSupplierImportError> Errors { get; } = new();
    public List<string> Unsupported_Columns { get; } = new();
    public List<string> Mapped_Columns { get; } = new();
    public List<string> Ignored_Columns { get; } = new();
    public List<CImportCapabilityIssue> Unsupported_Capabilities { get; } = new();
    public List<CImportFieldPolicyDiagnostic> Field_Policy_Diagnostics { get; } = new();
    public int Total_Rows => Rows.Count;
    public int Valid_Rows => Rows.Count(v_objRow => v_objRow.Is_Valid);
    public int Skipped_Rows => Rows.Count(v_objRow => v_objRow.Is_Skipped);
    public int Invalid_Rows => Rows.Count(v_objRow => !v_objRow.Is_Skipped && v_objRow.Errors.Count > 0);
    public bool Can_Confirm => Valid_Rows > 0 && Invalid_Rows == 0 && Errors.Count == 0 &&
        Unsupported_Columns.Count == 0 && Unsupported_Capabilities.Count == 0;
}
