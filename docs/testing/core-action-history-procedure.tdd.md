# Core action-history procedure TDD evidence

## User journey

Selecting the eye icon for an entity opens its information dialog. The **Lịch Sử Xử Lý** tab must list only the action-history records whose `Ref_ID` matches that entity's `Auto_ID`.

## Test contract

`Database/Tests/CoreLogActionHistory.IntegrationTests.sql` creates two temporary log records in a transaction, executes the shared procedure for one `Ref_ID`, verifies the expected record is returned and the other is excluded, then rolls back all fixtures.

## RED

```powershell
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\CoreLogActionHistory.IntegrationTests.sql
```

Result before the implementation: SQL Server error `2812`, because `dbo.FCommon_Sys_sp_sel_List_Log_Record_Action_History` did not exist.

## GREEN

```powershell
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\CoreLogActionHistory.Procedures.sql
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\CoreLogActionHistory.IntegrationTests.sql
```

Expected result: `Core action-history integration tests passed.`

Coverage note: this is a database-only compatibility fix. The repository has no SQL coverage collector; the transactional integration test covers the procedure contract directly. The existing Blazor page and ADO.NET controller already call this exact procedure name, so no application-source change is required.
