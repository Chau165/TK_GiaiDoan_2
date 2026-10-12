# Company-Style Master Entity Decomposition Report

Status: `COMPANY_STYLE_MASTER_ENTITY_DECOMPOSITION_PARTIAL`

The five DM entities, controllers, normal CRUD forms, and five master import writers now use feature-specific models. The generic models remain for shared Page/grid/display contracts and existing Document, Permission, and Report support that this task explicitly leaves out of scope. No generic model was deleted.

## Entity fields and evidence

The entity namespace and location follow the existing `Entity/DM` convention: `TKS_Thuc_Tap_V11_Data_Access.Entity.DM`. Field names and SQL widths were checked against `Database/WarehouseModule.Schema.sql` (tables at lines 38–97), current generic model/UI/import use, and the list/lookup Store result projections in `Database/WarehouseModule.Procedures.sql` (lines 3746–3822). The `Created_By*` and `Last_Updated_By*` properties preserve the existing C# model and Store audit arguments; the SQL tables themselves persist `Created` and `Last_Updated` timestamps.

| Entity | Business properties | Shared properties |
|---|---|---|
| `CDM_Kho` | `Auto_ID: long`, `Ten_Kho: string` (255), `Ghi_Chu: string` | `Created`, `Last_Updated: DateTime?`; four audit identity/function strings (100) |
| `CDM_Don_Vi_Tinh` | `Auto_ID: long`, `Ten_Don_Vi_Tinh: string` (200), `Ghi_Chu: string` | Same audit properties |
| `CDM_Loai_San_Pham` | `Auto_ID: long`, `Ma_LSP: string` (100), `Ten_LSP: string` (200), `Ghi_Chu: string` | Same audit properties |
| `CDM_San_Pham` | `Auto_ID: long`, `Ma_San_Pham: string` (100), `Ten_San_Pham: string` (255), `Loai_San_Pham_ID: long`, `Don_Vi_Tinh_ID: long`, `Ghi_Chu: string` | Same audit properties |
| `CDM_NCC` | `Auto_ID: long`, `Ma_NCC: string` (100), `Ten_NCC: string` (255), `Ghi_Chu: string` | Same audit properties |

The five entity source files are under `TKS_Thuc_Tap_V11_Data_Access/Entity/DM/`. `Ghi_Chu` remains a non-null C# string, matching the prior model; SQL defines it as nullable `NVARCHAR(1000)`. Product relation fields follow the database foreign-key names and use `long` for `BIGINT`.

List Store results use generic SQL aliases. The controllers map them explicitly without changing SQL:

- Kho: `Name` → `Ten_Kho`.
- Đơn vị tính: `Name` → `Ten_Don_Vi_Tinh`.
- Loại sản phẩm: `Code`/`Name` → `Ma_LSP`/`Ten_LSP`.
- Sản phẩm: `Code`/`Name`/`Related_ID`/`Related_ID_2` → `Ma_San_Pham`/`Ten_San_Pham`/`Loai_San_Pham_ID`/`Don_Vi_Tinh_ID`.
- NCC: `Code`/`Name` → `Ma_NCC`/`Ten_NCC`.

The lookup Stores return the matching `Auto_ID`, `Code`, and `Name` subset. Each normalized controller returns its dedicated entity for both list and lookup methods, mapping aliases in C#; no SQL alias was added or changed.

## Controller and caller changes

Each controller in `TKS_Thuc_Tap_V11_Data_Access/Controller/DM/` now accepts/returns only its matching entity for normal list, lookup, insert, transaction insert, and update operations. Delete signatures remain ID-based. Store method names, Store targets, positional argument order, audit assignment, and transaction overload shape are preserved. Update continues to use `ExecuteScalar` and reassign `Auto_ID`, matching the existing update Store result. Contract tests check method signatures and positional arguments.

The Master Razor edit forms hold the five typed entities, and Add and Update callbacks invoke the matching entity controller operation. The generic row used by the shared grid is only a presentation/page projection; its edit handler maps the selected row into the typed form model. A redundant generic clone on the edit path was removed. The generic info view and shared Telerik grid still use the support model while Page naming remains unresolved.

The five master import validators and writers now validate/create `CDM_Kho`, `CDM_Don_Vi_Tinh`, `CDM_Loai_San_Pham`, `CDM_San_Pham`, and `CDM_NCC` respectively. Their existing transaction overloads remain the write path. The product paging support service continues to use `sp_DM_Master_Page` and explicitly maps its result to `CDM_San_Pham` for import validation.

## Remaining generic-model audit

Repository-wide source and text searches found no `CWarehouseMaster` or `CWarehouseLookup` references in the five normalized DM controllers or the five master import writers. Remaining active source references are classified below.

| Model | Remaining owners | Classification / reason |
|---|---|---|
| `CWarehouseMaster` | `Entity/Warehouse/CWarehouseMasterModels.cs`; `Controller/Warehouse/CWarehouseMasterSupport_Controller.cs`; `FWarehouse_1_Warehouse_List.razor`; `FWarehouse_2_Warehouse_Master_Info.razor`; `Services/CWarehouseProductSupport_Service.cs` | `TEMPORARY_SHARED_PAGE_MODEL`: shared `sp_DM_Master_Page` payload, one grid/info projection, and product import paging adapter. The five DM CRUD controllers do not use it. |
| `CWarehouseMaster` | `Entity/Warehouse/CTransactionImportPreviewModels.cs`; `Utility/CTransactionImportPreviewService.cs`; `Services/CWarehouseTransactionImportService.cs`; `TransactionImportPreviewTests.cs` | `IMPORT` support contract for the Document transaction preview flow. The Web service adapts typed DM lookup results to that existing contract. Document/XNK migration was explicitly out of scope. |
| `CWarehouseLookup` | `Entity/Warehouse/CWarehouseLookupModels.cs`; `Controller/Warehouse/CWarehouseMasterSupport_Controller.cs`; `FWarehouse_1_Warehouse_List.razor`; `FWarehouse_3_Warehouse_Master_Edit.razor`; `FWarehouse_3_Warehouse_Edit.razor` | `TEMPORARY_SHARED_SUPPORT`: shared Page lookup and common dropdown contract used by DM, Document, and Permission views. Normalized DM lookup Stores themselves return typed entities. |
| `CWarehouseLookup` | `Controller/Warehouse/CWarehousePermission_Controller.cs`; `Controller/Warehouse/CWarehousePermissionFilter.cs`; `Controller/Warehouse/CWarehouseReport_Controller.cs` | `FEATURE_CONTROLLER` support for Permission and Report consumers, which remain outside this task. |
| Either model | `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseMasterEntityDecompositionContractTests.cs`, `WarehouseMasterControllerSplitContractTests.cs`, `WarehouseMasterFinalizationContractTests.cs`, `WarehouseModuleStructureTests.cs`, `WarehouseUiWorkflowTests.cs`, `WarehousePermissionFilterTests.cs`, `WarehousePermissionRefactorTests.cs`, `TransactionImportPreviewTests.cs`, and `Performance/PerformanceBenchmark.cs` | `TEST`/`BENCHMARK`: fixtures and contracts for the existing shared Page, Permission, Document-import, or benchmark behavior. |
| Either model | `TKS_Thuc_Tap_V11_Benchmarks/WarehouseReadOperations.cs`, `WarehouseBenchmarks.cs`; `TKS_Thuc_Tap_V11_Benchmarks_V2/V2ReadOperations.cs`; `TKS_Thuc_Tap_V11_Benchmarks_V22/V22PerformanceRunner.cs`, `V22CorrectnessRunner.cs`; `benchmarks/lite/BenchmarkLiteV2.cs`, `Program.cs`, `README.md` | `BENCHMARK`: existing Page/lookup measurement paths. No benchmark source or result was rewritten as part of the entity change. |
| Either model | Existing `TK_COMPANY_STYLE_*` reports/maps, `docs/audit/*`, `docs/reports/*`, `docs/testing/*`, benchmark manifests, and `benchmarks/lite/results/*` | `HISTORICAL_DOC`/benchmark evidence. Search hits are historical references; these files and recorded results were preserved. |

The checked-in generic classes are therefore retained, not retired. The remaining dependency is limited to shared support/UI projections and the explicitly excluded Document, Permission, and Report contracts. Retiring these models requires a separate migration of those contracts and their callers.

## Store, transaction, and scope invariants

- The five Fxxxx list, lookup, insert, update, and delete Store names remain unchanged. Procedure definitions and parameters in `Database/WarehouseModule.Procedures.sql` were not edited.
- Each public low-level method still targets one Store named after that method. Transaction insert overloads call the same insert Store. Contract tests verify the existing positional argument order.
- Insert ID assignment, update `ExecuteScalar`/`Auto_ID` assignment, `Set_Audit_Fields`, and import transaction ownership/rollback code remain in place.
- `sp_DM_Master_Page` and `sp_DM_Lookup_Page` remain unchanged in SQL and in `CWarehouseMasterSupport_Controller`.
- No business rules, routes, permissions, error mapping, audit values, database data, or schema were changed. No SQL or database files are in the source diff.

## Verification

- Data Access build: passed, 0 warnings and 0 errors (verified before the final UI-only cleanup).
- Web build after final cleanup: passed, 0 errors; 15 warnings.
- Earlier focused architecture/import/UI test runs: 232 passed, 0 failed, 0 skipped.
- Final focused controller/entity/UI regression run: 85 passed, 0 failed, 0 skipped.
- Final non-database `PerformanceBenchmarkTests`: 7 passed; the database benchmark case was excluded.
- The task-specific entity contract was first run RED before implementation (13 expected failures), then passed after implementation. RED test checkpoints are `299e1b3` and `f4db7af`.
- Database-writing tests and live database verification were not run. The live SQL preflight could not open a connection because the available `Microsoft.Data.SqlClient` provider reports that its constructor is unsupported on this platform. Checked-in SQL was used as the schema and Store-contract evidence.
- The repository has no configured coverage collector for this test project; numerical coverage was not measured.

## Worktree preservation and remaining blocker

The pre-existing untracked class/method/caller maps, plan, risk matrix, and TDD note were not overwritten or included in the checkpoint. Existing benchmark results and historical reports were also left untouched. The task-specific implementation checkpoint is local only; nothing was pushed.

The remaining blocker is retirement of generic support models: shared Page/grid payloads plus out-of-scope Document import, Permission, and Report consumers still depend on them. The Page Store naming rule is unresolved.

## Required confirmations

```text
DM_KHO_ENTITY_SPLIT_COMPLETE
DM_DON_VI_TINH_ENTITY_SPLIT_COMPLETE
DM_LOAI_SAN_PHAM_ENTITY_SPLIT_COMPLETE
DM_SAN_PHAM_ENTITY_SPLIT_COMPLETE
DM_NCC_ENTITY_SPLIT_COMPLETE
DM_CONTROLLERS_NO_LONGER_USE_CWarehouseMaster
IMPORT_SERVICES_USE_DEDICATED_DM_ENTITIES
ONE_ENTITY_ONE_CONTROLLER_TARGET_VERIFIED
ONE_METHOD_ONE_STORE_PRESERVED
METHOD_NAME_EQUALS_STORE_PRESERVED
STORE_NAMES_UNCHANGED
STORE_PARAMETER_ORDER_UNCHANGED
AUDIT_BEHAVIOR_UNCHANGED
TRANSACTION_BEHAVIOR_UNCHANGED
DATABASE_UNCHANGED
SQL_UNCHANGED
NO_BUSINESS_LOGIC_CHANGE
PAGE_SUPPORT_UNCHANGED
GENERIC_SUPPORT_MODEL_TEMPORARILY_RETAINED
COMPANY_STYLE_MASTER_ENTITY_DECOMPOSITION_PARTIAL
```
