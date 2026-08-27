# TDD evidence: BenchmarkDotNet + NBomber harness

## RED

- Added `ToolBenchmarkHarnessTests` for environment parsing and the read-only scenario catalog.
- Added a RED test for snapshot report dates and runner wiring to `SnapshotBaseline.sql`.
- First compile gate failed with the intended missing `TKS_Thuc_Tap_V11_Benchmarks` namespace after the project scaffold was introduced.
- A separate runner-contract test was added for Release mode, isolated database naming/drop, and the NBomber entry point.

## GREEN

- `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~ToolBenchmarkHarnessTests|FullyQualifiedName~ToolBenchmarkRunnerTests"`
- Result: 5 passed, 0 failed.
- `dotnet build TKS_Thuc_Tap_V11_Benchmarks\TKS_Thuc_Tap_V11_Benchmarks.csproj --configuration Release --no-restore --verbosity minimal`
- Result: build succeeded, 0 errors, 0 warnings for the benchmark project.

## Runtime evidence

- BenchmarkDotNet smoke run completed and exported Markdown/CSV/HTML.
- BenchmarkDotNet 1M synthetic and database runs completed; the inventory operation was recorded as a workload issue because the application SQL client timed out after 30 seconds.
- NBomber completed both a full 1-copy run and an 8-copy selected-scenario run, exporting Markdown/CSV/HTML/TXT.
- Snapshot rerun completed with 83,650 valid snapshot rows, `ReportProcUsesSnapshot=1`, and `FallbackLogRows=0`; Inventory completed in BDN and NBomber instead of the previous BDN client timeout.

## Coverage note

The existing solution does not expose a coverage collector or coverage gate in this benchmark project. The tests cover harness configuration and orchestration contracts; they do not claim 80% production-code coverage. Production performance conclusions are based on the recorded BDN, NBomber, and SQL STATISTICS IO/TIME artifacts.
