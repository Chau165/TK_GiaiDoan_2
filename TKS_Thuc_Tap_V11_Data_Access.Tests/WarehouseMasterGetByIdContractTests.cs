using System.Data;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseMasterGetByIdContractTests
{
    [Fact]
    public void Get_by_id_controllers_match_company_mapping_and_info_uses_deployed_procedures()
    {
        (string Code, string Controller, string Entity, string Table, string Info, string Edit, string[] Columns)[] v_arrModules =
        [
            ("2009", "CDM_Kho_Controller.cs", "CDM_Kho", "tbl_DM_Kho", "F2009_2_Kho_Info.razor", "F2009_3_Kho_Edit.razor", ["Auto_ID", "Ten_Kho", "Ghi_Chu", "Created", "Created_By", "Created_By_Function", "Last_Updated", "Last_Updated_By", "Last_Updated_By_Function"]),
            ("2016", "CDM_Don_Vi_Tinh_Controller.cs", "CDM_Don_Vi_Tinh", "tbl_DM_Don_Vi_Tinh", "FWarehouse_Unit_Info.razor", "FWarehouse_Unit_Edit.razor", ["Auto_ID", "Ten_Don_Vi_Tinh", "Ghi_Chu", "Created", "Created_By", "Created_By_Function", "Last_Updated", "Last_Updated_By", "Last_Updated_By_Function"]),
            ("2017", "CDM_Loai_San_Pham_Controller.cs", "CDM_Loai_San_Pham", "tbl_DM_Loai_San_Pham", "F2017_2_Loai_San_Pham_Info.razor", "F2017_3_Loai_San_Pham_Edit.razor", ["Auto_ID", "Ma_LSP", "Ten_LSP", "Ghi_Chu", "Created", "Created_By", "Created_By_Function", "Last_Updated", "Last_Updated_By", "Last_Updated_By_Function"]),
            ("2018", "CDM_San_Pham_Controller.cs", "CDM_San_Pham", "tbl_DM_San_Pham", "F2018_2_San_Pham_Info.razor", "F2018_3_San_Pham_Edit.razor", ["Auto_ID", "Ma_San_Pham", "Ten_San_Pham", "Loai_San_Pham_ID", "Don_Vi_Tinh_ID", "Ghi_Chu", "Created", "Created_By", "Created_By_Function", "Last_Updated", "Last_Updated_By", "Last_Updated_By_Function"]),
            ("2019", "CDM_NCC_Controller.cs", "CDM_NCC", "tbl_DM_NCC", "F2019_2_Nha_Cung_Cap_Info.razor", "F2019_3_Nha_Cung_Cap_Edit.razor", ["Auto_ID", "Ma_NCC", "Ten_NCC", "Ghi_Chu", "Created", "Created_By", "Created_By_Function", "Last_Updated", "Last_Updated_By", "Last_Updated_By_Function"])
        ];
        string v_strProcedureScript = File.ReadAllText(FindRepositoryPath(
            "Database", "WarehouseMaster.GetById.Procedures.sql"));

        foreach (var v_objModule in v_arrModules)
        {
            string v_strProcedure = $"F{v_objModule.Code}_sp_sel_Get_By_ID";
            string v_strController = File.ReadAllText(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Data_Access", "Controller", "DM", v_objModule.Controller));
            string v_strInfo = File.ReadAllText(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", v_objModule.Info));
            string v_strEdit = File.ReadAllText(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", v_objModule.Edit));

            Assert.Contains($"public {v_objModule.Entity} {v_strProcedure}(long p_iID)", v_strController);
            Assert.Contains($"FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, \"{v_strProcedure}\", p_iID)", v_strController);
            Assert.Contains($"{v_objModule.Entity} v_objRes = null", v_strController);
            Assert.Contains($"CUtility.Map_Row_To_Entity<{v_objModule.Entity}>(v_dt.Rows[0])", v_strController);
            Assert.Contains("if (v_dt.Rows.Count > 0)", v_strController);
            Assert.Contains("v_dt.Dispose()", v_strController);
            Assert.Contains($"CREATE OR ALTER PROCEDURE dbo.{v_strProcedure}", v_strProcedureScript);
            Assert.Contains("@Auto_ID BIGINT", v_strProcedureScript);
            Assert.Contains($"FROM dbo.{v_objModule.Table}", v_strProcedureScript);
            Assert.Contains("WHERE Auto_ID = @Auto_ID", v_strProcedureScript);
            Assert_Procedure_Selects_Columns(v_strProcedureScript, v_strProcedure, v_objModule.Columns);
            Assert.Contains($".{v_strProcedure}(r_iAuto_ID)", v_strInfo);
            Assert.DoesNotContain("v_objItem.Auto_ID == r_iAuto_ID", v_strInfo);
            Assert.DoesNotContain(v_strProcedure, v_strEdit);
            Assert.Contains("FCommon_Sys_sp_sel_List_Log_Record_Action_History(r_iAuto_ID)", v_strInfo);
            Assert.Contains("<FCommon_Record_Action_History_List", v_strInfo);
        }

        string v_strProductInfo = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "F2018_2_San_Pham_Info.razor"));
        Assert.Contains("F2017_sp_sel_List_Loai_San_Pham_Lookup", v_strProductInfo);
        Assert.Contains("F2016_sp_sel_List_Don_Vi_Tinh_Lookup", v_strProductInfo);
    }

    [Fact]
    public void Company_row_mapper_preserves_present_audit_values_and_does_not_invent_null_values()
    {
        DataTable v_dt = new();
        v_dt.Columns.Add("Auto_ID", typeof(long));
        v_dt.Columns.Add("Ten_Kho", typeof(string));
        v_dt.Columns.Add("Ten_Don_Vi_Tinh", typeof(string));
        v_dt.Columns.Add("Ma_LSP", typeof(string));
        v_dt.Columns.Add("Ten_LSP", typeof(string));
        v_dt.Columns.Add("Ma_San_Pham", typeof(string));
        v_dt.Columns.Add("Ten_San_Pham", typeof(string));
        v_dt.Columns.Add("Loai_San_Pham_ID", typeof(long));
        v_dt.Columns.Add("Don_Vi_Tinh_ID", typeof(long));
        v_dt.Columns.Add("Ma_NCC", typeof(string));
        v_dt.Columns.Add("Ten_NCC", typeof(string));
        v_dt.Columns.Add("Ghi_Chu", typeof(string));
        v_dt.Columns.Add("Created", typeof(DateTime));
        v_dt.Columns.Add("Created_By", typeof(string));
        v_dt.Columns.Add("Created_By_Function", typeof(string));
        v_dt.Columns.Add("Last_Updated", typeof(DateTime));
        v_dt.Columns.Add("Last_Updated_By", typeof(string));
        v_dt.Columns.Add("Last_Updated_By_Function", typeof(string));

        DataRow v_row = v_dt.NewRow();
        v_row["Created"] = new DateTime(2026, 10, 14, 1, 39, 54);
        v_row["Created_By"] = "created-user";
        v_row["Created_By_Function"] = "2009";
        v_row["Last_Updated"] = new DateTime(2026, 10, 14, 1, 40, 0);
        v_row["Last_Updated_By"] = "updated-user";
        v_row["Last_Updated_By_Function"] = "2016";
        v_dt.Rows.Add(v_row);

        Assert_Mapped_Audit<CDM_Kho>(v_row);
        Assert_Mapped_Audit<CDM_Don_Vi_Tinh>(v_row);
        Assert_Mapped_Audit<CDM_Loai_San_Pham>(v_row);
        Assert_Mapped_Audit<CDM_San_Pham>(v_row);
        Assert_Mapped_Audit<CDM_NCC>(v_row);

        v_row["Created"] = DBNull.Value;
        v_row["Created_By"] = DBNull.Value;
        CDM_Kho v_objNullAudit = CUtility.Map_Row_To_Entity<CDM_Kho>(v_row);

        Assert.Null(v_objNullAudit.Created);
        Assert.Equal("", v_objNullAudit.Created_By);
        Assert.Equal("-", CUtility.Get_Label_Text(v_objNullAudit.Created, false));
        Assert.Equal("-", CUtility.Get_Label_Text(v_objNullAudit.Created_By, false));
    }

    private static void Assert_Procedure_Selects_Columns(string p_strScript, string p_strProcedure, string[] p_arrExpected_Columns)
    {
        string v_strMarker = $"CREATE OR ALTER PROCEDURE dbo.{p_strProcedure}";
        int v_iStart = p_strScript.IndexOf(v_strMarker, StringComparison.OrdinalIgnoreCase);
        Assert.True(v_iStart >= 0, $"Procedure definition not found: {p_strProcedure}");

        int v_iEnd = p_strScript.IndexOf("\nGO", v_iStart, StringComparison.OrdinalIgnoreCase);
        Assert.True(v_iEnd > v_iStart, $"Procedure terminator not found: {p_strProcedure}");
        string v_strDefinition = p_strScript[v_iStart..v_iEnd];

        int v_iSelect = v_strDefinition.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase) + "SELECT".Length;
        int v_iFrom = v_strDefinition.IndexOf("FROM", v_iSelect, StringComparison.OrdinalIgnoreCase);
        Assert.True(v_iFrom > v_iSelect, $"SELECT projection not found: {p_strProcedure}");

        HashSet<string> v_setSelected_Columns = v_strDefinition[v_iSelect..v_iFrom]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string v_strColumn in p_arrExpected_Columns)
            Assert.Contains(v_strColumn, v_setSelected_Columns);
    }

    private static void Assert_Mapped_Audit<T>(DataRow p_row) where T : new()
    {
        T v_objData = CUtility.Map_Row_To_Entity<T>(p_row);

        Assert.Equal(new DateTime(2026, 10, 14, 1, 39, 54), typeof(T).GetProperty("Created")?.GetValue(v_objData));
        Assert.Equal("created-user", typeof(T).GetProperty("Created_By")?.GetValue(v_objData));
        Assert.Equal("2009", typeof(T).GetProperty("Created_By_Function")?.GetValue(v_objData));
        Assert.Equal(new DateTime(2026, 10, 14, 1, 40, 0), typeof(T).GetProperty("Last_Updated")?.GetValue(v_objData));
        Assert.Equal("updated-user", typeof(T).GetProperty("Last_Updated_By")?.GetValue(v_objData));
        Assert.Equal("2016", typeof(T).GetProperty("Last_Updated_By_Function")?.GetValue(v_objData));
    }

    private static string FindRepositoryPath(params string[] p_arrParts)
    {
        for (DirectoryInfo? v_objDirectory = new DirectoryInfo(AppContext.BaseDirectory);
             v_objDirectory is not null;
             v_objDirectory = v_objDirectory.Parent)
        {
            string v_strCandidate = Path.Combine(new[] { v_objDirectory.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_strCandidate))
                return v_strCandidate;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(p_arrParts)}");
    }
}
