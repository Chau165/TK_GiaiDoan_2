# Company-Style XNK Refactor Plan

Status: PLAN ONLY. Evidence reflects the current working tree inspected on 2026-10-12.

## Scope and evidence boundary

No production C#, SQL, database object, live database, Stored Procedure, cache class, class name, or file location was changed. Pre-existing dirty DM/cache/test changes remain preserved.

Source count: **33 files** (31 C#/Razor files: 6 Data Access controllers/helpers; 5 entity models; 1 import utility; 2 Web services; 4 Razor components; 2 route pages; 1 Web startup file; 5 DM controllers; 5 DM caches; plus 2 SQL source files). Fourteen test/benchmark files (10 test-project files and 4 standalone benchmark files) were separately searched for CWarehouseDocument_Controller references and are counted for migration impact, not in the 31 source-file count. CONTEXT.md and three inventory/reservation ADRs were consulted but are not counted.

CodeGraph was synchronized before this plan and used for navigation/call tracing. Important findings were checked against source. The SQL file already contains pre-existing working-tree changes; this task did not modify or attribute those changes.

## Required counts

Counting rules refer to CWarehouseDocument_Controller and direct XNK document Stores unless stated otherwise.

| Metric | Count | Rule |
|---|---:|---|
| XNK-related source files analyzed | 33 | 31 C#/Razor plus 2 SQL files; 14 test/benchmark files separately scanned |
| Current controller/service methods analyzed | 27 | Public operational methods in seven related classes; constructors/UI handlers excluded |
| Unique direct XNK Stores | 18 | 8 F3001 + 8 F3002 + shared Page/Post; excludes DM, permission, report, technical Stores |
| Multi-Store methods | 7 | Six list/CRUD routers plus one import transaction orchestrator |
| Receipt/issue routing methods | 7 | Same seven, with import routing retained at orchestration layer |
| Insert/update routing methods | 2 | Header save and detail save; import only inserts |
| F3001 Store methods | 8 | Receipt list/detail list and header/detail CRUD |
| F3002 Store methods | 8 | Issue list/detail list and header/detail CRUD |
| Required transaction overloads | 4 | F3001/F3002 insert Header and insert Detail |
| Shared Page/Post methods | 2 | sp_XNK_Document_Page and sp_XNK_Document_Post |
| Production caller methods to migrate | 9 | 7 methods in shared list UI + 2 methods in transaction import store |
| Current transaction entity classes | 2 | CWarehouseDocument and CWarehouseDocumentDetail |
| Current C# manual DataRow mappings | 0 | Current generic projections map through the base helper |
| Repeated/redundant read patterns | 5 | See section O; not all are avoidable |
| Cache candidate categories analyzed | 11 | See cache artifact |
| Cache candidate categories recommended | 1 | DM reference data reuse only, conditional |
| Cache candidate categories rejected | 10 | All transaction, inventory, and reservation categories |
| Overall cache recommendation | XNK_CACHE_PARTIALLY_RECOMMENDED | DM_CACHE_REUSE_ONLY |

## A. Current Nhap Kho flow

1. /Kho/Nhap_Kho renders FWarehouse_1_Warehouse_List with m_strSection="Receipt". Load_Data_Async loads reference data and binds the document grid.
2. The shared form creates/edits CWarehouseDocument and sets Is_Receipt=true. UI function permissions gate Add/Edit. Warehouse lists are permission-filtered.
3. Save_Document_Async sets Created/Last_Updated audit actor/function values, then routes by Auto_ID to F3001_sp_ins_Nhap_Kho_Header or F3001_sp_upd_Nhap_Kho_Header. The Store validates data and warehouse access and returns Auto_ID.
4. UI records action history best-effort, rebinds the grid, then loads details for the selected document. Interactive header and detail saves are separate Store operations.
5. Detail CRUD calls F3001_sp_ins_Nhap_Kho_Detail, F3001_sp_upd_Nhap_Kho_Detail, or F3001_sp_del_Nhap_Kho_Detail. SQL validates access, document state, product, positive quantity/price, and audit inputs. Draft receipt details do not change current on-hand inventory.
6. Header deletion calls F3001_sp_del_Nhap_Kho_Header. SQL rejects posted documents; schema cascades allowed draft deletion to raw details. UI clears selection and refreshes.
7. Interactive grid reads call shared sp_XNK_Document_Page with the receipt flag. Full-source/export reads call F3001_sp_sel_List_Nhap_Kho. Selecting a document calls F3001_sp_sel_List_Nhap_Kho_Detail.
8. Post calls only sp_XNK_Document_Post with receipt flag, document ID, login, and audit actor/function. SQL checks existence, non-empty details, access, duplicate Post, and unchanged document state after locks. It uses a serializable transaction and Inventory fences.
9. Receipt Post marks the header posted, adds quantities to InventoryBalance_Current, updates report scope, validates historical running inventory, queues movement invalidation, and commits atomically. There is no receipt reservation mutation. UI updates posted state only after success, then rebinds and reloads details.
10. Inbound import Preview validates workbook and references. Confirm rebuilds validation against fresh references, skips duplicates, and saves each accepted document in its own transaction: Header, sequential Details, Commit. A failure rolls back that document; earlier documents stay committed.

## B. Current Xuat Kho flow

1. /Kho/Xuat_Kho renders the same component with m_strSection="Issue". It shares lookup, grid, export, selection, and permission UI, but Is_Receipt=false and supplier is not an issue field.
2. Header insert/update call F3002_sp_ins_Xuat_Kho_Header or F3002_sp_upd_Xuat_Kho_Header. Header delete calls F3002_sp_del_Xuat_Kho_Header and is rejected after Post.
3. Details use F3002_sp_sel_List_Xuat_Kho_Detail, F3002_sp_ins_Xuat_Kho_Detail, F3002_sp_upd_Xuat_Kho_Detail, and F3002_sp_del_Xuat_Kho_Detail.
4. Issue detail insert/update adjusts InventoryReservation_Current in the same SQL transaction. Detail delete releases its reservation before deletion. Header delete releases reservations for all details before deleting the unposted header.
5. Post calls the shared sp_XNK_Document_Post with Is_Receipt=false. SQL rechecks authorization, non-empty details, duplicate Post, and document identity/state after Inventory fences are acquired.
6. Issue Post validates Available = CurrentQuantity - ReservedQuantity against the issue quantity, releases the document reservation, marks posted, deducts InventoryBalance_Current, validates non-negative current and historical balances, updates report scope and queues movement invalidation. Any error rolls back the state transition.
7. Outbound import reads current inventory in pages of 500 and validates against current on-hand and reserved quantities. Confirm revalidates and stores each issue document in a separate transaction; F3002 detail inserts create reservations inside it.
8. UI hides edit/delete/Post on posted rows and SQL rejects post-state mutations. No unpost/reversal Store or UI flow was found.

## C. Current architecture problems

- CWarehouseDocument_Controller mixes receipt, issue, Page/Post support, and import transaction orchestration.
- Six normal list/CRUD methods choose F3001 versus F3002 using p_bIs_Receipt or document state. Header/detail Save also choose insert/update from Auto_ID.
- Save_Imported_Document_Atomically_Async is a multi-Store coordinator inside low-level Data Access.
- Two generic entities hide distinct receipt/issue fields behind So_Phieu, Ngay_Chung_Tu, Document_ID, So_Luong, Don_Gia, and Is_Receipt.
- Current methods return Task.FromResult/Task.CompletedTask around synchronous CSqlHelper work. Future Store methods should follow CDM_Chu_Hang_Controller: explicit types; p_ parameters; v_ locals; obj/arr prefixes; CSqlHelper; direct DataRow-to-entity mapping; and disposed DataTables. Avoid var, fake async wrappers, dynamic Store strings, ternary/switch Store selection, and generic Save methods.
- Keep CWarehouse_Controller_Base generic list/page/scalar/execute helpers; do not broadly refactor infrastructure.
- Shared Page/Post naming is unresolved. Keep these support operations separate and do not let them block the F3001/F3002 split.
- Shared Razor UI consumes generic models, so typed-entity migration needs an explicit presentation adapter or later UI split.
- Inventory and reservation rules already live in SQL transactions and technical Stores. Preserve them; do not port them into C#.

## D. Multi-Store routing findings

Seven methods can reach more than one Store:
- List_Documents_Async: F3001 versus F3002 list.
- Save_Document_Async: receipt/issue and Auto_ID insert/update; four Stores.
- Delete_Document_Async: receipt/issue header delete.
- List_Document_Details_Async: receipt/issue detail list.
- Save_Document_Detail_Async: receipt/issue and Auto_ID insert/update; four Stores.
- Delete_Document_Detail_Async: receipt/issue detail delete.
- Save_Imported_Document_Atomically_Async: receipt/issue header and detail inserts under one transaction.

The six list/CRUD methods are low-level routers to split. Import remains orchestration and calls entity-specific methods. Page and Post each call one shared Store and are temporary-support operations.

## E. Target Nhap Kho controller

The user-proposed name CXNK_Nhap_Kho_Controller fits the observed CDM_ naming pattern. No existing CXNK controller or Controller/XNK folder conflicts.

Target folder/namespace:
- TKS_Thuc_Tap_V11_Data_Access/Controller/XNK
- TKS_Thuc_Tap_V11_Data_Access.Controller.XNK

Target synchronous methods, each with one literal Store:
- F3001_sp_sel_List_Nhap_Kho
- F3001_sp_sel_List_Nhap_Kho_Detail
- F3001_sp_ins_Nhap_Kho_Header
- F3001_sp_upd_Nhap_Kho_Header
- F3001_sp_del_Nhap_Kho_Header
- F3001_sp_ins_Nhap_Kho_Detail
- F3001_sp_upd_Nhap_Kho_Detail
- F3001_sp_del_Nhap_Kho_Detail

Use CXNK_Nhap_Kho and CXNK_Nhap_Kho_Detail; preserve current Auto_ID output, parameters, data types, access login, audit values, and delete audit behavior. No receipt flag, dynamic Store name, or Auto_ID Store choice belongs in this controller.

## F. Target Xuat Kho controller

The user-proposed name CXNK_Xuat_Kho_Controller fits the observed naming pattern and has no existing class/folder conflict.

Target folder/namespace:
- TKS_Thuc_Tap_V11_Data_Access/Controller/XNK
- TKS_Thuc_Tap_V11_Data_Access.Controller.XNK

Target methods:
- F3002_sp_sel_List_Xuat_Kho
- F3002_sp_sel_List_Xuat_Kho_Detail
- F3002_sp_ins_Xuat_Kho_Header
- F3002_sp_upd_Xuat_Kho_Header
- F3002_sp_del_Xuat_Kho_Header
- F3002_sp_ins_Xuat_Kho_Detail
- F3002_sp_upd_Xuat_Kho_Detail
- F3002_sp_del_Xuat_Kho_Detail

Use CXNK_Xuat_Kho and CXNK_Xuat_Kho_Detail. Each method executes only its named F3002 Store. There is no receipt flag, supplier field, F3001 call, or insert/update choice in the controller. Preserve SQL reservation effects.

## G. Header/Detail split

Split the current generic persistence models into four typed entities under Entity/XNK:
- CXNK_Nhap_Kho: receipt number/date, warehouse, supplier, note, posted state, audit fields.
- CXNK_Nhap_Kho_Detail: Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap, and display fields.
- CXNK_Xuat_Kho: issue number/date, warehouse, note, posted state, audit fields; no supplier.
- CXNK_Xuat_Kho_Detail: Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat, and display fields.

The shared grid/form currently binds generic models. Keep typed persistence entities separate; map them explicitly to a shared presentation model or split the UI components later.

## H. Insert/Update split

The caller already decides Add/Edit from Auto_ID and checks Add/Edit permissions. Preserve that decision at the caller:
- Receipt Add uses F3001 insert; receipt Edit uses F3001 update.
- Issue Add uses F3002 insert; issue Edit uses F3002 update.

Low-level methods never branch on Auto_ID. Preserve output IDs, login scope, Created/Last_Updated actors and function codes, SQL parameter precision/scale, and current interactive save boundaries.

## I. Transaction/import plan

Keep CWarehouseTransactionImportService as async Preview/Confirm orchestration and CWarehouseTransactionImportStore as adapter/reference loader. Do not move import loops into either entity controller.

Required transaction overloads, with the same Store method names:
1. F3001_sp_ins_Nhap_Kho_Header(SqlConnection, SqlTransaction, CXNK_Nhap_Kho).
2. F3001_sp_ins_Nhap_Kho_Detail(SqlConnection, SqlTransaction, CXNK_Nhap_Kho_Detail).
3. F3002_sp_ins_Xuat_Kho_Header(SqlConnection, SqlTransaction, CXNK_Xuat_Kho).
4. F3002_sp_ins_Xuat_Kho_Detail(SqlConnection, SqlTransaction, CXNK_Xuat_Kho_Detail).

Each overload calls only its Store with the supplied connection and transaction. No transaction overload is needed for reads, updates, or deletes because current import does not call them within its document transaction.

Preserve the actual boundary: Confirm revalidates against fresh references; one new document gets one connection/transaction; header insert precedes sequential detail inserts; commit occurs only after all details succeed; failure rolls back that document. Previously committed documents in the workbook remain committed. Do not change cache state before Commit.

## J. Post plan

Post is a business state transition. Keep SQL shared and unchanged. The UI calls a temporary one-Store method named exactly sp_XNK_Document_Post; that method executes only that Store and retains Is_Receipt at this shared support boundary.

Preserve UI permission/already-posted guards and SQL authorization, detail-presence, duplicate Post, concurrency, and balance validation. Preserve serializable isolation, Inventory fence ordering, issue reservation release, status/audit changes, current balance update, historical non-negative validation, report-scope update, movement invalidation, and rollback behavior. No inverse Post operation was found; posted documents are not editable/deletable.

## K. Page/shared-support plan

Keep the smallest temporary owner for unresolved shared operations, conceptually CWarehouseDocumentSupport_Controller in Controller/Warehouse:
- sp_XNK_Document_Page: one method/one Store, shared receipt/issue input, temporary UI page projection.
- sp_XNK_Document_Post: one method/one Store, shared receipt/issue input.

CWarehouseMasterSupport_Controller is precedent for temporary Page Store ownership. The proposed document support class is a project-level temporary name, not a claimed company convention. Do not invent F3001/F3002 Page/Post names or split SQL. The support owner must not absorb CRUD, import loops, or inventory helpers.

## L. Entity decomposition recommendation

Recommendation: SPLIT_ENTITY for both current transaction models. SQL schema has distinct receipt/issue headers and raw-data tables; generic C# models hide different FK, quantity, price, date, number, and supplier semantics. Migration affects the grid/form, detail view, import path, and tests. Keep any shared model at the UI projection seam only. CWarehouseMaster is a separate temporary DM/import-reference transport model used by CTransactionImportReferences and the generic DM Page path; it is not a transaction entity. Do not redesign it in this XNK phase.

## M. Direct DataRow to Entity mapping recommendation

Current C# manual row-mapping count is zero; the base helper maps generic projections using CUtility.Map_Row_To_Entity<T>. Classification: GENERIC_ALIAS_MAPPING for all four F3001/F3002 list/detail Stores and both shared Page branches. They are not DIRECT_MAPPING_READY for the proposed typed entities; no current Store result is manually mapped in C#.

Current SQL aliases:
- Receipt/issue header list uses So_Phieu and Ngay_Chung_Tu; issue also synthesizes Is_Receipt=false and empty/zero supplier fields.
- Receipt/issue detail lists use Document_ID, So_Luong, and Don_Gia aliases.
- Shared Page repeats those generic header projections in two branches.

That is six branch-specific generic projections across four Store procedures. Typed properties will not match all current aliases. Future XNK-8 should align result aliases to typed properties, then retain direct DataRow-to-entity mapping. That requires a separately reviewed SQL/result-contract change in that future phase; none is authorized or done now.

## N. Existing DM cache reuse opportunities

Web/Program.cs loads CCache_Kho, CCache_Don_Vi_Tinh, CCache_Loai_San_Pham, CCache_San_Pham, and CCache_NCC at startup. Each loads its dedicated *_For_Cache Store. DM UI CRUD synchronizes entries; import paths reload the corresponding cache after transaction commit.

Current XNK component reloads DM lookups on data load. Import reads supplier/unit lists and pages product master data through temporary sp_DM_Master_Page. These read-only reference paths can reuse their matching existing DM caches after typed integration, but concurrent read/write safety must be verified because the inspected collections are mutable and have no explicit lock.

Do not replace List_Authorized_Warehouses_Async or import's allowed-warehouse list with unfiltered CCache_Kho.List_Data(). A global warehouse cache may provide display names only after permission-filtered IDs are applied. Keep user/warehouse assignments, document state, duplicate checks, inventory, reservations, and Post validation database-backed.

Caches are static in-process. Startup loading is confirmed; whether deployment runs one or multiple Web instances is NOT VERIFIED. The cache classes expose mutable lists/dictionaries without an explicit lock in the inspected paths. Multi-instance use needs cross-instance invalidation, and concurrent mutation safety must be verified before reuse; otherwise retain current database reads. Do not redesign completed DM work here.

## O. Current XNK read-pattern analysis

| Read path | Behavior and scope |
|---|---|
| Interactive document grid | Shared Page Store returns one page; login and optional warehouse scope are passed |
| Export/full grid source | Full receipt or issue list then local shaping; full scoped header set can grow |
| Selected document details | One feature-specific detail query per selection; SQL checks warehouse access |
| Save/Delete/Post refresh | Grid rebind and selected-detail reload; intentional freshness after mutation |
| UI reference load | DM lookup plus authorized warehouse/user lookups on component data load |
| Import duplicate check | Full header list, then one details read per matching document; N+1 detail pattern |
| Outbound import balance check | Current inventory in 500-row pages until Total_Count |

Five repeated-read patterns are counted: grid Page/full-source dual paths; repeated DM lookup load; post-mutation refresh; import full-list plus per-match detail reads; and Preview/Confirm rebuilding fresh reference/duplicate validation. Some repeats are correctness requirements. Runtime frequency and production data volume cannot be established from static source. The accessible local DB snapshot had no relevant XNK/projection rows at analysis time and is not production-sizing evidence.

## P. XNK cache candidate matrix

The complete 11-category matrix and volume scenarios are in TK_COMPANY_STYLE_XNK_CACHE_ANALYSIS.md.

## Q. XNK cache feasibility conclusion

Outcome: XNK_CACHE_PARTIALLY_RECOMMENDED, scope DM_CACHE_REUSE_ONLY, conditional on cache readiness and deployment topology. Do not create CCache_Nhap_Kho, CCache_Xuat_Kho, transaction *_For_Cache Stores, or page-result caches.

Transaction headers/details are mutable or unbounded; issue details affect reservations; inventory and reservations are authoritative transactional state with critical stale-data risk. Current UI already pages list reads and there is no measured high-frequency transaction read justifying new cache state.

## R. Proposed cache architecture

Only existing DM caches are candidates. Use them for read-only lookup/reference creation after load readiness is verified. Keep documents, details, posted state, duplicate checks, inventory, reservations, and Post validation in SQL. No new XNK cache class or Store is recommended. Future cache tests, if approved, are specified in the cache artifact.

## S. Caller migration map

Nine distinct production methods call the old document controller: seven in FWarehouse_1_Warehouse_List.razor and two in CWarehouseTransactionImportStore. Per-Store callsite rows are in TK_COMPANY_STYLE_XNK_CALLER_MAP.csv. The map also records four benchmark methods in four files; they are not production callers. Fourteen test/benchmark files reference the old class or its path (10 test-project files plus 4 benchmark files); update them before retirement.

Preserve UI permissions, route type, paging/export, selection, details refresh, audit history, and warehouse filtering while adapting generic UI models to typed persistence models.

## T. Risk matrix

See TK_COMPANY_STYLE_XNK_RISK_MATRIX.md. Highest risks: issue reservation lifecycle, Post atomicity/fences, per-document import transactions, typed result projections, and warehouse authorization. Cache stale-data and memory-growth risks rule out transaction caches.

## U. Implementation checkpoint sequence

Each future checkpoint must build and contract-test independently.

1. XNK-1: finalize entities, folders/namespaces, UI projection seam, result-column contracts, and invariants.
2. XNK-2: split Nhap list/detail-list reads and migrate receipt reads.
3. XNK-3: split Nhap Header/Detail CRUD and insert transaction overloads.
4. XNK-4: split Xuat list/detail-list reads and migrate issue reads.
5. XNK-5: split Xuat Header/Detail CRUD; prove reservation lifecycle unchanged.
6. XNK-6: migrate import orchestration to typed controllers and four transaction overloads.
7. XNK-7: isolate shared Page/Post support in the temporary owner.
8. XNK-8: align SQL result aliases to typed entity properties only under separately approved contract change.
9. XNK-9: migrate UI, import, integration/contract tests and benchmarks; retire old controller only after zero legitimate references.
10. XNK-CACHE: separate later phase only if cache readiness and deployment topology are verified; no transaction cache is recommended now.

Future TDD starts RED, implements one small checkpoint, then proves GREEN. Contracts: receipt controller calls only F3001; issue controller only F3002; method name equals Store; one Store per method; no low-level receipt flag or Auto_ID routing; overload preserves supplied connection/transaction; import atomic per document; Post/Page/auth/audit/result/Inventory/Reservation behavior unchanged. Integration tests use an isolated test DB. Target at least 80% coverage of changed behavior. No tests are run in this plan-only phase.

## V. Artifact paths

- TK_COMPANY_STYLE_XNK_REFACTOR_PLAN.md
- TK_COMPANY_STYLE_XNK_CLASS_MAP.csv
- TK_COMPANY_STYLE_XNK_METHOD_MAP.csv
- TK_COMPANY_STYLE_XNK_CALLER_MAP.csv
- TK_COMPANY_STYLE_XNK_RISK_MATRIX.md
- TK_COMPANY_STYLE_XNK_CACHE_ANALYSIS.md

## Confirmations

PLAN_ONLY_NO_SOURCE_CHANGE
PLAN_ONLY_NO_SQL_CHANGE
PLAN_ONLY_NO_DATABASE_CHANGE
CURRENT_NHAP_FLOW_ANALYZED
CURRENT_XUAT_FLOW_ANALYZED
NHAP_KHO_CLASS_SPLIT_PLANNED
XUAT_KHO_CLASS_SPLIT_PLANNED
ONE_METHOD_ONE_STORE_TARGET
METHOD_NAME_EQUALS_STORE_TARGET
EXPLICIT_COMPANY_STYLE_TARGET
NO_RECEIPT_ISSUE_ROUTING_IN_LOW_LEVEL_TARGET
NO_INSERT_UPDATE_ROUTING_IN_LOW_LEVEL_TARGET
TRANSACTION_BOUNDARY_PRESERVATION_REQUIRED
POST_FLOW_PRESERVATION_REQUIRED
INVENTORY_BEHAVIOR_PRESERVATION_REQUIRED
RESERVATION_BEHAVIOR_PRESERVATION_REQUIRED
PAGE_POST_SHARED_SUPPORT_ANALYZED
CURRENT_XNK_CACHE_FEASIBILITY_ANALYZED
XNK_CACHE_DECISION_EVIDENCE_BASED
DM_PREVIOUS_WORK_PRESERVED

Final status: COMPANY_STYLE_XNK_REFACTOR_PLAN_COMPLETE








