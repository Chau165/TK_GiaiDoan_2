using System.Data;
using System.Reflection;
using System.Threading.Tasks;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehousePermissionRefactorTests
{
    private const string ControllerNamespace = "TKS_Thuc_Tap_V11_Data_Access.Controller.DM.";
    private const string EntityNamespace = "TKS_Thuc_Tap_V11_Data_Access.Entity.DM.";

    private static readonly string[] StoreNames =
    [
        "F2015_sp_sel_List_Kho_User",
        "F2015_sp_sel_List_Sys_Thanh_Vien_Cho_Phan_Quyen_Kho",
        "F2015_sp_ins_Kho_User",
        "F2015_sp_upd_Kho_User",
        "F2015_sp_del_Kho_User",
        "sp_DM_Kho_User_List_Allowed",
        "sp_DM_Kho_User_Page"
    ];

    [Fact]
    public void Permission_feature_exposes_synchronous_one_method_per_store()
    {
        var v_objAssembly = typeof(CWarehouseReport_Controller).Assembly;
        var v_objControllerType = v_objAssembly.GetType(ControllerNamespace + "CDM_Kho_User_Controller");

        Assert.NotNull(v_objControllerType);

        var v_arrPublicMethods = v_objControllerType!.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(StoreNames.OrderBy(v_strName => v_strName), v_arrPublicMethods.Select(v_objMethod => v_objMethod.Name).OrderBy(v_strName => v_strName));

        foreach (var v_strStoreName in StoreNames)
        {
            var v_objMethod = Assert.Single(v_arrPublicMethods, v_objMethod => v_objMethod.Name == v_strStoreName);
            Assert.False(typeof(Task).IsAssignableFrom(v_objMethod.ReturnType));
        }

        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "DM", "CDM_Kho_User_Controller.cs"));
        Assert.DoesNotContain("Task.FromResult", v_strSource);
        Assert.DoesNotContain("Task.CompletedTask", v_strSource);
        Assert.DoesNotContain("Save_Kho_User_Async", v_strSource);
        Assert.DoesNotContain("Map_Permission", v_strSource);
        Assert.DoesNotContain("p_strProcedure", v_strSource);

        foreach (var v_strStoreName in StoreNames)
            Assert.Contains("\"" + v_strStoreName + "\"", v_strSource);
    }

    [Fact]
    public void Legacy_permission_model_remains_a_ui_projection()
    {
        var v_dtData = new DataTable();
        v_dtData.Columns.Add("Permission_ID", typeof(long));
        v_dtData.Columns.Add("Login_Name", typeof(string));
        v_dtData.Columns.Add("User_Name", typeof(string));
        v_dtData.Columns.Add("Warehouse_ID", typeof(long));
        v_dtData.Columns.Add("Warehouse_Name", typeof(string));
        var v_row = v_dtData.Rows.Add(7L, "warehouse.user", "Warehouse User", 11L, "Kho Hà Nội");

        CWarehousePermission v_objPermission = CUtility.Map_Row_To_Entity<CWarehousePermission>(v_row);

        Assert.Equal(7L, v_objPermission.Permission_ID);
        Assert.Equal("warehouse.user", v_objPermission.Login_Name);
        Assert.Equal("Warehouse User", v_objPermission.User_Name);
        Assert.Equal(11L, v_objPermission.Warehouse_ID);
        Assert.Equal("Kho Hà Nội", v_objPermission.Warehouse_Name);
    }

    [Fact]
    public void Permission_list_and_page_map_directly_to_the_business_entity()
    {
        var v_objAssembly = typeof(CWarehouseReport_Controller).Assembly;
        var v_objEntityType = v_objAssembly.GetType(EntityNamespace + "CDM_Kho_User");
        Assert.NotNull(v_objEntityType);

        var v_dtData = new DataTable();
        v_dtData.Columns.Add("Auto_ID", typeof(long));
        v_dtData.Columns.Add("Ma_Dang_Nhap", typeof(string));
        v_dtData.Columns.Add("Ho_Ten", typeof(string));
        v_dtData.Columns.Add("Kho_ID", typeof(long));
        v_dtData.Columns.Add("Ten_Kho", typeof(string));
        var v_row = v_dtData.Rows.Add(7L, "warehouse.user", "Warehouse User", 11L, "Kho Hà Nội");

        var v_objMapper = Assert.Single(typeof(CUtility).GetMethods(), v_objMethod =>
            v_objMethod.Name == "Map_Row_To_Entity" &&
            v_objMethod.IsGenericMethodDefinition &&
            v_objMethod.GetParameters().Length == 1 &&
            v_objMethod.GetParameters()[0].ParameterType == typeof(DataRow));
        var v_objMapped = v_objMapper.MakeGenericMethod(v_objEntityType!).Invoke(null, [v_row]);

        Assert.Equal(7L, v_objEntityType!.GetProperty("Auto_ID")!.GetValue(v_objMapped));
        Assert.Equal("warehouse.user", v_objEntityType.GetProperty("Ma_Dang_Nhap")!.GetValue(v_objMapped));
        Assert.Equal("Warehouse User", v_objEntityType.GetProperty("Ho_Ten")!.GetValue(v_objMapped));
        Assert.Equal(11L, v_objEntityType.GetProperty("Kho_ID")!.GetValue(v_objMapped));
        Assert.Equal("Kho Hà Nội", v_objEntityType.GetProperty("Ten_Kho")!.GetValue(v_objMapped));

        var v_strSqlSource = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        foreach (var v_strStoreName in new[] { "F2015_sp_sel_List_Kho_User", "sp_DM_Kho_User_Page" })
        {
            var v_strStore = GetProcedure(v_strSqlSource, v_strStoreName);
            foreach (var v_strColumn in new[] { "u.Auto_ID", "u.Ma_Dang_Nhap", "tv.Ho_Ten", "u.Kho_ID", "k.Ten_Kho" })
                Assert.Contains(v_strColumn, v_strStore);
            Assert.DoesNotContain(" AS Name", v_strStore);
            Assert.DoesNotContain(" AS Login_Name", v_strStore);
            Assert.DoesNotContain(" AS Ghi_Chu", v_strStore);
            Assert.DoesNotContain(" AS Related_ID,", v_strStore);
        }
    }

    [Fact]
    public void Permission_callers_use_explicit_store_methods_and_authorized_lookup()
    {
        var v_strListSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));
        var v_strImportSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Services", "CWarehouseTransactionImportService.cs"));

        Assert.Contains("CDM_Kho_User_Controller", v_strListSource);
        Assert.Contains("F2015_sp_sel_List_Kho_User", v_strListSource);
        Assert.Contains("sp_DM_Kho_User_Page", v_strListSource);
        Assert.Contains("F2015_sp_ins_Kho_User", v_strListSource);
        Assert.Contains("F2015_sp_upd_Kho_User", v_strListSource);
        Assert.Contains("F2015_sp_del_Kho_User", v_strListSource);
        Assert.Contains("F2015_sp_sel_List_Sys_Thanh_Vien_Cho_Phan_Quyen_Kho", v_strListSource);
        Assert.Contains("sp_DM_Kho_User_List_Allowed", v_strListSource);
        Assert.DoesNotContain("CWarehousePermission_Controller", v_strListSource);
        Assert.DoesNotContain("m_objPermissionController.Save_Kho_User_Async", v_strListSource);
        var v_iAddBranch = v_strListSource.IndexOf("if (v_iPermission_ID == 0)", StringComparison.Ordinal);
        var v_iInsertCall = v_strListSource.IndexOf("F2015_sp_ins_Kho_User(", StringComparison.Ordinal);
        var v_iUpdateBranch = v_strListSource.IndexOf("else", v_iInsertCall, StringComparison.Ordinal);
        var v_iUpdateCall = v_strListSource.IndexOf("F2015_sp_upd_Kho_User(", StringComparison.Ordinal);
        Assert.True(v_iAddBranch >= 0 && v_iInsertCall > v_iAddBranch && v_iUpdateBranch > v_iInsertCall && v_iUpdateCall > v_iUpdateBranch);

        Assert.Contains("sp_DM_Kho_User_List_Allowed", v_strImportSource);
        Assert.DoesNotContain("CWarehousePermission_Controller", v_strImportSource);
        Assert.DoesNotContain("List_Authorized_Warehouses_Async", v_strImportSource);
        Assert.DoesNotContain("fallback", v_strImportSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Authorized_warehouse_store_keeps_login_validation_and_assignment_filter()
    {
        var v_strSqlSource = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strStore = GetProcedure(v_strSqlSource, "sp_DM_Kho_User_List_Allowed");

        Assert.Contains("LTRIM(RTRIM(ISNULL(@Ma_Dang_Nhap, N'')))", v_strStore);
        Assert.Contains("IF @Ma_Dang_Nhap = N'' OR NOT EXISTS", v_strStore);
        Assert.Contains("WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap ORDER BY k.Ten_Kho", v_strStore);
    }

    [Fact]
    public void Previous_permission_controller_is_retired()
    {
        var v_objAssembly = typeof(CWarehouseReport_Controller).Assembly;
        Assert.Null(v_objAssembly.GetType(
            "TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse.CWarehousePermission_Controller"));
        var v_strControllerPath = Path.Combine(
            FindRepositoryRoot(),
            "TKS_Thuc_Tap_V11_Data_Access",
            "Controller",
            "Warehouse",
            "CWarehousePermission_Controller.cs");
        Assert.False(File.Exists(v_strControllerPath));
    }

    [Fact]
    public void Master_support_controller_does_not_route_kho_user_stores()
    {
        var v_strMasterSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseMasterSupport_Controller.cs"));

        Assert.DoesNotContain("F2015_sp_sel_List_Kho_User", v_strMasterSource);
        Assert.DoesNotContain("sp_DM_Kho_User_Page", v_strMasterSource);
        Assert.DoesNotContain("sp_DM_Kho_User_List_Allowed", v_strMasterSource);
    }

    [Fact]
    public void Master_model_and_shared_master_stores_do_not_expose_kho_user_routing()
    {
        Assert.Null(typeof(CWarehouseMaster).GetProperty("Login_Name"));
        Assert.NotNull(typeof(CWarehousePermission).GetProperty("Login_Name"));

        var v_strSource = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_iMasterListStart = v_strSource.IndexOf("CREATE OR ALTER PROCEDURE dbo.F2009_sp_sel_List_Kho", StringComparison.Ordinal);
        var v_iLookupStart = v_strSource.IndexOf("CREATE OR ALTER PROCEDURE dbo.F2009_sp_sel_List_Kho_Lookup", StringComparison.Ordinal);
        var v_iMasterPageStart = v_strSource.IndexOf("CREATE OR ALTER PROCEDURE dbo.sp_DM_Master_Page", StringComparison.Ordinal);
        var v_iDocumentPageStart = v_strSource.IndexOf("CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Page", StringComparison.Ordinal);

        Assert.True(v_iMasterListStart >= 0 && v_iLookupStart > v_iMasterListStart);
        Assert.True(v_iMasterPageStart >= 0 && v_iDocumentPageStart > v_iMasterPageStart);
        Assert.DoesNotContain("KhoUser", v_strSource[v_iMasterListStart..v_iLookupStart]);
        Assert.DoesNotContain("KhoUser", v_strSource[v_iMasterPageStart..v_iDocumentPageStart]);
    }

    [Fact]
    public void Permission_delete_executes_only_its_named_store()
    {
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "DM", "CDM_Kho_User_Controller.cs"));
        var v_strSqlSource = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strDelete = GetProcedure(v_strSqlSource, "F2015_sp_del_Kho_User");

        Assert.Contains("F2015_sp_del_Kho_User", v_strController);
        Assert.DoesNotContain("Execute_Procedure(\"sp_DM_Delete\", \"KhoUser\"", v_strController);
        Assert.Contains("DELETE FROM dbo.tbl_DM_Kho_User WHERE Auto_ID=@Auto_ID;", v_strDelete);
    }

    private static string GetProcedure(string p_strSource, string p_strStoreName)
    {
        var v_iStart = p_strSource.IndexOf("CREATE OR ALTER PROCEDURE dbo." + p_strStoreName, StringComparison.Ordinal);
        Assert.True(v_iStart >= 0);

        var v_iEnd = p_strSource.IndexOf("\nGO", v_iStart, StringComparison.Ordinal);
        Assert.True(v_iEnd > v_iStart);
        return p_strSource[v_iStart..v_iEnd];
    }

    private static string FindRepositoryPath(params string[] p_arrParts)
    {
        for (var v_objDirectory = new DirectoryInfo(AppContext.BaseDirectory); v_objDirectory is not null; v_objDirectory = v_objDirectory.Parent)
        {
            var v_strCandidate = Path.Combine(new[] { v_objDirectory.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_strCandidate))
                return v_strCandidate;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(p_arrParts)}");
    }

    private static string FindRepositoryRoot()
    {
        for (var v_objDirectory = new DirectoryInfo(AppContext.BaseDirectory); v_objDirectory is not null; v_objDirectory = v_objDirectory.Parent)
        {
            if (Directory.Exists(Path.Combine(v_objDirectory.FullName, "TKS_Thuc_Tap_V11_Data_Access")) &&
                Directory.Exists(Path.Combine(v_objDirectory.FullName, "Database")))
                return v_objDirectory.FullName;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
