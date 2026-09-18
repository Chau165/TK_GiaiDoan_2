using BenchmarkDotNet.Running;

namespace TKS_Thuc_Tap_V11_Benchmarks;

public static class Program
{
    public static int Main(string[] p_args)
    {
        if (p_args.Any(v_arg => string.Equals(v_arg, "--nbomber", StringComparison.OrdinalIgnoreCase)))
            return WarehouseLoadTest.Run();

        var v_settings = BenchmarkSettings.FromEnvironment();
        if (v_settings.RunDatabaseBenchmarks)
        {
            BenchmarkSwitcher.FromTypes(new[]
            {
                typeof(WarehouseDatabaseBenchmarks),
                typeof(WarehouseCurrentBalanceBenchmarks)
            }).Run(p_args);
        }
        else
        {
            BenchmarkSwitcher.FromTypes(new[] { typeof(WarehouseSyntheticBenchmarks) }).Run(p_args);
        }

        return 0;
    }
}
