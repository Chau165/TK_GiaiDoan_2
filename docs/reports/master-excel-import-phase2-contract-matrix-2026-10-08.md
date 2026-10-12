# Master Excel Import Phase 2 - Contract Matrix

Audit date: 2026-10-08  
Source of truth: current repository schema/procedures and master UI. Business DB was queried read-only for dynamic function-code mappings. No database writes were performed for this audit.

## Contract Matrix

| Master | Route | Function | Table / entity | Insert / update SP | List / lookup SP | Sheet / exact columns | Required / optional and max | Business key / duplicate rule | Collation | References / audit |
|---|---|---|---|---|---|---|---|---|---|---|
| Unit | `/Kho/Don_Vi_Tinh` | `2016` (`Đơn vị tính`, Business `tbl_Sys_Chuc_Nang`) | `tbl_DM_Don_Vi_Tinh` / `CWarehouseMaster` | `F2016_sp_ins_Don_Vi_Tinh` / `F2016_sp_upd_Don_Vi_Tinh` | `F2016_sp_sel_List_Don_Vi_Tinh` / `F2016_sp_sel_List_Don_Vi_Tinh_Lookup` | `DonViTinh`: `Ten_Don_Vi_Tinh`, `Ghi_Chu` | Required name, 200 chars; optional note, 1000 chars | Name; trim, reject empty, reject an existing or in-file duplicate | Explicit `SQL_Latin1_General_CP1_CI_AS`; case-insensitive, accent-sensitive | Referenced by Product. Audit fields are supplied by server current user/function through the existing master save contract. |
| Category | `/Kho/Loai_San_Pham` | `2017` (`Loại sản phẩm`, Business `tbl_Sys_Chuc_Nang`) | `tbl_DM_Loai_San_Pham` / `CWarehouseMaster` | `F2017_sp_ins_Loai_San_Pham` / `F2017_sp_upd_Loai_San_Pham` | `F2017_sp_sel_List_Loai_San_Pham` / `F2017_sp_sel_List_Loai_San_Pham_Lookup` | `LoaiSanPham`: `Ma_LSP`, `Ten_LSP`, `Ghi_Chu` | Required code, 100; required name, 200; optional note, 1000 | Code and name are each unique; insert SP rejects a row when either matches an existing value | Inherits database/column collation; Business and Test DB report `SQL_Latin1_General_CP1_CI_AS` | Referenced by Product. Audit fields are server supplied. No workbook reference IDs. |
| Supplier / NCC | `/Kho/Nha_Cung_Cap` | `2019` (`Nhà cung cấp`, Business `tbl_Sys_Chuc_Nang`) | `tbl_DM_NCC` / `CWarehouseMaster` | `F2019_sp_ins_NCC` / `F2019_sp_upd_NCC` | `F2019_sp_sel_List_NCC` / `F2019_sp_sel_List_NCC_Lookup` | `NhaCungCap`: `Ma_NCC`, `Ten_NCC`, `Ghi_Chu` | Required code, 100; required name, 255; optional note, 1000 | Code and name are each unique; insert SP rejects a row when either matches an existing value | Inherits database/column collation; Business and Test DB report `SQL_Latin1_General_CP1_CI_AS` | Referenced by receipt headers. No tax/address/contact/status fields exist in this master contract. Audit fields are server supplied. |
| Warehouse / Kho | `/Kho/Quan_Ly` | `2009` (`Kho`, Business `tbl_Sys_Chuc_Nang`) | `tbl_DM_Kho` / `CWarehouseMaster` | `F2009_sp_ins_Kho` / `F2009_sp_upd_Kho` | `F2009_sp_sel_List_Kho` / `F2009_sp_sel_List_Kho_Lookup` | `Kho`: `Ten_Kho`, `Ghi_Chu` | Required name, 255; optional note, 1000 | Name; trim, reject empty, reject an existing or in-file duplicate | Inherits database/column collation; Business and Test DB report `SQL_Latin1_General_CP1_CI_AS` | Referenced by warehouse-user assignments, receipts, issues, and derived inventory structures. Audit fields are server supplied. |

## Evidence

- Routes are declared in `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Warehouse_Don_Vi_Tinh.razor`, `Warehouse_Loai_San_Pham.razor`, `Warehouse_Nha_Cung_Cap.razor`, and `Warehouse.razor`. All use the shared `FWarehouse_1_Warehouse_List` grid and pass the corresponding master type.
- Function mappings were read from Business DB `dbo.tbl_Sys_Chuc_Nang` by exact `Func_URL`. The runtime permission path resolves the active function by URL in `FBase`, then calls `CCommonFunction.Get_Chuc_Nang_By_User`; Add is `Is_Have_Add_Permission`. Import must use that screen's function code and must recheck Add on the server for Preview and Confirm.
- Fields, types, lengths, nullability, and unique indexes are defined in `Database/WarehouseModule.Schema.sql`; insert/update validation and list/lookup projections are in `Database/WarehouseModule.Procedures.sql`. `CWarehouseMaster_Controller.Build_Master_Save_Command` maps each entity to those procedures and passes created/updated audit identity from the caller.
- Unit UI fields are name and note; Category and NCC expose code, name, and note; Warehouse exposes name and note in `FWarehouse_3_Warehouse_Edit.razor`. Internal identity and audit fields are not import columns.
- Test and Business DB defaults both report `SQL_Latin1_General_CP1_CI_AS`. Test DB columns and six unique indexes for these masters were present with source-compatible lengths during this audit.

## Test DB Parity Gate

Read-only target: `DESKTOP-NHQ7QPL\MSSQLSERVER19` / `TKS_Thuc_Tap_V11_C01C02_Test_20260906`.

- Unit table columns, unique index, insert/list/lookup procedures exist. The insert procedure definition contains the current CI_AS comparison. Full module hash parity has not yet been recorded; verify the required Unit objects again immediately before runtime testing.
- At the initial pre-sync inspection, Category list and lookup procedures existed, but the insert procedure was missing.
- At the initial pre-sync inspection, Supplier insert/list/lookup procedures were missing.
- Warehouse insert/list/lookup procedures are missing.
- Test DB route metadata contains `/Kho/Quan_Ly` with function code `2010`; Business DB maps that route to `2009`. Test DB has no rows for the Unit, Category, or Supplier routes. This is an existing dynamic-permission/UI test-environment mismatch, not a source contract change. Do not copy or alter function metadata without a scoped need and a separately verified plan.
- No runtime test or cleanup has been executed as part of this contract audit. Before each master's DB runtime proof, sync only the required missing/stale objects to the approved Test DB, verify post-sync parity, then test with a verified test-DB connection.

## Implementation Order

Proceed strictly Unit -> Category -> Supplier -> Warehouse. Unit is the first implementation gate. No template headers or rules outside the audited contracts above should be added. Existing Product import and Telerik Excel/CSV exports remain unchanged; regression tests are required after each master phase.

## Phase 2B Update

- `F2017_sp_ins_Loai_San_Pham` was initially absent from the approved Test DB. Only this required insert procedure was synchronized from `Database/WarehouseModule.Procedures.sql`; post-sync checks confirmed the 100/200/1000-character parameters, required-field and code-or-name duplicate guards, transaction/fence contract, and the existing unique indexes. Business DB remained read-only.
- Category runtime integration and cleanup passed on the approved Test DB. See `master-excel-import-phase2b-category-2026-10-08.md` for run-prefix evidence.
- The dynamic-permission limitation is unchanged: Test DB still lacks the Category route metadata, while the Business DB mapping is `2017`. No permission metadata was copied or modified.

## Phase 2C Update

- The current source's `Load_Data_Async` calls `Load_Lookups_Async`, which requires the Supplier lookup SP in addition to the import's list and insert SPs. The approved Test DB initially lacked all three; only `F2019_sp_ins_NCC`, `F2019_sp_sel_List_NCC`, and `F2019_sp_sel_List_NCC_Lookup` were synced from their exact source batches.
- Post-sync checks confirmed all three procedures, the lookup projection, insert parameter sizes/guards/transaction contract, and unique CI_AS code/name indexes. Business DB remained read-only.
- The Test DB still has no `/Kho/Nha_Cung_Cap` function row; Business DB maps it to `2019`. Permission metadata was not changed. Supplier runtime details are in `master-excel-import-phase2c-supplier-2026-10-08.md`.

## Phase 2D Update

- The Test DB initially lacked the Warehouse insert/list/lookup procedures. Since the shared page loads the Warehouse lookup on startup, the exact source batches `F2009_sp_ins_Kho`, `F2009_sp_sel_List_Kho`, and `F2009_sp_sel_List_Kho_Lookup` were synchronized. Post-sync checks confirmed lengths, required/duplicate guards, transaction/fence, projections, and the unique CI_AS name index.
- The Test DB maps `/Kho/Quan_Ly` to `2010`; Business DB maps it to `2009`. This is not defined by the repository's import source, and permission metadata was left untouched. Warehouse runtime details are in `master-excel-import-phase2d-warehouse-2026-10-08.md`.
- All four source/UI/template phases and Test DB runtime integration gates are complete. Real browser UAT with live Add permission remains owner-side because the Test DB routes for Unit/Category/Supplier are absent and Warehouse's route code differs. See `master-excel-import-phase2-final-2026-10-08.md`.
