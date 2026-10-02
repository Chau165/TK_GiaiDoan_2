using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehousePhase10_2P101ContractTests
{
    [Fact]
    public void Current_report_uses_a_generation_state_and_validates_before_emitting_results()
    {
        var v_Procedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_Schema = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Schema.sql"));
        var v_iStart = v_Procedures.IndexOf("CREATE OR ALTER PROCEDURE dbo.sp_BC_Ton_Kho_Hien_Tai_Page", StringComparison.Ordinal);
        var v_iEnd = v_Procedures.IndexOf("\nGO", v_iStart, StringComparison.Ordinal);

        Assert.True(v_iStart >= 0 && v_iEnd > v_iStart, "Current report procedure was not found.");
        var v_Definition = v_Procedures.Substring(v_iStart, v_iEnd - v_iStart);
        Assert.Contains("CREATE TABLE dbo.Inventory_Current_Report_State", v_Schema, StringComparison.Ordinal);
        Assert.Contains("tr_Inventory_Current_Report_Generation", v_Procedures, StringComparison.Ordinal);
        Assert.Contains("Inventory_Current_Report_State", v_Definition, StringComparison.Ordinal);
        Assert.Contains("THROW 51324", v_Definition, StringComparison.Ordinal);
        Assert.Contains("#CurrentReportPage", v_Definition, StringComparison.Ordinal);
    }

    [Fact]
    public void Warehouse_security_denies_direct_projection_mutation()
    {
        var v_Security = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Security.sql"));

        Assert.Contains("DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.InventoryBalance_Current", v_Security, StringComparison.Ordinal);
        Assert.Contains("DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.InventoryReservation_Current", v_Security, StringComparison.Ordinal);
    }

    [Fact]
    public void Warehouse_security_deployment_has_a_read_only_preflight()
    {
        var v_PreflightPath = FindRepositoryPath("Database", "WarehouseModule.Security.Preflight.sql");
        var v_Readme = File.ReadAllText(FindRepositoryPath("Database", "README.md"));

        Assert.True(File.Exists(v_PreflightPath), "Security preflight script is missing.");
        Assert.Contains("APPLICATION_PRINCIPAL_NOT_VERIFIED", File.ReadAllText(v_PreflightPath), StringComparison.Ordinal);
        Assert.Contains("WarehouseModule.Security.sql", v_Readme, StringComparison.Ordinal);
        Assert.Contains("WarehouseModule.Security.Preflight.sql", v_Readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Security_preflight_rejects_broad_database_permissions_and_database_owner()
    {
        var v_Preflight = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Security.Preflight.sql"));

        Assert.Contains("@ApplicationUserId = 1", v_Preflight, StringComparison.Ordinal);
        Assert.Contains("permissionEntry.class IN (0, 1, 3)", v_Preflight, StringComparison.Ordinal);
        Assert.Contains("db_ddladmin", v_Preflight, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_report_retry_errors_are_presented_as_not_ready_in_the_ui()
    {
        var v_Readiness = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Utility", "CWarehouseReportReadiness.cs"));
        var v_Component = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("51323", v_Readiness, StringComparison.Ordinal);
        Assert.Contains("51324", v_Readiness, StringComparison.Ordinal);
        Assert.Contains("CWarehouseReportReadiness.IsRetryable", v_Component, StringComparison.Ordinal);
        Assert.Contains("Set_Report_Not_Ready", v_Component, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_readiness_only_maps_explicit_busy_and_generation_errors()
    {
        Assert.True(CWarehouseReportReadiness.IsRetryableSqlErrorNumber(51323));
        Assert.True(CWarehouseReportReadiness.IsRetryableSqlErrorNumber(51324));
        Assert.False(CWarehouseReportReadiness.IsRetryableSqlErrorNumber(51322));
        Assert.False(CWarehouseReportReadiness.IsRetryableSqlErrorNumber(51120));
        Assert.Contains("chưa sẵn sàng", CWarehouseReportReadiness.RetryMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryPath(params string[] p_arrParts)
    {
        var v_Path = AppContext.BaseDirectory;
        for (var v_iIndex = 0; v_iIndex < 5; v_iIndex++)
            v_Path = Directory.GetParent(v_Path)!.FullName;

        return Path.Combine(new[] { v_Path }.Concat(p_arrParts).ToArray());
    }
}
