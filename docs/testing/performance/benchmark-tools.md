# BenchmarkDotNet + NBomber cho TKS

Project `TKS_Thuc_Tap_V11_Benchmarks` dùng BenchmarkDotNet `0.15.8` và NBomber `6.6.0` để đo hai lớp khác nhau:

- BenchmarkDotNet đo ổn định trong Release: materialization `DataTable`, reflection mapper `CUtility.Map_Row_To_Entity`, và từng paged warehouse controller call.
- NBomber đo workload đọc đồng thời trên database: `MasterPaged`, `LookupPaged`, `DocumentPaged`, `DetailReportPaged`, `InventoryReportPaged`. Report có HTML, Markdown, CSV và TXT.

Không đưa CRUD vào NBomber mặc định vì load test lặp lại có side effect. Full-load cũng không chạy mặc định; chỉ nên bật trong database cô lập.

## Chạy với database 1M rows

Từ solution root:

```powershell
& .\TKS_Thuc_Tap_V11_Benchmarks\Run-WarehouseToolBenchmarks.ps1 `
    -RecordCount 1000000 `
    -Copies 8 `
    -DurationSeconds 15 `
    -DatabaseDirectory 'P:\TKS_Performance_Data'
```

Script tạo database `TKS_Thuc_Tap_V11_Perf_1000000`, chạy schema/procedures/seed hiện có, chạy BDN + NBomber, thu `STATISTICS IO/TIME`, rồi xóa database. Dùng `-KeepDatabase` để giữ lại database phục vụ lần đo tiếp theo; dùng `-Reset` chỉ với database benchmark tên trên.

Để benchmark đúng kiến trúc tồn kho snapshot, dùng snapshot ngày `2025-12-31`. Runner sẽ tạo baseline từ movement đã post, đặt kỳ report bắt đầu từ ngày kế tiếp (`2026-01-01`) và ghi verification số dòng snapshot/fallback:

```powershell
& .\TKS_Thuc_Tap_V11_Benchmarks\Run-WarehouseToolBenchmarks.ps1 `
    -RecordCount 1000000 `
    -Copies 8 `
    -DurationSeconds 10 `
    -UseSnapshot `
    -SnapshotDate '2025-12-31' `
    -ReportToDate '2026-12-31' `
    -DatabaseDirectory 'P:\TKS_Performance_Data'
```

Không chọn kỳ bắt đầu trước snapshot khi kiểm tra snapshot path: procedure chỉ dùng snapshot có `Snapshot_Date < @Tu_Ngay`.

Artifacts nằm trong `docs/testing/performance/tool-benchmarks/<timestamp>/`:

- `benchmarkdotnet-synthetic/`
- `benchmarkdotnet-database/`
- `nbomber/`
- `warehouse-tool-benchmark.sqlstats.txt`

## Chạy riêng

Synthetic BDN:

```powershell
$env:TKS_PERF_ROWS = '100000'
dotnet run --project .\TKS_Thuc_Tap_V11_Benchmarks\TKS_Thuc_Tap_V11_Benchmarks.csproj -c Release -- --job short --filter '*WarehouseSyntheticBenchmarks*'
```

Database BDN cần `TKS_PERF_CONNECTION_STRING` và `TKS_BDN_DATABASE=1`:

```powershell
$env:TKS_BDN_DATABASE = '1'
$env:TKS_PERF_CONNECTION_STRING = 'Server=localhost;Database=TKS_Thuc_Tap_V11_Perf_1000000;Integrated Security=True;TrustServerCertificate=True;'
dotnet run --project .\TKS_Thuc_Tap_V11_Benchmarks\TKS_Thuc_Tap_V11_Benchmarks.csproj -c Release -- --job short --filter '*WarehouseDatabaseBenchmarks*'
```

Khi chạy riêng database benchmark với snapshot, đặt thêm ngày report:

```powershell
$env:TKS_PERF_FROM_DATE = '2026-01-01'
$env:TKS_PERF_TO_DATE = '2026-12-31'
```

NBomber cần `TKS_PERF_CONNECTION_STRING`:

```powershell
$env:TKS_NBOMBER_COPIES = '8'
$env:TKS_NBOMBER_DURATION_SECONDS = '15'
dotnet run --project .\TKS_Thuc_Tap_V11_Benchmarks\TKS_Thuc_Tap_V11_Benchmarks.csproj -c Release -- --nbomber
```

Có thể chọn một nhóm scenario bằng tên phân tách dấu phẩy để tách contention:

```powershell
$env:TKS_NBOMBER_SCENARIOS = 'MasterPaged,LookupPaged,DocumentPaged'
```

Nếu bỏ biến này, NBomber chạy cả 5 read scenario. `TKS_NBOMBER_SCENARIOS` chỉ nhận: `MasterPaged`, `LookupPaged`, `DocumentPaged`, `DetailReportPaged`, `InventoryReportPaged`.

Đọc p50/p95/p99, mean, max, RPS và fail count của NBomber cùng với Mean/Allocated của BDN; không suy luận bottleneck chỉ từ một metric. SQL stats dùng để phân biệt thời gian nằm ở SQL/I/O hay ở `DataSet`/reflection mapping. Benchmark này không thay thế production-sized benchmark hoặc browser E2E.
