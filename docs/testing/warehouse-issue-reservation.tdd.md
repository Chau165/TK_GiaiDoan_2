# Draft issue reservation TDD evidence

## Contract

The existing Draft/Post lifecycle remains in place:

- Posted receipts and issues are the only movements in historical reports and
  `InventoryBalance_Current.CurrentQuantity` (On Hand).
- A Draft issue detail reserves quantity against the current
  `(Kho_ID, San_Pham_ID)` balance.
- Available quantity is `On Hand - Reserved`.
- Adding or increasing a Draft issue is rejected when Available is insufficient.
- Updating, deleting, moving, or Posting a Draft issue releases or transfers
  its reservation atomically with the document operation.
- A Posted document cannot be edited or deleted through the normal document
  stored procedures.

## RED checkpoint

Before the schema and procedure changes, the new integration tests were run with:

```text
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --filter FullyQualifiedName~WarehouseIssueReservationIntegrationTests --no-restore --logger "console;verbosity=minimal"
```

Result: 3 failed as expected because `ReservedQuantity` did not exist. This
proved the test contract was exercising the missing production behavior rather
than passing against the old model.

## GREEN checkpoint

After applying the schema and stored-procedure changes, the focused suite passed:

```text
Passed: 3, Failed: 0, Skipped: 0
```

The tests cover:

1. A 50-unit Posted balance accepts a 30-unit Draft issue, then rejects a
   second 30-unit Draft issue.
2. Updating and deleting the first Draft releases the correct reservation;
   Posting consumes On Hand and leaves Reserved at zero.
3. Deleting a Draft issue header releases all of its detail reservations.

The existing CRUD contract tests were updated to assert the new rejection point
at Draft detail save. The full integration suite then passed:

```text
Passed: 58, Failed: 0, Skipped: 0
```

The test project disables parallel execution because all integration tests use
the same local SQL Server database; this prevents unrelated cleanup operations
from deadlocking each other.

## Verification boundary

The evidence above verifies the controller-to-stored-procedure and SQL behavior.
It does not replace a browser smoke test. The UI should be checked manually for
the inventory report columns `Tồn thực tế`, `Đang giữ`, and `Khả dụng`, and for
the user-facing error when a second Draft issue exceeds Available.
