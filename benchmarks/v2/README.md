# Warehouse Benchmark V2

Protocol: WAREHOUSE_BENCHMARK_V2_1 (semantic version 2.0.0)

This is an independent low-memory benchmark surface for the real
TKS_Thuc_Tap_V11_Perf_10000000 Performance DB. It reuses the real Warehouse
Data Access controllers and stored-procedure paths. It does not reduce the
10M dataset and does not modify Warehouse product SQL.

## V1 preservation

The canonical V1 runner is preserved at:

C:\Users\Surface\Documents\Codex\2026-08-30\ban\outputs\run-fixed-warehouse-regression.ps1

Expected V1 SHA-256:

1D97CE267F3D0CE03FD4D6826E66B4ABFC2C04A59554E54B9A751486BD8F8CD6

V1 is historical and directional only for this host. It is not overwritten,
deleted, or treated as exactly apples-to-apples with V2.

## Runner modes

From the repository root:

    .\benchmarks\v2\run-warehouse-benchmark-v2.ps1 -Help
    .\benchmarks\v2\run-warehouse-benchmark-v2.ps1 -Validate
    .\benchmarks\v2\run-warehouse-benchmark-v2.ps1 -SelfTest
    .\benchmarks\v2\run-warehouse-benchmark-v2.ps1 -Baseline
    .\benchmarks\v2\run-warehouse-benchmark-v2.ps1 -Bdn
    .\benchmarks\v2\run-warehouse-benchmark-v2.ps1 -NbomberStandard
    .\benchmarks\v2\run-warehouse-benchmark-v2.ps1 -Mixed
    .\benchmarks\v2\run-warehouse-benchmark-v2.ps1 -Capacity
    .\benchmarks\v2\run-warehouse-benchmark-v2.ps1 -FenceModeCompare
    .\benchmarks\v2\run-warehouse-benchmark-v2.ps1 -WriterContention

The default target is hard-coded and guarded as:

- server: localhost\MSSQLSERVER19;
- database: TKS_Thuc_Tap_V11_Perf_10000000;
- DB ID: 5.

The Business DB is never a benchmark target. V2 does not start or stop SQL
Agent, change MAXDOP, change max server memory, flush cache, rebuild Current,
enable GROUP for the standard baseline, or regenerate data.

## Standard baseline

The standard profile is six isolated real read scenarios at explicit levels
1, 2, and 4. There are 18 sequential NBomber blocks. C6 and C8 are optional
capacity observations and are not required for standard PASS.

BenchmarkDotNet runs one scenario per child process with one Release build,
LaunchCount 1, WarmupCount 2, IterationCount 5, InvocationCount 1, and
UnrollFactor 1. BDN and NBomber never run at the same time.

NBomber runs one scenario per child process with
Simulation.KeepConstant. The copies value is the explicit concurrency level:
one copy means one scenario worker. Warmup is 3 seconds and measured duration
is 15 seconds. Application operation timeout remains 30 seconds.

## Safety and results

The hard free-physical-RAM stop is 512 MB and must not be lowered. Each block
waits for the 1,024 MB soft start threshold, records host pressure, and may be
classified HOST_CAPACITY_LIMIT if recovery does not occur. Completed blocks are
preserved. Host capacity is not a product performance failure.

Every block records host and SQL telemetry, process exit, request/failure
counts, latency percentiles where NBomber provides them, and cleanup residue.
The runner uses at most one automatic retry for a transient harness startup
failure; SQL timeout, deadlock, product error, correctness mismatch, and host
limit remain evidence.

The first V2 run is a baseline of the current system. It cannot prove an
improvement over V1. Future comparisons must be V2 baseline versus a future V2
candidate.
