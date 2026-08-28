# TDD evidence: receipt/issue warehouse filter

Date: 2026-08-28

## Requirement

The Receipt and Issue tabs must let a signed-in user choose one warehouse, while keeping `Tất cả kho` as the default. The list must remain restricted to warehouses assigned to that user.

## Execution path verified

`FWarehouse_1_Warehouse_List.razor` renders the authorized warehouse lookup and binds the selection to `Read_Document_Async`. That handler passes the nullable warehouse ID through `CWarehouseDocument_Controller` to `sp_XNK_Document_Page`. The procedure applies the selected warehouse predicate and calls `sp_DM_Kho_User_Ensure_Access` before reading data.

## RED

Tests were added before the production change:

- `WarehouseUiWorkflowTests.Warehouse_document_grids_filter_by_a_user_authorized_warehouse`
- `WarehouseDocumentWarehouseFilterIntegrationTests.Document_pages_filter_receipts_and_issues_by_selected_authorized_warehouse`

The focused run failed because `m_iDocument_Warehouse_ID` was absent and the live `sp_XNK_Document_Page` rejected the new `@Kho_ID` argument as “too many arguments specified”.

## GREEN

The implementation adds:

- one authorized-warehouse dropdown shared by Receipt and Issue tabs;
- a `Tất cả kho` option represented by `NULL`;
- nullable `Kho_ID` forwarding in document controllers/facade;
- server-side filtering and permission validation in both document list procedures.

Focused verification:

```text
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter "FullyQualifiedName~Warehouse_document_grids_filter_by_a_user_authorized_warehouse|FullyQualifiedName~Document_pages_filter_receipts_and_issues_by_selected_authorized_warehouse|FullyQualifiedName~Selecting_a_warehouse_restricts_reports_and_rejects_unassigned_warehouse"
Passed: 3, Failed: 0
```

The integration journey verified two assigned warehouses, both receipt and issue pages, the all-warehouses default, and SQL error `51054` for an unassigned warehouse. Test data was transaction-scoped and rolled back.

## Build and deployment evidence

- `dotnet build TKS_Thuc_Tap_V11_Web_Danh_Muc\TKS_Thuc_Tap_V11_Web_Danh_Muc.csproj --no-restore`: succeeded, 0 errors.
- `dotnet build TKS_Thuc_Tap_V11.sln --no-restore`: succeeded, 0 errors after stopping the verified `TKS_Thuc_Tap_V11_Web.exe` process that held the output DLLs.
- `sqlcmd ... -d TKS_Thuc_Tap_V11_GiaiDoan2 ... -i Database\WarehouseModule.Procedures.sql`: succeeded.
- Live metadata check confirmed `@Kho_ID` on `sp_XNK_Document_List` and `sp_XNK_Document_Page`, plus the deployed filter/authorization guard.
- `codegraph status`: index up-to-date.

## Coverage and residual risks

The requested `XPlat Code Coverage` collector is not installed in this environment, so no numeric coverage percentage is claimed. Browser E2E was not run; Razor compilation plus source contract and SQL integration tests cover the changed path. Telerik licensing and existing legacy analyzer warnings remain. A broader legacy CRUD run also had three cleanup failures caused by direct deletion of posted documents in existing test teardown; those failures are outside this feature and did not occur in the focused suite.
