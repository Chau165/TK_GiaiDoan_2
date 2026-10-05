# Warehouse Benchmark Lite V2

- RunId: `WHB-LITE2-20261005T090156Z-39716c676c8d47dbb015`
- Mode / verdict: **all / PARTIAL**
- Started / finished UTC: 2026-10-05T09:01:56.5068151+00:00 / 2026-10-05T09:14:01.8333607+00:00
- Target: `DESKTOP-NHQ7QPL\MSSQLSERVER19` / `TKS_Thuc_Tap_V11_Perf_10000000` / DB_ID `5` / `ONLINE`
- Detail-table partition metadata rows: **10,000,002**
- Repository: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`
- Build/runtime: `Release`; benchmark DLL SHA-256 `12976FE7CC5E9E8493796C9472EE11A49BBF4063FAF64AD9FF31F7DD03D380E8`; Data Access DLL SHA-256 `EA89541F875E2B920BFDCD79FA950A61BC0D4ADBF5045BDED1D08E0033E2F8C1`
- Business DB writes during measured workload: **0**; historical preparation worker calls: **1**
- Artifact integrity: **PASS**
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

- Status: **READY_AFTER_BOUNDED_PREPARATION**; worker calls: **1**
- Readiness reason: n/a
- Persisted pre-state fingerprint: `3D0343EAF87CF45327EE5E19390C7D409D27EF6A8493184C6887B6B75A9E4681`; post-state fingerprint: `4210C1BCFD908A2C4B0B8202AD297221BA47EAB8BFF3240DE3BCECCDD53A0278`
- A preparation call, if present, invoked only `sp_Inventory_Movement_Process_RebuildQueue` once with `@Batch_Size=1`; no SQL Agent job was started. Its pre/post queue and aggregate states are in `historical-readiness.json`.

## Sequential read results

| Scenario | Smoke | Warmup | Successful measured | Mean ms | SD ms | P50 ms | P95 ms | Max ms | Verdict |
|---|---|---|---:|---:|---:|---:|---:|---:|---|
| `MasterPaged` | True | 3/3 | 30/30 | 2.292 | 0.669 | 2.085 | 4.114 | 4.341 | **PASS** |
| `LookupPaged` | True | 3/3 | 30/30 | 2.324 | 0.640 | 2.148 | 3.342 | 3.814 | **PASS** |
| `DocumentPaged` | True | 3/3 | 29/30 | 897.150 | 3206.673 | 50.814 | 9961.037 | 14558.176 | **TIMEOUT** |
| `DetailReportPaged` | True | 3/3 | 30/30 | 178.970 | 29.182 | 172.652 | 246.230 | 249.058 | **PASS** |
| `InventoryHistoricalReportPaged` | True | 3/3 | 30/30 | 933.488 | 72.268 | 925.013 | 1066.149 | 1076.917 | **PASS** |
| `InventoryCurrentBalancePaged` | True | 3/3 | 30/30 | 25.032 | 5.416 | 22.413 | 35.774 | 38.249 | **PASS** |

## Data-derived matrix

| Scenario | Case | Parameters | Successful measured | Mean ms | P95 ms | Verdict / skip reason |
|---|---|---|---:|---:|---:|---|
| `DocumentPaged` | `DocumentPage1` | `{"IsReceipt":true,"PageNumber":1,"PageSize":10,"SearchText":"","Login":"PERF_USER","WarehouseId":null}` | 10/10 | 38.254 | 42.936 | **PASS** n/a |
| `DocumentPaged` | `DocumentPage100` | `{"IsReceipt":true,"PageNumber":100,"PageSize":10,"SearchText":"","Login":"PERF_USER","WarehouseId":null}` | 10/10 | 54.477 | 68.518 | **PASS** n/a |
| `DocumentPaged` | `DocumentPage1000` | `{"IsReceipt":true,"PageNumber":1000,"PageSize":10,"SearchText":"","Login":"PERF_USER","WarehouseId":null}` | 10/10 | 119.213 | 151.619 | **PASS** n/a |
| `DocumentPaged` | `DocumentPage10000` | `{"IsReceipt":true,"PageNumber":10000,"PageSize":10,"SearchText":"","Login":"PERF_USER","WarehouseId":null}` | 10/10 | 786.663 | 913.803 | **PASS** n/a |
| `DocumentPaged` | `DocumentSearchCommon` | `{"IsReceipt":true,"PageNumber":1,"PageSize":10,"SearchText":"PERF-WH-10","SearchTermSource":"most frequent observed Ten_Kho","Login":"PERF_USER"}` | 10/10 | 776.099 | 1018.039 | **PASS** n/a |
| `DocumentPaged` | `DocumentSearchRare` | `{"IsReceipt":true,"PageNumber":1,"PageSize":10,"SearchText":"PERF-IN-499320","SearchTermSource":"least frequent observed So_Phieu","Login":"PERF_USER"}` | 10/10 | 7342.988 | 9103.369 | **PASS** n/a |
| `DetailReportPaged` | `DetailPage1` | `{"IsReceipt":true,"From":"2025-01-01","To":"2026-12-31","PageNumber":1,"PageSize":10,"Login":"PERF_USER","WarehouseId":null}` | 10/10 | 243.994 | 299.215 | **PASS** n/a |
| `DetailReportPaged` | `DetailDeepPage100` | `{"IsReceipt":true,"From":"2025-01-01","To":"2026-12-31","PageNumber":100,"PageSize":10,"Login":"PERF_USER"}` | 10/10 | 236.754 | 255.337 | **PASS** n/a |
| `DetailReportPaged` | `DetailNarrowObservedDay` | `{"IsReceipt":true,"From":"2025-01-01","To":"2025-01-01","PageNumber":1,"PageSize":10,"Login":"PERF_USER","RangeSource":"observed Ngay in Data Access pages"}` | 10/10 | 22.849 | 26.312 | **PASS** n/a |
| `DetailReportPaged` | `DetailBroadObservedEnvelope` | `{"IsReceipt":true,"From":"2025-01-01","To":"2026-12-31","PageNumber":1,"PageSize":10,"Login":"PERF_USER","RangeSource":"observed Ngay plus baseline window"}` | 10/10 | 293.077 | 518.307 | **PASS** n/a |
| `InventoryCurrentBalancePaged` | `CurrentBalancePage1` | `{"PageNumber":1,"PageSize":10,"Login":"PERF_USER","WarehouseId":null,"ReadCurrentBalance":true}` | 10/10 | 32.912 | 47.228 | **PASS** n/a |
| `InventoryCurrentBalancePaged` | `CurrentBalanceDeepPage100` | `{"PageNumber":100,"PageSize":10,"Login":"PERF_USER","WarehouseId":null,"FilterSupport":"none in current Data Access method"}` | 10/10 | 34.928 | 51.342 | **PASS** n/a |
| `InventoryHistoricalReportPaged` | `HistoricalNormalRange` | `{"From":"2025-01-01","To":"2026-12-31","PageNumber":1,"PageSize":10,"Login":"PERF_USER","WarehouseId":null,"ReadCurrentBalance":false}` | 10/10 | 1014.586 | 1149.783 | **PASS** n/a |
| `InventoryHistoricalReportPaged` | `HistoricalBroadObservedRange` | `{"From":"0001-01-01","To":"2026-12-31","PageNumber":1,"PageSize":10,"Login":"PERF_USER","RangeSource":"authorized balance-scope metadata with SQL DATE lower bound"}` | 10/10 | 1015.103 | 1130.526 | **PASS** n/a |

## Bounded NBomber closed-load results

| Scenario | Users/copies | Warmup / measured | Warmup ok/fail | Measured ok/fail | Native RPS | Native mean / P95 / P99 / max ms | Verdict |
|---|---:|---:|---:|---:|---:|---|---|
| `DocumentPaged` | 1/1 | 3s / 30s | 52/0 | 618/0 | 20.600 | 47.750 / 57.540 / 62.590 / 210.010 | **PASS** n/a |
| `DocumentPaged` | 2/2 | 3s / 30s | 71/0 | 678/0 | 22.600 | 87.730 / 116.030 / 137.090 / 157.660 | **PASS** n/a |
| `DocumentPaged` | 4/4 | 3s / 30s | 60/0 | 678/0 | 22.600 | 174.960 / 240.770 / 290.820 / 361.250 | **PASS** n/a |
| `DetailReportPaged` | 1/1 | 3s / 30s | 12/0 | 154/0 | 5.130 | 191.660 / 249.600 / 279.300 / 622.280 | **PASS** n/a |
| `DetailReportPaged` | 2/2 | 3s / 30s | 21/0 | 162/0 | 5.400 | 368.530 / 414.720 / 495.620 / 785.600 | **PASS** n/a |
| `DetailReportPaged` | 4/4 | 3s / 30s | 23/0 | 163/0 | 5.430 | 736.940 / 845.310 / 958.980 / 1015.520 | **PASS** n/a |
| `InventoryHistoricalReportPaged` | 1/1 | 3s / 30s | 3/0 | 34/0 | 1.130 | 885.720 / 963.580 / 1189.890 / 1189.810 | **PASS** n/a |
| `InventoryHistoricalReportPaged` | 2/2 | 3s / 30s | 6/0 | 53/0 | 1.770 | 1135.500 / 1387.520 / 1618.940 / 1620.460 | **PASS** n/a |
| `InventoryHistoricalReportPaged` | 4/4 | 3s / 30s | 9/0 | 82/0 | 2.730 | 1476.370 / 1790.980 / 1925.120 / 1957.110 | **PASS** n/a |
| `InventoryCurrentBalancePaged` | 1/1 | 3s / 30s | 92/0 | 1198/0 | 39.930 | 24.640 / 38.660 / 52.260 / 118.700 | **PASS** n/a |
| `InventoryCurrentBalancePaged` | 2/2 | 3s / 30s | 184/0 | 1551/0 | 51.700 | 38.270 / 55.260 / 68.030 / 105.250 | **PASS** n/a |
| `InventoryCurrentBalancePaged` | 4/4 | 3s / 30s | 286/0 | 3083/0 | 102.770 | 38.490 / 51.100 / 55.900 / 64.960 | **PASS** n/a |

Per-stage RAM snapshots, reflected native NBomber `NodeStats.ScenarioStats`, and the comparison of callback counters with native counters are preserved in `benchmark-lite2-load-results.json`.

## Hotspots

- `DocumentPaged`: P95 9961.037 ms, mean 897.150 ms.
- `InventoryHistoricalReportPaged`: P95 1066.149 ms, mean 933.488 ms.
- `DetailReportPaged`: P95 246.230 ms, mean 178.970 ms.

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
