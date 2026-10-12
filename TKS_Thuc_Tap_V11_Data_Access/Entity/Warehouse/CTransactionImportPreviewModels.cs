using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

public sealed class CTransactionImportReferences
{
    public IReadOnlyList<CWarehouseMaster>? Warehouses { get; init; }
    public IReadOnlyList<CWarehouseMaster>? Suppliers { get; init; }
    public IReadOnlyList<CWarehouseMaster>? Products { get; init; }
    public IReadOnlyList<CWarehouseMaster>? Units { get; init; }
    public IReadOnlyList<CTransactionImportInventoryBalance>? InventoryBalances { get; init; }
    public IReadOnlyList<CTransactionImportExistingDocument>? ExistingDocuments { get; init; }
}

public sealed record CTransactionImportExistingDocument(
    CWarehouseDocument Header,
    IReadOnlyList<CWarehouseDocumentDetail> Lines);

public sealed record CTransactionImportInventoryBalance(
    long Warehouse_ID,
    long Product_ID,
    decimal Current_Quantity,
    decimal Reserved_Quantity)
{
    public decimal Available_Quantity => Current_Quantity - Reserved_Quantity;
}

public sealed class CTransactionImportPreviewResult
{
    public string Profile_Code { get; init; } = "";
    public bool StructureValid { get; init; }
    public bool ReferenceDataLoaded { get; init; }
    public List<CTransactionImportPreviewDocument> Documents { get; } = new();
    public List<CTransactionImportIssue> Errors { get; } = new();
    public List<string> Mapped_Columns { get; } = new();
    public List<string> Ignored_Columns { get; } = new();
    public List<string> Unsupported_Columns { get; } = new();
    public List<CImportFieldPolicyDiagnostic> Field_Policy_Diagnostics { get; } = new();
    public List<CImportValueMappingDiagnostic> Value_Mappings { get; } = new();
    public bool PersistenceBlocked => false;
    public bool Can_Confirm => StructureValid && ReferenceDataLoaded && !PersistenceBlocked &&
        Documents.Count > 0 && Errors.Count == 0;
}

public sealed class CTransactionImportPreviewDocument
{
    public string DocumentNumber { get; init; } = "";
    public DateTime? DocumentDate { get; init; }
    public string WarehouseCode { get; init; } = "";
    public string WarehouseName { get; set; } = "";
    public string SupplierCode { get; init; } = "";
    public string SupplierName { get; init; } = "";
    public string Note { get; init; } = "";
    public string SheetName { get; init; } = "";
    public int RowNumber { get; init; }
    public long Warehouse_ID { get; set; }
    public long Supplier_ID { get; set; }
    public string Import_Status { get; set; } = "READY";
    public List<CTransactionImportPreviewLine> Lines { get; } = new();
}

public sealed class CTransactionImportPreviewLine
{
    public string DocumentNumber { get; init; } = "";
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string UnitReference { get; set; } = "";
    public decimal? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public string SheetName { get; init; } = "";
    public int RowNumber { get; init; }
    public long Product_ID { get; set; }
    public long Unit_ID { get; set; }
}

public sealed record CTransactionImportIssue(
    int? RowNumber,
    string SheetName,
    string SheetRole,
    string SourceColumn,
    string CanonicalField,
    string Error_Code,
    string Message,
    string DocumentNumber = "");
