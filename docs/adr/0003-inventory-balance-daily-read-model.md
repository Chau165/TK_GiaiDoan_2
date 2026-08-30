# ADR-0003: Materialize historical inventory balances for paged reports

## Status

Accepted

## Date

2026-08-29

## Context

`sp_BC_Xuat_Nhap_Ton_Page` previously built `#MovementAggregate` for every request by reading `Inventory_Movement_Daily`, then calculating opening movement, receipt and issue across the requested interval.  The actual 10M execution plan showed this statement requesting a 233,608 KB memory grant and using a hash aggregate.  Concurrent report requests therefore contended for memory grants, CPU and tempdb.

The current-inventory read model in ADR-0002 cannot replace the historical report because it does not contain opening, receipt and issue for an arbitrary date interval.

## Decision

Add a worker-owned historical read model:

- `Inventory_Balance_Daily` stores an opening, daily receipt, daily issue, closing, and cumulative receipt/issue for each materialized movement date.  A valid snapshot can be materialized as an anchor row when no earlier balance row exists.
- `Inventory_Balance_Daily_Scope` provides one precomputed row per warehouse/product scope, so the report does not need to discover keys from the daily fact at request time.
- `sp_Inventory_Movement_Rebuild` rebuilds the balance suffix from the affected date forward in the same transaction and under the existing per-scope applock.  Posting still only updates `InventoryBalance_Current` and enqueues the movement rebuild.
- `sp_BC_Xuat_Nhap_Ton_Page` reads only the balance scope plus two indexed as-of balance rows: immediately before `@Tu_Ngay`, and at or before `@Den_Ngay`.  Receipt and issue are differences of cumulative values.  It retains authorization, the two-result-set paging contract, current-balance/reservation fields for the current date, and the queue stale-data guard.
- `sp_Inventory_Balance_Daily_Bootstrap_From_Movement` is a controlled cutover operation.  It is not run automatically by deployment and has its own initialization state.

## Consequences

- The per-request hash aggregate and `#MovementAggregate` are removed from the paged historical report path.
- Back-dated documents rebuild only the affected warehouse/product suffix; unrelated scopes and dates before the affected date are not rewritten.
- A report is intentionally unavailable with error 51230 until the controlled balance bootstrap completes.  This avoids serving an empty or partial read model after deployment.
- A valid snapshot remains an opening anchor and the snapshot lifecycle is retained.  The report itself no longer scans snapshot or movement rows.
- The final display-name ordering can still require a sort; this ADR removes the proven movement-aggregation bottleneck, not every possible workspace allocation.
- The controlled bootstrap and later concurrency benchmark must be scheduled separately.  They were not run as part of this change.

## Alternatives considered

### Read `Inventory_Movement_Daily` directly

Rejected: one warehouse/product has many movement-date rows.  A report row still needs a range aggregate, so this retains the same concurrency bottleneck or changes the paging contract.

### Use `InventoryBalance_Current` for historical reports

Rejected: it is correct only for current on-hand/reserved values and cannot truthfully provide opening, receipt and issue for historical intervals.

### Maintain balance rows in the Post transaction

Rejected: back-dated changes would lengthen the Post transaction and create a large lock/write fan-out.  Existing durable queue, retry and scope-lock behavior is the correct place for this work.
