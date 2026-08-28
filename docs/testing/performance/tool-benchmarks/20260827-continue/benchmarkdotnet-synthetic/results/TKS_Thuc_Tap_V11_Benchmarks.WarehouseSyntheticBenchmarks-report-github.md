```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.11GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
.NET SDK 9.0.317
  [Host]   : .NET 8.0.20 (8.0.20, 8.0.2025.41914), X64 RyuJIT x86-64-v3
  ShortRun : .NET 8.0.20 (8.0.20, 8.0.2025.41914), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                            | RecordCount | Mean    | Error    | StdDev   | Ratio | RatioSD | Gen0        | Gen1       | Gen2      | Allocated | Alloc Ratio |
|---------------------------------- |------------ |--------:|---------:|---------:|------:|--------:|------------:|-----------:|----------:|----------:|------------:|
| MapRowsUsingApplicationReflection | 1000000     | 2.413 s |  8.216 s | 0.4504 s |  1.02 |    0.23 | 105000.0000 |          - |         - | 418.91 MB |        1.00 |
| MaterializeSyntheticDataTable     | 1000000     | 3.065 s |  6.196 s | 0.3397 s |  1.30 |    0.24 |  60000.0000 | 30000.0000 | 1000.0000 |  518.1 MB |        1.24 |
| MaterializeAndMapSyntheticRows    | 1000000     | 4.409 s | 12.093 s | 0.6629 s |  1.87 |    0.39 | 165000.0000 | 32000.0000 | 1000.0000 | 937.02 MB |        2.24 |
