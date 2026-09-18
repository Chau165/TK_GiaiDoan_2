# Warehouse Benchmark V2.1 Mixed Extension

This directory is an additional `WAREHOUSE_BENCHMARK_V2_1_MIXED` profile. It supplements the accepted V2.1 isolated core baseline and does not replace, rewrite, or merge into that baseline.

The mixed runner launches six concurrent NBomber scenario processes. Each process uses the frozen V2.1 runtime and one real Data Access path; `TKS_V2_NBOMBER_COPIES=L` is the number of real NBomber workers for that scenario. Therefore the measured worker totals are 6, 12, 24, and 48 for MIXED-L1/L2/L4/L8.

Run with the PowerShell 7 host:

```powershell
pwsh.exe -NoProfile -File .\benchmarks\v2.1-mixed\run-warehouse-benchmark-v2.1-mixed.ps1 -Run
```

The runner targets only `localhost\MSSQLSERVER19`, database `TKS_Thuc_Tap_V11_Perf_10000000`, DB ID 5. It preserves page 1/page size 10, `PERF_USER`, the 2025-01-01 through 2026-12-31 range, the 30-second application timeout, `UseSnapshot=false`, historical `UseCurrentBalance=false`, current `UseCurrentBalance=true`, 3-second warmup, and 15-second measured windows.

MIXED-L8 is attempted only when the adaptive memory admission check is safe. A live 512 MB free-RAM floor stops only the active level and does not invalidate completed lower levels. The report records actual per-scenario windows and requires meaningful overlap across all six before accepting a level.

The output directory is durable under `P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_1_MIXED-*`. BenchmarkDotNet is not used for this profile; the existing V2.1 isolated BDN/NBomber evidence remains unchanged.
