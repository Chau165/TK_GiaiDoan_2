# InventoryReportPaged isolated load matrix

- Generated: 2026-08-30 14:49:17 +07:00
- Source revision: cbe10b7baa9f4fdf9a208fd2c8ad2da9131b5c29
- Database: TKS_Thuc_Tap_V11_Perf_10000000
- Data and log directory: P:\TKS_Thuc_Tap_V11_PerfData
- Data target: 10000000 detail rows
- Scope: InventoryReportPaged only; no other NBomber scenarios are registered.
- Read path: sp_BC_Ton_Kho_Hien_Tai_Page reads the materialized current balance; page size 10; PERF_USER is authorized for all benchmark warehouses.
- NBomber duration: 10 seconds for each worker level.

## Source and deployment verification

The dedicated benchmark database was reused; idempotent schema and procedure contracts were redeployed from the current source before measurement. The procedure source contains one current definition, and [the verification output](source-and-deployment-verification.txt) records the deployed procedure markers and movement-aggregate initialization.

## BenchmarkDotNet single-operation baseline

| Method | Mean | Allocated |
|---|---:|---:|
| InventoryReportPaged | 2.515 ms | 23.8 KB |

## NBomber isolated concurrency results

| Workers | Requests | OK | Failed | Mean OK ms | P95 OK ms | SQL CPU ms | SQL elapsed ms | Peak grant MB | TempDB peak MB | RESOURCE_SEMAPHORE ms | CXPACKET ms | CXCONSUMER ms | PAGEIOLATCH ms |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 917 | 917 | 0 | 10.32 | 33.76 | 0 | 0 | 0 | 5.62 | 50015 | 0 | 0 | 13 |
| 2 | 2 | 2 | 0 | 21623.16 | 21790.72 | 0 | 0 | 0 | 5.56 | 114532 | 0 | 0 | 10 |
| 4 | 4 | 4 | 0 | 23220.43 | 23396.35 | 0 | 0 | 0 | 5.56 | 123694 | 0 | 0 | 0 |
| 8 | 8226 | 8226 | 0 | 9.4 | 17.98 | 11112.38 | 35879.07 | 0 | 6.06 | 0 | 0 | 0 | 12 |
| 16 | 25137 | 25137 | 0 | 17.13 | 41.09 | 0 | 0 | 0 | 6.56 | 36375 | 0 | 0 | 33 |

## Telemetry interpretation

- SQL CPU/elapsed/logical reads are deltas from sys.dm_exec_procedure_stats for sp_BC_Ton_Kho_Hien_Tai_Page during each NBomber run.
- Memory grant and TempDB fields are peaks from 500 ms DMV samples. The TempDB values are server-wide because temporary tables are shared by SQL Server.
- Wait deltas are server-wide sys.dm_os_wait_stats deltas taken around each run. They may include unrelated local SQL activity; active request and memory-grant samples are restricted to the benchmark database.

## Artifacts

- [Matrix CSV](inventory-load-matrix.csv)
- [Source/deployment verification](source-and-deployment-verification.txt)
- [BenchmarkDotNet artifacts](benchmarkdotnet-inventory/)
- Per-worker NBomber reports and resource telemetry: `nbomber-workers-1/`, `nbomber-workers-2/`, `nbomber-workers-4/`, `nbomber-workers-8/`, `nbomber-workers-16/`.
