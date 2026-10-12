# Excel Import Safe-Loss Capability Audit

Date: 2026-10-09  
Scope: the seven supplied customer XLSX fixtures and the checked-in Warehouse WMS source. This is a source audit and policy design; it does not establish live database parity or production readiness.

## Executive Summary

All 84 source-column occurrences across the seven source sheets were classified. The source proves the WMS inventory identity is **Warehouse + Product**; it has one product UOM and no lot, expiry, or location dimension. The current document tables store only document identity/date, warehouse/supplier references, note, product, quantity, and unit price. Inbound/outbound Excel persistence therefore remains blocked.

| Classification | Column occurrences | Decision |
| --- | ---: | --- |
| SUPPORTED_DIRECT | 23 | Existing fields can be represented directly or as dates/quantities already accepted by the current validators. |
| REFERENCE_MAPPING | 13 | Exact codes/keys can resolve to an existing master or parent document; no fuzzy matching and no customer-code persistence. |
| SAFE_IGNORE | 17 | Only the observed value or explicitly safe header/value/equality condition is ignored; every nonblank decision produces a Preview diagnostic. |
| MUST_BLOCK | 28 | Unsupported business meaning is present or must block if supplied. |
| MUST_BLOCK_PENDING_EVIDENCE | 3 | Warehouse region/address and inbound proposed Location are operational site context; source does not prove they are semantically dispensable. |
| **Total** | **84** | Workbook-specific source-column occurrences, not unique canonical fields. |

Master fixture outcome after policy: Unit remains validator-eligible; Warehouse, Category, Product, and Supplier remain blocked. Supplier changes from the prior silently-permissive preview to blocked because tax code and payment terms are present. Transaction Preview remains non-persistable; the inbound ordered/received mismatch is now blocked, while outbound requested quantity is safe only where exactly equal to issued quantity.

## Source Evidence

Evidence keys used in the matrix:

| Key | Source and finding |
| --- | --- |
| S1 | `Database/WarehouseModule.Schema.sql:38-98`: Unit stores name/note; Category code/name/note; Product code/name/category ID/one `Don_Vi_Tinh_ID`/note; Supplier code/name/note; Warehouse name/note. No master status, code for Warehouse/Unit, address, contact, tax, payment-term, barcode, weight, conversion, parent, or lot flag column. |
| S2 | `Database/WarehouseModule.Schema.sql:111-171`: receipt/issue headers and details. Receipt header has document no, warehouse, supplier, date, posted state, note; issue header has document no, warehouse, date, posted state, note; details store product, quantity, price. No invoice, delivery/recipient/reason/reference, detail note, transaction UOM, lot/date, or location. |
| S3 | `Database/WarehouseModule.Schema.sql:227-261,367-428,798-810,1028-1046`: current balance, snapshot, movement, daily balance, reservation, and reconciliation data use warehouse/product keys; none has Lot or Location identity. |
| S4 | `Database/WarehouseModule.Procedures.sql:4553-4858`: `sp_XNK_Document_Post` aggregates receipt/issue quantities by `(Kho_ID, San_Pham_ID)` and applies posting/current-balance rules. `:6603` starts the current-stock report; `:7541` starts movement rebuild. Snapshot rebuild spans `:1049-1271`. Posting logic is the inventory write path and was not changed. |
| S5 | `CWarehouseDocumentModels.cs:5-52`: current document/detail model carries header refs/date/status/note and line product/display UOM/quantity/price; it has no transaction UOM conversion, Lot, expiry, or Location. `CTransactionImportPreviewService.cs:315-347` maps a source unit exactly and rejects a unit different from the product's one unit reference. |
| S6 | Read-only search of the checked-in schema/procedure source for `Lot_Code`, `Lot_ID`, `Manufacture_Date`, `Expiry_Date`, `HSD`, `NSX`, `Location_ID`, `Vi_Tri`, `Barcode`, `Weight_Kg`, `Conversion_Factor`, and `Base_Unit` returned no matches. Negative search is supporting evidence; the concrete schemas and posting grain above are primary evidence. |
| W1-W7 | Original workbooks under `docs/test-data/customer-import/`; headers and populated cells were read without writing the files. Fixture behavior is covered by `ImportStructureDetectionTests` and `TransactionImportPreviewTests`. |

## Inventory and UOM Boundary

- Current on-hand key: `(Kho_ID, San_Pham_ID)` (`S3`). Snapshots, movement, and daily balance retain the same business grain. Reservations and reconciliation also resolve warehouse/product, not Lot/Location.
- `tbl_DM_San_Pham` contains exactly one `Don_Vi_Tinh_ID` (`S1`). Receipt and issue detail store a quantity but no UOM (`S2`). The detail model's unit name is a display projection, not a persisted conversion relationship (`S5`).
- A product with `ĐVT nhập != ĐVT xuất` plus a conversion such as `24 chai/thùng` cannot be represented correctly. Dropping the output UOM/conversion can change quantity semantics; these fields remain blocked.
- LOT, manufacture/expiry dates, and locations cannot be attached to balance, reservation, movement, snapshot, or posting rows. They remain blocked whenever populated. No inventory import or direct balance update was added.
- Customer warehouse/unit codes are not database IDs and are never written. The configured exact map must resolve and, for master Warehouse/Unit, agree with the supplied canonical name.

## Per-Workbook Field Matrix

`SAFE_IGNORE` means safe for the observed value and current WMS behavior only. Conditional `BlockIfNonEmpty` values outside the explicit exception remain blocked. `MUST_BLOCK_PENDING_EVIDENCE` is fail-closed, not a suggestion to discard.

### 01_Danh_muc_Kho_KH.xlsx (9 columns)

| Customer field | Example | Support / business usage and loss impact | Classification | Severity | Policy |
| --- | --- | --- | --- | --- | --- |
| STT | 1 | Display sequence only; physical source row is retained for diagnostics. | SAFE_IGNORE | P3 | Safe-ignore header with reason. |
| Mã kho | KHO-HCM-01 | No Warehouse code column. Exact map to supplied name prevents binding the wrong site; source code is not stored. | REFERENCE_MAPPING | P1 | Exact `Warehouse` map + name cross-check; unknown/mismatch blocks. |
| Tên kho / bãi | Kho Bình Tân | Stored as Warehouse name and used by master/reference display. | SUPPORTED_DIRECT | P1 | Required direct field. |
| Khu vực | populated | No region field. Could distinguish physical/routing sites; source gives no proof this can be discarded. | MUST_BLOCK_PENDING_EVIDENCE | P1 | Block when nonempty. |
| Địa chỉ | populated | No address field. Physical-site identity/routing impact is unresolved. | MUST_BLOCK_PENDING_EVIDENCE | P1 | Block when nonempty. |
| Người phụ trách | populated | No field or source consumer in current warehouse posting/report flow; current manual master flow does not capture it. | SAFE_IGNORE | P2 | Reasoned safe-ignore header; diagnostic per value. |
| SĐT | populated | No field or source consumer in current warehouse posting/report flow; current manual master flow does not capture it. | SAFE_IGNORE | P2 | Reasoned safe-ignore header; diagnostic per value. |
| Trạng thái | Đang dùng / Tạm ngưng | No active/status behavior. A suspended warehouse could still receive/issue stock if imported as active. | MUST_BLOCK | P1 | Only exact `Đang dùng` is conditionally compatible; `Tạm ngưng` blocks. |
| Ghi chú | populated | Stored directly on Warehouse. | SUPPORTED_DIRECT | P2 | Direct field. |

### 02_Don_vi_tinh_KH.xlsx (7 columns)

| Customer field | Example | Support / business usage and loss impact | Classification | Severity | Policy |
| --- | --- | --- | --- | --- | --- |
| Mã ĐVT | CAI | No Unit code column. Exact map to supplied name; code is not stored. | REFERENCE_MAPPING | P1 | Exact `Unit` map + name cross-check; unknown/mismatch blocks. |
| Tên gọi | Cái | Stored as the unique Unit name and referenced by Product. | SUPPORTED_DIRECT | P1 | Required direct field. |
| Ký hiệu in phiếu | Kg | No symbol column or rendering behavior; current detail/report displays Unit name. | SAFE_IGNORE | P2 | Reasoned safe-ignore header; diagnostic per value. |
| Nhóm | populated | No unit-group relation or consumer in current quantity/posting domain. | SAFE_IGNORE | P2 | Reasoned safe-ignore header; diagnostic per value. |
| Hệ số QĐ | 1 | Identity factor does not alter quantity; non-identity conversion would. | SAFE_IGNORE | P0 | Exact identity values only; any other nonempty value blocks. |
| ĐVT gốc | blank | No base-unit relationship. A populated base unit affects conversion semantics. | MUST_BLOCK | P0 | Blank is inert; block if populated. |
| Ghi chú | `1 ram = 500 tờ` | Stored as text only. It does not create conversion logic and does not make mismatched Product UOMs safe. | SUPPORTED_DIRECT | P2 | Persist note as note; conversion remains separately blocked. |

### 03_Loai_san_pham_KH.xlsx (6 columns)

| Customer field | Example | Support / business usage and loss impact | Classification | Severity | Policy |
| --- | --- | --- | --- | --- | --- |
| Mã nhóm | LOT | Stored Category key referenced by Product. | SUPPORTED_DIRECT | P1 | Direct field. |
| Tên nhóm hàng | populated | Stored Category name and used in display. | SUPPORTED_DIRECT | P1 | Direct field. |
| Nhóm cha | blank | No category hierarchy. A populated parent would lose grouping semantics. | MUST_BLOCK | P1 | Blank is inert; block if populated. |
| Theo dõi LOT? | Có / Không | No lot identity in Product/receipt/issue/inventory. Enabling it changes traceability and stock grain. | MUST_BLOCK | P0 | `Không` is conditionally compatible; `Có` blocks. |
| Theo dõi HSD? | Có / Không | No expiry state in Product/receipt/issue/inventory/report. | MUST_BLOCK | P0 | `Không` is conditionally compatible; `Có` blocks. |
| Ghi chú | populated | Stored directly on Category. | SUPPORTED_DIRECT | P2 | Direct field. |

### 04_San_pham_KH.xlsx (13 columns)

| Customer field | Example | Support / business usage and loss impact | Classification | Severity | Policy |
| --- | --- | --- | --- | --- | --- |
| Mã VT | SP0001 | Product business key. | SUPPORTED_DIRECT | P0 | Required direct field; duplicate rules unchanged. |
| Tên hàng | Nước suối tinh khiết 500ml | Stored Product name and displayed in documents/reports. | SUPPORTED_DIRECT | P1 | Required direct field. |
| Nhóm hàng | category code | Resolves to Category ID used by Product. | REFERENCE_MAPPING | P1 | Exact existing reference resolution. |
| ĐVT nhập | THUNG | Exact map to `Thùng`; Product stores one Unit ID. | REFERENCE_MAPPING | P0 | Exact Unit map and existing-reference resolution. |
| ĐVT xuất | CHAI, LON, GOI | Differs from input UOM on customer rows; Product stores one UOM only. | MUST_BLOCK | P0 | Reject different resolved Unit IDs. |
| Quy cách | 24 chai/thùng | Conversion not represented in Unit/Product or posting quantities. | MUST_BLOCK | P0 | Block when nonempty. |
| Mã NCC chính | NCC001 | Product has no supplier relationship. Dropping changes purchasing/source semantics. | MUST_BLOCK | P1 | Block when nonempty. |
| Barcode | 8938501000011 | No Product barcode field or scanner-to-Product resolution. Barcode is an alternate identity, so absence of current consumer is not proof it is cosmetic. | MUST_BLOCK | P1 | Block when nonempty. |
| Theo dõi lô | Có / Không | Product/inventory have no lot dimension; enabled tracking changes stock traceability. | MUST_BLOCK | P0 | `Không` conditionally compatible; `Có` blocks. |
| Hạn dùng (tháng) | populated | No shelf-life or expiry field/behavior. | MUST_BLOCK | P0 | Block when nonempty. |
| Trọng lượng (kg) | 0.5 | No Product field or WMS behavior/consumer; not used in identity, posting, inventory, or report. | SAFE_IGNORE | P2 | Field-scoped safe-ignore; diagnostic per value. |
| Ngưng KD? | Không | Current import creates the only representable active/default behavior; discontinued state is unsupported. | SAFE_IGNORE | P1 | Exact `Không` only; other nonempty values block. |
| Ghi chú | populated | Stored directly on Product. | SUPPORTED_DIRECT | P2 | Direct field. |

### 05_Nha_cung_cap_KH.xlsx (9 columns)

| Customer field | Example | Support / business usage and loss impact | Classification | Severity | Policy |
| --- | --- | --- | --- | --- | --- |
| Mã đối tác | NCC001 | Supplier business key used to resolve transaction supplier. | SUPPORTED_DIRECT | P0 | Required direct field; duplicate rules unchanged. |
| Tên NCC | populated | Stored Supplier name and shown on receipt documents/reports. | SUPPORTED_DIRECT | P1 | Required direct field. |
| MST | populated | No tax-code field; loses supplier legal/tax identity. | MUST_BLOCK | P1 | Block when nonempty. |
| Điện thoại | populated | No field or current processing consumer; current receipt posting references Supplier ID, not phone. | SAFE_IGNORE | P2 | Reasoned safe-ignore header; diagnostic per value. |
| Email | populated | No field or current processing consumer. | SAFE_IGNORE | P2 | Reasoned safe-ignore header; diagnostic per value. |
| Địa chỉ | populated | No field or current processing consumer. | SAFE_IGNORE | P2 | Reasoned safe-ignore header; diagnostic per value. |
| Người liên hệ | populated | No field or current processing consumer; distinct from Supplier identity. | SAFE_IGNORE | P2 | Reasoned safe-ignore header; diagnostic per value. |
| Điều khoản TT | populated | No payment-term field; affects business obligations, so cannot be dropped. | MUST_BLOCK | P1 | Block when nonempty. |
| Còn giao dịch? | Có | Matches current import's create-as-active behavior; inactive supplier state is not representable. | SAFE_IGNORE | P1 | Exact `Có` only; other nonempty values block. |

### 06_Nhap_kho_Thang_09_2026_KH.xlsx (21 columns)

| Customer field | Example | Support / business usage and loss impact | Classification | Severity | Policy |
| --- | --- | --- | --- | --- | --- |
| Header: Số CT | PN260901 | Receipt document key used to group header/detail. | SUPPORTED_DIRECT | P0 | Required; uniqueness/grouping validation. |
| Header: Ngày CT | 2026-09 date | Receipt date is supported by current header. | SUPPORTED_DIRECT | P0 | Deterministic XLSX date parsing. |
| Header: Kho nhận | KHO-HCM-01 | Exact warehouse reference map; posting key uses resolved Warehouse ID. | REFERENCE_MAPPING | P0 | Exact configured map/reference; no fuzzy match. |
| Header: Mã NCC | supplier code | Resolves the existing Supplier ID required by receipt header. | REFERENCE_MAPPING | P0 | Exact existing-reference lookup. |
| Header: Số hóa đơn | populated | No invoice field/link; losing it drops document traceability. | MUST_BLOCK | P1 | Block when nonempty. |
| Header: Loại nhập | Mua hàng | Matches the only current receipt posting behavior; no separate type is represented. | SAFE_IGNORE | P1 | Exact allowed value only; diagnostic per value. |
| Header: Người giao | populated | Delivery accountability is not captured by current receipt model/manual flow. | MUST_BLOCK | P1 | Block when nonempty. |
| Header: Ghi chú | populated | Stored directly on receipt header. | SUPPORTED_DIRECT | P2 | Direct field. |
| Detail: Số CT | PN260901 | Joins detail to the exact receipt header; orphan detail is an error. | REFERENCE_MAPPING | P0 | Exact document-key join. |
| Detail: Dòng | 1 | Detail source order is retained in Preview; no separate line number is persisted. | SAFE_IGNORE | P3 | Safe-ignore field with reason; diagnostic per value. |
| Detail: Mã VT | SP0001 | Resolves Product ID used by receipt and inventory posting. | REFERENCE_MAPPING | P0 | Exact existing reference. |
| Detail: Tên hàng | product name | Used as displayed/checkable product description alongside Product code. | SUPPORTED_DIRECT | P1 | Preserve/check in Preview; no identity substitution. |
| Detail: ĐVT | THUNG | Exact unit map and checked against Product's single Unit ID. | REFERENCE_MAPPING | P0 | Exact map; differing unit blocks. |
| Detail: SL chứng từ | 40 vs received 39 | Differs from actual receipt quantity for PN260903; dropping the difference misstates receiving. | MUST_BLOCK | P0 | Ignore only when numerically equal to `SL thực nhận`; otherwise block. |
| Detail: SL thực nhận | 39 | Quantity used by receipt posting/current balance. | SUPPORTED_DIRECT | P0 | Required positive quantity. |
| Detail: Số lô | LOT-NS-0901 | No lot key in receipt detail or inventory grain. | MUST_BLOCK | P0 | Block when nonempty. |
| Detail: NSX | date | No manufacture-date field/state. | MUST_BLOCK | P0 | Block when nonempty. |
| Detail: HSD | date | No expiry field/state or expiry report behavior. | MUST_BLOCK | P0 | Block when nonempty. |
| Detail: Đơn giá | populated | Supported receipt detail value and amount calculation. | SUPPORTED_DIRECT | P1 | Existing positive-price validation. |
| Detail: Vị trí đề nghị | A-01-01 | No Location dimension; source does not prove whether suggestion is operationally dispensable. | MUST_BLOCK_PENDING_EVIDENCE | P0 | Block when nonempty. |
| Detail: Ghi chú | Thiếu 1 thùng so với chứng từ | Line-level difference note has no target field and carries receiving discrepancy meaning. | MUST_BLOCK | P1 | Block when nonempty. |

### 07_Xuat_kho_Thang_09_2026_KH.xlsx (19 columns)

| Customer field | Example | Support / business usage and loss impact | Classification | Severity | Policy |
| --- | --- | --- | --- | --- | --- |
| Header: Số PX | PX260901 | Issue document key. | SUPPORTED_DIRECT | P0 | Required; uniqueness/grouping validation. |
| Header: Ngày xuất | 2026-09 date | Issue date supported by current header. | SUPPORTED_DIRECT | P0 | Deterministic XLSX date parsing. |
| Header: Kho xuất | KHO-HCM-01 | Resolves stock scope used by availability and posting. | REFERENCE_MAPPING | P0 | Exact configured map/reference. |
| Header: Đơn vị nhận | populated | Current issue header has no destination-party field; loss changes destination meaning. | MUST_BLOCK | P1 | Block when nonempty. |
| Header: Lý do xuất | internal use | Reason classifies the business issue and is not represented. | MUST_BLOCK | P1 | Block when nonempty. |
| Header: Tham chiếu | populated | External/source reference is not stored; audit trace is lost. | MUST_BLOCK | P1 | Block when nonempty. |
| Header: Người nhận | populated | Recipient accountability is not represented. | MUST_BLOCK | P1 | Block when nonempty. |
| Header: Ghi chú | Văn phòng phẩm | Stored directly on issue header. | SUPPORTED_DIRECT | P2 | Direct field. |
| Detail: Số PX | PX260901 | Joins line to exact issue header. | REFERENCE_MAPPING | P0 | Exact document-key join. |
| Detail: Dòng | 1 | Source line order is retained in Preview; no persisted line-number field. | SAFE_IGNORE | P3 | Safe-ignore field with reason; diagnostic per value. |
| Detail: Mã hàng | SP0001 | Resolves Product ID used by stock checks/posting. | REFERENCE_MAPPING | P0 | Exact existing reference. |
| Detail: Tên hàng hóa | product name | Display/check beside Product code. | SUPPORTED_DIRECT | P1 | Preserve/check in Preview. |
| Detail: ĐVT | unit code/name | Unit must resolve to the Product's one Unit ID. | REFERENCE_MAPPING | P0 | Exact map/reference; differing unit blocks. |
| Detail: SL yêu cầu | 25 | Equals issued quantity for all fixture rows; a difference changes issue meaning/quantity control. | SAFE_IGNORE | P0 | Only numerically equal to `SL xuất`; otherwise `BlockIfNonEmpty`. |
| Detail: SL xuất | 25 | Quantity used in stock availability/posting. | SUPPORTED_DIRECT | P0 | Required positive quantity; persistence remains blocked. |
| Detail: LOT xuất | populated | No lot selection or stock-by-lot key. | MUST_BLOCK | P0 | Block when nonempty. |
| Detail: HSD | date | No expiry selection/state. | MUST_BLOCK | P0 | Block when nonempty. |
| Detail: Vị trí lấy | A-01-01 | No location-based availability/picking dimension. | MUST_BLOCK | P0 | Block when nonempty. |
| Detail: Ghi chú | Không quản lý LOT/HSD | Line-level operational statement cannot be preserved; it conflicts with dropping lot/expiry semantics. | MUST_BLOCK | P1 | Block when nonempty. |

## Safe-Loss Decision Tests

| Candidate | Quantity changes if omitted? | Reference identity changes? | Workflow/status changes? | Future receipt/issue behavior changes? | User sees a misleading state? | Decision |
| --- | --- | --- | --- | --- | --- | --- |
| STT and detail Dòng | No | No | No | No; source order remains | No | SAFE_IGNORE with row diagnostic. |
| Warehouse manager/phone, Supplier phone/email/address/contact | No | No | No in current source flow | No current consumer found | Only those contact details are lost; no current WMS operation depends on them | SAFE_IGNORE with explicit scope/reason. |
| Product weight | No | No | No | No current consumer found | No current report/picking behavior uses it | SAFE_IGNORE with explicit reason. |
| Identity unit factor `1` | No; multiplier is identity | No | No | No | No | SAFE_IGNORE for exact identity only. |
| Active/default status and receipt type | No | No | Exact values match existing default/only path | No for exact value | No for exact value | Conditional safe value only; all other values block. |
| Equal requested/received quantity fields | No, because values equal | No | No | No | No | Conditional safe equality only; differences block. |
| Region/address, tax/payment, barcode, LOT/HSD/location, UOM difference, notes/recipient/reason/invoice | Potentially / business identity or traceability changes | Yes or unresolved | Yes or unresolved | Yes or unresolved | Yes or unresolved | MUST_BLOCK or MUST_BLOCK_PENDING_EVIDENCE. |

## Policy Decision

- Safe ignore is opt-in and reason-required at field/header level. Every populated safe ignore generates a `Field_Policy_Diagnostics` entry with source value, policy, status, and reason.
- Conditional exceptions are exact-value or exact-equality checks; no synonym/fuzzy matching was added. Failure remains `UNSUPPORTED_DOMAIN_CAPABILITY` with `BLOCKED` policy diagnostic.
- Unit/Warehouse map-to-field policies accept only the configured source code and require the mapped target to equal the supplied name. Unknown code or mismatch blocks.
- No database query was used to resolve the actual customer's current master IDs. Such live reference resolution remains unverified.
