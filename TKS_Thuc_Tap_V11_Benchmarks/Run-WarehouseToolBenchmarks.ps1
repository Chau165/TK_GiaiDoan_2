[CmdletBinding()]
param(
    [ValidateRange(1, 10000000)]
    [int]$RecordCount = 1000000,
    [ValidateRange(1, 256)]
    [int]$Copies = 8,
    [ValidateRange(1, 3600)]
    [int]$DurationSeconds = 15,
    [switch]$KeepDatabase,
    [switch]$Reset,
    [switch]$UseSnapshot,
    [ValidatePattern('^\d{4}-\d{2}-\d{2}$')]
    [string]$SnapshotDate = '2025-12-31',
    [ValidatePattern('^\d{4}-\d{2}-\d{2}$')]
    [string]$ReportToDate = '2026-12-31',
    [string]$DatabaseDirectory = ''
)

$ErrorActionPreference = 'Stop'
$scriptRoot = (Resolve-Path $PSScriptRoot).Path
$projectRoot = (Resolve-Path (Join-Path $scriptRoot '..')).Path
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$databaseName = "TKS_Thuc_Tap_V11_Perf_$RecordCount"
$reportRoot = Join-Path $projectRoot "docs\testing\performance\tool-benchmarks\$timestamp"
$benchmarkProject = Join-Path $scriptRoot 'TKS_Thuc_Tap_V11_Benchmarks.csproj'
$schemaFile = Join-Path $projectRoot 'Database\WarehouseModule.Schema.sql'
$proceduresFile = Join-Path $projectRoot 'Database\WarehouseModule.Procedures.sql'
$seedFile = Join-Path $projectRoot 'Database\Performance\WarehousePerformance.Seed.sql'
$sqlStatsFile = Join-Path $projectRoot 'Database\Performance\WarehousePerformance.SqlStats.sql'
$snapshotBaselineFile = Join-Path $projectRoot 'Database\Performance\WarehousePerformance.SnapshotBaseline.sql'
$snapshotSqlStatsFile = Join-Path $projectRoot 'Database\Performance\WarehousePerformance.SnapshotSqlStats.sql'
$sqlStatsPath = Join-Path $reportRoot 'warehouse-tool-benchmark.sqlstats.txt'
$snapshotBeforePath = Join-Path $reportRoot 'snapshot-verification-before.txt'
$snapshotAfterPath = Join-Path $reportRoot 'snapshot-verification-after.txt'
$connectionString = "Server=localhost;Database=$databaseName;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=30;"
$sqlcmd = (Get-Command sqlcmd -ErrorAction Stop).Source

try {
    $snapshotDateValue = [DateTime]::ParseExact($SnapshotDate, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
    $reportToDateValue = [DateTime]::ParseExact($ReportToDate, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
}
catch {
    throw 'SnapshotDate and ReportToDate must be valid dates in yyyy-MM-dd format.'
}

if ($UseSnapshot -and $reportToDateValue -le $snapshotDateValue) {
    throw 'ReportToDate must be after SnapshotDate when -UseSnapshot is specified.'
}

$snapshotFromDate = $snapshotDateValue.AddDays(1).ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)

if ($databaseName -notmatch '^TKS_Thuc_Tap_V11_Perf_[0-9]+$') {
    throw "Refusing an unsafe performance database name: $databaseName"
}

New-Item -ItemType Directory -Force $reportRoot | Out-Null
$bdnSyntheticDirectory = Join-Path $reportRoot 'benchmarkdotnet-synthetic'
$bdnDatabaseDirectory = Join-Path $reportRoot 'benchmarkdotnet-database'
$nbomberDirectory = Join-Path $reportRoot 'nbomber'

function Invoke-Sql {
    param([string[]]$Arguments)

    & $sqlcmd @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed with exit code $LASTEXITCODE"
    }
}

function Set-ProcessEnvironment {
    param([string]$Name, [string]$Value)

    [Environment]::SetEnvironmentVariable($Name, $Value, 'Process')
}

function Write-SnapshotVerification {
    param([string]$OutputPath)

    $verificationQuery = @"
SET NOCOUNT ON;
SELECT 'SnapshotRows=' + CONVERT(varchar(30), COUNT_BIG(*))
FROM dbo.InventoryBalance_Snapshot_Daily
WHERE Snapshot_Date = CONVERT(date, '$SnapshotDate', 23);
SELECT 'SnapshotValidRows=' + CONVERT(varchar(30), COUNT_BIG(*))
FROM dbo.InventoryBalance_Snapshot_Daily
WHERE Snapshot_Date = CONVERT(date, '$SnapshotDate', 23) AND IsValid = 1;
SELECT 'SnapshotMinDate=' + COALESCE(CONVERT(varchar(10), MIN(Snapshot_Date), 23), 'NULL')
FROM dbo.InventoryBalance_Snapshot_Daily
WHERE IsValid = 1;
SELECT 'SnapshotMaxDate=' + COALESCE(CONVERT(varchar(10), MAX(Snapshot_Date), 23), 'NULL')
FROM dbo.InventoryBalance_Snapshot_Daily
WHERE IsValid = 1;
SELECT 'FallbackLogRows=' + CONVERT(varchar(30), COUNT_BIG(*))
FROM dbo.InventorySnapshot_ReportFallbackLog;
SELECT 'ReportProcUsesSnapshot=' + CASE
    WHEN OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_BC_Xuat_Nhap_Ton_Page')) LIKE N'%InventoryBalance_Snapshot_Daily%'
    THEN '1' ELSE '0' END;
"@

    Invoke-Sql @('-S', 'localhost', '-E', '-C', '-d', $databaseName, '-b', '-f', '65001', '-Q', $verificationQuery, '-o', $OutputPath)
}

$environmentNames = @(
    'TKS_PERF_ROWS',
    'TKS_PERF_PAGE_SIZE',
    'TKS_PERF_LOGIN',
    'TKS_PERF_CONNECTION_STRING',
    'TKS_NBOMBER_COPIES',
    'TKS_NBOMBER_DURATION_SECONDS',
    'TKS_NBOMBER_SCENARIOS',
    'TKS_BENCH_REPORT_DIR',
    'TKS_BDN_DATABASE',
    'TKS_PERF_FROM_DATE',
    'TKS_PERF_TO_DATE'
)
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

$databaseExists = $false
$existsOutput = & $sqlcmd -S localhost -E -C -d master -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$databaseName') IS NULL THEN 0 ELSE 1 END;"
if ($LASTEXITCODE -ne 0) {
    throw "Could not check whether $databaseName exists."
}
$databaseExists = (($existsOutput | Select-Object -First 1).ToString().Trim() -eq '1')
if ($databaseExists -and -not $Reset) {
    throw "$databaseName already exists. Use -Reset only for this isolated benchmark database."
}

$databaseDirectoryPath = $null
if (-not [string]::IsNullOrWhiteSpace($DatabaseDirectory)) {
    $databaseDirectoryPath = [System.IO.Path]::GetFullPath($DatabaseDirectory)
    [System.IO.Directory]::CreateDirectory($databaseDirectoryPath) | Out-Null
}

try {
    if ($databaseExists -and $Reset) {
        Invoke-Sql @('-S', 'localhost', '-E', '-C', '-d', 'master', '-b', '-Q', "ALTER DATABASE [$databaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$databaseName];")
    }

    $createDatabaseQuery = "CREATE DATABASE [$databaseName];"
    if ($null -ne $databaseDirectoryPath) {
        $dataFilePath = [System.IO.Path]::Combine($databaseDirectoryPath, "$databaseName.mdf")
        $logFilePath = [System.IO.Path]::Combine($databaseDirectoryPath, "${databaseName}_log.ldf")
        $escapedDataFilePath = $dataFilePath.Replace("'", "''")
        $escapedLogFilePath = $logFilePath.Replace("'", "''")
        $createDatabaseQuery = "CREATE DATABASE [$databaseName] ON PRIMARY (NAME = N'$databaseName', FILENAME = N'$escapedDataFilePath', SIZE = 64MB, FILEGROWTH = 256MB) LOG ON (NAME = N'${databaseName}_log', FILENAME = N'$escapedLogFilePath', SIZE = 128MB, FILEGROWTH = 256MB);"
    }

    Invoke-Sql @('-S', 'localhost', '-E', '-C', '-d', 'master', '-b', '-Q', $createDatabaseQuery)
    Invoke-Sql @('-S', 'localhost', '-E', '-C', '-d', $databaseName, '-b', '-f', '65001', '-i', $schemaFile)
    Invoke-Sql @('-S', 'localhost', '-E', '-C', '-d', $databaseName, '-b', '-f', '65001', '-i', $proceduresFile)
    Invoke-Sql @('-S', 'localhost', '-E', '-C', '-d', $databaseName, '-b', '-f', '65001', '-v', "RecordCount=$RecordCount", '-i', $seedFile)

    if ($UseSnapshot) {
        if (-not (Test-Path -LiteralPath $snapshotBaselineFile)) {
            throw "Snapshot baseline script was not found: $snapshotBaselineFile"
        }

        Set-ProcessEnvironment 'TKS_PERF_FROM_DATE' $snapshotFromDate
        Set-ProcessEnvironment 'TKS_PERF_TO_DATE' $ReportToDate
        Set-ProcessEnvironment 'TKS_NBOMBER_SCENARIOS' $null
        Invoke-Sql @('-S', 'localhost', '-E', '-C', '-d', $databaseName, '-b', '-f', '65001', '-v', "SnapshotDate=$SnapshotDate", '-i', $snapshotBaselineFile)
        Write-SnapshotVerification $snapshotBeforePath
        Write-Output "Snapshot enabled: $SnapshotDate; report period: $snapshotFromDate to $ReportToDate"
    }
    else {
        Set-ProcessEnvironment 'TKS_PERF_FROM_DATE' $null
        Set-ProcessEnvironment 'TKS_PERF_TO_DATE' $null
        Set-ProcessEnvironment 'TKS_NBOMBER_SCENARIOS' $null
    }

    Set-ProcessEnvironment 'TKS_PERF_ROWS' $RecordCount.ToString()
    Set-ProcessEnvironment 'TKS_PERF_PAGE_SIZE' '10'
    Set-ProcessEnvironment 'TKS_PERF_LOGIN' 'PERF_USER'
    Set-ProcessEnvironment 'TKS_PERF_CONNECTION_STRING' $connectionString

    Write-Output "Running BenchmarkDotNet synthetic benchmark at $RecordCount rows..."
    Set-ProcessEnvironment 'TKS_BDN_DATABASE' '0'
    & dotnet run --project $benchmarkProject --configuration Release --no-restore -- --job short --filter '*WarehouseSyntheticBenchmarks*' --artifacts $bdnSyntheticDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "BenchmarkDotNet synthetic benchmark failed with exit code $LASTEXITCODE"
    }

    Write-Output "Running BenchmarkDotNet database benchmark against $databaseName..."
    Set-ProcessEnvironment 'TKS_BDN_DATABASE' '1'
    & dotnet run --project $benchmarkProject --configuration Release --no-restore -- --job short --filter '*WarehouseDatabaseBenchmarks*' --artifacts $bdnDatabaseDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "BenchmarkDotNet database benchmark failed with exit code $LASTEXITCODE"
    }

    Write-Output "Running NBomber with $Copies copies for $DurationSeconds seconds..."
    Set-ProcessEnvironment 'TKS_BDN_DATABASE' '0'
    Set-ProcessEnvironment 'TKS_NBOMBER_COPIES' $Copies.ToString()
    Set-ProcessEnvironment 'TKS_NBOMBER_DURATION_SECONDS' $DurationSeconds.ToString()
    Set-ProcessEnvironment 'TKS_BENCH_REPORT_DIR' $nbomberDirectory
    & dotnet run --project $benchmarkProject --configuration Release --no-restore -- --nbomber
    if ($LASTEXITCODE -ne 0) {
        throw "NBomber failed with exit code $LASTEXITCODE"
    }

    if ($UseSnapshot) {
        Invoke-Sql @('-S', 'localhost', '-E', '-C', '-d', $databaseName, '-b', '-f', '65001', '-v', 'PageSize=10', "FromDate=$snapshotFromDate", "ToDate=$ReportToDate", '-i', $snapshotSqlStatsFile, '-o', $sqlStatsPath)
        Write-SnapshotVerification $snapshotAfterPath
    }
    else {
        Invoke-Sql @('-S', 'localhost', '-E', '-C', '-d', $databaseName, '-b', '-f', '65001', '-v', 'PageSize=10', '-i', $sqlStatsFile, '-o', $sqlStatsPath)
    }

    Write-Output "Tool benchmark report root: $reportRoot"
    Write-Output "BenchmarkDotNet synthetic artifacts: $bdnSyntheticDirectory"
    Write-Output "BenchmarkDotNet database artifacts: $bdnDatabaseDirectory"
    Write-Output "NBomber artifacts: $nbomberDirectory"
    Write-Output "SQL statistics: $sqlStatsPath"
    if ($UseSnapshot) {
        Write-Output "Snapshot verification before load: $snapshotBeforePath"
        Write-Output "Snapshot verification after load: $snapshotAfterPath"
    }
}
finally {
    foreach ($name in $environmentNames) {
        $value = $previousEnvironment[$name]
        if ($null -eq $value) {
            [Environment]::SetEnvironmentVariable($name, $null, 'Process')
        }
        else {
            [Environment]::SetEnvironmentVariable($name, $value, 'Process')
        }
    }

    if (-not $KeepDatabase) {
        $cleanupExists = & $sqlcmd -S localhost -E -C -d master -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$databaseName') IS NULL THEN 0 ELSE 1 END;"
        if ($LASTEXITCODE -eq 0 -and (($cleanupExists | Select-Object -First 1).ToString().Trim() -eq '1')) {
            Invoke-Sql @('-S', 'localhost', '-E', '-C', '-d', 'master', '-b', '-Q', "ALTER DATABASE [$databaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$databaseName];")
        }
    }
}
