# Warehouse Benchmark Lite — 10M Dataset

## 1. Mục tiêu

Đo source hiện tại qua các Warehouse Data Access controller trên Perf DB có dataset lớn, theo thứ tự tuần tự, và ghi latency thực tế. Đây là phép đo mô tả, không đặt SLA.

## 2. Environment

- Repository: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`
- Server: `n/a`; Database: `n/a`; DB_ID: ``; state: `n/a`
- Dataset primary detail rows: **NOT VERIFIED**
- Machine: `DESKTOP-NHQ7QPL`; OS: `Microsoft Windows 10.0.26200`; logical CPU: 8; RAM: 8109.000 MB; available before run: 1582.000 MB; .NET: `.NET 8.0.20`
- Protocol: warmup 3; measured 10; concurrency 1; sequential scenario order; nearest-rank percentiles.
- Host admission: not used; heavy telemetry: not used; application command timeout override: none.

## 3. Scenario Results

| Scenario | Smoke | Success | Avg ms | P50 ms | P95 ms | Max ms | Errors | Verdict |
|---|---:|---:|---:|---:|---:|---:|---:|---|

## 4. Ranking

No scenario produced a successful measured sample.

## 5. 10M Capability

- `SOURCE_CURRENT_CAN_RUN_ON_10M_DB` = **NOT VERIFIED**

## 6. Hotspots

None; no successful measured calls.

## 7. Errors and scenario call details

## 8. Limitations

- One machine, one process, concurrency 1, ten measured calls per scenario.
- Results represent this machine environment and do not guarantee production hardware performance.
- Read workloads only; no writes, concurrency stress, mixed workload, or heavy SQL telemetry.
- No performance SLA was defined; timings are reported as measured and are not labeled good or bad.
- P50/P95 use nearest-rank with only ten samples; P95 therefore selects the highest successful sample.
- Dataset scale uses SQL Server partition row-count metadata, not a full COUNT(*) scan or value-equality proof.

## 9. Run integrity

- RunId: `WHB-LITE-20261005T075828Z-25cd479284d34bb1`; status: `HARNESS_FAILED`; BusinessDBWrites: **0**.
- Results: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11\benchmarks\lite\results\WHB-LITE-20261005T075828Z-25cd479284d34bb1\benchmark-lite-results.json`
- Environment: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11\benchmarks\lite\results\WHB-LITE-20261005T075828Z-25cd479284d34bb1\benchmark-lite-environment.json`
- Raw CSV: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11\benchmarks\lite\results\WHB-LITE-20261005T075828Z-25cd479284d34bb1\benchmark-lite-raw.csv`
- Results represent this machine environment: **YES**; production hardware guarantee: **NO**.
