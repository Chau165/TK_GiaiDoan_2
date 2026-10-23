# Company Chu_Hang Excel Import Parity — Audit and Implementation

## Baseline before source changes

- Repository checkout: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`.
- Branch / HEAD: `main` / `98389f3509a6d120f16f57e27d9c9ef2573be9d3` (`Fix`).
- Before this phase, the worktree already contained 11 modified tracked files and three untracked files: six files from the preceding warehouse UI phase and five Master Data list files from the earlier migration. They are preserved; none were reset, cleaned, stashed, or reverted.
- Baseline focused import tests: 124 passed, 0 failed, 0 skipped. Command filtered `ImportStructureDetectionTests`, the five Master Data validation suites, `TransactionImportPreviewTests`, and `MasterDataCompanyStyleMigrationTests`.
- Read-only SQL verification confirmed `DB_NAME() = TKS_Thuc_Tap_V11_GiaiDoan2` on `DESKTOP-NHQ7QPL\MSSQLSERVER19`. Runtime definitions for the five Master Data Insert/Update procedures and the four Header/Detail Insert procedures matched `Database/WarehouseModule.Procedures.sql` after SQL normalization. The runtime also has unique indexes on the five Master Data business keys, product code, and receipt/issue document numbers. No business data was written.

## Pre-edit flow audit

| Module | Current Entry Point | Shared FBase | Excel Reader | Mapping | C# Validation | SQL Validation | Transaction | Cache/Log | Deviation |
|---|---|---|---|---|---|---|---|---|---|
| Chủ Hàng (2003) reference | `F2003_1_Chu_Hang_List.Import_Excel_Entry` | `FCommon_Import_Excel` → `FBase.Import_Excel` → override | `CExcel_Controller.List_Range_Value_To_End(0, "A2", "I")` | Positional `DataRow` columns A:I → entity | No separate business validator in the active page | Runtime `FQ_104_CH_sp_ins_Insert` checks required values and duplicate code/name/MST; procedure source is not present in repository SQL scripts | One transaction per row | Cache after commit; Action History after commit; FBase owns queue and import log | Reference pattern; current reference increments success before commit, so its ordering is not copied blindly |
| Kho (2009) | `F2009_1_Kho_List.Import_Excel_Entry` | Shared FBase template check, `FCommon_Import_Excel`, `Import_Excel_Entry`, queue and import log | `Read_Profiled_Import_Sheet("Warehouse")` through `CImportLegacyWorksheetAdapter` | Profile/canonical-field mapping into validator row | Required/length, duplicate/skip, formula and capability checks in `CWarehouseImportValidator` | `F2009_sp_ins_Kho` / `F2009_sp_upd_Kho`: required and unique name; runtime matches source. Runtime unique index `UX_tbl_DM_Kho_Ten` | One SQL transaction per row | Cache after commit; per-row Action History with warning isolation | Data written from validator row instead of the company positional `DataRow` pattern |
| Đơn vị tính (2016) | `FWarehouse_Unit_List.Import_Excel_Entry` | Same shared FBase path | `Read_Profiled_Import_Sheet("Unit")` plus adapter | Profile/canonical-field mapping | Required/length, duplicate/skip, formula and capability checks in `CUnitImportValidator` | `F2016_sp_ins_Don_Vi_Tinh` / `F2016_sp_upd_Don_Vi_Tinh`: required and unique name; runtime matches source. Runtime unique index `UX_tbl_DM_Don_Vi_Tinh_Ten` | One SQL transaction per row | Cache after commit; per-row Action History with warning isolation | Same mapping deviation; unit validator also preserves exact-duplicate SKIP behavior |
| Loại sản phẩm (2017) | `F2017_1_Loai_San_Pham_List.Import_Excel_Entry` | Same shared FBase path | `Read_Profiled_Import_Sheet("Category")` plus adapter | Profile/canonical-field mapping | Required/length, duplicate/skip, formula and capability checks in `CCategoryImportValidator` | `F2017_sp_ins_Loai_San_Pham` / `F2017_sp_upd_Loai_San_Pham`: required code/name and uniqueness; runtime matches source. Runtime has unique indexes for both fields | One SQL transaction per row | Cache after commit; per-row Action History with warning isolation | Same mapping deviation |
| Sản phẩm (2018) | `F2018_1_San_Pham_List.Import_Excel_Entry` | Same shared FBase path | `Read_Profiled_Import_Sheet("Product")` plus adapter | Profile/canonical-field mapping; category and unit references resolve to IDs | Required/length, duplicate/skip, formula/capability, and category/unit resolution in `CProductImportValidator` | `F2018_sp_ins_San_Pham` / `F2018_sp_upd_San_Pham`: required code/name, unique code, category/unit existence; runtime matches source. Runtime unique index on product code | One SQL transaction per row | Cache after commit; per-row Action History with warning isolation | Same mapping deviation; reference resolution must remain in C# to map Excel names/codes to foreign keys |
| Nhà cung cấp (2019) | `F2019_1_Nha_Cung_Cap_List.Import_Excel_Entry` | Same shared FBase path | `Read_Profiled_Import_Sheet("Supplier")` plus adapter | Profile/canonical-field mapping | Required/length, duplicate/skip, formula and capability checks in `CSupplierImportValidator` | `F2019_sp_ins_NCC` / `F2019_sp_upd_NCC`: required and unique code/name; runtime matches source. Runtime has unique indexes for both fields | One SQL transaction per row | Cache after commit; per-row Action History with warning isolation | Same mapping deviation |
| Nhập kho (3001) | `FWarehouse_Transaction_Import` → `CWarehouseTransactionImportService.PreviewAsync` / `ConfirmAsync` | Shared file-select style only; does not use FBase's Master Data import orchestration | Profiled two-sheet Header/Detail reader | Header and detail sheets grouped by document number, with original sheet/row identity | Preview resolves warehouse, supplier, product, unit, duplicates, quantity/price and other row/document issues; Confirm reloads references and revalidates | Runtime `F3001_sp_ins_Nhap_Kho_Header` / `Detail` matches repository source; insert SPs enforce scope, permission, references, positive quantity/price and draft-state rules | One transaction per document; header and all details commit/rollback together | Grid refresh and one summary Action History with Ref_ID 0; no import log or import queue in this component path | Header–Detail Preview/Confirm and atomic writes are required; import log/queue and per-document history linkage need parity work |
| Xuất kho (3002) | Same component/service with Outbound profile | Same as receipt | Profiled two-sheet Header/Detail reader | Grouped by document number; detail references resolve to products | Preview includes available-stock checks; Confirm reloads references and revalidates | Runtime `F3002_sp_ins_Xuat_Kho_Header` / `Detail` matches repository source; insert SPs enforce permission, product, positive quantity/price, posted-state and reservation rules | One transaction per document; header and all details commit/rollback together | Same current tracking gap as receipt | Keep Preview/Confirm, reservation rules and transaction boundary; do not route documents through Master Data `FBase.Import_Excel_Entry` |

### Evidence locations

- Chu_Hang positional import: `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_1_Chu_Hang_List.razor:119-200`.
- FBase shared import/header validation/log/queue: `TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor:341-417`; common file selection: `TKS_Thuc_Tap_V11_Web_Common/Common/FCommon_Import_Excel.razor:1-35,59-115`.
- `CExcel_Controller.List_Range_Value_To_End`: `TKS_Thuc_Tap_V11_Data_Access/Utility/CExcel_Controller.cs:197-227`; it returns values only and does not carry absolute Excel row metadata or formula metadata.
- Master Data import entry points: Kho `F2009_1_Kho_List.razor:99`; Unit `FWarehouse_Unit_List.razor:99`; Category `F2017_1_Loai_San_Pham_List.razor:100`; Product `F2018_1_San_Pham_List.razor:134`; Supplier `F2019_1_Nha_Cung_Cap_List.razor:100`.
- Transaction Preview/Confirm and atomic store: `CWarehouseTransactionImportService.cs:92,334,372,382,420`; parser/grouping: `CTransactionImportPreviewService.cs:27,48,104`.
- Transaction import UI confirmation callback: `FWarehouse_Transaction_Import.razor:286`; parent audit/log callback: `FWarehouse_Transaction_List.razor:240`.

## A. Changes made

| Area / files | Before | After | Reference / verification |
|---|---|---|---|
| Five active Master Data List components: `F2009_1_Kho_List.razor`, `FWarehouse_Unit_List.razor`, `F2017_1_Loai_San_Pham_List.razor`, `F2018_1_San_Pham_List.razor`, `F2019_1_Nha_Cung_Cap_List.razor` | Profile-validator rows were used as the source for entity values. | Keep profile checks, but read field values from `CExcel_Controller.List_Range_Value_To_End` and map each validated row by its original Excel row number. Successful counts/cache updates now occur after the row transaction commits. | Company positional pattern in `F2003_1_Chu_Hang_List.razor:119`; implementations at Kho `:99`, Unit `:99`, Category `:100`, Product `:134`, Supplier `:100`. Source/mapping tests pass. |
| `CExcel_Import_DataTable_Adapter.cs`; `MasterDataPositionalImportMappingTests.cs` | Validators and extracted DataTable could drift when blank rows were skipped. | Added bounds-checked, one-based Excel row to DataTable mapping (`ExcelRow - firstExcelRow`). A test covers a blank key row between populated rows and rejects out-of-range row numbers. | Adapter `TKS_Thuc_Tap_V11_Data_Access/Utility/CExcel_Import_DataTable_Adapter.cs:5-23`; tests `TKS_Thuc_Tap_V11_Data_Access.Tests/MasterDataPositionalImportMappingTests.cs:8-62`. |
| Five active `*ImportValidator.cs` files | C# duplicated required/unique business validation already enforced by Insert SPs. | Removed those shadow checks. Kept SQL parameter length guards, exact duplicate-row SKIP, formula/template/unsupported-column/capability checks, and Product category/unit resolution. | Product lookup: `CProductImportValidator.cs:127`; skip branches at lines 187 and 197. Focused validation tests pass. |
| `FWarehouse_1_Warehouse_List.razor`; UI contract tests | The shared host still contained hidden legacy Master import buttons/modal callbacks. | Removed those callbacks and modal hookups. The only routes using this host are Reports/Inventory and the warehouse-permission page; the five actual Master routes use the dedicated FBase List components. Export helper remains. | Host toolbar `FWarehouse_1_Warehouse_List.razor:51`; route references in `Warehouse_Bao_Cao.razor:5`, `Warehouse_Ton_Kho.razor:5`, `Warehouse_Phan_Quyen.razor:5`. Regression checks in `WarehouseUiWorkflowTests.cs:140` and `MasterDataCompanyStyleMigrationTests.cs:8`. |
| Transaction import: `CTransactionImportPreviewModels.cs`, `CWarehouseTransactionImportService.cs`, `FWarehouse_Transaction_Import.razor`, `FWarehouse_Transaction_List.razor` | Import returned aggregate counts only; post-commit history used a summary reference instead of each saved document ID. | Return committed document IDs; carry partial results when a later document fails; write queue/import log and per-document Action History after commit. Preview/Confirm, reference reload, Header–Detail transaction boundary, and document rules remain. Post-commit log/history/refresh issues are warnings, not failed inserts. | Service `CWarehouseTransactionImportService.cs:92`, commits `:372,420`; UI callback `FWarehouse_Transaction_Import.razor:175,286`; parent queue/log/history `FWarehouse_Transaction_List.razor:240,286,311`. Tests cover committed IDs and partial failure. |
| Tests | Prior focused baseline: 124 passed. | Added mapping and saved-ID/partial-result checks; updated stale source-contract expectations for the removed hidden host modals. | Final focused run: 173 passed, 0 failed, 0 skipped. |

## B. Final active import flows

| Module | Entry point and read/mapping | Shared FBase | Active custom work retained |
|---|---|---|---|
| Kho | `F2009_1_Kho_List.Import_Excel_Entry`; range A2:B; positional DataRow using original row number. | Yes: common template/header check, `FCommon_Import_Excel`, `FBase.Import_Excel`, queue and import log. | Profile/formula/capability checks; row validation; one-row SQL transaction; cache/history after commit. |
| Đơn vị tính | `FWarehouse_Unit_List.Import_Excel_Entry`; range A2:B; same mapping adapter. | Yes. | Same safeguards, with its existing exact duplicate SKIP behavior. |
| Loại sản phẩm | `F2017_1_Loai_San_Pham_List.Import_Excel_Entry`; range A2:C; same mapping adapter. | Yes. | Same safeguards. |
| Sản phẩm | `F2018_1_San_Pham_List.Import_Excel_Entry`; range A2:E; same mapping adapter. | Yes. | Product category/unit lookup stays in the validator; IDs are never defaulted when a lookup is missing. |
| Nhà cung cấp | `F2019_1_Nha_Cung_Cap_List.Import_Excel_Entry`; range A2:C; same mapping adapter. | Yes. | Same safeguards. |
| Nhập kho / Xuất kho | `FWarehouse_Transaction_Import` Preview → Confirm → `CWarehouseTransactionImportService`; Header and Detail sheets retain actual sheet/row identity. | No: this is a multi-document Header–Detail workflow, not a Master Data row import. | Preview/Confirm, per-document transaction, duplicate and reference validation, stock/permission business rules, partial document results, queue/log/history. |

The five active Master routes are `Warehouse.razor:5`, `Warehouse_Don_Vi_Tinh.razor:5`, `Warehouse_Loai_San_Pham.razor:5`, `Warehouse_San_Pham.razor:5`, and `Warehouse_Nha_Cung_Cap.razor:5`. They reference their dedicated List components. `FWarehouse_1_Warehouse_List` is routed only by `Warehouse_Bao_Cao.razor:5`, `Warehouse_Ton_Kho.razor:5`, and `Warehouse_Phan_Quyen.razor:5`; the legacy Master import UI was removed from that host.

### Excel mapping matrix

| Module | Excel Range | Mapping Method | Profile Removed? | Excel Row Accuracy |
|---|---|---|---|---|
| Kho | A2:B | `DataTable` → `CExcel_Import_DataTable_Adapter` → `CDM_Kho` | No; retained for formula/unsupported-capability checks | Original Excel row retained and used to select the matching DataRow. |
| Đơn vị tính | A2:B | `DataTable` → adapter → `CDM_Don_Vi_Tinh` | No | Same; blank-key/gap mapping is covered by test. |
| Loại sản phẩm | A2:C | `DataTable` → adapter → `CDM_Loai_San_Pham` | No | Same. |
| Sản phẩm | A2:E | `DataTable` → adapter → `CDM_San_Pham`; category/unit IDs from validated lookups | No | Same; invalid/missing lookup is reported and never mapped to a default ID. |
| Nhà cung cấp | A2:C | `DataTable` → adapter → `CDM_NCC` | No | Same. |
| Nhập kho / Xuất kho | Header and Detail sheets | Profiled header/detail rows → document model → Preview/Confirm | No; profile/parser required for document grouping and capability checks | Source sheet and row numbers remain on row diagnostics; not mapped through the Master Data adapter. |

## C. Validation and row identity

| Rule | Final enforcement | Status |
|---|---|---|
| Required business fields and unique code/name | Existing Insert Stored Procedures and database unique indexes; C# no longer emits competing required/duplicate outcomes before SQL. | Preserved at SQL boundary; runtime procedure definitions were compared read-only to repository source. |
| Exact duplicate rows in the uploaded workbook | Existing validator marks duplicates as skipped; row-level skip behavior remains. | Preserved. |
| Maximum string length | Validator length guard remains because Insert SP parameter widths are finite and input truncation must be prevented before the call. | Preserved; not treated as business uniqueness/required validation. |
| Product category and unit references | Validator resolves incoming labels to cache/lookup IDs and reports unresolved references; SQL remains the final FK/business check. | Preserved; no fallback/default IDs. |
| Formula cells, template/header/sheet, unsupported columns/capabilities | `CExcel_Controller` profile scan plus validator checks. | Preserved. Profile scan uses the already-open `ExcelPackage`; it is not a second custom reader reopening the file. FBase's existing template/header check remains unchanged. |
| Excel row number and DataTable mapping | `CExcel_Import_DataTable_Adapter.Get_Row_By_Excel_Row_Number` maps the validation result's original row to the range beginning at Excel row 2. | Verified for blank key rows and populated rows after gaps; out-of-range access throws. |

The row mapping test constructs Excel rows with blank key data and confirms row 5 still maps to its own DataTable values. Blank cells from `List_Range_Value_To_End` are represented as empty strings; the mapping does not depend on validators retaining the same list index after filtering.

## D. Stored Procedure compatibility

| Procedure group | Business checks used by import | Source changed? | Runtime definition verified? |
|---|---|---:|---:|
| `F2009_sp_ins_Kho` / `F2009_sp_upd_Kho` | Required and unique warehouse name. | No | Yes, read-only comparison to `Database/WarehouseModule.Procedures.sql`; runtime unique index also confirmed. |
| `F2016_sp_ins_Don_Vi_Tinh` / `F2016_sp_upd_Don_Vi_Tinh` | Required and unique unit name. | No | Yes, read-only comparison; runtime unique index confirmed. |
| `F2017_sp_ins_Loai_San_Pham` / `F2017_sp_upd_Loai_San_Pham` | Required and unique category code/name. | No | Yes, read-only comparison; both unique indexes confirmed. |
| `F2018_sp_ins_San_Pham` / `F2018_sp_upd_San_Pham` | Required/unique product code and category/unit reference validity. | No | Yes, read-only comparison; product-code unique index confirmed. |
| `F2019_sp_ins_NCC` / `F2019_sp_upd_NCC` | Required and unique supplier code/name. | No | Yes, read-only comparison; unique indexes confirmed. |
| `F3001_sp_ins_Nhap_Kho_Header` / `Detail`; `F3002_sp_ins_Xuat_Kho_Header` / `Detail` | Permission, references, positive quantity/price, draft/reservation rules as applicable. | No | Yes, read-only comparison. No document data was inserted. |

Runtime connection was read-only metadata inspection of `TKS_Thuc_Tap_V11_GiaiDoan2` on `DESKTOP-NHQ7QPL\\MSSQLSERVER19`. No procedure was deployed and no business row was written. The source SQL locations include Master Data definitions at `Database/WarehouseModule.Procedures.sql:3943,4003,5096,5157,5218` and transaction insert definitions at `:4211,4360,5275,5455`.

## E. Removed / retained code

| Class/path | Status | Reason | Active application callers |
|---|---|---|---|
| Legacy modal hooks in `FWarehouse_1_Warehouse_List.razor` (`Master_Import_Tool_Item`, open/close/completion callbacks, four modal tags) | Removed | Host has no active Master Data route; dedicated routes already use FBase imports. Prevents a second UI entry point and removes summary Action History with reference ID 0. | None after change. |
| `CExcel_Import_DataTable_Adapter` | Added | Preserves actual Excel row identity while using the company DataTable reader. | Five active Master List imports. |
| `CImportLegacyWorksheetAdapter`, `CProfiledWorkbook_Xlsx_Reader`, `CImportProfileProvider` | Retained | Carry formula/unsupported-column/capability diagnostics and workbook profile checks required by active import validators. Profile reader reuses `CExcel_Controller`'s package. | Five active Master List imports. |
| Five `C*ImportValidator` classes | Retained | Still perform file-shape checks, exact duplicate SKIP, field length guards, row diagnostics, and Product foreign-key resolution; business required/unique checks were removed from these pre-SQL validators. | Five active Master List imports and validation tests. |
| `CUnit/Category/Product/Supplier/Warehouse_Xlsx_Reader`, old `CWarehouse*ImportService`, old `FWarehouse_*_Import` modal components | Retained but disconnected from active routes | Existing authorization/runtime/contract tests still reference these legacy APIs. Current Master Data routes do not route through them, and `FWarehouse_1_Warehouse_List` no longer exposes their modals. Deleting them would also remove their separate legacy test contracts; this phase leaves that cleanup isolated. | No active route; test-only references remain. |
| `CTransactionImportPreviewService` and `CWarehouseTransactionImportService` | Retained | Required Preview/Confirm and Header–Detail document logic. | Inbound/outbound import UI. |
| `FBase.razor`, `FCommon_Import_Excel.razor`, `CExcel_Controller.cs` | Not changed | Original company shared orchestration and helpers are reused. | All five Master Data imports; existing document import uses its separate preview flow. |

## F. Transaction and audit ordering

- Master Data: each valid row runs Insert inside its own SQL transaction. `p_iCount_Success` increments only after `Commit`; cache insertion follows commit and cache failure is surfaced as a warning without changing the committed row to an import failure. Action History remains post-commit and isolated from row success.
- Document import: Header plus all Detail rows remain in one transaction per document. Commit returns the saved document Auto_ID; the service accumulates those IDs. If a later document fails, `CTransactionImportExecutionException.Partial_Result` preserves earlier committed IDs and counts so audit and refresh can still complete.
- The parent wraps confirmation with `Process_Queue`, logs the uploaded file using `FQ_422_IE_sp_ins_Insert`, and writes Action History once per committed document using its real Auto_ID. A post-commit history/log/rebind failure is reported as an audit/refresh warning and does not relabel a committed document as failed.
- `FBase` was not modified. Its existing import log derives status from success/error counts and has no separate skipped count/status (`FBase.razor:341,370-414`). Consequently an all-skipped Master Data file can still be logged as failure by the shared helper even though row-level UI skip handling is retained. Fixing that requires an approved change to the company shared helper/contract and remains a known limitation.

## G. Verification

| Check | Result |
|---|---|
| Baseline focused tests | 124 passed, 0 failed before source changes. |
| Final focused tests | 173 passed, 0 failed, 0 skipped. Filter included five Master validation suites, structure detection, mapping, Company Style migration, transaction import preview, transaction import UI, and warehouse UI workflow tests. |
| Data Access build | `dotnet build TKS_Thuc_Tap_V11_Data_Access/TKS_Thuc_Tap_V11_Data_Access.csproj --no-restore --verbosity quiet -clp:ErrorsOnly` — succeeded, 0 warnings, 0 errors. |
| Web host build | `dotnet build TKS_Thuc_Tap_V11_Web/TKS_Thuc_Tap_V11_Web.csproj --no-restore --verbosity quiet -clp:ErrorsOnly` — succeeded, 24 warnings, 0 errors. Earlier module build succeeded with 12 warnings, 0 errors. Telerik licensing warnings appeared during test/build; no compile errors. |
| `git diff --check` | Passed; no whitespace errors. Git emitted only existing LF-to-CRLF working-copy notices. |
| CodeGraph | Synced 31 changed files; final `codegraph status` reports the index is up to date. |
| Runtime DB | Read-only catalog/procedure/index verification only. No DML test, deploy, or SQL integration test against the business database. |
| Visual/runtime UI | Not run. Build and source tests do not establish browser UAT. |
| Commit/push | None. |

Final focused test command:

```powershell
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --verbosity quiet -clp:ErrorsOnly --filter "FullyQualifiedName~WarehouseExcelImportValidationTests|FullyQualifiedName~UnitExcelImportValidationTests|FullyQualifiedName~CategoryExcelImportValidationTests|FullyQualifiedName~ProductExcelImportValidationTests|FullyQualifiedName~SupplierExcelImportValidationTests|FullyQualifiedName~ImportStructureDetectionTests|FullyQualifiedName~TransactionImportPreviewTests|FullyQualifiedName~MasterDataCompanyStyleMigrationTests|FullyQualifiedName~MasterDataPositionalImportMappingTests|FullyQualifiedName~WarehouseTransactionImportUiTests|FullyQualifiedName~WarehouseUiWorkflowTests"
```

## H. Remaining differences from Chu_Hang

The five active Master Data imports now share the company FBase file-selection, template check, queue/log orchestration and `CExcel_Controller` positional DataTable mapping. Their domain validators and SQL Insert SP calls remain necessary extensions; Product additionally resolves category/unit references. `CProfiledWorkbook_Xlsx_Reader` remains for formula and unsupported-capability detection while reusing the open workbook.

Inbound/outbound documents intentionally remain different: they need Preview/Confirm, Header–Detail grouping, stock/permission checks, and one atomic transaction per document. The older Master import service/component files are still in the repository for test compatibility but are disconnected from active routes. Therefore this is parity for active Master Data import routes, not a claim that every historical import class has been deleted or that browser UAT is complete.

## Final module status

| Module | Reader/mapping | Validation | Transaction/audit | Verification | Status |
|---|---|---|---|---|---|
| Kho | Company DataTable + original-row adapter | SQL required/unique; formula/length/skip guards retained | Per-row commit then cache/history | Focused tests/build | Updated |
| Đơn vị tính | Company DataTable + original-row adapter | Same; duplicate rows skip | Per-row commit then cache/history | Focused tests/build | Updated |
| Loại sản phẩm | Company DataTable + original-row adapter | SQL required/unique; formula/length/skip guards retained | Per-row commit then cache/history | Focused tests/build | Updated |
| Sản phẩm | Company DataTable + original-row adapter | SQL code/FK; category/unit lookup preserved | Per-row commit then cache/history | Focused tests/build | Updated |
| Nhà cung cấp | Company DataTable + original-row adapter | SQL required/unique; formula/length/skip guards retained | Per-row commit then cache/history | Focused tests/build | Updated |
| Nhập kho / Xuất kho | Profiled two-sheet parser | Preview and confirm-time revalidation retained | Header+details atomic per document; per-document history | Focused tests/build; no runtime UAT | Updated, workflow intentionally distinct |
