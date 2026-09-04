using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

public static class CWarehousePermissionFilter
{
    public static List<CWarehouseLookup> List_Available_Warehouses(
        IEnumerable<CWarehouseLookup> p_arrAllWarehouses,
        IEnumerable<CWarehouseMaster> p_arrPermissions,
        string p_strLogin_Name,
        long p_iCurrentPermission_ID = 0)
    {
        var v_setAssignedWarehouseIds = p_arrPermissions
            .Where(p_objPermission =>
                string.Equals(p_objPermission.Login_Name, p_strLogin_Name, StringComparison.OrdinalIgnoreCase)
                && p_objPermission.Auto_ID != p_iCurrentPermission_ID)
            .Select(p_objPermission => p_objPermission.Related_ID)
            .ToHashSet();

        return p_arrAllWarehouses
            .Where(p_objWarehouse => !v_setAssignedWarehouseIds.Contains(p_objWarehouse.Auto_ID))
            .ToList();
    }
}
