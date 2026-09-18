namespace TKS_Thuc_Tap_V11_Benchmarks_V2;

public static class Program
{
    public static int Main(string[] p_args)
    {
        try
        {
            if (p_args.Length == 0 || HasSwitch(p_args, "--help") || HasSwitch(p_args, "-h"))
            {
                PrintHelp();
                return 0;
            }

            var v_mode = p_args[0].TrimStart('-').ToLowerInvariant();
            var v_settings = V2Settings.FromEnvironment();
            var v_scenario = ReadOption(p_args, "--scenario");
            if (!string.IsNullOrWhiteSpace(v_scenario))
            {
                v_settings = v_settings.WithScenario(v_scenario);
                Environment.SetEnvironmentVariable("TKS_V2_SCENARIO", v_settings.Scenario);
            }

            var v_output = ReadOption(p_args, "--output");
            if (!string.IsNullOrWhiteSpace(v_output))
                v_settings = v_settings with { OutputDirectory = v_output };

            return v_mode switch
            {
                "validate" => V2Validation.RunAsync(
                    v_settings,
                    v_output ?? v_settings.OutputDirectory,
                    p_includeReadSmoke: true).GetAwaiter().GetResult(),
                "smoke" => V2Validation.RunSmokeAsync(
                    v_settings,
                    v_settings.Scenario,
                    v_output ?? v_settings.OutputDirectory).GetAwaiter().GetResult(),
                "residue" => V2Validation.RunResidueAsync(
                    v_settings,
                    v_output ?? v_settings.OutputDirectory).GetAwaiter().GetResult(),
                "self-test" or "selftest" => V2Validation.RunSelfTests(
                    v_output ?? v_settings.OutputDirectory),
                "bdn" => V2BenchmarkDotNet.Run(
                    v_settings,
                    RemoveOptions(p_args, "--bdn", "--scenario", "--output")),
                "nbomber" => V2LoadTest.Run(
                    v_settings with
                    {
                        OutputDirectory = v_output ?? v_settings.OutputDirectory
                    }),
                "telemetry" => V2Telemetry.RunAsync(
                    v_settings,
                    ParseRequiredInt(p_args, "--target-pid"),
                    v_output ?? throw new ArgumentException("--output is required for telemetry mode."),
                    ReadOption(p_args, "--block") ?? "unlabeled").GetAwaiter().GetResult(),
                _ => throw new ArgumentException($"Unknown V2 mode: {p_args[0]}")
            };
        }
        catch (Exception p_exception)
        {
            Console.Error.WriteLine($"V2_FATAL|{p_exception.GetType().Name}|{p_exception.Message}");
            return 1;
        }
    }

    private static bool HasSwitch(IEnumerable<string> p_args, string p_switch)
    {
        return p_args.Any(p_arg => string.Equals(p_arg, p_switch, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadOption(IReadOnlyList<string> p_args, string p_name)
    {
        for (var v_index = 0; v_index < p_args.Count - 1; v_index++)
        {
            if (string.Equals(p_args[v_index], p_name, StringComparison.OrdinalIgnoreCase))
                return p_args[v_index + 1];
        }

        return null;
    }

    private static int ParseRequiredInt(IReadOnlyList<string> p_args, string p_name)
    {
        var v_value = ReadOption(p_args, p_name);
        if (int.TryParse(v_value, out var v_result) && v_result > 0)
            return v_result;
        throw new ArgumentException($"{p_name} must be a positive integer.");
    }

    private static string[] RemoveOptions(
        IReadOnlyList<string> p_args,
        params string[] p_names)
    {
        var v_result = new List<string>();
        for (var v_index = 1; v_index < p_args.Count; v_index++)
        {
            if (p_names.Contains(p_args[v_index], StringComparer.OrdinalIgnoreCase))
            {
                v_index++;
                continue;
            }

            v_result.Add(p_args[v_index]);
        }

        return v_result.ToArray();
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            WAREHOUSE_BENCHMARK_V2_1

            Modes:
              --validate --output <file>                  DB/data/object/path preflight
              --smoke --scenario <name> --output <file>   one bounded real read
              --residue --output <file>                   read-only cleanup guard
              --self-test --output <file>                 injected safety/self-tests
              --bdn --scenario <name> --artifacts <dir>   one BDN scenario
              --nbomber --scenario <name> --output <dir> one NBomber block
              --telemetry --target-pid <pid> --block <id> --output <file>

            Required database environment:
              TKS_V2_CONNECTION_STRING
              TKS_V2_LOGIN (default PERF_USER)
              TKS_V2_FROM_DATE (default 2025-01-01)
              TKS_V2_TO_DATE (default 2026-12-31)
            """);
    }
}
