# Warehouse module TDD evidence

Source plan: user journeys were derived from `Bai Tap Thuc Tap.docx` (Bài 1-17).

| # | Guarantee | Test target | Type | RED | GREEN |
|---|---|---|---|---|---|
| 1 | An empty unit name is rejected | `WarehouseModule.IntegrationTests.sql` | SQL integration | `sp_DM_Don_Vi_Tinh_Save` was missing | Passes with SQL error `51001` expected |
| 2 | A duplicate unit name is rejected | `WarehouseModule.IntegrationTests.sql` | SQL integration | N/A until procedure existed | Passes with SQL error `51002` expected |
| 3 | Unit validation messages preserve Vietnamese Unicode | `WarehouseModule.IntegrationTests.sql` | SQL integration | Failed with mojibake from the deployed procedure | Passes with exact messages for empty and duplicate names |
| 4 | Receipt/issue reporting uses opening-before-from-date and inclusive in-period dates | `WarehouseModule.IntegrationTests.sql` | SQL integration | Formula assertion failed while test data reused one receipt header | Passes: `10 + 5 - 4 = 11` |
| 5 | An issue that makes a historical warehouse-product balance negative is rejected | `WarehouseModule.IntegrationTests.sql` | SQL integration | N/A until procedure existed | Passes with SQL error `51120` expected |
| 6 | All Warehouse validation messages remain Unicode-safe across master, receipt, issue and report flows | `WarehouseModule.ValidationMessages.IntegrationTests.sql` | SQL integration | Detected mojibake in deployed procedure text | Passes with exact Vietnamese messages |
| 7 | Detail receipt/issue reports reject reversed date ranges instead of silently returning an empty table | `WarehouseModule.ValidationMessages.IntegrationTests.sql` | SQL integration | Procedure returned no error | Passes with SQL error `51200` and exact message |
| 8 | Warehouse editors are shown only after an explicit add/edit action, and a selected document exposes an add-product action | `WarehouseUiWorkflowTests` | C# source/UI regression | Editing-state controls were absent; product-entry form was always visible | Passes with explicit master/document/detail editing guards and `Thêm sản phẩm` action |

Commands actually run:

```powershell
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.IntegrationTests.sql
# RED: Could not find stored procedure 'dbo.sp_DM_Don_Vi_Tinh_Save'.

sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.Schema.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.Procedures.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.IntegrationTests.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.ValidationMessages.IntegrationTests.sql
# GREEN: PASS: Warehouse module integration tests

dotnet build TKS_Thuc_Tap_V11_Data_Access\TKS_Thuc_Tap_V11_Data_Access.csproj --no-restore --verbosity:minimal
# GREEN: 0 warnings, 0 errors

dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --verbosity:minimal
# GREEN: 8 passed, 0 failed

dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter FullyQualifiedName~WarehouseUiWorkflowTests --verbosity:minimal
# RED (before the Razor change): Assert.Contains failure for `private bool m_bMasterEditing;`
# GREEN: 1 passed, 0 failed

dotnet build TKS_Thuc_Tap_V11_Web_Danh_Muc\TKS_Thuc_Tap_V11_Web_Danh_Muc.csproj --no-restore --verbosity:minimal
# GREEN: 0 errors; Telerik license warnings only
```

Coverage note: this repository had no test project or existing UI test harness. The covered database integration path is transactional and rolls back all fixture data; browser E2E and numerical coverage are not available until the source sample's unavailable Telerik Reporting packages are restored.

Browser smoke verification on `/Kho/Quan_Ly` also confirmed the exact Vietnamese messages for empty/duplicate receipt and issue numbers, invalid receipt/issue products, and reversed date ranges in both detail reports. A valid issue report continued to load rows after the date guard was added.

The UI regression test is intentionally source-level because this repository has no bUnit/Playwright harness. The web project build verifies the Razor markup and event handlers compile; a restarted app process is required before an already-running `--no-build` process can serve the new UI.
