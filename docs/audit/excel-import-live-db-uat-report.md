# Excel Import Live DB UAT Report

Date: 2026-10-09  
Verdict: **BLOCKED — DB_PARITY_MISMATCH before persistence**

This phase completed read-only database parity checks and non-database regression tests. No import Confirm or database write was attempted. The deployed Unit insert procedure does not match the required accent-sensitive identity behavior, so fail-closed policy stopped live persistence.

## 1. Environment

- Repository: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`
- SQL Server: `localhost`, Windows Integrated Security
- Effective application connection key: `TKS_Thuc_Tap_V11_Conn_String` from `TKS_Thuc_Tap_V11_Web/appsettings.json`
- Verified with a read-only `SELECT DB_NAME()`: `TKS_Thuc_Tap_V11_GiaiDoan2`
- No automatic reset, schema deployment, database data mutation, or customer workbook edit occurred.

## 2. Read-Only Database Snapshot

Initial and post-test counts remained 0 for the five master tables and all receipt/issue header/detail tables:

| Table | Rows |
| --- | ---: |
| `tbl_DM_Don_Vi_Tinh` | 0 |
| `tbl_DM_Kho` | 0 |
| `tbl_DM_Loai_San_Pham` | 0 |
| `tbl_DM_NCC` | 0 |
| `tbl_DM_San_Pham` | 0 |
| `tbl_XNK_Nhap_Kho` / `tbl_XNK_Nhap_Kho_Raw_Data` | 0 / 0 |
| `tbl_XNK_Xuat_Kho` / `tbl_XNK_Xuat_Kho_Raw_Data` | 0 / 0 |

Inventory snapshot observed after the non-DB test run:

| Inventory table group | Rows |
| --- | ---: |
| Current balance, daily snapshot, movement, daily balance/scope, reservation | 0 |
| Reconciliation result/run, bootstrap audit, worker heartbeat | 0 |
| Rebuild queues/dead letters and report scope catalog | 0 |
| `InventorySnapshot_ReportFallbackLog` | 4 |
| `InventoryBalance_Daily_AggregateState`, `InventoryMovement_AggregateState` | 1 each |
| `Inventory_Current_Report_State`, `Inventory_Report_Fence_Config` | 1 each |

The four fallback-log rows and singleton state/config rows were left untouched. There were 0 other open transactions and 0 disabled DML triggers.

## 3. Database Parity Findings

| Mapping | External value | Configured target | DB lookup field | Live field/collation | Resolvable now? | Ambiguous? |
| --- | --- | --- | --- | --- | --- | --- |
| Unit | `CAI` | `Cái` | `tbl_DM_Don_Vi_Tinh.Ten_Don_Vi_Tinh` | `nvarchar(200)`, `SQL_Latin1_General_CP1_CI_AS`, unique | Profile map resolves; no existing DB row (new master) | 0 current candidates |
| Unit | `HOP` | `Hộp` | same | same | Profile map resolves; no existing DB row (new master) | 0 current candidates |
| Unit | `GOI` | `Gói` | same | same | Profile map resolves; no existing DB row (new master) | 0 current candidates |
| Unit | `CHAI` | `Chai` | same | same | Profile map resolves; no existing DB row (new master) | 0 current candidates |
| Unit | `LON` | `Lon` | same | same | Profile map resolves; no existing DB row (new master) | 0 current candidates |
| Unit | `THUNG` | `Thùng` | same | same | Profile map resolves; no existing DB row (new master) | 0 current candidates |
| Unit | `BAO` | `Bao` | same | same | Profile map resolves; no existing DB row (new master) | 0 current candidates |
| Unit | `KG` | `Kilogram` | same | same | Profile map resolves; no existing DB row (new master) | 0 current candidates |
| Unit | `RAM` | `Ram giấy` | same | same | Profile map resolves; no existing DB row (new master) | 0 current candidates |
| Unit | `PAL` | `Pallet` | same | same | Profile map resolves; no existing DB row (new master) | 0 current candidates |
| Warehouse | `KHO-HCM-01` | `Kho Bình Tân` | `tbl_DM_Kho.Ten_Kho` | `nvarchar(255)`, `SQL_Latin1_General_CP1_CI_AS`, unique | No; Warehouse table is empty | No rows to be ambiguous |
| Warehouse | `KHO-HCM-02` | `Kho Cát Lái` | same | same | No | No rows to be ambiguous |
| Warehouse | `KHO-BD-01` | `Kho Dĩ An` | same | same | No | No rows to be ambiguous |
| Warehouse | `KHO-DN-01` | `Kho Đà Nẵng` | same | same | No | No rows to be ambiguous |

These codes are map inputs, not persisted IDs. The Unit map/name agreement is exercised by the configured-profile fixture tests; a pre-existing DB row is not required to create a new Unit. The actual DB has no Unit or Warehouse records to resolve as existing references before import; Product, Category, and Supplier reference tables are also empty.

### Blocking parity mismatch

- Live Unit column and unique index are accent-sensitive (`SQL_Latin1_General_CP1_CI_AS`): a read-only SQL comparison returns `Cuốn != Cuộn`.
- The checked-in `F2016_sp_ins_Don_Vi_Tinh` source uses `SQL_Latin1_General_CP1_CI_AS` in its duplicate guard.
- The deployed database procedure instead uses `Latin1_General_CI_AI`; a read-only SQL comparison under that collation returns `Cuốn == Cuộn`.
- The deployed list procedure body matches the checked-in source; the deployed insert-procedure body does not. The only observed body difference is the collation in the duplicate predicate.
- The C# Unit validator/revalidator preserves Vietnamese diacritics. Therefore Preview/Confirm can treat an accent-distinct name as new while the deployed insert SP rejects it as a duplicate. This is `DB_PARITY_MISMATCH`; Confirm was not attempted. No stored procedure was changed.

## 4. Unit Import / Duplicate / Revalidation

- Source path is Upload → `CUnit_Xlsx_Reader` → profile detector → Unit validator → Preview; Confirm rechecks Add permission, reloads Units, revalidates, then calls the existing batch controller and `F2016_sp_ins_Don_Vi_Tinh` in one transaction.
- The default runtime reader constructs `CImportProfileProvider`; it loads the embedded `excel-import-profiles.json` resource. The normal-reader and configured-profile paths are exercised by the passing test run.
- Source tests show exact duplicates are skipped and same-name/different-note rows are rejected. An all-duplicate preview has no new valid rows, so Confirm is disabled.
- The customer Unit fixture's validator test is Confirm-eligible against an empty in-memory Unit list. Live UI Preview, Confirm, duplicate re-import, conflict mutation, and Confirm-time concurrency revalidation were **not performed** because the deployed SP parity gate failed first.
- No Unit rows were persisted; before/after DB row count is 0.

## 5. Permission, Preview, Transaction, and Audit

- Source checks Add permission at both Preview and Confirm. Runtime user permission was **NOT_VERIFIED** because browser UAT was unavailable.
- Preview source path loads master lists and validates in memory; it has no business-table write call. Tests are unit tests, not runtime proof of Preview zero-write.
- Source Unit Confirm uses one outer SQL transaction and rolls back the batch on an insert/commit exception. Live rollback behavior was **NOT_VERIFIED** because Confirm was not run.
- No separate action-history call was found on the Unit import service/controller path. Insert parameters carry the current user/function into the master audit columns; persisted audit behavior was **NOT_VERIFIED**.
- No open database transaction remained; no DML trigger was disabled.

## 6. Other Workbook Status

| Workbook | Preview evidence | Can Confirm | Persist attempted | DB rows before/after | Result / blocker |
| --- | --- | --- | --- | --- | --- |
| Unit | Fixture-backed validator tests only; not live UI | Yes in fixture test | No | 0 / 0 | Stopped at Unit procedure collation mismatch |
| Warehouse | Fixture-backed tests only; not live UI | No | No | 0 / 0 | Suspended status plus region/address blockers |
| Category | Fixture-backed tests only; not live UI | No | No | 0 / 0 | Enabled LOT/HSD blockers |
| Product | Fixture-backed tests only; not live UI | No | No | 0 / 0 | UOM/conversion/supplier/barcode/LOT/expiry blockers |
| Supplier | Fixture-backed tests only; not live UI | No | No | 0 / 0 | Tax code and payment terms blockers |
| Inbound | In-memory-reference Preview tests only | No; persistence blocked | No | Receipt 0/0 | Ordered 40 vs received 39 and unsupported fields remain blocked |
| Outbound | In-memory-reference Preview tests only | No; persistence blocked | No | Issue 0/0 | Destination/reason/reference/recipient/line note/LOT/HSD/location remain blocked |

The non-Unit workbook cases were not uploaded through a running UI. Counts stayed unchanged; no blocked Confirm was attempted.

## 7. Browser UAT

`BROWSER_UAT_NOT_PERFORMED`. The CUA browser had no open tabs and no application listener was found on the checked development ports (including the previously used 15073). The web host was not started for this phase; `Program.cs` registers nine hosted services, whose side effects were not part of the authorized read-only parity check.

## 8. Tests

- Non-database Excel import regression: **126 passed, 0 failed, 0 skipped**. This includes the prior 116-test regression scope plus additional reader/stream tests.
- Integration/runtime DB tests were not run. They are distinct from the unit tests and do not establish live UI E2E evidence.
- No browser UAT, live Preview, Confirm, or rollback test was run.

## 9. Database and Workbook Change Proof

- No `ALTER`, `CREATE`, migration, stored-procedure deployment, `INSERT`, `UPDATE`, `DELETE`, or `TRUNCATE` was executed.
- `WarehouseModule.Schema.sql` SHA-256: `62B20B66E7581C391A41B1CEE5644E70C525B2BAC460222C6DD7B6222AEFDD60`.
- `WarehouseModule.Procedures.sql` SHA-256: `75ABD0B4A15445F57D983E5D6061E5DE54E44C5672E16C519C7FBAE880B5A377`.
- All seven customer XLSX fixture hashes match the prior phase values; no workbook was opened for write or saved.
- No source code was changed in this phase; this report is the only new artifact.

## 10. Remaining Blockers

1. Resolve the deployed/source parity for `F2016_sp_ins_Don_Vi_Tinh` under a separately approved database change. This phase did not modify the procedure because the task forbids procedure changes to make import pass.
2. After parity is formally aligned, rerun the Unit original through the normal browser Preview/Confirm path, then re-import it to verify duplicate skips and run a controlled conflict/revalidation scenario.
3. Run Browser UAT, runtime permission checks, actual DB re-query/audit verification, and runtime rollback evidence.
4. Continue validating blocked master and transaction Preview through the UI when a safe app host is available; keep all unsupported capabilities blocked.
