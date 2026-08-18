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

    private static string FindWarehousePage()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Warehouse.razor");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Warehouse.razor was not found from the test output directory.");
    }
}
