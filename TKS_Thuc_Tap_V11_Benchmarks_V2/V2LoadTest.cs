using Microsoft.Data.SqlClient;
using NBomber.CSharp;
using NBomber.Contracts;
using NBomber.Contracts.Stats;

namespace TKS_Thuc_Tap_V11_Benchmarks_V2;

public static class V2LoadTest
{
    public static int Run(V2Settings p_settings)
    {
        if (string.IsNullOrWhiteSpace(p_settings.Scenario))
            throw new InvalidOperationException("A single --scenario is required for NBomber mode.");
        if (string.IsNullOrWhiteSpace(p_settings.OutputDirectory))
            throw new InvalidOperationException("An output directory is required for NBomber mode.");

        p_settings.ConfigureDataAccess();
        var v_operations = new V2ReadOperations(p_settings);
        var v_scenario = Scenario.Create(
            p_settings.Scenario,
            async _ =>
            {
                try
                {
                    var v_rows = await v_operations.ExecuteAsync(p_settings.Scenario);
                    GC.KeepAlive(v_rows);
                    return Response.Ok();
                }
                catch (Exception p_exception)
                {
                    var v_classification = ClassifyException(p_exception);
                    return Response.Fail(
                        v_classification,
                        p_exception.Message,
                        0L,
                        0d);
                }
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(p_settings.NbomberWarmupSeconds))
            .WithLoadSimulations(Simulation.KeepConstant(
                copies: ParseCopies(),
                during: TimeSpan.FromSeconds(p_settings.NbomberDurationSeconds)));

        Directory.CreateDirectory(p_settings.OutputDirectory);
        _ = NBomberRunner
            .RegisterScenarios(v_scenario)
            .WithReportFormats(ReportFormat.Csv, ReportFormat.Md, ReportFormat.Txt)
            .WithReportFolder(p_settings.OutputDirectory)
            .WithReportFileName("v2-nbomber")
            .WithTestSuite("WAREHOUSE_BENCHMARK_V2_1")
            .EnableStopTestForcibly(true)
            .Run();

        return 0;
    }

    public static string ClassifyException(Exception p_exception)
    {
        if (p_exception is SqlException v_sqlException)
        {
            if (v_sqlException.Number == -2)
                return "SQL_TIMEOUT";
            if (v_sqlException.Number == 1205)
                return "DEADLOCK";
            if (v_sqlException.Number is 8645 or 8651 or 8657)
                return "RESOURCE_SEMAPHORE";
        }

        if (p_exception.Message.Contains("RESOURCE_SEMAPHORE", StringComparison.OrdinalIgnoreCase))
            return "RESOURCE_SEMAPHORE";
        if (p_exception.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase))
            return "DEADLOCK";
        if (p_exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "SQL_TIMEOUT";
        return "PRODUCT_ERROR";
    }

    private static int ParseCopies()
    {
        var v_raw = Environment.GetEnvironmentVariable("TKS_V2_NBOMBER_COPIES");
        return int.TryParse(v_raw, out var v_copies) ? Math.Clamp(v_copies, 1, 256) : 1;
    }
}
