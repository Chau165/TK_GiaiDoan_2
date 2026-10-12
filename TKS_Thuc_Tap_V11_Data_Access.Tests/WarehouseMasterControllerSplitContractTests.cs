using System.Reflection;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseMasterControllerSplitContractTests
{
    public static IEnumerable<object[]> EntityControllerContracts =>
    [
        ["TKS_Thuc_Tap_V11_Data_Access.Controller.DM.CDM_Kho_Controller", new[]
        {
            "F2009_sp_sel_List_Kho",
            "F2009_sp_sel_List_Kho_Lookup",
            "F2009_sp_ins_Kho",
            "F2009_sp_ins_Kho",
            "F2009_sp_upd_Kho",
            "F2009_sp_del_Kho"
        }],
        ["TKS_Thuc_Tap_V11_Data_Access.Controller.DM.CDM_Don_Vi_Tinh_Controller", new[]
        {
            "F2016_sp_sel_List_Don_Vi_Tinh",
            "F2016_sp_sel_List_Don_Vi_Tinh_Lookup",
            "F2016_sp_ins_Don_Vi_Tinh",
            "F2016_sp_ins_Don_Vi_Tinh",
            "F2016_sp_upd_Don_Vi_Tinh",
            "F2016_sp_del_Don_Vi_Tinh"
        }],
        ["TKS_Thuc_Tap_V11_Data_Access.Controller.DM.CDM_Loai_San_Pham_Controller", new[]
        {
            "F2017_sp_sel_List_Loai_San_Pham",
            "F2017_sp_sel_List_Loai_San_Pham_Lookup",
            "F2017_sp_ins_Loai_San_Pham",
            "F2017_sp_ins_Loai_San_Pham",
            "F2017_sp_upd_Loai_San_Pham",
            "F2017_sp_del_Loai_San_Pham"
        }],
        ["TKS_Thuc_Tap_V11_Data_Access.Controller.DM.CDM_San_Pham_Controller", new[]
        {
            "F2018_sp_sel_List_San_Pham",
            "F2018_sp_sel_List_San_Pham_Lookup",
            "F2018_sp_ins_San_Pham",
            "F2018_sp_ins_San_Pham",
            "F2018_sp_upd_San_Pham",
            "F2018_sp_del_San_Pham"
        }],
        ["TKS_Thuc_Tap_V11_Data_Access.Controller.DM.CDM_NCC_Controller", new[]
        {
            "F2019_sp_sel_List_NCC",
            "F2019_sp_sel_List_NCC_Lookup",
            "F2019_sp_ins_NCC",
            "F2019_sp_ins_NCC",
            "F2019_sp_upd_NCC",
            "F2019_sp_del_NCC"
        }]
    ];

    [Theory]
    [MemberData(nameof(EntityControllerContracts))]
    public void Entity_controller_exposes_only_its_named_store_methods(
        string p_strControllerName,
        string[] p_arrExpectedMethodNames)
    {
        var v_objAssembly = typeof(CWarehouseMaster_Controller).Assembly;
        var v_objController = v_objAssembly.GetType(p_strControllerName);

        Assert.NotNull(v_objController);

        var v_arrActualMethodNames = v_objController!
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(v_objMethod => v_objMethod.Name)
            .OrderBy(v_strName => v_strName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            p_arrExpectedMethodNames.OrderBy(v_strName => v_strName, StringComparer.Ordinal),
            v_arrActualMethodNames);
    }

    [Theory]
    [MemberData(nameof(InsertStoreContracts))]
    public void Insert_store_has_normal_and_transaction_overloads(
        string p_strControllerName,
        string p_strStoreName)
    {
        var v_objController = typeof(CWarehouseMaster_Controller).Assembly.GetType(p_strControllerName);
        Assert.NotNull(v_objController);

        var v_arrMethods = v_objController!
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(v_objMethod => v_objMethod.Name == p_strStoreName)
            .ToArray();

        Assert.Equal(2, v_arrMethods.Length);
        Assert.Contains(v_arrMethods, v_objMethod => HasParameters(
            v_objMethod,
            typeof(CWarehouseMaster), typeof(string), typeof(string)));
        Assert.Contains(v_arrMethods, v_objMethod => HasParameters(
            v_objMethod,
            typeof(SqlConnection), typeof(SqlTransaction), typeof(CWarehouseMaster), typeof(string), typeof(string)));
    }

    [Fact]
    public void Generic_master_store_routers_are_removed_from_the_compatibility_controller()
    {
        var v_arrMethodNames = typeof(CWarehouseMaster_Controller)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Select(v_objMethod => v_objMethod.Name);

        Assert.DoesNotContain("List_Master_Async", v_arrMethodNames);
        Assert.DoesNotContain("List_Lookup_Async", v_arrMethodNames);
        Assert.DoesNotContain("Save_Master_Async", v_arrMethodNames);
        Assert.DoesNotContain("Delete_Master_Async", v_arrMethodNames);
        Assert.DoesNotContain("Build_Master_Save_Command", v_arrMethodNames);
    }

    public static IEnumerable<object[]> InsertStoreContracts =>
    [
        ["TKS_Thuc_Tap_V11_Data_Access.Controller.DM.CDM_Kho_Controller", "F2009_sp_ins_Kho"],
        ["TKS_Thuc_Tap_V11_Data_Access.Controller.DM.CDM_Don_Vi_Tinh_Controller", "F2016_sp_ins_Don_Vi_Tinh"],
        ["TKS_Thuc_Tap_V11_Data_Access.Controller.DM.CDM_Loai_San_Pham_Controller", "F2017_sp_ins_Loai_San_Pham"],
        ["TKS_Thuc_Tap_V11_Data_Access.Controller.DM.CDM_San_Pham_Controller", "F2018_sp_ins_San_Pham"],
        ["TKS_Thuc_Tap_V11_Data_Access.Controller.DM.CDM_NCC_Controller", "F2019_sp_ins_NCC"]
    ];

    private static bool HasParameters(MethodInfo p_objMethod, params Type[] p_arrParameterTypes) =>
        p_objMethod.GetParameters()
            .Select(v_objParameter => v_objParameter.ParameterType)
            .SequenceEqual(p_arrParameterTypes);
}
