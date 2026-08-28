# Inventory snapshot lifecycle — TDD and performance evidence

## Scope

The refactor is limited to the database layer. No Blazor, UI, controller, or existing post contract was changed.

## RED → GREEN

The lifecycle test was written before the schema/procedure implementation. The RED run failed because the old database did not have `IsValid`, `InvalidatedAt`, `InvalidReason`, `Version`, or `InventorySnapshot_RebuildQueue`.

```text
sqlcmd ... -i Database\Tests\WarehouseInventorySnapshotLifecycle.IntegrationTests.sql
Invalid column name 'IsValid' / 'InvalidatedAt' / 'InvalidReason' / 'Version'
```

After implementation, the same test passed:

```text
PASS: snapshot lifecycle invalidation, scoped queueing, rebuild, report fallback and rollback.
```

The test covers:

1. Back-date changes the affected snapshots to `IsValid = 0`.
2. Only the affected warehouse/product scope is invalidated.
3. A `WAITING` rebuild queue row is created.
4. Reports do not use invalid snapshots and can fall back to the ledger.
5. The worker rebuilds valid rows and increments `Version`.
6. Post rollback restores snapshot and queue state.

Additional focused checks passed:

- Existing snapshot integration test: back-dated posting invalidates later snapshots.
- Report result contract test: all 12 existing report columns remain compatible.
- .NET data-access snapshot test: 1 passed.

## Production lifecycle

```text
Post document
    ↓
Trigger detects posted/date/detail change
    ↓
Scoped invalidation: Kho_ID + San_Pham_ID + Snapshot_Date >= From_Date
    ↓
Insert or coalesce WAITING queue row
    ↓
Commit post immediately
    ↓
SQL Agent / worker calls sp_Inventory_Snapshot_Process_RebuildQueue
    ↓
sp_Inventory_Snapshot_Rebuild rebuilds from the latest valid prior snapshot
    ↓
Snapshots become valid and Version increases
```

Snapshots are never deleted by the lifecycle triggers. Reports filter `IsValid = 1`; if no valid snapshot exists before the requested period, the ledger fallback is used and `InventorySnapshot_ReportFallbackLog` records `NO_VALID_SNAPSHOT_BEFORE_PERIOD`.

## Benchmark setup

The benchmark databases are isolated: `TKS_Thuc_Tap_V11_Perf_1000000` and `TKS_Thuc_Tap_V11_Perf_10000000`.

| Scale | Products | Receipt lines | Issue lines | SQL stats |
|---:|---:|---:|---:|---|
| 1,000,000 logical records | 10,000 | 500,000 | 500,000 | `warehouse-snapshot-lifecycle-1000000-clean-20260826.sqlstats.txt` |
| 10,000,000 logical records | 10,000 | 1,000,000 | 1,000,000 | `warehouse-snapshot-lifecycle-10000000-clean-20260826.sqlstats.txt` |

The clean run removed only seed-generated queue backlog from the two disposable benchmark databases, recreated the baseline snapshot, and then measured one affected warehouse/product pair.

### SQL report path: delete/fallback vs invalid+queue vs rebuilt snapshot

| Scale | Delete/fallback CPU / elapsed | Invalid+queue CPU / elapsed | Rebuilt snapshot CPU / elapsed |
|---:|---:|---:|---:|
| 1M | 2,625 ms / 1,172 ms | 1,813 ms / 721 ms | 1,658 ms / 711 ms |
| 10M | 2,609 ms / 1,040 ms | 2,719 ms / 1,052 ms | 2,781 ms / 1,011 ms |

Interpretation: the rebuilt path removes the long historical fallback only when the report can reuse a valid snapshot. At 1M this reduced elapsed time by about 39%. At 10M the report is still dominated by current-period movement aggregation, report-key construction, joins, and authorization scope; snapshot lifecycle alone does not remove that cost.

The 10M run completed with `LifecycleState = COMPLETED`, `IsValid = 1`, and `Version = 2`. No invalid snapshot remained for the measured scope.

TempDB reserved user-object pages during the isolated report path were approximately:

- 1M: 18,240 KB after delete/fallback, 33,920 KB after invalidate/report, 17,728 KB after rebuild/report.
- 10M: 25,152 KB after delete/fallback, 47,744 KB after invalidate/report, 69,760 KB after rebuild/report.

These are point-in-time DMV readings, not peak allocation telemetry; a production run should collect a time series.

### Application benchmark limitation

The existing controller benchmark was also attempted. The 10M low-concurrency run completed with one sample per scenario; its `InventoryReportPaged` sample was 1,635.5 ms and `DetailReportPaged` was 472.2 ms. A 1M concurrent run and a 1M low-concurrency rerun hit the existing 30-second command wait timeout in the controller/data-access path, so those P95 values are not reported as valid. This was not hidden by changing the controller contract.

## Remaining production risks

- `sp_Inventory_Snapshot_Create_Daily` preserves the existing behavior of capturing `InventoryBalance_Current`. A site must seed a valid opening snapshot before relying on historical rebuild from that point; otherwise the rebuild can only derive from the posted ledger and cannot invent an opening balance.
- The local SQL Agent service is stopped/manual. The job script is installed and verified at 02:00, but this machine will not execute it until SQL Server Agent is enabled and started.
- The worker recovers `PROCESSING` rows on its next run, but there is no retry-count/dead-letter policy yet for permanently failing rows.
- Fallback logging is intentional observability, but high-volume fallback can itself create writes; monitor and archive the log table.
- The warehouse procedure bundle now contains one definition per object. `WarehouseDocumentPosting.Procedures.sql` is a comment-only deprecated placeholder, so it cannot override the canonical bundle.
- The repository-wide integration script still has an unrelated pre-existing parameter mismatch for `sp_XNK_Nhap_Kho_Save_Header`; it was not changed because it is outside this database-layer lifecycle scope.

## Coverage note

Focused SQL and integration assertions were run. A numeric 80% coverage report was not generated because this repository has no configured SQL coverage collector/threshold; CI should add one before production release.
