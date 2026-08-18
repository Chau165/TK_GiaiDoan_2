# Warehouse module structure refactor - TDD evidence

## Source and user journey

Derived from the user request to split the warehouse DTO and controller source files without changing the existing ADO.NET, SQL Server, or Blazor behavior.

As a developer, I want warehouse data access split by master data, documents, and reports so that I can locate and change a concern without editing one large controller file.

## RED evidence

`dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --verbosity:minimal`

Failed as intended because `CWarehouseMaster_Controller`, `CWarehouseDocument_Controller`, and `CWarehouseReport_Controller` did not yet exist.

## GREEN evidence

`dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --verbosity:minimal`

Passed: 2 / 2 tests.

| Guarantee | Test | Type | Result |
|---|---|---|---|
| Focused master, document, and report controllers are publicly available | `Warehouse_module_exposes_separate_controllers_by_responsibility` | structure/unit | PASS |
| Detail amount remains quantity times unit price | `Document_detail_keeps_total_value_calculation` | unit | PASS |

## Coverage and known gaps

There was no existing C# test project. The focused tests cover the new public structure and the pure DTO calculation. Database behavior remains covered by the existing SQL integration script `Database\Tests\WarehouseModule.IntegrationTests.sql`; controller methods require a configured SQL Server and are not unit-tested with a mock connection in this refactor.

The solution folder is not a Git repository, so no checkpoint commits could be created.

## Regression fixes

The database-backed regression test `Master_list_reads_bigint_ids_and_int_zero_placeholders` first failed with the reported `System.Int32` to `System.Int64` exception. After the mapper fix it passed together with the two existing tests: 3 / 3 tests.

The existing sample rows were repaired by `Database/WarehouseModule.SampleData.Repair.sql`. SQL Server verification passed for `Kho trung tâm`, `Bút bi Thiên Long`, and `Tồn đầu kỳ mẫu`. The sample seed remains idempotent when run with UTF-8 input (`sqlcmd -f 65001`).
