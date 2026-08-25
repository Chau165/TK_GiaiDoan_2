# ADR 0001: Reserve stock for draft issue documents

## Status

Accepted and implemented for the Warehouse module.

## Context

The existing module deliberately keeps Draft documents out of the movement
ledger and current on-hand balance. That is correct for physical stock, but it
allows multiple Draft issue documents to request the same available quantity.
For example, two Draft issues of 30 can be saved while the posted balance is
only 50.

The UI saves headers and details in separate requests, so a SQL transaction
cannot remain open while a user edits a document. The design must preserve the
current Draft/Post workflow and enforce the rule at the stored-procedure
boundary.

## Decision

Keep the existing lifecycle and introduce an active reservation for each Draft
issue detail:

- `On Hand` is `InventoryBalance_Current.CurrentQuantity` and changes only for
  Posted receipt/issue movements.
- `Reserved` is the sum of active Draft issue-detail reservations.
- `Available` is derived as `On Hand - Reserved`.
- Saving a Draft issue detail reserves its quantity and rejects the operation
  when Available is insufficient.
- Updating a Draft issue detail applies only the reservation delta.
- Deleting a Draft detail or header releases its reservation.
- Posting an issue releases its reservation and subtracts the same quantity
  from On Hand in the same transaction.
- Posted documents remain immutable; historical reports still use only Posted
  ledger movements.

The reservation table is keyed by issue detail so the system can identify the
owner of each held quantity and release it safely. The balance counter and
reservation rows are updated together under the existing SQL stored-procedure
architecture.

## Consequences

Users can no longer save Draft issue quantities that exceed current Available
stock. The inventory report exposes current On Hand, Reserved, and Available
alongside the existing historical opening/in/out/closing values.

Existing Draft issue data must be reconciled once with the reservation rebuild
procedure after deploying the schema extension. If existing Draft requests
exceed On Hand, the rebuild fails and those requests require business review;
the system does not silently create negative availability.

## Alternatives considered

- Reserving only when Post is clicked: rejected because it preserves the
  overbooking scenario between Draft documents.
- Client-only validation: rejected because a caller can bypass the browser and
  concurrent requests can race.
- Holding one database transaction across the whole edit screen: rejected
  because the current UI performs separate browser requests and would create
  long-lived locks.
