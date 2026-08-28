# TKS warehouse performance benchmark report

- Generated: 2026-08-28 21:12:09 +07:00
- Host: DESKTOP-NHQ7QPL
- .NET SDK: 9.0.317
- Database: TKS_Thuc_Tap_V11_Perf_10000000
- Dataset target: 10000000 detail rows (5000000 receipt + 5000000 issue)
- NBomber load: 8 concurrent copies per scenario, 15 seconds
- Total concurrent read operations when all 5 scenarios are enabled: 40
- Snapshot path enabled: False
- Synthetic BenchmarkDotNet run skipped: True
- NBomber process exit code: 0 (request failures, if any, are recorded in the NBomber report)

## Dataset verification

~~~text
-----------------------------------------------------------------------------------------------------------------------------------------
Database=TKS_Thuc_Tap_V11_Perf_10000000                                                                                                  
                                           
-------------------------------------------
ReceiptLines=5000000                       
                                         
-----------------------------------------
IssueLines=5000000                       
                                              
----------------------------------------------
TotalDetailRows=10000000                      
                                       
---------------------------------------
Products=10000                         
                                             
---------------------------------------------
ReceiptHeaders=500000                        
                                           
-------------------------------------------
IssueHeaders=500000                        
                                                    
----------------------------------------------------
MovementAggregateRows=4323380                       
                              
------------------------------
MovementAggregateInitialized=1
~~~

Movement aggregation is bootstrapped once from the posted ledger before the
inventory-report benchmark. This setup operation is intentionally excluded from
BenchmarkDotNet and NBomber timings.

~~~text
Reused existing database; bootstrap was completed during database creation.
~~~

## Measured summary

### BenchmarkDotNet database path

| Scenario | Mean | Allocated |
|---|---:|---:|
| MasterPaged | 3.187 ms | 36.85 KB |
| LookupPaged | 2.430 ms | 25.83 KB |
| DocumentPaged | 57.748 ms | 50.03 KB |
| DetailReportPaged | 224.289 ms | 57.42 KB |
| InventoryReportPaged | 4,292.829 ms | 117.96 KB |

### NBomber concurrent path

| Scenario | Requests | OK | Failed | Mean OK (ms) | P95 OK (ms) | P99 OK (ms) |
|---|---:|---:|---:|---:|---:|---:|
| MasterPaged | 61970 | 61970 | 0 | 27.46 | 53.6 | 86.85 |
| LookupPaged | 94267 | 94267 | 0 | 31.57 | 71.1 | 109.57 |
| DocumentPaged | 2703 | 2703 | 0 | 914.97 | 1970.18 | 2437.12 |
| DetailReportPaged | 892 | 892 | 0 | 2545.47 | 6688.77 | 8269.82 |
| InventoryReportPaged | 71 | 18 | 53 | 26019.88 | 29343.74 | 29573.12 |

## Artifacts

- [BenchmarkDotNet synthetic results](benchmarkdotnet-synthetic/)
- [BenchmarkDotNet database results](benchmarkdotnet-database/)
- [NBomber HTML/CSV/Markdown/TXT results](nbomber/)
- [SQL Server statistics](warehouse-tool-benchmark.sqlstats.txt)
- [Dataset verification](dataset-verification.txt)
- [Movement aggregate bootstrap log](movement-aggregate-bootstrap.txt)
- [NBomber process status](nbomber-status.txt)

## Reading the result

- BenchmarkDotNet measures isolated single-operation latency and managed
  allocations for the application data-access path.
- NBomber measures concurrent read behavior across the five warehouse paging
  paths. Review latency percentiles, request rate, failures and scenario-level
  statistics in its generated report.
- SQL Server statistics provide the database-side IO/time evidence needed to
  distinguish query cost from C# materialization/reflection cost.

## Scope and limitations

- The load harness invokes the existing warehouse controllers directly. It
  measures the Data Access + SQL path, not browser rendering or the complete
  Blazor Server circuit/network path.
- Results describe this host, local SQL Server instance and 10M-row synthetic
  dataset; they are not a production SLA or a claim of production capacity.
- All benchmark scenarios are read-only. The harness does not post, mutate or
  delete warehouse business data.
