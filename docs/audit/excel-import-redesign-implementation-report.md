# Excel Import Redesign Implementation Report

## Verdict

**PARTIALLY IMPLEMENTED.** The profile-driven parser and all five Warehouse master import paths are implemented while retaining their Preview/Confirm persistence workflow. Inbound and Outbound reach grouped, validated Preview, but persistence and Confirm are deliberately blocked because current schema/API evidence does not support a safe complete posting flow.

## Architecture and Scope

- Added generic bounded XLSX logical-cell reading, configurable JSON profiles, normalized header detection, sheet-role detection, mapping result/error models, and adapters to existing master validators.
- Migrated Product, Unit, Category, Supplier, and Warehouse readers to detected canonical mappings while preserving existing permission checks, Confirm revalidation, batch transaction/business SP path, and audit behavior.
- Added exact duplicate skip and conflicting-duplicate error behavior; Confirm receives only valid, non-skipped rows.
- Added grouped Inbound/Outbound preview with detail/header joins, structural/content issues, reference resolution when references are provided, and aggregate outbound availability validation by `(Warehouse_ID, Product_ID)`.
- Added fail-fast header/template validation to the legacy positional `FBase.Import_Excel` path. It runs before queue creation and before the handler, preventing reordered/moved headers from being silently read by ordinal.
- No database schema, database data, permissions, jobs, or worker state was modified as part of this implementation.

## Files Added and Changed

Principal added files:

- Generic parser/profile pipeline: `CWorkbook_Xlsx_Reader.cs`, `CImportProfileModels.cs`, `CImportProfileProvider.cs`, `CWorkbookStructureDetector.cs`, `CProfiledWorkbook_Xlsx_Reader.cs`, `CImportLegacyWorksheetAdapter.cs`, and `Utility/Profiles/excel-import-profiles.json`.
- Master adapters/models: `CMaster_Xlsx_Reader.cs`, the Product/Unit/Category/Supplier/Warehouse XLSX readers, and the five corresponding `Entity/Warehouse/*ImportModels.cs` files.
- Transaction preview: `CTransactionImportPreviewService.cs` and `CTransactionImportPreviewModels.cs`.
- Master workflow: five `C*ImportValidator.cs`/`CWarehouse*ImportService.cs` files and five `FWarehouse_*_Import.razor` components.
- Customer fixtures: the seven XLSX files in `docs/test-data/customer-import/`.
- Tests: profile/detection and transaction preview tests, five master validation/UI/authorization suites, parser compatibility and stream/resource tests, and `LegacyExcelHeaderValidationTests.cs`.

Key existing files changed:

- `CExcel_Controller.cs` and `FBase.razor` for the legacy positional-header gate.
- `CWarehouseMaster_Controller.cs` for loading complete Product master records during reference and duplicate validation.
- The Warehouse list/import UI and related project files to wire the five master workflows.
- Four profile-reader tests were updated to assert that sheet renames and bounded extensions follow the new contract while still rejecting unmapped data sheets and actual resource-limit violations.

## Old Components Reused

The existing five master validation and service/controller/SP paths remain the business/persistence layer. New reader adapters translate detected profile mappings into the worksheet models those validators already consume. F1018 was not repurposed. Legacy import handlers were not broadly rewritten; only their shared entry point now checks the selected template headers.

## Master Import Status

| Import | Status | Notes |
| --- | --- | --- |
| Product | Implemented | Category and unit reference validation remains strict; customer fixture's `THUNG`/`HOP` does not match name-only unit master (`Thùng`/`Hộp`). |
| Unit | Implemented | Persists current name/note fields only. |
| Category | Implemented | Persists current code/name/note fields only. |
| Supplier | Implemented | Persists supported code/name/note fields only. |
| Warehouse | Implemented | Current domain persists name/note; sample warehouse code and operational fields are unsupported. |

## Inbound / Outbound Status

**Inbound:** workbook is detected, header/detail rows are grouped by document number, and required values, date/quantity, orphan details, empty documents, and supplied references can be reported. There is no Confirm or persistence path.

**Outbound:** same Preview-only status. Aggregate stock checks use `Current - Reserved` grouped by warehouse/product when a balance snapshot is supplied. The current preview service does not load live database references or stock itself and is not connected to UI Confirm.

**Blocker:** current document schemas lack fields required to preserve customer data (notably warehouse code, transaction UOM, LOT/HSD, and location), and no verified atomic Excel receipt/issue API was found to reuse. Direct table/inventory writes were not introduced. A production-ready path needs schema/business-layer design and must reuse posting/concurrency rules before enabling Confirm.

## Transaction and Validation Behavior

Master imports keep the existing revalidation and single batch transaction. Duplicate rows matching existing business values are skipped; key conflicts with different content fail Preview. Product/category/unit/supplier/warehouse reference lookups remain exact after trim, whitespace collapse, NFC, and case normalization; accents remain significant. Transaction Preview always reports persistence blocked.

The legacy header gate compares the first worksheet's first-row headers against the selected template, in order, before creating an import queue or invoking positional code. It fails closed if the source/template is missing, the header is not at A1, the header sequence contains blank cells, or names/order differ. It does not change legacy per-row commit semantics; those remain a separately documented risk.

One existing legacy route (`F1003` member import) references `Thanh_Vien_Template.xlsx`, which is absent from the checked-in template directory. The guard therefore blocks that route until the expected template is restored and verified; it will not silently bypass validation.

## Acceptance Matrix

| Requirement | Implemented? | Evidence | Test |
| --- | --- | --- | --- |
| Reorder columns | Yes | Profile detector maps by canonical header, not ordinal | `ImportStructureDetectionTests.Reordered_columns_and_aliases_are_detected_and_unknown_columns_are_classified` |
| Rename to known alias | Yes | `SKU` maps to ProductCode | Same detector test |
| Add extra column | Yes | Classified unsupported/ignored; surfaced to Preview, not persisted | Same detector test and Product UI tests |
| Move header row | Yes | Scanner checks first 20 rows; customer Product and transaction headers are at row 3 | `Customer_product_workbook_detects_header_after_title_and_renamed_sheet`; transaction detector tests |
| Rename sheet | Yes | Role/header signatures take precedence over exact name | Customer Product fixture test; renamed-sheet detector test |
| Unsupported topology | Yes | Multiple candidate Product sheets rejected as `UNSUPPORTED_FILE_STRUCTURE` | `Multiple_candidate_product_sheets_are_rejected_as_unsupported_structure` |
| `Cuốn` != `Cuộn` | Yes | Accent-sensitive business comparer | `Business_comparer_preserves_vietnamese_diacritics`; Unit/Product validation tests |
| Exact duplicate skip | Yes | In-file and DB exact duplicates marked skipped | Product/Unit/Category/Supplier/Warehouse validation tests |
| Same-key content conflict | Yes | Conflict blocks Confirm; no overwrite | Product and master validation tests |
| Reference validation | Yes for master Preview | Current master records resolved by code/name; missing/ambiguous references fail | Master validation suites; Product customer fixture reports missing unit |
| Inbound product/reference validation | Partial | Resolver reports missing Product reference when caller supplies reference lists; no live reference-loading workflow | `Inbound_preview_reports_product_reference_not_found`; production wiring NOT VERIFIED |
| Outbound stock validation | Yes in Preview API | Uses available quantity `Current - Reserved` | `Outbound_preview_compares_aggregate_quantity_with_available_inventory` |
| Aggregate stock across documents | Yes in Preview API | Aggregates same warehouse/product key across the batch | `Outbound_preview_aggregates_same_inventory_key_across_documents` |
| Confirm-time revalidation | Master: preserved; transaction: no Confirm | Master services revalidate before writes; transaction `PersistenceBlocked` is always true | Master service tests; transaction test asserts Confirm disabled |
| Atomic document persistence | No, intentionally blocked | No verified complete posting API/schema contract; no direct inventory writes | No persistence test by design |
| Legacy positional header mismatch | Yes | Template comparison precedes queue and positional handler | `LegacyExcelHeaderValidationTests` |

## Customer Fixtures

All seven unmodified customer workbooks are included under `docs/test-data/customer-import/` and copied as test fixtures. Unit, Category, Supplier, and Warehouse samples reach valid previews. Product workbook parses and reaches validation, but cannot Confirm because sample unit values are codes (`THUNG`, `HOP`) while current unit master only exposes names. Inbound and Outbound workbooks detect the two roles, group documents and lines, and remain non-confirmable by design.

Synthetic detector tests additionally cover reordered columns, known aliases, unknown columns, ambiguous/missing mappings, renamed sheet, header row 3, and unsupported multi-candidate topology.

## Verification

- Command: `dotnet build TKS_Thuc_Tap_V11_Web/TKS_Thuc_Tap_V11_Web.csproj --no-restore -v:q -clp:ErrorsOnly`
- Result: **Build succeeded, 0 errors** (27 environment/licensing/platform warnings).
- Command: `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter "FullyQualifiedName~ExcelImportValidationTests|FullyQualifiedName~ExcelImportUiTests|FullyQualifiedName~ExcelImportAuthorizationTests|FullyQualifiedName~XlsxReaderTests|FullyQualifiedName~XlsxParserCompatibilityTests|FullyQualifiedName~ImportStructureDetectionTests|FullyQualifiedName~TransactionImportPreviewTests|FullyQualifiedName~LegacyExcelHeaderValidationTests|FullyQualifiedName~StreamCopyBehaviorTests|FullyQualifiedName~ProductImportDatabaseSafetyTests" -v:q -clp:ErrorsOnly`
- Result: **148 passed, 0 failed**. The filter excludes database runtime-integration suites.
- Runtime DB integration: **NOT VERIFIED**. `TKS_INTEGRATION_CONNECTION_STRING` was not configured; no live or production database was accessed.
- Browser UAT and test-database permission parity: **NOT VERIFIED**.
- Build occurred as part of the focused test command for the affected projects. Telerik licensing warnings remain in the environment; they did not fail this test run.

## Remaining Risks and Follow-up

1. Design/verify schema and domain handling for warehouse codes, transaction UOM, lot/expiry/location, and customer-specific fields before transaction persistence.
2. Identify or add a supported atomic receipt/issue posting boundary with current-reference and stock revalidation under existing inventory fences/locking. Do not enable Confirm before this is tested.
3. Wire safe bulk reference/current-stock loading and UI Preview for transaction imports; avoid per-row database lookups.
4. Run master-import runtime integration and end-to-end UAT against a designated safe test DB with matching permission metadata.
5. Review legacy handlers' existing per-row commit behavior separately; this change prevents positional header corruption but does not rewrite their transaction semantics.
6. Restore and verify the missing `Thanh_Vien_Template.xlsx` before re-enabling F1003 member import.

Detailed design: [`docs/import/excel-import-redesign.md`](../import/excel-import-redesign.md).
