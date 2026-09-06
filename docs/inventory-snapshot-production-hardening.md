# Inventory Snapshot production hardening

## Deployment order

1. Back up the database and record the current SQL Agent job state.
2. Run `Database/Migrations/20260904_InventorySnapshotProductionHardening.Schema.sql`.
3. Deploy `Database/WarehouseModule.Procedures.sql`.
4. Deploy the three scripts in `Database/Jobs/` for repair, daily finalize and monitoring.
5. Verify the earliest Posted Ledger date and explicitly confirm whether an opening balance exists outside the ledger.
6. Run `sp_Inventory_Snapshot_Bootstrap_From_Ledger` with an operator-selected baseline date and `@Opening_Balance_Confirmed = 1` only when no unrecorded opening balance exists.
7. Run the repair worker until no active queue row remains, finalize the desired as-of date from `Inventory_Balance_Daily`, then run reconciliation.

The bootstrap procedure never reads `InventoryBalance_Current`.  If confirmation is not supplied, it raises `OPENING_BALANCE_REQUIRED` and writes no snapshot.

## Lifecycle

`InventorySnapshot_RebuildQueue.Status` remains for existing consumers.  The new `RequestType` and `LifecycleStatus` are authoritative for the worker.

| Request | Meaning | Worker behaviour |
| --- | --- | --- |
| `REBUILD` | A valid snapshot existed and was invalidated. | Recalculate invalid snapshot dates from posted ledger. |
| `INITIALIZE` | The scope has never had a snapshot. | Wait for a completed ledger bootstrap, then initialize from posted ledger. |

The retry sequence is 1 minute, 5 minutes, 15 minutes and then 1 hour.  An expired lease consumes one attempt.  A non-transient failure, or the configured retry limit, produces `FAILED_FINAL` and a durable `InventorySnapshot_RebuildDeadLetter` row.

## Scheduled jobs

| Job | Frequency | Purpose |
| --- | --- | --- |
| `TKS Warehouse - Inventory Snapshot Repair` | every 5 minutes | Processes `REBUILD`, due retries and bootstrap-ready `INITIALIZE`. |
| `TKS Warehouse - Inventory Snapshot Finalize Daily` | 00:15 daily | Finalizes the previous day from `Inventory_Balance_Daily.ClosingQuantity`. |
| `TKS Warehouse - Inventory Snapshot Monitor` | every 15 minutes | Checks backlog, final failures, expired leases, worker heartbeat, Daily freshness and missing finalization. |

The old `TKS Warehouse - Inventory Snapshot Rebuild` job is retained disabled for rollback.

## Rollback

This is an additive schema migration: do not drop its columns or tables as a rollback action.  Disable the three new jobs, optionally re-enable the retained legacy job, and restore the prior procedure bundle from source control if an application rollback is needed.  A complete database rollback requires the pre-deployment backup; the audit, dead-letter and reconciliation evidence should be retained unless that backup is restored.

## TDD evidence

The hardening tests were written before the schema/procedure implementation.  Their initial run failed because lifecycle columns, procedures and tables did not exist.  After deployment, these tests cover initialization, ledger bootstrap, explicit opening-balance confirmation, Daily finalization, back-date rebuild, lease recovery, retry backoff, dead-lettering and read-only reconciliation.
