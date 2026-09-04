# TKS warehouse performance benchmark report

- Generated: 2026-08-30 14:39:43 +07:00
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
MovementAggregateRows=730000                        
                              
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
| MasterPaged | 1.828 ms | 36.76 KB |
| LookupPaged | 3.496 ms | 25.92 KB |
| DocumentPaged | NA | NA |
| DetailReportPaged | NA | NA |
| InventoryReportPaged | NA | NA |

### NBomber concurrent path

| Scenario | Requests | OK | Failed | Mean OK (ms) | P95 OK (ms) | P99 OK (ms) |
|---|---:|---:|---:|---:|---:|---:|
| MasterPaged | 77350 | 77350 | 0 | 15.49 | 43.46 | 94.53 |
| LookupPaged | 57131 | 57131 | 0 | 12.82 | 36.61 | 82.75 |
| DocumentPaged | 63 | 51 | 12 | 5187.29 | 12918.78 | 13148.16 |
| DetailReportPaged | 29 | 17 | 12 | 12940.39 | 16809.98 | 29605.89 |
| InventoryReportPaged | 92 | 53 | 39 | 7936.1 | 13697.02 | 23691.26 |

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
