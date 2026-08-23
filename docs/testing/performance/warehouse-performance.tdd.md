# Warehouse performance benchmark evidence

## Scope

The CodeGraph index covers the whole solution (687 files, including 165 C# and 202 Razor files). Runtime benchmarking is focused on the high-volume Warehouse data path because it is the part of the solution with the representative multi-table joins, reports, CRUD procedures, and existing server-side paging contracts.

User journeys were derived during this TDD run:

1. As a maintainer, I want to run a repeatable synthetic workload at 100,000 or 1,000,000 rows, so that application and SQL bottlenecks are measured instead of guessed.
2. As a maintainer, I want page, full-load, concurrent multi-table, and CRUD scenarios, so that the benchmark covers real operating patterns.
3. As a maintainer, I want RAM, CPU, database storage, latency percentiles, and SQL statistics in the output, so that a bottleneck can be located by layer.
4. As a maintainer, I want full-load at large scale to be opt-in, so that a benchmark cannot accidentally exhaust the workstation.

## RED / GREEN evidence

| Stage | Command | Evidence |
|---|---|---|
| Baseline | `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --verbosity minimal` | 30 passed, 0 failed |
| RED | `dotnet test ... --filter FullyQualifiedName~PerformanceBenchmarkTests --verbosity minimal` | Failed to compile because the new performance runner namespace/types did not exist; this was the intended missing implementation signal. |
| GREEN unit | `dotnet test ... --no-restore --filter FullyQualifiedName~PerformanceBenchmarkTests --verbosity minimal` | 6 passed, 0 failed |
| GREEN database, 100k | `Run-WarehousePerformance.ps1 -RecordCount 100000 -Workers 4 -Iterations 3 -Warmup 1 -FullLoad -KeepDatabase -Reset` | 1 benchmark test passed; report and SQL statistics were written. |
| GREEN database, 1M | `Run-WarehousePerformance.ps1 -RecordCount 1000000 -Workers 8 -Iterations 5 -Warmup 1 -KeepDatabase -Reset` | 1 benchmark test passed; paged/concurrent workload completed. Full-load was intentionally skipped by the safety guard. |

## Test specification

| Guarantee | Test or artifact | Type | Result |
|---|---|---|---|
| Parses 1,000,000 rows and keeps full-load opt-in | `PerformanceBenchmarkTests.Options_support_one_million_rows_and_keep_full_load_opt_in` | unit | PASS |
| Calculates latency percentile from sorted samples | `PerformanceBenchmarkTests.Percentile_uses_sorted_nearest_rank_values` | unit | PASS |
| Includes full, paged, join, concurrent, and CRUD scenarios | `PerformanceBenchmarkTests.Scenario_catalog_covers_full_paged_join_and_crud_paths` | unit | PASS |
| Materializes and maps 100,000 rows through the application's reflection mapper | `PerformanceBenchmarkTests.Synthetic_benchmark_materializes_and_maps_one_hundred_thousand_rows` | integration-with-source | PASS |
| Does not serialize the configured connection string | `PerformanceBenchmarkTests.Serialized_report_does_not_contain_connection_string_secrets` | unit | PASS |
| Runs the database workload only when explicitly enabled | `PerformanceBenchmarkTests.Database_benchmark_runs_only_when_explicitly_enabled` | database integration | PASS at 100k and 1M |

## Measured results

The test database was isolated from `TKS_Thuc_Tap_V11_GiaiDoan2` and seeded set-wise:

- 100,000 movement lines: 50,000 receipt lines, 50,000 issue lines, 10,000 products, 5,000 headers per movement type.
- 1,000,000 movement lines: 500,000 receipt lines, 500,000 issue lines, 10,000 products, 50,000 headers per movement type.
- Page size was 10 in every paged scenario.

Selected application/controller measurements:

| Scale / scenario | p50 | p95 | Allocation observed | Notes |
|---|---:|---:|---:|---|
| 100k `InventoryReportPaged` | 295.51 ms | 306.92 ms | 0.18 MB across 3 calls | Only 10 rows returned per call. |
| 100k `DetailReportFullLoad` | 373.57 ms | 446.61 ms | 200.18 MB across 3 calls | Full `DataTable` + entity mapping path. |
| 100k `InventoryReportFullLoad` | 183.61 ms | 217.76 ms | 48.31 MB across 3 calls | Report aggregates then maps a full list and loads warehouse lookup. |
| 1M `InventoryReportPaged` | 267.75 ms | 286.41 ms | 0.28 MB across 5 calls | Page size remains 10, but SQL aggregation still scales with movement volume. |
| 1M `ConcurrentMixedWorkload` | 46.07 ms | 84.74 ms | 2.39 MB across 40 calls | 8 workers, mixed master/document/report page requests. |
| 1M `Crud_Master_Save_Update` | 9.80 ms | 10.08 ms | 0.08 MB across 5 calls | Current master save/update path executed successfully. |

Database storage at the end of the run was approximately 190.9 MB used data and 478.2 MB allocated log for the 1M-row isolated database. These are database-file measurements, not a claim about production disk capacity.

SQL Server `STATISTICS IO/TIME` for 1M rows confirmed the inventory page bottleneck:

- `sp_BC_Xuat_Nhap_Ton_Page`: about 686–702 ms CPU and 268–280 ms elapsed; the movement inputs reported 3,156 logical reads each for receipt/issue raw tables, 466 each for their headers, and 625 for the product table before returning the 10-row page.
- `sp_BC_Chi_Tiet_Nhap_Page`: 500,000 total rows counted, about 63 ms CPU and 65 ms elapsed in the probe; the page query still performs the count separately from the page query.
- `sp_DM_Master_Page`: 10,000 total products, about 23 logical reads for the page query in the warm-cache probe.

## Findings grounded in source and measurements

1. **Primary database bottleneck:** `CWarehouseReport_Controller.Inventory_Report_Page_Async` calls `sp_BC_Xuat_Nhap_Ton_Page`, whose current procedure builds `#Aggregated` from all receipt and issue movements and only then applies `OFFSET/FETCH`. Paging limits the response payload, not the aggregation work.
2. **Primary application memory bottleneck:** `CWarehouse_Controller_Base.List_From_Procedure` fills a `DataTable`, then maps every `DataRow`; `CUtility.Map_Row_To_Entity` calls `GetProperty` for every returned column and row. This is visible in the 200 MB allocation for the 100k full detail-report run.
3. **Unbounded legacy paths remain:** Warehouse controllers still expose `List_Master_Async`, `List_Lookup_Async`, `List_Documents_Async`, `List_Document_Details_Async`, `Detail_Report_Async`, and `Inventory_Report_Async`; the paged alternatives exist separately, so callers can still choose the full-load path.
4. **CRUD correctness defect found before timing:** current controller arguments do not match deployed procedure signatures for `sp_DM_Delete`, `sp_XNK_Nhap_Kho_Save_Header`, `sp_XNK_Nhap_Kho_Save_Detail`, and `sp_XNK_Nhap_Kho_Delete_Header`. The benchmark records `Parameter count does not match Parameter Value count` instead of reporting false CRUD latency. Master save/update matched and executed.
5. **System-wide static risk:** the solution contains 135 `new DataTable` sites, 34 files calling `CSqlHelper.FillDataTable`, one `FillDataSet` caller, and 11 `Task.FromResult` wrappers in the data-access project. These are audit candidates; the Warehouse benchmark proves the cost at scale but does not claim every module has the same row volume.

## Known gaps and safety boundaries

- The run is local Windows SQL Server with warm cache; it is not a production network, multi-instance, or authenticated browser benchmark.
- Full-load at 1M rows is implemented but not run by default. Use `TKS_PERF_FULL_LOAD=1`/`-FullLoad` only on the isolated performance database after checking workstation memory.
- No production SQL index or application optimization was applied in this task; the measurements are a baseline and diagnosis guardrail.
- The current test project has no coverage collector configured, so a percentage coverage claim is not made. The focused tests and database artifacts are the available evidence.
