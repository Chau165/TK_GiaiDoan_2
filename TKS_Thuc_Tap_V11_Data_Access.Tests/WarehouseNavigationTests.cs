using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseNavigationTests
{
    [Fact]
    public void Warehouse_master_page_uses_only_the_master_section()
    {
        var v_Page = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Warehouse.razor"));
        var v_Component = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("<FWarehouse_1_Warehouse_List m_strSection=\"Master\" m_strInitial_Master_Type=\"Kho\" m_bMaster_Type_Only=\"true\" />", v_Page);
        Assert.Contains("[Parameter] public string m_strSection", v_Component);
        Assert.Contains("[Parameter] public string m_strInitial_Master_Type", v_Component);
        Assert.Contains("[Parameter] public bool m_bMaster_Type_Only", v_Component);
        Assert.Contains("class=\"d-flex gap-2 ms-auto\"", v_Component);
        Assert.DoesNotContain("Select_Section_Async", v_Component);
        Assert.DoesNotContain("@onclick=\"@(() => Select_Section_Async", v_Component);
    }

    [Theory]
    [InlineData("Warehouse_Don_Vi_Tinh.razor", "/Kho/Don_Vi_Tinh", "DonViTinh")]
    [InlineData("Warehouse_Loai_San_Pham.razor", "/Kho/Loai_San_Pham", "LoaiSanPham")]
    [InlineData("Warehouse_San_Pham.razor", "/Kho/San_Pham", "SanPham")]
    [InlineData("Warehouse_Nha_Cung_Cap.razor", "/Kho/Nha_Cung_Cap", "NCC")]
    public void Warehouse_master_data_has_dedicated_routes(string p_FileName, string p_Route, string p_MasterType)
    {
        var v_Page = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", p_FileName));

        Assert.Contains($"@page \"{p_Route}\"", v_Page);
        Assert.Contains($"m_strInitial_Master_Type=\"{p_MasterType}\"", v_Page);
        Assert.Contains("m_bMaster_Type_Only=\"true\"", v_Page);
    }

    [Theory]
    [InlineData("Warehouse_Nhap_Kho.razor", "/Kho/Nhap_Kho", "Receipt")]
    [InlineData("Warehouse_Xuat_Kho.razor", "/Kho/Xuat_Kho", "Issue")]
    [InlineData("Warehouse_Ton_Kho.razor", "/Kho/Ton_Kho", "Report")]
    [InlineData("Warehouse_Bao_Cao.razor", "/Kho/Bao_Cao", "Report")]
    public void Warehouse_operations_have_dedicated_routes(string p_FileName, string p_Route, string p_Section)
    {
        var v_Page = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", p_FileName));

        Assert.Contains($"@page \"{p_Route}\"", v_Page);
        Assert.Contains($"<FWarehouse_1_Warehouse_List m_strSection=\"{p_Section}\"", v_Page);
    }

    [Fact]
    public void Warehouse_menu_exposes_master_and_operations_as_separate_items()
    {
        var v_Menu = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Menu.sql"));

        foreach (var v_Label in new[] { "Đơn vị tính", "Loại sản phẩm", "Sản phẩm", "Kho", "Nhà cung cấp", "Quản lý kho", "Nhập kho", "Xuất kho", "Tồn kho", "Báo cáo", "Phân quyền kho-user" })
            Assert.Contains($"N'{v_Label}'", v_Menu);

        foreach (var v_Route in new[] { "/Kho/Don_Vi_Tinh", "/Kho/Loai_San_Pham", "/Kho/San_Pham", "/Kho/Quan_Ly", "/Kho/Nha_Cung_Cap", "/Kho/Nhap_Kho", "/Kho/Xuat_Kho", "/Kho/Ton_Kho", "/Kho/Bao_Cao", "/Kho/Phan_Quyen" })
            Assert.Contains($"N'{v_Route}'", v_Menu);

        Assert.Contains("N'Đơn vị tính', 5, @Master_Data_ID", v_Menu);
        Assert.Contains("N'Loại sản phẩm', 6, @Master_Data_ID", v_Menu);
        Assert.Contains("N'Sản phẩm', 7, @Master_Data_ID", v_Menu);
        Assert.Contains("N'Kho', 4, @Master_Data_ID", v_Menu);
        Assert.Contains("N'Nhà cung cấp', 8, @Master_Data_ID", v_Menu);
        Assert.Contains("N'Quản lý kho', 5, 0", v_Menu);
        Assert.Contains("N'Nhập kho', 1, @Warehouse_Management_ID", v_Menu);
        Assert.Contains("N'Phân quyền kho-user', 6, @System_ID", v_Menu);
    }

    [Fact]
    public void Warehouse_permission_page_only_exposes_the_warehouse_user_assignment()
    {
        var v_Page = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Warehouse_Phan_Quyen.razor"));
        var v_Component = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("@page \"/Kho/Phan_Quyen\"", v_Page);
        Assert.Contains("m_bMaster_Type_Only=\"true\"", v_Page);
        Assert.Contains("m_bWarehouse_Permission_Only=\"true\"", v_Page);
        Assert.Contains("[Parameter] public bool m_bWarehouse_Permission_Only", v_Component);
        Assert.Contains("m_strMaster_Type = m_bWarehouse_Permission_Only", v_Component);
    }

    private static string FindRepositoryPath(params string[] p_arrParts)
    {
        for (var v_Directory = new DirectoryInfo(AppContext.BaseDirectory); v_Directory is not null; v_Directory = v_Directory.Parent)
        {
            var v_Candidate = Path.Combine(new[] { v_Directory.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_Candidate))
                return v_Candidate;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(p_arrParts)}");
    }
}
