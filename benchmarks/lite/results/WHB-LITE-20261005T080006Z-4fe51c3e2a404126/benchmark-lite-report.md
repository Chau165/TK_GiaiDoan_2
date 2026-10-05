# Warehouse Benchmark Lite — 10M Dataset

## 1. Mục tiêu

Đo source hiện tại qua các Warehouse Data Access controller trên Perf DB có dataset lớn, theo thứ tự tuần tự, và ghi latency thực tế. Đây là phép đo mô tả, không đặt SLA.

## 2. Environment

- Repository: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`
- Server: `DESKTOP-NHQ7QPL\MSSQLSERVER19`; Database: `TKS_Thuc_Tap_V11_Perf_10000000`; DB_ID: `5`; state: `ONLINE`
- Dataset primary detail rows: **10,000,002**
  - `dbo.tbl_XNK_Nhap_Kho_Raw_Data`: 5,000,002
  - `dbo.tbl_XNK_Xuat_Kho_Raw_Data`: 5,000,000
  - `dbo.tbl_XNK_Nhap_Kho` headers: 500,002
  - `dbo.tbl_XNK_Xuat_Kho` headers: 500,000
- Machine: `DESKTOP-NHQ7QPL`; OS: `Microsoft Windows 10.0.26200`; logical CPU: 8; RAM: 8109.000 MB; available before run: 1569.000 MB; .NET: `.NET 8.0.20`
- Protocol: warmup 3; measured 10; concurrency 1; sequential scenario order; nearest-rank percentiles.
- Host admission: not used; heavy telemetry: not used; application command timeout override: none.

## 3. Scenario Results

| Scenario | Smoke | Success | Avg ms | P50 ms | P95 ms | Max ms | Errors | Verdict |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| MasterPaged | PASS | 10/10 | 2.246 | 1.851 | 3.840 | 3.840 | 0 | PASS |
| LookupPaged | PASS | 10/10 | 1.599 | 1.572 | 1.874 | 1.874 | 0 | PASS |
| DocumentPaged | PASS | 10/10 | 43.860 | 43.995 | 54.221 | 54.221 | 0 | PASS |
| DetailReportPaged | PASS | 10/10 | 149.705 | 140.640 | 195.466 | 195.466 | 0 | PASS |
| InventoryHistoricalReportPaged | SMOKE_FAIL | 0/10 | n/a | n/a | n/a | n/a | 1 | SMOKE_FAIL |
| InventoryCurrentBalancePaged | PASS | 10/10 | 21.511 | 21.060 | 24.181 | 24.181 | 0 | PASS |

## 4. Ranking

1. `DetailReportPaged` — Average 149.705 ms; P95 195.466 ms.
2. `DocumentPaged` — Average 43.860 ms; P95 54.221 ms.
3. `InventoryCurrentBalancePaged` — Average 21.511 ms; P95 24.181 ms.
4. `MasterPaged` — Average 2.246 ms; P95 3.840 ms.
5. `LookupPaged` — Average 1.599 ms; P95 1.874 ms.

## 5. 10M Capability

- `MasterPaged` = **PASS**
- `LookupPaged` = **PASS**
- `DocumentPaged` = **PASS**
- `DetailReportPaged` = **PASS**
- `InventoryHistoricalReportPaged` = **SMOKE_FAIL**
- `InventoryCurrentBalancePaged` = **PASS**
- `SOURCE_CURRENT_CAN_RUN_ON_10M_DB` = **PARTIAL**

## 6. Hotspots

- `DetailReportPaged` — Average 149.705 ms, P95 195.466 ms.
- `DocumentPaged` — Average 43.860 ms, P95 54.221 ms.
- `InventoryCurrentBalancePaged` — Average 21.511 ms, P95 24.181 ms.

## 7. Errors and scenario call details

### MasterPaged

- Application method: `CWarehouseMaster_Controller.List_Master_Page_Async`
- Stored procedure: `dbo.sp_DM_Master_Page`
- Parameters: `{"MasterType":"SanPham","PageNumber":1,"PageSize":10,"SearchText":""}`
- Errors: none.

### LookupPaged

- Application method: `CWarehouseMaster_Controller.List_Lookup_Page_Async`
- Stored procedure: `dbo.sp_DM_Lookup_Page`
- Parameters: `{"MasterType":"SanPham","PageNumber":1,"PageSize":10,"SearchText":""}`
- Errors: none.

### DocumentPaged

- Application method: `CWarehouseDocument_Controller.List_Documents_Page_Async`
- Stored procedure: `dbo.sp_XNK_Document_Page`
- Parameters: `{"IsReceipt":true,"PageNumber":1,"PageSize":10,"SearchText":"","Login":"PERF_USER","WarehouseId":null}`
- Errors: none.

### DetailReportPaged

- Application method: `CWarehouseReport_Controller.Detail_Report_Page_Async`
- Stored procedure: `dbo.sp_BC_Chi_Tiet_Nhap_Page`
- Parameters: `{"IsReceipt":true,"From":"2025-01-01","To":"2026-12-31","PageNumber":1,"PageSize":10,"Login":"PERF_USER","WarehouseId":null}`
- Errors: none.

### InventoryHistoricalReportPaged

- Application method: `CWarehouseReport_Controller.Inventory_Report_Page_Async`
- Stored procedure: `dbo.sp_BC_Xuat_Nhap_Ton_Page`
- Parameters: `{"From":"2025-01-01","To":"2026-12-31","PageNumber":1,"PageSize":10,"Login":"PERF_USER","WarehouseId":null,"ReadCurrentBalance":false}`
- SMOKE #1: SqlException; SQL=51222; timeout=False; Movement Aggregate của báo cáo đang tái tạo. Vui lòng thử lại sau khi worker hoàn tất.

### InventoryCurrentBalancePaged

- Application method: `CWarehouseReport_Controller.Inventory_Report_Page_Async`
- Stored procedure: `dbo.sp_BC_Ton_Kho_Hien_Tai_Page`
- Parameters: `{"PageNumber":1,"PageSize":10,"Login":"PERF_USER","WarehouseId":null,"ReadCurrentBalance":true}`
- Errors: none.

## 8. Limitations

- One machine, one process, concurrency 1, ten measured calls per scenario.
- Results represent this machine environment and do not guarantee production hardware performance.
- Read workloads only; no writes, concurrency stress, mixed workload, or heavy SQL telemetry.
- No performance SLA was defined; timings are reported as measured and are not labeled good or bad.
- P50/P95 use nearest-rank with only ten samples; P95 therefore selects the highest successful sample.
- Dataset scale uses SQL Server partition row-count metadata, not a full COUNT(*) scan or value-equality proof.

## 9. Run integrity

- RunId: `WHB-LITE-20261005T080006Z-4fe51c3e2a404126`; status: `COMPLETED`; BusinessDBWrites: **0**.
- Results: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11\benchmarks\lite\results\WHB-LITE-20261005T080006Z-4fe51c3e2a404126\benchmark-lite-results.json`
- Environment: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11\benchmarks\lite\results\WHB-LITE-20261005T080006Z-4fe51c3e2a404126\benchmark-lite-environment.json`
- Raw CSV: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11\benchmarks\lite\results\WHB-LITE-20261005T080006Z-4fe51c3e2a404126\benchmark-lite-raw.csv`
- Results represent this machine environment: **YES**; production hardware guarantee: **NO**.
