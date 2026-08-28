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
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.Menu.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.SampleData.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.SampleData.Repair.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.IntegrationTests.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.ValidationMessages.IntegrationTests.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.UnicodeDataTests.sql
```

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

Restart the Blazor application after the menu script runs so its function/permission cache reloads. The warehouse page route is `/Kho/Quan_Ly`.
