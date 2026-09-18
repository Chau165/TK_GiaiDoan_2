# Warehouse Architecture Cleanup Phase 2 Report

Review date: 2026-09-11  
Review basis: working tree at HEAD 7db2192, including the Phase 1 Warehouse refactor files already present. Pre-existing benchmark/performance changes were not attributed to this phase.  
Scope: authorization lookup boundary, CWarehouseMaster.Login_Name, legacy KhoUser SQL branches, source dependency tracing, build, and tests. No commit was created and no live database was changed.

## Executive Summary

**CLEANUP_STATUS: PASS_WITH_WARNING**

The safe part of Phase 2 is complete:

- Authorization and user lookup methods were moved from CWarehouseMaster_Controller to the existing CWarehousePermission_Controller.
- Warehouse UI and the authorization integration test now call the Permission Controller.
- Login_Name was removed from CWarehouseMaster; permission rows continue to use CWarehousePermission.Login_Name.
- The KhoUser branches were removed from sp_DM_Master_List and sp_DM_Master_Page.
- Stored Procedure names, parameters, result mapping, and business authorization behavior were preserved for the moved lookups.
- No benchmark, inventory, or warehouse-document business logic was changed.

One cleanup remains intentionally incomplete:

- sp_DM_Delete still has its @Entity = N'KhoUser' branch because CWarehousePermission_Controller.Delete_Kho_User_Async still calls that generic procedure. Removing it now would break the active permission delete flow. Adding a new permission-specific delete procedure would be a separate SQL contract change and was not performed under the stated constraints.

The result is therefore PASS_WITH_WARNING, not unconditional PASS.

## 1. Authorization Move

### Change

The two lookup methods now live in:

- TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehousePermission_Controller.cs:10
  - List_Authorized_Warehouses_Async(string p_strCurrent_Login)
  - calls sp_DM_Kho_User_List_Allowed with the same login parameter.
- TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehousePermission_Controller.cs:15
  - List_User_Lookup_Async()
  - calls sp_DM_Kho_User_User_List with no changed parameters.

They were removed from CWarehouseMaster_Controller. That controller now retains master list/page/lookup and master CRUD operations only.

### Caller migration

The Warehouse page now uses the Permission Controller:

- FWarehouse_1_Warehouse_List.razor:315 loads the current user's allowed warehouses.
- FWarehouse_1_Warehouse_List.razor:316 loads users for the permission editor.
- FWarehouse_1_Warehouse_List.razor:317 continues to load user-warehouse mappings through the Permission Controller.

WarehouseAuthorizationIntegrationTests.cs:53 was migrated to the Permission Controller without changing the authorization assertion.

### Business flow validation

- Document flow still receives the same allowed-warehouse list and the same current login.
- CWarehouseDocument_Controller continues to pass the current login into document procedures; no document procedure or parameter was changed.
- Report flow remains in CWarehouseReport_Controller; its allowed-warehouse name lookup still calls sp_DM_Kho_User_List_Allowed directly from the report controller. It does not route through CWarehouseMaster_Controller.
- The lookup Stored Procedures were not changed.

## 2. CWarehouseMaster Cleanup

### Removed

CWarehouseMaster.Login_Name was removed from:

TKS_Thuc_Tap_V11_Data_Access/Entity/Warehouse/CWarehouseMasterModels.cs:5

The model now contains master identity, master relationships, audit fields, and notes only:

- Auto_ID
- Code
- Name
- Related_ID
- Related_ID_2
- create/update audit fields
- Ghi_Chu

The ordinary master clone no longer copies Login_Name (FWarehouse_1_Warehouse_List.razor:501). The ordinary master information view now displays Name directly (FWarehouse_2_Warehouse_Master_Info.razor).

### Permission model boundary

CWarehousePermission.Login_Name remains intentionally present. It is a real field of the User-Warehouse mapping and is used by the permission grid/editor.

### Compatibility observation

The benchmark synthetic table still contains a Login_Name column as an input column for reflection-mapping coverage. Benchmark files were not changed, and no benchmark code accesses CWarehouseMaster.Login_Name. The build and non-integration tests confirm that the extra synthetic column does not require the removed model property.

The generic master SQL branches for ordinary master types still emit a legacy Login_Name placeholder column. The C# Master model no longer maps or exposes that field. Removing the output column would be a separate result-schema compatibility decision and was not bundled into this phase.

## 3. SQL Cleanup

### Removed safely

From Database/WarehouseModule.Procedures.sql:

- sp_DM_Master_List: removed the @Entity = N'KhoUser' branch.
- sp_DM_Master_Page: removed the @Entity = N'KhoUser' branch.

The generic master procedures now know only the master entities handled by CWarehouseMaster_Controller.

### Retained intentionally

sp_DM_Delete still contains:

~~~
ELSE IF @Entity=N'KhoUser' DELETE FROM dbo.tbl_DM_Kho_User WHERE Auto_ID=@Auto_ID;
~~~

This is not an unverified theoretical dependency. The current active caller is:

CWarehousePermission_Controller.cs:50

which invokes:

~~~
sp_DM_Delete, "KhoUser", p_iPermission_ID, ...
~~~

The following permission procedures remain unchanged:

- sp_DM_Kho_User_List
- sp_DM_Kho_User_Page
- sp_DM_Kho_User_Save

No SQL migration or deployment was performed.

## 4. Dependency Search

### Before

- CWarehouseMaster_Controller owned both authorization lookup methods.
- CWarehouseMaster.Login_Name was used by the master clone/info compatibility path.
- sp_DM_Master_List, sp_DM_Master_Page, and sp_DM_Delete contained KhoUser branches.
- Permission delete depended on the generic sp_DM_Delete contract.

### After

| Check | Result |
|---|---|
| List_Authorized_Warehouses_Async in Master Controller | 0 |
| List_User_Lookup_Async in Master Controller | 0 |
| Authorization lookup methods in Permission Controller | 2 |
| CWarehouseMaster.Login_Name consumer | 0 |
| List_Master_Async("KhoUser") production caller | 0 |
| List_Master_Page_Async("KhoUser") production caller | 0 |
| Save_Master_Async("KhoUser") production caller | 0 |
| Delete_Master_Async("KhoUser") production caller | 0 |
| KhoUser branch in sp_DM_Master_List | 0 |
| KhoUser branch in sp_DM_Master_Page | 0 |
| KhoUser branch in sp_DM_Delete | 1, retained for active Permission delete |

The remaining KhoUser strings in Razor are UI/tab/editor discriminators, not calls to the Master API. The test suite also contains literal assertions for the forbidden Master calls; those are evidence checks, not runtime callers.

CWarehouseMaster_Controller and the compatibility facade still expose generic string-based Master APIs. No internal production caller passes KhoUser, but an external compiled consumer cannot be ruled out from repository inspection alone.

### CodeGraph evidence

CodeGraph was used before editing to trace the Warehouse page, Master Controller, authorization lookup methods, document/report paths, and test blast radius. The existing index did not include the new untracked Permission symbols and remained stale after the local edits, still showing the old Master lookup methods. Therefore the final after-state was verified from current on-disk source search, compilation, and tests rather than from stale graph results.

## 5. Build/Test Result

### TDD evidence

- RED: after adding the migration assertions, the focused test build failed because CWarehousePermission_Controller did not yet expose List_Authorized_Warehouses_Async.
- GREEN: after the implementation, the focused refactor/authorization/UI tests passed.

### Results

~~~
dotnet build .\TKS_Thuc_Tap_V11.sln --no-restore --verbosity minimal
PASS: 0 errors, 27 warnings.

Focused Warehouse/refactor/UI tests
PASS: 32/32.

Non-integration tests
PASS: 105/105.

Full solution tests
INCONCLUSIVE for database-backed integration:
171 passed, 45 failed, 0 skipped, 216 total.
All 45 failures stopped at:
TKS_INTEGRATION_CONNECTION_STRING must point to a disposable test database.

git diff --check
PASS: no whitespace errors.
~~~

The build warnings are existing Telerik licensing warnings plus existing async-without-await warnings in the Warehouse Razor component. No Phase 2 compiler error was observed.

## 6. Risk Assessment

### Low risk

- Lookup Stored Procedures and parameters were preserved.
- The active UI now uses the Permission Controller for authorization/user lookup.
- The Master model no longer exposes a permission-shaped login field.
- Master List/Page no longer contain a Permission branch.
- Build and all non-integration tests pass.

### Medium risk

- The generic sp_DM_Delete contract still knows about KhoUser.
- Generic string APIs remain capable of accepting a legacy type at compile time, although no internal production caller was found.
- Unknown external consumers outside this repository cannot be verified.
- Full database-backed regression was not executable without an approved disposable database.
- CodeGraph needs a future index synchronization before it can be used as authoritative after-state evidence.

## 7. Remaining Technical Debt

### DONE

- Authorization lookup ownership moved to CWarehousePermission_Controller.
- Warehouse UI and the authorization test migrated.
- CWarehouseMaster.Login_Name removed with its active UI consumers.
- sp_DM_Master_List and sp_DM_Master_Page KhoUser branches removed.
- No benchmark or inventory files changed in this phase.

### OPTIONAL CLEANUP

- Introduce and validate a dedicated sp_DM_Kho_User_Delete contract, then remove the KhoUser branch from sp_DM_Delete in a separately approved SQL phase.
- Deprecate or split generic string-based compatibility APIs after an external consumer inventory.
- Decide whether the ordinary master SQL result schema should stop emitting the unused Login_Name placeholder column.
- Synchronize/re-index CodeGraph before the next dependency audit.

### BLOCKER / WARNING

- The Phase 2 objective is not an unconditional full SQL cleanup because sp_DM_Delete still has a confirmed active Permission caller. It was intentionally retained under the rule “safe cleanup over aggressive cleanup.”
- Full integration sign-off remains blocked until tests run against an approved disposable database.

