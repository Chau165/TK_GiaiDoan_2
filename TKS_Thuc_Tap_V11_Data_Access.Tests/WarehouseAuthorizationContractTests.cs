using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseAuthorizationContractTests
{
    [Fact]
    public void Warehouse_document_controller_carries_the_current_login_into_every_operation()
    {
        var v_Source = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseDocument_Controller.cs"));

        Assert.Contains("p_strCurrent_Login", v_Source);
        Assert.Contains("@Ma_Dang_Nhap", v_Source);
        Assert.Contains("sp_XNK_Document_Page", v_Source);
        Assert.Contains("sp_XNK_Document_Post", v_Source);
    }

    [Fact]
    public void Warehouse_database_contract_has_a_server_side_scope_guard_and_filters_reads()
    {
        var v_Source = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));

        Assert.Contains("sp_DM_Kho_User_Ensure_Access", v_Source);
        Assert.Contains("@Ma_Dang_Nhap NVARCHAR(100)", v_Source);
        Assert.Contains("sp_DM_Kho_User_List_Allowed", v_Source);
        Assert.Contains("ku.Ma_Dang_Nhap = @Ma_Dang_Nhap", v_Source);
    }

    [Fact]
    public void Warehouse_user_editor_uses_existing_users_and_displays_the_assignment_scope()
    {
        var v_Editor = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_3_Warehouse_Edit.razor"));
        var v_List = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("m_arrUser", v_Editor);
        Assert.Contains("Mã đăng nhập / user", v_Editor);
        Assert.Contains("Tên kho", v_List);
        Assert.Contains("Họ tên", v_List);
    }

    [Fact]
    public void Warehouse_user_grid_hides_filter_buttons_after_switching_to_the_assignment_tab()
    {
        var v_List = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("ShowFilterCellButtons=\"false\"", v_List);
        Assert.DoesNotContain("Field=\"Login_Name\" Title=\"Mã đăng nhập\" Width=\"180px\" Filterable=\"false\"", v_List);
        Assert.Contains("Format_Grid(m_grdMaster);", v_List);
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
