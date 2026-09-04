# Warehouse report immediate warehouse filter — TDD evidence

## Source plan

No `*.plan.md` was supplied. The journey was derived from the user's report workflow.

## User journey

As an authorized warehouse user, I want the current-inventory and period inventory reports to refresh immediately after selecting a warehouse, so that the grid no longer shows rows from the previously loaded warehouse until a page reload.

## Root cause and fix

The report warehouse selector only bound `m_iReport_Warehouse_ID`; the server-read Telerik grid was not rebound after the value changed. The selector now uses Blazor `@bind:after="Load_Report_Async"`, which updates the selected ID first and then rebinds the active report grid. The existing controller and stored procedures already forward and authorize `@Kho_ID`.

## RED/GREEN evidence

| Stage | Command/result |
|---|---|
| RED | `dotnet test ... --filter FullyQualifiedName~Warehouse_report_warehouse_selection_rebinds_the_active_grid_immediately`: 1 failed because the selector had no `@bind:after` refresh hook. |
| GREEN | Same focused test after the fix: 1 passed. |

## Verification

- `dotnet build TKS_Thuc_Tap_V11_Web_Danh_Muc/TKS_Thuc_Tap_V11_Web_Danh_Muc.csproj --no-restore --verbosity minimal`: 0 errors; existing Telerik/license and analyzer warnings remain.
- Read-only SQL against `TKS_Thuc_Tap_V11_GiaiDoan2` confirmed both report procedures have `@Kho_ID BIGINT` and server-side warehouse filtering; user `thuctap_kho` with warehouse 474 returned only `Kho Bình Dương` rows in both report paths.
- Full `WarehouseUiWorkflowTests` run: 18 passed and 2 pre-existing source-contract assertions failed because they expect older SQL/call formatting; the new regression test passed.
- Numeric coverage was not collected because the test project has no installed XPlat Code Coverage collector.
