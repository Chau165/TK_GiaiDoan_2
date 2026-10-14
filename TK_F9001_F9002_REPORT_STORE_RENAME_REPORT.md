# F9001/F9002 Report and Inventory Store Rename Report

**Status: `F9001_F9002_REPORT_RENAME_PARTIAL`**

The source rename and creation/verification of all seven new live procedures are complete. The seven old procedures remain temporarily because the running Web process (PID 3796) still holds the Web output assemblies; its loaded caller version is not verified, so removing the old procedures could break requests from that process.

## Approved mapping

| Old procedure and method | New procedure and method | Function |
|---|---|---:|
| `sp_BC_Ton_Kho_Hien_Tai_Page` | `F9001_sp_Ton_Kho_Hien_Tai_Page` | 9001 — Tồn kho |
| `sp_BC_Chi_Tiet_Nhap` | `F9002_sp_Chi_Tiet_Nhap` | 9002 — Báo cáo |
| `sp_BC_Chi_Tiet_Xuat` | `F9002_sp_Chi_Tiet_Xuat` | 9002 — Báo cáo |
| `sp_BC_Chi_Tiet_Nhap_Page` | `F9002_sp_Chi_Tiet_Nhap_Page` | 9002 — Báo cáo |
| `sp_BC_Chi_Tiet_Xuat_Page` | `F9002_sp_Chi_Tiet_Xuat_Page` | 9002 — Báo cáo |
| `sp_BC_Xuat_Nhap_Ton` | `F9002_sp_Xuat_Nhap_Ton` | 9002 — Báo cáo |
| `sp_BC_Xuat_Nhap_Ton_Page` | `F9002_sp_Xuat_Nhap_Ton_Page` | 9002 — Báo cáo |

`Xuat_Nhap_Ton` remains under function 9002 as approved. No action suffixes were invented.

## Source and caller changes

- Renamed the seven public methods in `TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs`; each now calls the Stored Procedure with the identical new name. Parameters, order, defaults, result mapping, paging, exception flow, and disposal logic were preserved.
- Renamed only the seven procedure declarations in `Database/WarehouseModule.Procedures.sql`. The source diff is limited to those declaration names; procedure bodies and projections are unchanged.
- Updated callers and SQL references in 42 other active code, test, benchmark, and SQL files (197 old-name occurrences), plus the controller's seven declarations and seven Store literals. That is 43 source/caller files in total: 27 C# files, one Razor file, 13 SQL files, and two PowerShell benchmark scripts.
- Updated current-inventory use in the Excel import service to `F9001_sp_Ton_Kho_Hien_Tai_Page`; its import logic and paging inputs are unchanged.
- A repository scan found zero executable old-name method or Store call sites in active `.cs`, `.razor`, `.sql`, and `.ps1` files. Old names remain in four unchanged exception-message labels, the test that rejects old procedure declarations, the rollback artifact, and historical reports/results; these are not call sites.
- No DM or XNK controller, technical inventory/reservation procedure, Excel profile, cache, or previous DM/XNK implementation work was changed by this rename.

## Database deployment and retirement

- Pre-DDL `SELECT DB_NAME()` returned exactly `TKS_Thuc_Tap_V11_GiaiDoan2`.
- Before creating the new objects, the old live procedure bodies matched the pre-rename canonical source for all seven Stores after normalizing the procedure header and ignoring comments outside the declaration.
- Created all seven new procedures in `TKS_Thuc_Tap_V11_GiaiDoan2` from the renamed canonical source. Post-deployment source/live body parity passed 7/7.
- Reapplied the existing direct `GRANT EXECUTE` to `[Warehouse_Application]` on each new procedure. Live metadata confirms one matching grant for each new procedure.
- The old procedures still exist. Live SQL dependency metadata reports zero SQL-module dependencies on each old name. Repository source call sites are also zero, but the running Web process may still hold callers compiled against the old names. The old procedures are therefore retained as temporary compatibility objects until that application process is restarted/deployed and old runtime callers are confirmed absent.
- The benchmark connection pointed to `TKS_Thuc_Tap_V11_Perf_10000000` and was not used for DDL. The target database was reached and verified separately before deployment.

## Read-only result and authorization verification

Executed each old/new procedure pair against the target DB with a valid application login and one authorized warehouse, for September 2026. All executions completed without SQL errors; no business data was written. For each pair, result-set count, column names, SQL types, row count, values, and row sequence matched.

| New procedure(s) | Live result contract | Sample rows |
|---|---|---:|
| `F9001_sp_Ton_Kho_Hien_Tai_Page` | Result 1: `Total_Count int`; result 2: 12 inventory columns | 1 count row; 0 page rows |
| `F9002_sp_Chi_Tiet_Nhap`, `F9002_sp_Chi_Tiet_Xuat` | One result set with 8 detail columns | 0 rows each |
| `F9002_sp_Chi_Tiet_Nhap_Page`, `F9002_sp_Chi_Tiet_Xuat_Page` | Result 1: `Total_Count int`; result 2: the same 8 detail columns | 1 count row and 0 page rows each |
| `F9002_sp_Xuat_Nhap_Ton` | One result set with 12 inventory columns | 0 rows |
| `F9002_sp_Xuat_Nhap_Ton_Page` | Result 1: `Total_Count int`; result 2: the same 12 inventory columns | 1 count row and 0 page rows |

The 8 detail columns are `Ngay`, `So_Phieu`, `Nha_Cung_Cap`, `Ma_San_Pham`, `Ten_San_Pham`, `So_Luong`, `Don_Gia`, and `Tri_Gia`. The 12 inventory columns are `Kho_ID`, `Ten_Kho`, `San_Pham_ID`, `Ma_San_Pham`, `Ten_San_Pham`, `SL_Dau_Ky`, `SL_Nhap`, `SL_Xuat`, `SL_Cuoi_Ky`, `SL_Ton_Thuc_Te`, `SL_Dang_Giu`, and `SL_Kha_Dung`.

All seven sample calls returned no page/detail rows, so the live run verifies empty result contracts and equality between old/new names; it does not observe nonempty row values or ordering. The complete procedure bodies match source, preserving their filters, authorization predicates, ordering, and inventory logic. The current-stock and historical report paging procedures each returned the expected two result sets.

## Tests and builds

- TDD RED: three report rename contract tests failed against the old method, caller, and procedure names.
- TDD GREEN: `WarehouseReportRefactorTests` and `WarehouseUiWorkflowTests` passed 30/30.
- Additional static regression contracts, `WarehouseGroupCDesharingContractTests` and `WarehousePhase10_2P101ContractTests`, passed 14/14.
- Combined focused regression result: 44/44 tests passed. The full integration suite was not run because it includes data-mutating tests and no isolated test database was established.
- Coverage percentage was not measured: this rename changes no executable business logic, and the full integration suite was intentionally not run against the non-isolated target DB.
- Builds passed with zero compile errors for Data Access, Web Danh Muc, Data Access Tests, Benchmarks, Benchmarks V2, Benchmarks V22, Benchmark Lite, and Q2 Qualification.
- The normal Web build could not copy assemblies because the running Web process locks them. Building the Web project to an isolated temporary output with project-reference rebuilding disabled passed with zero compile errors; the temporary output was removed. The running process was left untouched.
- `git diff --check` exited successfully. Git emitted LF-to-CRLF advisories for existing working-copy files; it reported no whitespace errors.

## Rollback artifact

[TK_F9001_F9002_REPORT_STORE_RENAME_ROLLBACK.sql](TK_F9001_F9002_REPORT_STORE_RENAME_ROLLBACK.sql) contains the seven original procedure definitions from the pre-rename canonical source and preserves their observed `ANSI_NULLS` and `QUOTED_IDENTIFIER` settings. It checks the target database and keeps the new procedures by default; its final flag only drops new names after application rollback is confirmed.

## Final confirmations

| Confirmation | Result |
|---|---|
| New F9001 Store created, granted, and source/live verified | PASS |
| Six new F9002 Stores created, granted, and source/live verified | PASS |
| Report method names equal their Store names | PASS |
| Function 9001 and 9002 mappings are correct | PASS |
| Active old-name source call sites | 0 |
| Old-name runtime callers | NOT VERIFIED; running Web process remains active |
| Result schema and paging contracts | PASS; sample data rows were empty |
| Authorization and import source behavior | Preserved by body-equivalent Store creation and caller-only rename |
| Previous DM/XNK work | Preserved |
| Old Stored Procedure retirement | PENDING safe application restart/deployment |
| Final status | `F9001_F9002_REPORT_RENAME_PARTIAL` |
