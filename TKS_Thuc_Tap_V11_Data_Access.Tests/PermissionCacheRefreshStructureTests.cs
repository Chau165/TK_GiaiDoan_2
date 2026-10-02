using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public class PermissionCacheRefreshStructureTests
{
    [Fact]
    public void Permission_changes_refresh_the_permission_cache()
    {
        var v_ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var v_ComponentPath = Path.Combine(v_ProjectRoot, "TKS_Thuc_Tap_V11_Web_Sys", "Pages", "Sys", "Components", "F1005_1_Phan_Quyen_Chuc_Nang_List.razor");
        var v_Source = File.ReadAllText(v_ComponentPath);

        foreach (var v_Operation in new[] { "View", "Add", "Edit", "Delete", "Export" })
            Assert.Contains(
                $"F1005_sp_upd_Phan_Quyen_Chuc_Nang_{v_Operation}(v_objData);\n            CCache_Phan_Quyen_Chuc_Nang.Load_Cache_Phan_Quyen_Chuc_Nang();",
                v_Source.Replace("\r\n", "\n"));
    }
}
