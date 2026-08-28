# Warehouse procedure cleanup - TDD evidence

## Source plan

Không có file `*.plan.md`. User journey và acceptance criteria được suy ra trong
lần chạy TDD này.

## User journey

As a database maintainer, I want one canonical definition for each warehouse
procedure and no executable legacy posting bundle, so that deployment order
cannot silently restore an older business contract.

## Task report

### RED

Added `WarehouseProcedureDeploymentTests` before changing SQL.

```text
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter FullyQualifiedName~WarehouseProcedureDeploymentTests --verbosity minimal
```

Result: `Failed: 2, Passed: 0, Total: 2`.

- The module test found 34 duplicated procedure names.
- The secondary posting script still contained 18 procedure definitions.

RED checkpoint: `8794caf` (`test: add stored procedure duplicate guard RED`).

### GREEN

- Removed duplicate definitions from `Database/WarehouseModule.Procedures.sql`.
- Preserved the Posted-only `sp_XNK_Validate_All_Balances` definition and the
  login-validating `sp_DM_Kho_User_Save` definition.
- Converted `Database/WarehouseDocumentPosting.Procedures.sql` to a deprecated,
  comment-only placeholder.
- Documented the canonical deployment rule in `Database/README.md`.

Validation commands and results:

```text
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter FullyQualifiedName~WarehouseProcedureDeploymentTests --verbosity minimal
Passed: 2, Failed: 0

dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter "FullyQualifiedName~WarehouseProcedureDeploymentTests|FullyQualifiedName~WarehouseAuthorizationContractTests|FullyQualifiedName~WarehouseDetailDisplayFormatTests" --verbosity minimal
Passed: 20, Failed: 0

dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter FullyQualifiedName~WarehouseInventoryMovementReliabilityIntegrationTests --verbosity minimal
Passed: 6, Failed: 0

sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\WarehouseModule.Procedures.sql
exit code 0

sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseInventoryMovementAggregate.IntegrationTests.sql
PASS: movement aggregate post, issue, back-date lifecycle, report and rollback.

sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseInventorySnapshotLifecycle.IntegrationTests.sql
PASS: snapshot lifecycle invalidation, scoped queueing, rebuild, report fallback and rollback.

sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.ReportContract.IntegrationTests.sql
PASS: Warehouse report result contracts match report entities
```

`SET PARSEONLY ON` passed for the complete `WarehouseModule.Procedures.sql`.
The final source inventory contains 51 procedure definitions, 11 trigger
definitions and one function definition; no object name is repeated in the
canonical module bundle. The posting placeholder contains no DDL.

## Test specification

| # | What is guaranteed | Test or command | Type | Result |
|---|---|---|---|---|
| 1 | Each warehouse procedure appears once in the canonical module script. | `WarehouseProcedureDeploymentTests.Warehouse_module_script_defines_each_procedure_once` | unit/source contract | PASS |
| 2 | The deprecated posting script cannot override any canonical procedure. | `WarehouseProcedureDeploymentTests.Legacy_document_posting_script_defines_no_procedures` | unit/source contract | PASS |
| 3 | The deployed movement-aware post procedure remains active after module deployment. | `WarehouseInventoryMovementReliabilityIntegrationTests.Deployment_post_script_does_not_override_the_canonical_movement_aware_post_procedure` | integration | PASS |
| 4 | Movement aggregate posting, rebuild, back-date invalidation and rollback remain correct. | `Database\Tests\WarehouseInventoryMovementAggregate.IntegrationTests.sql` | integration | PASS |
| 5 | Snapshot lifecycle invalidation, scoped queueing and fallback remain correct. | `Database\Tests\WarehouseInventorySnapshotLifecycle.IntegrationTests.sql` | integration | PASS |
| 6 | Report result columns remain compatible with the application entities. | `Database\Tests\WarehouseModule.ReportContract.IntegrationTests.sql` | integration | PASS |

## Coverage and known gaps

No numeric 80% coverage result was produced. This repository has no configured
coverage collector. The exact attempted command was:

```text
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter FullyQualifiedName~WarehouseProcedureDeploymentTests --collect:"XPlat Code Coverage" --verbosity minimal
```

It reported that the `XPlat Code Coverage` data collector was unavailable. SQL
behavior was verified through parse-only, deployment and rollback-isolated
integration tests instead.

`Database\Tests\WarehouseModule.ValidationMessages.IntegrationTests.sql` still
fails at its legacy header-save call because it omits the required
`@Ma_Dang_Nhap` parameter. The fixture is outside this cleanup and was not
changed to weaken the current warehouse authorization contract.

## Merge evidence

The RED evidence is preserved in checkpoint `8794caf`. The GREEN cleanup commit
must be kept reachable from the active branch with this report; do not merge the
legacy posting script as an executable deployment bundle.
