# Product Excel Import Phase 1A

Date: 2026-10-08

## 1. Executive Summary

Verdict: `PRODUCT_IMPORT_PHASE1A_READY_FOR_UAT`.

Phase 1A now implements Product-only `.xlsx` preview and insert flow. Preview performs reads only; confirmation rechecks Add permission and reference/product-code state, then uses one SQL connection and transaction for the complete batch. The verdict is source/build/test readiness for controlled UAT, not production readiness.

## 2. Architecture Implemented

`FWarehouse_1_Warehouse_List` -> `FBase` bounded upload helper -> existing `CProduct_Xlsx_Reader` -> Product validator/service -> `CWarehouseMaster_Controller` -> existing `F2018_sp_ins_San_Pham` contract.

Product rules remain in `Web_Danh_Muc/Services`; database access and transaction handling remain in Data Access. The legacy generic FBase import flow is not used for this preview/atomic workflow.

## 3. Export Architecture Preservation

No export implementation was replaced. `Export_Excel<T>`, Telerik default/custom export, CSV, `CExcel_Controller`, `<GridExcelExport>` and `<GridCsvExport>` remain in place. The focused export workflow tests passed 2/2.

## 4. Files Added

- `TKS_Thuc_Tap_V11_Data_Access/Entity/Warehouse/CProductImportModels.cs`
- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CProductImportValidator.cs`
- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseProductImportService.cs`
- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Product_Import.razor`
- `TKS_Thuc_Tap_V11_Web/wwwroot/FileManagement/Template/San_Pham_Import_Template.xlsx`
- Product import validation, permission and UI/template tests in `TKS_Thuc_Tap_V11_Data_Access.Tests`.

The existing `CProduct_Xlsx_Reader` and parser-compatibility fixtures/tests were reused, not replaced. The working tree contained pre-existing changes before implementation; they were preserved and no commit was created.

## 5. Files Modified

- `TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor`: generic bounded upload read helper only.
- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_1_Warehouse_List.razor`: Product/Add-gated action, modal wiring, one refresh and one best-effort history event.
- `TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehouseMaster_Controller.cs`: paged product-code preload, shared existing save-parameter mapping, transaction-aware batch insertion.
- Test project file: reference needed to test the Product service/UI contract.

Some touched files also had earlier unrelated modifications. Those were not reverted or overwritten.

## 6. Product Template Contract

Template path: `TKS_Thuc_Tap_V11_Web/wwwroot/FileManagement/Template/San_Pham_Import_Template.xlsx`.

It contains one `SanPham` sheet and exactly these headers, in order: `Ma_San_Pham`, `Ten_San_Pham`, `Ma_LSP`, `Ten_Don_Vi_Tinh`, `Ghi_Chu`. Product code cells are formatted as Text. No IDs or audit fields are present.

## 7. ClosedXML Reader

The existing `CProduct_Xlsx_Reader` is reused with ClosedXML 0.105.1. It reads only `SanPham`, rejects malformed workbooks, formula values are not trusted, and it enforces 1 MiB, 100 data rows and 5 columns. No second parser or Telerik fallback was added.

## 8. Validation Rules

The validator enforces exact headers/order, required fields, trimming, field limits (code 100, name 255, category 100, unit 200, note 1000), formula rejection with row/column context, duplicate codes in the file and database, category resolution by code, and unique normalized unit-name resolution. Existing products are never updated or skipped.

## 9. Lookup Strategy

Categories and units are preloaded once per Preview/Confirm. Existing product codes are fetched through the existing paged master query in pages of 500 and compared in memory; there are no per-row database lookups. This reads all product codes, so very large catalogs may justify a future bounded set lookup.

## 10. Permission Enforcement

The page shows Import Excel only for `SanPham` when the current function has Add permission. The service independently checks the current server-provided user/function on both Preview and Confirm. Edit permission is not used; no new permission type or hard-coded function code was introduced.

## 11. Preview Flow

The UI accepts `.xlsx` only and checks the 1 MiB size before preview. `FBase.Read_Import_File_Async` enforces extension/size and bounded stream reading; the reader independently enforces the file limit. Preview parses, loads reference data and validates but does not insert or update database rows.

## 12. Transaction Implementation

Confirm rechecks permission and reloads category, unit and existing-code state. `Import_Products_Async` opens one `SqlConnection`, begins one transaction, and invokes the existing `F2018_sp_ins_San_Pham` parameter mapping for every row using that same connection/transaction. Any row failure triggers rollback; unique-key and current reference failures are translated to row-specific messages. No bulk-copy path bypasses the stored procedure.

The stored procedure source currently contains caller-transaction-aware behavior. Its deployment/runtime state was not checked in this task.

## 13. Audit / History

Created/updated user and function values come from the server-side page context, never from workbook cells. After a successful batch, the page records one best-effort `Product Excel Import` history event with `Rows: N`. A history failure does not undo the committed import.

## 14. UI Flow

The Product page opens a dedicated modal with template download, `.xlsx` selection, Preview, row counts, per-row errors and Confirm. Confirm is enabled only for a fully valid preview and is disabled while importing. Successful completion rebinds the Product grid once; no row-level refresh is performed.

## 15. Tests

Source plan: user-provided Phase 1A task text; no local `*.plan.md` was used. User journeys covered: an Add-authorized user previews a product workbook and sees validation errors before any write; a fully valid preview can be confirmed as one atomic batch.

- TDD RED: validation/permission tests initially failed to compile because the Product import service/result types were absent; the UI helper test later failed because `FBase.Read_Import_File_Async` was absent. The template contract test failed while the template file did not yet exist.
- TDD GREEN: Product import + parser compatibility + stream-copy tests initially passed 39/39; after adding Preview-no-write and single-transaction contract tests, the combined import/parser/stream/export filter passed 43/43. The focused export workflow filter separately passed 2/2.
- Coverage collection rerun: the same combined filter passed 43/43 and produced a raw `.coverage` artifact under the test project's `TestResults` directory.
- Test mapping: `ProductExcelImportValidationTests.cs` (validation), `ProductExcelImportAuthorizationTests.cs` (permission), `ProductExcelImportUiTests.cs` (upload/UI/template/Preview-no-write/transaction contract), `XlsxParserCompatibilityTests.cs` and `StreamCopyBehaviorTests.cs` (reader/stream), `WarehouseUiWorkflowTests.cs` (export preservation).
- Validation tests cover headers, required/length limits, formulas, trimming, duplicate codes, unknown/ambiguous references, and preview validity.
- Permission tests cover Add denial (including Edit-only) and permission removal between Preview and Confirm.
- UI/template tests cover bounded upload checks, Product/Add action scope, success refresh/history callback, confirm gating, sheet/header contract and Text format.
- The collector did not produce a numeric summary in the available command output; the skill's 80% coverage threshold is **not verified**. No coverage percentage is claimed.
- `TRANSACTION_INTEGRATION_TEST_NOT_EXECUTED`: no safe test database was confirmed. No database connection or mutation was attempted.

Commands and outcomes:

```text
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter "FullyQualifiedName~ProductExcelImport|FullyQualifiedName~XlsxParserCompatibilityTests|FullyQualifiedName~StreamCopyBehaviorTests|FullyQualifiedName~Warehouse_excel_export" --verbosity quiet
Passed: 43, Failed: 0

dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter "FullyQualifiedName~Warehouse_excel_export" --verbosity quiet
Passed: 2, Failed: 0

dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter "FullyQualifiedName~ProductExcelImport|FullyQualifiedName~XlsxParserCompatibilityTests|FullyQualifiedName~StreamCopyBehaviorTests|FullyQualifiedName~Warehouse_excel_export" --collect:"Code Coverage" --verbosity quiet
Passed: 43, Failed: 0; raw .coverage artifact generated
```

## 16. Build Results

- `dotnet build TKS_Thuc_Tap_V11_Web_Common\TKS_Thuc_Tap_V11_Web_Common.csproj --no-restore --verbosity quiet`: succeeded, 4 warnings, 0 errors.
- `dotnet build TKS_Thuc_Tap_V11_Web_Danh_Muc\TKS_Thuc_Tap_V11_Web_Danh_Muc.csproj --no-restore --verbosity quiet`: succeeded, 12 warnings, 0 errors.
- `dotnet restore TKS_Thuc_Tap_V11_Web\TKS_Thuc_Tap_V11_Web.csproj --verbosity quiet`: succeeded.
- `dotnet build TKS_Thuc_Tap_V11_Web\TKS_Thuc_Tap_V11_Web.csproj --no-restore --verbosity quiet`: succeeded, 24 warnings, 0 errors.

Telerik warnings `TKL002`, `TKL101` and `TKL004` remain visible; they were not suppressed or altered.

## 17. Known Limitations

The paged duplicate preload reads all product codes. No browser E2E was run. The tests/builds do not prove database transaction behavior, live stored-procedure deployment, runtime permission-cache freshness, or compatibility with user-created workbooks from every spreadsheet producer.

## 18. Remaining Production Gates

- `TRANSACTION_INTEGRATION_TEST_NOT_EXECUTED`; run the 100-row/row-50-failure rollback test only against a confirmed safe test database.
- `UNIT_COLLISION_CHECK: NOT_EXECUTED`; no safe DEV/UAT database was confirmed.
- `TELERIK_ENTITLEMENT: NEED_OWNER_CONFIRMATION`.
- Verify the transaction-aware procedure source is deployed to the target UAT database.
- Verify deployed host upload limits and complete UAT with actual user-created `.xlsx` files.

## 19. Phase 1 Verdict

`PRODUCT_IMPORT_PHASE1A_READY_FOR_UAT` for source-level review and controlled UAT. This is **not** `PRODUCTION_READY`; the gates above remain open. No database changes, deployment, or commit were performed.
