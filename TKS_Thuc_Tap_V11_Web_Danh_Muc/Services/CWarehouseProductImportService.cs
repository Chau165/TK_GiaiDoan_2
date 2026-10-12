using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Common.Common;

namespace TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;

public interface IProductImportPermissionResolver
{
    bool Has_Add_Permission(string p_strUserName, string p_strFunctionCode);
}

public sealed class CWarehouseProductImportService
{
    private readonly CProduct_Xlsx_Reader m_objReader;
    private readonly CWarehouseProductSupport_Service m_objProductSupportService;
    private readonly IProductImportPermissionResolver m_objPermissionResolver;

    public CWarehouseProductImportService()
        : this(new CProduct_Xlsx_Reader(), new CWarehouseProductSupport_Service(), new CProductImportPermissionResolver())
    {
    }

    public CWarehouseProductImportService(IProductImportPermissionResolver p_objPermissionResolver)
        : this(new CProduct_Xlsx_Reader(), new CWarehouseProductSupport_Service(), p_objPermissionResolver)
    {
    }

    private CWarehouseProductImportService(
        CProduct_Xlsx_Reader p_objReader,
        CWarehouseProductSupport_Service p_objProductSupportService,
        IProductImportPermissionResolver p_objPermissionResolver)
    {
        m_objReader = p_objReader;
        m_objProductSupportService = p_objProductSupportService;
        m_objPermissionResolver = p_objPermissionResolver;
    }

    public async Task<CProductImportPreviewResult> PreviewAsync(
        string p_strFileName,
        Stream p_objSource,
        string p_strCurrentUser,
        string p_strCurrentFunction,
        CancellationToken p_objCancellationToken = default)
    {
        Demand_Add_Permission(p_strCurrentUser, p_strCurrentFunction);
        if (!string.Equals(Path.GetExtension(p_strFileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Chỉ chấp nhận file Excel .xlsx theo template Sản phẩm.");

        var v_objWorksheet = await m_objReader.ReadAsync(p_objSource, p_objCancellationToken);
        var (v_arrCategories, v_arrUnits, v_arrProducts) = await Load_Reference_Data_Async(p_objCancellationToken);
        return CProductImportValidator.Preview(v_objWorksheet, v_arrCategories, v_arrUnits, v_arrProducts);
    }

    public async Task<int> ConfirmAsync(
        CProductImportPreviewResult p_objPreview,
        string p_strCurrentUser,
        string p_strCurrentFunction,
        CancellationToken p_objCancellationToken = default)
    {
        Demand_Add_Permission(p_strCurrentUser, p_strCurrentFunction);
        ArgumentNullException.ThrowIfNull(p_objPreview);
        if (!p_objPreview.Can_Confirm)
            throw new CProductImportValidationException(p_objPreview);

        var (v_arrCategories, v_arrUnits, v_arrProducts) = await Load_Reference_Data_Async(p_objCancellationToken);
        var v_objCurrentPreview = CProductImportValidator.Revalidate(
            p_objPreview.Rows,
            v_arrCategories,
            v_arrUnits,
            v_arrProducts);
        if (!v_objCurrentPreview.Can_Confirm)
            throw new CProductImportValidationException(v_objCurrentPreview);

        await Import_Products_Async(
            v_objCurrentPreview.Rows.Where(v_objRow => v_objRow.Is_Valid).ToArray(),
            p_strCurrentUser,
            p_strCurrentFunction,
            p_objCancellationToken);
        return v_objCurrentPreview.Valid_Rows;
    }

    internal async Task Import_Products_Async(
        IReadOnlyList<CProductImportRow> p_arrRows,
        string p_strCurrent_User,
        string p_strCurrent_Function,
        CancellationToken p_objCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(p_arrRows);
        if (p_arrRows.Count == 0)
            throw new ArgumentException("The product import batch cannot be empty.", nameof(p_arrRows));

        CDM_San_Pham_Controller v_objProductController = new CDM_San_Pham_Controller();
        await using SqlConnection v_objConnection = new SqlConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String);
        await v_objConnection.OpenAsync(p_objCancellationToken);
        using SqlTransaction v_objTransaction = v_objConnection.BeginTransaction();

        try
        {
            foreach (CProductImportRow v_objRow in p_arrRows)
            {
                p_objCancellationToken.ThrowIfCancellationRequested();
                CWarehouseMaster v_objProduct = new CWarehouseMaster
                {
                    Auto_ID = 0,
                    Code = v_objRow.Ma_San_Pham,
                    Name = v_objRow.Ten_San_Pham,
                    Related_ID = v_objRow.Loai_San_Pham_ID,
                    Related_ID_2 = v_objRow.Don_Vi_Tinh_ID,
                    Ghi_Chu = v_objRow.Ghi_Chu
                };

                try
                {
                    v_objProduct.Auto_ID = v_objProductController.F2018_sp_ins_San_Pham(v_objConnection, v_objTransaction, v_objProduct, p_strCurrent_User, p_strCurrent_Function);
                }
                catch (SqlException p_objException)
                {
                    string v_strMessage = p_objException.Number switch
                    {
                        2601 or 2627 or 51021 => $"Dòng {v_objRow.Row_Number}: Mã sản phẩm '{v_objRow.Ma_San_Pham}' đã tồn tại; toàn bộ lô đã được hủy.",
                        51023 => $"Dòng {v_objRow.Row_Number}: Loại sản phẩm không còn tồn tại; toàn bộ lô đã được hủy.",
                        51024 => $"Dòng {v_objRow.Row_Number}: Đơn vị tính không còn tồn tại; toàn bộ lô đã được hủy.",
                        _ => $"Dòng {v_objRow.Row_Number}: Không thể lưu sản phẩm; toàn bộ lô đã được hủy."
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
            throw new UnauthorizedAccessException("Bạn không có quyền thêm sản phẩm.");
        }
    }

    private async Task<(List<CWarehouseMaster> Categories, List<CWarehouseMaster> Units, List<CWarehouseMaster> Products)> Load_Reference_Data_Async(
        CancellationToken p_objCancellationToken)
    {
        var v_arrCategories = new CDM_Loai_San_Pham_Controller().F2017_sp_sel_List_Loai_San_Pham();
        var v_arrUnits = new CDM_Don_Vi_Tinh_Controller().F2016_sp_sel_List_Don_Vi_Tinh();
        var v_arrProducts = await m_objProductSupportService.List_Product_Masters_Async(p_objCancellationToken);
        return (v_arrCategories, v_arrUnits, v_arrProducts);
    }

    private sealed class CProductImportPermissionResolver : IProductImportPermissionResolver
    {
        public bool Has_Add_Permission(string p_strUserName, string p_strFunctionCode)
        {
            return CCommonFunction.Get_Chuc_Nang_By_User(p_strUserName, p_strFunctionCode)?.Is_Have_Add_Permission == true;
        }
    }
}

public sealed class CProductImportValidationException : Exception
{
    public CProductImportPreviewResult PreviewResult { get; }

    public CProductImportValidationException(CProductImportPreviewResult p_objPreview)
        : base(string.Join(Environment.NewLine, p_objPreview.Errors.Select(v_objError => v_objError.Message).Distinct(StringComparer.Ordinal)))
    {
        PreviewResult = p_objPreview;
    }
}
