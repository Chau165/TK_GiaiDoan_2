using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseReportRefactorTests
{
    private static readonly string[] StoreNames =
    [
        "sp_BC_Chi_Tiet_Nhap",
        "sp_BC_Chi_Tiet_Xuat",
        "sp_BC_Chi_Tiet_Nhap_Page",
        "sp_BC_Chi_Tiet_Xuat_Page",
        "sp_BC_Xuat_Nhap_Ton",
        "sp_BC_Xuat_Nhap_Ton_Page",
        "sp_BC_Ton_Kho_Hien_Tai_Page"
    ];

    [Fact]
    public void Report_controller_exposes_exactly_seven_synchronous_store_methods()
    {
        var v_objControllerType = typeof(CWarehouseReport_Controller);
        var v_arrPublicMethods = v_objControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Equal(StoreNames.OrderBy(v_strName => v_strName), v_arrPublicMethods.Select(v_objMethod => v_objMethod.Name).OrderBy(v_strName => v_strName));
        foreach (var v_strStoreName in StoreNames)
        {
            var v_objMethod = Assert.Single(v_arrPublicMethods, v_objMethod => v_objMethod.Name == v_strStoreName);
            Assert.False(typeof(Task).IsAssignableFrom(v_objMethod.ReturnType));
        }

        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseReport_Controller.cs"));
        Assert.DoesNotContain("Task.FromResult", v_strSource);
        Assert.DoesNotContain("Task.CompletedTask", v_strSource);
        Assert.DoesNotContain("p_bIs_Receipt", v_strSource);
        Assert.DoesNotContain("p_bRead_Current_Balance", v_strSource);
        Assert.DoesNotContain("p_strProcedure", v_strSource);
        Assert.DoesNotContain("sp_DM_Kho_User_List_Allowed", v_strSource);
        Assert.Contains("CUtility.Map_Row_To_Entity<T>", v_strSource);
        Assert.Contains("Total_Count", v_strSource);
        Assert.Contains("Tables[1].Rows", v_strSource);

        foreach (var v_strStoreName in StoreNames)
        {
            Assert.Equal(1, Regex.Matches(v_strSource, "\\\"" + Regex.Escape(v_strStoreName) + "\\\"").Count);
        }
    }

    [Fact]
    public void Report_feature_and_projection_models_are_preserved()
    {
        Assert.NotNull(typeof(CWarehouseDetailReport));
        Assert.NotNull(typeof(CWarehouseInventoryReport));

        var v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Entity", "Warehouse", "CWarehouseReportModels.cs"));
        Assert.Contains("class CWarehouseDetailReport", v_strSource);
        Assert.Contains("class CWarehouseInventoryReport", v_strSource);
        Assert.DoesNotContain("CCache_Report", v_strSource);
        Assert.DoesNotContain("CCache_Inventory", v_strSource);
        Assert.DoesNotContain("_For_Cache", v_strSource);
    }

    [Fact]
    public void Report_callers_select_named_operations_and_keep_authorized_composition_outside_data_access()
    {
        var v_strListSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));
        var v_strImportSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Services", "CWarehouseTransactionImportService.cs"));

        Assert.Contains("sp_BC_Chi_Tiet_Nhap", v_strListSource);
        Assert.Contains("sp_BC_Chi_Tiet_Xuat", v_strListSource);
        Assert.Contains("sp_BC_Chi_Tiet_Nhap_Page", v_strListSource);
        Assert.Contains("sp_BC_Chi_Tiet_Xuat_Page", v_strListSource);
        Assert.Contains("sp_BC_Xuat_Nhap_Ton(", v_strListSource);
        Assert.Contains("sp_BC_Xuat_Nhap_Ton_Page(", v_strListSource);
        Assert.Contains("sp_BC_Ton_Kho_Hien_Tai_Page(", v_strListSource);
        Assert.DoesNotContain("Detail_Report_Async(", v_strListSource);
        Assert.DoesNotContain("Detail_Report_Page_Async(", v_strListSource);
        Assert.DoesNotContain("Inventory_Report_Async(", v_strListSource);
        Assert.DoesNotContain("Inventory_Report_Page_Async(", v_strListSource);
        Assert.Contains("sp_DM_Kho_User_List_Allowed", v_strListSource);

        Assert.Contains("sp_BC_Ton_Kho_Hien_Tai_Page(", v_strImportSource);
        Assert.DoesNotContain("Inventory_Report_Page_Async(", v_strImportSource);
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
}
