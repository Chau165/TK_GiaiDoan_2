# XNK Refactor Risk Matrix

Scope: plan-only analysis of the transaction-document controller, import path, shared Page/Post Stores, and required decomposition. No production source, SQL, database object, or test was changed or run.

| Risk | Severity | Current evidence | Required control |
|---|---|---|---|
| Receipt/issue Store routing | HIGH | Six list/CRUD methods select F3001/F3002 | Separate controllers; one literal Store per method; caller selects feature |
| Insert/update routing | HIGH | Header/detail Save uses Auto_ID to select insert/update | Caller chooses Add/Edit; low-level method is Store-specific |
| Receipt/issue entity conflation | HIGH | Generic entities contain Is_Receipt and aliases | Four typed entities; adapt only at presentation boundary |
| Result-contract mismatch | HIGH | SQL aliases So_Phieu, Ngay_Chung_Tu, Document_ID, So_Luong, Don_Gia | Align result columns to typed properties under separately approved result-contract change |
| Interactive header/detail boundary | HIGH | UI performs separate header and detail Store calls | Preserve current transaction boundaries; do not wrap them together accidentally |
| Import atomicity | CRITICAL | Header and details share one connection/transaction per document | Four transaction overloads; a failing detail rolls back that document only |
| Post state transition | CRITICAL | Shared Store is serializable, rejects duplicate Post, revalidates after locks | Keep one shared Store; preserve validation order, fences, state changes, commit/rollback |
| Inventory projection | CRITICAL | Post changes InventoryBalance_Current and invalidates historical reads | Do not reimplement/cache in C#; verify current balance and report contracts |
| Issue reservations | CRITICAL | F3002 detail/header mutations create, adjust, or release reservations | Keep Store calls and transactions intact; prove quantity at each lifecycle transition |
| Permission and warehouse scope | CRITICAL | UI permission gates coexist with SQL warehouse-user checks | Preserve both layers; never use unfiltered CCache_Kho for access |
| Page support naming | MEDIUM | Shared sp_XNK_Document_Page serves both types; naming unresolved | Temporary one-Store method; do not invent F3001/F3002 Page names |
| Post support naming | HIGH | Shared sp_XNK_Document_Post changes stock state; naming unresolved | Keep temporary exact-name method; do not duplicate Post business logic |
| UI caller migration | HIGH | Shared Razor list/edit/info components bind generic models | Preserve route, permissions, paging/export, selected details, audit history, filters, refresh |
| Old controller retirement | HIGH | Fourteen test/benchmark files and two production files reference current type | Migrate all; retire only after no legitimate runtime references remain |
| DM cache stale data | HIGH | Existing caches are static per process; deployment topology unknown | Reuse only after verifying topology; keep authorization and transactional state in DB |
| Transaction cache memory growth | HIGH | Headers/details/history have no source-level cache bound | Do not load all history into List<T>; keep paged/on-demand SQL |
| Import duplicate-read volume | MEDIUM | Full header list plus one detail query per matching document | Keep fresh validation; optimize only in a separately approved bounded query change |
| Concurrent edit during Post | CRITICAL | Post re-reads identity/state under Inventory fences and locks | Preserve isolation, lock order, identity verification, and status checks |
| Concurrent DM cache access | HIGH / UNVERIFIED | Static mutable collections show no explicit lock around load/update/list paths | Prove concurrent read/write safety before XNK consumes them; do not change DM cache implementation in this plan |
| Multi-instance cache consistency | HIGH / UNVERIFIED | Static caches have no cross-process invalidation evidence | If multi-instance, keep DB reads or define approved shared invalidation first |
| Existing dirty DM work | HIGH | Working tree already contains DM SQL/controller/cache/test changes | Preserve those changes; scope review to six new plan artifacts |

## Future implementation release gates

- All 18 XNK Stores retain names, parameters, transaction behavior, result meaning, and call order.
- F3001 controller methods call only F3001 Stores; F3002 methods call only F3002 Stores.
- The four transaction overloads use the exact caller-supplied connection and transaction.
- Import remains atomic per document, with no cache mutation before Commit.
- Page and Post remain shared one-Store methods while company naming is unresolved.
- Reservation, InventoryBalance_Current, report scope, movement invalidation, authorization, and audit outcomes match the current database behavior.
- All production, test, and benchmark references migrate before CWarehouseDocument_Controller is removed.
- No transaction cache is added under the current evidence.


