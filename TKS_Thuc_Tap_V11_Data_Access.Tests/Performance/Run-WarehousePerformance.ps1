[CmdletBinding()]
param(
    [ValidateRange(1, 10000000)]
    [int]$RecordCount = 100000,
    [ValidateRange(1, 256)]
    [int]$Workers = 8,
    [ValidateRange(1, 10000)]
    [int]$Iterations = 5,
    [ValidateRange(0, 100)]
    [int]$Warmup = 1,
    [switch]$FullLoad,
    [switch]$KeepDatabase,
    [switch]$Reset,
    [string]$DatabaseDirectory = ''
)

$ErrorActionPreference = 'Stop'
$scriptRoot = (Resolve-Path $PSScriptRoot).Path
$projectRoot = (Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$databaseName = "TKS_Thuc_Tap_V11_Perf_$RecordCount"
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outputPath = Join-Path $projectRoot "docs\testing\performance\warehouse-performance-$RecordCount-$timestamp.json"
$sqlStatsPath = $outputPath -replace '\.json$', '.sqlstats.txt'
$sqlcmd = (Get-Command sqlcmd -ErrorAction Stop).Source
$testProject = Join-Path $projectRoot 'TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj'
$schemaFile = Join-Path $projectRoot 'Database\WarehouseModule.Schema.sql'
$proceduresFile = Join-Path $projectRoot 'Database\WarehouseModule.Procedures.sql'
$seedFile = Join-Path $projectRoot 'Database\Performance\WarehousePerformance.Seed.sql'
$sqlStatsFile = Join-Path $projectRoot 'Database\Performance\WarehousePerformance.SqlStats.sql'

if ($databaseName -notmatch '^TKS_Thuc_Tap_V11_Perf_[0-9]+$') {
    throw "Refusing an unsafe performance database name: $databaseName"
}

function Invoke-Sql {
    param([string[]]$Arguments)

    & $sqlcmd @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed with exit code $LASTEXITCODE"
    }
}

$databaseExists = $false
$existsOutput = & $sqlcmd -S localhost -E -C -d master -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$databaseName') IS NULL THEN 0 ELSE 1 END;"
if ($LASTEXITCODE -ne 0) {
    throw "Could not check whether $databaseName exists."
}
$databaseExists = (($existsOutput | Select-Object -First 1).ToString().Trim() -eq '1')

if ($databaseExists -and -not $Reset) {
    throw "$databaseName already exists. Use -Reset only for this isolated benchmark database, or choose a different scale."
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

    $env:TKS_PERF_RUN = '1'
    $env:TKS_PERF_ROWS = $RecordCount.ToString()
    $env:TKS_PERF_PAGE_SIZE = '10'
    $env:TKS_PERF_WORKERS = $Workers.ToString()
    $env:TKS_PERF_ITERATIONS = $Iterations.ToString()
    $env:TKS_PERF_WARMUP = $Warmup.ToString()
    $env:TKS_PERF_FULL_LOAD = if ($FullLoad) { '1' } else { '0' }
    $env:TKS_PERF_CONNECTION_STRING = "Server=localhost;Database=$databaseName;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=30;"
    $env:TKS_PERF_OUTPUT = $outputPath

    & dotnet test $testProject --no-restore --filter 'FullyQualifiedName~PerformanceBenchmarkTests.Database_benchmark_runs_only_when_explicitly_enabled' --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "The performance benchmark test failed with exit code $LASTEXITCODE"
    }

    Invoke-Sql @('-S', 'localhost', '-E', '-C', '-d', $databaseName, '-b', '-f', '65001', '-v', 'PageSize=10', '-i', $sqlStatsFile, '-o', $sqlStatsPath)

    Write-Output "Performance report: $outputPath"
    Write-Output "SQL statistics: $sqlStatsPath"
}
finally {
    Remove-Item Env:TKS_PERF_RUN -ErrorAction SilentlyContinue
    Remove-Item Env:TKS_PERF_ROWS -ErrorAction SilentlyContinue
    Remove-Item Env:TKS_PERF_PAGE_SIZE -ErrorAction SilentlyContinue
    Remove-Item Env:TKS_PERF_WORKERS -ErrorAction SilentlyContinue
    Remove-Item Env:TKS_PERF_ITERATIONS -ErrorAction SilentlyContinue
    Remove-Item Env:TKS_PERF_WARMUP -ErrorAction SilentlyContinue
    Remove-Item Env:TKS_PERF_FULL_LOAD -ErrorAction SilentlyContinue
    Remove-Item Env:TKS_PERF_CONNECTION_STRING -ErrorAction SilentlyContinue
    Remove-Item Env:TKS_PERF_OUTPUT -ErrorAction SilentlyContinue

    if (-not $KeepDatabase) {
        $cleanupExists = & $sqlcmd -S localhost -E -C -d master -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$databaseName') IS NULL THEN 0 ELSE 1 END;"
        if ($LASTEXITCODE -eq 0 -and (($cleanupExists | Select-Object -First 1).ToString().Trim() -eq '1')) {
            Invoke-Sql @('-S', 'localhost', '-E', '-C', '-d', 'master', '-b', '-Q', "ALTER DATABASE [$databaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$databaseName];")
        }
    }
}
