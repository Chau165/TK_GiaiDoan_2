using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using System.Text.RegularExpressions;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseMasterCreatedFilterContractTests
{
    [Theory]
    [InlineData("2009")]
    [InlineData("2016")]
    [InlineData("2017")]
    [InlineData("2018")]
    [InlineData("2019")]
    public void By_created_controller_rejects_reversed_date_ranges_before_database_access(string p_strCode)
    {
        DateTime v_dtmFrom = new(2026, 10, 22);
        DateTime v_dtmTo = new(2026, 10, 21);

        Action v_objCall = p_strCode switch
        {
            "2009" => () => new CDM_Kho_Controller().F2009_sp_sel_List_Kho_By_Created(v_dtmFrom, v_dtmTo),
            "2016" => () => new CDM_Don_Vi_Tinh_Controller().F2016_sp_sel_List_Don_Vi_Tinh_By_Created(v_dtmFrom, v_dtmTo),
            "2017" => () => new CDM_Loai_San_Pham_Controller().F2017_sp_sel_List_Loai_San_Pham_By_Created(v_dtmFrom, v_dtmTo),
            "2018" => () => new CDM_San_Pham_Controller().F2018_sp_sel_List_San_Pham_By_Created(v_dtmFrom, v_dtmTo),
            "2019" => () => new CDM_NCC_Controller().F2019_sp_sel_List_NCC_By_Created(v_dtmFrom, v_dtmTo),
            _ => throw new ArgumentOutOfRangeException(nameof(p_strCode))
        };

        ArgumentException v_objException = Assert.Throws<ArgumentException>(v_objCall);

        Assert.Contains("Ngày bắt đầu", v_objException.Message);
    }

    [Fact]
    public void Local_calendar_date_bounds_convert_to_utc_start_and_exclusive_next_day()
    {
        DateTime v_dtmSelected = new(2026, 10, 21, 16, 45, 0, DateTimeKind.Unspecified);
        DateTime v_dtmExpectedFrom = DateTime.SpecifyKind(v_dtmSelected.Date, DateTimeKind.Local).ToUniversalTime();
        DateTime v_dtmExpectedToExclusive = DateTime.SpecifyKind(v_dtmSelected.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();

        DateTime? v_dtmActualFrom = CWMS_Utility.Convert_Local_Day_Start_To_Utc(v_dtmSelected);
        DateTime? v_dtmActualToExclusive = CWMS_Utility.Convert_Local_Next_Day_Start_To_Utc(v_dtmSelected);

        Assert.Equal((DateTime?)v_dtmExpectedFrom, v_dtmActualFrom);
        Assert.Equal((DateTime?)v_dtmExpectedToExclusive, v_dtmActualToExclusive);
        Assert.Equal(DateTimeKind.Utc, v_dtmActualFrom!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, v_dtmActualToExclusive!.Value.Kind);
        Assert.True(v_dtmActualFrom.Value < v_dtmActualToExclusive.Value);

        DateTime v_dtmLastTickLocal = DateTime.SpecifyKind(
            v_dtmSelected.Date.AddDays(1).AddTicks(-1),
            DateTimeKind.Local);
        Assert.Equal(v_dtmExpectedToExclusive.AddTicks(-1), v_dtmLastTickLocal.ToUniversalTime());
    }

    [Fact]
    public void Null_filter_dates_remain_unbounded()
    {
        Assert.Null(CWMS_Utility.Convert_Local_Day_Start_To_Utc(null));
        Assert.Null(CWMS_Utility.Convert_Local_Next_Day_Start_To_Utc(null));
    }

    [Fact]
    public void All_master_lists_use_the_shared_filter_toolbar_and_created_date_procedures()
    {
        (string Component, string Template, string ListProcedure, string ManualAddHandler, string ManualImportHandler)[] v_arrModules =
        [
            ("F2009_1_Kho_List.razor", "Kho_Import_Template.xlsx", "F2009_sp_sel_List_Kho_By_Created", "Open_Kho_Edit(0)", "Open_Kho_Import_Excel"),
            ("FWarehouse_Unit_List.razor", "Don_Vi_Tinh_Import_Template.xlsx", "F2016_sp_sel_List_Don_Vi_Tinh_By_Created", "Open_Unit_Edit(0)", "Open_Unit_Import_Excel"),
            ("F2017_1_Loai_San_Pham_List.razor", "Loai_San_Pham_Import_Template.xlsx", "F2017_sp_sel_List_Loai_San_Pham_By_Created", "Open_Category_Edit(0)", "Open_Category_Import_Excel"),
            ("F2018_1_San_Pham_List.razor", "San_Pham_Import_Template.xlsx", "F2018_sp_sel_List_San_Pham_By_Created", "Open_Product_Edit(0)", "Open_Product_Import_Excel"),
            ("F2019_1_Nha_Cung_Cap_List.razor", "Nha_Cung_Cap_Import_Template.xlsx", "F2019_sp_sel_List_NCC_By_Created", "Open_Supplier_Edit(0)", "Open_Supplier_Import_Excel")
        ];

        foreach ((string v_strComponent, string v_strTemplate, string v_strListProcedure, string v_strManualAddHandler, string v_strManualImportHandler) in v_arrModules)
        {
            string v_strList = File.ReadAllText(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", v_strComponent));

            Assert.Equal(1, Regex.Matches(v_strList, "@Layout_Default_Filter\\(grdData").Count);
            Assert.Contains($"@Layout_Default_Filter(grdData, \"{v_strTemplate}\")", v_strList);
            Assert.DoesNotContain("Layout_Tool_Button(grdData)", v_strList);
            Assert.DoesNotContain(v_strManualAddHandler, v_strList);
            Assert.DoesNotContain(v_strManualImportHandler, v_strList);
            Assert.Contains(v_strListProcedure, v_strList);
            Assert.Contains("r_objFilter.Date_From, r_objFilter.Date_To", v_strList);
        }
    }

    [Fact]
    public void By_created_script_keeps_existing_lists_unchanged_and_filters_created_with_exclusive_upper_bound()
    {
        string v_strScript = File.ReadAllText(FindRepositoryPath("Database", "WarehouseMaster.ByCreated.Procedures.sql"));
        (string ControllerFile, string Entity, string Procedure, string Table, string OrderBy)[] v_arrContracts =
        [
            ("CDM_Kho_Controller.cs", "CDM_Kho", "F2009_sp_sel_List_Kho_By_Created", "tbl_DM_Kho", "Ten_Kho"),
            ("CDM_Don_Vi_Tinh_Controller.cs", "CDM_Don_Vi_Tinh", "F2016_sp_sel_List_Don_Vi_Tinh_By_Created", "tbl_DM_Don_Vi_Tinh", "Ten_Don_Vi_Tinh"),
            ("CDM_Loai_San_Pham_Controller.cs", "CDM_Loai_San_Pham", "F2017_sp_sel_List_Loai_San_Pham_By_Created", "tbl_DM_Loai_San_Pham", "Ma_LSP"),
            ("CDM_San_Pham_Controller.cs", "CDM_San_Pham", "F2018_sp_sel_List_San_Pham_By_Created", "tbl_DM_San_Pham", "Ma_San_Pham"),
            ("CDM_NCC_Controller.cs", "CDM_NCC", "F2019_sp_sel_List_NCC_By_Created", "tbl_DM_NCC", "Ma_NCC")
        ];

        foreach ((string v_strControllerFile, string v_strEntity, string v_strProcedure, string v_strTable, string v_strOrderBy) in v_arrContracts)
        {
            string v_strController = File.ReadAllText(FindRepositoryPath(
                "TKS_Thuc_Tap_V11_Data_Access", "Controller", "DM", v_strControllerFile));
            string v_strProcedureMarker = $"CREATE OR ALTER PROCEDURE dbo.{v_strProcedure}";
            int v_iStart = v_strScript.IndexOf(v_strProcedureMarker, StringComparison.OrdinalIgnoreCase);
            Assert.True(v_iStart >= 0, $"Missing procedure {v_strProcedure}.");

            int v_iNextProcedure = v_strScript.IndexOf("CREATE OR ALTER PROCEDURE", v_iStart + v_strProcedureMarker.Length, StringComparison.OrdinalIgnoreCase);
            string v_strProcedureBody = v_iNextProcedure < 0
                ? v_strScript[v_iStart..]
                : v_strScript[v_iStart..v_iNextProcedure];

            Assert.Contains("@Date_From_Utc DATETIME2(7) = NULL", v_strProcedureBody, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("@Date_To_Utc_Exclusive DATETIME2(7) = NULL", v_strProcedureBody, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"FROM dbo.{v_strTable}", v_strProcedureBody, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Created >= @Date_From_Utc", v_strProcedureBody, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Created < @Date_To_Utc_Exclusive", v_strProcedureBody, StringComparison.OrdinalIgnoreCase);
            int v_iSelectStart = v_strProcedureBody.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase);
            int v_iFromStart = v_strProcedureBody.IndexOf("FROM", v_iSelectStart, StringComparison.OrdinalIgnoreCase);
            Assert.True(v_iSelectStart >= 0 && v_iFromStart > v_iSelectStart);
            Assert.Contains("Created", v_strProcedureBody[v_iSelectStart..v_iFromStart], StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"ORDER BY {v_strOrderBy}", v_strProcedureBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Last_Updated >=", v_strProcedureBody, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"FillDataTable(CConfig.TKS_Thuc_Tap_V11_Conn_String, v_dt, \"{v_strProcedure}\", v_dtmDate_From_Utc, v_dtmDate_To_Utc_Exclusive)", v_strController);
            Assert.Contains($"CUtility.Map_Row_To_Entity<{v_strEntity}>(v_row)", v_strController);
        }

        Assert.Contains("IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'", v_strScript, StringComparison.OrdinalIgnoreCase);
        Assert.False(Regex.IsMatch(
            v_strScript,
            @"CREATE OR ALTER PROCEDURE dbo\.F2009_sp_sel_List_Kho(?:\s|$)",
            RegexOptions.IgnoreCase));
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
