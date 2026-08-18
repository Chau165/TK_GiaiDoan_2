using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public class PermissionCacheRefreshStructureTests
{
    [Fact]
    public void Permission_changes_refresh_the_permission_cache()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var componentPath = Path.Combine(projectRoot, "TKS_Thuc_Tap_V11_Web_Sys", "Pages", "Sys", "Components", "F1005_1_Phan_Quyen_Chuc_Nang_List.razor");
        var source = File.ReadAllText(componentPath);

        foreach (var operation in new[] { "View", "Add", "Edit", "Delete", "Export" })
            Assert.Contains(
                $"F1005_sp_upd_Phan_Quyen_Chuc_Nang_{operation}(v_objData);\n            CCache_Phan_Quyen_Chuc_Nang.Load_Cache_Phan_Quyen_Chuc_Nang();",
                source.Replace("\r\n", "\n"));
    }
}
