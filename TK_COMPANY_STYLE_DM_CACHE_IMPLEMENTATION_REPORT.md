# Company Style DM Cache Implementation Report

**Status:** `COMPANY_STYLE_DM_CACHE_IMPLEMENTATION_COMPLETE`

## Scope

Implemented explicit company style caches for the five dedicated DM entities and registered their loaders in the existing Web startup cache sequence. The change adds no Stored Procedures, SQL changes, database writes, business routing, or entity changes.

## Company cache precedent

- `CCache_Chu_Hang` uses static `List<T>`/ID and business key dictionaries, `Load_Cache_*`, `Add_Data`, `Update_Data`, `Delete_Data`, typed getters, lowercase string keys, and sorted `List_Data`.
- `CCache_Chuc_Nang`, `CCache_Nhom_Thanh_Vien`, and `CCache_Thanh_Vien` confirm ID/code indexing and delete-then-add update behavior. `CCache_Chuc_Nang` also demonstrates collection-valued indexes for one-to-many relationships.
- `CCache_Common_Controller` holds cache state flags; it is not a loader registry. `TKS_Thuc_Tap_V11_Web/Program.cs` loads `CCache_Chu_Hang` directly during startup. `Cache_Timer_Service` loads other configuration/system caches and does not reload `CCache_Chu_Hang`.
- Production searches found no external `Add_Data`/`Update_Data`/`Delete_Data` calls for the existing `CCache_Chu_Hang` or `CCache_Chuc_Nang`; their current data is initialized centrally.

## Cache map and key evidence

| Cache | Entity | Load method → Store | Indexes | `List_Data` order |
|---|---|---|---|---|
| `CCache_Kho` | `CDM_Kho` | `Load_Cache_Kho` → `F2009_sp_sel_List_Kho` | `Auto_ID`, `Ten_Kho` | `Ten_Kho` |
| `CCache_Don_Vi_Tinh` | `CDM_Don_Vi_Tinh` | `Load_Cache_Don_Vi_Tinh` → `F2016_sp_sel_List_Don_Vi_Tinh` | `Auto_ID`, `Ten_Don_Vi_Tinh` | `Ten_Don_Vi_Tinh` |
| `CCache_Loai_San_Pham` | `CDM_Loai_San_Pham` | `Load_Cache_Loai_San_Pham` → `F2017_sp_sel_List_Loai_San_Pham` | `Auto_ID`, `Ma_LSP`, `Ten_LSP` | `Ma_LSP` |
| `CCache_San_Pham` | `CDM_San_Pham` | `Load_Cache_San_Pham` → `F2018_sp_sel_List_San_Pham` | `Auto_ID`, `Ma_San_Pham` | `Ma_San_Pham` |
| `CCache_NCC` | `CDM_NCC` | `Load_Cache_NCC` → `F2019_sp_sel_List_NCC` | `Auto_ID`, `Ma_NCC`, `Ten_NCC` | `Ma_NCC` |

Secondary keys are backed by unique indexes in `Database/WarehouseModule.Schema.sql` and matching current import/lookup use:

- Kho name: `UX_tbl_DM_Kho_Ten`; `CWarehouseImportValidator` compares existing warehouses by `Ten_Kho`.
- Unit name: `UX_tbl_DM_Don_Vi_Tinh_Ten`; product import resolves units by `Ten_Don_Vi_Tinh`.
- Category code and name: `UX_tbl_DM_Loai_San_Pham_Ma` and `UX_tbl_DM_Loai_San_Pham_Ten`; product and category import validate both fields.
- Product code: `UX_tbl_DM_San_Pham_Ma`; product import checks existing products by `Ma_San_Pham`.
- Supplier code and name: `UX_tbl_DM_NCC_Ma` and `UX_tbl_DM_NCC_Ten`; supplier import checks both fields.

No index was added for `Ten_San_Pham`, `Loai_San_Pham_ID`, or `Don_Vi_Tinh_ID`. Product category/unit relationships are not represented by a one-to-one dictionary. The list Stores select all rows from their matching master table, return the fields required for the master entities, and use the same order listed above. The reduced Lookup Stores are not used as cache loaders.

String indexes follow the company `.ToLower()` convention. `Add_Data`, `Delete_Data`, and key getters guard null/blank values, avoiding `NullReferenceException` for missing keys.

## Lifecycle and synchronization

The five `Load_Cache_*` calls were added to the existing startup sequence in `TKS_Thuc_Tap_V11_Web/Program.cs`, alongside the established direct `CCache_*` startup loads. No second bootstrap or timer was introduced.

The existing company pattern initializes these caches centrally; no production CRUD caller currently mutates `CCache_Chu_Hang` or `CCache_Chuc_Nang`. The new DM cache APIs implement add/update/delete index maintenance, but the DM save and import services were not changed to write the caches. Thus no cache mutation can occur before an import transaction commits, and transaction semantics remain unchanged. Current DM screens/import services have not been redirected to read from these new caches; they continue using their existing Store paths. If cache reads are wired into write flows later, synchronization should occur only after a successful database commit or through an established reload path.

## TDD and verification

- **RED:** focused `WarehouseDmCacheContractTests` run failed 10/10 because the five cache types were absent.
- **GREEN:** focused cache contract and in-memory behavior tests passed 10/10. They cover typed APIs, startup registration, approved full List Stores, ID/key indexes, duplicate/zero IDs, update/delete cleanup, case-insensitive lookup, sort order, and null/empty key handling. No database Store was executed.
- **Focused regression:** 155 passed, 0 failed, 0 skipped across the new cache tests, master decomposition/finalization contracts, Warehouse UI workflow, and DM import validation/UI tests.
- **Data Access build:** succeeded, 0 warnings, 0 errors.
- **Web build:** succeeded, 24 warnings, 0 errors.
- **Tests build:** succeeded, 12 warnings, 0 errors.
- **Runtime database cache load:** not run; the tests avoid the shared development database. The Store definitions and loader method calls were verified from checked-in source.
- **Coverage:** not measured; no coverage instrumentation was run.

## Change and state confirmation

- SQL source and database objects were not modified or accessed for writes.
- Existing CRUD validation, transactions, audit values, errors, routes, and Store parameters were not changed.
- The six pre-existing untracked refactor-plan/TDD artifacts were left untouched. No files were staged or committed for this task.

## Required confirmations

```text
DM_KHO_CACHE_IMPLEMENTED
DM_DON_VI_TINH_CACHE_IMPLEMENTED
DM_LOAI_SAN_PHAM_CACHE_IMPLEMENTED
DM_SAN_PHAM_CACHE_IMPLEMENTED
DM_NCC_CACHE_IMPLEMENTED

CACHE_USES_DEDICATED_DM_ENTITIES
CACHE_DOES_NOT_USE_CWarehouseMaster
CACHE_DOES_NOT_USE_CWarehouseLookup

CACHE_LOAD_SOURCE_VERIFIED
CACHE_SECONDARY_KEYS_EVIDENCE_BASED

CACHE_TRANSACTION_SAFETY_PRESERVED
NO_PRECOMMIT_CACHE_MUTATION

SQL_UNCHANGED
DATABASE_UNCHANGED
NO_BUSINESS_LOGIC_CHANGE

COMPANY_CACHE_STYLE_PRESERVED
```
