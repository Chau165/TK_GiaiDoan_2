using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Common.Common;

namespace TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;

public interface ITransactionImportPermissionResolver
{
    bool Has_Add_Permission(string p_strUserName, string p_strFunctionCode);
}

public interface ITransactionImportStore
{
    Task<CTransactionImportReferences> Load_References_Async(
        string p_strProfileCode,
        IReadOnlyList<string> p_arrDocumentNumbers,
        string p_strCurrent_Login,
        CancellationToken p_objCancellationToken);

    Task Save_Document_Atomically_Async(
        CWarehouseDocument p_objDocument,
        IReadOnlyList<CWarehouseDocumentDetail> p_arrDetails,
        string p_strCurrent_User,
        string p_strCurrent_Function,
        CancellationToken p_objCancellationToken);
}

public sealed class CWarehouseTransactionImportService
{
    private const int m_iMaxFileSize = 25 * 1024 * 1024;
    private readonly CTransactionImportPreviewService m_objPreviewService;
    private readonly ITransactionImportStore m_objStore;
    private readonly ITransactionImportPermissionResolver m_objPermissionResolver;

    public CWarehouseTransactionImportService()
        : this(new CTransactionImportPreviewService(), new CWarehouseTransactionImportStore(), new CTransactionImportPermissionResolver())
    {
    }

    public CWarehouseTransactionImportService(
        CTransactionImportPreviewService p_objPreviewService,
        ITransactionImportStore p_objStore,
        ITransactionImportPermissionResolver p_objPermissionResolver)
    {
        m_objPreviewService = p_objPreviewService ?? throw new ArgumentNullException(nameof(p_objPreviewService));
        m_objStore = p_objStore ?? throw new ArgumentNullException(nameof(p_objStore));
        m_objPermissionResolver = p_objPermissionResolver ?? throw new ArgumentNullException(nameof(p_objPermissionResolver));
    }

    public Task<CTransactionImportPreviewResult> PreviewAsync(
        string p_strFileName,
        byte[] p_arrContent,
        string p_strProfileCode,
        string p_strCurrent_User,
        string p_strCurrent_Function,
        CancellationToken p_objCancellationToken = default)
    {
        Demand_Add_Permission(p_strCurrent_User, p_strCurrent_Function);
        Validate_Upload(p_strFileName, p_arrContent, p_strProfileCode);
        return Build_Preview_Async(p_arrContent, p_strProfileCode, p_strCurrent_User, p_objCancellationToken);
    }

    public async Task<CTransactionImportConfirmResult> ConfirmAsync(
        string p_strFileName,
        byte[] p_arrContent,
        string p_strProfileCode,
        string p_strCurrent_User,
        string p_strCurrent_Function,
        CancellationToken p_objCancellationToken = default)
    {
        Demand_Add_Permission(p_strCurrent_User, p_strCurrent_Function);
        Validate_Upload(p_strFileName, p_arrContent, p_strProfileCode);

        var v_objCurrentPreview = await Build_Preview_Async(
            p_arrContent, p_strProfileCode, p_strCurrent_User, p_objCancellationToken);
        if (!v_objCurrentPreview.Can_Confirm)
            throw new CTransactionImportValidationException(v_objCurrentPreview);

        var v_bIsReceipt = p_strProfileCode == "Inbound";
        var v_iImported = 0;
        var v_iSkipped = 0;
        foreach (var v_objDocument in v_objCurrentPreview.Documents)
        {
            p_objCancellationToken.ThrowIfCancellationRequested();
            if (v_objDocument.Import_Status == "SKIPPED_DUPLICATE_DOCUMENT")
            {
                v_iSkipped++;
                continue;
            }

            var v_objHeader = new CWarehouseDocument
            {
                Is_Receipt = v_bIsReceipt,
                So_Phieu = v_objDocument.DocumentNumber,
                Kho_ID = v_objDocument.Warehouse_ID,
                NCC_ID = v_bIsReceipt ? v_objDocument.Supplier_ID : 0,
                Ngay_Chung_Tu = v_objDocument.DocumentDate!.Value,
                Ghi_Chu = v_objDocument.Note
            };
            var v_arrDetails = v_objDocument.Lines.Select(v_objLine => new CWarehouseDocumentDetail
            {
                San_Pham_ID = v_objLine.Product_ID,
                So_Luong = v_objLine.Quantity!.Value,
                Don_Gia = v_objLine.UnitPrice!.Value
            }).ToArray();

            try
            {
                await m_objStore.Save_Document_Atomically_Async(
                    v_objHeader, v_arrDetails, p_strCurrent_User, p_strCurrent_Function, p_objCancellationToken);
                v_iImported++;
            }
            catch (Exception p_objException)
            {
                throw new InvalidOperationException(
                    $"Không thể nhập chứng từ '{v_objDocument.DocumentNumber}'. Chứng từ này được xử lý trong transaction riêng; đã lưu {v_iImported} chứng từ trước đó và bỏ qua {v_iSkipped} chứng từ trùng.",
                    p_objException);
            }
        }

        return new CTransactionImportConfirmResult(v_iImported, v_iSkipped);
    }

    private async Task<CTransactionImportPreviewResult> Build_Preview_Async(
        byte[] p_arrContent,
        string p_strProfileCode,
        string p_strCurrent_Login,
        CancellationToken p_objCancellationToken)
    {
        using var v_objStructureStream = new MemoryStream(p_arrContent, writable: false);
        var v_objStructurePreview = await m_objPreviewService.PreviewAsync(
            v_objStructureStream, p_strProfileCode, null, p_objCancellationToken);
        if (!v_objStructurePreview.StructureValid || v_objStructurePreview.Documents.Count == 0)
            return v_objStructurePreview;

        var v_arrDocumentNumbers = v_objStructurePreview.Documents
            .Select(v_objDocument => v_objDocument.DocumentNumber)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var v_objReferences = await m_objStore.Load_References_Async(
            p_strProfileCode, v_arrDocumentNumbers, p_strCurrent_Login, p_objCancellationToken);

        using var v_objValidatedStream = new MemoryStream(p_arrContent, writable: false);
        return await m_objPreviewService.PreviewAsync(
            v_objValidatedStream, p_strProfileCode, v_objReferences, p_objCancellationToken);
    }

    private void Demand_Add_Permission(string p_strCurrent_User, string p_strCurrent_Function)
    {
        if (string.IsNullOrWhiteSpace(p_strCurrent_User) ||
            string.IsNullOrWhiteSpace(p_strCurrent_Function) ||
            !m_objPermissionResolver.Has_Add_Permission(p_strCurrent_User, p_strCurrent_Function))
            throw new UnauthorizedAccessException("Bạn không có quyền tạo phiếu nhập/xuất.");
    }

    private static void Validate_Upload(string p_strFileName, byte[]? p_arrContent, string p_strProfileCode)
    {
        if (!string.Equals(Path.GetExtension(p_strFileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Chỉ chấp nhận file Excel .xlsx.");
        if (p_arrContent is null || p_arrContent.Length == 0 || p_arrContent.Length > m_iMaxFileSize)
            throw new InvalidDataException("File Excel không hợp lệ hoặc vượt quá 25 MiB.");
        if (p_strProfileCode is not "Inbound" and not "Outbound")
            throw new ArgumentException("Loại chứng từ import không hợp lệ.", nameof(p_strProfileCode));
    }

    private sealed class CTransactionImportPermissionResolver : ITransactionImportPermissionResolver
    {
        public bool Has_Add_Permission(string p_strUserName, string p_strFunctionCode) =>
            CCommonFunction.Get_Chuc_Nang_By_User(p_strUserName, p_strFunctionCode)?.Is_Have_Add_Permission == true;
    }
}

public sealed record CTransactionImportConfirmResult(int Imported_Documents, int Skipped_Documents);

public sealed class CTransactionImportValidationException : Exception
{
    public CTransactionImportPreviewResult PreviewResult { get; }

    public CTransactionImportValidationException(CTransactionImportPreviewResult p_objPreview)
        : base(string.Join(Environment.NewLine, p_objPreview.Errors.Select(v_objError => v_objError.Message).Distinct(StringComparer.Ordinal)))
    {
        PreviewResult = p_objPreview;
    }
}

public sealed class CWarehouseTransactionImportStore : ITransactionImportStore
{
    private readonly CWarehouseProductSupport_Service m_objProductSupportService = new();
    private readonly CWarehouseDocument_Controller m_objDocumentController = new();
    private readonly CWarehousePermission_Controller m_objPermissionController = new();
    private readonly CWarehouseReport_Controller m_objReportController = new();

    public async Task<CTransactionImportReferences> Load_References_Async(
        string p_strProfileCode,
        IReadOnlyList<string> p_arrDocumentNumbers,
        string p_strCurrent_Login,
        CancellationToken p_objCancellationToken)
    {
        var v_bIsReceipt = p_strProfileCode == "Inbound";
        var v_arrAllowedWarehouses = await m_objPermissionController.List_Authorized_Warehouses_Async(p_strCurrent_Login);
        var v_arrWarehouses = v_arrAllowedWarehouses.Select(v_objWarehouse => new CWarehouseMaster
        {
            Auto_ID = v_objWarehouse.Auto_ID,
            Code = v_objWarehouse.Code,
            Name = v_objWarehouse.Name
        }).ToArray();
        IReadOnlyList<CWarehouseMaster> v_arrSuppliers = v_bIsReceipt
            ? new CDM_NCC_Controller().F2019_sp_sel_List_NCC()
            : Array.Empty<CWarehouseMaster>();
        var v_arrUnits = new CDM_Don_Vi_Tinh_Controller().F2016_sp_sel_List_Don_Vi_Tinh();
        var v_arrProducts = await m_objProductSupportService.List_Product_Masters_Async(p_objCancellationToken);
        var v_arrExistingDocuments = await Load_Existing_Documents_Async(
            v_bIsReceipt, p_arrDocumentNumbers, p_strCurrent_Login, p_objCancellationToken);
        var v_arrInventoryBalances = v_bIsReceipt
            ? null
            : await Load_Current_Inventory_Balances_Async(p_strCurrent_Login, p_objCancellationToken);

        return new CTransactionImportReferences
        {
            Warehouses = v_arrWarehouses,
            Suppliers = v_arrSuppliers,
            Units = v_arrUnits,
            Products = v_arrProducts,
            ExistingDocuments = v_arrExistingDocuments,
            InventoryBalances = v_arrInventoryBalances
        };
    }

    public async Task Save_Document_Atomically_Async(
        CWarehouseDocument p_objDocument,
        IReadOnlyList<CWarehouseDocumentDetail> p_arrDetails,
        string p_strCurrent_User,
        string p_strCurrent_Function,
        CancellationToken p_objCancellationToken)
    {
        await m_objDocumentController.Save_Imported_Document_Atomically_Async(
            p_objDocument, p_arrDetails, p_strCurrent_User, p_strCurrent_Function, p_objCancellationToken);
    }

    private async Task<List<CTransactionImportExistingDocument>> Load_Existing_Documents_Async(
        bool p_bIsReceipt,
        IReadOnlyList<string> p_arrDocumentNumbers,
        string p_strCurrent_Login,
        CancellationToken p_objCancellationToken)
    {
        var v_setDocumentNumbers = p_arrDocumentNumbers
            .Select(CImportBusinessValueComparer.Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var v_arrHeaders = await m_objDocumentController.List_Documents_Async(p_bIsReceipt, p_strCurrent_Login);
        var v_arrMatches = v_arrHeaders
            .Where(v_objHeader => v_setDocumentNumbers.Contains(CImportBusinessValueComparer.Normalize(v_objHeader.So_Phieu)))
            .ToArray();
        var v_arrExisting = new List<CTransactionImportExistingDocument>(v_arrMatches.Length);
        foreach (var v_objHeader in v_arrMatches)
        {
            p_objCancellationToken.ThrowIfCancellationRequested();
            var v_arrDetails = await m_objDocumentController.List_Document_Details_Async(
                p_bIsReceipt, v_objHeader.Auto_ID, p_strCurrent_Login);
            v_arrExisting.Add(new CTransactionImportExistingDocument(v_objHeader, v_arrDetails));
        }

        return v_arrExisting;
    }

    private async Task<IReadOnlyList<CTransactionImportInventoryBalance>> Load_Current_Inventory_Balances_Async(
        string p_strCurrent_Login,
        CancellationToken p_objCancellationToken)
    {
        const int v_iPageSize = 500;
        var v_arrBalances = new List<CTransactionImportInventoryBalance>();
        var v_iPage = 1;
        var v_iFetched = 0;
        var v_iTotal = 0;
        do
        {
            p_objCancellationToken.ThrowIfCancellationRequested();
            var v_objPage = await m_objReportController.Inventory_Report_Page_Async(
                DateTime.Today, DateTime.Today, v_iPage, v_iPageSize, p_strCurrent_Login,
                p_bRead_Current_Balance: true);
            v_iTotal = v_objPage.Total_Count;
            v_arrBalances.AddRange(v_objPage.Items.Select(v_objBalance => new CTransactionImportInventoryBalance(
                v_objBalance.Kho_ID,
                v_objBalance.San_Pham_ID,
                v_objBalance.SL_Ton_Thuc_Te,
                v_objBalance.SL_Dang_Giu)));
            v_iFetched += v_objPage.Items.Count;
            v_iPage++;
            if (v_objPage.Items.Count == 0)
                break;
        }
        while (v_iFetched < v_iTotal);

        return v_arrBalances;
    }
}
