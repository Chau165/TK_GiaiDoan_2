# Warehouse Permission SQL Contract Cleanup Phase 3

## Executive Summary

Status: **PASS_WITH_WARNING**

The permission delete flow is now separated from the generic Master delete contract. `CWarehousePermission_Controller.Delete_Kho_User_Async` calls `sp_DM_Kho_User_Delete`, and the `KhoUser` branch was removed from `sp_DM_Delete`. The source build and non-integration tests pass. Full integration execution remains environment-gated because `TKS_INTEGRATION_CONNECTION_STRING` is not configured for a disposable test database.

No live database deployment or commit was performed.

## 1. Dependency Audit

### Before migration

- The only repository C# caller passing the `KhoUser` entity to `sp_DM_Delete` was `CWarehousePermission_Controller.Delete_Kho_User_Async`.
- `CWarehouseMaster_Controller.Delete_Master_Async` still calls `sp_DM_Delete` for generic master types, but does not pass a literal `KhoUser`.
- No SQL script, migration, tool, or test caller was found that invokes `sp_DM_Delete` with `Entity = 'KhoUser'`.
- `Database/Tests/WarehouseModule.ValidationMessages.IntegrationTests.sql` calls `sp_DM_Delete` with `Entity = 'Unknown'` only.
- Razor components still contain `KhoUser` as a UI/editor discriminator, but the delete action delegates to `CWarehousePermission_Controller`, not to the generic Master delete contract.

### External dependency risk

Repository search cannot prove that an external compiled client or separately deployed SQL script does not call the old contract. Such a caller would now receive the generic invalid-entity error for `KhoUser`; deployment consumers should be checked before rollout.

## 2. New Procedure

Created in `Database/WarehouseModule.Procedures.sql`:

```sql
dbo.sp_DM_Kho_User_Delete
```

The procedure preserves the old branch's behavior:

- table: `dbo.tbl_DM_Kho_User`
- key column: `Auto_ID`
- condition: `WHERE Auto_ID=@Auto_ID`
- no new transaction or business rule was introduced
- existing actor parameters are retained in the signature so the controller call shape remains compatible; they are not used by the old delete branch either

The existing `sp_DM_Kho_User_List`, `sp_DM_Kho_User_Page`, and `sp_DM_Kho_User_Save` contracts were not changed.

## 3. C# Migration

`CWarehousePermission_Controller.Delete_Kho_User_Async` changed from:

```text
sp_DM_Delete("KhoUser", permissionId, ...)
```

to:

```text
sp_DM_Kho_User_Delete(permissionId, ...)
```

Return behavior, parameter values, exception propagation, and the Razor delete/reload flow remain unchanged.

## 4. SQL Cleanup

Removed the `ELSE IF @Entity=N'KhoUser'` branch from `sp_DM_Delete`.

The generic procedure now handles only the remaining Master Data entities (`DonViTinh`, `LoaiSanPham`, `SanPham`, `NCC`, and `Kho`) and throws for unsupported entities. Permission delete is represented only by `sp_DM_Kho_User_Delete`.

The security grant/preflight scripts were not changed. They did not contain an existing grant/approval for `sp_DM_Delete` or the generic Master CRUD procedures. The deployment role must therefore be verified for `sp_DM_Kho_User_Delete` if the runtime uses a least-privilege SQL principal.

## 5. Validation

- CodeGraph was synchronized after the edits and confirms the UI delete action reaches `CWarehousePermission_Controller.Delete_Kho_User_Async`, which reaches `Execute_Procedure` with the dedicated procedure name.
- Targeted contract test: **1 passed, 0 failed**.
- Solution build: **0 errors, 24 warnings**. Warnings are pre-existing licensing/analyzer warnings; no Phase 3 compile error was observed.
- Non-integration test suite: **106 passed, 0 failed, 0 skipped**.
- Full solution test suite: **172 passed, 45 failed, 0 skipped, 217 total**. The failures stop at the disposable-database guard with `TKS_INTEGRATION_CONNECTION_STRING must point to a disposable test database`; they do not reach the Phase 3 SQL contract.
- Direct source search confirms the only remaining `sp_DM_Delete` C# caller is the generic `CWarehouseMaster_Controller` path, while `CWarehousePermission_Controller` calls `sp_DM_Kho_User_Delete`.
- `git diff --check`: no whitespace errors.

## 6. Risk Assessment

### Low risk

- The old delete predicate was transferred verbatim.
- The generic `sp_DM_Delete` signature remains unchanged for Master Data callers.
- UI behavior and permission model types were not changed in this phase.

### Medium risk

- Any external caller that still sends `KhoUser` to `sp_DM_Delete` will break by design.
- The new procedure's database permission must be verified in the target deployment because no live database deployment was performed and the repository security scripts do not explicitly grant either delete procedure.
- A disposable integration database was unavailable, so runtime SQL execution and actual permission-delete behavior were not proven against SQL Server in this run.

## Scope Result

The requested Permission Delete contract separation is complete in source. Remaining rollout work is operational: verify external consumers, apply the SQL script to the intended database, grant/verify execution rights as appropriate, and run the permission delete integration test against a disposable database.
