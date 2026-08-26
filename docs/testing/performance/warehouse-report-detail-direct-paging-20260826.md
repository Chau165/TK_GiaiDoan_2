# DetailReportPaged direct paging — 2026-08-26

## Scope

This change only targets `sp_BC_Chi_Tiet_Nhap_Page` and
`sp_BC_Chi_Tiet_Xuat_Page`. UI, Blazor, controller signatures, DTOs, output
columns, authorization semantics, and inventory movement calculation remain
unchanged.

## Before and after

Before, the procedure joined every matching header/detail row into
`#DetailScope`, counted that temporary table, and then applied `OFFSET/FETCH`.
That copied the whole report scope into TempDB even when the user requested a
10-row page.

After, the procedure has two direct paths:

1. `COUNT(*)` reads the indexed header/detail path to preserve the existing
   exact `Total_Count` result set.
2. A `PageScope` CTE applies `ORDER BY ... OFFSET/FETCH` before joining supplier
   and product dimensions. Only the requested page reaches the dimension joins
   and application result set.

`#AuthorizedWarehouse` remains, but it contains only the user's warehouse IDs;
the detail rows are no longer copied to a temporary table.

## Indexes

Added to `Database/WarehouseModule.Schema.sql`:

- `IX_tbl_XNK_Nhap_Kho_Report_Page`: filtered (`Is_Posted = 1`) covering
  header index ordered by date, document number, and document ID.
- `IX_tbl_XNK_Xuat_Kho_Report_Page`: equivalent issue-header index.

The existing raw-detail indexes
`IX_tbl_XNK_Nhap_Kho_Raw_NhapKho_ID` and
`IX_tbl_XNK_Xuat_Kho_Raw_XuatKho_ID` already cover the detail columns needed by
the page lookup, so no duplicate raw indexes were retained.

## Benchmark evidence

Database: SQL Server local isolated performance databases. Page size: 10.
The benchmark compares the previous `sp_Benchmark_Before_*` procedures with
the current procedures on the same seeded database. Values below are one
`SET STATISTICS TIME` run after deployment, so they are directional rather
than a production p95 SLA.

| Dataset | Report | Before elapsed | After elapsed | CPU before | CPU after |
|---:|---|---:|---:|---:|---:|
| 1,000,000 | Receipt detail | 396 ms | 218 ms | 360 ms | 218 ms |
| 1,000,000 | Issue detail | 370 ms | 260 ms | 344 ms | 234 ms |
| 10,000,000 | Receipt detail | 596 ms | 171 ms | 3,251 ms | 703 ms |
| 10,000,000 | Issue detail | 613 ms | 158 ms | 3,375 ms | 609 ms |

The after page statement showed approximately 6 logical reads on the raw
detail lookup and 3 on the header lookup for page 1, plus 20 logical reads per
dimension table for the 10 returned rows. There is no `#DetailScope` read or
insert. The exact count statement still reads the matching indexed report
scope (for example, about 2,998 raw-detail reads at 1M and 6,148 at 10M),
because the existing controller contract requires an exact total count.

The actual plan used the new filtered receipt header index
`IX_tbl_XNK_Nhap_Kho_Report_Page` and a covering raw-detail index seek, with
`Top/Offset` before the dimension lookups. The issue path is structurally
equivalent.

## TDD evidence

RED:

- `Detail_report_procedures_page_from_covering_indexes_without_full_scope_materialization`
  failed against the old deployed definition because `#DetailScope` was still
  present.

GREEN:

- `WarehouseReportPagingOptimizationTests`: 3 passed.
- `WarehouseReportingPostedOnlyIntegrationTests`: 2 passed serially.

The structural test verifies no full detail scope materialization, direct
`OFFSET/FETCH`, and the two report-page indexes. The existing integration test
continues to verify count, output columns, posted-only filtering, and warehouse
authorization isolation.

## Remaining risk

High page numbers still pay the normal `OFFSET` skip cost. The controller
contract exposes page number rather than a continuation key, so keyset/cursor
paging would require a separate contract decision. Exact `Total_Count` also
continues to scan the matching index; removing that scan would require cached
counts or a changed response contract.
