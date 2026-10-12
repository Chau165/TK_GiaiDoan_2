# XNK Implementation Closure Report

- **Final status:** `COMPANY_STYLE_XNK_IMPLEMENTATION_PARTIAL`
- **Required action:** `LIVE_DETAIL_STORE_FIXTURE_REQUIRED_FOR_END_TO_END_CLOSE`

## Scope and starting status

Starting status was `COMPANY_STYLE_XNK_IMPLEMENTATION_PARTIAL`. This closure covers only the read projections of:

- `F3001_sp_sel_List_Nhap_Kho`
- `F3001_sp_sel_List_Nhap_Kho_Detail`
- `F3002_sp_sel_List_Xuat_Kho`
- `F3002_sp_sel_List_Xuat_Kho_Detail`

No source code, SQL file, or business data was changed by this verification. The user ran the deployment script in SSMS; the live database now confirms the four procedure updates. Existing XNK and DM working-tree changes remain preserved.

## Live database and deployment verification

Connected read-only to `DESKTOP-NHQ7QPL\MSSQLSERVER19`. `DB_NAME()` returned exactly `TKS_Thuc_Tap_V11_GiaiDoan2`.

All four target procedures exist in `dbo`. Their live `modify_date` values are between `2026-10-12 17:01:19.950` and `2026-10-12 17:01:19.957`, consistent with the SSMS run shown by the user. Each live `sys.sql_modules.definition` matches `Database/WarehouseModule.Procedures.sql` after normalizing only the DDL prefix (`CREATE OR ALTER PROCEDURE` in source versus SQL Server's stored `CREATE   PROCEDURE`) and line endings. No procedure body differences remain. `SOURCE_LIVE_PARITY_PASS` for all four.

Live parameter metadata also matches the source order and types:

| Store group | Live parameters |
|---|---|
| Header list | `@Ma_Dang_Nhap NVARCHAR(100)`, `@Kho_ID BIGINT` (source default `NULL`) |
| Detail list | `@Document_ID BIGINT`, `@Ma_Dang_Nhap NVARCHAR(100)` |

### Full live result-column contract

`sys.dm_exec_describe_first_result_set_for_object` returned the following complete first-result metadata. Names and SQL types match the checked-in source and typed entity properties.

| Store | Live result columns in ordinal order (`name type`) | Status |
|---|---|---|
| `F3001_sp_sel_List_Nhap_Kho` | `Auto_ID bigint`; `Is_Receipt bit`; `So_Phieu_Nhap_Kho nvarchar(100)`; `Kho_ID bigint`; `Ten_Kho nvarchar(255)`; `NCC_ID bigint`; `Ten_NCC nvarchar(255)`; `Ngay_Nhap_Kho date`; `Is_Posted bit`; `Ghi_Chu nvarchar(1000)` | PASS |
| `F3001_sp_sel_List_Nhap_Kho_Detail` | `Auto_ID bigint`; `Nhap_Kho_ID bigint`; `San_Pham_ID bigint`; `Ma_San_Pham nvarchar(100)`; `Ten_San_Pham nvarchar(255)`; `Ten_Don_Vi_Tinh nvarchar(200)`; `SL_Nhap decimal(18,3)`; `Don_Gia_Nhap decimal(18,2)` | PASS |
| `F3002_sp_sel_List_Xuat_Kho` | `Auto_ID bigint`; `Is_Receipt bit`; `So_Phieu_Xuat_Kho nvarchar(100)`; `Kho_ID bigint`; `Ten_Kho nvarchar(255)`; `NCC_ID bigint`; `Ten_NCC nvarchar(255)`; `Ngay_Xuat_Kho date`; `Is_Posted bit`; `Ghi_Chu nvarchar(1000)` | PASS |
| `F3002_sp_sel_List_Xuat_Kho_Detail` | `Auto_ID bigint`; `Xuat_Kho_ID bigint`; `San_Pham_ID bigint`; `Ma_San_Pham nvarchar(100)`; `Ten_San_Pham nvarchar(255)`; `Ten_Don_Vi_Tinh nvarchar(200)`; `SL_Xuat decimal(18,3)`; `Don_Gia_Xuat decimal(18,2)` | PASS |

`TK_COMPANY_STYLE_XNK_RESULT_CONTRACT_DEPLOY.sql` contains only these four scoped procedure definitions; the user's SSMS screenshot shows that script ran successfully. Live definitions and result metadata now verify the deployment. The rollback artifact remains available but was not run.

The previous live baseline had generic aliases (`So_Phieu`, `Ngay_Chung_Tu`, `Document_ID`, `So_Luong`, `Don_Gia`). Those aliases have been replaced by the typed names above; no current mismatch was found.

## Direct mapping and live read results

All four typed read methods call `CUtility.Map_Row_To_Entity<T>(v_row)` directly; no manual alias translation is present. The focused `WarehouseXnkSplitContractTests` include `Representative_typed_rows_map_without_manual_alias_translation`, which passed using representative in-memory rows. `SOURCE_AND_SYNTHETIC_MAPPER_PASS`.

Read-only runtime results:

| Store | Execution | Rows | Result |
|---|---|---:|---|
| `F3001_sp_sel_List_Nhap_Kho` | Valid authorized login and warehouse | 0 | PASS; typed columns returned |
| `F3002_sp_sel_List_Xuat_Kho` | Valid authorized login and warehouse | 0 | PASS; typed columns returned |
| `F3001_sp_sel_List_Nhap_Kho_Detail` | Not called | — | No valid receipt document exists in this database |
| `F3002_sp_sel_List_Xuat_Kho_Detail` | Not called | — | No valid issue document exists in this database |

The live database has `0` receipt headers, `0` receipt details, `0` issue headers, and `0` issue details. Therefore no live detail `DataRow` exists to pass through `Map_Row_To_Entity`; direct live row mapping is `NOT_VERIFIED_NO_LIVE_ROWS`. No placeholder document IDs or business rows were inserted.

## Behavior and protected Stores

For each target, the live definition matches the checked-in source. Comparing the checked-in source against the rollback definitions after reversing only the typed output-alias substitutions produced exact body matches for all four Stores. This confirms that selected expressions, joins, filters, parameter use, authorization calls, and `ORDER BY` clauses are unchanged in source. Runtime row-set, value, and ordering comparisons cannot be exercised because all four XNK data tables are empty.

The deployment script contains DDL only for the four target procedures. A read-only catalog check found no protected procedure with the same `modify_date` as those four updated procedures:

| Protected area | Live objects checked | Same target modification timestamp |
|---|---:|---:|
| `sp_XNK_Document_Page` / `sp_XNK_Document_Post` | 2 | 0 |
| `sp_Inventory_*` | 35 | 0 |
| Reservation technical procedures | 5 | 0 |
| DM Stores | 56 | 0 |

The authorized header-list calls succeeded, confirming the supplied warehouse authorization path for those reads. Detail authorization and non-empty row behavior remain unverified because no documents exist. No Page/Post, Inventory, Reservation, or DM Store was executed or changed by this verification.

## Tests and builds

Focused source/unit regression command covered the XNK split contracts, import preview, authorization contracts, UI workflow, and the prior group-B/group-C/module structure contracts:

```text
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~WarehouseXnkSplitContractTests|FullyQualifiedName~TransactionImportPreviewTests|FullyQualifiedName~WarehouseAuthorizationContractTests|FullyQualifiedName~WarehouseUiWorkflowTests|FullyQualifiedName~WarehouseGroupBSplitContractTests|FullyQualifiedName~WarehouseGroupCDesharingContractTests|FullyQualifiedName~WarehouseModuleStructureTests"
```

**Result: 89 passed, 0 failed, 0 skipped.** The selected suite includes the four entity mapping contract checks using representative in-memory rows; those pass without manual alias mapping. Live detail-row mapping remains unverified because the database has no documents. The selected tests do not execute destructive database operations. `COVERAGE_NOT_MEASURED`.

| Project | Result | Warnings |
|---|---|---|
| Data Access | PASS, 0 errors | 0 |
| Data Access Tests | PASS, 0 errors | 12 Telerik licensing warnings (`TKL002`, `TKL004`, `TKL101`) |
| Web Danh Muc | PASS, 0 errors | 12 Telerik licensing warnings (`TKL002`, `TKL004`, `TKL101`) |
| Web | PASS, 0 errors | 24 Telerik licensing warnings (`TKL002`, `TKL004`, `TKL101`) |

The Telerik license warnings match the existing warning categories (`TKL002`, `TKL004`, `TKL101`). Destructive XNK integration tests were not run because no isolated XNK integration database was confirmed: `DESTRUCTIVE_XNK_INTEGRATION_NOT_RUN_NO_ISOLATED_DB`.

## Worktree checks

This verification updated only this closure report. No production source, SQL file, or business data was changed by the verification. No staging, commit, reset, restore, stash, or source cleanup was performed.

## Required confirmations

| Confirmation | Result |
|---|---|
| `DB_NAME_PREFLIGHT_PASS` | PASS; exact live database confirmed |
| `F3001_LIST_LIVE_DEPLOYED` | PASS; exists and source parity |
| `F3001_DETAIL_LIVE_DEPLOYED` | PASS; exists and source parity |
| `F3002_LIST_LIVE_DEPLOYED` | PASS; exists and source parity |
| `F3002_DETAIL_LIVE_DEPLOYED` | PASS; exists and source parity |
| `ALL_TYPED_RESULT_COLUMNS_LIVE` | PASS; full four-Store metadata matches source/entity |
| `HEADER_LIST_READS_LIVE` | PASS; valid authorized login, 0 rows in each list |
| `DETAIL_STORE_READS_LIVE` | NOT RUN; no valid receipt or issue document exists |
| `DIRECT_TYPED_DATAROW_MAPPING_LIVE_VERIFIED` | NOT VERIFIED; live tables contain no rows |
| `REPRESENTATIVE_ENTITY_MAPPING_TESTS` | PASS; included in 89/89 regression |
| `NO_MANUAL_XNK_ALIAS_MAPPING` | PASS; controllers call the generic mapper directly |
| `ROW_SET_AND_LOGICAL_VALUE_INVARIANTS` | PASS by exact source/rollback comparison; runtime data empty |
| `ORDERING_INVARIANT` | PASS by exact source/rollback comparison; no live rows to exercise ordering |
| `WAREHOUSE_AUTHORIZATION_INVARIANT` | PASS in definitions; authorized header reads succeeded |
| `PAGE_POST_INVENTORY_RESERVATION_DM_UNCHANGED` | PASS; deployment scope plus catalog timestamps |
| `DM_PREVIOUS_WORK_PRESERVED` | YES |
| `OLD_DOCUMENT_CONTROLLER_REMAINS_RETIRED` | PASS; source contract test passed |
| `NO_XNK_TRANSACTION_CACHE_CREATED` | PASS by source review |
| `DESTRUCTIVE_XNK_INTEGRATION_NOT_RUN_NO_ISOLATED_DB` | YES |

**Final status remains `COMPANY_STYLE_XNK_IMPLEMENTATION_PARTIAL`.** All four procedures are deployed, match source, and expose the complete typed result contract; regression and relevant builds pass. The remaining closure blocker is live data availability: the database has no receipt/issue headers or details, so the two detail Stores cannot be called with valid IDs and no live row can be passed through the mapper. Keep the current status until read-only detail calls and direct mapping can be verified against existing authorized documents; do not insert business data to manufacture that evidence.
