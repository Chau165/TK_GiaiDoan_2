# Current inventory uses the posted balance read model

The Current Inventory View reads `InventoryBalance_Current`, which is updated atomically only when a document is Posted. The historical Xuất nhập tồn report remains a separate period-report contract backed by snapshots and daily movement aggregates, because a current balance cannot truthfully supply opening, receipt, and issue quantities for an arbitrary date interval.
