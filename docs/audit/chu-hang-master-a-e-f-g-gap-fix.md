# Chu_Hang Master A/E/F/G Gap Fix

## Baseline

- Repository/worktree: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`; branch `main`; starting commit `fd3235f` (`Fix/ refactor UI`). The initial worktree was clean. There were no benchmark log deletions to preserve.
- Database checked read-only: `TKS_Thuc_Tap_V11_GiaiDoan2`. All five tables have `Created` and audit columns. The five proposed `F{code}_sp_sel_Get_By_ID` procedures were absent at the time of the check; no SQL was deployed and no business data was written.
- CodeGraph was used for navigation and synced after edits. The index reports up to date.
- No changes were made to `FBase`, the legacy Action History procedure/UI, existing CRUD procedure definitions, routes, or Chu_Hang.

## A. File changes

| File / method | Before | After | Chu_Hang reference | Status |
|---|---|---|---|---|
| `CDM_Kho_Controller.cs`: `F2009_sp_ins_Kho`, `F2009_sp_upd_Kho`, audit helpers | One helper assigned creator and updater fields for both operations. | Insert helper assigns all four user/function audit fields; update helper assigns only `Last_Updated_By` and `Last_Updated_By_Function`. | `CDM_Chu_Hang_Controller` insert/update contract; `F2003_3_Chu_Hang_Edit` retains its before/after snapshots. | Implemented; helper tests pass. |
| `CDM_Don_Vi_Tinh_Controller.cs`: `F2016_sp_ins_Don_Vi_Tinh`, `F2016_sp_upd_Don_Vi_Tinh`, audit helpers | Same shared insert/update assignment bug. | Split insert/update audit assignment. | Same company convention. | Implemented; helper tests pass. |
| `CDM_Loai_San_Pham_Controller.cs`: `F2017_sp_ins_Loai_San_Pham`, `F2017_sp_upd_Loai_San_Pham`, audit helpers | Same shared insert/update assignment bug. | Split insert/update audit assignment. | Same company convention. | Implemented; helper tests pass. |
| `CDM_San_Pham_Controller.cs`: `F2018_sp_ins_San_Pham`, `F2018_sp_upd_San_Pham`, audit helpers | Same shared insert/update assignment bug. | Split insert/update audit assignment. | Same company convention. | Implemented; helper tests pass. |
| `CDM_NCC_Controller.cs`: `F2019_sp_ins_NCC`, `F2019_sp_upd_NCC`, audit helpers | Same shared insert/update assignment bug. | Split insert/update audit assignment. | Same company convention. | Implemented; helper tests pass. |
| Five `CCache_* .cs`: `Update_Data` | Replaced the cached entry with the caller object as-is, including potentially overwritten creator fields. | Copy `Created`, `Created_By`, and `Created_By_Function` from the cached entry before removing/re-adding by `Auto_ID`; existing delete/add key maintenance remains. | Cache update follows successful SQL update in the edit components. | Implemented. Unit cache regression test passes. |

Controller references: `CDM_Kho_Controller.cs:11, 125, 147, 165, 189, 200`; `CDM_Don_Vi_Tinh_Controller.cs:11, 125, 147, 165, 189, 200`; corresponding locations in the Category, Product, and Supplier controllers. Cache preservation is at `CCache_Kho.cs:44, 49`, `CCache_Don_Vi_Tinh.cs:44, 49`, `CCache_Loai_San_Pham.cs:53, 58`, `CCache_San_Pham.cs:44, 49`, and `CCache_NCC.cs:53, 58`.

## B. Audit Fields

### Root cause

The old helper was used by both Insert and Update and assigned the current updater to `Created_By` and `Created_By_Function`. The existing SQL Update procedures only set `Last_Updated` and updater metadata; they leave creator metadata unchanged. Examples are `WarehouseModule.Procedures.sql:3993` (Unit), `4054` (Category), `5143` (Product), `5202` (Supplier), and `5261` (Warehouse). This made the in-memory object/cache disagree with the unchanged SQL creator fields after an update.

### Corrected contract

- Insert, including the `SqlConnection`/`SqlTransaction` overload, initializes `Created_By`, `Created_By_Function`, `Last_Updated_By`, and `Last_Updated_By_Function` from the active user/function.
- Update changes only the two `Last_Updated_*` fields. It does not assign `Created`, `Created_By`, or `Created_By_Function`.
- Cache replacement carries the old cached creation metadata forward, then removes/re-adds by `Auto_ID`, which also removes the old business-key entry through the existing cache deletion path.
- Edit components invoke SQL Update before cache Update. If SQL Update throws, the cache call is not reached.

### Verification and limitation

`WarehouseMasterFinalizationContractTests` covers insert/update metadata (A01/A02), cache metadata and key replacement (A03), SQL-before-cache ordering on all five edit paths (A04), and both insert overloads calling the Insert helper (A05). These are source/unit tests; no SQL Update was executed.

The current List and `List_For_Cache` procedures return business columns without audit metadata. Consequently a freshly initialized cache may not know the database creator value. The fix prevents overwriting creator metadata that is present in the entity/cache; loading the actual persisted creator for every row still depends on the new Get_By_ID procedure becoming available and being used. This database-dependent part is not claimed as verified.

## C. Import Excel

### Shared path

Each active List importer now obtains its profile-detected sheet from the already-open `CExcel_Controller` package via `Read_Profiled_Import_Sheet`, adapts it through `CImportLegacyWorksheetAdapter`, validates with the existing module validator, and maps entity fields from validated rows. The validator row retains the original Excel row number; `Row_Number - 2` and the `DataTable`-index-to-validator-index correspondence have been removed from these import paths.

| Module | Reader before | Active reader/mapping after | Validation preserved | Verification |
|---|---|---|---|---|
| Kho | `CExcel_Controller.List_Range_Value_To_End` plus `CWarehouse_Xlsx_Reader` reopening/parsing the workbook. | Profile `Warehouse` → `ToWarehouse` → `CWarehouseImportValidator`; entity maps from the validated warehouse row. | Existing header, required/length, duplicate, unsupported-column/capability, empty-file, and row error behavior retained. | Profile parity and import/UI contract tests passed. |
| Đơn vị tính | Direct `List_Range_Value_To_End`/`DataTable` row mapping. | Profile `Unit` → `ToUnit` → `CUnitImportValidator`; entity maps from the validated unit row. | Required name, length, duplicate-in-file/database, identical-row skip, unsupported metadata, empty file, and per-row transaction behavior retained. | Blank-row absolute-row test and unit validator/UI tests passed. |
| Loại sản phẩm | `CExcel_Controller` range read plus `CCategory_Xlsx_Reader`. | Profile `Category` → `ToCategory` → `CCategoryImportValidator`; entity maps from the validated category row. | Existing code/name uniqueness, required/length, duplicate, sheet/header, unsupported-column/capability, and row error behavior retained. | Profile parity and category validation/UI tests passed. |
| Sản phẩm | `CExcel_Controller` range read plus `CProduct_Xlsx_Reader`; mapping used `Row_Number - 2` into the range `DataTable`. | Profile `Product` → `ToProduct` → `CProductImportValidator`; entity uses validated code/name, resolved category/unit IDs, and note. | Required fields, code/name duplicate checks, category/unit resolution, unsupported fields/capabilities, formula/value metadata, and row-level transaction behavior retained. | Profile parity, mapping, and product validation/UI tests passed. |
| Nhà cung cấp | `CExcel_Controller` range read plus `CSupplier_Xlsx_Reader`. | Profile `Supplier` → `ToSupplier` → `CSupplierImportValidator`; entity maps from the validated supplier row. | Existing required/length, code/name uniqueness, duplicate, header/sheet, unsupported-column/capability, and row error behavior retained. | Profile parity and supplier validation/UI tests passed. |

The active importer code is at `F2009_1_Kho_List.razor:123`, `FWarehouse_Unit_List.razor:123`, `F2017_1_Loai_San_Pham_List.razor:124`, `F2018_1_San_Pham_List.razor:160`, and `F2019_1_Nha_Cung_Cap_List.razor:124`. The shared entry point is `CExcel_Controller.cs:29`; the EPPlus-to-profile detector is `CProfiledWorkbook_Xlsx_Reader.cs:53`.

The custom `*Xlsx_Reader` classes were not deleted: existing tests and diagnostic/service paths still use them. They are no longer the second parser used by these five active List import handlers. `FBase` still performs its existing template/header precheck (`FBase.razor:368-370`), which opens the file for that precheck; it was left untouched. The content/profile/mapping source inside the active handlers is now the single `CExcel_Controller` package rather than a second module-specific reader.

`CExcel_profile_detection_preserves_absolute_row_number_when_a_blank_row_is_present` verifies that a data row at Excel row 4 remains row 4. Existing focused validator/parser/UI tests passed. Database-backed import runtime tests were not run.

## D. Get_By_ID

The five controller methods follow the Chu_Hang pattern: `DataTable`, `CSqlHelper.FillDataTable` using the configured connection string, map row zero with `CUtility.Map_Row_To_Entity<T>`, return null for no row, dispose the table, and propagate exceptions. Their lines begin at line 11 in each `CDM_*_Controller.cs`.

| Module | Controller method / proposed procedure | Runtime DB procedure | UI caller switched? | Status |
|---|---|---|---|---|
| Kho | `F2009_sp_sel_Get_By_ID(long p_iID)` | Absent at read-only check | No | `GET_BY_ID_IMPLEMENTED_DEPLOYMENT_PENDING` |
| Đơn vị tính | `F2016_sp_sel_Get_By_ID(long p_iID)` | Absent at read-only check | No | `GET_BY_ID_IMPLEMENTED_DEPLOYMENT_PENDING` |
| Loại sản phẩm | `F2017_sp_sel_Get_By_ID(long p_iID)` | Absent at read-only check | No | `GET_BY_ID_IMPLEMENTED_DEPLOYMENT_PENDING` |
| Sản phẩm | `F2018_sp_sel_Get_By_ID(long p_iID)` | Absent at read-only check | No | `GET_BY_ID_IMPLEMENTED_DEPLOYMENT_PENDING` |
| Nhà cung cấp | `F2019_sp_sel_Get_By_ID(long p_iID)` | Absent at read-only check | No | `GET_BY_ID_IMPLEMENTED_DEPLOYMENT_PENDING` |

`Database/WarehouseMaster.GetById.Procedures.sql` prepares five read-only procedures. Each selects one row by `Auto_ID` and returns business plus audit columns. It was not deployed. Info/Edit remain on their existing List-based callers so no active component calls an uninstalled procedure. The contract test checks names, parameter, mapping, disposal, source table, and that Info/Edit do not invoke the pending procedure. SQL execution and runtime mapping for an existing/missing ID remain unverified pending deployment to an approved test database.

## E. Info Created

All five Info components now show localized `Ngày giờ tạo` using `CUtility.Get_Label_Text(m_objData.Created, false)` in the existing third column, before creator metadata. Null uses the shared label helper placeholder. Existing business fields, three-column layout, and History tab remain in place; the old `FCommon_Sys_sp_sel_List_Log_Record_Action_History(r_iAuto_ID)` path was not changed.

Files/lines: `F2009_2_Kho_Info.razor:28`, `FWarehouse_Unit_Info.razor:30`, `F2017_2_Loai_San_Pham_Info.razor:29`, `F2018_2_San_Pham_Info.razor:31`, `F2019_2_Nha_Cung_Cap_Info.razor:29`.

**Display data caveat:** the current active List procedures omit `Created`, so Info can render the field but can show the placeholder until Get_By_ID is deployed and the Info caller is switched under the compatibility gate. No existing Info/Edit caller was switched in this change.

## F. Verification

| Check | Result |
|---|---|
| `dotnet build TKS_Thuc_Tap_V11_Web_Danh_Muc/TKS_Thuc_Tap_V11_Web_Danh_Muc.csproj --no-restore -v:q` | Passed. Existing Telerik license and analyzer warnings remain non-blocking. |
| Focused A/E/F/G source tests (`MasterDataCompanyStyleMigrationTests`, `WarehouseMasterFinalizationContractTests`, `WarehouseMasterGetByIdContractTests`, profile detection, unit import UI) | Passed: 25, failed: 0. |
| Focused import validation/parser/mapping/UI tests for the five modules | Passed: 122, failed: 0. |
| `git diff --check` | Passed. |
| `codegraph sync` / `codegraph status` | Synced; index up to date. |
| Runtime UI, SQL write, Get_By_ID execution, database-backed import integration | Not run. No business/shared DB writes were made. |

## G. Final comparison

| Module | Audit Fields | Import mapping | Get_By_ID | Info Created | FBase regression | Final status |
|---|---|---|---|---|---|---|
| Kho | Insert/update split; cache retains cached creator metadata; no DB write verification. | Profiled CExcel source, existing validation, original row numbers; focused tests pass. | Implemented in source; runtime SP absent; inactive. | Added; current List result may yield placeholder. | FBase unchanged. | A/E/G implemented; F deployment pending. |
| Đơn vị tính | Insert/update split; cache test verifies creator/time and business-key replacement; no DB write verification. | Profiled source, existing unit validator, original row numbers; focused tests pass. | Implemented in source; runtime SP absent; inactive. | Added; current List result may yield placeholder. | FBase unchanged. | A/E/G implemented; F deployment pending. |
| Loại sản phẩm | Insert/update split; cache retains cached creator metadata; no DB write verification. | Profiled CExcel source, existing validation, original row numbers; focused tests pass. | Implemented in source; runtime SP absent; inactive. | Added; current List result may yield placeholder. | FBase unchanged. | A/E/G implemented; F deployment pending. |
| Sản phẩm | Insert/update split; cache retains cached creator metadata; no DB write verification. | Profiled source and validated category/unit ID mapping; focused tests pass. | Implemented in source; runtime SP absent; inactive. | Added; current List result may yield placeholder. | FBase unchanged. | A/E/G implemented; F deployment pending. |
| Nhà cung cấp | Insert/update split; cache retains cached creator metadata; no DB write verification. | Profiled CExcel source, existing validation, original row numbers; focused tests pass. | Implemented in source; runtime SP absent; inactive. | Added; current List result may yield placeholder. | FBase unchanged. | A/E/G implemented; F deployment pending. |

## Remaining risk / next action

Deploy `WarehouseMaster.GetById.Procedures.sql` only to an approved database after review, then verify the exact runtime procedure contracts and switch the five Info/Edit callers. Until then, the code methods and Info fields are ready but Get_By_ID is not active, and audit metadata omitted by existing List/Cache queries cannot be reconstructed from those results.
