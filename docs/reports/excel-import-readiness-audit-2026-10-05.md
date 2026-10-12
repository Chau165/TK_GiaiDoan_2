# Final Excel Import Readiness Audit

Date: 2026-10-05  
Repository: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`  
Branch: `main`  
HEAD: `9f361e49b475c34b3b85eab7a4fc2af084a0c564`

## 1. Executive Summary

- **Phase 1 is feasible without a schema migration** for insert-only Product import. Use `Ma_San_Pham` as the product key, `Ma_LSP` to resolve category, and normalized `Ten_Don_Vi_Tinh` to resolve unit. Do not put database IDs or audit fields in the workbook.
- **Unit lookup is not code-safe:** `tbl_DM_Don_Vi_Tinh` has no code column; both page/lookup procedures return an empty `Code`. Recommendation: `UNIT_LOOKUP_REQUIRES_IMPORT_NORMALIZATION`; trim and resolve by unit name, rejecting unresolved or ambiguous values.
- **Authorization is currently UI-level for master saves.** The form checks Add/Edit, but the controller and save procedures do not perform a current-user permission check. An import path needs server-side Add authorization.
- **Current upload configuration is not ready for a 20 MB Excel file.** Generic Telerik FileSelect advertises 20,480,000 bytes, while the Blazor Server SignalR message limit is not explicitly raised in the inspected application configuration. Telerik documents a default-message-size issue for larger FileSelect uploads. The actual deployed host/runtime behavior was not exercised.
- **A row-by-row UI save is not an all-or-nothing import.** Each ordinary save opens its own database call and the procedures can own their own transaction. Use a capped, validated file and one outer transaction if Phase 1 requires atomicity.
- The resolved project assets contain Telerik `Telerik.Documents.SpreadsheetStreaming` transitively. Its `SpreadImporter` is a candidate XLSX reader, distinct from Telerik Grid Excel export. Reuse is recommended, subject to verifying license entitlement and a small parser spike before implementation.
- Recommended Phase 1: `.xlsx`, insert-only, Add permission, preview then confirm, maximum **100 rows** and **1 MiB** file, preloaded lookup maps, one transaction, and one import-specific history event. These are conservative design limits, **not benchmark results**.
- No live database catalog/data, deployed host settings, valid Telerik license key, import runtime, build, or tests were inspected/run. All schema claims below describe repository SQL and the current checkout, not certified live DB state.

## 2. Evidence

### Scope and repository state

- Applicable repository instruction files were checked at the repository and relevant Database, Warehouse, Web_Danh_Muc, Data_Access, and Tests paths; no additional applicable repository `AGENTS.md` was found. User-provided audit-only instructions govern this report.
- `git status --short` showed these pre-existing worktree changes. They were preserved; the report is the only file created by this audit:
  - Modified: `Database/WarehouseModule.Procedures.sql`
  - Modified: `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseUiWorkflowTests.cs`
  - Modified: `TKS_Thuc_Tap_V11_Web/Program.cs`
  - Modified: `TKS_Thuc_Tap_V11_Web/TKS_Thuc_Tap_V11_Web.csproj`
  - Modified: `TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor`
  - Modified: `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_1_Warehouse_List.razor`
  - Untracked: `TKS_Thuc_Tap_V11_Web_Common/Common/CGrid_DataSource_Utility.cs`
- The report audits the visible working-tree source, including the modified SQL and C# files. There was no live SQL Server connection/catalog inspection.
- Primary source evidence: `Database/WarehouseModule.Schema.sql`, `Database/WarehouseModule.Procedures.sql`, `CWarehouseMaster_Controller`, `CWarehouseMaster`/`CWarehouseLookup` models, `FWarehouse_1_Warehouse_List.razor`, `FBase.razor`, `FCommon_Import_Excel.razor`, `CExcel_Controller`, project `.csproj` files, `Program.cs`, and relevant tests/performance files.
- Supporting library documentation: [Telerik FileSelect overview](https://www.telerik.com/blazor-ui/documentation/components/fileselect/overview), [Telerik RadSpreadStreamProcessing import](https://www.telerik.com/document-processing-libraries/documentation/libraries/radspreadstreamprocessing/import), [Telerik package list](https://www.telerik.com/document-processing-libraries/documentation/getting-started/installation/nuget-packages), [Telerik license key setup](https://www.telerik.com/document-processing-libraries/documentation/distribution-and-licensing/license-key/setting-up-license-key), [Microsoft SignalR for Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/signalr?view=aspnetcore-5.0), [ClosedXML](https://github.com/ClosedXML/ClosedXML), [ExcelDataReader](https://github.com/ExcelDataReader/ExcelDataReader), [Open XML large spreadsheet reading](https://learn.microsoft.com/en-us/office/open-xml/spreadsheet/how-to-parse-and-read-a-large-spreadsheet).
- External documentation describes library/framework behavior; it does not establish this application's deployed configuration or runtime behavior.

## 3. Master Table Schema

Source: `Database/WarehouseModule.Schema.sql` (table definitions around lines 38–106; unique indexes around 174–180; conditional audit columns around 201–205). SQL types below are from the repository script. No computed columns were found in these five definitions.

| Table | Column | SQL definition | Identity/default/computed |
|---|---|---|---|
| `tbl_DM_Don_Vi_Tinh` | `Auto_ID` | `BIGINT NOT NULL` | `IDENTITY(1,1)`, PK |
| | `Ten_Don_Vi_Tinh` | `NVARCHAR(200) NOT NULL` | — |
| | `Ghi_Chu` | `NVARCHAR(1000) NULL` | — |
| | `Created` | `DATETIME2 NOT NULL` | `DEFAULT SYSUTCDATETIME()` |
| | `Last_Updated` | `DATETIME2 NOT NULL` | `DEFAULT SYSUTCDATETIME()` |
| `tbl_DM_Loai_San_Pham` | `Auto_ID` | `BIGINT NOT NULL` | `IDENTITY(1,1)`, PK |
| | `Ma_LSP` | `NVARCHAR(100) NOT NULL` | — |
| | `Ten_LSP` | `NVARCHAR(200) NOT NULL` | — |
| | `Ghi_Chu` | `NVARCHAR(1000) NULL` | — |
| | `Created` | `DATETIME2 NOT NULL` | `DEFAULT SYSUTCDATETIME()` |
| | `Last_Updated` | `DATETIME2 NOT NULL` | `DEFAULT SYSUTCDATETIME()` |
| `tbl_DM_San_Pham` | `Auto_ID` | `BIGINT NOT NULL` | `IDENTITY(1,1)`, PK |
| | `Ma_San_Pham` | `NVARCHAR(100) NOT NULL` | — |
| | `Ten_San_Pham` | `NVARCHAR(255) NOT NULL` | — |
| | `Loai_San_Pham_ID` | `BIGINT NOT NULL` | FK |
| | `Don_Vi_Tinh_ID` | `BIGINT NOT NULL` | FK |
| | `Ghi_Chu` | `NVARCHAR(1000) NULL` | — |
| | `Created` | `DATETIME2 NOT NULL` | `DEFAULT SYSUTCDATETIME()` |
| | `Last_Updated` | `DATETIME2 NOT NULL` | `DEFAULT SYSUTCDATETIME()` |
| `tbl_DM_NCC` | `Auto_ID` | `BIGINT NOT NULL` | `IDENTITY(1,1)`, PK |
| | `Ma_NCC` | `NVARCHAR(100) NOT NULL` | — |
| | `Ten_NCC` | `NVARCHAR(255) NOT NULL` | — |
| | `Ghi_Chu` | `NVARCHAR(1000) NULL` | — |
| | `Created` | `DATETIME2 NOT NULL` | `DEFAULT SYSUTCDATETIME()` |
| | `Last_Updated` | `DATETIME2 NOT NULL` | `DEFAULT SYSUTCDATETIME()` |
| `tbl_DM_Kho` | `Auto_ID` | `BIGINT NOT NULL` | `IDENTITY(1,1)`, PK |
| | `Ten_Kho` | `NVARCHAR(255) NOT NULL` | — |
| | `Ghi_Chu` | `NVARCHAR(1000) NULL` | — |
| | `Created` | `DATETIME2 NOT NULL` | `DEFAULT SYSUTCDATETIME()` |
| | `Last_Updated` | `DATETIME2 NOT NULL` | `DEFAULT SYSUTCDATETIME()` |

All five primary keys are named `PK_<table>` on `Auto_ID`. The script conditionally adds `Created_By`, `Created_By_Function`, `Last_Updated_By`, and `Last_Updated_By_Function` as `NVARCHAR(100) NULL`; these are audit columns added by `ALTER TABLE`, not part of the initial table declarations. Their conditional check is keyed on `Created_By`.

| Table | Unique index in schema script | Scope / meaning |
|---|---|---|
| `tbl_DM_Don_Vi_Tinh` | `UX_tbl_DM_Don_Vi_Tinh_Ten` on `Ten_Don_Vi_Tinh` | Global name uniqueness |
| `tbl_DM_Loai_San_Pham` | `UX_tbl_DM_Loai_San_Pham_Ma` on `Ma_LSP`; `UX_tbl_DM_Loai_San_Pham_Ten` on `Ten_LSP` | Each globally unique |
| `tbl_DM_San_Pham` | `UX_tbl_DM_San_Pham_Ma` on `Ma_San_Pham` | Global product-code uniqueness; no unique product-name index found |
| `tbl_DM_NCC` | `UX_tbl_DM_NCC_Ma` on `Ma_NCC`; `UX_tbl_DM_NCC_Ten` on `Ten_NCC` | Each globally unique |
| `tbl_DM_Kho` | `UX_tbl_DM_Kho_Ten` on `Ten_Kho` | Global name uniqueness; no `Ma_Kho` column found |

| Product FK | Referenced key | Delete/update behavior |
|---|---|---|
| `FK_tbl_DM_San_Pham_Loai` (`Loai_San_Pham_ID`) | `tbl_DM_Loai_San_Pham(Auto_ID)` | No cascade clause in the script; default `NO ACTION` behavior |
| `FK_tbl_DM_San_Pham_Don_Vi` (`Don_Vi_Tinh_ID`) | `tbl_DM_Don_Vi_Tinh(Auto_ID)` | No cascade clause in the script; default `NO ACTION` behavior |

**Indexes:** primary-key and unique indexes are **REQUIRED FOR IMPORT** as final database integrity/race guards. The product FK constraints are required for valid references; no explicit product indexes on `Loai_San_Pham_ID` or `Don_Vi_Tinh_ID` were found, so such indexes may be **USEFUL** for high-volume joins/deletes but are not evidenced as present or needed for the proposed 100-row import. The unique code/name indexes also support exact lookup and are **USEFUL**. Search procedures use leading-wildcard `LIKE '%...%'`; ordinary B-tree indexes are **NOT RELIABLE FOR SEEKING** those substring filters. No index collation or live DB index definitions were inspected. The script's `IF NOT EXISTS` logic cannot prove an existing live index has the exact intended definition.

Search across repository `Database/` schema/migration/deployment/test/performance SQL found the canonical master definitions in `WarehouseModule.Schema.sql`; the inspected inventory snapshot hardening migration references product/warehouse FKs for its own table, not new master constraints. This does not certify live database parity.

## 4. Business Keys

“DB unique” below means a unique index exists in the repository schema script, not that the live database was queried. SP comparison collation and DB index collation are not necessarily identical.

| Master | Business key | DB unique? | SP validation? | Safe for Excel lookup? |
|---|---|---:|---|---|
| DonViTinh | `Ten_Don_Vi_Tinh` | Yes, unique index | F2016 insert/update trims incoming name, rejects blank, checks duplicates with `Latin1_General_CI_AI` | **Conditional:** resolve by trimmed/normalized name; reject unknown/ambiguous; live collation and legacy data not verified |
| LoaiSanPham | `Ma_LSP` (name also unique) | Yes, code and name indexes | F2017 checks both code and name; trims submitted values | Yes by exact business code after trim; DB collation behavior must be respected |
| SanPham | `Ma_San_Pham` | Yes, global unique index | F2018 checks duplicate product code; no name-unique check | Yes by product code; duplicate race still must be handled by DB index |
| NCC | `Ma_NCC` (name also unique) | Yes, code and name indexes | F2019 checks both code and name; trims submitted values | Yes by exact business code after trim |
| Kho | `Ten_Kho` | Yes, unique index | F2009 checks duplicate name; trims submitted value | Yes by exact name after trim; no warehouse code exists |

Product name has **NO UNIQUE ENFORCEMENT** in the inspected schema/SP path. Category and supplier names are additionally unique, so an insert can collide on a secondary unique index even when its code differs. SP checks that lack explicit `COLLATE` use the applicable SQL column/database collation; live collation was not read. Repository schema does not specify unique-index collation.

## 5. DonViTinh Lookup Analysis

Evidence: `CWarehouseMaster`/`CWarehouseLookup` model files; `CWarehouseMaster_Controller.Save_Master_Async`; `Database/WarehouseModule.Procedures.sql` procedures `F2016_sp_sel_List_Don_Vi_Tinh`, `F2016_sp_sel_List_Don_Vi_Tinh_Lookup`, `F2016_sp_ins_Don_Vi_Tinh`, `F2016_sp_upd_Don_Vi_Tinh`, `sp_DM_Master_Page`, and `sp_DM_Lookup_Page` (around lines 3829–4030); `Database/WarehouseModule.Schema.sql`.

1. `tbl_DM_Don_Vi_Tinh` has no `Code` column, only `Auto_ID`, `Ten_Don_Vi_Tinh`, note, timestamps, and audit fields.
2. The page and lookup projections return `Auto_ID`, `Code = CAST(N'' AS NVARCHAR(100))`, and `Name = Ten_Don_Vi_Tinh`. Thus `Code` is **empty string**, not the name, ID text, or another field.
3. Product UI selects the related unit using `CWarehouseMaster.Related_ID_2` (the unit `Auto_ID`). The import must resolve a workbook value to that ID before invoking product save.
4. DVI search in the shared page/lookup procedures filters `Ten_Don_Vi_Tinh LIKE '%' + @Search_Text + '%'`. It does not trim the search term or specify `COLLATE`. Its case/accent behavior follows the actual SQL collation, which was not inspected.
5. F2016 trims the submitted name and performs a duplicate check with `Latin1_General_CI_AI`. The unique index is on the stored name, and its effective collation is not stated in the script. Existing legacy rows and direct SQL writes were not inspected. Since only the input is trimmed, a pre-existing stored value with surrounding whitespace is not proven impossible; case/accent variants depend on the actual unique-index collation and write path.

**Conclusion: `UNIT_LOOKUP_REQUIRES_IMPORT_NORMALIZATION`.** The workbook should contain a unit name, which the importer trims and resolves to a single unit ID. It must fail on no match or multiple matches. Do not use `CWarehouseLookup.Code` for unit matching. The source prevents many duplicate variants through its normal SP path, but the repository alone cannot certify whether `Hộp`, `hop`, ` Hộp `, and `HỘP` coexist in the live DB.

## 6. DTO Contract

Source: `CWarehouseMasterModels.cs`, `CWarehouseLookupModels.cs`, `CWarehouseDocumentModels.cs`, and `CWarehousePermissionModels.cs`.

### Existing models

| Model | Field | Type / annotation | Meaning or mapping |
|---|---|---|---|
| `CWarehouseMaster` | `Auto_ID` | `long` | Master primary key; `0` selects insert in save flow |
| | `Code` | `string`, `[StringLength(100)]` | Master code where supported; DVI and warehouse have no DB code |
| | `Name` | `string`, `[StringLength(255)]` | Display/name field; **mismatch** for DVI/category SP/DB limit 200 |
| | `Related_ID` | `long` | Product category ID |
| | `Related_ID_2` | `long` | Product unit ID |
| | `Ghi_Chu` | `string` | Optional note; no model length annotation, while DB allows up to 1000 |
| | `Created_By`, `Created_By_Function` | `string`, each `[StringLength(100)]` | Create audit values |
| | `Last_Updated_By`, `Last_Updated_By_Function` | `string`, each `[StringLength(100)]` | Update audit values |
| | `Created`, `Last_Updated` | `DateTime?` | DB timestamps |
| `CWarehouseLookup` | `Auto_ID` | `long` | Lookup row ID |
| | `Code`, `Name` | `string`, no length annotations | Generic lookup contract; DVI's projected code is empty |
| `CWarehouseDocument` | `Auto_ID`, `Kho_ID`, `NCC_ID` | `long` | Document, warehouse and supplier IDs |
| | `Is_Receipt`, `Is_Posted` | `bool` | Document state/type |
| | `So_Phieu` | `string`, `[Required]`, `[StringLength(100)]` | Document number |
| | `Ten_Kho`, `Ten_NCC`, `Ghi_Chu`, `Created_By`, `Created_By_Function`, `Last_Updated_By`, `Last_Updated_By_Function` | `string` | Display/note/audit values |
| | `Kho_ID` | `[Range(1, long.MaxValue)]` | Positive warehouse reference |
| | `Ngay_Chung_Tu` | `DateTime`, initialized to today | Document date |
| | `Posted_At`, `Created`, `Last_Updated` | `DateTime?` | Optional timestamps |
| `CWarehouseDocumentDetail` | `Auto_ID`, `Document_ID`, `San_Pham_ID` | `long`; product ID has `[Range(1, long.MaxValue)]` | Detail/document/product identity |
| | `Ma_San_Pham`, `Ten_San_Pham`, `Ten_Don_Vi_Tinh` | `string` | Display values |
| | `So_Luong` | `decimal`, `[Range(0.0001, double.MaxValue)]` | Quantity |
| | `Don_Gia` | `decimal`, `[Range(0.01, double.MaxValue)]` | Unit price |
| | `Tri_Gia` | computed property `So_Luong * Don_Gia` | Derived amount, not an import field |
| | `Created`, `Created_By`, `Created_By_Function`, `Last_Updated`, `Last_Updated_By`, `Last_Updated_By_Function` | nullable timestamps/string audit fields | Detail audit |
| `CWarehousePermission` | `Permission_ID`, `Warehouse_ID` | `long` | Permission and warehouse IDs |
| | `Login_Name` | `string`, `[StringLength(100)]` | Login identity |
| | `User_Name`, `Warehouse_Name` | `string`, `[StringLength(255)]` | Display values; this model is warehouse access, not Add/Edit function authorization |

### Proposed import contract (not implemented)

| Field | Source | Phase 1 status | Validation/resolution |
|---|---|---|---|
| `Row_Number` | Parser metadata, not a workbook column | Required for diagnostics | 1-based worksheet row |
| `Product_Code` | `Ma_San_Pham` | Required | Trim, nonblank, max 100, duplicate check within file and DB |
| `Product_Name` | `Ten_San_Pham` | Required | Trim, nonblank, max 255 |
| `Category_Code` | `Ma_LSP` | Required | Trim; resolve to one category `Auto_ID` by unique code, max 100 |
| `Unit_Name` | `Ten_Don_Vi_Tinh` | Required | Trim and resolve to exactly one unit by `Ten_Don_Vi_Tinh`, max 200; no unit code exists |
| `Note` | `Ghi_Chu` | Optional | Max 1000; blank maps to null/empty according to existing SP contract |

Do not include `Auto_ID`, `Created`, `Created_By`, `Last_Updated`, or `Last_Updated_By` as user-editable columns. The resolved category/unit IDs should be internal validated values, not untrusted workbook IDs.

`CProductImportRow` is **REQUIRED FOR PHASE 1**. A small row-error result (row, field/code, message) and preview result (counts plus errors) are **USEFUL** and may be simple local types. A generic reflection mapper, dynamic profile framework, or elaborate error-type hierarchy is **OVER-ENGINEERED** for one Product import.

## 7. Authorization

Evidence: `FBase.razor` loads the active function and current user's function rights; `CCommonFunction.Get_Chuc_Nang_By_User` aggregates View/Add/Edit/Delete/Export; `FWarehouse_1_Warehouse_List.razor` and the product editor gate Add/Edit actions; `CWarehousePermission_Controller` controls user-to-warehouse access, not master function rights.

- Existing function rights found: **View, Add, Edit, Delete, Export**. No `Import` right was found in source.
- Product import Phase 1 is insert-only, so **Add only** is least privilege. Add-only users can import new products; any already-existing product code is an error (not an update/skip). Edit-only users cannot import new rows.
- If a later upsert mode is introduced, each row's operation must be authorized: Add for new rows and Edit for existing rows. Avoid unconditional `Add OR Edit` if the file can modify records; that grants edit-only users creation and add-only users updates.
- A dedicated Import permission is not necessary for the stated Phase 1 unless the owner explicitly needs a separate policy.

## 8. Server-side Security

Current flow: `FWarehouse_1_Warehouse_List.razor`/product form → `Save_Master_Async` event → `CWarehouseMaster_Controller.Save_Master_Async` → F2009/F2016/F2017/F2018/F2019 stored procedure → SQL table.

- The UI save handler checks Add vs Edit using current function permissions before calling the controller.
- `CWarehouseMaster_Controller.Save_Master_Async` selects insert/update based on `Auto_ID` and calls the stored procedure; it does not itself authorize the current user.
- The master write procedures accept row and audit parameters but no current login/permission token and no function-right check was found in them.
- Therefore a direct controller call is not shown to enforce Add/Edit, and the database procedures do not independently enforce those rights. Classification: **`UI_ONLY_AUTHORIZATION`; `SERVER_ENFORCEMENT_REQUIRED` for import.**
- Reusing `Save_Master_Async` directly from a new import service does not automatically execute the UI event-handler permission check. The server-side import entry point must resolve the authenticated/current application user and active function from trusted server context and authorize Add before parsing/preview as appropriate and again before commit. Do not trust identity or permission values from workbook/client input.

## 9. Upload Limits

Architecture evidence: Web project uses `AddServerSideBlazor()` in `TKS_Thuc_Tap_V11_Web/Program.cs`; the page uses Telerik Blazor components. This is **Blazor Server**, not WebAssembly upload-to-API flow.

The repository scan for `InputFile`, `IBrowserFile`, `InputFileChangeEventArgs`, `TelerikUpload`, `TelerikFileSelect`/`FileSelect`, `IFormFile`, multipart/request-size limits, `MaximumReceiveMessageSize`, `HubOptions`, SignalR, and `CircuitOptions` found FileSelect upload use in the shared import/file components and unrelated workflows. No Warehouse Product import path, `IFormFile` endpoint, or explicit server upload/Hub limit was found. This is source/config evidence, not a deployed-host inventory.

| Layer | Current repository evidence | Assessment |
|---|---|---|
| Telerik FileSelect | `FCommon_Import_Excel.razor` sets `MaxFileSize=20480000` (20,480,000 bytes) and one file | Client/component validation value; not proof of a matching server transport cap |
| SignalR Hub | `AddServerSideBlazor().AddCircuitOptions(...)` found; no explicit `AddHubOptions` / `MaximumReceiveMessageSize` found | Framework default is not overridden in inspected `Program.cs`; Telerik documents FileSelect files beyond the default SignalR message size need configuration. Deployment behavior not tested |
| `IBrowserFile.OpenReadStream` | No use in this Telerik FileSelect flow identified | Microsoft's default 500 KB applies when that API is used; **not the observed FileSelect stream path** |
| HTTP multipart / Kestrel / IIS | No application `MultipartBodyLengthLimit`, `MaxRequestBodySize`, request-size attribute, or IIS `maxAllowedContentLength` found in targeted source/config scan | Not evidenced as the active FileSelect limit; hosting/proxy settings are unknown |
| Telerik generic import copy | Reads chunks but writes the requested `eachReadLength` rather than the actual returned byte count in the inspected loop | Potential truncation/corruption on a partial stream read; static observation only, not reproduced |
| Server extension validation | Generic import allows `.xls` and `.xlsx` | Extension allow-list is not content validation; parser compatibility and malformed-file rejection still required |

The application-level 20 MB selection cap and default Blazor Server SignalR settings are **not aligned by explicit configuration**. Do not treat 20 MB as an effective, tested safe upload size. Framework defaults can be changed by hosting/runtime; no production host config was available.

| Limit | Phase 1 recommendation | Reason |
|---|---:|---|
| File size | **1 MiB** | Conservative template-sized workbook cap; enforce server-side and align SignalR configuration if using FileSelect. If transport configuration is not approved, choose an HTTP upload path rather than assume the UI limit protects the server |
| Row count | **100 data rows** | Keeps preview memory, validation, transaction duration, and per-row SP work bounded; no import performance measurements exist |

These are recommended initial bounds, not demonstrated framework maxima or measured capacity.

## 10. Telerik / Excel Library Analysis

### Current project dependency evidence

- The relevant Web projects target .NET 8; the main Web project and `Web_Danh_Muc`, `Web_Common`, and `Web_Control` reference `Telerik.UI.for.Blazor` **14.0.0**.
- `TKS_Thuc_Tap_V11_Data_Access` directly references **EPPlus 7.0.5**. The generic `CExcel_Controller` sets `ExcelPackage.LicenseContext=NonCommercial`; actual production license eligibility was not evaluated.
- Main Web directly references `Telerik.Licensing` **1.8.2** and calls `TelerikLicensing.Register()`. Resolved `obj/project.assets.json` contains `Telerik.Documents.Core`, `Telerik.Documents.Spreadsheet`, `Telerik.Documents.SpreadsheetStreaming` (2026.2.519) and related Telerik packages **transitively**. The source does not use a Telerik workbook reader.
- Resolved assets did **not** contain `Telerik.Documents.Spreadsheet.FormatProviders.OpenXml`; therefore `RadSpreadProcessing` `XlsxFormatProvider` is not available through the inspected current dependency graph. Telerik's package documentation identifies that format-provider package separately.
- `Telerik.Documents.SpreadsheetStreaming` is present transitively and Telerik documents `SpreadImporter` for sequential XLSX/CSV import. This is a genuine reader candidate, unlike Grid Excel export. Actual API compatibility with this app's exact restored runtime should be proven with a minimal isolated parser test before implementation.
- Telerik Grid `GridExcelExport` is export-only in the observed Warehouse component; it does not parse a user workbook.
- EPPlus is already direct but the current generic code allows `.xls`; EPPlus is an OOXML workbook library, not a legacy `.xls` reader. It materializes workbook/data-table representations in the generic path. Its configured `NonCommercial` context requires a separate entitlement review; this report makes no legal conclusion.

### Reader comparison

| Option | Dependency / license | Memory, streaming, formulas | Fit and risk |
|---|---|---|---|
| Telerik `RadSpreadStreamProcessing` | Present transitively; verify vendor license entitlement | Sequential streaming reader; not a full calculation engine. Formula handling/value semantics must be defined and tested | **Primary: REUSE_EXISTING_TELERIK** for `.xlsx`, if `SpreadImporter` works with the resolved version and license is valid. No new package if confirmed |
| ClosedXML | Not found in current dependency graph; MIT | Convenient high-level workbook object model; typically loads workbook in memory; `.xlsx/.xlsm`; no streaming-first advantage for this small workload | **Secondary** if simpler worksheet API outweighs adding a dependency and 1 MiB cap is enforced |
| ExcelDataReader | Not found; MIT | Low-level sequential reader; supports `.xls` and `.xlsx`; values-oriented, without a full formula engine | Consider only if legacy `.xls` is a real requirement; Phase 1 should accept `.xlsx` only |
| Open XML SDK | Not found; MIT | SAX `OpenXmlReader` supports large-file streaming, but API is lower-level and more complex | Not recommended for this simple capped table import; useful only if lower-level control or large-file needs emerge |
| Existing EPPlus 7.0.5 | Direct dependency, but licensing configured as NonCommercial | Workbook model can consume more memory; XLSX, not legacy XLS | Existing dependency does not justify selecting it until the production entitlement and generic importer defects are resolved |

**Maintenance/security evidence boundary:** current resolved Telerik and EPPlus versions are known from project/assets files; current upstream release cadence, support entitlement, and dependency advisory/CVE status were not audited. ClosedXML, ExcelDataReader, and Open XML SDK are not present in the resolved graph, so no deployed version can be compared for them. Treat every uploaded workbook as untrusted ZIP/XML input: enforce byte/row/column/string limits, reject malformed content, and review advisories/license terms before adding or upgrading a parser. No package security scan was run.

**Recommendation:** `REUSE_EXISTING_TELERIK`, with a proof-of-compatibility/licensing gate. **Secondary:** ClosedXML if the Telerik streaming API proves awkward and the small capped file is acceptable in memory. **Not recommended for Phase 1:** adding Open XML SDK or a generalized parser framework; ExcelDataReader only if `.xls` support is required.

Parser policy should explicitly reject or define formula cells, blank rows, hidden rows, date/number coercion, and duplicate/unknown headers. For Phase 1, accept only `.xlsx`, treat fields as text, reject formulas in required business-key/reference columns, and never silently use stale cached formula values.

## 11. Performance / Row Limit

Evidence: `CWarehouseMaster_Controller.Save_Master_Async`; `CWarehouse_Controller_Base.Scalar_ID`; `CSqlHelper.ExecuteScalar`; F2018; existing Warehouse performance benchmark/seed files.

- The ordinary controller path performs one stored-procedure call for each saved row; `CSqlHelper` opens a SQL connection and executes synchronously for each non-transaction call.
- F2018 performs duplicate/reference checks and participates in the inventory root fence. When called without a caller transaction, it owns its transaction. It supports a caller transaction; transaction-aware scalar overloads exist in the base/helper, but the current public master `Save_Master_Async` path does not wrap multiple rows in one transaction.
- The Warehouse UI save handler also refreshes lookup collections and list data and records action history. It must not be invoked once per imported row.
- No timings or throughput claims were produced. Existing benchmark code has a CRUD master save/update metric, but its source notes parameter-contract failures; it is not valid import performance evidence without repair/review. The performance seed is explicitly for an isolated performance database and must not be run against the business DB.

| Rows | Row-by-row assessment | Rationale |
|---:|---|---|
| 100 | **LIKELY ACCEPTABLE**, unmeasured | Conservative cap; preloaded lookups and one outer transaction avoid UI refresh per row, but still 100 SP calls |
| 500 | **NEEDS BENCHMARK** | Longer transaction and repeated SP/fence work |
| 1,000 | **NEEDS BENCHMARK; not initial cap** | More memory and transaction duration; no evidence current UI save flow is suitable |
| 5,000 | **NOT RECOMMENDED ROW-BY-ROW** | Large repeated-call and lock-duration exposure without batch evidence |
| 10,000 | **NOT RECOMMENDED ROW-BY-ROW** | Requires benchmarked batch/staging design rather than UI-row-save loop |

If higher volume is later required, reuse the isolated performance environment and add a Product import scenario (10/100/500/1,000 rows), measuring elapsed time, allocations, lock duration, and rollback behavior. First correct the existing benchmark contract and record an independent baseline; do not use the business DB seed script.

## 12. Product Template

No Warehouse Product import template was found in the inspected static templates. `Chu_Hang_Template.xlsx` is for another workflow. `CExcel_Controller.Export_Excel_Template` can populate an existing workbook from object rows (EPPlus), but no Product call site was found. Warehouse `GridExcelExport` is data export, not an import template generator.

| Column | Required | Resolve to | Validation |
|---|---:|---|---|
| `Ma_San_Pham` | Yes | `tbl_DM_San_Pham.Ma_San_Pham` | Text, trim, nonblank, max 100; unique in workbook and DB |
| `Ten_San_Pham` | Yes | `Ten_San_Pham` | Text, trim, nonblank, max 255 |
| `Ma_LSP` | Yes | Unique `tbl_DM_Loai_San_Pham.Ma_LSP` → `Loai_San_Pham_ID` | Text, trim, max 100; reject unknown/ambiguous code |
| `Ten_Don_Vi_Tinh` | Yes | `tbl_DM_Don_Vi_Tinh.Ten_Don_Vi_Tinh` → `Don_Vi_Tinh_ID` | Text, trim, max 200; exact single normalized lookup; no unit code exists |
| `Ghi_Chu` | No | `Ghi_Chu` | Text, max 1000 |

Do not include `Auto_ID`, category/unit IDs, `Created_By`, `Created`, `Last_Updated_By`, or `Last_Updated`. The category code is preferable to category name because both fields exist and `Ma_LSP` is a unique business code. Unit name is required by the existing schema/lookup contract; a stable unit code would require a schema/business decision, not a fabricated mapping.

## 13. Duplicate Policy

**Recommended: Option A, insert-only.** Product `Ma_San_Pham` already has a global unique index and F2018 validation. If a product key exists in the database or appears twice in the file, report an error and reject the entire file. Do not silently skip or update. An upsert creates an Edit permission requirement, increases accidental overwrite/audit risk, and does not match the current simple save contract. The unique index remains the race-safe final guard even after preview validation.

## 14. Transaction Strategy

Current `Save_Master_Async` does not create a transaction spanning multiple rows. In a normal UI path each row's SP can run/commit independently. Thus if row 50 fails after rows 1–49 succeeded, this path does **not** roll back rows 1–49.

**Required now:** parse and validate the complete file before writes; then perform all inserts in one outer DB transaction and roll back the entire file on any database failure. F2018 supports a caller transaction and transaction-aware scalar infrastructure exists, so this is possible without changing table schema or making one UI save per row. Confirm the root-fence behavior remains valid under the outer transaction in integration tests.

**Later hardening:** batch/TVP/staging/background processing only if volume benchmarks justify it. Partial-success mode is not recommended for Phase 1 because it complicates user expectations and history.

## 15. Proposed Phase 1 Architecture

1. Add a Product-only `.xlsx` import action, visible to users with Add permission.
2. Enforce Add permission in the server-side import service at preview and again at commit; derive user/function identity from trusted application context.
3. Stream the workbook with Telerik `SpreadImporter` after a compatibility/licensing spike. Enforce 1 MiB and 100 data rows server-side; reject malformed workbook, formulas in supported columns, duplicate/missing/unknown headers, and unsupported file types.
4. Convert each row into `CProductImportRow`; trim and validate all fields. Load category and unit lookups once, build in-memory maps, and resolve each row to category/unit IDs. Detect within-file duplicate product codes before writing.
5. Show a preview/error list. If any row fails validation, do not write. On confirmation, recheck Add permission and database duplicates, then call F2018 insert under one outer transaction. Any persistence error rolls back the whole file.
6. Commit and record one import-specific action-history event with row count/user/function. Keep per-row Created/LastUpdated audit fields sourced from the current authenticated user and active page function. History currently occurs after save and is best-effort; document that it is not atomic with the data transaction.

No schema change is required for this bounded insert-only approach. Do not call the UI handler's full list/lookup refresh once per imported row.

## 16. Required / Useful / Over-engineered

| Classification | Component | Reason |
|---|---|---|
| REQUIRED | `CProductImportRow` | Explicit workbook-to-domain contract, separate from generic master DTO |
| REQUIRED | Product import service/orchestrator | Central place for trusted authorization, parsing, validation, lookup resolution and transaction boundary |
| REQUIRED | XLSX reader and server-side file/row limits | Current export code is not a reader and UI limit alone is insufficient |
| REQUIRED | Preview with row/field errors | Prevents malformed or duplicate workbook data from reaching writes |
| REQUIRED | Add authorization on server; insert-only duplicate policy | Protects direct service calls and avoids unapproved updates |
| REQUIRED | One all-or-nothing transaction | Avoids partial file persistence |
| USEFUL | Small validation error and preview result types | Makes errors actionable without generic framework machinery |
| USEFUL | Preloaded category/unit dictionaries and one import history summary | Avoids lookup-per-row work and distinguishes import in history |
| USEFUL | Static Product template with headers/examples/text formatting | Makes required keys and code formatting explicit |
| OVER-ENGINEERED FOR PHASE 1 | Generic ImportProfile/reflection mapper/framework | Only one entity is in scope; schemas/keys differ |
| OVER-ENGINEERED FOR PHASE 1 | Background jobs, staging table, TVP/BulkCopy | No measured volume requirement supports their cost yet |
| OVER-ENGINEERED FOR PHASE 1 | Separate complex `CImportError` hierarchy | A compact row/field/message result is sufficient |

## 17. Planned Files

These are candidates for a future implementation, not files changed by this audit. Confirm local folder conventions and service registration during implementation.

| Change | Candidate path | Why |
|---|---|---|
| NEW | `TKS_Thuc_Tap_V11_Data_Access/Entity/Warehouse/CProductImportRow.cs` | Product-specific import row contract |
| NEW | `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Product_Import.razor` | Product file selection, preview and confirmation UI |
| NEW | `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseProductImportService.cs` | Parse, validate, authorize, resolve references and orchestrate transaction; proposed folder placement |
| MODIFY | `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_1_Warehouse_List.razor` | Add button gated by Add and open the import component |
| MODIFY | `TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehouseMaster_Controller.cs` | Add transaction-aware batch-of-inserts entry point or equivalent helper, retaining F2018 contract |
| MODIFY, conditional | `TKS_Thuc_Tap_V11_Web/Program.cs` | Only if Telerik FileSelect is retained: configure a bounded SignalR message size aligned to server-enforced file size |
| NEW | `TKS_Thuc_Tap_V11_Web/wwwroot/FileManagement/Template/San_Pham_Import_Template.xlsx` | Product-specific downloadable template |
| NEW/MODIFY | `TKS_Thuc_Tap_V11_Data_Access.Tests/...` | Parser/service validation, authorization and transaction integration tests; avoid editing unrelated pre-existing test changes |
| DATABASE | None for Phase 1 | Existing product key/FKs and F2018 insert procedure are sufficient according to source scripts |

## 18. Test Matrix

No tests were implemented or run in this audit.

| Area | Minimum case | Expected result |
|---|---|---|
| Workbook shape | Valid template; empty workbook; missing, unknown, duplicate headers | Valid preview; empty/invalid structure rejected with clear error |
| Upload | Wrong extension (`.xls`, `.csv`); malformed `.xlsx`; over 1 MiB | Rejected server-side; no writes |
| Row validation | Blank code/name; unexpected numeric cell; overly long code/name/unit/note; surrounding whitespace | Required errors, type policy applied, length errors; supported text trimmed consistently |
| Lookup | Unknown category code; unknown unit; duplicate/ambiguous normalized unit; Vietnamese Unicode | Correct ID mapping or row error; Vietnamese text preserved |
| Duplicate | Duplicate product code within file; code already in DB; case/accent variant according to DB collation | File rejected; no update/skip; DB unique index race handled with rollback |
| Permission | No Add; Add only; Edit only | No Add denied; Add-only new rows allowed; Edit-only denied for insert-only import |
| Workbook semantics | Formula cell; hidden row; blank rows; formatted numeric-looking code | Explicit documented behavior; no silent formula/cache or row omission surprises |
| Limits | 10 rows; 100 rows; 101 rows | 10/100 accepted if valid; 101 rejected before writes |
| Atomicity | Force DB failure at row 50 in a 100-row transaction | Zero imported rows remain after rollback |
| Audit/history | User/function populated; import-specific history event; history insert fails | Data audit identity correct; behavior of best-effort history failure made visible/documented |
| Performance | Isolated 10/100/500/1,000-row runs | Record time/allocations/lock duration; increase cap only from evidence |

## 19. Decision Table

| Decision | Recommendation | Confidence | Evidence |
|---|---|---|---|
| Product business key | `Ma_San_Pham` (global) | High, source-level | Unique index + F2018 duplicate check |
| Category lookup key | `Ma_LSP` | High, source-level | Unique index and F2017 validation |
| Unit lookup key | Trimmed/normalized `Ten_Don_Vi_Tinh`; resolve to ID | High for contract; live collision behavior unknown | No unit code; page/lookup `Code=''`; unique name index; F2016 trim/CI_AI duplicate check |
| Supplier lookup key | `Ma_NCC` | High, source-level | Unique index + F2019 validation |
| Warehouse lookup key | `Ten_Kho` | High, source-level | Unique index + F2009 validation; no warehouse code |
| Import permission | Add only for insert-only Phase 1 | High | Existing Add/Edit rights and no Import right found |
| Server-side authorization | Required in import service | High | Current controller/SP write path has no permission enforcement |
| Duplicate policy | Insert-only; any existing/in-file code duplicate fails whole file | High | Product unique index, save behavior, least privilege |
| Excel library | Reuse Telerik `SpreadImporter` after compatibility/license proof | Medium | Streaming package resolved transitively; no reader usage in source |
| Max file size | 1 MiB, server enforced and transport aligned | Medium; conservative recommendation | Existing FileSelect says 20,480,000 bytes; no explicit Hub limit; deployed limits unknown |
| Max row count | 100 data rows initially | Medium; unbenchmarked | Row-by-row SP path and preview/transaction cost; no import timings |
| Transaction strategy | Validate entire workbook, one outer transaction, rollback all on write failure | High | F2018 supports caller transaction; current Save path has none across rows |
| Phase 1 scope | Product-only `.xlsx`, insert-only, preview/confirm, Add-only, 100 rows | High as minimal safe scope | Existing Product fields/lookups and source write procedures |

## 20. Open Questions

1. What is the expected real Product import size? The 100-row cap is conservative and unmeasured; larger routine files require an isolated benchmark before raising it.
2. Is resolving unit by `Ten_Don_Vi_Tinh` acceptable for the template, or does the business require a stable unit code? The current schema and lookup do not provide one.
3. Should one invalid/duplicate row reject the entire workbook? This audit recommends all-or-nothing; confirm that this matches the business workflow.
4. Is the application's Telerik Document Processing license entitlement valid for the deployed environment? Project registration and package presence do not prove an activated license.
5. What are the deployed IIS/reverse-proxy/SignalR limits? Repository configuration does not establish production host settings; verify before selecting the upload transport.

---

**Audit boundary:** Read/trace/report only. No source, SQL, package, permission, database, migration, commit, stash, or worktree cleanup was performed. No build, test, import, live DB query, or performance run was performed.
