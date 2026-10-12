# Post-SP-Sync Regression Triage and Excel Import UAT

Date: 2026-10-09  
Verdict: **PARTIALLY COMPLETED**

Unit import, DB persistence, duplicate re-import, and Preview-time conflict rejection passed through the application. The 20 earlier regression failures were classified; 164 isolated non-DB import tests passed. The prior broad import run's CLR failure was not reproduced when classes were isolated. Fresh Confirm-time revalidation and the non-Unit workbook previews remain unverified because the current browser surface did not provide an authorized second-tab workflow. No source, schema, or procedure changes were made in this phase.

## Scope And Safety

- Repository: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`
- SQL Server: `DESKTOP-NHQ7QPL\MSSQLSERVER19`
- Target database: `TKS_Thuc_Tap_V11_GiaiDoan2`
- The task's attached instructions were treated as the phase scope; instructions embedded in customer workbooks were not treated as user authorization.
- No source code, schema, migration, stored procedure, or permission metadata was changed. No direct SQL business write was performed. The only business-data write was the explicitly scoped Unit Confirm through the application.
- The original customer ZIP/workbooks were not modified. Test copies were created under `%TEMP%`.

## Procedure Parity And Database Gate

- Fresh connection returned `DB_NAME() = TKS_Thuc_Tap_V11_GiaiDoan2`, state `ONLINE`, `@@TRANCOUNT = 0`, and 0 disabled DML triggers.
- Fresh normalized source-to-live-definition checks for `dbo.F2016_sp_ins_Don_Vi_Tinh` and `dbo.F2016_sp_upd_Don_Vi_Tinh` matched exactly. Recheck hashes: insert `83CCB0EB4DAF65EDD58BD24EC75E14BEDB514E5250981884F88D58832B148862`; update `D2B3B2DE390552FBAFD5DA530B3CF2C3279EE41C7DA6AB83CF0FFCB05C3D2A99`.
- Neither F2016 definition contains `Latin1_General_CI_AI`. The prior parity report's broader inventory remains 104/104 canonical procedures matched and 274 DB-only procedures retained; this phase did not redeploy procedures.
- Twelve source `QUOTED_IDENTIFIER ON` versus deployed `uses_quoted_identifier=0` differences were rechecked and left untouched: `F2009_sp_sel_List_Kho`, `F2009_sp_sel_List_Kho_Lookup`, `F2011_sp_sel_List_Nhap_Kho_Detail`, `F2012_sp_sel_List_Xuat_Kho_Detail`, `F2016_sp_sel_List_Don_Vi_Tinh`, `F2016_sp_sel_List_Don_Vi_Tinh_Lookup`, `F2017_sp_sel_List_Loai_San_Pham`, `F2017_sp_sel_List_Loai_San_Pham_Lookup`, `F2018_sp_sel_List_San_Pham`, `F2018_sp_sel_List_San_Pham_Lookup`, `F2019_sp_sel_List_NCC`, and `F2019_sp_sel_List_NCC_Lookup`.
- Per the parity audit, their normalized bodies match; no double-quoted identifiers or dynamic SQL dependency was found. This is a non-blocking metadata variance, not a reason to redeploy.

## The 20 Regression Failures

All failures came from `TKS_Thuc_Tap_V11_Data_Access.Tests`. Classification counts: **11 `TEST_ENVIRONMENT_FAILURE`, 6 `UNRELATED_PRE_EXISTING_FAILURE`, 3 `TEST_DATA_DEPENDENCY`, 0 import or SP-sync regression**. No test expectation or production code was changed.

Flags in the table are `DB/write`, `Import`, `Warehouse`, `Pagination`, and `History`. `N` means no; `Y` means yes; guarded writes were not reached.

| Test | DB/write | Import | Warehouse / paging / history | Failure evidence and cause | Classification and action |
|---|---|---|---|---|---|
| `Stable_historical_result_is_equal_when_switching_legacy_and_group_modes` (`WarehouseHistoricalGroupModeTests`) | Y / guarded | N | Y / Y / Y | `$XunitDynamicSkip$Set TKS_PERF06D_REHEARSAL_CONNECTION_STRING...`; environment variable absent, test did not reach DB setup. | `TEST_ENVIRONMENT_FAILURE`; do not point it at the business DB. |
| `Stable_nonpaged_historical_result_is_equal_when_switching_modes` (`WarehouseHistoricalGroupModeTests`) | Y / guarded | N | Y / N / Y | Same missing PERF-06D rehearsal connection guard. | `TEST_ENVIRONMENT_FAILURE`; no DB test run. |
| `Group_report_holds_one_root_and_sorted_distinct_group_locks_without_scope_locks` (`WarehouseHistoricalGroupModeTests`) | Y / guarded | N | Y / N / Y | Same missing rehearsal connection guard. | `TEST_ENVIRONMENT_FAILURE`; no DB test run. |
| `Legacy_report_holds_one_shared_scope_lock_per_distinct_scope` (`WarehouseHistoricalGroupModeTests`) | Y / guarded | N | Y / N / Y | Same missing rehearsal connection guard. | `TEST_ENVIRONMENT_FAILURE`; no DB test run. |
| `New_warehouse_after_group_discovery_fails_closed_without_late_group_acquisition` (`WarehouseHistoricalGroupModeTests`) | Y / guarded | N | Y / N / Y | Same missing rehearsal connection guard; isolated rerun emitted `$XunitDynamicSkip$` as FAIL under the current adapter. | `TEST_ENVIRONMENT_FAILURE`; xUnit skip reporting mismatch, not a product assertion. |
| `Empty_range_to_non_empty_race_fails_closed` (`WarehouseHistoricalGroupModeTests`) | Y / guarded | N | Y / N / Y | Same missing rehearsal connection guard. | `TEST_ENVIRONMENT_FAILURE`; no DB test run. |
| `Group_conflict_fails_closed_and_public_report_cleans_its_transaction` (`WarehouseHistoricalGroupModeTests`) | Y / guarded | N | Y / N / Y | Same missing rehearsal connection guard. | `TEST_ENVIRONMENT_FAILURE`; no DB test run. |
| `Missing_fence_config_fails_closed_with_51450_and_restores_state` (`WarehouseHistoricalGroupModeTests`) | Y / guarded | N | Y / N / Y | Same missing rehearsal connection guard. | `TEST_ENVIRONMENT_FAILURE`; no DB test run. |
| `Invalid_fence_config_value_fails_closed_with_51450_and_restores_state` (`WarehouseHistoricalGroupModeTests`) | Y / guarded | N | Y / N / Y | Same missing rehearsal connection guard. | `TEST_ENVIRONMENT_FAILURE`; no DB test run. |
| `Unknown_fence_config_mode_fails_closed_with_51450_and_restores_state` (`WarehouseHistoricalGroupModeTests`) | Y / guarded | N | Y / N / Y | Same missing rehearsal connection guard. | `TEST_ENVIRONMENT_FAILURE`; no DB test run. |
| `Duplicate_fence_config_cardinality_fails_closed_and_restores_state` (`WarehouseHistoricalGroupModeTests`) | Y / guarded | N | Y / N / Y | Same missing rehearsal connection guard. | `TEST_ENVIRONMENT_FAILURE`; no DB test run. |
| `Don_Vi_Tinh_controller_dispatches_by_zero_or_nonzero_Auto_ID` (`WarehouseGroupBSplitContractTests`) | N / N | N | Y / N / N | `Assert.Matches` expects old `case "DonViTinh": if (...)` source shape; controller uses a switch expression with equivalent zero/nonzero insert/update dispatch. | `UNRELATED_PRE_EXISTING_FAILURE`; stale source-shape assertion. |
| `Kho_controller_dispatches_by_zero_or_nonzero_Auto_ID` (`WarehouseGroupBSplitContractTests`) | N / N | N | Y / N / N | Same test regex expects old switch statement; controller uses switch expression. | `UNRELATED_PRE_EXISTING_FAILURE`; no production change. |
| `Loai_San_Pham_controller_dispatches_by_zero_or_nonzero_Auto_ID` (`WarehouseGroupBSplitContractTests`) | N / N | N | Y / N / N | Same stale switch-statement assertion. | `UNRELATED_PRE_EXISTING_FAILURE`; no production change. |
| `San_Pham_controller_dispatches_by_zero_or_nonzero_Auto_ID` (`WarehouseGroupBSplitContractTests`) | N / N | N | Y / N / N | Same stale switch-statement assertion. | `UNRELATED_PRE_EXISTING_FAILURE`; no production change. |
| `NCC_controller_dispatches_by_zero_or_nonzero_Auto_ID` (`WarehouseGroupBSplitContractTests`) | N / N | N | Y / N / N | Same stale switch-statement assertion. | `UNRELATED_PRE_EXISTING_FAILURE`; no production change. |
| `Paged_inventory_and_detail_reports_preserve_count_rows_and_warehouse_scope` (`WarehouseReportPagingOptimizationTests`) | Y / transaction rollback | N | Y / Y / Y | Insert failed at FK `FK_tbl_XNK_Nhap_Kho_NCC`; the cleaned business DB has no Supplier master row. Fixture uses a SQL transaction and `finally` calls `RollbackAsync()`. | `TEST_DATA_DEPENDENCY`; no import/SP regression. |
| `Selecting_a_warehouse_restricts_reports_and_rejects_unassigned_warehouse` (`WarehouseReportPagingOptimizationTests`) | Y / transaction rollback | N | Y / Y / Y | Same supplier FK failure due to empty master fixture; writes are wrapped and rolled back in `finally`. | `TEST_DATA_DEPENDENCY`; no import/SP regression. |
| `Warehouse_permission_page_only_exposes_the_warehouse_user_assignment` (`WarehouseNavigationTests`) | N / N | N | Y / N / N | Assertion searches for `m_strMaster_Type = m_bWarehouse_Permission_Only`; current `OnParametersSet` assigns `"KhoUser"` when the permission-only flag is true. | `UNRELATED_PRE_EXISTING_FAILURE`; runtime route behavior is correct; test expects obsolete text. |
| `Master_list_reads_bigint_ids_and_int_zero_placeholders` (`WarehouseModuleStructureTests`) | Y / read-only | N | Y / N / N | `Assert.Contains` expected Unit `Cái`, but DB query returned an empty collection after the intentional business-data reset. | `TEST_DATA_DEPENDENCY`; test fixture assumption, no source change. |

The remaining 283 tests in the prior filtered run passed. The failure set does not touch Excel import code or the two synchronized F2016 procedures. The two report tests were source-reviewed before execution; their transactional fixture writes roll back on both success and exception paths. The 11 rehearsal tests were not connected to any DB because the required variable was absent.

## CLR Failure And Import Test Gate

- Runtime evidence: .NET SDK `9.0.318`, MSBuild `17.14.51`, target/runtime `.NET 8.0.20`, VSTest `17.14.1`, xUnit adapter `2.8.2+699d445a1a`; assembly parallelization is disabled (`MaxParallelThreads = 1`).
- A previous broad import-filtered `dotnet test` invocation terminated with `Internal CLR error (0x80131506)` in `Microsoft.Build.CommandLine.MSBuildApp.Main`, before testhost discovery. No test class had started. The retained process output did not preserve the full filter expression or memory measurement; therefore neither is reconstructed here.
- Each non-DB import test class was then run as a separate `dotnet vstest` process. The crash did not recur: **28 classes, 164 passed, 0 assertion failures, 0 isolated-process crashes**. Coverage included readers/parser, structure detection, value mapping, compatibility, validation, UI, authorization, database-safety guards, transaction preview, legacy headers, Telerik runtime, and stored-procedure deployment contract.
- Per-class results:

| Class | Passed |
|---|---:|
| `CategoryXlsxReaderTests` | 2 |
| `CategoryExcelImportValidationTests` | 10 |
| `CategoryExcelImportUiTests` | 3 |
| `CategoryExcelImportAuthorizationTests` | 2 |
| `ImportValueMappingTests` | 4 |
| `ImportStructureDetectionTests` | 18 |
| `ProductExcelImportAuthorizationTests` | 3 |
| `ProductExcelImportUiTests` | 6 |
| `ProductExcelImportValidationTests` | 27 |
| `ProductImportDatabaseSafetyTests` | 4 |
| `StreamCopyBehaviorTests` | 1 |
| `SupplierExcelImportAuthorizationTests` | 2 |
| `SupplierExcelImportValidationTests` | 10 |
| `SupplierExcelImportUiTests` | 3 |
| `SupplierXlsxReaderTests` | 2 |
| `TelerikSpreadImporterRuntimeTests` | 2 |
| `UnitExcelImportValidationTests` | 15 |
| `UnitExcelImportUiTests` | 3 |
| `UnitExcelImportAuthorizationTests` | 2 |
| `TransactionImportPreviewTests` | 8 |
| `WarehouseExcelImportUiTests` | 4 |
| `WarehouseExcelImportValidationTests` | 9 |
| `WarehouseExcelImportAuthorizationTests` | 2 |
| `UnitXlsxReaderTests` | 3 |
| `WarehouseXlsxReaderTests` | 2 |
| `XlsxParserCompatibilityTests` | 12 |
| `LegacyExcelHeaderValidationTests` | 3 |
| `WarehouseProcedureDeploymentTests` | 2 |

- Five `*ImportRuntimeIntegrationTests` classes (Unit, Product, Category, Supplier, Warehouse) were not run: `TKS_INTEGRATION_CONNECTION_STRING` was absent and the designated approved Test DB was not enabled. They were not pointed at the Business DB. This is `SKIPPED_FOR_DB_SAFETY` (five classes; no aggregate test count recorded).
- The broad-run CLR failure is classified as `TEST_RUNNER_INFRASTRUCTURE / PROCESS_STABILITY`, not an import product regression: it happened before tests started, while every safe non-DB import class passed in isolation. The underlying CLR/MSBuild fault itself remains unexplained; no dump or process-memory sample was captured.
- Build: `dotnet build TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore -v:q -clp:ErrorsOnly` succeeded with 0 errors and 12 warnings (existing Telerik/licensing warnings).

## Live Unit UAT

The app ran locally in Development at `http://127.0.0.1:15074/Kho/Don_Vi_Tinh` so bundled Telerik assets loaded. The successful import used the customer workbook `02_Don_vi_tinh_KH.xlsx` through the UI; no SQL was used for persistence.

| Step | Result |
|---|---|
| Original workbook Preview | PASS: 10 rows, 10 valid, 0 skipped, 0 errors; Confirm enabled. Three business fields mapped; four unsupported ancillary fields were ignored. |
| Confirm | PASS: UI reported `Đã nhập thành công 10 đơn vị tính.` |
| DB re-query | PASS: 10 expected names; notes match where present; no duplicate names. Names: Bao, Cái, Chai, Gói, Hộp, Kilogram, Lon, Pallet, Ram giấy, Thùng. |
| Duplicate re-import of original | PASS: 10 duplicate skips, 0 valid, 0 errors; Confirm disabled; DB count remained 10. |
| Conflict Preview using a temporary copy | PASS: changed the note for existing Unit `Cái`; Preview reported `Tên đơn vị tính 'Cái' trùng khóa nhưng dữ liệu khác trong hệ thống.`, 1 error and 9 duplicate skips; Confirm disabled. No write attempted. |
| Accent-sensitive identity | PASS in `UnitExcelImportValidationTests.Preview_uses_case_insensitive_accent_sensitive_unit_identity`: `Cuốn` and `Cuộn` remain distinct. The customer workbook contains no such pair. |
| Fresh Confirm revalidation after a concurrent change | NOT RUN: a second tab opened read-only and did not expose Add/Import actions. No authorization/session workaround or direct SQL write was used. |
| Transaction atomicity under injected mid-batch failure | NOT VERIFIED: no safe controlled failure fixture was available on the Business DB. |

## Blocked Workbook And Transaction Previews

These are **NOT RUN**, not presumed passed. A second in-app browser tab for the same app route exposed only the read-only list and not Add/Import actions. The current authorized tab remained on the Unit route; no supported authenticated route transition to the other import screens was available in this browser surface. I did not attempt Confirm for any blocked workbook.

| Workbook | Parse / mapping / validation / Preview | CanConfirm | Confirm attempted | DB impact evidence |
|---|---|---|---|---|
| `01_Danh_muc_Kho_KH.xlsx` | NOT RUN live; prior safe-loss audit expects suspended-status and region/address blockers. | NOT VERIFIED | No | No live workbook-specific before/after comparison; final Warehouse count remains 0. |
| `03_Loai_san_pham_KH.xlsx` | NOT RUN live; prior audit expects LOT/HSD semantic blockers. | NOT VERIFIED | No | No live workbook-specific before/after comparison; final Category count remains 0. |
| `04_San_pham_KH.xlsx` | NOT RUN live; prior audit expects genuine UOM/conversion/supplier/barcode/LOT/expiry blockers. | NOT VERIFIED | No | No live workbook-specific before/after comparison; final Product count remains 0. |
| `05_Nha_cung_cap_KH.xlsx` | NOT RUN live; prior audit expects tax-code/payment-term classification blockers. | NOT VERIFIED | No | No live workbook-specific before/after comparison; final Supplier count remains 0. |
| `06_Nhap_kho_Thang_09_2026_KH.xlsx` | NOT RUN live; previous evidence identifies ordered/received quantity `40 / 39` and unsupported fields. | NOT VERIFIED | No | Receipt header/raw counts remain 0; no Preview claim. |
| `07_Xuat_kho_Thang_09_2026_KH.xlsx` | NOT RUN live; prior policy marks persistence blocked. | NOT VERIFIED | No | Issue header/raw counts remain 0; no Preview claim. |

The final zero counts are corroborating safety evidence only; because these six workbook Previews were not run in this phase, they do not prove Preview zero-write or current `CanConfirm=false` behavior.

## Database Counts After UAT

Fresh read-only counts on `DESKTOP-NHQ7QPL\MSSQLSERVER19 / TKS_Thuc_Tap_V11_GiaiDoan2` after the Unit conflict Preview:

| Entity/table | Rows |
|---|---:|
| Unit | 10 |
| Warehouse / WarehouseUser | 0 / 0 |
| Category / Supplier / Product | 0 / 0 / 0 |
| Receipt header / raw rows | 0 / 0 |
| Issue header / raw rows | 0 / 0 |
| Inventory current / movement daily / balance daily / snapshot daily | 0 / 0 / 0 / 0 |
| Reconciliation result / run | 0 / 0 |
| Bootstrap audit / worker heartbeat | 0 / 0 |
| Snapshot fallback log | 4 (unchanged) |

No other business/inventory row change was observed. DB remained ONLINE; current-session `@@TRANCOUNT=0`; disabled DML triggers `0`.

## Customer Workbook Hashes

The 7 XLSX entries extracted from the customer ZIP matched the previously recorded hashes. The archive and its original entries were not edited.

| Workbook | SHA-256 |
|---|---|
| `01_Danh_muc_Kho_KH.xlsx` | `DF48CA93DA318289438A5F11C4B8E53513CE0F6D34D6AB98385FE71F41B2E2DA` |
| `02_Don_vi_tinh_KH.xlsx` | `95BFB88E4ABF9C1D82235E5AD3E80EEC91AEB8B2835F0631978BE5BC5BA958C6` |
| `03_Loai_san_pham_KH.xlsx` | `7EE3C901936CE56F24B8FD49F5925636E407675D34BEB6D5F78FFFF22B1B6934` |
| `04_San_pham_KH.xlsx` | `61E2642E5F98C4974601932055377D93403F3D1A26945BE6F0573A00CB337807` |
| `05_Nha_cung_cap_KH.xlsx` | `EC2AA0497CE36EFB653EF9935D532AD368D127C06995EF787C881C934821B446` |
| `06_Nhap_kho_Thang_09_2026_KH.xlsx` | `11689B98548FA3E33180725370F42B56211A28AC3D988289F6C7AA7CF176BAE7` |
| `07_Xuat_kho_Thang_09_2026_KH.xlsx` | `9823C5138226D3843D93900BDFFD707D31734B68A0907418086D889706787B07` |

## Status Counts And Remaining Work

- Import tests (isolated non-DB): **PASS 164 / FAIL 0 / CRASH 0**; runtime integration: **SKIPPED_FOR_DB_SAFETY, 5 classes**. Prior broad import-filter attempt: **one process-level CLR crash**, no test-level count.
- Regression filter: **283 passed / 20 failed**; all 20 classified above, with no import or SP-sync regression identified.
- DB-backed tests: warehouse paging tests **RUN 2**, both failed from absent Supplier fixture and rolled back; warehouse structure test **RUN 1** read-only, failed because Unit fixture was empty; rehearsal integration tests **not reached due environment guard**; 5 import runtime integration classes **skipped for DB safety**.
- Live UAT: **PASS** Unit original Preview/Confirm/DB re-query, duplicate re-import, and Preview-time conflict rejection; **FAIL 0**; **NOT RUN** stale Confirm revalidation, injected rollback, four blocked master Previews, Inbound Preview, and Outbound Preview.
- Source/schema/procedure changes: **none**. Original workbook changes: **none**. Direct inventory writes: **none**.
- Remaining gate: rerun the six non-Unit workbooks in the same Add-authorized UI session and complete the supported-path concurrency/revalidation test before declaring this phase complete. Do not use the Business DB as the import integration-test database.
