# Unresolved function-code and retirement items

Status: exact function codes 2011–2014 are resolved and applied. Only the 2015 function-group assignment remains unresolved.

## Function code 2015

Live row 2015 is Phân quyền kho-user, Auto_ID 110000106, route `/Kho/Phan_Quyen`, with parent Hệ thống. This is suggestive but not company approval. Keep code 2015 and all five F2015 procedures unchanged until an authoritative group is provided.

## Legacy procedure retirement window

The live DB contains both the new eight F3001 and eight F3002 procedures and the old eight F2011 and eight F2012 procedures. Web PID 19388 is still running a binary that calls the old names. The old procedures and their `Warehouse_Application` grants remain available to protect that active process. Retire them after a controlled application restart to the new build.

## Component ownership

`F2011_2_Noi_Xuat_Den_Info` is referenced by `TKS_Thuc_Tap_V11_Web_Common/Common/FCommon_Drill_Down.razor`, but no matching component source was found in the inspected project tree. Ownership and whether it represents the renumbered function remain unresolved; no rename was inferred.

## Procedure naming constraints and source/live drift

- Report, Page, and Post procedure prefixes remain outside the approved rename scope. Existing `sp_BC_*`, Page, and `sp_XNK_Document_Post` names were preserved.
- Live SQL contains `sp_XNK_Validate_Affected_Balances`, absent from `Database/WarehouseModule.Procedures.sql`. This pre-existing source/live difference was not changed.
- No exact code decision remains for 2011, 2012, 2013, or 2014.