using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseDmResultContractTests
{
    public static IEnumerable<object[]> ProcedureContracts =>
    [
        ["F2009_sp_sel_List_Kho", "F2009_sp_sel_List_Kho_Lookup", "view_DM_Kho", "Ten_Kho", new[] { "Auto_ID", "Ten_Kho", "Ghi_Chu" }, new[] { "Auto_ID", "Ten_Kho" }],
        ["F2016_sp_sel_List_Don_Vi_Tinh", "F2016_sp_sel_List_Don_Vi_Tinh_Lookup", "view_DM_Don_Vi_Tinh", "Ten_Don_Vi_Tinh", new[] { "Auto_ID", "Ten_Don_Vi_Tinh", "Ghi_Chu" }, new[] { "Auto_ID", "Ten_Don_Vi_Tinh" }],
        ["F2017_sp_sel_List_Loai_San_Pham", "F2017_sp_sel_List_Loai_San_Pham_Lookup", "view_DM_Loai_San_Pham", "Ma_LSP", new[] { "Auto_ID", "Ma_LSP", "Ten_LSP", "Ghi_Chu" }, new[] { "Auto_ID", "Ma_LSP", "Ten_LSP" }],
        ["F2018_sp_sel_List_San_Pham", "F2018_sp_sel_List_San_Pham_Lookup", "view_DM_San_Pham", "Ma_San_Pham", new[] { "Auto_ID", "Ma_San_Pham", "Ten_San_Pham", "Loai_San_Pham_ID", "Don_Vi_Tinh_ID", "Ghi_Chu" }, new[] { "Auto_ID", "Ma_San_Pham", "Ten_San_Pham" }],
        ["F2019_sp_sel_List_NCC", "F2019_sp_sel_List_NCC_Lookup", "view_DM_NCC", "Ma_NCC", new[] { "Auto_ID", "Ma_NCC", "Ten_NCC", "Ghi_Chu" }, new[] { "Auto_ID", "Ma_NCC", "Ten_NCC" }]
    ];

    [Theory]
    [MemberData(nameof(ProcedureContracts))]
    public void List_and_lookup_store_projection_names_match_the_dedicated_entity(
        string p_strList_Procedure,
        string p_strLookup_Procedure,
        string p_strTable_Name,
        string p_strOrder_By,
        string[] p_arrList_Columns,
        string[] p_arrLookup_Columns)
    {
        string v_strSource = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));

        AssertProcedureProjection(v_strSource, p_strList_Procedure, p_strTable_Name, p_strOrder_By, p_arrList_Columns);
        AssertProcedureProjection(v_strSource, p_strLookup_Procedure, p_strTable_Name, p_strOrder_By, p_arrLookup_Columns);
    }

    [Fact]
    public void All_DM_list_and_lookup_methods_map_rows_directly_without_alias_assignments()
    {
        (string Controller, string Entity, string ListMethod, string LookupMethod)[] v_arrContracts =
        [
            ("CDM_Kho_Controller", "CDM_Kho", "F2009_sp_sel_List_Kho", "F2009_sp_sel_List_Kho_Lookup"),
            ("CDM_Don_Vi_Tinh_Controller", "CDM_Don_Vi_Tinh", "F2016_sp_sel_List_Don_Vi_Tinh", "F2016_sp_sel_List_Don_Vi_Tinh_Lookup"),
            ("CDM_Loai_San_Pham_Controller", "CDM_Loai_San_Pham", "F2017_sp_sel_List_Loai_San_Pham", "F2017_sp_sel_List_Loai_San_Pham_Lookup"),
            ("CDM_San_Pham_Controller", "CDM_San_Pham", "F2018_sp_sel_List_San_Pham", "F2018_sp_sel_List_San_Pham_Lookup"),
            ("CDM_NCC_Controller", "CDM_NCC", "F2019_sp_sel_List_NCC", "F2019_sp_sel_List_NCC_Lookup")
        ];

        foreach ((string v_strController, string v_strEntity, string v_strList, string v_strLookup) in v_arrContracts)
        {
            string v_strControllerSource = File.ReadAllText(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Data_Access", "Controller", "DM", v_strController + ".cs"));

            AssertDirectMapping(v_strControllerSource, v_strEntity, v_strList);
            AssertDirectMapping(v_strControllerSource, v_strEntity, v_strLookup);
        }
    }

    [Fact]
    public void Dedicated_DM_entity_properties_map_from_list_and_lookup_result_columns()
    {
        (Type Entity, (string Name, object Value)[] List, (string Name, object Value)[] Lookup)[] v_arrContracts =
        [
            (typeof(CDM_Kho), [("Auto_ID", 101L), ("Ten_Kho", "Kho A"), ("Ghi_Chu", "note")], [("Auto_ID", 101L), ("Ten_Kho", "Kho A")]),
            (typeof(CDM_Don_Vi_Tinh), [("Auto_ID", 102L), ("Ten_Don_Vi_Tinh", "Cái"), ("Ghi_Chu", "note")], [("Auto_ID", 102L), ("Ten_Don_Vi_Tinh", "Cái")]),
            (typeof(CDM_Loai_San_Pham), [("Auto_ID", 103L), ("Ma_LSP", "LSP-1"), ("Ten_LSP", "Loại A"), ("Ghi_Chu", "note")], [("Auto_ID", 103L), ("Ma_LSP", "LSP-1"), ("Ten_LSP", "Loại A")]),
            (typeof(CDM_San_Pham), [("Auto_ID", 104L), ("Ma_San_Pham", "SP-1"), ("Ten_San_Pham", "Sản phẩm A"), ("Loai_San_Pham_ID", 103L), ("Don_Vi_Tinh_ID", 102L), ("Ghi_Chu", "note")], [("Auto_ID", 104L), ("Ma_San_Pham", "SP-1"), ("Ten_San_Pham", "Sản phẩm A")]),
            (typeof(CDM_NCC), [("Auto_ID", 105L), ("Ma_NCC", "NCC-1"), ("Ten_NCC", "Nhà cung cấp A"), ("Ghi_Chu", "note")], [("Auto_ID", 105L), ("Ma_NCC", "NCC-1"), ("Ten_NCC", "Nhà cung cấp A")])
        ];

        foreach ((Type v_objEntity, (string Name, object Value)[] v_arrList, (string Name, object Value)[] v_arrLookup) in v_arrContracts)
        {
            AssertMapped(v_objEntity, v_arrList);
            AssertMapped(v_objEntity, v_arrLookup);
        }
    }

    [Fact]
    public void Shared_DM_page_and_dedicated_cache_store_definitions_use_active_views()
    {
        (string Name, string[] Views)[] v_arrContracts =
        {
            ("sp_DM_Master_Page", ["view_DM_Don_Vi_Tinh", "view_DM_Kho", "view_DM_NCC", "view_DM_Loai_San_Pham", "view_DM_San_Pham"]),
            ("sp_DM_Lookup_Page", ["view_DM_Don_Vi_Tinh", "view_DM_Kho", "view_DM_NCC", "view_DM_Loai_San_Pham", "view_DM_San_Pham"]),
            ("F2009_sp_sel_List_Kho_For_Cache", ["view_DM_Kho"]),
            ("F2016_sp_sel_List_Don_Vi_Tinh_For_Cache", ["view_DM_Don_Vi_Tinh"]),
            ("F2017_sp_sel_List_Loai_San_Pham_For_Cache", ["view_DM_Loai_San_Pham"]),
            ("F2018_sp_sel_List_San_Pham_For_Cache", ["view_DM_San_Pham"]),
            ("F2019_sp_sel_List_NCC_For_Cache", ["view_DM_NCC"])
        };

        string v_strSource = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        foreach ((string v_strName, string[] v_arrViews) in v_arrContracts)
        {
            string v_strProcedure = ExtractProcedure(v_strSource, v_strName);
            foreach (string v_strView in v_arrViews)
                Assert.Contains("FROM dbo." + v_strView, v_strProcedure, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertProcedureProjection(
        string p_strSource,
        string p_strProcedure_Name,
        string p_strTable_Name,
        string p_strOrder_By,
        string[] p_arrExpected_Columns)
    {
        string v_strProcedure = ExtractProcedure(p_strSource, p_strProcedure_Name);
        Match v_objProjection = Regex.Match(v_strProcedure,
            @"\bSELECT\s+(?<columns>.*?)\s+FROM\s+dbo\.[A-Za-z0-9_]+",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        Assert.True(v_objProjection.Success, $"SELECT projection not found in {p_strProcedure_Name}.");
        string[] v_arrActual_Columns = v_objProjection.Groups["columns"].Value
            .Split(',')
            .Select(v_strExpression => GetResultColumnName(v_strExpression.Trim()))
            .ToArray();

        Assert.Equal(p_arrExpected_Columns, v_arrActual_Columns);
        foreach (string v_strForbidden_Alias in new[] { "AS Code", "AS Name", "AS Related_ID", "AS Related_ID_2", "AS Login_Name" })
            Assert.DoesNotContain(v_strForbidden_Alias, v_strProcedure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM dbo." + p_strTable_Name, v_strProcedure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY " + p_strOrder_By, v_strProcedure, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WHERE", v_strProcedure, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetResultColumnName(string p_strExpression)
    {
        Match v_objAlias = Regex.Match(p_strExpression, @"\bAS\s+(?<name>[A-Za-z0-9_]+)\s*$", RegexOptions.IgnoreCase);
        if (v_objAlias.Success)
            return v_objAlias.Groups["name"].Value;

        Match v_objColumn = Regex.Match(p_strExpression, @"(?<name>[A-Za-z0-9_]+)\s*$");
        Assert.True(v_objColumn.Success, $"Cannot determine result column from '{p_strExpression}'.");
        return v_objColumn.Groups["name"].Value;
    }

    private static void AssertDirectMapping(string p_strSource, string p_strEntity_Name, string p_strMethod_Name)
    {
        Match v_objSignature = Regex.Match(p_strSource,
            $@"public\s+List<{Regex.Escape(p_strEntity_Name)}>\s+{Regex.Escape(p_strMethod_Name)}\s*\(\s*\)\s*\{{",
            RegexOptions.Singleline);
        Assert.True(v_objSignature.Success, $"Method {p_strMethod_Name} not found.");

        int v_iOpen = p_strSource.IndexOf('{', v_objSignature.Index);
        int v_iDepth = 0;
        int v_iClose = -1;
        for (int v_i = v_iOpen; v_i < p_strSource.Length; v_i++)
        {
            if (p_strSource[v_i] == '{')
                v_iDepth++;
            else if (p_strSource[v_i] == '}' && --v_iDepth == 0)
            {
                v_iClose = v_i;
                break;
            }
        }

        Assert.True(v_iClose > v_iOpen, $"Method body for {p_strMethod_Name} is incomplete.");
        string v_strBody = p_strSource[(v_iOpen + 1)..v_iClose];
        Assert.Contains($"CUtility.Map_Row_To_Entity<{p_strEntity_Name}>(v_row)", v_strBody, StringComparison.Ordinal);
        Assert.DoesNotContain("v_row[", v_strBody, StringComparison.Ordinal);
    }

    private static void AssertMapped(Type p_objEntity_Type, (string Name, object Value)[] p_arrValues)
    {
        using DataTable v_dt = new();
        foreach ((string v_strName, object v_objValue) in p_arrValues)
            v_dt.Columns.Add(v_strName, v_objValue.GetType());

        DataRow v_row = v_dt.NewRow();
        foreach ((string v_strName, object v_objValue) in p_arrValues)
            v_row[v_strName] = v_objValue;
        v_dt.Rows.Add(v_row);

        MethodInfo v_objMapper = typeof(CUtility).GetMethod(nameof(CUtility.Map_Row_To_Entity))!
            .MakeGenericMethod(p_objEntity_Type);
        object v_objEntity = v_objMapper.Invoke(null, [v_row])!;

        foreach ((string v_strName, object v_objExpected) in p_arrValues)
            Assert.Equal(v_objExpected, p_objEntity_Type.GetProperty(v_strName)!.GetValue(v_objEntity));
    }

    private static string ExtractProcedure(string p_strSource, string p_strProcedure_Name)
    {
        Match v_objProcedure = Regex.Match(p_strSource,
            $@"^CREATE OR ALTER PROCEDURE dbo\.{Regex.Escape(p_strProcedure_Name)}\b.*?^GO\r?$",
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Assert.True(v_objProcedure.Success, $"Procedure {p_strProcedure_Name} not found.");
        return v_objProcedure.Value;
    }

    private static string FindRepositoryPath(params string[] p_arrPath_Parts)
    {
        DirectoryInfo? v_objDirectory = new(AppContext.BaseDirectory);
        while (v_objDirectory is not null)
        {
            string v_strCandidate = Path.Combine([v_objDirectory.FullName, .. p_arrPath_Parts]);
            if (File.Exists(v_strCandidate))
                return v_strCandidate;
            v_objDirectory = v_objDirectory.Parent;
        }

        throw new FileNotFoundException("Could not find repository file: " + Path.Combine(p_arrPath_Parts));
    }
}
