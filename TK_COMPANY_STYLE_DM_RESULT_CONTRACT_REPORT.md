# DM List / Lookup Result Contract Report

**Status:** `COMPANY_STYLE_DM_RESULT_CONTRACT_COMPLETE`  
**Date:** 2026-10-12

## Scope and caller audit

Reviewed the ten dedicated DM List/Lookup procedures and all matching procedure-name references in the repository. The active callers return or consume the dedicated `CDM_*` entity types. Tests for every procedure are in `WarehouseDmResultContractTests` and the master entity/finalization contract suites. No active generic Page, Document, Permission, Report, or other caller of these ten procedures was found; there is no `GENERIC_RESULT_CONSUMER_BLOCKER`.

| Store | Classification | Active consumers |
|---|---|---|
| `F2009_sp_sel_List_Kho` | `TYPED_DM_ENTITY`, `TEST` | `FWarehouse_1_Warehouse_List.razor`; `CWarehouseKhoImportService` preview/revalidation; result/entity contract tests |
| `F2009_sp_sel_List_Kho_Lookup` | `TYPED_DM_ENTITY`, `TEST` | `FWarehouse_1_Warehouse_List.razor` dropdown/reference data; result/entity contract tests |
| `F2016_sp_sel_List_Don_Vi_Tinh` | `TYPED_DM_ENTITY`, `TEST` | `FWarehouse_1_Warehouse_List.razor`; Unit/Product/Transaction import services; result/entity contract tests |
| `F2016_sp_sel_List_Don_Vi_Tinh_Lookup` | `TYPED_DM_ENTITY`, `TEST` | `FWarehouse_1_Warehouse_List.razor` dropdown/reference data; result/entity contract tests |
| `F2017_sp_sel_List_Loai_San_Pham` | `TYPED_DM_ENTITY`, `TEST` | `FWarehouse_1_Warehouse_List.razor`; Category/Product import services; result/entity contract tests |
| `F2017_sp_sel_List_Loai_San_Pham_Lookup` | `TYPED_DM_ENTITY`, `TEST` | `FWarehouse_1_Warehouse_List.razor` dropdown/reference data; result/entity contract tests |
| `F2018_sp_sel_List_San_Pham` | `TYPED_DM_ENTITY`, `TEST`, `BENCHMARK` | `FWarehouse_1_Warehouse_List.razor`; `PerformanceBenchmark` reads only the returned count; result/entity contract tests |
| `F2018_sp_sel_List_San_Pham_Lookup` | `TYPED_DM_ENTITY`, `TEST`, `BENCHMARK` | `FWarehouse_1_Warehouse_List.razor` dropdown; `PerformanceBenchmark` reads only the returned count; result/entity contract tests |
| `F2019_sp_sel_List_NCC` | `TYPED_DM_ENTITY`, `TEST` | `FWarehouse_1_Warehouse_List.razor`; Supplier/Transaction import services; result/entity contract tests |
| `F2019_sp_sel_List_NCC_Lookup` | `TYPED_DM_ENTITY`, `TEST` | `FWarehouse_1_Warehouse_List.razor` dropdown/reference data; result/entity contract tests |

Tests reference these contracts by name or exercise the typed controller methods. The generic shared page flows continue to call `sp_DM_Master_Page` and `sp_DM_Lookup_Page`; they do not call these ten Stores. Repository search found no other active runtime callsites. External consumers outside this repository were not independently inventoried.

## Result contracts

Before deployment, each List Store returned `Auto_ID, Code, Name, Related_ID, Related_ID_2, Login_Name, Ghi_Chu`; each Lookup Store returned `Auto_ID, Code, Name`.

| Store | Result columns after change | Rows before → after | Logical values |
|---|---|---:|---|
| `F2009_sp_sel_List_Kho` | `Auto_ID, Ten_Kho, Ghi_Chu` | 4 → 4 | Match |
| `F2009_sp_sel_List_Kho_Lookup` | `Auto_ID, Ten_Kho` | 4 → 4 | Match |
| `F2016_sp_sel_List_Don_Vi_Tinh` | `Auto_ID, Ten_Don_Vi_Tinh, Ghi_Chu` | 10 → 10 | Match |
| `F2016_sp_sel_List_Don_Vi_Tinh_Lookup` | `Auto_ID, Ten_Don_Vi_Tinh` | 10 → 10 | Match |
| `F2017_sp_sel_List_Loai_San_Pham` | `Auto_ID, Ma_LSP, Ten_LSP, Ghi_Chu` | 5 → 5 | Match |
| `F2017_sp_sel_List_Loai_San_Pham_Lookup` | `Auto_ID, Ma_LSP, Ten_LSP` | 5 → 5 | Match |
| `F2018_sp_sel_List_San_Pham` | `Auto_ID, Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu` | 15 → 15 | Match |
| `F2018_sp_sel_List_San_Pham_Lookup` | `Auto_ID, Ma_San_Pham, Ten_San_Pham` | 15 → 15 | Match |
| `F2019_sp_sel_List_NCC` | `Auto_ID, Ma_NCC, Ten_NCC, Ghi_Chu` | 5 → 5 | Match |
| `F2019_sp_sel_List_NCC_Lookup` | `Auto_ID, Ma_NCC, Ten_NCC` | 5 → 5 | Match |

The changes replace only the projection aliases/placeholder columns. Tables, selected business values, row order, `ORDER BY`, parameter lists, filters, and data types are unchanged. The source contract tests require the expected projection, same table/order, and no `WHERE` clause or forbidden generic aliases.

## Controller mapping

Removed 18 manual `DataRow` assignments from the List/Lookup methods in:

- `CDM_Kho_Controller`
- `CDM_Don_Vi_Tinh_Controller`
- `CDM_Loai_San_Pham_Controller`
- `CDM_San_Pham_Controller`
- `CDM_NCC_Controller`

All ten methods now map each row directly with `CUtility.Map_Row_To_Entity<TEntity>()`. Mapping tests create List-shaped and Lookup-shaped `DataTable` rows for all five entities and verify the business properties, including product category and unit IDs. No `v_row[...]` alias translations remain in these methods.

## Preserved areas and database state

- The five `*_For_Cache` procedures, `sp_DM_Master_Page`, and `sp_DM_Lookup_Page` were not edited in source. Source-hash tests verify their procedure blocks remain unchanged.
- Live SHA-256 hashes for the two Page Stores and five Cache Stores match their pre-deployment hashes. Cache Store source/live definitions also match.
- No CRUD Store, cache class, cache lifecycle callback, import synchronization path, table, or schema object changed in this task.
- The only database changes were `ALTER` operations for the ten target List/Lookup procedures, in one transaction. The script checked `DB_NAME()` before each procedure alteration and before commit: all returned `TKS_Thuc_Tap_V11_GiaiDoan2` (11 checks). No DML or import batch ran.
- Read-only execution after deployment confirmed the exact expected result columns, identical before/after row counts, and equal SHA-256 fingerprints of the logical projected values for all ten Stores.
- All ten changed procedure definitions match the checked-in source after normalizing SQL Server's `CREATE OR ALTER` header formatting.

**Pre-existing out-of-scope drift:** `sp_DM_Master_Page` did not match its checked-in source definition before this task. Its source block and live definition were both unchanged during this task; its live hash is identical before and after. `sp_DM_Lookup_Page` source/live definition also remained unchanged.

## TDD, tests, and builds

- **RED:** new contract tests initially failed on the old generic result names and manual controller assignments; the direct mapping test already passed.
- **GREEN:** `WarehouseDmResultContractTests` passed 8/8.
- **Targeted regression:** master decomposition/finalization, cache contracts/sync, Warehouse UI workflow, all five entity import validation/UI suites, and the new result-contract tests passed **167/167**.
- **Builds:** Data Access, Web Danh Mục, Web, and Tests built with zero compile errors. The regular Web output path was locked by the running Web process; Web built successfully to an isolated temporary output with `--no-dependencies`, and that temporary directory was removed. The running process was left untouched.
- `git diff --check` passed. Existing Telerik license and line-ending notices are unrelated.
- `WarehouseCrudContractIntegrationTests` and `MasterDataDeleteIntegrationTests` were not run: both use `WarehouseTestDatabase`, whose default connection points to the target database and whose tests insert/delete data. `TKS_INTEGRATION_CONNECTION_STRING` was unset, so no isolated integration database was configured. No mutating integration test was run against the target database.

## Rollback and worktree

- Rollback artifact: `TK_COMPANY_STYLE_DM_RESULT_CONTRACT_ROLLBACK.sql`. It contains exactly the ten previous List/Lookup procedure definitions, checks `DB_NAME()` before each restore, and does not alter Page, Cache, CRUD, or data objects.
- The worktree already contained the preceding DM cache-sync changes. They were preserved. This task adds the result-contract test, report, and rollback artifact, and updates only the ten List/Lookup projections plus the five List/Lookup controller mappings.
- No commit was created, following the supplied `AGENTS.md` Git inspection-only rule.

## Confirmations

`DM_LIST_RESULT_NAMES_ALIGNED`  
`DM_LOOKUP_RESULT_NAMES_ALIGNED`  
`DM_LIST_DIRECT_ENTITY_MAPPING_COMPLETE`  
`DM_LOOKUP_DIRECT_ENTITY_MAPPING_COMPLETE`  
`NO_MANUAL_ALIAS_MAPPING_IN_NORMALIZED_DM_CONTROLLERS`  
`CACHE_STORES_UNCHANGED`  
`PAGE_STORES_UNCHANGED`  
`CRUD_STORES_UNCHANGED`  
`CACHE_SYNC_UNCHANGED`  
`ROW_SET_UNCHANGED`  
`VALUE_SEMANTICS_UNCHANGED`  
`DATABASE_DATA_UNCHANGED`

**Final status:** `COMPANY_STYLE_DM_RESULT_CONTRACT_COMPLETE`
