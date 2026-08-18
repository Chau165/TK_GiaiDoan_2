# Warehouse architecture refactor TDD evidence

## Source plan

Derived from the Warehouse checklist and the requested company-source alignment. No external plan file was supplied.

## User journeys

1. As an authenticated user with View permission, I can open `/Kho/Quan_Ly` and let `FBase` resolve function 2010 from the URL.
2. As a user with Add/Edit/Delete/Export permissions, I can operate Warehouse master data, receipt/issue headers, receipt/issue details, reports, and print/export actions through the existing permission framework.
3. As an auditor, I can trace Warehouse writes to the active user and function and see CRUD action history through the existing common helper.
4. As a developer, I can change Warehouse data access through stored procedures and `CSqlHelper`, with database transactions preserving inventory integrity.

## RED/GREEN evidence

- RED: `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --verbosity:minimal` failed the new Warehouse architecture assertions because the page had no `FWarehouse_1_*` component, controllers contained inline `SELECT`, and the schema had no audit columns.
- GREEN: `dotnet build TKS_Thuc_Tap_V11_Web_Danh_Muc\TKS_Thuc_Tap_V11_Web_Danh_Muc.csproj --no-restore --verbosity:minimal` succeeded with 0 errors.
- GREEN: `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --verbosity:minimal` passed 11 tests.
- GREEN: `sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.IntegrationTests.sql` passed.
- GREEN: `sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.ValidationMessages.IntegrationTests.sql` passed.
- GREEN: `sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.UnicodeDataTests.sql` passed.
- GREEN: targeted scan of `TKS_Thuc_Tap_V11_Data_Access\Controller\Warehouse` found no `SELECT`, `CommandType.Text`, `ReadAsync`, or `ReadProcedureAsync`.
- GREEN: a transactional audit probe confirmed `Created_By`, `Created_By_Function`, `Last_Updated_By`, and `Last_Updated_By_Function` receive `audit_user` and `2010`; the probe rolled back.

## Test specification

| # | Guarantee | Evidence | Result |
|---|---|---|---|
| 1 | Warehouse page delegates to `FWarehouse_1_Warehouse_List : FBase`, with Info/Edit children and dynamic permissions | `WarehouseUiWorkflowTests.Warehouse_page_uses_the_company_list_info_edit_component_pattern` | PASS |
| 2 | Existing editor workflow remains guarded and reloads the saved document/detail scope | `WarehouseUiWorkflowTests.Warehouse_page_only_renders_editors_when_the_user_starts_an_editing_action` | PASS |
| 3 | Warehouse controllers call `CSqlHelper` through stored procedures and carry audit values | `WarehouseUiWorkflowTests.Warehouse_data_access_uses_stored_procedures_and_audit_arguments` | PASS |
| 4 | Schema/procedure contract contains the four audit identity fields | `WarehouseUiWorkflowTests.Warehouse_database_contract_contains_full_audit_columns` | PASS |
| 5 | Inventory validation, receipt/issue flows, reports, and Unicode messages remain valid | Three `Database\Tests\WarehouseModule.*.sql` scripts | PASS |

## Coverage and known gaps

No coverage collector is configured for the .NET solution, so an 80% numerical coverage result was not claimed. The UI test remains source-level; authenticated browser E2E permission tests were not available in this run. A manual authenticated smoke test should still verify each permission combination and the Telerik export menu.

## Refactor checkpoint

- RED checkpoint commit: `c69386c test: add Warehouse architecture contracts`.
- GREEN implementation checkpoint is recorded in the final refactor commit; no unrelated files were changed.
