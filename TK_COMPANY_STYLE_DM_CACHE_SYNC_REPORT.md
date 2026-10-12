# Company-Style DM Cache Sync Report

**Status:** `COMPANY_STYLE_DM_CACHE_SYNC_COMPLETE`  
**Date:** 2026-10-12

## Architecture applied

Followed `CCache_Chu_Hang` and `CDM_Chu_Hang_Controller`: each cache now loads through one synchronous controller method whose name matches one dedicated Stored Procedure. The five procedures read the canonical `dbo.tbl_DM_*` tables, return the full cache entity fields under their entity property names, preserve the normal List ordering, and add no filter or paging.

| Entity | Dedicated Store and controller method | Cache |
|---|---|---|
| Kho | `F2009_sp_sel_List_Kho_For_Cache` | `CCache_Kho` |
| Đơn vị tính | `F2016_sp_sel_List_Don_Vi_Tinh_For_Cache` | `CCache_Don_Vi_Tinh` |
| Loại sản phẩm | `F2017_sp_sel_List_Loai_San_Pham_For_Cache` | `CCache_Loai_San_Pham` |
| Sản phẩm | `F2018_sp_sel_List_San_Pham_For_Cache` | `CCache_San_Pham` |
| Nhà cung cấp | `F2019_sp_sel_List_NCC_For_Cache` | `CCache_NCC` |

The existing normal List, Lookup, and CRUD Stores remain in place. No menu/function-master code, view, table, or schema object was added. `Program.cs` startup cache registration was already present and remains unchanged.

## CRUD cache synchronization

`FWarehouse_1_Warehouse_List.razor` synchronizes each cache only after its corresponding database call returns successfully:

- Insert assigns the returned `Auto_ID` before calling `Add_Data`.
- Update calls `Update_Data` after the update Store returns.
- Delete calls `Delete_Data` after the delete Store returns.

The UI workflow invokes the save delegate inside its existing `try`; a Store exception exits the delegate before cache mutation. Cache synchronization stays outside the low-level Data Access controllers.

## Import synchronization and refresh failures

The five master import services reload their matching cache once, after `SqlTransaction.Commit()`. The reload is outside the transaction `catch`/rollback block. A row or commit failure therefore rolls back and rethrows before any cache operation.

If a post-commit reload fails, the service raises an error whose message says the import **already committed** and identifies the cache refresh failure. The existing Razor import components display that message. This path does not attempt rollback; a later cache load or application restart can recover the derived cache.

## Security and database verification

The current `WarehouseModule.Security.sql` grant list does not explicitly grant the existing F2009/F2016/F2017/F2018/F2019 master List procedures. No security file change was needed, and no permission was broadened. The five new procedures executed successfully through the configured application connection.

Before deployment, `SELECT DB_NAME()` returned exactly `TKS_Thuc_Tap_V11_GiaiDoan2`; the five target names were absent. Deployment rechecked the database name before each procedure creation and committed the five new procedures in one transaction. No data rows were written or changed.

After deployment, each live definition matched the checked-in source after normalizing SQL Server's harmless `CREATE OR ALTER`/`CREATE` spacing. Each cache procedure's ordered results matched its corresponding normal List Store:

| Store | Normal List rows | Cache rows | Result parity |
|---|---:|---:|---|
| Kho | 4 | 4 | Match |
| Đơn vị tính | 10 | 10 | Match |
| Loại sản phẩm | 5 | 5 | Match |
| Sản phẩm | 15 | 15 | Match |
| Nhà cung cấp | 5 | 5 | Match |

## TDD and verification evidence

- **RED:** the 15 focused cache-sync tests failed before implementation because the dedicated Stores/methods, CRUD synchronization, and post-commit reloads were missing.
- **GREEN:** the same focused tests passed, 15/15.
- **Regression:** the cache contracts, master decomposition/finalization contracts, Warehouse UI workflow, and all five entity import UI/validation suites passed, 159/159.
- **Builds:** Data Access, Web Danh Mục, Web, and Tests all succeeded with zero compile errors. Existing Telerik licensing and analyzer warnings remain.
- `git diff --check` found no whitespace errors. Git reported only its LF-to-CRLF normalization notices.
- Live DB procedure execution and result parity were verified. No database-writing import batch was run.
- Coverage percentage was not measured: the test project has no configured coverage collector and no `dotnet-coverage` tool is installed. No coverage dependency was added.

## SQL artifacts and worktree

- Procedure source: `Database/WarehouseModule.Procedures.sql`
- Safe rollback script: `TK_COMPANY_STYLE_DM_CACHE_SYNC_ROLLBACK.sql` — checks the target database before each conditional drop and names only these five new cache procedures.
- This report: `TK_COMPANY_STYLE_DM_CACHE_SYNC_REPORT.md`
- Only task-related source, tests, and the two required SQL/report artifacts are changed. No unrelated files were modified. No checkpoint commit was created, following the supplied AGENTS.md Git inspection-only rule.

## Confirmations

`DM_KHO_CACHE_STORE_SPLIT_COMPLETE`  
`DM_DON_VI_TINH_CACHE_STORE_SPLIT_COMPLETE`  
`DM_LOAI_SAN_PHAM_CACHE_STORE_SPLIT_COMPLETE`  
`DM_SAN_PHAM_CACHE_STORE_SPLIT_COMPLETE`  
`DM_NCC_CACHE_STORE_SPLIT_COMPLETE`

`DEDICATED_CACHE_CONTROLLER_METHODS_COMPLETE`  
`CACHE_LOADERS_USE_FOR_CACHE_STORES`  
`DM_CRUD_CACHE_SYNC_COMPLETE`  
`CACHE_INSERT_SYNC_AFTER_DB_SUCCESS`  
`CACHE_UPDATE_SYNC_AFTER_DB_SUCCESS`  
`CACHE_DELETE_SYNC_AFTER_DB_SUCCESS`

`IMPORT_CACHE_SYNC_AFTER_COMMIT`  
`NO_PRECOMMIT_CACHE_MUTATION`  
`ROLLBACK_DOES_NOT_MUTATE_CACHE`  
`CACHE_DATABASE_SOURCE_OF_TRUTH_PRESERVED`

`NORMAL_LIST_STORES_PRESERVED`  
`LOOKUP_STORES_PRESERVED`  
`CRUD_STORES_PRESERVED`  
`NO_BUSINESS_LOGIC_CHANGE`
