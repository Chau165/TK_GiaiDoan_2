using System.Data;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehousePermissionRefactorTests
{
    [Fact]
    public void Warehouse_module_exposes_a_dedicated_permission_model_and_controller()
    {
        Assert.NotNull(typeof(CWarehousePermission));
        Assert.NotNull(typeof(CWarehousePermission_Controller));
    }

    [Fact]
    public void Permission_model_maps_the_dedicated_user_warehouse_projection()
    {
        var v_dtData = new DataTable();
        v_dtData.Columns.Add("Permission_ID", typeof(long));
        v_dtData.Columns.Add("Login_Name", typeof(string));
        v_dtData.Columns.Add("User_Name", typeof(string));
        v_dtData.Columns.Add("Warehouse_ID", typeof(long));
        v_dtData.Columns.Add("Warehouse_Name", typeof(string));
        var v_row = v_dtData.Rows.Add(7L, "warehouse.user", "Warehouse User", 11L, "Kho Hà Nội");

        var v_objPermission = CUtility.Map_Row_To_Entity<CWarehousePermission>(v_row);

        Assert.Equal(7L, v_objPermission.Permission_ID);
        Assert.Equal("warehouse.user", v_objPermission.Login_Name);
        Assert.Equal("Warehouse User", v_objPermission.User_Name);
        Assert.Equal(11L, v_objPermission.Warehouse_ID);
        Assert.Equal("Kho Hà Nội", v_objPermission.Warehouse_Name);
    }

    [Fact]
    public void Permission_controller_exposes_dedicated_mapping_operations()
    {
        var v_objControllerType = typeof(CWarehousePermission_Controller);

        Assert.NotNull(v_objControllerType.GetMethod(nameof(CWarehousePermission_Controller.List_Kho_User_Async)));
        Assert.NotNull(v_objControllerType.GetMethod(nameof(CWarehousePermission_Controller.List_Kho_User_Page_Async)));
        Assert.NotNull(v_objControllerType.GetMethod(nameof(CWarehousePermission_Controller.Save_Kho_User_Async)));
        Assert.NotNull(v_objControllerType.GetMethod(nameof(CWarehousePermission_Controller.Delete_Kho_User_Async)));
    }

    [Fact]
    public void Permission_controller_owns_authorization_lookup_operations()
    {
        var v_objMasterControllerType = typeof(CWarehouseMaster_Controller);
        var v_objPermissionControllerType = typeof(CWarehousePermission_Controller);

        Assert.Null(v_objMasterControllerType.GetMethod("List_Authorized_Warehouses_Async"));
        Assert.Null(v_objMasterControllerType.GetMethod("List_User_Lookup_Async"));
        Assert.NotNull(v_objPermissionControllerType.GetMethod("List_Authorized_Warehouses_Async"));
        Assert.NotNull(v_objPermissionControllerType.GetMethod("List_User_Lookup_Async"));

        var v_strMasterSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseMaster_Controller.cs"));
        var v_strPermissionSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehousePermission_Controller.cs"));

        Assert.DoesNotContain("sp_DM_Kho_User_List_Allowed", v_strMasterSource);
        Assert.DoesNotContain("sp_DM_Kho_User_User_List", v_strMasterSource);
        Assert.Contains("sp_DM_Kho_User_List_Allowed", v_strPermissionSource);
        Assert.Contains("sp_DM_Kho_User_User_List", v_strPermissionSource);
    }

    [Fact]
    public void Master_model_does_not_expose_permission_login_field()
    {
        Assert.Null(typeof(CWarehouseMaster).GetProperty("Login_Name"));
        Assert.NotNull(typeof(CWarehousePermission).GetProperty("Login_Name"));
    }

    [Fact]
    public void Master_sql_list_and_page_contracts_do_not_contain_kho_user_branch()
    {
        var v_strSource = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_iMasterListStart = v_strSource.IndexOf("CREATE OR ALTER PROCEDURE dbo.sp_DM_Master_List", StringComparison.Ordinal);
        var v_iLookupStart = v_strSource.IndexOf("CREATE OR ALTER PROCEDURE dbo.sp_DM_Lookup_List", StringComparison.Ordinal);
        var v_iMasterPageStart = v_strSource.IndexOf("CREATE OR ALTER PROCEDURE dbo.sp_DM_Master_Page", StringComparison.Ordinal);
        var v_iDocumentPageStart = v_strSource.IndexOf("CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Page", StringComparison.Ordinal);

        Assert.True(v_iMasterListStart >= 0 && v_iLookupStart > v_iMasterListStart);
        Assert.True(v_iMasterPageStart >= 0 && v_iDocumentPageStart > v_iMasterPageStart);

        var v_strMasterList = v_strSource[v_iMasterListStart..v_iLookupStart];
        var v_strMasterPage = v_strSource[v_iMasterPageStart..v_iDocumentPageStart];

        Assert.DoesNotContain("KhoUser", v_strMasterList);
        Assert.DoesNotContain("KhoUser", v_strMasterPage);
    }

    [Fact]
    public void Permission_delete_uses_a_dedicated_sql_contract()
    {
        var v_strControllerSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehousePermission_Controller.cs"));
        var v_strSqlSource = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_iDeleteStart = v_strSqlSource.IndexOf("CREATE OR ALTER PROCEDURE dbo.sp_DM_Delete", StringComparison.Ordinal);
        var v_iMasterListStart = v_strSqlSource.IndexOf("CREATE OR ALTER PROCEDURE dbo.sp_DM_Master_List", StringComparison.Ordinal);
        var v_iPermissionDeleteStart = v_strSqlSource.IndexOf("CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_Delete", StringComparison.Ordinal);
        var v_iPermissionListStart = v_strSqlSource.IndexOf("CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_List\n", StringComparison.Ordinal);

        Assert.Contains("sp_DM_Kho_User_Delete", v_strControllerSource);
        Assert.DoesNotContain("Execute_Procedure(\"sp_DM_Delete\", \"KhoUser\"", v_strControllerSource);
        Assert.True(v_iDeleteStart >= 0 && v_iMasterListStart > v_iDeleteStart);
        Assert.True(v_iPermissionDeleteStart >= 0 && v_iPermissionListStart > v_iPermissionDeleteStart);

        var v_strGenericDelete = v_strSqlSource[v_iDeleteStart..v_iMasterListStart];
        var v_strPermissionDelete = v_strSqlSource[v_iPermissionDeleteStart..v_iPermissionListStart];

        Assert.DoesNotContain("KhoUser", v_strGenericDelete);
        Assert.Contains("DELETE FROM dbo.tbl_DM_Kho_User WHERE Auto_ID=@Auto_ID;", v_strPermissionDelete);
    }

    [Fact]
    public void Master_controller_does_not_dispatch_user_warehouse_mapping()
    {
        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseMaster_Controller.cs"));

        Assert.DoesNotContain("if (p_strMaster_Type == \"KhoUser\")", v_strSource);
        Assert.DoesNotContain("\"KhoUser\" =>", v_strSource);
        Assert.DoesNotContain("sp_DM_Kho_User_List\"", v_strSource);
        Assert.DoesNotContain("sp_DM_Kho_User_Page", v_strSource);
        Assert.DoesNotContain("sp_DM_Kho_User_Save", v_strSource);
    }

    [Fact]
    public void Warehouse_page_routes_permission_mapping_to_the_permission_controller()
    {
        var v_strListSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));
        var v_strEditorSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_3_Warehouse_Edit.razor"));
        var v_strWrapperSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_3_Warehouse_Master_Edit.razor"));

        Assert.Contains("CWarehousePermission_Controller", v_strListSource);
        Assert.Contains("List_Kho_User_Async", v_strListSource);
        Assert.Contains("List_Kho_User_Page_Async", v_strListSource);
        Assert.Contains("Save_Kho_User_Async", v_strListSource);
        Assert.Contains("Delete_Kho_User_Async", v_strListSource);
        Assert.Contains("m_objPermissionController.List_Kho_User_Page_Async", v_strListSource);
        Assert.Contains("m_objPermissionController.Save_Kho_User_Async", v_strListSource);
        Assert.Contains("m_objPermissionController.Delete_Kho_User_Async", v_strListSource);
        Assert.DoesNotContain("List_Master_Async(\"KhoUser\")", v_strListSource);
        Assert.Contains("CWarehousePermission", v_strEditorSource);
        Assert.Contains("CWarehousePermission", v_strWrapperSource);
    }

    private static string FindRepositoryPath(params string[] parts)
    {
        for (var v_objDirectory = new DirectoryInfo(AppContext.BaseDirectory); v_objDirectory is not null; v_objDirectory = v_objDirectory.Parent)
        {
            var v_strCandidate = Path.Combine(new[] { v_objDirectory.FullName }.Concat(parts).ToArray());
            if (File.Exists(v_strCandidate))
                return v_strCandidate;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(parts)}");
    }
}
