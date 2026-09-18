# Warehouse Permission Phase 3 Business Database Deployment Result

Date: 2026-09-11  
Server: `localhost` (resolved SQL Server: `DESKTOP-NHQ7QPL\MSSQLSERVER19`)  
Database: `TKS_Thuc_Tap_V11_GiaiDoan2`  
Environment: **Business database / active local business instance**  
Application source changed: **No**  
Commit created: **No**

## Deployment status

```text
DEPLOYMENT_STATUS: BLOCKED
TARGET_DATABASE: localhost / TKS_Thuc_Tap_V11_GiaiDoan2 / Business database
BACKUP_STATUS: PASS
SQL_MIGRATION_RESULT: PROCEDURES PASS; SECURITY PASS; PREFLIGHT BLOCKED
SECURITY_RESULT: PASS for role and procedure grant
PREFLIGHT_RESULT: FAIL - 28 failed checks
SMOKE_TEST_RESULT: NOT_RUN - blocked by unresolved application principal
REGRESSION_RESULT: READ_ONLY BUSINESS PROCEDURES PASS; APPLICATION-ROLE EVIDENCE BLOCKED
```

The SQL procedure and security changes were applied to the identified
business database. Release activation remains blocked because the repository
does not contain, and the database does not currently have, the exact
application login/database-user mapping required by the security preflight.

## 1. Target confirmation

The target was not inferred from a disposable database. It was confirmed from
the repository and SQL Server:

- `Database/README.md` identifies `TKS_Thuc_Tap_V11_GiaiDoan2` as the active
  local database and specifies Windows Integrated Security.
- `Database/WarehouseModule.Schema.sql` is headed for the same database.
- `sys.databases` reported the target as `ONLINE`, `MULTI_USER`, and
  `READ_WRITE`.
- The disposable test database was not used for this deployment.
- The performance, clone, and other non-business databases were not targeted.

## 2. Backup status

`BACKUP_STATUS: PASS`

A copy-only, checksum backup was completed before migration:

```text
C:\Program Files\Microsoft SQL Server\MSSQL15.MSSQLSERVER19\MSSQL\Backup\TKS_Thuc_Tap_V11_GiaiDoan2_Phase3_PreDeploy_20260911.bak
```

`RESTORE VERIFYONLY ... WITH CHECKSUM` reported that the backup set is valid.
No migration command was run before this backup completed.

## 3. SQL migration result

### Step 1 - Procedures

Command executed with `sqlcmd -b`:

```text
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.Procedures.sql
```

Result: **PASS**.

Post-step checks confirmed:

- `dbo.sp_DM_Kho_User_Delete` exists.
- Its definition deletes from `dbo.tbl_DM_Kho_User` by `Auto_ID`.
- `dbo.sp_DM_Delete` no longer contains the `KhoUser` branch.

`WarehouseModule.Procedures.sql` was applied as the existing deployment
bundle; it was not source-modified during this task.

### Step 2 - Security

Command executed with `sqlcmd -b`:

```text
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.Security.sql
```

Result: **PASS**.

Post-step checks confirmed:

- `Warehouse_Application` exists.
- The role has `EXECUTE` on `dbo.sp_DM_Kho_User_Delete`.
- The role has no direct `INSERT`, `UPDATE`, or `DELETE` grant on
  `dbo.tbl_DM_Kho_User`.

The source diff for this task is one grant line:

```diff
+GRANT EXECUTE ON OBJECT::dbo.sp_DM_Kho_User_Delete TO [Warehouse_Application];
```

### Step 3 - Security preflight

The preflight contract explicitly requires the exact application login and
database user. The repository README contains only the example
`DOMAIN\\WarehouseApp`; no corresponding principal was found in the business
database. Only system principals (`guest`, `INFORMATION_SCHEMA`, and `sys`)
were present in the database-principal audit.

The preflight was run with its documented unresolved-identity sentinel. It
completed its checks and failed with exit code 1:

```text
FINAL | All checks PASS | 28 failed check(s) | FAIL
Warehouse security preflight failed; production application identity is not deployment-ready.
```

This is an intentional fail-closed result. No fake application user, login,
role membership, or privilege was created in the business database.

The source diff for the preflight is one approved-procedure entry:

```diff
-(N'dbo.sp_DM_Kho_User_List_Allowed');
+(N'dbo.sp_DM_Kho_User_List_Allowed'),
+(N'dbo.sp_DM_Kho_User_Delete');
```

## 4. Database validation

Final read-only validation on `TKS_Thuc_Tap_V11_GiaiDoan2` confirmed:

| Check | Result |
| --- | --- |
| `dbo.tbl_DM_Kho_User` exists | PASS; 7 rows, same count observed before migration |
| `dbo.sp_DM_Kho_User_Delete` exists and has the expected delete contract | PASS |
| `dbo.sp_DM_Delete` contains no `KhoUser` branch | PASS |
| `Warehouse_Application` exists | PASS |
| `EXECUTE` on `dbo.sp_DM_Kho_User_Delete` | PASS |
| Direct mapping-table DML grant | PASS; none present |
| Temporary test principal remains | PASS; none was created on business DB |

The database is therefore partially migrated at the SQL-contract level, but
not certified for application activation until the real application principal
is supplied and passes preflight.

## 5. Application smoke test

`SMOKE_TEST_RESULT: NOT_RUN`

The UI/controller delete flow was not invoked against business data. Running
it under the current developer/admin connection would not validate the
`Warehouse_Application` security boundary and could delete a real
user-warehouse mapping. The C# source path had already been migrated to
`CWarehousePermission_Controller.Delete_Kho_User_Async` →
`sp_DM_Kho_User_Delete`, but runtime application-role evidence is not claimed
here.

## 6. Read-only regression

The following business procedures were executed read-only under the trusted
deployment connection using `thuctap_kho` and warehouse `175`:

- Authorization lookup `sp_DM_Kho_User_List_Allowed`: PASS; returned 7
  authorized warehouses.
- Receipt and issue document lists: PASS.
- Receipt and issue detail reports: PASS.
- Warehouse movement report: PASS.
- Current warehouse-balance page: PASS.

`sp_XNK_Document_Post` was not invoked because it mutates business document
data; its object and security contract were already verified in the test
validation. No Inventory mutation procedure was invoked. The regression result
therefore confirms read-only business behavior, not application-role
authorization.

## 7. Risk and required next step

`DEPLOYMENT_STATUS` remains **BLOCKED** for these reasons:

1. The exact application login and database user are unknown/unmapped in the
   business database.
2. `WarehouseModule.Security.Preflight.sql` cannot verify role membership,
   least-privilege restrictions, or effective grants without that identity.
3. The new SQL contract is already installed, so application rollout must be
   coordinated; an older application binary that still calls generic
   `sp_DM_Delete` for `KhoUser` would not be a supported compatibility path.

Before declaring `COMPLETED`, an authorized DBA/application owner must provide
the real application principal mapping, then run the unchanged preflight with
that mapping and obtain zero failures. After that, run the controlled
application smoke test and business-approved regression checks. Do not use the
developer/admin identity as application-security evidence.
