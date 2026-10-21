using System.Reflection;
using System.Text.RegularExpressions;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseMasterFinalizationContractTests
{
    [Fact]
    public void Master_controller_is_retired_and_unresolved_page_support_has_a_temporary_owner()
    {
        Assembly v_objDataAccessAssembly = typeof(CDM_Kho_Controller).Assembly;

        Assert.DoesNotContain(
            v_objDataAccessAssembly.GetTypes(),
            v_objType => v_objType.Name == "CWarehouseMaster_Controller");
        string v_strDataAccessProject = FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "TKS_Thuc_Tap_V11_Data_Access.csproj");
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(v_strDataAccessProject)!,
            "Controller", "DM", "CWarehouseMaster_Controller.cs")));

        Type? v_objSupportType = v_objDataAccessAssembly.GetType(
            "TKS_Thuc_Tap_V11_Data_Access.Controller.DM.CWarehouseMasterSupport_Controller");

        Assert.NotNull(v_objSupportType);
        string[] v_arrPublicMethodNames = v_objSupportType!
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(v_objMethod => v_objMethod.Name)
            .OrderBy(v_strName => v_strName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "List_Lookup_Page_Async", "List_Master_Page_Async" },
            v_arrPublicMethodNames);

        string v_strSupportSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "DM", "CWarehouseMasterSupport_Controller.cs"));
        Assert.Contains("sp_DM_Master_Page", v_strSupportSource);
        Assert.Contains("sp_DM_Lookup_Page", v_strSupportSource);
    }

    [Fact]
    public void Product_page_helpers_have_a_separate_temporary_product_support_owner()
    {
        Assembly v_objWebAssembly = typeof(CWarehouseProductImportService).Assembly;
        Type? v_objProductSupportType = v_objWebAssembly.GetType(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc.Services.CWarehouseProductSupport_Service");

        Assert.NotNull(v_objProductSupportType);
        Assert.NotNull(v_objProductSupportType!.GetMethod("List_Product_Codes_Async"));
        Assert.NotNull(v_objProductSupportType.GetMethod("List_Product_Masters_Async"));

        string v_strSupportSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Services", "CWarehouseProductSupport_Service.cs"));
        Assert.Contains("CWarehouseMasterSupport_Controller", v_strSupportSource);
        Assert.DoesNotContain("CWarehouseMaster_Controller", v_strSupportSource);
    }

    [Fact]
    public void Master_import_services_own_one_transaction_for_each_batch()
    {
        (string ServiceName, string StoreMethod)[] v_arrContracts =
        [
            ("CWarehouseProductImportService.cs", "F2018_sp_ins_San_Pham"),
            ("CWarehouseUnitImportService.cs", "F2016_sp_ins_Don_Vi_Tinh"),
            ("CWarehouseCategoryImportService.cs", "F2017_sp_ins_Loai_San_Pham"),
            ("CWarehouseSupplierImportService.cs", "F2019_sp_ins_NCC"),
            ("CWarehouseKhoImportService.cs", "F2009_sp_ins_Kho")
        ];

        foreach ((string v_strServiceName, string v_strStoreMethod) in v_arrContracts)
        {
            string v_strSource = File.ReadAllText(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Services", v_strServiceName));

            Assert.DoesNotContain("CWarehouseMaster_Controller", v_strSource);
            Assert.Equal(1, CountOccurrences(v_strSource, "new SqlConnection("));
            Assert.Equal(1, CountOccurrences(v_strSource, ".BeginTransaction()"));
            Assert.Contains(v_strStoreMethod + "(v_objConnection, v_objTransaction", v_strSource);
            Assert.Contains("v_objTransaction.Commit();", v_strSource);
            Assert.Contains("v_objTransaction.Rollback();", v_strSource);
        }
    }

    [Fact]
    public void Entity_store_methods_use_synchronous_company_style_signatures()
    {
        (Type Controller, string ListMethod, string CacheListMethod, string LookupMethod, string InsertMethod, string UpdateMethod, string DeleteMethod)[] v_arrContracts =
        [
            (typeof(CDM_Kho_Controller), "F2009_sp_sel_List_Kho", "F2009_sp_sel_List_Kho_For_Cache", "F2009_sp_sel_List_Kho_Lookup", "F2009_sp_ins_Kho", "F2009_sp_upd_Kho", "F2009_sp_del_Kho"),
            (typeof(CDM_Don_Vi_Tinh_Controller), "F2016_sp_sel_List_Don_Vi_Tinh", "F2016_sp_sel_List_Don_Vi_Tinh_For_Cache", "F2016_sp_sel_List_Don_Vi_Tinh_Lookup", "F2016_sp_ins_Don_Vi_Tinh", "F2016_sp_upd_Don_Vi_Tinh", "F2016_sp_del_Don_Vi_Tinh"),
            (typeof(CDM_Loai_San_Pham_Controller), "F2017_sp_sel_List_Loai_San_Pham", "F2017_sp_sel_List_Loai_San_Pham_For_Cache", "F2017_sp_sel_List_Loai_San_Pham_Lookup", "F2017_sp_ins_Loai_San_Pham", "F2017_sp_upd_Loai_San_Pham", "F2017_sp_del_Loai_San_Pham"),
            (typeof(CDM_San_Pham_Controller), "F2018_sp_sel_List_San_Pham", "F2018_sp_sel_List_San_Pham_For_Cache", "F2018_sp_sel_List_San_Pham_Lookup", "F2018_sp_ins_San_Pham", "F2018_sp_upd_San_Pham", "F2018_sp_del_San_Pham"),
            (typeof(CDM_NCC_Controller), "F2019_sp_sel_List_NCC", "F2019_sp_sel_List_NCC_For_Cache", "F2019_sp_sel_List_NCC_Lookup", "F2019_sp_ins_NCC", "F2019_sp_upd_NCC", "F2019_sp_del_NCC")
        ];

        foreach ((Type v_objController, string v_strListMethod, string v_strCacheListMethod, string v_strLookupMethod, string v_strInsertMethod, string v_strUpdateMethod, string v_strDeleteMethod) in v_arrContracts)
        {
            string v_strEntityName = v_objController.Name.Replace("_Controller", "", StringComparison.Ordinal);
            Type? v_objEntity = v_objController.Assembly.GetType(
                "TKS_Thuc_Tap_V11_Data_Access.Entity.DM." + v_strEntityName);
            Assert.NotNull(v_objEntity);
            Type v_objEntityListType = typeof(List<>).MakeGenericType(v_objEntity!);
            Assert.Equal(v_objEntityListType, v_objController.GetMethod(v_strListMethod)?.ReturnType);
            Assert.Equal(v_objEntityListType, v_objController.GetMethod(v_strCacheListMethod)?.ReturnType);
            Assert.Equal(v_objEntityListType, v_objController.GetMethod(v_strLookupMethod)?.ReturnType);
            Assert.Equal(typeof(void), v_objController.GetMethod(v_strUpdateMethod)?.ReturnType);
            Assert.Equal(typeof(void), v_objController.GetMethod(v_strDeleteMethod)?.ReturnType);

            MethodInfo[] v_arrInsertMethods = v_objController
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(v_objMethod => v_objMethod.Name == v_strInsertMethod)
                .ToArray();

            Assert.Equal(2, v_arrInsertMethods.Length);
            Assert.All(v_arrInsertMethods, v_objMethod => Assert.Equal(typeof(long), v_objMethod.ReturnType));

            string v_strControllerFile = v_objController.Name + ".cs";
            string v_strControllerSource = File.ReadAllText(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Data_Access", "Controller", "DM", v_strControllerFile));

            Assert.DoesNotContain("Task.FromResult", v_strControllerSource);
            Assert.DoesNotContain("Task.CompletedTask", v_strControllerSource);
            Assert.DoesNotContain("=>", v_strControllerSource);
            Assert.DoesNotContain("p_strMaster_Type", v_strControllerSource);
            Assert.DoesNotContain("var ", v_strControllerSource);

            MatchCollection v_arrStoreMethods = Regex.Matches(v_strControllerSource,
                @"(?ms)^    public (?:List<\w+>|long|void) (?<name>F\w+)\(.*?^    \}");
            Assert.Equal(7, v_arrStoreMethods.Count);
            foreach (Match v_objStoreMethod in v_arrStoreMethods)
            {
                MatchCollection v_arrStoreCalls = Regex.Matches(v_objStoreMethod.Value,
                    @"CSqlHelper\.(?:FillDataTable|ExecuteScalar|ExecuteNonquery)\([^;]*?""(?<store>F\w+)""");
                Assert.Single(v_arrStoreCalls.Cast<Match>());
                Assert.Equal(v_objStoreMethod.Groups["name"].Value, v_arrStoreCalls[0].Groups["store"].Value);
            }
        }
    }

    [Fact]
    public void Audit_helpers_initialize_insert_metadata_and_preserve_creator_metadata_on_update()
    {
        Type[] v_arrControllerTypes =
        [
            typeof(CDM_Kho_Controller), typeof(CDM_Don_Vi_Tinh_Controller),
            typeof(CDM_Loai_San_Pham_Controller), typeof(CDM_San_Pham_Controller), typeof(CDM_NCC_Controller)
        ];

        foreach (Type v_objControllerType in v_arrControllerTypes)
        {
            MethodInfo? v_objInsertAuditMethod = v_objControllerType.GetMethod(
                "Set_Insert_Audit_Fields", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo? v_objUpdateAuditMethod = v_objControllerType.GetMethod(
                "Set_Update_Audit_Fields", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(v_objInsertAuditMethod);
            Assert.NotNull(v_objUpdateAuditMethod);

            foreach (long v_iAuto_ID in new[] { 0L, 17L })
            {
                string v_strEntityName = v_objControllerType.Name.Replace("_Controller", "", StringComparison.Ordinal);
                Type? v_objEntityType = v_objControllerType.Assembly.GetType(
                    "TKS_Thuc_Tap_V11_Data_Access.Entity.DM." + v_strEntityName);
                Assert.NotNull(v_objEntityType);
                object v_objData = Activator.CreateInstance(v_objEntityType!)!;
                v_objEntityType!.GetProperty("Auto_ID")!.SetValue(v_objData, v_iAuto_ID);
                v_objEntityType.GetProperty("Created_By")!.SetValue(v_objData, "stale creator");
                v_objEntityType.GetProperty("Created_By_Function")!.SetValue(v_objData, "stale function");
                v_objEntityType.GetProperty("Last_Updated_By")!.SetValue(v_objData, "old updater");
                v_objEntityType.GetProperty("Last_Updated_By_Function")!.SetValue(v_objData, "old update function");

                v_objInsertAuditMethod!.Invoke(null, new object[] { v_objData, "creator A", "function A" });
                v_objUpdateAuditMethod!.Invoke(null, new object[] { v_objData, "updater B", "function B" });

                Assert.Equal(v_iAuto_ID, v_objEntityType.GetProperty("Auto_ID")!.GetValue(v_objData));
                Assert.Equal("creator A", v_objEntityType.GetProperty("Created_By")!.GetValue(v_objData));
                Assert.Equal("function A", v_objEntityType.GetProperty("Created_By_Function")!.GetValue(v_objData));
                Assert.Equal("updater B", v_objEntityType.GetProperty("Last_Updated_By")!.GetValue(v_objData));
                Assert.Equal("function B", v_objEntityType.GetProperty("Last_Updated_By_Function")!.GetValue(v_objData));
            }
        }
    }

    [Fact]
    public void Master_update_calls_refresh_cache_only_after_sql_and_cache_preserves_creator_fields()
    {
        (string Code, string Controller, string Edit, string Cache)[] v_arrModules =
        [
            ("2009", "CDM_Kho_Controller.cs", "F2009_3_Kho_Edit.razor", "CCache_Kho.cs"),
            ("2016", "CDM_Don_Vi_Tinh_Controller.cs", "FWarehouse_Unit_Edit.razor", "CCache_Don_Vi_Tinh.cs"),
            ("2017", "CDM_Loai_San_Pham_Controller.cs", "F2017_3_Loai_San_Pham_Edit.razor", "CCache_Loai_San_Pham.cs"),
            ("2018", "CDM_San_Pham_Controller.cs", "F2018_3_San_Pham_Edit.razor", "CCache_San_Pham.cs"),
            ("2019", "CDM_NCC_Controller.cs", "F2019_3_Nha_Cung_Cap_Edit.razor", "CCache_NCC.cs")
        ];

        foreach (var v_objModule in v_arrModules)
        {
            string v_strController = File.ReadAllText(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Data_Access", "Controller", "DM", v_objModule.Controller));
            string v_strEdit = File.ReadAllText(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", v_objModule.Edit));
            string v_strCache = File.ReadAllText(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Cache", v_objModule.Cache));

            Assert.Equal(2, CountOccurrences(v_strController, "Set_Insert_Audit_Fields(p_objData,"));
            Assert.Contains("Set_Update_Audit_Fields(p_objData,", v_strController);
            Assert.Contains("p_objData.Created_By = v_objOldData.Created_By", v_strCache);
            Assert.Contains("p_objData.Created_By_Function = v_objOldData.Created_By_Function", v_strCache);

            int v_iUpdateCall = v_strEdit.IndexOf($"{v_objModule.Code}_sp_upd_", StringComparison.Ordinal);
            int v_iCacheRefresh = v_strEdit.IndexOf("CCache_", v_iUpdateCall, StringComparison.Ordinal);
            Assert.True(v_iUpdateCall >= 0 && v_iCacheRefresh > v_iUpdateCall,
                $"Module {v_objModule.Code} must update SQL before refreshing cache.");
        }
    }

    [Fact]
    public void Unit_cache_update_preserves_creator_metadata_and_replaces_the_business_key()
    {
        long v_iAuto_ID = Random.Shared.NextInt64(8_000_000_000L, 9_000_000_000L);
        var v_dtmCreated = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var v_objOriginal = new TKS_Thuc_Tap_V11_Data_Access.Entity.DM.CDM_Don_Vi_Tinh
        {
            Auto_ID = v_iAuto_ID,
            Ten_Don_Vi_Tinh = "audit-cache-old-" + v_iAuto_ID,
            Created = v_dtmCreated,
            Created_By = "creator A",
            Created_By_Function = "function A"
        };
        var v_objUpdated = new TKS_Thuc_Tap_V11_Data_Access.Entity.DM.CDM_Don_Vi_Tinh
        {
            Auto_ID = v_iAuto_ID,
            Ten_Don_Vi_Tinh = "audit-cache-new-" + v_iAuto_ID,
            Ghi_Chu = "edited",
            Created = null,
            Created_By = "updater B",
            Created_By_Function = "function B",
            Last_Updated_By = "updater B",
            Last_Updated_By_Function = "function B"
        };

        try
        {
            TKS_Thuc_Tap_V11_Data_Access.Controller.Cache.CCache_Don_Vi_Tinh.Delete_Data(v_iAuto_ID);
            TKS_Thuc_Tap_V11_Data_Access.Controller.Cache.CCache_Don_Vi_Tinh.Add_Data(v_objOriginal);
            TKS_Thuc_Tap_V11_Data_Access.Controller.Cache.CCache_Don_Vi_Tinh.Update_Data(v_objUpdated);

            var v_objCached = TKS_Thuc_Tap_V11_Data_Access.Controller.Cache.CCache_Don_Vi_Tinh.Get_Data_By_ID(v_iAuto_ID);
            Assert.NotNull(v_objCached);
            Assert.Equal(v_iAuto_ID, v_objCached!.Auto_ID);
            Assert.Equal(v_dtmCreated, v_objCached.Created);
            Assert.Equal("creator A", v_objCached.Created_By);
            Assert.Equal("function A", v_objCached.Created_By_Function);
            Assert.Equal("updater B", v_objCached.Last_Updated_By);
            Assert.Equal("function B", v_objCached.Last_Updated_By_Function);
            Assert.Equal("edited", v_objCached.Ghi_Chu);
            Assert.Null(TKS_Thuc_Tap_V11_Data_Access.Controller.Cache.CCache_Don_Vi_Tinh
                .Get_Data_By_Ten_Don_Vi_Tinh(v_objOriginal.Ten_Don_Vi_Tinh));
            Assert.Same(v_objCached, TKS_Thuc_Tap_V11_Data_Access.Controller.Cache.CCache_Don_Vi_Tinh
                .Get_Data_By_Ten_Don_Vi_Tinh(v_objUpdated.Ten_Don_Vi_Tinh));
        }
        finally
        {
            TKS_Thuc_Tap_V11_Data_Access.Controller.Cache.CCache_Don_Vi_Tinh.Delete_Data(v_iAuto_ID);
        }
    }

    private static int CountOccurrences(string p_strSource, string p_strValue)
    {
        return p_strSource.Split(p_strValue, StringSplitOptions.None).Length - 1;
    }

    private static string FindRepositoryPath(params string[] p_arrParts)
    {
        for (DirectoryInfo? v_objDirectory = new DirectoryInfo(AppContext.BaseDirectory);
             v_objDirectory is not null;
             v_objDirectory = v_objDirectory.Parent)
        {
            string v_strCandidate = Path.Combine(new[] { v_objDirectory.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_strCandidate))
            {
                return v_strCandidate;
            }
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(p_arrParts)}");
    }
}
