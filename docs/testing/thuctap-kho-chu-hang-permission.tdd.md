# `thuctap_kho` Chủ hàng permission TDD evidence

Source plan: journeys derived during this TDD run.

## User journey

As an administrator, I want `thuctap_kho` to open the Chủ hàng page and use the add-new action, so the account can manage owner records.

## Diagnosis

`/Danh_Muc/Chu_Hang` resolves to function code `2003`. `FBase` redirects to `/Sys/Permission_Error` when the effective View permission is false. Before recovery, the account's Chủ hàng membership was soft-deleted and the group had no active permission row for function `2003`.

## RED/GREEN evidence

| # | Guarantee | Test or command | Result |
|---|---|---|---|
| 1 | `thuctap_kho` has an active Chủ hàng membership and full permissions for function `2003`. | `dotnet test .\\TKS_Thuc_Tap_V11_Data_Access.Tests\\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --filter FullyQualifiedName~ChuHangPermissionTests --no-restore --no-build` | RED before recovery; GREEN after recovery (1 passed). |
| 2 | The whole existing data-access test target remains green. | `dotnet test .\\TKS_Thuc_Tap_V11_Data_Access.Tests\\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-build` | 7 passed, 0 failed. |
| 3 | The restarted web host is reachable. | `Invoke-WebRequest http://localhost:15073/` | HTTP 200. |
| 4 | Database state matches the UI contract. | SQL verification of `view_Sys_Nhom_Thanh_Vien_User`, `view_Sys_Phan_Quyen_Chuc_Nang`, and `view_Sys_Thanh_Vien` | Membership active; View/Add/Edit/Delete/Export = 1; summary includes `Chủ hàng`. |

## Coverage and known gaps

No coverage collector is configured in this repository. Browser login was not automated because the password was not provided; the authenticated authorization seam is covered by the SQL integration test and the runtime host check.
