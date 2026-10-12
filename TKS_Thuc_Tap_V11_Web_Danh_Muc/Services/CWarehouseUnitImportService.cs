using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Common.Common;

namespace TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;

public interface IUnitImportPermissionResolver
{
    bool Has_Add_Permission(string p_strUserName, string p_strFunctionCode);
}

public sealed class CWarehouseUnitImportService
{
    private readonly CUnit_Xlsx_Reader m_objReader;
    private readonly CWarehouseMaster_Controller m_objMasterController;
    private readonly IUnitImportPermissionResolver m_objPermissionResolver;

    public CWarehouseUnitImportService()
        : this(new CUnit_Xlsx_Reader(), new CWarehouseMaster_Controller(), new CUnitImportPermissionResolver())
    {
    }

    public CWarehouseUnitImportService(IUnitImportPermissionResolver p_objPermissionResolver)
        : this(new CUnit_Xlsx_Reader(), new CWarehouseMaster_Controller(), p_objPermissionResolver)
    {
    }

    private CWarehouseUnitImportService(
        CUnit_Xlsx_Reader p_objReader,
        CWarehouseMaster_Controller p_objMasterController,
        IUnitImportPermissionResolver p_objPermissionResolver)
    {
        m_objReader = p_objReader;
        m_objMasterController = p_objMasterController;
        m_objPermissionResolver = p_objPermissionResolver;
    }

    public async Task<CUnitImportPreviewResult> PreviewAsync(
        string p_strFileName,
        Stream p_objSource,
        string p_strCurrentUser,
        string p_strCurrentFunction,
        CancellationToken p_objCancellationToken = default)
    {
        Demand_Add_Permission(p_strCurrentUser, p_strCurrentFunction);
        if (!string.Equals(Path.GetExtension(p_strFileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Chỉ chấp nhận file Excel .xlsx theo template Đơn vị tính.");

        var v_objWorksheet = await m_objReader.ReadAsync(p_objSource, p_objCancellationToken);
        var v_arrExistingUnits = await m_objMasterController.List_Master_Async("DonViTinh");
        return CUnitImportValidator.Preview(v_objWorksheet, v_arrExistingUnits);
    }

    public async Task<int> ConfirmAsync(
        CUnitImportPreviewResult p_objPreview,
        string p_strCurrentUser,
        string p_strCurrentFunction,
        CancellationToken p_objCancellationToken = default)
    {
        Demand_Add_Permission(p_strCurrentUser, p_strCurrentFunction);
        ArgumentNullException.ThrowIfNull(p_objPreview);
        if (!p_objPreview.Can_Confirm)
            throw new CUnitImportValidationException(p_objPreview);

        var v_arrExistingUnits = await m_objMasterController.List_Master_Async("DonViTinh");
        var v_objCurrentPreview = CUnitImportValidator.Revalidate(p_objPreview.Rows, v_arrExistingUnits);
        if (!v_objCurrentPreview.Can_Confirm)
            throw new CUnitImportValidationException(v_objCurrentPreview);

        await m_objMasterController.Import_Units_Async(
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
        {
            throw new UnauthorizedAccessException("Bạn không có quyền thêm đơn vị tính.");
        }
    }

    private sealed class CUnitImportPermissionResolver : IUnitImportPermissionResolver
    {
        public bool Has_Add_Permission(string p_strUserName, string p_strFunctionCode)
        {
            return CCommonFunction.Get_Chuc_Nang_By_User(p_strUserName, p_strFunctionCode)?.Is_Have_Add_Permission == true;
        }
    }
}

public sealed class CUnitImportValidationException : Exception
{
    public CUnitImportPreviewResult PreviewResult { get; }

    public CUnitImportValidationException(CUnitImportPreviewResult p_objPreview)
        : base(string.Join(Environment.NewLine, p_objPreview.Errors.Select(v_objError => v_objError.Message).Distinct(StringComparer.Ordinal)))
    {
        PreviewResult = p_objPreview;
    }
}
