using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Common.Common;

namespace TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;

public interface IWarehouseImportPermissionResolver
{
    bool Has_Add_Permission(string p_strUserName, string p_strFunctionCode);
}

public sealed class CWarehouseKhoImportService
{
    private readonly CWarehouse_Xlsx_Reader m_objReader;
    private readonly IWarehouseImportPermissionResolver m_objPermissionResolver;

    public CWarehouseKhoImportService()
        : this(new CWarehouse_Xlsx_Reader(), new CWarehouseImportPermissionResolver())
    {
    }

    public CWarehouseKhoImportService(IWarehouseImportPermissionResolver p_objPermissionResolver)
        : this(new CWarehouse_Xlsx_Reader(), p_objPermissionResolver)
    {
    }

    private CWarehouseKhoImportService(
        CWarehouse_Xlsx_Reader p_objReader,
        IWarehouseImportPermissionResolver p_objPermissionResolver)
    {
        m_objReader = p_objReader;
        m_objPermissionResolver = p_objPermissionResolver;
    }

    public async Task<CWarehouseImportPreviewResult> PreviewAsync(
        string p_strFileName,
        Stream p_objSource,
        string p_strCurrentUser,
        string p_strCurrentFunction,
        CancellationToken p_objCancellationToken = default)
    {
        Demand_Add_Permission(p_strCurrentUser, p_strCurrentFunction);
        if (!string.Equals(Path.GetExtension(p_strFileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Chỉ chấp nhận file Excel .xlsx theo template Kho.");
        var v_objWorksheet = await m_objReader.ReadAsync(p_objSource, p_objCancellationToken);
        var v_arrExistingWarehouses = new CDM_Kho_Controller().F2009_sp_sel_List_Kho();
        return CWarehouseImportValidator.Preview(v_objWorksheet, v_arrExistingWarehouses);
    }

    public async Task<int> ConfirmAsync(
        CWarehouseImportPreviewResult p_objPreview,
        string p_strCurrentUser,
        string p_strCurrentFunction,
        CancellationToken p_objCancellationToken = default)
    {
        Demand_Add_Permission(p_strCurrentUser, p_strCurrentFunction);
        ArgumentNullException.ThrowIfNull(p_objPreview);
        if (!p_objPreview.Can_Confirm)
            throw new CWarehouseImportValidationException(p_objPreview);
        var v_arrExistingWarehouses = new CDM_Kho_Controller().F2009_sp_sel_List_Kho();
        var v_objCurrentPreview = CWarehouseImportValidator.Revalidate(p_objPreview.Rows, v_arrExistingWarehouses);
        if (!v_objCurrentPreview.Can_Confirm)
            throw new CWarehouseImportValidationException(v_objCurrentPreview);
        await Import_Warehouses_Async(
            v_objCurrentPreview.Rows.Where(v_objRow => v_objRow.Is_Valid).ToArray(),
            p_strCurrentUser,
            p_strCurrentFunction,
            p_objCancellationToken);
        return v_objCurrentPreview.Valid_Rows;
    }

    internal async Task Import_Warehouses_Async(
        IReadOnlyList<CWarehouseImportRow> p_arrRows,
        string p_strCurrent_User,
        string p_strCurrent_Function,
        CancellationToken p_objCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(p_arrRows);
        if (p_arrRows.Count == 0)
            throw new ArgumentException("The warehouse import batch cannot be empty.", nameof(p_arrRows));

        CDM_Kho_Controller v_objWarehouseController = new CDM_Kho_Controller();
        await using SqlConnection v_objConnection = new SqlConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String);
        await v_objConnection.OpenAsync(p_objCancellationToken);
        using SqlTransaction v_objTransaction = v_objConnection.BeginTransaction();
        try
        {
            foreach (CWarehouseImportRow v_objRow in p_arrRows)
            {
                p_objCancellationToken.ThrowIfCancellationRequested();
                CWarehouseMaster v_objWarehouse = new CWarehouseMaster
                {
                    Auto_ID = 0,
                    Name = v_objRow.Ten_Kho,
                    Ghi_Chu = v_objRow.Ghi_Chu
                };
                try
                {
                    v_objWarehouse.Auto_ID = v_objWarehouseController.F2009_sp_ins_Kho(v_objConnection, v_objTransaction, v_objWarehouse, p_strCurrent_User, p_strCurrent_Function);
                }
                catch (SqlException p_objException)
                {
                    string v_strMessage = p_objException.Number switch
                    {
                        2601 or 2627 or 51041 => $"Dòng {v_objRow.Row_Number}: Tên kho '{v_objRow.Ten_Kho}' đã tồn tại; toàn bộ lô đã được hủy.",
                        51040 => $"Dòng {v_objRow.Row_Number}: Tên kho không được để trống; toàn bộ lô đã được hủy.",
                        _ => $"Dòng {v_objRow.Row_Number}: Không thể lưu kho; toàn bộ lô đã được hủy."
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
            throw new UnauthorizedAccessException("Bạn không có quyền thêm kho.");
    }

    private sealed class CWarehouseImportPermissionResolver : IWarehouseImportPermissionResolver
    {
        public bool Has_Add_Permission(string p_strUserName, string p_strFunctionCode) =>
            CCommonFunction.Get_Chuc_Nang_By_User(p_strUserName, p_strFunctionCode)?.Is_Have_Add_Permission == true;
    }
}

public sealed class CWarehouseImportValidationException : Exception
{
    public CWarehouseImportPreviewResult PreviewResult { get; }

    public CWarehouseImportValidationException(CWarehouseImportPreviewResult p_objPreview)
        : base(string.Join(Environment.NewLine, p_objPreview.Errors.Select(v_objError => v_objError.Message).Distinct(StringComparer.Ordinal)))
    {
        PreviewResult = p_objPreview;
    }
}
