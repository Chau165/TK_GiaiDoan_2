# Inventory Movement Aggregate — TDD evidence

## Source intent

User journeys derived from the requested architecture upgrade:

1. A warehouse user posts a receipt or issue; Post stays transactional and only queues the affected daily aggregate scope.
2. A worker rebuilds the daily receipt/issue totals from the posted ledger and marks the scope valid.
3. A back-dated Post invalidates and rebuilds only the affected daily scope.
4. `InventoryReportPaged` reads snapshot plus daily aggregate, never raw receipt/issue details.
5. A rolled-back Post leaves no movement rebuild request.

## RED

Command run before aggregate production code existed:

```powershell
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseInventoryMovementAggregate.IntegrationTests.sql
```

Result: `Msg 208 ... Invalid object name 'dbo.InventoryMovement_RebuildQueue'`.
The fixture had already passed the real Post authorization path; the failure was the missing aggregate queue.

RED checkpoint: `2d9108e` (`test: add movement aggregate lifecycle reproducer`).

## GREEN

Deployment and validation commands actually run:

```powershell
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.Schema.sql
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.Procedures.sql
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -Q "EXEC dbo.sp_Inventory_Movement_Bootstrap_From_Ledger;"
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseInventoryMovementAggregate.IntegrationTests.sql
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.ReportContract.IntegrationTests.sql
```

Results:

- `PASS: movement aggregate post, issue, back-date lifecycle, report and rollback.`
- `PASS: Warehouse report result contracts match report entities`
- Bootstrap state is initialized; the local database materialized 2 pre-existing daily rows.

## Guarantees

| # | What is guaranteed | Test | Result |
|---|---|---|---|
| 1 | Post receipt queues then rebuilds `Total_Receipt` | `WarehouseInventoryMovementAggregate.IntegrationTests.sql` | PASS |
| 2 | Post issue queues then rebuilds `Total_Issue` | same | PASS |
| 3 | Back-date Post invalidates its existing daily row and rebuilds it with a higher version | same | PASS |
| 4 | Paged report procedure references `Inventory_Movement_Daily`, not either raw-detail table | same | PASS |
| 5 | Snapshot + aggregate report values are 0 / 105 / 25 / 80 | same | PASS |
| 6 | Savepoint rollback removes the queued aggregate work | same | PASS |
| 7 | Existing report result-set contract remains valid | `WarehouseModule.ReportContract.IntegrationTests.sql` | PASS |

## Coverage and known operational gap

This is a SQL stored-procedure feature, validated with a rollback-isolated integration test rather than .NET line coverage; no C# production code changed, so a meaningful 80% C# coverage metric does not apply.

`Database/Jobs/WarehouseInventoryMovementRebuild.SqlAgent.sql` was deployed and the job is enabled every minute. The local SQL Server Agent service was stopped at deployment time, so the job cannot run until that service is started. While a queue item is waiting, processing, or failed, `sp_BC_Xuat_Nhap_Ton_Page` returns a retryable freshness error instead of stale inventory data.
