# Warehouse Report paging optimization — 2026-08-26

## Scope

This change covers only:

- `sp_BC_Xuat_Nhap_Ton_Page` paging materialization.
- `sp_BC_Chi_Tiet_Nhap_Page` and `sp_BC_Chi_Tiet_Xuat_Page` scope reuse.
- authorization scope reuse inside the detail procedures.

The UI, Blazor components, controller signatures, DTOs, output columns, snapshot algorithm, and `#MovementAggregate` logic were not changed.

## Source bottleneck before

The effective inventory procedure built a wide `#WarehouseScopeResult_Snapshot` containing warehouse/product names and all display columns before both `COUNT(*)` and `OFFSET/FETCH`. The same wide temporary result was therefore read for count and page.

The effective detail procedures executed the header/detail join twice. Both statements repeated the warehouse authorization `EXISTS` predicate:

1. count all matching detail rows;
2. join master data and return the requested page.

## Implementation

The effective definitions are the final `CREATE OR ALTER` blocks in [WarehouseModule.Procedures.sql](../../../Database/WarehouseModule.Procedures.sql):

- Inventory: `#AuthorizedWarehouse`, `#SnapshotBalance`, and `#MovementAggregate` are preserved. A narrow `#ReportKeys` stores only warehouse/product keys and numeric measures; count reads `#ReportKeys`; dimensions are joined only by the page query.
- Detail receipt: `#AuthorizedWarehouse` and `#DetailScope` are populated once, then count/page read the scope. The page query joins only supplier/product master data.
- Detail issue: same flow, with the existing empty supplier column preserved.

The source file still contains older legacy duplicate procedure blocks. SQL Server uses the last `CREATE OR ALTER` definition; the deployed object definition and tests validate that effective definition.

## TDD evidence

RED was captured before deployment: the structural optimization test failed because the deployed objects did not yet contain the new narrow scopes; the functional fixture passed.

GREEN after deployment:

- `WarehouseReportPagingOptimizationTests`: 2 passed.
- `WarehouseReportingPostedOnlyIntegrationTests`: 2 passed.
- `WarehouseInventorySnapshotIntegrationTests`: 1 passed when run serially.

The test fixture verifies total count, warehouse authorization isolation, and unchanged inventory/detail result shape. Running the two database-mutating integration classes concurrently once produced a test-harness deadlock because both share the same database; the serial rerun passed.

## Before/After SQL benchmark

Dataset databases:

- `TKS_Thuc_Tap_V11_Perf_1000000` — 1,000,000 seeded movement records.
- `TKS_Thuc_Tap_V11_Perf_10000000` — 10,000,000 seeded movement records.

Page size was 10. The benchmark loaded the previous effective procedures under isolated `sp_Benchmark_Before_*` names and compared them with the current procedures on the same database. Inventory movement aggregation is identical in both paths; its algorithm was not benchmarked as an optimization target.

SQL elapsed time from the final `selectinto` benchmark run:

| Scale | Report | Before | After | Result |
|---|---|---:|---:|---|
| 1M | Detail receipt | 377 ms | 1,479 ms | slower |
| 1M | Detail issue | 467 ms | 1,516 ms | slower |
| 1M | Inventory | 2,044 ms | 884 ms | improved |
| 10M | Detail receipt | 543 ms | 1,603 ms | slower |
| 10M | Detail issue | 452 ms | 1,888 ms | slower |
| 10M | Inventory | 2,403 ms | 1,433 ms | improved |

The result is important: inventory paging optimization is confirmed, but the requested full `#DetailScope` materialization is not a performance win on the current indexes and page-1 workload. It removes a raw-table re-read but introduces a large tempdb copy and a second scope read. This is recorded as an open bottleneck rather than hidden.

Current application-level final run (`Workers=1`, 3 measured operations, page size 10):

| Scale | Report | P50 | P95 | Process CPU | Working-set delta |
|---|---|---:|---:|---:|---:|
| 1M | Detail | 478.21 ms | 531.16 ms | 421.88 ms | 3.55 MiB |
| 1M | Inventory | 647.10 ms | 695.37 ms | 265.62 ms | 1.07 MiB |
| 10M | Detail | 974.49 ms | 1,033.60 ms | 203.12 ms | 0.70 MiB |
| 10M | Inventory | 963.21 ms | 967.25 ms | 125.00 ms | 1.75 MiB |

## Logical reads and execution-plan evidence

Inventory temporary-result reads in the 1M run changed from approximately `2,009 + 2,009` logical reads on the wide result (count + page) to `850 + 1,395` on `#ReportKeys`. At 10M, the corresponding change was approximately `2,795 + 2,795` to `1,182 + 1,728`.

The actual plans are captured in:

- [inventory before actual plan](warehouse-report-inventory-before-actualplan-20260826.xml)
- [inventory after actual plan](warehouse-report-inventory-after-actualplan-20260826.xml)
- [detail before actual plan](warehouse-report-detail-before-actualplan-20260826.xml)
- [detail after actual plan](warehouse-report-detail-after-actualplan-20260826.xml)

The detail after-plan contains a `Table Insert` for an estimated 500,040 rows in `#DetailScope`; this is the measured cost behind the Detail regression. The inventory after-plan contains the narrow `#ReportKeys` insert and no effective wide-result materialization.

The session-level tempdb probe reports zero net pages after each procedure because the temporary objects are dropped before the post-call sample. Therefore it is not a peak-tempdb measurement. The benchmark files retain temporary-table logical reads and actual plan evidence; a production peak requires Extended Events or server-side workload telemetry.

## Benchmark artifacts

- [Benchmark SQL](../../../Database/Performance/WarehouseReportPaging.Benchmark.sql)
- [Tempdb probe SQL](../../../Database/Performance/WarehouseReportPaging.Tempdb.sql)
- [1M SQL stats](warehouse-report-paging-1000000-before-after-selectinto-20260826.sqlstats.txt)
- [10M SQL stats](warehouse-report-paging-10000000-before-after-selectinto-20260826.sqlstats.txt)
- [1M final application benchmark](warehouse-report-paging-1000000-after-final-20260826.json)
- [10M final application benchmark](warehouse-report-paging-10000000-after-final-20260826.json)

## Remaining risk

Inventory paging is improved without changing movement calculation. Detail authorization is now evaluated once and the output is correct, but the current `#DetailScope` design should not be declared production-optimal yet. The next safe optimization should be measured separately: a narrow key-only scope/keyset strategy or a covering index for the date/warehouse/document-detail path, followed by the same 1M/10M benchmark.
