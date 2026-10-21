# Scoped Action History Deployment Preflight (Superseded)

**Status: REJECTED — do not deploy the scoped procedure.** The user directed the application to use the company procedure `FCommon_Sys_sp_sel_List_Log_Record_Action_History`. The former scoped-procedure proposal below is retained only as historical preflight context. No SQL connection, DDL, or DML was executed.

## A. Target environment

| Item | Finding |
|---|---|
| Checked-in local launch target | `localhost` / `TKS_Thuc_Tap_V11_GiaiDoan2`, read from `TKS_Thuc_Tap_V11_Web/appsettings.json:9-10`. |
| Expected local environment | `Development` in both launch profiles. |
| Confirmed business SQL Server/database | **NOT VERIFIED**. The local launch target is not evidence that this is the business database. |
| `@@SERVERNAME`, `DB_NAME()`, `ORIGINAL_LOGIN()`, `SUSER_SNAME()` | **NOT QUERIED**; no SQL connection was opened. |
| Deployment approval | **NOT GIVEN**. No DDL was run. |
| Credentials | No credentials were printed or written to this report. |

`Program.cs:39` reads the named `ConnectionStrings:TKS_Thuc_Tap_V11_Conn_String`. The checked-in value resolves to a local Development target. A server/database supplied for the business deployment, or an identified secured runtime configuration source, is required before read-only database preflight.

## B. Database preflight

The following are source/repository observations only. Database existence, definitions, parameters, dependencies, permissions, and view columns remain **NOT VERIFIED** until the exact business target is identified.

| Object | Exists before | Parameters / source contract | Dependency status | Result |
|---|---|---|---|---|
| `dbo.FCommon_Sys_sp_sel_List_Log_Record_Action_History` | NOT VERIFIED | Repository source: `@Ref_ID BIGINT`; lines 5-15 | Reads `dbo.view_Log_Record_Action_History`; DB definition and permissions not checked | Pending |
| `dbo.FCommon_Sys_sp_sel_List_Log_Record_Action_History_By_Scope` | NOT VERIFIED | Not present in current repository source; former scoped proposal was removed | Not called by current application; live database existence was not checked | Withdrawn |
| `dbo.FQ_425_RAH_sp_ins_Insert` | NOT VERIFIED | Live signature/definition not checked | No change proposed | Pending |
| `dbo.FQ_422_IE_sp_ins_Insert` | NOT VERIFIED | Live signature/definition not checked | No change proposed | Pending |
| `dbo.view_Log_Record_Action_History` | NOT VERIFIED | Live columns/definition not checked | Must expose the fields required by the scoped SELECT and entity mapping | Pending |

## C. Withdrawn proposal and current source path

The prior proposal to deploy `dbo.FCommon_Sys_sp_sel_List_Log_Record_Action_History_By_Scope` is withdrawn. Do not deploy it. The six Master Data Info components now call the one-argument controller method, which uses the company procedure with `Ref_ID` only. The scoped procedure is no longer defined in the repository SQL script or called by application code.

- No database deployment or DDL is authorized by the current request.
- The legacy procedure and existing insert procedures remain unchanged.
- No database connection, DDL, or DML was executed.

The source path has been reverted to the legacy company procedure. Database compatibility and runtime execution have not been re-tested in this change.

## D. Verification

| Check | Status |
|---|---|
| Live procedure exists in target | NOT VERIFIED |
| Live definition matches source | NOT VERIFIED |
| Live parameter metadata matches controller | NOT VERIFIED |
| View columns and scoped filtering | NOT VERIFIED |
| Invalid-scope rejection | PARTIAL SOURCE EVIDENCE: NULL/blank rejected; nonblank invalid rule undefined |
| Legacy procedure compatibility | NOT VERIFIED against target |
| App identity permissions | NOT VERIFIED |
| Source/build/tests | NOT RUN in this deployment preflight |
| Runtime/browser UAT | NOT RUN |

No SQL connection, DDL, or DML was executed in this preflight.

## E. Rollback

No rollback SQL has been generated because the target and its current procedure definition are unknown. After read-only preflight, record the existing scoped procedure definition (if any) and prepare a target-specific rollback script before the DDL approval gate. Roll back by restoring that definition, or by dropping only the newly created scoped procedure if it was absent before deployment and no dependency prevents that action.

## F. Remaining issues

- Exact business server/database and execution identity are not established.
- Live object definitions, schema, dependencies, and permissions are unverified.
- The source only rejects blank function codes; the business rule for other invalid values is unspecified.
- Runtime verification and browser UAT remain pending.
- The 26 legacy readers described in the deployment request must remain compatible; no caller or legacy procedure is to be changed in this deployment.

## G. Historical preflight status

- `DEPLOYED`: **NO**
- `DB_VERIFIED`: **NO**
- `SOURCE_TEST_PASS`: **NOT RUN**
- `RUNTIME_PENDING`: **YES**
- `DEPLOYMENT_APPROVED`: **NO — proposal rejected; do not deploy**
