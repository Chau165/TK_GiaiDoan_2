using NBomber.CSharp;
using NBomber.Contracts;
using NBomber.Contracts.Stats;

namespace TKS_Thuc_Tap_V11_Benchmarks;

public static class WarehouseLoadTest
{
    public static int Run()
    {
        var v_settings = BenchmarkSettings.FromEnvironment();
        v_settings.RequireDatabase();

        var v_operations = new WarehouseReadOperations(v_settings);

        var v_allScenarios = new[]
        {
            CreateScenario("MasterPaged", v_operations.MasterPagedAsync, v_settings),
            CreateScenario("LookupPaged", v_operations.LookupPagedAsync, v_settings),
            CreateScenario("DocumentPaged", v_operations.DocumentPagedAsync, v_settings),
            CreateScenario("DetailReportPaged", v_operations.DetailReportPagedAsync, v_settings),
            CreateScenario("InventoryReportPaged", v_operations.InventoryReportPagedAsync, v_settings)
        };
        var v_selectedNames = v_settings.NBomberScenarioNames.ToHashSet(StringComparer.Ordinal);
        var v_scenarios = v_allScenarios
            .Where(p_scenario => v_selectedNames.Contains(p_scenario.ScenarioName))
            .ToArray();

        Directory.CreateDirectory(v_settings.ReportDirectory);
        _ = NBomberRunner
            .RegisterScenarios(v_scenarios)
            .WithReportFormats(ReportFormat.Html, ReportFormat.Csv, ReportFormat.Md, ReportFormat.Txt)
            .WithReportFolder(v_settings.ReportDirectory)
            .WithReportFileName("warehouse-nbomber")
            .WithTestSuite("TKS_Thuc_Tap_V11 warehouse read workload")
            .Run();

        return 0;
    }

    private static ScenarioProps CreateScenario(
        string p_name,
        Func<Task<int>> p_operation,
        BenchmarkSettings p_settings)
    {
        return Scenario.Create(p_name, async _ =>
        {
            try
            {
                var v_rows = await p_operation();
                GC.KeepAlive(v_rows);
                return Response.Ok();
            }
            catch (Exception p_exception)
            {
                return Response.Fail("-101", p_exception.Message, 0L, 0d);
            }
        }).WithLoadSimulations(Simulation.KeepConstant(
            copies: p_settings.NBomberCopies,
            during: TimeSpan.FromSeconds(p_settings.NBomberDurationSeconds)));
    }
}
