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

        const string user = "thuctap_kho";
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

        AddMemberGroup(1, user, 101);
        AddMemberGroup(2, user, 102);
        AddDeniedPermission(1, 101, functionId);
        AddDeniedPermission(2, 102, functionId);

        var permission = CCommonFunction.Get_Chuc_Nang_By_User(user, "MASTER_DATA_TEST");

        Assert.False(permission.Is_Have_View_Permission);
        Assert.False(permission.Is_Have_Add_Permission);
        Assert.False(permission.Is_Have_Edit_Permission);
        Assert.False(permission.Is_Have_Delete_Permission);

        var menuPermission = Assert.Single(CCommonFunction.List_Chuc_Nang_By_User(user));
        Assert.False(menuPermission.Is_Have_View_Permission);
        Assert.False(menuPermission.Is_Have_Add_Permission);
        Assert.False(menuPermission.Is_Have_Edit_Permission);
        Assert.False(menuPermission.Is_Have_Delete_Permission);
    }

    private static void AddMemberGroup(long id, string user, long groupId) =>
        CCache_Nhom_Thanh_Vien_User.Add_Data(new CSys_Nhom_Thanh_Vien_User
        {
            Auto_ID = id,
            Ma_Dang_Nhap = user,
            Nhom_Thanh_Vien_ID = groupId
        });

    private static void AddDeniedPermission(long id, long groupId, long functionId) =>
        CCache_Phan_Quyen_Chuc_Nang.Add_Data(new CSys_Phan_Quyen_Chuc_Nang
        {
            Auto_ID = id,
            Nhom_Thanh_Vien_ID = groupId,
            Chuc_Nang_ID = functionId,
            Is_Have_View_Permission = false,
            Is_Have_Add_Permission = false,
            Is_Have_Edit_Permission = false,
            Is_Have_Delete_Permission = false,
            Is_Have_Export_Permission = false
        });

    private static void ClearCache(Type type)
    {
        foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            if (field.GetValue(null) is System.Collections.IList list)
                list.Clear();
            else if (field.GetValue(null) is System.Collections.IDictionary dictionary)
                dictionary.Clear();
    }
}
