# Product Excel Import Phase 1B - Final Runtime Verification

Date: 2026-10-08

## 1. Executive Summary

**Verdict: `PRODUCT_IMPORT_PHASE1B_RUNTIME_VERIFIED`.**

The current-source Product Import service and database path passed the required TEST DB runtime gates: source parity is complete (12/12 objects), Preview made no writes, 100 rows committed, a real row-50 SQL failure rolled back the batch, the Preview-to-Confirm duplicate race was rejected without partial batch rows, and Unit resolution/CRUD followed the owner-approved CI_AS contract. Test data and temporary probe objects were cleaned. This verifies the service/database runtime, not full browser, identity-profile, Excel Desktop upload, or deployed-host UAT.

## 2. Owner Authorization

The task owner explicitly authorized forward DDL/DML, test data, rollback/race tests, and exact-prefix cleanup on `DESKTOP-NHQ7QPL\MSSQLSERVER19` / `TKS_Thuc_Tap_V11_C01C02_Test_20260906`. The owner explicitly kept `TKS_Thuc_Tap_V11_GiaiDoan2` read-only. No production deployment was attempted.

## 3. Source / Business / Test Roles

| Environment | Role | Result |
|---|---|---|
| Repository current source | Authoritative Phase 1B target contract | Used for all 12 deployed definitions |
| Business DB | Read-only deployed reference | F2016 remains CI_AI; expected `PRODUCTION_DEPLOYMENT_PENDING` |
| Approved Test DB | Forward-sync and disposable runtime target | Synced to source; runtime writes and cleanup verified |

## 4. Test DB Identity Guard

Before DDL/DML, the target resolved to exactly `@@SERVERNAME = DESKTOP-NHQ7QPL\MSSQLSERVER19` and `DB_NAME() = TKS_Thuc_Tap_V11_C01C02_Test_20260906`. The SQLCMD proposal contains the same fail-closed guard; integration operations used the existing `ProductImportTestDatabase` allowlist and live identity check. Connection details used Windows Integrated Security; no credentials are recorded.

## 5. Pre-Sync Test DB State

The TEST DB was `ONLINE` and `READ_WRITE`, with 84 Products. Category `96/TP`, Unit `82/Cái`, Unit `205/Cuộn` (3 Product references), and Unit `211/Cuốn` (1 reference) were reverified. Product/Category/Unit column lengths, audit columns, collations, unique indexes, and enabled/trusted Product foreign keys matched source. All 12 required functions/procedures in Section 7 were absent; `sp_DM_Master_Page` and `sp_DM_Lookup_Page` existed and were already source-aligned. No schema/index/FK change was necessary.

## 6. Test DB Sync Execution

The existing proposal was parsed and normalized against current `Database/WarehouseModule.Procedures.sql`; all 12 object bodies matched before execution. The first SQLCMD invocation used the default input code page. Post-sync hashing caught mojibake in the Vietnamese `THROW` messages of the two F2016 procedures and F2018 Product insert before any runtime test began. The same idempotent `CREATE OR ALTER` proposal was rerun with UTF-8 input (`sqlcmd -f i:65001`); it completed successfully and all 12 normalized definitions then matched current source. No source change was made to accommodate the encoding issue.

## 7. Test DB Objects Changed

All deployed definitions below came from `Database/WarehouseModule.Procedures.sql`. Each object was missing before sync and is now present with the normalized current-source definition.

| Object | Action | Old state | New state / reason |
|---|---|---|---|
| `fn_Inventory_Fence_Root_Resource` | `CREATE OR ALTER` | Missing | Current-source function; F2016/F2018 fence dependency |
| `fn_Inventory_Fence_Mode_Satisfies` | `CREATE OR ALTER` | Missing | Current-source function; Root fence dependency |
| `sp_Inventory_Fence_Require_Transaction` | `CREATE OR ALTER` | Missing | Current-source procedure; transaction guard dependency |
| `sp_Inventory_Fence_Acquire_Root` | `CREATE OR ALTER` | Missing | Current-source procedure; Unit/Product write dependency |
| `F2016_sp_ins_Don_Vi_Tinh` | `CREATE OR ALTER` | Missing | Current-source CI_AS Unit insert contract |
| `F2016_sp_upd_Don_Vi_Tinh` | `CREATE OR ALTER` | Missing | Current-source CI_AS Unit update contract |
| `F2016_sp_sel_List_Don_Vi_Tinh` | `CREATE OR ALTER` | Missing | Unit reference list for Preview |
| `F2016_sp_sel_List_Don_Vi_Tinh_Lookup` | `CREATE OR ALTER` | Missing | Required Unit lookup parity |
| `F2017_sp_sel_List_Loai_San_Pham` | `CREATE OR ALTER` | Missing | Category reference list for Preview |
| `F2017_sp_sel_List_Loai_San_Pham_Lookup` | `CREATE OR ALTER` | Missing | Required Category lookup parity |
| `F2018_sp_sel_List_San_Pham_Lookup` | `CREATE OR ALTER` | Missing | Required Product lookup parity |
| `F2018_sp_ins_San_Pham` | `CREATE OR ALTER` | Missing | Product Confirm insert contract |

No permanent table, column, index, FK, constraint, grant, or permission was changed. A uniquely named test-only transaction probe table and Product trigger were created to record session/transaction metadata for the 100-row Confirm, then both were dropped. Runtime DML created 100 success-test Products, one competing Product, and two temporary Units; all were deleted by exact binary prefix. The row-50 attempt rolled back. Final counts: 84 Products; zero Product/Unit rows for the run prefixes; probe table/trigger absent.

## 8. Post-Sync Parity

`REQUIRED_TEST_DB_PARITY: PASS (12/12)`. Every required function/procedure exists; all normalized module definitions match current source, including `F2018_sp_ins_San_Pham` and both F2016 CI_AS procedures. Procedure/function types, ANSI_NULLS and QUOTED_IDENTIFIER settings were verified. Catalog parameter metadata matched the F2016 write procedures, F2018 Product insert, and fence acquisition procedure; the remaining list/guard procedures are parameterless as in source. F2018 has the expected 10-parameter contract, including `@Auto_ID BIGINT OUTPUT`, Product code/name, Category/Unit IDs, note, and four audit values. Fence functions and procedures were also called successfully inside a rollback-only fence verification transaction.

| Object | Source / Business DB / Test DB |
|---|---|
| Product, Category, Unit tables/indexes/FKs | Source = Business = Test metadata |
| `F2018_sp_ins_San_Pham` | Source = Business = Test; normalized SHA-256 `503C99847CAABCA64DD6A84D27991A71F105AB991063DA4F7CE2B7206936DCAA` |
| `F2016_sp_ins_Don_Vi_Tinh` | Source = Test CI_AS; Business CI_AI; expected deployment pending |
| `F2016_sp_upd_Don_Vi_Tinh` | Source = Test CI_AS; Business CI_AI; expected deployment pending |
| Remaining 9 fence/list/lookup objects | Source = Test; present and callable where exercised |

Source/Test normalized SHA-256 values: F2016 insert `8AEC10143DC24E3592D3A7D216328B73D723F80D21B415DCB77E527980AD0417`; F2016 update `3C45E60ED2A80A58F25813B6E9715C51C94836DC51DA75638F8178F65EC37567`; F2018 Product insert as above.

## 9. Production Deployment Pending

The Business DB was queried read-only and was not modified. Its F2016 Unit insert/update procedures still use CI_AI while current source and the approved Test DB use CI_AS. Their normalized Business hashes remain insert `173EAE5607F40E1789BAE4D805434D32402230C4498ED466D6B133D224190949` and update `B82C5692C3C25E595C1E38AB6345C843413CBC00EC6E8C227F7EA9513C337644`. This is `PRODUCTION_DEPLOYMENT_PENDING`, not a Test runtime blocker. After UAT approval, production deployment planning must include those F2016 definitions and applicable source/schema alignment. No production SQL was run.

## 10. Unit Contract Runtime

Owner rule: case-insensitive, accent-sensitive; `Cuộn != Cuốn`. Actual service Preview resolved `Cuộn -> 205`, `CUỘN -> 205`, `Cuốn -> 211`, and `CUỐN -> 211`, with no ambiguity. F2016 runtime created temporary accent-distinct Unit names successfully; case-only duplicate insert and update attempts both returned SQL error `51002`. The original Units 205/211 were untouched; both temporary Unit rows were removed (`2 -> 0`).

## 11. Preview No-Write

The actual `CWarehouseProductImportService.PreviewAsync` path ran against TEST DB. Four-Unit resolution Preview count was `0 -> 0`. The 100-row Preview returned `100 valid / 0 invalid`, with Product prefix count `0 -> 0`. `PREVIEW_NO_WRITE: PASS`.

## 12. 100-Row Success

The service confirmed 100 of 100 generated XLSX rows. Product prefix count changed from `0` before Confirm to `100` after Confirm. The data used verified Category `96/TP` and Unit `82/Cái`; a sample Product code preserved trailing text `00123` within the text code. `IMPORT_100_ROWS: PASS_100_OF_100`.

## 13. Audit Field Verification

The persisted sample had the expected Vietnamese name, Category ID `96`, Unit ID `82`, and blank note. `Created_By` / `Last_Updated_By` were `UAT_PI_RUNTIME`; `Created_By_Function` / `Last_Updated_By_Function` were `ProductImportPhase1B`. These values were supplied by the server-side test context, not workbook columns.

## 14. Row-50 Rollback

The transaction-aware Data Access batch path received 100 rows: rows 1-49 valid, row 50 had a deterministic nonexistent Category ID, and rows 51-100 were valid but were not reached after failure. Row 50 raised SQL error `51023` (invalid Category); the controller surfaced row 50 and rolled back. Prefix count was `0` before and `0` after. `ALL_OR_NOTHING_TRANSACTION: PASS`.

## 15. Duplicate Race

Preview contained Products A, X, and C while X was absent. A separate verified SQL connection inserted competing Product X after Preview. Confirm reloaded current Product codes and rejected the preview; original A/C batch rows remained `0`, and only the competing X existed (`1` row under race prefix). Exact-prefix cleanup then returned the race prefix to `0`. This exercises the prescribed Preview -> competing insert -> Confirm revalidation path; it does not claim a forced interleaving after Confirm's final revalidation and before its first insert. `DUPLICATE_RACE: PASS` for the prescribed scenario.

## 16. Cleanup Verification

The 100-row cleanup count was `100 -> 0`; the race competitor cleanup was `1 -> 0`; Unit cleanup was `2 -> 0`; rollback prefix remained `0`. Final read-only checks found 84 total Products, zero rows under the run's Product/Unit prefixes, and no probe table or trigger. No `TRUNCATE`, broad delete, identity reset, FK/index disable, or benchmark-data cleanup was performed.

## 17. Permission Runtime

`NOT_EXECUTED`. Integration exercised the service with an explicit test-only Add-permission resolver. No real application accounts/profiles were supplied for Add-only, Edit-only, no-rights, or permission revocation between Preview and Confirm. Existing permission unit tests passed, but they are not runtime identity evidence.

## 18. Excel Desktop UAT

`NOT_EXECUTED`. Microsoft Excel is installed but was not running, and Codex reported no connected Excel document session. The official template was not opened/saved by Excel and no Excel-produced file was uploaded. Runtime integration used generated in-memory XLSX workbooks through the actual reader/service; this is not a Microsoft Excel Desktop round-trip claim.

## 19. Host Upload

`NOT_EXECUTED`. Local port `15073` was not listening and no deployed UAT host was provided. The browser/SignalR upload limits were not exercised. No app was launched, so no default Business DB appsettings or environment was changed.

## 20. Performance

Correctness gates passed before timing. One warmup plus three measured runs were performed for each batch size. Preview timing is the full Preview operation (including parsing and database reads); parse was separately timed as a diagnostic, so those two columns overlap. Total is Preview + Confirm. The Windows locale rendered decimal commas in raw test output; values below use decimal points, in milliseconds.

| Rows | Run | Parse | Preview | Confirm | Total |
|---:|---:|---:|---:|---:|---:|
| 10 | 1 | 6.550 | 496.433 | 205.021 | 701.454 |
| 10 | 2 | 4.851 | 12.180 | 15.535 | 27.715 |
| 10 | 3 | 19.959 | 10.253 | 7.205 | 17.459 |
| 100 | 1 | 7.847 | 41.142 | 55.462 | 96.604 |
| 100 | 2 | 7.750 | 28.587 | 53.531 | 82.117 |
| 100 | 3 | 10.966 | 41.711 | 78.371 | 120.081 |

| Batch / metric | Min | Median | Max |
|---|---:|---:|---:|
| 10 rows / parse | 4.851 | 6.550 | 19.959 |
| 10 rows / preview | 10.253 | 12.180 | 496.433 |
| 10 rows / confirm | 7.205 | 15.535 | 205.021 |
| 10 rows / total | 17.459 | 27.715 | 701.454 |
| 100 rows / parse | 7.750 | 7.847 | 10.966 |
| 100 rows / preview | 28.587 | 41.142 | 41.711 |
| 100 rows / confirm | 53.531 | 55.462 | 78.371 |
| 100 rows / total | 82.117 | 96.604 | 120.081 |

The first measured 10-row run retained a large cold-start outlier. These are local TEST DB observations, not a production performance guarantee. The 100-row limit was not increased.

## 21. Product Preload

After cleanup, Product count remained 84. `List_Product_Codes_Async` uses page size 500, so one page was expected and observed. One warmup plus three timings yielded min/median/max `1.029 / 1.731 / 2.132 ms`. No preload optimization was justified or made.

## 22. Regression Tests

The runtime integration test passed `1/1` against the guarded Test DB. The focused non-runtime regression filter passed `58/58`, zero failures/skips, covering Product validation, authorization/UI contracts, permission-cache structure, Unit encoding/source contract, DB safety guard, XLSX parser compatibility, stream copy, and Warehouse Excel export. No full test suite was run.

## 23. Build Results

All required projects built with zero errors:

| Project | Warnings | Errors |
|---|---:|---:|
| `TKS_Thuc_Tap_V11_Web_Common` | 4 | 0 |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc` | 12 | 0 |
| `TKS_Thuc_Tap_V11_Web` | 24 | 0 |

The test project also built with 14 warnings and 0 errors. Telerik warnings `TKL002`, `TKL101`, and `TKL004` remain unchanged and out of scope.

## 24. Remaining Gates

- Browser UI, deployed host upload limits, runtime permission profiles/revocation, and Excel Desktop round-trip/upload remain `NOT_EXECUTED`.
- Product audit columns were verified; the UI history event was not exercised because no app session was launched.
- Production deployment remains pending for the approved F2016 CI_AS definitions. No production deployment is authorized by this task.
- Telerik entitlement remains `NEED_OWNER_CONFIRMATION`; no Telerik warning/package/license changes were made.

## 25. Final Decision Table

| Gate | Result |
|---|---|
| TEST DB identity guard | `PASS` |
| Test DB forward sync | `PASS` |
| Required source/Test parity | `PASS (12/12)` |
| Business DB read-only boundary | `PASS`; F2016 deployment pending |
| F2016 Unit CI_AS runtime | `PASS` |
| Unit distinctness Preview | `PASS` |
| Preview no-write | `PASS (0 -> 0)` |
| 100-row Confirm and audit values | `PASS (100/100)` |
| True row-50 SQL rollback | `PASS (51023; final count 0)` |
| Preview/competing insert/Confirm race | `PASS; no original partial rows` |
| Test data/probe cleanup | `PASS; zero remaining` |
| Focused tests / required builds | `PASS` |
| Permission/browser/Excel Desktop/host upload | `NOT_EXECUTED` |
| Performance and Product preload | `MEASURED; no optimization needed` |

## 26. Final Verdict

`PRODUCT_IMPORT_PHASE1B_RUNTIME_VERIFIED`

This verdict covers the guarded current-source service/database runtime and required atomicity, race, Unit, regression, and build gates. It does not mean full browser/Excel/host UAT or production deployment is complete. Business DB remained unchanged; its F2016 CI_AI mismatch is an expected `PRODUCTION_DEPLOYMENT_PENDING` item, not a Test runtime blocker.
