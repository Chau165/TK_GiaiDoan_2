# Warehouse Permission Deployment Validation Report

Date: 2026-09-11  
Scope: Warehouse Permission SQL Contract Cleanup Phase 3 deployment preparation  
Target used for validation: `TKS_Thuc_Tap_V11_C01C02_Test_20260906` on `localhost`  
Production/business database: **not touched**

## 1. Security Changes

The source security bundle now grants only procedure execution for the new
permission-delete contract:

- `Database/WarehouseModule.Security.sql:45`
  adds `GRANT EXECUTE ON OBJECT::dbo.sp_DM_Kho_User_Delete TO [Warehouse_Application];`.
- `Database/WarehouseModule.Security.Preflight.sql:258`
  adds `dbo.sp_DM_Kho_User_Delete` to the approved-procedure verification list.
- No table DML grant was added for `dbo.tbl_DM_Kho_User`.
- No new security role was created. The existing `Warehouse_Application` role
  remains the boundary used by the bundle.

The test database initially had no `Warehouse_Application` role. The existing
security bundle created it as designed. A contained user named
`Phase3_Test_Application` was created temporarily only to verify role
membership and effective execution; it was removed after the smoke test.

## 2. Migration Checklist

The repository README documents the deployment order as schema, procedures,
security, and preflight for the Warehouse module. The requested test migration
was executed with `sqlcmd -b` in that order.

| Step | Test-database verification | Result |
| --- | --- | --- |
| Backup | Copy-only, checksum backup completed; `RESTORE VERIFYONLY WITH CHECKSUM` reported that the backup set is valid. | PASS |
| Schema | `dbo.tbl_DM_Kho_User` exists; 7 rows remain after cleanup; FK to `dbo.tbl_DM_Kho` and unique `(Ma_Dang_Nhap, Kho_ID)` index verified. | PASS |
| Procedures | `dbo.sp_DM_Kho_User_Delete` exists; its delete contract targets `dbo.tbl_DM_Kho_User`; generic `sp_DM_Delete` has no `KhoUser` branch. | PASS |
| Security | `Warehouse_Application` exists; it has `EXECUTE` on `dbo.sp_DM_Kho_User_Delete`; no direct `INSERT`, `UPDATE`, or `DELETE` grant exists on `dbo.tbl_DM_Kho_User`. | PASS |
| Preflight | `WarehouseModule.Security.Preflight.sql` completed with 0 failed checks using the temporary test principal mapped to `Warehouse_Application`. | PASS |

Backup artifact:

`C:\Program Files\Microsoft SQL Server\MSSQL15.MSSQLSERVER19\MSSQL\Backup\TKS_Thuc_Tap_V11_C01C02_Test_20260906_Phase3_PreDeploy_20260911.bak`

The `WarehouseModule.Procedures.sql` and `WarehouseModule.Schema.sql` files
were applied to the disposable test database but were not modified by this
task. The source changes in this task are limited to the two security files
and this report.

## 3. Database Test Result

Final read-only validation on `TKS_Thuc_Tap_V11_C01C02_Test_20260906` returned:

- Database is `READ_WRITE` and is the designated C01/C02 test database.
- `dbo.tbl_DM_Kho_User`: present; 7 non-test rows remain.
- `dbo.sp_DM_Kho_User_Delete`: present.
- `dbo.sp_DM_Delete`: `PASS_NO_KHOUSER`.
- `Warehouse_Application`: present.
- `EXECUTE` on `dbo.sp_DM_Kho_User_Delete`: present.
- Direct mapping-table DML grant: absent.
- Remaining Phase 3 test markers: 0.

The application/database contract is therefore deployable on this test
database. No command in this validation targeted
`TKS_Thuc_Tap_V11_GiaiDoan2`, the business database, or the performance
database.

## 4. Smoke Test Result

Two disposable mappings were created and removed, with all test data cleaned
afterward:

1. SQL security smoke test: mapping `Auto_ID=1413` was present before the
   call and absent afterward when the procedure was invoked under the
   temporary application principal. Result: PASS.
2. C# controller smoke test: mapping `Auto_ID=1414` was deleted through
   `CWarehousePermission_Controller.Delete_Kho_User_Async`, which calls
   `sp_DM_Kho_User_Delete`. The row was absent afterward. Result: PASS.

The temporary controller harness and temporary database principal were
removed. No test marker remains in the test database.

## 5. Regression Result

Read-only and contract checks completed as follows:

- `sp_DM_Kho_User_List_Allowed`: executed under the temporary application
  role and returned 7 authorized warehouse rows. PASS.
- `sp_XNK_Document_List`: executed under the temporary application role. PASS.
- `sp_BC_Chi_Tiet_Nhap` and `sp_BC_Chi_Tiet_Xuat`: executed for the test
  warehouse/date scope. PASS.
- `sp_XNK_Document_Post`: object and application-role authorization guard were
  verified, but the procedure was not invoked because it mutates business
  document data. No mutation was performed.
- Movement and current-balance procedures: object/definition and security
  mapping were verified. They were not invoked because the check is intended
  to remain read-only and must not write Inventory state or depend on worker
  freshness.
- Focused C# contract/UI tests: **35 passed, 0 failed, 0 skipped**.
- Solution build with `dotnet build TKS_Thuc_Tap_V11.sln --no-restore`:
  **0 errors, 24 existing Telerik licensing warnings**.

No application, Inventory, Document, benchmark, or controller source logic was
changed in this deployment-preparation task.

## 6. Production Deployment Recommendation

Status: **BLOCKED**

The disposable test migration and permission-delete smoke test passed, but
this report does not authorize or prove business-database deployment. The
business database was intentionally not touched, so the following gates must
be completed against the exact business target before release:

1. Take and verify a backup of the business database.
2. Apply the repository deployment bundle in the documented order, including
   any repository-required CoreLog prerequisite before the Warehouse bundle:
   Schema, Procedures, Security, then Preflight.
3. Run preflight using the real application login/user mapping, not the
   temporary test principal.
4. Verify effective `EXECUTE` on `dbo.sp_DM_Kho_User_Delete` and absence of
   direct mapping-table DML grants.
5. Coordinate application rollout so the new controller contract and database
   procedure are available together; then run a controlled permission-delete
   smoke test.
6. Run the business-approved document, report, authorization, and Inventory
   regression checks.

Until those business-database gates pass, the appropriate release status is
`BLOCKED`, despite the test-database result being green.
