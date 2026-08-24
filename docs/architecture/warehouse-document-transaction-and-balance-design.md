# Warehouse document transaction and InventoryBalance design audit

## Scope and evidence

This is an audit/design only. It creates no table, stored procedure, production code, or migration.

The Word exercise is business-requirement evidence, not a technical authority. It requires receipt/issue headers and details, header/detail editing, and historical XNT fields (opening, in, out, closing). Its note saying that the database has no foreign keys conflicts with the current schema: the deployed database has header/detail FKs with `ON DELETE CASCADE`. The source/deployed schema and procedures are therefore the technical source of truth.

Evidence inspected:

- `Bai Tap Thuc Tap.docx`: exercises 7-17.
- `CWarehouseDocument_Controller`, `CWarehouse_Controller_Base`, `CSqlHelper`, `FWarehouse_1_Warehouse_List`.
- `WarehouseModule.Schema.sql`, `WarehouseModule.Procedures.sql`, current local database `TKS_Thuc_Tap_V11_GiaiDoan2`.
- No adjustment, transfer, lot, serial, bin/location, reservation, or balance table exists in source or deployed database.

## A. Current transaction architecture

```text
Blazor edit form
  -> Save_Document_Async (one click/request)
  -> CWarehouseDocument_Controller
  -> one header SP
  -> header table

Later, separate detail edit click/request
  -> Save_Document_Detail_Async
  -> one detail SP
  -> detail table
```

`CSqlHelper` opens a new connection for the normal Warehouse calls. Each current save SP starts and commits its own transaction only when `@@TRANCOUNT = 0`; the detail SPs also use `SERIALIZABLE`. Delete SPs own a transaction or savepoint. Although the helper and base controller already expose `SqlConnection`/`SqlTransaction` overloads, the Warehouse public path does not use them.

| Nghiệp vụ | Header save | Detail save | Transaction hiện tại | Rủi ro |
|---|---|---|---|---|
| Nhập | `sp_XNK_Nhap_Kho_Save_Header` | `sp_XNK_Nhap_Kho_Save_Detail` | từng request/SP | header tồn tại không có detail; các detail đã commit không rollback khi detail sau lỗi |
| Xuất | `sp_XNK_Xuat_Kho_Save_Header` | `sp_XNK_Xuat_Kho_Save_Detail` | từng request/SP | tương tự; validation tồn chạy nhiều lần |
| Điều chỉnh | Không có | Không có | N/A | không được suy diễn delta điều chỉnh |
| Điều chuyển | Không có | Không có | N/A | không được suy diễn hai chiều kho |

The existing ledger is the stock source of truth: `sp_XNK_Validate_All_Balances` unions receipt and issue movements and checks their date-ordered running balance. Header warehouse/date updates are permitted; detail update forbids changing document or product. Header deletion cascades its details.

## B. Fit with the current architecture

The requested goal is correct, but the sample flow cannot be added literally to the current UI. A SQL transaction cannot safely remain open while a user creates a header, then adds details through later browser requests. Holding it would create long-lived locks and fail on disconnect.

The smallest architecture-aligned change is **Option A, an application transaction coordinator**, not a new general Service layer:

```text
Blazor: one explicit “Lưu toàn bộ phiếu” command (header + detail set)
  -> CWarehouseDocumentTransaction_Controller
  -> one SqlConnection + one SqlTransaction
  -> existing named/typed Warehouse SPs participating in that transaction
  -> future delta/balance SP + affected-bucket validation
  -> Commit; otherwise Rollback
```

This matches the codebase's existing controller-to-SP design and existing transaction overloads in `CSqlHelper`/`CWarehouse_Controller_Base`; it keeps business SQL inside SPs. Microsoft.Data.SqlClient requires commands to be associated with the same connection and transaction, which the existing helper already supports. [SqlConnection.BeginTransaction](https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlconnection.begintransaction?view=sqlclient-dotnet-standard-5.2)

Do not start a second independent transaction inside a participant SP. Participant SPs must detect caller ownership, never commit/rollback a caller transaction, and use `TRY/CATCH` plus `XACT_STATE()`/savepoint only where partial recovery is intended. [TRY...CATCH](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/try-catch-transact-sql?view=sql-server-ver17), [XACT_STATE](https://learn.microsoft.com/en-us/sql/t-sql/functions/xact-state-transact-sql?view=sql-server-ver17), [SAVE TRANSACTION](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/save-transaction-transact-sql?view=sql-server-ver17)

### Alternatives rejected for the first implementation

| Option | Assessment |
|---|---|
| B. One orchestration SP | Valid later if a table-valued document command is desired, but is a larger new SQL contract and duplicates orchestration facilities already in DAL. |
| C. Keep current click-by-click UI and open one transaction | Invalid: browser requests cannot share a safe SQL transaction. |
| Draft/Post state | Needed only if the business must retain the current incremental editing UX. It requires a real `Draft/Posted/Cancelled` state and a post command; do not add it without an approved behavior change. |

## C. Target transaction boundary

For the approved atomic-save UX, transaction owner is `CWarehouseDocumentTransaction_Controller`. It receives an immutable document command: receipt/issue discriminator, header, complete desired detail collection, user/function audit context, and optional optimistic-concurrency token. It opens one connection, begins one `READ COMMITTED` transaction, executes all commands using that exact transaction, and publishes returned IDs only after commit.

```text
BEGIN
  lock/read old effective document state
  save header
  replace/apply detail changes
  derive old-versus-new effective movements
  aggregate deltas by (Kho_ID, San_Pham_ID)
  apply balance deltas
  validate affected historical balances
COMMIT
on any error: ROLLBACK; do not return newly allocated IDs
```

`SERIALIZABLE` should not be applied to the whole document transaction by default. Reserve it (or targeted update/key locks) for balance rows and conflict-sensitive validation; keep the transaction short.

## D. Domain model and delta design

Effective movement is a derived tuple `(Kho_ID, San_Pham_ID, MovementDate, SignedQuantity)`: receipt is `+SL_Nhap`; issue is `-SL_Xuat`. Current schema proves the current-stock key is exactly `(Kho_ID, San_Pham_ID)`; quantity is `DECIMAL(18,3)`. Supplier, price, document number, and date do not affect **current** quantity. Price has no balance delta.

For every write, first read/lock the old effective state; delta is `new signed movement - old signed movement`, grouped by balance key. Never update by the new quantity alone.

| Operation | Old state | New state | Delta |
|---|---|---|---|
| Insert receipt detail | none | A/X/+10 | A/X `+10` |
| Insert issue detail | none | A/X/-10 | A/X `-10` |
| Update quantity | A/X/+10 | A/X/+15 | A/X `+5` |
| Delete receipt detail | A/X/+10 | none | A/X `-10` |
| Delete issue/detail or issue header | A/X/-10 | none | A/X `+10` |
| Change warehouse | A/X/+10 | B/X/+10 | A/X `-10`; B/X `+10` |
| Change date only | A/X/+10/date1 | A/X/+10/date2 | current balance `0`; historical validation/report must be recomputed from `min(date1,date2)` |

The same reverse-then-apply rule applies to header deletion because cascade deletes all effective details. Product change is currently forbidden; if approved later, it is equivalent to changing both product keys.

### Backdate and historical report

Changing only date does not change `InventoryBalance_Current`; it changes period reporting and may make a previously valid issue negative at an earlier point in history. Historical `sp_BC_Xuat_Nhap_Ton` must remain ledger-based. Validate the affected `(Kho_ID, San_Pham_ID)` from the earliest old/new date through current history inside the command transaction. No snapshot is necessary for the current requirements; add daily snapshots only after a measured historical-report need and explicit retention semantics.

## E. Proposed `InventoryBalance_Current`

```sql
InventoryBalance_Current
  Kho_ID             BIGINT NOT NULL
  San_Pham_ID        BIGINT NOT NULL
  CurrentQuantity    DECIMAL(18,3) NOT NULL
  UpdatedAt          DATETIME2 NOT NULL
  RowVersion         ROWVERSION NOT NULL
  PRIMARY KEY (Kho_ID, San_Pham_ID)
  CHECK (CurrentQuantity >= 0)
```

The composite key is also the required concurrency key. Apply a grouped delta set in deterministic `(Kho_ID, San_Pham_ID)` order; take update/key locks on existing/missing keys and preserve the unique PK as the final race guard. Do not use a background worker or eventual consistency. Reconcile by recomputing the same signed ledger aggregate and comparing every key, including missing-on-one-side keys.

## F. Migration and rollback plan

1. **Contract/design approval:** approve atomic-save UX versus Draft/Post; define cancellation semantics. No DB change.
2. **Schema only:** create balance table and a ledger-to-balance backfill/reconcile SP. Read path remains ledger. Rollback: drop the unused table/migration only before dual write is enabled.
3. **Prove equivalence:** backfill in a maintenance window; reconciliation must report zero differences; run negative-stock and backdate cases. Rollback: truncate/rebuild projection only.
4. **Dual write:** deploy atomic document command, participant SP changes, delta apply, affected-bucket validation, and observability. Keep reads on ledger. Rollback: disable the new command/read feature; ledger remains authoritative; rebuild projection later.
5. **Current-stock read cutover:** only a new explicit current-stock screen/query may read balance. Do not change historical XNT. Keep reconciliation and a feature flag.

## G. TDD implementation contract (not yet executed)

No RED/GREEN test is claimed because this audit makes no production change. The next phase must add these tests **before** implementation:

| Guarantee | Test type |
|---|---|
| Header plus multiple details commits all rows and exact balance deltas | SQL/C# integration |
| Any failing detail leaves no header, detail, or balance row from the command | SQL/C# integration |
| Qty increase/decrease, delete, warehouse change, and header cascade apply reverse-then-apply deltas | SQL/C# integration |
| Date-only change preserves current balance but changes historical result and detects a historical negative position | SQL integration |
| Concurrent writes to the same warehouse/product never lose a delta or create duplicate balance keys | concurrent integration |
| Reconcile ledger versus balance is zero after every valid scenario | SQL integration |
| Existing receipt/issue CRUD and report-contract tests remain green | regression |

## Risks and decisions required before implementation

1. **UX decision (blocking):** approve either a single atomic-save document command or an explicit Draft/Post lifecycle. The current click-by-click UX cannot satisfy the requested boundary by itself.
2. **Cancellation semantics:** current behavior is physical delete, not a business cancellation state. Choose whether posted documents can be deleted, must be reversed, or may be reopened.
3. **Concurrency/deadlocks:** lock affected balance keys in a stable order; do not run a global all-ledger validation under high write concurrency.
4. **Authorization:** warehouse-user mapping exists but write/report enforcement still needs confirmation in the execution boundary before exposing new command paths.
5. **Transaction duration:** do not stream/export, await user interaction, or perform full reporting work inside the write transaction.

## Recommendation

Approve the **single atomic-save document command** first if the current user experience may change. It is the least invasive technically: it reuses the existing Razor -> focused Warehouse controller -> `CSqlHelper` -> stored procedure pattern and transaction-capable helpers, preserves the ledger, and gives one clear place for delta and balance work. If incremental editing is non-negotiable, stop and approve the larger Draft/Post state model before implementing any balance table.
