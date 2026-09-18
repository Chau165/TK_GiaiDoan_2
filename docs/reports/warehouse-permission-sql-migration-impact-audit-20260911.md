# Warehouse Permission SQL Migration Impact Audit

Audit date: 2026-09-11  
Scope: Phase 3 SQL contract cleanup before deployment  
Change policy: source, SQL, database, jobs, and tools were not modified; this report is the only artifact created by this audit.

## Executive Summary

**AUDIT_STATUS: BLOCKED_UNTIL_DEPLOYMENT_GATES**

The repository audit found no remaining internal caller that sends `KhoUser` to `sp_DM_Delete`. No job, benchmark tool, manual SQL script, or migration script uses that old delete contract with `KhoUser`.

Two deployment gates remain:

1. `Warehouse_Application` has no explicit `EXECUTE` grant or preflight entry for the new `dbo.sp_DM_Kho_User_Delete` procedure. The target database must prove an effective execute permission before application rollout.
2. The current SQL and application contracts are not rolling-deployment compatible. The old application requires the removed `sp_DM_Delete('KhoUser', ...)` branch, while the new application requires `sp_DM_Kho_User_Delete`. They must be deployed in a coordinated maintenance window or through a staged compatibility release.

## 1. `sp_DM_Delete` Dependency Audit

### Repository callers

| Location | Finding | Impact |
|---|---|---|
| `TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehouseMaster_Controller.cs:62` | Calls `sp_DM_Delete` with the variable `p_strMaster_Type` | Generic Master Data path; no literal `KhoUser` caller found |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehousePermission_Controller.cs:50` | Calls `sp_DM_Kho_User_Delete` | Correct Phase 3 permission path |
| `Database/Tests/WarehouseModule.ValidationMessages.IntegrationTests.sql:182` | Calls `sp_DM_Delete` with `Entity = N'Unknown'` | Negative validation test; not a `KhoUser` dependency |
| `Database/WarehouseModule.Procedures.sql:2137` | Defines `sp_DM_Delete` | Generic procedure definition only |

The old `KhoUser` branch is absent from the current `sp_DM_Delete` definition. No SQL script was found that invokes `sp_DM_Delete` with `KhoUser`.

### Static search scope

The exact-name search covered source files, all `Database` SQL files, `Database/Jobs`, `Database/Migrations`, `Database/Performance`, `Database/Tests`, and repository PowerShell/CMD/BAT/SQL files, excluding build output, `.codegraph`, and reports.

## 2. Other Stored Procedure References

No stored procedure contains the literal generic entity value `KhoUser` after the Phase 3 cleanup.

There are intentional authorization references to the mapping table and dedicated permission procedures. They are not dependencies on the generic delete contract and must remain:

- Permission boundary: `sp_DM_Kho_User_Ensure_Access`, `sp_DM_Kho_User_List_Allowed`, `sp_DM_Kho_User_User_List`, `sp_DM_Kho_User_List`, `sp_DM_Kho_User_Page`, `sp_DM_Kho_User_Save`, and `sp_DM_Kho_User_Delete`.
- Document boundary: `sp_XNK_Document_List`, `sp_XNK_Document_Page`, `sp_XNK_Document_Detail_List`, `sp_XNK_Document_Post`, and the receipt/issue save/delete procedures call the access guard or filter through `tbl_DM_Kho_User`.
- Report/inventory boundary: the warehouse detail, movement, current-balance, and report-scope procedures use `tbl_DM_Kho_User` for authorization filtering.

These references represent User-Warehouse authorization checks. Removing or renaming them would be outside Phase 3 and would risk changing document/report behavior.

## 3. Job, Tool, Manual Script, and Test Audit

| Area | Result |
|---|---|
| `Database/Jobs` | No `sp_DM_Delete`, `KhoUser`, or `sp_DM_Kho_User_Delete` call found |
| `Database/Migrations` | No Phase 3 delete reference; the only migration is Inventory Snapshot hardening |
| `Database/Performance` | No generic delete call; performance seed scripts insert `tbl_DM_Kho_User` rows only |
| `Database/Tests` | Only `sp_DM_Delete @Entity = N'Unknown'`; permission tests inspect source contracts rather than execute the old delete path |
| Repository `*.ps1`, `*.cmd`, `*.bat`, and `*.sql` files | No `sp_DM_Delete` call with `KhoUser` found |
| Benchmark scripts | They apply the consolidated procedure bundle to benchmark databases but do not call generic delete with `KhoUser` |

No operational job or manual script dependency was identified.

## 4. Migration Order Analysis

The documented fresh-database order in `Database/README.md` is:

1. `WarehouseModule.Schema.sql`
2. `WarehouseModule.Procedures.sql`
3. `WarehouseModule.Security.sql`
4. `WarehouseModule.Security.Preflight.sql`
5. menu and sample-data scripts, followed by tests

The order is structurally correct for object creation: `tbl_DM_Kho_User` is created by the schema, and `sp_DM_Kho_User_Delete` is defined by the procedure bundle. The procedure bundle must therefore be applied after the schema and before security/preflight verification.

### Migration-order risks

#### Application/SQL version skew

- Deploy SQL cleanup first while the old application is running: permission delete continues to call `sp_DM_Delete('KhoUser', ...)`, which now throws the generic invalid-entity error.
- Deploy the new application first while the old database is running: permission delete calls `sp_DM_Kho_User_Delete`, which does not yet exist.

The current change is therefore unsafe as an independently rolled application or database deployment.

#### Security contract gap

`Database/WarehouseModule.Security.sql` grants `Warehouse_Application` explicit execute permissions for approved document/report procedures and `sp_DM_Kho_User_List_Allowed`, but not `sp_DM_Kho_User_Delete`. `Database/WarehouseModule.Security.Preflight.sql` likewise does not include the new delete procedure in its approved-procedure list.

The repository does not prove that the target application principal can execute the new procedure. This is a deployment gate, not a reason to grant broad direct table DML.

#### Partial procedure-bundle failure

`WarehouseModule.Procedures.sql` is a consolidated sequence of `CREATE OR ALTER PROCEDURE` batches. The new generic branch removal is at approximately line 2143, while the new permission procedure is defined later at approximately line 2927. No single outer deployment transaction wrapping the complete procedure bundle was found. If the bundle fails part way through, the database could temporarily have the generic branch removed before the new permission procedure is created.

## 5. Recommended Deployment Gate

Before deployment is approved:

1. Use a maintenance window or another mechanism that prevents permission-delete requests during the SQL/application transition.
2. Apply `WarehouseModule.Procedures.sql` only after the target schema is present and the command completes successfully.
3. Verify that `OBJECT_ID(N'dbo.sp_DM_Kho_User_Delete', N'P')` exists and that `sp_DM_Delete` no longer contains the `KhoUser` branch.
4. Verify effective `EXECUTE` permission for the actual application principal on `dbo.sp_DM_Kho_User_Delete`; update the approved security contract through a separately authorized change if the explicit `Warehouse_Application` role is the runtime boundary.
5. Deploy the application that calls the dedicated procedure, restart it if required by the hosting model, and run a permission-delete smoke test against a disposable or approved test database.
6. If rolling deployment is mandatory, use a staged compatibility release: create the new procedure first, deploy the new application, observe successful usage, and remove the generic branch only in a later controlled step. That staged approach is not performed by this audit.

## 6. Final Assessment

### Confirmed safe from repository dependency perspective

- No internal SQL/job/tool/manual caller uses `sp_DM_Delete('KhoUser', ...)`.
- Generic Master Controller usage remains limited to the generic Master path.
- Permission, document, inventory, and report procedures reference the authorization mapping directly and do not depend on the generic delete branch.

### Not safe to deploy without additional gates

- Effective execute permission for the new procedure is unverified and absent from the repository's explicit role grant/preflight lists.
- An uncoordinated application/database rollout creates a guaranteed compatibility failure in one direction or the other.
- A failure in the consolidated procedure script could leave an intermediate contract state.

No source code, SQL script, database object, job, benchmark, or configuration was changed. Only this report was created.
