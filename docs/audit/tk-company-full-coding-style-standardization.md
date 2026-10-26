# TK company coding style standardization — P0–P4

Date: 23/10/2026. Executor: Codex. Git identity observed: Nguyễn Võ Minh Châu (recorded as repository context, not used to claim human authorship).

**Overall: PARTIAL — source refactor implemented and built; full company parity is NOT achieved.** Eight protected core files match Source A byte for byte. FBase and FCommon retain specifically identified dependency blockers. Final regression: 252 passed, 1 failed, 0 skipped. The failed frozen Inventory hash test already disagrees with the task-entry commit. Browser UAT and live SQL behavior are NOT VERIFIED.

## 0. Baseline, authority and preservation

- Source A (read only): `P:\Giao đoạn 2 TK_root\Giao đoạn 2 TK\TKS_Thuc_Tap_11`. Source B (edited): `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`.
- Branch `main`; entry and final HEAD `e65b6221bf21f3a7ed5358b72b48aa04ce4d70da` (`Modifile/ excel`). Entry `git status --short` and `git diff --stat` were empty. No inherited uncommitted changes and no benchmark log deletions existed at entry.
- Read all 748 paragraphs of `C:/Users/Surface/Downloads/Cam Nang Code TK.docx` (19,691 text characters), including 2.1–2.3, 3.1–3.5 and 5–6. Compared local Source A, not GitHub.
- Protected/core comparison includes SHA256, UTF-8 BOM, CRLF counts, signatures and actual caller searches. Final Source A manifest: 1,135 source/config/document files checked, 0 changed (excludes build/cache/git output).
- Source A reference: Chu Hang List/Info/Edit, entity/controller/cache, the ten core files; additional Source A `CSys_Chuc_Nang_Controller` confirms DataTable/CSqlHelper/try–catch–finally/foreach convention.
- No reset, clean, stash, stage, commit, push, SQL deployment, schema change, database write, benchmark or load run. Database scripts and Chu Hang have no diff.
- Evidence/scripts/logs/TRX: `C:\Users\Surface\AppData\Local\Temp\tk-style-p0-p4`. No connection secrets stored in this report. RTK unavailable; bounded git/rg used. CodeGraph used before navigation and synced after changes.

## 1. Core File Parity

| File | Source A SHA256 | B before SHA256 | B after SHA256 | Restored | Dependencies migrated |
|---|---|---|---|---|---|
| `TKS_Thuc_Tap_V11_Data_Access\DataLayer\CSqlHelper.cs` | `af10feefcea4984a550b8fbf1453265b0b7daef678e59995ed53f7b44c9512f1` | `32f873a7b139e7dac851fefd19bd6edcb8137d15b32b45b7eb92bf87fa51561c` | `af10feefcea4984a550b8fbf1453265b0b7daef678e59995ed53f7b44c9512f1` | RESTORED | XNK writes -> original metadata overloads; multi-result paging -> existing Warehouse controller |
| `TKS_Thuc_Tap_V11_Data_Access\Utility\CConfig.cs` | `1fdb148dbecb51f26f5ad15e44767d444a3c0b5158dca04f92c66cdba8d7de2d` | `1fdb148dbecb51f26f5ad15e44767d444a3c0b5158dca04f92c66cdba8d7de2d` | `1fdb148dbecb51f26f5ad15e44767d444a3c0b5158dca04f92c66cdba8d7de2d` | ORIGINAL_UNCHANGED | Already identical |
| `TKS_Thuc_Tap_V11_Data_Access\Utility\CConst.cs` | `8f5d9d932e82a52fd57541174e7292d3701c9bdaf6d08c0007e715190b9c17f6` | `8f5d9d932e82a52fd57541174e7292d3701c9bdaf6d08c0007e715190b9c17f6` | `8f5d9d932e82a52fd57541174e7292d3701c9bdaf6d08c0007e715190b9c17f6` | ORIGINAL_UNCHANGED | Already identical |
| `TKS_Thuc_Tap_V11_Data_Access\Utility\CExcel_Controller.cs` | `8836234abd2e9b8074f8d5345ba8494a01e476ff726514fb30448b88c52b5bbe` | `b9bf74fb198dcdd4565224dd1b515e8779b1f2924d9511909c854df5df0da170` | `8836234abd2e9b8074f8d5345ba8494a01e476ff726514fb30448b88c52b5bbe` | RESTORED | Template guard -> CWMS_Excel_Utility, six Entry owners / seven modules |
| `TKS_Thuc_Tap_V11_Data_Access\Utility\CNumber.cs` | `d71255592256687e44c84ddf480b28cd1c0dc5fa5e94245050145f0c231ad16f` | `d71255592256687e44c84ddf480b28cd1c0dc5fa5e94245050145f0c231ad16f` | `d71255592256687e44c84ddf480b28cd1c0dc5fa5e94245050145f0c231ad16f` | ORIGINAL_UNCHANGED | Already identical |
| `TKS_Thuc_Tap_V11_Data_Access\Utility\CNumberToEnglish.cs` | `f915f07026a8658ab88a3e78bfb7807c10d0a1f8d389403933427548aa0d29c9` | `f915f07026a8658ab88a3e78bfb7807c10d0a1f8d389403933427548aa0d29c9` | `f915f07026a8658ab88a3e78bfb7807c10d0a1f8d389403933427548aa0d29c9` | ORIGINAL_UNCHANGED | Already identical |
| `TKS_Thuc_Tap_V11_Data_Access\Utility\CUtility.cs` | `847c53abe7dfc6a0bf3e2f259fdbabcbded376be51fd0a5d8d866f33a9b1a457` | `ad067f980a407a902765550440500563a282347e13b80fc7aa85dd8adcef338a` | `847c53abe7dfc6a0bf3e2f259fdbabcbded376be51fd0a5d8d866f33a9b1a457` | RESTORED | Decimal Warehouse mapping and display -> CWMS_Utility; master snapshots retain original Clone_Entity |
| `TKS_Thuc_Tap_V11_Data_Access\Utility\CUtility_Date.cs` | `1c80bf1a81f279bfc0a78237247310e3f6977751a98680b4063a63133b3f65e0` | `0fbe47a522de76e0ec97af3a52772d56618b0eade3a8044c6f8ab428a1e580cc` | `1c80bf1a81f279bfc0a78237247310e3f6977751a98680b4063a63133b3f65e0` | RESTORED | UTC half-open bounds -> CWMS_Utility, five By_Created controllers |
| `TKS_Thuc_Tap_V11_Web_Common\Common\FBase.razor` | `dc72d5788f08c57ef9cf6661ac0822081313b80ee74d6e9e1e7945f36367088d` | `083062b0e26a4fb0a7270b9b11d23c6cae42d69a601f519124aa7302ace6f361` | `39b7d5799a93bc68bbe41bd014724f83b172559de7c515722c9cd05c16083b6e` | DEPENDENCY_BLOCKER | Removed unused file reader/hook; restored company Import_Excel; export/history blockers retained |
| `TKS_Thuc_Tap_V11_Web_Common\Common\FCommon_Import_Excel.razor` | `28a772303df52ef7c457264bd6542498a0e242b6596c3454feef6d876783eba5` | `a24613dc0848acfff208a1b010b7be3eab207ffe09e441a5c5c8779957996bac` | `a24613dc0848acfff208a1b010b7be3eab207ffe09e441a5c5c8779957996bac` | DEPENDENCY_BLOCKER | Unmodified this task; localization and short-read upload blockers retained |

All eight matching files retain the original BOM and exact CRLF/bytes. Do not append change comments to restored protected files, because byte parity is an acceptance condition. FCommon is byte-identical to its task-entry B version.

### 1.1 Member classification and migration

| Modified core | Added/modified member | Classification before | Active callers / replacement | Resolution |
|---|---|---|---|---|
| CUtility | Convert_To_Decimal, Format_So_Tien, Format_So_Luong | CUSTOM_ADDED_METHOD | Warehouse display, benchmarks/tests -> CWMS_Utility | Original file restored |
| CUtility | Map_Row_To_Entity, Clone_Entity decimal conversion | ORIGINAL_METHOD_MODIFIED | XNK/report/page decimal mapping -> Map_Warehouse_Row; five DM snapshots -> original Clone_Entity | Original file restored |
| CUtility_Date | two Convert_Local_*_To_Utc methods | CUSTOM_ADDED_METHOD | Five DM Created filters and tests -> CWMS_Utility | Original file restored; UTC-exclusive end preserved |
| CExcel_Controller | Validate_Header_Matches + Read_Header_Row | CUSTOM_ADDED_METHOD | Five master Entry owners + shared receipt/issue Entry -> CWMS_Excel_Utility | Original restored |
| CExcel_Controller | Read_Profiled_Import_Sheet | NOT_PRESENT_AT_ENTRY | No active callers | No speculative removal |
| CSqlHelper | FillDataSet, typed SqlParameter overloads / public exposure of private ExecuteScalar overloads | CUSTOM_ADDED_METHOD / ORIGINAL_METHOD_MODIFIED | Original object[] + metadata API; unique DataSet paging capability stays in existing Warehouse controller | Original restored; original transaction overloads retained |
| FBase | Read_Import_File_Async | CUSTOM_ADDED_METHOD | No production callers | Removed |
| FBase | After_Render_Async | CUSTOM_ADDED_METHOD | Transaction, permission and report components -> normal OnAfterRenderAsync, await company base first | Removed |
| FBase | Import_Excel header guard | ORIGINAL_METHOD_MODIFIED | Warehouse Entry callers validate before their row/document try block | Company method body restored |
| FBase | Save/Delete, export dispatcher, Tool_Button, Close_Edit | DEPENDENCY_BLOCKER | See section 6 | Kept; no new methods added |
| FCommon | Localize and stream copy | DEPENDENCY_BLOCKER | Six render sites / seven modules; stream copy regression | Kept unchanged |

No removed original company method was found. The original `_Cmd` APIs still exist in restored CSqlHelper; no Warehouse business caller was introduced for them.

### 1.2 Migration details / behavior boundaries

- Decimal mapping has **no safe equivalent** in original CUtility for these Warehouse entities: original Decimal branch converts to `double` before assigning a `decimal` property. `CWMS_Utility.Map_Warehouse_Row` retains required destination-type/nullable handling and decimal precision; it delegates ordinary primitive conversions to original CUtility. It is scoped to Warehouse, not a replacement global mapper. Master Data continues to use company mapper/Clone_Entity.
- New CWMS utilities contain only the capabilities removed from protected utilities: decimal/UTC/display and template guard. No new generic base framework or replacement FBase was created.
- XNK INSERT/UPDATE/DELETE now call original CSqlHelper directly. Text-empty -> DBNull, Date.Date, SQL parameter order, return Auto_ID and same connection/transaction remain. Metadata derives type/size/precision/scale from the existing SP contract. A pure test confirms original parameter assignment preserves decimal and InputOutput metadata.
- Two-result-set paging cannot use Source A FillDataTable without losing count/page data. `Fill_Page_Data` stays in the existing CWarehouse_Controller_Base, using original connection factory and parameter cache, `CommandType.StoredProcedure`, disposal and the original effective **30 second timeout**. No SQL text / contract change.
- Template validation is now in Warehouse Entry before its catch/counting boundary, so an invalid template still aborts before inserts rather than being counted as a failed business row. Original FBase Import queue begins earlier, as in Source A; this queue-order restoration is explicitly acknowledged. FBase log ownership remains unchanged.

## 2. Explicit C# Refactor

Static audit covers 85 scoped files, including 73 C# files / Razor code blocks. Counts below exclude Razor markup event binding and existing company source. They include removed inactive generic branches as well as explicit rewrites.

| Metric | Before | After |
|---|---:|---:|
| Arrow expression clauses (methods/properties/local functions) | 45 | 0 |
| Conditional expressions in C# | 63 | 0 |
| Switch expressions | 2 | 0 |
| LINQ operations in audited set | 76 | 21 |
| C# parse errors | 0 | 0 |

Private Grid callback parameters and catch variables were renamed with syntax tokens (`args` -> `p_objArgs`, `ex` -> `v_objException`); original company/public override parameter names were retained.

The remaining 21 operations are simple Any/FirstOrDefault existence and label lookups in master validation/Info/Delete or the product-unit guard. Complex Select/Where/ToDictionary business mapping and Header–Detail import pipelines were expanded. Delegate/lambda callbacks and Razor presentation expressions remain where appropriate; public API names and named-argument compatibility were preserved.

| File / method | Old syntax | New syntax | Behavior changed |
|---|---|---|---|
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2018_1_San_Pham_List.razor:80` Load_Data | ToDictionary + Select + nested conditional labels | Explicit dictionaries, lookup checks and grid projection | No: order, duplicate-key failure and missing-label empty strings retained |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Transaction_List.razor:185` Load_Transaction_Lookups | Select and projected dictionary | foreach + Dictionary.Add + explicit display name | No |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Transaction_List.razor:562` Load_Document_Source / Load_Document_Details | Repeated controller queries + Select + Where | One mapping owner, explicit loops; reused by UI and export | No: date bounds, permissions and details retained |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Transaction_List.razor.cs:17` Import_Excel_Entry | Where(...).ToArray, All, nested choices | Explicit matches, blank-row loops and branches | No: unique lookup required, no default IDs, transaction unchanged |
| Five master Lists / Import_Excel_Entry | All blank-row shortcut; product Where matches | Explicit loops | No: only all-empty business rows ignored |
| Five master Edit / Can_Save, save entry | Expression getter / conditional | get + if/else + blocks | No: permissions and snapshot lifecycle retained |
| CWarehousePermissionFilter.List_Available_Warehouses | Where/Select/Where pipeline | HashSet + two foreach loops | No: case-insensitive login, current permission exclusion, order retained |
| CWarehouseDocumentExcelExporter / CWarehouseMasterExcelExporter | Select projection and inline choices | Explicit rows, columns, header and worksheet branches | No: ClosedXML regression covers values/formats |
| CGrid_DataSource_Utility.Apply | Three inline page choices | Explicit page/skip/page-size variables | No: zero-page/all-pages contract retained |
| Cache classes, XNK detail totals, comparer, readiness | Unbraced control flow / expression bodies | Blocks and returns | No |

## 3. UI Structure Refactor

| Function | Route | Old active component | New owner | List / Info / Edit | Remaining host responsibility |
|---|---|---|---|---|---|
| Kho 2009 | /Kho/Quan_Ly | F2009_1_Kho_List; inactive legacy host branch | Existing F2009_1/2/3 | Existing three components retained | Routing compatibility only |
| Unit 2016 | /Kho/Don_Vi_Tinh | FWarehouse_Unit_List; inactive host branch | Existing Unit List/Info/Edit | Existing three retained | Routing compatibility only |
| Category 2017 | /Kho/Loai_San_Pham | F2017_1/2/3; inactive host branch | Existing category components | Existing three retained | Routing compatibility only |
| Product 2018 | /Kho/San_Pham | F2018_1/2/3; inactive host branch | Existing product components | Existing three retained | Routing compatibility only |
| Supplier 2019 | /Kho/Nha_Cung_Cap | F2019_1/2/3; inactive host branch | Existing supplier components | Existing three retained | Routing compatibility only |
| Warehouse/User permission | /Kho/Phan_Quyen | FWarehouse_1_Warehouse_List | FWarehouse_Permission_List | Existing Master_Info; Master_Edit now owns only permission form | Route to child |
| Read-only reports | /Kho/Bao_Cao | FWarehouse_1_Warehouse_List | FWarehouse_Report_List | List only; no artificial Edit | Route to child |
| Current inventory | /Kho/Ton_Kho | Same combined host | FWarehouse_Report_List (InventoryCurrent) | List only; original conditional grids retained | Route to child |
| Receipt / Issue | /Kho/Nhap_Kho, /Kho/Xuat_Kho | Dedicated F3001/F3002 wrappers + shared transaction list/edit | Same dedicated wrappers; shared edit now Document/Detail only | Existing Info/Edit wrappers retained | No legacy master branch |

- `FWarehouse_1_Warehouse_List`: **1,307 -> 27 lines**. No Controller, Grid or CRUD handler remains. Public routing parameters remain for existing callers; no new routes/function codes.
- `FWarehouse_3_Warehouse_Edit`: **267 -> 133 lines**. Removed master entity fields/forms/Add/Update dispatch. It shares only the genuine receipt/issue Document/Detail UI and callbacks.
- Permission list: 371 lines; report list: 387. Moved from old host, with no report Insert/Update and no generic master-save path.
- Permission edit moved into existing `FWarehouse_3_Warehouse_Master_Edit`. Login comparison remains case-insensitive: only a changed login resets the warehouse. Available warehouses still exclude assignments for the same user while allowing the current edited permission.
- Reuse: FBase lifecycle/loading/localization/permissions, Format_Grid, Layout_Tool_Button, shared Import, Info/Edit callbacks, existing help/export/history. No new toolbar, duplicate modal framework or replacement FBase helper.
- Existing `FWarehouse_Filter_Toolbar` remains module-local: document/report date semantics and callbacks differ from company Created filter. No new copy/overload was added; changing the visible label/range to Created would change meaning. Five masters continue original Layout_Default_Filter.
- Company OnAfterRenderAsync completes before formatting newly rendered Warehouse Grid refs. Report switching, @key, current/period inventory columns, filter/sort/all-page export, loading/error branches remain and have source regression coverage. Visual/runtime equivalence is NOT VERIFIED.

## 4. Controller / SP Compliance

| Controller | SP contract | Company pattern | Deviation | Resolution |
|---|---|---|---|---|
| CDM_Kho / Don_Vi_Tinh / Loai_San_Pham / San_Pham / NCC | Existing F2009/F2016/F2017/F2018/F2019 CRUD, Get_By_ID, By_Created, Lookup, For_Cache | DataTable + CSqlHelper + foreach + company mapper + dispose + propagate | Created UTC bounds differ from company local-date reference | Moved UTC capability to CWMS; business date contract retained |
| CXNK_Nhap_Kho / CXNK_Xuat_Kho | Existing F3001/F3002 Header/Detail read and 12 write SPs | Direct CSqlHelper calls, original transaction overload | Decimal entity mapping needs Warehouse adapter | CWMS mapper; typed SQL builder functions removed; no SP change |
| CDM_Kho_User | Existing F2015 list/insert/update/delete and allowed/page stores | Explicit entities and CSqlHelper | Page has two result sets | Existing Warehouse controller owns DataSet reader |
| CWarehouseReport_Controller | Existing F9001/F9002 detail, period and current paging | Explicit SP, disposal and mapping | Decimal values + multi-result page | CWMS mapper / Fill_Page_Data |
| CWarehouseDocumentSupport_Controller | sp_XNK_Document_Page / Post | SP only | Paging shared with existing support controller | Post now directly uses original ExecuteNonquery |
| CWarehouseMasterSupport_Controller | sp_DM_Master_Page / sp_DM_Lookup_Page | Existing unresolved support methods | No approved new company code | Names/contracts retained; no generated function code |

Static checks: no CommandType.Text, business CommandText assignment, FillDataTable_Cmd, ExecuteNonquery_Cmd or EF usage introduced in scoped Warehouse controllers. The sole explicit SqlCommand in new paging logic is StoredProcedure and reuses company connection/parameter cache. SQL signature order tests cover 12 write SPs, including Insert transaction overloads; this is source-contract verification, NOT live SQL execution.

## 5. Custom Helper Cleanup

| Helper | Company equivalent | Callers | Removed / retained | Reason |
|---|---|---|---|---|
| CWarehouse_Controller_Base | CSqlHelper covers normal reads/writes | Two existing support subclasses; report/permission DataSet calls | Retained only paging responsibility; List_From_Procedure, Scalar_ID, Execute_Procedure removed | Original lacks DataSet count/page capability; avoids wrapper duplication |
| CWarehouseMasterSupport_Controller | No exact company Page/Lookup SP equivalent | Existing public support API and benchmarks | Retained | Unresolved approved-name boundary; no business change |
| CWarehouseDocumentSupport_Controller | Original CSqlHelper write API | Transaction list | Retained for distinct Page/Post responsibility | Post no longer hides CSqlHelper behind generic Execute_Procedure |
| CWarehouseProductSupport_Service | Product typed List exists; paged support contract distinct | No active production caller found; public API / contract tests retained | Retained, mapping expanded | Avoid unnecessary public API removal during style task |
| CWMS_Utility | No decimal-safe/UTC-exclusive/precision-display equivalent in Source A | Warehouse mapping/display/five date controllers | Added as permitted domain Utility | Restores protected originals without losing precision/date semantics |
| CWMS_Excel_Utility | No original header guard | Six Entry owners | Added as Warehouse-only capability | Original CExcel untouched |
| CWarehousePermissionFilter | No exact current-edit-aware assignment helper | Permission form | Retained; loops replace pipeline | Business-specific responsibility |
| CGrid_DataSource_Utility | Telerik ToDataSourceResult | OnRead lists | Retained | Keeps actual Telerik filter/sort/all-page semantics |
| CStream_Copy_Utility | Original upload loop is unsafe for partial ReadAsync results | FCommon upload | Retained, untouched | Stream byte-integrity dependency blocker |
| Existing document/master Excel exporters | Generic company export lacks document Header–Detail shape | Transaction export; existing public tests/contracts | Retained | No export content/format loss |

## 6. Protected Core Modifications Still Present — explicit blockers

**These are retained task-entry changes, not new extensions. No claim of full company parity.**

### 6.1 FBase — DEPENDENCY_BLOCKER

| Member / source | Necessary active behavior | Why not restored blindly |
|---|---|---|
| `TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor:499` Save_Data | Separate successful SQL save from failed History write, required source/destination snapshots, warning callback | Original reports error after a committed save and can leave Edit open; replacing it loses the existing post-commit warning contract |
| `TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor:586` Delete_Data | Post-delete history warning and reload | Blind restore can present a saved deletion as a failure and lose warning propagation |
| `TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor:1666` Close_Edit | Display Message_Description after reload | Depends on committed-write warning contract |
| `TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor:1012` Export_Excel + `TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor:951` export state | OnRead AllPages dispatcher, custom Header–Detail workbook and full-source grid request | Original dispatcher always calls custom reflection export; does not support existing OnRead/custom callback semantics |
| `TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor:1838` Layout_Tool_Button / `TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor:42` flag | Existing custom export callback and OnRead request mode | Removing active API breaks receipt/issue and report/permission export; copying the company tool menu or adding a new framework is prohibited by this task |

Completed: removed unused Read_Import_File_Async and After_Render_Async; migrated render callers; restored original Import_Excel body; removed Warehouse header guard from FBase. The remaining blockers require a separate approved design for write-result warnings and OnRead/document export without duplicating/replacing company helpers. No silent fallback added.

### 6.2 FCommon_Import_Excel — DEPENDENCY_BLOCKER

- `TKS_Thuc_Tap_V11_Web_Common/Common/FCommon_Import_Excel.razor:45`: removing Localize breaks six Warehouse render sites or removes existing localization.
- `TKS_Thuc_Tap_V11_Web_Common/Common/FCommon_Import_Excel.razor:98`: Source A upload loop writes requested byte count instead of actual ReadAsync count, and relies on stream Length/Position. With partial reads, restoring it risks corrupted uploads. Existing `StreamCopyBehaviorTests.Partial_reads_copy_exact_bytes` uses 10,123 bytes and a 4,096-byte partial reader and verifies the retained copy via SHA256. This does not claim original Telerik runtime was executed.
- FCommon remains unchanged from task entry. Copying the entire modal into a new Warehouse variant just to make this file hash match is deliberately avoided. Full restoration needs a separately approved shared upload/localization decision.

## 7. Tests / Verification

| Phase / check | Result | Evidence / boundary |
|---|---|---|
| P0 Data Access | PASS: 0 errors, 0 warnings | p0-da.log |
| P0 Web isolated | PASS: 0 errors, 32 warnings | p0-web-isolated.log |
| P0 focused | 43 pass / 0 fail | p0.trx |
| P1 compile / focused | PASS; 54 pass / 0 fail | p1-web.log, p1.trx |
| P2 compile / focused | PASS; 47 pass / 0 fail | p2-build.log, p2.trx |
| P3 expanded regression | Initially exposed obsolete ownership assertions; updated to new owners without removing tests | Old generic master callback assertions now inspect dedicated forms/cache ordering; actual decimal tests invoke the new Warehouse adapter |
| Final Data Access | PASS: 0 errors / 0 warnings | final-da.log |
| Final Web isolated | PASS: 0 errors / 30 warnings | final-web.log; output `C:\Users\Surface\AppData\Local\Temp\tk-style-p0-p4\web-final` |
| Final regression | 252 pass / 1 fail / 0 skipped / 253 total | final-regression.trx; known baseline hash failure below |
| Static core parity | PASS: 8 exact files | TKCompanyStyleParityTests |
| Static syntax | 0 arrow / conditional / switch expressions, 0 parse errors in scoped C# | syntax-before.json / syntax-after.json |
| Source A preservation | PASS: 1,135 checked, 0 changed | baseline.json / final-verification.json |
| git diff --check | PASS | No stage/commit/push |
| CodeGraph | Synced; status up to date | Final impact query: two Page-support subclasses; six Fill_Page_Data callers in report/permission/base; report/permission Lists each have one routing host |
| Live business DB / SQL write integration | NOT RUN | No DB connection/test writes performed this task |
| Browser smoke / UAT | NOT RUN | Existing Web process PID 17748 locked normal build output; build uses isolated output. Process was not stopped/restarted and old runtime is not evidence of the new code |

Warning categories: Telerik licensing TKL002/TKL004/TKL101, existing duplicate using / async-without-await / test analyzer warnings. No package/dependency changes. Initial ordinary Web build failed with MSB3021/MSB3027 due to the running host lock; isolated build passed.

### 7.1 Known pre-existing failure — NOT suppressed

`WarehouseXnkSplitContractTests.XNK_page_post_write_and_technical_inventory_sql_match_the_frozen_baseline`:

- Expected aggregate Inventory hash in existing test: `211244BD7F1F50BB899B1FB38C716E577202121B86A94600DEBAEEDCF5271F27`.
- Actual aggregate of 35 Inventory procedures: `461008FA0DAAB28F248C1A34AC1F5334CE1E0BC651EAA96341F4DC5797EC0606`.
- Independently computed from task-entry `git show HEAD:Database/WarehouseModule.Procedures.sql` and current checkout: same count/hash. `git diff -- Database` empty. Git HEAD has LF normalization; raw blob/checkout byte equality is not claimed.
- The test remains enabled and failing in the final run. Neither expected hash nor SQL was changed to make it green. Requires a separate review of the older Inventory baseline before changing its frozen expectation.

The pre-existing test `Master_list_maps_unit_names_to_the_entity_property` remains outside this source/fixture filter: it requires a configured live SQL sample row “Cái”. No business DB was used to satisfy it. No broad/full suite or SQL integration was run. Phase counts overlap; do not sum them as distinct tests.

### 7.2 Coverage by workflow (source / unit boundary)

| Workflow | Verified | Not verified |
|---|---|---|
| Five master CRUD/filter/cache/history | Company-style component/route contracts; audit/date helpers; Clone metadata; store-before-cache order; import fixture cases | Live SQL CRUD, cache coherence across sessions, actual History persistence |
| Receipt/Issue Header–Detail | CExcel mapping, physical row numbers/blank/error grouping; per-document transaction contract; SQL argument order, decimal metadata, permission/source guards, no automatic Post | Real SQL rollback/reservation/Post and browser end-to-end import |
| Export | ClosedXML workbook values and types; Header–Detail rows; OnRead all-page/filter/sort source contracts | Actual browser download and license-dependent runtime |
| Report/inventory | Route, tab and paging/columns/readiness/source contracts; no write calls added | Live projection data, queue/movement values and runtime snapshot correctness |
| Upload/core | Exact core hashes, file stream partial-read integrity, original SQL parameter assignment; company Import body retained | Full Chu Hang browser import, localization rendering and visual UAT |

### 7.3 Commands

```powershell
dotnet build TKS_Thuc_Tap_V11_Data_Access/TKS_Thuc_Tap_V11_Data_Access.csproj --no-restore --verbosity quiet
dotnet build TKS_Thuc_Tap_V11_Web/TKS_Thuc_Tap_V11_Web.csproj --no-restore --output "$env:TEMP/tk-style-p0-p4/web-final" --verbosity quiet
$v_strFilter = @'
(FullyQualifiedName~TKCompanyStyleParityTests|FullyQualifiedName~CompanyOriginalExcelImportTests|FullyQualifiedName~LegacyExcelHeaderValidationTests|FullyQualifiedName~MasterDataCompanyStyleMigrationTests|FullyQualifiedName~UnitExcelImportUiTests|FullyQualifiedName~CategoryExcelImportUiTests|FullyQualifiedName~ProductExcelImportUiTests|FullyQualifiedName~SupplierExcelImportUiTests|FullyQualifiedName~WarehouseExcelImportUiTests|FullyQualifiedName~WarehouseDmCacheSyncTests|FullyQualifiedName~WarehouseMasterFinalizationContractTests|FullyQualifiedName~WarehouseMasterEntityDecompositionContractTests|FullyQualifiedName~WarehousePermissionRefactorTests|FullyQualifiedName~WarehouseReportRefactorTests|FullyQualifiedName~WarehouseUiWorkflowTests|FullyQualifiedName~WarehouseDocumentExcelExportTests|FullyQualifiedName~WarehouseMasterExcelExportTests|FullyQualifiedName~XlsxParserCompatibilityTests|FullyQualifiedName~WarehouseNavigationTests|FullyQualifiedName~WarehouseMasterGetByIdContractTests|FullyQualifiedName~WarehouseMasterCreatedFilterContractTests|FullyQualifiedName~WarehouseModuleStructureTests|FullyQualifiedName~WarehouseControllerRetirementTests|FullyQualifiedName~WarehouseXnkSplitContractTests|FullyQualifiedName~WarehouseGroupBSplitContractTests|FullyQualifiedName~WarehouseDetailDisplayFormatTests|FullyQualifiedName~StreamCopyBehaviorTests|FullyQualifiedName~WarehousePermissionFilterTests)&FullyQualifiedName!~Master_list_maps_unit_names_to_the_entity_property
'@
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter $v_strFilter --logger "trx;LogFileName=final-regression.trx" --results-directory "$env:TEMP/tk-style-p0-p4/results" --verbosity quiet
git diff --check
codegraph sync
codegraph status
```

## 8. Remaining Deviations / Final Status

| Item | Category | Status / decision |
|---|---|---|
| FBase / FCommon not exact Source A | Protected-core dependency blocker | PARTIAL. Exact remaining members and their callers above; no 100% parity claim |
| 21 simple LINQ existence / label checks and Razor callbacks | Style choice, not complex mapping | Retained; no expression-bodied C# methods/properties or conditional expressions remain in audited owned code |
| Existing public names / component names / support SPs lacking approved code | Naming exception | Unchanged; no invented function code or route rename |
| UTC half-open Created filters, decimal Header–Detail/report data, reservations and historical/current inventory semantics | Required business deviation | Preserved in Warehouse-owned code / original SQL |
| Frozen Inventory test expectation | Pre-existing verification blocker | 1 failing test retained; review older baseline separately |
| Visual and live DB behavior | Verification limit | NOT VERIFIED; build/unit/source checks are not UAT or runtime SQL PASS |

## 9. Changed-file ledger

File/line evidence below identifies the touched owners; line 1 means the whole-file restoration/test contract rather than a single changed statement. Core hashes and method-specific rows above provide the detailed classification.

| File | Method / role | Old -> new | Behavior changed |
|---|---|---|---|
| `TKS_Thuc_Tap_V11_Data_Access.Tests/LegacyExcelHeaderValidationTests.cs:1` | Regression contract | Same behavior assertions, new owner/helper; frozen SQL hash retained | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/Performance/PerformanceBenchmark.cs:1` | Regression contract | Same behavior assertions, new owner/helper; frozen SQL hash retained | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/UnitExcelImportUiTests.cs:1` | Regression contract | Same behavior assertions, new owner/helper; frozen SQL hash retained | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseControllerRetirementTests.cs:1` | Regression contract | Same behavior assertions, new owner/helper; frozen SQL hash retained | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseDetailDisplayFormatTests.cs:1` | Regression contract | Same behavior assertions, new owner/helper; frozen SQL hash retained | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseDmCacheSyncTests.cs:1` | Regression contract | Same behavior assertions, new owner/helper; frozen SQL hash retained | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseGroupBSplitContractTests.cs:1` | Regression contract | Same behavior assertions, new owner/helper; frozen SQL hash retained | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseMasterCreatedFilterContractTests.cs:1` | Regression contract | Same behavior assertions, new owner/helper; frozen SQL hash retained | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseMasterEntityDecompositionContractTests.cs:1` | Regression contract | Same behavior assertions, new owner/helper; frozen SQL hash retained | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseNavigationTests.cs:1` | Regression contract | Same behavior assertions, new owner/helper; frozen SQL hash retained | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehousePermissionRefactorTests.cs:1` | Regression contract | Same behavior assertions, new owner/helper; frozen SQL hash retained | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseReportRefactorTests.cs:1` | Regression contract | Same behavior assertions, new owner/helper; frozen SQL hash retained | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseUiWorkflowTests.cs:1` | Regression contract | Same behavior assertions, new owner/helper; frozen SQL hash retained | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseXnkSplitContractTests.cs:1` | Regression contract | Same behavior assertions, new owner/helper; frozen SQL hash retained | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Don_Vi_Tinh.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Kho.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Loai_San_Pham.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_NCC.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_San_Pham.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/Common/CWarehouse_Controller_Base.cs:14` | Fill_Page_Data / Page_From_Procedure | Keep unique DataSet reader, remove redundant scalar/list/write wrappers | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Don_Vi_Tinh_Controller.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_Controller.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_User_Controller.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Loai_San_Pham_Controller.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_NCC_Controller.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_San_Pham_Controller.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CWarehouseDocumentSupport_Controller.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Nhap_Kho_Controller.cs:10` | Read/write methods | Original CSqlHelper metadata calls, explicit decimal mapping | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Xuat_Kho_Controller.cs:10` | Read/write methods | Original CSqlHelper metadata calls, explicit decimal mapping | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/DataLayer/CSqlHelper.cs:1` | Protected core restore | B -> exact Source A bytes | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Entity/XNK/CXNK_Nhap_Kho_Detail.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Entity/XNK/CXNK_Xuat_Kho_Detail.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CExcel_Controller.cs:1` | Protected core restore | B -> exact Source A bytes | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CImportBusinessValueComparer.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CUtility.cs:1` | Protected core restore | B -> exact Source A bytes | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CUtility_Date.cs:1` | Protected core restore | B -> exact Source A bytes | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CWarehouseDocumentExcelExporter.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CWarehouseMasterExcelExporter.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CWarehousePermissionFilter.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CWarehouseReportReadiness.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Common/Common/CGrid_DataSource_Utility.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor:315` | Import_Excel, render hook, unused reader | Restore Import body; remove unused hook/reader; preserve explicit blockers | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2009_1_Kho_List.razor:1` | Load_Data / Import_Excel_Entry / Grid callbacks | Explicit mapping and branches; template validation outside counting boundary | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2009_3_Kho_Edit.razor:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2017_1_Loai_San_Pham_List.razor:1` | Load_Data / Import_Excel_Entry / Grid callbacks | Explicit mapping and branches; template validation outside counting boundary | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2017_3_Loai_San_Pham_Edit.razor:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2018_1_San_Pham_List.razor:1` | Load_Data / Import_Excel_Entry / Grid callbacks | Explicit mapping and branches; template validation outside counting boundary | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2018_2_San_Pham_Info.razor:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2018_3_San_Pham_Edit.razor:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2019_1_Nha_Cung_Cap_List.razor:1` | Load_Data / Import_Excel_Entry / Grid callbacks | Explicit mapping and branches; template validation outside counting boundary | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2019_3_Nha_Cung_Cap_Edit.razor:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_1_Warehouse_List.razor:1` | UI responsibility split | Independent active owners; eliminate duplicate master CRUD | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_2_Warehouse_Info.razor:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_3_Warehouse_Edit.razor:1` | UI responsibility split | Independent active owners; eliminate duplicate master CRUD | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_3_Warehouse_Master_Edit.razor:1` | UI responsibility split | Independent active owners; eliminate duplicate master CRUD | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Transaction_List.razor:1` | Load_Data / Import_Excel_Entry / Grid callbacks | Explicit mapping and branches; template validation outside counting boundary | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Transaction_List.razor.cs:1` | Load_Data / Import_Excel_Entry / Grid callbacks | Explicit mapping and branches; template validation outside counting boundary | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Unit_Edit.razor:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Unit_List.razor:1` | Load_Data / Import_Excel_Entry / Grid callbacks | Explicit mapping and branches; template validation outside counting boundary | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseProductSupport_Service.cs:1` | Scoped explicit syntax / regression contract | Explicit blocks / updated caller ownership | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/TKCompanyStyleParityTests.cs:1` | Regression contract | Same behavior assertions, new owner/helper; frozen SQL hash retained | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CWMS_Excel_Utility.cs:1` | Warehouse-only migrated capability | Move unsupported decimal/UTC/display/template capability out of protected core | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CWMS_Utility.cs:1` | Warehouse-only migrated capability | Move unsupported decimal/UTC/display/template capability out of protected core | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Permission_List.razor:1` | UI responsibility split | Independent active owners; eliminate duplicate master CRUD | No business rule change intended; source/unit boundary above |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Report_List.razor:1` | UI responsibility split | Independent active owners; eliminate duplicate master CRUD | No business rule change intended; source/unit boundary above |

Final acceptance remains **PARTIAL**, because two protected core dependencies and one pre-existing regression failure are unresolved. The safe source changes are implemented; no production readiness or runtime PASS is asserted.
