```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.11GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
.NET SDK 9.0.317
  [Host]   : .NET 8.0.20 (8.0.20, 8.0.2025.41914), X64 RyuJIT x86-64-v3
  ShortRun : .NET 8.0.20 (8.0.20, 8.0.2025.41914), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                            | RecordCount | Mean    | Error    | StdDev   | Median  | Ratio | RatioSD | Gen0        | Gen1       | Gen2      | Allocated | Alloc Ratio |
|---------------------------------- |------------ |--------:|---------:|---------:|--------:|------:|--------:|------------:|-----------:|----------:|----------:|------------:|
| MapRowsUsingApplicationReflection | 1000000     | 1.147 s |  3.702 s | 0.2029 s | 1.029 s |  1.02 |    0.21 | 105000.0000 |          - |         - | 418.91 MB |        1.00 |
| MaterializeSyntheticDataTable     | 1000000     | 3.258 s |  1.173 s | 0.0643 s | 3.284 s |  2.90 |    0.41 |  66000.0000 | 36000.0000 | 7000.0000 |  518.1 MB |        1.24 |
| MaterializeAndMapSyntheticRows    | 1000000     | 6.352 s | 41.769 s | 2.2895 s | 5.069 s |  5.65 |    1.94 | 170000.0000 | 38000.0000 | 6000.0000 | 937.02 MB |        2.24 |
