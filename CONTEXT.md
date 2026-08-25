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

**Warehouse Scope (Phạm vi kho)**:
The set of warehouses a signed-in User may read or mutate, defined by the User–Warehouse Assignment `(Ma_Dang_Nhap, Kho_ID)`.
_Avoid_: treating a warehouse scope as a menu/function permission or as a client-only dropdown filter.

**User–Warehouse Assignment (Phân quyền kho - user)**:
A unique assignment that connects an existing login identity `Ma_Dang_Nhap` to one warehouse `Kho_ID`.
_Avoid_: calling `Ma_Dang_Nhap` a new permission code; it identifies the User and is not itself authorization.

**On Hand (Tồn thực tế)**:
The quantity physically available in a warehouse after posted receipt and issue movements; it changes only when a document is Posted.
_Avoid_: treating a Draft issue as a physical movement.

**Reserved (Đang giữ)**:
The quantity committed to active Draft issue details in a warehouse; it remains physically present but cannot be promised to another issue.
_Avoid_: subtracting Reserved from On Hand in the historical movement ledger.

**Available (Khả dụng)**:
The quantity that a new Draft issue may reserve, calculated as `On Hand - Reserved`.
_Avoid_: using the historical period closing quantity as the current reservation limit.

**Issue Reservation (Giữ chỗ phiếu xuất)**:
An active allocation owned by one Draft issue detail. Saving, changing, deleting, moving, or Posting that detail must adjust its reservation atomically with the current balance.
_Avoid_: calculating availability only in the browser or allowing two Draft issues to reserve the same stock.
