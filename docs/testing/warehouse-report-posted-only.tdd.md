# Warehouse report Posted-only TDD evidence

## Source plan

No plan file was provided. The journey was derived from the reported behavior:

> As a warehouse user, I want reports to include only Posted receipts and issues, so Draft documents do not affect operational reporting or stock totals.

## Test journey and evidence

The regression test creates one Draft and one Posted receipt plus one Draft and one Posted issue in a user-authorized warehouse. It verifies both ordinary and paginated report procedures.

| Guarantee | Test | RED evidence | GREEN evidence |
|---|---|---|---|
| Detail receipt report excludes Draft lines | `WarehouseReportingPostedOnlyIntegrationTests.Reports_include_posted_documents_but_exclude_drafts` | Failed with 2 rows: Draft and Posted | Passed |
| Detail issue report excludes Draft lines | Same integration test | Covered after the first RED assertion | Passed |
| Inventory report counts only Posted receipt and issue quantities | Same integration test | Failed with Draft quantity included (`15` instead of `10`) | Passed with received `10`, issued `3`, closing `7` |
| Paginated receipt, issue, and inventory reports use the same Posted-only rule | Same integration test | Covered by the failing report contract | Passed; each page total excludes Draft |
| Document list returns the persisted `Is_Posted` value for both receipt and issue rows | `WarehouseReportingPostedOnlyIntegrationTests.Document_page_returns_the_persisted_post_status` | Failed because `Is_Posted` was missing from the result set | Passed |

## Validation

- RED: `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --filter FullyQualifiedName~WarehouseReportingPostedOnlyIntegrationTests --no-restore` failed because Draft rows were returned.
- GREEN: the same command passed with `1` test passed.
- Full regression: `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore` passed with `55` tests passed.
- Runtime smoke check: existing `PNK-001` has `Is_Posted = 0`; both paginated reports returned `Total_Count = 0`.

## Coverage note

The repository does not configure a coverage collector, so no numerical coverage percentage is claimed. The database-backed regression test directly exercises every changed report procedure contract and rolls back its fixture transaction.
