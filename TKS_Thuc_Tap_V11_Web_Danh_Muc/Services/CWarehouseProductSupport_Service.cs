using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;

// Temporary product query owner; its name is a project convention, not a company Store convention.
public sealed class CWarehouseProductSupport_Service
{
    private readonly CWarehouseMasterSupport_Controller m_objSupportController = new CWarehouseMasterSupport_Controller();

    public async Task<HashSet<string>> List_Product_Codes_Async(CancellationToken p_objCancellationToken = default)
    {
        List<CWarehouseMaster> v_arrProducts = await List_Product_Masters_Async(p_objCancellationToken);
        return v_arrProducts
            .Where(v_objProduct => !string.IsNullOrWhiteSpace(v_objProduct.Code))
            .Select(v_objProduct => v_objProduct.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<List<CWarehouseMaster>> List_Product_Masters_Async(CancellationToken p_objCancellationToken = default)
    {
        const int v_iPage_Size = 500;
        List<CWarehouseMaster> v_arrProducts = new List<CWarehouseMaster>();
        int v_iFetched = 0;
        int v_iPage = 1;
        int v_iTotal = 0;

        do
        {
            p_objCancellationToken.ThrowIfCancellationRequested();
            CWarehousePagedResult<CWarehouseMaster> v_objPage = await m_objSupportController.List_Master_Page_Async("SanPham", v_iPage, v_iPage_Size);
            v_iTotal = v_objPage.Total_Count;
            v_arrProducts.AddRange(v_objPage.Items);

            v_iFetched += v_objPage.Items.Count;
            v_iPage++;
            if (v_objPage.Items.Count == 0)
                break;
        }
        while (v_iFetched < v_iTotal);

        return v_arrProducts;
    }
}
