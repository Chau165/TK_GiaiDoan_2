# Receipt / Issue Excel Import Completion Report

## Status

Implementation is present in the local source tree. Static tests and the affected project build passed. Live database execution, browser login, and UI UAT were not run; the user will perform UAT.

No database schema, stored procedure, permission metadata, business data, or deployment was changed.

## Import Contract

The import is available from **Công cụ → Nhập Excel** on receipt and issue pages and is gated by the page's Add permission. It accepts `.xlsx` files up to 25 MiB. Preview resolves references using current authorized data; Confirm reparses the uploaded bytes and reloads references and duplicate-document state before any save.

| Document | Header fields | Detail fields written |
| --- | --- | --- |
| Receipt | `Số CT` → receipt number; `Ngày CT` → document date; `Kho nhận` → exact authorized warehouse reference; `Mã NCC` → exact supplier reference; supported note → header note | `Mã VT` → exact product reference; `SL thực nhận` → quantity; `Đơn giá` → unit price; unit is resolved from the product master, never from workbook UOM |
| Issue | `Số PX` → issue number; `Ngày xuất` → document date; `Kho xuất` → exact authorized warehouse reference; supported note → header note | `Mã hàng` → exact product reference; `SL xuất` → quantity; `Đơn giá` → unit price; unit is resolved from the product master, never from workbook UOM |

Unit price is required and must be greater than zero, matching the existing detail form/stored-procedure contract. It is never inferred from a product or inventory record. Quantity fields `SL chứng từ` and `SL yêu cầu`, workbook UOM/category, lot, manufacture/expiry dates, location, recipient, reason, reference, and line notes are source-only diagnostics and do not override persisted values. Unmatched warehouse, supplier, or product references are not fuzzy-matched or created.

## Duplicate And Transaction Behavior

- Exact existing document number with matching target header and matching detail multiset is marked `SKIPPED_DUPLICATE_DOCUMENT`; it is not saved again.
- Same document number with different target values is a blocking conflict; existing data is not overwritten.
- Each new document uses one SQL transaction for its existing header and detail stored-procedure calls. Any failed detail rolls back that document's header and all its details. Earlier successfully committed documents remain committed and are identified in the failure message.
- Import does not call the document Post procedure and does not directly write inventory or reservation tables.
- After successful confirmation, the page rebinds the receipt/issue grid and reports imported and skipped counts.

## Verification

- `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --verbosity quiet --filter "FullyQualifiedName~TransactionImportPreviewTests|FullyQualifiedName~Customer_transaction_workbooks_report_ignored" -clp:ErrorsOnly` — **17 passed, 0 failed**. Coverage includes received-vs-ordered quantity, product-master unit, required positive price, source-only fields, inventory availability, exact duplicate skip, duplicate conflict, Add permission, save-only-new-document behavior, and both supplied transaction workbook structures.
- `dotnet build TKS_Thuc_Tap_V11_Web_Danh_Muc/TKS_Thuc_Tap_V11_Web_Danh_Muc.csproj --no-restore --verbosity quiet -clp:ErrorsOnly` — **succeeded, 0 errors**. Existing Telerik license and async warnings remain.
- SQL transaction rollback behavior is implemented but was not exercised against a database in this task.
- Browser/UI UAT was not run, as requested. The user should test the UI with Add permission and confirm receipt/issue documents in their own environment.

## Supplied Workbook Evidence

Hashes are SHA-256 of the checked-in customer workbooks used as compatibility fixtures:

| Workbook | SHA-256 | Import readiness |
| --- | --- | --- |
| `06_Nhap_kho_Thang_09_2026_KH.xlsx` | `11689B98548FA3E33180725370F42B56211A28AC3D988289F6C7AA7CF176BAE7` | Has `Đơn giá`; parser tests cover the received quantity field. Live import not tested. |
| `07_Xuat_kho_Thang_09_2026_KH.xlsx` | `9823C5138226D3843D93900BDFFD707D31734B68A0907418086D889706787B07` | Has no `Đơn giá`; it is intentionally blocked by Preview. The user will provide a corrected workbook with that column. |

## Transaction Matrix

| Scenario | Verified result |
| --- | --- |
| Receipt with `SL chứng từ` 40 and `SL thực nhận` 39 | Parser selects 39; test passed. |
| Excel UOM differs from product master | Master unit remains canonical; diagnostic only; test passed. |
| Unit price missing or zero | Blocking validation; tests passed. |
| Exact duplicate document | Skipped without save; service test passed. |
| Existing number with changed detail quantity | Confirm blocked before any save; service test passed. |
| User lacks Add permission | Preview denied before reference load; service test passed. |
| Issue workbook without `Đơn giá` | Preview cannot be confirmed; source fixture has no price column. |
| Per-document SQL failure and rollback | Transaction/rollback path is present; live database failure injection not run. |
| Browser workflow and grid refresh | Implemented in source; UI UAT pending user's test. |
