# Product Import Phase 1B Continuation - Safe UAT and Unit Identity

Date: 2026-10-08

Repository: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`

Branch / HEAD: `main` / `9f361e49b475c34b3b85eab7a4fc2af084a0c564`

## 1. Executive Summary

**Verdict: `PRODUCT_IMPORT_PHASE1B_BLOCKED`.**

No database was confirmed as a writable disposable UAT target. The two database names resembling test environments are candidates only; their names and `ONLINE` / `MULTI_USER` state do not prove they are disposable or authorized for DML and cleanup. No write gate was run.

Unit identity is `CASE C: Cannot determine from source/data`. The live Unit column and unique index are accent-sensitive, and the database contains both `Cuộn` and `Cuốn`; however, F2016 insert/update duplicate checks and the importer normalize Unit names accent-insensitively. The two rows have existing Product references. No Unit behavior, source, database, or configuration was changed pending the business owner's decision.

## 2. Safe UAT Environment

| Item | Evidence / result |
|---|---|
| Current business target | `DESKTOP-NHQ7QPL\MSSQLSERVER19` / `TKS_Thuc_Tap_V11_GiaiDoan2`; read-only only |
| Candidate database | `test`, `ONLINE`, `MULTI_USER`, `is_read_only=0`; owner authorization and disposable purpose not confirmed |
| Candidate database | `TKS_Thuc_Tap_V11_C01C02_Test_20260906`, `ONLINE`, `MULTI_USER`, `is_read_only=0`; owner authorization and disposable purpose not confirmed |
| Classification | `SAFE_DEV_DB` / `SAFE_DISPOSABLE_UAT_DB`: none established |
| Write authorization evidence category | `NONE`; no explicit owner confirmation to mutate or clean up either candidate |
| Actions on candidates | Only database names/state were listed from `master.sys.databases`; no candidate database was queried for data and no writes were attempted |

Before any integration test, the owner must provide or confirm:

- SQL Server instance.
- Exact database name.
- Connection configuration location/mechanism for test-only use.
- Explicit confirmation that DML, test data, and cleanup are allowed in that database.

Do not send a password in chat or report. No backup, restore, clone, or database creation was attempted.

## 3. Environment Isolation

`Program.cs` assigns `CConfig.TKS_Thuc_Tap_V11_Conn_String` from `GetConnectionString("TKS_Thuc_Tap_V11_Conn_String")`. The default `appsettings.json` connection reached the business database above. `appsettings.Development.json` has no connection-string override; `appsettings.UAT.json` does not exist; the matching application connection environment override and `TKS_INTEGRATION_CONNECTION_STRING` are absent. No application was launched, and port `15073` was not listening.

No connection setting was changed. For a web UAT app, use an approved UAT-specific configuration or the environment override `ConnectionStrings__TKS_Thuc_Tap_V11_Conn_String`; for integration tests, use a dedicated test-only connection setting such as `TKS_INTEGRATION_CONNECTION_STRING`. Do not replace the business connection globally. Before every test that can write, assert both `@@SERVERNAME` and `DB_NAME()` against the owner-confirmed expected values and abort before DML on mismatch.

Test-infrastructure precedent: `InventoryFenceFoundationIntegrationTests` requires explicit opt-in, a supplied connection, `InitialCatalog=tempdb`, and rejects the business database. `WarehouseInventoryC01C02IntegrationTests` reads `TKS_INTEGRATION_CONNECTION_STRING` and documents a disposable target, but that environment-variable requirement alone is not a server/database identity allowlist. No Product Import DB identity guard or safe-DB integration run exists yet.

## 4. Unit Identity Analysis

The current business DB's `tbl_DM_Don_Vi_Tinh.Ten_Don_Vi_Tinh` column uses `SQL_Latin1_General_CP1_CI_AS`. Its enabled unique index `UX_tbl_DM_Don_Vi_Tinh_Ten` keys that column and inherits the same effective collation. Direct SQL comparisons returned `Cuộn = Cuốn` as false under `CI_AS`, true under `Latin1_General_CI_AI`.

This proves the current DB uniqueness behavior permits those spellings as distinct values. It does not prove whether the business intends them to be distinct measurement units or historical synonyms. No cleanup or merge is authorized or proposed as an automatic action.

The same DB reports `SQL_Latin1_General_CP1_CI_AS` for Product `Ma_San_Pham` and Category `Ma_LSP`. The existing `CodeComparisonKey` uses `IgnoreCase` without `IgnoreNonSpace`, so the Phase 1B Product/Category accent-sensitive hardening remains consistent with observed column collations. Its regression test passed in this continuation.

## 5. Cuộn / Cuốn Evidence

| Unit ID | Stored name | Products referencing Unit ID |
|---:|---|---:|
| 205 | `Cuộn` | 3 |
| 211 | `Cuốn` | 1 |

Both rows currently coexist in the business DB. Counts are read-only results from `tbl_DM_San_Pham`; they are not UAT data and no row was changed.

## 6. Unit DB / SP / Importer Contract Comparison

| Layer | Observed behavior |
|---|---|
| Unit column and unique index | `SQL_Latin1_General_CP1_CI_AS`; `Cuộn` and `Cuốn` compare unequal, so the unique index can contain both |
| Live `F2016_sp_ins_Don_Vi_Tinh` | Duplicate predicate explicitly collates both sides to `Latin1_General_CI_AI`; throws `51002` for accent-insensitive matches |
| Live `F2016_sp_upd_Don_Vi_Tinh` | Same `CI_AI` duplicate predicate and error `51002` |
| F2016 Unit list / lookup | Returns Unit IDs and names without CI_AI normalization. The paged lookup filters `Ten_Don_Vi_Tinh` with `LIKE` under the column/database collation |
| Manual Unit UI | Create/edit save is routed through `CWarehouseMaster_Controller.Save_Master_Async` to F2016 insert/update; the server procedure owns the duplicate check |
| Product importer | `CProductImportValidator.UnitComparisonKey` uses `IgnoreCase` plus `IgnoreNonSpace`; the .NET comparer returned `Compare=0` and equal sort keys for `Cuộn` / `Cuốn`, so both records land in one normalized group and a match is rejected as ambiguous rather than choosing an ID |

**Finding: `UNIT_DB_SP_CONTRACT_MISMATCH`.** The DB column/index permit accent variants as distinct while F2016 insert/update reject them as duplicates. The importer currently follows the F2016 `CI_AI` contract and refuses the ambiguous lookup, but that behavior is not equivalent to the DB uniqueness semantics. Do not change only the importer and leave manual CRUD behavior undocumented.

This is a live business-DB behavior observation plus source inspection, not safe-UAT deployment parity. The live F2016 definitions contain the CI_AI duplicate predicate; source `WarehouseModule.Procedures.sql` contains the same predicate. No procedure was deployed or changed.

## 7. Owner Decision

Classification: **`UNIT_IDENTITY_REQUIRES_OWNER_DECISION`**.

Required question:

> Trong nghiệp vụ hiện tại, Đơn vị tính "Cuộn" và "Cuốn" có phải hai đơn vị riêng biệt không?

- If **YES**, Unit name matching must not be accent-insensitive. Align Product Import and F2016 manual CRUD duplicate behavior explicitly with the approved `CI_AS` contract; add regression tests before changing behavior.
- If **NO**, the business owner must approve a reference-data cleanup/merge plan that first accounts for the 3 and 1 Product references. Do not rely on name lookup until canonical identity is established. No deletion, rename, or merge was performed.
- Stable Unit business codes are a possible later design, but schema migration is outside this task.

## 8. UAT F2018 Parity

`F2018_DEPLOYMENT_PARITY: NOT_VERIFIED_FOR_SAFE_UAT`. The prior Phase 1B report recorded `MATCH` for the configured business DB (normalized SHA-256 `503C99847CAABCA64DD6A84D27991A71F105AB991063DA4F7CE2B7206936DCAA`). That does not establish parity for either unapproved candidate DB or a deployed UAT host. No candidate DB procedure was queried.

## 9. Preview No-Write

`PREVIEW_NO_WRITE: NOT_EXECUTED`. No safe writable database, UAT host, or test workbook was authorized. The prior source/unit-test evidence that `PreviewAsync` is read-only is not a runtime before/after count proof.

## 10. 100-Row Import

`IMPORT_100_ROWS: NOT_EXECUTED`. No prefix or test rows were created. No category/unit fixture was inserted. There are no imported count, sample, or audit-field results.

## 11. Atomic Rollback

`ALL_OR_NOTHING: NOT_EXECUTED`. No row-50 database failure was induced. Failure number/message and before/after prefix counts are unavailable. Existing source tests are not a runtime transaction proof.

## 12. Duplicate Race

`DUPLICATE_RACE: NOT_EXECUTED`. No competing connection inserted a code and no Confirm race was run.

## 13. Runtime Permission

`RUNTIME_PERMISSION: NOT_EXECUTED`. No UAT accounts/profiles were supplied. Existing fake-resolver/source tests are not UI/server runtime evidence.

## 14. Permission Revocation

`REVOCATION_BEFORE_CONFIRM: NOT_EXECUTED`. No running UAT circuit was available to measure cache refresh or prove Confirm denial after Add revocation.

## 15. Microsoft Excel UAT

`EXCEL_DESKTOP_UAT: NOT_EXECUTED`. The official `San_Pham_Import_Template.xlsx` was not opened, edited, saved, or uploaded in Excel Desktop in this continuation.

## 16. Host Upload

`HOST_UPLOAD: NOT_EXECUTED`. No running UAT host was identified. No approximately 500 KiB, at-limit, or over-1-MiB upload was attempted; there are no runtime byte counts or rejection observations.

## 17. Browser UAT

`BROWSER_UAT: NOT_EXECUTED`. Port `15073` was not listening and no deployed UAT URL/build was provided. Product import UI flow, Confirm gating, double-click behavior, and grid refresh were not exercised.

## 18. Audit / History

`AUDIT_HISTORY_RUNTIME: NOT_EXECUTED`. No successful UAT import occurred; no history summary or persisted audit values were inspected.

## 19. Performance

`PERFORMANCE: NOT_RUN`. Correctness and runtime gates have not passed. No timing, memory, or throughput claim is made.

## 20. Product Preload

The configured business DB currently has 84 Products (read-only count). The previous sizing analysis therefore does not justify speculative preload optimization. Safe-UAT Product count and Preview/preload measurements are unavailable; no optimization or row-limit increase was made.

## 21. Changes Made

Changed file: `docs/reports/product-excel-import-phase1b-continuation-2026-10-08.md`.

No source, test, appsettings, SQL, database, or Telerik files were changed in this continuation. In particular, `UnitComparisonKey` and both F2016 procedures remain unchanged pending owner decision.

## 22. Tests / Builds

Focused non-database tests passed:

| Test group | Passed | Failed |
|---|---:|---:|
| Product import, permission-cache structure, Unit duplicate encoding | 34 | 0 |
| XLSX parser compatibility, excluding parser timing | 11 | 0 |
| Telerik SpreadImporter runtime regression | 2 | 0 |
| Stream copy behavior | 1 | 0 |
| Warehouse Excel export workflow | 2 | 0 |
| **Total** | **50** | **0** |

Builds passed with zero errors:

| Project | Warnings | Errors |
|---|---:|---:|
| `TKS_Thuc_Tap_V11_Web_Common` | 4 | 0 |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc` | 12 | 0 |
| `TKS_Thuc_Tap_V11_Web` | 24 | 0 |

The warnings include Telerik `TKL002`, `TKL101`, and `TKL004`. These tests/builds do not prove UAT runtime behavior. No database integration test or performance test was run.

Commands used:

```powershell
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~ProductExcelImport|FullyQualifiedName~PermissionCacheRefreshStructureTests|FullyQualifiedName~WarehouseUnitDuplicateEncodingTests" --verbosity quiet
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~XlsxParserCompatibilityTests&FullyQualifiedName!~Parser_allocations_and_elapsed_time" --verbosity quiet
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~TelerikSpreadImporterRuntimeTests" --verbosity quiet
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~StreamCopyBehaviorTests" --verbosity quiet
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~Warehouse_excel_export" --verbosity quiet
```

Test files exercised: `TKS_Thuc_Tap_V11_Data_Access.Tests/ProductExcelImportAuthorizationTests.cs`, `TKS_Thuc_Tap_V11_Data_Access.Tests/ProductExcelImportUiTests.cs`, `TKS_Thuc_Tap_V11_Data_Access.Tests/ProductExcelImportValidationTests.cs`, `TKS_Thuc_Tap_V11_Data_Access.Tests/PermissionCacheRefreshStructureTests.cs`, `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseUnitDuplicateEncodingTests.cs`, `TKS_Thuc_Tap_V11_Data_Access.Tests/XlsxParserCompatibilityTests.cs`, `TKS_Thuc_Tap_V11_Data_Access.Tests/TelerikSpreadImporterRuntimeTests.cs`, `TKS_Thuc_Tap_V11_Data_Access.Tests/StreamCopyBehaviorTests.cs`, and `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseUiWorkflowTests.cs`.

## 23. Telerik Entitlement

`TELERIK_ENTITLEMENT: NEED_OWNER_CONFIRMATION`. Telerik packages and licensing configuration were left unchanged. Build diagnostics report the license is not found/listed; no license key contents were retrieved or changed.

## 24. Remaining Blockers

1. Owner must identify and authorize one exact disposable DEV/UAT server/database for DML and cleanup.
2. Owner must answer whether `Cuộn` and `Cuốn` are distinct units; the current DB/SP contract mismatch needs an explicit alignment decision.
3. The confirmed test DB must pass server/database identity guards and source/deployed F2018 parity before writes.
4. UAT URL/build and permission-profile accounts are needed for runtime permission, revocation, upload, and browser gates.
5. Excel Desktop workbook UAT and all database-backed correctness gates remain outstanding.
6. Telerik entitlement remains unconfirmed.

## 25. Final Decision Table

| Gate | Result | Evidence | Blocks runtime verification |
|---|---|---|---|
| Safe writable DB | `NOT_CONFIRMED` | Two unapproved name candidates only | Yes |
| Environment isolation | `NOT_CONFIGURED` | Business default remains active; no UAT override | Yes |
| Unit identity | `OWNER_DECISION_REQUIRED` | CI_AS index, CI_AI SP/importer, 205/211 coexist | Yes |
| DB/SP Unit contract | `UNIT_DB_SP_CONTRACT_MISMATCH` | F2016 insert/update CI_AI guard | Yes |
| Safe-UAT F2018 parity | `NOT_EXECUTED` | No approved safe DB | Yes |
| Preview no-write | `NOT_EXECUTED` | No runtime UAT | Yes |
| 100-row commit | `NOT_EXECUTED` | Writes blocked | Yes |
| Row-50 rollback | `NOT_EXECUTED` | Writes blocked | Yes |
| Duplicate race | `NOT_EXECUTED` | Writes blocked | Yes |
| Permission / revocation | `NOT_EXECUTED` | No UAT profiles or host | Yes |
| Excel / upload / browser | `NOT_EXECUTED` | No workbook session or UAT host | Yes |
| Audit / history | `NOT_EXECUTED` | No runtime import | Yes |
| Focused tests / builds | `PASS` | 48 tests; 3 builds, 0 errors | No, but not runtime proof |
| Performance | `NOT_RUN` | Correctness gates remain blocked | Yes |
| Telerik entitlement | `NEED_OWNER_CONFIRMATION` | TKL002/TKL101/TKL004 | Yes |

## 26. Final Verdict

`PRODUCT_IMPORT_PHASE1B_BLOCKED`

No production-ready or runtime-verified claim is made. Continue only after the owner confirms the exact disposable DB and write/cleanup permission, and answers the Unit identity question.

## 27. Read-Only SQL Evidence

All statements below are read-only and were run against the configured business DB, except the first catalog-candidate query, which was run in `master`. No connection string or credential is included.

```sql
SELECT @@SERVERNAME AS ServerName, name AS DatabaseName, state_desc, is_read_only, user_access_desc
FROM sys.databases
WHERE name LIKE '%dev%' OR name LIKE '%uat%' OR name LIKE '%test%'
   OR name LIKE '%sandbox%' OR name LIKE '%import%'
ORDER BY name;

SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName,
       CONVERT(sysname, DATABASEPROPERTYEX(DB_NAME(), 'Collation')) AS DatabaseCollation;

SELECT t.name AS TableName, c.name AS ColumnName, c.collation_name
FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
WHERE t.name='tbl_DM_Don_Vi_Tinh' AND c.name='Ten_Don_Vi_Tinh';

SELECT i.name AS IndexName, i.is_unique, i.is_disabled, ic.key_ordinal,
       c.name AS ColumnName, c.collation_name
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.object_id=OBJECT_ID('dbo.tbl_DM_Don_Vi_Tinh') AND ic.key_ordinal>0;

SELECT u.Auto_ID, u.Ten_Don_Vi_Tinh, COUNT(p.Auto_ID) AS ProductReferenceCount
FROM dbo.tbl_DM_Don_Vi_Tinh u
LEFT JOIN dbo.tbl_DM_San_Pham p ON p.Don_Vi_Tinh_ID=u.Auto_ID
WHERE u.Auto_ID IN (205,211)
GROUP BY u.Auto_ID,u.Ten_Don_Vi_Tinh;

SELECT CASE WHEN N'Cuộn' COLLATE SQL_Latin1_General_CP1_CI_AS=N'Cuốn' COLLATE SQL_Latin1_General_CP1_CI_AS THEN 1 ELSE 0 END AS Equal_CI_AS,
       CASE WHEN N'Cuộn' COLLATE Latin1_General_CI_AI=N'Cuốn' COLLATE Latin1_General_CI_AI THEN 1 ELSE 0 END AS Equal_CI_AI;

SELECT name,
       SUBSTRING(OBJECT_DEFINITION(object_id),
         CASE WHEN CHARINDEX(N'Ten_Don_Vi_Tinh COLLATE', OBJECT_DEFINITION(object_id))>120
              THEN CHARINDEX(N'Ten_Don_Vi_Tinh COLLATE', OBJECT_DEFINITION(object_id))-120 ELSE 1 END,
         420) AS DuplicateCheckSnippet
FROM sys.procedures
WHERE name IN ('F2016_sp_ins_Don_Vi_Tinh','F2016_sp_upd_Don_Vi_Tinh');

SELECT OBJECT_DEFINITION(OBJECT_ID('dbo.F2016_sp_sel_List_Don_Vi_Tinh_Lookup')) AS UnitLookupDefinition;
```

Observed results: server/database `DESKTOP-NHQ7QPL\MSSQLSERVER19` / `TKS_Thuc_Tap_V11_GiaiDoan2`; column/index collation `SQL_Latin1_General_CP1_CI_AS`; unique index enabled; `Equal_CI_AS=0`, `Equal_CI_AI=1`; reference counts 205=`Cuộn`: 3, 211=`Cuốn`: 1; both live F2016 insert/update definitions contain the CI_AI duplicate predicate and `THROW 51002`.
