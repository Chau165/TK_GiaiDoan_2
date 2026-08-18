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

Restart the Blazor application after the menu script runs so its function/permission cache reloads. The warehouse page route is `/Kho/Quan_Ly`.
