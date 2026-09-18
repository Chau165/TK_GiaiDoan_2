# Warehouse Permission Refactor Review Report

Review date: 2026-09-11  
Review basis: working tree at `HEAD 7db2192`, including the Warehouse refactor files currently present but not yet tracked. Unrelated pre-existing benchmark/performance working-tree changes were not attributed to this refactor.  
Review scope: source, SQL scripts, CodeGraph call/dependency tracing, build, and tests. No source, SQL, schema, naming, or commit changes were made during this review. Only this report was created.

## 1. Executive Summary

**Conclusion: PASS_WITH_WARNING**

The scoped refactor is implemented correctly for the active Warehouse UI flow:

- Warehouse permission rows are now represented by `CWarehousePermission`.
- Permission list/page/save/delete operations are routed through `CWarehousePermission_Controller`.
- The active UI no longer calls `CWarehouseMaster_Controller` for `KhoUser` list/page/save/delete.
- `CWarehouseMaster_Controller` no longer contains a dedicated `KhoUser` branch or permission save mapping.
- SQL/schema files were not modified; the existing SQL compatibility branches remain intact.
- The permission filter preserves the previous duplicate-assignment behavior.

Warnings prevent an unconditional `PASS`:

1. `CWarehouseMaster_Controller` still contains `List_Authorized_Warehouses_Async` and `List_User_Lookup_Async`, which are lookup/authorization concerns rather than pure master CRUD.
2. The generic string APIs and SQL compatibility branches still allow an old external caller to route `KhoUser` through the master contract. No such internal caller was found.
3. Full integration verification is not available in this environment: 45 tests stopped at the disposable-database guard because `TKS_INTEGRATION_CONNECTION_STRING` did not point to an approved disposable test database.

These are boundary/verification warnings, not an observed functional regression in the migrated UI path.

## 2. Architecture Review

### Master boundary

`CWarehouseMaster_Controller` now owns the ordinary master list/page/save/delete operations. Its save switch contains only:

- `DonViTinh`
- `LoaiSanPham`
- `SanPham`
- `NCC`
- `Kho`

The source has no dedicated `KhoUser` branch in `List_Master_Async`, `List_Master_Page_Async`, or `Save_Master_Async`.

The Warehouse list component separates the grid and callback paths:

- ordinary master grid: `CWarehouseMaster`
- permission grid: `CWarehousePermission`
- ordinary master callbacks: `Save_Master_Async` and `Delete_Master_Async`
- permission callbacks: `Save_Kho_User_Async` and `Delete_Kho_User_Async`

This is the correct boundary for the active UI.

### Permission boundary

`CWarehousePermission_Controller` is a focused Warehouse permission controller. It calls the permission-specific procedures directly and maps the legacy SQL column names into a permission-specific model. No Repository or Service layer was added, matching the current focused Warehouse controller style.

### Remaining boundary warning

The following methods remain on `CWarehouseMaster_Controller`:

- `List_Authorized_Warehouses_Async` -> `sp_DM_Kho_User_List_Allowed`
- `List_User_Lookup_Async` -> `sp_DM_Kho_User_User_List`

They are not active `KhoUser` CRUD routes, but they are authorization/user lookup responsibilities. If “Master Controller only handles Master Data” is interpreted literally, these methods should be moved in a later boundary cleanup. The current implementation deliberately kept them to avoid changing document/report lookup behavior.

## 3. CWarehouseMaster Review

### Current properties

`CWarehouseMaster` currently contains:

- `Auto_ID`
- `Code`
- `Name`
- `Related_ID`
- `Related_ID_2`
- `Login_Name`
- `Created_By`
- `Created_By_Function`
- `Last_Updated_By`
- `Last_Updated_By_Function`
- `Created`
- `Last_Updated`
- `Ghi_Chu`

### KEEP

- `Auto_ID`, `Code`, `Name`, and `Ghi_Chu`: master identity and descriptive data.
- `Related_ID`, `Related_ID_2`: generic master relationships; they are used by `SanPham` for category/unit relationships and are not permission-specific.
- `Created`, `Last_Updated`, and audit actor/function fields: master audit data.

### REMOVE_LATER

- `Login_Name`: this is the remaining permission-shaped property in the generic model. It is no longer used by the active permission UI, but it remains because the generic master SQL contract still exposes a `Login_Name` column and the compatibility path can still populate it for `KhoUser`.

There is no `Permission_ID`, `User_Name`, `Warehouse_ID`, or `Warehouse_Name` property in `CWarehouseMaster`. The active permission UI uses the separate model instead. Removing `Login_Name` now would require a separate SQL contract/API compatibility decision and should not be bundled into this review.

## 4. KhoUser Usage Review

### Exact Master API caller search

Source search (excluding this report and generated build output) found **zero active C# callers** for:

```text
List_Master_Async("KhoUser")
List_Master_Page_Async("KhoUser")
Save_Master_Async("KhoUser")
Delete_Master_Async("KhoUser")
```

This report repeats those strings as evidence, so a literal whole-repository search performed after the report is created will also match the report text itself.

The dynamic calls in `FWarehouse_1_Warehouse_List.razor` pass `m_strMaster_Type`, but they are attached to the ordinary-master grid/callback branch. The `KhoUser` branch calls the permission controller directly.

### Remaining `KhoUser` occurrences

| Location | Purpose | Classification |
|---|---|---|
| `CWarehousePermission_Controller.cs:40` | Passes the entity token to the existing generic delete procedure | ACCEPTABLE compatibility with `sp_DM_Delete` |
| `FWarehouse_1_Warehouse_List.razor` and child components | UI/presentation discriminator for the existing tab/editor mode | ACCEPTABLE, not a Master API call |
| `Database/WarehouseModule.Procedures.sql:2146, 2170-2171, 2225-2228` | Existing delete/list/page SQL compatibility branches | ACCEPTABLE for backward compatibility; optional Phase 2 cleanup |

`CWarehouse_Controller` still exposes generic master methods accepting a string. No internal caller of that compatibility facade was found. A compiled consumer outside this repository cannot be ruled out from source inspection alone.

## 5. Permission Controller Review

### Naming

`CWarehousePermission_Controller` and `CWarehousePermission` clearly communicate the new boundary. The methods retain the existing storage/business vocabulary:

- `List_Kho_User_Async`
- `List_Kho_User_Page_Async`
- `Save_Kho_User_Async`
- `Delete_Kho_User_Async`

This is acceptable for the current company style because existing Warehouse procedures and UI use `Kho_User`. `List_User_Warehouse_Permission_Async` would be more explicit, but renaming is optional cleanup, not a defect.

### Controller pattern

The controller matches the newer Warehouse controllers:

- inherits `CWarehouse_Controller_Base`;
- calls stored procedures directly;
- uses `Task.FromResult` for reads and `Task.CompletedTask` for writes;
- does not introduce Repository or Service abstractions;
- keeps data mapping at the controller boundary.

This differs in syntax from the older `CSys_Phan_Quyen_Chuc_Nang_Controller` and `CSys_Nhom_Thanh_Vien_User_Controller`, but it matches `CWarehouseDocument_Controller` and `CWarehouseReport_Controller`, which is the more relevant local convention.

### Stored procedure mapping

| Operation | Procedure | Review result |
|---|---|---|
| List | `sp_DM_Kho_User_List` | Correct. Maps `Auto_ID` -> `Permission_ID`, `Name` -> `User_Name`, `Login_Name` -> `Login_Name`, `Related_ID` -> `Warehouse_ID`, `Ghi_Chu` -> `Warehouse_Name`. |
| Page | `sp_DM_Kho_User_Page` | Correct. Reads result set 0 as `Total_Count` and result set 1 as page rows using the same mapping. |
| Save | `sp_DM_Kho_User_Save` | Correct positional order: ID, login, warehouse ID, created actor/function, updated actor/function. The returned ID is written back to `Permission_ID`. |
| Delete | `sp_DM_Delete` with `KhoUser` | Correct and intentionally preserves the existing generic delete contract. |

The manual mapping is necessary because the unchanged SQL procedures return legacy aliases rather than the new model property names.

## 6. UI Regression Review

### Permission data display

The permission grid now binds to `CWarehousePermission` and preserves:

- Login name;
- user display name;
- warehouse display name.

The information dialog also reads these fields from the permission model. The action-history reference uses `Permission_ID` for permission rows and `Auto_ID` for master rows.

### Duplicate-assignment filter

`CWarehousePermissionFilter` now compares:

- `Login_Name` case-insensitively;
- `Permission_ID` to exclude the current row while editing;
- `Warehouse_ID` against the complete warehouse lookup list.

This preserves the old behavior while removing the previous dependency on `CWarehouseMaster.Related_ID` and `CWarehouseMaster.Auto_ID`.

### Paging

The permission grid uses `Read_Kho_User_Async`, which calls `List_Kho_User_Page_Async` with Telerik's page number and page size and assigns both `Items` and `Total_Count`. The default page size remains 10. The UI does not pass a grid search value; this matches the previous Warehouse page flow.

### Save/delete

Permission add/edit checks use `Permission_ID`, then call the permission controller. Delete uses `Permission_ID` and the same existing `sp_DM_Delete` route. After save/delete, lookup data and the grid are reloaded and action history is recorded as before.

No source evidence shows a changed warehouse authorization rule or a changed document/report business path.

## 7. Database Boundary Review

### Table relationship

`tbl_DM_Kho_User` is a User-Warehouse permission mapping table, not a warehouse master table. It contains:

- `Auto_ID` as mapping identity;
- `Ma_Dang_Nhap` as the user login key;
- `Kho_ID` as the warehouse key.

The schema has a foreign key from `tbl_DM_Kho_User.Kho_ID` to `tbl_DM_Kho.Auto_ID` and a unique index on `(Ma_Dang_Nhap, Kho_ID)`. There is no declared foreign key from `Ma_Dang_Nhap` to `tbl_Sys_Thanh_Vien` in this repository schema; the relationship is validated by stored procedures and joins.

### Procedure behavior

- `sp_DM_Kho_User_List` returns the mapping identity, login, user name, warehouse name, and warehouse ID through legacy aliases.
- `sp_DM_Kho_User_Page` returns the same mapping plus a total-count result set.
- `sp_DM_Kho_User_Save` validates the login against `tbl_Sys_Thanh_Vien`, validates the warehouse, rejects duplicate `(login, warehouse)` pairs, and inserts/updates the mapping.
- `sp_DM_Kho_User_List_Allowed` returns only warehouses assigned to the supplied login.
- `sp_DM_Kho_User_Ensure_Access` is used by document/report procedures to enforce warehouse authorization.

### Compatibility branches

The following SQL compatibility paths remain unchanged:

- `sp_DM_Delete` accepts `@Entity = N'KhoUser'`;
- `sp_DM_Master_List` has a `KhoUser` branch;
- `sp_DM_Master_Page` has a `KhoUser` branch.

Keeping these branches avoids breaking old callers, but it means the database contract is not yet a hard architectural boundary. They should only be removed after an external-consumer inventory and a deliberate compatibility/deprecation decision.

No SQL or schema file is part of the refactor change set.

## 8. Risk Assessment

### Low risk

- Active UI permission list/page/save/delete path is separated.
- Permission model field mapping is explicit and covered by focused tests.
- Duplicate warehouse filtering is covered by unit tests.
- Solution build completed with zero errors.

### Medium risk / warning

- The generic `string` master API and SQL compatibility branches can still be misused by a future or external caller with `KhoUser`.
- `CWarehouseMaster.Login_Name` and the master-controller lookup methods leave a small semantic boundary leak.
- No external API/client outside this repository can be verified from source search.
- Full database-backed integration behavior was not executable in this environment.

### Verification results

```text
dotnet build .\TKS_Thuc_Tap_V11.sln --no-restore --verbosity minimal
PASS: 0 errors, 24 warnings.
The warnings are Telerik/Kendo licensing warnings; no new C# compiler error or refactor-specific warning was observed.

Focused Warehouse/refactor tests
PASS: 31/31.

Non-integration test set
PASS: 102/102.

Full solution test
INCONCLUSIVE for integration: 168 passed, 45 failed, 0 skipped, 213 total.
All observed failures stopped at the guard requiring
TKS_INTEGRATION_CONNECTION_STRING to point to a disposable test database.

git diff --check
PASS: no whitespace errors.
```

CodeGraph was used for caller/callee and blast-radius tracing. The existing graph resolved the Warehouse page -> `CWarehouseMaster_Controller` paths, but did not expose the newly added untracked permission symbols. Those symbols were therefore verified by current on-disk source search, compilation, and focused tests rather than treated as graph-index evidence.

## 9. Recommended Next Step

### DONE

- Separate permission model and controller created.
- Active Warehouse UI migrated to the permission model/controller.
- Master controller permission-specific C# branches removed.
- Exact internal Master API `KhoUser` caller search is clean.
- SQL/schema and unrelated business/benchmark logic were not changed by the refactor.
- Focused and non-integration tests pass; solution builds.

### OPTIONAL CLEANUP

1. Move `List_Authorized_Warehouses_Async` and `List_User_Lookup_Async` to a dedicated Warehouse lookup/security boundary if the company standard requires a strictly master-only controller.
2. Plan removal of `CWarehouseMaster.Login_Name` after the generic SQL DTO contract and external compatibility facade are split or deprecated.
3. Consider clearer permission method names if the team wants to replace the storage-oriented `Kho_User` vocabulary.
4. Retire the `KhoUser` branches in `sp_DM_Master_List`, `sp_DM_Master_Page`, and `sp_DM_Delete` only after external dependency inventory and a versioned compatibility plan.

### BLOCKER

- There is no source-level blocker in the migrated path.
- Full release/regression sign-off remains blocked until the integration suite is run against an approved disposable database. The current 45 failures are environment-guard failures, not evidence that the refactor's SQL mapping is wrong.
- If “Master Controller only handles Master Data” is an absolute acceptance criterion rather than a scoped goal for `KhoUser` CRUD, the two remaining lookup methods must be moved before declaring an unconditional `PASS`.
