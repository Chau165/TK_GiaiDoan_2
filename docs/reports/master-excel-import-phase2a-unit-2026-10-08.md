# Master Excel Import Phase 2A - Unit

Date: 2026-10-08  
Verdict: `UNIT_IMPORT_RUNTIME_VERIFIED`

The Unit import path is implemented and verified against the approved Test DB. Browser UAT with a real user session remains external and is not claimed as complete.

## Contract and Template

- Route `/Kho/Don_Vi_Tinh`; Business DB dynamic function code `2016`; Add permission is resolved from the active function and checked server-side on both Preview and Confirm.
- Table/SP contract: `tbl_DM_Don_Vi_Tinh`; `F2016_sp_ins_Don_Vi_Tinh`; existing Unit list/lookup procedures. The insert remains through the stored procedure; audit identity comes from the server-side user/function context.
- Template: `TKS_Thuc_Tap_V11_Web/wwwroot/FileManagement/Template/Don_Vi_Tinh_Import_Template.xlsx`; one `DonViTinh` sheet, exact header order `Ten_Don_Vi_Tinh`, `Ghi_Chu`, header-only, no internal IDs or audit fields.
- Limits: `.xlsx`, 1 MiB, 100 data rows, name required/max 200, note optional/max 1000. Name comparison follows the Unit's CI_AS business identity: case-insensitive and accent-sensitive. No `IgnoreNonSpace` is used.

## Changes

- Added typed Unit XLSX reader, row/preview/error models, validator, and import service.
- Added `CWarehouseMaster_Controller.Import_Units_Async`: one connection and outer transaction for the insert batch, existing F2016 procedure calls, row-level safe error mapping, rollback on any failure.
- Added Unit import dialog with template download, bounded file selection, Preview counts/errors, gated Confirm, loading/success states, and one grid rebind plus one summary history event after success.
- Added focused validation, parser, authorization, template/UI, and runtime integration tests. Product import code and Telerik export paths were not changed.

## Runtime Evidence

Approved target: `DESKTOP-NHQ7QPL\MSSQLSERVER19` / `TKS_Thuc_Tap_V11_C01C02_Test_20260906`.

- Preflight confirmed Unit table/index and F2016 insert/update/list/lookup objects; the target insert procedure has the expected CI_AS, required-name, and duplicate checks. No Phase 2 DB sync was needed. Business DB was read-only.
- Run prefix: `UAT_UNIT_20261008_055510_3CACE7F8573C_`.
- Preview: 10 valid rows; prefix count stayed `0 -> 0`.
- Confirm: inserted 10 rows in one batch.
- Duplicate: Preview rejected a DB-existing name; direct transactional insert was also rejected by the database duplicate rule.
- Mid-batch failure: first row was inserted inside the transaction, second attempted row failed F2016 required-name validation; post-rollback prefix count was `0`.
- Cleanup: exact test prefix returned to `0` rows in `finally`.

## Verification

- Unit tests: 22 passed, 0 failed.
- Product/export/parser/UI regression: 83 passed, 0 failed.
- Product runtime regression: 1 passed; 100-row import, duplicate-race handling, rollback and cleanup passed on the approved Test DB; exact run prefix `UAT_PI_20261008_055248_5FD82967B256_` was cleaned to zero.
- Builds: Web_Common 0 errors (4 warnings); Web_Danh_Muc 0 errors (12 warnings); Web 0 errors (24 warnings). Existing Telerik-license and CS1998 warnings were not modified.

## Remaining UAT

- Test DB route metadata lacks `/Kho/Don_Vi_Tinh`; Business DB read-only metadata maps that route to function `2016`. Runtime authorization unit tests prove Add denial and Confirm recheck using a test resolver, but a real Test DB login/Add permission could not be exercised without changing function metadata or having a valid UI session.
- Phase 1C reported the browser session had expired. No Business DB changes were made. Manual browser UAT on an authorized environment remains outstanding.

## Next Phase

Proceed to Category only after this 2A result. Category's Test DB insert procedure and route metadata were absent during the contract audit; sync only the required procedure to Test DB before its runtime test and keep the dynamic-permission UI limitation explicit.
