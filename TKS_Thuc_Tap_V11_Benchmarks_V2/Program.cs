namespace TKS_Thuc_Tap_V11_Benchmarks_V2;

public static class Program
{
    public static int Main(string[] p_arrArgs)
    {
        try
        {
            if (p_arrArgs.Length == 0 || HasSwitch(p_arrArgs, "--help") || HasSwitch(p_arrArgs, "-h"))
            {
                PrintHelp();
                return 0;
            }

            var v_mode = p_arrArgs[0].TrimStart('-').ToLowerInvariant();
            var v_settings = V2Settings.FromEnvironment();
            var v_scenario = ReadOption(p_arrArgs, "--scenario");
            if (!string.IsNullOrWhiteSpace(v_scenario))
            {
                v_settings = v_settings.WithScenario(v_scenario);
                Environment.SetEnvironmentVariable("TKS_V2_SCENARIO", v_settings.Scenario);
            }

            var v_output = ReadOption(p_arrArgs, "--output");
            if (!string.IsNullOrWhiteSpace(v_output))
                v_settings = v_settings with { OutputDirectory = v_output };

            int v_iExitCode;
            switch (v_mode)
            {
                case "validate":
                    v_iExitCode = V2Validation.RunAsync(
                        v_settings,
                        v_output ?? v_settings.OutputDirectory,
                        p_bIncludeReadSmoke: true).GetAwaiter().GetResult();
                    break;
                case "smoke":
                    v_iExitCode = V2Validation.RunSmokeAsync(
                        v_settings,
                        v_settings.Scenario,
                        v_output ?? v_settings.OutputDirectory).GetAwaiter().GetResult();
                    break;
                case "residue":
                    v_iExitCode = V2Validation.RunResidueAsync(
                        v_settings,
                        v_output ?? v_settings.OutputDirectory).GetAwaiter().GetResult();
                    break;
                case "self-test":
                case "selftest":
                    v_iExitCode = V2Validation.RunSelfTests(
                        v_output ?? v_settings.OutputDirectory);
                    break;
                case "bdn":
                    v_iExitCode = V2BenchmarkDotNet.Run(
                        v_settings,
                        RemoveOptions(p_arrArgs, "--bdn", "--scenario", "--output"));
                    break;
                case "nbomber":
                    v_iExitCode = V2LoadTest.Run(
                        v_settings with
                        {
                            OutputDirectory = v_output ?? v_settings.OutputDirectory
                        });
                    break;
                case "telemetry":
                    if (v_output == null)
                    {
                        throw new ArgumentException("--output is required for telemetry mode.");
                    }

                    v_iExitCode = V2Telemetry.RunAsync(
                        v_settings,
                        ParseRequiredInt(p_arrArgs, "--target-pid"),
                        v_output,
                        ReadOption(p_arrArgs, "--block") ?? "unlabeled").GetAwaiter().GetResult();
                    break;
                default:
                    throw new ArgumentException($"Unknown V2 mode: {p_arrArgs[0]}");
            }

            return v_iExitCode;
        }
        catch (Exception v_Exception)
        {
            Console.Error.WriteLine($"V2_FATAL|{v_Exception.GetType().Name}|{v_Exception.Message}");
            return 1;
        }
    }

    private static bool HasSwitch(IEnumerable<string> p_args, string p_switch)
    {
        return p_args.Any(p_arg => string.Equals(p_arg, p_switch, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadOption(IReadOnlyList<string> p_arrArgs, string p_name)
    {
        for (var v_index = 0; v_index < p_arrArgs.Count - 1; v_index++)
        {
            if (string.Equals(p_arrArgs[v_index], p_name, StringComparison.OrdinalIgnoreCase))
                return p_arrArgs[v_index + 1];
        }

        return null;
    }

    private static int ParseRequiredInt(IReadOnlyList<string> p_arrArgs, string p_name)
    {
        var v_value = ReadOption(p_arrArgs, p_name);
        if (int.TryParse(v_value, out var v_result) && v_result > 0)
            return v_result;
        throw new ArgumentException($"{p_name} must be a positive integer.");
    }

    private static string[] RemoveOptions(
        IReadOnlyList<string> p_arrArgs,
        params string[] p_arrNames)
    {
        var v_result = new List<string>();
        for (var v_index = 1; v_index < p_arrArgs.Count; v_index++)
        {
            if (p_arrNames.Contains(p_arrArgs[v_index], StringComparer.OrdinalIgnoreCase))
            {
                v_index++;
                continue;
            }

            v_result.Add(p_arrArgs[v_index]);
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
