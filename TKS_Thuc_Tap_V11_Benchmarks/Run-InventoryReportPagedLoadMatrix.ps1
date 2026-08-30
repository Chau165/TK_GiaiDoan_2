[CmdletBinding()]
param(
    [ValidateRange(1, 10000000)]
    [int]$RecordCount = 10000000,
    [ValidateRange(1, 256)]
    [int[]]$WorkerCounts = @(1, 2, 4, 8, 16),
    [ValidateRange(1, 3600)]
    [int]$DurationSeconds = 15,
    [string]$DatabaseDirectory = 'P:\TKS_Thuc_Tap_V11_PerfData',
    [switch]$Reset,
    [switch]$ReuseDatabase,
    [switch]$DropAfter
)

<#
Runs only InventoryReportPaged against a dedicated 10M-row database. The
database data/log files and all generated artifacts are placed on P:. SQL
Server's existing tempdb remains a server-level resource and is observed, not
reconfigured, by this script.
#>

$ErrorActionPreference = 'Stop'
$scriptRoot = (Resolve-Path $PSScriptRoot).Path
$projectRoot = (Resolve-Path (Join-Path $scriptRoot '..')).Path
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$databaseName = "TKS_Thuc_Tap_V11_Perf_$RecordCount"
$reportRoot = Join-Path $projectRoot "docs\testing\performance\tool-benchmarks\inventory-only-$timestamp"
$benchmarkProject = Join-Path $scriptRoot 'TKS_Thuc_Tap_V11_Benchmarks.csproj'
$schemaFile = Join-Path $projectRoot 'Database\WarehouseModule.Schema.sql'
$proceduresFile = Join-Path $projectRoot 'Database\WarehouseModule.Procedures.sql'
$seedFile = Join-Path $projectRoot 'Database\Performance\WarehousePerformance.Seed.sql'
$sqlcmd = (Get-Command sqlcmd -ErrorAction Stop).Source
$connectionString = "Server=localhost;Database=$databaseName;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=30;"
$databaseDirectoryPath = [System.IO.Path]::GetFullPath($DatabaseDirectory)

if ($databaseName -notmatch '^TKS_Thuc_Tap_V11_Perf_[0-9]+$') {
    throw "Refusing an unsafe benchmark database name: $databaseName"
}
if ($WorkerCounts.Count -eq 0) {
    throw 'WorkerCounts must contain at least one value.'
}

New-Item -ItemType Directory -Force $databaseDirectoryPath, $reportRoot | Out-Null

function Invoke-Sql {
    param(
        [string]$Database,
        [string[]]$Arguments
    )

    & $sqlcmd -S localhost -E -C -d $Database -b -f 65001 @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed with exit code $LASTEXITCODE"
    }
}

function Get-DatabaseExists {
    $output = & $sqlcmd -S localhost -E -C -d master -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$databaseName') IS NULL THEN 0 ELSE 1 END;"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not check database $databaseName."
    }

    return (($output | Select-Object -First 1).ToString().Trim() -eq '1')
}

function Invoke-TelemetrySample {
    param([string]$OutputPath)

    $query = @"
SET NOCOUNT ON;
DECLARE @DbId INT = DB_ID(N'$databaseName');
DECLARE @ProcId INT = OBJECT_ID(N'dbo.sp_BC_Ton_Kho_Hien_Tai_Page');
SELECT
    CONVERT(char(33), SYSUTCDATETIME(), 126) AS TimestampUtc,
    COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_exec_requests r WHERE r.database_id = @DbId AND r.session_id <> @@SPID), 0) AS ActiveRequests,
    COALESCE((SELECT SUM(CONVERT(bigint, r.cpu_time)) FROM sys.dm_exec_requests r WHERE r.database_id = @DbId AND r.session_id <> @@SPID), 0) AS ActiveCpuMs,
    COALESCE((SELECT SUM(CONVERT(bigint, r.granted_query_memory)) * 8 FROM sys.dm_exec_requests r WHERE r.database_id = @DbId AND r.session_id <> @@SPID), 0) AS ActiveRequestGrantKB,
    COALESCE((SELECT SUM(CONVERT(bigint, g.requested_memory_kb)) FROM sys.dm_exec_query_memory_grants g JOIN sys.dm_exec_sessions s ON s.session_id = g.session_id WHERE s.database_id = @DbId AND g.session_id <> @@SPID), 0) AS RequestedGrantKB,
    COALESCE((SELECT SUM(CONVERT(bigint, g.granted_memory_kb)) FROM sys.dm_exec_query_memory_grants g JOIN sys.dm_exec_sessions s ON s.session_id = g.session_id WHERE s.database_id = @DbId AND g.session_id <> @@SPID), 0) AS GrantedGrantKB,
    COALESCE((SELECT MAX(CONVERT(bigint, g.granted_memory_kb)) FROM sys.dm_exec_query_memory_grants g JOIN sys.dm_exec_sessions s ON s.session_id = g.session_id WHERE s.database_id = @DbId AND g.session_id <> @@SPID), 0) AS PeakGrantKB,
    COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_exec_query_memory_grants g JOIN sys.dm_exec_sessions s ON s.session_id = g.session_id WHERE s.database_id = @DbId AND g.grant_time IS NULL AND g.session_id <> @@SPID), 0) AS WaitingMemoryGrants,
    COALESCE((SELECT COUNT_BIG(*) FROM sys.dm_os_waiting_tasks wt JOIN sys.dm_exec_sessions s ON s.session_id = wt.session_id WHERE s.database_id = @DbId AND wt.wait_type = N'RESOURCE_SEMAPHORE'), 0) AS ActiveResourceSemaphoreWaits,
    COALESCE((SELECT SUM(CONVERT(bigint, ps.execution_count)) FROM sys.dm_exec_procedure_stats ps WHERE ps.database_id = @DbId AND ps.object_id = @ProcId), 0) AS ProcExecutionCount,
    COALESCE((SELECT SUM(CONVERT(bigint, ps.total_worker_time)) FROM sys.dm_exec_procedure_stats ps WHERE ps.database_id = @DbId AND ps.object_id = @ProcId), 0) AS ProcWorkerTimeUs,
    COALESCE((SELECT SUM(CONVERT(bigint, ps.total_elapsed_time)) FROM sys.dm_exec_procedure_stats ps WHERE ps.database_id = @DbId AND ps.object_id = @ProcId), 0) AS ProcElapsedTimeUs,
    COALESCE((SELECT SUM(CONVERT(bigint, ps.total_logical_reads)) FROM sys.dm_exec_procedure_stats ps WHERE ps.database_id = @DbId AND ps.object_id = @ProcId), 0) AS ProcLogicalReads,
    COALESCE((SELECT SUM(CONVERT(bigint, fs.user_object_reserved_page_count + fs.internal_object_reserved_page_count + fs.version_store_reserved_page_count + fs.mixed_extent_page_count)) * 8 FROM tempdb.sys.dm_db_file_space_usage fs), 0) AS TempdbUsedKB,
    COALESCE((SELECT SUM(CONVERT(bigint, fs.user_object_reserved_page_count)) * 8 FROM tempdb.sys.dm_db_file_space_usage fs), 0) AS TempdbUserKB,
    COALESCE((SELECT SUM(CONVERT(bigint, fs.internal_object_reserved_page_count)) * 8 FROM tempdb.sys.dm_db_file_space_usage fs), 0) AS TempdbInternalKB,
    COALESCE((SELECT SUM(CONVERT(bigint, ws.waiting_tasks_count)) FROM sys.dm_os_wait_stats ws WHERE ws.wait_type = N'RESOURCE_SEMAPHORE'), 0) AS WaitResourceSemaphoreTasks,
    COALESCE((SELECT SUM(CONVERT(bigint, ws.wait_time_ms)) FROM sys.dm_os_wait_stats ws WHERE ws.wait_type = N'RESOURCE_SEMAPHORE'), 0) AS WaitResourceSemaphoreMs,
    COALESCE((SELECT SUM(CONVERT(bigint, ws.waiting_tasks_count)) FROM sys.dm_os_wait_stats ws WHERE ws.wait_type = N'CXPACKET'), 0) AS WaitCXPacketTasks,
    COALESCE((SELECT SUM(CONVERT(bigint, ws.wait_time_ms)) FROM sys.dm_os_wait_stats ws WHERE ws.wait_type = N'CXPACKET'), 0) AS WaitCXPacketMs,
    COALESCE((SELECT SUM(CONVERT(bigint, ws.waiting_tasks_count)) FROM sys.dm_os_wait_stats ws WHERE ws.wait_type = N'CXCONSUMER'), 0) AS WaitCXConsumerTasks,
    COALESCE((SELECT SUM(CONVERT(bigint, ws.wait_time_ms)) FROM sys.dm_os_wait_stats ws WHERE ws.wait_type = N'CXCONSUMER'), 0) AS WaitCXConsumerMs,
    COALESCE((SELECT SUM(CONVERT(bigint, ws.waiting_tasks_count)) FROM sys.dm_os_wait_stats ws WHERE ws.wait_type LIKE N'PAGEIOLATCH%'), 0) AS WaitPageIOLatchTasks,
    COALESCE((SELECT SUM(CONVERT(bigint, ws.wait_time_ms)) FROM sys.dm_os_wait_stats ws WHERE ws.wait_type LIKE N'PAGEIOLATCH%'), 0) AS WaitPageIOLatchMs;
"@

    $row = & $sqlcmd -S localhost -E -C -d $databaseName -h -1 -W -s ',' -Q $query
    if ($LASTEXITCODE -ne 0) {
        throw "Telemetry query failed with exit code $LASTEXITCODE"
    }

    $line = ($row | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1).Trim()
    if (-not [string]::IsNullOrWhiteSpace($line)) {
        Add-Content -LiteralPath $OutputPath -Value $line -Encoding UTF8
    }
}

function Get-Delta {
    param([long]$Before, [long]$After)
    if ($After -ge $Before) { return $After - $Before }
    return $After
}

function Get-TelemetrySummary {
    param([string]$TelemetryPath)

    $rows = @(Import-Csv -LiteralPath $TelemetryPath)
    if ($rows.Count -lt 2) {
        throw "Telemetry did not produce a baseline and final sample: $TelemetryPath"
    }

    $first = $rows[0]
    $last = $rows[$rows.Count - 1]
    $maxActiveRequests = ($rows | Measure-Object -Property ActiveRequests -Maximum).Maximum
    $maxPeakGrantKB = ($rows | Measure-Object -Property PeakGrantKB -Maximum).Maximum
    $maxGrantedGrantKB = ($rows | Measure-Object -Property GrantedGrantKB -Maximum).Maximum
    $maxWaitingMemoryGrants = ($rows | Measure-Object -Property WaitingMemoryGrants -Maximum).Maximum
    $maxResourceSemaphoreWaits = ($rows | Measure-Object -Property ActiveResourceSemaphoreWaits -Maximum).Maximum
    $maxTempdbUsedKB = ($rows | Measure-Object -Property TempdbUsedKB -Maximum).Maximum
    $maxTempdbInternalKB = ($rows | Measure-Object -Property TempdbInternalKB -Maximum).Maximum

    return [pscustomobject]@{
        TelemetrySamples = $rows.Count
        PeakActiveRequests = $maxActiveRequests
        PeakSingleGrantMB = [math]::Round(([double]$maxPeakGrantKB) / 1024, 2)
        PeakGrantedMemoryMB = [math]::Round(([double]$maxGrantedGrantKB) / 1024, 2)
        PeakWaitingMemoryGrants = $maxWaitingMemoryGrants
        PeakActiveResourceSemaphoreWaits = $maxResourceSemaphoreWaits
        PeakTempdbUsedMB = [math]::Round(([double]$maxTempdbUsedKB) / 1024, 2)
        PeakTempdbInternalMB = [math]::Round(([double]$maxTempdbInternalKB) / 1024, 2)
        TempdbUsedDeltaMB = [math]::Round((([double]$last.TempdbUsedKB - [double]$first.TempdbUsedKB) / 1024), 2)
        SqlProcedureExecutions = Get-Delta ([long]$first.ProcExecutionCount) ([long]$last.ProcExecutionCount)
        SqlProcedureCpuMs = [math]::Round((Get-Delta ([long]$first.ProcWorkerTimeUs) ([long]$last.ProcWorkerTimeUs)) / 1000, 2)
        SqlProcedureElapsedMs = [math]::Round((Get-Delta ([long]$first.ProcElapsedTimeUs) ([long]$last.ProcElapsedTimeUs)) / 1000, 2)
        SqlProcedureLogicalReads = Get-Delta ([long]$first.ProcLogicalReads) ([long]$last.ProcLogicalReads)
        ResourceSemaphoreTasks = Get-Delta ([long]$first.WaitResourceSemaphoreTasks) ([long]$last.WaitResourceSemaphoreTasks)
        ResourceSemaphoreMs = Get-Delta ([long]$first.WaitResourceSemaphoreMs) ([long]$last.WaitResourceSemaphoreMs)
        CXPacketTasks = Get-Delta ([long]$first.WaitCXPacketTasks) ([long]$last.WaitCXPacketTasks)
        CXPacketMs = Get-Delta ([long]$first.WaitCXPacketMs) ([long]$last.WaitCXPacketMs)
        CXConsumerTasks = Get-Delta ([long]$first.WaitCXConsumerTasks) ([long]$last.WaitCXConsumerTasks)
        CXConsumerMs = Get-Delta ([long]$first.WaitCXConsumerMs) ([long]$last.WaitCXConsumerMs)
        PageIOLatchTasks = Get-Delta ([long]$first.WaitPageIOLatchTasks) ([long]$last.WaitPageIOLatchTasks)
        PageIOLatchMs = Get-Delta ([long]$first.WaitPageIOLatchMs) ([long]$last.WaitPageIOLatchMs)
    }
}

function Get-NBomberSummary {
    param([string]$NbomberDirectory)

    $csv = Get-ChildItem -LiteralPath $NbomberDirectory -Filter 'warehouse-nbomber.csv' -File | Select-Object -First 1
    if ($null -eq $csv) {
        throw "NBomber CSV was not produced in $NbomberDirectory"
    }

    $result = Import-Csv -LiteralPath $csv.FullName | Where-Object { $_.scenario -eq 'InventoryReportPaged' } | Select-Object -First 1
    if ($null -eq $result) {
        throw "NBomber result did not contain InventoryReportPaged in $($csv.FullName)"
    }

    return $result
}

$existingDatabase = Get-DatabaseExists
if ($Reset -and $ReuseDatabase) {
    throw 'Reset and ReuseDatabase cannot be used together.'
}
if ($ReuseDatabase -and -not $existingDatabase) {
    throw "$databaseName does not exist, so ReuseDatabase cannot be used."
}
if ($existingDatabase -and -not $Reset -and -not $ReuseDatabase) {
    throw "$databaseName already exists. Use -Reset or -ReuseDatabase only for this isolated benchmark database."
}

try {
    if (-not $ReuseDatabase) {
        if ($existingDatabase) {
            Invoke-Sql -Database 'master' -Arguments @('-Q', "ALTER DATABASE [$databaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$databaseName];")
        }

        $dataFile = Join-Path $databaseDirectoryPath "$databaseName.mdf"
        $logFile = Join-Path $databaseDirectoryPath "${databaseName}_log.ldf"
        $createQuery = "CREATE DATABASE [$databaseName] ON PRIMARY (NAME = N'$databaseName', FILENAME = N'$($dataFile.Replace("'", "''"))', SIZE = 64MB, FILEGROWTH = 256MB) LOG ON (NAME = N'${databaseName}_log', FILENAME = N'$($logFile.Replace("'", "''"))', SIZE = 128MB, FILEGROWTH = 256MB);"
        Invoke-Sql -Database 'master' -Arguments @('-Q', $createQuery)
    }

    # Reapply idempotent database contracts even when reusing the dedicated
    # benchmark database, so measurements match the checked-out source.
    Invoke-Sql -Database $databaseName -Arguments @('-i', $schemaFile)
    Invoke-Sql -Database $databaseName -Arguments @('-i', $proceduresFile)
    if (-not $ReuseDatabase) {
        Invoke-Sql -Database $databaseName -Arguments @('-v', "RecordCount=$RecordCount", '-i', $seedFile)
        # The seed inserts an already-posted historical ledger directly, so it
        # must materialize the same current-balance read model that normal Post
        # updates transactionally.
        Invoke-Sql -Database $databaseName -Arguments @('-Q', 'EXEC dbo.sp_XNK_InventoryBalance_Rebuild;')
        Invoke-Sql -Database $databaseName -Arguments @('-Q', 'EXEC dbo.sp_Inventory_Movement_Bootstrap_From_Ledger;')
    }

    $sourceIdentityPath = Join-Path $reportRoot 'source-and-deployment-verification.txt'
    $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $proceduresFile).Hash
    $sourceRevision = (git -C $projectRoot rev-parse HEAD).Trim()
    $definitionVerification = @"
SET NOCOUNT ON;
SELECT 'Database=' + DB_NAME();
SELECT 'ProcedureLength=' + CONVERT(varchar(30), LEN(OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_BC_Ton_Kho_Hien_Tai_Page'))));
SELECT 'UsesCurrentBalance=' + CASE WHEN OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_BC_Ton_Kho_Hien_Tai_Page')) LIKE N'%InventoryBalance_Current%' THEN '1' ELSE '0' END;
SELECT 'UsesMovementAggregate=' + CASE WHEN OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_BC_Ton_Kho_Hien_Tai_Page')) LIKE N'%#MovementAggregate%' THEN '1' ELSE '0' END;
SELECT 'UsesPagedFetch=' + CASE WHEN OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_BC_Ton_Kho_Hien_Tai_Page')) LIKE N'%OFFSET (%' THEN '1' ELSE '0' END;
SELECT 'AggregateInitialized=' + CONVERT(varchar(1), IsInitialized) FROM dbo.InventoryMovement_AggregateState WHERE State_ID = 1;
"@
    @("GitRevision=$sourceRevision", "WarehouseModule.Procedures.sql SHA256=$sourceHash") | Set-Content -LiteralPath $sourceIdentityPath -Encoding UTF8
    & $sqlcmd -S localhost -E -C -d $databaseName -b -f 65001 -Q $definitionVerification | Add-Content -LiteralPath $sourceIdentityPath -Encoding UTF8
    if ($LASTEXITCODE -ne 0) { throw 'Source/deployment verification query failed.' }

    $env:TKS_PERF_ROWS = $RecordCount.ToString()
    $env:TKS_PERF_PAGE_SIZE = '10'
    $env:TKS_PERF_LOGIN = 'PERF_USER'
    $env:TKS_PERF_CONNECTION_STRING = $connectionString
    $env:TKS_PERF_FROM_DATE = '2025-01-01'
    $env:TKS_PERF_TO_DATE = (Get-Date).ToString('yyyy-MM-dd')
    $env:TKS_PERF_USE_CURRENT_BALANCE = '1'
    $env:TKS_NBOMBER_SCENARIOS = 'InventoryReportPaged'

    & dotnet build $benchmarkProject --configuration Release --no-restore -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

    $bdnDirectory = Join-Path $reportRoot 'benchmarkdotnet-inventory'
    $env:TKS_BDN_DATABASE = '1'
    Push-Location $scriptRoot
    try {
        & dotnet run --project $benchmarkProject --configuration Release --no-build -- --job short --filter '*InventoryReportPaged*' --artifacts $bdnDirectory
    }
    finally {
        Pop-Location
    }
    if ($LASTEXITCODE -ne 0) { throw 'BenchmarkDotNet InventoryReportPaged run failed.' }
    $bdnCsv = Get-ChildItem -LiteralPath (Join-Path $bdnDirectory 'results') -Filter '*report.csv' -File | Select-Object -First 1
    if ($null -eq $bdnCsv -or (Select-String -LiteralPath $bdnCsv.FullName -SimpleMatch ',NA,NA' -Quiet)) {
        throw 'BenchmarkDotNet did not produce a valid InventoryReportPaged measurement.'
    }

    $summaryRows = @()
    foreach ($workers in $WorkerCounts) {
        $runDirectory = Join-Path $reportRoot "nbomber-workers-$workers"
        $telemetryPath = Join-Path $runDirectory 'sql-resource-telemetry.csv'
        $stdoutPath = Join-Path $runDirectory 'nbomber-stdout.txt'
        $stderrPath = Join-Path $runDirectory 'nbomber-stderr.txt'
        New-Item -ItemType Directory -Force $runDirectory | Out-Null
        'TimestampUtc,ActiveRequests,ActiveCpuMs,ActiveRequestGrantKB,RequestedGrantKB,GrantedGrantKB,PeakGrantKB,WaitingMemoryGrants,ActiveResourceSemaphoreWaits,ProcExecutionCount,ProcWorkerTimeUs,ProcElapsedTimeUs,ProcLogicalReads,TempdbUsedKB,TempdbUserKB,TempdbInternalKB,WaitResourceSemaphoreTasks,WaitResourceSemaphoreMs,WaitCXPacketTasks,WaitCXPacketMs,WaitCXConsumerTasks,WaitCXConsumerMs,WaitPageIOLatchTasks,WaitPageIOLatchMs' | Set-Content -LiteralPath $telemetryPath -Encoding UTF8

        $env:TKS_BDN_DATABASE = '0'
        $env:TKS_NBOMBER_COPIES = $workers.ToString()
        $env:TKS_NBOMBER_DURATION_SECONDS = $DurationSeconds.ToString()
        $env:TKS_BENCH_REPORT_DIR = $runDirectory

        Invoke-TelemetrySample -OutputPath $telemetryPath
        $dotnetArguments = "run --project `"$benchmarkProject`" --configuration Release --no-build -- --nbomber"
        $process = Start-Process -FilePath 'dotnet' -ArgumentList $dotnetArguments -WorkingDirectory $scriptRoot -NoNewWindow -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
        while (-not $process.HasExited) {
            Start-Sleep -Milliseconds 500
            Invoke-TelemetrySample -OutputPath $telemetryPath
        }
        $process.WaitForExit()
        Invoke-TelemetrySample -OutputPath $telemetryPath

        $telemetry = Get-TelemetrySummary -TelemetryPath $telemetryPath
        $nbomber = Get-NBomberSummary -NbomberDirectory $runDirectory
        $summaryRows += [pscustomobject]@{
            Workers = $workers
            DurationSeconds = $DurationSeconds
            Requests = [long]$nbomber.request_count
            Ok = [long]$nbomber.ok
            Failed = [long]$nbomber.failed
            OkMeanMs = [double]$nbomber.ok_mean
            OkP95Ms = [double]$nbomber.ok_95_percent
            OkP99Ms = [double]$nbomber.ok_99_percent
            NbomberProcessExitCode = $process.ExitCode
            TelemetrySamples = $telemetry.TelemetrySamples
            PeakActiveRequests = $telemetry.PeakActiveRequests
            PeakSingleGrantMB = $telemetry.PeakSingleGrantMB
            PeakGrantedMemoryMB = $telemetry.PeakGrantedMemoryMB
            PeakWaitingMemoryGrants = $telemetry.PeakWaitingMemoryGrants
            PeakActiveResourceSemaphoreWaits = $telemetry.PeakActiveResourceSemaphoreWaits
            PeakTempdbUsedMB = $telemetry.PeakTempdbUsedMB
            PeakTempdbInternalMB = $telemetry.PeakTempdbInternalMB
            TempdbUsedDeltaMB = $telemetry.TempdbUsedDeltaMB
            SqlProcedureExecutions = $telemetry.SqlProcedureExecutions
            SqlProcedureCpuMs = $telemetry.SqlProcedureCpuMs
            SqlProcedureElapsedMs = $telemetry.SqlProcedureElapsedMs
            SqlProcedureLogicalReads = $telemetry.SqlProcedureLogicalReads
            ResourceSemaphoreTasks = $telemetry.ResourceSemaphoreTasks
            ResourceSemaphoreMs = $telemetry.ResourceSemaphoreMs
            CXPacketTasks = $telemetry.CXPacketTasks
            CXPacketMs = $telemetry.CXPacketMs
            CXConsumerTasks = $telemetry.CXConsumerTasks
            CXConsumerMs = $telemetry.CXConsumerMs
            PageIOLatchTasks = $telemetry.PageIOLatchTasks
            PageIOLatchMs = $telemetry.PageIOLatchMs
        }
    }

    $summaryPath = Join-Path $reportRoot 'inventory-load-matrix.csv'
    $summaryRows | Export-Csv -LiteralPath $summaryPath -NoTypeInformation -Encoding UTF8
    $bdnCsv = Get-ChildItem -LiteralPath (Join-Path $bdnDirectory 'results') -Filter '*.csv' -File | Select-Object -First 1
    $bdnMarkdown = 'BenchmarkDotNet result CSV was not produced.'
    if ($null -ne $bdnCsv) {
        $bdn = Import-Csv -LiteralPath $bdnCsv.FullName | Where-Object { $_.Method -eq 'InventoryReportPaged' } | Select-Object -First 1
        if ($null -ne $bdn) {
            $bdnMarkdown = "| Method | Mean | Allocated |`n|---|---:|---:|`n| InventoryReportPaged | $($bdn.Mean) | $($bdn.Allocated) |"
        }
    }
    $matrixMarkdown = @(
        '| Workers | Requests | OK | Failed | Mean OK ms | P95 OK ms | SQL CPU ms | SQL elapsed ms | Peak grant MB | TempDB peak MB | RESOURCE_SEMAPHORE ms | CXPACKET ms | CXCONSUMER ms | PAGEIOLATCH ms |'
        '|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|'
        ($summaryRows | ForEach-Object { "| $($_.Workers) | $($_.Requests) | $($_.Ok) | $($_.Failed) | $($_.OkMeanMs) | $($_.OkP95Ms) | $($_.SqlProcedureCpuMs) | $($_.SqlProcedureElapsedMs) | $($_.PeakGrantedMemoryMB) | $($_.PeakTempdbUsedMB) | $($_.ResourceSemaphoreMs) | $($_.CXPacketMs) | $($_.CXConsumerMs) | $($_.PageIOLatchMs) |" })
    ) -join [Environment]::NewLine
    $databaseLifecycle = if ($ReuseDatabase) { 'The dedicated benchmark database was reused; idempotent schema and procedure contracts were redeployed from the current source before measurement.' } else { 'The dedicated benchmark database was freshly created from the current source before measurement.' }
    $workerArtifacts = ($WorkerCounts | ForEach-Object { '`nbomber-workers-' + $_ + '/`' }) -join ', '
    $reportPath = Join-Path $reportRoot 'performance-report.md'
    @"
# InventoryReportPaged isolated load matrix

- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')
- Source revision: $sourceRevision
- Database: $databaseName
- Data and log directory: $databaseDirectoryPath
- Data target: $RecordCount detail rows
- Scope: `InventoryReportPaged` only; no other NBomber scenarios are registered.
- Read path: `sp_BC_Ton_Kho_Hien_Tai_Page` reads the materialized current balance; page size 10; PERF_USER is authorized for all benchmark warehouses.
- NBomber duration: $DurationSeconds seconds for each worker level.

## Source and deployment verification

$databaseLifecycle The procedure source contains one current definition, and [the verification output](source-and-deployment-verification.txt) records the deployed procedure markers and movement-aggregate initialization.

## BenchmarkDotNet single-operation baseline

$bdnMarkdown

## NBomber isolated concurrency results

$matrixMarkdown

## Telemetry interpretation

- SQL CPU/elapsed/logical reads are deltas from `sys.dm_exec_procedure_stats` for `sp_BC_Ton_Kho_Hien_Tai_Page` during each NBomber run.
- Memory grant and TempDB fields are peaks from 500 ms DMV samples. The TempDB values are server-wide because temporary tables are shared by SQL Server.
- Wait deltas are server-wide `sys.dm_os_wait_stats` deltas taken around each run. They may include unrelated local SQL activity; active request and memory-grant samples are restricted to the benchmark database.

## Artifacts

- [Matrix CSV](inventory-load-matrix.csv)
- [Source/deployment verification](source-and-deployment-verification.txt)
- [BenchmarkDotNet artifacts](benchmarkdotnet-inventory/)
- Per-worker NBomber reports and resource telemetry: $workerArtifacts.
"@ | Set-Content -LiteralPath $reportPath -Encoding UTF8

    Write-Output "Benchmark report: $reportPath"
}
finally {
    if ($DropAfter -and (Get-DatabaseExists)) {
        Invoke-Sql -Database 'master' -Arguments @('-Q', "ALTER DATABASE [$databaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$databaseName];")
    }
}
