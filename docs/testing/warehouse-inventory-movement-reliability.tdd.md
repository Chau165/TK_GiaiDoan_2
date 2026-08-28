# Inventory Movement Aggregate reliability — TDD evidence

## Scope and user journeys

This run implements Phase 2 and Phase 3 without benchmarking or changing UI, DTOs, or the paging contract.

1. A repeated invalidation for one warehouse/product/day merges into one active queue row.
2. A worker that loses the scope or bootstrap applock retries; lock contention is not a business failure.
3. A request arriving while a worker is rebuilding prevents that old claim from completing stale work.
4. Exhausted transient retries become a visible dead-letter, while a later invalidation or a full bootstrap can resolve it.
5. Bootstrap excludes both Post and normal worker rebuilds.
6. Posted ledger headers and details cannot be directly updated or deleted outside the canonical Post transaction.
7. A worker crash that leaves a queue in `PROCESSING` is recovered after its processing lease expires; it cannot block the report forever.
8. The deployment script that runs after the module cannot overwrite the canonical movement-aware Post procedure.

## RED

```powershell
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter "FullyQualifiedName~WarehouseInventoryMovementReliabilityIntegrationTests"
```

Actual failures before production changes:

- `Invalid column name 'Claimed_Version'`.
- `sp_Inventory_Movement_Process_RebuildQueue has too many arguments specified` for the retry-delay contract.
- Direct update of a posted receipt detail succeeded instead of being rejected.

RED checkpoint: `a24a1d0` (`test: reproduce movement queue reliability gaps`).

The later lease-recovery RED test produced `Expected: COMPLETED; Actual: PROCESSING` before the worker recovery path existed.
RED checkpoint: `2061812` (`test: cover expired movement worker claim`).

The deployment-script RED test found `WarehouseDocumentPosting.Procedures.sql` redefining the old two-parameter Post procedure after the canonical module script.
RED checkpoint: `af35169` (`test: prevent post deploy script override`).

## GREEN

```powershell
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.Schema.sql
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.Procedures.sql
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter "FullyQualifiedName~WarehouseInventoryMovementReliabilityIntegrationTests"
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseInventoryMovementAggregate.IntegrationTests.sql
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.ReportContract.IntegrationTests.sql
```

Results:

- Reliability integration suite: `Passed: 6`.
- Existing aggregate lifecycle: `PASS: movement aggregate post, issue, back-date lifecycle, report and rollback.`
- Existing report result-set contract: `PASS: Warehouse report result contracts match report entities`.

## Lifecycle after migration

```text
WAITING -> PROCESSING -> COMPLETED
                  |          |
                  |          +-- newer Requested_Version -> WAITING
                  |
                  +-- processing lease expires -> RETRY_WAITING -> PROCESSING
                  |
                  +-- transient failure -> RETRY_WAITING -> PROCESSING
                  |
                  +-- retry exhausted or non-transient failure -> FAILED_FINAL + DeadLetter
```

`FAILED_FINAL` blocks a report only when its exact scope/day overlaps the report's movement window, because only then the aggregate is actually stale. A newer invalidation reactivates the row; a successful bootstrap resolves all outstanding queue/dead-letter entries after rebuilding the ledger.

## Guarantees

| # | Guarantee | Test | Result |
|---|---|---|---|
| 1 | One active queue exists for `(Kho_ID, San_Pham_ID, From_Date)` and a duplicate insert violates the filtered unique index. | `Duplicate_daily_invalidations_are_merged_and_a_stale_claim_is_requeued` | PASS |
| 2 | A worker claim with an older version becomes `WAITING`, not `COMPLETED`, after a newer invalidation. | same | PASS |
| 3 | Scope applock contention becomes `RETRY_WAITING`; the second transient failure reaches `FAILED_FINAL` and writes one dead-letter. | `Scope_lock_contention_retries_then_moves_the_queue_to_dead_letter` | PASS |
| 4 | Bootstrap's exclusive lock makes a worker retry and rejects a new Post with error `51226`. | `Bootstrap_gate_blocks_worker_claims_and_document_posting` | PASS |
| 5 | Direct quantity update of a posted receipt detail is rejected with error `51228`. | `Direct_update_of_a_posted_detail_is_rejected` | PASS |
| 6 | An expired `PROCESSING` claim consumes one retry and is completed by a later worker; it is not stranded indefinitely. | `Expired_processing_claim_is_recovered_by_a_later_worker` | PASS |
| 7 | Prior daily aggregate/back-date/report/rollback behavior remains correct. | `WarehouseInventoryMovementAggregate.IntegrationTests.sql` | PASS |
| 8 | Phase 1 snapshot-scope selection and report result sets remain unchanged. | `WarehouseInventorySnapshotScopeIntegrationTests`, `WarehouseModule.ReportContract.IntegrationTests.sql` | PASS |
| 9 | The secondary deployment script cannot overwrite the movement-aware Post procedure with its old contract. | `Deployment_post_script_does_not_override_the_canonical_movement_aware_post_procedure` | PASS |

## Migration and operational notes

- `InventoryMovement_RebuildQueue` gains `Requested_Version`, `Claimed_Version`, `LastAttemptAt`, `NextRetryAt`, and `LastError`.
- `sp_Inventory_Movement_Process_RebuildQueue` has `@Processing_Lease_Seconds` (default `300`); an expired claim consumes retry budget and moves to `RETRY_WAITING` or `FAILED_FINAL` plus dead-letter.
- Legacy `FAILED` rows migrate to `RETRY_WAITING` when retryable, otherwise `FAILED_FINAL`; duplicate legacy active rows are retained as `SUPERSEDED`, never deleted silently.
- `InventoryMovement_RebuildDeadLetter` keeps the final failure evidence and resolution timestamp.
- Bootstrap has `InventoryMovement:Bootstrap` exclusive applock. Post and normal rebuild acquire a shared lock within their transaction.
- The direct-DML guards protect application-level callers. A SQL Server `sysadmin` can disable triggers or modify data regardless; production must also run the application under a least-privilege login granted only stored-procedure execution.
- `WarehouseDocumentPosting.Procedures.sql` no longer declares the old Post procedure. The canonical declaration remains in `WarehouseModule.Procedures.sql`, so running the secondary script cannot erase queue invalidation or the maintenance gate.

## Coverage and known gaps

The changed production logic is T-SQL, so the meaningful coverage is rollback-isolated database integration behavior rather than a C# line-coverage percentage. No benchmark was run.

`Database\Tests\WarehouseModule.IntegrationTests.sql` was not used as GREEN evidence: it currently fails before warehouse behavior assertions because its call to `sp_XNK_Nhap_Kho_Save_Header` omits the required `@Ma_Dang_Nhap` parameter. That legacy fixture needs a separate contract update.

Only the stale Post declaration was removed from `WarehouseDocumentPosting.Procedures.sql`. Other legacy procedure declarations in that historical script were intentionally not changed in this Phase 2/3 task and need a separate deployment-bundle audit before treating the whole script as a canonical release artifact.
