```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.11GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
.NET SDK 9.0.317
  [Host]   : .NET 8.0.20 (8.0.20, 8.0.2025.41914), X64 RyuJIT x86-64-v3
  ShortRun : .NET 8.0.20 (8.0.20, 8.0.2025.41914), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method               | Mean         | Error        | StdDev      | Ratio    | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------- |-------------:|-------------:|------------:|---------:|--------:|-------:|----------:|------------:|
| MasterPaged          |     3.187 ms |     3.633 ms |   0.1991 ms |     1.00 |    0.08 | 7.8125 |  36.85 KB |        1.00 |
| LookupPaged          |     2.430 ms |     1.971 ms |   0.1081 ms |     0.76 |    0.05 | 3.9063 |  25.83 KB |        0.70 |
| DocumentPaged        |    57.748 ms |    42.786 ms |   2.3453 ms |    18.17 |    1.20 |      - |  50.03 KB |        1.36 |
| DetailReportPaged    |   224.289 ms |   265.202 ms |  14.5366 ms |    70.56 |    5.59 |      - |  57.42 KB |        1.56 |
| InventoryReportPaged | 4,292.829 ms | 6,692.665 ms | 366.8476 ms | 1,350.47 |  125.24 |      - | 117.96 KB |        3.20 |
