using System.Reflection;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseMasterEntityDecompositionContractTests
{
    private const string EntityNamespace = "TKS_Thuc_Tap_V11_Data_Access.Entity.DM.";
    private const string ControllerNamespace = "TKS_Thuc_Tap_V11_Data_Access.Controller.DM.";

    public static IEnumerable<object[]> EntityContracts =>
    [
        ["CDM_Kho_Controller", "CDM_Kho", "F2009_sp_sel_List_Kho", "F2009_sp_sel_List_Kho_Lookup", "F2009_sp_ins_Kho", "F2009_sp_upd_Kho", "F2009_sp_del_Kho"],
        ["CDM_Don_Vi_Tinh_Controller", "CDM_Don_Vi_Tinh", "F2016_sp_sel_List_Don_Vi_Tinh", "F2016_sp_sel_List_Don_Vi_Tinh_Lookup", "F2016_sp_ins_Don_Vi_Tinh", "F2016_sp_upd_Don_Vi_Tinh", "F2016_sp_del_Don_Vi_Tinh"],
        ["CDM_Loai_San_Pham_Controller", "CDM_Loai_San_Pham", "F2017_sp_sel_List_Loai_San_Pham", "F2017_sp_sel_List_Loai_San_Pham_Lookup", "F2017_sp_ins_Loai_San_Pham", "F2017_sp_upd_Loai_San_Pham", "F2017_sp_del_Loai_San_Pham"],
        ["CDM_San_Pham_Controller", "CDM_San_Pham", "F2018_sp_sel_List_San_Pham", "F2018_sp_sel_List_San_Pham_Lookup", "F2018_sp_ins_San_Pham", "F2018_sp_upd_San_Pham", "F2018_sp_del_San_Pham"],
        ["CDM_NCC_Controller", "CDM_NCC", "F2019_sp_sel_List_NCC", "F2019_sp_sel_List_NCC_Lookup", "F2019_sp_ins_NCC", "F2019_sp_upd_NCC", "F2019_sp_del_NCC"]
    ];

    [Theory]
    [MemberData(nameof(EntityContracts))]
    public void Each_DM_controller_uses_its_dedicated_entity(
        string p_strControllerName,
        string p_strEntityName,
        string p_strListMethod,
        string p_strLookupMethod,
        string p_strInsertMethod,
        string p_strUpdateMethod,
        string p_strDeleteMethod)
    {
        Assembly v_objAssembly = typeof(CDM_Kho_Controller).Assembly;
        Type? v_objEntity = v_objAssembly.GetType(EntityNamespace + p_strEntityName);
        Type? v_objController = v_objAssembly.GetType(ControllerNamespace + p_strControllerName);

        Assert.NotNull(v_objEntity);
        Assert.NotNull(v_objController);
        Assert.Equal(typeof(List<>).MakeGenericType(v_objEntity!), v_objController!.GetMethod(p_strListMethod)?.ReturnType);

        MethodInfo[] v_arrInsertMethods = v_objController.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(p_objMethod => p_objMethod.Name == p_strInsertMethod)
            .ToArray();
        Assert.Equal(2, v_arrInsertMethods.Length);
        Assert.Contains(v_arrInsertMethods, p_objMethod => HasParameters(p_objMethod, v_objEntity!, typeof(string), typeof(string)));
        Assert.Contains(v_arrInsertMethods, p_objMethod => HasParameters(p_objMethod, typeof(SqlConnection), typeof(SqlTransaction), v_objEntity!, typeof(string), typeof(string)));
        Assert.True(HasParameters(v_objController.GetMethod(p_strUpdateMethod)!, v_objEntity!, typeof(string), typeof(string)));
        Assert.True(HasParameters(v_objController.GetMethod(p_strDeleteMethod)!, typeof(long), typeof(string), typeof(string)));

        Type? v_objLookup = v_objAssembly.GetType("TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse.CWarehouseLookup");
        Assert.NotNull(v_objLookup);
        Assert.Equal(typeof(List<>).MakeGenericType(v_objLookup!), v_objController.GetMethod(p_strLookupMethod)?.ReturnType);

        foreach (MethodInfo v_objMethod in v_objController.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            Assert.DoesNotContain("CWarehouseMaster", v_objMethod.ReturnType.FullName ?? "");
            Assert.DoesNotContain("CWarehouseMaster", string.Join(" ", v_objMethod.GetParameters().Select(p_objParameter => p_objParameter.ParameterType.FullName)));
        }
    }

    [Fact]
    public void Dedicated_entities_expose_the_verified_store_and_audit_fields()
    {
        Dictionary<string, Type> v_dicCommonProperties = new()
        {
            ["Auto_ID"] = typeof(long),
            ["Created"] = typeof(DateTime?),
            ["Created_By"] = typeof(string),
            ["Created_By_Function"] = typeof(string),
            ["Last_Updated"] = typeof(DateTime?),
            ["Last_Updated_By"] = typeof(string),
            ["Last_Updated_By_Function"] = typeof(string),
            ["Ghi_Chu"] = typeof(string)
        };

        Dictionary<string, (string Name, Type Type)[]> v_dicBusinessProperties = new()
        {
            ["CDM_Kho"] = [("Ten_Kho", typeof(string))],
            ["CDM_Don_Vi_Tinh"] = [("Ten_Don_Vi_Tinh", typeof(string))],
            ["CDM_Loai_San_Pham"] = [("Ma_LSP", typeof(string)), ("Ten_LSP", typeof(string))],
            ["CDM_San_Pham"] = [("Ma_San_Pham", typeof(string)), ("Ten_San_Pham", typeof(string)), ("Loai_San_Pham_ID", typeof(long)), ("Don_Vi_Tinh_ID", typeof(long))],
            ["CDM_NCC"] = [("Ma_NCC", typeof(string)), ("Ten_NCC", typeof(string))]
        };

        Assembly v_objAssembly = typeof(CDM_Kho_Controller).Assembly;
        foreach (KeyValuePair<string, (string Name, Type Type)[]> v_objContract in v_dicBusinessProperties)
        {
            Type? v_objEntity = v_objAssembly.GetType(EntityNamespace + v_objContract.Key);
            Assert.NotNull(v_objEntity);

            foreach ((string v_strPropertyName, Type v_objPropertyType) in v_dicCommonProperties)
                Assert.Equal(v_objPropertyType, v_objEntity!.GetProperty(v_strPropertyName)?.PropertyType);

            foreach ((string v_strPropertyName, Type v_objPropertyType) in v_objContract.Value)
                Assert.Equal(v_objPropertyType, v_objEntity!.GetProperty(v_strPropertyName)?.PropertyType);
        }
    }

    [Theory]
    [MemberData(nameof(EntityContracts))]
    public void Store_targets_and_argument_order_match_the_existing_contract(
        string p_strControllerName,
        string p_strEntityName,
        string p_strListMethod,
        string p_strLookupMethod,
        string p_strInsertMethod,
        string p_strUpdateMethod,
        string p_strDeleteMethod)
    {
        string v_strController = ReadRepositoryFile("TKS_Thuc_Tap_V11_Data_Access", "Controller", "DM", p_strControllerName + ".cs");
        (string v_strInsertArguments, string v_strUpdateArguments) = p_strEntityName switch
        {
            "CDM_Kho" => ("p_objData.Auto_ID, p_objData.Ten_Kho, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function", "p_objData.Auto_ID, p_objData.Ten_Kho, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function"),
            "CDM_Don_Vi_Tinh" => ("p_objData.Auto_ID, p_objData.Ten_Don_Vi_Tinh, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function", "p_objData.Auto_ID, p_objData.Ten_Don_Vi_Tinh, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function"),
            "CDM_Loai_San_Pham" => ("p_objData.Auto_ID, p_objData.Ma_LSP, p_objData.Ten_LSP, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function", "p_objData.Auto_ID, p_objData.Ma_LSP, p_objData.Ten_LSP, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function"),
            "CDM_San_Pham" => ("p_objData.Auto_ID, p_objData.Ma_San_Pham, p_objData.Ten_San_Pham, p_objData.Loai_San_Pham_ID, p_objData.Don_Vi_Tinh_ID, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function", "p_objData.Auto_ID, p_objData.Ma_San_Pham, p_objData.Ten_San_Pham, p_objData.Loai_San_Pham_ID, p_objData.Don_Vi_Tinh_ID, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function"),
            _ => ("p_objData.Auto_ID, p_objData.Ma_NCC, p_objData.Ten_NCC, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function", "p_objData.Auto_ID, p_objData.Ma_NCC, p_objData.Ten_NCC, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function")
        };

        string v_strInsertCall = $"\"{p_strInsertMethod}\", {v_strInsertArguments}";
        string v_strUpdateCall = $"\"{p_strUpdateMethod}\", {v_strUpdateArguments}";
        Assert.Equal(2, CountOccurrences(v_strController, v_strInsertCall));
        Assert.Equal(1, CountOccurrences(v_strController, v_strUpdateCall));
        Assert.Contains($"\"{p_strListMethod}\"", v_strController);
        Assert.Contains($"\"{p_strLookupMethod}\"", v_strController);
        Assert.Contains($"\"{p_strDeleteMethod}\"", v_strController);
    }

    [Fact]
    public void Import_services_construct_their_dedicated_entities()
    {
        (string v_strService, string v_strEntity)[] v_arrContracts =
        [
            ("CWarehouseProductImportService.cs", "CDM_San_Pham"),
            ("CWarehouseUnitImportService.cs", "CDM_Don_Vi_Tinh"),
            ("CWarehouseCategoryImportService.cs", "CDM_Loai_San_Pham"),
            ("CWarehouseSupplierImportService.cs", "CDM_NCC"),
            ("CWarehouseKhoImportService.cs", "CDM_Kho")
        ];

        foreach ((string v_strService, string v_strEntity) in v_arrContracts)
        {
            string v_strSource = ReadRepositoryFile("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Services", v_strService);
            Assert.Contains("new " + v_strEntity, v_strSource);
            Assert.DoesNotContain("CWarehouseMaster", v_strSource);
        }

        string v_strProductSupport = ReadRepositoryFile("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Services", "CWarehouseProductSupport_Service.cs");
        Assert.Contains("Task<List<CDM_San_Pham>> List_Product_Masters_Async", v_strProductSupport);
    }

    [Fact]
    public void Master_editor_and_save_callers_use_typed_entities_while_shared_page_support_stays_unchanged()
    {
        string v_strListSource = ReadRepositoryFile("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor");
        string v_strEditSource = ReadRepositoryFile("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_3_Warehouse_Edit.razor");
        string v_strPageSupport = ReadRepositoryFile("TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseMasterSupport_Controller.cs");

        foreach (string v_strEntity in new[] { "CDM_Kho", "CDM_Don_Vi_Tinh", "CDM_Loai_San_Pham", "CDM_San_Pham", "CDM_NCC" })
        {
            Assert.Contains(v_strEntity, v_strListSource);
            Assert.Contains(v_strEntity, v_strEditSource);
        }

        Assert.DoesNotContain("Save_Master_Async(CWarehouseMaster", v_strListSource);
        Assert.Contains("sp_DM_Master_Page", v_strPageSupport);
        Assert.Contains("sp_DM_Lookup_Page", v_strPageSupport);
        Assert.Contains("Page_From_Procedure<CWarehouseMaster>", v_strPageSupport);
        Assert.Contains("Page_From_Procedure<CWarehouseLookup>", v_strPageSupport);
    }

    private static bool HasParameters(MethodInfo p_objMethod, params Type[] p_arrParameterTypes) =>
        p_objMethod.GetParameters()
            .Select(p_objParameter => p_objParameter.ParameterType)
            .SequenceEqual(p_arrParameterTypes);

    private static int CountOccurrences(string p_strSource, string p_strValue) =>
        p_strSource.Split(p_strValue, StringSplitOptions.None).Length - 1;

    private static string ReadRepositoryFile(params string[] p_arrParts)
    {
        for (DirectoryInfo? v_objDirectory = new(AppContext.BaseDirectory);
             v_objDirectory is not null;
             v_objDirectory = v_objDirectory.Parent)
        {
            string v_strCandidate = Path.Combine(new[] { v_objDirectory.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_strCandidate))
                return File.ReadAllText(v_strCandidate);
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(p_arrParts)}");
    }
}
