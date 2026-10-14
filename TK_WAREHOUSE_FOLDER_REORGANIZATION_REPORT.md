# Warehouse Controller Folder Reorganization Report

**Final status: WAREHOUSE_FOLDER_REORGANIZATION_COMPLETE**
**Scope:** move-only file/folder/namespace reorganization; no business behavior or database work.

## 1. Baseline and target trees

Baseline contained these six production files under `TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/`:

- `CWarehouse_Controller_Base.cs`
- `CWarehouseActionHistory_Recorder.cs`
- `CWarehouseDocumentSupport_Controller.cs`
- `CWarehouseMasterSupport_Controller.cs`
- `CWarehousePermissionFilter.cs`
- `CWarehouseReport_Controller.cs`

Target layout:

```text
TKS_Thuc_Tap_V11_Data_Access/
├── Controller/
│   ├── Common/CWarehouse_Controller_Base.cs
│   ├── DM/CWarehouseMasterSupport_Controller.cs
│   ├── XNK/CWarehouseDocumentSupport_Controller.cs
│   └── BC/CWarehouseReport_Controller.cs
└── Utility/
    ├── CWarehouseActionHistory_Recorder.cs
    └── CWarehousePermissionFilter.cs
```

`Controller/Warehouse` is absent after the move. Existing DM, XNK, Cache, Sys, Entity/Warehouse, and other prior refactor work remains in place.

## 2. Six file and namespace moves

| Old path | New path | Old namespace | New namespace |
|---|---|---|---|
| `Controller/Warehouse/CWarehouse_Controller_Base.cs` | `Controller/Common/CWarehouse_Controller_Base.cs` | `TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse` | `TKS_Thuc_Tap_V11_Data_Access.Controller.Common` |
| `Controller/Warehouse/CWarehouseActionHistory_Recorder.cs` | `Utility/CWarehouseActionHistory_Recorder.cs` | `TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse` | `TKS_Thuc_Tap_V11_Data_Access.Utility` |
| `Controller/Warehouse/CWarehouseDocumentSupport_Controller.cs` | `Controller/XNK/CWarehouseDocumentSupport_Controller.cs` | `TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse` | `TKS_Thuc_Tap_V11_Data_Access.Controller.XNK` |
| `Controller/Warehouse/CWarehouseMasterSupport_Controller.cs` | `Controller/DM/CWarehouseMasterSupport_Controller.cs` | `TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse` | `TKS_Thuc_Tap_V11_Data_Access.Controller.DM` |
| `Controller/Warehouse/CWarehousePermissionFilter.cs` | `Utility/CWarehousePermissionFilter.cs` | `TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse` | `TKS_Thuc_Tap_V11_Data_Access.Utility` |
| `Controller/Warehouse/CWarehouseReport_Controller.cs` | `Controller/BC/CWarehouseReport_Controller.cs` | `TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse` | `TKS_Thuc_Tap_V11_Data_Access.Controller.BC` |

Class names and public method names were retained. The only non-namespace source adjustment was qualifying the existing `CLogger.Warning` reference after the recorder moved into `Utility`.

## 3. Callers, tests, benchmarks, and path/reflection references

Production caller/import updates:

- `TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Nhap_Kho_Controller.cs`
- `TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Xuat_Kho_Controller.cs`
- `TKS_Thuc_Tap_V11_Web_Danh_Muc/_Imports.razor`
- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_1_Warehouse_List.razor`
- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseProductSupport_Service.cs`
- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseTransactionImportService.cs`

Twenty-three test files were updated only for namespace, physical path, or reflection-based assertions. The active benchmark references were updated in eight files: `TKS_Thuc_Tap_V11_Benchmarks/WarehouseReadOperations.cs`, `TKS_Thuc_Tap_V11_Benchmarks_V2/V2ReadOperations.cs`, `TKS_Thuc_Tap_V11_Benchmarks_V22/V22CorrectnessRunner.cs`, `TKS_Thuc_Tap_V11_Benchmarks_V22/V22PerformanceRunner.cs`, `benchmarks/lite/BenchmarkLiteV2.cs`, `benchmarks/lite/Program.cs`, `benchmarks/v2.2/q2-qualification/Program.cs`, and `benchmarks/v2.2/q2-qualification/WarehouseDocumentQualificationHarness.cs`.

The post-move search found **zero active source, test, benchmark, project/config, or runtime path references** to the retired namespace/path. Reflection and source-path assertions in active tests/benchmarks now use the moved locations. No project-file or DI architecture changes were needed.

## 4. Remaining historical references

The pre-report audit found old paths/namespaces only in historical documents, mapping artifacts, and saved benchmark evidence. These were intentionally retained per the instruction not to rewrite historical evidence:

- 18 Markdown documents: `docs/audit/excel-import-current-architecture-audit.md`; `docs/reports/excel-import-readiness-audit-2026-10-05.md`; `docs/reports/master-excel-import-phase2-final-2026-10-08.md`; `docs/reports/product-excel-import-phase1a-implementation-2026-10-08.md`; `docs/reports/warehouse-architecture-cleanup-phase2-20260911.md`; `docs/reports/warehouse-permission-sql-migration-impact-audit-20260911.md`; `docs/testing/production-readiness/Inventory-Phase9-Production-Gate-20260907.md`; `docs/testing/warehouse-architecture-refactor.tdd.md`; `docs/testing/warehouse-crud-contract.tdd.md`; `TK_COMPANY_STYLE_CLASS_7_8_9_IMPLEMENTATION_REPORT.md`; `TK_COMPANY_STYLE_CLASS_REFACTOR_PLAN.md`; `TK_COMPANY_STYLE_MASTER_ENTITY_DECOMPOSITION_REPORT.md`; `TK_COMPANY_STYLE_MASTER_FINALIZATION_REPORT.md`; `TK_COMPANY_STYLE_REFACTOR_RISK_MATRIX.md`; `TK_COMPANY_STYLE_XNK_IMPLEMENTATION_REPORT.md`; `TK_COMPANY_STYLE_XNK_REFACTOR_PLAN.md`; `TK_FUNCTION_CODE_RENUMBER_PLAN.md`; `TK_FUNCTION_CODE_RENUMBER_REPORT.md`.
- Three CSV mapping artifacts: `TK_COMPANY_STYLE_CLASS_MAP.csv`, `TK_COMPANY_STYLE_METHOD_MAP.csv`, and `TK_COMPANY_STYLE_XNK_CLASS_MAP.csv`.
- Fourteen JSON files in eight saved benchmark result folders under `benchmarks/lite/results/`: two `WHB-LITE-*` runs with `benchmark-lite-results.json`, and six `WHB-LITE2-*` runs with `benchmark-lite2-environment.json` and `benchmark-lite2-results.json`.

These are documentation/evidence references, not active build or runtime dependencies. The report itself records the old-to-new paths as required.

## 5. Behavior and SQL/database audit

All six moved files were compared with their baseline `HEAD` versions after normalizing only the approved namespace/import/type-reference changes. All six normalized comparisons passed. No method-body rewrite, class rename, public method rename, Store rename, parameter-order change, result mapping change, or control-flow change was found.

The move preserves the existing Store calls and behavior for paging, posting, reports, authorization, inventory/reservation, action-history recording, and Excel import. No SQL source file was changed; no database deployment, write, or schema/data change was performed.

## 6. TDD and verification results

| Check | Result |
|---|---|
| Focused regression baseline | 158 passed, 0 failed, 0 skipped |
| RED path-contract check after updating assertions but before moving files | 3 expected location failures; 13 passed |
| Focused regression after moves | 158 passed, 0 failed, 0 skipped |
| Relevant builds | 9 projects, 0 errors |
| `git diff --check` | Pass; only Git LF/CRLF conversion notices |
| Active old-path/namespace search | 0 matches |
| `Controller/Warehouse` | Removed; no production files remain |

The nine successful builds covered Data Access, Web Danh Mục, Web, Data Access.Tests, the three benchmark projects, `benchmarks/lite`, and Q2 qualification. Warning counts were respectively 0, 12, 4, 12, 0, 1, 0, 0, and 0; all were non-fatal. The Web process was already running and held its normal output DLL, so that build used an isolated temporary output directory without stopping the user's process.

## 7. Deferred naming cleanup

The moved support/base class names remain unchanged because this task approved folder/namespace cleanup only and no company naming rule was provided for these support types. Historical reports/maps and saved benchmark outputs remain as evidence. No logic cleanup was mixed into this change.

## 8. Required confirmations

| Confirmation | Status |
|---|---|
| `WAREHOUSE_BASE_MOVED_TO_COMMON` | PASS |
| `ACTION_HISTORY_RECORDER_MOVED_TO_UTILITY` | PASS |
| `DOCUMENT_SUPPORT_MOVED_TO_XNK` | PASS |
| `MASTER_SUPPORT_MOVED_TO_DM` | PASS |
| `PERMISSION_FILTER_MOVED_TO_UTILITY` | PASS |
| `REPORT_CONTROLLER_MOVED_TO_BC` | PASS |
| `CLASS_NAMES_UNCHANGED` | PASS |
| `PUBLIC_METHOD_NAMES_UNCHANGED` | PASS |
| `METHOD_BODIES_LOGIC_UNCHANGED` | PASS |
| `STORE_NAMES_UNCHANGED` | PASS |
| `STORE_PARAMETER_ORDER_UNCHANGED` | PASS |
| `SQL_SOURCE_UNCHANGED` | PASS |
| `DATABASE_UNCHANGED` | PASS — no database action was performed |
| `AUTHORIZATION_BEHAVIOR_UNCHANGED` | PASS |
| `POST_BEHAVIOR_UNCHANGED` | PASS |
| `INVENTORY_BEHAVIOR_UNCHANGED` | PASS |
| `RESERVATION_BEHAVIOR_UNCHANGED` | PASS |
| `EXCEL_IMPORT_BEHAVIOR_UNCHANGED` | PASS |
| `DM_PREVIOUS_WORK_PRESERVED` | PASS |
| `XNK_PREVIOUS_WORK_PRESERVED` | PASS |
| `CLASS_7_8_9_PREVIOUS_WORK_PRESERVED` | PASS |
| `BUILD_PASS` | PASS |
| `REGRESSION_PASS` | PASS |
| `WAREHOUSE_CONTROLLER_FOLDER_RETIRED` | PASS |

**Final status: WAREHOUSE_FOLDER_REORGANIZATION_COMPLETE**
