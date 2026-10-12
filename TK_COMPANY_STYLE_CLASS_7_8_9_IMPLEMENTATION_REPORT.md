# Class 7–9 Company-Style Implementation Report

**Status: `COMPANY_STYLE_CLASS_7_8_9_IMPLEMENTATION_COMPLETE`**

Scope: Kho_User permission access, report Store access/composition, and final generic-controller retirement audit. The implementation preserves the completed DM and XNK phases.

## A. Class 7 before / after

Before, `CWarehousePermission_Controller` exposed async-shaped wrappers, generic mapping, and Add/Edit Store routing. It has been retired. The new `CDM_Kho_User_Controller` is in `Controller/DM`, owns the Kho_User persistence and lookup Stores, and exposes synchronous Store-named methods.

## B. Kho_User entity decision

Added `CDM_Kho_User` using the actual Kho_User terms: `Auto_ID`, `Ma_Dang_Nhap`, `Ho_Ten`, `Kho_ID`, and `Ten_Kho`. The CRUD/list/page persistence methods use this entity. `CWarehousePermission` remains only as a temporary UI projection at the Razor boundary; conversion there is explicit.

## C. Permission Store method inventory

Each public method below executes only the Stored Procedure with the same name:

| Method | Result / action |
|---|---|
| `F2015_sp_sel_List_Kho_User` | `List<CDM_Kho_User>` |
| `F2015_sp_sel_List_Sys_Thanh_Vien_Cho_Phan_Quyen_Kho` | Existing lookup projection |
| `F2015_sp_ins_Kho_User` | Insert; returns the Store-generated ID |
| `F2015_sp_upd_Kho_User` | Update |
| `F2015_sp_del_Kho_User` | Delete |
| `sp_DM_Kho_User_List_Allowed` | Existing authorized-warehouse lookup projection |
| `sp_DM_Kho_User_Page` | `CWarehousePagedResult<CDM_Kho_User>` |

The two unresolved support Store names remain unchanged; no F2015 name was invented.

## D. Add/Edit routing removal

Insert and update are separate methods. The UI selects `F2015_sp_ins_Kho_User` for Add and `F2015_sp_upd_Kho_User` for Edit. There is no `Save_Kho_User_Async` or `Permission_ID`-based Store selection in the Data Access controller. Delete calls its dedicated Store.

## E. Permission direct-mapping decision

The List and Page methods use `CUtility.Map_Row_To_Entity<CDM_Kho_User>` directly. The SQL projections now expose typed entity names for the mapped fields. Existing compatibility columns `Code` and `Related_ID_2` are retained to preserve result shape; the entity mapper ignores columns without entity properties. No manual `Map_Permission` remains in the low-level controller.

## F. Authorized warehouse behavior proof

`sp_DM_Kho_User_List_Allowed` still performs its existing login validation and membership check, returns only assigned warehouses, and keeps its ordering. The same Store is called by the import workflow. No authorization cache, fallback to all warehouses, or permission bypass was added. Source and live Store definitions were checked; no live unauthorized-user request was executed as UAT.

## G. Import permission caller migration

`CWarehouseTransactionImportService` now obtains the allowed-warehouse list through `CDM_Kho_User_Controller.sp_DM_Kho_User_List_Allowed`. This is a caller/API migration only; warehouse reference resolution and permission filtering remain intact.

## H. Class 8 before / after

`CWarehouseReport_Controller` remains the report feature controller. Its dynamic receipt/issue and current/history Store routing was replaced by explicit synchronous methods, one exact Store per method. The report models remain projections.

## I. Seven Report Store methods

The controller exposes exactly these low-level Store methods:

1. `sp_BC_Chi_Tiet_Nhap`
2. `sp_BC_Chi_Tiet_Xuat`
3. `sp_BC_Chi_Tiet_Nhap_Page`
4. `sp_BC_Chi_Tiet_Xuat_Page`
5. `sp_BC_Xuat_Nhap_Ton`
6. `sp_BC_Xuat_Nhap_Ton_Page`
7. `sp_BC_Ton_Kho_Hien_Tai_Page`

Every method name matches the only Store it calls. Page methods retain the current `Total_Count` and item mapping behavior.

## J. Report routing removal

The low-level API no longer selects receipt versus issue with `p_bIs_Receipt`, or current versus historical balance with `p_bRead_Current_Balance`. Callers choose the explicit method that matches the requested report.

## K. Report composition relocation

Historical inventory report enrichment no longer runs a permission Store from inside the report controller. The UI/workflow composes the report result with `CDM_Kho_User_Controller.sp_DM_Kho_User_List_Allowed`. The report controller now accesses report Stores only.

## L. Report projection preservation

`CWarehouseDetailReport` and `CWarehouseInventoryReport` are retained. No artificial report persistence entities were introduced. Result contracts did not require SQL alias changes for this phase.

## M. Current inventory report behavior

Current balance uses `sp_BC_Ton_Kho_Hien_Tai_Page`; historical Xuat-Nhap-Ton uses `sp_BC_Xuat_Nhap_Ton_Page`. No report cache or `*_For_Cache` report Store was added. Import's current-balance reference was migrated to the explicit report method.

## N. Class 9 retirement audit

| Controller | Final disposition |
|---|---|
| `CWarehouseMaster_Controller` | Already retired in the completed DM phase; remains retired |
| `CWarehouseDocument_Controller` | Already retired in the completed XNK phase; remains retired |
| `CWarehousePermission_Controller` | Retired in Class 7; no active production caller |
| `CWarehouseReport_Controller` | Retained and normalized in Class 8 |
| `CWarehouse_Controller_Base` | Retained; four active subclasses remain (see below) |

No active production, Razor, service, or compiled benchmark caller references the three retired controllers. Remaining source references are retirement assertions. Legacy benchmark manifests and frozen result JSON retain historical method/file metadata; active benchmark C# callsites were migrated.

## O. Generic base final disposition

`CWarehouse_Controller_Base` is **retained**, because these four production classes still derive from it:

- `CWarehouseDocumentSupport_Controller`
- `CWarehouseMasterSupport_Controller`
- `CXNK_Nhap_Kho_Controller`
- `CXNK_Xuat_Kho_Controller`

Its generic helpers remain in use for shared Page/support and transaction execution. Retiring the base or deleting its helpers would be unsupported by the current call graph.

## P. Generic model classification

| Model | Remaining role |
|---|---|
| `CWarehouseLookup` | Shared lookup support |
| `CWarehousePagedResult<T>` | Page support |
| `CWarehouseMaster` | Shared master Page/UI/import projection; not normalized DM CRUD persistence |
| `CWarehousePermission` | Temporary Kho_User UI projection |
| `CWarehouseDetailReport` | Report projection |
| `CWarehouseInventoryReport` | Report projection |
| `CWarehouseDocument`, `CWarehouseDocumentDetail` | Existing document UI projection/bridge to typed XNK entities |

No model was deleted merely because its name is generic; each listed model has an active support or projection owner.

## Q. Remaining active generic support

`CWarehouseMasterSupport_Controller` and `CWarehouseDocumentSupport_Controller` remain for shared support Stores. `CWarehouseDocumentSupport_Controller` continues to own the shared XNK Page/Post access. These are intentional support owners, not the retired multi-entity CRUD/document controllers.

## R. Files changed for this phase

- Added `TKS_Thuc_Tap_V11_Data_Access/Entity/DM/CDM_Kho_User.cs` and `Controller/DM/CDM_Kho_User_Controller.cs`.
- Deleted `TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehousePermission_Controller.cs`.
- Updated `TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehouseReport_Controller.cs` and `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_1_Warehouse_List.razor`.
- Migrated the permission/current-balance callers, including `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseTransactionImportService.cs` and active benchmark C# entry points under `TKS_Thuc_Tap_V11_Benchmarks*` and `benchmarks/`.
- Added `WarehouseReportRefactorTests.cs` and `WarehouseControllerRetirementTests.cs`; updated focused permission, report, UI, and architecture contract tests.
- Updated `Database/WarehouseModule.Procedures.sql` only for the Class 7 result-column aliases described below. Other DM/XNK SQL and source changes already present in the worktree belong to prior phases and were preserved.

The working tree already contains a separate earlier change to `Utility/Profiles/excel-import-profiles.json` (optional issue `UnitPrice` mapping). This Class 7–9 implementation did not edit that profile or alter Excel import mapping behavior.

## S. SQL changes

Only the result-column aliases for `F2015_sp_sel_List_Kho_User` and `sp_DM_Kho_User_Page` were changed for Class 7 typed mapping. Joins, filters, ordering, search, paging, values, and types were preserved. No SQL body change was made to a report Store, XNK Page/Post, inventory, reservation, or DM CRUD/cache Store in this phase. The two alias-only definitions were deployed to the guarded target database; no business data was written.

## T. Source/live parity

The live database identity was verified as `TKS_Thuc_Tap_V11_GiaiDoan2` before each DDL. After deployment, normalized source/live definition comparison passed for both changed Stores. All seven Kho_User Stores were checked for source/live parity. Live result metadata exposed the typed fields, including `Ho_Ten`, `Ma_Dang_Nhap`, `Ten_Kho`, and `Kho_ID`; List and Page retain their existing compatibility columns and Page total-count result set. No live result-row UAT was run.

## U. Build results

- Data Access, Web, and test projects compiled successfully during the focused architecture verification; benchmark projects affected by callsite migration also built successfully.
- Final Web build: `dotnet build TKS_Thuc_Tap_V11_Web_Danh_Muc/TKS_Thuc_Tap_V11_Web_Danh_Muc.csproj --no-restore --verbosity quiet` — **0 errors**, 12 Telerik license warnings.
- Previously verified affected benchmark builds: V2 **0 errors / 1 warning**; V22, Lite, and Q2 qualification **0 errors / 0 warnings**.

## V. Test results

- Accumulated DM/XNK/Permission/Report/retirement regression filter: **135 passed, 0 failed, 0 skipped** (rerun during closure).
- Focused report/UI/XNK contract suite: **37/37 passed**.
- Focused retirement audit tests: **3/3 passed**.
- No destructive integration test was run against the shared development database.

## W. Destructive integration status

No Insert, Update, Delete, Post, Import, or other business-data write was run against the live database. The permitted SQL deployment altered only the two result aliases above. No destructive integration test was run.

## X. `git diff --check`

**PASS.** Git emitted line-ending normalization notices for existing CRLF/LF-mixed files; no whitespace errors were reported.

## Y. Worktree preservation

Pre-existing DM, cache, XNK, import, and benchmark changes were preserved. No `git reset`, `git restore`, `git checkout -- .`, or `git stash` was used. The worktree remains intentionally dirty with the accumulated earlier phases and this implementation; this report does not claim the whole repository diff is limited to Class 7–9.

## Required confirmations

| Confirmation | Result |
|---|---|
| `CLASS7_PERMISSION_CONTROLLER_NORMALIZED` | PASS |
| `CLASS7_ONE_METHOD_ONE_STORE` | PASS |
| `CLASS7_METHOD_NAME_EQUALS_STORE` | PASS |
| `CLASS7_INSERT_UPDATE_ROUTING_REMOVED` | PASS |
| `CLASS7_AUTHORIZATION_BEHAVIOR_UNCHANGED` | PASS by source/live definition and caller parity; no unauthorized-user UAT |
| `UNAUTHORIZED_WAREHOUSE_REMAINS_BLOCKED` | PASS by unchanged authorization Store definition; runtime denial not separately exercised |
| `EXCEL_IMPORT_PROFILE_UNCHANGED` | PASS for this phase; the earlier worktree profile change is documented above |
| `CLASS8_REPORT_CONTROLLER_NORMALIZED` | PASS |
| `CLASS8_ONE_METHOD_ONE_STORE` | PASS |
| `CLASS8_METHOD_NAME_EQUALS_STORE` | PASS |
| `CLASS8_RECEIPT_ISSUE_ROUTING_REMOVED` | PASS |
| `CLASS8_CURRENT_HISTORY_ROUTING_REMOVED` | PASS |
| `CLASS8_REPORT_COMPOSITION_SEPARATED` | PASS |
| `REPORT_PROJECTION_MODELS_PRESERVED` | PASS |
| `NO_REPORT_CACHE_CREATED` | PASS |
| `CLASS9_GENERIC_CONTROLLER_AUDIT_COMPLETE` | PASS |
| `MASTER_GENERIC_CONTROLLER_REMAINS_RETIRED` | PASS |
| `DOCUMENT_GENERIC_CONTROLLER_REMAINS_RETIRED` | PASS |
| `PERMISSION_GENERIC_CONTROLLER_RETIRED` | PASS |
| `REPORT_FEATURE_CONTROLLER_RETAINED` | PASS |
| `BASE_CONTROLLER_RETAINED_WITH_ACTIVE_CALLERS` | PASS — four listed above |
| `DM_PREVIOUS_WORK_PRESERVED` | PASS |
| `XNK_PREVIOUS_WORK_PRESERVED` | PASS |
| `POST_UNCHANGED` | PASS |
| `INVENTORY_TECHNICAL_SUBSYSTEM_UNCHANGED` | PASS |
| `RESERVATION_TECHNICAL_SUBSYSTEM_UNCHANGED` | PASS |

**Final status: `COMPANY_STYLE_CLASS_7_8_9_IMPLEMENTATION_COMPLETE`**
