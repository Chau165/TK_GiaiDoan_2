using System.Data;
using System.Reflection;
using System.Text.RegularExpressions;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseXnkSplitContractTests
{
    private static readonly string[] m_arrNhap_Stores =
    [
        "F3001_sp_sel_List_Nhap_Kho",
        "F3001_sp_sel_List_Nhap_Kho_Detail",
        "F3001_sp_ins_Nhap_Kho_Header",
        "F3001_sp_upd_Nhap_Kho_Header",
        "F3001_sp_del_Nhap_Kho_Header",
        "F3001_sp_ins_Nhap_Kho_Detail",
        "F3001_sp_upd_Nhap_Kho_Detail",
        "F3001_sp_del_Nhap_Kho_Detail"
    ];

    private static readonly string[] m_arrXuat_Stores =
    [
        "F3002_sp_sel_List_Xuat_Kho",
        "F3002_sp_sel_List_Xuat_Kho_Detail",
        "F3002_sp_ins_Xuat_Kho_Header",
        "F3002_sp_upd_Xuat_Kho_Header",
        "F3002_sp_del_Xuat_Kho_Header",
        "F3002_sp_ins_Xuat_Kho_Detail",
        "F3002_sp_upd_Xuat_Kho_Detail",
        "F3002_sp_del_Xuat_Kho_Detail"
    ];

    [Fact]
    public void Four_typed_XNK_entities_and_two_business_controllers_exist()
    {
        Assembly v_objAssembly = typeof(CUtility).Assembly;
        foreach (string v_strType_Name in new[]
        {
            "TKS_Thuc_Tap_V11_Data_Access.Entity.XNK.CXNK_Nhap_Kho",
            "TKS_Thuc_Tap_V11_Data_Access.Entity.XNK.CXNK_Nhap_Kho_Detail",
            "TKS_Thuc_Tap_V11_Data_Access.Entity.XNK.CXNK_Xuat_Kho",
            "TKS_Thuc_Tap_V11_Data_Access.Entity.XNK.CXNK_Xuat_Kho_Detail",
            "TKS_Thuc_Tap_V11_Data_Access.Controller.XNK.CXNK_Nhap_Kho_Controller",
            "TKS_Thuc_Tap_V11_Data_Access.Controller.XNK.CXNK_Xuat_Kho_Controller",
            "TKS_Thuc_Tap_V11_Data_Access.Controller.XNK.CWarehouseDocumentSupport_Controller"
        })
        {
            Assert.NotNull(v_objAssembly.GetType(v_strType_Name));
        }
    }

    [Theory]
    [InlineData("CXNK_Nhap_Kho_Controller", 3001)]
    [InlineData("CXNK_Xuat_Kho_Controller", 3002)]
    public void Typed_controller_exposes_only_its_eight_store_methods(
        string p_strController_Name,
        int p_iFunction_Code)
    {
        string v_strType_Name = $"TKS_Thuc_Tap_V11_Data_Access.Controller.XNK.{p_strController_Name}";
        Type v_objController_Type = typeof(CUtility).Assembly.GetType(v_strType_Name)
            ?? throw new InvalidOperationException($"Controller type {v_strType_Name} was not found.");
        string[] v_arrExpected_Stores = p_iFunction_Code == 3001 ? m_arrNhap_Stores : m_arrXuat_Stores;
        string[] v_arrActual_Methods = v_objController_Type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(v_objMethod => v_objMethod.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v_strName => v_strName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(v_arrExpected_Stores.OrderBy(v_strName => v_strName, StringComparer.Ordinal), v_arrActual_Methods);
        Assert.Equal(10, v_objController_Type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length);

        string v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "XNK", p_strController_Name + ".cs"));
        Assert.DoesNotContain(p_iFunction_Code == 3001 ? "F3002_" : "F3001_", v_strSource, StringComparison.Ordinal);
        Assert.DoesNotContain("p_bIs_Receipt", v_strSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CWarehouseDocument", v_strSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CWarehouseDocumentDetail", v_strSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Auto_ID == 0", v_strSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.FromResult", v_strSource, StringComparison.Ordinal);

        foreach (string v_strStore in v_arrExpected_Stores)
        {
            string[] v_arrBodies = ExtractMethodBodies(v_strSource, v_strStore);
            Assert.Equal(v_strStore.Contains("_ins_") ? 2 : 1, v_arrBodies.Length);
            foreach (string v_strBody in v_arrBodies)
            {
                string[] v_arrStore_Literals = Regex.Matches(v_strBody, "\"((?:F|sp)[A-Za-z0-9_]+)\"")
                    .Select(v_objMatch => v_objMatch.Groups[1].Value)
                    .Where(v_strLiteral => v_strLiteral.StartsWith("F3", StringComparison.Ordinal) || v_strLiteral.StartsWith("sp_", StringComparison.Ordinal))
                    .ToArray();
                Assert.Equal([v_strStore], v_arrStore_Literals);
            }
        }

        Assert.Empty(v_objController_Type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(v_objMethod => v_objMethod.Name.StartsWith("F", StringComparison.Ordinal))
            .SelectMany(v_objMethod => v_objMethod.GetParameters())
            .Where(v_objParameter => v_objParameter.ParameterType == typeof(bool))
            .Where(v_objParameter => v_objParameter.Name?.Contains("Receipt", StringComparison.OrdinalIgnoreCase) == true));
    }

    [Fact]
    public void Four_insert_transaction_overloads_accept_the_callers_connection_and_transaction()
    {
        Assembly v_objAssembly = typeof(CUtility).Assembly;
        (string Controller, string Store)[] v_arrContracts =
        [
            ("CXNK_Nhap_Kho_Controller", "F3001_sp_ins_Nhap_Kho_Header"),
            ("CXNK_Nhap_Kho_Controller", "F3001_sp_ins_Nhap_Kho_Detail"),
            ("CXNK_Xuat_Kho_Controller", "F3002_sp_ins_Xuat_Kho_Header"),
            ("CXNK_Xuat_Kho_Controller", "F3002_sp_ins_Xuat_Kho_Detail")
        ];

        foreach ((string v_strController, string v_strStore) in v_arrContracts)
        {
            Type v_objType = v_objAssembly.GetType(
                $"TKS_Thuc_Tap_V11_Data_Access.Controller.XNK.{v_strController}")
                ?? throw new InvalidOperationException($"Controller {v_strController} was not found.");
            MethodInfo[] v_arrOverloads = v_objType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(v_objMethod => v_objMethod.Name == v_strStore)
                .ToArray();
            Assert.Equal(2, v_arrOverloads.Length);
            MethodInfo v_objTransaction_Overload = Assert.Single(v_arrOverloads,
                v_objMethod => v_objMethod.GetParameters().Length == 3);
            ParameterInfo[] v_arrParameters = v_objTransaction_Overload.GetParameters();
            Assert.Equal(typeof(Microsoft.Data.SqlClient.SqlConnection), v_arrParameters[0].ParameterType);
            Assert.Equal(typeof(Microsoft.Data.SqlClient.SqlTransaction), v_arrParameters[1].ParameterType);
        }
    }

    [Fact]
    public void Four_F3001_F3002_list_and_detail_projections_match_typed_entity_fields()
    {
        string v_strSource = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        (string Store, string[] Columns)[] v_arrContracts =
        [
            ("F3001_sp_sel_List_Nhap_Kho", ["Auto_ID", "Is_Receipt", "So_Phieu_Nhap_Kho", "Kho_ID", "Ten_Kho", "NCC_ID", "Ten_NCC", "Ngay_Nhap_Kho", "Is_Posted", "Ghi_Chu"]),
            ("F3001_sp_sel_List_Nhap_Kho_Detail", ["Auto_ID", "Nhap_Kho_ID", "San_Pham_ID", "Ma_San_Pham", "Ten_San_Pham", "Ten_Don_Vi_Tinh", "SL_Nhap", "Don_Gia_Nhap"]),
            ("F3002_sp_sel_List_Xuat_Kho", ["Auto_ID", "Is_Receipt", "So_Phieu_Xuat_Kho", "Kho_ID", "Ten_Kho", "NCC_ID", "Ten_NCC", "Ngay_Xuat_Kho", "Is_Posted", "Ghi_Chu"]),
            ("F3002_sp_sel_List_Xuat_Kho_Detail", ["Auto_ID", "Xuat_Kho_ID", "San_Pham_ID", "Ma_San_Pham", "Ten_San_Pham", "Ten_Don_Vi_Tinh", "SL_Xuat", "Don_Gia_Xuat"])
        ];

        foreach ((string v_strStore, string[] v_arrExpected_Columns) in v_arrContracts)
        {
            string v_strProcedure = ExtractProcedure(v_strSource, v_strStore);
            MatchCollection v_arrSelects = Regex.Matches(v_strProcedure,
                @"\bSELECT\s+(?<columns>.*?)\s+FROM\s+dbo\.",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            Match? v_objSelect = v_arrSelects.Cast<Match>().FirstOrDefault(v_objMatch =>
                GetResultColumnName(v_objMatch.Groups["columns"].Value.Split(',')[0].Trim()) == "Auto_ID");
            Assert.NotNull(v_objSelect);
            string[] v_arrColumns = v_objSelect.Groups["columns"].Value
                .Split(',')
                .Select(v_strExpression => GetResultColumnName(v_strExpression.Trim()))
                .ToArray();
            Assert.Equal(v_arrExpected_Columns, v_arrColumns);
        }
    }

    [Fact]
    public void Shared_page_and_post_stores_are_owned_by_the_temporary_support_controller()
    {
        Type v_objType = typeof(CUtility).Assembly.GetType(
            "TKS_Thuc_Tap_V11_Data_Access.Controller.XNK.CWarehouseDocumentSupport_Controller")
            ?? throw new InvalidOperationException("Temporary XNK support controller was not found.");
        string[] v_arrMethods = v_objType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(v_objMethod => v_objMethod.Name)
            .OrderBy(v_strName => v_strName, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["sp_XNK_Document_Page", "sp_XNK_Document_Post"], v_arrMethods);

        string v_strSource = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "XNK", "CWarehouseDocumentSupport_Controller.cs"));
        Assert.Contains("Page_From_Procedure<CWarehouseDocument>(\"sp_XNK_Document_Page\"", v_strSource, StringComparison.Ordinal);
        Assert.Contains("Execute_Procedure(\"sp_XNK_Document_Post\"", v_strSource, StringComparison.Ordinal);
        Assert.DoesNotContain("F3001_", v_strSource, StringComparison.Ordinal);
        Assert.DoesNotContain("F3002_", v_strSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Save_Imported", v_strSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Typed_read_methods_use_direct_entity_mapping_and_no_alias_translation()
    {
        foreach ((string v_strController, string v_strEntity) in new[]
        {
            ("CXNK_Nhap_Kho_Controller", "CXNK_Nhap_Kho"),
            ("CXNK_Nhap_Kho_Controller", "CXNK_Nhap_Kho_Detail"),
            ("CXNK_Xuat_Kho_Controller", "CXNK_Xuat_Kho"),
            ("CXNK_Xuat_Kho_Controller", "CXNK_Xuat_Kho_Detail")
        })
        {
            string v_strSource = File.ReadAllText(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Data_Access", "Controller", "XNK", v_strController + ".cs"));
            Assert.Contains($"CUtility.Map_Row_To_Entity<{v_strEntity}>(v_row)", v_strSource, StringComparison.Ordinal);
            Assert.DoesNotContain("v_row[", v_strSource, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Representative_typed_rows_map_without_manual_alias_translation()
    {
        AssertMapped("CXNK_Nhap_Kho",
        [
            ("Auto_ID", 101L), ("Is_Receipt", true), ("So_Phieu_Nhap_Kho", "NK-101"),
            ("Kho_ID", 12L), ("Ten_Kho", "Kho A"), ("NCC_ID", 19L), ("Ten_NCC", "NCC A"),
            ("Ngay_Nhap_Kho", new DateTime(2026, 10, 12)), ("Is_Posted", false), ("Ghi_Chu", "note")
        ]);
        AssertMapped("CXNK_Nhap_Kho_Detail",
        [
            ("Auto_ID", 201L), ("Nhap_Kho_ID", 101L), ("San_Pham_ID", 31L),
            ("Ma_San_Pham", "SP-31"), ("Ten_San_Pham", "Product 31"),
            ("Ten_Don_Vi_Tinh", "Cái"), ("SL_Nhap", 2.5m), ("Don_Gia_Nhap", 1200.25m)
        ]);
        AssertMapped("CXNK_Xuat_Kho",
        [
            ("Auto_ID", 102L), ("Is_Receipt", false), ("So_Phieu_Xuat_Kho", "XK-102"),
            ("Kho_ID", 13L), ("Ten_Kho", "Kho B"), ("NCC_ID", 0L), ("Ten_NCC", ""),
            ("Ngay_Xuat_Kho", new DateTime(2026, 10, 13)), ("Is_Posted", true), ("Ghi_Chu", "out")
        ]);
        AssertMapped("CXNK_Xuat_Kho_Detail",
        [
            ("Auto_ID", 202L), ("Xuat_Kho_ID", 102L), ("San_Pham_ID", 32L),
            ("Ma_San_Pham", "SP-32"), ("Ten_San_Pham", "Product 32"),
            ("Ten_Don_Vi_Tinh", "Thùng"), ("SL_Xuat", 1.25m), ("Don_Gia_Xuat", 4500.50m)
        ]);
    }

    [Fact]
    public void Twelve_write_methods_match_the_SQL_parameter_order()
    {
        string v_strSql = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        (string Controller, string[] Stores)[] v_arrControllers =
        [
            ("CXNK_Nhap_Kho_Controller", m_arrNhap_Stores.Where(v_strStore => v_strStore.Contains("_ins_") || v_strStore.Contains("_upd_") || v_strStore.Contains("_del_")).ToArray()),
            ("CXNK_Xuat_Kho_Controller", m_arrXuat_Stores.Where(v_strStore => v_strStore.Contains("_ins_") || v_strStore.Contains("_upd_") || v_strStore.Contains("_del_")).ToArray())
        ];

        foreach ((string v_strController, string[] v_arrStores) in v_arrControllers)
        {
            string v_strController_Source = File.ReadAllText(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Data_Access", "Controller", "XNK", v_strController + ".cs"));
            foreach (string v_strStore in v_arrStores)
            {
                Match v_objSignature = Regex.Match(ExtractProcedure(v_strSql, v_strStore),
                    @"(?is)^CREATE OR ALTER PROCEDURE dbo\.[A-Za-z0-9_]+\s*(?<parameters>.*?)\s+AS\s+BEGIN");
                string[] v_arrSql_Parameters = Regex.Matches(v_objSignature.Groups["parameters"].Value, @"@[A-Za-z_][A-Za-z0-9_]*")
                    .Select(v_objMatch => v_objMatch.Value)
                    .ToArray();
                string[] v_arrBodies = ExtractMethodBodies(v_strController_Source, v_strStore);
                Assert.Equal(v_strStore.Contains("_ins_") ? 2 : 1, v_arrBodies.Length);
                foreach (string v_strBody in v_arrBodies)
                {
                    string[] v_arrController_Parameters = Regex.Matches(v_strBody, "\"(@[A-Za-z_][A-Za-z0-9_]*)\"")
                        .Select(v_objMatch => v_objMatch.Groups[1].Value)
                        .ToArray();
                    Assert.Equal(v_arrSql_Parameters, v_arrController_Parameters);
                }
            }
        }
    }

    [Fact]
    public void XNK_page_post_write_and_technical_inventory_sql_match_the_frozen_baseline()
    {
        string v_strSource = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        Dictionary<string, string> v_dicExpected_Hashes = new()
        {
            ["sp_XNK_Document_Page"] = "8D2C2E57CCEA12ADC77BB5197B51065FC8412FE2703D8FEA0562E483DA1DF2FD",
            ["sp_XNK_Document_Post"] = "4785A3E575A891A30CCFE09D1EAEE4B23784B2950D022FB821B7A2627B2227AF",
            ["F3001_sp_ins_Nhap_Kho_Header"] = "1E73BD1324764C104B1E931B2EEC2EDDA31D072E05A7D4F7E25F417D66699E7A",
            ["F3001_sp_upd_Nhap_Kho_Header"] = "007A2834EA586C7AD9560A13024D91A7ED90CD6951795EFCA6C82C0BE7D8C2D8",
            ["F3001_sp_del_Nhap_Kho_Header"] = "6A6782BECE37ABC2662E245BA7A748933CD6B0C20C359D41D71DDCBC1A1DBCC4",
            ["F3001_sp_ins_Nhap_Kho_Detail"] = "DF32913C35856C05360B12E8113D39B30F3C30FD037E14C5D894F5AA4AC40BDB",
            ["F3001_sp_upd_Nhap_Kho_Detail"] = "3911FE4FE30951E744F9C7C5D73D5E46A1B0E4861B3591371962517A786F3103",
            ["F3001_sp_del_Nhap_Kho_Detail"] = "257CEF84E3E378AAAEDECBDC693866C69C28A044FE75E96EA5A36B1084ADD250",
            ["F3002_sp_ins_Xuat_Kho_Header"] = "3020F5C50D90BB0CAA8174F3452989450AFEB584A8EADFF4B637ADB069411606",
            ["F3002_sp_upd_Xuat_Kho_Header"] = "44E112BDF2257F427467EAEDE457423EC23480345E970CDC29ED2090364FFDAE",
            ["F3002_sp_del_Xuat_Kho_Header"] = "FF3645AFC8C738AAA177829BFE14BC94C6F91D341EDC05952DF54650F20E5583",
            ["F3002_sp_ins_Xuat_Kho_Detail"] = "B5172D04F3729361256CD2573698CA940358D52A869BD4BDF47F665C8783A85F",
            ["F3002_sp_upd_Xuat_Kho_Detail"] = "264D3DEDEAC66C7BD0E6B4DA4934F0DDF6EA96947E5C8F229D5B07562A9F006B",
            ["F3002_sp_del_Xuat_Kho_Detail"] = "7D2D03ABC85F0DDD960E7C446356AB9DDC6D08684ED252B4C233C8FF54DECFF6"
        };

        foreach ((string v_strName, string v_strExpectedHash) in v_dicExpected_Hashes)
        {
            string v_strProcedure = ExtractProcedure(v_strSource, v_strName);
            string v_strNormalized = Regex.Replace(v_strProcedure, "\\r\\n|\\r", "\n");
            Assert.Equal(v_strExpectedHash, Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(v_strNormalized))));
        }

        MatchCollection v_arrInventory_Procedures = Regex.Matches(v_strSource,
            @"^CREATE OR ALTER PROCEDURE dbo\.sp_Inventory_[A-Za-z0-9_]+\b.*?^GO\r?$",
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.IgnoreCase);
        string v_strInventory_Source = string.Join("\n", v_arrInventory_Procedures.Select(v_objMatch => v_objMatch.Value).OrderBy(v_strValue => v_strValue, StringComparer.Ordinal));
        string v_strInventory_Normalized = Regex.Replace(v_strInventory_Source, "\\r\\n|\\r", "\n");
        Assert.Equal(35, v_arrInventory_Procedures.Count);
        Assert.Equal("211244BD7F1F50BB899B1FB38C716E577202121B86A94600DEBAEEDCF5271F27",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(v_strInventory_Normalized))));
    }

    [Fact]
    public void Legacy_document_persistence_controller_is_retired()
    {
        Assert.DoesNotContain(
            typeof(CUtility).Assembly.GetTypes(),
            v_objType => v_objType.Name == "CWarehouseDocument_Controller");
    }

    private static string[] ExtractMethodBodies(string p_strSource, string p_strMethod_Name)
    {
        MatchCollection v_arrMatches = Regex.Matches(p_strSource,
            $@"\bpublic\s+[^;{{}}]+?\b{Regex.Escape(p_strMethod_Name)}\s*\(",
            RegexOptions.Singleline);
        List<string> v_arrBodies = new();
        foreach (Match v_objMatch in v_arrMatches)
        {
            int v_iOpen = p_strSource.IndexOf('{', v_objMatch.Index + v_objMatch.Length);
            if (v_iOpen < 0)
                continue;
            int v_iDepth = 0;
            for (int v_i = v_iOpen; v_i < p_strSource.Length; v_i++)
            {
                if (p_strSource[v_i] == '{')
                    v_iDepth++;
                else if (p_strSource[v_i] == '}' && --v_iDepth == 0)
                {
                    v_arrBodies.Add(p_strSource[(v_iOpen + 1)..v_i]);
                    break;
                }
            }
        }
        return v_arrBodies.ToArray();
    }

    private static void AssertMapped(string p_strEntity_Name, (string Name, object Value)[] p_arrValues)
    {
        using DataTable v_dt = new();
        foreach ((string v_strName, object v_objValue) in p_arrValues)
            v_dt.Columns.Add(v_strName, v_objValue.GetType());
        DataRow v_row = v_dt.NewRow();
        foreach ((string v_strName, object v_objValue) in p_arrValues)
            v_row[v_strName] = v_objValue;
        v_dt.Rows.Add(v_row);

        Type v_objEntity_Type = typeof(CUtility).Assembly.GetType(
            $"TKS_Thuc_Tap_V11_Data_Access.Entity.XNK.{p_strEntity_Name}")
            ?? throw new InvalidOperationException($"Entity type {p_strEntity_Name} was not found.");
        MethodInfo v_objMapper = typeof(CUtility).GetMethod(nameof(CUtility.Map_Row_To_Entity))!
            .MakeGenericMethod(v_objEntity_Type);
        object v_objEntity = v_objMapper.Invoke(null, [v_row])!;
        foreach ((string v_strName, object v_objExpected) in p_arrValues)
            Assert.Equal(v_objExpected, v_objEntity_Type.GetProperty(v_strName)!.GetValue(v_objEntity));
    }

    private static string ExtractProcedure(string p_strSource, string p_strProcedure_Name)
    {
        Match v_objProcedure = Regex.Match(p_strSource,
            $@"^CREATE OR ALTER PROCEDURE dbo\.{Regex.Escape(p_strProcedure_Name)}\b.*?^GO\r?$",
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Assert.True(v_objProcedure.Success, $"Stored procedure {p_strProcedure_Name} not found.");
        return v_objProcedure.Value;
    }

    private static string GetResultColumnName(string p_strExpression)
    {
        Match v_objAlias = Regex.Match(p_strExpression, @"\bAS\s+(?<name>[A-Za-z0-9_]+)\s*$", RegexOptions.IgnoreCase);
        if (v_objAlias.Success)
            return v_objAlias.Groups["name"].Value;
        Match v_objColumn = Regex.Match(p_strExpression, @"(?<name>[A-Za-z0-9_]+)\s*$");
        Assert.True(v_objColumn.Success, $"Cannot determine column name from '{p_strExpression}'.");
        return v_objColumn.Groups["name"].Value;
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
