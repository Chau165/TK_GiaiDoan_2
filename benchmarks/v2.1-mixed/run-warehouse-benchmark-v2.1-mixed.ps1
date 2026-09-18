[CmdletBinding()]
param(
    [switch]$Help,
    [switch]$Run,
    [switch]$SelfTest,
    [string]$PhaseRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$script:ExtensionRoot = $PSScriptRoot
$script:CoreRoot = Join-Path $script:RepoRoot "benchmarks\v2.1"
$script:CoreRunner = Join-Path $script:CoreRoot "run-warehouse-benchmark-v2.1-adaptive.ps1"
$script:CoreManifest = Join-Path $script:CoreRoot "benchmark-v2.1-adaptive-manifest.json"
$script:CoreBaselineRoot = "P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_1_ADAPTIVE-20260916-FINAL"
$script:CoreBaselinePublished = "P:\Warehouse-Benchmark-V2\BENCHMARK_V2_1_BASELINE_20260915-185645"
$script:RuntimeRoot = "P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_1-20260915-211500\runtime"
$script:RuntimeReference = Join-Path $script:CoreBaselineRoot "v2.1-runtime-reference.json"
$script:MainDllName = "TKS_Thuc_Tap_V11_Benchmarks_V2.dll"
$script:MainDll = Join-Path $script:RuntimeRoot $script:MainDllName
$script:DataAccessDll = Join-Path $script:RuntimeRoot "TKS_Thuc_Tap_V11_Data_Access.dll"
$script:TargetServer = "localhost\MSSQLSERVER19"
$script:TargetDatabase = "TKS_Thuc_Tap_V11_Perf_10000000"
$script:TargetDatabaseId = 5
$script:ConnectionString = "Server=localhost\MSSQLSERVER19;Database=TKS_Thuc_Tap_V11_Perf_10000000;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=30;Application Name=WarehouseBenchmarkV2_1_Mixed"
$script:LoginName = "PERF_USER"
$script:WarmupSeconds = 3
$script:DurationSeconds = 15
$script:ApplicationTimeoutSeconds = 30
$script:HardFloorMb = 512
$script:EmergencyMarginMb = 128
$script:SafetyMinimumMb = 640
$script:IdealStartMb = 1024
$script:CooldownMinimumSeconds = 5
$script:CooldownMaximumSeconds = 60
$script:MaximumLevelSeconds = 90
$script:MinimumMeaningfulOverlapSeconds = 10
$script:Levels = @(1, 2, 4, 8)
$script:Scenarios = @(
    "MasterPaged",
    "LookupPaged",
    "DocumentPaged",
    "DetailReportPaged",
    "InventoryHistoricalReportPaged",
    "InventoryCurrentBalancePaged"
)
$script:ExpectedCore = [ordered]@{
    CoreRunnerSha256 = "0338A1D59BD3C1680B955793D6CDAC64F60479AF72E11EC7ABFCDE7BE9988470"
    CoreManifestSha256 = "39321648149B6A3213B7E88455E38CAD87B33FA78015D240E1ED88363F99D3EF"
    BenchmarkDllSha256 = "DCB75373489DC1CC3288142D02484327E1369EF8B6A522C80BC6108D06B3BA84"
    DataAccessDllSha256 = "9262DDA93A1CBCEC7B2F188BB0E2FCFE92CEC09846741E32C9427FAFDFB9CAC1"
    RuntimeBundleManifestSha256 = "2DEE48CC2D9E8ECE7B1D5A483E694DEC83F159C5F33489791BB9BCB37F413C6C"
    CoreSourceClosureSha256 = "09339FA812A7A2DAE9E1FD53CE18377C0522D087282752EA2418F819E0A8DC39"
    CoreBaselineId = "BENCHMARK_V2_1_BASELINE_20260915-185645"
    CoreResult = "BENCHMARK_V2_1_CORE_BASELINE_CREATED"
}
$script:ProductExpected = [ordered]@{
    Schema = "7576666BE99F921885844F51767691244BD8B839413DB5D2397036CFE3A9904C"
    Procedures = "88A245B85B09D1087FDB201F7E43BB963005707791BFCBF84FD26B7654085894"
    Security = "291C0FA789E854FCA5A6AEB2584E8E1DACDE6A457690566247B6E35ACE2435BB"
    HistoricalGroupModeTests = "809B7EF7080C2C2803DD9867401AB35130337BCD836BFD497766C9D786E53179"
}
$script:ProductPaths = [ordered]@{
    Schema = Join-Path $script:RepoRoot "Database\WarehouseModule.Schema.sql"
    Procedures = Join-Path $script:RepoRoot "Database\WarehouseModule.Procedures.sql"
    Security = Join-Path $script:RepoRoot "Database\WarehouseModule.Security.sql"
    HistoricalGroupModeTests = Join-Path $script:RepoRoot "TKS_Thuc_Tap_V11_Data_Access.Tests\WarehouseHistoricalGroupModeTests.cs"
}
$script:PhaseRoot = ""
$script:ManifestFile = Join-Path $script:ExtensionRoot "benchmark-v2.1-mixed-manifest.json"
$script:RunnerFile = Join-Path $script:ExtensionRoot "run-warehouse-benchmark-v2.1-mixed.ps1"
$script:ReadmeFile = Join-Path $script:ExtensionRoot "README.md"
$script:DotnetPath = ""
$script:SqlcmdPath = ""
$script:ManifestHash = ""
$script:RunnerHash = ""
$script:MixedControlClosureHash = ""
$script:RuntimeBundleHash = ""
$script:SourceRows = @()
$script:ActiveRootPids = [System.Collections.Generic.HashSet[int]]::new()
$script:ActiveTelemetryPids = [System.Collections.Generic.HashSet[int]]::new()
$script:HostCapacityObserved = $false
$script:ProductFailureObserved = $false
$script:HarnessFailureObserved = $false
$script:ExecutedLevels = [System.Collections.Generic.List[object]]::new()
$script:PerScenarioRows = [System.Collections.Generic.List[object]]::new()
$script:LevelRows = [System.Collections.Generic.List[object]]::new()
$script:ComparisonRows = [System.Collections.Generic.List[object]]::new()

function Show-Help {
    @"
WAREHOUSE_BENCHMARK_V2_1_MIXED (semantic version 2.1.1)

This is an additional all-six mixed profile. It does not rewrite the
accepted V2.1 isolated core baseline.

Run:
  pwsh.exe -NoProfile -File $($script:RunnerFile) -Run

Optional:
  -PhaseRoot <durable directory>  use an explicit new evidence directory
  -SelfTest                       static/protocol checks only

The runner uses the frozen V2.1 runtime and targets only:
  $($script:TargetServer) / $($script:TargetDatabase) / DB_ID $($script:TargetDatabaseId)

Mixed levels:
  MIXED-L1 = 1 worker per scenario x 6 = 6 total workers
  MIXED-L2 = 2 worker per scenario x 6 = 12 total workers
  MIXED-L4 = 4 worker per scenario x 6 = 24 total workers
  MIXED-L8 = 8 worker per scenario x 6 = 48 total workers
"@
}

function Get-Sha256([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "File not found for SHA-256: $Path"
    }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-TextSha256([string]$Text) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($Text)))
}

function Get-RepoRelativePath([string]$Path) {
    return [IO.Path]::GetRelativePath($script:RepoRoot, $Path).Replace("\", "/")
}

function Write-TextFile([string]$Path, [string]$Text) {
    $directory = Split-Path -Parent $Path
    if ($directory) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Write-JsonFile([string]$Path, [object]$Value) {
    Write-TextFile $Path ($Value | ConvertTo-Json -Depth 60)
}

function Get-JsonValue([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "JSON evidence not found: $Path"
    }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Get-InventoryFingerprint([object[]]$Rows) {
    $lines = @(
        $Rows |
            Sort-Object RelativePath, Role |
            ForEach-Object { "$($_.RelativePath)|$($_.Role)|$($_.Size)|$($_.Sha256)" }
    )
    return Get-TextSha256 (($lines -join [Environment]::NewLine) + [Environment]::NewLine)
}

function Add-InventoryFile(
    [System.Collections.Generic.List[object]]$Rows,
    [string]$Path,
    [string]$Role,
    [string]$RelativePath = ""
) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Inventory file not found: $Path"
    }
    $item = Get-Item -LiteralPath $Path
    $displayPath = if ([string]::IsNullOrWhiteSpace($RelativePath)) { Get-RepoRelativePath $Path } else { $RelativePath }
    $Rows.Add([pscustomobject]@{
        RelativePath = $displayPath
        Role = $Role
        Size = [int64]$item.Length
        Sha256 = Get-Sha256 $Path
    })
}

function Get-Number([object]$Value) {
    if ($null -eq $Value) {
        return $null
    }
    $text = ([string]$Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }
    $number = 0.0
    $styles = [Globalization.NumberStyles]::Any
    $cultures = if ($text.Contains(",") -and -not $text.Contains(".")) {
        @([Globalization.CultureInfo]::CurrentCulture, [Globalization.CultureInfo]::InvariantCulture)
    } else {
        @([Globalization.CultureInfo]::InvariantCulture, [Globalization.CultureInfo]::CurrentCulture)
    }
    foreach ($culture in $cultures) {
        if ([double]::TryParse($text, $styles, $culture, [ref]$number)) {
            return [double]$number
        }
    }
    return $null
}

function Get-PropertyText([object]$Row, [string[]]$Names) {
    foreach ($name in $Names) {
        $property = $Row.PSObject.Properties[$name]
        if ($null -ne $property) {
            return ([string]$property.Value).Trim()
        }
    }
    return ""
}

function Get-PropertyNumber([object]$Row, [string[]]$Names) {
    return Get-Number (Get-PropertyText $Row $Names)
}

function Format-Number([object]$Value, [int]$Decimals = 3) {
    $number = Get-Number $Value
    if ($null -eq $number) {
        return "N/A"
    }
    return $number.ToString("F$Decimals", [Globalization.CultureInfo]::InvariantCulture)
}

function Format-Percent([object]$Value) {
    return Format-Number $Value 2
}

function Get-DeltaPercent([object]$Baseline, [object]$Mixed) {
    $base = Get-Number $Baseline
    $current = Get-Number $Mixed
    if ($null -eq $base -or $null -eq $current -or $base -eq 0) {
        return $null
    }
    return [math]::Round((($current - $base) / $base) * 100.0, 2)
}

function Add-CsvRow([string]$Path, [object]$Row) {
    $csv = @($Row | ConvertTo-Csv -NoTypeInformation)
    if ($csv.Count -lt 2) {
        return
    }
    [IO.File]::AppendAllText(
        $Path,
        [Environment]::NewLine + ($csv[1..($csv.Count - 1)] -join [Environment]::NewLine),
        [Text.UTF8Encoding]::new($false))
}

function Initialize-OutputFiles([string]$Root) {
    New-Item -ItemType Directory -Path (Join-Path $Root "references") -Force | Out-Null
    $headers = [ordered]@{
        "mixed-per-scenario.csv" = "Level,TotalWorkers,WorkersPerScenario,Scenario,Requests,Success,Failed,Timeout,MeanMs,P50Ms,P75Ms,P95Ms,P99Ms,MaxMs,RPS,Result,FailureType,ConfiguredCopies,ObservedCopies,LaunchUtc,MeasuredStartUtc,MeasuredStopUtc,WindowSeconds,Notes"
        "mixed-level-summary.csv" = "Level,TotalWorkers,WorkersPerScenario,Executed,Admission,StartAvailableMB,PreviousDropMB,PredictedNextDropMB,PredictedMinimumMB,TotalRequests,Success,Failed,Timeout,AggregateRPS,MinRAMMB,CPUPeak,SQLWorkingSetPeakMB,BenchmarkWorkingSetPeakMB,PendingGrants,RSWaiters,BlockedRequests,Deadlocks,LogicalReadsMax,TempdbUsedKBMax,WindowOverlapSeconds,Result,FailureType,Notes"
        "mixed-host-telemetry.csv" = "Level,SampleKind,TimestampUtc,FreePhysicalRamMb,TotalPhysicalRamMb,CommitUsedMb,CommitLimitMb,ChildPrivateMemoryMb,ChildWorkingSetMb,SqlServerWorkingSetMb,CpuPercent,ProcessCount,ActiveBenchmarkProcesses,HardFloorMb,EmergencyMarginMb,PredictedSafeMinimumMb"
        "mixed-sql-telemetry.csv" = "BlockId,TimestampUtc,DatabaseName,DatabaseId,ActiveRequests,BlockingRequests,ActiveRequestGrantKB,RequestedGrantKB,GrantedGrantKB,PendingMemoryGrants,ResourceSemaphoreWaiters,LogicalReads,TempdbUsedKB,MemoryGrantsPendingCounter,DeadlockCounter"
        "mixed-comparison.csv" = "Scenario,Level,IsolatedMeanMs,MixedMeanMs,MeanDeltaPct,IsolatedP95Ms,MixedP95Ms,P95DeltaPct,IsolatedP99Ms,MixedP99Ms,P99DeltaPct,IsolatedRPS,MixedRPS,RPSDeltaPct,Result,Notes"
    }
    foreach ($name in $headers.Keys) {
        Write-TextFile (Join-Path $Root $name) $headers[$name]
    }
}

function Get-DotnetPath {
    $command = Get-Command dotnet.exe -ErrorAction Stop
    if ([string]::IsNullOrWhiteSpace($command.Path)) {
        throw "dotnet.exe path could not be resolved."
    }
    return $command.Path
}

function Get-SqlcmdPath {
    $command = Get-Command sqlcmd.exe -ErrorAction Stop
    if ([string]::IsNullOrWhiteSpace($command.Path)) {
        throw "sqlcmd.exe path could not be resolved."
    }
    return $command.Path
}

function Start-LoggedProcess(
    [string]$FilePath,
    [string[]]$Arguments,
    [string]$WorkingDirectory,
    [string]$StdoutPath,
    [string]$StderrPath,
    [hashtable]$Environment = @{}
) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $FilePath
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        [void]$info.ArgumentList.Add([string]$argument)
    }
    foreach ($key in $Environment.Keys) {
        $info.Environment[$key] = [string]$Environment[$key]
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    if (-not $process.Start()) {
        throw "Could not start process: $FilePath"
    }
    return [pscustomobject]@{
        Process = $process
        Arguments = @($Arguments)
        StdoutTask = $process.StandardOutput.ReadToEndAsync()
        StderrTask = $process.StandardError.ReadToEndAsync()
        StdoutPath = $StdoutPath
        StderrPath = $StderrPath
        StartedUtc = [DateTime]::UtcNow
    }
}

function Complete-LoggedProcess([object]$Handle) {
    $Handle.Process.WaitForExit()
    $stdout = $Handle.StdoutTask.GetAwaiter().GetResult()
    $stderr = $Handle.StderrTask.GetAwaiter().GetResult()
    Write-TextFile $Handle.StdoutPath $stdout
    Write-TextFile $Handle.StderrPath $stderr
    return [pscustomobject]@{
        ExitCode = [int]$Handle.Process.ExitCode
        Stdout = [string]$stdout
        Stderr = [string]$stderr
        StartedUtc = $Handle.StartedUtc
        FinishedUtc = [DateTime]::UtcNow
        ProcessId = [int]$Handle.Process.Id
    }
}

function Invoke-LoggedProcess(
    [string]$FilePath,
    [string[]]$Arguments,
    [string]$WorkingDirectory,
    [string]$StdoutPath,
    [string]$StderrPath,
    [hashtable]$Environment = @{}
) {
    $handle = Start-LoggedProcess $FilePath $Arguments $WorkingDirectory $StdoutPath $StderrPath $Environment
    try {
        return Complete-LoggedProcess $handle
    } finally {
        $handle.Process.Dispose()
    }
}

function Get-ProcessTreeIds([int]$RootPid) {
    $processRows = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue)
    $ids = [System.Collections.Generic.HashSet[int]]::new()
    $pending = [System.Collections.Generic.Stack[int]]::new()
    [void]$pending.Push($RootPid)
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        if (-not $ids.Add($current)) {
            continue
        }
        foreach ($row in $processRows | Where-Object { [int]$_.ParentProcessId -eq $current }) {
            [void]$pending.Push([int]$row.ProcessId)
        }
    }
    return @($ids)
}

function Stop-ProcessTree([int]$RootPid) {
    $currentPid = [Diagnostics.Process]::GetCurrentProcess().Id
    foreach ($processId in @(Get-ProcessTreeIds $RootPid | Sort-Object -Descending)) {
        if ($processId -eq $currentPid) {
            continue
        }
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
    }
}

function Test-ProcessExited([object]$Process) {
    try {
        return [bool]$Process.HasExited
    } catch {
        return $true
    }
}

function Stop-AllTrackedProcesses {
    foreach ($pid in @($script:ActiveTelemetryPids)) {
        Stop-ProcessTree ([int]$pid)
    }
    foreach ($pid in @($script:ActiveRootPids)) {
        Stop-ProcessTree ([int]$pid)
    }
    $script:ActiveTelemetryPids.Clear()
    $script:ActiveRootPids.Clear()
}

function Get-ProcessMemoryMb([int[]]$ProcessIds) {
    $privateMb = 0.0
    $workingMb = 0.0
    foreach ($processId in $ProcessIds) {
        try {
            $process = Get-Process -Id $processId -ErrorAction Stop
            $privateMb += [double]$process.PrivateMemorySize64 / 1MB
            $workingMb += [double]$process.WorkingSet64 / 1MB
        } catch {
        }
    }
    return [pscustomobject]@{
        PrivateMb = [math]::Round($privateMb, 1)
        WorkingMb = [math]::Round($workingMb, 1)
    }
}

function Get-HostSnapshot {
    $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
    $freeMb = [math]::Round(([double]$os.FreePhysicalMemory * 1KB) / 1MB, 1)
    $totalMb = [math]::Round(([double]$os.TotalVisibleMemorySize * 1KB) / 1MB, 1)
    $commitUsedMb = [math]::Round((([double]$os.TotalVirtualMemorySize - [double]$os.FreeVirtualMemory) * 1KB) / 1MB, 1)
    $commitLimitMb = [math]::Round(([double]$os.TotalVirtualMemorySize * 1KB) / 1MB, 1)
    $sqlWorkingMb = 0.0
    foreach ($sqlProcess in @(Get-Process -Name sqlservr -ErrorAction SilentlyContinue)) {
        try {
            $sqlWorkingMb += [double]$sqlProcess.WorkingSet64 / 1MB
        } catch {
        }
    }
    $cpuRows = @(Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq "_Total" })
    $cpuPercent = if ($cpuRows.Count -gt 0) { [math]::Round([double]$cpuRows[0].PercentProcessorTime, 1) } else { 0.0 }
    return [pscustomobject]@{
        TimestampUtc = [DateTime]::UtcNow.ToString("O")
        FreePhysicalRamMb = $freeMb
        TotalPhysicalRamMb = $totalMb
        CommitUsedMb = $commitUsedMb
        CommitLimitMb = $commitLimitMb
        SqlServerWorkingSetMb = [math]::Round($sqlWorkingMb, 1)
        CpuPercent = $cpuPercent
        ProcessCount = @(Get-Process -ErrorAction SilentlyContinue).Count
    }
}

function Get-ChildProcessIds([object[]]$Children) {
    $ids = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($child in $Children) {
        if ($null -eq $child -or $null -eq $child.Handle) {
            continue
        }
        try {
            foreach ($processId in @(Get-ProcessTreeIds ([int]$child.Handle.Process.Id))) {
                [void]$ids.Add([int]$processId)
            }
        } catch {
        }
    }
    return @($ids)
}

function Add-HostSample(
    [string]$LevelId,
    [string]$SampleKind,
    [object[]]$Children,
    [double]$PredictedMinimumMb = 0.0
) {
    $snapshot = Get-HostSnapshot
    $processIds = @(Get-ChildProcessIds $Children)
    $memory = Get-ProcessMemoryMb $processIds
    $activeCount = @($Children | Where-Object { -not (Test-ProcessExited $_.Handle.Process) }).Count
    $row = [pscustomobject]@{
        Level = $LevelId
        SampleKind = $SampleKind
        TimestampUtc = $snapshot.TimestampUtc
        FreePhysicalRamMb = $snapshot.FreePhysicalRamMb
        TotalPhysicalRamMb = $snapshot.TotalPhysicalRamMb
        CommitUsedMb = $snapshot.CommitUsedMb
        CommitLimitMb = $snapshot.CommitLimitMb
        ChildPrivateMemoryMb = $memory.PrivateMb
        ChildWorkingSetMb = $memory.WorkingMb
        SqlServerWorkingSetMb = $snapshot.SqlServerWorkingSetMb
        CpuPercent = $snapshot.CpuPercent
        ProcessCount = $snapshot.ProcessCount
        ActiveBenchmarkProcesses = $activeCount
        HardFloorMb = $script:HardFloorMb
        EmergencyMarginMb = $script:EmergencyMarginMb
        PredictedSafeMinimumMb = [math]::Round($PredictedMinimumMb, 1)
    }
    Add-CsvRow (Join-Path $script:PhaseRoot "mixed-host-telemetry.csv") $row
    return [pscustomobject]@{
        Row = $row
        ProcessIds = $processIds
        Snapshot = $snapshot
        ChildPrivateMemoryMb = $memory.PrivateMb
        ChildWorkingSetMb = $memory.WorkingMb
    }
}

function Get-ChildEnvironment([string]$OutputDirectory, [int]$Copies) {
    $temp = Join-Path $script:PhaseRoot "temp"
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    return @{
        TKS_V2_CONNECTION_STRING = $script:ConnectionString
        TKS_V2_LOGIN = $script:LoginName
        TKS_V2_FROM_DATE = "2025-01-01"
        TKS_V2_TO_DATE = "2026-12-31"
        TKS_V2_PAGE_SIZE = "10"
        TKS_V2_OUTPUT_DIRECTORY = $OutputDirectory
        TKS_V2_NBOMBER_COPIES = [string]$Copies
        TKS_V2_NBOMBER_WARMUP_SECONDS = [string]$script:WarmupSeconds
        TKS_V2_NBOMBER_DURATION_SECONDS = [string]$script:DurationSeconds
        TEMP = $temp
        TMP = $temp
    }
}

function Invoke-V2Runtime(
    [string[]]$Arguments,
    [string]$OutputDirectory,
    [string]$StdoutName,
    [string]$StderrName
) {
    $environment = Get-ChildEnvironment $OutputDirectory 1
    return Invoke-LoggedProcess $script:DotnetPath (@($script:MainDll) + $Arguments) $script:RepoRoot (Join-Path $OutputDirectory $StdoutName) (Join-Path $OutputDirectory $StderrName) $environment
}

function Read-SqlScalar(
    [string]$Query,
    [string]$Label
) {
    $safeLabel = ($Label -replace "[^A-Za-z0-9_-]", "_")
    $guardDirectory = Join-Path $script:PhaseRoot "guards"
    $result = Invoke-LoggedProcess $script:SqlcmdPath @(
        "-S", $script:TargetServer,
        "-d", $script:TargetDatabase,
        "-E",
        "-b",
        "-h", "-1",
        "-W",
        "-s", "|",
        "-Q", $Query
    ) $script:RepoRoot (Join-Path $guardDirectory "$safeLabel.stdout.txt") (Join-Path $guardDirectory "$safeLabel.stderr.txt") @{}
    if ($result.ExitCode -ne 0) {
        throw "SQL guard failed: $Label; $($result.Stderr)"
    }
    $lines = @($result.Stdout -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })
    if ($lines.Count -eq 0) {
        throw "SQL guard returned no scalar: $Label"
    }
    return $lines[0]
}

function Invoke-TargetGuard([string]$Label) {
    $identityText = Read-SqlScalar "SET NOCOUNT ON; SELECT CAST(@@SERVERNAME AS nvarchar(128)) + N'|' + DB_NAME() + N'|' + CONVERT(varchar(20), DB_ID());" "identity-$Label"
    $identityParts = $identityText.Split("|", 3)
    if ($identityParts.Count -ne 3) {
        throw "TARGET_DB_MISMATCH|Malformed identity output: $identityText"
    }
    $server = $identityParts[0].Trim()
    $database = $identityParts[1].Trim()
    $databaseId = [int]$identityParts[2].Trim()
    $serverMatches = $server.Equals($script:TargetServer, [StringComparison]::OrdinalIgnoreCase) -or $server.EndsWith("\MSSQLSERVER19", [StringComparison]::OrdinalIgnoreCase)
    $targetMatches = $serverMatches -and $database.Equals($script:TargetDatabase, [StringComparison]::OrdinalIgnoreCase) -and $databaseId -eq $script:TargetDatabaseId
    if (-not $targetMatches) {
        throw "TARGET_DB_MISMATCH|Server=$server|Database=$database|DB_ID=$databaseId"
    }
    $mode = Read-SqlScalar "SET NOCOUNT ON; SELECT Historical_Report_Mode FROM dbo.Inventory_Report_Fence_Config WHERE Config_ID = 1;" "mode-$Label"
    if (-not $mode.Equals("LEGACY", [StringComparison]::OrdinalIgnoreCase)) {
        throw "HISTORICAL_MODE_MISMATCH|Expected LEGACY, actual $mode"
    }
    $queueState = Read-SqlScalar @"
SET NOCOUNT ON;
SELECT CONCAT(
    N'Movement=', CONVERT(varchar(30), (SELECT COUNT_BIG(*) FROM dbo.InventoryMovement_RebuildQueue)),
    N'|Snapshot=', CONVERT(varchar(30), (SELECT COUNT_BIG(*) FROM dbo.InventorySnapshot_RebuildQueue)),
    N'|MovementDLQ=', CONVERT(varchar(30), (SELECT COUNT_BIG(*) FROM dbo.InventoryMovement_RebuildDeadLetter)),
    N'|SnapshotDLQ=', CONVERT(varchar(30), (SELECT COUNT_BIG(*) FROM dbo.InventorySnapshot_RebuildDeadLetter)));
"@ "queue-$Label"
    $guard = [ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow
        Label = $Label
        ServerName = $server
        DatabaseName = $database
        DatabaseId = $databaseId
        IsTarget = $targetMatches
        HistoricalMode = $mode
        QueueState = $queueState
        QueueStateSha256 = Get-TextSha256 $queueState
    }
    Write-JsonFile (Join-Path $script:PhaseRoot "guards\target-$Label.json") $guard
    return [pscustomobject]$guard
}

function Capture-Residue([string]$Label) {
    $path = Join-Path $script:PhaseRoot "guards\residue-$Label.json"
    $directory = Split-Path -Parent $path
    $result = Invoke-V2Runtime @("--residue", "--output", $path) $directory "residue-$Label.stdout.txt" "residue-$Label.stderr.txt"
    if ($result.ExitCode -ne 0) {
        $script:HarnessFailureObserved = $true
    }
    $json = if (Test-Path -LiteralPath $path -PathType Leaf) { Get-JsonValue $path } else { $null }
    if ($null -eq $json -or -not [bool]$json.Passed) {
        $script:HarnessFailureObserved = $true
    }
    return $json
}

function Capture-SqlAgent([string]$Label) {
    $service = Get-CimInstance Win32_Service -Filter "Name='SQLAgent`$MSSQLSERVER19'" -ErrorAction SilentlyContinue
    $exists = $null -ne $service
    $status = if ($exists) { [string]$service.State } else { "MISSING" }
    $startType = if ($exists) { [string]$service.StartMode } else { "MISSING" }
    $evidence = [ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow
        Label = $Label
        Name = "SQLAgent`$MSSQLSERVER19"
        Exists = $exists
        Status = $status
        StartType = $startType
        RequiredStatus = "Stopped"
        RequiredStartType = "Manual"
        UnchangedSafeState = $exists -and $status -eq "Stopped" -and $startType -eq "Manual"
    }
    Write-JsonFile (Join-Path $script:PhaseRoot "guards\sql-agent-$Label.json") $evidence
    return [pscustomobject]$evidence
}

function Capture-ProductIntegrity([string]$Label) {
    $files = [System.Collections.Generic.List[object]]::new()
    foreach ($name in $script:ProductPaths.Keys) {
        $path = $script:ProductPaths[$name]
        $actual = Get-Sha256 $path
        $files.Add([pscustomobject]@{
            Name = $name
            Path = $path
            ExpectedSha256 = $script:ProductExpected[$name]
            ActualSha256 = $actual
            Match = $actual -eq $script:ProductExpected[$name]
        })
    }
    $evidence = [ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow
        Label = $Label
        Passed = @($files | Where-Object { -not $_.Match }).Count -eq 0
        Files = @($files)
        BusinessDatabase = "TKS_Thuc_Tap_V11_GiaiDoan2; not accessed"
        PerformanceDatabase = $script:TargetDatabase
        ProductSqlModified = $false
    }
    Write-JsonFile (Join-Path $script:PhaseRoot "guards\product-integrity-$Label.json") $evidence
    if (-not [bool]$evidence.Passed) {
        throw "PRODUCT_CANDIDATE_DRIFT|$Label"
    }
    return [pscustomobject]$evidence
}

function Compare-ProductIntegrity([object]$Before, [object]$After) {
    if ($null -eq $Before -or $null -eq $After) {
        return $false
    }
    $beforeRows = @($Before.Files)
    $afterRows = @($After.Files)
    if ($beforeRows.Count -ne $afterRows.Count) {
        return $false
    }
    foreach ($beforeRow in $beforeRows) {
        $afterRow = $afterRows | Where-Object { $_.Name -eq $beforeRow.Name } | Select-Object -First 1
        if ($null -eq $afterRow -or $beforeRow.ActualSha256 -ne $afterRow.ActualSha256 -or -not [bool]$afterRow.Match) {
            return $false
        }
    }
    return $true
}

function Verify-CoreSourceClosure {
    $inventoryPath = Join-Path $script:CoreBaselineRoot "source\source-inventory.json"
    $coreRows = @(Get-JsonValue $inventoryPath)
    if ($coreRows.Count -ne 170) {
        throw "CORE_SOURCE_INVENTORY_COUNT_MISMATCH|$($coreRows.Count)"
    }
    $currentRows = [System.Collections.Generic.List[object]]::new()
    foreach ($row in $coreRows) {
        $path = Join-Path $script:RepoRoot ($row.RelativePath.Replace("/", "\"))
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "CORE_SOURCE_FILE_MISSING|$($row.RelativePath)"
        }
        $item = Get-Item -LiteralPath $path
        $actual = Get-Sha256 $path
        if ([int64]$item.Length -ne [int64]$row.Size -or $actual -ne $row.Sha256) {
            throw "CORE_SOURCE_DRIFT|$($row.RelativePath)"
        }
        $currentRows.Add([pscustomobject]@{
            RelativePath = $row.RelativePath
            Role = $row.Role
            Size = [int64]$item.Length
            Sha256 = $actual
        })
    }
    $hash = Get-InventoryFingerprint @($currentRows)
    if ($hash -ne $script:ExpectedCore.CoreSourceClosureSha256) {
        throw "CORE_SOURCE_CLOSURE_DRIFT|$hash"
    }
    Copy-Item -LiteralPath $inventoryPath -Destination (Join-Path $script:PhaseRoot "references\core-source-inventory.json") -Force
    Write-JsonFile (Join-Path $script:PhaseRoot "references\core-source-closure.json") ([ordered]@{
        ExpectedSha256 = $script:ExpectedCore.CoreSourceClosureSha256
        ActualSha256 = $hash
        FileCount = $currentRows.Count
        Passed = $true
    })
    return @($currentRows)
}

function Capture-ControlInventory([object[]]$CoreRows) {
    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($row in $CoreRows) {
        $rows.Add($row)
    }
    Add-InventoryFile $rows $script:RunnerFile "MIXED_EXTENSION_RUNNER"
    Add-InventoryFile $rows $script:ManifestFile "MIXED_EXTENSION_MANIFEST"
    Add-InventoryFile $rows $script:ReadmeFile "MIXED_EXTENSION_README"
    $script:SourceRows = @($rows | Sort-Object RelativePath, Role)
    $script:MixedControlClosureHash = Get-InventoryFingerprint $script:SourceRows
    Write-JsonFile (Join-Path $script:PhaseRoot "mixed-source-inventory.json") $script:SourceRows
    Write-JsonFile (Join-Path $script:PhaseRoot "mixed-source-closure.json") ([ordered]@{
        Sha256 = $script:MixedControlClosureHash
        FileCount = $script:SourceRows.Count
        CoreSourceClosureSha256 = $script:ExpectedCore.CoreSourceClosureSha256
        Policy = "Includes current accepted V2.1 source closure plus mixed extension controls; manifest is separately hashed and does not contain this self-referential value."
    })
    return $script:SourceRows
}

function Verify-CoreArtifacts {
    $runnerHash = Get-Sha256 $script:CoreRunner
    $manifestHash = Get-Sha256 $script:CoreManifest
    if ($runnerHash -ne $script:ExpectedCore.CoreRunnerSha256) {
        throw "V2_1_CORE_RUNNER_DRIFT|$runnerHash"
    }
    if ($manifestHash -ne $script:ExpectedCore.CoreManifestSha256) {
        throw "V2_1_CORE_MANIFEST_DRIFT|$manifestHash"
    }
    $manifest = Get-JsonValue $script:CoreManifest
    if ($manifest.protocolVersion -ne "WAREHOUSE_BENCHMARK_V2_1_ADAPTIVE" -or $manifest.protocolSemanticVersion -ne "2.1.0") {
        throw "V2_1_CORE_PROTOCOL_MISMATCH"
    }
    $coreResultPath = Join-Path $script:CoreBaselineRoot "v2.1-result.json"
    $coreResult = Get-JsonValue $coreResultPath
    if ($coreResult.Result -ne $script:ExpectedCore.CoreResult -or
        $coreResult.Candidate.BenchmarkDllSha256 -ne $script:ExpectedCore.BenchmarkDllSha256 -or
        $coreResult.BaselineId -ne $script:ExpectedCore.CoreBaselineId) {
        throw "V2_1_CORE_BASELINE_REFERENCE_MISMATCH"
    }
    $script:RunnerHash = Get-Sha256 $script:RunnerFile
    $script:ManifestHash = Get-Sha256 $script:ManifestFile
    $extensionManifest = Get-JsonValue $script:ManifestFile
    if ($extensionManifest.protocolVersion -ne "WAREHOUSE_BENCHMARK_V2_1_MIXED" -or $extensionManifest.protocolSemanticVersion -ne "2.1.1") {
        throw "MIXED_PROTOCOL_MISMATCH"
    }
    if ($extensionManifest.runnerSha256 -ne $script:RunnerHash) {
        throw "MIXED_RUNNER_HASH_MISMATCH|Manifest=$($extensionManifest.runnerSha256)|Actual=$($script:RunnerHash)"
    }
    if (@($extensionManifest.scenarioCatalog).Count -ne 6 -or @($extensionManifest.mixedLevels).Count -ne 4) {
        throw "MIXED_MANIFEST_SHAPE_MISMATCH"
    }
    $runtimeBaseName = [IO.Path]::GetFileNameWithoutExtension($script:MainDllName)
    foreach ($path in @($script:MainDll, $script:DataAccessDll, (Join-Path $script:RuntimeRoot "$runtimeBaseName.deps.json"), (Join-Path $script:RuntimeRoot "$runtimeBaseName.runtimeconfig.json"))) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "FROZEN_RUNTIME_FILE_MISSING|$path"
        }
    }
    $dllHash = Get-Sha256 $script:MainDll
    $dataAccessHash = Get-Sha256 $script:DataAccessDll
    if ($dllHash -ne $script:ExpectedCore.BenchmarkDllSha256 -or $dataAccessHash -ne $script:ExpectedCore.DataAccessDllSha256) {
        throw "FROZEN_RUNTIME_DRIFT|Benchmark=$dllHash|DataAccess=$dataAccessHash"
    }
    Copy-Item -LiteralPath $script:CoreRunner -Destination (Join-Path $script:PhaseRoot "references\core-v2.1-runner.ps1") -Force
    Copy-Item -LiteralPath $script:CoreManifest -Destination (Join-Path $script:PhaseRoot "references\core-v2.1-manifest.json") -Force
    Copy-Item -LiteralPath $coreResultPath -Destination (Join-Path $script:PhaseRoot "references\core-v2.1-result.json") -Force
    Copy-Item -LiteralPath $script:ManifestFile -Destination (Join-Path $script:PhaseRoot "manifest-used.json") -Force
    Write-TextFile (Join-Path $script:PhaseRoot "manifest-used.sha256") $script:ManifestHash
    return [pscustomobject]@{
        CoreRunnerSha256 = $runnerHash
        CoreManifestSha256 = $manifestHash
        ExtensionRunnerSha256 = $script:RunnerHash
        ExtensionManifestSha256 = $script:ManifestHash
        BenchmarkDllSha256 = $dllHash
        DataAccessDllSha256 = $dataAccessHash
        ExtensionManifest = $extensionManifest
    }
}

function Verify-RuntimeBundle {
    $reference = Get-JsonValue $script:RuntimeReference
    if ($reference.MainDllSha256 -ne $script:ExpectedCore.BenchmarkDllSha256 -or $reference.RuntimeBundleManifestSha256 -ne $script:ExpectedCore.RuntimeBundleManifestSha256) {
        throw "RUNTIME_REFERENCE_EXPECTATION_MISMATCH"
    }
    $referenceRows = @($reference.RuntimeInventory | Sort-Object RelativePath, Role)
    if ($referenceRows.Count -ne 150) {
        throw "RUNTIME_REFERENCE_COUNT_MISMATCH|$($referenceRows.Count)"
    }
    $actualRows = [System.Collections.Generic.List[object]]::new()
    foreach ($row in $referenceRows) {
        $path = Join-Path $script:RuntimeRoot ($row.RelativePath.Replace("/", "\"))
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "FROZEN_RUNTIME_FILE_MISSING|$($row.RelativePath)"
        }
        $item = Get-Item -LiteralPath $path
        $hash = Get-Sha256 $path
        if ([int64]$item.Length -ne [int64]$row.Size -or $hash -ne $row.Sha256) {
            throw "FROZEN_RUNTIME_FILE_DRIFT|$($row.RelativePath)"
        }
        $actualRows.Add([pscustomobject]@{
            RelativePath = $row.RelativePath
            Role = $row.Role
            Size = [int64]$item.Length
            Sha256 = $hash
            Origin = "Accepted V2.1 frozen runtime; reused without build"
            Required = [bool]$row.Required
            ActualPath = $path
        })
    }
    $computed = Get-InventoryFingerprint @($actualRows)
    if ($computed -ne $script:ExpectedCore.RuntimeBundleManifestSha256) {
        throw "RUNTIME_BUNDLE_FINGERPRINT_DRIFT|$computed"
    }
    $script:RuntimeBundleHash = $computed
    Copy-Item -LiteralPath $script:RuntimeReference -Destination (Join-Path $script:PhaseRoot "references\v2.1-runtime-reference.json") -Force
    Write-JsonFile (Join-Path $script:PhaseRoot "mixed-runtime-inventory.json") $actualRows
    Write-JsonFile (Join-Path $script:PhaseRoot "mixed-runtime-reference-check.json") ([ordered]@{
        ExpectedSha256 = $script:ExpectedCore.RuntimeBundleManifestSha256
        ActualSha256 = $computed
        FileCount = $actualRows.Count
        MainDllSha256 = Get-Sha256 $script:MainDll
        DataAccessDllSha256 = Get-Sha256 $script:DataAccessDll
        Passed = $true
    })
    return [pscustomobject]@{
        Reference = $reference
        Rows = @($actualRows)
        Fingerprint = $computed
    }
}

function Capture-BuildEnvironment {
    $environmentDirectory = Join-Path $script:PhaseRoot "environment"
    New-Item -ItemType Directory -Path $environmentDirectory -Force | Out-Null
    $dotnetInfo = Invoke-LoggedProcess $script:DotnetPath @("--info") $script:RepoRoot (Join-Path $environmentDirectory "dotnet-info.stdout.txt") (Join-Path $environmentDirectory "dotnet-info.stderr.txt") @{}
    $dotnetSdks = Invoke-LoggedProcess $script:DotnetPath @("--list-sdks") $script:RepoRoot (Join-Path $environmentDirectory "dotnet-sdks.stdout.txt") (Join-Path $environmentDirectory "dotnet-sdks.stderr.txt") @{}
    $dotnetRuntimes = Invoke-LoggedProcess $script:DotnetPath @("--list-runtimes") $script:RepoRoot (Join-Path $environmentDirectory "dotnet-runtimes.stdout.txt") (Join-Path $environmentDirectory "dotnet-runtimes.stderr.txt") @{}
    $os = Get-CimInstance Win32_OperatingSystem
    $computer = Get-CimInstance Win32_ComputerSystem
    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    $snapshot = Get-HostSnapshot
    $record = [ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow
        OS = [ordered]@{
            Caption = [string]$os.Caption
            Version = [string]$os.Version
            BuildNumber = [string]$os.BuildNumber
        }
        Hardware = [ordered]@{
            Processor = [string]$cpu.Name
            PhysicalCores = [int]$cpu.NumberOfCores
            LogicalProcessors = [int]$computer.NumberOfLogicalProcessors
            TotalVisibleMemoryMb = $snapshot.TotalPhysicalRamMb
            AvailableMemoryAtCaptureMb = $snapshot.FreePhysicalRamMb
        }
        Dotnet = [ordered]@{
            Path = $script:DotnetPath
            InfoExitCode = $dotnetInfo.ExitCode
            SdkExitCode = $dotnetSdks.ExitCode
            RuntimeExitCode = $dotnetRuntimes.ExitCode
            TargetFramework = "net8.0"
        }
        BuildPolicy = "NO_BUILD; NO_RESTORE; accepted frozen V2.1 runtime reused"
        Configuration = "Release"
        RuntimeIdentifier = "none"
        Deterministic = $true
        EnvironmentVariables = [ordered]@{
            TKS_V2_LOGIN = $script:LoginName
            TKS_V2_FROM_DATE = "2025-01-01"
            TKS_V2_TO_DATE = "2026-12-31"
            TKS_V2_PAGE_SIZE = "10"
            TKS_V2_NBOMBER_WARMUP_SECONDS = [string]$script:WarmupSeconds
            TKS_V2_NBOMBER_DURATION_SECONDS = [string]$script:DurationSeconds
        }
    }
    Write-JsonFile (Join-Path $script:PhaseRoot "mixed-environment.json") $record
    return $record
}

function Invoke-StaticSelfTest {
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($script:RunnerFile, [ref]$tokens, [ref]$errors) | Out-Null
    $manifest = Get-JsonValue $script:ManifestFile
    $checks = [System.Collections.Generic.List[object]]::new()
    $checks.Add([pscustomobject]@{ Name = "Runner parses"; Passed = @($errors).Count -eq 0; Detail = "ParseErrors=$(@($errors).Count)" })
    $checks.Add([pscustomobject]@{ Name = "Mixed manifest protocol"; Passed = $manifest.protocolVersion -eq "WAREHOUSE_BENCHMARK_V2_1_MIXED" -and $manifest.protocolSemanticVersion -eq "2.1.1"; Detail = "$($manifest.protocolVersion)/$($manifest.protocolSemanticVersion)" })
    $checks.Add([pscustomobject]@{ Name = "Six real scenarios declared"; Passed = @($manifest.scenarioCatalog).Count -eq 6; Detail = "Count=$(@($manifest.scenarioCatalog).Count)" })
    $checks.Add([pscustomobject]@{ Name = "Four exact mixed levels declared"; Passed = @($manifest.mixedLevels).Count -eq 4; Detail = "Count=$(@($manifest.mixedLevels).Count)" })
    $checks.Add([pscustomobject]@{ Name = "Worker totals are 6/12/24/48"; Passed = (@($manifest.mixedLevels | ForEach-Object simulatedConcurrentWorkers) -join ",") -eq "6,12,24,48"; Detail = (@($manifest.mixedLevels | ForEach-Object simulatedConcurrentWorkers) -join ",") })
    $checks.Add([pscustomobject]@{ Name = "No BDN mixed profile"; Passed = -not [bool]$manifest.execution.benchmarkDotNet; Detail = [string]$manifest.execution.benchmarkDotNet })
    $result = [ordered]@{
        Result = if (@($checks | Where-Object { -not $_.Passed }).Count -eq 0) { "PASS" } else { "FAIL" }
        CapturedAtUtc = [DateTime]::UtcNow
        Checks = @($checks)
    }
    if (-not [bool]($result.Result -eq "PASS")) {
        throw "MIXED_STATIC_SELF_TEST_FAILED"
    }
    if (-not [string]::IsNullOrWhiteSpace($script:PhaseRoot)) {
        Write-JsonFile (Join-Path $script:PhaseRoot "mixed-static-validation.json") $result
    }
    return [pscustomobject]$result
}

function Invoke-Validation([string]$Label) {
    $outputPath = Join-Path $script:PhaseRoot "mixed-validation-$Label.json"
    $directory = Split-Path -Parent $outputPath
    $result = Invoke-V2Runtime @("--validate", "--output", $outputPath) $directory "validation-$Label.stdout.txt" "validation-$Label.stderr.txt"
    if ($result.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
        throw "VALIDATION_FAILED|$Label|ExitCode=$($result.ExitCode)"
    }
    $json = Get-JsonValue $outputPath
    $checks = @($json.Consistency)
    $consistency = [ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow
        Source = "mixed-validation-$Label.json"
        CheckCount = $checks.Count
        Checks = $checks
        AllZero = $checks.Count -eq 11 -and @($checks | Where-Object { [int64]$_.MismatchCount -ne 0 }).Count -eq 0
    }
    Write-JsonFile (Join-Path $script:PhaseRoot "mixed-consistency-$Label.json") $consistency
    if (-not [bool]$json.Passed -or -not [bool]$consistency.AllZero) {
        throw "CORRECTNESS_FAILURE|Validation=$Label"
    }
    return $json
}

function Get-TextFailureType([string]$Text) {
    if ($Text -match "(?i)RESOURCE_SEMAPHORE|8645|8651|8657") { return "RESOURCE_SEMAPHORE" }
    if ($Text -match "(?i)deadlock|1205") { return "DEADLOCK" }
    if ($Text -match "(?i)timeout|timed out|error -2") { return "SQL_TIMEOUT" }
    if ($Text -match "(?i)blocking") { return "BLOCKING_REGRESSION" }
    if ($Text -match "(?i)target_db_mismatch") { return "TARGET_DB_MISMATCH" }
    return "PRODUCT_ERROR"
}

function Find-NBomberCsv([string]$Directory, [string]$Scenario) {
    foreach ($file in @(Get-ChildItem -LiteralPath $Directory -Recurse -File -Filter "*.csv" | Sort-Object LastWriteTime -Descending)) {
        try {
            $rows = @(Import-Csv -LiteralPath $file.FullName)
            foreach ($row in $rows) {
                $scenarioText = Get-PropertyText $row @("scenario", "Scenario")
                $hasRequestCount = $null -ne $row.PSObject.Properties["request_count"] -or $null -ne $row.PSObject.Properties["RequestCount"]
                if ($hasRequestCount -and $scenarioText -eq $Scenario) {
                    return [pscustomobject]@{
                        Path = $file.FullName
                        RequestCount = Get-PropertyNumber $row @("request_count", "RequestCount")
                        Success = Get-PropertyNumber $row @("ok", "Ok")
                        Failed = Get-PropertyNumber $row @("failed", "Failed")
                        MeanMs = Get-PropertyNumber $row @("ok_mean", "OkMean")
                        P50Ms = Get-PropertyNumber $row @("ok_50_percent", "Ok50Percent")
                        P75Ms = Get-PropertyNumber $row @("ok_75_percent", "Ok75Percent")
                        P95Ms = Get-PropertyNumber $row @("ok_95_percent", "Ok95Percent")
                        P99Ms = Get-PropertyNumber $row @("ok_99_percent", "Ok99Percent")
                        MaxMs = Get-PropertyNumber $row @("ok_max", "OkMax")
                        RPS = Get-PropertyNumber $row @("ok_rps", "OkRps")
                    }
                }
            }
        } catch {
        }
    }
    return $null
}

function Convert-LogTimestamp([string]$Text) {
    try {
        return ([DateTimeOffset]::Parse($Text, [Globalization.CultureInfo]::InvariantCulture)).UtcDateTime
    } catch {
        return $null
    }
}

function Get-NBomberWindow(
    [string]$Directory,
    [DateTime]$LaunchUtc,
    [int]$ConfiguredCopies
) {
    $combined = ""
    $evidenceFiles = @(Get-ChildItem -LiteralPath $Directory -Recurse -File -Filter "nbomber-log-*.txt")
    $stdoutEvidencePath = Join-Path $Directory "NBOMBER.stdout.txt"
    if (Test-Path -LiteralPath $stdoutEvidencePath -PathType Leaf) {
        $evidenceFiles += Get-Item -LiteralPath $stdoutEvidencePath
    }
    foreach ($file in @($evidenceFiles | Sort-Object LastWriteTime)) {
        $combined += (Get-Content -LiteralPath $file.FullName -Raw) + [Environment]::NewLine
    }
    $start = $null
    $stop = $null
    $startMatch = [regex]::Match($combined, "(?m)^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{1,7} [+-]\d{2}:\d{2}) \[INF\].*Starting bombing")
    $stopMatch = [regex]::Match($combined, "(?m)^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{1,7} [+-]\d{2}:\d{2}) \[INF\].*Stopping scenarios")
    if ($startMatch.Success) {
        $start = Convert-LogTimestamp $startMatch.Groups["ts"].Value
    }
    if ($stopMatch.Success) {
        $stop = Convert-LogTimestamp $stopMatch.Groups["ts"].Value
    }
    $copiesMatch = [regex]::Match($combined, "(?is)load simulations:.*?copies:\s*(?<copies>\d+)")
    $observedCopies = if ($copiesMatch.Success) { [int]$copiesMatch.Groups["copies"].Value } else { 0 }
    $windowSeconds = if ($null -ne $start -and $null -ne $stop) { [math]::Round(($stop - $start).TotalSeconds, 3) } else { 0.0 }
    return [pscustomobject]@{
        EvidenceFound = $null -ne $start -and $null -ne $stop
        MeasuredStartUtc = if ($null -ne $start) { $start.ToString("O") } else { "" }
        MeasuredStopUtc = if ($null -ne $stop) { $stop.ToString("O") } else { "" }
        WindowSeconds = $windowSeconds
        ObservedCopies = $observedCopies
        CopiesMatch = $observedCopies -eq $ConfiguredCopies
        LogEvidenceFiles = @($evidenceFiles | ForEach-Object FullName)
        LaunchUtc = $LaunchUtc.ToString("O")
    }
}

function Get-NBomberFailureType(
    [object]$ChildResult,
    [object]$Artifact,
    [object]$TelemetrySummary
) {
    $text = [string]$ChildResult.Stdout + [Environment]::NewLine + [string]$ChildResult.Stderr
    if ($null -ne $Artifact -and (Get-Number $Artifact.Failed) -gt 0) {
        $textFailure = Get-TextFailureType $text
        if ($textFailure -ne "PRODUCT_ERROR") {
            return $textFailure
        }
        if ($null -ne $TelemetrySummary -and [int64]$TelemetrySummary.PendingMemoryGrantsMax -gt 0) {
            return "RESOURCE_SEMAPHORE"
        }
        return "PRODUCT_ERROR"
    }
    if ($ChildResult.ExitCode -ne 0) {
        return Get-TextFailureType $text
    }
    return ""
}

function Get-SqlTelemetrySummary([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [pscustomobject]@{
            Rows = 0; TargetRows = 0; PendingMemoryGrantsMax = 0; ResourceSemaphoreWaitersMax = 0; BlockingRequestsMax = 0; DeadlocksMax = 0; LogicalReadsMax = 0; TempdbUsedKBMax = 0
        }
    }
    $rows = @(Import-Csv -LiteralPath $Path)
    $validRows = @($rows | Where-Object { $_.DatabaseName -ne "TELEMETRY_ERROR" })
    $targetRows = @($validRows | Where-Object { $_.DatabaseName -eq $script:TargetDatabase -and [int]$_.DatabaseId -eq $script:TargetDatabaseId })
    if ($targetRows.Count -eq 0 -and $validRows.Count -gt 0) {
        $script:HarnessFailureObserved = $true
    }
    function Get-MaxField([object[]]$InputRows, [string]$Name) {
        $numbers = @($InputRows | ForEach-Object { Get-PropertyNumber $_ @($Name) } | Where-Object { $null -ne $_ })
        if ($numbers.Count -eq 0) { return 0.0 }
        return [math]::Round(([double]($numbers | Measure-Object -Maximum).Maximum), 1)
    }
    return [pscustomobject]@{
        Rows = $rows.Count
        TargetRows = $targetRows.Count
        PendingMemoryGrantsMax = [int64](Get-MaxField $targetRows "PendingMemoryGrants")
        ResourceSemaphoreWaitersMax = [int64](Get-MaxField $targetRows "ResourceSemaphoreWaiters")
        BlockingRequestsMax = [int64](Get-MaxField $targetRows "BlockingRequests")
        DeadlocksMax = [int64](Get-MaxField $targetRows "DeadlockCounter")
        LogicalReadsMax = [int64](Get-MaxField $targetRows "LogicalReads")
        TempdbUsedKBMax = [int64](Get-MaxField $targetRows "TempdbUsedKB")
    }
}

function Merge-SqlTelemetry([string]$Source) {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        return
    }
    $lines = @(Get-Content -LiteralPath $Source)
    if ($lines.Count -gt 1) {
        [IO.File]::AppendAllText(
            (Join-Path $script:PhaseRoot "mixed-sql-telemetry.csv"),
            [Environment]::NewLine + ($lines[1..($lines.Count - 1)] -join [Environment]::NewLine),
            [Text.UTF8Encoding]::new($false))
    }
}

function Stop-Telemetry([object]$TelemetryHandle, [string]$SourcePath) {
    if ($null -eq $TelemetryHandle) {
        $script:HarnessFailureObserved = $true
        return
    }
    if (-not (Test-ProcessExited $TelemetryHandle.Process)) {
        Stop-ProcessTree ([int]$TelemetryHandle.Process.Id)
    }
    try {
        [void](Complete-LoggedProcess $TelemetryHandle)
    } catch {
        $script:HarnessFailureObserved = $true
    }
    [void]$script:ActiveTelemetryPids.Remove([int]$TelemetryHandle.Process.Id)
    Merge-SqlTelemetry $SourcePath
}

function Get-Admission(
    [string]$LevelId,
    [double]$PreviousDropMb,
    [bool]$IsFirst
) {
    $snapshot = Get-HostSnapshot
    $startMb = [double]$snapshot.FreePhysicalRamMb
    $predictedDrop = if ($IsFirst) { 0.0 } else { [math]::Round([math]::Max($PreviousDropMb * 1.25, $PreviousDropMb + 64.0), 1) }
    $predictedMinimum = [math]::Round($startMb - $predictedDrop, 1)
    $allowed = $startMb - $predictedDrop -gt $script:SafetyMinimumMb
    $reason = if ($allowed) { "Observed free RAM minus previous-level conservative prediction remains above 640 MB." } else { "Admission denied: predicted minimum free RAM is not above 640 MB." }
    $admission = [ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow
        Level = $LevelId
        StartAvailableMB = $startMb
        ActualDropPreviousLevel = if ($IsFirst) { 0.0 } else { [math]::Round($PreviousDropMb, 1) }
        PredictedNextDrop = $predictedDrop
        PredictedMinimumAvailableMB = $predictedMinimum
        SafetyMarginMB = $script:EmergencyMarginMb
        HardFloorMB = $script:HardFloorMb
        PredictedSafeMinimumMB = $script:SafetyMinimumMb
        Allowed = $allowed
        Reason = $reason
    }
    Write-JsonFile (Join-Path $script:PhaseRoot "levels\$LevelId-admission.json") $admission
    Add-CsvRow (Join-Path $script:PhaseRoot "mixed-host-telemetry.csv") ([pscustomobject]@{
        Level = $LevelId
        SampleKind = "ADMISSION"
        TimestampUtc = $snapshot.TimestampUtc
        FreePhysicalRamMb = $snapshot.FreePhysicalRamMb
        TotalPhysicalRamMb = $snapshot.TotalPhysicalRamMb
        CommitUsedMb = $snapshot.CommitUsedMb
        CommitLimitMb = $snapshot.CommitLimitMb
        ChildPrivateMemoryMb = 0
        ChildWorkingSetMb = 0
        SqlServerWorkingSetMb = $snapshot.SqlServerWorkingSetMb
        CpuPercent = $snapshot.CpuPercent
        ProcessCount = $snapshot.ProcessCount
        ActiveBenchmarkProcesses = 0
        HardFloorMb = $script:HardFloorMb
        EmergencyMarginMb = $script:EmergencyMarginMb
        PredictedSafeMinimumMb = $predictedMinimum
    })
    return [pscustomobject]$admission
}

function Wait-Cooldown([string]$LevelId) {
    Start-Sleep -Seconds $script:CooldownMinimumSeconds
    $deadline = [DateTime]::UtcNow.AddSeconds($script:CooldownMaximumSeconds)
    $probe = 0
    while ([DateTime]::UtcNow -lt $deadline) {
        $probe++
        $cooldownSample = Add-HostSample $LevelId "COOLDOWN" @() $script:SafetyMinimumMb
        $residue = Capture-Residue "cooldown-$LevelId-$probe"
        $benchmarkProcesses = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
            $_.Name -eq "dotnet.exe" -and $_.CommandLine -match [regex]::Escape($script:MainDllName) -and $_.CommandLine -match "--nbomber|--telemetry"
        }).Count
        $safe = [double]$cooldownSample.Snapshot.FreePhysicalRamMb -gt $script:SafetyMinimumMb
        $clean = $null -ne $residue -and [bool]$residue.Passed -and [int64]$residue.ActiveRequests -eq 0 -and [int64]$residue.BlockingRequests -eq 0 -and [int64]$residue.PendingMemoryGrants -eq 0 -and [int64]$residue.ResourceSemaphoreWaiters -eq 0 -and [int64]$residue.OpenTransactions -eq 0 -and [int64]$residue.ApplicationLocks -eq 0
        if ($safe -and $clean -and $benchmarkProcesses -eq 0) {
            return $true
        }
        Start-Sleep -Seconds 2
    }
    $script:HarnessFailureObserved = $true
    return $false
}

function Invoke-MixedLevel(
    [int]$WorkersPerScenario,
    [double]$PreviousDropMb,
    [bool]$IsFirst
) {
    $levelId = "MIXED-L$WorkersPerScenario"
    $totalWorkers = $WorkersPerScenario * $script:Scenarios.Count
    $levelDirectory = Join-Path $script:PhaseRoot "levels\$levelId"
    New-Item -ItemType Directory -Path $levelDirectory -Force | Out-Null
    $admission = Get-Admission $levelId $PreviousDropMb $IsFirst
    if (-not [bool]$admission.Allowed) {
        $script:HostCapacityObserved = $true
        $resultName = if ($WorkersPerScenario -eq 8) { "MIXED_L8_HOST_CAPACITY_LIMIT" } else { "HOST_CAPACITY_LIMIT" }
        $summary = [pscustomobject]@{
            Level = $levelId
            TotalWorkers = $totalWorkers
            WorkersPerScenario = $WorkersPerScenario
            Executed = $false
            Admission = "DENIED"
            StartAvailableMB = $admission.StartAvailableMB
            PreviousDropMB = $admission.ActualDropPreviousLevel
            PredictedNextDropMB = $admission.PredictedNextDrop
            PredictedMinimumMB = $admission.PredictedMinimumAvailableMB
            TotalRequests = 0
            Success = 0
            Failed = 0
            Timeout = 0
            AggregateRPS = 0
            MinRAMMB = $admission.StartAvailableMB
            CPUPeak = 0
            SQLWorkingSetPeakMB = 0
            BenchmarkWorkingSetPeakMB = 0
            PendingGrants = 0
            RSWaiters = 0
            BlockedRequests = 0
            Deadlocks = 0
            LogicalReadsMax = 0
            TempdbUsedKBMax = 0
            WindowOverlapSeconds = 0
            Result = $resultName
            FailureType = "HOST_CAPACITY_LIMIT"
            Notes = "Level not started because adaptive admission predicted an unsafe free-RAM floor. Lower completed levels remain valid."
        }
        Add-CsvRow (Join-Path $script:PhaseRoot "mixed-level-summary.csv") $summary
        $script:LevelRows.Add($summary)
        return $summary
    }

    $children = [System.Collections.Generic.List[object]]::new()
    $telemetryHandle = $null
    $telemetryPath = Join-Path $levelDirectory "sql-telemetry.csv"
    $launchStartUtc = [DateTime]::UtcNow
    $startError = ""
    try {
        try {
            $telemetryHandle = Start-LoggedProcess $script:DotnetPath @(
                $script:MainDll,
                "--telemetry",
                "--target-pid", [string][Diagnostics.Process]::GetCurrentProcess().Id,
                "--block", $levelId,
                "--output", $telemetryPath
            ) $script:RepoRoot (Join-Path $levelDirectory "telemetry.stdout.txt") (Join-Path $levelDirectory "telemetry.stderr.txt") (Get-ChildEnvironment $levelDirectory 1)
            [void]$script:ActiveTelemetryPids.Add([int]$telemetryHandle.Process.Id)
        } catch {
            $startError = "TelemetryStart: $($_.Exception.Message)"
        }
        foreach ($scenario in $script:Scenarios) {
            $scenarioDirectory = Join-Path $levelDirectory $scenario
            New-Item -ItemType Directory -Path $scenarioDirectory -Force | Out-Null
            $childHandle = Start-LoggedProcess $script:DotnetPath @(
                $script:MainDll,
                "--nbomber",
                "--scenario", $scenario,
                "--output", $scenarioDirectory
            ) $script:RepoRoot (Join-Path $scenarioDirectory "NBOMBER.stdout.txt") (Join-Path $scenarioDirectory "NBOMBER.stderr.txt") (Get-ChildEnvironment $scenarioDirectory $WorkersPerScenario)
            [void]$script:ActiveRootPids.Add([int]$childHandle.Process.Id)
            $children.Add([pscustomobject]@{
                Scenario = $scenario
                Handle = $childHandle
                ProcessId = [int]$childHandle.Process.Id
                Directory = $scenarioDirectory
                ConfiguredCopies = $WorkersPerScenario
                LaunchUtc = $childHandle.StartedUtc
                Result = $null
            })
        }
    } catch {
        $startError = if ([string]::IsNullOrWhiteSpace($startError)) { $_.Exception.Message } else { "$startError; $($_.Exception.Message)" }
    }
    $launchEndUtc = [DateTime]::UtcNow
    $state = [ordered]@{
        StartFreeMb = [double]$admission.StartAvailableMB
        MinFreeMb = [double]$admission.StartAvailableMB
        CpuPeak = 0.0
        SqlWorkingSetPeakMb = 0.0
        BenchmarkWorkingSetPeakMb = 0.0
        BenchmarkPrivatePeakMb = 0.0
        HostSamples = 0
    }
    $hardStop = $false
    $runnerTimeout = $false
    $activeWindowStartUtc = [DateTime]::UtcNow
    while (@($children | Where-Object { -not (Test-ProcessExited $_.Handle.Process) }).Count -gt 0) {
        try {
            $sample = Add-HostSample $levelId "ACTIVE" @($children) $admission.PredictedMinimumAvailableMB
            $state.HostSamples++
            $state.MinFreeMb = [math]::Min($state.MinFreeMb, [double]$sample.Snapshot.FreePhysicalRamMb)
            $state.CpuPeak = [math]::Max($state.CpuPeak, [double]$sample.Snapshot.CpuPercent)
            $state.SqlWorkingSetPeakMb = [math]::Max($state.SqlWorkingSetPeakMb, [double]$sample.Snapshot.SqlServerWorkingSetMb)
            $state.BenchmarkWorkingSetPeakMb = [math]::Max($state.BenchmarkWorkingSetPeakMb, [double]$sample.ChildWorkingSetMb)
        } catch {
            $script:HarnessFailureObserved = $true
            $startError = "$startError; HostTelemetry: $($_.Exception.Message)"
        }
        if ($state.MinFreeMb -le $script:HardFloorMb) {
            $hardStop = $true
            $script:HostCapacityObserved = $true
            foreach ($child in $children) {
                if (-not (Test-ProcessExited $child.Handle.Process)) {
                    Stop-ProcessTree ([int]$child.ProcessId)
                }
            }
            break
        }
        if ([DateTime]::UtcNow -gt $activeWindowStartUtc.AddSeconds($script:MaximumLevelSeconds)) {
            $runnerTimeout = $true
            $script:HarnessFailureObserved = $true
            foreach ($child in $children) {
                if (-not (Test-ProcessExited $child.Handle.Process)) {
                    Stop-ProcessTree ([int]$child.ProcessId)
                }
            }
            break
        }
        Start-Sleep -Milliseconds 500
    }
    try {
        $finalSample = Add-HostSample $levelId "FINAL" @($children) $admission.PredictedMinimumAvailableMB
        $state.MinFreeMb = [math]::Min($state.MinFreeMb, [double]$finalSample.Snapshot.FreePhysicalRamMb)
        $state.CpuPeak = [math]::Max($state.CpuPeak, [double]$finalSample.Snapshot.CpuPercent)
        $state.SqlWorkingSetPeakMb = [math]::Max($state.SqlWorkingSetPeakMb, [double]$finalSample.Snapshot.SqlServerWorkingSetMb)
        $state.BenchmarkWorkingSetPeakMb = [math]::Max($state.BenchmarkWorkingSetPeakMb, [double]$finalSample.ChildWorkingSetMb)
    } catch {
        $script:HarnessFailureObserved = $true
    }
    foreach ($child in $children) {
        try {
            $child.Result = Complete-LoggedProcess $child.Handle
        } catch {
            $child.Result = [pscustomobject]@{ ExitCode = -1; Stdout = ""; Stderr = $_.Exception.Message; StartedUtc = $child.LaunchUtc; FinishedUtc = [DateTime]::UtcNow; ProcessId = $child.ProcessId }
            $script:HarnessFailureObserved = $true
        }
        [void]$script:ActiveRootPids.Remove([int]$child.ProcessId)
    }
    Stop-Telemetry $telemetryHandle $telemetryPath
    $telemetrySummary = Get-SqlTelemetrySummary $telemetryPath
    if ($telemetrySummary.Rows -eq 0) {
        $script:HarnessFailureObserved = $true
    }

    $workerRows = [System.Collections.Generic.List[object]]::new()
    foreach ($child in $children) {
        $artifact = Find-NBomberCsv $child.Directory $child.Scenario
        $window = Get-NBomberWindow $child.Directory $child.LaunchUtc $child.ConfiguredCopies
        $failureType = Get-NBomberFailureType $child.Result $artifact $telemetrySummary
        $requests = if ($null -ne $artifact -and $null -ne $artifact.RequestCount) { [int64]$artifact.RequestCount } else { 0 }
        $success = if ($null -ne $artifact -and $null -ne $artifact.Success) { [int64]$artifact.Success } else { 0 }
        $failed = if ($null -ne $artifact -and $null -ne $artifact.Failed) { [int64]$artifact.Failed } else { 0 }
        $timeout = if ($failureType -eq "SQL_TIMEOUT") { $failed } else { 0 }
        $result = if ($null -eq $artifact -or $requests -le 0) { "HARNESS_FAILURE" } elseif ($child.Result.ExitCode -ne 0 -or $failed -gt 0) { $failureType } else { "PASS" }
        if (-not $window.EvidenceFound -or -not $window.CopiesMatch) {
            $result = "MIXED_HARNESS_CONCURRENCY_INVALID"
            $failureType = "MIXED_HARNESS_CONCURRENCY_INVALID"
        }
        $row = [pscustomobject]@{
            Level = $levelId
            TotalWorkers = $totalWorkers
            WorkersPerScenario = $WorkersPerScenario
            Scenario = $child.Scenario
            Requests = $requests
            Success = $success
            Failed = $failed
            Timeout = $timeout
            MeanMs = if ($null -ne $artifact) { $artifact.MeanMs } else { $null }
            P50Ms = if ($null -ne $artifact) { $artifact.P50Ms } else { $null }
            P75Ms = if ($null -ne $artifact) { $artifact.P75Ms } else { $null }
            P95Ms = if ($null -ne $artifact) { $artifact.P95Ms } else { $null }
            P99Ms = if ($null -ne $artifact) { $artifact.P99Ms } else { $null }
            MaxMs = if ($null -ne $artifact) { $artifact.MaxMs } else { $null }
            RPS = if ($null -ne $artifact) { $artifact.RPS } else { $null }
            Result = $result
            FailureType = $failureType
            ConfiguredCopies = $child.ConfiguredCopies
            ObservedCopies = $window.ObservedCopies
            LaunchUtc = $child.LaunchUtc.ToString("O")
            MeasuredStartUtc = $window.MeasuredStartUtc
            MeasuredStopUtc = $window.MeasuredStopUtc
            WindowSeconds = $window.WindowSeconds
            Notes = if ($null -ne $artifact) { "NBomber 6.6.0 raw CSV; one child process contains exactly one scenario." } else { "No scenario result CSV with request_count was found." }
        }
        $workerRows.Add($row)
        $script:PerScenarioRows.Add($row)
    }
    $windows = @($workerRows | Where-Object { $_.MeasuredStartUtc -and $_.MeasuredStopUtc })
    $latestStart = $null
    $earliestStop = $null
    if ($windows.Count -eq 6) {
        $latestStart = ($windows | ForEach-Object { [DateTime]$_.MeasuredStartUtc } | Sort-Object -Descending | Select-Object -First 1)
        $earliestStop = ($windows | ForEach-Object { [DateTime]$_.MeasuredStopUtc } | Sort-Object | Select-Object -First 1)
    }
    $overlapSeconds = if ($null -ne $latestStart -and $null -ne $earliestStop) { [math]::Round(($earliestStop - $latestStart).TotalSeconds, 3) } else { 0.0 }
    $allWindowValid = $windows.Count -eq 6 -and $overlapSeconds -ge $script:MinimumMeaningfulOverlapSeconds -and @($workerRows | Where-Object { [int]$_.ObservedCopies -ne $WorkersPerScenario }).Count -eq 0
    if ($hardStop) {
        $levelResult = "HOST_CAPACITY_LIMIT"
        $levelFailureType = "HOST_CAPACITY_LIMIT"
    } elseif ($runnerTimeout) {
        $levelResult = "HARNESS_FAILURE"
        $levelFailureType = "HARNESS_TIMEOUT"
    } elseif (-not $allWindowValid) {
        $levelResult = "MIXED_HARNESS_CONCURRENCY_INVALID"
        $levelFailureType = "MIXED_HARNESS_CONCURRENCY_INVALID"
    } elseif (@($workerRows | Where-Object { $_.Result -ne "PASS" }).Count -gt 0) {
        $levelResult = ([string](@($workerRows | Where-Object { $_.Result -ne "PASS" } | Select-Object -First 1).Result))
        $levelFailureType = ([string](@($workerRows | Where-Object { $_.Result -ne "PASS" } | Select-Object -First 1).FailureType))
    } elseif ($WorkersPerScenario -eq 8) {
        $levelResult = "MIXED_L8_STANDALONE_CAPACITY_RESULT"
        $levelFailureType = ""
    } else {
        $levelResult = "PASS"
        $levelFailureType = ""
    }
    if ($levelResult -eq "MIXED_HARNESS_CONCURRENCY_INVALID") {
        $script:HarnessFailureObserved = $true
    }
    if ($levelFailureType -in @("PRODUCT_ERROR", "SQL_TIMEOUT", "DEADLOCK", "RESOURCE_SEMAPHORE", "BLOCKING_REGRESSION")) {
        $script:ProductFailureObserved = $true
    }
    if ($hardStop) {
        foreach ($row in $workerRows | Where-Object { $_.Result -eq "PASS" }) {
            $row.Result = "HOST_CAPACITY_LIMIT"
            $row.FailureType = "HOST_CAPACITY_LIMIT"
        }
    } elseif (-not $allWindowValid) {
        foreach ($row in $workerRows | Where-Object { $_.Result -eq "PASS" }) {
            $row.Result = "MIXED_HARNESS_CONCURRENCY_INVALID"
            $row.FailureType = "MIXED_HARNESS_CONCURRENCY_INVALID"
        }
    }
    foreach ($row in $workerRows) {
        $row | Out-Null
    }
    $sumRequests = [int64](@($workerRows | Measure-Object Requests -Sum).Sum)
    $sumSuccess = [int64](@($workerRows | Measure-Object Success -Sum).Sum)
    $sumFailed = [int64](@($workerRows | Measure-Object Failed -Sum).Sum)
    $sumTimeout = [int64](@($workerRows | Measure-Object Timeout -Sum).Sum)
    $rpsNumbers = @($workerRows | ForEach-Object { Get-Number $_.RPS } | Where-Object { $null -ne $_ })
    $aggregateRps = if ($rpsNumbers.Count -gt 0) { [math]::Round(([double]($rpsNumbers | Measure-Object -Sum).Sum), 2) } else { 0.0 }
    $summary = [pscustomobject]@{
        Level = $levelId
        TotalWorkers = $totalWorkers
        WorkersPerScenario = $WorkersPerScenario
        Executed = $true
        Admission = "ALLOWED"
        StartAvailableMB = [math]::Round([double]$admission.StartAvailableMB, 1)
        PreviousDropMB = [math]::Round([double]$admission.ActualDropPreviousLevel, 1)
        PredictedNextDropMB = [math]::Round([double]$admission.PredictedNextDrop, 1)
        PredictedMinimumMB = [math]::Round([double]$admission.PredictedMinimumAvailableMB, 1)
        TotalRequests = $sumRequests
        Success = $sumSuccess
        Failed = $sumFailed
        Timeout = $sumTimeout
        AggregateRPS = $aggregateRps
        MinRAMMB = [math]::Round([double]$state.MinFreeMb, 1)
        CPUPeak = [math]::Round([double]$state.CpuPeak, 1)
        SQLWorkingSetPeakMB = [math]::Round([double]$state.SqlWorkingSetPeakMb, 1)
        BenchmarkWorkingSetPeakMB = [math]::Round([double]$state.BenchmarkWorkingSetPeakMb, 1)
        PendingGrants = $telemetrySummary.PendingMemoryGrantsMax
        RSWaiters = $telemetrySummary.ResourceSemaphoreWaitersMax
        BlockedRequests = $telemetrySummary.BlockingRequestsMax
        Deadlocks = $telemetrySummary.DeadlocksMax
        LogicalReadsMax = $telemetrySummary.LogicalReadsMax
        TempdbUsedKBMax = $telemetrySummary.TempdbUsedKBMax
        WindowOverlapSeconds = $overlapSeconds
        Result = $levelResult
        FailureType = $levelFailureType
        Notes = if ([string]::IsNullOrWhiteSpace($startError)) { "Six NBomber child processes launched in one coordinated batch; no hidden multiplier." } else { $startError }
    }
    foreach ($row in $workerRows) {
        Add-CsvRow (Join-Path $script:PhaseRoot "mixed-per-scenario.csv") $row
    }
    Add-CsvRow (Join-Path $script:PhaseRoot "mixed-level-summary.csv") $summary
    $childMetadata = @($children | ForEach-Object {
        $scenarioName = $_.Scenario
        $workerRow = $workerRows | Where-Object { $_.Scenario -eq $scenarioName } | Select-Object -First 1
        [ordered]@{
            Scenario = $scenarioName
            ProcessId = $_.ProcessId
            ConfiguredCopies = $_.ConfiguredCopies
            LaunchUtc = $_.LaunchUtc
            ExitCode = $_.Result.ExitCode
            FinishedUtc = $_.Result.FinishedUtc
            Result = $workerRow
        }
    })
    Write-JsonFile (Join-Path $levelDirectory "mixed-level-metadata.json") ([ordered]@{
        Level = $levelId
        WorkersPerScenario = $WorkersPerScenario
        SimulatedConcurrentWorkers = $totalWorkers
        ScenarioCount = 6
        LaunchStartUtc = $launchStartUtc
        LaunchEndUtc = $launchEndUtc
        LaunchSpreadSeconds = [math]::Round(($launchEndUtc - $launchStartUtc).TotalSeconds, 3)
        MeasuredWindowOverlapSeconds = $overlapSeconds
        MinimumMeaningfulOverlapSeconds = $script:MinimumMeaningfulOverlapSeconds
        AllSixMeasuredWindowsOverlap = $allWindowValid
        HardStop = $hardStop
        RunnerTimeout = $runnerTimeout
        Admission = $admission
        HostState = $state
        SqlTelemetry = $telemetrySummary
        Children = $childMetadata
        PerScenario = @($workerRows)
        Result = $levelResult
        FailureType = $levelFailureType
    })
    $script:LevelRows.Add($summary)
    $script:ExecutedLevels.Add($summary)
    return $summary
}

function Get-CoreBaselineRows {
    $path = Join-Path $script:CoreBaselineRoot "v2.1-nbomber-summary.csv"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $path = Join-Path $script:CoreBaselinePublished "v2.1-nbomber-summary.csv"
    }
    $rows = @(Import-Csv -LiteralPath $path | Where-Object { $_.Profile -in @("CORE_NBOMBER", "EXTENDED_STANDARD") -and $_.Status -eq "PASS" })
    if ($rows.Count -lt 18) {
        throw "CORE_BASELINE_ROWS_MISSING|$($rows.Count)"
    }
    return $rows
}

function Build-Comparisons {
    $baselineRows = Get-CoreBaselineRows
    foreach ($mixed in @($script:PerScenarioRows)) {
        $levelNumber = [int]$mixed.WorkersPerScenario
        if ($levelNumber -notin @(1, 2, 4)) {
            continue
        }
        $baseline = $baselineRows | Where-Object { $_.Scenario -eq $mixed.Scenario -and [int]$_.Level -eq $levelNumber } | Select-Object -First 1
        $hasBaseline = $null -ne $baseline
        $comparison = [pscustomobject]@{
            Scenario = $mixed.Scenario
            Level = $levelNumber
            IsolatedMeanMs = if ($hasBaseline) { Get-PropertyNumber $baseline @("MeanMs") } else { $null }
            MixedMeanMs = Get-Number $mixed.MeanMs
            MeanDeltaPct = if ($hasBaseline) { Get-DeltaPercent (Get-PropertyNumber $baseline @("MeanMs")) $mixed.MeanMs } else { $null }
            IsolatedP95Ms = if ($hasBaseline) { Get-PropertyNumber $baseline @("P95Ms") } else { $null }
            MixedP95Ms = Get-Number $mixed.P95Ms
            P95DeltaPct = if ($hasBaseline) { Get-DeltaPercent (Get-PropertyNumber $baseline @("P95Ms")) $mixed.P95Ms } else { $null }
            IsolatedP99Ms = if ($hasBaseline) { Get-PropertyNumber $baseline @("P99Ms") } else { $null }
            MixedP99Ms = Get-Number $mixed.P99Ms
            P99DeltaPct = if ($hasBaseline) { Get-DeltaPercent (Get-PropertyNumber $baseline @("P99Ms")) $mixed.P99Ms } else { $null }
            IsolatedRPS = if ($hasBaseline) { Get-PropertyNumber $baseline @("Rps", "RPS") } else { $null }
            MixedRPS = Get-Number $mixed.RPS
            RPSDeltaPct = if ($hasBaseline) { Get-DeltaPercent (Get-PropertyNumber $baseline @("Rps", "RPS")) $mixed.RPS } else { $null }
            Result = if ($hasBaseline) { $mixed.Result } else { "NO_BASELINE" }
            Notes = "Descriptive same-level comparison only; no arbitrary pass threshold."
        }
        Add-CsvRow (Join-Path $script:PhaseRoot "mixed-comparison.csv") $comparison
        $script:ComparisonRows.Add($comparison)
    }
}

function Get-SaturationNarrative {
    $summaries = @($script:LevelRows | Where-Object { $_.Executed -eq $true -and $_.Result -eq "PASS" } | Sort-Object WorkersPerScenario)
    $signals = [System.Collections.Generic.List[string]]::new()
    for ($index = 1; $index -lt $summaries.Count; $index++) {
        $previous = $summaries[$index - 1]
        $current = $summaries[$index]
        if ([int]$current.WorkersPerScenario -ne ([int]$previous.WorkersPerScenario * 2)) {
            continue
        }
        $previousRps = Get-Number $previous.AggregateRPS
        $currentRps = Get-Number $current.AggregateRPS
        if ($null -ne $previousRps -and $previousRps -gt 0 -and $null -ne $currentRps) {
            $rpsGrowth = (($currentRps - $previousRps) / $previousRps) * 100.0
            foreach ($scenario in $script:Scenarios) {
                $old = @($script:PerScenarioRows | Where-Object { $_.Scenario -eq $scenario -and [int]$_.WorkersPerScenario -eq [int]$previous.WorkersPerScenario -and $_.Result -eq "PASS" } | Select-Object -First 1)
                $new = @($script:PerScenarioRows | Where-Object { $_.Scenario -eq $scenario -and [int]$_.WorkersPerScenario -eq [int]$current.WorkersPerScenario -and $_.Result -eq "PASS" } | Select-Object -First 1)
                if ($old.Count -eq 0 -or $new.Count -eq 0) { continue }
                $oldMean = Get-Number $old[0].MeanMs
                $newMean = Get-Number $new[0].MeanMs
                if ($null -ne $oldMean -and $oldMean -gt 0 -and $null -ne $newMean) {
                    $latencyGrowth = (($newMean - $oldMean) / $oldMean) * 100.0
                    if ($rpsGrowth -lt 25.0 -and $latencyGrowth -gt 25.0) {
                        $signals.Add("$($scenario): workers doubled $($previous.WorkersPerScenario)->$($current.WorkersPerScenario), aggregate RPS growth $([math]::Round($rpsGrowth, 1))%, mean latency growth $([math]::Round($latencyGrowth, 1))%; descriptive saturation signal.")
                    }
                }
            }
        }
    }
    if ($signals.Count -eq 0) {
        return "No documented saturation heuristic signal was observed among completed levels. This is not proof of unlimited scalability."
    }
    return ($signals -join " ")
}

function Get-UserAnswerRows {
    return @($script:PerScenarioRows | Sort-Object @{ Expression = { [int]$_.WorkersPerScenario } }, Scenario | ForEach-Object {
        [pscustomobject]@{
            Level = $_.Level
            TotalWorkers = $_.TotalWorkers
            Scenario = $_.Scenario
            MeanSec = if ($null -ne (Get-Number $_.MeanMs)) { [math]::Round((Get-Number $_.MeanMs) / 1000.0, 4) } else { $null }
            P95Sec = if ($null -ne (Get-Number $_.P95Ms)) { [math]::Round((Get-Number $_.P95Ms) / 1000.0, 4) } else { $null }
            P99Sec = if ($null -ne (Get-Number $_.P99Ms)) { [math]::Round((Get-Number $_.P99Ms) / 1000.0, 4) } else { $null }
            RPS = Get-Number $_.RPS
            Result = $_.Result
        }
    })
}

function Add-MarkdownTable([System.Collections.Generic.List[string]]$Lines, [string[]]$Headers, [object[]]$Rows) {
    [void]$Lines.Add("| " + ($Headers -join " | ") + " |")
    [void]$Lines.Add("| " + (($Headers | ForEach-Object { "---" }) -join " | ") + " |")
    foreach ($row in $Rows) {
        $values = foreach ($header in $Headers) {
            $property = $row.PSObject.Properties[$header]
            $value = if ($null -ne $property) { [string]$property.Value } else { "" }
            $value.Replace("|", "\\|").Replace("`r", " ").Replace("`n", " ")
        }
        [void]$Lines.Add("| " + ($values -join " | ") + " |")
    }
}

function Write-Report(
    [string]$ResultName,
    [object]$BeforeValidation,
    [object]$AfterValidation,
    [object]$BeforeGuard,
    [object]$AfterGuard,
    [object]$BeforeProduct,
    [object]$AfterProduct,
    [object]$BeforeAgent,
    [object]$AfterAgent,
    [bool]$CoreBaselineUnchanged,
    [string]$PostStateVerdict
) {
    $lines = [System.Collections.Generic.List[string]]::new()
    [void]$lines.Add("# Benchmark V2.1 All-Six Mixed Extension Report")
    [void]$lines.Add("")
    [void]$lines.Add("## 1. Executive summary")
    [void]$lines.Add("")
    [void]$lines.Add("Final mixed verdict: **$ResultName**")
    [void]$lines.Add("")
    [void]$lines.Add("This is the versioned `WAREHOUSE_BENCHMARK_V2_1_MIXED` / semantic version `2.1.1` extension. The accepted V2.1 isolated core baseline remains immutable and its results were not merged into the mixed CSV.")
    [void]$lines.Add("")
    [void]$lines.Add("The mixed workload uses six simultaneous real Data Access scenarios. One NBomber child process is one scenario, and `TKS_V2_NBOMBER_COPIES=L` is the configured number of real worker streams for that scenario.")
    [void]$lines.Add("")
    [void]$lines.Add("## 2. Frozen candidate and scope")
    [void]$lines.Add("")
    Add-MarkdownTable $lines @("Item", "Value") @(
        [pscustomobject]@{ Item = "Mixed runner SHA-256"; Value = $script:RunnerHash },
        [pscustomobject]@{ Item = "Mixed manifest SHA-256"; Value = $script:ManifestHash },
        [pscustomobject]@{ Item = "Accepted benchmark DLL SHA-256"; Value = $script:ExpectedCore.BenchmarkDllSha256 },
        [pscustomobject]@{ Item = "Data Access DLL SHA-256"; Value = $script:ExpectedCore.DataAccessDllSha256 },
        [pscustomobject]@{ Item = "Runtime bundle SHA-256"; Value = $script:RuntimeBundleHash },
        [pscustomobject]@{ Item = "Mixed control closure SHA-256"; Value = $script:MixedControlClosureHash },
        [pscustomobject]@{ Item = "Target"; Value = "$($script:TargetServer) / $($script:TargetDatabase) / DB_ID $($script:TargetDatabaseId)" },
        [pscustomobject]@{ Item = "Profile"; Value = "ADDITIONAL_PROFILE_ONLY; NBomber only; no BDN" }
    )
    [void]$lines.Add("")
    [void]$lines.Add("## 3. Contract and worker mapping")
    [void]$lines.Add("")
    Add-MarkdownTable $lines @("Level", "Workers per scenario", "SIMULATED_CONCURRENT_WORKERS", "Six scenarios concurrent") @(
        [pscustomobject]@{ Level = "MIXED-L1"; "Workers per scenario" = 1; SIMULATED_CONCURRENT_WORKERS = 6; "Six scenarios concurrent" = "Required" },
        [pscustomobject]@{ Level = "MIXED-L2"; "Workers per scenario" = 2; SIMULATED_CONCURRENT_WORKERS = 12; "Six scenarios concurrent" = "Required" },
        [pscustomobject]@{ Level = "MIXED-L4"; "Workers per scenario" = 4; SIMULATED_CONCURRENT_WORKERS = 24; "Six scenarios concurrent" = "Required" },
        [pscustomobject]@{ Level = "MIXED-L8"; "Workers per scenario" = 8; SIMULATED_CONCURRENT_WORKERS = 48; "Six scenarios concurrent" = "Required" }
    )
    [void]$lines.Add("")
    [void]$lines.Add("Parameters preserved from V2.1: page 1, page size 10, `PERF_USER`, 2025-01-01 through 2026-12-31, `UseSnapshot=false`, historical `UseCurrentBalance=false`, current `UseCurrentBalance=true`, application timeout 30 seconds, warmup 3 seconds, measured window 15 seconds, `Simulation.KeepConstant`, and no additional request pacing.")
    [void]$lines.Add("")
    [void]$lines.Add("## 4. Level summary")
    [void]$lines.Add("")
    Add-MarkdownTable $lines @("Level", "Total Workers", "Requests", "Success", "Failed", "Timeout", "Aggregate RPS", "Min RAM MB", "CPU Peak", "Pending Grants", "RS Waiters", "Deadlocks", "Overlap s", "Result") @(
        @($script:LevelRows | Sort-Object WorkersPerScenario | ForEach-Object {
            [pscustomobject]@{
                Level = $_.Level
                "Total Workers" = $_.TotalWorkers
                Requests = $_.TotalRequests
                Success = $_.Success
                Failed = $_.Failed
                Timeout = $_.Timeout
                "Aggregate RPS" = Format-Number $_.AggregateRPS 2
                "Min RAM MB" = Format-Number $_.MinRAMMB 1
                "CPU Peak" = Format-Number $_.CPUPeak 1
                "Pending Grants" = $_.PendingGrants
                "RS Waiters" = $_.RSWaiters
                Deadlocks = $_.Deadlocks
                "Overlap s" = Format-Number $_.WindowOverlapSeconds 3
                Result = $_.Result
            }
        })
    )
    [void]$lines.Add("")
    [void]$lines.Add("Aggregate latency percentiles are **NOT_COMPUTED** because averaging six P95/P99 values would be invalid. Percentiles below are per scenario.")
    [void]$lines.Add("")
    [void]$lines.Add("## 5. Required human-readable answer: how long is each page under mixed use?")
    [void]$lines.Add("")
    $secondsRows = @(Get-UserAnswerRows | ForEach-Object {
        [pscustomobject]@{
            Level = $_.Level
            "SimulatedConcurrentWorkers" = $_.TotalWorkers
            Scenario = $_.Scenario
            "Mean s" = Format-Number $_.MeanSec 4
            "P95 s" = Format-Number $_.P95Sec 4
            "P99 s" = Format-Number $_.P99Sec 4
            RPS = Format-Number $_.RPS 2
            Result = $_.Result
        }
    })
    Add-MarkdownTable $lines @("Level", "SimulatedConcurrentWorkers", "Scenario", "Mean s", "P95 s", "P99 s", "RPS", "Result") $secondsRows
    [void]$lines.Add("")
    [void]$lines.Add("Values are milliseconds divided by 1,000. A non-PASS level is retained as evidence but is not treated as a valid performance result.")
    [void]$lines.Add("")
    [void]$lines.Add("## 6. Raw per-scenario result table")
    [void]$lines.Add("")
    Add-MarkdownTable $lines @("Level", "TotalWorkers", "Scenario", "Requests", "Success", "Failed", "Timeout", "MeanMs", "P50Ms", "P75Ms", "P95Ms", "P99Ms", "MaxMs", "RPS", "Result") @(
        @($script:PerScenarioRows | Sort-Object @{ Expression = { [int]$_.WorkersPerScenario } }, Scenario | ForEach-Object {
            [pscustomobject]@{
                Level = $_.Level
                TotalWorkers = $_.TotalWorkers
                Scenario = $_.Scenario
                Requests = $_.Requests
                Success = $_.Success
                Failed = $_.Failed
                Timeout = $_.Timeout
                MeanMs = Format-Number $_.MeanMs 3
                P50Ms = Format-Number $_.P50Ms 3
                P75Ms = Format-Number $_.P75Ms 3
                P95Ms = Format-Number $_.P95Ms 3
                P99Ms = Format-Number $_.P99Ms 3
                MaxMs = Format-Number $_.MaxMs 3
                RPS = Format-Number $_.RPS 2
                Result = $_.Result
            }
        })
    )
    [void]$lines.Add("")
    [void]$lines.Add("## 7. Isolated V2.1 versus mixed comparison")
    [void]$lines.Add("")
    [void]$lines.Add("L1/L2/L4 are compared with isolated C1/C2/C4 respectively. L8 has no exact isolated C8 baseline and is classified as `MIXED_L8_STANDALONE_CAPACITY_RESULT` when measured.")
    [void]$lines.Add("")
    Add-MarkdownTable $lines @("Scenario", "Level", "Isolated Mean", "Mixed Mean", "Mean Delta %", "Isolated P95", "Mixed P95", "P95 Delta %", "Isolated P99", "Mixed P99", "P99 Delta %", "Isolated RPS", "Mixed RPS", "RPS Delta %", "Result") @(
        @($script:ComparisonRows | Sort-Object Level, Scenario | ForEach-Object {
            [pscustomobject]@{
                Scenario = $_.Scenario
                Level = $_.Level
                "Isolated Mean" = Format-Number $_.IsolatedMeanMs 3
                "Mixed Mean" = Format-Number $_.MixedMeanMs 3
                "Mean Delta %" = Format-Percent $_.MeanDeltaPct
                "Isolated P95" = Format-Number $_.IsolatedP95Ms 3
                "Mixed P95" = Format-Number $_.MixedP95Ms 3
                "P95 Delta %" = Format-Percent $_.P95DeltaPct
                "Isolated P99" = Format-Number $_.IsolatedP99Ms 3
                "Mixed P99" = Format-Number $_.MixedP99Ms 3
                "P99 Delta %" = Format-Percent $_.P99DeltaPct
                "Isolated RPS" = Format-Number $_.IsolatedRPS 2
                "Mixed RPS" = Format-Number $_.MixedRPS 2
                "RPS Delta %" = Format-Percent $_.RPSDeltaPct
                Result = $_.Result
            }
        })
    )
    [void]$lines.Add("")
    [void]$lines.Add("No arbitrary pass/fail threshold is applied to mixed penalties; the deltas are descriptive evidence.")
    [void]$lines.Add("")
    [void]$lines.Add("## 8. Synchronization and no-hidden-multiplier evidence")
    [void]$lines.Add("")
    $overlapRows = @($script:LevelRows | ForEach-Object {
        [pscustomobject]@{
            Level = $_.Level
            "Configured total workers" = $_.TotalWorkers
            "Measured overlap seconds" = Format-Number $_.WindowOverlapSeconds 3
            "Required overlap seconds" = $script:MinimumMeaningfulOverlapSeconds
            "Result" = $_.Result
        }
    })
    Add-MarkdownTable $lines @("Level", "Configured total workers", "Measured overlap seconds", "Required overlap seconds", "Result") $overlapRows
    [void]$lines.Add("")
    [void]$lines.Add("Every executed level required six scenario logs containing `Starting bombing` and `Stopping scenarios`, at least $($script:MinimumMeaningfulOverlapSeconds) seconds of common measured-window overlap, and observed NBomber `copies` equal to the configured workers per scenario.")
    [void]$lines.Add("")
    [void]$lines.Add("## 9. Host/resource behavior and interpretation")
    [void]$lines.Add("")
    [void]$lines.Add("Adaptive safety policy: hard floor $($script:HardFloorMb) MB, emergency margin $($script:EmergencyMarginMb) MB, predicted safe minimum above $($script:SafetyMinimumMb) MB, and 1,024 MB as ideal rather than a hard admission gate.")
    [void]$lines.Add("")
    [void]$lines.Add("Saturation inspection: $(Get-SaturationNarrative)")
    $passRows = @($script:PerScenarioRows | Where-Object { $_.Result -eq "PASS" })
    if ($passRows.Count -gt 0) {
        $firstBottleneck = $passRows | Sort-Object @{ Expression = { [int]$_.WorkersPerScenario } }, @{ Expression = { [double](Get-Number $_.MeanMs) }; Descending = $true } | Select-Object -First 1
        [void]$lines.Add("")
        [void]$lines.Add("First observed bottleneck by mean latency among completed PASS rows: **$($firstBottleneck.Scenario)** at $($firstBottleneck.Level), mean $(Format-Number $firstBottleneck.MeanMs 3) ms ($(Format-Number ((Get-Number $firstBottleneck.MeanMs) / 1000.0) 4) s).")
        $lastLevel = ($passRows | Sort-Object @{ Expression = { [int]$_.WorkersPerScenario }; Descending = $true } | Select-Object -First 1).WorkersPerScenario
        $scaleRows = @($passRows | Where-Object { [int]$_.WorkersPerScenario -eq [int]$lastLevel } | Sort-Object @{ Expression = { [double](Get-Number $_.MeanMs) } })
        if ($scaleRows.Count -gt 0) {
            [void]$lines.Add("At the highest completed level, the lowest mean latency was **$($scaleRows[0].Scenario)**; this is a descriptive within-run observation, not a cross-hardware scalability proof.")
        }
    }
    [void]$lines.Add("")
    [void]$lines.Add("## 10. Consistency, integrity, and database safety")
    [void]$lines.Add("")
    Add-MarkdownTable $lines @("Check", "Result") @(
        [pscustomobject]@{ Check = "Pre mixed validation"; Result = if ($null -ne $BeforeValidation) { [string]$BeforeValidation.Result } else { "N/A" } },
        [pscustomobject]@{ Check = "Post mixed validation"; Result = if ($null -ne $AfterValidation) { [string]$AfterValidation.Result } else { "N/A" } },
        [pscustomobject]@{ Check = "Pre/post consistency"; Result = "11 checks, all mismatch 0 required" },
        [pscustomobject]@{ Check = "Pre/post dataset fingerprint"; Result = if ($null -ne $BeforeValidation -and $null -ne $AfterValidation) { "$($BeforeValidation.Dataset.FingerprintSha256) == $($AfterValidation.Dataset.FingerprintSha256)" } else { "N/A" } },
        [pscustomobject]@{ Check = "Pre/post Current fingerprint"; Result = if ($null -ne $BeforeValidation -and $null -ne $AfterValidation) { "$($BeforeValidation.CurrentRowsetSha256) == $($AfterValidation.CurrentRowsetSha256)" } else { "N/A" } },
        [pscustomobject]@{ Check = "Historical mode"; Result = "$($BeforeGuard.HistoricalMode) -> $($AfterGuard.HistoricalMode)" },
        [pscustomobject]@{ Check = "Queue state"; Result = if ($BeforeGuard.QueueStateSha256 -eq $AfterGuard.QueueStateSha256) { "unchanged" } else { "CHANGED" } },
        [pscustomobject]@{ Check = "Product source hashes"; Result = if (Compare-ProductIntegrity $BeforeProduct $AfterProduct) { "unchanged" } else { "DRIFT" } },
        [pscustomobject]@{ Check = "Core V2.1 baseline"; Result = if ($CoreBaselineUnchanged) { "unchanged" } else { "DRIFT" } },
        [pscustomobject]@{ Check = "Post-state verdict"; Result = $PostStateVerdict }
    )
    [void]$lines.Add("")
    [void]$lines.Add("Performance DB identity was guarded as `$script:TargetDatabase` with DB ID 5 before each level. Business DB `TKS_Thuc_Tap_V11_GiaiDoan2` was not accessed. No product SQL, application source, fixture, Current rebuild, cache flush, SQL Agent, or global SQL setting was changed.")
    [void]$lines.Add("")
    [void]$lines.Add("SQL Agent before/after: $($BeforeAgent.Status) / $($BeforeAgent.StartType) -> $($AfterAgent.Status) / $($AfterAgent.StartType). Required state remained Stopped / Manual.")
    [void]$lines.Add("")
    [void]$lines.Add("## 11. What does this mean for real users?")
    [void]$lines.Add("")
    [void]$lines.Add("At approximately 6 concurrent workers (MIXED-L1), read the six scenario rows above as the response time for each page while the other five pages are also active. The same interpretation applies to 12, 24, and 48 simulated concurrent workers. Mean, P95, and P99 are shown in seconds in the required table; RPS is per scenario.")
    [void]$lines.Add("")
    [void]$lines.Add("A host-capacity-limited L8 is not a product failure by itself. A slow but successful request remains valid evidence; timeout, deadlock, resource semaphore, target mismatch, dataset mismatch, and harness-invalid outcomes retain their distinct classifications.")
    [void]$lines.Add("")
    [void]$lines.Add("## 12. Final boundaries")
    [void]$lines.Add("")
    [void]$lines.Add("This extension did not run BenchmarkDotNet, writer contention, C8 diagnostics, Current rebuild, Business DB deployment, SQL Agent start, index tuning, SQL global configuration changes, or a new benchmark build. The next phase is not started automatically.")
    [void]$lines.Add("")
    [void]$lines.Add("**Durable evidence root:** $($script:PhaseRoot)")
    $reportText = $lines -join [Environment]::NewLine
    $reportPath = Join-Path $script:PhaseRoot "Benchmark-V2.1-All-Six-Mixed-Report.md"
    Write-TextFile $reportPath $reportText
}

function Get-FinalResultName {
    $levelRows = @($script:LevelRows | Sort-Object WorkersPerScenario)
    if ($script:HarnessFailureObserved -or @($levelRows | Where-Object { $_.Result -eq "MIXED_HARNESS_CONCURRENCY_INVALID" -or $_.Result -eq "HARNESS_FAILURE" }).Count -gt 0) {
        return "BENCHMARK_V2_1_ALL_SIX_MIXED_HARNESS_INVALID"
    }
    if ($script:ProductFailureObserved) {
        return "BENCHMARK_V2_1_ALL_SIX_MIXED_PRODUCT_FAILURE_OBSERVED"
    }
    $l1 = $levelRows | Where-Object Level -eq "MIXED-L1" | Select-Object -First 1
    $l2 = $levelRows | Where-Object Level -eq "MIXED-L2" | Select-Object -First 1
    $l4 = $levelRows | Where-Object Level -eq "MIXED-L4" | Select-Object -First 1
    $l8 = $levelRows | Where-Object Level -eq "MIXED-L8" | Select-Object -First 1
    if ($null -ne $l1 -and $null -ne $l2 -and $null -ne $l4 -and $null -ne $l8 -and $l1.Result -eq "PASS" -and $l2.Result -eq "PASS" -and $l4.Result -eq "PASS" -and $l8.Result -in @("PASS", "MIXED_L8_STANDALONE_CAPACITY_RESULT")) {
        return "BENCHMARK_V2_1_ALL_SIX_MIXED_COMPLETE"
    }
    if ($null -ne $l8 -and $l8.Result -in @("MIXED_L8_HOST_CAPACITY_LIMIT", "HOST_CAPACITY_LIMIT") -and $null -ne $l1 -and $null -ne $l2 -and $null -ne $l4 -and $l1.Result -eq "PASS" -and $l2.Result -eq "PASS" -and $l4.Result -eq "PASS") {
        return "BENCHMARK_V2_1_ALL_SIX_MIXED_CORE_COMPLETE_L8_HOST_LIMIT"
    }
    return "BENCHMARK_V2_1_ALL_SIX_MIXED_PARTIAL_HOST_LIMIT"
}

function Get-CoreBaselineIntegrity {
    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($path in @(Get-ChildItem -LiteralPath $script:CoreBaselineRoot -File | Sort-Object Name)) {
        $rows.Add([pscustomobject]@{ RelativePath = $path.Name; Size = [int64]$path.Length; Sha256 = Get-Sha256 $path.FullName })
    }
    return @($rows)
}

function Compare-CoreBaselineIntegrity([object[]]$BeforeRows) {
    $afterRows = @(Get-CoreBaselineIntegrity)
    if ($BeforeRows.Count -ne $afterRows.Count) { return $false }
    foreach ($before in $BeforeRows) {
        $after = $afterRows | Where-Object RelativePath -eq $before.RelativePath | Select-Object -First 1
        if ($null -eq $after -or $after.Size -ne $before.Size -or $after.Sha256 -ne $before.Sha256) { return $false }
    }
    Write-JsonFile (Join-Path $script:PhaseRoot "references\core-baseline-integrity-after.json") $afterRows
    return $true
}

function Write-RunMetadata(
    [string]$Stage,
    [string]$ResultName,
    [object]$Candidate,
    [object]$BeforeGuard,
    [object]$AfterGuard
) {
    $metadata = [ordered]@{
        ProtocolVersion = "WAREHOUSE_BENCHMARK_V2_1_MIXED"
        ProtocolSemanticVersion = "2.1.1"
        Stage = $Stage
        Result = $ResultName
        CapturedAtUtc = [DateTime]::UtcNow
        PhaseRoot = $script:PhaseRoot
        Candidate = [ordered]@{
            CoreBaselineId = $script:ExpectedCore.CoreBaselineId
            BenchmarkDllSha256 = $script:ExpectedCore.BenchmarkDllSha256
            DataAccessDllSha256 = $script:ExpectedCore.DataAccessDllSha256
            RuntimeBundleManifestSha256 = $script:RuntimeBundleHash
            MixedRunnerSha256 = $script:RunnerHash
            MixedManifestSha256 = $script:ManifestHash
            MixedControlClosureSha256 = $script:MixedControlClosureHash
            BuildPolicy = "NO_BUILD; NO_RESTORE; accepted V2.1 frozen runtime reused"
        }
        Target = [ordered]@{
            Server = $script:TargetServer
            Database = $script:TargetDatabase
            DatabaseId = $script:TargetDatabaseId
        }
        Workload = [ordered]@{
            Scenarios = $script:Scenarios
            Levels = [ordered]@{ "MIXED-L1" = 6; "MIXED-L2" = 12; "MIXED-L4" = 24; "MIXED-L8" = 48 }
            WorkersMeaning = "SIMULATED_CONCURRENT_WORKERS; one worker is one NBomber concurrent request stream"
            WarmupSeconds = $script:WarmupSeconds
            DurationSeconds = $script:DurationSeconds
            ApplicationTimeoutSeconds = $script:ApplicationTimeoutSeconds
            Page = 1
            PageSize = 10
            Login = $script:LoginName
            FromDate = "2025-01-01"
            ToDate = "2026-12-31"
            HistoricalMode = "LEGACY"
            HistoricalUseCurrentBalance = $false
            CurrentUseCurrentBalance = $true
        }
        AdaptiveSafety = [ordered]@{
            HardFloorMb = $script:HardFloorMb
            EmergencyMarginMb = $script:EmergencyMarginMb
            PredictedSafeMinimumMb = $script:SafetyMinimumMb
            IdealStartMb = $script:IdealStartMb
            MinimumMeaningfulOverlapSeconds = $script:MinimumMeaningfulOverlapSeconds
        }
        BeforeGuard = $BeforeGuard
        AfterGuard = $AfterGuard
        BusinessDatabaseAccessed = $false
        ProductSqlModified = $false
        FixtureMutated = $false
        CurrentRebuilt = $false
        CacheFlush = $false
        SqlAgentModified = $false
        BenchmarkDotNetUsed = $false
    }
    Write-JsonFile (Join-Path $script:PhaseRoot "mixed-run-metadata.json") $metadata
}

function Invoke-Run {
    $script:DotnetPath = Get-DotnetPath
    $script:SqlcmdPath = Get-SqlcmdPath
    if ([string]::IsNullOrWhiteSpace($PhaseRoot)) {
        $stamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
        $PhaseRoot = Join-Path "P:\Warehouse-Benchmark-V2" "WAREHOUSE_BENCHMARK_V2_1_MIXED-$stamp"
        $suffix = 1
        while (Test-Path -LiteralPath $PhaseRoot) {
            $PhaseRoot = Join-Path "P:\Warehouse-Benchmark-V2" "WAREHOUSE_BENCHMARK_V2_1_MIXED-$stamp-$suffix"
            $suffix++
        }
    } elseif (Test-Path -LiteralPath $PhaseRoot) {
        throw "Refusing to overwrite existing phase root: $PhaseRoot"
    }
    $script:PhaseRoot = [IO.Path]::GetFullPath($PhaseRoot)
    New-Item -ItemType Directory -Path $script:PhaseRoot -Force | Out-Null
    Initialize-OutputFiles $script:PhaseRoot
    Write-TextFile (Join-Path $script:PhaseRoot "phase-started-utc.txt") ([DateTime]::UtcNow.ToString("O"))

    $coreBaselineBefore = Get-CoreBaselineIntegrity
    $coreRows = Verify-CoreSourceClosure
    $candidate = Verify-CoreArtifacts
    $runtime = Verify-RuntimeBundle
    Capture-ControlInventory $coreRows | Out-Null
    Invoke-StaticSelfTest | Out-Null
    $environment = Capture-BuildEnvironment
    $productBefore = Capture-ProductIntegrity "before"
    $agentBefore = Capture-SqlAgent "before"
    if (-not [bool]$agentBefore.UnchangedSafeState) {
        throw "SQL Agent is not Stopped/Manual; refusing to mutate or benchmark."
    }

    $beforeGuard = Invoke-TargetGuard "before"
    $residueBefore = Capture-Residue "before"
    if ($null -eq $residueBefore -or -not [bool]$residueBefore.Passed) {
        throw "RESIDUE_BEFORE_NOT_CLEAN"
    }
    $beforeValidation = Invoke-Validation "before"
    if ($beforeValidation.Identity.DatabaseId -ne $script:TargetDatabaseId -or $beforeValidation.Identity.DatabaseName -ne $script:TargetDatabase) {
        throw "TARGET_DB_MISMATCH|Validation identity"
    }
    Write-RunMetadata "PREPARED" "" $candidate $beforeGuard $null

    $previousDropMb = 0.0
    $isFirst = $true
    foreach ($level in $script:Levels) {
        $summary = Invoke-MixedLevel $level $previousDropMb $isFirst
        $isFirst = $false
        if ([bool]$summary.Executed) {
            $previousDropMb = [math]::Max(0.0, [double]$summary.StartAvailableMB - [double]$summary.MinRAMMB)
        }
        if (-not (Wait-Cooldown $summary.Level)) {
            $script:HarnessFailureObserved = $true
            break
        }
        if ($summary.Result -in @("HARNESS_FAILURE", "MIXED_HARNESS_CONCURRENCY_INVALID", "PRODUCT_ERROR", "SQL_TIMEOUT", "DEADLOCK", "RESOURCE_SEMAPHORE", "BLOCKING_REGRESSION")) {
            break
        }
    }

    $residueAfter = Capture-Residue "after"
    $afterValidation = $null
    try {
        $afterValidation = Invoke-Validation "after"
    } catch {
        $script:ProductFailureObserved = $true
        Write-TextFile (Join-Path $script:PhaseRoot "post-validation-error.txt") $_.Exception.Message
    }
    $afterGuard = Invoke-TargetGuard "after"
    $productAfter = Capture-ProductIntegrity "after"
    $agentAfter = Capture-SqlAgent "after"
    $coreBaselineUnchanged = Compare-CoreBaselineIntegrity $coreBaselineBefore
    if (-not $coreBaselineUnchanged) {
        $script:ProductFailureObserved = $true
    }
    if (-not (Compare-ProductIntegrity $productBefore $productAfter) -or -not [bool]$agentAfter.UnchangedSafeState -or $beforeGuard.QueueStateSha256 -ne $afterGuard.QueueStateSha256 -or $beforeGuard.HistoricalMode -ne $afterGuard.HistoricalMode -or $null -eq $residueAfter -or -not [bool]$residueAfter.Passed) {
        $script:ProductFailureObserved = $true
    }
    if ($null -ne $afterValidation) {
        if ($beforeValidation.Dataset.FingerprintSha256 -ne $afterValidation.Dataset.FingerprintSha256 -or $beforeValidation.CurrentRowsetSha256 -ne $afterValidation.CurrentRowsetSha256) {
            $script:ProductFailureObserved = $true
        }
    } else {
        $script:ProductFailureObserved = $true
    }
    Build-Comparisons
    $resultName = Get-FinalResultName
    $postStateVerdict = if (-not $script:ProductFailureObserved -and $coreBaselineUnchanged) { "PASS; target/fixture/Current/mode/queue/residue/source state preserved" } else { "FAIL; see integrity and post-validation evidence" }
    if ($postStateVerdict.StartsWith("FAIL", [StringComparison]::Ordinal)) {
        $resultName = "BENCHMARK_V2_1_ALL_SIX_MIXED_PRODUCT_FAILURE_OBSERVED"
    }
    $result = [ordered]@{
        Result = $resultName
        CapturedAtUtc = [DateTime]::UtcNow
        ProtocolVersion = "WAREHOUSE_BENCHMARK_V2_1_MIXED"
        ProtocolSemanticVersion = "2.1.1"
        CoreBaseline = [ordered]@{
            Id = $script:ExpectedCore.CoreBaselineId
            Result = $script:ExpectedCore.CoreResult
            Immutable = $coreBaselineUnchanged
            Path = $script:CoreBaselineRoot
            BenchmarkDllSha256 = $script:ExpectedCore.BenchmarkDllSha256
        }
        Candidate = [ordered]@{
            BenchmarkDllSha256 = $script:ExpectedCore.BenchmarkDllSha256
            DataAccessDllSha256 = $script:ExpectedCore.DataAccessDllSha256
            RuntimeBundleManifestSha256 = $script:RuntimeBundleHash
            MixedRunnerSha256 = $script:RunnerHash
            MixedManifestSha256 = $script:ManifestHash
            MixedControlClosureSha256 = $script:MixedControlClosureHash
            BuildPolicy = "NO_BUILD; NO_RESTORE; frozen V2.1 runtime reused"
        }
        Target = [ordered]@{ Server = $script:TargetServer; Database = $script:TargetDatabase; DatabaseId = $script:TargetDatabaseId }
        Levels = @($script:LevelRows)
        PerScenario = @($script:PerScenarioRows)
        Comparisons = @($script:ComparisonRows)
        Validation = [ordered]@{
            BeforePassed = $null -ne $beforeValidation -and [bool]$beforeValidation.Passed
            AfterPassed = $null -ne $afterValidation -and [bool]$afterValidation.Passed
            DatasetFingerprintBefore = if ($null -ne $beforeValidation) { $beforeValidation.Dataset.FingerprintSha256 } else { "" }
            DatasetFingerprintAfter = if ($null -ne $afterValidation) { $afterValidation.Dataset.FingerprintSha256 } else { "" }
            CurrentRowsetBefore = if ($null -ne $beforeValidation) { $beforeValidation.CurrentRowsetSha256 } else { "" }
            CurrentRowsetAfter = if ($null -ne $afterValidation) { $afterValidation.CurrentRowsetSha256 } else { "" }
            ConsistencyBefore = if ($null -ne $beforeValidation) { @($beforeValidation.Consistency).Count } else { 0 }
            ConsistencyAfter = if ($null -ne $afterValidation) { @($afterValidation.Consistency).Count } else { 0 }
        }
        Safety = [ordered]@{
            HardFloorMb = $script:HardFloorMb
            EmergencyMarginMb = $script:EmergencyMarginMb
            PredictedSafeMinimumMb = $script:SafetyMinimumMb
            IdealStartMb = $script:IdealStartMb
            MinimumMeaningfulOverlapSeconds = $script:MinimumMeaningfulOverlapSeconds
        }
        Observed = [ordered]@{
            HostCapacityObserved = $script:HostCapacityObserved
            ProductFailureObserved = $script:ProductFailureObserved
            HarnessFailureObserved = $script:HarnessFailureObserved
            AggregatePercentiles = "NOT_COMPUTED"
        }
        Boundaries = [ordered]@{
            ProductSqlModified = $false
            ProductSourceModified = $false
            PerformanceDatabaseMutated = $false
            BusinessDatabaseAccessed = $false
            FixtureMutated = $false
            CurrentRebuilt = $false
            CacheFlush = $false
            SqlAgentModified = $false
            BenchmarkDotNetUsed = $false
            WriterContention = $false
        }
        PostStateVerdict = $postStateVerdict
        DurableEvidenceRoot = $script:PhaseRoot
    }
    Write-JsonFile (Join-Path $script:PhaseRoot "mixed-result.json") $result
    Write-RunMetadata "COMPLETE" $resultName $candidate $beforeGuard $afterGuard
    Write-Report $resultName $beforeValidation $afterValidation $beforeGuard $afterGuard $productBefore $productAfter $agentBefore $agentAfter $coreBaselineUnchanged $postStateVerdict
    Write-TextFile (Join-Path $script:PhaseRoot "phase-completed-utc.txt") ([DateTime]::UtcNow.ToString("O"))
    return [pscustomobject]$result
}

if ($Help -or -not ($Run -or $SelfTest)) {
    Show-Help
    exit 0
}

try {
    if ($SelfTest -and -not $Run) {
        $script:DotnetPath = Get-DotnetPath
        $null = Get-SqlcmdPath
        $script:RunnerHash = Get-Sha256 $script:RunnerFile
        $script:ManifestHash = Get-Sha256 $script:ManifestFile
        Invoke-StaticSelfTest | ConvertTo-Json -Depth 20
        exit 0
    }
    $final = Invoke-Run
    Write-Output "RESULT=$($final.Result)"
    Write-Output "PHASE_ROOT=$($script:PhaseRoot)"
    Write-Output "REPORT=$(Join-Path $script:PhaseRoot 'Benchmark-V2.1-All-Six-Mixed-Report.md')"
    if ($final.Result -in @("BENCHMARK_V2_1_ALL_SIX_MIXED_COMPLETE", "BENCHMARK_V2_1_ALL_SIX_MIXED_CORE_COMPLETE_L8_HOST_LIMIT", "BENCHMARK_V2_1_ALL_SIX_MIXED_PARTIAL_HOST_LIMIT")) {
        exit 0
    }
    exit 1
} catch {
    Stop-AllTrackedProcesses
    $message = "$($_.Exception.GetType().Name): $($_.Exception.Message)"
    if (-not [string]::IsNullOrWhiteSpace($script:PhaseRoot)) {
        Write-TextFile (Join-Path $script:PhaseRoot "mixed-fatal-error.txt") $message
        Write-JsonFile (Join-Path $script:PhaseRoot "mixed-result.json") ([ordered]@{
            Result = "BENCHMARK_V2_1_ALL_SIX_MIXED_HARNESS_INVALID"
            CapturedAtUtc = [DateTime]::UtcNow
            Error = $message
            DurableEvidenceRoot = $script:PhaseRoot
        })
    }
    Write-Error "MIXED_FATAL|$message"
    exit 1
} finally {
    Stop-AllTrackedProcesses
}
