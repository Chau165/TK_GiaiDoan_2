# DB-First Five-Master Import Completion Report

Date: 2026-10-09  
Outcome: **PARTIALLY COMPLETED**  
Target database: `TKS_Thuc_Tap_V11_GiaiDoan2`

The current WMS schema and business behavior remained authoritative. Unit, Warehouse, Category, and Supplier are operational through the existing UI, service, and stored-procedure paths. Product is deliberately not Confirmed because 14 source rows provide two different resolved UOMs for a target with only one `Unit_ID`.

## 1. Current Target Domain Contracts

The entity/column/procedure/required/key/reference/manual-UI contract and per-source-field matrix are in [master-import-db-first-target-contract.md](master-import-db-first-target-contract.md). The current model persists Unit name/note; Warehouse name/note; Category code/name/note; Supplier code/name/note; and Product code/name/Category ID/one Unit ID/note. Product has no Supplier relationship. No customer-only field was assigned to an unrelated target property.

## 2. Excel Field Classification

Every field in the five original workbooks is classified exactly once in the target-contract matrix as `MAP_DIRECT`, `MAP_VALUE`, `MAP_REFERENCE`, `IGNORE_SOURCE_ONLY`, or conditional Product UOM reference. Unknown non-empty fields remain blocking; no fuzzy business-key matching is used.

## 3. MAP_DIRECT Fields

- Warehouse: `Tên kho / bãi` -> `Name`; `Ghi chú` -> `Ghi_Chu`.
- Unit: `Tên gọi` -> `Name`; `Ghi chú` -> `Ghi_Chu`.
- Category: `Mã nhóm` -> `Code`; `Tên nhóm hàng` -> `Name`; `Ghi chú` -> `Ghi_Chu`.
- Supplier: `Mã đối tác` -> `Code`; `Tên NCC` -> `Name`.
- Product: `Mã VT` -> `Code`; `Tên hàng` -> `Name`; `Ghi chú` -> `Ghi_Chu`.

## 4. MAP_VALUE Fields

- Warehouse `Mã kho` uses the configured exact code-to-name map and must agree with `Tên kho / bãi`; Warehouse has no persisted code column.
- Unit `Mã ĐVT` uses the configured exact code-to-name map; Unit has no persisted code column.
- Product UOM representations such as `THUNG` -> `Thùng` and `BAO` -> `Bao` use configured exact mappings before reference resolution.

## 5. MAP_REFERENCE Fields

- Product `Nhóm hàng` resolves the exact Category code to `Loai_San_Pham_ID`.
- Product `ĐVT nhập` and `ĐVT xuất` resolve independently to Unit IDs. The pair is projected only when both resolved IDs are equal; see section 7.
- Live Product Preview resolved the workbook's Category and Unit references; the blocking condition was the different inbound/outbound Unit IDs, not a missing reference.

## 6. IGNORE_SOURCE_ONLY Fields

All populated ignored cells emitted visible `SOURCE_FIELD_IGNORED` diagnostics and were excluded from target projection, persistence, target validation, and duplicate comparison.

- Warehouse: `STT`, `Khu vực`, `Địa chỉ`, `Người phụ trách`, `SĐT`, `Trạng thái`.
- Unit: `Ký hiệu in phiếu`, `Nhóm`, `Hệ số QĐ`, `ĐVT gốc`.
- Category: `Nhóm cha`, `Theo dõi LOT?`, `Theo dõi HSD?`.
- Product: `Quy cách`, `Mã NCC chính`, `Barcode`, `Theo dõi lô`, `Hạn dùng`, `Trọng lượng`, `Ngưng KD?`.
- Supplier: `MST`, `Điện thoại`, `Email`, `Địa chỉ`, `Người liên hệ`, `Điều khoản TT`, `Còn giao dịch?`.

## 7. Product Unit_ID Semantics

The Product form, entity, table, insert procedure, receipt/issue detail models, and receipt/issue procedures expose one catalog-level Product Unit and no transaction UOM or conversion behavior. They do not establish that inbound or outbound UOM is preferred when the two differ. The importer therefore uses the common resolved Unit only when both exact references agree. A mismatch produces `BUSINESS_DECISION_REQUIRED` on both source columns and clears the target Unit projection for that row.

Original Product workbook Preview: 15 rows; 1 valid row (`BAO` and `Bao` both resolve to `Bao`); 14 rows have different resolved inbound/outbound Units and are errors. Confirm was disabled. The one valid row was not imported separately because the workbook Confirm path is batch-atomic. Product remains at 0 rows.

## 8. Dependency Graph

Unit and Category are Product's required independent references. Warehouse and Supplier are independent of Product. Live sequence: pre-existing Unit data, Category, Warehouse, Supplier, then Product Preview. Product Confirm remains pending an explicit UOM decision; Supplier is not a Product dependency in the current model.

## 9. Implementation Changes

- Added profile-driven `IgnoreSourceOnly` classification with a required reason.
- Excluded ignored source values from the legacy adapter's projected row values while preserving row-level diagnostics.
- Added the shared ignored-field diagnostics component to all five master import dialogs.
- Added Product pair-level UOM resolution: common Unit ID projects; differing IDs block with `BUSINESS_DECISION_REQUIRED` and do not project either side.
- Kept existing importer services, repository/controller persistence, and stored procedures. No direct database insert/update was used.
- No schema, migration, stored-procedure, or original-workbook changes were made for this phase.

## 10. Tests and Builds

Passed: 9 focused test cases covering Supplier source-only duplicate behavior; all master workbook readers/current validators; Product unequal/equal UOM projection; ignored canonical-field filtering in the legacy adapter; Category LOT/HSD diagnostics; Unit exact mapping/safe ignores; all five dialogs rendering diagnostics; and the original Product workbook reaching Preview with only ambiguous rows blocked. Both the UI and Data Access projects built successfully with zero errors.

The broad `ImportStructureDetectionTests` class run did not complete because the test host ran out of memory; isolated focused tests passed. Test-DB runtime revalidation/rollback integration tests were not run because `TKS_INTEGRATION_CONNECTION_STRING` was unset in this session. No destructive concurrency or rollback setup was run against the Business DB.

## 11. Warehouse Live Confirm

Original `01_Danh_muc_Kho_KH.xlsx`: Preview 4 total, 4 valid, 0 skipped, 0 errors, 24 ignored-cell diagnostics. Existing UI Confirm succeeded with 4 rows. DB rows verified:

| Name | Note |
|---|---|
| Kho Bình Tân | Kho hàng FMCG chính |
| Kho Cát Lái | Kho gần cảng |
| Kho Đà Nẵng | Đang kiểm kê cuối kỳ |
| Kho Dĩ An | empty |

Only current Warehouse name/note fields were persisted.

## 12. Category Live Confirm

Original `03_Loai_san_pham_KH.xlsx`: Preview 5 total, 5 valid, 0 skipped, 0 errors, 10 LOT/HSD ignored-cell diagnostics. Existing UI Confirm succeeded with 5 rows. DB values verified: `GSU`, `HGD`, `NGK`, `TPK`, `VPP`, with source code/name/note only.

## 13. Supplier Live Confirm

Original `05_Nha_cung_cap_KH.xlsx`: Preview 5 total, 5 valid, 0 skipped, 0 errors, 35 ignored-cell diagnostics. Existing UI Confirm succeeded with 5 rows. DB rows verified for codes `NCC001` through `NCC005`, with the matching source names and empty notes. Tax code, contacts, contact details, payment terms, and status were not persisted.

## 14. Product Live Confirm

Product upload and Preview were run through the UI after Unit and Category data existed. Result: 15 total, 1 valid, 0 skipped, 14 errors, 102 ignored-cell diagnostics. Confirm was disabled and was not attempted. No Product rows were written. Required next step: business owner decides which UOM semantics a single Product `Unit_ID` represents for rows where inbound and outbound units differ, or provides a supported business rule/model change in a separately authorized phase.

## 15. Unit Regression

The original Unit workbook re-Previewed through the UI: 10 total, 0 valid, 10 skipped as duplicates, 0 errors, 30 ignored-cell diagnostics. The existing 10 Unit rows and their notes remained unchanged. This phase did not add another Unit row.

## 16. Duplicate Re-import

| Master | Re-import Preview | Errors | DB count before -> after |
|---|---:|---:|---:|
| Unit | 10 skipped | 0 | 10 -> 10 |
| Warehouse | 4 skipped | 0 | 4 -> 4 |
| Category | 5 skipped | 0 | 5 -> 5 |
| Supplier | 5 skipped | 0 | 5 -> 5 |
| Product | Not applicable; no Product rows were persisted | 14 UOM decisions | 0 -> 0 |

For all-duplicate re-imports, Confirm was disabled because there were no valid rows.

## 17. Ignored-Field and Persisted-Field Conflict Tests

Using temporary copies of the Supplier workbook, without modifying the original:

- Changed only row 5 `Điều khoản TT`: Preview returned 5 duplicate skips and 0 errors; the ignored-field difference did not conflict.
- Changed row 5 `Tên NCC` while keeping business key `NCC001`: Preview returned 1 target-field conflict (`Mã nhà cung cấp 'NCC001' trùng khóa nhưng dữ liệu khác trong hệ thống.`) and 4 duplicate skips. It remained Preview-only; Confirm was not run.

## 18. Database Before/After

Read-only `DB_NAME()` verified the target before the first Confirm and after UAT. Counts:

| Table/group | Before | After |
|---|---:|---:|
| Unit | 10 | 10 |
| Warehouse | 0 | 4 |
| Category | 0 | 5 |
| Supplier | 0 | 5 |
| Product | 0 | 0 |
| Receipt + receipt raw | 0 + 0 | 0 + 0 |
| Issue + issue raw | 0 + 0 | 0 + 0 |
| Inventory daily balance / movement | 0 / 0 | 0 / 0 |
| Current balance / daily snapshot / reservation | 0 / 0 / 0 | 0 / 0 / 0 |
| Snapshot fallback log | 4 | 4 |
| Reconciliation result / run | 0 / 0 | 0 / 0 |
| Snapshot bootstrap audit / worker heartbeat | 0 / 0 | 0 / 0 |
| Current report state / report fence config | 1 / 1 | 1 / 1 |
| Balance / movement aggregate state | 1 / 1 | 1 / 1 |

No transaction or inventory posting occurred. All rows outside the four expected master-data outcomes remained unchanged.

## 19. Original Workbook Hashes

SHA-256 before/after values for the five original workbooks match:

| Original workbook | SHA-256 |
|---|---|
| `01_Danh_muc_Kho_KH.xlsx` | `DF48CA93DA318289438A5F11C4B8E53513CE0F6D34D6AB98385FE71F41B2E2DA` |
| `02_Don_vi_tinh_KH.xlsx` | `95BFB88E4ABF9C1D82235E5AD3E80EEC91AEB8B2835F0631978BE5BC5BA958C6` |
| `03_Loai_san_pham_KH.xlsx` | `7EE3C901936CE56F24B8FD49F5925636E407675D34BEB6D5F78FFFF22B1B6934` |
| `04_San_pham_KH.xlsx` | `61E2642E5F98C4974601932055377D93403F3D1A26945BE6F0573A00CB337807` |
| `05_Nha_cung_cap_KH.xlsx` | `EC2AA0497CE36EFB653EF9935D532AD368D127C06995EF787C881C934821B446` |

Original ZIP final SHA-256: `EC9493F7B6264182F0061C0B7156A8EEF0F0DCEA82C864ADFC13B3247FB94F9A`. No write targeted the ZIP; its pre-UAT hash was not retained in the captured baseline, so ZIP hash equality is not independently asserted.

## 20. Final Master Matrix and Remaining Blockers

| Master | Mapped source columns | Ignored columns | Preview / ignored diagnostics | Confirm | Rows persisted | Duplicate re-import | Result |
|---|---:|---:|---|---|---:|---|---|
| Unit | 3 | 4 | 10 total; 30 diagnostics; 0 errors | Previously completed | 10 pre-existing | 10 skipped | PASS |
| Warehouse | 3 | 6 | 4 valid; 24 diagnostics; 0 errors | Succeeded | 4 | 4 skipped | PASS |
| Category | 3 | 3 | 5 valid; 10 diagnostics; 0 errors | Succeeded | 5 | 5 skipped | PASS |
| Supplier | 2 | 7 | 5 valid; 35 diagnostics; 0 errors | Succeeded | 5 | 5 skipped | PASS |
| Product | 6 candidate fields (two conditional UOM references) | 7 | 15 total; 102 diagnostics; 14 UOM errors | Disabled, not run | 0 | Not applicable | BUSINESS_DECISION_REQUIRED |

Remaining blockers/evidence gaps: Product's single `Unit_ID` has no justified choice for the 14 rows with different mapped inbound/outbound Units; approved Test-DB runtime revalidation/rollback was unavailable because its required connection variable was unset; the broad test class stopped on test-host out-of-memory; and the original ZIP's pre-UAT hash was not retained. No schema/SP change, no original workbook edit, and no Product or inventory write occurred.
