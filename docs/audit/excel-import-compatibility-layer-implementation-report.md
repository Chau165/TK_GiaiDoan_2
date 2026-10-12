# Excel Import Compatibility Layer Implementation Report

## Phase A: Compatibility Audit

Audit basis: the seven unchanged workbooks in `docs/test-data/customer-import/`, the current profiles/readers/validators, `CWarehouseMaster_Controller`, `CWarehouseDocument_Controller`, and `Database/WarehouseModule.Schema.sql` / `WarehouseModule.Procedures.sql`. No live database was queried.

| Workbook | Customer field / example | Canonical field | Strategy | Current WMS representation / audit result |
|---|---|---|---|---|
| Warehouse | STT | — | IGNORE | Display ordinal only. |
| Warehouse | Mã kho / `KHO-HCM-01` | WarehouseCode | VALUE_MAPPING + REFERENCE_MAPPING | Profile map to `Kho Bình Tân`, then exact Warehouse name lookup; no warehouse-code column exists. |
| Warehouse | Tên kho / bãi | WarehouseName | DIRECT | `tbl_DM_Kho.Ten_Kho`. |
| Warehouse | Khu vực, Địa chỉ, Người phụ trách, SĐT | — | IGNORE | Not used by current warehouse identity or posting contracts; surfaced as ignored, never persisted. |
| Warehouse | Trạng thái / `Đang dùng`, `Tạm ngưng` | WarehouseStatus | UNSUPPORTED_DOMAIN_CAPABILITY | No active/status field exists. `Đang dùng` matches the current create-as-available behavior; `Tạm ngưng` cannot be represented safely. |
| Warehouse | Ghi chú | Note | DIRECT | `tbl_DM_Kho.Ghi_Chu`. |
| Unit | Mã ĐVT / `THUNG` | UnitReference | VALUE_MAPPING source | Config map to `Thùng`; the code is not stored in the Unit table. |
| Unit | Tên gọi | UnitName | DIRECT | `tbl_DM_Don_Vi_Tinh.Ten_Don_Vi_Tinh`. |
| Unit | Ký hiệu in phiếu, Nhóm | — | IGNORE | Not consumed by current unit/product/posting model. |
| Unit | Hệ số QĐ / `1` | UnitConversionFactor | DERIVABLE / IGNORE when identity | All fixture values are 1; no conversion is needed for these rows. Non-identity conversion is unsupported. |
| Unit | ĐVT gốc | BaseUnit | UNSUPPORTED_DOMAIN_CAPABILITY when nonempty | Current Unit model has no base-unit relation. The fixture values are blank. |
| Unit | Ghi chú | Note | DIRECT | Current Unit note field. |
| Category | Mã nhóm | CategoryCode | DIRECT | `tbl_DM_Loai_San_Pham.Ma_LSP`. |
| Category | Tên nhóm hàng | CategoryName | DIRECT | `tbl_DM_Loai_San_Pham.Ten_LSP`. |
| Category | Nhóm cha | ParentCategory | UNSUPPORTED_DOMAIN_CAPABILITY when nonempty | Current category model is flat; fixture values are blank. |
| Category | Theo dõi LOT? / Theo dõi HSD? | LotTracking / ExpiryTracking | UNSUPPORTED_DOMAIN_CAPABILITY when `Có` | No category LOT/HSD controls or end-to-end lot/expiry storage. `Không` is compatible with the current untracked behavior. |
| Category | Ghi chú | Note | DIRECT | Current Category note field. |
| Product | Mã VT, Tên hàng | ProductCode, ProductName | DIRECT | Product code/name columns. |
| Product | Nhóm hàng / `NGK` | CategoryCode | DIRECT + REFERENCE_MAPPING | Exact category-code lookup. |
| Product | ĐVT nhập / `THUNG` | UnitName | VALUE_MAPPING + REFERENCE_MAPPING | Map code to Unit name, then exact Unit lookup. The row also contains a distinct output UOM, so persistence remains blocked until semantics are representable. |
| Product | ĐVT xuất | IssueUnitName | VALUE_MAPPING + REFERENCE_MAPPING | Map and resolve for diagnostics; current Product has only one `Don_Vi_Tinh_ID`, so distinct input/output units are unsupported. |
| Product | Quy cách | UnitConversion | UNSUPPORTED_DOMAIN_CAPABILITY when nonempty | No product UOM-conversion representation. |
| Product | Mã NCC chính | PrimarySupplierCode | UNSUPPORTED_DOMAIN_CAPABILITY when nonempty | Product schema has no Supplier relationship. |
| Product | Barcode, Trọng lượng (kg) | Barcode, Weight | UNSUPPORTED_DOMAIN_CAPABILITY when nonempty | No corresponding Product fields. |
| Product | Theo dõi lô, Hạn dùng (tháng) | LotTracking, ExpiryMonths | UNSUPPORTED_DOMAIN_CAPABILITY when nonempty/true | No end-to-end Product/Category LOT/HSD model. |
| Product | Ngưng KD? | Discontinued | UNSUPPORTED_DOMAIN_CAPABILITY when `Có` | No Product active/status field; `Không` matches current create-as-available behavior. |
| Product | Ghi chú | Note | DIRECT | `tbl_DM_San_Pham.Ghi_Chu`. |
| Supplier | Mã đối tác, Tên NCC | SupplierCode, SupplierName | DIRECT | `tbl_DM_NCC.Ma_NCC` / `Ten_NCC`; exact-code matching is supported. |
| Supplier | MST, Điện thoại, Email, Địa chỉ, Người liên hệ, Điều khoản TT, Còn giao dịch? | — | IGNORE | Current WMS purchasing/stock path does not consume these supplier-contact/accounting fields; report them as ignored. |
| Inbound header | Số CT, Ngày CT | DocumentNumber, DocumentDate | DIRECT | Receipt header number/date. |
| Inbound header | Kho nhận / `KHO-HCM-01` | WarehouseCode | VALUE_MAPPING + REFERENCE_MAPPING | Map to warehouse name, then exact current Warehouse lookup. |
| Inbound header | Mã NCC | SupplierCode | DIRECT + REFERENCE_MAPPING | Exact Supplier code lookup. |
| Inbound header | Số hóa đơn, Người giao | — | IGNORE | Not consumed by current receipt posting contract. |
| Inbound header | Loại nhập / `Mua hàng` | ReceiptType | UNSUPPORTED_DOMAIN_CAPABILITY for other types | Current receipt contract has no receipt-type field; fixture is purchase-only. |
| Inbound header | Ghi chú | Note | DIRECT | Receipt header note. |
| Inbound detail | Số CT, Dòng | DocumentNumber, LineNumber | DIRECT join / IGNORE | Join key is supported; line order is not stored. |
| Inbound detail | Mã VT, Tên hàng | ProductCode, ProductName | DIRECT + strict code/name cross-check | Product lookup by code; supplied name must match exactly under the business comparer. |
| Inbound detail | ĐVT / `THUNG` | UnitReference | VALUE_MAPPING + REFERENCE_MAPPING | Map to Unit name and resolve; transaction detail has no UOM field, so only diagnostic/compatibility validation. |
| Inbound detail | SL chứng từ, SL thực nhận | OrderedQuantity, Quantity | IGNORE / DIRECT | Current receipt posts actual received quantity only. |
| Inbound detail | Đơn giá | UnitPrice | DIRECT | Current receipt detail stores `Don_Gia_Nhap`. |
| Inbound detail | Số lô, NSX, HSD, Vị trí đề nghị | LotCode, ManufactureDate, ExpiryDate, Location | UNSUPPORTED_DOMAIN_CAPABILITY when nonempty | Receipt detail schema has no lot, manufacturing/expiry, or location fields. |
| Inbound detail | Ghi chú | LineNote | UNSUPPORTED_DOMAIN_CAPABILITY when nonempty | Current receipt detail has no note field; fixture includes a quantity discrepancy note. |
| Outbound header | Số PX, Ngày xuất | DocumentNumber, DocumentDate | DIRECT | Issue header number/date. |
| Outbound header | Kho xuất / `KHO-HCM-01` | WarehouseCode | VALUE_MAPPING + REFERENCE_MAPPING | Map to warehouse name, then exact current Warehouse lookup. |
| Outbound header | Đơn vị nhận, Lý do xuất, Tham chiếu, Người nhận | — | UNSUPPORTED_DOMAIN_CAPABILITY when nonempty | Current issue header cannot preserve destination, reason, external reference, or recipient. |
| Outbound header | Ghi chú | Note | DIRECT | Issue header note. |
| Outbound detail | Số PX, Dòng | DocumentNumber, LineNumber | DIRECT join / IGNORE | Join key is supported; line order is not stored. |
| Outbound detail | Mã hàng, Tên hàng hóa | ProductCode, ProductName | DIRECT + strict code/name cross-check | Product lookup by code; supplied name must match exactly. |
| Outbound detail | ĐVT / `THUNG` | UnitReference | VALUE_MAPPING + REFERENCE_MAPPING | Map to Unit name and resolve; transaction detail has no UOM field. |
| Outbound detail | SL yêu cầu, SL xuất | RequestedQuantity, Quantity | IGNORE / DIRECT | Current issue posting uses shipped quantity only. |
| Outbound detail | LOT xuất, HSD, Vị trí lấy | LotCode, ExpiryDate, Location | UNSUPPORTED_DOMAIN_CAPABILITY when nonempty | No lot, expiry, or location dimensions in the issue detail/current inventory grain. |
| Outbound detail | Ghi chú | LineNote | IGNORE | No populated line-note value in the fixture; not used by current posting. |

## Domain Evidence

- Unit stores only name and note. Product stores one Unit_ID; receipt/issue detail stores Product_ID, quantity and unit price, but no transaction UOM, conversion, LOT, HSD, or location.
- Warehouse stores name and note only. Supplier stores code/name/note; Category stores code/name/note.
- `sp_XNK_Document_Post` applies deltas at `(Kho_ID, San_Pham_ID)` under the existing inventory fences and stock checks. Excel import must not bypass this path.
- Product customer rows provide two different UOMs and conversion/other product attributes. Their reference values can be normalized and resolved, but the customer Product rows cannot be safely persisted in full under the current domain.

## Implementation Status

Phase A audit and compatibility implementation complete in source. No live database was queried or changed; schema/data/permissions/procedures, transaction persistence, and the seven customer workbooks remain untouched.

### Configured Maps and Runtime Behavior

`excel-import-profiles.json` declares shared, field-scoped exact maps:

| Map | Source | Target |
|---|---|---|
| Unit | CAI, HOP, GOI, CHAI, LON, THUNG, BAO, KG, RAM, PAL | Cái, Hộp, Gói, Chai, Lon, Thùng, Bao, Kilogram, Ram giấy, Pallet |
| Warehouse | KHO-HCM-01, KHO-HCM-02, KHO-BD-01, KHO-DN-01 | Kho Bình Tân, Kho Cát Lái, Kho Dĩ An, Kho Đà Nẵng |

Mappings run only for the canonical field declaring that map, before exact current-reference lookup. Preview records source value, mapped target, resolution status, and resolved ID when supplied references allow it. Normalization is NFC + trim/collapsed whitespace + case-insensitive; diacritics remain significant. Duplicate normalized sources with conflicting targets fail profile loading with `VALUE_MAPPING_CONFLICT`. No fuzzy mapping, implicit reference creation, or C# customer-code branch was added.

Known unsupported capabilities are evaluated row by row. Configured safe values (such as `Không`, `Đang dùng`, or identity conversion factor `1`) are ignored; nonempty unsafe values and nonempty unknown columns produce `UNSUPPORTED_DOMAIN_CAPABILITY`. Those issues and unsupported headers block master `Can_Confirm` and are displayed in the master import preview. Product input/output UOM values are independently mapped/resolved; different resolved IDs produce a blocker because Product persists only one `Unit_ID`. Transaction Preview resolves warehouse/unit references from caller-supplied lists, cross-checks product code/name, and remains unconditionally persistence-blocked.

### Acceptance Fixture Outcomes

| Fixture | Preview outcome | Compatibility result |
|---|---|---|
| `01_Danh_muc_Kho_KH.xlsx` | Blocked | Warehouse codes have explicit name mappings. Populated `Tạm ngưng` status is unsupported; no warehouse status is persisted. |
| `02_Don_vi_tinh_KH.xlsx` | Eligible for Confirm | Names map directly; code/symbol/group fields are ignored; conversion factor `1` is identity-safe and base unit is empty. |
| `03_Loai_san_pham_KH.xlsx` | Blocked | Category identity fields map; enabled LOT/HSD flags are unsupported by the current flat category/inventory domain. |
| `04_San_pham_KH.xlsx` | Blocked | `THUNG`/`HOP` map and resolve to existing Unit names. Different input/output units and nonempty unsupported product attributes prevent Confirm. |
| `05_Nha_cung_cap_KH.xlsx` | Eligible for Confirm | Code/name/note are represented; contact/tax/accounting extras are explicitly ignored. |
| `06_Nhap_kho_Thang_09_2026_KH.xlsx` | Preview only | Header/detail roles parse; warehouse/unit mappings and exact product/supplier references are available to Preview. Unsupported detail attributes are surfaced; no posting/persistence path was enabled. |
| `07_Xuat_kho_Thang_09_2026_KH.xlsx` | Preview only | Header/detail roles parse; exact identity and supplied inventory checks remain preview-only. Destination/reason/recipient, LOT/HSD/location capability gaps are surfaced; no posting/persistence path was enabled. |

The master fixture statuses above are source-level validator outcomes, not proof of a live customer database reference match. Transaction tests exercise supplied in-memory reference records; no live DB resolution was attempted.

### Verification

- TDD red phase: new focused tests first failed because mapping/profile APIs and preview contracts were absent.
- Focused regression command: `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter "FullyQualifiedName~ImportValueMappingTests|FullyQualifiedName~ProductExcelImportValidationTests|FullyQualifiedName~TransactionImportPreviewTests|FullyQualifiedName~ImportStructureDetectionTests|FullyQualifiedName~UnitExcelImportValidationTests|FullyQualifiedName~CategoryExcelImportValidationTests|FullyQualifiedName~WarehouseExcelImportValidationTests|FullyQualifiedName~SupplierExcelImportValidationTests"`.
- Focused validator/parser regression: 99 passed, 0 failed. Master import UI tests: 19 passed, 0 failed.
- Web host build: succeeded with 27 Telerik licensing warnings (`TKL002`, `TKL101`, `TKL004`); 0 build errors.
- Coverage measurement is not configured in the test project (no Coverlet package). Broader regression, live DB parity, and browser UAT were not run. No DB credentials were used or needed for these source tests.
- Current fixture SHA-256 values:

| Fixture | SHA-256 |
|---|---|
| `01_Danh_muc_Kho_KH.xlsx` | `DF48CA93DA318289438A5F11C4B8E53513CE0F6D34D6AB98385FE71F41B2E2DA` |
| `02_Don_vi_tinh_KH.xlsx` | `95BFB88E4ABF9C1D82235E5AD3E80EEC91AEB8B2835F0631978BE5BC5BA958C6` |
| `03_Loai_san_pham_KH.xlsx` | `7EE3C901936CE56F24B8FD49F5925636E407675D34BEB6D5F78FFFF22B1B6934` |
| `04_San_pham_KH.xlsx` | `61E2642E5F98C4974601932055377D93403F3D1A26945BE6F0573A00CB337807` |
| `05_Nha_cung_cap_KH.xlsx` | `EC2AA0497CE36EFB653EF9935D532AD368D127C06995EF787C881C934821B446` |
| `06_Nhap_kho_Thang_09_2026_KH.xlsx` | `11689B98548FA3E33180725370F42B56211A28AC3D988289F6C7AA7CF176BAE7` |
| `07_Xuat_kho_Thang_09_2026_KH.xlsx` | `9823C5138226D3843D93900BDFFD707D31734B68A0907418086D889706787B07` |

The fixtures were not edited. SQL/schema working-tree changes predated this phase and were not edited by this implementation.
