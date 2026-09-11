using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehousePhase10_2P101ContractTests
{
    [Fact]
    public void Current_report_uses_a_generation_state_and_validates_before_emitting_results()
    {
        var procedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var schema = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Schema.sql"));
        var start = procedures.IndexOf("CREATE OR ALTER PROCEDURE dbo.sp_BC_Ton_Kho_Hien_Tai_Page", StringComparison.Ordinal);
        var end = procedures.IndexOf("\nGO", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "Current report procedure was not found.");
        var definition = procedures.Substring(start, end - start);
        Assert.Contains("CREATE TABLE dbo.Inventory_Current_Report_State", schema, StringComparison.Ordinal);
        Assert.Contains("tr_Inventory_Current_Report_Generation", procedures, StringComparison.Ordinal);
        Assert.Contains("Inventory_Current_Report_State", definition, StringComparison.Ordinal);
        Assert.Contains("THROW 51324", definition, StringComparison.Ordinal);
        Assert.Contains("#CurrentReportPage", definition, StringComparison.Ordinal);
    }

    [Fact]
    public void Warehouse_security_denies_direct_projection_mutation()
    {
        var security = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Security.sql"));

        Assert.Contains("DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.InventoryBalance_Current", security, StringComparison.Ordinal);
        Assert.Contains("DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.InventoryReservation_Current", security, StringComparison.Ordinal);
    }

    [Fact]
    public void Warehouse_security_deployment_has_a_read_only_preflight()
    {
        var preflightPath = FindRepositoryPath("Database", "WarehouseModule.Security.Preflight.sql");
        var readme = File.ReadAllText(FindRepositoryPath("Database", "README.md"));

        Assert.True(File.Exists(preflightPath), "Security preflight script is missing.");
        Assert.Contains("APPLICATION_PRINCIPAL_NOT_VERIFIED", File.ReadAllText(preflightPath), StringComparison.Ordinal);
        Assert.Contains("WarehouseModule.Security.sql", readme, StringComparison.Ordinal);
        Assert.Contains("WarehouseModule.Security.Preflight.sql", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Security_preflight_rejects_broad_database_permissions_and_database_owner()
    {
        var preflight = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Security.Preflight.sql"));

        Assert.Contains("@ApplicationUserId = 1", preflight, StringComparison.Ordinal);
        Assert.Contains("permissionEntry.class IN (0, 1, 3)", preflight, StringComparison.Ordinal);
        Assert.Contains("db_ddladmin", preflight, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_report_retry_errors_are_presented_as_not_ready_in_the_ui()
    {
        var readiness = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Data_Access", "Utility", "CWarehouseReportReadiness.cs"));
        var component = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("51323", readiness, StringComparison.Ordinal);
        Assert.Contains("51324", readiness, StringComparison.Ordinal);
        Assert.Contains("CWarehouseReportReadiness.IsRetryable", component, StringComparison.Ordinal);
        Assert.Contains("Set_Report_Not_Ready", component, StringComparison.Ordinal);
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

    private static string FindRepositoryPath(params string[] parts)
    {
        var path = AppContext.BaseDirectory;
        for (var index = 0; index < 5; index++)
            path = Directory.GetParent(path)!.FullName;

        return Path.Combine(new[] { path }.Concat(parts).ToArray());
    }
}
