using System.Reflection;
using System.Text.Json;
using TKS_Thuc_Tap_V11_Benchmarks_V22;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

[assembly: AssemblyMetadata("WarehouseBenchmarkProtocol", "2.2")]

if (args.Length == 1 && args[0] == "--protocol-info")
{
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        ProtocolVersion = "2.2",
        HarnessState = "BATCH3_LEGACY_PERFORMANCE_PROTOCOL",
        PerformanceExecutionEnabled = true,
        DataAccessAssembly = "TKS_Thuc_Tap_V11_Data_Access",
        NBomberVersion = "6.6.0",
        BenchmarkDotNetVersion = "0.15.8"
    }));
    return 0;
}

if (args.Length > 0 && args[0] == "failure-injection")
    return V22CorrectnessFailureInjection.Run(args.Skip(1).FirstOrDefault());

if (args.Length > 0 && args[0] == "correctness")
{
    var v_ConnectionString = Environment.GetEnvironmentVariable("TKS_V22_CONNECTION_STRING");
    if (!string.IsNullOrWhiteSpace(v_ConnectionString))
        CConfig.TKS_Thuc_Tap_V11_Conn_String = v_ConnectionString;
    CLogger.Enable_Trace = false;
    return await V22CorrectnessRunner.RunAsync(args.Skip(1).ToArray());
}

if (args.Length > 0 && args[0] == "performance-load")
{
    var v_ConnectionString = Environment.GetEnvironmentVariable("TKS_V22_CONNECTION_STRING");
    if (!string.IsNullOrWhiteSpace(v_ConnectionString))
        CConfig.TKS_Thuc_Tap_V11_Conn_String = v_ConnectionString;
    CLogger.Enable_Trace = false;
    return await V22PerformanceRunner.RunLoadAsync(args.Skip(1).ToArray());
}

if (args.Length > 0 && args[0] == "performance-bdn")
{
    var v_ConnectionString = Environment.GetEnvironmentVariable("TKS_V22_CONNECTION_STRING");
    if (!string.IsNullOrWhiteSpace(v_ConnectionString))
        CConfig.TKS_Thuc_Tap_V11_Conn_String = v_ConnectionString;
    CLogger.Enable_Trace = false;
    return V22PerformanceRunner.RunBdn(args.Skip(1).ToArray());
}

if (args.Length > 0 && args[0] == "performance-telemetry")
{
    var v_ConnectionString = Environment.GetEnvironmentVariable("TKS_V22_CONNECTION_STRING");
    if (!string.IsNullOrWhiteSpace(v_ConnectionString))
        CConfig.TKS_Thuc_Tap_V11_Conn_String = v_ConnectionString;
    return await V22PerformanceTelemetry.RunAsync(args.Skip(1).ToArray());
}

if (args.Length > 0 && args[0] == "performance-telemetry-probe")
{
    var v_ConnectionString = Environment.GetEnvironmentVariable("TKS_V22_CONNECTION_STRING");
    if (!string.IsNullOrWhiteSpace(v_ConnectionString))
        CConfig.TKS_Thuc_Tap_V11_Conn_String = v_ConnectionString;
    return await V22PerformanceTelemetry.RunAsync(args.Skip(1).Concat(new[] { "--probe", "true" }).ToArray());
}

if (args.Length > 0 && args[0] == "performance-residue")
{
    var v_ConnectionString = Environment.GetEnvironmentVariable("TKS_V22_CONNECTION_STRING");
    if (!string.IsNullOrWhiteSpace(v_ConnectionString))
        CConfig.TKS_Thuc_Tap_V11_Conn_String = v_ConnectionString;
    return await V22PerformanceTelemetry.RunResidueAsync(args.Skip(1).ToArray());
}

Console.Error.WriteLine("Supported modes: --protocol-info, failure-injection <output-root>, correctness --output <new-root> --phase3 <json> --closure <json> --parity <json> --security <json>, performance-load, performance-bdn, performance-telemetry, performance-telemetry-probe, performance-residue.");
return 64;
