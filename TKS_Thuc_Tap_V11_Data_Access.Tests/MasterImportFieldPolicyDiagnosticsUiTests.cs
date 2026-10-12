using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class MasterImportFieldPolicyDiagnosticsUiTests
{
    [Fact]
    public void Every_master_import_dialog_renders_source_only_diagnostics()
    {
        var v_arrImportComponents = new[]
        {
            "FWarehouse_Unit_Import.razor",
            "FWarehouse_Kho_Import.razor",
            "FWarehouse_Category_Import.razor",
            "FWarehouse_Product_Import.razor",
            "FWarehouse_Supplier_Import.razor"
        };

        foreach (var v_strFileName in v_arrImportComponents)
        {
            var v_strSource = File.ReadAllText(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", v_strFileName));
            Assert.Contains("<FImportFieldPolicyDiagnostics", v_strSource);
            Assert.Contains("m_objPreview.Field_Policy_Diagnostics", v_strSource);
        }

        var v_strDiagnosticComponent = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FImportFieldPolicyDiagnostics.razor"));
        Assert.Contains("SOURCE_FIELD_IGNORED", v_strDiagnosticComponent);
        Assert.Contains("SourceColumn", v_strDiagnosticComponent);
        Assert.Contains("SourceValue", v_strDiagnosticComponent);
        Assert.Contains("Reason", v_strDiagnosticComponent);
    }

    private static string FindRepositoryPath(params string[] p_arrParts)
    {
        for (var v_objDirectory = new DirectoryInfo(AppContext.BaseDirectory); v_objDirectory is not null; v_objDirectory = v_objDirectory.Parent)
        {
            var v_strCandidate = Path.Combine(new[] { v_objDirectory.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_strCandidate))
                return v_strCandidate;
        }
        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(p_arrParts)}");
    }
}
