# Warehouse CRUD contract TDD evidence

Date: 2026-08-24  
Source plan: user request derived from the Inventory/Paging audit; no external plan file was used.

## User journeys

1. As a Warehouse user, I can save receipt and issue headers/details and receive the persisted ID.
2. As a Warehouse user, I can update a header date/warehouse and a detail quantity without creating duplicate rows.
3. As a Warehouse user, I can delete a detail or header and retain the existing cascade/negative-stock rules.
4. As an application caller, a failed delete inside my transaction does not rollback the caller-owned transaction.

## A. Root cause

### Header save

- C# expected: `Save_Document_Async` passed 10 receipt or 9 issue positional values, including audit values.
- SP actual: receipt header has 6 parameters; issue header has 5.
- Why mismatch: `CSqlHelper.AssignParameterValues` derives the current SP signature and requires exact count.
- Canonical contract: named, typed Warehouse parameters only; receipt `@Auto_ID, @So_Phieu_Nhap_Kho, @Kho_ID, @NCC_ID, @Ngay_Nhap_Kho, @Ghi_Chu`; issue replaces supplier with `@Ngay_Xuat_Kho`.

### Detail save

- C# expected: 9 positional values, including audit values.
- SP actual: 5 parameters for each receipt/issue detail.
- Canonical contract: named, typed `@Auto_ID, @Nhap_Kho_ID/@Xuat_Kho_ID, @San_Pham_ID, @SL_*, @Don_Gia_*`.

### Delete

- C# expected: passed `Auto_ID`, user and function values.
- SP actual: each delete SP accepts only `@Auto_ID`.
- Canonical contract: named `@Auto_ID bigint` only. Public controller method signatures retain user/function arguments for UI/facade compatibility but no longer bind them to SPs.

### Save ID

- C# expected: `Scalar_ID` reads the first scalar result.
- Deployed Warehouse save SP actual before: only `@Auto_ID OUTPUT`, no result set.
- Canonical contract chosen: match the existing master-SP convention: retain `@Auto_ID BIGINT OUTPUT` for direct-SQL compatibility and return `SELECT @Auto_ID AS Auto_ID`; C# contract is the scalar result consistently for all four Warehouse save SPs.

### Transaction ownership

- Delete SPs previously started a transaction unconditionally and rollback all open transactions in CATCH.
- `@OwnTransaction` alone was insufficient because `XACT_ABORT ON` still aborted the caller transaction on validation `THROW`.
- Canonical contract: delete SPs use `@OwnTransaction`; if caller owns the transaction they create `SAVE TRANSACTION WarehouseDelete`, use `XACT_ABORT OFF`, and rollback only that savepoint on error. Their own transaction is rolled back when the SP owns it.

## B. Before/after contract table

| Operation | Before | After |
|---|---|---|
| Receipt header save | 10 positional C# values vs 6 SP parameters; no scalar result | 6 named/type-matched parameters; scalar `Auto_ID` result |
| Issue header save | 9 positional C# values vs 5 SP parameters; no scalar result | 5 named/type-matched parameters; scalar `Auto_ID` result |
| Receipt detail save | 9 positional C# values vs 5 SP parameters; no scalar result | 5 named/type-matched parameters; scalar `Auto_ID` result |
| Issue detail save | 9 positional C# values vs 5 SP parameters; no scalar result | 5 named/type-matched parameters; scalar `Auto_ID` result |
| Receipt/issue header delete | 3 positional C# values vs 1 SP parameter; unconditional transaction | named `@Auto_ID`; own transaction or savepoint |
| Receipt/issue detail delete | 3 positional C# values vs 1 SP parameter; unconditional transaction | named `@Auto_ID`; own transaction or savepoint |

## C. Code changes

- `TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehouseDocument_Controller.cs`
  - Builds named `SqlParameter` instances with explicit bigint/date/decimal/nvarchar types.
  - Sends only fields accepted by each SP.
- `TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehouse_Controller_Base.cs`
  - Adds narrow named-parameter overloads for scalar and non-query Warehouse calls.
- `TKS_Thuc_Tap_V11_Data_Access/DataLayer/CSqlHelper.cs`
  - Exposes the existing typed `SqlParameter[]` scalar path and adds a typed non-query wrapper; generic positional callers are unchanged.
- `Database/WarehouseModule.Procedures.sql`
  - Four save SPs return `SELECT @Auto_ID AS Auto_ID`.
  - Four delete SPs preserve caller transaction ownership using a savepoint.
- `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseCrudContractIntegrationTests.cs`
  - Adds isolated, GUID-tagged integration fixtures and CRUD/transaction tests.

## D. Tests

| Test/command | Scenario | Result |
|---|---|---|
| `WarehouseCrudContractIntegrationTests.Controller_executes_receipt_and_issue_crud_with_returned_ids_and_header_cascade` | Receipt/issue insert, ID, header date/kho update, detail quantity update, product-change rejection, direct detail delete, header cascade | PASS |
| `WarehouseCrudContractIntegrationTests.Controller_rejects_invalid_detail_and_rolls_back_negative_issue_detail` | zero quantity, missing header FK, negative issue rollback | PASS |
| `WarehouseCrudContractIntegrationTests.Delete_validation_failure_does_not_rollback_a_caller_owned_transaction` | validation error leaves caller `@@TRANCOUNT = 1` | PASS |
| `sqlcmd ... WarehouseModule.IntegrationTests.sql` | valid receipt then issue, report calculation, negative stock rejection | PASS |
| `sqlcmd ... WarehouseModule.ValidationMessages.IntegrationTests.sql` | validation message regression | PASS |
| `sqlcmd ... WarehouseModule.ReportContract.IntegrationTests.sql` | report result contract regression | PASS |
| `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests...` | full data-access suite | PASS, 39/39 |
| `dotnet build TKS_Thuc_Tap_V11.sln --no-restore` | full solution | PASS, 0 errors, 27 existing warnings |

## RED/GREEN evidence

- RED 1: the new controller integration tests failed with `sp_XNK_Nhap_Kho_Save_Header. Parameter count does not match Parameter Value count.`
- GREEN 1: after typed named binding and scalar return contract, focused CRUD tests passed 2/2.
- RED 2: caller-owned delete transaction test observed `@@TRANCOUNT` changed from expected `1` to actual `0`.
- GREEN 2: after savepoint ownership handling, the same test passed and caller transaction stayed active.

Checkpoint commits: `ee14b59` (RED CRUD), `bc8bccb` (GREEN CRUD), `a730d6c` (RED ownership), `3e4cd96` (GREEN ownership).

## Coverage and known gaps

The test project has no `coverlet.collector`/coverage collector configured. `dotnet test --collect:"XPlat Code Coverage"` executed the focused tests successfully but reported that the collector was unavailable, so no percentage is claimed. The new integration tests execute every changed Warehouse CRUD branch, but solution-wide 80% coverage has not been mechanically measured.

## Remaining risks before InventoryBalance_Current

- Header and detail remain separate UI/SP operations; this phase deliberately does not create document-level save.
- Header date/kho edits are correct under existing ledger semantics but still require a document/balance delta design before adding a balance table.
- Detail product/document change remains explicitly rejected by current business rule (`51110`/`51138`); a future requirement to allow it needs a separate migration/semantics test.
- No InventoryBalance table, snapshot, paging/read query, event pipeline, or validator algorithm change was introduced.
