# Warehouse master class decomposition - TDD evidence

## Source plan

Implemented the MASTER CLASS-1/2/3 scope from:

- `TK_COMPANY_STYLE_CLASS_REFACTOR_PLAN.md`
- `TK_COMPANY_STYLE_CLASS_MAP.csv`
- `TK_COMPANY_STYLE_METHOD_MAP.csv`
- `TK_COMPANY_STYLE_CALLER_MIGRATION_MAP.csv`
- `TK_COMPANY_STYLE_REFACTOR_RISK_MATRIX.md`

## User journeys

1. A Warehouse master page reads, looks up, inserts, updates, and deletes one selected entity through that entity's controller and its named Store.
2. Each master import keeps one connection and one transaction for its batch and sends every insert through the matching entity controller's transaction overload.
3. Page/support Stores whose company naming rule is unresolved retain their current names and behavior.

## RED evidence

Command:

`dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --verbosity quiet --filter FullyQualifiedName~WarehouseMasterControllerSplitContractTests -clp:ErrorsOnly`

Result: expected RED, 11 failed and 0 passed. The test assembly compiled; failures showed that the five entity controllers were absent and generic Store routers remained.

RED checkpoint: `0d304ad test: add warehouse master split contract checks`.

## GREEN evidence

Build:

`dotnet build TKS_Thuc_Tap_V11_Web_Danh_Muc/TKS_Thuc_Tap_V11_Web_Danh_Muc.csproj --no-restore --verbosity quiet -m:1 -p:BuildInParallel=false -clp:ErrorsOnly`

Result: succeeded, 0 errors and 12 warnings.

Group B contract command:

`dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --verbosity quiet -m:1 -p:BuildInParallel=false -p:BuildProjectReferences=false --filter FullyQualifiedName~WarehouseGroupBSplitContractTests -clp:ErrorsOnly`

Result: 22 passed.

Final focused contract/import command:

`dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --verbosity quiet -m:1 -p:BuildInParallel=false -p:BuildProjectReferences=false --filter "FullyQualifiedName~WarehouseMasterControllerSplitContractTests|FullyQualifiedName~WarehouseGroupBSplitContractTests|FullyQualifiedName~WarehouseGroupCDesharingContractTests|FullyQualifiedName~WarehouseUiWorkflowTests|FullyQualifiedName~ProductExcelImportUiTests|FullyQualifiedName~CategoryExcelImportValidationTests|FullyQualifiedName~CategoryExcelImportUiTests|FullyQualifiedName~CategoryXlsxReaderTests|FullyQualifiedName~ProductExcelImportValidationTests|FullyQualifiedName~ProductImportDatabaseSafetyTests|FullyQualifiedName~SupplierExcelImportValidationTests|FullyQualifiedName~SupplierExcelImportUiTests|FullyQualifiedName~SupplierXlsxReaderTests|FullyQualifiedName~UnitExcelImportValidationTests|FullyQualifiedName~UnitExcelImportUiTests|FullyQualifiedName~UnitXlsxReaderTests|FullyQualifiedName~WarehouseExcelImportValidationTests|FullyQualifiedName~WarehouseExcelImportUiTests|FullyQualifiedName~WarehouseXlsxReaderTests|FullyQualifiedName~TransactionImportPreviewTests" -clp:ErrorsOnly`

Result: 185 passed, 0 failed, 0 skipped.

GREEN/refactor checkpoint: `3f0fc67 refactor: split warehouse master access by entity`.

## Test specification

| Guarantee | Test/evidence | Result |
|---|---|---|
| Five entity controllers expose only the expected Store-named methods; each insert has normal and transaction overloads | `WarehouseMasterControllerSplitContractTests` | PASS |
| The compatibility controller no longer declares generic master list/lookup/save/delete routers or `Build_Master_Save_Command` | `WarehouseMasterControllerSplitContractTests` | PASS |
| Existing Group B SQL contracts remain, and Add/Update selection is in the master caller | `WarehouseGroupBSplitContractTests` | PASS |
| CRUD/List/Lookup Store contracts and UI workflows remain aligned with the split controllers | `WarehouseGroupCDesharingContractTests`, `WarehouseUiWorkflowTests` | PASS |
| Product import owns one connection and transaction and calls the product insert transaction overload | `ProductExcelImportUiTests.Product_batch_shares_one_transaction_aware_connection_across_all_inserts` | PASS |
| Import validation, workbook readers, and transaction preview tests remain green | Targeted 185-test command above | PASS |

## Coverage and known gaps

The test project has no configured Coverlet/coverage collector, so numerical line/branch coverage, including an 80% threshold, was not measured. The run intentionally excluded database-writing runtime integration tests to preserve the no-database-change scope. Transaction behavior is therefore supported by source-contract tests and build evidence, not by a live/test database execution in this implementation run.

The build reported 12 warnings and 0 errors. Stored Procedures and database objects were not changed.

## Scope

The five DM controllers and Master callers/import paths were updated. Page support methods `List_Master_Page_Async` and `List_Lookup_Page_Async`, product paging helpers, and import orchestration remain on `CWarehouseMaster_Controller` temporarily because their support naming/orchestration owner is unresolved. Document, Permission, Report, and Inventory technical controllers remain outside this implementation.
