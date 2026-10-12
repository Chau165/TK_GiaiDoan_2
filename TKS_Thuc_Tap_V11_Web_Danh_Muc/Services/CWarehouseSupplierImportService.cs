using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Common.Common;

namespace TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;

public interface ISupplierImportPermissionResolver
{
    bool Has_Add_Permission(string p_strUserName, string p_strFunctionCode);
}

public sealed class CWarehouseSupplierImportService
{
    private readonly CSupplier_Xlsx_Reader m_objReader;
    private readonly CWarehouseMaster_Controller m_objMasterController;
    private readonly ISupplierImportPermissionResolver m_objPermissionResolver;

    public CWarehouseSupplierImportService()
        : this(new CSupplier_Xlsx_Reader(), new CWarehouseMaster_Controller(), new CSupplierImportPermissionResolver())
    {
    }

    public CWarehouseSupplierImportService(ISupplierImportPermissionResolver p_objPermissionResolver)
        : this(new CSupplier_Xlsx_Reader(), new CWarehouseMaster_Controller(), p_objPermissionResolver)
    {
    }

    private CWarehouseSupplierImportService(
        CSupplier_Xlsx_Reader p_objReader,
        CWarehouseMaster_Controller p_objMasterController,
        ISupplierImportPermissionResolver p_objPermissionResolver)
    {
        m_objReader = p_objReader;
        m_objMasterController = p_objMasterController;
        m_objPermissionResolver = p_objPermissionResolver;
    }

    public async Task<CSupplierImportPreviewResult> PreviewAsync(
        string p_strFileName,
        Stream p_objSource,
        string p_strCurrentUser,
        string p_strCurrentFunction,
        CancellationToken p_objCancellationToken = default)
    {
        Demand_Add_Permission(p_strCurrentUser, p_strCurrentFunction);
        if (!string.Equals(Path.GetExtension(p_strFileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Chỉ chấp nhận file Excel .xlsx theo template Nhà cung cấp.");
        var v_objWorksheet = await m_objReader.ReadAsync(p_objSource, p_objCancellationToken);
        var v_arrExistingSuppliers = await new CDM_NCC_Controller().F2019_sp_sel_List_NCC();
        return CSupplierImportValidator.Preview(v_objWorksheet, v_arrExistingSuppliers);
    }

    public async Task<int> ConfirmAsync(
        CSupplierImportPreviewResult p_objPreview,
        string p_strCurrentUser,
        string p_strCurrentFunction,
        CancellationToken p_objCancellationToken = default)
    {
        Demand_Add_Permission(p_strCurrentUser, p_strCurrentFunction);
        ArgumentNullException.ThrowIfNull(p_objPreview);
        if (!p_objPreview.Can_Confirm)
            throw new CSupplierImportValidationException(p_objPreview);
        var v_arrExistingSuppliers = await new CDM_NCC_Controller().F2019_sp_sel_List_NCC();
        var v_objCurrentPreview = CSupplierImportValidator.Revalidate(p_objPreview.Rows, v_arrExistingSuppliers);
        if (!v_objCurrentPreview.Can_Confirm)
            throw new CSupplierImportValidationException(v_objCurrentPreview);
        await m_objMasterController.Import_Suppliers_Async(
            v_objCurrentPreview.Rows.Where(v_objRow => v_objRow.Is_Valid).ToArray(),
            p_strCurrentUser,
            p_strCurrentFunction,
            p_objCancellationToken);
        return v_objCurrentPreview.Valid_Rows;
    }

    private void Demand_Add_Permission(string p_strCurrentUser, string p_strCurrentFunction)
    {
        if (string.IsNullOrWhiteSpace(p_strCurrentUser) ||
            string.IsNullOrWhiteSpace(p_strCurrentFunction) ||
            !m_objPermissionResolver.Has_Add_Permission(p_strCurrentUser, p_strCurrentFunction))
            throw new UnauthorizedAccessException("Bạn không có quyền thêm nhà cung cấp.");
    }

    private sealed class CSupplierImportPermissionResolver : ISupplierImportPermissionResolver
    {
        public bool Has_Add_Permission(string p_strUserName, string p_strFunctionCode) =>
            CCommonFunction.Get_Chuc_Nang_By_User(p_strUserName, p_strFunctionCode)?.Is_Have_Add_Permission == true;
    }
}

public sealed class CSupplierImportValidationException : Exception
{
    public CSupplierImportPreviewResult PreviewResult { get; }

    public CSupplierImportValidationException(CSupplierImportPreviewResult p_objPreview)
        : base(string.Join(Environment.NewLine, p_objPreview.Errors.Select(v_objError => v_objError.Message).Distinct(StringComparer.Ordinal)))
    {
        PreviewResult = p_objPreview;
    }
}
