using System.Text.RegularExpressions;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseGroupCDesharingContractTests
{
    [Fact]
    public void Master_lookup_stores_are_split_by_function_and_entity()
    {
        var v_strSql = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseMaster_Controller.cs"));
        var v_arrEntities = new[]
        {
            ("Kho", "F2009", "tbl_DM_Kho"),
            ("Don_Vi_Tinh", "F2016", "tbl_DM_Don_Vi_Tinh"),
            ("Loai_San_Pham", "F2017", "tbl_DM_Loai_San_Pham"),
            ("San_Pham", "F2018", "tbl_DM_San_Pham"),
            ("NCC", "F2019", "tbl_DM_NCC")
        };

        foreach (var (v_strMeaning, v_strFamily, v_strTable) in v_arrEntities)
        {
            var v_strLookupName = $"{v_strFamily}_sp_sel_List_{v_strMeaning}_Lookup";
            var v_strLookup = ExtractProcedure(v_strSql, v_strLookupName);

            Assert.DoesNotContain("@Entity", v_strLookup, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"FROM dbo.{v_strTable}", v_strLookup, StringComparison.Ordinal);
            Assert.Contains($"\"{v_strLookupName}\"", v_strController, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("\"sp_DM_Lookup_List\"", v_strController, StringComparison.Ordinal);
    }

    [Fact]
    public void Master_list_stores_are_split_by_function_and_entity()
    {
        var v_strSql = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseMaster_Controller.cs"));
        var v_arrEntities = new[]
        {
            ("Kho", "F2009", "tbl_DM_Kho"),
            ("Don_Vi_Tinh", "F2016", "tbl_DM_Don_Vi_Tinh"),
            ("Loai_San_Pham", "F2017", "tbl_DM_Loai_San_Pham"),
            ("San_Pham", "F2018", "tbl_DM_San_Pham"),
            ("NCC", "F2019", "tbl_DM_NCC")
        };

        foreach (var (v_strMeaning, v_strFamily, v_strTable) in v_arrEntities)
        {
            var v_strStoreName = $"{v_strFamily}_sp_sel_List_{v_strMeaning}";
            var v_strStore = ExtractProcedure(v_strSql, v_strStoreName);

            Assert.DoesNotContain("@Entity", v_strStore, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"FROM dbo.{v_strTable}", v_strStore, StringComparison.Ordinal);
            Assert.Contains($"\"{v_strStoreName}\"", v_strController, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("\"sp_DM_Master_List\"", v_strController, StringComparison.Ordinal);
    }

    [Fact]
    public void Master_delete_stores_are_split_by_function_and_entity()
    {
        var v_strSql = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseMaster_Controller.cs"));
        var v_arrEntities = new[]
        {
            ("Kho", "F2009", "tbl_DM_Kho"),
            ("Don_Vi_Tinh", "F2016", "tbl_DM_Don_Vi_Tinh"),
            ("Loai_San_Pham", "F2017", "tbl_DM_Loai_San_Pham"),
            ("San_Pham", "F2018", "tbl_DM_San_Pham"),
            ("NCC", "F2019", "tbl_DM_NCC")
        };

        foreach (var (v_strMeaning, v_strFamily, v_strTable) in v_arrEntities)
        {
            var v_strDeleteName = $"{v_strFamily}_sp_del_{v_strMeaning}";
            var v_strDelete = ExtractProcedure(v_strSql, v_strDeleteName);

            Assert.DoesNotContain("@Entity", v_strDelete, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"DELETE FROM dbo.{v_strTable}", v_strDelete, StringComparison.Ordinal);
            Assert.Contains($"\"{v_strDeleteName}\"", v_strController, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("\"sp_DM_Delete\"", v_strController, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Master_delete_rejects_unknown_entity_before_database_call()
    {
        var v_objController = new CWarehouseMaster_Controller();
        var v_objException = await Assert.ThrowsAsync<ArgumentException>(() =>
            v_objController.Delete_Master_Async("Unknown", 0));

        Assert.Contains("Loại danh mục không hợp lệ.", v_objException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Document_list_stores_are_split_by_receipt_or_issue()
    {
        var v_strSql = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseDocument_Controller.cs"));
        var v_strReceiptList = ExtractProcedure(v_strSql, "F2011_sp_sel_List_Nhap_Kho");
        var v_strIssueList = ExtractProcedure(v_strSql, "F2012_sp_sel_List_Xuat_Kho");
        Assert.DoesNotContain("@Is_Receipt", v_strReceiptList, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@Is_Receipt", v_strIssueList, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tbl_XNK_Nhap_Kho", v_strReceiptList, StringComparison.Ordinal);
        Assert.DoesNotContain("tbl_XNK_Xuat_Kho", v_strReceiptList, StringComparison.Ordinal);
        Assert.Contains("tbl_XNK_Xuat_Kho", v_strIssueList, StringComparison.Ordinal);
        Assert.DoesNotContain("tbl_XNK_Nhap_Kho", v_strIssueList, StringComparison.Ordinal);
        Assert.Contains("\"F2011_sp_sel_List_Nhap_Kho\"", v_strController, StringComparison.Ordinal);
        Assert.Contains("\"F2012_sp_sel_List_Xuat_Kho\"", v_strController, StringComparison.Ordinal);
        Assert.DoesNotContain("\"sp_XNK_Document_List\"", v_strController, StringComparison.Ordinal);
    }

    [Fact]
    public void Document_detail_stores_are_split_by_receipt_or_issue()
    {
        var v_strSql = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseDocument_Controller.cs"));
        var v_strReceiptDetail = ExtractProcedure(v_strSql, "F2011_sp_sel_List_Nhap_Kho_Detail");
        var v_strIssueDetail = ExtractProcedure(v_strSql, "F2012_sp_sel_List_Xuat_Kho_Detail");

        Assert.DoesNotContain("@Is_Receipt", v_strReceiptDetail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@Is_Receipt", v_strIssueDetail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tbl_XNK_Nhap_Kho_Raw_Data", v_strReceiptDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("tbl_XNK_Xuat_Kho_Raw_Data", v_strReceiptDetail, StringComparison.Ordinal);
        Assert.Contains("tbl_XNK_Xuat_Kho_Raw_Data", v_strIssueDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("tbl_XNK_Nhap_Kho_Raw_Data", v_strIssueDetail, StringComparison.Ordinal);
        Assert.Contains("\"F2011_sp_sel_List_Nhap_Kho_Detail\"", v_strController, StringComparison.Ordinal);
        Assert.Contains("\"F2012_sp_sel_List_Xuat_Kho_Detail\"", v_strController, StringComparison.Ordinal);
        Assert.DoesNotContain("\"sp_XNK_Document_Detail_List\"", v_strController, StringComparison.Ordinal);
    }

    [Fact]
    public void Issue_detail_report_keeps_its_single_feature_store_pending_naming_rule()
    {
        var v_strSql = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseReport_Controller.cs"));
        var v_strOldProcedure = ExtractProcedure(v_strSql, "sp_BC_Chi_Tiet_Xuat");

        Assert.DoesNotContain("@Is_Receipt", v_strOldProcedure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("v_strProcedure = \"sp_BC_Chi_Tiet_Xuat\"", v_strController, StringComparison.Ordinal);
    }

    [Fact]
    public void Inventory_report_keeps_its_current_store_until_report_naming_rule_is_proven()
    {
        var v_strSql = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseReport_Controller.cs"));
        var v_strProcedure = ExtractProcedure(v_strSql, "sp_BC_Xuat_Nhap_Ton");

        Assert.Contains("CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton", v_strProcedure, StringComparison.Ordinal);
        Assert.Contains("List_From_Procedure<CWarehouseInventoryReport>(\"sp_BC_Xuat_Nhap_Ton\"", v_strController, StringComparison.Ordinal);
    }

    [Fact]
    public void Retired_business_dispatchers_are_removed_from_deployable_sql()
    {
        var v_arrRetiredNames = new[]
        {
            "sp_DM_Master_List",
            "sp_DM_Delete",
            "sp_DM_Lookup_List",
            "sp_XNK_Document_List",
            "sp_XNK_Document_Detail_List"
        };
        var v_arrPaths = new[]
        {
            FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"),
            FindRepositoryPath("Database", "WarehouseModule.Security.sql"),
            FindRepositoryPath("Database", "WarehouseModule.Security.Preflight.sql")
        };

        foreach (var v_strPath in v_arrPaths)
        {
            var v_strSource = File.ReadAllText(v_strPath);
            foreach (var v_strOldName in v_arrRetiredNames)
            {
                Assert.DoesNotContain(v_strOldName, v_strSource, StringComparison.Ordinal);
            }
        }
    }

    private static string ExtractProcedure(string p_strSource, string p_strProcedureName)
    {
        var v_strPattern = $@"(?ims)^CREATE\s+OR\s+ALTER\s+PROCEDURE\s+dbo\.{Regex.Escape(p_strProcedureName)}\b.*?^GO\s*$";
        var v_objMatch = Regex.Match(p_strSource, v_strPattern);
        Assert.True(v_objMatch.Success, $"Procedure definition not found: {p_strProcedureName}");
        return v_objMatch.Value;
    }

    private static string NormalizeLineEndings(string p_strValue)
    {
        return p_strValue.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }

    private static string FindRepositoryPath(params string[] p_arrParts)
    {
        for (var v_objDirectory = new DirectoryInfo(AppContext.BaseDirectory); v_objDirectory is not null; v_objDirectory = v_objDirectory.Parent)
        {
            var v_strCandidate = Path.Combine(new[] { v_objDirectory.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_strCandidate))
            {
                return v_strCandidate;
            }
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(p_arrParts)}");
    }
}
