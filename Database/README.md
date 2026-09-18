# Database setup

The active local database is `TKS_Thuc_Tap_V11_GiaiDoan2`, restored from the provided backup. The application connection string uses Windows Integrated Security and points to that database.

Apply the shared action-history lookup after restoring the supplied database:

```powershell
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\CoreLogActionHistory.Procedures.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\CoreLogActionHistory.IntegrationTests.sql
```

To apply the warehouse module to a freshly restored copy, run in order:

```powershell
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.Schema.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.Procedures.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.Security.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.Security.Preflight.sql -v ApplicationLogin="DOMAIN\\WarehouseApp" ApplicationUser="DOMAIN\\WarehouseApp"
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.Menu.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.SampleData.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.SampleData.Repair.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.IntegrationTests.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.ValidationMessages.IntegrationTests.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.UnicodeDataTests.sql
```

`WarehouseModule.Security.sql` is mandatory: it establishes the supported
application boundary (stored-procedure EXECUTE plus no direct ledger/current
projection DML). `WarehouseModule.Security.Preflight.sql` is read-only and
must be run with the exact application login/database-user mapping before
activation. If that identity is not known, leave the placeholders unchanged;
pass `-v ApplicationLogin=__APPLICATION_PRINCIPAL_NOT_VERIFIED__
ApplicationUser=__APPLICATION_PRINCIPAL_NOT_VERIFIED__`; the preflight
deliberately fails as `APPLICATION_PRINCIPAL_NOT_VERIFIED`.
The security script does not create or map a production login, and it never
contains a password. A principal with `db_owner`, `db_datawriter`, sysadmin,
or equivalent direct table DML is outside the supported application contract.

`Database\WarehouseDocumentPosting.Procedures.sql` is deprecated and contains
no procedure definitions. Do not use it as a second warehouse procedure bundle;
document posting is deployed by `WarehouseModule.Procedures.sql`.

## Daily Movement Aggregate cutover

After deploying the schema and procedures, initialize the new report source once
from the posted ledger before allowing `InventoryReportPaged` to use it:

```powershell
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -Q "EXEC dbo.sp_Inventory_Movement_Bootstrap_From_Ledger;"
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseInventoryMovementAggregate.IntegrationTests.sql
```

Deploy `Database\Jobs\WarehouseInventoryMovementRebuild.SqlAgent.sql` against
`msdb` to process the queue every minute. Until that worker completes a queued
scope, the report returns a retryable freshness error rather than stale stock data.

Restart the Blazor application after the menu script runs so its function/permission cache reloads. The warehouse routes are:

- `/Kho/Quan_Ly` for Master Data > Kho.
- `/Kho/Nhap_Kho` for Quản lý kho > Nhập kho.
- `/Kho/Xuat_Kho` for Quản lý kho > Xuất kho.
- `/Kho/Ton_Kho` for Quản lý kho > Tồn kho.
- `/Kho/Bao_Cao` for Quản lý kho > Báo cáo.
- `/Kho/Phan_Quyen` for Quản trị > Phân quyền kho-user.

## Authoritative final local-demo deployment order

For a fresh restored copy, use this order. All `sqlcmd` commands below are
examples for the current local-demo instance and database; do not put passwords
or other secrets in this file.

1. Apply `Database\CoreLogActionHistory.Procedures.sql` to the application
   database.
2. Apply `Database\WarehouseModule.Schema.sql` to create/upgrade the warehouse
   tables, queue lifecycle, and worker heartbeat state.
3. Apply `Database\WarehouseModule.Procedures.sql` to install the canonical
   document, posting, report, movement, snapshot, monitor, and reconciliation
   procedures.
4. Apply `Database\WarehouseModule.Security.sql`, then run the read-only
   `Database\WarehouseModule.Security.Preflight.sql` with the exact intended
   application login/user mapping before activation. Do not use a privileged
   developer identity as production-security evidence.
5. After schema and procedures are deployed, run the required one-time
   cutover/bootstrap from the posted ledger:

   ```powershell
   sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -Q "EXEC dbo.sp_Inventory_Movement_Bootstrap_From_Ledger;"
   ```

   The bootstrap must complete before relying on `InventoryReportPaged` for
   daily movement freshness. Do not run it repeatedly as a substitute for the
   worker.

6. Deploy these SQL Agent scripts against `msdb` in the same final system:

   ```text
   Database\Jobs\WarehouseInventoryMovementRebuild.SqlAgent.sql
   Database\Jobs\WarehouseInventorySnapshotRebuild.SqlAgent.sql
   Database\Jobs\WarehouseInventorySnapshotFinalize.SqlAgent.sql
   Database\Jobs\WarehouseInventorySnapshotMonitor.SqlAgent.sql
   ```

   Their expected active schedules are: Movement Aggregate Rebuild every
   minute; Snapshot Repair every 5 minutes; Snapshot Monitor every 15 minutes;
   and Snapshot Finalize Daily at 00:15. Finalize computes the previous
   business day using the SQL Server time-zone expression in its job script
   (`SE Asia Standard Time`). All four job steps target
   `TKS_Thuc_Tap_V11_GiaiDoan2` and use the canonical worker/monitor procedures.

7. Keep the legacy `TKS Warehouse - Inventory Snapshot Rebuild` job disabled.
   It is retained for history/rollback context and must not race the current
   Snapshot Repair lifecycle worker.

8. Post-deployment, verify read-only: source-to-database object parity;
   Business DB consistency invariants; SQL Server Agent `Running` with its
   approved `Manual` startup type; fresh successful job history; fresh worker
   heartbeat; and a non-critical snapshot monitor result. A stopped Agent after
   a machine reboot is an operational dependency of this local-demo setup and
   must be started manually before background workers are expected to run.

The four job scripts above are the authoritative current deployment surface.
Do not edit `WarehouseModule.Schema.sql`, `WarehouseModule.Procedures.sql`,
`WarehouseModule.Security.sql`, or the job scripts to make a verification
result green. If source-to-`msdb` job definition parity differs, stop and report
the drift before changing the job.
