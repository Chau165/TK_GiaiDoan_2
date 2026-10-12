using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class UnitExcelImportAuthorizationTests
{
    [Fact]
    public async Task Preview_requires_Add_permission_for_the_unit_function()
    {
        var v_objPermission = new TestPermissionResolver(false);
        var v_objService = new CWarehouseUnitImportService(v_objPermission);
        await using var v_objStream = new MemoryStream();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            v_objService.PreviewAsync("unit.xlsx", v_objStream, "test-user", "2016"));

        Assert.Equal(("test-user", "2016"), v_objPermission.LastRequest);
    }

    [Fact]
    public async Task Confirm_rechecks_Add_permission_after_preview()
    {
        var v_objPermission = new TestPermissionResolver(true);
        var v_objService = new CWarehouseUnitImportService(v_objPermission);
        var v_objPreview = new CUnitImportPreviewResult();
        v_objPreview.Rows.Add(new CUnitImportRow { Row_Number = 2, Ten_Don_Vi_Tinh = "Hộp" });
        v_objPermission.IsAllowed = false;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            v_objService.ConfirmAsync(v_objPreview, "test-user", "2016"));

        Assert.Equal(("test-user", "2016"), v_objPermission.LastRequest);
    }

    private sealed class TestPermissionResolver : IUnitImportPermissionResolver
    {
        public TestPermissionResolver(bool p_bIsAllowed) => IsAllowed = p_bIsAllowed;

        public bool IsAllowed { get; set; }
        public (string UserName, string FunctionCode) LastRequest { get; private set; }

        public bool Has_Add_Permission(string p_strUserName, string p_strFunctionCode)
        {
            LastRequest = (p_strUserName, p_strFunctionCode);
            return IsAllowed;
        }
    }
}
