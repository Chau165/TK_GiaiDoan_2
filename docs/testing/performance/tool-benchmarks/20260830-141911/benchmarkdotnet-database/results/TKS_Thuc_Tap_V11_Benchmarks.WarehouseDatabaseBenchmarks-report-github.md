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
| MasterPaged          | 2.037 ms | 3.253 ms | 0.1783 ms |  1.01 |    0.11 | 7.8125 |  36.76 KB |        1.00 |
| LookupPaged          | 2.015 ms | 3.412 ms | 0.1870 ms |  0.99 |    0.11 | 3.9063 |  25.73 KB |        0.70 |
| DocumentPaged        |       NA |       NA |        NA |     ? |       ? |     NA |        NA |           ? |
| DetailReportPaged    |       NA |       NA |        NA |     ? |       ? |     NA |        NA |           ? |
| InventoryReportPaged |       NA |       NA |        NA |     ? |       ? |     NA |        NA |           ? |

Benchmarks with issues:
  WarehouseDatabaseBenchmarks.DocumentPaged: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
  WarehouseDatabaseBenchmarks.DetailReportPaged: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
  WarehouseDatabaseBenchmarks.InventoryReportPaged: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
