using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseNavigationTests
{
    [Fact]
    public void Warehouse_master_page_uses_only_the_master_section()
    {
        var page = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Warehouse.razor"));
        var component = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("<FWarehouse_1_Warehouse_List m_strSection=\"Master\" m_strInitial_Master_Type=\"Kho\" m_bMaster_Type_Only=\"true\" />", page);
        Assert.Contains("[Parameter] public string m_strSection", component);
        Assert.Contains("[Parameter] public string m_strInitial_Master_Type", component);
        Assert.Contains("[Parameter] public bool m_bMaster_Type_Only", component);
        Assert.Contains("class=\"d-flex gap-2 ms-auto\"", component);
        Assert.DoesNotContain("Select_Section_Async", component);
        Assert.DoesNotContain("@onclick=\"@(() => Select_Section_Async", component);
    }

    [Theory]
    [InlineData("Warehouse_Don_Vi_Tinh.razor", "/Kho/Don_Vi_Tinh", "DonViTinh")]
    [InlineData("Warehouse_Loai_San_Pham.razor", "/Kho/Loai_San_Pham", "LoaiSanPham")]
    [InlineData("Warehouse_San_Pham.razor", "/Kho/San_Pham", "SanPham")]
    [InlineData("Warehouse_Nha_Cung_Cap.razor", "/Kho/Nha_Cung_Cap", "NCC")]
    public void Warehouse_master_data_has_dedicated_routes(string fileName, string route, string masterType)
    {
        var page = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", fileName));

        Assert.Contains($"@page \"{route}\"", page);
        Assert.Contains($"m_strInitial_Master_Type=\"{masterType}\"", page);
        Assert.Contains("m_bMaster_Type_Only=\"true\"", page);
    }

    [Theory]
    [InlineData("Warehouse_Nhap_Kho.razor", "/Kho/Nhap_Kho", "Receipt")]
    [InlineData("Warehouse_Xuat_Kho.razor", "/Kho/Xuat_Kho", "Issue")]
    [InlineData("Warehouse_Ton_Kho.razor", "/Kho/Ton_Kho", "Report")]
    [InlineData("Warehouse_Bao_Cao.razor", "/Kho/Bao_Cao", "Report")]
    public void Warehouse_operations_have_dedicated_routes(string fileName, string route, string section)
    {
        var page = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", fileName));

        Assert.Contains($"@page \"{route}\"", page);
        Assert.Contains($"<FWarehouse_1_Warehouse_List m_strSection=\"{section}\"", page);
    }

    [Fact]
    public void Warehouse_menu_exposes_master_and_operations_as_separate_items()
    {
        var menu = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Menu.sql"));

        foreach (var label in new[] { "Đơn vị tính", "Loại sản phẩm", "Sản phẩm", "Kho", "Nhà cung cấp", "Quản lý kho", "Nhập kho", "Xuất kho", "Tồn kho", "Báo cáo", "Phân quyền kho-user" })
            Assert.Contains($"N'{label}'", menu);

        foreach (var route in new[] { "/Kho/Don_Vi_Tinh", "/Kho/Loai_San_Pham", "/Kho/San_Pham", "/Kho/Quan_Ly", "/Kho/Nha_Cung_Cap", "/Kho/Nhap_Kho", "/Kho/Xuat_Kho", "/Kho/Ton_Kho", "/Kho/Bao_Cao", "/Kho/Phan_Quyen" })
            Assert.Contains($"N'{route}'", menu);

        Assert.Contains("N'Đơn vị tính', 5, @Master_Data_ID", menu);
        Assert.Contains("N'Loại sản phẩm', 6, @Master_Data_ID", menu);
        Assert.Contains("N'Sản phẩm', 7, @Master_Data_ID", menu);
        Assert.Contains("N'Kho', 4, @Master_Data_ID", menu);
        Assert.Contains("N'Nhà cung cấp', 8, @Master_Data_ID", menu);
        Assert.Contains("N'Quản lý kho', 5, 0", menu);
        Assert.Contains("N'Nhập kho', 1, @Warehouse_Management_ID", menu);
        Assert.Contains("N'Phân quyền kho-user', 6, @System_ID", menu);
    }

    [Fact]
    public void Warehouse_permission_page_only_exposes_the_warehouse_user_assignment()
    {
        var page = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Warehouse_Phan_Quyen.razor"));
        var component = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("@page \"/Kho/Phan_Quyen\"", page);
        Assert.Contains("m_bMaster_Type_Only=\"true\"", page);
        Assert.Contains("m_bWarehouse_Permission_Only=\"true\"", page);
        Assert.Contains("[Parameter] public bool m_bWarehouse_Permission_Only", component);
        Assert.Contains("m_strMaster_Type = m_bWarehouse_Permission_Only", component);
    }

    private static string FindRepositoryPath(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(parts)}");
    }
}
