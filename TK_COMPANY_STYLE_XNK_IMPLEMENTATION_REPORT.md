# Company-style XNK implementation report

**Status: `COMPANY_STYLE_XNK_IMPLEMENTATION_PARTIAL`**

The C# decomposition and source result-contract updates are implemented. The live database deployment of the four read-projection changes was blocked by the automatic approval review before execution. No live database object was changed.

## 1. Planning-ledger revalidation

Revalidated the six plan artifacts against the current controller callers, Store definitions, UI bindings, import mapping, and cache boundary. `Save_Document_Async` maps to the four expected Add/Edit Stores. A final CSV structural audit found a blank row in the class map and four caller-map rows missing the migration-type column; those ledger formatting issues were corrected. All CSV rows now match their headers:

| Artifact | Data rows | Columns | Invalid rows |
|---|---:|---:|---:|
| `TK_COMPANY_STYLE_XNK_CLASS_MAP.csv` | 34 | 10 | 0 |
| `TK_COMPANY_STYLE_XNK_METHOD_MAP.csv` | 27 | 14 | 0 |
| `TK_COMPANY_STYLE_XNK_CALLER_MAP.csv` | 30 | 11 | 0 |

The corrected call map records the four import insert calls as transaction-overload calls. These corrections do not change the source behavior or routing decisions.

## 2. Files changed by this implementation

**Added:**

- `TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Nhap_Kho_Controller.cs`
- `TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Xuat_Kho_Controller.cs`
- `TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehouseDocumentSupport_Controller.cs`
- `TKS_Thuc_Tap_V11_Data_Access/Entity/XNK/CXNK_Nhap_Kho.cs`
- `TKS_Thuc_Tap_V11_Data_Access/Entity/XNK/CXNK_Nhap_Kho_Detail.cs`
- `TKS_Thuc_Tap_V11_Data_Access/Entity/XNK/CXNK_Xuat_Kho.cs`
- `TKS_Thuc_Tap_V11_Data_Access/Entity/XNK/CXNK_Xuat_Kho_Detail.cs`
- `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseDocumentTestHarness.cs`
- `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseXnkSplitContractTests.cs`
- `benchmarks/v2.2/q2-qualification/WarehouseDocumentQualificationHarness.cs`
- `TK_COMPANY_STYLE_XNK_RESULT_CONTRACT_ROLLBACK.sql`

**Modified:**

- `Database/WarehouseModule.Procedures.sql` (the four XNK aliases below; existing DM edits remain preserved)
- `TKS_Thuc_Tap_V11_Data_Access/Entity/Warehouse/CTransactionImportPreviewModels.cs`
- `TKS_Thuc_Tap_V11_Data_Access/Utility/CTransactionImportPreviewService.cs`
- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_1_Warehouse_List.razor`
- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseTransactionImportService.cs`
- Tests: `Performance/PerformanceBenchmark.cs`, `TransactionImportPreviewTests.cs`, `WarehouseAuthorizationContractTests.cs`, `WarehouseAuthorizationIntegrationTests.cs`, `WarehouseCrudContractIntegrationTests.cs`, `WarehouseGroupBSplitContractTests.cs`, `WarehouseGroupCDesharingContractTests.cs`, `WarehouseIssueReservationIntegrationTests.cs`, `WarehouseModuleStructureTests.cs`, `WarehousePhase6M02M03IntegrationTests.cs`, and `WarehouseUiWorkflowTests.cs`
- Benchmark callers: `TKS_Thuc_Tap_V11_Benchmarks/WarehouseReadOperations.cs`, `TKS_Thuc_Tap_V11_Benchmarks_V2/V2ReadOperations.cs`, `TKS_Thuc_Tap_V11_Benchmarks_V22/V22CorrectnessRunner.cs`, `TKS_Thuc_Tap_V11_Benchmarks_V22/V22PerformanceRunner.cs`, `benchmarks/lite/Program.cs`, `benchmarks/lite/BenchmarkLiteV2.cs`, and `benchmarks/v2.2/q2-qualification/Program.cs`
- Benchmark metadata: `benchmarks/lite/README.md`, `benchmarks/v2/benchmark-v2-manifest.json`, `benchmarks/v2.1/benchmark-v2.1-adaptive-manifest.json`, and `benchmarks/v2.1-mixed/benchmark-v2.1-mixed-manifest.json`
- Planning ledgers: `TK_COMPANY_STYLE_XNK_CLASS_MAP.csv` and `TK_COMPANY_STYLE_XNK_CALLER_MAP.csv`

**Deleted:** `TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehouseDocument_Controller.cs`.

The source tree also contained pre-existing DM/cache changes. They were preserved and not staged or committed.

## 3. Typed entities and field evidence

Created four separate classes in `Entity/XNK`:

- `CXNK_Nhap_Kho` and `CXNK_Xuat_Kho`: document ID, receipt/issue number and date, warehouse/supplier fields present in the respective Store result contracts, posted state, note, login, and create/update audit fields.
- `CXNK_Nhap_Kho_Detail` and `CXNK_Xuat_Kho_Detail`: detail ID, typed document foreign key, product ID/code/name, unit name, typed quantity and price properties, login, and audit fields.

Fields were checked against the F3001/F3002 parameters and result columns, the SQL table columns, Razor bindings, and import DTO mappings. `Is_Receipt` remains on the generic Page/UI projection seam and is not a selector in either typed Store controller.

## 4. Result-contract change

Only the four F3001/F3002 read projections in the local SQL source were changed. Filters, joins, authorization calls, ordering, result values, parameters, and write Stores were retained.

| Store | Previous result aliases | Source aliases for typed mapping |
|---|---|---|
| `F3001_sp_sel_List_Nhap_Kho` | `So_Phieu`, `Ngay_Chung_Tu` | `So_Phieu_Nhap_Kho`, `Ngay_Nhap_Kho` |
| `F3001_sp_sel_List_Nhap_Kho_Detail` | `Document_ID`, `So_Luong`, `Don_Gia` | `Nhap_Kho_ID`, `SL_Nhap`, `Don_Gia_Nhap` |
| `F3002_sp_sel_List_Xuat_Kho` | `So_Phieu`, `Ngay_Chung_Tu` | `So_Phieu_Xuat_Kho`, `Ngay_Xuat_Kho` |
| `F3002_sp_sel_List_Xuat_Kho_Detail` | `Document_ID`, `So_Luong`, `Don_Gia` | `Xuat_Kho_ID`, `SL_Xuat`, `Don_Gia_Xuat` |

Both entity controllers use `CUtility.Map_Row_To_Entity<T>(v_row)` directly and dispose their `DataTable`. They use explicit types, full method bodies, and synchronous Store access.

## 5. Controller inventories and Store contract

`CXNK_Nhap_Kho_Controller` owns these eight distinct Stores:

- `F3001_sp_sel_List_Nhap_Kho`
- `F3001_sp_sel_List_Nhap_Kho_Detail`
- `F3001_sp_ins_Nhap_Kho_Header`
- `F3001_sp_upd_Nhap_Kho_Header`
- `F3001_sp_del_Nhap_Kho_Header`
- `F3001_sp_ins_Nhap_Kho_Detail`
- `F3001_sp_upd_Nhap_Kho_Detail`
- `F3001_sp_del_Nhap_Kho_Detail`

`CXNK_Xuat_Kho_Controller` owns the corresponding eight F3002 Stores: `F3002_sp_sel_List_Xuat_Kho`, `F3002_sp_sel_List_Xuat_Kho_Detail`, and the insert/update/delete Header and Detail Stores. Each controller has two additional insert overloads, for ten public methods total; each overload pair calls the same single Store.

The contract test checks that each method contains one Store literal, the method name equals that literal, the receipt controller has no F3002 Store, the issue controller has no F3001 Store, and neither typed controller contains `p_bIs_Receipt` or chooses Insert versus Update from `Auto_ID`.

### Transaction overloads and parameter order

The four transaction overloads accept `SqlConnection`, `SqlTransaction`, then the typed entity. They pass that exact connection and transaction to `Scalar_ID`; the controllers do not open a second connection.

The insert argument order matches the source Store definitions:

- F3001 Header: `Auto_ID`, number, warehouse, supplier, date, note, login, Created audit pair, Last_Updated audit pair.
- F3001 Detail: `Auto_ID`, `Nhap_Kho_ID`, product, quantity, price, login, Created audit pair, Last_Updated audit pair.
- F3002 Header: `Auto_ID`, number, warehouse, date, note, login, Created audit pair, Last_Updated audit pair.
- F3002 Detail: `Auto_ID`, `Xuat_Kho_ID`, product, quantity, price, login, Created audit pair, Last_Updated audit pair.

The same ordered parameter construction is used by each transaction overload and its ordinary insert method. Update/Delete audit nullability and login arguments preserve the prior calls.

## 6. Caller migration and transaction boundary

- `FWarehouse_1_Warehouse_List.razor` now chooses the receipt or issue controller at the page-feature boundary, and chooses Insert or Update in the UI caller. Detail CRUD and full-list reads also call the entity controllers directly. Page and Post use the shared support controller.
- `CWarehouseTransactionImportService` maps inbound/outbound import DTOs to the corresponding typed entities. Fresh duplicate checks read typed header/detail Stores.
- `CWarehouseTransactionImportStore` remains the transaction owner: one connection and transaction per imported document, then header insert, ordered detail inserts, Commit; an error rolls back the current document. The UI header/detail edit boundary remains separate.

## 7. Page/Post, Inventory, and Reservation boundaries

`CWarehouseDocumentSupport_Controller` exposes only `sp_XNK_Document_Page` and `sp_XNK_Document_Post`; each method calls its identically named single Store. Page keeps its generic projection and `Is_Receipt` input at the shared support boundary.

The source SQL for Page, Post, the 12 F3001/F3002 write Stores, and all 35 `sp_Inventory_*` Stores is guarded by the XNK split contract's frozen-source checks. No Post, Inventory, Reservation, or XNK write-procedure body was changed. This is source/contract evidence; live transaction behavior was not integration-tested in this run.

## 8. Remaining generic model references

`CWarehouseDocument` and `CWarehouseDocumentDetail` are **temporarily retained for UI/Page projection** (`TEMPORARY_XNK_UI_PROJECTION`). Current production references are the shared list/edit/info Razor components, the `sp_XNK_Document_Page` return type, and the conversion seam between typed entities and the existing UI model. Tests and benchmark harnesses retain test-only projection adapters. Import persistence now uses typed XNK models. Generic model retirement is not complete.

## 9. Old controller retirement gate

- Active production callers: **0**.
- Active benchmark callers/manifests: migrated to `CWarehouseDocumentSupport_Controller.sp_XNK_Document_Page`.
- Test reference: the split contract test explicitly asserts that the old type and source file are absent; no active fixture instantiates it.
- Remaining non-runtime references are plan artifacts and historical docs/reports. Sixteen benchmark result/environment/report files preserve the recorded old operation and were left unchanged as required. The source controller file is retired.

## 10. Live SQL status and rollback

Read-only preflight returned `DB_NAME() = TKS_Thuc_Tap_V11_GiaiDoan2`. Live definitions of the four read Stores still had the old aliases; their non-projection logic matched the local source. A guarded deployment of only the four source procedure definitions was then attempted. The automatic approval review rejected the DB object mutation before execution with a policy-blocked result. No alternate deployment path was attempted and no live object was modified. Therefore source/live result-contract parity is **not complete**.

`TK_COMPANY_STYLE_XNK_RESULT_CONTRACT_ROLLBACK.sql` contains the four prior projection contracts and a database-name guard. It is a rollback artifact, not evidence of deployment.

## 11. Build, tests, and verification

- Focused contracts/import/UI suite: **89 passed, 0 failed, 0 skipped**. A preceding test-host process ended with an internal CLR error; an isolated run of the 11 XNK split tests passed, followed by the complete focused filter passing 89/89.
- Web project build after the final controller-style changes: **0 errors, 12 warnings** (Telerik licensing warnings).
- Earlier implementation builds: main benchmark project passed with 0 warnings/errors; Benchmark V2 passed with 1 warning and 0 errors; Benchmark V22 and Q2 qualification passed with 0 warnings/errors. Q2 restore was required for a missing local package before its successful build.
- `git diff --check`: exit 0; Git emitted line-ending normalization notices only.
- Destructive/database integration suites and benchmark measurements: **NOT RUN**. No isolated test database was confirmed; the configured default integration target is the shared development database.
- Coverage: **`COVERAGE_NOT_MEASURED`**.

## 12. Cache and worktree scope

No XNK transaction, document, inventory, reservation, or Post cache was added. Existing DM cache/controller work and dirty working-tree changes were preserved. No staging, commit, reset, restore, stash, or unrelated cleanup was performed.

## Required confirmations

| Confirmation | Result |
|---|---|
| `XNK_PLAN_LEDGER_REVALIDATED` | YES; CSV structure repaired and validated |
| `XNK_TYPED_ENTITIES_CREATED` | YES |
| `XNK_NHAP_CONTROLLER_SPLIT_COMPLETE` | YES |
| `XNK_XUAT_CONTROLLER_SPLIT_COMPLETE` | YES |
| `F3001_CONTROLLER_ONLY_CALLS_F3001` / `F3002_CONTROLLER_ONLY_CALLS_F3002` | YES |
| `ONE_METHOD_ONE_STORE_PRESERVED` / `METHOD_NAME_EQUALS_STORE_PRESERVED` | YES |
| `NO_RECEIPT_ISSUE_ROUTING_IN_TYPED_CONTROLLERS` / `NO_INSERT_UPDATE_ROUTING_IN_TYPED_CONTROLLERS` | YES |
| `FOUR_TRANSACTION_INSERT_OVERLOADS_COMPLETE` | YES |
| `DIRECT_TYPED_DATAROW_MAPPING_COMPLETE` | YES |
| `XNK_TYPED_RESULT_CONTRACT_ALIGNED` | SOURCE ONLY; live deployment blocked |
| `IMPORT_TRANSACTION_BOUNDARY_UNCHANGED` | YES by source path; integration behavior not run |
| `POST_STORE_UNCHANGED` / `PAGE_STORE_UNCHANGED` | YES by source contract checks |
| `POST_BEHAVIOR_UNCHANGED` / `PAGE_BEHAVIOR_UNCHANGED` | Source path preserved; runtime unverified |
| `INVENTORY_TECHNICAL_SUBSYSTEM_UNCHANGED` / `RESERVATION_BEHAVIOR_UNCHANGED` | SQL source unchanged; runtime unverified |
| `DM_PREVIOUS_WORK_PRESERVED` | YES |
| `NO_XNK_TRANSACTION_CACHE_CREATED` | YES |
| `OLD_DOCUMENT_CONTROLLER_ZERO_ACTIVE_CALLERS` / `OLD_DOCUMENT_CONTROLLER_RETIRED` | YES |
| `GENERIC_DOCUMENT_MODELS_RETIRED` | NO; `GENERIC_DOCUMENT_MODELS_TEMPORARILY_RETAINED_FOR_UI_PROJECTION` |

**Final status: `COMPANY_STYLE_XNK_IMPLEMENTATION_PARTIAL`** — blocker: automatic approval review prevented deployment of the four read-only result-projection changes to the verified development database.
