# Product Excel Import Phase 1B - UAT / Database Verification

Date: 2026-10-08

Repository: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`

Branch: `main`
HEAD: `9f361e49b475c34b3b85eab7a4fc2af084a0c564`

## 1. Executive Summary

**Verdict: `PRODUCT_IMPORT_PHASE1B_BLOCKED`.**

Read-only verification reached the database configured by the application. Its identity is `DESKTOP-NHQ7QPL\MSSQLSERVER19` / `TKS_Thuc_Tap_V11_GiaiDoan2`. This is the business database identified in the prior user request, not a confirmed disposable DEV/UAT target. No import, DML, DDL, test-data creation, or cleanup was run.

The deployed `dbo.F2018_sp_ins_San_Pham` definition matches the repository procedure after documented normalization; the Product unique index, reference FKs, and audit columns exist and are enabled. The Unit collision query found `Cuộn` and `Cuốn` in the same `Latin1_General_CI_AI` group. Runtime correctness gates therefore remain blocked; no production-ready conclusion is made.

One source hardening fix was made from observed DB collation evidence: Product/category codes are now accent-sensitive while Unit names retain accent-insensitive matching. Its regression test was observed RED before the fix and GREEN after it. No other source or database changes were made.

## 2. Environment Identity

| Item | Evidence |
|---|---|
| Branch / HEAD | `main` / `9f361e49b475c34b3b85eab7a4fc2af084a0c564` |
| Config source | `TKS_Thuc_Tap_V11_Web/appsettings.json`, connection key `TKS_Thuc_Tap_V11_Conn_String`; `Program.cs` loads it through `GetConnectionString`. `appsettings.Development.json` has no connection-string override. |
| App environment | Not running. `launchSettings.json` selects `Development` for local launch, but no process was launched. Port `15073` was not listening. Deployed app environment/build is unknown. |
| SQL identity | `@@SERVERNAME = DESKTOP-NHQ7QPL\MSSQLSERVER19`; `DB_NAME() = TKS_Thuc_Tap_V11_GiaiDoan2` |
| Authentication evidence | The local settings use integrated security; no username/password or connection secret is included here. |
| Safety classification | `PRODUCTION_OR_BUSINESS_DB` conservatively, based on the prior user description and the configured target. No disposable DEV/UAT authorization was found. |
| Write gate | `WRITE_DB_TESTS_BLOCKED_BY_ENVIRONMENT` |

At task entry, the worktree already contained Phase 0/1A changes: 11 tracked files were modified (480 insertions / 173 deletions in `git diff --stat`) plus untracked Phase 0/1A source, fixtures, tests, template, and reports. They were preserved. This Phase 1B task adds only the report and the two narrowly scoped validator/test edits listed in Section 19; no commit was created.

## 3. Source / DB Deployment Parity

The target was reached through the current local application configuration, not a deployed UAT host. This establishes parity only for the configured business database; it does not prove which DB a deployed UAT application uses.

`dbo.tbl_DM_San_Pham` metadata agrees with `Database/WarehouseModule.Schema.sql` for the import contract:

- Unique enabled key `UX_tbl_DM_San_Pham_Ma` on `Ma_San_Pham`.
- Enabled, trusted FK `FK_tbl_DM_San_Pham_Loai` to `tbl_DM_Loai_San_Pham(Auto_ID)`.
- Enabled, trusted FK `FK_tbl_DM_San_Pham_Don_Vi` to `tbl_DM_Don_Vi_Tinh(Auto_ID)`.
- Nullable `Created_By`, `Created_By_Function`, `Last_Updated_By`, and `Last_Updated_By_Function` columns exist as `NVARCHAR(100)`.

The configured database and Product-code columns use `SQL_Latin1_General_CP1_CI_AS`. This is accent-sensitive; the Unit collision contract is separately evaluated with explicit `Latin1_General_CI_AI` as required by this task.

## 4. F2018 Procedure Verification

`F2018_DEPLOYMENT_PARITY: MATCH` for the configured database.

`OBJECT_DEFINITION(OBJECT_ID(N'dbo.F2018_sp_ins_San_Pham'))` was available. Source and deployed text were normalized by replacing `CREATE OR ALTER PROCEDURE` with `CREATE PROCEDURE`, removing identifier brackets and comments, collapsing whitespace, and uppercasing. The normalized texts were equal (1,843 characters); both SHA-256 hashes are:

`503C99847CAABCA64DD6A84D27991A71F105AB991063DA4F7CE2B7206936DCAA`

The live parameter order/types match source: `@Auto_ID BIGINT OUTPUT`; `@Ma_San_Pham NVARCHAR(100)`; `@Ten_San_Pham NVARCHAR(255)`; category/unit IDs `BIGINT`; `@Ghi_Chu NVARCHAR(1000)`; and four `NVARCHAR(100)` audit parameters. The deployed body has `SET XACT_ABORT ON`, joins an existing caller transaction when `@@TRANCOUNT > 0`, owns/commits/rolls back a transaction only when it starts one, and contains duplicate/category/unit errors `51021`/`51023`/`51024`. Audit values are passed into the Product insert.

The procedure was not deployed or changed. Exact read-only catalog statements are in Section 25.

## 5. Unit Collision Result

`UNIT_COLLISION_CHECK: COLLISION_FOUND`.

The required `LTRIM/RTRIM` plus `Latin1_General_CI_AI` grouping returned:

| Auto_ID | Ten_Don_Vi_Tinh | NormalizedName display |
|---:|---|---|
| 205 | `Cuộn` | `Cuộn` |
| 211 | `Cuốn` | `Cuốn` |

The displayed spellings differ, but SQL groups them as equal under the requested CI_AI collation. The importer correctly rejects a normalized Unit lookup with multiple matches; it must not select the first row. Business/DBA remediation must choose a canonical Unit and account for existing Product references before changing data. No records were edited.

## 6. Preview No-Write Verification

`PREVIEW_DB_SIDE_EFFECT: NOT_EXECUTED` at runtime. The application was not running and no UAT account/workbook was available. No test prefix was created and no Preview was submitted to the business DB.

Source review and the unit/static contract test show `PreviewAsync` checks Add permission, parses, reads category/unit/Product-code state, and validates; it does not call `Import_Products_Async`, `Save_Master_Async`, or an execute procedure. This is source evidence, not the required real-runtime before/after count proof.

## 7. 100-Row Success Integration Test

`ATOMIC_SUCCESS_IMPORT: NOT_EXECUTED`.

No test rows, prefix, category, or Unit were inserted. The target is the business DB, so the required 100-row Preview/Confirm and audit-field query were blocked. There is no count/sample/audit result to report.

## 8. Mid-Batch Failure / Rollback Test

`ALL_OR_NOTHING_TRANSACTION: NOT_EXECUTED`; `WRITE_DB_TESTS_BLOCKED_BY_ENVIRONMENT`.

No row-50 failure was induced. There is no prefix, failure row/type, or post-rollback count. Static source shows one `SqlConnection`, one `SqlTransaction`, each F2018 call associated with that transaction, and rollback on exceptions. Existing tests assert this source contract only; they do not prove that 49 real inserts are rolled back.

## 9. Duplicate Race Test

`DUPLICATE_RACE: NOT_EXECUTED`.

No concurrent transaction was started. Static safeguards are: Confirm reloads Product codes; F2018 checks duplicate codes; the deployed unique index is enabled; and the batch transaction rolls back if a row fails. A real Preview → competing insert → Confirm race remains unproven.

## 10. Runtime Permission Test

`Add permission runtime: NOT_EXECUTED`.

No UAT accounts for profiles A/B/C were provided. Source gates the Product action on Add, and the server-side import service calls its Add-permission resolver at both Preview and Confirm. Unit tests use a fake resolver and prove denial for Edit-only/no-Add and recheck invocation; they are not runtime account evidence.

## 11. Permission Revocation Between Preview / Confirm

`PERMISSION_RECHECK_RUNTIME: NOT_EXECUTED`.

Static review found the existing admin permission handlers reload `CCache_Phan_Quyen_Chuc_Nang` after View/Add/Edit/Delete/Export updates. This is encouraging source evidence, not proof of propagation in a running circuit or of denial after revocation. No cache-delay measurement was possible.

## 12. Microsoft Excel Workbook UAT

`MICROSOFT_EXCEL_UAT: NOT_EXECUTED` for the Phase 1B official-template workflow. The actual official template was not populated and re-saved in Excel during this run.

Parser regression tests passed, and the earlier parser-selection gate contains a real Excel 16.0 fixture. Neither substitutes for this task's official-template round trip with `00123`, valid category/Unit values, Vietnamese text, whitespace, and blank note through the deployed Preview UI.

## 13. Host Upload 1 MiB Verification

`HOST_UPLOAD_1MIB: NOT_EXECUTED` on a deployed host. No browser/app endpoint was running or supplied.

Current source uses Blazor `<InputFile>` (not Telerik FileSelect): the component rejects metadata over 1,048,576 bytes; `FBase.Read_Import_File_Async` checks extension/size and calls `OpenReadStream(maxAllowedSize)`; `CProduct_Xlsx_Reader` independently caps bytes at 1 MiB. `Program.cs` sets SignalR `MaximumReceiveMessageSize` to 22 MiB. These are source limits only. Actual <=1 MiB acceptance and >1 MiB rejection across the deployed browser/SignalR/server path remain unverified.

## 14. Browser UAT

`BROWSER_UAT: NOT_EXECUTED`.

The local port in launch settings (`15073`) was not listening; no deployed UAT URL/build was identified. Template download, Product/Add button visibility, Preview errors, Confirm gating, double-click behavior, success/history, and one grid refresh were not exercised in a browser.

## 15. Negative UAT Cases

Browser/runtime negative UAT is `NOT_EXECUTED`; no writes were attempted. Unit/parser tests do cover malformed/missing-sheet files, formula cells, size/row bounds, headers, missing/duplicate fields, unknown references, in-file/database duplicate validation, and invalid extension behavior. Those tests do not demonstrate zero DB writes on a deployed runtime.

## 16. Audit / History Runtime Result

`AUDIT_HISTORY_RUNTIME: NOT_EXECUTED`.

No Product rows were created. The code path supplies current server context to audit parameters and records one best-effort `Product Excel Import` summary after success, but neither audit fields nor history event count were queried at runtime.

## 17. Performance Baseline

`PERFORMANCE_10_ROWS: NOT_EXECUTED`; `PERFORMANCE_100_ROWS: NOT_EXECUTED`.

Correctness gates are incomplete and there is no safe test DB, so no Preview/Confirm timings, allocations, warmup, or measured runs were performed. The parser timing test was deliberately excluded from the parser regression filter to avoid presenting parser-only timings as the Phase 1B import baseline.

## 18. Product-Code Preload Analysis

The configured business DB contains **84 Products** (`COUNT_BIG(*)`, read-only). `List_Product_Codes_Async` pages at 500, materializes `CWarehouseMaster` rows, and retains all Product codes in memory. For this observed database the preload is one page per Preview/Confirm. No elapsed time or memory was measured, and 84 is not a DEV/UAT catalog measurement.

| Product count | Preload time | Estimated page calls per full preload | Recommendation |
|---:|---|---:|---|
| 84 observed on configured business DB | Not measured | 1 | Keep current approach for this observed size; recheck UAT count. |
| 100 | Not measured | 1 | Keep; bounded incoming lookup is not justified by count alone. |
| 1,000 | Not measured | 2 | Keep pending a safe UAT baseline. |
| 10,000 | Not measured | 20 | Measure Preview and memory before deciding; bounded lookup becomes worth evaluating. |
| 100,000 | Not measured | 200 | Prefer evaluating a parameterized incoming-code lookup before production use at this scale. |
| 1,000,000 | Not measured | 2,000 | Current all-code preload is not a suitable assumed scale; benchmark and design a bounded lookup. |

Page-call counts are derived from the source page size of 500 (`ceil(count / 500)`), not measured timings. No optimization was made in Phase 1B.

## 19. Hardening Changes Made

- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CProductImportValidator.cs`: separated code comparison (`IgnoreCase`, accent-sensitive) from Unit-name comparison (`IgnoreCase | IgnoreNonSpace`). This fixes the observed mismatch where accent variants of Product/category codes were treated as identical despite the configured DB's CI_AS code columns.
- `TKS_Thuc_Tap_V11_Data_Access.Tests/ProductExcelImportValidationTests.cs`: added a regression proving Product/category code accents remain significant while Unit names still resolve without case/accent differences.
- No `Program.cs`, DB procedure/schema, cache architecture, upload transport, Telerik, or other import feature changes. The full SQL collation weight table is not reimplemented in .NET; the database F2018 checks and unique index remain final write guards.
- Report file: this document. No new integration test was added because no safe DB was confirmed.

## 20. Tests / Builds

TDD evidence:

```text
RED:
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter "FullyQualifiedName~Preview_keeps_product_and_category_codes_accent_sensitive_but_units_accent_insensitive" --logger "console;verbosity=normal" --verbosity quiet
Failed: 1, Passed: 0. Assertion: Expected True, Actual False (line 134).

GREEN:
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter "FullyQualifiedName~ProductExcelImportValidationTests" --verbosity quiet
Passed: 20, Failed: 0.
```

Post-change regression commands:

```text
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~ProductExcelImport|FullyQualifiedName~PermissionCacheRefreshStructureTests" --verbosity quiet
Passed: 30, Failed: 0.

dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~XlsxParserCompatibilityTests&FullyQualifiedName!~Parser_allocations_and_elapsed_time" --verbosity quiet
Passed: 11, Failed: 0.

dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~StreamCopyBehaviorTests" --verbosity quiet
Passed: 1, Failed: 0.

dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~Warehouse_excel_export" --verbosity quiet
Passed: 2, Failed: 0.
```

Total post-change regression: **44 passed, 0 failed**. No DB integration tests were selected. Numeric coverage was not measured; no 80% coverage claim is made.

Builds after source change:

```text
dotnet build TKS_Thuc_Tap_V11_Web_Common\TKS_Thuc_Tap_V11_Web_Common.csproj --no-restore --verbosity quiet
Succeeded; 4 warnings, 0 errors.

dotnet build TKS_Thuc_Tap_V11_Web_Danh_Muc\TKS_Thuc_Tap_V11_Web_Danh_Muc.csproj --no-restore --verbosity quiet
Succeeded; 12 warnings, 0 errors.

dotnet build TKS_Thuc_Tap_V11_Web\TKS_Thuc_Tap_V11_Web.csproj --no-restore --verbosity quiet
Succeeded; 24 warnings, 0 errors.
```

Warnings include `TKL002`, `TKL101`, and `TKL004`. `git diff --check` was run after edits and reported no whitespace errors.

## 21. Telerik Entitlement

`TELERIK_ENTITLEMENT: NEED_OWNER_CONFIRMATION`.

Builds still report `TKL002` (license file not found), `TKL101` (Telerik UI for Blazor and Document Processing Libraries not listed in current license), and `TKL004` (licenses not located for all products). No warning was suppressed; no Telerik package, license, or export implementation was changed.

## 22. Remaining Blockers

1. Provide/identify a dedicated disposable DEV/UAT DB and explicit write approval for it; then rerun Preview no-write, 100-row commit, row-50 rollback, duplicate race, and cleanup tests there.
2. Resolve the `Cuộn` / `Cuốn` Unit collision at the business-data level before importing products that use either normalized name.
3. Provide deployed UAT URL/build and accounts for Add-only, Edit-only, and no-rights profiles; verify permission revocation/cache behavior and browser flow.
4. Open/save the official template in desktop Excel and perform deployed Preview with `00123`, valid references, Unicode, whitespace, and blank note.
5. Measure <=1 MiB accept and >1 MiB reject on the deployed host; verify effective SignalR/proxy limits.
6. After correctness gates pass on safe UAT, collect warmed 10/100-row timings and record the UAT Product count/preload cost.
7. Owner must confirm Telerik entitlement.
8. Decide whether Product/category identifiers are accent-sensitive per database collation; this change aligns common Latin/Vietnamese accent behavior, but .NET `CompareInfo` is not a full emulation of every SQL collation weight.

## 23. Final Decision Table

| Gate | Result | Evidence | Production blocker? |
|---|---|---|---|
| F2018 deployment parity | `MATCH` for configured DB | Normalized source/deployed text equal; hash in Section 4 | No for this DB; deployed UAT parity still unknown |
| Unit collision | `COLLISION_FOUND` | IDs 205/211: `Cuộn` / `Cuốn` under CI_AI | Yes for either ambiguous Unit |
| Preview no-write | `NOT_EXECUTED` runtime | Source/static test only; no real Preview | Yes |
| 100-row commit | `NOT_EXECUTED` | Business DB write blocked | Yes |
| Atomic rollback | `NOT_EXECUTED` | No mid-batch DB failure test | Yes |
| Duplicate race | `NOT_EXECUTED` | Static guards only | Yes |
| Add permission runtime | `NOT_EXECUTED` | Fake resolver tests only | Yes |
| Permission recheck | `NOT_EXECUTED` runtime | Source cache refresh and fake resolver test only | Yes |
| Microsoft Excel file | `NOT_EXECUTED` Phase 1B UAT | Existing parser fixture tests do not cover official-template runtime flow | Yes |
| Host upload <=1 MiB | `NOT_EXECUTED` | Source limits only; no deployed host | Yes |
| >1 MiB rejection | `NOT_EXECUTED` runtime | Unit/parser checks only | Yes |
| Browser UAT | `NOT_EXECUTED` | No running local app/UAT URL | Yes |
| Audit/history | `NOT_EXECUTED` runtime | Source path only | Yes |
| Performance 10 rows | `NOT_EXECUTED` | Correctness gates incomplete | Phase gate outstanding |
| Performance 100 rows | `NOT_EXECUTED` | Correctness gates incomplete | Phase gate outstanding |
| Product-code preload | `PARTIAL_READ_ONLY` | 84 rows on business DB; 1 page; no timing | No optimization blocker at observed size; UAT count remains unknown |
| Telerik entitlement | `NEED_OWNER_CONFIRMATION` | TKL002/TKL101/TKL004 build warnings | Yes |

## 24. Final Verdict

`PRODUCT_IMPORT_PHASE1B_BLOCKED`

The app is not proven against a safe DEV/UAT environment. Critical runtime/database gates remain unexecuted, and the live Unit collision requires business remediation. The source hardening and non-DB regression suite pass, but they do not upgrade Phase 1A to production readiness.

## 25. Read-Only SQL Evidence

The following are the read-only statements used. No `INSERT`, `UPDATE`, `DELETE`, DDL, or transaction test was run.

```sql
-- Database identity
SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName;

-- Deployed procedure definition (hashed/compared in memory; body not copied into report)
SELECT OBJECT_DEFINITION(OBJECT_ID(N'dbo.F2018_sp_ins_San_Pham'));

-- Deployed parameter contract
SELECT p.parameter_id, p.name, TYPE_NAME(p.user_type_id) AS type_name, p.max_length, p.is_output
FROM sys.parameters AS p
WHERE p.object_id = OBJECT_ID(N'dbo.F2018_sp_ins_San_Pham')
ORDER BY p.parameter_id;

-- Unique Product keys
SELECT i.name, i.is_unique, i.is_disabled, c.name AS key_column, ic.key_ordinal
FROM sys.indexes AS i
JOIN sys.index_columns AS ic
  ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.object_id = OBJECT_ID(N'dbo.tbl_DM_San_Pham') AND i.is_unique = 1
ORDER BY i.name, ic.key_ordinal;

-- Product reference FKs
SELECT fk.name, pc.name AS parent_column, rt.name AS referenced_table,
       rc.name AS referenced_column, fk.is_disabled, fk.is_not_trusted
FROM sys.foreign_keys AS fk
JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns AS pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
JOIN sys.tables AS rt ON rt.object_id = fkc.referenced_object_id
JOIN sys.columns AS rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
WHERE fk.parent_object_id = OBJECT_ID(N'dbo.tbl_DM_San_Pham')
ORDER BY fk.name;

-- Product audit columns
SELECT c.name, TYPE_NAME(c.user_type_id) AS type_name, c.max_length, c.is_nullable
FROM sys.columns AS c
WHERE c.object_id = OBJECT_ID(N'dbo.tbl_DM_San_Pham')
  AND c.name IN (N'Created_By', N'Created_By_Function', N'Last_Updated_By', N'Last_Updated_By_Function')
ORDER BY c.column_id;

-- Unit collision semantics required by this task
;WITH UnitNames AS
(
    SELECT Auto_ID, Ten_Don_Vi_Tinh,
           LTRIM(RTRIM(Ten_Don_Vi_Tinh)) COLLATE Latin1_General_CI_AI AS NormalizedName
    FROM dbo.tbl_DM_Don_Vi_Tinh
), Collisions AS
(
    SELECT NormalizedName
    FROM UnitNames
    GROUP BY NormalizedName
    HAVING COUNT(*) > 1
)
SELECT u.Auto_ID, u.Ten_Don_Vi_Tinh, u.NormalizedName
FROM UnitNames AS u
JOIN Collisions AS c ON c.NormalizedName = u.NormalizedName
ORDER BY u.NormalizedName, u.Auto_ID;

-- Product catalog size; no product rows were selected
SELECT COUNT_BIG(*) AS ProductCount FROM dbo.tbl_DM_San_Pham;

-- Database and import-key collations
SELECT DB_NAME() AS DatabaseName,
       CONVERT(sysname, DATABASEPROPERTYEX(DB_NAME(), N'Collation')) AS DatabaseCollation;
SELECT OBJECT_NAME(object_id) AS TableName, name AS ColumnName, collation_name
FROM sys.columns
WHERE object_id IN (OBJECT_ID(N'dbo.tbl_DM_San_Pham'), OBJECT_ID(N'dbo.tbl_DM_Loai_San_Pham'), OBJECT_ID(N'dbo.tbl_DM_Don_Vi_Tinh'))
  AND name IN (N'Ma_San_Pham', N'Ma_LSP', N'Ten_Don_Vi_Tinh')
ORDER BY TableName, ColumnName;
```

## 26. Files Changed in Phase 1B

Source:

- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CProductImportValidator.cs`
- `TKS_Thuc_Tap_V11_Data_Access.Tests/ProductExcelImportValidationTests.cs`

Tests: no new test file; one regression test added to the existing validation test file.

SQL evidence: included in Section 25; no separate SQL file.

Performance evidence: none; performance gates were not run.
Report: `docs/reports/product-excel-import-phase1b-uat-db-verification-2026-10-08.md`.
