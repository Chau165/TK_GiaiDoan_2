using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;
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
    private readonly IUnitImportPermissionResolver m_objPermissionResolver;

    public CWarehouseUnitImportService()
        : this(new CUnit_Xlsx_Reader(), new CUnitImportPermissionResolver())
    {
    }

    public CWarehouseUnitImportService(IUnitImportPermissionResolver p_objPermissionResolver)
        : this(new CUnit_Xlsx_Reader(), p_objPermissionResolver)
    {
    }

    private CWarehouseUnitImportService(
        CUnit_Xlsx_Reader p_objReader,
        IUnitImportPermissionResolver p_objPermissionResolver)
    {
        m_objReader = p_objReader;
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
        var v_arrExistingUnits = new CDM_Don_Vi_Tinh_Controller().F2016_sp_sel_List_Don_Vi_Tinh();
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

        var v_arrExistingUnits = new CDM_Don_Vi_Tinh_Controller().F2016_sp_sel_List_Don_Vi_Tinh();
        var v_objCurrentPreview = CUnitImportValidator.Revalidate(p_objPreview.Rows, v_arrExistingUnits);
        if (!v_objCurrentPreview.Can_Confirm)
            throw new CUnitImportValidationException(v_objCurrentPreview);

        await Import_Units_Async(
            v_objCurrentPreview.Rows.Where(v_objRow => v_objRow.Is_Valid).ToArray(),
            p_strCurrentUser,
            p_strCurrentFunction,
            p_objCancellationToken);
        return v_objCurrentPreview.Valid_Rows;
    }

    internal async Task Import_Units_Async(
        IReadOnlyList<CUnitImportRow> p_arrRows,
        string p_strCurrent_User,
        string p_strCurrent_Function,
        CancellationToken p_objCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(p_arrRows);
        if (p_arrRows.Count == 0)
            throw new ArgumentException("The unit import batch cannot be empty.", nameof(p_arrRows));

        CDM_Don_Vi_Tinh_Controller v_objUnitController = new CDM_Don_Vi_Tinh_Controller();
        await using SqlConnection v_objConnection = new SqlConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String);
        await v_objConnection.OpenAsync(p_objCancellationToken);
        using SqlTransaction v_objTransaction = v_objConnection.BeginTransaction();

        try
        {
            foreach (CUnitImportRow v_objRow in p_arrRows)
            {
                p_objCancellationToken.ThrowIfCancellationRequested();
                CDM_Don_Vi_Tinh v_objUnit = new CDM_Don_Vi_Tinh
                {
                    Auto_ID = 0,
                    Ten_Don_Vi_Tinh = v_objRow.Ten_Don_Vi_Tinh,
                    Ghi_Chu = v_objRow.Ghi_Chu
                };

                try
                {
                    v_objUnit.Auto_ID = v_objUnitController.F2016_sp_ins_Don_Vi_Tinh(v_objConnection, v_objTransaction, v_objUnit, p_strCurrent_User, p_strCurrent_Function);
                }
                catch (SqlException p_objException)
                {
                    string v_strMessage = p_objException.Number switch
                    {
                        2601 or 2627 or 51002 => $"Dòng {v_objRow.Row_Number}: Tên đơn vị tính '{v_objRow.Ten_Don_Vi_Tinh}' đã tồn tại; toàn bộ lô đã được hủy.",
                        51001 => $"Dòng {v_objRow.Row_Number}: Tên đơn vị tính không được để trống; toàn bộ lô đã được hủy.",
                        _ => $"Dòng {v_objRow.Row_Number}: Không thể lưu đơn vị tính; toàn bộ lô đã được hủy."
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
