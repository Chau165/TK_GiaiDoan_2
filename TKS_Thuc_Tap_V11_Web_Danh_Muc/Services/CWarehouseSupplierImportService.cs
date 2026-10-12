using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Cache;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;
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
    private readonly ISupplierImportPermissionResolver m_objPermissionResolver;

    public CWarehouseSupplierImportService()
        : this(new CSupplier_Xlsx_Reader(), new CSupplierImportPermissionResolver())
    {
    }

    public CWarehouseSupplierImportService(ISupplierImportPermissionResolver p_objPermissionResolver)
        : this(new CSupplier_Xlsx_Reader(), p_objPermissionResolver)
    {
    }

    private CWarehouseSupplierImportService(
        CSupplier_Xlsx_Reader p_objReader,
        ISupplierImportPermissionResolver p_objPermissionResolver)
    {
        m_objReader = p_objReader;
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
        var v_arrExistingSuppliers = new CDM_NCC_Controller().F2019_sp_sel_List_NCC();
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
        var v_arrExistingSuppliers = new CDM_NCC_Controller().F2019_sp_sel_List_NCC();
        var v_objCurrentPreview = CSupplierImportValidator.Revalidate(p_objPreview.Rows, v_arrExistingSuppliers);
        if (!v_objCurrentPreview.Can_Confirm)
            throw new CSupplierImportValidationException(v_objCurrentPreview);
        await Import_Suppliers_Async(
            v_objCurrentPreview.Rows.Where(v_objRow => v_objRow.Is_Valid).ToArray(),
            p_strCurrentUser,
            p_strCurrentFunction,
            p_objCancellationToken);
        return v_objCurrentPreview.Valid_Rows;
    }

    internal async Task Import_Suppliers_Async(
        IReadOnlyList<CSupplierImportRow> p_arrRows,
        string p_strCurrent_User,
        string p_strCurrent_Function,
        CancellationToken p_objCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(p_arrRows);
        if (p_arrRows.Count == 0)
            throw new ArgumentException("The supplier import batch cannot be empty.", nameof(p_arrRows));

        CDM_NCC_Controller v_objSupplierController = new CDM_NCC_Controller();
        await using SqlConnection v_objConnection = new SqlConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String);
        await v_objConnection.OpenAsync(p_objCancellationToken);
        using SqlTransaction v_objTransaction = v_objConnection.BeginTransaction();
        try
        {
            foreach (CSupplierImportRow v_objRow in p_arrRows)
            {
                p_objCancellationToken.ThrowIfCancellationRequested();
                CDM_NCC v_objSupplier = new CDM_NCC
                {
                    Auto_ID = 0,
                    Ma_NCC = v_objRow.Ma_NCC,
                    Ten_NCC = v_objRow.Ten_NCC,
                    Ghi_Chu = v_objRow.Ghi_Chu
                };
                try
                {
                    v_objSupplier.Auto_ID = v_objSupplierController.F2019_sp_ins_NCC(v_objConnection, v_objTransaction, v_objSupplier, p_strCurrent_User, p_strCurrent_Function);
                }
                catch (SqlException p_objException)
                {
                    string v_strMessage = p_objException.Number switch
                    {
                        2601 or 2627 or 51031 => $"Dòng {v_objRow.Row_Number}: Mã hoặc tên nhà cung cấp '{v_objRow.Ma_NCC} / {v_objRow.Ten_NCC}' đã tồn tại; toàn bộ lô đã được hủy.",
                        51030 => $"Dòng {v_objRow.Row_Number}: Mã nhà cung cấp không được để trống; toàn bộ lô đã được hủy.",
                        51032 => $"Dòng {v_objRow.Row_Number}: Tên nhà cung cấp không được để trống; toàn bộ lô đã được hủy.",
                        _ => $"Dòng {v_objRow.Row_Number}: Không thể lưu nhà cung cấp; toàn bộ lô đã được hủy."
                    };
                    throw new InvalidOperationException(v_strMessage, p_objException);
                }
            }
            v_objTransaction.Commit();
        }
        catch
        {
            try
            {
                v_objTransaction.Rollback();
            }
            catch
            {
                // Preserve the original insert or commit failure.
            }
            throw;
        }

        try
        {
            CCache_NCC.Load_Cache_NCC();
        }
        catch (Exception p_objException)
        {
            throw new InvalidOperationException(
                "Import nhà cung cấp đã commit thành công, nhưng cache nhà cung cấp chưa được làm mới. Dữ liệu vẫn đã lưu trong cơ sở dữ liệu; cache sẽ được nạp lại ở lần tải tiếp theo.",
                p_objException);
        }
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
