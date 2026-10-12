# Function-code reclassification and implementation plan

Status: FUNCTION_CODE_RENUMBER_IMPLEMENTED_RETIREMENT_DEFERRED_ACTIVE_WEB_PROCESS

Target database: `TKS_Thuc_Tap_V11_GiaiDoan2` on `DESKTOP-NHQ7QPL\MSSQLSERVER19`. Every live mutation was guarded by `SELECT DB_NAME()` and an exact database-name check.

## Approved mapping and live function master

The final user-provided task explicitly approves these exact values:

| Before | After | Function | Auto_ID | Route |
|---|---|---|---:|---|
| 2011 | 3001 | Nhập kho | 110000102 | /Kho/Nhap_Kho |
| 2012 | 3002 | Xuất kho | 110000103 | /Kho/Xuat_Kho |
| 2013 | 9001 | Tồn kho | 110000104 | /Kho/Ton_Kho |
| 2014 | 9002 | Báo cáo | 110000105 | /Kho/Bao_Cao |

Only `Ma_Chuc_Nang` was updated on those four existing rows. Names, `Auto_ID`, parent/group IDs, routes, flags, audit fields, and other metadata were preserved. No current function row uses the old codes 2011–2014; target-code collision count was zero before update.

2015 (Phân quyền kho-user, Auto_ID 110000106) remains unchanged pending an approved function group. Codes 2009 and 2016–2019 remain in Master Data 2xxx. System 1xxx codes remain unchanged.

## Procedure, caller, and permission state

- Source now defines eight `F3001_sp_*` receipt procedures and eight `F3002_sp_*` issue procedures. Their live definitions were created from those source batches.
- All 16 new procedures have explicit `EXECUTE` grants to `Warehouse_Application`; Security and Preflight source list the new names. New modules use `ANSI_NULLS ON` and `QUOTED_IDENTIFIER ON`, matching the checked-in procedure script. Two old list-detail procedures had `QUOTED_IDENTIFIER OFF`; neither definition contains a double-quoted token.
- The live database currently has eight old `F2011_sp_*` and eight old `F2012_sp_*` objects and grants as well. They were retained because Web PID 19388 is still running the old binary and can call them. After a controlled application restart to the new build, retire the 16 old procedures and old grants.
- Live old/new procedure pairs: 16/16 match after changing only the prefix and normalizing whitespace. No business-logic edits were made.
- The production C# caller changed in `TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehouseDocument_Controller.cs`. Existing tests, SQL integration scripts, and the benchmark caller inventory were updated to the new Store prefixes.
- Razor route wrappers remain on their existing paths and resolve function metadata by route. One old component tag, `F2011_2_Noi_Xuat_Den_Info`, remains in `TKS_Thuc_Tap_V11_Web_Common/Common/FCommon_Drill_Down.razor`; no matching component source was found, so its ownership is still unresolved.
- Report/Page/Post stored-procedure names were not changed because the task does not approve those naming prefixes. 2013/2014 function codes changed without renaming their existing report procedures.

## Verification and protected scope

- Function master, routes, target code collisions, procedure counts, grants, and procedure body equivalence: VERIFIED against the live target DB.
- Focused static contract tests: 5/5 passed. Data_Access, Web, and test-project builds passed. Web build used a scratch output because the active executable locks the normal output; the build reported existing Telerik licensing warnings.
- Live CRUD, browser/UI UAT, and report/inventory behavior: NOT VERIFIED. The available integration tests mutate data in the production-like target database, so they were not run against it.
- The migration DDL targeted only the 16 F3001/F3002 procedures and the four function-code rows. No `sp_Inventory_*` object was targeted; the live catalog still contains all 35. Pre-migration protected-family fingerprint recorded at R0: `DE1BFB7D294B30ACAFF24CF64B48389AF49E01DD320FA2C215B5F2C2CAE60D15`.
- Worktree at implementation start: 142 changed paths (18 tracked modifications, 124 untracked). Current status: 161 paths (37 tracked modifications, 124 untracked). No existing paths were cleaned, reset, or reverted; the additional tracked edits are the requested renumber source/test changes.

## R0 inventory retained

- The live Master Data rows 2009 and 2016–2019 remain unchanged. Function 2015 remains in place with five F2015 procedures; the R0 catalog found an explicit object-level grant only for `F2015_sp_del_Kho_User`, so no broader permission claim is made.
- Before implementation, the 16 old receipt/issue names had 20 exact application-source references and 98 test/SQL-test references. Per-procedure counts remain in `TK_FUNCTION_CODE_RENUMBER_MAP.csv`.
- R0 found no SQL expression-dependency callers, external SQL-module text callers, synonyms, or SQL Agent job-step references for the 2011–2014 Store procedures.
- Seven current `sp_BC_*` report/page procedures retain their existing names. No F2013/F2014 procedure family was created.
- The live-only `sp_XNK_Validate_Affected_Balances` remains absent from local `Database/WarehouseModule.Procedures.sql` and was not modified.

## R0 preservation snapshot

The earlier exact-code blockers are superseded by the final task's explicit mapping. Historical per-procedure caller counts remain in the map; implementation status and current live retirement state are recorded in this plan and the final report. The five existing `TK_FUNCTION_CODE_RENUMBER_*` artifacts were updated in place.

## Remaining items

1. Schedule a controlled restart of Web PID 19388 using the rebuilt application.
2. After the restart, verify the new application calls F3001/F3002, then remove the 16 old procedures and their grants with a DB_NAME guard before each mutation.
3. Re-run the old-name DB/source audit. The task reaches its complete retirement state only after this step.
4. Keep 2015 unchanged until its function group is approved. Resolve the component-tag ownership separately; do not infer its mapping from its name.