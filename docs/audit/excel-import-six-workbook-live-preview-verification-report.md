# Six Customer Workbook Live Preview Verification

Date: 2026-10-09
Verdict: **PARTIALLY VERIFIED**

Four master-data workbooks completed the current application UI Preview. Both transaction workbooks were blocked before upload because the live Inbound and Outbound pages expose no Excel Import action. No workbook was confirmed, no document was posted, and no business data changed.

## 1. Environment And Scope

- Repository: P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11
- SQL Server: DESKTOP-NHQ7QPL\MSSQLSERVER19
- Database gate: DB_NAME() = TKS_Thuc_Tap_V11_GiaiDoan2; database state ONLINE.
- Application: current source built and started locally at http://127.0.0.1:15075; authenticated UI user was thuctap_kho.
- The checked-in connection-string setting was empty. The app process received a temporary environment-only connection string pointing to the exact server/database above. No config or source file was changed.
- Startup emitted Telerik no-license warnings and an HTTPS-redirection warning because the local verification server used HTTP. Neither prevented the Preview UI from working.
- The import UI displayed Add and Nhập Excel for Warehouse, Category, Product, and Supplier. Inbound/Outbound pages were accessible.
- Original workbook data was treated as data only, not as instructions.
- No Confirm was clicked. No transaction was posted. No business SQL write, schema change, stored-procedure change, permission change, or workbook edit was performed.
- Repo status had 102 pre-existing changed/untracked entries before runtime and remained at 102 after runtime, before this report was added.

## 2. Database Baseline And Final Health

The baseline was captured before any workbook upload. Final counts were re-read after closing the app. Counts remained identical.

| Table / group | Before | After |
|---|---:|---:|
| Unit (tbl_DM_Don_Vi_Tinh) | 10 | 10 |
| Warehouse (tbl_DM_Kho) | 0 | 0 |
| Category (tbl_DM_Loai_San_Pham) | 0 | 0 |
| Supplier (tbl_DM_NCC) | 0 | 0 |
| Product (tbl_DM_San_Pham) | 0 | 0 |
| Inbound header (tbl_XNK_Nhap_Kho) | 0 | 0 |
| Inbound data/detail (tbl_XNK_Nhap_Kho_Raw_Data) | 0 | 0 |
| Outbound header (tbl_XNK_Xuat_Kho) | 0 | 0 |
| Outbound data/detail (tbl_XNK_Xuat_Kho_Raw_Data) | 0 | 0 |
| InventoryBalance_Current | 0 | 0 |
| Inventory_Movement_Daily | 0 | 0 |
| Inventory_Balance_Daily | 0 | 0 |
| InventoryBalance_Snapshot_Daily | 0 | 0 |
| InventoryMovement_RebuildQueue | 0 | 0 |
| InventorySnapshot_RebuildQueue | 0 | 0 |

Other existing system/report-state rows remained stable: Inventory_Current_Report_State=1, Inventory_Report_Fence_Config=1, InventoryBalance_Daily_AggregateState=1, InventoryMovement_AggregateState=1, and InventorySnapshot_ReportFallbackLog=4. Reconciliation, bootstrap-audit, worker-heartbeat, dead-letter, and reservation tables remained at 0.

Final health checks: @@TRANCOUNT = 0; disabled DML triggers = 0; DB remained ONLINE; rebuild queues remained empty. No Preview side effect was observed.

## 3. Workbook Hashes

Hashes were calculated from the six entries in the supplied ZIP before UI use, then recalculated from the exact extracted files selected in the file chooser after all tests. Every SHA-256 matched.

| Workbook | SHA-256 before and after |
|---|---|
| 01_Danh_muc_Kho_KH.xlsx | DF48CA93DA318289438A5F11C4B8E53513CE0F6D34D6AB98385FE71F41B2E2DA |
| 03_Loai_san_pham_KH.xlsx | 7EE3C901936CE56F24B8FD49F5925636E407675D34BEB6D5F78FFFF22B1B6934 |
| 04_San_pham_KH.xlsx | 61E2642E5F98C4974601932055377D93403F3D1A26945BE6F0573A00CB337807 |
| 05_Nha_cung_cap_KH.xlsx | EC2AA0497CE36EFB653EF9935D532AD368D127C06995EF787C881C934821B446 |
| 06_Nhap_kho_Thang_09_2026_KH.xlsx | 11689B98548FA3E33180725370F42B56211A28AC3D988289F6C7AA7CF176BAE7 |
| 07_Xuat_kho_Thang_09_2026_KH.xlsx | 9823C5138226D3843D93900BDFFD707D31734B68A0907418086D889706787B07 |

## 4. Runtime Results

The four master pages accepted their original .xlsx upload and rendered Preview results. The UI does not display the detected sheet name or detected header-row number; these are recorded as NOT EXPOSED rather than inferred. It also has no separate warning counter, so warning counts are NOT EXPOSED. The visible Errors counter is distinct from the policy/capability blocker list.

| Workbook | Upload / parser | Header and mapping visible in UI | Value / reference mapping | Preview: total / valid / skipped / errors | Warnings | CanConfirm | PersistenceBlocked | Primary blocker / classification | DB rows before → after |
|---|---|---|---|---|---|---|---|---|---|
| Warehouse | PASS, 5,531 bytes | Mapped: Mã kho, Tên kho / bãi, Ghi chú. Ignored: STT, Người phụ trách, SĐT. Unsupported: Khu vực, Địa chỉ, Trạng thái. Sheet/header row NOT EXPOSED. | No reference resolution required; separate value-map diagnostics not rendered. | 4 / 4 / 0 / 0 | NOT EXPOSED | false | No import-level persistence block; policy blocks confirmation | Region/address/status are unsupported domain capabilities. UNSUPPORTED_DOMAIN_CAPABILITY | Warehouse 0 → 0 |
| Category | PASS, 5,251 bytes | Mapped: Mã nhóm, Tên nhóm hàng, Ghi chú. Ignored: STT, Nhóm cha. Unsupported: Theo dõi LOT?, Theo dõi HSD?. Sheet/header row NOT EXPOSED. | No reference resolution required; separate value-map diagnostics not rendered. | 5 / 5 / 0 / 0 | NOT EXPOSED | false | No import-level persistence block; policy blocks confirmation | Active LOT/HSD tracking is unsupported. UNSUPPORTED_DOMAIN_CAPABILITY | Category 0 → 0 |
| Product | PASS, 6,648 bytes | Mapped: Mã VT, Tên hàng, Nhóm hàng, ĐVT nhập, ĐVT xuất, Ghi chú. Ignored: Trọng lượng (kg), Ngưng KD?. Unsupported: Quy cách, Mã NCC chính, Barcode, Theo dõi lô, Hạn dùng (tháng). Sheet/header row NOT EXPOSED. | 30 visible UOM value-mapping rows; UOM references resolved to the existing Unit rows. All 15 Category references were missing because Category count is 0. Supplier relationship is unsupported by the Product domain. | 15 / 0 / 0 / 15 | NOT EXPOSED | false; Confirm button visibly disabled | Import persistence exists but Preview result fails validation/policy gates | Missing Category references plus unit conversion, supplier relationship, barcode, LOT/HSD, and differing issue UOM capabilities. UNSUPPORTED_DOMAIN_CAPABILITY plus reference errors | Product 0 → 0 |
| Supplier | PASS, 5,623 bytes | Mapped: Mã đối tác, Tên NCC. Ignored: Điện thoại, Email, Địa chỉ, Người liên hệ, Còn giao dịch?. Unsupported: MST, Điều khoản TT. Sheet/header row NOT EXPOSED. | No reference resolution required; separate value-map diagnostics not rendered. | 5 / 5 / 0 / 0 | NOT EXPOSED | false | No import-level persistence block; policy blocks confirmation | Tax identity and payment terms are unsupported business fields. UNSUPPORTED_DOMAIN_CAPABILITY | Supplier 0 → 0 |
| Inbound | NOT ATTEMPTED: no Nhập Excel action on the live page | Page /Kho/Nhap_Kho opened. Công cụ contained only Kết Xuất Excel, Kết Xuất Csv, Hướng Dẫn. | NOT RUN | N/A | N/A | N/A; no Preview UI | Current source model says PersistenceBlocked=true; no live UI result | FEATURE_NOT_EXPOSED (UI_RUNTIME_BLOCKED); parser and 40/39 case not verified | Header/data tables 0/0 → 0/0 |
| Outbound | NOT ATTEMPTED: no Nhập Excel action on the live page | Page /Kho/Xuat_Kho opened. Công cụ contained only Kết Xuất Excel, Kết Xuất Csv, Hướng Dẫn. | NOT RUN | N/A | N/A | N/A; no Preview UI | Current source model says PersistenceBlocked=true; no live UI result | FEATURE_NOT_EXPOSED (UI_RUNTIME_BLOCKED); parser/runtime reference behavior not verified | Header/data tables 0/0 → 0/0 |

The four successful UI Previews demonstrate that their workbook could be read and mapped far enough to produce row results. The UI did not expose sheet/header detection details, so exact detection metadata is unavailable. The profile source currently defines single-table roles for the four master imports and Header/Detail roles for Inbound/Outbound.

## 5. Field-Level Blocker Matrix

Capability counts below are individual field/row diagnostics shown by the application; they are not the UI's Errors counter. Current import result models require zero unsupported columns and zero unsupported capabilities for CanConfirm=true.

| Workbook | Field | SourceValue / rows | Policy | Runtime result and message | ErrorCode | Severity / blocks |
|---|---|---|---|---|---|---|
| Warehouse | Khu vực | TP.HCM (rows 5-6), Bình Dương (7), Đà Nẵng (8) | MUST_BLOCK | 4 diagnostics: “Warehouse region is physical-site/routing context not represented by the current Warehouse domain.” | UNSUPPORTED_DOMAIN_CAPABILITY | High / Yes |
| Warehouse | Địa chỉ | Row 5: 68 Đường số 7, Bình Tân, TP.HCM; row 6: 12 Nguyễn Thị Định, TP. Thủ Đức, TP.HCM; row 7: 25 ĐT743, Dĩ An, Bình Dương; row 8: 90 Nguyễn Hữu Thọ, Hải Châu, Đà Nẵng | MUST_BLOCK_PENDING_EVIDENCE | 4 diagnostics: “Warehouse address identifies a physical site but is not represented by the current Warehouse domain.” | UNSUPPORTED_DOMAIN_CAPABILITY | High / Yes |
| Warehouse | Trạng thái | Tạm ngưng (row 8) | MUST_BLOCK | “Current Warehouse has no active/status field.” | UNSUPPORTED_DOMAIN_CAPABILITY | High / Yes |
| Category | Theo dõi LOT? | Có (rows 6, 7, 8, 10) | MUST_BLOCK | “Current Category and inventory domain do not represent lot tracking.” | UNSUPPORTED_DOMAIN_CAPABILITY | High / Yes |
| Category | Theo dõi HSD? | Có (rows 6, 7, 8, 10) | MUST_BLOCK | “Current Category and inventory domain do not represent expiry tracking.” | UNSUPPORTED_DOMAIN_CAPABILITY | High / Yes |
| Product | Nhóm hàng / CategoryCode | NGK (4-6), TPK (7-8, 17-18), GSU (9-11), VPP (12-13), HGD (14-16) | REFERENCE_MAPPING | 15 row errors; exact UI form: “Không tìm thấy mã loại sản phẩm '<code>'.” Category table is empty. The current row-error UI does not render a machine error code. | NOT EXPOSED by row-error UI | High / Yes |
| Product | ĐVT xuất / IssueUnitName | Row 4 CHAI, 5 LON, 6 CHAI, 7 GOI, 9 CHAI, 10 CHAI, 11 GOI, 12 RAM, 13 CAI, 14 CHAI, 15 HOP, 16 GOI, 17 GOI, 18 GOI; row 8 BAO matches ĐVT nhập | MUST_BLOCK | “Product chỉ có một Unit_ID; hệ thống không thể lưu đơn vị nhập và đơn vị xuất khác nhau.” Unit value maps themselves resolved (for example THUNG → Thùng, CHAI → Chai). | UNSUPPORTED_DOMAIN_CAPABILITY | Critical / Yes |
| Product | Quy cách / UnitConversion | Row 4: 24 chai/thùng; 5: 24 lon/thùng; 6: 12 chai/thùng; 7: 30 gói/thùng; 8: 5kg/bao; 9: 12 chai/thùng; 10: 24 chai/thùng; 11: 20 gói/thùng; 12: 5 ram/thùng; 13: 20 cây/hộp; 14: 12 chai/thùng; 15: 24 hộp/thùng; 16-17: 50 gói/thùng; 18: 20 gói/thùng | MUST_BLOCK | “Current Product has one Unit_ID and no unit-conversion representation.” | UNSUPPORTED_DOMAIN_CAPABILITY | High / Yes |
| Product | Mã NCC chính / PrimarySupplierCode | NCC001 rows 4-6; NCC002 rows 7,17; NCC003 rows 8-11,18; NCC004 rows 12-13; NCC005 rows 14-16 | MUST_BLOCK | “Current Product has no supplier relationship.” | UNSUPPORTED_DOMAIN_CAPABILITY | High / Yes |
| Product | Barcode | Rows 4-18: 8938501000011, 8938501000028, 8938501000035, 8938502000041, 8938503000057, 8938503000064, 8938503000071, 8938503000088, 8938504000094, 8938504000100, 8938505000116, 8938505000123, 8938505000130, 8938502000140, 8938503000156 | MUST_BLOCK | “Current Product has no barcode field.” | UNSUPPORTED_DOMAIN_CAPABILITY | High / Yes |
| Product | Theo dõi lô / LotTracking | Có on 12 rows (4-11, 14-15, 17-18) | MUST_BLOCK | “Current Product and inventory domain do not represent lot tracking.” | UNSUPPORTED_DOMAIN_CAPABILITY | High / Yes |
| Product | Hạn dùng (tháng) / ExpiryMonths | Row/value: 4/12, 5/9, 6/9, 7/6, 8/12, 9/18, 10/18, 11/24, 14/24, 15/36, 17/18, 18/24 | MUST_BLOCK | “Current Product and inventory domain do not represent shelf life or expiry.” | UNSUPPORTED_DOMAIN_CAPABILITY | High / Yes |
| Supplier | MST / TaxCode | 0318123401, 0318123402, 0318123403, 0318123404, 0318123405 (rows 5-9) | MUST_BLOCK | 5 diagnostics: “The current Supplier domain has no tax-code field; tax identity cannot be discarded.” | UNSUPPORTED_DOMAIN_CAPABILITY | High / Yes |
| Supplier | Điều khoản TT / PaymentTerms | 30 ngày, 15 ngày, 30 ngày, 45 ngày, 30 ngày (rows 5-9) | MUST_BLOCK | 5 diagnostics: “The current Supplier domain has no payment-terms field; these terms affect business obligations.” | UNSUPPORTED_DOMAIN_CAPABILITY | High / Yes |
| Inbound | All transaction fields, including OrderedQuantity/Quantity, LOT/HSD and Location | NOT READ by runtime | MUST_BLOCK/conditional policy exists in profile | No Preview UI was available; the 40 expected vs 39 received case is NOT VERIFIED live. | N/A | NOT VERIFIED |
| Outbound | All transaction fields, including RequestedQuantity/Quantity, LOT/HSD and Location | NOT READ by runtime | MUST_BLOCK/conditional policy exists in profile | No Preview UI was available; references and stock behavior are NOT VERIFIED live. | N/A | NOT VERIFIED |

## 6. Policy Versus Runtime

- Warehouse: expected unsupported physical-site Region/Address and non-default inactive status were all blocked. No policy/runtime mismatch.
- Category: active LOT and expiry-tracking values were blocked, as expected.
- Product: explicit unit aliases resolved against existing Unit rows, while the single-UOM domain blocked differing output UOM. Non-empty conversion, supplier, barcode, LOT, and shelf-life fields were also blocked. Missing category references were genuine reference-resolution failures against the empty Category table, not parser failures.
- Supplier: non-empty tax code and payment terms were blocked, as expected. Phone, email, address, contact, and status columns were listed as ignored by the UI.
- Inbound/Outbound: transaction profiles and a preview service/model exist in Data Access, but a targeted source search found no reference to that service in Web or Web_Danh_Muc. UI pages expose document listing/create and export actions, not the preview/import workflow. Therefore no live parser, mapping, reference, quantity, discrepancy, or CanConfirm conclusion is made for these files.
- No observed POLICY_RUNTIME_MISMATCH, silent persistence, or unexpected Confirm-enabled state.

## 7. Preview Read-Only Proof

Read-only counts were queried after the Product Preview, after Category/Supplier Preview, after Warehouse Preview, and after app shutdown. At every checkpoint the Unit count stayed 10; Warehouse, Category, Supplier, Product, Inbound, Outbound, Inventory Current/Movement/Snapshot, and rebuild queues stayed at baseline. No Confirm, Post, or business write was executed.

The local app server was stopped after verification; port 15075 was no longer listening. Original ZIP entries were not modified, and all six workbook SHA-256 values matched before/after.

## 8. Permissions And Remaining Blockers

- Session user thuctap_kho could access the four master import dialogs; Add and Nhập Excel were visible. No permission denial occurred on those pages.
- Inbound/Outbound routes were available, but the import capability was not exposed in their Công cụ menus. This is a feature/UI exposure blocker, not NO_PERMISSION, PARSER_RUNTIME_FAILURE, or REFERENCE_NOT_FOUND.
- Exact detected sheet/header row and warning counter are not shown in the current master Preview UI. They remain NOT EXPOSED.
- No code fix was made in this verification-only phase.

## 9. Recommended Next Phase

1. Keep these four master imports fail-closed until the customer decides how to represent the reported business semantics (warehouse site/status, category/product LOT/HSD, product UOM conversion/supplier/barcode, supplier tax/payment terms).
2. Add a dedicated, permission-gated Preview UI for transaction workbooks before attempting the Inbound 40/39 and Outbound stock/reference checks. Preserve PersistenceBlocked=true; do not Confirm or Post until a separate authorized phase.
3. Consider surfacing detected sheet/header row, machine error codes, and a distinct warning count in Preview diagnostics so the required runtime evidence is directly visible.

## 10. Verification Matrix Summary

| Workbook | Runtime classification | CanConfirm | Business data changed | Workbook changed |
|---|---|---|---|---|
| Warehouse | UNSUPPORTED_DOMAIN_CAPABILITY | false | No | No |
| Category | UNSUPPORTED_DOMAIN_CAPABILITY | false | No | No |
| Product | UNSUPPORTED_DOMAIN_CAPABILITY + missing references | false | No | No |
| Supplier | UNSUPPORTED_DOMAIN_CAPABILITY | false | No | No |
| Inbound | FEATURE_NOT_EXPOSED / UI_RUNTIME_BLOCKED | N/A | No | No |
| Outbound | FEATURE_NOT_EXPOSED / UI_RUNTIME_BLOCKED | N/A | No | No |

DB schema changed: **No**. Source code changed: **No**. Unit count: **10**. Inventory counts changed: **No**.
