# Excel Import Current Architecture Audit

Audit date: 2026-10-09  
Repository: P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11  
Mode: read-only source, SQL and static-template inspection. No application code or database was changed.

## 1. Executive Summary

- Current design is TYPE B plus a limited shared TYPE C layer: multiple fixed import handlers share upload/Excel utilities, while mapping remains hard-coded per data type. Five newer warehouse master imports add dedicated readers/validators/services, a shared parser for four types, Preview and Confirm.
- This is not an arbitrary or customer-configurable Excel import engine. Multiple fixed formats exist, but aliases, arbitrary customer templates, template versions, configurable source-to-target mapping and transformation profiles were not found.
- Validation differs by generation. New master imports enforce workbook bounds, exact sheet/header contracts, row validation, duplicate/reference checks, Preview, Confirm revalidation and database checks. Legacy imports primarily trust fixed column positions and defer behavior to stored procedures.
- New master imports use one transaction for a whole batch. Legacy handlers commit each row independently, so partial persistence is expected when a later row fails.
- Current working-tree source contains pre-existing modified and untracked files, including import and SQL files. This report describes that working tree, not a clean commit or deployed runtime. No live DB was queried.

Finding totals: 0 CRITICAL, 1 HIGH, 4 MEDIUM, 2 LOW, 2 INFO.

## 2. Import Feature Inventory

No HTTP import endpoint was found in these flows. Entry points are Blazor pages/components and event handlers.

| Import type | UI/route | Handler/service | Template | Destination / status |
|---|---|---|---|---|
| Product | /Kho/San_Pham, warehouse master tool menu | FWarehouse_Product_Import; CWarehouseProductImportService; CWarehouseMaster_Controller | San_Pham_Import_Template.xlsx; SanPham; five exact columns | dbo.tbl_DM_San_Pham via F2018_sp_ins_San_Pham; IMPLEMENTED |
| Unit | /Kho/Don_Vi_Tinh | FWarehouse_Unit_Import; CWarehouseUnitImportService | Don_Vi_Tinh_Import_Template.xlsx; DonViTinh; two exact columns | dbo.tbl_DM_Don_Vi_Tinh via F2016_sp_ins_Don_Vi_Tinh; IMPLEMENTED |
| Category | /Kho/Loai_San_Pham | FWarehouse_Category_Import; CWarehouseCategoryImportService | Loai_San_Pham_Import_Template.xlsx; LoaiSanPham; three exact columns | dbo.tbl_DM_Loai_San_Pham via F2017_sp_ins_Loai_San_Pham; IMPLEMENTED |
| Supplier | /Kho/Nha_Cung_Cap | FWarehouse_Supplier_Import; CWarehouseSupplierImportService | Nha_Cung_Cap_Import_Template.xlsx; NhaCungCap; three exact columns | dbo.tbl_DM_NCC via F2019_sp_ins_NCC; IMPLEMENTED |
| Warehouse | /Kho/Quan_Ly | FWarehouse_Kho_Import; CWarehouseKhoImportService | Kho_Import_Template.xlsx; Kho; two exact columns | dbo.tbl_DM_Kho via F2009_sp_ins_Kho; IMPLEMENTED |
| Chu Hang | /Danh_Muc/Chu_Hang | F2003_1_Chu_Hang_List.Import_Excel_Entry | Chu_Hang_Template.xlsx; A:I positional mapping | CDM_Chu_Hang/FQ_104_CH_sp_ins_Insert; IMPLEMENTED, SQL behavior not source-verifiable |
| Member | /Sys/Thanh_Vien | F1003_1_Thanh_Vien_List.Import_Excel_Entry | UI references Thanh_Vien_Template.xlsx; handler maps A:G; file absent from checked Template folder | CSys_Thanh_Vien/FQ_531_TV_sp_ins_Insert; PARTIAL |
| Field color | /Sys/Dinh_Nghia_Mau_Column | F1013_1_Dinh_Nghia_Mau_Column_List.Import_Excel_Entry | Dinh_Nghia_Mau_Column_Template.xlsx; A:C | CSys_Mau_Column/FQ_522_MC_sp_ins_Insert; IMPLEMENTED, SQL behavior not source-verifiable |
| Multilanguage | /Sys/Multilanguage | F1014_1_Multilanguage_List.Import_Excel_Entry | MultiLanguage_Template.xlsx; A:G | CSys_Language/FQ_521_L_sp_ins_Insert; IMPLEMENTED, SQL behavior not source-verifiable |
| Webhook config | /Sys/Webhook_Config | F1016_1_Webhook_Config_List override exists; page passes empty template name | No template found; A:C handler | CSys_Webhook/FQ_533_W_sp_ins_Insert; DEAD CODE/unreachable via page UI |
| Hide/show column | /Sys/Hien_An_Column | F1020_1_Hien_An_Column_List.Import_Excel_Entry | Hien_An_Column_Template.xlsx; A:D | CSys_Hien_An_Column/FQ_519_HAC insert/update; IMPLEMENTED, SQL behavior not source-verifiable |
| Column width | /Sys/Dieu_Chinh_Do_Rong_Cot | F1027_1_Dieu_Chinh_Do_Rong_Cot_List.Import_Excel_Entry | Dieu_Chinh_Do_Rong_Cot_Template.xlsx; A:C | CSys_Column_Width/FQ_509_CW_sp_ins_Insert; IMPLEMENTED, SQL behavior not source-verifiable |
| Drill-down config | /Sys/Drill_Down | F1019_1_Drill_Down_List.Import_Excel_Entry | Drill_Down_Template.xlsx; A:D | CSys_Drill_Down/FQ_511_DD_sp_ins_Insert; IMPLEMENTED, SQL behavior not source-verifiable |
| PDA groups | /Sys/Nhom_PDA | F1036_1_Nhom_PDA_List.Import_Excel_Entry | Nhom_PDA_Template.xlsx; A:B | CSys_Nhom_PDA/FQ_523_NP_sp_ins_Insert; IMPLEMENTED, SQL behavior not source-verifiable |
| Grid fields | /Sys/Cau_Hinh_Grid_Field | F1044_1_Cau_Hinh_Grid_Field_List.Import_Excel_Entry; explicit button | Cau_Hinh_Grid_Field_Template.xlsx; A:G | CSys_Grid_Field/FQ_514_GF_sp_ins_Insert; IMPLEMENTED, SQL behavior not source-verifiable |
| Global grid UI | /Sys/Cau_Hinh_Grid_UI_Global | F1045_1_Cau_Hinh_Grid_UI_Global_List.Import_Excel_Entry; explicit button | Cau_Hinh_Grid_UI_Global_Template.xlsx; A:E | CSys_Grid_UI_Global/FQ_516_GUG_sp_ins_Insert; IMPLEMENTED, SQL behavior not source-verifiable |

No Excel import for receipt/inbound or issue/outbound documents was found. Existing transaction pages/export controls are not import evidence.

Legacy handler evidence (method, input range, and persistence call): F2003_1_Chu_Hang_List.razor:119, 128-161; F1003_1_Thanh_Vien_List.razor:120, 129-160; F1013_1_Dinh_Nghia_Mau_Column_List.razor:113, 122-149; F1014_1_Multilanguage_List.razor:117, 126-157; F1016_1_Webhook_Config_List.razor:116, 125-152; F1019_1_Drill_Down_List.razor:114, 123-151; F1020_1_Hien_An_Column_List.razor:113, 122-172; F1027_1_Dieu_Chinh_Do_Rong_Cot_List.razor:114, 123-151; F1036_1_Nhom_PDA_List.razor:115, 124-150; F1044_1_Cau_Hinh_Grid_Field_List.razor:161, 170-201; F1045_1_Cau_Hinh_Grid_UI_Global_List.razor:402, 411-452. UI wiring evidence: FBase.razor:1808-1815, F1016_1_Webhook_Config_List.razor:3, F1044_1_Cau_Hinh_Grid_Field_List.razor:33, F1045_1_Cau_Hinh_Grid_UI_Global_List.razor:28.

## 3. End-to-End Flow

### New warehouse master imports

User opens a master route, chooses a supported type, and selects Nhập Excel from the tool menu. The menu is gated by Add permission. The modal links a static template and accepts one .xlsx up to 1 MiB.

Upload → FBase.Read_Import_File_Async → type-specific service PreviewAsync → ClosedXML reader → sheet/shape/header/data validation → load current records and Product references from SQL → in-memory preview → Confirm → permission check and fresh revalidation → CWarehouseMaster_Controller.Import_<type>_Async → one SQL transaction and per-row SP calls → commit whole batch or rollback whole batch → action history and grid refresh.

Evidence: FWarehouse_1_Warehouse_List.razor:40-43, 275-293, 602-724; FWarehouse_Product_Import.razor:12-20, 120-206; CWarehouseProductImportService.cs:39-80; CProduct_Xlsx_Reader.cs; CProductImportValidator.cs; CWarehouseMaster_Controller.cs:117-178.

### Legacy shared Excel imports

User opens a page import action → FBase.Open_Import_Excel opens FCommon_Import_Excel → Telerik FileSelect accepts .xlsx/.xls and copies the file to a dated Import_Excel folder → FBase.Close_Import_Excel calls Import_Excel → queue/loading/log setup → EPPlus CExcel_Controller opens FileInfo → virtual Import_Excel_Entry override reads fixed A2-to-column range → fixed DataRow ordinals are converted into an entity → data-access controller calls a stored procedure → row transaction commits/rolls back → cache/action-history updates and row messages → shared import log call and grid reload.

Evidence: FBase.razor:279-284, 343-410, 1692-1718; FCommon_Import_Excel.razor:9-20, 39-91; CExcel_Controller.cs:20-25, 91-156; representative handler F2003_1_Chu_Hang_List.razor:119-190.

## 4. Component Map

| Layer | Location | Responsibility |
|---|---|---|
| Master menu | Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_1_Warehouse_List.razor | Selects one of five import modals, gates Add permission, refreshes grid |
| Master UI | Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_*_Import.razor | File selection, template download, Preview/Confirm state, error rendering |
| Master services | Web_Danh_Muc/Services/CWarehouse*ImportService.cs | Permission, file/read/lookup/validate, Confirm revalidation and persistence call |
| Master validators/readers | Web_Danh_Muc/Services/C*ImportValidator.cs; Data_Access/Utility/C*Xlsx*Reader.cs | Fixed schema validation and XLSX parsing |
| Master persistence/SQL | Data_Access/Controller/Warehouse/CWarehouseMaster_Controller.cs; Database/WarehouseModule.*.sql | Type-to-SP mapping, transactions, business checks and schema |
| Legacy uploader/orchestrator | Web_Common/Common/FCommon_Import_Excel.razor; FBase.razor | Upload, queue/loading, virtual dispatch, common result/log path |
| Legacy parser | Data_Access/Utility/CExcel_Controller.cs | EPPlus workbook and range-to-DataTable conversion |
| Legacy handlers | Web_Danh_Muc/.../F2003_1_Chu_Hang_List.razor; Web_Sys/.../F10*_List.razor | Per-type positional mapping, controller/SP calls, per-row transactions |
| Import-config admin/cache | Web_Sys/.../F1018_*; Data_Access/Controller/Cache/CCache_Import_Excel_Template_Config.cs | Owner-ID CRUD/cache; not consumed by runtime row mapping |
| Common import log | FBase.razor:343-391 | References CLog_Import_Excel_Controller/FQ_422_IE_sp_ins_Insert; class definition not found in searched source |

No separate import API controller, staging model/table, background job, or generic DI-registered import service was found in the inspected call paths. New services are constructed by their Razor components.

## 5. Template Analysis

Static workbooks are under TKS_Thuc_Tap_V11_Web/wwwroot/FileManagement/Template. Fourteen were present: five new master templates and nine legacy templates. No dynamic template generator, selector, or importer-side version/profile selector was found.

| Template type | Sheet | Observed row-1 headers |
|---|---|---|
| Product | SanPham | Ma_San_Pham, Ten_San_Pham, Ma_LSP, Ten_Don_Vi_Tinh, Ghi_Chu |
| Unit | DonViTinh | Ten_Don_Vi_Tinh, Ghi_Chu |
| Category | LoaiSanPham | Ma_LSP, Ten_LSP, Ghi_Chu |
| Supplier | NhaCungCap | Ma_NCC, Ten_NCC, Ghi_Chu |
| Warehouse | Kho | Ten_Kho, Ghi_Chu |
| Chu Hang | Sheet1 | Mã Chủ Hàng, Tên Viết Tắt, Tên Chủ Hàng, Địa Chỉ, Điện Thoại, Email, MST, Header Title, Ghi Chú |
| Grid field | Sheet1 | Tên Grid, Mã Chức Năng, Field Name, Tiêu đề Column, Column Width, Field_Type_ID; later columns are blank/instruction area |
| Global grid UI | Sheet1 | Tên Grid, Mã Chức Năng, Field Name, Column Width, Sắp xếp |
| Column width | Sheet1 | Field name, Độ Rộng Cột, Number Format |
| Field color | Sheet1 | Field name, Mã số màu, Ghi chú |
| Drill down | Sheet1 | Field name, Link URL, Parameter Field, Function |
| Hide column | Sheet1 | Chủ hàng (-5 = Tất cả), Field Name, Hiện / Ẩn (=1: Ẩn, =2: Hiện), Mã Chức Năng |
| Multilanguage | Sheet1 | Field name, Việt Nam, English, Japanese, Chinese, Korean, Loại (=1: Cơ bản) |
| PDA group | Sheet1 | Tên Nhóm PDA, Ghi Chú |

The member page references Thanh_Vien_Template.xlsx, absent from the checked-in Template folder. Webhook has no template reference and its page passes an empty template name. Grid-field code reads through G and maps it to Field_Name_Parent, but the supplied workbook has no meaningful G header.

For new imports, each type has one versionless static format. The four shared-reader imports require exactly one worksheet and an exact sheet name. Product selects the required SanPham worksheet by name but does not reject unrelated extra sheets. Type is selected by the UI, not inferred from headers.

## 6. Column Mapping Analysis

New imports use a two-stage fixed contract: exact header text/order validation, then fixed physical cell positions in CreateInputRow. Headers do not dynamically bind to fields. Product contract is in CProductImportValidator.cs:9-12, validation at :136-198, positional mapping at :236-275.

Legacy handlers use fixed worksheet index/range and fixed DataRow ordinals, without reading header labels. Examples:

- Chu Hang: A2:I, indices 0-8 to entity properties, F2003_1_Chu_Hang_List.razor:128-161.
- Member: A2:G to login/password/name/email/phone/status/note, with MD5 password transformation, F1003_1_Thanh_Vien_List.razor:129-160.
- Grid field: A2:G, F1044_1_Cau_Hinh_Grid_Field_List.razor:170-201.
- Hide column: A2:D and update-or-insert by an existing owner/field/function tuple, F1020_1_Hien_An_Column_List.razor:122-172.
- Global grid UI: A2:E; resolves Grid_Field_ID from cache by function/grid/field key, F1045_1_Cau_Hinh_Grid_UI_Global_List.razor:411-452.

Classification: physical column-index mapping is used by both families; exact-header validation is additionally applied only to new master imports. Alias mapping, reorder-by-header, reflection mapping, auto-detection, and configurable mapping were not found.

## 7. Hard-coded Assumptions

| ID | Evidence | Assumption | Risk and impact |
|---|---|---|---|
| H-01 | Legacy handlers, e.g. F2003 lines 128-161 | Fixed worksheet index, row start, end column and ordinal-to-property assignments; no header validation | HIGH: inserted/reordered columns can silently map wrong values |
| H-02 | CProductImportValidator.cs:9-12, 136-198; CMaster_Xlsx_Reader.cs:38-44 | Exact sheet/header names/order and type-specific column cap | ACCEPTABLE for controlled templates, restrictive for customer files |
| H-03 | F1003_1_Thanh_Vien_List.razor:3; static Template folder | Member template URL refers to a missing checked-in asset | MEDIUM: standard template download unavailable from source assets |
| H-04 | F1016_1_Webhook_Config_List.razor:3, 75, 116; FBase.razor:1808-1815 | Handler exists but empty template suppresses the base import button | LOW: handler is unreachable through inspected UI |
| H-05 | F1018_3_Import_Excel_Config_Edit.razor:21-32, 76-102; CCache_Import_Excel_Template_Config.cs:62-76 | Config/cache associates owner ID but runtime import mapping does not read it | MEDIUM: not per-owner column/template mapping |
| H-06 | F2003 handler:142-175; master controller:127-178 | Legacy transaction boundary is per row; master is whole batch | MEDIUM: legacy files can partially persist |
| H-07 | CMaster_Xlsx_Reader.cs:7-8, 62-67; CProduct_Xlsx_Reader.cs:7-9 | New import limit is 1 MiB, 100 rows and fixed columns | LOW: larger files must be split |
| H-08 | F1044 handler:170-201 vs actual Grid_Field template | G is mapped to Field_Name_Parent despite no meaningful G header | LOW: contract is not discoverable in provided workbook |

Acceptable hard-coding includes type-specific field contracts and limits for controlled templates. The high-risk hard-coding is legacy positional mapping because labels are not validated and semantic shifts may be silent.

## 8. Master Data Import

Five master types have separate modals and services. They use ClosedXML; four readers delegate parsing to CMaster_Xlsx_Reader, while Product uses CProduct_Xlsx_Reader with similar bounds but different worksheet-count policy.

All are create-only. Validators check required values/lengths, duplicates against existing records and within the file, and blank rows. Product also resolves Ma_LSP to one category and Ten_Don_Vi_Tinh to one unit, storing IDs in the import model. Confirm reloads references and revalidates.

Controller maps each fixed type to one insert SP. IDs come from SQL; user/function audit fields come from the UI context; timestamps default in schema. Product table has category/unit FKs. Checked SQL defines unique indexes for unit name, category code/name, product code, supplier code/name, warehouse name.

## 9. Transaction Import

No Excel import was found for receipt or issue documents. Warehouse_Nhap_Kho and Warehouse_Xuat_Kho render the warehouse list in Receipt/Issue sections. Their export controls do not connect a file input, reader, handler, service, or Excel write path for those transaction types.

## 10. Inbound Import Deep Dive

NOT FOUND in current import source. No inbound workbook, receipt header/detail importer, grouping key, orphan-detail rule, or Excel call to receipt insert/posting procedures was found. This is distinct from receipt UI and export functionality.

## 11. Outbound Import Deep Dive

NOT FOUND in current import source. No outbound workbook, issue header/detail importer, grouping key, orphan-detail rule, or Excel call to issue insert/posting procedures was found. This is distinct from issue UI and export functionality.

## 12. Validation Pipeline

| Area | New five master imports | Legacy imports |
|---|---|---|
| Extension/size | .xlsx only; 1 MiB enforced while reading | FileSelect allows .xlsx/.xls and 20,480,000 bytes; extension checked before disk copy |
| Empty/corrupt | Reader/parser error becomes invalid-file error | EPPlus/range exception is surfaced; no Preview |
| Sheet/header | Exact required sheet, row-1 headers, exact names/order, duplicate/unknown/missing/formula checks | Worksheet by index, fixed A2 range, no header check |
| Data | Required/length checks; blank rows ignored; formulas rejected | Utility string/int conversion; removes rows with blank first cell; row exceptions displayed |
| References | Product category code/unit name must resolve uniquely; existing product codes checked | Some handler-specific cache/list logic; no uniform validator |
| DB | New schema/SP checks are source-visible | Legacy procedure/schema definitions not source-visible |

The five new templates have no transaction quantity/date/boolean fields. No inbound/outbound business-validation path exists in Excel source.

## 13. Error Handling

New Preview returns total/valid/invalid counts plus errors carrying row number, column name and message. Confirm is enabled only for a nonempty all-valid preview. No error workbook is generated. A DB write error includes the row number and aborts/rolls back the whole batch.

Legacy handlers catch row errors, append a “Dòng N có lỗi” message, roll back the current transaction and continue. FBase reports success/partial/failure counts. There is no pre-import Preview or shared structured result DTO in this path. In the common pattern, success is incremented before Commit and post-commit cache/history work is still inside the catch region; if such later work fails, displayed counters may differ from committed state.

## 14. Transaction / Rollback

New master: one connection and transaction per Confirm batch, one SP call per row, commit after all rows, rollback attempt on any exception. SQL procedures start their own transaction only when no outer transaction exists and use XACT_ABORT ON. Intended result: later failure rolls back the batch.

Legacy: a connection and transaction are opened inside the row loop. Each row commits independently before cache/action-history updates. A later row failure does not undo earlier rows. A 100-row file can open up to 100 row connections/transactions.

## 15. Duplicate / Idempotency

No file hash, ImportBatchId, IdempotencyKey or replay ledger was found.

New master Preview/Confirm rejects business keys already in DB or duplicated in the input. Reupload after success should be rejected by key validation, not skipped/updated; this is duplicate protection, not file-level idempotency.

Most legacy handlers insert every row and have no source-level in-file dedupe. F1020 preloads hide-column records and updates matching owner/field/function keys or inserts otherwise. Repeated legacy-file behavior cannot be fully established without missing SP definitions, DB constraints and live state.

## 16. Database & Stored Procedure Integration

### New master imports: SQL source present

Database/WarehouseModule.Procedures.sql defines:

- F2016_sp_ins_Don_Vi_Tinh
- F2017_sp_ins_Loai_San_Pham
- F2018_sp_ins_San_Pham
- F2019_sp_ins_NCC
- F2009_sp_ins_Kho
- F2009/F2016/F2017/F2018/F2019 list and lookup procedures, sp_DM_Master_Page and sp_DM_Lookup_Page

Insert procedures are around lines 3903-3989 and 5052-5228. WarehouseModule.Schema.sql defines master tables, Product FKs and unique indexes around lines 38-95 and 174-180. The procedures call sp_Inventory_Fence_Acquire_Root. This allows source-level master persistence review, but does not prove deployed DB parity.

### Legacy imports: SQL source missing

The following handler calls were found, but no CREATE PROCEDURE definitions were found in the repository SQL search:

- FQ_104_CH_sp_ins_Insert
- FQ_531_TV_sp_ins_Insert
- FQ_522_MC_sp_ins_Insert
- FQ_521_L_sp_ins_Insert
- FQ_533_W_sp_ins_Insert
- FQ_511_DD_sp_ins_Insert
- FQ_519_HAC_sp_sel_List, FQ_519_HAC_sp_ins_Insert, FQ_519_HAC_sp_upd_Update
- FQ_509_CW_sp_ins_Insert
- FQ_523_NP_sp_ins_Insert
- FQ_514_GF_sp_ins_Insert
- FQ_516_GUG_sp_ins_Insert

FBase also calls FQ_422_IE_sp_ins_Insert for import logging. CLog_Import_Excel_Controller is referenced by FBase and the import-log page but no definition was found in searched project source. F1018 config CRUD calls FQ_520_IETC_* procedures; definitions are also absent from checked SQL.

The legacy controllers call CSqlHelper with stored-procedure names and parameters (CSqlHelper.cs:105-163, 434-565). Their physical table writes, DB validation, triggers, uniqueness, transaction behavior and logging side effects cannot be verified from checked-in legacy SQL.

## 17. Preview / Staging Support

The five new master imports implement Upload → Preview → Validate → Confirm → Save in memory. Preview is not persisted to a staging table. Confirm reloads references and validates before writes.

Legacy imports save immediately after clicking Import Excel. No staging table or ImportBatch/ImportJob workflow identifier was found.

## 18. Performance Risks

No benchmark was run.

- New master imports cap a file at 100 rows. Product Preview/Confirm preload categories and units and fetch existing product codes in pages of 500. Writes are one SP call per row over one connection/transaction, not bulk insert. A very large product catalog increases reference paging.
- Other new imports preload existing master data for duplicate checks, then call one write procedure per row.
- Legacy imports open one connection/transaction per row and call action-history logic after successful writes. F1020 reads its existing list once, then writes row by row.
- At 1,000/10,000 rows, the new import rejects one-file input; the legacy path has no visible row cap but incurs O(N) connection/procedure/history costs. No timing estimate is claimed.

## 19. Import Dependency Graph

Category → Product (category code resolves to category ID)  
Unit → Product (unit name resolves to unit ID)  
Existing Product Codes → Product import duplicate validation  
Supplier and Warehouse have no import-time dependency in their handlers.  
Receipt/Issue → no Excel import node found.

## 20. Architecture Classification

CURRENT ARCHITECTURE = TYPE B + limited TYPE C, not TYPE D.

- TYPE B dominates: separate fixed importers with compiled-in sheet/column/DB contracts.
- TYPE C exists only as shared upload/orchestration/parser/result patterns. It does not supply metadata-driven mapping.
- TYPE A describes each individual fixed importer, not the project as a whole.
- TYPE D is absent. The F1018 model stores Chu_Hang_ID but no source/target mapping, aliases, transformations or validation rules; runtime import handlers do not consume it.

## 21. Answers to Q1-Q30

Q1. Product, Unit, Category, Supplier, Warehouse, Chu Hang, Member, field color, multilanguage, hide/show columns, column width, drill-down, PDA groups, grid fields and global grid UI have handlers. Webhook has an unreachable handler. Inbound/Outbound Excel import was not found.

Q2. Yes, separate static templates for the five new master types and nine legacy wired types. Member’s referenced file is missing; Webhook has no template and no reachable UI import action.

Q3. No arbitrary multiple formats for one type, no customer profiles and no template versioning found.

Q4. New imports require exact header text and order. Legacy imports ignore header labels but require expected values in fixed positions.

Q5. New mapping is in validators/CreateInputRow, service reference resolution, CWarehouseMaster_Controller and SQL. Legacy mapping is in each Razor Import_Excel_Entry assignment and controller/SP call.

Q6. Both use physical column indexes. New imports also validate exact headers/order; legacy imports do not.

Q7. No runtime configurable mapping. F1018 config is owner-ID cache/CRUD, not column mapping, and is not read by import handlers.

Q8. No per-customer/tenant mapping profile. F1020 reads Chu_Hang_ID as an ordinary fixed input field.

Q9. New imports reject unknown/extra columns. Legacy reads a fixed range; trailing columns outside it are ignored, inserted columns inside it can shift mappings.

Q10. New imports report missing required headers. Legacy does not check header presence; absent positional cells may error or become blank/default, with exact DB behavior unknown.

Q11. New IDs come from SQL; timestamps from schema defaults; audit user/function from current UI context; Product category/unit IDs from lookups. Legacy sets audit values and some transforms in C#; other defaults are unknown without SP definitions.

Q12. Product resolves category code and unit name against preloaded data, requires a unique match and passes matched IDs to the product SP. There is no generic resolver.

Q13. Limited transforms: cell-to-string conversion, trimmed/case-normalized comparisons, Product code/name-to-ID lookup and MD5 password hashing in Member import. No generic date/decimal/boolean/value mapping found.

Q14. Yes, full pre-save validation in new master imports and DB checks in checked SQL. Legacy validation is limited and depends heavily on conversions/SPs.

Q15. Yes for five new master imports; no for legacy.

Q16. No staging table. New Preview is in memory.

Q17. Yes: one batch transaction for new imports, one transaction per row for legacy.

Q18. New controller attempts full-batch rollback. Legacy rolls back only the current row; prior committed rows remain.

Q19. New master: one write failure aborts and rolls back batch. Legacy: failed row is reported and later rows continue, with earlier successes persisted.

Q20. No file idempotency key/hash. New imports reject existing business keys. Legacy replay/dedup behavior is partly unknown; source lacks generic replay checks. F1020 updates a matching existing key.

Q21. No inbound/outbound Header+Detail Excel flow found, so no grouping/orphan rules.

Q22. New imports use master controller/SPs and database constraints, but no separate domain-service layer was found. Legacy builds entities in UI and calls controllers/SPs; reuse of hidden DB business rules cannot be verified.

Q23. New insert SPs: F2016_sp_ins_Don_Vi_Tinh, F2017_sp_ins_Loai_San_Pham, F2018_sp_ins_San_Pham, F2019_sp_ins_NCC, F2009_sp_ins_Kho, plus list/page/reference SPs in Section 16. Legacy SP names and shared log/config calls are listed in Section 16; definitions are missing from checked SQL.

Q24. New writes one SP per row, with preloaded lookups; product codes page in batches of 500. Legacy opens a connection/transaction and records action history per row. No bulk insert/chunk commit found.

Q25. No. SKU/Description/Unit/Qty is not the required Product schema; category is missing and header names are unknown to the validator. No arbitrary customer-file importer exists.

Q26. New imports reject extra columns. Legacy trailing columns beyond fixed range are ignored; inserted columns inside the range can shift mappings.

Q27. New imports reject reordering. Legacy reads positions and may silently map values incorrectly.

Q28. New shared readers require exact sheet names. Product requires SanPham but permits unrelated extra sheets. Legacy selects by worksheet index and does not validate its name.

Q29. New import requires header row 1. Legacy starts at A2 and treats subsequent rows as data; a row-5 header is not auto-detected.

Q30. Not generic arbitrary Excel import. It is a collection of predefined, source-coded formats behind some shared upload/parser utilities.

## 22. Findings

### HIGH

H1. Legacy positional mapping may silently assign values to the wrong fields after column reorder/insertion. Handlers directly assign fixed ordinals and do not validate header semantics.

### MEDIUM

M1. Legacy SP definitions, import-log controller source and legacy schema/constraints are absent from searched repository sources, preventing full verification of DB validation, destinations and duplicate behavior.

M2. Legacy commits each row independently; imports can partially persist.

M3. F1018 Import Excel Config does not implement mapping configuration and runtime handlers do not consume its cache.

M4. Member import points to Thanh_Vien_Template.xlsx, absent from checked-in static templates.

### LOW

L1. Webhook handler is unreachable from the page because the page passes an empty template name and the base button requires a nonempty template name.

L2. Grid-field code maps column G to Field_Name_Parent although the provided template has no meaningful G header.

### INFO

I1. New master imports are intentionally limited to 1 MiB and 100 rows.

I2. Receipt/Issue Excel import is NOT FOUND; current export controls do not establish import support.

## 23. Unknown / Cannot Verify

1. Needed legacy procedure definitions or redacted module exports: FQ_104_CH_sp_ins_Insert; FQ_531_TV_sp_ins_Insert; FQ_522_MC_sp_ins_Insert; FQ_521_L_sp_ins_Insert; FQ_533_W_sp_ins_Insert; FQ_511_DD_sp_ins_Insert; FQ_519_HAC_sp_sel_List/FQ_519_HAC_sp_ins_Insert/FQ_519_HAC_sp_upd_Update; FQ_509_CW_sp_ins_Insert; FQ_523_NP_sp_ins_Insert; FQ_514_GF_sp_ins_Insert; FQ_516_GUG_sp_ins_Insert.
2. Source/assembly for CLog_Import_Excel_Controller and SQL definition for FQ_422_IE_sp_ins_Insert.
3. If reviewing F1018 admin/cache behavior, definitions for FQ_520_IETC_* and its associated table schema.
4. Deployed database definitions/parity evidence for any production-state conclusion. This audit did not query a live DB.
5. Deployed static files may differ; missing Member template finding is bounded to the checked-in template folder.

## 24. Final Current-State Diagram

MASTER DATA (five types):

Route + selected type + Add permission  
→ Nhập Excel menu  
→ type-specific modal and static template  
→ bounded .xlsx read  
→ reader + exact sheet/header/row validation  
→ preload lookups and validate duplicates/references  
→ in-memory Preview  
→ Confirm permission check + revalidation  
→ one batch SQL transaction  
→ type-specific insert SP + SQL checks  
→ commit whole batch OR rollback whole batch  
→ action history + grid refresh

LEGACY (fixed types):

Page import action  
→ shared Telerik file picker (.xlsx/.xls)  
→ dated local upload file  
→ FBase queue/log orchestration  
→ EPPlus range parser at fixed sheet index and A2-to-fixed-column range  
→ handler-specific ordinal-to-entity assignments  
→ legacy controller/SP (SQL definition absent in checked-in source)  
→ transaction per row; commit/rollback row; continue  
→ row messages + shared log call (controller/SP source incomplete)

TRANSACTION DOCUMENTS: Inbound/Outbound Excel import → NOT FOUND.

## 25. Reviewer Handoff

The repository contains two generations rather than one uniform import engine. The legacy generation centralizes upload, queue/loading, EPPlus opening and summary behavior in FCommon_Import_Excel and FBase. Each list Razor component decides what each cell means. Handlers use fixed ranges and ordinals; they do not bind by header text. This makes the upload facility reusable, but each importer rigid. Reordering/inserting columns can silently shift semantics. Trailing columns outside a configured range may be ignored.

The newer warehouse master imports have stronger file-boundary controls. Five dedicated modals/services download one static template, preview in memory and require Confirm. Readers cap files at 1 MiB/100 rows. Validators check exact sheet/header names and order, formulas, required lengths, duplicates and Product references. Confirm checks permission again, reloads references, revalidates and then writes. The controller uses one transaction for the batch; checked SQL includes unique indexes, Product FKs and repeated business checks.

The newer path remains fixed-format, not configurable: no aliases, dynamic column mapping, version selection, per-customer profiles or staging table. F1018 “Import Excel Config” stores/loads owner-ID configuration but does not feed runtime mapping. A previously unknown customer workbook cannot be imported unchanged.

Important positives are bounded parsing and Preview in new master imports, revalidation at Confirm, database constraints in checked SQL and batch rollback. Priorities for the next reviewer are legacy positional mapping, per-row commit behavior, missing Member template, unreachable Webhook import, and missing legacy SQL/controller definitions. Do not infer legacy uniqueness, atomicity or business validation from stored-procedure names alone.

No Excel import for inbound/outbound documents was found. If a reviewer expects transaction import, they must first establish whether code exists outside this repository. This audit describes the current working tree, which already contains uncommitted/untracked changes; it does not establish clean-commit contents, deployed files, live DB parity or production behavior.
