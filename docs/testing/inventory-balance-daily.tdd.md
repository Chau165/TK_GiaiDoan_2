# Inventory Balance Daily TDD evidence

## Source and journeys

Journeys were derived from the InventoryReportPaged optimization request.

1. A warehouse user posts receipt and issue documents; the worker materializes both daily movement and balance rows.
2. A back-dated document rebuilds only its warehouse/product suffix and preserves earlier dates.
3. Competing workers cannot overwrite one another for a scope.
4. A user cannot query a warehouse outside their assigned scope.
5. The paged historical report keeps its result-set/paging contract but no longer calculates movement ranges during a request.

## RED evidence

| Target | Command | Result |
|---|---|---|
| Balance read model integration | `sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseInventoryBalanceDaily.IntegrationTests.sql` | Failed as intended with `52500: Inventory_Balance_Daily has not been deployed.` |
| Same-scope worker lock | `TKS_INTEGRATION_CONNECTION_STRING=...Perf_10000000; dotnet test ... --filter FullyQualifiedName~Balance_daily_rebuild_is_rejected_while_another_worker_owns_the_scope --no-restore` | Failed as intended: expected 51224, actual 2812 because the new worker procedure was absent. |

No production code was changed before the SQL RED test was executed.

## GREEN evidence

Schema and procedures were deployed only to the isolated `TKS_Thuc_Tap_V11_Perf_10000000` database.  The production/business database was not bootstrapped and no performance benchmark was run.

| # | Guarantee | Test | Type | Result |
|---:|---|---|---|---|
| 1 | Posted receipt creates correct movement and balance daily values | `Database/Tests/WarehouseInventoryBalanceDaily.IntegrationTests.sql` | SQL integration | PASS |
| 2 | Posted issue reduces closing quantity and advances cumulative issue | `Database/Tests/WarehouseInventoryBalanceDaily.IntegrationTests.sql` | SQL integration | PASS |
| 3 | Back-date rebuild preserves pre-affected date and recalculates later dates in one scope | `Database/Tests/WarehouseInventoryBalanceDaily.IntegrationTests.sql` | SQL integration | PASS |
| 4 | A second worker is rejected while the existing scope applock is held | `WarehouseInventoryMovementReliabilityIntegrationTests.Balance_daily_rebuild_is_rejected_while_another_worker_owns_the_scope` | SQL Server integration | PASS |
| 5 | Explicit unauthorized warehouse report request is rejected with 51054 | `Database/Tests/WarehouseInventoryBalanceDaily.IntegrationTests.sql` | SQL integration | PASS |
| 6 | Paged report definition reads Balance Daily and contains no daily movement scan, `#MovementAggregate`, `SUM`, or `GROUP BY` | `Database/Tests/WarehouseInventoryBalanceDaily.IntegrationTests.sql` plus deployed-definition query | SQL integration | PASS |
| 7 | Existing movement lifecycle/report regression remains valid | `Database/Tests/WarehouseInventoryMovementAggregate.IntegrationTests.sql` | SQL integration | PASS |

Commands executed after deployment:

```text
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_Perf_10000000 -b -f 65001 -i Database\Tests\WarehouseInventoryBalanceDaily.IntegrationTests.sql
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_Perf_10000000 -b -f 65001 -i Database\Tests\WarehouseInventoryMovementAggregate.IntegrationTests.sql
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --filter FullyQualifiedName~Balance_daily_rebuild_is_rejected_while_another_worker_owns_the_scope --no-restore --no-build
dotnet build TKS_Thuc_Tap_V11.sln --no-restore
```

The focused C# test used `TKS_INTEGRATION_CONNECTION_STRING` to point at the isolated benchmark database.  The solution build passed with 0 errors and 27 pre-existing/non-blocking warnings, including Telerik licensing warnings and Blazor async warnings.

## Coverage and gaps

The SQL integration scripts do not have a repository coverage collector, so an 80% line-coverage percentage is not available.  The passing tests cover all six requested acceptance behaviors.  The controlled full bootstrap and a post-change concurrency benchmark are intentionally deferred; they operate on the full data set and require a maintenance window/next user instruction.

## Git checkpoint note

No TDD checkpoint commits were created because the shared worktree already contained unrelated uncommitted user changes.  Staging or committing them would risk claiming ownership of unrelated work.  The RED/GREEN evidence is recorded here instead.
