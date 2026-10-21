# Warehouse Master Data Info — Get_By_ID Parity Audit

## Status

**Root cause confirmed; five Get_By_ID procedures are deployed and all five Info components now use them.** The user authorized deployment to `TKS_Thuc_Tap_V11_GiaiDoan2`. The procedure definitions and read results were verified after deployment. No business table data was changed. Browser Info E2E remains pending.

## Baseline and scope

- Repository: `Chau165/TK_GiaiDoan_2`
- Branch / baseline commit: `main` / `fd3235f78bd3c17e596324265310b2be5df02512`
- The worktree already had 27 modified tracked paths and 6 untracked paths at inspection. Those changes were preserved.
- The CodeGraph index exists and was used for source navigation. The available CodeGraph integration exposes `codegraph_explore`; no sync/status operation was available.
- Runtime configuration in `TKS_Thuc_Tap_V11_Web/appsettings.json` points to `localhost` and `TKS_Thuc_Tap_V11_GiaiDoan2`; `Program.cs` assigns that configured connection to `CConfig.TKS_Thuc_Tap_V11_Conn_String`.
- A read-only SQLCMD connection to that endpoint resolved to `DESKTOP-NHQ7QPL\MSSQLSERVER19` and the expected database. SQLCMD was the query client; effective runtime application identity was not independently probed.

## A. Root cause

The current Info path is:

```text
Info.Load_Data → List_* stored procedure → FirstOrDefault(Auto_ID)
               → CUtility.Map_Row_To_Entity → CUtility.Get_Label_Text
```

All five Info components use their normal `List_*` procedure to load `m_objData`. The live definitions of those procedures select only `Auto_ID` and business columns. They omit `Created`, `Created_By`, `Created_By_Function`, `Last_Updated`, `Last_Updated_By`, and `Last_Updated_By_Function`.

`CUtility.Map_Row_To_Entity` maps only columns present in the `DataRow`; it skips `DBNull`. Therefore the missing audit columns stay at Entity defaults. `CUtility.Get_Label_Text` renders null or empty values as `-`. This explains the missing values without evidence of a mapping defect.

The database tables do contain all six audit columns. Read-only samples had populated values for every audit field, so the sample records are **not** `SOURCE_DATA_NULL`.

The company reference follows the required pattern: [F2003_2_Chu_Hang_Info.razor](</P:/Giao đoạn 2 TK/Giao đoạn 2 TK/TKS_Thuc_Tap_11/TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_2_Chu_Hang_Info.razor:184>) calls `FQ_104_CH_sp_sel_Get_By_ID(r_iAuto_ID)` and separately calls the legacy action-history procedure. The matching Controller uses `FillDataTable`, maps the returned row through `CUtility.Map_Row_To_Entity`, disposes the table, and propagates exceptions.

## B. Chu_Hang comparison and current Info sources

| Module | Chu_Hang reference | Info source after fix | Runtime Get_By_ID | Status |
|---|---|---|---|---|
| Kho (2009) | Dedicated Get_By_ID | `F2009_sp_sel_Get_By_ID(r_iAuto_ID)` | Deployed and queried | `ACTIVE` |
| Đơn vị tính (2016) | Dedicated Get_By_ID | `F2016_sp_sel_Get_By_ID(r_iAuto_ID)` | Deployed and queried | `ACTIVE` |
| Loại sản phẩm (2017) | Dedicated Get_By_ID | `F2017_sp_sel_Get_By_ID(r_iAuto_ID)` | Deployed and queried | `ACTIVE` |
| Sản phẩm (2018) | Dedicated Get_By_ID plus FK lookups | `F2018_sp_sel_Get_By_ID(r_iAuto_ID)`; category/unit lookups remain separate | Deployed and queried | `ACTIVE` |
| Nhà cung cấp (2019) | Dedicated Get_By_ID | `F2019_sp_sel_Get_By_ID(r_iAuto_ID)` | Deployed and queried | `ACTIVE` |

All five Get_By_ID Controller methods already exist and follow the company DataTable-to-Entity mapping convention. The Info UI already has the six metadata bindings, and each Info keeps `FCommon_Sys_sp_sel_List_Log_Record_Action_History(r_iAuto_ID)` plus `FCommon_Record_Action_History_List`. Product category and unit lookup/display bindings remain present.

| Info component | `Load_Data` lines |
|---|---:|
| [F2009_2_Kho_Info.razor](</P:/Giao đoạn 2 TK/Giao đoạn 2 TK/TKS_Thuc_Tap_11/TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2009_2_Kho_Info.razor:52>) | 52–60 |
| [FWarehouse_Unit_Info.razor](</P:/Giao đoạn 2 TK/Giao đoạn 2 TK/TKS_Thuc_Tap_11/TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Unit_Info.razor:54>) | 54–62 |
| [F2017_2_Loai_San_Pham_Info.razor](</P:/Giao đoạn 2 TK/Giao đoạn 2 TK/TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2017_2_Loai_San_Pham_Info.razor:53>) | 53–61 |
| [F2018_2_San_Pham_Info.razor](</P:/Giao đoạn 2 TK/Giao đoạn 2 TK/TKS_Thuc_Tap_11/TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2018_2_San_Pham_Info.razor:62>) | 62–77 |
| [F2019_2_Nha_Cung_Cap_Info.razor](</P:/Giao đoạn 2 TK/Giao đoạn 2 TK/TKS_Thuc_Tap_11/TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2019_2_Nha_Cung_Cap_Info.razor:53>) | 53–61 |

## C. SQL deployment compatibility gate

Read-only catalog inspection was performed against `TKS_Thuc_Tap_V11_GiaiDoan2`.

| Procedure | Exists in runtime DB | Parameter / projection | SQLCMD / appsettings connection can execute |
|---|---|---|---|
| `F2009_sp_sel_Get_By_ID` | Yes | `@Auto_ID BIGINT`; 9 columns incl. all audit fields | Yes (`HAS_PERMS_BY_NAME=1`) |
| `F2016_sp_sel_Get_By_ID` | Yes | `@Auto_ID BIGINT`; 9 columns incl. all audit fields | Yes (`HAS_PERMS_BY_NAME=1`) |
| `F2017_sp_sel_Get_By_ID` | Yes | `@Auto_ID BIGINT`; 10 columns incl. all audit fields | Yes (`HAS_PERMS_BY_NAME=1`) |
| `F2018_sp_sel_Get_By_ID` | Yes | `@Auto_ID BIGINT`; 12 columns incl. product FKs and all audit fields | Yes (`HAS_PERMS_BY_NAME=1`) |
| `F2019_sp_sel_Get_By_ID` | Yes | `@Auto_ID BIGINT`; 10 columns incl. all audit fields | Yes (`HAS_PERMS_BY_NAME=1`) |

The post-deployment SQLCMD checks reported `HAS_PERMS_BY_NAME(..., 'EXECUTE') = 1` for all five objects. The connection string read from `TKS_Thuc_Tap_V11_Web/appsettings.json` was then used without printing its contents; all five procedures returned their known sample and returned no row for a missing ID. Runtime environment overrides were not evaluated.

The script [WarehouseMaster.GetById.Procedures.sql](</P:/Giao đoạn 2 TK/Giao đoạn 2 TK/TKS_Thuc_Tap_11/Database/WarehouseMaster.GetById.Procedures.sql:1>) was applied with SQLCMD to the configured server/database; command exit code was 0. SHA-256 before deployment: `631E1438F5724D8ECA43FFF65E94A057F5B3930A03C727AF7CAF3AE751DF5843`. Catalog metadata confirmed all names, one `BIGINT @Auto_ID` parameter per procedure, and expected ordered result columns. Each procedure returned one matching current sample row (Kho 11012, Unit 276, Category 272, Product 477, Supplier 283); each returned zero rows for `9223372036854775807`. No table data was written. The application's effective SQL identity is still unverified.

The live `List_*` definitions were inspected through `sys.sql_modules`; each projection omits the audit columns. For example, `F2009_sp_sel_List_Kho` selects only `Auto_ID, Ten_Kho, Ghi_Chu`. The same pattern holds for Unit, Category, Product, and Supplier.

## D. Read-only metadata verification

Kho uses the requested example ID 11012. For other modules, the query sampled the lowest existing `Auto_ID` in each table. All six audit fields in these sample rows were non-null.

| Module | Auto_ID | DB Created | Created_By / function | DB Last_Updated | Last_Updated_By / function | Result |
|---|---:|---|---|---|---|---|
| Kho | 11012 | 2026-10-14 01:39:54.2180265 | `thuctap_kho` / `2009` | 2026-10-14 01:39:54.2180265 | `thuctap_kho` / `2009` | `DB_VALUE_PRESENT`; List projection omits it |
| Đơn vị tính | 276 | 2026-10-14 01:40:11.6460182 | `thuctap_kho` / `2016` | 2026-10-14 01:40:11.6460182 | `thuctap_kho` / `2016` | `DB_VALUE_PRESENT`; List projection omits it |
| Loại sản phẩm | 272 | 2026-10-14 01:40:25.3825051 | `thuctap_kho` / `2017` | 2026-10-14 01:40:25.3825051 | `thuctap_kho` / `2017` | `DB_VALUE_PRESENT`; List projection omits it |
| Sản phẩm | 477 | 2026-10-14 01:40:39.2696637 | `thuctap_kho` / `2018` | 2026-10-14 01:40:39.2696637 | `thuctap_kho` / `2018` | `DB_VALUE_PRESENT`; List projection omits it |
| Nhà cung cấp | 283 | 2026-10-14 01:41:00.9706230 | `thuctap_kho` / `2019` | 2026-10-14 01:41:00.9706230 | `thuctap_kho` / `2019` | `DB_VALUE_PRESENT`; List projection omits it |

| Module | SP Created | Entity Created | UI Created | Result |
|---|---|---|---|---|
| All five samples | Get_By_ID procedures returned populated Created and audit metadata | Entity mapping contract test verifies present values and null handling | Info components now load by Auto_ID; browser not exercised | DB SP read verified; browser rendering remains pending |

The Entity test below proves that the shared row mapper carries populated audit values when those columns are supplied, and does not invent values for `DBNull`. Separately, the live stored procedures were executed read-only against sample records and returned the audit values.

## E. Changes

| File | Method / section | Before | After | Reason |
|---|---|---|---|---|
| [F2009_2_Kho_Info.razor](</P:/Giao đoạn 2 TK/Giao đoạn 2 TK/TKS_Thuc_Tap_11/TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2009_2_Kho_Info.razor:52>), [FWarehouse_Unit_Info.razor](</P:/Giao đoạn 2 TK/Giao đoạn 2 TK/TKS_Thuc_Tap_11/TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Unit_Info.razor:54>), [F2017_2_Loai_San_Pham_Info.razor](</P:/Giao đoạn 2 TK/Giao đoạn 2 TK/TKS_Thuc_Tap_11/TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2017_2_Loai_San_Pham_Info.razor:53>), [F2018_2_San_Pham_Info.razor](</P:/Giao đoạn 2 TK/Giao đoạn 2 TK/TKS_Thuc_Tap_11/TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2018_2_San_Pham_Info.razor:62>), [F2019_2_Nha_Cung_Cap_Info.razor](</P:/Giao đoạn 2 TK/Giao đoạn 2 TK/TKS_Thuc_Tap_11/TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2019_2_Nha_Cung_Cap_Info.razor:53>) | `Load_Data` | Loaded full `List_*` then filtered in memory by `Auto_ID` | Calls dedicated `Get_By_ID(r_iAuto_ID)`; preserves missing-record error, legacy Action History call, and Product category/unit lookups | Matches Chu_Hang's one-record loading pattern; avoids full-list load |
| [WarehouseMasterGetByIdContractTests.cs](</P:/Giao đoạn 2 TK/Giao đoạn 2 TK/TKS_Thuc_Tap_11/TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseMasterGetByIdContractTests.cs:1>) | Contract and mapper tests | Expected Info callers to wait for deployment | Asserts each Info calls its own Get_By_ID, not List filtering; checks legacy history, Product lookups, projections, and mapping | Protect deployed contract and active callers |
| This report | Findings and deployment | Described DB deployment and Info activation as pending | Records completed deployment, read verification, source switch, and remaining runtime limits | Preserve review evidence |

The five Info `Load_Data` methods were switched to their existing dedicated Controller methods. FBase, List/Toolbar, CRUD, Import, lookup behavior, Action History, and business table data were left unchanged. The existing legacy Action History SP remains in use.

## F. Verification

- Deployment: `sqlcmd -S localhost -d TKS_Thuc_Tap_V11_GiaiDoan2 -E -b -i Database\WarehouseMaster.GetById.Procedures.sql` — **exit 0**. Fresh catalog checks verified all five procedures, `@Auto_ID BIGINT`, expected result columns, and SQLCMD caller EXECUTE permission. Each procedure returned the matching sample and zero rows for a nonexistent ID.
- App settings data-access check: used the connection string configured in `TKS_Thuc_Tap_V11_Web/appsettings.json` without displaying it. All five stored procedures returned the known record with `Created` populated and returned zero rows for `long.MaxValue` — **5/5 passed**. Environment-variable or deployment-time overrides were not checked.
- Focused test command: `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --nologo --verbosity quiet --filter "FullyQualifiedName~WarehouseMasterGetByIdContractTests"` — final run **2 passed, 0 failed**. The test was first changed to assert active Info usage and failed before the five Info edits, then passed afterward.
- Web build: `dotnet build TKS_Thuc_Tap_V11_Web/TKS_Thuc_Tap_V11_Web.csproj --no-restore --nologo --verbosity quiet` with `BaseOutputPath` redirected to a unique temp directory — **exit 0**; Telerik license warnings only, no build errors.
- `git diff --check` and targeted whitespace scan of changed Info/test/SQL/report files: **passed**; Git emitted only existing LF/CRLF normalization warnings for modified files.
- Browser Info E2E: `UI_E2E_PENDING`; database access through the checked-in appsettings connection is verified, but rendered screens and runtime configuration overrides were not exercised.
- Database writes: only the five approved `CREATE OR ALTER PROCEDURE` definitions; no business table data changed.

## G. Final status and next step

| Status | Meaning |
|---|---|
| `ROOT_CAUSE_CONFIRMED` | Info List projections omit audit fields while sample database rows contain values. |
| `SOURCE_TESTS_PASS` | Prepared procedure contract and shared mapper behavior pass focused tests. |
| `GET_BY_ID_DB_CONFIRMED` | All five procedures are deployed; live parameter/projection/read checks passed. |
| `INFO_SOURCE_ACTIVE` | All five Info components call their deployed Get_By_ID procedure; product FK lookups and legacy history remain intact. |
| `UI_E2E_PENDING` | No browser verification of the active path. |

The DDL deployment and source switch are complete. No Action History procedure or business data migration was changed.
