# Warehouse report warehouse filter — TDD evidence

## Source plan

No `*.plan.md` was supplied. User journeys were derived from the requested report behavior and the existing warehouse authorization flow.

## User journeys

- As an authorized user, I want to choose one warehouse in the report filter, so that detail receipt/issue and inventory reports show only that warehouse.
- As an authorized user, I want an “all warehouses” option, so that the existing authorized-scope report remains available.
- As a user, I must not be able to view an unassigned warehouse by tampering with the selected ID.

## RED/GREEN evidence

| Stage | Command/result |
|---|---|
| RED — UI/SQL contract | `dotnet test ... --filter FullyQualifiedName~Warehouse_reports_filter_by_a_user_authorized_warehouse` failed because the `Kho` selector was absent. |
| RED — deployed runtime | New integration test failed with `sp_BC_Xuat_Nhap_Ton_Page has too many arguments specified`, proving the live procedure had not received `@Kho_ID`. |
| GREEN — selected scope | Same integration test passed after deploying `Database/WarehouseModule.Procedures.sql`: selected warehouse returned only its rows and an unassigned warehouse raised SQL error `51054`. |
| GREEN — focused regressions | 5 passed: UI/SQL contract, selected report scope, paging scope/index checks, and existing authorization checks. |

## Test specification

| # | Guarantee | Test | Type | Result |
|---|---|---|---|---|
| 1 | Report UI exposes `Tất cả kho` plus only the authorized warehouse list and forwards the nullable ID to both paged report paths. | `WarehouseUiWorkflowTests.Warehouse_reports_filter_by_a_user_authorized_warehouse` | source contract | PASS |
| 2 | Selecting one of two assigned warehouses restricts inventory and detail reports to that warehouse. | `WarehouseReportPagingOptimizationTests.Selecting_a_warehouse_restricts_reports_and_rejects_unassigned_warehouse` | SQL integration | PASS |
| 3 | A selected warehouse that is not assigned to the login is rejected with error `51054`. | Same integration test | SQL authorization | PASS |
| 4 | Paged procedures retain authorized-scope materialization and covering-index paging behavior. | `Effective_report_procedures_use_narrow_paging_scopes_and_reuse_authorization`; `Detail_report_procedures_page_from_covering_indexes_without_full_scope_materialization` | SQL integration | PASS |
| 5 | Existing warehouse authorization behavior remains valid. | `WarehouseAuthorizationIntegrationTests` | SQL integration | PASS |

## Coverage and known gaps

- `dotnet build TKS_Thuc_Tap_V11.sln --no-restore --verbosity minimal`: succeeded with 0 errors.
- Focused regression command: 5/5 passed.
- Coverage collection was attempted with `--collect:"XPlat Code Coverage"`, but this test project has no installed XPlat coverage data collector; therefore no numeric 80% coverage result is claimed.
- A broader pre-existing subset still has three failures unrelated to this change: one brittle source assertion expects SQL text without current whitespace, and two legacy inventory tests seed raw ledger rows without the current `Inventory_Movement_Daily` aggregate. They were not changed in this task.

## Deployment evidence

- Deployed to `TKS_Thuc_Tap_V11_GiaiDoan2` with `sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\\WarehouseModule.Procedures.sql`.
- `sys.parameters` and `OBJECT_DEFINITION` confirmed all six report procedures contain `@Kho_ID` and the authorization guard.
- CodeGraph was synced after source edits: 16 changed files processed; no pending changes remained.
