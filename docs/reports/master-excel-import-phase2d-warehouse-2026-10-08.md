# Master Excel Import Phase 2D - Warehouse / Kho

Date: 2026-10-08  
Verdict: `WAREHOUSE_IMPORT_RUNTIME_VERIFIED`

Warehouse import is implemented end-to-end in source and runtime-verified against the approved Test DB. Real browser UAT using live Add permission remains outstanding.

## Contract and Template

- Route `/Kho/Quan_Ly`; Business DB function mapping `2009`. The import action is Add-gated and server-side permission is rechecked for Preview and Confirm.
- Table/SP contract: `tbl_DM_Kho` / `F2009_sp_ins_Kho`; insert uses the existing master save command and server-supplied audit identity. Preview and Confirm revalidation use `F2009_sp_sel_List_Kho`.
- Template: `TKS_Thuc_Tap_V11_Web/wwwroot/FileManagement/Template/Kho_Import_Template.xlsx`; one `Kho` sheet, exact columns `Ten_Kho`, `Ghi_Chu`, header-only, no internal IDs or audit fields.
- Limits: `.xlsx`, 1 MiB, 100 data rows; required name max 255, optional note max 1000. Name uniqueness follows `SQL_Latin1_General_CP1_CI_AS` (case-insensitive, accent-sensitive).

## Changes

- Added typed Warehouse models, XLSX reader on the shared bounded ClosedXML utility, validator, permission-aware Preview/Confirm service, and Kho import dialog.
- Added a Kho-only Add-gated action/modal in the shared master list. Success records one summary history item and rebinds the grid once.
- Existing Product import and Telerik Excel/CSV export paths were preserved.

## Test DB and Runtime Evidence

- Approved target: `DESKTOP-NHQ7QPL\MSSQLSERVER19` / `TKS_Thuc_Tap_V11_C01C02_Test_20260906`.
- Initial parity inspection found `F2009_sp_ins_Kho`, `F2009_sp_sel_List_Kho`, and `F2009_sp_sel_List_Kho_Lookup` absent. All three exact source batches were synchronized because the existing page loads all master lookups during startup. Post-sync checks confirmed procedure parameters/guards/transaction-fence/projections and the unique CI_AS name index. Business DB remained read-only.
- Run prefix: `UAT_WH_20261008_062332_00F9DE930724_`.
- Preview: 10 valid rows and no writes (`0`). Confirm inserted 10 in one batch.
- Preview and direct database insertion rejected an existing name.
- Empty required name at row 3 failed after the first row had executed; transaction rollback left `0` rows under the rollback prefix.
- Cleanup in `finally` returned the exact run prefix to `0` rows.

## Verification

- Warehouse focused validation/parser/authorization/UI tests: 15 passed, 0 failed.
- Import/parser/UI/export regression set across all master imports: 152 passed, 0 failed.
- Warehouse runtime integration: 1 passed; evidence above.
- Product runtime regression after Warehouse source changes: 1 passed. The 100-row insert, duplicate race, row-50 rollback, and cleanup passed; prefix `UAT_PI_20261008_062413_6B6875AA520A_` returned to zero.
- Builds: Web_Common 0 errors (4 warnings), Web_Danh_Muc 0 errors (12 warnings), Web 0 errors (24 warnings). Existing Telerik license and code warnings were not changed.

## Remaining UAT

- Test DB `/Kho/Quan_Ly` currently maps to function `2010`; Business DB maps it to `2009`. The three other import routes have no Test DB function rows. No dynamic permission metadata was changed.
- Automated Add-denial and Confirm-recheck tests use explicit test resolvers; a real active-user Add permission and browser flow were not verified. Manual browser UAT remains outstanding.
