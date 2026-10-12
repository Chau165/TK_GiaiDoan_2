using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class ProductExcelImportAuthorizationTests
{
    [Fact]
    public async Task Preview_requires_Add_permission_even_when_Edit_is_granted()
    {
        var v_objService = new CWarehouseProductImportService(new TestPermissionResolver(false));
        await using var v_objStream = new MemoryStream();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            v_objService.PreviewAsync("products.csv", v_objStream, "user", "2018"));
    }

    [Fact]
    public async Task Add_permission_allows_preview_to_continue_to_server_file_validation()
    {
        var v_objPermission = new TestPermissionResolver(true);
        var v_objService = new CWarehouseProductImportService(v_objPermission);
        await using var v_objStream = new MemoryStream();

        var v_objException = await Assert.ThrowsAsync<InvalidDataException>(() =>
            v_objService.PreviewAsync("products.csv", v_objStream, "user", "2018"));

        Assert.Contains(".xlsx", v_objException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, v_objPermission.CallCount);
    }

    [Fact]
    public async Task Confirm_rechecks_Add_permission_after_preview()
    {
        var v_objPermission = new TestPermissionResolver(true);
        var v_objService = new CWarehouseProductImportService(v_objPermission);
        await using var v_objStream = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            v_objService.PreviewAsync("products.csv", v_objStream, "user", "2018"));

        v_objPermission.IsAllowed = false;
        var v_objPreview = new CProductImportPreviewResult();
        v_objPreview.Rows.Add(new CProductImportRow
        {
            Row_Number = 2,
            Ma_San_Pham = "SP001",
            Ten_San_Pham = "Sữa",
            Ma_LSP = "LSP001",
            Ten_Don_Vi_Tinh = "Hộp",
            Loai_San_Pham_ID = 10,
            Don_Vi_Tinh_ID = 20
        });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            v_objService.ConfirmAsync(v_objPreview, "user", "2018"));
        Assert.Equal(2, v_objPermission.CallCount);
    }

    private sealed class TestPermissionResolver : IProductImportPermissionResolver
    {
        public TestPermissionResolver(bool p_bIsAllowed) => IsAllowed = p_bIsAllowed;

        public bool IsAllowed { get; set; }
        public int CallCount { get; private set; }

        public bool Has_Add_Permission(string p_strUserName, string p_strFunctionCode)
        {
            CallCount++;
            return IsAllowed;
        }
    }
}
