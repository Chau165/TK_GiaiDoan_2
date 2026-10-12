# XNK Cache Feasibility and Design Analysis

Status: PLAN ONLY. No cache class, Store, source, SQL, or database change was made.

## Decision

**XNK_CACHE_PARTIALLY_RECOMMENDED — DM_CACHE_REUSE_ONLY.**

The only recommended category is read-only DM reference data through existing CCache_Kho, CCache_San_Pham, CCache_Don_Vi_Tinh, CCache_Loai_San_Pham, and CCache_NCC. Do not create transaction-document caches, transaction *_For_Cache Stores, or cached Page results.

This recommendation is conditional on deployment scope. The caches are static in-process and initialized in Web/Program.cs. The repository does not establish whether deployment runs one Web process or multiple instances. If there are multiple instances or concurrent cache mutation is not proven safe, keep existing database lookups until synchronization/read safety is approved.

## Current evidence and read patterns

- FWarehouse_1_Warehouse_List.Load_Lookups_Async reads DM Lookup Stores on component data load and reads authorized warehouses separately.
- Transaction import reads supplier/unit lists and pages products through temporary sp_DM_Master_Page support.
- Program.cs loads the five DM caches at startup. DM CRUD synchronizes cache entries; DM import reloads the corresponding cache after transaction commit. The inspected cache implementations use mutable static collections and show no explicit lock around load/update/list operations.
- Interactive XNK grid uses the shared Page Store. Export/full-source reads use the full receipt or issue list Store.
- Selecting a document reads details. Save/Delete/Post refresh the grid and selected details.
- Import duplicate detection reads the full feature header list and then each matching detail list. Preview and Confirm rebuild validation against fresh references.
- Outbound import reads current inventory in 500-row pages.
- Post and F3002 detail operations mutate inventory/reservations transactionally in SQL.
- The accessible local DB snapshot had no XNK/projection rows at analysis time. This is not production-volume evidence; sizing below is a growth classification, not measured bytes.

## Cache candidate matrix

| Data_Set | Current_Read_Path | Read_Frequency | Mutation_Frequency | Growth | Consistency_Risk | Cache_Benefit | Recommended | Recommended_Strategy | Reason |
|---|---|---|---|---|---|---|---|---|---|
| DM reference data: Kho, San_Pham, Don_Vi_Tinh, Loai_San_Pham, NCC | UI Lookup Stores; import lists/pages; existing DM caches | High on list load and Preview/Confirm | Low to moderate; CRUD/import | Small to moderate; bounded | High; process-local; mutable collections; warehouse-scope sensitive | High | Yes, conditional | DM_CACHE_REUSE_ONLY | Existing caches load/sync; no explicit lock observed; warehouse authorization must remain permission-filtered |
| Nhap Header | F3001 list; shared Page; import duplicate check | Paged grid; full list for export/import | Draft CRUD; one-way Post | Potentially unbounded | High | Medium | No | CACHE_NONE | Page query already bounds UI; full-history cache grows and status changes |
| Nhap Detail | F3001 detail list; duplicate check | On selection and per matching import document | Draft CRUD; no reservation | Potentially unbounded with line multiplication | High | Low to medium | No | CACHE_NONE | On-demand read is scoped; no measured high-frequency benefit |
| Xuat Header | F3002 list; shared Page; duplicate check | Paged grid; full list for export/import | Draft CRUD; one-way Post | Potentially unbounded | High | Medium | No | CACHE_NONE | Mutable status, user/warehouse filter, and history growth |
| Xuat Detail | F3002 detail list; duplicate check | On selection and per matching import document | Draft CRUD; each line affects reservation | Potentially unbounded | Critical | Low to medium | No | CACHE_NONE | Reservation-bearing detail must reflect authoritative SQL |
| Unposted Nhap | Generic header Page/list includes Is_Posted; no dedicated unposted Store | Grid and selection | CRUD and Post transition | Not bounded by source contract | High | Medium | No | CACHE_NONE | No dedicated bounded Store; status changes on Post |
| Unposted Xuat | Generic header Page/list includes Is_Posted plus detail reads; no dedicated unposted Store | Grid and selection | CRUD and Post; reservation changes | Not bounded by source contract | Critical | Medium | No | CACHE_NONE | Stale draft details can overstate Available |
| Posted Nhap history | F3001 list/Page and detail list | History page; export may request all | Normally immutable after Post; history grows | Potentially unbounded | High | Low to medium | No | CACHE_NONE | Paged DB query exists; history remains unbounded |
| Posted Xuat history | F3002 list/Page and detail list | History page; export may request all | Normally immutable after Post; history grows | Potentially unbounded | High | Low to medium | No | CACHE_NONE | Same growth concern plus reservation release/status contract |
| Inventory | Current inventory Page/report and Post | Import validation/reporting | Every successful Post | Grows by warehouse/product scope | Critical | Medium to high | No | CACHE_NONE | Current balance is a transactional projection and stays authoritative in SQL |
| Reservation | SQL issue-detail/reservation paths | Detail changes, Post, validation | Each qualifying draft edit/delete/Post | Grows with active issue details | Critical | Low | No | CACHE_NONE | Stale reservation can cause overselling or reject valid issues |

Counts: 11 categories analyzed; 1 recommended category; 10 rejected categories. No new XNK cache is recommended.

## Full-history memory scenarios

| History size | Header-only cache | Header plus detail cache | Assessment |
|---:|---|---|---|
| 100 documents | Small | Likely small, depends on average lines | No evidence it improves current paged UI |
| 10,000 documents | Moderate | Moderate to large as details multiply | Needs an explicit bound and invalidation |
| 100,000 documents | Large | Potentially very large | Do not load complete history into process memory |
| 1,000,000 documents | Unacceptable unbounded List<T> | Millions of detail objects and indexes | Not acceptable; retain paging/on-demand SQL |

A Page-result cache was considered and rejected because entries vary by user scope, warehouse, filter, and page and would need invalidation after every relevant mutation. Production row width and average detail count are unknown. A bounded recent/active cache needs workload evidence and a dedicated bounded contract; current source does not justify it.

## Existing DM cache reuse rules

- CCache_San_Pham, CCache_Don_Vi_Tinh, CCache_Loai_San_Pham, and CCache_NCC can serve read-only lookup/reference values after startup and after-commit DM synchronization.
- CCache_Kho may provide display data only after permitted warehouse IDs come from List_Authorized_Warehouses_Async. Never use its full list to grant access.
- Keep user assignments, warehouse authorization, document status, duplicate checks, current inventory, reservations, and Post validation database-backed.
- Static caches do not provide cross-process invalidation. The inspected collection operations also show no explicit synchronization; prove concurrent read/write safety before XNK adopts them.

## Conceptual design

No new XNK cache class or *_For_Cache Store is recommended. If a later approved phase consumes DM caches, use the matching existing cache after its load contract is confirmed. Existing reference entities/indexes are:
- Kho: Auto_ID and Ten_Kho.
- San Pham: Auto_ID and Ma_San_Pham.
- Don Vi Tinh: Auto_ID and Ten_Don_Vi_Tinh.
- Loai San Pham: Auto_ID, Ma_LSP, Ten_LSP.
- NCC: Auto_ID, Ma_NCC, Ten_NCC.

DM caches load from their dedicated *_For_Cache Stores at Web startup. CRUD synchronization follows Store completion; import reload follows Commit. No Post/transaction/import synchronization is needed because transaction caches are not recommended.

## Future cache TDD gates

If DM cache reuse is later approved, test startup/load readiness, parity with DM result contracts, duplicate/index consistency, CRUD synchronization, import refresh only after Commit, permission-filtered warehouse visibility, concurrent read/write behavior, and reload-failure behavior. If deployment is multi-instance, prove cross-instance invalidation or retain database reads. A transaction cache would additionally require bounded-scope, duplicate, CRUD, Post, rollback, commit-order, and memory-bound tests; current recommendation is not to build one.


