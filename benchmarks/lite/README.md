# Warehouse Benchmark Lite V2

Lite V2 measures the current Warehouse read paths on `DESKTOP-NHQ7QPL\MSSQLSERVER19`, database `TKS_Thuc_Tap_V11_Perf_10000000` (expected `DB_ID() = 5`). It is a lightweight, standalone measurement path and does not resume the Full Batch4 canonical workload. Existing Lite V1 result directories remain preserved and are not reused.

## Application path and scope

Every measured request follows the existing Data Access controller, Warehouse controller/base, `CSqlHelper`, stored procedure, and SQL Server path. Direct SQL is limited to target/database identity, partition metadata, live procedure-source parity, and bounded historical-readiness/preparation checks.

| Scenario | Data Access method | Procedure |
|---|---|---|
| MasterPaged | `CWarehouseMaster_Controller.List_Master_Page_Async` | `dbo.sp_DM_Master_Page` |
| LookupPaged | `CWarehouseMaster_Controller.List_Lookup_Page_Async` | `dbo.sp_DM_Lookup_Page` |
| DocumentPaged | `CWarehouseDocument_Controller.List_Documents_Page_Async` | `dbo.sp_XNK_Document_Page` |
| DetailReportPaged | `CWarehouseReport_Controller.Detail_Report_Page_Async` | `dbo.sp_BC_Chi_Tiet_Nhap_Page` |
| InventoryHistoricalReportPaged | `CWarehouseReport_Controller.Inventory_Report_Page_Async` | `dbo.sp_BC_Xuat_Nhap_Ton_Page` |
| InventoryCurrentBalancePaged | `CWarehouseReport_Controller.Inventory_Report_Page_Async` | `dbo.sp_BC_Ton_Kho_Hien_Tai_Page` |

The runner verifies the configured connection uses Integrated Security and the live SQL identity is the expected server, database, DB_ID, and ONLINE state. It compares the six workload procedures with `Database/WarehouseModule.Procedures.sql` before measurement. Historical preparation additionally requires exact source parity for the queue worker and rebuild procedures.

## Default protocol

- Six sequential scenarios, one at a time, concurrency 1: one smoke call, 3 warmups, 30 measured controller calls.
- Data-derived matrix cases use one smoke call, one warmup, and 10 measured calls. Document pages 1/100/1000/10000 are included only when valid for current `Total_Count`; search terms and report dates are derived from current Data Access results. Detail and current-balance deep pages are capped at page 100.
- Historical matrix and load cases run only after readiness passes. If preparation is needed, the runner persists the full relevant pre-state, rechecks it, and may invoke `sp_Inventory_Movement_Process_RebuildQueue` exactly once with `@Batch_Size = 1`. Any other pending/retry/failure state, dead letter, active processing row, uninitialized aggregate, parity mismatch, or changed pre-state blocks the worker. No SQL Agent job is started.
- Bounded NBomber stages cover Document, Detail, Historical (when ready), and Current Balance at closed concurrency 1/2/4, one scenario and user level at a time. Each stage uses a 3-second warmup and 30-second measured duration. No per-request retries are configured.
- Measured calls use page size 10, receipt path, login `PERF_USER`, and no warehouse filter. Detail/historical normal dates are 2025-01-01 through 2026-12-31. The current-balance Data Access method does not pass dates or expose a filter.
- The application command timeout is not overridden. Measurement workload invokes no business write method. A historical preparation worker call can refresh aggregate and queue state; it is reported separately with pre/post snapshots.
- No latency SLA is defined. Timings are observations for this machine, not pass/fail performance targets.

The application workload makes no direct SQL reads. Preparation and identity queries are bounded/read-only except for the explicitly gated one-call historical worker preparation. The runner does not use BDN, mixed workload, 8-user load, host admission, heavy telemetry, or write benchmarks.

## Build and run

From the repository root:

```powershell
dotnet build .\benchmarks\lite\BenchmarkLite.csproj -c Release
& .\benchmarks\lite\bin\Release\net8.0\BenchmarkLite.exe
```

The default mode is `all` (sequential + matrix + bounded load). Optional modes:

```powershell
& .\benchmarks\lite\bin\Release\net8.0\BenchmarkLite.exe --mode sequential
& .\benchmarks\lite\bin\Release\net8.0\BenchmarkLite.exe --mode load
```

Options: `--preflight-only` checks target identity and all eight stored-procedure definitions, then exits before historical preparation or workload; `--config <path>`, `--measured-count 1..300` (default 30), `--matrix-measured-count 1..100` (default 10), `--load-seconds 1..300` (default 30), `--load-warmup-seconds 0..60` (default 3), and `--users 1,2,4` (any subset, default all three). `--help` prints the command synopsis. Never put passwords in the benchmark config.

Every execution creates a new `benchmarks/lite/results/WHB-LITE2-<timestamp>-<id>/` folder. Existing result folders are never reused.

## Output

- `benchmark-lite2-results.json` — protocol, source/build identity, procedure parity, scenario and matrix metrics, load stages, verdicts, and artifact check.
- `benchmark-lite2-report.md` — concise run report, hotspots, skips, errors, and limitations.
- `benchmark-lite2-sequential-raw.csv` — every sequential smoke/warmup/measured call.
- `benchmark-lite2-matrix-raw.csv` — data discovery plus every matrix smoke/warmup/measured call.
- `benchmark-lite2-load-results.json` — every NBomber stage, per-stage memory snapshots, counters, latency fields, and projected native `NodeStats.ScenarioStats`.
- `benchmark-lite2-load-raw.csv` — compact stage-level load results and error categories.
- `benchmark-lite2-environment.json` — machine, SQL target, dataset metadata, config hash, procedure parity, and source/build hashes; no connection string is saved.
- `historical-readiness.json` — queue/aggregate pre-state, immediate pre-worker state, worker call evidence, and post-state where applicable.
- `benchmark-lite2-run.log` — progress and stop reason with secrets omitted.

`PASS` means the requested mode completed its required controller calls with valid results and no observed request errors. It does not claim a production SLA, full-dataset value equality, production-hardware performance, or Full Batch4 readiness.
