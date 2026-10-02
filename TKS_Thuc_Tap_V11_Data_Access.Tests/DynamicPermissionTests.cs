using System.Reflection;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Cache;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Sys;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Common.Common;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public class DynamicPermissionTests
{
    [Fact]
    public void Group_permissions_override_function_defaults()
    {
        ClearCache(typeof(CCache_Chuc_Nang));
        ClearCache(typeof(CCache_Nhom_Thanh_Vien_User));
        ClearCache(typeof(CCache_Phan_Quyen_Chuc_Nang));

        const string v_User = "thuctap_kho";
        const long functionId = 900001;
        CCache_Chuc_Nang.Add_Data(new CSys_Chuc_Nang
        {
            Auto_ID = functionId,
            Ma_Chuc_Nang = "MASTER_DATA_TEST",
            Nhom_Chuc_Nang_ID = (int)ENhom_Chuc_Nang_ID.Quan_Tri,
            Is_Have_View_Permission = true,
            Is_Have_Add_Permission = true,
            Is_Have_Edit_Permission = true,
            Is_Have_Delete_Permission = true
        });

        AddMemberGroup(1, v_User, 101);
        AddMemberGroup(2, v_User, 102);
        AddDeniedPermission(1, 101, functionId);
        AddDeniedPermission(2, 102, functionId);

        var v_objPermission = CCommonFunction.Get_Chuc_Nang_By_User(v_User, "MASTER_DATA_TEST");

        Assert.False(v_objPermission.Is_Have_View_Permission);
        Assert.False(v_objPermission.Is_Have_Add_Permission);
        Assert.False(v_objPermission.Is_Have_Edit_Permission);
        Assert.False(v_objPermission.Is_Have_Delete_Permission);

        var v_objMenuPermission = Assert.Single(CCommonFunction.List_Chuc_Nang_By_User(v_User));
        Assert.False(v_objMenuPermission.Is_Have_View_Permission);
        Assert.False(v_objMenuPermission.Is_Have_Add_Permission);
        Assert.False(v_objMenuPermission.Is_Have_Edit_Permission);
        Assert.False(v_objMenuPermission.Is_Have_Delete_Permission);
    }

    private static void AddMemberGroup(long id, string p_User, long groupId)
    {
        CCache_Nhom_Thanh_Vien_User.Add_Data(new CSys_Nhom_Thanh_Vien_User { Auto_ID = id, Ma_Dang_Nhap = p_User, Nhom_Thanh_Vien_ID = groupId });
    }

    private static void AddDeniedPermission(long id, long groupId, long functionId)
    {
        CCache_Phan_Quyen_Chuc_Nang.Add_Data(new CSys_Phan_Quyen_Chuc_Nang { Auto_ID = id, Nhom_Thanh_Vien_ID = groupId, Chuc_Nang_ID = functionId, Is_Have_View_Permission = false, Is_Have_Add_Permission = false, Is_Have_Edit_Permission = false, Is_Have_Delete_Permission = false, Is_Have_Export_Permission = false });
    }

    private static void ClearCache(Type p_Type)
    {
        foreach (var v_Field in p_Type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            if (v_Field.GetValue(null) is System.Collections.IList v_arrList)
                v_arrList.Clear();
            else if (v_Field.GetValue(null) is System.Collections.IDictionary v_dicDictionary)
                v_dicDictionary.Clear();
    }
}
