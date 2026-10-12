using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Cache;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Common.Common;

namespace TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;

public interface ICategoryImportPermissionResolver
{
    bool Has_Add_Permission(string p_strUserName, string p_strFunctionCode);
}

public sealed class CWarehouseCategoryImportService
{
    private readonly CCategory_Xlsx_Reader m_objReader;
    private readonly ICategoryImportPermissionResolver m_objPermissionResolver;

    public CWarehouseCategoryImportService()
        : this(new CCategory_Xlsx_Reader(), new CCategoryImportPermissionResolver())
    {
    }

    public CWarehouseCategoryImportService(ICategoryImportPermissionResolver p_objPermissionResolver)
        : this(new CCategory_Xlsx_Reader(), p_objPermissionResolver)
    {
    }

    private CWarehouseCategoryImportService(
        CCategory_Xlsx_Reader p_objReader,
        ICategoryImportPermissionResolver p_objPermissionResolver)
    {
        m_objReader = p_objReader;
        m_objPermissionResolver = p_objPermissionResolver;
    }

    public async Task<CCategoryImportPreviewResult> PreviewAsync(
        string p_strFileName,
        Stream p_objSource,
        string p_strCurrentUser,
        string p_strCurrentFunction,
        CancellationToken p_objCancellationToken = default)
    {
        Demand_Add_Permission(p_strCurrentUser, p_strCurrentFunction);
        if (!string.Equals(Path.GetExtension(p_strFileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Chỉ chấp nhận file Excel .xlsx theo template Loại sản phẩm.");
        var v_objWorksheet = await m_objReader.ReadAsync(p_objSource, p_objCancellationToken);
        var v_arrExistingCategories = new CDM_Loai_San_Pham_Controller().F2017_sp_sel_List_Loai_San_Pham();
        return CCategoryImportValidator.Preview(v_objWorksheet, v_arrExistingCategories);
    }

    public async Task<int> ConfirmAsync(
        CCategoryImportPreviewResult p_objPreview,
        string p_strCurrentUser,
        string p_strCurrentFunction,
        CancellationToken p_objCancellationToken = default)
    {
        Demand_Add_Permission(p_strCurrentUser, p_strCurrentFunction);
        ArgumentNullException.ThrowIfNull(p_objPreview);
        if (!p_objPreview.Can_Confirm)
            throw new CCategoryImportValidationException(p_objPreview);
        var v_arrExistingCategories = new CDM_Loai_San_Pham_Controller().F2017_sp_sel_List_Loai_San_Pham();
        var v_objCurrentPreview = CCategoryImportValidator.Revalidate(p_objPreview.Rows, v_arrExistingCategories);
        if (!v_objCurrentPreview.Can_Confirm)
            throw new CCategoryImportValidationException(v_objCurrentPreview);
        await Import_Categories_Async(
            v_objCurrentPreview.Rows.Where(v_objRow => v_objRow.Is_Valid).ToArray(),
            p_strCurrentUser,
            p_strCurrentFunction,
            p_objCancellationToken);
        return v_objCurrentPreview.Valid_Rows;
    }

    internal async Task Import_Categories_Async(
        IReadOnlyList<CCategoryImportRow> p_arrRows,
        string p_strCurrent_User,
        string p_strCurrent_Function,
        CancellationToken p_objCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(p_arrRows);
        if (p_arrRows.Count == 0)
            throw new ArgumentException("The category import batch cannot be empty.", nameof(p_arrRows));

        CDM_Loai_San_Pham_Controller v_objCategoryController = new CDM_Loai_San_Pham_Controller();
        await using SqlConnection v_objConnection = new SqlConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String);
        await v_objConnection.OpenAsync(p_objCancellationToken);
        using SqlTransaction v_objTransaction = v_objConnection.BeginTransaction();
        try
        {
            foreach (CCategoryImportRow v_objRow in p_arrRows)
            {
                p_objCancellationToken.ThrowIfCancellationRequested();
                CDM_Loai_San_Pham v_objCategory = new CDM_Loai_San_Pham
                {
                    Auto_ID = 0,
                    Ma_LSP = v_objRow.Ma_LSP,
                    Ten_LSP = v_objRow.Ten_LSP,
                    Ghi_Chu = v_objRow.Ghi_Chu
                };
                try
                {
                    v_objCategory.Auto_ID = v_objCategoryController.F2017_sp_ins_Loai_San_Pham(v_objConnection, v_objTransaction, v_objCategory, p_strCurrent_User, p_strCurrent_Function);
                }
                catch (SqlException p_objException)
                {
                    string v_strMessage = p_objException.Number switch
                    {
                        2601 or 2627 or 51011 => $"Dòng {v_objRow.Row_Number}: Mã hoặc tên loại sản phẩm '{v_objRow.Ma_LSP} / {v_objRow.Ten_LSP}' đã tồn tại; toàn bộ lô đã được hủy.",
                        51010 => $"Dòng {v_objRow.Row_Number}: Mã loại sản phẩm không được để trống; toàn bộ lô đã được hủy.",
                        51012 => $"Dòng {v_objRow.Row_Number}: Tên loại sản phẩm không được để trống; toàn bộ lô đã được hủy.",
                        _ => $"Dòng {v_objRow.Row_Number}: Không thể lưu loại sản phẩm; toàn bộ lô đã được hủy."
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
            CCache_Loai_San_Pham.Load_Cache_Loai_San_Pham();
        }
        catch (Exception p_objException)
        {
            throw new InvalidOperationException(
                "Import loại sản phẩm đã commit thành công, nhưng cache loại sản phẩm chưa được làm mới. Dữ liệu vẫn đã lưu trong cơ sở dữ liệu; cache sẽ được nạp lại ở lần tải tiếp theo.",
                p_objException);
        }
    }

    private void Demand_Add_Permission(string p_strCurrentUser, string p_strCurrentFunction)
    {
        if (string.IsNullOrWhiteSpace(p_strCurrentUser) ||
            string.IsNullOrWhiteSpace(p_strCurrentFunction) ||
            !m_objPermissionResolver.Has_Add_Permission(p_strCurrentUser, p_strCurrentFunction))
            throw new UnauthorizedAccessException("Bạn không có quyền thêm loại sản phẩm.");
    }

    private sealed class CCategoryImportPermissionResolver : ICategoryImportPermissionResolver
    {
        public bool Has_Add_Permission(string p_strUserName, string p_strFunctionCode) =>
            CCommonFunction.Get_Chuc_Nang_By_User(p_strUserName, p_strFunctionCode)?.Is_Have_Add_Permission == true;
    }
}

public sealed class CCategoryImportValidationException : Exception
{
    public CCategoryImportPreviewResult PreviewResult { get; }

    public CCategoryImportValidationException(CCategoryImportPreviewResult p_objPreview)
        : base(string.Join(Environment.NewLine, p_objPreview.Errors.Select(v_objError => v_objError.Message).Distinct(StringComparer.Ordinal)))
    {
        PreviewResult = p_objPreview;
    }
}
