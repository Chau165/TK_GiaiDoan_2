# Inventory snapshot scope selection — TDD evidence

## Scope

Phase 1 correctness only: each `Kho_ID` + `San_Pham_ID` must choose its own latest valid snapshot before the report start date. No worker, retry, direct-DML, bootstrap, index, or benchmark change is included.

## RED

The rollback-isolated integration fixture creates two authorized warehouse scopes for the same product:

| Scope | Valid snapshots before 21/02/2099 | Daily movement used by report | Expected opening |
|---|---|---|---:|
| Warehouse A | 31/01 = 100; 20/02 = 999 but invalid | +10 on 10/02, +5 on 22/02 | 110 |
| Warehouse B | 31/01 = 100; 20/02 = 200 | -20 on 22/02 | 200 |

Before the production change, the paged report selected the global valid date `20/02` from warehouse B. Warehouse A had no valid snapshot on that date, so it returned opening `0` instead of `110`.

```powershell
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter "FullyQualifiedName~WarehouseInventorySnapshotScopeIntegrationTests"
```

Result: `Expected: 110; Actual: 0.000`.

RED checkpoint: `3a95afc` (`test: reproduce per-scope snapshot selection`).

## GREEN

```powershell
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.Procedures.sql
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter "FullyQualifiedName~WarehouseInventorySnapshotScopeIntegrationTests"
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.ReportContract.IntegrationTests.sql
```

Results:

- Scope test: `Passed: 1`.
- Existing report result-set contract: `PASS: Warehouse report result contracts match report entities`.
- The deployed `fn_Inventory_Report_Snapshot` and `sp_BC_Xuat_Nhap_Ton_Page` contain no `MAX(Snapshot_Date)` global selection.

## Guarantees

| # | Guarantee | Evidence |
|---|---|---|
| 1 | A valid snapshot is chosen with `TOP (1) ... ORDER BY Snapshot_Date DESC` for the exact warehouse/product scope. | Scope integration test covers distinct valid dates and an invalid newer snapshot. |
| 2 | Paged reporting reads daily movement only after that scope's snapshot date. | Warehouse A returns opening/receipt/issue/closing `110/5/0/115`; B returns `200/0/20/180`. |
| 3 | The non-paged report follows the same per-scope baseline rule. | The same integration test executes `sp_BC_Xuat_Nhap_Ton` and asserts both scopes. |
| 4 | Fallback telemetry identifies missing snapshots for one or more scopes, rather than treating another scope's snapshot as sufficient. | `NO_VALID_SNAPSHOT_FOR_ONE_OR_MORE_SCOPES`. |
| 5 | Aggregate freshness checking compares a queue item with the affected scope's own movement window. | `#SnapshotBalance` carries the per-scope snapshot date into the existing queue check. |

## Coverage and deliberate next phases

This is SQL stored-procedure behavior; the test is integration-level and rolls back all fixture data. No C# production line changed, so a C# line-coverage percentage is not meaningful for this phase.

The remaining production-hardening work is deliberately deferred: concurrent worker claim semantics, duplicate queue deduplication, retry/dead-letter lifecycle, direct ledger DML protection, and bootstrap locking. No benchmark was run.
