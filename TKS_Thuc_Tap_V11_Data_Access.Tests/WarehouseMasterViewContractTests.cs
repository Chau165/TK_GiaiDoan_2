using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseMasterViewContractTests
{
    [Fact]
    public void Five_master_views_project_the_table_and_filter_only_deleted_rows()
    {
        string v_strViews = File.ReadAllText(FindRepositoryPath("Database", "WarehouseMaster.Views.sql"));
        string v_strSchema = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Schema.sql"));
        (string View, string Table, string[] Columns, string Index)[] v_arrContracts =
        [
            ("view_DM_Don_Vi_Tinh", "tbl_DM_Don_Vi_Tinh", ["Auto_ID", "Ten_Don_Vi_Tinh", "Ghi_Chu", "deleted", "Created", "Created_By", "Created_By_Function", "Last_Updated", "Last_Updated_By", "Last_Updated_By_Function"], "UX_tbl_DM_Don_Vi_Tinh_Ten"),
            ("view_DM_Kho", "tbl_DM_Kho", ["Auto_ID", "Ten_Kho", "Ghi_Chu", "deleted", "Created", "Created_By", "Created_By_Function", "Last_Updated", "Last_Updated_By", "Last_Updated_By_Function"], "UX_tbl_DM_Kho_Ten"),
            ("view_DM_NCC", "tbl_DM_NCC", ["Auto_ID", "Ma_NCC", "Ten_NCC", "Ghi_Chu", "deleted", "Created", "Created_By", "Created_By_Function", "Last_Updated", "Last_Updated_By", "Last_Updated_By_Function"], "UX_tbl_DM_NCC_Ma"),
            ("view_DM_Loai_San_Pham", "tbl_DM_Loai_San_Pham", ["Auto_ID", "Ma_LSP", "Ten_LSP", "Ghi_Chu", "deleted", "Created", "Created_By", "Created_By_Function", "Last_Updated", "Last_Updated_By", "Last_Updated_By_Function"], "UX_tbl_DM_Loai_San_Pham_Ma"),
            ("view_DM_San_Pham", "tbl_DM_San_Pham", ["Auto_ID", "Ma_San_Pham", "Ten_San_Pham", "Loai_San_Pham_ID", "Don_Vi_Tinh_ID", "Ghi_Chu", "deleted", "Created", "Created_By", "Created_By_Function", "Last_Updated", "Last_Updated_By", "Last_Updated_By_Function"], "UX_tbl_DM_San_Pham_Ma")
        ];

        foreach ((string v_strView, string v_strTable, string[] v_arrColumns, string v_strIndex) in v_arrContracts)
        {
            string v_strMarker = $"CREATE OR ALTER VIEW dbo.{v_strView}";
            int v_iStart = v_strViews.IndexOf(v_strMarker, StringComparison.OrdinalIgnoreCase);
            Assert.True(v_iStart >= 0, $"Missing view {v_strView}.");
            int v_iEnd = v_strViews.IndexOf("GO", v_iStart, StringComparison.OrdinalIgnoreCase);
            string v_strDefinition = v_iEnd < 0 ? v_strViews[v_iStart..] : v_strViews[v_iStart..v_iEnd];

            Assert.Contains($"FROM dbo.{v_strTable} AS A", v_strDefinition, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("WHERE (A.deleted = 0);", v_strDefinition, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("JOIN", v_strDefinition, StringComparison.OrdinalIgnoreCase);
            foreach (string v_strColumn in v_arrColumns)
                Assert.Contains("A." + v_strColumn, v_strDefinition, StringComparison.OrdinalIgnoreCase);

            Assert.Contains($"deleted INT NULL CONSTRAINT DF_{v_strTable}_deleted DEFAULT 0", v_strSchema, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CREATE UNIQUE INDEX " + v_strIndex, v_strSchema, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Master_updates_require_an_active_view_row_before_writing_the_table()
    {
        string v_strProcedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        (string Procedure, string View)[] v_arrContracts =
        [
            ("F2016_sp_upd_Don_Vi_Tinh", "view_DM_Don_Vi_Tinh"),
            ("F2009_sp_upd_Kho", "view_DM_Kho"),
            ("F2019_sp_upd_NCC", "view_DM_NCC"),
            ("F2017_sp_upd_Loai_San_Pham", "view_DM_Loai_San_Pham"),
            ("F2018_sp_upd_San_Pham", "view_DM_San_Pham")
        ];

        foreach ((string v_strProcedure, string v_strView) in v_arrContracts)
        {
            string v_strBody = ExtractProcedure(v_strProcedures, v_strProcedure);
            Assert.Contains($"FROM dbo.{v_strView} WHERE Auto_ID=@Auto_ID", v_strBody, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("UPDATE dbo.tbl_DM_", v_strBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE dbo.view_DM_", v_strBody, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FindRepositoryPath(params string[] p_arrParts)
    {
        DirectoryInfo? v_objDirectory = new(AppContext.BaseDirectory);
        while (v_objDirectory is not null)
        {
            string v_strCandidate = Path.Combine([v_objDirectory.FullName, .. p_arrParts]);
            if (File.Exists(v_strCandidate) || Directory.Exists(v_strCandidate))
                return v_strCandidate;
            v_objDirectory = v_objDirectory.Parent;
        }

        throw new DirectoryNotFoundException(string.Join(Path.DirectorySeparatorChar, p_arrParts));
    }

    private static string ExtractProcedure(string p_strSource, string p_strProcedure)
    {
        int v_iStart = p_strSource.IndexOf($"CREATE OR ALTER PROCEDURE dbo.{p_strProcedure}", StringComparison.OrdinalIgnoreCase);
        Assert.True(v_iStart >= 0, $"Missing procedure {p_strProcedure}.");
        int v_iEnd = p_strSource.IndexOf("GO", v_iStart, StringComparison.OrdinalIgnoreCase);
        return v_iEnd < 0 ? p_strSource[v_iStart..] : p_strSource[v_iStart..v_iEnd];
    }
}
