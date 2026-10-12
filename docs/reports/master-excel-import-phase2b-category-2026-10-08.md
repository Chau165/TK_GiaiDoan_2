# Master Excel Import Phase 2B - Category

Date: 2026-10-08  
Verdict: `CATEGORY_IMPORT_RUNTIME_VERIFIED`

Category import is implemented end-to-end in source and runtime-verified against the approved Test DB. Real browser UAT with an authorized user remains outstanding.

## Contract and Template

- Route `/Kho/Loai_San_Pham`; Business DB function mapping `2017`. Add permission is resolved through the existing dynamic function contract and rechecked server-side for Preview and Confirm.
- Table/SP contract: `tbl_DM_Loai_San_Pham` / `F2017_sp_ins_Loai_San_Pham`. Batch writes use the existing insert procedure inside one outer transaction; server context supplies audit identity.
- Template: `TKS_Thuc_Tap_V11_Web/wwwroot/FileManagement/Template/Loai_San_Pham_Import_Template.xlsx`; one `LoaiSanPham` sheet with exact columns `Ma_LSP`, `Ten_LSP`, `Ghi_Chu`, header-only, no internal IDs or audit fields.
- Limits: `.xlsx`, 1 MiB, 100 data rows; required code max 100, required name max 200, optional note max 1000. Duplicate identity follows `SQL_Latin1_General_CP1_CI_AS` (case-insensitive, accent-sensitive); code and name are independently unique, and either existing code or name blocks the row.

## Changes

- Added typed Category workbook/row/error models, strict XLSX reader contract, validator, permission-aware import service, and Category import dialog.
- Added a Category-only Add-gated button and dialog integration in the shared master list. Successful Confirm emits one summary and rebinds the grid once; the Product import path is unchanged.
- After Unit and Category, the duplicated bounded-stream/ClosedXML cell-reading mechanics were extracted into `CMaster_Xlsx_Reader`. The Unit and Category public readers still return their typed models; per-master contracts and validators remain separate.

## Test DB and Runtime Evidence

- Approved target: `DESKTOP-NHQ7QPL\MSSQLSERVER19` / `TKS_Thuc_Tap_V11_C01C02_Test_20260906`.
- Initial parity inspection found `F2017_sp_ins_Loai_San_Pham` missing. Only that required stored procedure was synced from current source. Post-sync checks verified parameter sizes 100/200/1000, required and code-or-name duplicate checks, transaction/fence contract, and the Category unique indexes. No Business DB writes were made.
- Run prefix: `UAT_CAT_20261008_060600_2529F53437B1_`.
- Preview: 10 valid rows and `0` rows written.
- Confirm: 10 inserted in one batch. Preview rejected both existing-code and existing-name conflicts; direct database duplicate rejection also passed.
- Mid-batch invalid required code at row 3 rolled back the first insert; rollback-prefix count was `0`.
- Cleanup in `finally`: exact run prefix returned to `0` rows.

## Verification

- Import/parser/UI/export regression set after the reader refactor: 121 passed, 0 failed.
- Unit runtime integration after the reader refactor: 1 passed; preview no-write, 10-row insert, duplicate rejection, row-3 rollback, and cleanup passed. Prefix `UAT_UNIT_20261008_060556_543560F15A5D_` cleaned to zero.
- Category runtime integration after the reader refactor: 1 passed; evidence above.
- Product runtime regression after Category controller/service changes: 1 passed on the approved Test DB. The shared reader refactor does not modify the Product reader or export paths.
- Builds: Web_Common 0 errors (4 warnings), Web_Danh_Muc 0 errors (12 warnings), Web 0 errors (24 warnings). Existing Telerik license and code warnings were not changed.

## Remaining UAT and Next Phase

- Test DB still has no `/Kho/Loai_San_Pham` permission-function row. Business DB maps it to `2017`; the Test DB permission metadata was not changed. Automated authorization tests cover denial and Confirm recheck using a test resolver, but a live authorized Add session was not available.
- Manual browser UAT remains outstanding. Proceed next with Supplier / NCC only after this Category phase is closed.
