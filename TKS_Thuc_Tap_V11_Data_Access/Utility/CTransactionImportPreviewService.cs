using System.Globalization;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Utility;

public sealed class CTransactionImportPreviewService
{
    private readonly CImportProfileProvider m_objProfiles;
    private readonly CImportValueMapper m_objValueMapper;
    private readonly CProfiledWorkbook_Xlsx_Reader m_objReader;
    private readonly CWorkbookStructureDetector m_objDetector;

    public CTransactionImportPreviewService()
        : this(new CImportProfileProvider())
    {
    }

    public CTransactionImportPreviewService(CImportProfileProvider p_objProfiles)
    {
        ArgumentNullException.ThrowIfNull(p_objProfiles);
        m_objProfiles = p_objProfiles;
        m_objValueMapper = new CImportValueMapper(p_objProfiles);
        m_objReader = new CProfiledWorkbook_Xlsx_Reader(p_objProfiles);
        m_objDetector = new CWorkbookStructureDetector(p_objProfiles);
    }

    public async Task<CTransactionImportPreviewResult> PreviewAsync(
        Stream p_objSource,
        string p_strProfileCode,
        CTransactionImportReferences? p_objReferences = null,
        CancellationToken p_objCancellationToken = default)
    {
        var v_objDetection = await m_objReader.ReadAsync(p_objSource, p_strProfileCode, p_objCancellationToken);
        return Preview(v_objDetection, p_strProfileCode, p_objReferences);
    }

    public CTransactionImportPreviewResult Preview(
        CWorkbook_Xlsx_Workbook p_objWorkbook,
        string p_strProfileCode,
        CTransactionImportReferences? p_objReferences = null)
    {
        ArgumentNullException.ThrowIfNull(p_objWorkbook);
        var v_objProfile = m_objProfiles.GetByCode(p_strProfileCode);
        var v_objDetection = m_objDetector.Detect(p_objWorkbook, v_objProfile);
        return Preview(v_objDetection, p_strProfileCode, p_objReferences);
    }

    private CTransactionImportPreviewResult Preview(
        CWorkbookStructureDetectionResult p_objDetection,
        string p_strProfileCode,
        CTransactionImportReferences? p_objReferences)
    {
        var v_objProfile = m_objProfiles.GetByCode(p_strProfileCode);
        if (v_objProfile.Structure != EImportWorkbookStructure.HeaderDetail)
            throw new ArgumentException($"Profile '{p_strProfileCode}' is not a header/detail transaction profile.", nameof(p_strProfileCode));

        var v_objResult = new CTransactionImportPreviewResult
        {
            Profile_Code = v_objProfile.Code,
            StructureValid = p_objDetection.IsValid,
            ReferenceDataLoaded = p_objReferences?.Warehouses is not null &&
                p_objReferences.Products is not null &&
                p_objReferences.Units is not null &&
                p_objReferences.ExistingDocuments is not null &&
                (v_objProfile.Code != "Outbound" || p_objReferences.InventoryBalances is not null) &&
                (v_objProfile.Code != "Inbound" || p_objReferences.Suppliers is not null)
        };
        foreach (var v_objStructureError in p_objDetection.Errors)
        {
            v_objResult.Errors.Add(new CTransactionImportIssue(
                v_objStructureError.RowNumber,
                v_objStructureError.SheetName ?? "",
                "",
                "",
                "",
                v_objStructureError.Code,
                v_objStructureError.Message));
        }
        if (!p_objDetection.IsValid)
            return v_objResult;

        var v_arrSheets = p_objDetection.SheetMappings;
        var v_objHeaderSheet = v_arrSheets.Single(v_objSheet => string.Equals(v_objSheet.Role, "Header", StringComparison.OrdinalIgnoreCase));
        var v_objDetailSheet = v_arrSheets.Single(v_objSheet => string.Equals(v_objSheet.Role, "Detail", StringComparison.OrdinalIgnoreCase));
        v_objResult.Mapped_Columns.AddRange(v_arrSheets.SelectMany(v_objSheet => v_objSheet.MappedColumns).Distinct(StringComparer.OrdinalIgnoreCase));
        v_objResult.Ignored_Columns.AddRange(v_arrSheets.SelectMany(v_objSheet => v_objSheet.IgnoredColumns).Distinct(StringComparer.OrdinalIgnoreCase));
        v_objResult.Unsupported_Columns.AddRange(v_arrSheets.SelectMany(v_objSheet => v_objSheet.UnsupportedColumns).Distinct(StringComparer.OrdinalIgnoreCase));
        v_objResult.Field_Policy_Diagnostics.AddRange(v_arrSheets.SelectMany(v_objSheet => v_objSheet.PolicyDiagnostics));
        foreach (var v_objSheet in v_arrSheets)
        {
            foreach (var v_objCapability in v_objSheet.CapabilityIssues)
            {
                v_objResult.Errors.Add(new CTransactionImportIssue(
                    v_objCapability.RowNumber,
                    v_objCapability.SheetName,
                    v_objCapability.SheetRole,
                    v_objCapability.SourceColumn,
                    v_objCapability.CanonicalField,
                    v_objCapability.ErrorCode,
                    v_objCapability.Message));
            }
        }

        var v_dicDocuments = new Dictionary<string, CTransactionImportPreviewDocument>(StringComparer.Ordinal);
        var v_dicHeaderRows = new Dictionary<string, CImportDetectedRow>(StringComparer.Ordinal);
        foreach (var v_objRow in v_objHeaderSheet.DataRows)
        {
            if (IsBlankRow(v_objRow, v_objHeaderSheet))
                continue;

            var v_strDocumentNumber = ReadString(v_objResult, v_objHeaderSheet, v_objRow, "DocumentNumber", required: true);
            if (v_strDocumentNumber.Length == 0)
                continue;

            var v_strDocumentKey = ComparisonKey(v_strDocumentNumber);
            if (v_dicDocuments.ContainsKey(v_strDocumentKey))
            {
                AddIssue(v_objResult, v_objHeaderSheet, v_objRow, "DocumentNumber", "DUPLICATE_KEY_CONFLICT",
                    $"Số chứng từ '{v_strDocumentNumber}' xuất hiện nhiều lần trong sheet header.");
                continue;
            }

            var v_objDocument = new CTransactionImportPreviewDocument
            {
                DocumentNumber = v_strDocumentNumber,
                DocumentDate = ReadDate(v_objResult, v_objHeaderSheet, v_objRow, "DocumentDate"),
                WarehouseCode = ReadString(v_objResult, v_objHeaderSheet, v_objRow, "WarehouseCode", required: true),
                WarehouseName = ReadString(v_objResult, v_objHeaderSheet, v_objRow, "WarehouseName", required: false),
                SupplierCode = ReadString(v_objResult, v_objHeaderSheet, v_objRow, "SupplierCode", required: v_objProfile.Code == "Inbound"),
                SupplierName = ReadString(v_objResult, v_objHeaderSheet, v_objRow, "SupplierName", required: false),
                Note = ReadString(v_objResult, v_objHeaderSheet, v_objRow, "Note", required: false),
                SheetName = v_objHeaderSheet.Name,
                RowNumber = v_objRow.RowNumber
            };
            v_dicDocuments.Add(v_strDocumentKey, v_objDocument);
            v_dicHeaderRows.Add(v_strDocumentKey, v_objRow);
            v_objResult.Documents.Add(v_objDocument);
        }

        foreach (var v_objRow in v_objDetailSheet.DataRows)
        {
            if (IsBlankRow(v_objRow, v_objDetailSheet))
                continue;

            var v_strDocumentNumber = ReadString(v_objResult, v_objDetailSheet, v_objRow, "DocumentNumber", required: true);
            var v_strProductCode = ReadString(v_objResult, v_objDetailSheet, v_objRow, "ProductCode", required: true);
            var v_decQuantity = ReadQuantity(v_objResult, v_objDetailSheet, v_objRow);
            var v_objLine = new CTransactionImportPreviewLine
            {
                DocumentNumber = v_strDocumentNumber,
                ProductCode = v_strProductCode,
                ProductName = ReadString(v_objResult, v_objDetailSheet, v_objRow, "ProductName", required: false),
                UnitReference = ReadString(v_objResult, v_objDetailSheet, v_objRow, "UnitReference", required: false),
                Quantity = v_decQuantity,
                UnitPrice = ReadUnitPrice(v_objResult, v_objDetailSheet, v_objRow),
                SheetName = v_objDetailSheet.Name,
                RowNumber = v_objRow.RowNumber
            };

            CTransactionImportPreviewDocument? v_objDocument = null;
            if (v_strDocumentNumber.Length > 0 && !v_dicDocuments.TryGetValue(ComparisonKey(v_strDocumentNumber), out v_objDocument))
                AddIssue(v_objResult, v_objDetailSheet, v_objRow, "DocumentNumber", "ORPHAN_DETAIL",
                    $"Chi tiết '{v_strDocumentNumber}' không có chứng từ header tương ứng.");

            if (v_objDocument is not null)
                v_objDocument.Lines.Add(v_objLine);

            ResolveProductReference(v_objResult, v_objDetailSheet, v_objRow, v_objLine, p_objReferences?.Products, p_objReferences?.Units);
        }

        foreach (var v_objDocument in v_objResult.Documents)
        {
            if (v_objDocument.Lines.Count == 0)
            {
                var v_objRow = v_dicHeaderRows[ComparisonKey(v_objDocument.DocumentNumber)];
                AddIssue(v_objResult, v_objHeaderSheet, v_objRow, "DocumentNumber", "MISSING_DETAIL",
                    $"Chứng từ '{v_objDocument.DocumentNumber}' chưa có dòng chi tiết.");
            }

            var v_objHeaderRow = v_dicHeaderRows[ComparisonKey(v_objDocument.DocumentNumber)];
            ResolveWarehouseReference(v_objResult, v_objHeaderSheet, v_objHeaderRow, v_objDocument, p_objReferences?.Warehouses, p_strProfileCode);
            if (v_objProfile.Code == "Inbound")
                ResolveSupplierReference(v_objResult, v_objHeaderSheet, v_objHeaderRow, v_objDocument, p_objReferences?.Suppliers);
        }

        if (v_objProfile.Code == "Outbound" && p_objReferences?.InventoryBalances is not null)
            ValidateOutboundStock(v_objResult, p_objReferences.InventoryBalances);

        if (p_objReferences?.ExistingDocuments is not null)
            ValidateExistingDocuments(v_objResult, p_objReferences.ExistingDocuments, v_objProfile.Code == "Inbound");

        return v_objResult;
    }

    private static void ValidateExistingDocuments(
        CTransactionImportPreviewResult p_objResult,
        IReadOnlyList<CTransactionImportExistingDocument> p_arrExistingDocuments,
        bool p_bIsReceipt)
    {
        foreach (var v_objDocument in p_objResult.Documents)
        {
            var v_strDocumentKey = ComparisonKey(v_objDocument.DocumentNumber);
            if (p_objResult.Errors.Any(v_objError =>
                    ComparisonKey(v_objError.DocumentNumber) == v_strDocumentKey) ||
                v_objDocument.DocumentDate is null || v_objDocument.Warehouse_ID <= 0 ||
                (p_bIsReceipt && v_objDocument.Supplier_ID <= 0) ||
                v_objDocument.Lines.Any(v_objLine => v_objLine.Product_ID <= 0 ||
                    v_objLine.Quantity is null or <= 0 || v_objLine.UnitPrice is null or <= 0))
                continue;

            var v_arrMatches = p_arrExistingDocuments
                .Where(v_objExisting => ComparisonKey(v_objExisting.So_Phieu) == v_strDocumentKey)
                .ToArray();
            if (v_arrMatches.Length == 0)
                continue;

            var v_bIsIdentical = v_arrMatches.Length == 1 &&
                ExistingDocumentMatches(v_objDocument, v_arrMatches[0], p_bIsReceipt);
            if (v_bIsIdentical)
            {
                v_objDocument.Import_Status = "SKIPPED_DUPLICATE_DOCUMENT";
                continue;
            }

            v_objDocument.Import_Status = "DUPLICATE_DOCUMENT_CONFLICT";
            p_objResult.Errors.Add(new CTransactionImportIssue(
                v_objDocument.RowNumber,
                v_objDocument.SheetName,
                "Header",
                "",
                "DocumentNumber",
                "DUPLICATE_DOCUMENT_CONFLICT",
                $"Số chứng từ '{v_objDocument.DocumentNumber}' đã tồn tại nhưng dữ liệu Header/Detail khác; không ghi đè chứng từ hiện có.",
                v_objDocument.DocumentNumber));
        }
    }

    private static bool ExistingDocumentMatches(
        CTransactionImportPreviewDocument p_objDocument,
        CTransactionImportExistingDocument p_objExisting,
        bool p_bIsReceipt)
    {
        var v_objHeader = p_objExisting;
        if (v_objHeader.Is_Receipt != p_bIsReceipt ||
            v_objHeader.Kho_ID != p_objDocument.Warehouse_ID ||
            (p_bIsReceipt && v_objHeader.NCC_ID != p_objDocument.Supplier_ID) ||
            v_objHeader.Ngay_Chung_Tu.Date != p_objDocument.DocumentDate!.Value.Date ||
            !CImportBusinessValueComparer.AreEqual(v_objHeader.Ghi_Chu, p_objDocument.Note) ||
            v_objHeader.So_Phieu.Length == 0 ||
            p_objExisting.Lines.Count != p_objDocument.Lines.Count)
            return false;

        var v_arrImportedLines = p_objDocument.Lines
            .Select(v_objLine => (ProductId: v_objLine.Product_ID,
                Quantity: v_objLine.Quantity!.Value,
                UnitPrice: v_objLine.UnitPrice!.Value))
            .OrderBy(v_objLine => v_objLine.ProductId)
            .ThenBy(v_objLine => v_objLine.Quantity)
            .ThenBy(v_objLine => v_objLine.UnitPrice)
            .ToArray();
        var v_arrExistingLines = p_objExisting.Lines
            .Select(v_objLine => (ProductId: v_objLine.San_Pham_ID,
                Quantity: v_objLine.So_Luong,
                UnitPrice: v_objLine.Don_Gia))
            .OrderBy(v_objLine => v_objLine.ProductId)
            .ThenBy(v_objLine => v_objLine.Quantity)
            .ThenBy(v_objLine => v_objLine.UnitPrice)
            .ToArray();
        return v_arrImportedLines.SequenceEqual(v_arrExistingLines);
    }

    private static void ValidateOutboundStock(
        CTransactionImportPreviewResult p_objResult,
        IReadOnlyList<CTransactionImportInventoryBalance> p_arrBalances)
    {
        var v_dicBalances = p_arrBalances
            .GroupBy(v_objBalance => (v_objBalance.Warehouse_ID, v_objBalance.Product_ID))
            .ToDictionary(v_objGroup => v_objGroup.Key, v_objGroup => v_objGroup.Sum(v_objBalance => v_objBalance.Available_Quantity));
        foreach (var v_objGroup in p_objResult.Documents
            .SelectMany(v_objDocument => v_objDocument.Lines.Select(v_objLine => (Document: v_objDocument, Line: v_objLine)))
            .Where(v_objItem => v_objItem.Document.Warehouse_ID > 0 && v_objItem.Line.Product_ID > 0 && v_objItem.Line.Quantity > 0)
            .GroupBy(v_objItem => (v_objItem.Document.Warehouse_ID, v_objItem.Line.Product_ID)))
        {
            var v_decAvailable = v_dicBalances.GetValueOrDefault(v_objGroup.Key, 0m);
            var v_decRequired = v_objGroup.Sum(v_objItem => v_objItem.Line.Quantity!.Value);
            if (v_decRequired <= v_decAvailable)
                continue;

            foreach (var v_objItem in v_objGroup)
                p_objResult.Errors.Add(new CTransactionImportIssue(
                    v_objItem.Line.RowNumber,
                    v_objItem.Line.SheetName,
                    "Detail",
                    "",
                    "Quantity",
                    "INSUFFICIENT_STOCK",
                    $"Tổng lượng xuất {v_decRequired} vượt khả dụng {v_decAvailable} tại kho/sản phẩm này.",
                    v_objItem.Document.DocumentNumber));
        }
    }

    private void ResolveWarehouseReference(
        CTransactionImportPreviewResult p_objResult,
        CImportDetectedSheet p_objSheet,
        CImportDetectedRow p_objRow,
        CTransactionImportPreviewDocument p_objDocument,
        IReadOnlyList<CWarehouseMaster>? p_arrWarehouses,
        string p_strProfileCode)
    {
        if (p_objDocument.WarehouseCode.Length == 0)
            return;

        var v_objMapping = m_objValueMapper.Map(p_strProfileCode, "WarehouseCode", p_objDocument.WarehouseCode);
        if (p_arrWarehouses is null)
        {
            AddMappingDiagnostic(p_objResult, p_objSheet, p_objRow, "WarehouseCode", v_objMapping, "MAPPED_NOT_RESOLVED");
            return;
        }

        var v_arrMatches = p_arrWarehouses
            .Where(v_objWarehouse => ComparisonKey(v_objWarehouse.Code) == ComparisonKey(v_objMapping.MappedValue) ||
                ComparisonKey(v_objWarehouse.Name) == ComparisonKey(v_objMapping.MappedValue))
            .GroupBy(v_objWarehouse => v_objWarehouse.Auto_ID)
            .Select(v_objGroup => v_objGroup.First())
            .ToArray();
        if (v_arrMatches.Length == 1)
        {
            p_objDocument.Warehouse_ID = v_arrMatches[0].Auto_ID;
            var v_strSourceWarehouseName = p_objDocument.WarehouseName;
            if (v_strSourceWarehouseName.Length > 0 &&
                !CImportBusinessValueComparer.AreEqual(v_strSourceWarehouseName, v_arrMatches[0].Name))
                AddIssue(p_objResult, p_objSheet, p_objRow, "WarehouseName", "WAREHOUSE_NAME_MISMATCH",
                    $"Tên kho '{v_strSourceWarehouseName}' không khớp với kho được resolve '{v_arrMatches[0].Name}'.");
            p_objDocument.WarehouseName = v_arrMatches[0].Name;
            AddMappingDiagnostic(p_objResult, p_objSheet, p_objRow, "WarehouseCode", v_objMapping, "RESOLVED", v_arrMatches[0].Auto_ID);
        }
        else
        {
            AddIssue(p_objResult, p_objSheet, p_objRow, "WarehouseCode",
                v_arrMatches.Length == 0 ? "REFERENCE_NOT_FOUND" : "AMBIGUOUS_REFERENCE",
                $"Không xác định duy nhất được mã kho '{p_objDocument.WarehouseCode}'.");
            AddMappingDiagnostic(p_objResult, p_objSheet, p_objRow, "WarehouseCode", v_objMapping,
                v_arrMatches.Length == 0 ? "REFERENCE_NOT_FOUND" : "AMBIGUOUS_REFERENCE");
        }
    }

    private static void ResolveSupplierReference(
        CTransactionImportPreviewResult p_objResult,
        CImportDetectedSheet p_objSheet,
        CImportDetectedRow p_objRow,
        CTransactionImportPreviewDocument p_objDocument,
        IReadOnlyList<CWarehouseMaster>? p_arrSuppliers)
    {
        if (p_arrSuppliers is null || p_objDocument.SupplierCode.Length == 0)
            return;
        var v_arrMatches = p_arrSuppliers
            .Where(v_objSupplier => ComparisonKey(v_objSupplier.Code) == ComparisonKey(p_objDocument.SupplierCode))
            .ToArray();
        if (v_arrMatches.Length == 1)
        {
            p_objDocument.Supplier_ID = v_arrMatches[0].Auto_ID;
            if (p_objDocument.SupplierName.Length > 0 &&
                !CImportBusinessValueComparer.AreEqual(p_objDocument.SupplierName, v_arrMatches[0].Name))
                AddIssue(p_objResult, p_objSheet, p_objRow, "SupplierName", "SUPPLIER_NAME_MISMATCH",
                    $"Tên nhà cung cấp '{p_objDocument.SupplierName}' không khớp mã '{p_objDocument.SupplierCode}'.");
        }
        else
            AddIssue(p_objResult, p_objSheet, p_objRow, "SupplierCode",
                v_arrMatches.Length == 0 ? "REFERENCE_NOT_FOUND" : "AMBIGUOUS_REFERENCE",
                $"Không xác định duy nhất được mã nhà cung cấp '{p_objDocument.SupplierCode}'.");
    }

    private void ResolveProductReference(
        CTransactionImportPreviewResult p_objResult,
        CImportDetectedSheet p_objSheet,
        CImportDetectedRow p_objRow,
        CTransactionImportPreviewLine p_objLine,
        IReadOnlyList<CWarehouseMaster>? p_arrProducts,
        IReadOnlyList<CWarehouseMaster>? p_arrUnits)
    {
        if (p_objLine.ProductCode.Length == 0)
            return;
        var v_arrMatches = p_arrProducts?
            .Where(v_objProduct => ComparisonKey(v_objProduct.Code) == ComparisonKey(p_objLine.ProductCode))
            .ToArray();
        if (v_arrMatches?.Length == 1)
        {
            var v_objProduct = v_arrMatches[0];
            p_objLine.Product_ID = v_objProduct.Auto_ID;
            p_objLine.Unit_ID = v_objProduct.Related_ID_2;
            if (p_objLine.ProductName.Length > 0 &&
                !CImportBusinessValueComparer.AreEqual(p_objLine.ProductName, v_objProduct.Name))
                AddIssue(p_objResult, p_objSheet, p_objRow, "ProductName", "PRODUCT_NAME_MISMATCH",
                    $"Tên sản phẩm '{p_objLine.ProductName}' không khớp mã '{p_objLine.ProductCode}' (danh mục: '{v_objProduct.Name}').");

            if (p_objLine.Unit_ID <= 0)
                AddIssue(p_objResult, p_objSheet, p_objRow, "UnitReference", "PRODUCT_UNIT_NOT_CONFIGURED",
                    $"Sản phẩm '{p_objLine.ProductCode}' chưa được cấu hình đơn vị tính trong danh mục sản phẩm.");
            else if (p_arrUnits is not null)
            {
                var v_objUnit = p_arrUnits.FirstOrDefault(v_objItem => v_objItem.Auto_ID == p_objLine.Unit_ID);
                if (v_objUnit is null)
                    AddIssue(p_objResult, p_objSheet, p_objRow, "UnitReference", "PRODUCT_UNIT_NOT_FOUND",
                        $"Không tìm thấy đơn vị ID {p_objLine.Unit_ID} được cấu hình cho sản phẩm '{p_objLine.ProductCode}'.");
                else
                    p_objLine.UnitReference = v_objUnit.Name;
            }
        }
        else if (p_arrProducts is not null)
        {
            AddIssue(p_objResult, p_objSheet, p_objRow, "ProductCode",
                v_arrMatches?.Length == 0 ? "REFERENCE_NOT_FOUND" : "AMBIGUOUS_REFERENCE",
                $"Không xác định duy nhất được mã sản phẩm '{p_objLine.ProductCode}'.");
        }

    }

    private static void AddMappingDiagnostic(
        CTransactionImportPreviewResult p_objResult,
        CImportDetectedSheet p_objSheet,
        CImportDetectedRow p_objRow,
        string p_strField,
        CImportValueMappingResult p_objMapping,
        string p_strResolvedStatus,
        long? p_lResolvedId = null)
    {
        if (!p_objMapping.WasMapped)
            return;
        p_objResult.Value_Mappings.Add(new CImportValueMappingDiagnostic(
            p_objRow.RowNumber,
            p_objSheet.Name,
            p_strField,
            p_objMapping.SourceValue,
            p_objMapping.MappedValue,
            p_strResolvedStatus,
            p_lResolvedId));
    }

    private static string ReadString(
        CTransactionImportPreviewResult p_objResult,
        CImportDetectedSheet p_objSheet,
        CImportDetectedRow p_objRow,
        string p_strField,
        bool required)
    {
        if (p_objRow.FormulaFields.Contains(p_strField))
        {
            AddIssue(p_objResult, p_objSheet, p_objRow, p_strField, "FORMULA_NOT_SUPPORTED",
                $"Cột '{SourceColumn(p_objSheet, p_strField)}' không được chứa công thức Excel.");
            return "";
        }

        var v_strValue = p_objRow.Values.TryGetValue(p_strField, out var v_objValue)
            ? CImportBusinessValueComparer.Normalize(Convert.ToString(v_objValue, CultureInfo.InvariantCulture))
            : "";
        if (required && v_strValue.Length == 0)
            AddIssue(p_objResult, p_objSheet, p_objRow, p_strField, "MISSING_REQUIRED_VALUE",
                $"Thiếu giá trị bắt buộc '{SourceColumn(p_objSheet, p_strField)}'.");
        return v_strValue;
    }

    private static DateTime? ReadDate(
        CTransactionImportPreviewResult p_objResult,
        CImportDetectedSheet p_objSheet,
        CImportDetectedRow p_objRow,
        string p_strField)
    {
        if (p_objRow.FormulaFields.Contains(p_strField))
        {
            AddIssue(p_objResult, p_objSheet, p_objRow, p_strField, "FORMULA_NOT_SUPPORTED",
                $"Cột '{SourceColumn(p_objSheet, p_strField)}' không được chứa công thức Excel.");
            return null;
        }

        if (p_objRow.Values.TryGetValue(p_strField, out var v_objValue))
        {
            if (v_objValue is DateTime v_objDate)
                return v_objDate.Date;
            if (v_objValue is DateOnly v_objDateOnly)
                return v_objDateOnly.ToDateTime(TimeOnly.MinValue);
            var v_strValue = Convert.ToString(v_objValue, CultureInfo.InvariantCulture);
            if (DateTime.TryParse(v_strValue, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.None, out v_objDate) ||
                DateTime.TryParse(v_strValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out v_objDate))
                return v_objDate.Date;
        }

        AddIssue(p_objResult, p_objSheet, p_objRow, p_strField, "INVALID_DATE",
            $"Ngày chứng từ '{SourceColumn(p_objSheet, p_strField)}' không hợp lệ.");
        return null;
    }

    private static decimal? ReadQuantity(
        CTransactionImportPreviewResult p_objResult,
        CImportDetectedSheet p_objSheet,
        CImportDetectedRow p_objRow)
    {
        const string v_strField = "Quantity";
        if (p_objRow.FormulaFields.Contains(v_strField))
        {
            AddIssue(p_objResult, p_objSheet, p_objRow, v_strField, "FORMULA_NOT_SUPPORTED",
                $"Cột '{SourceColumn(p_objSheet, v_strField)}' không được chứa công thức Excel.");
            return null;
        }

        decimal? v_decQuantity = null;
        if (p_objRow.Values.TryGetValue(v_strField, out var v_objValue))
        {
            try
            {
                if (v_objValue is not null)
                    v_decQuantity = Convert.ToDecimal(v_objValue, CultureInfo.InvariantCulture);
            }
            catch (Exception p_objException) when (p_objException is FormatException or InvalidCastException or OverflowException)
            {
                v_decQuantity = null;
            }
        }

        if (v_decQuantity is null or <= 0)
        {
            AddIssue(p_objResult, p_objSheet, p_objRow, v_strField, "INVALID_QUANTITY",
                $"Số lượng '{SourceColumn(p_objSheet, v_strField)}' phải là số lớn hơn 0.");
            return null;
        }
        return v_decQuantity;
    }

    private static decimal? ReadUnitPrice(
        CTransactionImportPreviewResult p_objResult,
        CImportDetectedSheet p_objSheet,
        CImportDetectedRow p_objRow)
    {
        const string v_strField = "UnitPrice";
        if (p_objRow.FormulaFields.Contains(v_strField))
        {
            AddIssue(p_objResult, p_objSheet, p_objRow, v_strField, "FORMULA_NOT_SUPPORTED",
                $"Cột '{SourceColumn(p_objSheet, v_strField)}' không được chứa công thức Excel.");
            return null;
        }
        if (!p_objRow.Values.TryGetValue(v_strField, out var v_objValue) || v_objValue is null ||
            string.IsNullOrWhiteSpace(Convert.ToString(v_objValue, CultureInfo.InvariantCulture)))
        {
            AddIssue(p_objResult, p_objSheet, p_objRow, v_strField, "MISSING_REQUIRED_PRICE",
                $"Thiếu đơn giá tại '{SourceColumn(p_objSheet, v_strField)}'.");
            return null;
        }
        try
        {
            var v_decValue = Convert.ToDecimal(v_objValue, CultureInfo.InvariantCulture);
            if (v_decValue > 0)
                return v_decValue;
        }
        catch (Exception p_objException) when (p_objException is FormatException or InvalidCastException or OverflowException)
        {
        }
        AddIssue(p_objResult, p_objSheet, p_objRow, v_strField, "INVALID_UNIT_PRICE",
            $"Đơn giá '{SourceColumn(p_objSheet, v_strField)}' phải là số lớn hơn 0.");
        return null;
    }

    private static bool IsBlankRow(CImportDetectedRow p_objRow, CImportDetectedSheet p_objSheet) =>
        p_objRow.FormulaFields.Count == 0 && p_objSheet.Columns
            .Where(v_objColumn => v_objColumn.CanonicalField is not null)
            .All(v_objColumn => !p_objRow.Values.TryGetValue(v_objColumn.CanonicalField!, out var v_objValue) ||
                v_objValue is null || string.IsNullOrWhiteSpace(Convert.ToString(v_objValue, CultureInfo.InvariantCulture)));

    private static void AddIssue(
        CTransactionImportPreviewResult p_objResult,
        CImportDetectedSheet p_objSheet,
        CImportDetectedRow p_objRow,
        string p_strField,
        string p_strCode,
        string p_strMessage) =>
        p_objResult.Errors.Add(new CTransactionImportIssue(
            p_objRow.RowNumber,
            p_objSheet.Name,
            p_objSheet.Role,
            SourceColumn(p_objSheet, p_strField),
            p_strField,
            p_strCode,
            p_strMessage,
            p_objRow.Values.TryGetValue("DocumentNumber", out var v_objValue)
                ? CImportBusinessValueComparer.Normalize(Convert.ToString(v_objValue, CultureInfo.InvariantCulture))
                : ""));

    private static string SourceColumn(CImportDetectedSheet p_objSheet, string p_strField) =>
        p_objSheet.Columns.FirstOrDefault(v_objColumn =>
            string.Equals(v_objColumn.CanonicalField, p_strField, StringComparison.OrdinalIgnoreCase))?.Header ?? "";

    private static string ComparisonKey(string? p_strValue) =>
        CImportBusinessValueComparer.Normalize(p_strValue).ToUpperInvariant();
}
