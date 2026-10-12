# Warehouse Excel Import Redesign

## 1. Current vs New Architecture

The previous master-import readers assumed a fixed worksheet name, fixed header row, and fixed physical column order. The new read path separates workbook structure from business-field mapping:

```text
XLSX stream -> bounded generic workbook reader -> structure/header detector
            -> JSON import profile -> canonical mapped rows
            -> existing master validator -> Preview -> Confirm/revalidate
            -> existing batch controller/SP transaction
```

`CWorkbook_Xlsx_Reader` reads logical cells and records sheet names, row/column numbers, cell types, formulas, and number formats. It does not assign business meaning to a column. `CWorkbookStructureDetector` finds a profile's sheet role and header within the first 20 rows, then maps normalized headers to canonical field names. Specialized `CProduct_Xlsx_Reader`, `CUnit_Xlsx_Reader`, `CCategory_Xlsx_Reader`, `CSupplier_Xlsx_Reader`, and `CWarehouse_Xlsx_Reader` adapt those mappings to the existing validators and workflows.

The master import UI and persistence workflow remain in the Warehouse module. The shared legacy `FBase.Import_Excel` path is separate and now checks the selected template header before queue creation and before calling its positional handler.

## 2. Import Profile Model

Profiles live in `TKS_Thuc_Tap_V11_Data_Access/Utility/Profiles/excel-import-profiles.json`, loaded by `CImportProfileProvider`. The JSON root contains shared `referenceMaps` and the Product, Warehouse, Unit, Category, Supplier, Inbound, and Outbound profiles. A field can declare `Map`, `Required`, `SafeIgnore`, or `BlockIfNonEmpty`, a reason, exact safe values, an equality condition, and an optional `MapToField` cross-check. Extra headers are safe-ignored only through `safeIgnoreHeaders` entries with reasons. These decisions are configuration-driven rather than customer-specific service branches; F1018 is not a runtime mapping platform.

Master profiles bound workbooks to 1 MiB and 100 data rows; Inbound/Outbound profiles use 25 MiB and 20,000 data rows. Worksheet and column counts are also bounded. The generic reader buffers only up to the profile byte limit and never evaluates formulas.

The detector returns logical sheet and row objects plus each source column's disposition (`MAPPED`, `IGNORED`, or `UNSUPPORTED`). It reports nonempty unknown columns and non-compatible values in `BlockIfNonEmpty` fields as `UNSUPPORTED_DOMAIN_CAPABILITY`. Populated safe-ignore fields, safe headers, exact map results, compatible conditional values, and blocked values are retained as row-level `Field_Policy_Diagnostics` in the master and transaction Preview models. Unsupported columns/capabilities block master Confirm. Required fields must map exactly once. Ambiguous mappings and missing required mappings fail Preview. Worksheet names are hints, not identity; a profile must still have a uniquely detectable structure.

## 3. Header Normalization Rules

Header normalization applies Unicode normalization, trims ends, collapses whitespace, normalizes `_` and `-` separators, and compares without case. Thus ` Mã_VT `, `Mã   VT`, and `MÃ VT` can resolve to the configured alias. It does not use fuzzy matching. If two source columns map to one canonical field, the detector returns `AMBIGUOUS_MAPPING`.

The generic detector scans the first 20 rows. It accepts headers below title rows and does not require the old sheet name. Formula cells are recorded as formulas and are not evaluated; business validators reject formula content in mapped fields.

## 4. Business Value Comparison Rules

`CImportBusinessValueComparer` applies NFC, trim, repeated-whitespace collapse, and case-insensitive comparison. Vietnamese diacritics remain significant: `Cuốn` is not `Cuộn`. `CImportValueMapper` applies only an exact source value configured for the current profile and canonical field before lookup; it does not remove accents or use edit distance, substring, phonetic, or AI matching. Reference lookup and duplicate identity use business codes/names under this comparer, not database IDs supplied by a workbook.

## 5. Duplicate Behavior

An exact duplicate in the database or earlier in the workbook is marked skipped and is not written. Reusing a business key with different content is an error; the importer does not overwrite the existing record. `Can_Confirm` requires at least one valid row, zero errors, and no unsupported columns or capability issues. Confirm revalidates current data before persistence, and only valid, non-skipped rows are passed to the existing controller.

## 6. Reference Validation

Validators resolve references from preloaded current records where the workflow requires them (for example Product category/unit and transaction warehouse/supplier/product). Configured source values are mapped to internal business names/codes first, then exact lookup resolves the existing ID. Zero or multiple matches are errors; imports do not create missing references implicitly. When both code and name are supplied, Product, Warehouse, and Supplier names are cross-checked against the resolved record. Product unit matching is strict and accent-sensitive. Product input/output UOMs must resolve to the same Unit_ID because Product stores one unit reference.

## 7-11. Master Profiles

| Profile | Mapped fields | Current-domain boundary |
| --- | --- | --- |
| Product | ProductCode, ProductName, CategoryCode, UnitName, IssueUnitName, Note | Unit codes map exactly. Product persists one unit reference; different input/output units, conversion, supplier link, barcode, LOT, and expiry remain blocked. Weight and the fixture's `Ngưng KD? = Không` are diagnostic safe-ignores; non-active status is not representable. |
| Warehouse | WarehouseName, Note | Customer code maps exactly and must match the supplied name. Region/address and suspended status block; manager/phone and row sequence are reasoned safe-ignores. |
| Unit | UnitName, Note | Customer unit code maps exactly and must match the supplied name. Print symbol/group and identity factor `1` are safe for the fixture; a populated base unit or non-identity factor blocks. |
| Category | CategoryCode, CategoryName, Note | Parent is unsupported when populated. `LOT/HSD = Không` matches current behavior, but any enabled tracking value blocks. |
| Supplier | SupplierCode, SupplierName, Note | Phone/email/address/contact and fixture status `Có` are diagnostic safe-ignores. Tax code and payment terms block because current supplier persistence cannot represent them. |

Known extra columns are classified as ignored or unsupported according to the profile and are not persisted. Preview models retain row-level diagnostics for safe ignores, conditional compatibility, exact maps, and blocked values, plus source-to-mapped-to-resolved diagnostics for configured value maps. Neither disposition silently maps a value to a different field.

## 12. Inbound Workbook Structure

Inbound is a two-role `HeaderDetail` profile. The detector identifies one header sheet and one detail sheet from their header signatures; their names may vary. Header and detail rows are grouped by normalized document number. Preview reports duplicate document numbers, orphan details, documents without lines, invalid dates/quantities, and resolvable reference issues.

## 13. Outbound Workbook Structure

Outbound uses the same two-role structure and document-number join. It validates required header/detail values and, when current inventory references are supplied, resolves available quantity per actual `(Warehouse_ID, Product_ID)` key.

## 14. Stock Validation

`Available_Quantity = Current_Quantity - Reserved_Quantity`. Outbound quantities are aggregated across the entire preview by warehouse/product before comparison, so separate documents cannot each consume the same available stock. Insufficient quantity is an error. The preview service does not itself query inventory; its caller must supply a current reference snapshot.

## 15. Transaction Behavior

Master Confirm retains the existing whole-batch transaction, revalidation, permission checks, business stored procedures, rollback, and audit/history behavior. Only valid, non-skipped rows are submitted.

Inbound/Outbound currently provide parsing, grouping, and preview validation only. `PersistenceBlocked` is always true and `Can_Confirm` remains false. No inventory or transaction tables are written from Excel. This is deliberate until a reusable receipt/issue posting API can atomically save header and details, revalidate references/stock under the project's concurrency mechanism, and represent the workbook's required fields.

## 16. How to Add a New Alias

Edit the target field's `aliases` array in `excel-import-profiles.json`, then add or update a detector test for the alias and verify it maps to exactly one canonical field. Do not add business-value synonyms to header normalization or use an alias to bypass domain validation.

## 17. How to Add a New Import Profile

Add a profile with a unique `code`, supported `structure`, bounded limits, required sheet roles, canonical field definitions, aliases, reasoned `safeIgnoreHeaders`, and field-scoped policies/maps where required. Validate that one normalized source has only one target. Add a canonical model/adapter and focused parser, detector, validator, Preview, and Confirm tests. Persistence must use the current business layer; do not add direct inventory writes.

## 18. Unsupported Structural Changes

Examples include changing a single table into multiple related sheets, flattening a header/detail workbook, mixing multiple candidate tables into one sheet, or changing the business topology so roles cannot be uniquely identified. The detector returns `UNSUPPORTED_FILE_STRUCTURE` or a mapping error instead of guessing. Ordinary column order, supported aliases, extra columns, header row movement, and sheet rename are supported within a profile.

## 19. Known Limitations

- Only `.xlsx` is supported by the new generic reader; legacy positional imports continue to depend on their existing EPPlus path and selected `.xlsx` template.
- The seven customer fixtures parse and reach their corresponding previews. Exact code maps are diagnostic only and do not persist external codes. Current fixture validator outcomes are: Unit eligible for Confirm; Warehouse, Category, Product, and Supplier blocked. Supplier is now blocked by tax code/payment terms rather than silently losing them; the earlier statement that Supplier was eligible is superseded by the safe-loss audit.
- The master fixtures are not end-to-end database/UAT evidence. Validator eligibility does not establish successful database persistence, live reference parity, authorization, or production readiness.
- The legacy member import points to `Thanh_Vien_Template.xlsx`, which is absent from the checked-in template folder. The new guard fails closed for this path until its intended template is restored and verified.
- Warehouse business code, UOM conversion/transaction UOM, LOT, expiry, and location data are not represented end-to-end by the current schema/business API. The customer warehouse-code map is only a resolution aid and does not persist the source code.
- Inbound/Outbound do not have UI Confirm, live reference loaders, current-stock query wiring, posting, concurrency revalidation, or atomic persistence yet. Those remain blocked pending a verified reusable business API and schema support.
- Runtime database integration and real browser UAT require an explicitly configured safe test database and permission parity. They are not inferred from unit tests or build success.
