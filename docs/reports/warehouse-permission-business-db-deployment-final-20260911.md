# Warehouse Permission Business Database Deployment - Final Validation

Date: 2026-09-11  
Server: `localhost` / `DESKTOP-NHQ7QPL\MSSQLSERVER19`  
Database: `TKS_Thuc_Tap_V11_GiaiDoan2`  
Scope: Phase 3 application-identity verification only  
Source/SQL changes in this validation: **None**  
Rollback performed: **No**

## Final result

```text
APPLICATION_IDENTITY: DESKTOP-NHQ7QPL\Surface
DATABASE_MAPPING: FAIL
PREFLIGHT: FAIL
SMOKE_TEST: NOT_RUN
FINAL_STATUS: BLOCKED
DEPLOYMENT_STATUS: BLOCKED
```

The supplied application login was checked against the business database. The
server login exists and is enabled, but it has no database user mapped by SID
and is not a member of `Warehouse_Application`. The current SQL context
resolves as `dbo` because the audit account is sysadmin; `dbo` was not used as
the application user.

## 1. Application identity input

The application-session identity supplied for this validation is:

```text
Application Login: DESKTOP-NHQ7QPL\Surface
Original Login:    MicrosoftAccount\kiriza165@gmail.com
Host:              DESKTOP-NHQ7QPL
Program:           Core Microsoft SqlClient Data Provider
Database:          TKS_Thuc_Tap_V11_GiaiDoan2
```

The identity values above are treated as the application-session evidence
provided for this run. No `sa` or `dbo` value was substituted into the
preflight command. The database mapping itself was independently checked
read-only using `sys.server_principals`, `sys.database_principals`, and
`sys.database_role_members`.

## 2. Database mapping

Expected chain:

```text
DESKTOP-NHQ7QPL\Surface
        |
        v
Database User
        |
        v
Warehouse_Application
```

Observed on `TKS_Thuc_Tap_V11_GiaiDoan2`:

| Check | Result |
| --- | --- |
| Server login `DESKTOP-NHQ7QPL\Surface` exists | PASS; `WINDOWS_LOGIN`, enabled |
| Database user mapped by login SID | FAIL; no mapped database user |
| `Warehouse_Application` role exists | PASS |
| Mapped database user is a member of `Warehouse_Application` | FAIL; no mapped user/member |
| Use of `dbo` as `ApplicationUser` | NOT USED |
| Creation of login/user or role | NOT PERFORMED |

The role already had the Phase 3 procedure grant from the earlier SQL
migration, but an effective application principal cannot be verified without
the database-user mapping.

## 3. Security preflight

The unchanged preflight was run against the business database with:

```text
ApplicationLogin = DESKTOP-NHQ7QPL\Surface
ApplicationUser  = __APPLICATION_PRINCIPAL_NOT_VERIFIED__
```

The sentinel was necessary because the mapping query found no real database
user. The preflight correctly failed closed:

```text
APPLICATION_PRINCIPAL       | ... | APPLICATION_PRINCIPAL_NOT_VERIFIED | FAIL
Warehouse_Application membership | ... | NOT_VERIFIED_OR_NOT_MEMBER | FAIL
sysadmin membership          | 0 | 1 | FAIL
db_owner membership         | 0 | NOT_VERIFIED | FAIL
db_datawriter membership    | 0 | NOT_VERIFIED | FAIL
Can impersonate trusted principal | 0 | NOT_VERIFIED | FAIL
FINAL                        | All checks PASS | 28 failed check(s) | FAIL
PREFLIGHT_EXIT               | 1
```

No grants, users, logins, role membership, or SQL definitions were changed by
the preflight. The command only inspected metadata and raised its documented
deployment-gate error.

## 4. Application smoke test

```text
SMOKE_TEST: NOT_RUN / BLOCKED
```

The UI permission-delete flow was not invoked because preflight did not pass.
This avoids deleting even a test mapping through an unverified `dbo`/sysadmin
connection and avoids reporting an application-role test that was not actually
performed.

Expected flow after the identity mapping is corrected:

```text
UI Permission Management
  -> CWarehousePermission_Controller
  -> Delete_Kho_User_Async()
  -> sp_DM_Kho_User_Delete
```

The smoke test must use a disposable test mapping and must verify that the
mapping is deleted, without touching a real business mapping.

## 5. Validation status

- The Phase 3 procedure and security grant remain present from the previous
  deployment step.
- No application source was modified.
- No SQL source was modified.
- No migration was created or rerun.
- No benchmark, Inventory, Document, or business mapping data was changed by
  this validation.
- No rollback was performed.

## 6. Required next step

An authorized DBA/application owner must identify or provision the approved
database user mapping for the real application login through the organization's
normal security process. That action is intentionally outside this audit and
was not performed here.

After the mapping exists, rerun the unchanged preflight with:

```powershell
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 `
  -i Database\WarehouseModule.Security.Preflight.sql `
  -v ApplicationLogin="DESKTOP-NHQ7QPL\Surface" ApplicationUser="<verified database user>"
```

Only if all checks pass should the controlled application smoke test be run and
the final status reconsidered as `COMPLETED`.
