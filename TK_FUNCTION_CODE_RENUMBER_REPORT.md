# Final function-code renumber implementation report

Status: `FUNCTION_CODE_RENUMBER_IMPLEMENTED_RETIREMENT_DEFERRED_ACTIVE_WEB_PROCESS`

1. **Function master before/after.** Live DB `TKS_Thuc_Tap_V11_GiaiDoan2` retained the existing rows and changed only `Ma_Chuc_Nang`.

   | Auto_ID | Function | Before | After | Route |
   |---:|---|---:|---:|---|
   | 110000102 | Nhập kho | 2011 | 3001 | /Kho/Nhap_Kho |
   | 110000103 | Xuất kho | 2012 | 3002 | /Kho/Xuat_Kho |
   | 110000104 | Tồn kho | 2013 | 9001 | /Kho/Ton_Kho |
   | 110000105 | Báo cáo | 2014 | 9002 | /Kho/Bao_Cao |

2. **Approved mapping.** 2011 → 3001; 2012 → 3002; 2013 → 9001; 2014 → 9002. The live routes, parent IDs, names, and other row fields remain unchanged.

3. **F2011 → F3001 procedure mappings.**

   - `F2011_sp_ins_Nhap_Kho_Header` → `F3001_sp_ins_Nhap_Kho_Header`
   - `F2011_sp_upd_Nhap_Kho_Header` → `F3001_sp_upd_Nhap_Kho_Header`
   - `F2011_sp_ins_Nhap_Kho_Detail` → `F3001_sp_ins_Nhap_Kho_Detail`
   - `F2011_sp_upd_Nhap_Kho_Detail` → `F3001_sp_upd_Nhap_Kho_Detail`
   - `F2011_sp_del_Nhap_Kho_Header` → `F3001_sp_del_Nhap_Kho_Header`
   - `F2011_sp_del_Nhap_Kho_Detail` → `F3001_sp_del_Nhap_Kho_Detail`
   - `F2011_sp_sel_List_Nhap_Kho` → `F3001_sp_sel_List_Nhap_Kho`
   - `F2011_sp_sel_List_Nhap_Kho_Detail` → `F3001_sp_sel_List_Nhap_Kho_Detail`

4. **F2012 → F3002 procedure mappings.**

   - `F2012_sp_ins_Xuat_Kho_Header` → `F3002_sp_ins_Xuat_Kho_Header`
   - `F2012_sp_upd_Xuat_Kho_Header` → `F3002_sp_upd_Xuat_Kho_Header`
   - `F2012_sp_ins_Xuat_Kho_Detail` → `F3002_sp_ins_Xuat_Kho_Detail`
   - `F2012_sp_upd_Xuat_Kho_Detail` → `F3002_sp_upd_Xuat_Kho_Detail`
   - `F2012_sp_del_Xuat_Kho_Header` → `F3002_sp_del_Xuat_Kho_Header`
   - `F2012_sp_del_Xuat_Kho_Detail` → `F3002_sp_del_Xuat_Kho_Detail`
   - `F2012_sp_sel_List_Xuat_Kho` → `F3002_sp_sel_List_Xuat_Kho`
   - `F2012_sp_sel_List_Xuat_Kho_Detail` → `F3002_sp_sel_List_Xuat_Kho_Detail`

5. **Old procedures retired:** 0. The old 16 objects and grants remain because Web PID 19388 is still running the old binary. Retire after a controlled restart to the new build.

6. **New procedures created:** 16 total; 8 F3001 and 8 F3002. Live catalog counts are 8 each. All 16 old/new procedure bodies match after changing only the prefix and normalizing whitespace. New modules use `ANSI_NULLS ON` and `QUOTED_IDENTIFIER ON`, matching the checked-in procedure script. Two legacy list-detail objects have `QUOTED_IDENTIFIER OFF`; their definitions contain no double-quoted tokens.

7. **C# callers changed.** Production caller: `TKS_Thuc_Tap_V11_Data_Access/Controller/Warehouse/CWarehouseDocument_Controller.cs`. Existing C# tests, SQL integration scripts, and `benchmarks/v2.2/q2-qualification/Program.cs` were updated to the new procedure names.

8. **Razor/component references changed:** 0. Route wrappers continue resolving function metadata dynamically by route. One old tag, `F2011_2_Noi_Xuat_Den_Info`, remains in `TKS_Thuc_Tap_V11_Web_Common/Common/FCommon_Drill_Down.razor`; no matching component source was found, so ownership is unresolved.

9. **Security and Preflight:** `Database/WarehouseModule.Security.sql` and `Database/WarehouseModule.Security.Preflight.sql` now list all 16 new procedures. Live `Warehouse_Application` grants are 8/8 for each new prefix. The old 8+8 grants remain for the active binary.

10. **Menu/function seed:** `Database/WarehouseModule.Menu.sql` now seeds 3001/3002/9001/9002 with the existing routes. The live master rows were updated in place by Auto_ID; no route or other metadata changed.

11. **Permission verification:** VERIFIED in the live target DB; 16 explicit new grants exist and match the Preflight source entries. Old grants remain until retirement.

12. **Nhập kho tests:** focused static contract tests passed 5/5 overall, including receipt header/detail split and the approved mapping contract. Live CRUD integration/UAT was not run.

13. **Xuất kho tests:** focused static contract tests passed 5/5 overall, including issue header/detail split. Live CRUD integration/UAT was not run.

14. **Tồn kho tests:** function-code and route mapping contract is VERIFIED; inventory UI/data behavior was not run.

15. **Báo cáo tests:** function-code and route mapping contract is VERIFIED; report selector/data behavior was not run.

16. **Build:** Data_Access build passed (0 warnings, 0 errors). Web build passed from scratch output (0 errors; existing Telerik licensing warnings). Test project build passed (0 warnings, 0 errors). `git diff --check` reported no whitespace errors. Live CRUD integration and browser/UI UAT were not run because the available tests write to the production-like target DB.

17. **Old active references:** Live function-master codes 2011–2014 are absent. Live DB still has the 16 old F2011/F2012 procedures and grants for PID 19388. Active C# caller and SQL deployment/test definitions use the new prefixes; test assertions intentionally mention old prefixes. No F2013/F2014 procedures were introduced. The unowned Razor tag listed in item 8 remains.

18. **2015 status:** unchanged as code 2015, with five F2015 procedures unchanged. Its approved function group remains unresolved.

19. **35 `sp_Inventory_*` procedures:** live count remains 35. The audited DDL targeted only F3001/F3002; DML updated only `tbl_Sys_Chuc_Nang.Ma_Chuc_Nang`, so none of the 35 definitions was altered. R0 pre-migration aggregate fingerprint: `DE1BFB7D294B30ACAFF24CF64B48389AF49E01DD320FA2C215B5F2C2CAE60D15`. A post-migration aggregate hash was not recomputed.

20. **Worktree preservation:** implementation began with 142 changed paths (18 tracked, 124 untracked); current status has 161 (37 tracked, 124 untracked). No pre-existing path was cleaned, reset, or reverted. Nineteen previously clean tracked paths became modified for the requested source, SQL, and regression-reference updates; the five R0 artifacts were updated in place.

21. **Rollback artifact:** `TK_FUNCTION_CODE_RENUMBER_ROLLBACK.sql` (absolute path: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11\TK_FUNCTION_CODE_RENUMBER_ROLLBACK.sql`). It restores the old procedures/grants and function codes and drops the new procedure objects, with database-name guards.

## Required confirmations

- `SYSTEM_1XXX_PRESERVED` — VERIFIED
- `MASTER_DATA_2XXX_PRESERVED` — VERIFIED
- `NHAP_KHO_3001_COMPLETE` — IMPLEMENTED; old-procedure retirement pending restart
- `XUAT_KHO_3002_COMPLETE` — IMPLEMENTED; old-procedure retirement pending restart
- `TON_KHO_9001_COMPLETE` — function code applied; behavior UAT not run
- `BAO_CAO_9002_COMPLETE` — function code applied; behavior UAT not run
- `WAREHOUSE_PERMISSION_2015_UNCHANGED` — VERIFIED
- `TECHNICAL_INVENTORY_STORES_UNCHANGED` — VERIFIED by migration scope and live count 35
- `NO_BUSINESS_LOGIC_CHANGE` — VERIFIED; changes are prefix/code substitutions
- `GROUP_A_PRESERVED`, `GROUP_B_PRESERVED`, `GROUP_C_PRESERVED` — VERIFIED
- `NO_ADAPTER_CHANGE`, `NO_API_PDA_CHANGE` — VERIFIED

Retirement is the remaining implementation step. Complete it after the active Web process is restarted against the new build.