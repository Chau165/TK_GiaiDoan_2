# Master Excel Import Phase 2C - Supplier / NCC

Date: 2026-10-08  
Verdict: `SUPPLIER_IMPORT_RUNTIME_VERIFIED`

Supplier import is implemented end-to-end in source and runtime-verified against the approved Test DB. A real authorized browser session remains unverified.

## Contract and Template

- Route `/Kho/Nha_Cung_Cap`; Business DB function mapping `2019`. The import action is Add-gated and server-side permission is checked again for Preview and Confirm.
- Table/SP contract: `tbl_DM_NCC` / `F2019_sp_ins_NCC`, using the existing master save contract and server-provided audit identity. Preview/revalidation use `F2019_sp_sel_List_NCC`.
- Template: `TKS_Thuc_Tap_V11_Web/wwwroot/FileManagement/Template/Nha_Cung_Cap_Import_Template.xlsx`; one `NhaCungCap` sheet, exact columns `Ma_NCC`, `Ten_NCC`, `Ghi_Chu`, header-only, text-formatted code column, no internal IDs/audit fields.
- Limits: `.xlsx`, 1 MiB, 100 data rows; required code max 100, required name max 255, optional note max 1000. Duplicate identity follows `SQL_Latin1_General_CP1_CI_AS`; both code and name are independently unique and either conflict blocks import.

## Changes

- Added typed Supplier models, XLSX reader on the shared bounded ClosedXML utility, validator, permission-aware Preview/Confirm service, and Supplier import dialog.
- Added an NCC-only Add-gated action/modal in the shared master list. Success records one summary history item and rebinds the grid once.
- Preserved all existing Product import, Telerik export, and CSV export paths.

## Test DB and Runtime Evidence

- Approved target: `DESKTOP-NHQ7QPL\MSSQLSERVER19` / `TKS_Thuc_Tap_V11_C01C02_Test_20260906`.
- Initial parity inspection found Supplier insert/list/lookup procedures absent. The page's `Load_Data_Async` calls `Load_Lookups_Async`, so the import and page paths require all three. Only those exact source batches were synchronized. Post-sync checks confirmed all procedures, lookup projection, 100/255/1000 parameter sizes, required and code-or-name duplicate guards, transaction/fence behavior, and unique CI_AS indexes. No Business DB writes were made.
- Run prefix: `UAT_NCC_20261008_061317_62B879C5DB9C_`.
- Preview: 10 valid rows; no rows written (`0`). Confirm inserted 10 in one transaction.
- Preview rejected both existing-code and existing-name collisions; a direct DB duplicate attempt was rejected.
- A blank code at row 3 caused the first row in the batch to roll back; rollback-prefix count was `0`.
- Cleanup in `finally` returned the exact run prefix to `0` rows.

## Verification

- Supplier focused validation/parser/authorization/UI tests: 16 passed, 0 failed.
- Import/parser/UI/export regression set: 137 passed, 0 failed.
- Supplier runtime integration: 1 passed; evidence above.
- Product runtime regression after Supplier source changes: 1 passed; 100-row import, transaction, duplicate race, rollback, and cleanup passed. Prefix `UAT_PI_20261008_061349_F0D8FF8C0CB2_` was cleaned to zero.
- Builds: Web_Common 0 errors (4 warnings), Web_Danh_Muc 0 errors (12 warnings), Web 0 errors (24 warnings). Existing Telerik license and code warnings were not changed.

## Remaining UAT and Next Phase

- Test DB still has no `/Kho/Nha_Cung_Cap` function row; Business DB maps it to `2019`. The app's real Add permission and first-page browser workflow therefore were not demonstrated against Test DB. Automated tests exercise allow/deny and Confirm recheck through a test resolver.
- The page's required Supplier lookup procedure is now present, but this does not resolve the separate route-metadata gap. No dynamic permission metadata was copied or changed.
- Manual browser UAT remains outstanding. Proceed next with Warehouse / Kho.
