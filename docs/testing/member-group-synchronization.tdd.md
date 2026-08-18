# Member group synchronization TDD evidence

Source plan: journeys derived during this TDD run.

## User journeys

- As an administrator, when I remove a member from a group, I see the member's current groups without stale entries.
- As an administrator, I can restore a user to the `Quản trị` group so the account regains its administrative access.

## Evidence

| # | Guarantee | Test or command | Type | Result |
|---|---|---|---|---|
| 1 | Deleting a group mapping refreshes the member's group summary. | `dotnet test .\\TKS_Thuc_Tap_V11_Data_Access.Tests\\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --filter FullyQualifiedName~MemberGroupSynchronizationTests --no-restore` | Integration | RED before the procedure update; GREEN after it (1 passed). |
| 2 | `thuctap_kho` is mapped to `Quản trị`, and the summary is synchronized. | SQL verification against `view_Sys_Nhom_Thanh_Vien_User` and `view_Sys_Thanh_Vien` | Integration | `Quản trị` returned by both queries. |
| 3 | The restarted web application is reachable. | `Invoke-WebRequest http://localhost:15073/` | Runtime | HTTP 200. |

## Coverage and gaps

This repository has no configured coverage collector. The targeted integration test is the regression guard for the stored-procedure contract; browser login credentials were intentionally not exercised by automation.
