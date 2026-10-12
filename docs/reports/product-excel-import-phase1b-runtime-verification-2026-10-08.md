# Product Excel Import Phase 1B Runtime Verification

## 1. Executive Summary

**Verdict: `PRODUCT_IMPORT_PHASE1B_BLOCKED`.**

The owner-approved Unit identity rule is implemented in source and covered by tests: identity is case-insensitive and accent-sensitive. Source and Business DB were equal before that approved change. The Test DB has matching Product, Category, Unit tables, indexes, and foreign keys, but is missing stored procedures and their fence dependencies required by Preview and Confirm. After the source F2016 change, the Business DB remains read-only and differs from source. No database writes, app launch, import, cleanup, or runtime UAT were performed.

## 2. Owner Decisions

- `Cuộn` and `Cuốn` are distinct Units. Unit comparison is case-insensitive and accent-sensitive (`CI_AS`).
- Business DB `TKS_Thuc_Tap_V11_GiaiDoan2` is the read-only deployment reference.
- Test DB `TKS_Thuc_Tap_V11_C01C02_Test_20260906` is approved for test data changes, but not DDL or procedure deployment without separate approval.

## 3. Source-of-Truth Policy

Repository SQL and C# define the intended design. Business DB is read-only comparison evidence. Test DB is an execution target only and is stale where it differs. Neither DB was changed.

## 4. Business DB Identity

Read-only identity query returned server `DESKTOP-NHQ7QPL\MSSQLSERVER19`, database `TKS_Thuc_Tap_V11_GiaiDoan2`, status `ONLINE`, updateability `READ_WRITE`. The owner-mandated read-only restriction was followed; no DML, DDL, transaction test, cleanup, or permission change was run.

## 5. Test DB Identity

Read-only identity query returned server `DESKTOP-NHQ7QPL\MSSQLSERVER19`, database `TKS_Thuc_Tap_V11_C01C02_Test_20260906`, status `ONLINE`, updateability `READ_WRITE`. The target identity matched the owner-approved test DB. Write tests remained blocked by parity findings below.

## 6. Three-Way Parity Matrix

| Object | Source | Business DB | Test DB | Verdict / Action |
|---|---|---|---|---|
| `tbl_DM_Don_Vi_Tinh` | `Auto_ID bigint`; `Ten_Don_Vi_Tinh nvarchar(200) NOT NULL COLLATE SQL_Latin1_General_CP1_CI_AS`; notes and audit columns | Matching types, nullability, and collation | Matching types, nullability, and collation | `MATCH_ALL` |
| `tbl_DM_Loai_San_Pham` | `Auto_ID`; `Ma_LSP nvarchar(100)`; `Ten_LSP nvarchar(200)`; notes, timestamps, audit columns | Matching metadata; text columns `SQL_Latin1_General_CP1_CI_AS` | Matching metadata; text columns `SQL_Latin1_General_CP1_CI_AS` | `MATCH_ALL` |
| `tbl_DM_San_Pham` | `Auto_ID`; code/name `nvarchar(100/255)`; Category/Unit IDs `bigint`; notes, timestamps, audit columns | Matching metadata; text columns `SQL_Latin1_General_CP1_CI_AS` | Matching metadata; text columns `SQL_Latin1_General_CP1_CI_AS` | `MATCH_ALL` |
| Audit fields | `Created`, `Last_Updated`, `Created_By`, `Created_By_Function`, `Last_Updated_By`, `Last_Updated_By_Function` | Present with source-compatible types/nullability | Present with source-compatible types/nullability | `MATCH_ALL` |
| Unique indexes | Unit name; Category code/name; Product code | All present, unique, enabled, matching key columns | All present, unique, enabled, matching key columns | `MATCH_ALL` |
| Product foreign keys | Product Category ID -> Category Auto_ID; Product Unit ID -> Unit Auto_ID | Both enabled and trusted | Both enabled and trusted | `MATCH_ALL` |
| `F2018_sp_ins_San_Pham` | Present; normalized SHA-256 `503C99847CAABCA64DD6A84D27991A71F105AB991063DA4F7CE2B7206936DCAA` | Same normalized hash | Missing | `TEST_DB_STALE` |
| `F2016_sp_ins_Don_Vi_Tinh` | Updated to explicit `SQL_Latin1_General_CP1_CI_AS`; normalized hash `8AEC10143DC24E3592D3A7D216328B73D723F80D21B415DCB77E527980AD0417` | Old `CI_AI` definition; hash `173EAE5607F40E1789BAE4D805434D32402230C4498ED466D6B133D224190949` | Missing | `SOURCE_BUSINESS_DB_PARITY_MISMATCH`; Business DB remains read-only |
| `F2016_sp_upd_Don_Vi_Tinh` | Updated to explicit `SQL_Latin1_General_CP1_CI_AS`; normalized hash `3C45E60ED2A80A58F25813B6E9715C51C94836DC51DA75638F8178F65EC37567` | Old `CI_AI` definition; hash `B82C5692C3C25E595C1E38AB6345C843413CBC00EC6E8C227F7EA9513C337644` | Missing | `SOURCE_BUSINESS_DB_PARITY_MISMATCH`; Business DB remains read-only |
| Unit/Category lists and related lookups | Source definitions present | Normalized definitions match source before the Unit write change | Unit list/lookup and Category list/lookup missing | `TEST_DB_STALE` |
| `sp_DM_Master_Page`, `sp_DM_Lookup_Page` | Present | Normalized definitions match source | Present; normalized definitions match source | `MATCH_ALL` |
| Fence functions/procedures used by Unit/Product writes | Source definitions present | Dependencies resolved in deployed Business DB | `fn_Inventory_Fence_Root_Resource`, `fn_Inventory_Fence_Mode_Satisfies`, `sp_Inventory_Fence_Require_Transaction`, `sp_Inventory_Fence_Acquire_Root` missing | `TEST_DB_STALE`; include in approved sync scope |

Stored procedure hashes use whitespace/comment/bracket normalization and SHA-256. The Test DB's Product, Category, Unit columns, audit fields, unique indexes, and Product foreign keys match the current design metadata; the blocking drift is stored procedures and fence dependencies. `F2018_sp_ins_San_Pham` matched Source and Business DB, but is absent from Test DB.

## 7. Test DB Staleness Findings

`TEST_DB_STALE_OR_MISMATCHED`: all tables and listed indexes/FKs are present and match, but the Test DB lacks `F2018_sp_ins_San_Pham`, F2016 Unit insert/update/list/lookup, F2017 Category list/lookup, F2018 Product lookup, and four fence dependencies. The app Preview calls Unit and Category list procedures plus `sp_DM_Master_Page`; Confirm calls `F2018_sp_ins_San_Pham`. It cannot complete those paths on this Test DB as currently deployed.

## 8. Required Test-DB Sync

`TEST_DB_DDL_SYNC_REQUIRED`. A test-only proposal is at [product-import-test-db-sync-proposal-2026-10-08.sql](proposals/product-import-test-db-sync-proposal-2026-10-08.sql). It contains 12 missing procedures/functions copied from current source and a server/database preflight. All 12 normalized object bodies were checked against source. It was not run; separate DDL approval is required. No table, column, index, FK, grant, or permission change is proposed.

## 9. Unit Contract Alignment

- `CProductImportValidator` now uses `CompareOptions.IgnoreCase` without `IgnoreNonSpace`.
- `WarehouseModule.Schema.sql` now declares `Ten_Don_Vi_Tinh` as `SQL_Latin1_General_CP1_CI_AS` so new databases do not inherit an unintended default.
- F2016 insert/update source duplicate predicates now explicitly compare using `SQL_Latin1_General_CP1_CI_AS`, matching current Business/Test column and unique-index semantics.
- Regression tests prove `Cuộn -> 205`, `Cuốn -> 211`, `CUỘN -> 205`, `CUỐN -> 211`; `HỘP` matches `Hộp`, while unaccented `HOP` does not.
- Test DB contains Unit 205 `Cuộn` (3 Product references) and Unit 211 `Cuốn` (1 reference). These rows were only read, not changed.

## 10. Source Changes

- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CProductImportValidator.cs`: Unit comparer removes `IgnoreNonSpace`.
- `Database/WarehouseModule.Schema.sql`: Unit name column explicitly uses `SQL_Latin1_General_CP1_CI_AS`.
- `Database/WarehouseModule.Procedures.sql`: F2016 insert/update duplicate predicates use `SQL_Latin1_General_CP1_CI_AS`. Pre-existing unrelated Inventory changes in this file were preserved.
- `TKS_Thuc_Tap_V11_Data_Access.Tests/ProductExcelImportValidationTests.cs` and `WarehouseUnitDuplicateEncodingTests.cs`: Unit and source-contract regression coverage.
- `TKS_Thuc_Tap_V11_Data_Access.Tests/ProductImportTestDatabase.cs` and `ProductImportDatabaseSafetyTests.cs`: test-only connection guard. It accepts only `TKS_INTEGRATION_CONNECTION_STRING` for the exact approved server/database and verifies live `@@SERVERNAME`/`DB_NAME()` before returning a connection. No Product Import DML test used it in this run.
- No appsettings, Business DB, Test DB, Telerik, or export files were modified.

## 11. Test DB F2018 Parity

`F2018_sp_ins_San_Pham`: Source/Business normalized match; Test DB object missing. Product transaction tests are blocked. The test-only sync proposal includes the source definition plus its missing fence dependencies.

## 12. Preview No-Write

`NOT_EXECUTED` against the app/Test DB. Pure validator tests passed, but the actual Preview path depends on missing Unit and Category list procedures. No test prefix was inserted or queried.

## 13. 100-Row Success

`NOT_EXECUTED`. No Product rows were inserted. Category reference candidate read from Test DB: ID 96, code `TP`. Unambiguous Unit candidate: ID 82, name `Cái`. These were selected as read-only candidates only.

## 14. Row-50 Rollback

`NOT_EXECUTED`. No transaction or forced failure was started; no test rows exist from this run.

## 15. Duplicate Race

`NOT_EXECUTED`. No competing insert, confirm, or cleanup was attempted.

## 16. Unit Distinctness Runtime

Pure Preview validator checks passed for the four owner-required inputs. DB rows 205/211 are present, but app/runtime resolution was `NOT_EXECUTED` because the Test DB lacks the Unit list procedure. No runtime data was changed.

## 17. Permission Runtime

`NOT_EXECUTED`. No UAT identities/permission scenarios were available or changed.

## 18. Excel Desktop Runtime

`NOT_EXECUTED`. Microsoft Excel desktop was not used for this task; no new Excel-saved fixture is claimed.

## 19. Host Upload

`NOT_EXECUTED`. No UAT host/browser upload was run. The source limit is not evidence of deployed transport behavior.

## 20. Audit Fields / History

Source `F2018_sp_ins_San_Pham` maps all four audit actor/function fields; Product/Category/Unit metadata contains the corresponding audit columns. Runtime inserted values and history were not verified because the DB write gate is blocked.

## 21. Performance

`NOT_EXECUTED`. No parse/preview/confirm or performance workload was run.

## 22. Product Preload

Test DB contains 84 Products. `sp_DM_Master_Page` matches source and uses the importer page contract, but app page count and Preview timing were not measured because Preview's Unit/Category list calls cannot run on this DB.

## 23. Cleanup Evidence

No rows were created, so no cleanup was required or run. No prefix count, broad delete, truncate, identity reset, or constraint change was performed.

## 24. Tests / Builds

- TDD RED reproduced the accent-insensitive Unit collision and the old F2016 contract; after source changes, focused Product/Unit tests passed.
- Final focused regression: 58 passed, 0 failed, 0 skipped. Included Product validation, authorization/UI, permission cache, Unit duplicate encoding, database safety guard, XLSX parser compatibility, stream copy, and Warehouse Excel export checks.
- `Web_Common`: build succeeded, 0 errors, 4 warnings.
- `Web_Danh_Muc`: build succeeded, 0 errors, 12 warnings.
- `Web`: build succeeded, 0 errors, 24 warnings.
- No DB-mutating test or full test suite was run.

## 25. Known Out-of-Scope Telerik Warnings

`TKL002`, `TKL101`, and `TKL004` remain unchanged and are out of scope for Product Import.

## 26. Remaining Blockers

- Test DB requires approved procedure/function DDL sync before app/runtime verification.
- Source F2016 Unit insert/update now implement the owner-approved CI_AS rule, while the read-only Business DB still has CI_AI definitions. Per the provided parity gate, runtime write tests stop on this source/Business mismatch.
- No DDL approval was granted; no procedure deployment was attempted.
- UAT permission, Excel desktop, host upload, transaction, race, cleanup, audit-value, and performance gates remain unverified.
- Telerik entitlement remains outside this task.

## 27. Final Decision Table

| Gate | Result |
|---|---|
| Owner Unit rule in importer/source | `PASS` |
| Business DB identity/read-only boundary | `PASS` |
| Test DB identity | `PASS` |
| Schema/index/FK parity | `MATCH_ALL` |
| Test DB procedure parity | `TEST_DB_STALE` |
| Source/Business F2016 parity after owner-approved change | `MISMATCH - STOP RUNTIME WRITES` |
| Test DB DDL sync | `REQUIRED - NOT APPROVED/NOT RUN` |
| Unit pure tests | `PASS` |
| Runtime write/UAT gates | `NOT EXECUTED` |
| Focused regression/build | `PASS` |

## 28. Final Verdict

**`PRODUCT_IMPORT_PHASE1B_BLOCKED`**

Reason: `TEST_DB_STALE` and the source/Business F2016 parity mismatch require resolution before any runtime write test. Product Import code has not failed in DB runtime; that path remains unverified. Business DB remains unchanged and Test DB received no writes.
