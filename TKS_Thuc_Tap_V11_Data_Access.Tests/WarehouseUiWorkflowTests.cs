using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseUiWorkflowTests
{
    [Fact]
    public void Warehouse_page_only_renders_editors_when_the_user_starts_an_editing_action()
    {
        var source = File.ReadAllText(FindWarehousePage());

        Assert.Contains("private bool m_bMasterEditing;", source);
        Assert.Contains("private bool m_bDocumentEditing;", source);
        Assert.Contains("private bool m_bDetailEditing;", source);
        Assert.Contains("@if (m_bMasterEditing)", source);
        Assert.Contains("@if (m_bDocumentEditing)", source);
        Assert.Contains("@if (m_bDetailEditing)", source);
        Assert.Contains("Thêm sản phẩm", source);
        Assert.Contains("var documentId=m_objDocument.Auto_ID;", source);
        Assert.Contains("var saved=m_arrDocument.FirstOrDefault", source);
        Assert.Contains("m_objDetail.Document_ID=m_objSelectedDocument.Auto_ID;", source);
    }

    [Fact]
    public void Warehouse_page_uses_the_company_list_info_edit_component_pattern()
    {
        var page = File.ReadAllText(FindWarehousePage());
        var list = File.ReadAllText(FindWarehouseComponent("FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("<FWarehouse_1_Warehouse_List />", page);
        Assert.Contains("@inherits FBase", list);
        Assert.Contains("FWarehouse_2_Warehouse_Info", list);
        Assert.Contains("FWarehouse_3_Warehouse_Edit", list);
        Assert.Contains("<TelerikGrid", list);
        Assert.Contains("Is_Have_Add_Permission", list);
        Assert.Contains("Is_Have_Edit_Permission", list);
        Assert.Contains("Is_Have_Delete_Permission", list);
        Assert.Contains("Is_Have_Export_Permission", list);
    }

    [Fact]
    public void Warehouse_data_access_uses_stored_procedures_and_audit_arguments()
    {
        var controllerDirectory = FindRepositoryDirectory("TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse");
        var master = File.ReadAllText(Path.Combine(controllerDirectory, "CWarehouseMaster_Controller.cs"));
        var document = File.ReadAllText(Path.Combine(controllerDirectory, "CWarehouseDocument_Controller.cs"));
        var report = File.ReadAllText(Path.Combine(controllerDirectory, "CWarehouseReport_Controller.cs"));
        var source = master + document + report;

        Assert.DoesNotContain("SELECT ", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CSqlHelper.FillDataTable", source);
        Assert.Contains("Last_Updated_By", source);
        Assert.Contains("Last_Updated_By_Function", source);
        Assert.Contains("sp_DM_Master_List", source);
        Assert.Contains("sp_XNK_Document_List", source);
        Assert.Contains("sp_XNK_Document_Detail_List", source);
    }

    [Fact]
    public void Warehouse_database_contract_contains_full_audit_columns()
    {
        var schema = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Schema.sql"));
        var procedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));

        foreach (var field in new[] { "Created_By", "Created_By_Function", "Last_Updated_By", "Last_Updated_By_Function" })
        {
            Assert.Contains(field, schema);
            Assert.Contains(field, procedures);
        }
    }

    private static string FindWarehousePage()
    {
        return FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Warehouse.razor");
    }

    private static string FindWarehouseComponent(string fileName)
    {
        return FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", fileName);
    }

    private static string FindRepositoryDirectory(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException($"Repository directory was not found: {Path.Combine(parts)}");
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
