# Warehouse validation scope — TDD evidence

## Scope

Post validation must inspect only the `(Kho_ID, San_Pham_ID)` pairs present in
the document being posted. It uses the document date as the lower bound, while
retaining the aggregate before that date as the opening balance. The existing
`sp_XNK_Validate_All_Balances` remains available for full reconciliation.

## RED

Command:

```text
sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseDocumentPosting.ValidationScope.IntegrationTests.sql
```

Result before the fix: failed with SQL error `51120` from
`sp_XNK_Validate_All_Balances` because an unrelated warehouse/product bucket
was negative.

## GREEN

The same SQL test passed after `sp_XNK_Document_Post` was changed to call
`sp_XNK_Validate_Affected_Balances`.

Guarantees:

- An unrelated negative bucket does not block a valid Post.
- The affected `(Kho_ID, San_Pham_ID)` balance is updated.
- The test rolls back all fixture data.

Additional verification:

```text
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --filter FullyQualifiedName~WarehouseCrudContractIntegrationTests --no-restore
```

Result: 4 passed, 0 failed.

## Known gap

The current production database is small, so no representative 4M-row
before/after timing was recorded in this run. The large synthetic benchmark
reported the original full-history validation bottleneck; a production-sized
comparison should be run after the benchmark database is deployed with the
same `Is_Posted` schema and posting procedures.
