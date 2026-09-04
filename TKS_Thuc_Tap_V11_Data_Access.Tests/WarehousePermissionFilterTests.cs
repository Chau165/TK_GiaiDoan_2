using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehousePermissionFilterTests
{
    [Fact]
    public void Add_permission_shows_only_warehouses_not_assigned_to_selected_user()
    {
        var v_arrWarehouses = new List<CWarehouseLookup>
        {
            new() { Auto_ID = 1, Name = "Kho Hồ Chí Minh" },
            new() { Auto_ID = 2, Name = "Kho Hà Nội" },
            new() { Auto_ID = 3, Name = "Kho Bình Dương" }
        };
        var v_arrPermissions = new List<CWarehouseMaster>
        {
            new() { Auto_ID = 10, Login_Name = "thuctap_kho", Related_ID = 1 },
            new() { Auto_ID = 11, Login_Name = "thuctap_kho", Related_ID = 2 },
            new() { Auto_ID = 12, Login_Name = "other_user", Related_ID = 3 }
        };

        var v_arrAvailable = CWarehousePermissionFilter.List_Available_Warehouses(
            v_arrWarehouses, v_arrPermissions, "thuctap_kho");

        var v_arrAvailableIds = v_arrAvailable.Select(p_objWarehouse => p_objWarehouse.Auto_ID).ToArray();
        Assert.Equal(new long[] { 3 }, v_arrAvailableIds);
    }

    [Fact]
    public void Edit_permission_keeps_current_warehouse_but_excludes_other_assignments()
    {
        var v_arrWarehouses = new List<CWarehouseLookup>
        {
            new() { Auto_ID = 1, Name = "Kho Hồ Chí Minh" },
            new() { Auto_ID = 2, Name = "Kho Hà Nội" },
            new() { Auto_ID = 3, Name = "Kho Bình Dương" }
        };
        var v_arrPermissions = new List<CWarehouseMaster>
        {
            new() { Auto_ID = 10, Login_Name = "thuctap_kho", Related_ID = 1 },
            new() { Auto_ID = 11, Login_Name = "thuctap_kho", Related_ID = 2 }
        };

        var v_arrAvailable = CWarehousePermissionFilter.List_Available_Warehouses(
            v_arrWarehouses, v_arrPermissions, "thuctap_kho", 10);

        var v_arrAvailableIds = v_arrAvailable.Select(p_objWarehouse => p_objWarehouse.Auto_ID).ToArray();
        Assert.Equal(new long[] { 1, 3 }, v_arrAvailableIds);
    }
}
