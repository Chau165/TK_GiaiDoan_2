# Product Inbound-UOM Import Completion Report

Date: 2026-10-10  
Status: **PARTIALLY COMPLETED - source and focused verification passed; live UI UAT deferred to the user's self-test**  
Target database: `TKS_Thuc_Tap_V11_GiaiDoan2` on SQL Server `localhost`

## 1. Business Decision

The business rule is explicit: `Product.Unit_ID` comes only from Excel `ĐVT nhập`. `ĐVT xuất` is source-only because the current Product model persists one Unit reference and has no outbound-UOM field.

## 2. Previous Product Blocker

The previous Preview resolved both source UOM columns independently and blocked rows when the IDs differed. Its historical result was 15 rows, 1 valid, 14 errors, Confirm disabled, Product count 0. That behavior is superseded by the business decision above.

## 3. New Unit_ID Policy

Inbound UOM is required, mapped through the configured exact Unit value map, and resolved uniquely against current Unit records. An invalid or ambiguous inbound value is an error; outbound is never a fallback. Outbound UOM is ignored, not reference-resolved, not persisted, and excluded from duplicate identity. Its diagnostic reason is exactly:

> Current Product domain stores one Unit_ID. Business rule defines Product.Unit_ID from Inbound UOM. Outbound UOM is not persisted by the current Product model.

## 4. Target Contract Update

Updated [master-import-db-first-target-contract.md](master-import-db-first-target-contract.md): `ĐVT nhập` maps to Product `Don_Vi_Tinh_ID`; `ĐVT xuất` is `IGNORE_SOURCE_ONLY`. Existing Category and Unit reference rules and stored-procedure persistence are unchanged.

## 5. Profile And Projection Changes

- `TKS_Thuc_Tap_V11_Data_Access/Utility/Profiles/excel-import-profiles.json`: outbound field now uses `IgnoreSourceOnly` with the exact reason above and no Unit reference map.
- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CProductImportValidator.cs`: only inbound is mapped/resolved; outbound is not formula-validated, mapped, or projected into the Product row.
- The existing `CProduct_Xlsx_Reader` -> profile detector -> legacy adapter -> `CProductImportValidator` path remains. Confirm continues through `CWarehouseProductImportService` and the existing controller/SP path.
- No schema, migration, SQL procedure, workbook source, inventory code, or permission metadata change was made.

## 6. UOM Resolution Matrix

Live Unit lookup before Confirm returned: `Cái`=266, `Hộp`=267, `Gói`=268, `Chai`=269, `Lon`=270, `Thùng`=271, `Bao`=272, `Kilogram`=273, `Ram giấy`=274, `Pallet`=275.

| Source | Configured mapping | Target resolution | Policy |
| --- | --- | --- | --- |
| Inbound `THUNG` | `Thùng` | Exact Unit reference, ID 271 | Required and persisted |
| Inbound `BAO` | `Bao` | Exact Unit reference, ID 272 | Required and persisted |
| Inbound `HOP` | `Hộp` | Exact Unit reference, ID 267 | Required and persisted |
| Outbound values, including `CHAI`, `LON`, `GOI`, `RAM` | None | No lookup | Ignored with row diagnostic |

The customer workbook has 15 Product rows; its inbound values are 13 `THUNG`, 1 `BAO`, and 1 `HOP`. A synthetic `THUNG`/`CHAI` row resolves to inbound ID 2 and remains valid; a mapped same-unit row also remains valid.

## 7. Tests And Builds

- TDD RED: before production changes, the outbound-only duplicate test failed because outbound UOM was treated as a blocking mismatch.
- GREEN focused tests: 5 inbound/outbound canonical-policy tests passed; original Product fixture Preview test passed; all 27 `ProductExcelImportValidationTests` passed. Total final focused results: 33 passed, 0 failed.
- Release web build succeeded with 0 errors. Existing Telerik licensing and unrelated legacy async warnings remain.
- Debug web build could not copy DLLs because the already-running user app (PID 7096) held `bin/Debug` files. That process was not stopped; live UAT uses an isolated Release build.
- The initial Production-profile run missed the Telerik static-web-asset registration and logged a JS interop error. A full page reload under Development loaded the asset and cleared browser JS errors; a Telerik license watermark remains. The temporary test server has been stopped. No credentials were entered.
- The broad test suite was not run.

## 8. Original Product Preview

Source workbook parser/validator test using customer Category and Unit fixtures: **15 total, 15 valid, 0 invalid**, every Product has a positive inbound Unit ID, outbound Unit ID remains 0, and 15 populated outbound values have `SOURCE_FIELD_IGNORED` diagnostics with the exact reason above. This is not live database Preview evidence.

## 9. Live Confirm

**Deferred to user self-test.** The local app login page was reached at `http://localhost:15075/sys/dang_nhap`; no credentials were entered and no Confirm was submitted. Source inspection showed login calls `CCache_Token.Set_Token`, which can replace the existing token for that user. Per the user's instruction, no login or UI UAT will be performed by the assistant.

## 10. Database Product Verification

**Deferred to user self-test.** Pre-Confirm baseline was Product=0. After the user's Confirm, verify Product rows and each persisted `Don_Vi_Tinh_ID` against inbound UOM. No database write has been made by this work.

## 11. Duplicate Reimport

**Deferred to user self-test.** Expected outcome after Product Confirm: 15 skipped, 0 conflict, Product count unchanged.

## 12. Outbound-Only Change Test

Source-copy fixture prepared at `%TEMP%\Product-Inbound-UOM-UAT-20261010\outbound-only.xlsx`. Only `E4` changed from `CHAI` to `LON`; semantic diff against source is exactly that one cell. UI Preview was not run; user self-test can verify all 15 existing Products are skipped without conflict.

## 13. Inbound-UOM Conflict Test

Source-copy fixture prepared at `%TEMP%\Product-Inbound-UOM-UAT-20261010\inbound-changed.xlsx`. Only `D4` changed from `THUNG` to valid mapped `CHAI`; semantic diff is exactly that one cell. UI Preview was not run; user self-test can verify `SP0001` conflicts because its persisted inbound Unit differs.

## 14. Invalid Inbound Test

Source-copy fixture prepared at `%TEMP%\Product-Inbound-UOM-UAT-20261010\invalid-inbound.xlsx`. Only `D4` changed from `THUNG` to `UNKNOWN_UNIT`; outbound remains valid `CHAI`. Source-level focused test confirms inbound error, Unit ID 0, and no outbound fallback. UI Preview was not run; no Confirm should be attempted for this invalid fixture.

## 15. Database Before/After

Fresh read-only pre-Confirm baseline matched the required gate:

| Table/group | Before |
| --- | ---: |
| Unit / Warehouse / Category / Supplier / Product | 10 / 4 / 5 / 5 / 0 |
| Product-Warehouse relation | 0 |
| Receipt header/raw; issue header/raw | 0/0; 0/0 |
| Inventory daily balance / scope / report state | 0 / 0 / 1 |
| Movement daily / report fence / report scope | 0 / 1 / 0 |
| Current balance / daily aggregate state / snapshot | 0 / 1 / 0 |
| Movement aggregate state / rebuild dead letter / queue | 1 / 0 / 0 |
| Reconciliation result / run / reservation | 0 / 0 / 0 |
| Snapshot bootstrap audit / rebuild dead letter / queue | 0 / 0 / 0 |
| Snapshot fallback log / worker heartbeat | 4 / 0 |

After-state and inventory parity remain unverified until the user's live Confirm and self-test. The read-only precheck made no DB changes; current Product count remains at the observed baseline of 0 as no Confirm was submitted.

## 16. Workbook Hash

Original `docs/test-data/customer-import/04_San_pham_KH.xlsx` SHA-256 before UAT: `61E2642E5F98C4974601932055377D93403F3D1A26945BE6F0573A00CB337807`, matching the prior recorded original hash. Three changed-value test fixtures are copies in `%TEMP%`; the original workbook was not edited. Recheck after the user's self-test if needed.

## 17. Remaining Blockers

- No coding blocker remains from the source/test work. The user will perform live UI testing; no login was performed by the assistant.
- Live Confirm, post-Confirm DB `Unit_ID` verification, duplicate reimport, and three copy-fixture UI Previews remain unverified and are deferred to that self-test.
- No test-DB runtime integration or broad suite was run; focused tests and Release build passed.
