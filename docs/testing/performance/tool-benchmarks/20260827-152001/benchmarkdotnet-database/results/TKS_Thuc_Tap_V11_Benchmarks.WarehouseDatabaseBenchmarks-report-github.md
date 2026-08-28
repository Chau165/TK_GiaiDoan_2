```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.11GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
.NET SDK 9.0.317
  [Host]   : .NET 8.0.20 (8.0.20, 8.0.2025.41914), X64 RyuJIT x86-64-v3
  ShortRun : .NET 8.0.20 (8.0.20, 8.0.2025.41914), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method               | Mean       | Error      | StdDev     | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------- |-----------:|-----------:|-----------:|-------:|--------:|-------:|----------:|------------:|
| MasterPaged          |   1.725 ms |   3.629 ms |  0.1989 ms |   1.01 |    0.14 | 7.8125 |  36.75 KB |        1.00 |
| LookupPaged          |   1.738 ms |   1.107 ms |  0.0607 ms |   1.02 |    0.10 | 5.8594 |   25.7 KB |        0.70 |
| DocumentPaged        |  25.222 ms |  40.733 ms |  2.2327 ms |  14.74 |    1.82 |      - |  46.14 KB |        1.26 |
| DetailReportPaged    | 104.115 ms | 160.513 ms |  8.7983 ms |  60.86 |    7.38 |      - |  51.51 KB |        1.40 |
| InventoryReportPaged | 556.607 ms | 415.925 ms | 22.7982 ms | 325.38 |   33.46 |      - | 117.84 KB |        3.21 |
