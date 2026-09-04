```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.11GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
.NET SDK 9.0.317
  [Host]   : .NET 8.0.20 (8.0.20, 8.0.2025.41914), X64 RyuJIT x86-64-v3
  ShortRun : .NET 8.0.20 (8.0.20, 8.0.2025.41914), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method               | Mean     | Error    | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------- |---------:|---------:|----------:|------:|--------:|-------:|----------:|------------:|
| MasterPaged          | 1.828 ms | 1.303 ms | 0.0714 ms |  1.00 |    0.05 | 7.8125 |  36.76 KB |        1.00 |
| LookupPaged          | 3.496 ms | 5.567 ms | 0.3052 ms |  1.91 |    0.16 |      - |  25.92 KB |        0.71 |
| DocumentPaged        |       NA |       NA |        NA |     ? |       ? |     NA |        NA |           ? |
| DetailReportPaged    |       NA |       NA |        NA |     ? |       ? |     NA |        NA |           ? |
| InventoryReportPaged |       NA |       NA |        NA |     ? |       ? |     NA |        NA |           ? |

Benchmarks with issues:
  WarehouseDatabaseBenchmarks.DocumentPaged: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
  WarehouseDatabaseBenchmarks.DetailReportPaged: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
  WarehouseDatabaseBenchmarks.InventoryReportPaged: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
