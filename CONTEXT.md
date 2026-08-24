# Warehouse Inventory

Warehouse Inventory records goods movements by receipt and issue documents. It separates the historical movement ledger from a future current-stock projection.

## Language

**Document (Phiếu)**:
The business document consisting of one header and zero or more product details; it is either a receipt or an issue and follows the Draft → Posted lifecycle.
_Avoid_: treating a header or detail row as the whole document.

**Draft (Nháp)**:
A saved header or detail that is still editable and does not contribute a stock movement.
_Avoid_: calling a saved detail a movement before Post.

**Posted (Đã Post)**:
The irreversible confirmation of a complete document. It makes every detail an effective movement and updates current stock atomically.
_Avoid_: using “saved” and “posted” interchangeably.

**Movement Ledger**:
The receipt and issue header/detail tables that are authoritative for stock history and period reports.
_Avoid_: Current balance, cache.

**Current Inventory Balance**:
A derived quantity for one warehouse and product at the present time; it is not the historical source of truth.
_Avoid_: Movement ledger, historical snapshot.

**Inventory Delta**:
The signed change to current inventory obtained by reversing an old effective movement and applying a new effective movement.
_Avoid_: the new detail quantity alone.
