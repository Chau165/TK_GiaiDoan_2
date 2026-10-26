using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Utility;

public static class CWarehousePermissionFilter
{
    public static List<CWarehouseLookup> List_Available_Warehouses(
        IEnumerable<CWarehouseLookup> p_arrAllWarehouses,
        IEnumerable<CWarehousePermission> p_arrPermissions,
        string p_strLogin_Name,
        long p_iCurrentPermission_ID = 0)
    {
        ArgumentNullException.ThrowIfNull(p_arrPermissions);
        HashSet<long> v_arrAssigned_IDs = new HashSet<long>();
        foreach (CWarehousePermission v_objPermission in p_arrPermissions)
        {
            if (string.Equals(v_objPermission.Login_Name, p_strLogin_Name, StringComparison.OrdinalIgnoreCase)
                && v_objPermission.Permission_ID != p_iCurrentPermission_ID)
            {
                v_arrAssigned_IDs.Add(v_objPermission.Warehouse_ID);
            }
        }
        ArgumentNullException.ThrowIfNull(p_arrAllWarehouses);
        List<CWarehouseLookup> v_arrResult = new List<CWarehouseLookup>();
        foreach (CWarehouseLookup v_objWarehouse in p_arrAllWarehouses)
        {
            if (!v_arrAssigned_IDs.Contains(v_objWarehouse.Auto_ID))
            {
                v_arrResult.Add(v_objWarehouse);
            }
        }
        return v_arrResult;
    }
}
