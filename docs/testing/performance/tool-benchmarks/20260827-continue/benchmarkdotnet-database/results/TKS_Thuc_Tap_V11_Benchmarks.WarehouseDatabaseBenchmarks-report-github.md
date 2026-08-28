```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.11GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
.NET SDK 9.0.317
  [Host]   : .NET 8.0.20 (8.0.20, 8.0.2025.41914), X64 RyuJIT x86-64-v3
  ShortRun : .NET 8.0.20 (8.0.20, 8.0.2025.41914), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method               | Mean       | Error       | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------- |-----------:|------------:|----------:|------:|--------:|-------:|----------:|------------:|
| MasterPaged          |   1.875 ms |   0.5207 ms | 0.0285 ms |  1.00 |    0.02 | 7.8125 |  36.75 KB |        1.00 |
| LookupPaged          |   2.704 ms |   0.3175 ms | 0.0174 ms |  1.44 |    0.02 | 5.8594 |   25.7 KB |        0.70 |
| DocumentPaged        |  32.795 ms |  96.5353 ms | 5.2914 ms | 17.50 |    2.46 |      - |  47.52 KB |        1.29 |
| DetailReportPaged    | 103.336 ms | 109.8229 ms | 6.0198 ms | 55.13 |    2.88 |      - |  52.53 KB |        1.43 |
| InventoryReportPaged |         NA |          NA |        NA |     ? |       ? |     NA |        NA |           ? |

Benchmarks with issues:
  WarehouseDatabaseBenchmarks.InventoryReportPaged: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
