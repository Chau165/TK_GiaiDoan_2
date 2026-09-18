# Warehouse Benchmark V2.1 Adaptive

This directory contains the versioned adaptive low-memory runner and protocol manifest.

V2.0 evidence under `benchmarks/v2` and `P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_1-20260915-211500` is historical and is not overwritten. V2.1 reuses the accepted frozen V2 runtime, removes the universal 1024 MB admission gate, calibrates each block class, and retains the 512 MB live-workload hard stop.

The runner targets only `localhost\MSSQLSERVER19`, database `TKS_Thuc_Tap_V11_Perf_10000000`, DB ID 5. It does not deploy SQL, rebuild Current, flush caches, start SQL Agent, or benchmark the Business DB.
