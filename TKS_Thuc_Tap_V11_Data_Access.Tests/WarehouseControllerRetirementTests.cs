using TKS_Thuc_Tap_V11_Data_Access.Controller.BC;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Common;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Controller.XNK;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseControllerRetirementTests
{
    [Fact]
    public void Obsolete_generic_business_controllers_have_no_active_production_callers()
    {
        var v_strRepositoryRoot = FindRepositoryRoot();
        var v_arrRetiredControllerNames = new[]
        {
            "CWarehouseMaster_Controller",
            "CWarehouseDocument_Controller",
            "CWarehousePermission_Controller"
        };
        var v_arrProductionDirectories = new[]
        {
            Path.Combine(v_strRepositoryRoot, "TKS_Thuc_Tap_V11_Data_Access"),
            Path.Combine(v_strRepositoryRoot, "TKS_Thuc_Tap_V11_Web_Danh_Muc")
        };
        var v_arrProductionFiles = v_arrProductionDirectories
            .SelectMany(v_strDirectory => Directory.EnumerateFiles(v_strDirectory, "*", SearchOption.AllDirectories))
            .Where(v_strPath => Path.GetExtension(v_strPath) is ".cs" or ".razor")
            .Where(v_strPath => !v_strPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(v_strPath => !v_strPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var v_strControllerName in v_arrRetiredControllerNames)
        {
            foreach (var v_strSourcePath in v_arrProductionFiles)
            {
                Assert.DoesNotContain(v_strControllerName, File.ReadAllText(v_strSourcePath), StringComparison.Ordinal);
            }
        }

        Assert.False(File.Exists(Path.Combine(v_strRepositoryRoot,
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseMaster_Controller.cs")));
        Assert.False(File.Exists(Path.Combine(v_strRepositoryRoot,
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseDocument_Controller.cs")));
        Assert.False(File.Exists(Path.Combine(v_strRepositoryRoot,
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehousePermission_Controller.cs")));
    }

    [Fact]
    public void Generic_base_is_retained_only_for_the_four_active_support_and_xnk_controllers()
    {
        var v_arrActiveSubclasses = typeof(CWarehouse_Controller_Base).Assembly.GetTypes()
            .Where(v_objType => v_objType != typeof(CWarehouse_Controller_Base))
            .Where(v_objType => typeof(CWarehouse_Controller_Base).IsAssignableFrom(v_objType))
            .Select(v_objType => v_objType.Name)
            .OrderBy(v_strName => v_strName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "CWarehouseDocumentSupport_Controller",
                "CWarehouseMasterSupport_Controller",
                "CXNK_Nhap_Kho_Controller",
                "CXNK_Xuat_Kho_Controller"
            }.OrderBy(v_strName => v_strName, StringComparer.Ordinal),
            v_arrActiveSubclasses);
        Assert.NotNull(typeof(CWarehouseReport_Controller));

        var v_strBaseSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Common", "CWarehouse_Controller_Base.cs"));
        Assert.Contains("List_From_Procedure", v_strBaseSource);
        Assert.Contains("Page_From_Procedure", v_strBaseSource);
        Assert.Contains("Scalar_ID", v_strBaseSource);
        Assert.Contains("Execute_Procedure", v_strBaseSource);
    }

    [Fact]
    public void Remaining_generic_models_have_active_support_or_ui_projection_owners()
    {
        var v_strRoot = FindRepositoryRoot();
        var v_strDataAccess = Path.Combine(v_strRoot, "TKS_Thuc_Tap_V11_Data_Access");
        var v_strWarehouseList = File.ReadAllText(Path.Combine(v_strRoot,
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));
        var v_strMasterSupport = File.ReadAllText(Path.Combine(v_strDataAccess,
            "Controller", "DM", "CWarehouseMasterSupport_Controller.cs"));
        var v_strReportController = File.ReadAllText(Path.Combine(v_strDataAccess,
            "Controller", "BC", "CWarehouseReport_Controller.cs"));

        Assert.NotNull(typeof(CWarehouseLookup));
        Assert.NotNull(typeof(CWarehousePagedResult<>));
        Assert.NotNull(typeof(CWarehouseMaster));
        Assert.NotNull(typeof(CWarehousePermission));
        Assert.NotNull(typeof(CWarehouseDetailReport));
        Assert.NotNull(typeof(CWarehouseInventoryReport));
        Assert.NotNull(typeof(CWarehouseDocument));
        Assert.NotNull(typeof(CWarehouseDocumentDetail));
        Assert.Contains("CWarehouseLookup", v_strMasterSupport);
        Assert.Contains("CWarehouseMaster", v_strMasterSupport);
        Assert.Contains("CWarehousePermission", v_strWarehouseList);
        Assert.Contains("CWarehouseDocument", v_strWarehouseList);
        Assert.Contains("CWarehouseDocumentDetail", v_strWarehouseList);
        Assert.Contains("CWarehouseDetailReport", v_strReportController);
        Assert.Contains("CWarehouseInventoryReport", v_strReportController);
    }

    private static string FindRepositoryRoot()
    {
        for (var v_objDirectory = new DirectoryInfo(AppContext.BaseDirectory); v_objDirectory is not null; v_objDirectory = v_objDirectory.Parent)
        {
            if (Directory.Exists(Path.Combine(v_objDirectory.FullName, "TKS_Thuc_Tap_V11_Data_Access")) &&
                File.Exists(Path.Combine(v_objDirectory.FullName, "Database", "WarehouseModule.Procedures.sql")))
            {
                return v_objDirectory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Warehouse repository root was not found from the test output directory.");
    }
}
