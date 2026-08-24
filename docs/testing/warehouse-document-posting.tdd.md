# Warehouse Draft/Post TDD evidence

## User journeys

- As a warehouse user, I save a receipt/issue header first and then its details as a draft, without changing stock.
- As a warehouse user, I Post a complete draft so the movement ledger and current balance change atomically.
- As a warehouse user, I cannot Post an issue that would make stock negative; the document remains a draft and balance remains unchanged.

## RED

`dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --filter FullyQualifiedName~Post_makes_a_draft_document --no-restore`

Result: compile failed with `CS1061`: `CWarehouseDocument_Controller` did not contain `Post_Document_Async`.

## GREEN

`dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --filter FullyQualifiedName~WarehouseCrudContractIntegrationTests --no-restore`

Result: 4 passed, 0 failed.

| Guarantee | Test |
|---|---|
| Draft receipt has no balance row before Post | `Post_makes_a_draft_document_a_movement_and_rolls_back_an_insufficient_issue` |
| Posted receipt creates the expected balance | same |
| Insufficient issue fails at Post and keeps document unposted/balance unchanged | same |
| Draft deletion inside a caller transaction does not commit that caller transaction | `Delete_draft_does_not_commit_a_caller_owned_transaction` |

## Known coverage gap

No browser E2E test was run. The component project was built successfully; manual authenticated UI smoke testing remains necessary.
