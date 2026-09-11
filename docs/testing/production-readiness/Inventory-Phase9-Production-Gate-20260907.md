# Inventory Phase 9 Production Gate — 2026-09-07

## Executive Summary

**Final verdict: NO-GO.**

The final Phase 1–8 source was deployed to a fresh rehearsal database restored from the Business DB. The full Warehouse test project passed **176/176, 0 failed, 0 skipped**. Source/deployed parity passed for all **71** procedure/trigger definitions in `Database/WarehouseModule.Procedures.sql`. Rehearsal consistency checks returned zero unexplained mismatches, and the M01 index was present and used by the real rebuild procedure.

Production is not approved because:

1. H06 has not been activated: SQL Server Agent remains `Stopped/Manual`, and the live Finalize job still contains the old UTC-date command rather than the final H07 source command.
2. L01 has not received authenticated browser/print-preview acceptance.
3. No production deployment was executed by design.

Business DB, Business SQL Agent/msdb, and production jobs were not modified. No commit was made.

## Source Candidate Manifest

### Database

- `Database/WarehouseModule.Schema.sql`
- `Database/WarehouseModule.Procedures.sql`
- `Database/Migrations/20260904_InventorySnapshotProductionHardening.Schema.sql`
- `Database/Jobs/WarehouseInventoryMovementRebuild.SqlAgent.sql`
- `Database/Jobs/WarehouseInventorySnapshotFinalize.SqlAgent.sql`
- `Database/Jobs/WarehouseInventorySnapshotMonitor.SqlAgent.sql`
- `Database/Jobs/WarehouseInventorySnapshotRebuild.SqlAgent.sql`
- Relevant SQL integration tests under `Database/Tests/`

The rehearsal applied the final consolidated `WarehouseModule.Schema.sql`, then `WarehouseModule.Procedures.sql`. Job scripts were validated as source only; they were not applied to `msdb`.

### C# and UI

- Warehouse controllers under `TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/`
- `TKS_Thuc_Tap_V11_Data_Access/Utility/CUtility.cs`
- `TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehouseActionHistory_Recorder.cs`
- Warehouse entity models under `TKS_Thuc_Tap_V11_Data_Access/Entity/Warehouse/`
- Warehouse Razor components under `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/`

### Tests

The complete `TKS_Thuc_Tap_V11_Data_Access.Tests` project was executed against the fresh rehearsal database. The test connection is selected through `TKS_INTEGRATION_CONNECTION_STRING`; the default Business DB fallback was not used for this run.

## Fresh Rehearsal Environment

| Item | Result |
|---|---|
| SQL Server | `DESKTOP-NHQ7QPL\MSSQLSERVER19`, SQL Server 15.0.2180.2 Developer |
| Business DB | `TKS_Thuc_Tap_V11_GiaiDoan2` |
| Rehearsal DB | `TKS_Thuc_Tap_V11_Inventory_Rehearsal_20260907` |
| Source | Copy-only backup restored from Business DB |
| Business DB write access | Not used |
| SQL Agent/msdb mutation | None |
| Rehearsal backup | `TKS_Thuc_Tap_V11_Inventory_Rehearsal_20260907_Source.bak` |

Read-only Business baseline remained stable:

| Measure | Value |
|---|---:|
| Snapshot rows | 1,242 |
| Snapshot checksum | 1,454,122,485 |
| Movement queue rows | 1,227 |
| Snapshot queue rows | 414 |
| Movement rows | 1,227 |
| Daily rows | 1,227 |
| Current rows | 414 |
| Reservation rows | 0 |

## Upgrade Steps and Migration Result

1. Restored the fresh rehearsal database from the Business copy-only backup.
2. Applied the final schema source.
3. Applied the final procedure/trigger source.
4. Verified schema/index metadata and deployed definitions.
5. Built the affected test project and Web project.
6. Executed the full test project against the rehearsal database.

The existing 414 snapshot queue rows were preserved. In this source dataset they were all already completed rows; after migration they remained `Status=COMPLETED`, `LifecycleStatus=COMPLETED`, `RequestType=REBUILD`, `Requested_Version=1`, and `Claimed_Version=NULL`. No invalid state combination, processing row without a claim, non-processing row with a claim, or active duplicate queue group was found. Existing snapshot row count/checksum remained unchanged.

The fresh rehearsal contains the M01 index:

```text
IX_Inventory_Movement_Daily_Scope_Date
keys:     Kho_ID, San_Pham_ID, Movement_Date
includes: Total_Receipt, Total_Issue, IsValid
```

## Source / Deployed Parity

Normalized `OBJECT_DEFINITION` body hashes were compared with the source declarations after deployment:

```text
MATCH=71  MISMATCH=0  MISSING=0
```

The checked set is every `CREATE OR ALTER PROCEDURE/TRIGGER` block in `Database/WarehouseModule.Procedures.sql`. Critical objects included:

```text
sp_XNK_Nhap_Kho_Save_Header
sp_XNK_Nhap_Kho_Save_Detail
sp_XNK_Xuat_Kho_Save_Header
sp_XNK_Xuat_Kho_Save_Detail
sp_XNK_Document_Post
sp_BC_Xuat_Nhap_Ton_Page
sp_Inventory_Reconciliation_Run
sp_Inventory_Movement_Apply_Invalidation
sp_Inventory_Movement_Complete_Claim
sp_Inventory_Movement_Process_RebuildQueue
sp_Inventory_Balance_Daily_Rebuild
sp_Inventory_Snapshot_Apply_Invalidation
sp_Inventory_Snapshot_Complete_Claim
sp_Inventory_Snapshot_Process_RebuildQueue
sp_Inventory_Snapshot_Rebuild
sp_Inventory_Snapshot_Finalize_Daily
sp_Inventory_Snapshot_Monitor
tr_Inventory_Movement_Guard_Receipt_Posted_Header
tr_Inventory_Movement_Guard_Receipt_Posted_Detail
tr_Inventory_Movement_Guard_Issue_Posted_Header
tr_Inventory_Movement_Guard_Issue_Posted_Detail
tr_Inventory_Snapshot_Invalidate_Receipt_Post
tr_Inventory_Snapshot_Invalidate_Issue_Post
```

No production definition was changed during this verification.

## Full Test Suite

Command:

```text
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-build --nologo --logger "trx;LogFileName=phase9-full-final.trx" --results-directory "%LOCALAPPDATA%\Temp\phase9-results-final"
```

Result:

```text
Passed! - Failed: 0, Passed: 176, Skipped: 0, Total: 176
```

The 15 failures reported by the earlier Phase 8 run were classified and resolved as follows. They were not hidden by changing assertions without first checking the source contract.

| Test group | Count | Classification | Resolution | Final |
|---|---:|---|---|---|
| `WarehouseIssueReservationIntegrationTests` | 3 | STALE_DEPLOYMENT | Fresh rehearsal deployed the current Save_Header contract | PASS |
| `WarehouseCrudContractIntegrationTests` | 4 | STALE_DEPLOYMENT | Same old deployed-signature drift corrected by fresh deployment | PASS |
| `WarehouseUiWorkflowTests` | 2 | STALE_TEST_EXPECTATION | Updated static expectations to the intentional current contract | PASS |
| `WarehouseReportingPostedOnlyIntegrationTests` | 1 | TEST_FIXTURE_BUG | Seeded Daily/Scope projection required by H03 | PASS |
| `WarehouseInventorySnapshotScopeIntegrationTests` | 1 | TEST_FIXTURE_BUG | Seeded Daily/Scope/aggregate readiness | PASS |
| `WarehouseAuthorizationIntegrationTests` | 1 | TEST_FIXTURE_BUG | Corrected invalid supplier fixture so authorization was reached | PASS |
| `WarehouseReportPagingOptimizationTests` | 3 | 1 STALE_TEST_EXPECTATION + 2 TEST_FIXTURE_BUG | Updated static marker and seeded Daily/Scope data | PASS |

No unresolved full-suite failure remains.

## Regression Summary

| Scope | Result |
|---|---|
| C01/C02 | PASS |
| H01/H02/M04 | PASS |
| H03/H08/M06 | PASS |
| H04/H05 | PASS |
| H07 source/date tests | PASS |
| H06 | READY_FOR_ACTIVATION; not activated |
| M05 | PASS |
| M02/M03 | PASS |
| M01 | PASS; clean rehearsal index and real-procedure plan verified |
| L02 | PASS |
| L01 | MANUAL_ACCEPTANCE_PENDING |

Deterministic coverage included receipt SaveDetail/Post race, ambient transactions, authorization and reservation moves, deleted-detail update, report cutoff, scoped historical validation, reconciliation cutoffs, snapshot version handoff, lease/retry/recovery, `FAILED_FINAL` blocking/recovery, business-date calculation, monitor heartbeat, actor persistence, Action History failure outcome, M01 rebuild, and `1.234` precision round trip.

## Consistency Checks

All checks below were run on the rehearsal database after the final full suite:

| Invariant | Mismatch count |
|---|---:|
| Posted Ledger vs `InventoryBalance_Current` | 0 |
| Current Reserved vs `InventoryReservation_Current` | 0 |
| Posted Ledger vs `Inventory_Movement_Daily` | 0 |
| Daily arithmetic | 0 |
| Valid Snapshot vs accumulated Posted Ledger | 0 |
| Daily scope bounds | 0 |
| Active Movement queue duplicates | 0 |
| Active Snapshot queue duplicates | 0 |
| Invalid Snapshot without pending/recoverable representation | 0 |
| Valid Snapshot affected by unresolved terminal failure | 0 |

Reconciliation also passed on rehearsal:

- Current mode: run 17, `COMPLETED`, 414 result rows, 0 failures.
- Historical mode, cutoff `2026-09-04`: run 18, `COMPLETED`, 8,190 result rows, 0 failures.

## Performance Verification

The actual clean-deployed procedure `sp_Inventory_Balance_Daily_Rebuild` was executed in a rollback-isolated plan probe. It used `IX_Inventory_Movement_Daily_Scope_Date` with an `Index Seek`, equality predicates on `Kho_ID` and `San_Pham_ID`, a range predicate on `Movement_Date >= @From_Date`, and residual `IsValid=1`. No Key Lookup was present. The representative call reported one scan and two logical reads.

The Phase 7 benchmark artifact remains the before/after evidence:

| Workload | Before | After |
|---|---:|---:|
| Recent suffix logical reads | 663 | 33 |
| Medium suffix logical reads | 3,965 | 168 |
| Long suffix logical reads | 15,753 | 663 |

Overlapping-request measurement used the same elapsed metric for three separate requests versus one coalesced range. The coalesced range was not beneficial for the measured fixture, so queue semantics were not changed. The index was retained because the read-path improvement was demonstrated and correctness remained green. Approximate index size was 31.02 MB; no abnormal write regression was observed in the rollback-isolated rebuild calls.

No new 10M benchmark was run in this gate.

## Remaining Performance Debt

| Area | Classification | Phase 9 action |
|---|---|---|
| Paged XNT | Existing/secondary observation | Not changed; no architecture redesign |
| Non-paged XNT | `LEGACY/SECONDARY`; approximately 17+ seconds observed | No hot UI caller found; retain as technical debt/recommendation |
| Synchronous SQL behind `Async` names | Not changed | No framework-wide async refactor |
| Load-all lookups | Not changed | No common lookup architecture refactor |

These items are not being reclassified as fixed by this gate.

## H07 and H06 Job Readiness

The final H07 source calculates:

```sql
DATEADD(DAY, -1, CONVERT(date,
    (SYSUTCDATETIME() AT TIME ZONE N'UTC')
        AT TIME ZONE N'SE Asia Standard Time'))
```

The tested results were correct for midnight, late-day, month-boundary, and year-boundary UTC inputs. H07 source/test status is PASS.

Live read-only SQL Agent state remains:

```text
SQL Server Agent (MSSQLSERVER19): Stopped / Manual
```

The live job metadata was not changed. The Finalize job is enabled and scheduled at 00:15, but its current live command still uses the old `CONVERT(DATE, SYSUTCDATETIME())` expression. This is deployment drift and must be corrected only during an approved production activation.

| Job | Live schedule | Live status | Target | Rehearsal/manual tick |
|---|---|---|---|---|
| Movement aggregate rebuild | Every 1 minute | Enabled | Business DB | PASS; second idle tick idempotent |
| Snapshot repair | Every 5 minutes | Enabled | Business DB | PASS; second idle tick idempotent |
| Snapshot Finalize | 00:15 | Enabled | Business DB | Source/date logic tested; live command drift remains |
| Snapshot Monitor | Every 15 minutes | Enabled | Business DB | PASS; all final metrics 0 |

H06 is therefore **READY_FOR_ACTIVATION**, not FIXED. Activation requires reviewing every enabled instance-level job before starting SQL Agent, verifying owners/permissions and target DB, deploying the final H07 command, starting the service under an approved window, observing one controlled tick, checking job history/heartbeat/queue/monitor output, and confirming that unrelated jobs do not run unexpectedly.

## L01 Manual Acceptance Status

L02 is PASS. `CUtility.Format_So_Luong` is used only by Warehouse paths found in the repository and now preserves up to three decimals (`0.###`). The `1.234` round trip remained unchanged through ledger, current, movement, daily, snapshot, and report calculations.

L01 remains **READY_FOR_MANUAL_ACCEPTANCE / MANUAL_ACCEPTANCE_PENDING** because no authenticated browser print-preview session was available and no production credential was used.

Manual acceptance checklist for an authorized Warehouse user:

- Receipt: one detail and multi-page detail set.
- Issue: one detail and multi-page detail set.
- Quantity `1.234`, long product name, large amount, and missing optional supplier.
- Print preview, A4 margins, clipping/overflow, page breaks, repeated table header, footer/signature placement, Vietnamese font, quantity, and total.
- Confirm receipt and issue required fields from the text requirements; do not require image-only fields absent from the current schema/contract.

## Deployment Runbook (Prepared, Not Executed)

### Pre-deploy

1. Take and verify a restorable database backup.
2. Define maintenance/write policy and point of no return.
3. Capture current object parity, schema/index metadata, queue states, and reconciliation baseline.
4. Review disk space, index-creation impact, application binary compatibility, and rollback prerequisites.
5. Inventory every enabled SQL Agent job on the instance before any Agent start.

### Deployment order

1. Schema and additive migration/backfill, if applicable.
2. M01 index.
3. Procedures and triggers.
4. Application binaries.
5. Job definitions/source command parity.
6. Approved SQL Agent activation and controlled tick.

### Post-deploy

Run smoke tests for receipt draft/detail/post, current increase, movement/daily processing, period report, issue reservation/post/release, snapshot repair, monitor heartbeat, actor persistence, and Action History warning behavior. Then run reconciliation and the consistency queries above.

### Rollback

Application rollback and database rollback are separate decisions. After business writes use the new schema/procedure contract, restoring old procedures alone is not assumed safe. The rollback plan must preserve new data/columns or use a tested reverse migration, and must identify the point of no return before production writes. No rollback was executed.

## Production Smoke Tests

The following are prepared for an approved production window and were not run against Business DB in this gate:

1. Create Draft receipt and detail.
2. Post receipt; verify Current/Ledger/Movement.
3. Process movement queue; verify Daily.
4. Verify period report and snapshot.
5. Create Draft issue, reserve, post, and verify reservation release/current decrease.
6. Verify monitor heartbeat and queue state.
7. Verify actor fields and Action History failure warning path.

## Files Changed

The current worktree contains these source/test/report artifacts; no unrelated cleanup was performed:

```text
Database/Jobs/WarehouseInventorySnapshotFinalize.SqlAgent.sql
Database/WarehouseModule.Procedures.sql
Database/WarehouseModule.Schema.sql
TKS_Thuc_Tap_V11_Data_Access.Tests/AssemblyInfo.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/ChuHangPermissionTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/MasterDataDeleteIntegrationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/MemberGroupSynchronizationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseAuthorizationIntegrationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseCrudContractIntegrationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseDetailDisplayFormatTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseDocumentWarehouseFilterIntegrationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseInventoryC01C02IntegrationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseInventoryMovementReliabilityIntegrationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseInventorySnapshotHardeningIntegrationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseInventorySnapshotIntegrationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseInventorySnapshotScopeIntegrationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseIssueReservationIntegrationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseModuleStructureTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseOpeningBalanceIntegrationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseReportPagingOptimizationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseReportingPostedOnlyIntegrationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseUiWorkflowTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehousePhase2H01H02M04IntegrationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehousePhase3H03H08M06IntegrationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehousePhase5IntegrationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehousePhase6M02M03IntegrationTests.cs
TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseTestDatabase.cs
TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehouseDocument_Controller.cs
TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehouseActionHistory_Recorder.cs
TKS_Thuc_Tap_V11_Data_Access/Utility/CUtility.cs
TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_1_Warehouse_List.razor
docs/testing/performance/phase7-m01-20260907.md
docs/testing/performance/phase7-m01-showplan-probe.sql
docs/testing/production-readiness/Inventory-Phase9-Production-Gate-20260907.md
```

## Database Objects / Indexes Changed

- `InventorySnapshot_RebuildQueue`: `Requested_Version`, `Claimed_Version`, lifecycle/version constraints and supporting metadata in the final schema source.
- `IX_Inventory_Movement_Daily_Scope_Date` on `Inventory_Movement_Daily`.
- All 71 procedure/trigger definitions in `Database/WarehouseModule.Procedures.sql`; the critical deployed set is listed in the parity section above.
- No `msdb` job object was changed.
- No Business DB object was changed.

## Remaining Risks and Gate Status

- H06 production activation is pending and the live Finalize command is stale relative to H07 source.
- L01 browser/manual acceptance is pending.
- Non-paged XNT, synchronous SQL naming, and load-all lookup behavior remain documented performance debt; none was silently marked fixed.
- The rehearsal proves the source/deployment path and correctness against the cloned state, not production activation or production credentials.

## Final Re-scored Audit

| Finding | Status |
|---|---|
| C01/C02 | PASS |
| H01/H02/M04 | PASS |
| H03/H08/M06 | PASS |
| H04/H05 | PASS |
| H07 | PASS — source/test; live activation pending |
| H06 | READY_FOR_ACTIVATION |
| M05 | PASS |
| M02/M03 | PASS |
| M01 | PASS |
| L02 | PASS |
| L01 | MANUAL_ACCEPTANCE_PENDING |

## Verification Record

```text
Fresh rehearsal DB created:       YES
Business DB unchanged:            YES
Business SQL Agent unchanged:     YES
Full test count:                  176
Full failures:                    0
Build:                            PASS (0 errors)
git diff --check:                 PASS
Production deployment executed:  NO
Commit:                           NO
```

**Production Gate: NO-GO pending H06 approved activation and L01 manual acceptance.**
