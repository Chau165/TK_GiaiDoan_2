# DB-First Five-Master Import Target Contract

Date: 2026-10-09  
Status: Product UOM business decision accepted; source and focused verification passed; live Confirm and post-import verification are deferred to the user's self-test. See `product-inbound-uom-import-completion-report.md`.  
Authority: Current WMS schema, procedures, entities, manual create UI, and services are the target contract. Customer workbooks are input data only.

## Current Target Contracts

`CWarehouseMaster` is the shared application entity (`Code`, `Name`, `Related_ID`, `Related_ID_2`, `Ghi_Chu`). Import row models are narrower. The manual create/edit form uses `Name` and `Ghi_Chu` for all masters; only Category, Product, and Supplier show `Code`. Product additionally selects Category and Unit references (`FWarehouse_3_Warehouse_Edit.razor:36-50`). The controller maps the shared model to existing procedures (`CWarehouseMaster_Controller.cs:430-477`).

| Master | Current entity/import projection | DB columns and existing insert procedure | Required / business key / references | Manual create and persistence path |
|---|---|---|---|---|
| Unit | `Name` <- `Ten_Don_Vi_Tinh`; `Ghi_Chu` | `tbl_DM_Don_Vi_Tinh.Ten_Don_Vi_Tinh`, `Ghi_Chu`; `F2016_sp_ins_Don_Vi_Tinh(@Ten_Don_Vi_Tinh, @Ghi_Chu, ...)` | Name required; key is unit name; no FK. Note optional. | Name and note; `Import_Units_Async` -> `Save_Master_Async` -> F2016 SP. |
| Warehouse | `Name` <- `Ten_Kho`; `Ghi_Chu` | `tbl_DM_Kho.Ten_Kho`, `Ghi_Chu`; `F2009_sp_ins_Kho(@Ten_Kho, @Ghi_Chu, ...)` | Name required; key is warehouse name; no FK. Note optional. | Name and note; `Import_Warehouses_Async` -> same save command -> F2009 SP. There is no persisted Warehouse code/status/region/address/manager/phone. |
| Category | `Code` <- `Ma_LSP`; `Name` <- `Ten_LSP`; `Ghi_Chu` | `tbl_DM_Loai_San_Pham.Ma_LSP`, `Ten_LSP`, `Ghi_Chu`; `F2017_sp_ins_Loai_San_Pham(@Ma_LSP, @Ten_LSP, @Ghi_Chu, ...)` | Code and name required; SP rejects a duplicate code **or** name; no FK. Note optional. | Code, name, note; `Import_Categories_Async` -> same save command -> F2017 SP. |
| Supplier | `Code` <- `Ma_NCC`; `Name` <- `Ten_NCC`; `Ghi_Chu` | `tbl_DM_NCC.Ma_NCC`, `Ten_NCC`, `Ghi_Chu`; `F2019_sp_ins_NCC(@Ma_NCC, @Ten_NCC, @Ghi_Chu, ...)` | Code and name required; SP rejects a duplicate code **or** name; no FK. Note optional. | Code, name, note; `Import_Suppliers_Async` -> same save command -> F2019 SP. |
| Product | `Code` <- `Ma_San_Pham`; `Name` <- `Ten_San_Pham`; `Related_ID` <- Category ID; `Related_ID_2` <- Unit ID; `Ghi_Chu` | `tbl_DM_San_Pham.Ma_San_Pham`, `Ten_San_Pham`, `Loai_San_Pham_ID`, `Don_Vi_Tinh_ID`, `Ghi_Chu`; `F2018_sp_ins_San_Pham(@Ma_San_Pham, @Ten_San_Pham, @Loai_San_Pham_ID, @Don_Vi_Tinh_ID, @Ghi_Chu, ...)` | Code/name required; category and one Unit ID required FKs; Product code is the import key; note optional. Category and Unit references must resolve exactly. | Code/name, Category dropdown, one `Đơn vị tính` dropdown, note; `Import_Products_Async` -> same save command -> F2018 SP. |

Schema evidence: `Database/WarehouseModule.Schema.sql:38-98`. SP required fields, duplicate checks, and FK checks: `Database/WarehouseModule.Procedures.sql:3903-3924,3963-3985,5052-5074,5113-5134,5174-5194`. Current entity and import DTOs: `TKS_Thuc_Tap_V11_Data_Access/Entity/Warehouse/CWarehouseMasterModels.cs`, `CUnitImportModels.cs`, `CWarehouseImportModels.cs`, `CCategoryImportModels.cs`, `CSupplierImportModels.cs`, `CProductImportModels.cs`. The batch import methods use the controller's existing transactional SP path; no direct table persistence is needed.

## Product Unit_ID Semantics

The current Product target has one `Don_Vi_Tinh_ID`, and the manual Product form exposes one generic `Đơn vị tính` selector. Receipt and issue detail models carry Product ID, quantity and price (with a displayed unit name); their insert SPs accept Product ID, quantity and price, not a transaction UOM or conversion (`CWarehouseDocumentModels.cs:27-42`; `F2011_sp_ins_Nhap_Kho_Detail` at `WarehouseModule.Procedures.sql:4316-4319`; `F2012_sp_ins_Xuat_Kho_Detail` at `:5411-5414`).

This proves the target has one catalog-level unit and no distinct inbound/outbound unit behavior. The business decision for this import is explicit: `Product.Unit_ID` is sourced only from `ĐVT nhập`. That value is mapped through the exact configured Unit value map, then must resolve uniquely to an existing Unit reference. It is required; invalid inbound values block the row and never fall back to outbound. `ĐVT xuất` is `IGNORE_SOURCE_ONLY`: it produces a visible `SOURCE_FIELD_IGNORED` diagnostic with the reason recorded in the Product profile, is not reference-resolved or persisted, and does not affect duplicate identity. Product persisted-field comparisons still include the inbound Unit ID, so changing inbound UOM for an existing Product code is a conflict.

Historical Preview before this business decision reported 1 valid row and 14 inbound/outbound UOM mismatch errors. That result is superseded by the inbound-canonical decision; current Preview and live Confirm evidence are recorded in `product-inbound-uom-import-completion-report.md`.

## Dependency Graph

`Unit` and `Category` are independent master imports. `Warehouse` and `Supplier` are independent. `Product` depends on both `Category` and `Unit` because its current insert SP requires both FKs. Current Product has no Supplier relationship. Live UAT order was Unit (already persisted before this phase), Category, Warehouse, Supplier, then Product Preview. Product Confirm remains pending its UOM decision.

## Workbook Field Classification

Classes follow the task contract. `MAP_DIRECT` persists a semantically equivalent current field. `MAP_VALUE` applies an explicit exact configured representation map. `MAP_REFERENCE` resolves an exact current foreign key. `IGNORE_SOURCE_ONLY` is diagnostic-only, nonblocking, nonpersisted, and excluded from duplicate comparison. `AMBIGUOUS` blocks until the target mapping is decided. No fuzzy mapping is used.

| Workbook | ExcelField | ExampleValue | Classification | TargetEntity | TargetProperty | DBColumn | SPParameter | Required? | Blocking? | Persisted? | Reason | SourceEvidence |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `01_Danh_muc_Kho_KH.xlsx` | STT | 1 | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | Display sequence only. | Profile `Warehouse.safeIgnoreHeaders`; detector source-row diagnostics. |
| same | Mã kho | KHO-HCM-01 | MAP_VALUE | Warehouse | Name (exact configured code-to-name map) | Ten_Kho | @Ten_Kho | No | Yes if map/name disagree | Indirectly via mapped name | Warehouse has no stored code; exact map must agree with supplied name. | `excel-import-profiles.json` Warehouse `WarehouseCode`; `CWorkbookStructureDetector.cs` map-to-field validation. |
| same | Tên kho / bãi | non-empty | MAP_DIRECT | Warehouse | Name | Ten_Kho | @Ten_Kho | Yes | Yes | Yes | Current required warehouse identity. | `CWarehouseImportValidator.cs`; F2009 SP. |
| same | Khu vực | non-empty | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No Warehouse region property/column/behavior. | Schema S1; Warehouse profile. |
| same | Địa chỉ | non-empty | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No Warehouse address property/column/behavior. | Schema S1; Warehouse profile. |
| same | Người phụ trách | non-empty | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No manager relationship or behavior. | Schema S1; Warehouse profile `safeIgnoreHeaders`. |
| same | SĐT | non-empty | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No Warehouse phone property or behavior. | Schema S1; Warehouse profile `safeIgnoreHeaders`. |
| same | Trạng thái | Đang dùng / Tạm ngưng | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | Current Warehouse has no lifecycle/status field; neither value is persisted. | Schema S1; Warehouse create UI; profile field. |
| same | Ghi chú | optional | MAP_DIRECT | Warehouse | Ghi_Chu | Ghi_Chu | @Ghi_Chu | No | Yes on invalid length/SP failure | Yes | Current note field. | Schema S1; F2009 SP. |
| `02_Don_vi_tinh_KH.xlsx` | Mã ĐVT | THUNG | MAP_VALUE | Unit | Name (exact configured code-to-name map) | Ten_Don_Vi_Tinh | @Ten_Don_Vi_Tinh | No | Yes if unmapped or inconsistent | Indirectly via mapped name | Unit has no stored code; map resolves source code to the provided canonical name. | Unit reference map; Unit profile `UnitCode`/`MapToField`. |
| same | Tên gọi | Thùng | MAP_DIRECT | Unit | Name | Ten_Don_Vi_Tinh | @Ten_Don_Vi_Tinh | Yes | Yes | Yes | Required current Unit name. | Unit profile; Unit validator; F2016 SP. |
| same | Ký hiệu in phiếu | non-empty | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No separate print-symbol behavior. | Schema S1; Unit profile. |
| same | Nhóm | non-empty | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No Unit-group relationship. | Schema S1; Unit profile. |
| same | Hệ số QĐ | 1 | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No conversion-factor field; identity value does not change target Unit. | Schema S1; Unit profile; original workbook values. |
| same | ĐVT gốc | blank | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No base-unit relationship. | Schema S1; Unit profile. |
| same | Ghi chú | optional | MAP_DIRECT | Unit | Ghi_Chu | Ghi_Chu | @Ghi_Chu | No | Yes on invalid length/SP failure | Yes | Current note field. | Schema S1; F2016 SP. |
| `03_Loai_san_pham_KH.xlsx` | Mã nhóm | non-empty | MAP_DIRECT | Category | Code | Ma_LSP | @Ma_LSP | Yes | Yes | Yes | Current required Category code/key. | Category profile; validator; F2017 SP. |
| same | Tên nhóm hàng | non-empty | MAP_DIRECT | Category | Name | Ten_LSP | @Ten_LSP | Yes | Yes | Yes | Current required Category name. | Category profile; validator; F2017 SP. |
| same | Nhóm cha | non-empty | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | Current Category is flat; no parent reference exists. | Schema S1; Category profile. |
| same | Theo dõi LOT? | Có / Không | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No Category/Inventory lot-tracking capability. | Schema S1/S3; Category profile. |
| same | Theo dõi HSD? | Có / Không | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No expiry-tracking capability. | Schema S1/S3; Category profile. |
| same | Ghi chú | optional | MAP_DIRECT | Category | Ghi_Chu | Ghi_Chu | @Ghi_Chu | No | Yes on invalid length/SP failure | Yes | Current note field. | Schema S1; F2017 SP. |
| `04_San_pham_KH.xlsx` | Mã VT | non-empty | MAP_DIRECT | Product | Code | Ma_San_Pham | @Ma_San_Pham | Yes | Yes | Yes | Current Product key. | Product profile/validator; F2018 SP. |
| same | Tên hàng | non-empty | MAP_DIRECT | Product | Name | Ten_San_Pham | @Ten_San_Pham | Yes | Yes | Yes | Current required Product name. | Product profile/validator; F2018 SP. |
| same | Nhóm hàng | category code | MAP_REFERENCE | Product -> Category | Related_ID -> Auto_ID | Loai_San_Pham_ID | @Loai_San_Pham_ID | Yes | Yes if unresolved/nonunique | Yes | Resolve exact Category business code; never ignore a required FK. | Product profile/validator; F2018 SP FK check. |
| same | ĐVT nhập | THUNG | MAP_REFERENCE | Product -> Unit | Related_ID_2 -> Auto_ID after exact configured value mapping and unique Unit reference lookup | Don_Vi_Tinh_ID | @Don_Vi_Tinh_ID | Yes | Yes if unmapped/unresolved/nonunique; no outbound fallback | Yes | Business decision defines inbound UOM as the sole source for Product.Unit_ID. | Product profile/validator; manual UI; Product schema/SP; receipt/issue detail models/SP. |
| same | ĐVT xuất | CHAI | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | Current Product stores one Unit_ID, defined by inbound UOM; outbound UOM is not persisted by the current Product model. | Product profile policy reason and detector `SOURCE_FIELD_IGNORED` diagnostic. |
| same | Quy cách | non-empty | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No product conversion/package relationship. | Product schema/SP; Product profile. |
| same | Mã NCC chính | non-empty | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | Product has no supplier relationship. | Product schema/SP; Product profile. |
| same | Barcode | non-empty | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No barcode property/index/behavior. | Product schema/SP; Product profile. |
| same | Theo dõi lô | Có / Không | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No lot tracking dimension. | Schema S2/S3; Product profile. |
| same | Hạn dùng (tháng) | populated | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No shelf-life/expiry model. | Schema S2/S3; Product profile. |
| same | Trọng lượng (kg) | populated | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No weight behavior or target property. | Product schema/SP; Product profile. |
| same | Ngưng KD? | Có / Không | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | Current Product has no status/active field. | Product schema/SP; Product profile. |
| same | Ghi chú | optional | MAP_DIRECT | Product | Ghi_Chu | Ghi_Chu | @Ghi_Chu | No | Yes on invalid length/SP failure | Yes | Current note field. | Schema S1; F2018 SP. |
| `05_Nha_cung_cap_KH.xlsx` | Mã đối tác | non-empty | MAP_DIRECT | Supplier | Code | Ma_NCC | @Ma_NCC | Yes | Yes | Yes | Current Supplier key. | Supplier profile/validator; F2019 SP. |
| same | Tên NCC | non-empty | MAP_DIRECT | Supplier | Name | Ten_NCC | @Ten_NCC | Yes | Yes | Yes | Current required Supplier name. | Supplier profile/validator; F2019 SP. |
| same | MST | populated | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No tax-code field. | Schema S1; Supplier create UI/profile. |
| same | Điện thoại | populated | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No Supplier phone field/behavior. | Schema S1; Supplier profile. |
| same | Email | populated | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No Supplier email field/behavior. | Schema S1; Supplier profile. |
| same | Địa chỉ | populated | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No Supplier address field/behavior. | Schema S1; Supplier profile. |
| same | Người liên hệ | populated | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No Supplier contact-person field/behavior. | Schema S1; Supplier profile. |
| same | Điều khoản TT | populated | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | No payment-terms field; do not put into Note or Name. | Schema S1; Supplier profile. |
| same | Còn giao dịch? | Có / status | IGNORE_SOURCE_ONLY | - | - | - | - | No | No | No | Current Supplier has no lifecycle/status field. | Schema S1; Supplier create UI/profile. |

All configured ignored fields above must yield row-level `SOURCE_FIELD_IGNORED` diagnostics when populated. They do not participate in target validation, Confirm eligibility, persistence, or duplicate comparison. An unknown, unclassified non-empty header remains a blocking ambiguity until explicitly classified.

## Prior Evidence And Remaining Gate

Prior live UAT on `TKS_Thuc_Tap_V11_GiaiDoan2` confirmed Unit=10, Warehouse=4, Category=5, and Supplier=5 through the existing import services and stored procedures. Prior Product Preview (1 valid / 14 errors) preceded the explicit inbound-UOM decision and was not confirmed. Current Product Confirm, duplicate reimport, reference verification, and DB before/after evidence are in `product-inbound-uom-import-completion-report.md`. No schema or stored-procedure change is part of the Product UOM decision.

Source reports reviewed: `excel-import-six-workbook-live-preview-verification-report.md`, `excel-import-post-sp-sync-regression-and-live-uat-report.md`, `stored-procedure-source-db-parity-report.md`, `excel-import-safe-loss-capability-audit.md`, `excel-import-safe-loss-implementation-report.md`, `excel-import-compatibility-layer-implementation-report.md`, and `excel-import-redesign.md`.
