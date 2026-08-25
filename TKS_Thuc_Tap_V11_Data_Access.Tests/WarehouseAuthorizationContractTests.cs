using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseAuthorizationContractTests
{
    [Fact]
    public void Warehouse_document_controller_carries_the_current_login_into_every_operation()
    {
        var source = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseDocument_Controller.cs"));

        Assert.Contains("p_strCurrent_Login", source);
        Assert.Contains("@Ma_Dang_Nhap", source);
        Assert.Contains("sp_XNK_Document_Page", source);
        Assert.Contains("sp_XNK_Document_Post", source);
    }

    [Fact]
    public void Warehouse_database_contract_has_a_server_side_scope_guard_and_filters_reads()
    {
        var source = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));

        Assert.Contains("sp_DM_Kho_User_Ensure_Access", source);
        Assert.Contains("@Ma_Dang_Nhap NVARCHAR(100)", source);
        Assert.Contains("sp_DM_Kho_User_List_Allowed", source);
        Assert.Contains("ku.Ma_Dang_Nhap = @Ma_Dang_Nhap", source);
    }

    [Fact]
    public void Warehouse_user_editor_uses_existing_users_and_displays_the_assignment_scope()
    {
        var editor = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_3_Warehouse_Edit.razor"));
        var list = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("m_arrUser", editor);
        Assert.Contains("Mã đăng nhập / user", editor);
        Assert.Contains("Tên kho", list);
        Assert.Contains("Họ tên", list);
    }

    [Fact]
    public void Warehouse_user_grid_hides_filter_buttons_after_switching_to_the_assignment_tab()
    {
        var list = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("ShowFilterCellButtons=\"false\"", list);
        Assert.DoesNotContain("Field=\"Login_Name\" Title=\"Mã đăng nhập\" Width=\"180px\" Filterable=\"false\"", list);
        Assert.Contains("Format_Grid(m_grdMaster);", list);
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
