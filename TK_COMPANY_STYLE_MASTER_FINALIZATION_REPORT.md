# Company-style Master finalization report

Date: 2026-10-12 (Asia/Saigon)

Final status: COMPANY_STYLE_MASTER_FINALIZATION_COMPLETE

Baseline implementation: 3f0fc67. Current task RED checkpoint: 3897496. GREEN implementation checkpoint: 60e6614. All checkpoints are on the existing main branch.

## Scope and authority

Implemented the user task "COMPLETE MASTER DECOMPOSITION + RETIRE CWarehouseMaster_Controller + ALIGN DM CONTROLLERS TO COMPANY CODING STYLE". The existing CDM_Chu_Hang_Controller is the company style reference. The prior TK_COMPANY_STYLE planning artifacts remain preserved.

This is a C# ownership/signature/style refactor. SQL, entity models, function codes, routes, validation, authorization, and the Document/Permission/Report/Inventory implementations were not refactored.

The user journeys were derived from that task: catalog operations retain their result mapping, IDs, and audit values while using synchronous entity Store methods; Master Preview/Confirm retains authorization, revalidation, and whole-batch atomic import; Page/Product query callers retain paging behavior through separate temporary owners while the old mixed controller is retired.

## 1. Import ownership and callers

| Method | Previous owner | Final owner | Visibility |
|---|---|---|---|
| Import_Products_Async | CWarehouseMaster_Controller | TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseProductImportService.cs | internal batch writer |
| Import_Units_Async | CWarehouseMaster_Controller | TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseUnitImportService.cs | internal batch writer |
| Import_Categories_Async | CWarehouseMaster_Controller | TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseCategoryImportService.cs | internal batch writer |
| Import_Suppliers_Async | CWarehouseMaster_Controller | TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseSupplierImportService.cs | internal batch writer |
| Import_Warehouses_Async | CWarehouseMaster_Controller | TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseKhoImportService.cs | internal batch writer |

The paths in this report are relative to the repository root.

All five production ConfirmAsync callers now invoke their own service's batch writer. The public PreviewAsync/ConfirmAsync permission checks and revalidation remain in place. Nine direct calls in the existing database integration tests were migrated to these service writers so those tests still compile. The Web_Danh_Muc project grants internal access only to the existing Data_Access.Tests assembly; the batch writers were not added as public workflow entry points.

Each batch still opens one SqlConnection asynchronously, starts one SqlTransaction, iterates rows in the same order with the same cancellation checks, inserts through its entity controller using that connection/transaction, and commits once. Any insert/commit failure attempts a whole-batch rollback and rethrows the original failure. Rollback failure handling is unchanged.

The five moved writer bodies were compared against checkpoint 3897496 after normalizing only ownership, explicit local types, and synchronous returned-ID assignment: 5/5 equivalent. SQL error numbers and domain message text were preserved exactly.

## 2. Product and Page support

| Responsibility | Final owner | Classification |
|---|---|---|
| List_Product_Codes_Async; List_Product_Masters_Async | TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseProductSupport_Service.cs | TEMPORARY_PRODUCT_SUPPORT_OWNER |
| List_Master_Page_Async; List_Lookup_Page_Async | TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehouseMasterSupport_Controller.cs | TEMPORARY_SHARED_STORE_CONTROLLER; BLOCKED_BY_PAGE_NAMING_RULE |

No existing shared product-query service was found among the relevant service/controller precedents. The product support owner's name is a temporary project name, not a company naming convention.

The two product helper bodies preserve the 500-row Page loop, Total_Count handling, cancellation, empty-page exit, and case-insensitive Code set. Their normalized source comparison passed 2/2. Three external caller sites now use the product support service: Product import reference loading, transaction import reference loading, and the product preload integration-test helper.

The Page owner contains only the two unresolved Page methods. Their signatures and bodies were moved unchanged (source comparison 2/2). They still call sp_DM_Master_Page and sp_DM_Lookup_Page respectively. Their existing Task signatures and support method names remain temporary under the explicit unresolved-support exception. All 17 Page invocation sites in UI, product support, tests, and active benchmark source use the new owner.

These business helpers were not moved to Utility. No Fxxxx name, SQL prefix, Store split, or Store rename was invented.

## 3. Old controller retirement and reference audit

CWarehouseMaster_Controller is RETIRED. Its Controller/DM/CWarehouseMaster_Controller.cs file was deleted after caller migration. The final contract test confirms that the old class is absent from the Data Access assembly and the old file is absent.

Active production, Razor, benchmark C# and config/script references to the old class: 0.

The remaining references are intentional retirement assertions and preserved historical documentation/benchmark evidence. The reference appendix lists all 39 matching files, excluding this report and build/cache files:

- 1 test file containing four absence/ownership assertions;
- 4 prior plan artifacts;
- 16 historical documentation files;
- 15 historical benchmark result/report files;
- 3 benchmark manifests bound to their original runner/source/runtime hashes.

The manifests contain historical operation labels; active runners call the new owner directly. Existing historical result hashes and source identity evidence were not rewritten. The current Lite README and active Lite source identity paths were updated to the new Page owner.

## 4. DM signatures and company style

All 30 public Store method bodies across the five DM controllers were normalized: 10 List/Lookup methods, 10 Insert methods including transaction overloads, five Update methods, and five Delete methods. There are 25 distinct Store names.

| Operation | Before return type | Final return type |
|---|---|---|
| List | `Task<List<CWarehouseMaster>>` | `List<CWarehouseMaster>` |
| Lookup | `Task<List<CWarehouseLookup>>` | `List<CWarehouseLookup>` |
| Insert, normal and transaction overload | Task | long |
| Update | Task | void, with required scalar readback preserved |
| Delete | Task | void |

Each low-level Store method uses a full block body and direct CSqlHelper access with an exact literal Store name matching its method name. Lists explicitly allocate a DataTable, map each DataRow with CUtility.Map_Row_To_Entity, and dispose the DataTable in finally. Locals are explicit types with the existing p_/v_ naming convention. The five classes no longer inherit the generic base because they now use direct company-style SQL helper calls; the generic base itself is unchanged.

The contract tests inspect all 30 method bodies and verify one Store execution target per body and exact method/Store name equality. The normalized DM files contain no Task.FromResult, Task.CompletedTask, expression-bodied executable method, var local, master-type routing, or dynamic Store selection.

Exact before/after signatures are listed in the signature appendix.

## 5. Insert, update, delete, and audit contracts

Insert methods now return the scalar ID as long. Their callers assign that returned ID to the same entity's Auto_ID, preserving the previous observable entity state. This includes all five Add branches in the UI, all five batch writers, and ID-dependent CRUD benchmark/integration-test callers. Transaction inserts retain the same method name, SqlConnection/SqlTransaction parameters, parameter order, and Store target.

All five update Store bodies in the checked-in Database/WarehouseModule.Procedures.sql return SELECT Auto_ID. Previously, Scalar_ID read that scalar and assigned it to the entity. Each final synchronous void update therefore keeps Convert.ToInt64(CSqlHelper.ExecuteScalar(...)) and assigns the result to p_objData.Auto_ID. ExecuteNonquery was not substituted for that scalar contract.

| Update Store | Decision |
|---|---|
| F2009_sp_upd_Kho | Preserve ExecuteScalar and Auto_ID assignment |
| F2016_sp_upd_Don_Vi_Tinh | Preserve ExecuteScalar and Auto_ID assignment |
| F2017_sp_upd_Loai_San_Pham | Preserve ExecuteScalar and Auto_ID assignment |
| F2018_sp_upd_San_Pham | Preserve ExecuteScalar and Auto_ID assignment |
| F2019_sp_upd_NCC | Preserve ExecuteScalar and Auto_ID assignment |

This decision is based on checked-in SQL and current caller behavior. Live database definitions were not queried.

Deletes use synchronous void and CSqlHelper.ExecuteNonquery, preserving Auto_ID and both audit parameters.

Set_Audit_Fields was preserved exactly in all five controllers. Its existing behavior assigns Created_By, Created_By_Function, Last_Updated_By, and Last_Updated_By_Function to the supplied current values for both insert and update. The new runtime unit test verifies all four fields, and unchanged Auto_ID, for insert ID 0 and update ID 17 across all five controllers without opening a database.

Static comparison against checkpoint 3897496 confirmed identical Store names and ordered parameter tails for all 30 Store calls, and identical audit helper source for all five controllers. Database/SQL/entity/base/Document/Permission/Report changes: none.

## 6. Build and test evidence

| Checkpoint/check | Result |
|---|---|
| Initial RED build/test attempt | Assembly built, but test host aborted while loading symbols/types; not counted as RED |
| Valid RED rerun | Four new tests executed; four failed for the intended missing ownership/signature/retirement implementation |
| Checkpoint 1: imports | Data Access, Web_Danh_Muc, Tests and referenced benchmark compiled with 0 errors; 99 focused import tests passed |
| Checkpoint 2: support and retirement | References compiled with 0 errors; 55 focused contract/UI/permission/import tests passed |
| Checkpoint 3: final signatures and all required focused tests | References compiled with 0 errors; 199 passed, 0 failed, 0 skipped |
| Additional benchmark catalog/adapter checks | 3 passed, 0 failed, 0 skipped |
| Web host build | 0 errors, 24 warnings |
| Benchmark V2 build | 0 errors, 1 warning |
| Benchmark V22 build | 0 errors, 0 warnings |
| Benchmark Lite build | 0 errors, 0 warnings |
| Whitespace check | PASS |
| Import / Store parameter / audit / Product helper / Page helper source comparisons | 5/5; 30/30; 5/5; 2/2; 2/2 PASS |

Runtime database-write integration tests: NOT RUN. The user explicitly restricted destructive testing on a shared database. The existing integration tests were migrated and compiled, but no database was opened by this implementation's validation commands. Database data/schema/objects were not modified.

Numerical coverage and the skill's 80% threshold were not measured or claimed. No new coverage dependency or test database fixture was added. The verification here is bounded to safe unit/source-contract tests, compilation, and source equivalence. Browser UAT and live DB-write transaction execution are not established by these results.

### TDD commands and checkpoint evidence

RED assembly-build attempt:

~~~powershell
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --verbosity quiet -m:1 -p:BuildInParallel=false --filter FullyQualifiedName~WarehouseMasterFinalizationContractTests -clp:ErrorsOnly
~~~

Valid RED execution after the aborted host run:

~~~powershell
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --no-build --verbosity normal --filter FullyQualifiedName~WarehouseMasterFinalizationContractTests -clp:ErrorsOnly
~~~

Checkpoint 1 (99 passed):

~~~powershell
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --verbosity quiet -m:1 -p:BuildInParallel=false --filter "FullyQualifiedName~ProductExcelImportUiTests|FullyQualifiedName~ProductExcelImportValidationTests|FullyQualifiedName~ProductXlsxReaderTests|FullyQualifiedName~CategoryExcelImportUiTests|FullyQualifiedName~CategoryExcelImportValidationTests|FullyQualifiedName~CategoryXlsxReaderTests|FullyQualifiedName~SupplierExcelImportUiTests|FullyQualifiedName~SupplierExcelImportValidationTests|FullyQualifiedName~SupplierXlsxReaderTests|FullyQualifiedName~UnitExcelImportUiTests|FullyQualifiedName~UnitExcelImportValidationTests|FullyQualifiedName~UnitXlsxReaderTests|FullyQualifiedName~WarehouseExcelImportUiTests|FullyQualifiedName~WarehouseExcelImportValidationTests|FullyQualifiedName~WarehouseXlsxReaderTests" -clp:ErrorsOnly
~~~

Checkpoint 2 (55 passed):

~~~powershell
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --verbosity quiet -m:1 -p:BuildInParallel=false --filter "FullyQualifiedName~WarehouseMasterFinalizationContractTests.Master_controller|FullyQualifiedName~WarehouseMasterFinalizationContractTests.Product_page_helpers|FullyQualifiedName~WarehouseMasterFinalizationContractTests.Master_import_services|FullyQualifiedName~WarehouseMasterControllerSplitContractTests|FullyQualifiedName~WarehouseUiWorkflowTests|FullyQualifiedName~WarehousePermissionRefactorTests|FullyQualifiedName~ProductExcelImportUiTests" -clp:ErrorsOnly
~~~

Final GREEN:

~~~powershell
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --verbosity quiet -m:1 -p:BuildInParallel=false --filter "FullyQualifiedName~WarehouseMasterFinalizationContractTests|FullyQualifiedName~WarehouseMasterControllerSplitContractTests|FullyQualifiedName~WarehouseGroupBSplitContractTests|FullyQualifiedName~WarehouseGroupCDesharingContractTests|FullyQualifiedName~WarehouseUiWorkflowTests|FullyQualifiedName~WarehousePermissionRefactorTests|FullyQualifiedName~ProductExcelImportUiTests|FullyQualifiedName~CategoryExcelImportValidationTests|FullyQualifiedName~CategoryExcelImportUiTests|FullyQualifiedName~CategoryXlsxReaderTests|FullyQualifiedName~ProductExcelImportValidationTests|FullyQualifiedName~ProductImportDatabaseSafetyTests|FullyQualifiedName~SupplierExcelImportValidationTests|FullyQualifiedName~SupplierExcelImportUiTests|FullyQualifiedName~SupplierXlsxReaderTests|FullyQualifiedName~UnitExcelImportValidationTests|FullyQualifiedName~UnitExcelImportUiTests|FullyQualifiedName~UnitXlsxReaderTests|FullyQualifiedName~WarehouseExcelImportValidationTests|FullyQualifiedName~WarehouseExcelImportUiTests|FullyQualifiedName~WarehouseXlsxReaderTests|FullyQualifiedName~TransactionImportPreviewTests" -clp:ErrorsOnly
~~~

Additional benchmark checks:

~~~powershell
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-build --no-restore --verbosity quiet --filter "FullyQualifiedName~ToolBenchmarkHarnessTests.Scenario_catalog_exposes_only_read_only_warehouse_paths|FullyQualifiedName~ToolBenchmarkHarnessTests.Load_scenarios_convert_operation_exceptions_into_recorded_failures|FullyQualifiedName~PerformanceBenchmarkTests.Scenario_catalog_covers_full_paged_join_and_crud_paths" -clp:ErrorsOnly
~~~

Each following build was run separately/sequentially, not as a load benchmark:

~~~powershell
dotnet build TKS_Thuc_Tap_V11_Web/TKS_Thuc_Tap_V11_Web.csproj --no-restore --verbosity quiet -m:1 -p:BuildInParallel=false -clp:ErrorsOnly
dotnet build TKS_Thuc_Tap_V11_Benchmarks_V2/TKS_Thuc_Tap_V11_Benchmarks_V2.csproj --no-restore --verbosity quiet -m:1 -p:BuildInParallel=false -clp:ErrorsOnly
dotnet build TKS_Thuc_Tap_V11_Benchmarks_V22/TKS_Thuc_Tap_V11_Benchmarks_V22.csproj --no-restore --verbosity quiet -m:1 -p:BuildInParallel=false -clp:ErrorsOnly
dotnet build benchmarks/lite/BenchmarkLite.csproj --no-restore --verbosity quiet -m:1 -p:BuildInParallel=false -clp:ErrorsOnly
~~~

The new test guarantees are indexed in WarehouseMasterFinalizationContractTests.cs: retirement/owner shape, product owner, one transaction per batch, synchronous signatures/one literal Store per method, and audit assignments. Existing import validation/readers, UI workflows, Group B/C Store contracts, permission separation, and transaction Preview tests remain GREEN.

| Guarantee | Test | Type | Result |
|---|---|---|---|
| Old class/file retired; Page owner contains only two support methods | Master_controller_is_retired_and_unresolved_page_support_has_a_temporary_owner | Reflection/source contract | PASS |
| Product helpers have a separate temporary owner | Product_page_helpers_have_a_separate_temporary_product_support_owner | Reflection/source contract | PASS |
| All five services own one connection/transaction and direct entity inserts | Master_import_services_own_one_transaction_for_each_batch | Source contract | PASS |
| All 30 Store bodies use synchronous signatures, one literal Store, matching names, and explicit style | Entity_store_methods_use_synchronous_company_style_signatures | Reflection/source contract | PASS |
| All four audit fields retain their previous insert/update assignment contract | Audit_values_keep_the_existing_insert_and_update_assignment_contract | Runtime unit test without DB | PASS |

## 7. Worktree preservation and remaining naming blockers

The five prior root TK_COMPANY_STYLE plan/map/risk artifacts and docs/testing/warehouse-master-class-decomposition.tdd.md were already untracked at task start. They were not edited or staged in this task. No worktree, branch, database, deployment, service, job, credential, or external resource change was made. Local TDD checkpoint commits were created; nothing was pushed.

The required finalization report is also the TDD evidence report, avoiding a second redundant report file.

The only remaining architecture naming limitations are the accepted temporary Page Store naming rule and the temporary product support owner name. They do not prevent CRUD decomposition or retirement of the old controller. Entity-model decomposition and the later Document/Permission/Report/Inventory phases remain separate work.

## 8. Required confirmations

~~~text
MASTER_IMPORT_ORCHESTRATION_MOVED
MASTER_IMPORT_TRANSACTION_PRESERVED (source-contract/equivalence verified; live DB-write execution NOT RUN)
MASTER_PRODUCT_SUPPORT_ISOLATED
MASTER_PAGE_SUPPORT_ISOLATED
PAGE_STORE_NAMES_UNCHANGED
PAGE_NAMING_BLOCKER_PRESERVED
MASTER_OLD_CONTROLLER_RETIRED
DM_CONTROLLERS_COMPANY_STYLE_ALIGNED
ONE_METHOD_ONE_STORE_VERIFIED (30 public method bodies, 25 distinct Stores)
METHOD_NAME_EQUALS_STORE_VERIFIED
NO_FAKE_ASYNC_IN_NORMALIZED_DM_STORE_METHODS
NO_EXPRESSION_BODIED_DM_STORE_METHODS
ENTITY_MODEL_UNCHANGED
DATABASE_UNCHANGED
STORED_PROCEDURES_UNCHANGED
NO_BUSINESS_LOGIC_CHANGE (source equivalence and safe tests; live DB runtime NOT RUN)
PREVIOUS_WORK_PRESERVED
COMPANY_STYLE_MASTER_FINALIZATION_COMPLETE
~~~


## Appendix A. Exact method signatures

| Controller | Before | After |
|---|---|---|
| `CDM_Kho_Controller` | `Task<List<CWarehouseMaster>> F2009_sp_sel_List_Kho()` | `List<CWarehouseMaster> F2009_sp_sel_List_Kho()` |
| `CDM_Kho_Controller` | `Task<List<CWarehouseLookup>> F2009_sp_sel_List_Kho_Lookup()` | `List<CWarehouseLookup> F2009_sp_sel_List_Kho_Lookup()` |
| `CDM_Kho_Controller` | `Task F2009_sp_ins_Kho(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `long F2009_sp_ins_Kho(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_Kho_Controller` | `Task F2009_sp_ins_Kho(SqlConnection p_conn, SqlTransaction p_trans, CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `long F2009_sp_ins_Kho(SqlConnection p_conn, SqlTransaction p_trans, CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_Kho_Controller` | `Task F2009_sp_upd_Kho(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `void F2009_sp_upd_Kho(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_Kho_Controller` | `Task F2009_sp_del_Kho(long p_iAuto_ID, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `void F2009_sp_del_Kho(long p_iAuto_ID, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_Don_Vi_Tinh_Controller` | `Task<List<CWarehouseMaster>> F2016_sp_sel_List_Don_Vi_Tinh()` | `List<CWarehouseMaster> F2016_sp_sel_List_Don_Vi_Tinh()` |
| `CDM_Don_Vi_Tinh_Controller` | `Task<List<CWarehouseLookup>> F2016_sp_sel_List_Don_Vi_Tinh_Lookup()` | `List<CWarehouseLookup> F2016_sp_sel_List_Don_Vi_Tinh_Lookup()` |
| `CDM_Don_Vi_Tinh_Controller` | `Task F2016_sp_ins_Don_Vi_Tinh(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `long F2016_sp_ins_Don_Vi_Tinh(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_Don_Vi_Tinh_Controller` | `Task F2016_sp_ins_Don_Vi_Tinh(SqlConnection p_conn, SqlTransaction p_trans, CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `long F2016_sp_ins_Don_Vi_Tinh(SqlConnection p_conn, SqlTransaction p_trans, CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_Don_Vi_Tinh_Controller` | `Task F2016_sp_upd_Don_Vi_Tinh(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `void F2016_sp_upd_Don_Vi_Tinh(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_Don_Vi_Tinh_Controller` | `Task F2016_sp_del_Don_Vi_Tinh(long p_iAuto_ID, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `void F2016_sp_del_Don_Vi_Tinh(long p_iAuto_ID, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_Loai_San_Pham_Controller` | `Task<List<CWarehouseMaster>> F2017_sp_sel_List_Loai_San_Pham()` | `List<CWarehouseMaster> F2017_sp_sel_List_Loai_San_Pham()` |
| `CDM_Loai_San_Pham_Controller` | `Task<List<CWarehouseLookup>> F2017_sp_sel_List_Loai_San_Pham_Lookup()` | `List<CWarehouseLookup> F2017_sp_sel_List_Loai_San_Pham_Lookup()` |
| `CDM_Loai_San_Pham_Controller` | `Task F2017_sp_ins_Loai_San_Pham(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `long F2017_sp_ins_Loai_San_Pham(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_Loai_San_Pham_Controller` | `Task F2017_sp_ins_Loai_San_Pham(SqlConnection p_conn, SqlTransaction p_trans, CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `long F2017_sp_ins_Loai_San_Pham(SqlConnection p_conn, SqlTransaction p_trans, CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_Loai_San_Pham_Controller` | `Task F2017_sp_upd_Loai_San_Pham(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `void F2017_sp_upd_Loai_San_Pham(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_Loai_San_Pham_Controller` | `Task F2017_sp_del_Loai_San_Pham(long p_iAuto_ID, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `void F2017_sp_del_Loai_San_Pham(long p_iAuto_ID, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_San_Pham_Controller` | `Task<List<CWarehouseMaster>> F2018_sp_sel_List_San_Pham()` | `List<CWarehouseMaster> F2018_sp_sel_List_San_Pham()` |
| `CDM_San_Pham_Controller` | `Task<List<CWarehouseLookup>> F2018_sp_sel_List_San_Pham_Lookup()` | `List<CWarehouseLookup> F2018_sp_sel_List_San_Pham_Lookup()` |
| `CDM_San_Pham_Controller` | `Task F2018_sp_ins_San_Pham(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `long F2018_sp_ins_San_Pham(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_San_Pham_Controller` | `Task F2018_sp_ins_San_Pham(SqlConnection p_conn, SqlTransaction p_trans, CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `long F2018_sp_ins_San_Pham(SqlConnection p_conn, SqlTransaction p_trans, CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_San_Pham_Controller` | `Task F2018_sp_upd_San_Pham(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `void F2018_sp_upd_San_Pham(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_San_Pham_Controller` | `Task F2018_sp_del_San_Pham(long p_iAuto_ID, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `void F2018_sp_del_San_Pham(long p_iAuto_ID, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_NCC_Controller` | `Task<List<CWarehouseMaster>> F2019_sp_sel_List_NCC()` | `List<CWarehouseMaster> F2019_sp_sel_List_NCC()` |
| `CDM_NCC_Controller` | `Task<List<CWarehouseLookup>> F2019_sp_sel_List_NCC_Lookup()` | `List<CWarehouseLookup> F2019_sp_sel_List_NCC_Lookup()` |
| `CDM_NCC_Controller` | `Task F2019_sp_ins_NCC(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `long F2019_sp_ins_NCC(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_NCC_Controller` | `Task F2019_sp_ins_NCC(SqlConnection p_conn, SqlTransaction p_trans, CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `long F2019_sp_ins_NCC(SqlConnection p_conn, SqlTransaction p_trans, CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_NCC_Controller` | `Task F2019_sp_upd_NCC(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `void F2019_sp_upd_NCC(CWarehouseMaster p_objData, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |
| `CDM_NCC_Controller` | `Task F2019_sp_del_NCC(long p_iAuto_ID, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` | `void F2019_sp_del_NCC(long p_iAuto_ID, string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")` |

## Appendix B. Remaining historical/assertion references

Snapshot after retirement, excluding this report, bin/obj, Git/CodeGraph caches, and log files. Counts are matching source lines, not active callers.

| Classification | Path | Matching lines |
|---|---|---|
| Hash-bound benchmark manifests | `benchmarks/v2.1-mixed/benchmark-v2.1-mixed-manifest.json` | 2 |
| Hash-bound benchmark manifests | `benchmarks/v2.1/benchmark-v2.1-adaptive-manifest.json` | 2 |
| Hash-bound benchmark manifests | `benchmarks/v2/benchmark-v2-manifest.json` | 2 |
| Historical benchmark results | `benchmarks/lite/results/WHB-LITE-20261005T075828Z-25cd479284d34bb1/benchmark-lite-results.json` | 1 |
| Historical benchmark results | `benchmarks/lite/results/WHB-LITE-20261005T080006Z-4fe51c3e2a404126/benchmark-lite-report.md` | 2 |
| Historical benchmark results | `benchmarks/lite/results/WHB-LITE-20261005T080006Z-4fe51c3e2a404126/benchmark-lite-results.json` | 3 |
| Historical benchmark results | `benchmarks/lite/results/WHB-LITE2-20261005T085718Z-077bc1ea654145c5b57e/benchmark-lite2-environment.json` | 1 |
| Historical benchmark results | `benchmarks/lite/results/WHB-LITE2-20261005T085718Z-077bc1ea654145c5b57e/benchmark-lite2-results.json` | 1 |
| Historical benchmark results | `benchmarks/lite/results/WHB-LITE2-20261005T085852Z-ad304a04202241d089eb/benchmark-lite2-environment.json` | 1 |
| Historical benchmark results | `benchmarks/lite/results/WHB-LITE2-20261005T085852Z-ad304a04202241d089eb/benchmark-lite2-results.json` | 1 |
| Historical benchmark results | `benchmarks/lite/results/WHB-LITE2-20261005T090022Z-53a1e2d712894046a892/benchmark-lite2-environment.json` | 1 |
| Historical benchmark results | `benchmarks/lite/results/WHB-LITE2-20261005T090022Z-53a1e2d712894046a892/benchmark-lite2-results.json` | 1 |
| Historical benchmark results | `benchmarks/lite/results/WHB-LITE2-20261005T090058Z-8062502d2f77452095e1/benchmark-lite2-environment.json` | 1 |
| Historical benchmark results | `benchmarks/lite/results/WHB-LITE2-20261005T090058Z-8062502d2f77452095e1/benchmark-lite2-results.json` | 1 |
| Historical benchmark results | `benchmarks/lite/results/WHB-LITE2-20261005T090141Z-b7ac5f6605514bf5a491/benchmark-lite2-environment.json` | 1 |
| Historical benchmark results | `benchmarks/lite/results/WHB-LITE2-20261005T090141Z-b7ac5f6605514bf5a491/benchmark-lite2-results.json` | 1 |
| Historical benchmark results | `benchmarks/lite/results/WHB-LITE2-20261005T090156Z-39716c676c8d47dbb015/benchmark-lite2-environment.json` | 1 |
| Historical benchmark results | `benchmarks/lite/results/WHB-LITE2-20261005T090156Z-39716c676c8d47dbb015/benchmark-lite2-results.json` | 3 |
| Historical documentation | `docs/audit/excel-import-compatibility-layer-implementation-report.md` | 1 |
| Historical documentation | `docs/audit/excel-import-current-architecture-audit.md` | 5 |
| Historical documentation | `docs/audit/excel-import-redesign-implementation-report.md` | 1 |
| Historical documentation | `docs/audit/master-import-db-first-target-contract.md` | 1 |
| Historical documentation | `docs/reports/excel-import-readiness-audit-2026-10-05.md` | 6 |
| Historical documentation | `docs/reports/master-excel-import-phase2-contract-matrix-2026-10-08.md` | 1 |
| Historical documentation | `docs/reports/master-excel-import-phase2-final-2026-10-08.md` | 1 |
| Historical documentation | `docs/reports/master-excel-import-phase2a-unit-2026-10-08.md` | 1 |
| Historical documentation | `docs/reports/product-excel-import-phase1a-implementation-2026-10-08.md` | 2 |
| Historical documentation | `docs/reports/product-excel-import-phase1b-continuation-2026-10-08.md` | 1 |
| Historical documentation | `docs/reports/warehouse-architecture-cleanup-phase2-20260911.md` | 6 |
| Historical documentation | `docs/reports/warehouse-permission-refactor-review-20260911.md` | 6 |
| Historical documentation | `docs/reports/warehouse-permission-sql-contract-cleanup-phase3-20260911.md` | 2 |
| Historical documentation | `docs/reports/warehouse-permission-sql-migration-impact-audit-20260911.md` | 1 |
| Historical documentation | `docs/testing/warehouse-master-class-decomposition.tdd.md` | 1 |
| Historical documentation | `docs/testing/warehouse-module-structure-refactor.tdd.md` | 1 |
| Preserved previous plan artifacts | `TK_COMPANY_STYLE_CALLER_MIGRATION_MAP.csv` | 74 |
| Preserved previous plan artifacts | `TK_COMPANY_STYLE_CLASS_MAP.csv` | 1 |
| Preserved previous plan artifacts | `TK_COMPANY_STYLE_CLASS_REFACTOR_PLAN.md` | 3 |
| Preserved previous plan artifacts | `TK_COMPANY_STYLE_METHOD_MAP.csv` | 15 |
| Retirement assertions | `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseMasterFinalizationContractTests.cs` | 4 |
