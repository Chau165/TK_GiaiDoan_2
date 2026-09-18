using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;

namespace TKS_Thuc_Tap_V11_Benchmarks_V2;

public static class V2BenchmarkDotNet
{
    public static int Run(V2Settings p_settings, string[] p_benchmarkArguments)
    {
        if (string.IsNullOrWhiteSpace(p_settings.Scenario))
            throw new InvalidOperationException("A single --scenario is required for BDN mode.");

        p_settings.ConfigureDataAccess();
        BenchmarkRunner.Run<V2DatabaseBenchmark>(
            new V2LowMemoryConfig(),
            p_benchmarkArguments);
        return 0;
    }
}

public sealed class V2LowMemoryConfig : ManualConfig
{
    public V2LowMemoryConfig()
    {
        AddJob(Job.Default
            .WithId("V2-LowMemory")
            .WithToolchain(InProcessNoEmitToolchain.Instance)
            .WithLaunchCount(V2Constants.BdnLaunchCount)
            .WithWarmupCount(V2Constants.BdnWarmupCount)
            .WithIterationCount(V2Constants.BdnIterationCount)
            .WithInvocationCount(V2Constants.BdnInvocationCount)
            .WithUnrollFactor(V2Constants.BdnUnrollFactor));
        AddExporter(CsvExporter.Default);
        AddColumnProvider(DefaultColumnProviders.Instance);
        AddLogger(ConsoleLogger.Default);
        WithOptions(ConfigOptions.DisableOptimizationsValidator);
    }
}

public class V2DatabaseBenchmark
{
    private V2Settings m_settings = null!;
    private V2ReadOperations m_operations = null!;

    [GlobalSetup]
    public void Setup()
    {
        m_settings = V2Settings.FromEnvironment();
        m_settings = m_settings.WithScenario(m_settings.Scenario);
        m_operations = new V2ReadOperations(m_settings);
        V2Validation.AssertTargetDatabaseAsync(m_settings).GetAwaiter().GetResult();
    }

    [Benchmark]
    public Task<int> Execute()
    {
        return m_operations.ExecuteAsync(m_settings.Scenario);
    }
}
