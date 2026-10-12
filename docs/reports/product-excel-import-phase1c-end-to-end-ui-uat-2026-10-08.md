# Product Excel Import Phase 1C - End-to-End UI UAT

Date: 2026-10-08

## 1. Executive Summary

**Verdict: `PRODUCT_IMPORT_PHASE1C_READY_WITH_REMAINING_EXTERNAL_UAT`.**

The current app was launched with a process-scoped TEST-only connection override. SQL Server showed the app's own pooled session connected to the owner-approved TEST DB. The browser reached the app, but the Product route redirected to the expired-token screen and required a user login. No runtime account credentials or permission profiles were available in this session, and no Excel Desktop window was available. Therefore no Product Import browser action, upload, Preview, Confirm, permission profile, history, audit, or grid-refresh claim is made. No Product rows were written; the run-prefix count remained `0`.

No Product Import source or appsettings changes were made. Phase 1B backend/runtime verification remains intact and was not repeated.

## 2. Environment Identity

| Item | Observed value |
|---|---|
| App environment | `Development` |
| App URL attempted | `http://127.0.0.1:15573/Kho/San_Pham` |
| App listening address | `http://127.0.0.1:15573` |
| SQL server | `DESKTOP-NHQ7QPL\MSSQLSERVER19` |
| SQL database | `TKS_Thuc_Tap_V11_C01C02_Test_20260906` |
| Test DB state | `ONLINE`, `READ_WRITE` |
| Business DB | Not used by the app process or modified |

The shell launched the app with `ConnectionStrings__TKS_Thuc_Tap_V11_Conn_String` set only for that process, targeting the approved TEST server/database with Windows Integrated Security and a unique application name. `Program.cs` reads this setting through `GetConnectionString`. A read-only query against `sys.dm_exec_sessions` returned `ProductImportPhase1C_UAT_20261008` with `DB_NAME = TKS_Thuc_Tap_V11_C01C02_Test_20260906`; `@@SERVERNAME` matched the approved server. The checked-in `appsettings*.json` files were not changed.

## 3. Browser Happy Path

`BROWSER_HAPPY_PATH: NOT_EXECUTED`.

Opening `/Kho/San_Pham` redirected to `/sys/token_expired_error`, which displayed “Token đã hết hạn hoặc có máy khác đăng nhập vào!”. The page's login link opened `/sys/dang_nhap`; the form requires a login code and password. No credentials were provided, so no sign-in attempt was made. Product page, import action, Preview, Confirm, success feedback, and grid visibility were not reached. This is an authentication/access prerequisite, not evidence of a Product Import regression.

## 4. Template Download

`TEMPLATE_DOWNLOAD_UI: NOT_EXECUTED`.

The authenticated Product page and its UI download action were not reached. The Phase 1A implementation report records the official artifact contract as `San_Pham_Import_Template.xlsx`, sheet `SanPham`, headers `Ma_San_Pham`, `Ten_San_Pham`, `Ma_LSP`, `Ten_Don_Vi_Tinh`, `Ghi_Chu`, and Product code cells formatted as Text. That prior source/template evidence does not substitute for the required UI download check.

## 5. Microsoft Excel Desktop Round-Trip

`EXCEL_DESKTOP_ROUND_TRIP: NOT_EXECUTED`.

No Excel Desktop window or connected workbook was available. No workbook was opened, edited, saved by Excel, or uploaded. No generated workbook is represented as an Excel Desktop result. Actual file size: not applicable; no test workbook was uploaded.

## 6. Valid Preview UI

`UI_PREVIEW_VALID: NOT_EXECUTED`.

No file selection or Preview was possible before authentication. Preview counts and the no-write-before-Confirm behavior were not observed through the browser. Phase 1B's service-level Preview no-write result remains `PASS (0 -> 0)` as recorded in its final report; it is not browser-path evidence.

## 7. Negative Validation UI

`UI_NEGATIVE_VALIDATION: NOT_EXECUTED`.

The following cases were not uploaded through the browser: duplicate code in workbook, existing TEST DB Product code, unknown Category, unknown Unit, formula cell, blank required code, 101 data rows, and wrong extension. Row/column presentation, Confirm gating, DB no-write, and exception UX were therefore not observed in the UI. Phase 1A validation tests are source-level evidence only.

## 8. Formula UX

`FORMULA_UI_ERROR: NOT_EXECUTED`.

Formula rejection and row/column messaging were not exercised in the browser. The Phase 1A report records unit coverage for formula validation, but that does not prove the UI displays the intended explanation.

## 9. Runtime Permission Profiles

`PERMISSION_ADD_RUNTIME: NOT_EXECUTED`

`PERMISSION_EDIT_ONLY_RUNTIME: NOT_EXECUTED`

`PERMISSION_NO_RIGHT_RUNTIME: NOT_EXECUTED`.

No authenticated runtime user profiles were available. Import button visibility, direct Preview/Confirm denial, and Edit-only denial were not tested against real application identities. Existing automated permission tests and the Phase 1B test resolver are not runtime-profile evidence.

## 10. Permission Revocation Between Preview and Confirm

`PERMISSION_REVOKE_BEFORE_CONFIRM: NOT_EXECUTED`.

No Add-authorized session or administrator permission screen/account was available. Cache refresh delay and server-side Confirm recheck were not measured in the live UI.

## 11. Upload Limit Runtime

| Gate | Result | Actual bytes |
|---|---|---:|
| Browser upload about 500 KiB | `NOT_EXECUTED` | N/A |
| Browser upload at/below 1 MiB | `NOT_EXECUTED` | N/A |
| Browser upload over 1 MiB | `NOT_EXECUTED` | N/A |

No file chooser was opened. Browser -> Blazor Server -> `FBase.Read_Import_File_Async` -> ClosedXML runtime behavior and user-facing oversize handling remain unverified.

## 12. Wrong Extension Runtime

`NOT_EXECUTED` for `.xls`, `.csv`, `.xlsm`, and a non-XLSX file. No upload was attempted, so clean rejection and absence of parser fallback were not observed through the UI.

## 13. Double-Confirm Protection

`DOUBLE_CONFIRM_PROTECTION: NOT_EXECUTED`.

No Confirm button was reached. Disabled-while-importing behavior, duplicate-click protection, and exact batch count were not observed.

## 14. Modal / State Behavior

`NOT_EXECUTED` for file replacement, invalid-file stale-state clearing, Importing, Success, and retry/error recovery. The import modal did not open.

## 15. Grid Refresh

`GRID_REFRESH_AFTER_IMPORT: NOT_EXECUTED`.

No import completed; one rebind, Product visibility without manual reload, and absence of a reload storm were not observed.

## 16. History Event

`HISTORY_SUMMARY_EVENT: NOT_EXECUTED`.

No Confirm occurred. Phase 1C created zero new Product-import history events because no import was attempted; event count after a successful browser import remains unknown.

## 17. UI Audit Fields

`UI_AUDIT_FIELDS: NOT_EXECUTED`.

No Product was imported through the UI, so no runtime-created Product was available for audit-field inspection. Phase 1B verified its separate service/runtime context; that does not establish the current browser user's audit values.

## 18. Cuộn / Cuốn UI Runtime

`UNIT_CI_AS_UI: NOT_EXECUTED`.

No unit workbook was previewed or confirmed through the UI. The Phase 1B service runtime previously resolved `Cuộn`/`CUỘN -> 205` and `Cuốn`/`CUỐN -> 211`; this remains backend evidence only, not browser UAT.

## 19. Cleanup

Test prefix: `UAT_UI_PI_20261008_121754_13a32a45_`.

| Check | Count |
|---|---:|
| TEST DB matching Product rows before UI attempt | 0 |
| TEST DB matching Product rows after UI attempt | 0 |
| Rows deleted | 0 |

No Product import was attempted, so cleanup required no deletion. The count was checked with an exact binary prefix comparison on the TEST DB. No broad delete, `TRUNCATE`, identity reset, or Business DB operation was performed.

## 20. Bugs Found

No Product Import UI behavior was reachable, so this run found no classifiable Product Import bug and cannot claim the UI flow is correct. The observed blocker is the app's login requirement with no runtime account credentials/profile supplied. The login page also displayed a Telerik missing-license watermark; Telerik licensing warnings are known and explicitly out of scope for this phase.

## 21. Source Changes Made

No Product Import source, SQL, project configuration, `appsettings.json`, or `appsettings.Development.json` changes were made. The TEST connection override existed only in the app process environment. This report is the only file added by Phase 1C.

## 22. Regression Tests

No tests were rerun because no source was changed and Phase 1B explicitly passed the focused regression filter `58/58` with zero failures/skips. Its runtime integration test passed `1/1`. Those results remain the latest recorded regression evidence, not new Phase 1C UI evidence. Destructive backend proofs were not repeated.

## 23. Build Results

The current Web app was started successfully from source with `dotnet run --no-restore --no-launch-profile --urls http://127.0.0.1:15573`; the browser received the app's login page after the token-expired redirect. The app reported `Development` and listened on the requested address. The build emitted existing Telerik warnings `TKL002`, `TKL101`, and `TKL004`; they were not changed or suppressed. The Phase 1B report records all three required project builds at zero errors (Web_Common: 4 warnings, Web_Danh_Muc: 12, Web: 24). No separate rebuild of all projects was run for Phase 1C.

## 24. Production Deployment Pending

`PRODUCTION_DEPLOYMENT_PENDING` remains unchanged. Business DB `TKS_Thuc_Tap_V11_GiaiDoan2` was not used or modified. Its F2016 CI_AI versus source/TEST CI_AS mismatch remains the separately documented deployment item; it does not block TEST DB UI UAT.

## 25. Remaining UAT Gates

The owner/user must provide or open a real Add-authorized application session and, if available, Edit-only/no-right profiles plus an administrator session for the permission-revoke test. With those profiles, complete the Product route, template download, valid and invalid Preview, Confirm, double-click, refresh, history, audit, and Cuộn/Cuốn checks against the TEST app.

Excel Desktop round-trip still requires the UI-downloaded official template, an Excel-saved `.xlsx` containing a text `00123`-style code, Vietnamese Unicode, trimming cases, and blank note. Browser upload tests still need measured files near 500 KiB, near/below 1 MiB, and over 1 MiB, plus `.xls`, `.csv`, `.xlsm`, and non-XLSX rejection cases. Clean only the exact run prefix after successful UI imports.

The app was stopped after verification; port `15573` is no longer listening. To relaunch without changing repository configuration, use a process-scoped TEST override:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ConnectionStrings__TKS_Thuc_Tap_V11_Conn_String = 'Server=DESKTOP-NHQ7QPL\MSSQLSERVER19;Database=TKS_Thuc_Tap_V11_C01C02_Test_20260906;Integrated Security=True;TrustServerCertificate=True;'
dotnet run --no-restore --no-launch-profile --urls http://127.0.0.1:15573
```

## 26. Final Decision Table

| Gate | Result | Evidence |
|---|---|---|
| Browser Product page | `NOT_EXECUTED` | Direct route redirected to `/sys/token_expired_error`; login required |
| Import button/Add gating | `NOT_EXECUTED` | No authenticated Add profile |
| Template download | `NOT_EXECUTED` | Product page was not reached; source contract exists in Phase 1A report |
| Excel Desktop workbook | `NOT_EXECUTED` | No Excel Desktop session; no workbook saved/uploaded |
| Valid Preview | `NOT_EXECUTED` | No file selected |
| Invalid Preview | `NOT_EXECUTED` | Invalid files not uploaded |
| Formula rejection | `NOT_EXECUTED` | UI formula message not observed |
| Add permission | `NOT_EXECUTED` | No runtime Add account |
| Edit-only denial | `NOT_EXECUTED` | No runtime Edit-only account |
| No-right denial | `NOT_EXECUTED` | No runtime no-right account |
| Permission revoke before Confirm | `NOT_EXECUTED` | No user/admin session; cache delay not measured |
| 500 KiB upload | `NOT_EXECUTED` | No browser upload; bytes N/A |
| <=1 MiB upload | `NOT_EXECUTED` | No browser upload; bytes N/A |
| >1 MiB rejection | `NOT_EXECUTED` | No browser upload; bytes N/A |
| Wrong extension | `NOT_EXECUTED` | No `.xls`/`.csv`/`.xlsm`/other upload |
| Double Confirm | `NOT_EXECUTED` | Confirm was not reached |
| Grid refresh once | `NOT_EXECUTED` | No import completed |
| History one summary | `NOT_EXECUTED` | No import completed; no new event created |
| Audit fields | `NOT_EXECUTED` | No UI-created Product to inspect |
| Cuộn runtime | `NOT_EXECUTED` | Phase 1B service result `205` is not UI evidence |
| Cuốn runtime | `NOT_EXECUTED` | Phase 1B service result `211` is not UI evidence |
| Cleanup | `PASS (0 -> 0)` | Exact run prefix count in TEST DB; no rows were written |
| Regression | `PASS (Phase 1B baseline; not rerun)` | 58/58 focused tests and 1/1 runtime integration recorded in Phase 1B; no source changed |
| Build | `PASS (app launch)` | Web app started on requested local port; all required project builds previously had 0 errors in Phase 1B |

## 27. Final Verdict

`PRODUCT_IMPORT_PHASE1C_READY_WITH_REMAINING_EXTERNAL_UAT`.

The TEST-only app environment was verified and the run left the Product prefix at zero. Required browser, real Excel, and runtime-permission gates remain unexecuted because authenticated user/profile access was unavailable. Phase 1B service/database verification remains valid and unchanged. This verdict is not `END_TO_END_VERIFIED` and is not production deployment approval.
