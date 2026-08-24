# Warehouse print template — TDD evidence

## Source

User journey derived during this change: a warehouse user selects a receipt or issue document and prints only a formal A4 document, rather than the whole management screen.

## Guarantees

| # | What is guaranteed | Test | Result |
|---|---|---|---|
| 1 | The selected document renders a distinct receipt/issue template with document data, item rows, totals, and signature areas. | `WarehouseUiWorkflowTests.Warehouse_document_print_uses_a_dedicated_receipt_template` | PASS |
| 2 | Print CSS hides the page UI and exposes only `.warehouse-print-document` on A4 portrait media. | `WarehouseUiWorkflowTests.Warehouse_document_print_uses_a_dedicated_receipt_template` | PASS |

## Evidence

- RED: `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --filter FullyQualifiedName~Warehouse_document_print_uses_a_dedicated_receipt_template --no-restore` failed because `warehouse-print-document` did not exist.
- GREEN: the same command passed with 1 passed, 0 failed.
- Build: `dotnet build TKS_Thuc_Tap_V11_Web/TKS_Thuc_Tap_V11_Web.csproj --no-restore -v:q` completed with 0 errors. The existing Telerik license warnings remain.

## Known gap

The application was not running on port 15073 and no authenticated browser session was available, so print-preview visual confirmation remains a manual smoke test.
