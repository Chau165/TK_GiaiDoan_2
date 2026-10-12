# Excel Import Safe-Loss Implementation Report

Date: 2026-10-09  
Result: **PARTIALLY COMPLETED**. The 84-column source audit and fail-closed policy implementation are complete. The customer workbooks intentionally do not all become Confirmable; transaction persistence remains disabled. Live-database resolution, permission parity, and end-to-end UAT were not performed.

## 1. Phase A Result

The detailed source audit is [excel-import-safe-loss-capability-audit.md](excel-import-safe-loss-capability-audit.md). It classifies 84 workbook column occurrences: 23 `SUPPORTED_DIRECT`, 13 `REFERENCE_MAPPING`, 17 `SAFE_IGNORE`, 28 `MUST_BLOCK`, and 3 `MUST_BLOCK_PENDING_EVIDENCE`. The last category is Warehouse region/address and inbound proposed Location: current source does not prove this physical-site context is semantically dispensable.

Current inventory grain is `(Warehouse, Product)`. Product has one Unit ID. Receipt/issue details persist quantity/price but no transaction UOM, conversion, LOT, manufacture/expiry date, or Location. Posting remains exclusively through the existing document-posting business procedure. No source evidence supports changing that boundary.

## 2. Fields and Conditional Values

- `SAFE_IGNORE`: row sequence, Unit print symbol/group, identity factor `1`, Supplier contact fields, Product weight, Product discontinued `Không`, Supplier active `Có`, inbound type `Mua hàng`, equal ordered/received quantity, and equal requested/issued quantity. Safe decisions carry a row-level reason/value diagnostic. Conditional rules reject any value outside the exact allowed value/equality.
- `MAP_OR_DERIVE`: Unit and Warehouse customer codes map through explicit configured maps and must equal the supplied canonical name in master workbooks. Product/category/supplier/warehouse/unit/document references resolve exactly. Unit codes such as `THUNG` are exact, accent-sensitive map entries; no fuzzy match is used.
- `MUST_BLOCK`: active Warehouse status that is not the default (`Tạm ngưng`), Category/Product LOT or HSD enabled, Product input/output UOM mismatch or conversion, primary-supplier link, barcode, shelf life, Supplier tax/payment terms, inbound invoice/delivery identity and line discrepancy/note/LOT/dates, outbound destination/reason/reference/recipient/note/LOT/HSD/location, and any other configured unsupported nonempty value.
- `MUST_BLOCK_PENDING_EVIDENCE`: populated warehouse region/address and inbound proposed Location remain blocked because their source semantics are not proven safe to lose.

## 3. Policy and Code Changes

- `Utility/Profiles/excel-import-profiles.json`: replaced unreasoned ignored-header arrays with reasoned `safeIgnoreHeaders`; added explicit policies, safe-value/equality conditions, unsupported reasons, exact Unit/Warehouse code-to-name cross-checks, Supplier tax/payment blockers, transaction invoice/delivery/note blockers, and line-number policy.
- `Utility/CImportProfileModels.cs`, `CImportProfileProvider.cs`: added field policy model and validation requiring reasons for safe ignores, blockers, maps, and conditional exceptions.
- `Utility/CWorkbookStructureDetector.cs`: applies profile policies, checks exact mapped references against supplied canonical fields, retains per-value diagnostics for ignored/mapped/blocked values, and continues to emit `UNSUPPORTED_DOMAIN_CAPABILITY` for meaningful unsupported values.
- `Utility/CProfiledWorkbook_Xlsx_Reader.cs`, `CImportLegacyWorksheetAdapter.cs`, the five master worksheet/result models and validators, and `CTransactionImportPreviewService.cs`: propagate diagnostics from detection through the Preview result. The transaction reader/detector now share the injected profile provider.
- `TKS_Thuc_Tap_V11_Data_Access.Tests/ImportStructureDetectionTests.cs`: exercises exact map mismatch, ignored-value reasons, conditional LOT/status behavior, product UOM/weight behavior, inbound quantity mismatch, outbound requested/issued equality, and non-persistable transaction Preview.

Diagnostics are retained in Preview result models. This phase does not add a new UI panel to render every diagnostic; the requirement that Preview/report be able to inspect the decision is met at the model/API boundary.

## 4. Fixture Status Before / After

“Eligible” means validator Preview `Can_Confirm`, not successful database persistence or UAT. Before is the pre-phase profile/test baseline; after is the current fixture-backed test result.

| Workbook | Before | After | Remaining reason |
| --- | --- | --- | --- |
| Warehouse | Blocked | Blocked | Suspended status; populated region/address fail closed pending evidence. Active status only is conditionally compatible. |
| Unit | Eligible | Eligible | Fixture has exact code/name maps, factor 1, blank base UOM; Preview diagnostics record safe values. |
| Category | Blocked | Blocked | One or more `LOT/HSD = Có` values. `Không` is conditionally compatible. |
| Product | Blocked | Blocked | Different input/output UOMs, conversion, supplier link, barcode, LOT/shelf-life semantics remain unsupported. Weight is safely ignored. |
| Supplier | Eligible | Blocked | Tax code and payment terms were previously ignored; they now produce capability blockers. Contact fields and active `Có` are diagnostic safe-ignores. |
| Inbound | Preview only; persistence blocked | Preview only; persistence blocked | Ordered quantity 40 vs received 39, invoice/delivery, line note, LOT/NSX/HSD and proposed location block. Equal quantity rows are conditionally safe. |
| Outbound | Preview only; persistence blocked | Preview only; persistence blocked | Destination/reason/reference/recipient, line note, LOT/HSD/location block. Requested and issued quantity match in fixture. |

Inbound and Outbound remain `PersistenceBlocked == true` and `Can_Confirm == false` regardless of fewer compatibility diagnostics. No receipt/issue persistence, stock query wiring, concurrency validation, or direct inventory update was added.

## 5. Required Output Matrix

The table below is the compact implementation crosswalk for every workbook column. The audit report contains the field-by-field business-impact rationale. Evidence keys `S1-S6` and workbook keys `W1-W7` are defined there. Test IDs are listed after the table. `BIEN` means `BlockIfNonEmpty`; a value shown as safe is an exact exception, not a fuzzy match.

| Workbook | Customer Field | Example Value | Canonical Meaning | Current Domain Support | Business Usage | Loss Impact | Classification | Severity | Policy | Source Evidence | Test Evidence |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 01 Warehouse | STT | 1 | Display row order | Source row retained | Diagnostics only | None | SAFE_IGNORE | P3 | Header safe-ignore | S6,W1 | T-WH |
| 01 Warehouse | Mã kho | KHO-HCM-01 | Warehouse key | Exact map only; not stored | Site reference | Wrong site binding | REFERENCE_MAPPING | P1 | Exact map + name match | S1,W1 | T-WH,T-MAP |
| 01 Warehouse | Tên kho / bãi | Kho Bình Tân | Warehouse name | Direct column | Master/reference display | Identity lost if omitted | SUPPORTED_DIRECT | P1 | Required direct | S1,W1 | T-WH |
| 01 Warehouse | Khu vực | nonempty | Site region | Absent | Physical/routing context unresolved | Site distinction lost | MUST_BLOCK_PENDING_EVIDENCE | P1 | BIEN | S1,W1 | T-WH |
| 01 Warehouse | Địa chỉ | nonempty | Physical address | Absent | Site identity unresolved | Physical-site meaning lost | MUST_BLOCK_PENDING_EVIDENCE | P1 | BIEN | S1,W1 | T-WH |
| 01 Warehouse | Người phụ trách | nonempty | Contact person | No current field/consumer | No posting/report use found | Contact detail lost only | SAFE_IGNORE | P2 | Header safe-ignore + diagnostic | S1,S4,W1 | T-WH |
| 01 Warehouse | SĐT | nonempty | Contact phone | No current field/consumer | No posting/report use found | Contact detail lost only | SAFE_IGNORE | P2 | Header safe-ignore + diagnostic | S1,S4,W1 | T-WH |
| 01 Warehouse | Trạng thái | Đang dùng/Tạm ngưng | Active state | No active/status support | Posting can still use a suspended site | Inactive state lost | MUST_BLOCK | P1 | `Đang dùng` exact safe value; else BIEN | S1,S4,W1 | T-WH |
| 01 Warehouse | Ghi chú | nonempty | Warehouse note | Direct column | Master note | Note lost | SUPPORTED_DIRECT | P2 | Direct | S1,W1 | T-WH |
| 02 Unit | Mã ĐVT | CAI | Unit business code | Exact map only; not stored | Resolve Unit name | Wrong UOM binding | REFERENCE_MAPPING | P1 | Exact map + name match | S1,W2 | T-UNIT,T-MAP |
| 02 Unit | Tên gọi | Cái | Unit name | Direct column | Product Unit reference | UOM identity lost | SUPPORTED_DIRECT | P1 | Required direct | S1,W2 | T-UNIT |
| 02 Unit | Ký hiệu in phiếu | Kg | Print symbol | Absent | Current output uses Unit name | Cosmetic symbol lost | SAFE_IGNORE | P2 | Header safe-ignore + diagnostic | S1,S5,W2 | T-UNIT |
| 02 Unit | Nhóm | populated | Unit grouping | Absent | No quantity/posting consumer | Group label lost | SAFE_IGNORE | P2 | Header safe-ignore + diagnostic | S1,S4,W2 | T-UNIT |
| 02 Unit | Hệ số QĐ | 1 | Conversion factor | Identity only | Non-identity changes quantity | No loss for identity only | SAFE_IGNORE | P0 | Exact 1/1.0/1.00; else BIEN | S1-S5,W2 | T-UNIT |
| 02 Unit | ĐVT gốc | blank | Base UOM | Absent | Conversion relationship | Nonblank base semantics lost | MUST_BLOCK | P0 | BIEN; blank inert | S1-S5,W2 | T-UNIT |
| 02 Unit | Ghi chú | `1 ram = 500 tờ` | Unit note | Direct text only | Does not convert quantity | Note stored; no conversion effect | SUPPORTED_DIRECT | P2 | Direct; Product conversion still blocked | S1,W2 | T-UNIT |
| 03 Category | Mã nhóm | LOT | Category key | Direct column | Product reference | Identity lost | SUPPORTED_DIRECT | P1 | Required direct | S1,W3 | T-CAT |
| 03 Category | Tên nhóm hàng | populated | Category name | Direct column | Product display | Name lost | SUPPORTED_DIRECT | P1 | Required direct | S1,W3 | T-CAT |
| 03 Category | Nhóm cha | blank | Parent category | Absent | Hierarchy | Parent relation lost | MUST_BLOCK | P1 | BIEN; blank inert | S1,W3 | T-CAT |
| 03 Category | Theo dõi LOT? | Có/Không | Lot policy | Absent | Inventory traceability | Lot identity lost | MUST_BLOCK | P0 | `Không` exact safe value; else BIEN | S1-S4,W3 | T-CAT |
| 03 Category | Theo dõi HSD? | Có/Không | Expiry policy | Absent | Expiry control/report | Expiry semantics lost | MUST_BLOCK | P0 | `Không` exact safe value; else BIEN | S1-S4,W3 | T-CAT |
| 03 Category | Ghi chú | nonempty | Category note | Direct column | Master note | Note lost | SUPPORTED_DIRECT | P2 | Direct | S1,W3 | T-CAT |
| 04 Product | Mã VT | SP0001 | Product key | Direct column | Identity and posting | Wrong item | SUPPORTED_DIRECT | P0 | Required direct | S1-S4,W4 | T-PROD |
| 04 Product | Tên hàng | Nước suối tinh khiết 500ml | Product name | Direct column | Documents/reports | Name lost | SUPPORTED_DIRECT | P1 | Required direct | S1-S4,W4 | T-PROD |
| 04 Product | Nhóm hàng | category code | Category reference | Exact existing lookup | Product identity | Wrong category | REFERENCE_MAPPING | P1 | Exact reference | S1,W4 | T-PROD |
| 04 Product | ĐVT nhập | THUNG | Product Unit | Exact `THUNG` map | Quantity interpretation | Wrong quantity UOM | REFERENCE_MAPPING | P0 | Exact map/reference | S1,S5,W4 | T-PROD,T-MAP |
| 04 Product | ĐVT xuất | CHAI | Issue UOM | Product has one Unit ID | Issue quantity meaning | Conversion/stock error | MUST_BLOCK | P0 | Different Unit ID blocks | S1-S5,W4 | T-PROD |
| 04 Product | Quy cách | 24 chai/thùng | Conversion | Absent | Quantity conversion | Stock quantity changes | MUST_BLOCK | P0 | BIEN | S1-S5,W4 | T-PROD |
| 04 Product | Mã NCC chính | NCC001 | Primary supplier link | Absent | Purchasing source | Relationship lost | MUST_BLOCK | P1 | BIEN | S1,W4 | T-PROD |
| 04 Product | Barcode | 8938501000011 | Alternate item identity | Absent | No scan-to-Product resolution | Scan identity lost | MUST_BLOCK | P1 | BIEN | S1,S6,W4 | T-PROD |
| 04 Product | Theo dõi lô | Có/Không | Lot policy | Absent | Stock traceability | Lot semantics lost | MUST_BLOCK | P0 | `Không` exact safe value; else BIEN | S1-S4,W4 | T-PROD |
| 04 Product | Hạn dùng (tháng) | populated | Shelf life | Absent | Expiry behavior | Expiry state lost | MUST_BLOCK | P0 | BIEN | S1-S4,W4 | T-PROD |
| 04 Product | Trọng lượng (kg) | 0.5 | Product weight | No field/consumer found | No identity/posting/inventory/report use | No current WMS behavior lost | SAFE_IGNORE | P2 | Field SafeIgnore + diagnostic | S1,S4,S6,W4 | T-PROD |
| 04 Product | Ngưng KD? | Không | Active state | No status column | Active create default only | `Không` matches default | SAFE_IGNORE | P1 | Exact `Không`; else BIEN | S1,W4 | T-PROD |
| 04 Product | Ghi chú | nonempty | Product note | Direct column | Master note | Note lost | SUPPORTED_DIRECT | P2 | Direct | S1,W4 | T-PROD |
| 05 Supplier | Mã đối tác | NCC001 | Supplier key | Direct column | Receipt reference | Wrong supplier | SUPPORTED_DIRECT | P0 | Required direct | S1,S2,W5 | T-SUP |
| 05 Supplier | Tên NCC | populated | Supplier name | Direct column | Receipt display | Name lost | SUPPORTED_DIRECT | P1 | Required direct | S1,S2,W5 | T-SUP |
| 05 Supplier | MST | populated | Tax identity | Absent | Supplier legal identity | Tax data lost | MUST_BLOCK | P1 | BIEN | S1,W5 | T-SUP |
| 05 Supplier | Điện thoại | populated | Contact phone | No field/consumer found | No receipt/posting use | Contact lost only | SAFE_IGNORE | P2 | Header safe-ignore + diagnostic | S1,S4,S6,W5 | T-SUP |
| 05 Supplier | Email | populated | Contact email | No field/consumer found | No receipt/posting use | Contact lost only | SAFE_IGNORE | P2 | Header safe-ignore + diagnostic | S1,S4,S6,W5 | T-SUP |
| 05 Supplier | Địa chỉ | populated | Supplier address | No field/consumer found | No receipt/posting use | Contact lost only | SAFE_IGNORE | P2 | Header safe-ignore + diagnostic | S1,S4,S6,W5 | T-SUP |
| 05 Supplier | Người liên hệ | populated | Contact person | No field/consumer found | No receipt/posting use | Contact lost only | SAFE_IGNORE | P2 | Header safe-ignore + diagnostic | S1,S4,S6,W5 | T-SUP |
| 05 Supplier | Điều khoản TT | populated | Payment terms | Absent | Business obligations | Terms lost | MUST_BLOCK | P1 | BIEN | S1,W5 | T-SUP |
| 05 Supplier | Còn giao dịch? | Có | Active state | No status column | Create defaults active | Exact `Có` matches default | SAFE_IGNORE | P1 | Exact `Có`; else BIEN | S1,W5 | T-SUP |
| 06 Inbound/Header | Số CT | PN260901 | Receipt key | Direct | Header/detail grouping | Document identity lost | SUPPORTED_DIRECT | P0 | Required/group key | S2,W6 | T-IN |
| 06 Inbound/Header | Ngày CT | Sep 2026 | Receipt date | Direct DATE | Posting/report date | Period changes | SUPPORTED_DIRECT | P0 | Deterministic date parse | S2,S4,W6 | T-IN |
| 06 Inbound/Header | Kho nhận | KHO-HCM-01 | Warehouse ref | Exact configured map | Stock scope | Wrong stock scope | REFERENCE_MAPPING | P0 | Exact map/reference | S1-S4,W6 | T-IN |
| 06 Inbound/Header | Mã NCC | supplier code | Supplier ref | Exact existing lookup | Receipt identity | Wrong supplier | REFERENCE_MAPPING | P0 | Exact reference | S1,S2,W6 | T-IN |
| 06 Inbound/Header | Số hóa đơn | populated | Invoice trace | Absent | Accounting trace | Reference lost | MUST_BLOCK | P1 | BIEN | S2,W6 | T-IN |
| 06 Inbound/Header | Loại nhập | Mua hàng | Receipt type | Only current receipt behavior | Posting path | No loss for exact type | SAFE_IGNORE | P1 | Exact safe value; else BIEN | S2,S4,W6 | T-IN |
| 06 Inbound/Header | Người giao | populated | Delivery accountability | Absent | Receipt trace | Accountability lost | MUST_BLOCK | P1 | BIEN | S2,S5,W6 | T-IN |
| 06 Inbound/Header | Ghi chú | populated | Header note | Direct | Document note | Note lost | SUPPORTED_DIRECT | P2 | Direct | S2,W6 | T-IN |
| 06 Inbound/Detail | Số CT | PN260901 | Header reference | Exact key join | Detail ownership | Orphan/misgrouped line | REFERENCE_MAPPING | P0 | Exact join | S2,W6 | T-IN |
| 06 Inbound/Detail | Dòng | 1 | Source line order | Preserved in Preview | Ordering only | None for current Preview | SAFE_IGNORE | P3 | Field SafeIgnore + diagnostic | S5,W6 | T-IN |
| 06 Inbound/Detail | Mã VT | SP0001 | Product ref | Exact existing lookup | Posting key | Wrong Product | REFERENCE_MAPPING | P0 | Exact reference | S1,S2,S4,W6 | T-IN |
| 06 Inbound/Detail | Tên hàng | product name | Display/check | Available in Preview | Product cross-check | Name mismatch risk | SUPPORTED_DIRECT | P1 | Preserve/check | S5,W6 | T-IN |
| 06 Inbound/Detail | ĐVT | THUNG | Transaction UOM | Exact map; must match Product UOM | Quantity interpretation | UOM quantity error | REFERENCE_MAPPING | P0 | Exact map + Product match | S1,S2,S5,W6 | T-IN |
| 06 Inbound/Detail | SL chứng từ | 40 | Ordered quantity | Not separately stored | Compare to received | Mismatch lost | MUST_BLOCK | P0 | BIEN; safe only equals actual | S2,S4,W6 | T-IN |
| 06 Inbound/Detail | SL thực nhận | 39 | Receipt quantity | Direct | Posting/current balance | Inventory quantity changes | SUPPORTED_DIRECT | P0 | Required positive quantity | S2,S4,W6 | T-IN |
| 06 Inbound/Detail | Số lô | LOT-NS-0901 | Lot identity | Absent | Inventory traceability | Lot state lost | MUST_BLOCK | P0 | BIEN | S2-S4,W6 | T-IN |
| 06 Inbound/Detail | NSX | date | Manufacture date | Absent | Lot lifecycle | Date lost | MUST_BLOCK | P0 | BIEN | S2-S4,W6 | T-IN |
| 06 Inbound/Detail | HSD | date | Expiry date | Absent | Expiry control | Expiry lost | MUST_BLOCK | P0 | BIEN | S2-S4,W6 | T-IN |
| 06 Inbound/Detail | Đơn giá | populated | Receipt price | Direct | Value calculation | Value lost | SUPPORTED_DIRECT | P1 | Existing price validation | S2,W6 | T-IN |
| 06 Inbound/Detail | Vị trí đề nghị | A-01-01 | Putaway location | Absent; semantics not proven | Possible operational putaway | Location lost | MUST_BLOCK_PENDING_EVIDENCE | P0 | BIEN | S2-S4,W6 | T-IN |
| 06 Inbound/Detail | Ghi chú | Thiếu 1 thùng so với chứng từ | Line discrepancy | Absent | Receiving decision/audit | Discrepancy lost | MUST_BLOCK | P1 | BIEN | S2,S5,W6 | T-IN |
| 07 Outbound/Header | Số PX | PX260901 | Issue key | Direct | Header/detail grouping | Document identity lost | SUPPORTED_DIRECT | P0 | Required/group key | S2,W7 | T-OUT |
| 07 Outbound/Header | Ngày xuất | Sep 2026 | Issue date | Direct DATE | Posting/report date | Period changes | SUPPORTED_DIRECT | P0 | Deterministic date parse | S2,S4,W7 | T-OUT |
| 07 Outbound/Header | Kho xuất | KHO-HCM-01 | Warehouse ref | Exact configured map | Availability/posting scope | Wrong stock scope | REFERENCE_MAPPING | P0 | Exact map/reference | S1-S4,W7 | T-OUT |
| 07 Outbound/Header | Đơn vị nhận | populated | Destination | Absent | Issue intent | Destination lost | MUST_BLOCK | P1 | BIEN | S2,S5,W7 | T-OUT |
| 07 Outbound/Header | Lý do xuất | internal use | Issue reason | Absent | Business classification | Reason lost | MUST_BLOCK | P1 | BIEN | S2,S5,W7 | T-OUT |
| 07 Outbound/Header | Tham chiếu | populated | External reference | Absent | Audit trace | Reference lost | MUST_BLOCK | P1 | BIEN | S2,W7 | T-OUT |
| 07 Outbound/Header | Người nhận | populated | Recipient | Absent | Accountability | Recipient lost | MUST_BLOCK | P1 | BIEN | S2,S5,W7 | T-OUT |
| 07 Outbound/Header | Ghi chú | Văn phòng phẩm | Header note | Direct | Document note | Note lost | SUPPORTED_DIRECT | P2 | Direct | S2,W7 | T-OUT |
| 07 Outbound/Detail | Số PX | PX260901 | Header reference | Exact key join | Detail ownership | Misgrouped line | REFERENCE_MAPPING | P0 | Exact join | S2,W7 | T-OUT |
| 07 Outbound/Detail | Dòng | 1 | Source line order | Preserved in Preview | Ordering only | None for current Preview | SAFE_IGNORE | P3 | Field SafeIgnore + diagnostic | S5,W7 | T-OUT |
| 07 Outbound/Detail | Mã hàng | SP0001 | Product ref | Exact existing lookup | Availability/posting key | Wrong Product | REFERENCE_MAPPING | P0 | Exact reference | S1,S2,S4,W7 | T-OUT |
| 07 Outbound/Detail | Tên hàng hóa | product name | Display/check | Available in Preview | Product cross-check | Name mismatch risk | SUPPORTED_DIRECT | P1 | Preserve/check | S5,W7 | T-OUT |
| 07 Outbound/Detail | ĐVT | unit code/name | Transaction UOM | Exact map; must equal Product UOM | Quantity interpretation | UOM quantity error | REFERENCE_MAPPING | P0 | Exact map + Product match | S1,S2,S5,W7 | T-OUT |
| 07 Outbound/Detail | SL yêu cầu | 25 | Requested quantity | Same as actual in fixture | Issue control | Difference would change intent | SAFE_IGNORE | P0 | Safe only if equals `SL xuất`; else BIEN | S2,S4,W7 | T-OUT |
| 07 Outbound/Detail | SL xuất | 25 | Issue quantity | Direct | Stock validation/posting | Inventory quantity changes | SUPPORTED_DIRECT | P0 | Required positive; persistence blocked | S2-S4,W7 | T-OUT |
| 07 Outbound/Detail | LOT xuất | populated | Lot selection | Absent | Stock traceability | Lot allocation lost | MUST_BLOCK | P0 | BIEN | S2-S4,W7 | T-OUT |
| 07 Outbound/Detail | HSD | date | Expiry selection | Absent | Expiry control | Expiry selection lost | MUST_BLOCK | P0 | BIEN | S2-S4,W7 | T-OUT |
| 07 Outbound/Detail | Vị trí lấy | A-01-01 | Pick location | Absent | Picking/availability | Wrong/missing location | MUST_BLOCK | P0 | BIEN | S2-S4,W7 | T-OUT |
| 07 Outbound/Detail | Ghi chú | Không quản lý LOT/HSD | Line note | Absent | Operational statement | Meaning/conflict lost | MUST_BLOCK | P1 | BIEN | S2-S5,W7 | T-OUT |

Test IDs:

| ID | Test evidence |
| --- | --- |
| T-WH / T-UNIT / T-CAT / T-SUP | `ImportStructureDetectionTests.Customer_master_workbooks_flow_through_profiled_readers_and_current_validators`; test asserts preview outcomes and representative policy diagnostics. |
| T-UNIT | `Unit_fixture_uses_exact_code_mapping_and_audited_safe_ignores`. |
| T-CAT | `Category_fixture_blocks_only_unrepresentable_lot_tracking_values`. |
| T-PROD | `Customer_product_workbook_reaches_preview_with_sample_category_and_unit_references`. |
| T-MAP | `Unit_code_mapping_must_exactly_match_the_supplied_unit_name`; exact map regression remains accent-sensitive (`Cuốn != Cuộn`). |
| T-IN / T-OUT | `Customer_transaction_workbooks_reach_grouped_preview_without_enabling_confirm`; asserts blocked inbound ordered/received mismatch and safe equality for outbound quantities, with persistence still blocked. |
| T-DOC | `Customer_document_workbooks_detect_header_and_detail_roles`. |

The table's test IDs indicate fixture-level Preview coverage, not an assertion for every direct field. Live DB reference identity is unverified.

## 6. Verification

- TDD RED: before adding `PolicyDiagnostics` to the result models, the focused test failed to compile on the missing properties.
- Focused GREEN: `dotnet test ... --filter "FullyQualifiedName~ImportStructureDetectionTests|FullyQualifiedName~ImportValueMappingTests|FullyQualifiedName~TransactionImportPreviewTests"` passed **30/30**.
- Expanded import regression: detector/mapping/transaction preview, five master validators, legacy header checks, and parser compatibility passed **116/116**.
- Web build: `dotnet build TKS_Thuc_Tap_V11_Web/TKS_Thuc_Tap_V11_Web.csproj --no-restore -v:q -clp:ErrorsOnly` succeeded with **0 errors and 24 warnings**; warnings were not triaged as part of this phase.
- JSON profile parsed successfully with PowerShell `ConvertFrom-Json`.
- Coverage percentage is **not measured**; no coverage collector/configuration was used for the focused run.

## 7. Database and Workbook No-Change Proof

- No database connection, SQL execution, migration, schema/procedure edit, or live data mutation was performed in this phase.
- `Database/WarehouseModule.Schema.sql` and `Database/WarehouseModule.Procedures.sql` were already dirty before this phase began and remained byte-identical to phase start. SHA-256: Schema `62B20B66E7581C391A41B1CEE5644E70C525B2BAC460222C6DD7B6222AEFDD60`; Procedures `75ABD0B4A15445F57D983E5D6061E5DE54E44C5672E16C519C7FBAE880B5A377`.
- All seven customer workbook originals remained byte-identical to phase start. No workbook was opened for write or saved.

| Customer workbook | SHA-256 (phase start = final) |
| --- | --- |
| `01_Danh_muc_Kho_KH.xlsx` | `DF48CA93DA318289438A5F11C4B8E53513CE0F6D34D6AB98385FE71F41B2E2DA` |
| `02_Don_vi_tinh_KH.xlsx` | `95BFB88E4ABF9C1D82235E5AD3E80EEC91AEB8B2835F0631978BE5BC5BA958C6` |
| `03_Loai_san_pham_KH.xlsx` | `7EE3C901936CE56F24B8FD49F5925636E407675D34BEB6D5F78FFFF22B1B6934` |
| `04_San_pham_KH.xlsx` | `61E2642E5F98C4974601932055377D93403F3D1A26945BE6F0573A00CB337807` |
| `05_Nha_cung_cap_KH.xlsx` | `EC2AA0497CE36EFB653EF9935D532AD368D127C06995EF787C881C934821B446` |
| `06_Nhap_kho_Thang_09_2026_KH.xlsx` | `11689B98548FA3E33180725370F42B56211A28AC3D988289F6C7AA7CF176BAE7` |
| `07_Xuat_kho_Thang_09_2026_KH.xlsx` | `9823C5138226D3843D93900BDFFD707D31734B68A0907418086D889706787B07` |

## 8. Remaining Risks / Gates

- Current configured maps prove code-to-name semantics only; resolving IDs against a real customer database has not been done.
- `Warehouse.Region`, `Warehouse.Address`, and inbound proposed `Location` remain blocked pending business-owner evidence.
- Safe-ignored contact/weight values are lost from WMS; the decision is bounded to current checked-in source and must be revisited if a consumer, report, or workflow is added.
- Transaction persistence remains deliberately disabled until full-field semantics can be represented and an atomic business posting path, current-stock revalidation, and database/UAT gates are separately approved.
- No changes were made to database schema, stored procedures, customer workbook originals, posting logic, inventory tables, or direct SQL write paths by this phase.
