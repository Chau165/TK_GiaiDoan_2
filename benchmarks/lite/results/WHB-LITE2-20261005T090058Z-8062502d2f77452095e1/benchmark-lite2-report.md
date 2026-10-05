# Warehouse Benchmark Lite V2

- RunId: `WHB-LITE2-20261005T090058Z-8062502d2f77452095e1`
- Mode / verdict: **all / PREFLIGHT_ONLY**
- Started / finished UTC: 2026-10-05T09:00:58.0638709+00:00 / 2026-10-05T09:00:58.7689867+00:00
- Target: `DESKTOP-NHQ7QPL\MSSQLSERVER19` / `TKS_Thuc_Tap_V11_Perf_10000000` / DB_ID `5` / `ONLINE`
- Detail-table partition metadata rows: **10,000,002**
- Repository: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`
- Build/runtime: `Release`; benchmark DLL SHA-256 `FEC724A82CF16E1F30A48A4C4E4EAFCE22CB245B8EB696809CD96581BA90B670`; Data Access DLL SHA-256 `EA89541F875E2B920BFDCD79FA950A61BC0D4ADBF5045BDED1D08E0033E2F8C1`
- Business DB writes during measured workload: **0**; historical preparation worker calls: **0**
- Artifact integrity: **FAIL_REPORT_CONTENT**
- Full Batch4 canonical path: **STOPPED_BY_OWNER_SCOPE_CHANGE**

## Application path

Every measured request uses the current Data Access controller and Warehouse controller/base path to `CSqlHelper` and its paging stored procedure. Direct SQL is limited to target identity, dataset metadata, procedure parity, and historical readiness/preparation evidence.

## Procedure source parity

| Procedure | Role | Status | Source body SHA-256 | Live body SHA-256 |
|---|---|---|---|---|
| `sp_DM_Master_Page` | WORKLOAD | **PASS** | `965BB397D01A9183D61989BC25311E3DA51A5CCD24E0D889E4959D991A8ADC55` | `965BB397D01A9183D61989BC25311E3DA51A5CCD24E0D889E4959D991A8ADC55` |
| `sp_DM_Lookup_Page` | WORKLOAD | **PASS** | `CD02DA9E9498AE9F51D1091F5BAAB144D37B605F7C4CB99409DE5A4A38160209` | `CD02DA9E9498AE9F51D1091F5BAAB144D37B605F7C4CB99409DE5A4A38160209` |
| `sp_XNK_Document_Page` | WORKLOAD | **PASS** | `61A3AC146D4CD634D2FDEAA9FD7C33A73EFA4BEB19E70EA94C4816CC688305AC` | `61A3AC146D4CD634D2FDEAA9FD7C33A73EFA4BEB19E70EA94C4816CC688305AC` |
| `sp_BC_Chi_Tiet_Nhap_Page` | WORKLOAD | **PASS** | `354909FB19428C619AA43CD22E8FD31C088482E50EF53CCCB5C8D49CE1AC443F` | `354909FB19428C619AA43CD22E8FD31C088482E50EF53CCCB5C8D49CE1AC443F` |
| `sp_BC_Xuat_Nhap_Ton_Page` | WORKLOAD | **PASS** | `4DD986A65618E423EAA9FA96AFA57091185DD20FDC231E4F6A52495F0688FFEE` | `4DD986A65618E423EAA9FA96AFA57091185DD20FDC231E4F6A52495F0688FFEE` |
| `sp_BC_Ton_Kho_Hien_Tai_Page` | WORKLOAD | **PASS** | `C2A9265EE4380A7AEFD1A3BECB20F29F2FFC7B8B2C6E6D55A40C823484D31BDB` | `C2A9265EE4380A7AEFD1A3BECB20F29F2FFC7B8B2C6E6D55A40C823484D31BDB` |
| `sp_Inventory_Movement_Process_RebuildQueue` | HISTORICAL_PREPARATION_SUPPORT | **PASS** | `619FC765339FFA46A2FFC3EF4EE7A93B4DB60360B5907D5E7B7521A920D6A731` | `619FC765339FFA46A2FFC3EF4EE7A93B4DB60360B5907D5E7B7521A920D6A731` |
| `sp_Inventory_Movement_Rebuild` | HISTORICAL_PREPARATION_SUPPORT | **PASS** | `36B9955ED2E8EBDE53C10F9E245A58270E765BB7B719D5038E6E764D1C733DA3` | `36B9955ED2E8EBDE53C10F9E245A58270E765BB7B719D5038E6E764D1C733DA3` |

## Historical readiness and preparation

- Status: **NOT_EVALUATED**; worker calls: **0**
- Readiness reason: n/a
- Persisted pre-state fingerprint: `n/a`; post-state fingerprint: `n/a`
- A preparation call, if present, invoked only `sp_Inventory_Movement_Process_RebuildQueue` once with `@Batch_Size=1`; no SQL Agent job was started. Its pre/post queue and aggregate states are in `historical-readiness.json`.

## Sequential read results

Not run in this mode.

## Data-derived matrix

Not run in this mode.

## Bounded NBomber closed-load results

Not run in this mode.

## Hotspots

No sequential scenario produced measured samples.

## Errors and limitations

- Each phase records SQL timeout, SQL error, application exception, and other error separately; no per-request retries are configured.
- No latency SLA is defined; reported timings are observations, not a pass/fail performance target.
- One machine and one target database; the report does not guarantee production hardware performance.
- Matrix cases are qualified from current Data Access results. Out-of-range pages are omitted; cases that could not be verified remain explicit skips.
- Read measurement workload invokes no business write method. If historical preparation ran, it may update the inventory aggregate and queue through the one recorded bounded worker call.
- No BDN, mixed workload, 8-user stage, Full Batch4 canonical run, host admission, heavy telemetry, or production write path is included.
- Dataset size is partition metadata, not full-dataset value equality. Results do not establish a performance SLA or causal attribution.
- `HistoricalTimeoutCause=NOT_VERIFIED`, `HistoricalTelemetrySqlMinus2Cause=NOT_VERIFIED`, `FullDatasetValueEquality=NOT_VERIFIED`, and database data parity with the business database is not verified by this Lite run.

## Artifacts

- `benchmark-lite2-results.json`
- `benchmark-lite2-sequential-raw.csv`
- `benchmark-lite2-matrix-raw.csv`
- `benchmark-lite2-load-results.json`
- `benchmark-lite2-load-raw.csv`
- `benchmark-lite2-environment.json`
- `historical-readiness.json`
- `benchmark-lite2-run.log`
