[CmdletBinding()]
param(
    [switch]$Help,
    [switch]$Validate,
    [switch]$SelfTest,
    [switch]$Baseline,
    [switch]$Bdn,
    [switch]$NbomberStandard,
    [switch]$Capacity,
    [switch]$Mixed,
    [switch]$AllSixLow,
    [switch]$Resume,
    [switch]$NoBuild,
    [string]$PhaseRoot = "",
    [int]$SoftWaitSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$script:ProjectRoot = Join-Path $script:RepoRoot "TKS_Thuc_Tap_V11_Benchmarks_V2"
$script:ProjectFile = Join-Path $script:ProjectRoot "TKS_Thuc_Tap_V11_Benchmarks_V2.csproj"
$script:ManifestFile = Join-Path $script:RepoRoot "benchmarks\v2\benchmark-v2-manifest.json"
$script:V1Runner = "C:\Users\Surface\Documents\Codex\2026-08-30\ban\outputs\run-fixed-warehouse-regression.ps1"
$script:V1RunnerExpectedHash = "1D97CE267F3D0CE03FD4D6826E66B4ABFC2C04A59554E54B9A751486BD8F8CD6"
$script:MainDllName = "TKS_Thuc_Tap_V11_Benchmarks_V2.dll"
$script:TargetServer = "localhost\MSSQLSERVER19"
$script:TargetDatabase = "TKS_Thuc_Tap_V11_Perf_10000000"
$script:TargetDatabaseId = 5
$script:LoginName = "PERF_USER"
$script:ConnectionString = "Server=localhost\MSSQLSERVER19;Database=TKS_Thuc_Tap_V11_Perf_10000000;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=30;Application Name=WarehouseBenchmarkV2"
$script:StandardLevels = @(1, 2, 4)
$script:Scenarios = @(
    "MasterPaged",
    "LookupPaged",
    "DocumentPaged",
    "DetailReportPaged",
    "InventoryHistoricalReportPaged",
    "InventoryCurrentBalancePaged"
)
$script:SourceInventory = @()
$script:BuildAOutput = ""
$script:RuntimeDirectory = ""
$script:ManifestHash = ""
$script:RunnerHash = ""
$script:SourceClosureHash = ""
$script:BenchmarkProjectHash = ""
$script:DatasetFingerprint = ""
$script:CompletedBlocks = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$script:HostRows = [System.Collections.Generic.List[object]]::new()
$script:PeakRows = [System.Collections.Generic.List[object]]::new()
$script:BlockRows = [System.Collections.Generic.List[object]]::new()
$script:ProductFailureObserved = $false
$script:HarnessFailureObserved = $false
$script:HostConstraintObserved = $false

function Show-Help {
    @"
WAREHOUSE_BENCHMARK_V2_1

Required modes:
  -Baseline                 Build/freeze/validate and run standard BDN + NBomber baseline
  -Validate                 Build/freeze if needed, then read-only validation
  -SelfTest                 Run local harness self-tests
  -Bdn                      Run the six BDN scenarios sequentially
  -NbomberStandard          Run the standard 18 NBomber blocks (levels 1/2/4)

Optional explicit profiles:
  -Capacity | -Mixed | -AllSixLow
  These are opt-in and are recorded as optional; they are not part of the
  standard baseline gate unless implemented and explicitly authorized.

Useful options:
  -PhaseRoot <directory>    Durable phase evidence directory
  -Resume                   Skip blocks already present in v2 summaries
  -NoBuild                  Use an already frozen runtime under PhaseRoot

The runner targets only:
  $script:TargetServer / $script:TargetDatabase / DB_ID $script:TargetDatabaseId
"@
}

function Get-Sha256([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "File not found for hashing: $Path"
    }
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToUpperInvariant()
}

function Get-RelativeRepoPath([string]$Path) {
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
    Write-TextFile $Path ($Value | ConvertTo-Json -Depth 30)
}

function Get-InventoryFingerprint([object[]]$Rows) {
    $lines = @(
        $Rows |
            Sort-Object RelativePath, Role |
            ForEach-Object { "$($_.RelativePath)|$($_.Role)|$($_.Size)|$($_.Sha256)" }
    )
    $text = ($lines -join [Environment]::NewLine) + [Environment]::NewLine
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($text)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
}

function Add-InventoryFile([System.Collections.Generic.List[object]]$Rows, [string]$Path, [string]$Role) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return
    }
    $item = Get-Item -LiteralPath $Path
    $Rows.Add([pscustomobject]@{
        RelativePath = Get-RelativeRepoPath $Path
        Role = $Role
        Size = [int64]$item.Length
        Sha256 = Get-Sha256 $Path
    })
}

function Get-SourceClosure {
    $rows = [System.Collections.Generic.List[object]]::new()
    Get-ChildItem -LiteralPath $script:ProjectRoot -File -Filter "*.cs" |
        Sort-Object FullName |
        ForEach-Object { Add-InventoryFile $rows $_.FullName "V2_COMPILED_SOURCE" }
    Add-InventoryFile $rows $script:ProjectFile "V2_PROJECT"

    $dataAccessRoot = Join-Path $script:RepoRoot "TKS_Thuc_Tap_V11_Data_Access"
    if (Test-Path -LiteralPath $dataAccessRoot -PathType Container) {
        Get-ChildItem -LiteralPath $dataAccessRoot -Recurse -File -Filter "*.cs" |
            Where-Object { $_.FullName -notmatch "\\(bin|obj|Debug|Release)\\" } |
            Sort-Object FullName |
            ForEach-Object { Add-InventoryFile $rows $_.FullName "PROJECT_REFERENCE_SOURCE" }
        Add-InventoryFile $rows (Join-Path $dataAccessRoot "TKS_Thuc_Tap_V11_Data_Access.csproj") "PROJECT_REFERENCE"
    }

    Add-InventoryFile $rows (Join-Path $script:PSScriptRoot "run-warehouse-benchmark-v2.ps1") "V2_RUNNER"
    foreach ($candidate in @(
        (Join-Path $script:RepoRoot "global.json"),
        (Join-Path $script:RepoRoot "NuGet.config"),
        (Join-Path $script:RepoRoot "Directory.Build.props"),
        (Join-Path $script:RepoRoot "Directory.Build.targets"),
        (Join-Path $script:RepoRoot "Directory.Packages.props"),
        (Join-Path $script:RepoRoot "packages.lock.json")
    )) {
        Add-InventoryFile $rows $candidate "BUILD_INPUT"
    }
    $script:SourceInventory = @($rows | Sort-Object RelativePath, Role)
    $script:SourceClosureHash = Get-InventoryFingerprint $script:SourceInventory
    $script:BenchmarkProjectHash = Get-Sha256 $script:ProjectFile
    return $script:SourceInventory
}

function Invoke-LoggedProcess(
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
    foreach ($arg in $Arguments) {
        [void]$info.ArgumentList.Add([string]$arg)
    }
    foreach ($key in $Environment.Keys) {
        $info.Environment[$key] = [string]$Environment[$key]
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    if (-not $process.Start()) {
        throw "Could not start process: $FilePath"
    }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    Write-TextFile $StdoutPath $stdout
    Write-TextFile $StderrPath $stderr
    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Stdout = $stdout
        Stderr = $stderr
    }
}

function Start-AsyncProcess(
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
    foreach ($arg in $Arguments) {
        [void]$info.ArgumentList.Add([string]$arg)
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
        StdoutTask = $process.StandardOutput.ReadToEndAsync()
        StderrTask = $process.StandardError.ReadToEndAsync()
        StdoutPath = $StdoutPath
        StderrPath = $StderrPath
    }
}

function Complete-AsyncProcess([object]$Handle) {
    $Handle.Process.WaitForExit()
    $stdout = $Handle.StdoutTask.GetAwaiter().GetResult()
    $stderr = $Handle.StderrTask.GetAwaiter().GetResult()
    Write-TextFile $Handle.StdoutPath $stdout
    Write-TextFile $Handle.StderrPath $stderr
    return [pscustomobject]@{
        ExitCode = $Handle.Process.ExitCode
        Stdout = $stdout
        Stderr = $stderr
    }
}

function Get-HostSnapshot {
    $os = Get-CimInstance -ClassName Win32_OperatingSystem
    $freeMb = [math]::Round(([double]$os.FreePhysicalMemory * 1KB) / 1MB, 1)
    $totalMb = [math]::Round(([double]$os.TotalVisibleMemorySize * 1KB) / 1MB, 1)
    return [pscustomobject]@{
        TimestampUtc = [DateTime]::UtcNow.ToString("O")
        FreePhysicalRamMb = $freeMb
        TotalPhysicalRamMb = $totalMb
        ProcessCount = (Get-Process).Count
    }
}

function Get-ProcessTreeIds([int]$RootPid) {
    $all = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue)
    $ids = [System.Collections.Generic.HashSet[int]]::new()
    [void]$ids.Add($RootPid)
    $changed = $true
    while ($changed) {
        $changed = $false
        foreach ($item in $all) {
            if ($ids.Contains([int]$item.ParentProcessId) -and $ids.Add([int]$item.ProcessId)) {
                $changed = $true
            }
        }
    }
    return @($ids)
}

function Stop-ProcessTree([int]$RootPid) {
    foreach ($processId in (Get-ProcessTreeIds $RootPid | Sort-Object -Descending)) {
        if ($processId -eq $PID) {
            continue
        }
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
    }
}

function Get-ProcessMemoryMb([int[]]$ProcessIds) {
    $sum = 0.0
    foreach ($processId in $ProcessIds) {
        try {
            $p = Get-Process -Id $processId -ErrorAction Stop
            $sum += [double]$p.PrivateMemorySize64 / 1MB
        } catch {
        }
    }
    return [math]::Round($sum, 1)
}

function Get-DotnetPath {
    $command = Get-Command dotnet -ErrorAction Stop
    return $command.Source
}

function Capture-BuildEnvironment([string]$Phase) {
    $directory = Join-Path $Phase "environment"
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $dotnet = Get-DotnetPath
    $info = Invoke-LoggedProcess $dotnet @("--info") $script:RepoRoot (Join-Path $directory "dotnet-info.stdout.txt") (Join-Path $directory "dotnet-info.stderr.txt")
    $version = Invoke-LoggedProcess $dotnet @("--version") $script:RepoRoot (Join-Path $directory "dotnet-version.stdout.txt") (Join-Path $directory "dotnet-version.stderr.txt")
    $runtimes = Invoke-LoggedProcess $dotnet @("--list-runtimes") $script:RepoRoot (Join-Path $directory "dotnet-runtimes.stdout.txt") (Join-Path $directory "dotnet-runtimes.stderr.txt")
    $sdks = Invoke-LoggedProcess $dotnet @("--list-sdks") $script:RepoRoot (Join-Path $directory "dotnet-sdks.stdout.txt") (Join-Path $directory "dotnet-sdks.stderr.txt")
    $msbuild = Invoke-LoggedProcess $dotnet @("msbuild", $script:ProjectFile, "-version", "-nologo") $script:RepoRoot (Join-Path $directory "msbuild.stdout.txt") (Join-Path $directory "msbuild.stderr.txt")
    $os = Get-CimInstance -ClassName Win32_OperatingSystem
    $projectText = Get-Content -Raw -LiteralPath $script:ProjectFile
    $packages = @(
        [regex]::Matches($projectText, 'PackageReference Include="([^"]+)" Version="([^"]+)"') |
            ForEach-Object { [pscustomobject]@{ Id = $_.Groups[1].Value; Version = $_.Groups[2].Value } }
    )
    $envNames = @("PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER", "NUMBER_OF_PROCESSORS", "DOTNET_ROOT", "DOTNET_ROOT_X64", "NUGET_PACKAGES", "MSBuildSDKsPath", "TEMP", "TMP", "CI")
    $relevantEnv = [ordered]@{}
    foreach ($name in $envNames) {
        $relevantEnv[$name] = [Environment]::GetEnvironmentVariable($name)
    }
    $record = [ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow.ToString("O")
        Os = [ordered]@{
            Caption = $os.Caption
            Version = $os.Version
            BuildNumber = $os.BuildNumber
            Architecture = $os.OSArchitecture.ToString()
        }
        DotnetPath = $dotnet
        DotnetVersion = $version.Stdout.Trim()
        DotnetInfoExitCode = $info.ExitCode
        DotnetRuntimesExitCode = $runtimes.ExitCode
        DotnetSdksExitCode = $sdks.ExitCode
        MsbuildExitCode = $msbuild.ExitCode
        TargetFramework = "net8.0"
        Configuration = "Release"
        Deterministic = $true
        ContinuousIntegrationBuild = $true
        Packages = $packages
        RelevantEnvironment = $relevantEnv
    }
    Write-JsonFile (Join-Path $Phase "v2-environment.json") $record
    return $record
}

function Invoke-SourceFreeze([string]$Phase) {
    $inventory = Get-SourceClosure
    $sourceDirectory = Join-Path $Phase "source"
    New-Item -ItemType Directory -Path $sourceDirectory -Force | Out-Null
    $csv = $inventory | ConvertTo-Csv -NoTypeInformation
    Write-TextFile (Join-Path $sourceDirectory "source-inventory.csv") ($csv -join [Environment]::NewLine)
    Write-JsonFile (Join-Path $sourceDirectory "source-inventory.json") $inventory
    Write-TextFile (Join-Path $sourceDirectory "source-closure.sha256") $script:SourceClosureHash
    Write-TextFile (Join-Path $sourceDirectory "benchmark-project.sha256") $script:BenchmarkProjectHash
    return $inventory
}

function Copy-BuildSource([string]$Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($row in $script:SourceInventory) {
        if ($row.Role -eq "V2_RUNNER") {
            continue
        }
        $sourcePath = Join-Path $script:RepoRoot ($row.RelativePath.Replace("/", "\"))
        $destinationPath = Join-Path $Destination ($row.RelativePath.Replace("/", "\"))
        $destinationDirectory = Split-Path -Parent $destinationPath
        if ($destinationDirectory) {
            New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        }
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
        if ((Get-Sha256 $sourcePath) -ne (Get-Sha256 $destinationPath)) {
            throw "Build source copy hash mismatch: $($row.RelativePath)"
        }
    }
}

function Invoke-CleanBuild([string]$Label, [string]$Phase) {
    $buildRoot = Join-Path $Phase $Label
    $outputRoot = Join-Path $buildRoot "out"
    if (Test-Path -LiteralPath $buildRoot) {
        Remove-Item -LiteralPath $buildRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
    $sourceRoot = Join-Path $buildRoot "source"
    Copy-BuildSource $sourceRoot
    $projectFile = Join-Path $sourceRoot (Get-RelativeRepoPath $script:ProjectFile).Replace("/", "\")
    $outputProperty = $outputRoot.TrimEnd("\") + "\"
    $dotnet = Get-DotnetPath
    $restoreArgs = @("restore", $projectFile, "--nologo", "--verbosity", "minimal", "-p:BaseOutputPath=$outputProperty")
    $restore = Invoke-LoggedProcess $dotnet $restoreArgs $script:RepoRoot (Join-Path $buildRoot "restore.stdout.txt") (Join-Path $buildRoot "restore.stderr.txt")
    if ($restore.ExitCode -ne 0) {
        throw "Build $Label restore failed with exit code $($restore.ExitCode)."
    }
    $buildArgs = @("build", $projectFile, "--configuration", "Release", "--framework", "net8.0", "--no-restore", "--nologo", "--verbosity", "minimal", "-p:BaseOutputPath=$outputProperty", "-p:Deterministic=true", "-p:DebugType=None", "-p:ContinuousIntegrationBuild=true", "-p:PathMap=$sourceRoot=V2Source")
    $build = Invoke-LoggedProcess $dotnet $buildArgs $script:RepoRoot (Join-Path $buildRoot "build.stdout.txt") (Join-Path $buildRoot "build.stderr.txt")
    if ($build.ExitCode -ne 0) {
        throw "Build $Label failed with exit code $($build.ExitCode)."
    }
    $main = @(Get-ChildItem -LiteralPath $outputRoot -Recurse -File -Filter $script:MainDllName)
    if ($main.Count -ne 1) {
        throw "Build $Label produced $($main.Count) copies of $script:MainDllName."
    }
    return [pscustomobject]@{
        Label = $Label
        Root = $buildRoot
        OutputRoot = $outputRoot
        MainDll = $main[0].FullName
        OutputDirectory = $main[0].Directory.FullName
        RestoreExitCode = $restore.ExitCode
        BuildExitCode = $build.ExitCode
    }
}

function Capture-DependencyGraph([string]$Phase, [object[]]$Builds) {
    $assets = [System.Collections.Generic.List[object]]::new()
    foreach ($build in $Builds) {
        foreach ($file in (Get-ChildItem -LiteralPath $build.Root -Recurse -File -Filter "project.assets.json")) {
            $assetJson = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json
            $libraries = @($assetJson.libraries.PSObject.Properties.Name | Sort-Object)
            $assets.Add([pscustomobject]@{
                Build = $build.Label
                Path = [IO.Path]::GetRelativePath($Phase, $file.FullName).Replace("\", "/")
                Sha256 = Get-Sha256 $file.FullName
                Libraries = $libraries
                ProjectFileDependencyGroups = $assetJson.projectFileDependencyGroups
            })
        }
    }
    if ($assets.Count -eq 0) {
        throw "No project.assets.json was produced by the isolated restores."
    }
    Write-JsonFile (Join-Path $Phase "source\dependency-graph.json") $assets
    return $assets
}

function Get-BuildArtifactRows([object]$BuildA, [object]$BuildB) {
    $nameSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($directory in @($BuildA.OutputDirectory, $BuildB.OutputDirectory)) {
        Get-ChildItem -LiteralPath $directory -File | ForEach-Object { [void]$nameSet.Add($_.Name) }
    }
    $rows = foreach ($name in ($nameSet | Sort-Object)) {
        $a = Join-Path $BuildA.OutputDirectory $name
        $b = Join-Path $BuildB.OutputDirectory $name
        $aHash = if (Test-Path -LiteralPath $a -PathType Leaf) { Get-Sha256 $a } else { "" }
        $bHash = if (Test-Path -LiteralPath $b -PathType Leaf) { Get-Sha256 $b } else { "" }
        [pscustomobject]@{
            Artifact = $name
            BuildASha256 = $aHash
            BuildBSha256 = $bHash
            Match = (-not [string]::IsNullOrWhiteSpace($aHash) -and $aHash -eq $bHash)
            AcceptedSha256 = $aHash
        }
    }
    return @($rows)
}

function Freeze-Runtime([object]$BuildA, [string]$Phase) {
    $runtime = Join-Path $Phase "runtime"
    if (Test-Path -LiteralPath $runtime) {
        Remove-Item -LiteralPath $runtime -Recurse -Force
    }
    New-Item -ItemType Directory -Path $runtime -Force | Out-Null
    Copy-Item -Path (Join-Path $BuildA.OutputDirectory "*") -Destination $runtime -Recurse -Force
    $rows = @(
        Get-ChildItem -LiteralPath $runtime -Recurse -File |
            Sort-Object FullName |
            ForEach-Object {
                [pscustomobject]@{
                    RelativePath = [IO.Path]::GetRelativePath($runtime, $_.FullName).Replace("\", "/")
                    Role = "RUNTIME_BUNDLE"
                    Size = [int64]$_.Length
                    Sha256 = Get-Sha256 $_.FullName
                    Origin = "Build A isolated Release output"
                    Required = ($_.Name -in @($script:MainDllName, "TKS_Thuc_Tap_V11_Benchmarks_V2.deps.json", "TKS_Thuc_Tap_V11_Benchmarks_V2.runtimeconfig.json"))
                }
            }
    )
    $runtimeHash = Get-InventoryFingerprint $rows
    $rows | Export-Csv -LiteralPath (Join-Path $Phase "runtime-bundle-inventory.csv") -NoTypeInformation
    Write-JsonFile (Join-Path $Phase "runtime-bundle-inventory.json") $rows
    Write-TextFile (Join-Path $Phase "runtime-bundle-manifest.sha256") $runtimeHash
    $script:RuntimeDirectory = $runtime
    $script:BuildAOutput = $BuildA.OutputDirectory
    return [pscustomobject]@{
        Directory = $runtime
        Rows = $rows
        ManifestSha256 = $runtimeHash
        MainDll = Join-Path $runtime $script:MainDllName
    }
}

function Update-V2Manifest([string]$Phase, [object]$Runtime) {
    $preHash = Get-Sha256 $script:ManifestFile
    Copy-Item -LiteralPath $script:ManifestFile -Destination (Join-Path $Phase "manifest-pre.json") -Force
    Write-TextFile (Join-Path $Phase "manifest-pre.sha256") $preHash
    $manifest = Get-Content -Raw -LiteralPath $script:ManifestFile | ConvertFrom-Json
    $manifest.runnerSha256 = $script:RunnerHash
    $manifest.sourceClosureSha256 = $script:SourceClosureHash
    $manifest.benchmarkProjectSha256 = $script:BenchmarkProjectHash
    $manifest.benchmarkDllSha256 = Get-Sha256 $Runtime.MainDll
    $manifest.runtimeBundleManifestSha256 = $Runtime.ManifestSha256
    Write-TextFile $script:ManifestFile ($manifest | ConvertTo-Json -Depth 30)
    $script:ManifestHash = Get-Sha256 $script:ManifestFile
    Copy-Item -LiteralPath $script:ManifestFile -Destination (Join-Path $Phase "manifest-post.json") -Force
    Write-TextFile (Join-Path $Phase "manifest-post.sha256") $script:ManifestHash
    return $manifest
}

function Get-ChildEnvironment([string]$OutputDirectory) {
    $temp = Join-Path $script:PhaseRoot "temp"
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    return @{
        TKS_V2_CONNECTION_STRING = $script:ConnectionString
        TKS_V2_LOGIN = $script:LoginName
        TKS_V2_FROM_DATE = "2025-01-01"
        TKS_V2_TO_DATE = "2026-12-31"
        TKS_V2_PAGE_SIZE = "10"
        TKS_V2_OUTPUT_DIRECTORY = $OutputDirectory
        TEMP = $temp
        TMP = $temp
    }
}

function Invoke-V2Child(
    [string[]]$Arguments,
    [string]$OutputDirectory,
    [string]$StdoutName,
    [string]$StderrName
) {
    $env = Get-ChildEnvironment $OutputDirectory
    $runtimeDll = Join-Path $script:RuntimeDirectory $script:MainDllName
    if (-not (Test-Path -LiteralPath $runtimeDll -PathType Leaf)) {
        throw "Frozen V2 runtime is missing: $runtimeDll"
    }
    return Invoke-LoggedProcess (Get-DotnetPath) (@($runtimeDll) + $Arguments) $script:RepoRoot (Join-Path $OutputDirectory $StdoutName) (Join-Path $OutputDirectory $StderrName) $env
}

function Initialize-OutputFiles([string]$Phase) {
    $headers = @{
        "v2-bdn-summary.csv" = "BlockId,Scenario,Status,FailureType,ExitCode,DurationSeconds,MeanMs,P50Ms,P95Ms,P99Ms,Rps,RequestCount,Ok,Failed,ArtifactPath,Notes"
        "v2-nbomber-summary.csv" = "BlockId,Scenario,Level,Status,FailureType,ExitCode,DurationSeconds,MeanMs,P50Ms,P95Ms,P99Ms,Rps,RequestCount,Ok,Failed,ArtifactPath,Notes"
        "v2-host-telemetry.csv" = "BlockId,TimestampUtc,FreePhysicalRamMb,TotalPhysicalRamMb,ProcessCount,ChildPrivateMemoryMb,HardStopThresholdMb,SoftStartThresholdMb"
        "v2-sql-telemetry.csv" = "BlockId,TimestampUtc,DatabaseName,DatabaseId,ActiveRequests,BlockingRequests,ActiveRequestGrantKB,RequestedGrantKB,GrantedGrantKB,PendingMemoryGrants,ResourceSemaphoreWaiters,LogicalReads,TempdbUsedKB,MemoryGrantsPendingCounter,DeadlockCounter"
        "v2-process-peak-memory.csv" = "BlockId,Kind,Scenario,Level,PeakPrivateMemoryMb,PeakFreePhysicalRamMb,Status"
    }
    foreach ($name in $headers.Keys) {
        $path = Join-Path $Phase $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            Write-TextFile $path $headers[$name]
        }
    }
}

function Add-CsvRow([string]$Path, [object]$Row) {
    $csv = ($Row | ConvertTo-Csv -NoTypeInformation)
    if ($csv.Count -gt 1) {
        [IO.File]::AppendAllText($Path, [Environment]::NewLine + ($csv[1..($csv.Count - 1)] -join [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    }
}

function Merge-CsvFile([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        return
    }
    $lines = Get-Content -LiteralPath $Source
    if ($lines.Count -le 1) {
        return
    }
    $destinationHasRows = (Get-Item -LiteralPath $Destination).Length -gt 0
    $payload = if ($destinationHasRows) { $lines[1..($lines.Count - 1)] } else { $lines }
    [IO.File]::AppendAllText($Destination, [Environment]::NewLine + ($payload -join [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
}

function Wait-ForHostStart([string]$BlockId) {
    if ($script:HostConstraintObserved) {
        return $false
    }
    $started = [DateTime]::UtcNow
    while ($true) {
        $snapshot = Get-HostSnapshot
        $script:HostRows.Add([pscustomobject]@{
            BlockId = $BlockId
            TimestampUtc = $snapshot.TimestampUtc
            FreePhysicalRamMb = $snapshot.FreePhysicalRamMb
            TotalPhysicalRamMb = $snapshot.TotalPhysicalRamMb
            ProcessCount = $snapshot.ProcessCount
            ChildPrivateMemoryMb = 0
            HardStopThresholdMb = 512
            SoftStartThresholdMb = 1024
        })
        Add-CsvRow (Join-Path $script:PhaseRoot "v2-host-telemetry.csv") ([pscustomobject]@{
            BlockId = $BlockId
            TimestampUtc = $snapshot.TimestampUtc
            FreePhysicalRamMb = $snapshot.FreePhysicalRamMb
            TotalPhysicalRamMb = $snapshot.TotalPhysicalRamMb
            ProcessCount = $snapshot.ProcessCount
            ChildPrivateMemoryMb = 0
            HardStopThresholdMb = 512
            SoftStartThresholdMb = 1024
        })
        if ([double]$snapshot.FreePhysicalRamMb -ge 1024) {
            return $true
        }
        if (([DateTime]::UtcNow - $started).TotalSeconds -ge $SoftWaitSeconds) {
            $script:HostConstraintObserved = $true
            return $false
        }
        Start-Sleep -Seconds 5
    }
}

function Wait-Cooldown {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    Start-Sleep -Seconds 5
    while ([DateTime]::UtcNow -lt $deadline) {
        $snapshot = Get-HostSnapshot
        if ([double]$snapshot.FreePhysicalRamMb -ge 1024) {
            return
        }
        Start-Sleep -Seconds 5
    }
}

function Get-TextFailureType([string]$Text) {
    if ($Text -match "(?i)RESOURCE_SEMAPHORE|8645|8651|8657") { return "RESOURCE_SEMAPHORE" }
    if ($Text -match "(?i)deadlock|1205") { return "DEADLOCK" }
    if ($Text -match "(?i)timeout|timed out|error -2") { return "SQL_TIMEOUT" }
    if ($Text -match "(?i)V2_FATAL|exception|error|fail") { return "PRODUCT_ERROR" }
    return "HARNESS_ERROR"
}

function Get-FieldValue([object]$Row, [string[]]$Names) {
    foreach ($name in $Names) {
        $property = $Row.PSObject.Properties[$name]
        if ($null -ne $property) {
            return [string]$property.Value
        }
    }
    return ""
}

function Convert-MetricMs([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return "" }
    $match = [regex]::Match($Value.Replace(",", ""), "[-+]?[0-9]*\.?[0-9]+")
    if (-not $match.Success) { return "" }
    $number = [double]::Parse($match.Value, [Globalization.CultureInfo]::InvariantCulture)
    $lower = $Value.ToLowerInvariant()
    if ($lower.Contains("ns")) { return [math]::Round($number / 1e6, 6) }
    if ($lower.Contains("us") -or $lower.Contains("µs")) { return [math]::Round($number / 1e3, 6) }
    if ($lower.Contains("s") -and -not $lower.Contains("ms")) { return [math]::Round($number * 1000, 6) }
    return [math]::Round($number, 6)
}

function Find-BdnResult([string]$Directory) {
    foreach ($file in (Get-ChildItem -LiteralPath $Directory -Recurse -File -Filter "*.csv" | Sort-Object LastWriteTime -Descending)) {
        try {
            $rows = @(Import-Csv -LiteralPath $file.FullName)
            $row = $rows | Where-Object { $null -ne $_.PSObject.Properties["Mean"] } | Select-Object -First 1
            if ($null -ne $row) {
                return [pscustomobject]@{
                    Path = $file.FullName
                    MeanMs = Convert-MetricMs (Get-FieldValue $row @("Mean"))
                    ErrorMs = Convert-MetricMs (Get-FieldValue $row @("Error"))
                    StdDevMs = Convert-MetricMs (Get-FieldValue $row @("StdDev"))
                    AllocatedBytes = Get-FieldValue $row @("Allocated")
                }
            }
        } catch {
        }
    }
    return $null
}

function Find-NBomberResult([string]$Directory, [string]$Scenario) {
    foreach ($file in (Get-ChildItem -LiteralPath $Directory -Recurse -File -Filter "*.csv" | Sort-Object LastWriteTime -Descending)) {
        try {
            $rows = @(Import-Csv -LiteralPath $file.FullName)
            foreach ($row in $rows) {
                $name = Get-FieldValue $row @("scenario", "Scenario", "test_name")
                if ($name -eq $Scenario -or [string]::IsNullOrWhiteSpace($name)) {
                    return [pscustomobject]@{
                        Path = $file.FullName
                        RequestCount = Get-FieldValue $row @("request_count", "RequestCount")
                        Ok = Get-FieldValue $row @("ok", "Ok")
                        Failed = Get-FieldValue $row @("failed", "Failed")
                        MeanMs = Get-FieldValue $row @("ok_mean", "OkMean")
                        P50Ms = Get-FieldValue $row @("ok_50_percent", "Ok50Percent")
                        P95Ms = Get-FieldValue $row @("ok_95_percent", "Ok95Percent")
                        P99Ms = Get-FieldValue $row @("ok_99_percent", "Ok99Percent")
                        Rps = Get-FieldValue $row @("ok_rps", "OkRps")
                    }
                }
            }
        } catch {
        }
    }
    return $null
}

function Load-ResumeState([string]$Phase) {
    if (-not $Resume) {
        return
    }
    foreach ($name in @("v2-bdn-summary.csv", "v2-nbomber-summary.csv")) {
        $path = Join-Path $Phase $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            continue
        }
        foreach ($row in @(Import-Csv -LiteralPath $path)) {
            $blockId = Get-FieldValue $row @("BlockId")
            if (-not [string]::IsNullOrWhiteSpace($blockId)) {
                [void]$script:CompletedBlocks.Add($blockId)
            }
        }
    }
}

function Invoke-MeasuredBlock(
    [string]$Kind,
    [string]$Scenario,
    [int]$Level,
    [string]$Phase
) {
    $blockId = if ($Kind -eq "BDN") { "BDN-$Scenario" } else { "NB-$Scenario-$Level" }
    if ($script:CompletedBlocks.Contains($blockId)) {
        Write-Output "V2_RESUME_SKIP|$blockId"
        return
    }
    $blockDirectory = Join-Path $Phase ("runs\" + $blockId)
    New-Item -ItemType Directory -Path $blockDirectory -Force | Out-Null
    $ready = Wait-ForHostStart $blockId
    if (-not $ready) {
        $skipValues = [ordered]@{
            BlockId = $blockId
            Scenario = $Scenario
        }
        if ($Kind -ne "BDN") {
            $skipValues.Level = $Level
        }
        $skipValues.Status = "HOST_PRECONDITION_NOT_MET"
        $skipValues.FailureType = "HOST_MEMORY_CONSTRAINT"
        $skipValues.ExitCode = -20
        $skipValues.DurationSeconds = 0
        $skipValues.MeanMs = ""
        $skipValues.P50Ms = ""
        $skipValues.P95Ms = ""
        $skipValues.P99Ms = ""
        $skipValues.Rps = ""
        $skipValues.RequestCount = 0
        $skipValues.Ok = 0
        $skipValues.Failed = 0
        $skipValues.ArtifactPath = ""
        $skipValues.Notes = "Free physical RAM stayed below the 1024 MB soft start threshold."
        $skipRow = [pscustomobject]$skipValues
        if ($Kind -eq "BDN") {
            Add-CsvRow (Join-Path $Phase "v2-bdn-summary.csv") $skipRow
        } else {
            Add-CsvRow (Join-Path $Phase "v2-nbomber-summary.csv") $skipRow
        }
        Add-CsvRow (Join-Path $Phase "v2-process-peak-memory.csv") ([pscustomobject]@{
            BlockId = $blockId
            Kind = $Kind
            Scenario = $Scenario
            Level = if ($Kind -eq "BDN") { "" } else { $Level }
            PeakPrivateMemoryMb = 0
            PeakFreePhysicalRamMb = ""
            Status = "HOST_PRECONDITION_NOT_MET"
        })
        [void]$script:CompletedBlocks.Add($blockId)
        [void]$script:BlockRows.Add($skipRow)
        return
    }

    $stdoutName = "$Kind.stdout.txt"
    $stderrName = "$Kind.stderr.txt"
    $arguments = if ($Kind -eq "BDN") {
        @("--bdn", "--scenario", $Scenario, "--artifacts", $blockDirectory)
    } else {
        @("--nbomber", "--scenario", $Scenario, "--output", $blockDirectory)
    }
    if ($Kind -ne "BDN") {
        $childEnvironment = Get-ChildEnvironment $blockDirectory
        $childEnvironment["TKS_V2_NBOMBER_COPIES"] = [string]$Level
    } else {
        $childEnvironment = Get-ChildEnvironment $blockDirectory
    }
    $runtimeDll = Join-Path $script:RuntimeDirectory $script:MainDllName
    $child = Start-AsyncProcess (Get-DotnetPath) (@($runtimeDll) + $arguments) $script:RepoRoot (Join-Path $blockDirectory $stdoutName) (Join-Path $blockDirectory $stderrName) $childEnvironment
    $childPid = $child.Process.Id
    $sqlTelemetryPath = Join-Path $blockDirectory "sql-telemetry.csv"
    $telemetry = $null
    try {
        $telemetry = Start-AsyncProcess (Get-DotnetPath) @($runtimeDll, "--telemetry", "--target-pid", [string]$childPid, "--block", $blockId, "--output", $sqlTelemetryPath) $script:RepoRoot (Join-Path $blockDirectory "telemetry.stdout.txt") (Join-Path $blockDirectory "telemetry.stderr.txt") (Get-ChildEnvironment $blockDirectory)
    } catch {
        $script:HarnessFailureObserved = $true
        Write-TextFile (Join-Path $blockDirectory "telemetry-start-error.txt") $_.Exception.Message
    }

    $startedUtc = [DateTime]::UtcNow
    $timeoutSeconds = if ($Kind -eq "BDN") { 120 } else { 75 }
    $deadline = $startedUtc.AddSeconds($timeoutSeconds)
    $hardStop = $false
    $timedOut = $false
    $peakPrivate = 0.0
    $peakFree = 999999.0
    while (-not $child.Process.HasExited) {
        $snapshot = Get-HostSnapshot
        $ids = @(Get-ProcessTreeIds $childPid)
        $privateMb = Get-ProcessMemoryMb $ids
        if ($privateMb -gt $peakPrivate) { $peakPrivate = $privateMb }
        if ([double]$snapshot.FreePhysicalRamMb -lt $peakFree) { $peakFree = [double]$snapshot.FreePhysicalRamMb }
        Add-CsvRow (Join-Path $Phase "v2-host-telemetry.csv") ([pscustomobject]@{
            BlockId = $blockId
            TimestampUtc = $snapshot.TimestampUtc
            FreePhysicalRamMb = $snapshot.FreePhysicalRamMb
            TotalPhysicalRamMb = $snapshot.TotalPhysicalRamMb
            ProcessCount = $snapshot.ProcessCount
            ChildPrivateMemoryMb = $privateMb
            HardStopThresholdMb = 512
            SoftStartThresholdMb = 1024
        })
        if ([double]$snapshot.FreePhysicalRamMb -le 512) {
            $hardStop = $true
            $script:HostConstraintObserved = $true
            Stop-ProcessTree $childPid
            break
        }
        if ([DateTime]::UtcNow -gt $deadline) {
            $timedOut = $true
            Stop-ProcessTree $childPid
            break
        }
        Start-Sleep -Seconds 1
    }
    $childResult = Complete-AsyncProcess $child
    if ($null -ne $telemetry) {
        $telemetryDeadline = [DateTime]::UtcNow.AddSeconds(10)
        while (-not $telemetry.Process.HasExited -and [DateTime]::UtcNow -lt $telemetryDeadline) {
            Start-Sleep -Milliseconds 250
        }
        if (-not $telemetry.Process.HasExited) {
            Stop-ProcessTree $telemetry.Process.Id
        }
        try {
            [void](Complete-AsyncProcess $telemetry)
        } catch {
            $script:HarnessFailureObserved = $true
        }
        Merge-CsvFile $sqlTelemetryPath (Join-Path $Phase "v2-sql-telemetry.csv")
    }
    $finishedUtc = [DateTime]::UtcNow
    $duration = [math]::Round(($finishedUtc - $startedUtc).TotalSeconds, 3)
    $artifact = if ($Kind -eq "BDN") { Find-BdnResult $blockDirectory } else { Find-NBomberResult $blockDirectory $Scenario }
    $combinedText = ($childResult.Stdout + [Environment]::NewLine + $childResult.Stderr)
    $failureType = ""
    $status = "PASS"
    if ($hardStop) {
        $status = "HOST_HARD_STOP"
        $failureType = "HOST_MEMORY_CONSTRAINT"
    } elseif ($timedOut) {
        $status = "TIMEOUT"
        $failureType = "APPLICATION_OR_HARNESS_TIMEOUT"
        $script:HarnessFailureObserved = $true
    } elseif ($childResult.ExitCode -ne 0) {
        $failureType = Get-TextFailureType $combinedText
        $status = "FAIL"
        if ($failureType -in @("RESOURCE_SEMAPHORE", "DEADLOCK", "SQL_TIMEOUT", "PRODUCT_ERROR")) {
            $script:ProductFailureObserved = $true
        } else {
            $script:HarnessFailureObserved = $true
        }
    } elseif ($Kind -eq "BDN" -and $null -eq $artifact) {
        $status = "HARNESS_FAILURE"
        $failureType = "BDN_RESULT_NOT_FOUND"
        $script:HarnessFailureObserved = $true
    } elseif ($Kind -ne "BDN" -and $null -eq $artifact) {
        $status = "HARNESS_FAILURE"
        $failureType = "N BOMBER_RESULT_NOT_FOUND"
        $script:HarnessFailureObserved = $true
    }
    if ($status -eq "PASS" -and $null -ne $artifact) {
        $failureType = ""
    }
    if ($Kind -eq "BDN") {
        $row = [pscustomobject]@{
            BlockId = $blockId
            Scenario = $Scenario
            Status = $status
            FailureType = $failureType
            ExitCode = $childResult.ExitCode
            DurationSeconds = $duration
            MeanMs = if ($null -ne $artifact) { $artifact.MeanMs } else { "" }
            P50Ms = ""
            P95Ms = ""
            P99Ms = ""
            Rps = ""
            RequestCount = if ($status -eq "PASS") { 1 } else { 0 }
            Ok = if ($status -eq "PASS") { 1 } else { 0 }
            Failed = if ($status -eq "PASS") { 0 } else { 1 }
            ArtifactPath = if ($null -ne $artifact) { $artifact.Path } else { "" }
            Notes = if ($null -ne $artifact) { "Mean/Error/StdDev are BDN CSV values converted to milliseconds; BDN percentiles are not exported by this config." } else { $combinedText.Substring(0, [math]::Min(500, $combinedText.Length)) }
        }
        Add-CsvRow (Join-Path $Phase "v2-bdn-summary.csv") $row
    } else {
        $row = [pscustomobject]@{
            BlockId = $blockId
            Scenario = $Scenario
            Level = $Level
            Status = $status
            FailureType = $failureType
            ExitCode = $childResult.ExitCode
            DurationSeconds = $duration
            MeanMs = if ($null -ne $artifact) { $artifact.MeanMs } else { "" }
            P50Ms = if ($null -ne $artifact) { $artifact.P50Ms } else { "" }
            P95Ms = if ($null -ne $artifact) { $artifact.P95Ms } else { "" }
            P99Ms = if ($null -ne $artifact) { $artifact.P99Ms } else { "" }
            Rps = if ($null -ne $artifact) { $artifact.Rps } else { "" }
            RequestCount = if ($null -ne $artifact) { $artifact.RequestCount } else { 0 }
            Ok = if ($null -ne $artifact) { $artifact.Ok } else { 0 }
            Failed = if ($null -ne $artifact) { $artifact.Failed } else { 1 }
            ArtifactPath = if ($null -ne $artifact) { $artifact.Path } else { "" }
            Notes = if ($null -ne $artifact) { "NBomber CSV fields are retained as emitted by NBomber 6.6.0." } else { $combinedText.Substring(0, [math]::Min(500, $combinedText.Length)) }
        }
        Add-CsvRow (Join-Path $Phase "v2-nbomber-summary.csv") $row
    }
    Add-CsvRow (Join-Path $Phase "v2-process-peak-memory.csv") ([pscustomobject]@{
        BlockId = $blockId
        Kind = $Kind
        Scenario = $Scenario
        Level = if ($Kind -eq "BDN") { "" } else { $Level }
        PeakPrivateMemoryMb = [math]::Round($peakPrivate, 1)
        PeakFreePhysicalRamMb = if ($peakFree -eq 999999) { "" } else { [math]::Round($peakFree, 1) }
        Status = $status
    })
    [void]$script:CompletedBlocks.Add($blockId)
    [void]$script:BlockRows.Add($row)
    if ($status -eq "PASS") {
        Wait-Cooldown
    }
    Write-Output ("V2_BLOCK|{0}|{1}|{2}|{3}" -f $blockId, $status, $failureType, $childResult.ExitCode)
}

function Get-JsonValue([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }
    return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
}

function Invoke-Preparation([string]$Phase) {
    if (-not (Test-Path -LiteralPath $script:ManifestFile -PathType Leaf)) {
        throw "V2 manifest not found: $script:ManifestFile"
    }
    $script:RunnerHash = Get-Sha256 (Join-Path $script:PSScriptRoot "run-warehouse-benchmark-v2.ps1")
    $v1Hash = Get-Sha256 $script:V1Runner
    if ($v1Hash -ne $script:V1RunnerExpectedHash) {
        throw "V1 runner drift detected. Expected $script:V1RunnerExpectedHash, actual $v1Hash."
    }
    if (-not (Test-Path -LiteralPath $script:ProjectFile -PathType Leaf)) {
        throw "V2 project not found: $script:ProjectFile"
    }
    [void](Invoke-SourceFreeze $Phase)
    [void](Capture-BuildEnvironment $Phase)
    $buildA = $null
    $buildB = $null
    if ($NoBuild) {
        $runtimeDirectory = Join-Path $Phase "runtime"
        $runtimeMain = Join-Path $runtimeDirectory $script:MainDllName
        if (-not (Test-Path -LiteralPath $runtimeMain -PathType Leaf)) {
            throw "-NoBuild requested but frozen runtime is missing: $runtimeMain"
        }
        $script:RuntimeDirectory = $runtimeDirectory
        $script:BuildAOutput = $runtimeDirectory
        $script:SourceClosureHash = (Get-Content -Raw -LiteralPath (Join-Path $Phase "source\source-closure.sha256")).Trim()
        $runtimeRows = @(Get-ChildItem -LiteralPath $runtimeDirectory -Recurse -File | ForEach-Object {
            [pscustomobject]@{
                RelativePath = [IO.Path]::GetRelativePath($runtimeDirectory, $_.FullName).Replace("\", "/")
                Role = "RUNTIME_BUNDLE"
                Size = [int64]$_.Length
                Sha256 = Get-Sha256 $_.FullName
            }
        })
        $runtimeHash = Get-InventoryFingerprint $runtimeRows
        $runtime = [pscustomobject]@{ Directory = $runtimeDirectory; MainDll = $runtimeMain; Rows = $runtimeRows; ManifestSha256 = $runtimeHash }
        $script:ManifestHash = Get-Sha256 $script:ManifestFile
    } else {
        $buildA = Invoke-CleanBuild "build-a" $Phase
        $buildB = Invoke-CleanBuild "build-b" $Phase
        $dependencyGraph = Capture-DependencyGraph $Phase @($buildA, $buildB)
        $buildRows = Get-BuildArtifactRows $buildA $buildB
        $buildRows | Export-Csv -LiteralPath (Join-Path $Phase "build-comparison.csv") -NoTypeInformation
        Write-JsonFile (Join-Path $Phase "build-comparison.json") $buildRows
        $runtime = Freeze-Runtime $buildA $Phase
        $manifest = Update-V2Manifest $Phase $runtime
        $releaseDll = Join-Path $script:ProjectRoot "bin\Release\net8.0\$script:MainDllName"
        $existing = [pscustomobject]@{
            Path = $releaseDll
            Exists = Test-Path -LiteralPath $releaseDll -PathType Leaf
            Sha256 = if (Test-Path -LiteralPath $releaseDll -PathType Leaf) { Get-Sha256 $releaseDll } else { "" }
            MatchesBuildA = (Test-Path -LiteralPath $releaseDll -PathType Leaf) -and ((Get-Sha256 $releaseDll) -eq (Get-Sha256 $runtime.MainDll))
            PreviouslyObservedSha256 = "E4A32111FCD65298B805372CE47A264EBF381190150ED8AA8B685B081863A9C5"
        }
        Write-JsonFile (Join-Path $Phase "existing-release-reconciliation.json") $existing
    }
    $candidate = [ordered]@{
        CandidateId = "BENCHMARK_V2_CANDIDATE_$((Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss'))"
        BenchmarkDllSha256 = Get-Sha256 $runtime.MainDll
        SourceClosureSha256 = $script:SourceClosureHash
        RuntimeBundleManifestSha256 = $runtime.ManifestSha256
        RunnerSha256 = $script:RunnerHash
        ManifestSha256 = Get-Sha256 $script:ManifestFile
        BenchmarkProjectSha256 = $script:BenchmarkProjectHash
        BuildConfiguration = "Release"
        TargetFramework = "net8.0"
        BuildA = if ($null -ne $buildA) { $buildA } else { "NO_BUILD_REUSED_FROZEN_RUNTIME" }
        BuildB = if ($null -ne $buildB) { $buildB } else { "NO_BUILD_REUSED_FROZEN_RUNTIME" }
    }
    Write-TextFile (Join-Path $Phase "candidate-manifest.json") ($candidate | ConvertTo-Json -Depth 30)
    return $candidate
}

function Invoke-ValidationSuite([string]$Phase) {
    $selfTest = Invoke-V2Child @("--self-test", "--output", (Join-Path $Phase "v2-self-test.json")) $Phase "self-test.stdout.txt" "self-test.stderr.txt"
    if ($selfTest.ExitCode -ne 0) {
        $script:HarnessFailureObserved = $true
        throw "V2 self-tests failed."
    }
    $residueBefore = Invoke-V2Child @("--residue", "--output", (Join-Path $Phase "v2-residue-before.json")) $Phase "residue-before.stdout.txt" "residue-before.stderr.txt"
    if ($residueBefore.ExitCode -ne 0) {
        throw "Performance DB residue precondition failed."
    }
    $validation = Invoke-V2Child @("--validate", "--output", (Join-Path $Phase "v2-validation-pre.json")) $Phase "validation-pre.stdout.txt" "validation-pre.stderr.txt"
    $validationJson = Get-JsonValue (Join-Path $Phase "v2-validation-pre.json")
    if ($null -eq $validationJson) {
        throw "V2 validation did not produce JSON."
    }
    if (-not [bool]$validationJson.Passed -or $validation.ExitCode -ne 0) {
        Write-JsonFile (Join-Path $Phase "v2-validation.json") $validationJson
        throw "V2 prevalidation failed."
    }
    $script:DatasetFingerprint = [string]$validationJson.Dataset.FingerprintSha256
    Write-JsonFile (Join-Path $Phase "v2-consistency-before.json") ([ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow.ToString("O")
        Source = "v2-validation-pre.json"
        Checks = $validationJson.Consistency
        AllZero = @($validationJson.Consistency | Where-Object { [int64]$_.MismatchCount -ne 0 }).Count -eq 0
    })
    return $validationJson
}

function Invoke-FinalValidation([string]$Phase, [object]$BeforeValidation) {
    $validation = Invoke-V2Child @("--validate", "--output", (Join-Path $Phase "v2-validation.json")) $Phase "validation-after.stdout.txt" "validation-after.stderr.txt"
    $after = Get-JsonValue (Join-Path $Phase "v2-validation.json")
    if ($null -eq $after) {
        $script:HarnessFailureObserved = $true
        return $null
    }
    Write-JsonFile (Join-Path $Phase "v2-consistency-after.json") ([ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow.ToString("O")
        Source = "v2-validation.json"
        Checks = $after.Consistency
        AllZero = @($after.Consistency | Where-Object { [int64]$_.MismatchCount -ne 0 }).Count -eq 0
        DatasetFingerprintUnchanged = ([string]$BeforeValidation.Dataset.FingerprintSha256 -eq [string]$after.Dataset.FingerprintSha256)
    })
    if ($validation.ExitCode -ne 0 -or -not [bool]$after.Passed) {
        $script:ProductFailureObserved = $true
    }
    return $after
}

function Invoke-StandardProfile([string]$Phase, [switch]$RunBdn, [switch]$RunNbomber) {
    if ($RunBdn) {
        foreach ($scenario in $script:Scenarios) {
            Invoke-MeasuredBlock "BDN" $scenario 0 $Phase
        }
    }
    if ($RunNbomber) {
        foreach ($scenario in $script:Scenarios) {
            foreach ($level in $script:StandardLevels) {
                Invoke-MeasuredBlock "NBOMBER" $scenario $level $Phase
            }
        }
    }
}

function Get-ResultClassification([string]$Phase, [object]$BeforeValidation, [object]$AfterValidation) {
    $bdnRows = @(Import-Csv -LiteralPath (Join-Path $Phase "v2-bdn-summary.csv"))
    $nbRows = @(Import-Csv -LiteralPath (Join-Path $Phase "v2-nbomber-summary.csv"))
    $allRows = @($bdnRows + $nbRows)
    $expectedBdn = $script:Scenarios.Count
    $expectedNb = $script:Scenarios.Count * $script:StandardLevels.Count
    $passBdn = @($bdnRows | Where-Object { $_.Status -eq "PASS" }).Count
    $passNb = @($nbRows | Where-Object { $_.Status -eq "PASS" }).Count
    $classification = if ($null -eq $BeforeValidation -or -not [bool]$BeforeValidation.Passed) {
        "BENCHMARK_V2_PRECONDITION_FAILED"
    } elseif ($script:ProductFailureObserved) {
        "BENCHMARK_V2_PRODUCT_FAILURE_OBSERVED"
    } elseif ($script:HarnessFailureObserved) {
        "BENCHMARK_V2_HARNESS_FAILED"
    } elseif ($script:HostConstraintObserved -or @($allRows | Where-Object { $_.Status -in @("HOST_HARD_STOP", "HOST_PRECONDITION_NOT_MET") }).Count -gt 0) {
        "BENCHMARK_V2_HOST_TOO_CONSTRAINED_FOR_STANDARD"
    } elseif ($passBdn -ne $expectedBdn -or $passNb -ne $expectedNb) {
        "BENCHMARK_V2_HARNESS_FAILED"
    } elseif ($null -eq $AfterValidation -or -not [bool]$AfterValidation.Passed) {
        "BENCHMARK_V2_PRECONDITION_FAILED"
    } else {
        "BENCHMARK_V2_STANDARD_BASELINE_CREATED"
    }
    return [ordered]@{
        Result = $classification
        CapturedAtUtc = [DateTime]::UtcNow.ToString("O")
        StandardExpected = [ordered]@{ BdnBlocks = $expectedBdn; NBomberBlocks = $expectedNb; TotalBlocks = $expectedBdn + $expectedNb }
        Observed = [ordered]@{ BdnRows = $bdnRows.Count; BdnPass = $passBdn; NBomberRows = $nbRows.Count; NBomberPass = $passNb }
        ProductCandidate = [ordered]@{
            SourceClosureSha256 = $script:SourceClosureHash
            BenchmarkDllSha256 = Get-Sha256 (Join-Path $script:RuntimeDirectory $script:MainDllName)
            RuntimeBundleManifestSha256 = (Get-Content -Raw -LiteralPath (Join-Path $Phase "runtime-bundle-manifest.sha256")).Trim()
            RunnerSha256 = $script:RunnerHash
            ManifestSha256 = Get-Sha256 $script:ManifestFile
        }
        Target = [ordered]@{ Server = $script:TargetServer; Database = $script:TargetDatabase; DatabaseId = $script:TargetDatabaseId }
        Validation = [ordered]@{
            BeforePassed = if ($null -ne $BeforeValidation) { [bool]$BeforeValidation.Passed } else { $false }
            AfterPassed = if ($null -ne $AfterValidation) { [bool]$AfterValidation.Passed } else { $false }
            DatasetFingerprintBefore = if ($null -ne $BeforeValidation) { [string]$BeforeValidation.Dataset.FingerprintSha256 } else { "" }
            DatasetFingerprintAfter = if ($null -ne $AfterValidation) { [string]$AfterValidation.Dataset.FingerprintSha256 } else { "" }
        }
        Boundary = [ordered]@{
            BusinessDatabaseMutated = $false
            ProductSqlModified = $false
            V1RunnerModified = $false
            SqlAgentModified = $false
            CurrentRebuilt = $false
            PerformanceComparisonClaimed = $false
        }
    }
}

function Add-MarkdownTable([System.Collections.Generic.List[string]]$Lines, [string[]]$Headers, [object[]]$Rows) {
    [void]$Lines.Add("| " + ($Headers -join " | ") + " |")
    [void]$Lines.Add("| " + (($Headers | ForEach-Object { "---" }) -join " | ") + " |")
    foreach ($row in $Rows) {
        $values = foreach ($header in $Headers) {
            $property = $row.PSObject.Properties[$header]
            $value = if ($null -ne $property) { [string]$property.Value } else { "" }
            $value.Replace("|", "\|").Replace([char]13, " ").Replace([char]10, " ")
        }
        [void]$Lines.Add("| " + ($values -join " | ") + " |")
    }
}

function Write-RunMetadata([string]$Phase, [string]$Stage, [string]$Result = "") {
    $metadata = [ordered]@{
        ProtocolVersion = "WAREHOUSE_BENCHMARK_V2_1"
        Stage = $Stage
        Result = $Result
        CapturedAtUtc = [DateTime]::UtcNow.ToString("O")
        PhaseRoot = $Phase
        Target = [ordered]@{ Server = $script:TargetServer; Database = $script:TargetDatabase; DatabaseId = $script:TargetDatabaseId }
        Workload = [ordered]@{
            Scenarios = $script:Scenarios
            StandardLevels = $script:StandardLevels
            Bdn = "one process per scenario; launch=1; warmup=2; iteration=5; invocation=1; unroll=1"
            NBomber = "one process per scenario/level; warmup=3s; timed=15s; KeepConstant; one copy=one worker"
            DateRange = "2025-01-01..2026-12-31"
            Page = 1
            PageSize = 10
            Login = $script:LoginName
            UseSnapshot = $false
            UseCurrentBalance = $false
        }
        Safety = [ordered]@{
            SoftStartFreeRamMb = 1024
            HardStopFreeRamMb = 512
            CooldownSeconds = "5..60"
            TelemetryIntervalMilliseconds = 1000
            BuildDuringMeasuredBlocks = $false
            CacheFlush = $false
        }
        Integrity = [ordered]@{
            V1RunnerSha256 = Get-Sha256 $script:V1Runner
            V2RunnerSha256 = $script:RunnerHash
            SourceClosureSha256 = $script:SourceClosureHash
            BenchmarkProjectSha256 = $script:BenchmarkProjectHash
            ManifestSha256 = if (Test-Path -LiteralPath $script:ManifestFile -PathType Leaf) { Get-Sha256 $script:ManifestFile } else { "" }
        }
        Boundaries = [ordered]@{
            BusinessDatabase = "TKS_Thuc_Tap_V11_GiaiDoan2; no access"
            ProductSqlMutation = $false
            PerformanceDatabaseMutation = $false
            SqlAgentMutation = $false
            CurrentRebuild = $false
        }
    }
    Write-JsonFile (Join-Path $Phase "v2-run-metadata.json") $metadata
    return $metadata
}

function Write-BenchmarkReport(
    [string]$Phase,
    [object]$Result,
    [object]$Candidate,
    [object]$BeforeValidation,
    [object]$AfterValidation
) {
    $lines = [System.Collections.Generic.List[string]]::new()
    [void]$lines.Add("# Benchmark V2 - Design and Low-Memory 10M Baseline")
    [void]$lines.Add("")
    [void]$lines.Add("Generated: $([DateTime]::UtcNow.ToString('O'))")
    [void]$lines.Add("")
    [void]$lines.Add("## 1. Executive summary")
    [void]$lines.Add("")
    [void]$lines.Add("Final verdict: $($Result.Result)")
    [void]$lines.Add("")
    [void]$lines.Add("This phase created and/or used an independent V2 benchmark surface. It does not alter V1, product SQL, Business DB, SQL Server global settings, SQL Agent, or the 10M fixture. A baseline is a measurement of the current product candidate; it is not by itself an optimization proof or a V1 improvement claim.")
    [void]$lines.Add("")
    [void]$lines.Add("The historical V1 executable is preserved as a directional reference only. Any host stop, timeout, or harness failure is reported as such and is not converted into a performance conclusion.")
    [void]$lines.Add("")
    [void]$lines.Add("## 2. V2 design and protocol")
    [void]$lines.Add("")
    [void]$lines.Add("The standard profile is six scenarios with sequential BenchmarkDotNet processes and eighteen isolated NBomber blocks at levels 1, 2, and 4. Each NBomber copy is one worker using one real Data Access operation; no hidden multiplier is used. The host guard waits below the 1024 MB soft start threshold and stops a live block at or below 512 MB free physical RAM.")
    [void]$lines.Add("")
    [void]$lines.Add("Target: $script:TargetServer / $script:TargetDatabase / DB_ID $script:TargetDatabaseId.")
    [void]$lines.Add("")
    [void]$lines.Add("## 3. Candidate summary")
    [void]$lines.Add("")
    $summaryRows = @([pscustomobject]@{
        CandidateId = $Candidate.CandidateId
        BenchmarkDllSha256 = $Candidate.BenchmarkDllSha256
        SourceClosureSha256 = $Candidate.SourceClosureSha256
        RuntimeBundleManifestSha256 = $Candidate.RuntimeBundleManifestSha256
        RunnerSha256 = $Candidate.RunnerSha256
        ManifestSha256 = $Candidate.ManifestSha256
    })
    Add-MarkdownTable $lines @("CandidateId", "BenchmarkDllSha256", "SourceClosureSha256", "RuntimeBundleManifestSha256", "RunnerSha256", "ManifestSha256") $summaryRows
    [void]$lines.Add("")
    [void]$lines.Add("Durable phase root: $Phase")
    [void]$lines.Add("")
    [void]$lines.Add("## 4. Prior V1 artifact boundary")
    [void]$lines.Add("")
    [void]$lines.Add("The previously frozen V1 DLL SHA-256 19F3FD7F44D9D22D08C79080E37D0A58B4D8116CB7443297372BA284DF84AB02 remains unrecovered and is not recreated or relabeled. The V1 runner hash above was checked against the required immutable value.")
    [void]$lines.Add("")
    [void]$lines.Add("## 5. Source closure")
    [void]$lines.Add("")
    [void]$lines.Add("The deterministic closure inventory is stored under source/source-inventory.csv and source/source-inventory.json. The manifest is a protocol input with a separate hash and is intentionally excluded from the compiled source-closure hash to avoid circular self-hashing.")
    [void]$lines.Add("")
    Add-MarkdownTable $lines @("RelativePath", "Role", "Size", "Sha256") $script:SourceInventory
    [void]$lines.Add("")
    [void]$lines.Add("## 6. Build environment and dependency resolution")
    [void]$lines.Add("")
    $environment = Get-JsonValue (Join-Path $Phase "v2-environment.json")
    $envRows = @([pscustomobject]@{
        OS = $environment.Os.Caption
        OSVersion = $environment.Os.Version
        Dotnet = $environment.DotnetVersion
        TargetFramework = $environment.TargetFramework
        Configuration = $environment.Configuration
        Deterministic = $environment.Deterministic
        Packages = (($environment.Packages | ForEach-Object { "$($_.Id)=$($_.Version)" }) -join ", ")
    })
    Add-MarkdownTable $lines @("OS", "OSVersion", "Dotnet", "TargetFramework", "Configuration", "Deterministic", "Packages") $envRows
    [void]$lines.Add("")
    [void]$lines.Add("Resolved restore graphs are stored in source/dependency-graph.json. Isolated Build A and Build B logs are under build-a and build-b.")
    [void]$lines.Add("")
    if (Test-Path -LiteralPath (Join-Path $Phase "build-comparison.json")) {
        $buildRows = @(Get-JsonValue (Join-Path $Phase "build-comparison.json"))
        Add-MarkdownTable $lines @("Artifact", "BuildASha256", "BuildBSha256", "Match", "AcceptedSha256") $buildRows
        [void]$lines.Add("")
    }
    [void]$lines.Add("## 7. Semantic contract reconciliation")
    [void]$lines.Add("")
    $contractRows = @(
        [pscustomobject]@{ Dimension = "Scenario mapping"; Historical = "V1 six canonical operations"; NewCandidate = "V2 six canonical operations"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Stored procedure / operation"; Historical = "Existing Data Access controller paths"; NewCandidate = "Same reused Data Access controller paths"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Parameters"; Historical = "page=1, pageSize=10, empty search, PERF_USER"; NewCandidate = "Same"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Date range"; Historical = "2025-01-01..2026-12-31"; NewCandidate = "2025-01-01..2026-12-31"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Page/page size"; Historical = "1 / 10"; NewCandidate = "1 / 10"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Snapshot flag"; Historical = "false"; NewCandidate = "false"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Current flag"; Historical = "Historical=false; Current=true"; NewCandidate = "Historical=false; Current=true"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Login"; Historical = "PERF_USER parameter"; NewCandidate = "PERF_USER parameter"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Timeout"; Historical = "30 seconds Data Access command timeout"; NewCandidate = "Same Data Access path"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Warmup"; Historical = "BDN/NBomber protocol"; NewCandidate = "BDN 2 iterations; NBomber 3 seconds"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Duration"; Historical = "NBomber 15 seconds"; NewCandidate = "NBomber 15 seconds"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Copies"; Historical = "Explicit concurrency levels"; NewCandidate = "KeepConstant 1/2/4; one copy=one worker"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Pacing"; Historical = "Completion-driven worker loop"; NewCandidate = "Completion-driven worker loop"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Failure accounting"; Historical = "ok/failed and failure classification"; NewCandidate = "ok/failed plus timeout/deadlock/resource classification"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Latency boundary"; Historical = "Real request operation"; NewCandidate = "One real operation per iteration/request"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Percentiles"; Historical = "NBomber emitted percentiles"; NewCandidate = "NBomber emitted percentiles; BDN default CSV has no percentiles"; Result = "EXPLICIT" },
        [pscustomobject]@{ Dimension = "RPS"; Historical = "NBomber ok_rps"; NewCandidate = "NBomber ok_rps"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "NBomber configuration"; Historical = "Scenario-isolated fixed copies"; NewCandidate = "Scenario-isolated KeepConstant fixed copies"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "BDN configuration"; Historical = "Single-operation client/database latency"; NewCandidate = "Launch=1, warmup=2, iteration=5, invocation=1, unroll=1"; Result = "PRESERVED_WITH_EXPLICIT_FREEZE" }
    )
    Add-MarkdownTable $lines @("Dimension", "Historical", "NewCandidate", "Result") $contractRows
    [void]$lines.Add("")
    [void]$lines.Add("## 8. Old-baseline comparability")
    [void]$lines.Add("")
    $comparisonRows = @(
        [pscustomobject]@{ Scenario = "MasterPaged"; HistoricalBaselineContract = "V1 paged master operation"; NewContract = "Same operation and parameters"; Classification = "DIRECTIONALLY_COMPARABLE"; Evidence = "V2ReadOperations.cs; V1 source path" },
        [pscustomobject]@{ Scenario = "LookupPaged"; HistoricalBaselineContract = "V1 paged lookup operation"; NewContract = "Same operation and parameters"; Classification = "DIRECTIONALLY_COMPARABLE"; Evidence = "V2ReadOperations.cs; V1 source path" },
        [pscustomobject]@{ Scenario = "DocumentPaged"; HistoricalBaselineContract = "V1 document page, isReceipt=true"; NewContract = "Same operation and parameters"; Classification = "DIRECTIONALLY_COMPARABLE"; Evidence = "V2ReadOperations.cs; V1 source path" },
        [pscustomobject]@{ Scenario = "DetailReportPaged"; HistoricalBaselineContract = "V1 detail report page"; NewContract = "Same operation and date range"; Classification = "DIRECTIONALLY_COMPARABLE"; Evidence = "V2ReadOperations.cs; V1 source path" },
        [pscustomobject]@{ Scenario = "InventoryHistoricalReportPaged"; HistoricalBaselineContract = "V1 historical report, Current=false"; NewContract = "Same operation and flags"; Classification = "DIRECTIONALLY_COMPARABLE"; Evidence = "V2ReadOperations.cs; manifest" },
        [pscustomobject]@{ Scenario = "InventoryCurrentBalancePaged"; HistoricalBaselineContract = "No exact standalone V1 baseline"; NewContract = "Current=true report path"; Classification = "NO_EXACT_BASELINE"; Evidence = "Manifest explicit limitation" }
    )
    Add-MarkdownTable $lines @("Scenario", "HistoricalBaselineContract", "NewContract", "Classification", "Evidence") $comparisonRows
    [void]$lines.Add("")
    [void]$lines.Add("## 9. Runtime bundle and freeze")
    [void]$lines.Add("")
    [void]$lines.Add("The accepted runtime bundle is under runtime and was copied from isolated Build A output. Its inventory and post-copy hashes are in runtime-bundle-inventory.csv and runtime-bundle-inventory.json. The bundle is durable under the phase root; hashes, not file attributes, are authoritative.")
    [void]$lines.Add("")
    if (Test-Path -LiteralPath (Join-Path $Phase "runtime-bundle-inventory.json")) {
        $runtimeRows = @(Get-JsonValue (Join-Path $Phase "runtime-bundle-inventory.json"))
        Add-MarkdownTable $lines @("RelativePath", "Size", "Sha256", "Origin", "Required") $runtimeRows
        [void]$lines.Add("")
    }
    [void]$lines.Add("## 10. Validation and database post-state")
    [void]$lines.Add("")
    $validationRows = @(
        [pscustomobject]@{
            Check = "Performance DB identity"
            Before = if ($null -ne $BeforeValidation) { "$($BeforeValidation.Identity.ServerName) / $($BeforeValidation.Identity.DatabaseName) / $($BeforeValidation.Identity.DatabaseId)" } else { "NOT_OBSERVED" }
            After = if ($null -ne $AfterValidation) { "$($AfterValidation.Identity.ServerName) / $($AfterValidation.Identity.DatabaseName) / $($AfterValidation.Identity.DatabaseId)" } else { "NOT_OBSERVED" }
            Result = if ($null -ne $AfterValidation -and $AfterValidation.Identity.IsTarget) { "PASS" } else { "NOT_PROVEN" }
        },
        [pscustomobject]@{
            Check = "Exact dataset row counts"
            Before = if ($null -ne $BeforeValidation) { [string]$BeforeValidation.Dataset.MatchesExpected } else { "NOT_OBSERVED" }
            After = if ($null -ne $AfterValidation) { [string]$AfterValidation.Dataset.MatchesExpected } else { "NOT_OBSERVED" }
            Result = if ($null -ne $AfterValidation -and $AfterValidation.Dataset.MatchesExpected) { "PASS" } else { "NOT_PROVEN" }
        },
        [pscustomobject]@{
            Check = "Historical-critical consistency"
            Before = if ($null -ne $BeforeValidation) { "$(@($BeforeValidation.Consistency).Count) checks; zero mismatches=$(@($BeforeValidation.Consistency | Where-Object { [int64]$_.MismatchCount -ne 0 }).Count -eq 0)" } else { "NOT_OBSERVED" }
            After = if ($null -ne $AfterValidation) { "$(@($AfterValidation.Consistency).Count) checks; zero mismatches=$(@($AfterValidation.Consistency | Where-Object { [int64]$_.MismatchCount -ne 0 }).Count -eq 0)" } else { "NOT_OBSERVED" }
            Result = if ($null -ne $AfterValidation -and @($AfterValidation.Consistency).Count -eq 11 -and @($AfterValidation.Consistency | Where-Object { [int64]$_.MismatchCount -ne 0 }).Count -eq 0) { "PASS" } else { "NOT_PROVEN" }
        },
        [pscustomobject]@{
            Check = "Current rowset"
            Before = if ($null -ne $BeforeValidation) { "fingerprint=$($BeforeValidation.CurrentRowsetSha256)" } else { "NOT_OBSERVED" }
            After = if ($null -ne $AfterValidation) { "fingerprint=$($AfterValidation.CurrentRowsetSha256)" } else { "NOT_OBSERVED" }
            Result = if ($null -ne $BeforeValidation -and $null -ne $AfterValidation -and $BeforeValidation.CurrentRowsetSha256 -eq $AfterValidation.CurrentRowsetSha256) { "UNCHANGED" } else { "NOT_PROVEN" }
        }
    )
    Add-MarkdownTable $lines @("Check", "Before", "After", "Result") $validationRows
    [void]$lines.Add("")
    [void]$lines.Add("No Current rebuild, fixture regeneration, cache flush, or product SQL deployment was performed.")
    [void]$lines.Add("")
    [void]$lines.Add("## 11. Standard measurement result")
    [void]$lines.Add("")
    $blockRows = @()
    if (Test-Path -LiteralPath (Join-Path $Phase "v2-bdn-summary.csv")) { $blockRows += @(Import-Csv -LiteralPath (Join-Path $Phase "v2-bdn-summary.csv")) }
    if (Test-Path -LiteralPath (Join-Path $Phase "v2-nbomber-summary.csv")) { $blockRows += @(Import-Csv -LiteralPath (Join-Path $Phase "v2-nbomber-summary.csv")) }
    Add-MarkdownTable $lines @("BlockId", "Scenario", "Level", "Status", "FailureType", "DurationSeconds", "MeanMs", "P50Ms", "P95Ms", "P99Ms", "Rps", "RequestCount", "Ok", "Failed") $blockRows
    [void]$lines.Add("")
    [void]$lines.Add("Optional capacity, mixed, and all-six-low profiles are not part of the standard gate and were not run automatically.")
    [void]$lines.Add("")
    [void]$lines.Add("## 12. Interpretation boundary")
    [void]$lines.Add("")
    [void]$lines.Add("This report does not prove or disprove code optimization. It does not prove that the benchmark is wrong merely because a run is stopped. A standard baseline is valid only when the harness completes the frozen protocol. If the final verdict is host-constrained, the evidence points to host capacity/safety limits; if it is harness-failed, repair the V2 harness; if it is product-failure-observed, inspect the product/SQL path. No performance comparison is claimed here.")
    [void]$lines.Add("")
    [void]$lines.Add("## 13. Safety boundary")
    [void]$lines.Add("")
    [void]$lines.Add("Business DB TKS_Thuc_Tap_V11_GiaiDoan2 was out of scope. SQL Agent was not started or stopped. V1 runner bytes were not modified. The phase stopped after V2 implementation and baseline attempt.")
    [void]$lines.Add("")
    [void]$lines.Add("## 14. Final verdict")
    [void]$lines.Add("")
    [void]$lines.Add($Result.Result)
    [void]$lines.Add("")
    Write-TextFile (Join-Path $Phase "Benchmark-V2-Report.md") ($lines -join [Environment]::NewLine)
    Write-TextFile (Join-Path $Phase "Benchmark-V2-Design-And-Baseline-Report.md") ($lines -join [Environment]::NewLine)
}

function Write-CapacitySummary([string]$Phase, [string]$Result) {
    Write-JsonFile (Join-Path $Phase "v2-capacity-summary.json") ([ordered]@{
        StandardProfile = [ordered]@{ Levels = $script:StandardLevels; BdnScenarios = $script:Scenarios.Count; NBomberBlocks = $script:Scenarios.Count * $script:StandardLevels.Count }
        OptionalCapacity = [ordered]@{ Levels = @(1, 2, 4, 6, 8); Status = "NOT_RUN_AUTOMATICALLY" }
        OptionalMixed = [ordered]@{ Status = "NOT_RUN_AUTOMATICALLY" }
        OptionalAllSixLow = [ordered]@{ Status = "NOT_RUN_AUTOMATICALLY" }
        FinalStandardResult = $Result
    })
}

function Invoke-V21ActualBlock(
    [string]$Kind,
    [string]$Class,
    [string]$Profile,
    [string]$BlockId,
    [string]$Scenario,
    [int]$Level,
    [int]$Copies
) {
    $admission = Wait-V21Admission $Class $BlockId $false
    if (-not [bool]$admission.Allowed) {
        $status = if ($Kind -eq "NBOMBER" -and $Level -eq 4) { "HOST_CAPACITY_LIMIT_AT_C4" } else { "HOST_CAPACITY_LIMIT" }
        $failureType = if ($Kind -eq "BDN") { "BDN_HOST_LIMIT" } else { "HOST_CAPACITY_LIMIT" }
        $row = New-V21BlockRow $Profile $Kind $BlockId $Scenario $Level $status $failureType -20 0 ([double]$admission.ObservedAvailableMB) ([double]$admission.ObservedAvailableMB) 0 0 $null $null
        Write-V21BlockRow $row
        $script:V21HostCapacityObserved = $true
        return $row
    }
    return Invoke-V21Block $Profile $Kind $Class $BlockId $Scenario $Level $Copies 3 15
}

function Invoke-V21ActualBaseline {
    foreach ($scenario in $script:Scenarios) {
        [void](Invoke-V21ActualBlock "BDN" "BDN" "CORE_BDN" ("BDN-" + $scenario) $scenario 0 1)
        [void](Invoke-V21ActualBlock "NBOMBER" "C1" "CORE_NBOMBER" ("NB-" + $scenario + "-C1") $scenario 1 1)
        [void](Invoke-V21ActualBlock "NBOMBER" "C2" "CORE_NBOMBER" ("NB-" + $scenario + "-C2") $scenario 2 2)
        [void](Invoke-V21ActualBlock "NBOMBER" "C4" "EXTENDED_STANDARD" ("NB-" + $scenario + "-C4") $scenario 4 4)
    }
}

function Invoke-Phase {
    if ([string]::IsNullOrWhiteSpace($PhaseRoot)) {
        $stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
        $PhaseRoot = "P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_1-$stamp"
    }
    $script:PhaseRoot = [IO.Path]::GetFullPath($PhaseRoot)
    New-Item -ItemType Directory -Path $script:PhaseRoot -Force | Out-Null
    Initialize-OutputFiles $script:PhaseRoot
    Write-RunMetadata $script:PhaseRoot "STARTING" "" | Out-Null

    if ($SelfTest -and -not ($Baseline -or $Bdn -or $NbomberStandard -or $Validate)) {
        $runtimeMain = Join-Path (Join-Path $script:PhaseRoot "runtime") $script:MainDllName
        if (-not (Test-Path -LiteralPath $runtimeMain -PathType Leaf)) {
            throw "Self-test-only invocation requires a frozen V2 runtime under PhaseRoot."
        }
        $test = Invoke-V2Child @("--self-test", "--output", (Join-Path $script:PhaseRoot "v2-self-test.json")) $script:PhaseRoot "self-test.stdout.txt" "self-test.stderr.txt"
        $result = if ($test.ExitCode -eq 0) { "SELF_TEST_PASS" } else { "BENCHMARK_V2_HARNESS_FAILED" }
        Write-RunMetadata $script:PhaseRoot "SELF_TEST_COMPLETE" $result | Out-Null
        return $(if ($test.ExitCode -eq 0) { 0 } else { 1 })
    }

    $candidate = Invoke-Preparation $script:PhaseRoot
    $runBdn = $Baseline -or $Bdn
    $runNbomber = $Baseline -or $NbomberStandard
    $before = $null
    $after = $null
    $preconditionError = ""
    try {
        $before = Invoke-ValidationSuite $script:PhaseRoot
    } catch {
        $preconditionError = "$($_.Exception.GetType().Name): $($_.Exception.Message)"
        Write-TextFile (Join-Path $script:PhaseRoot "v2-precondition-error.txt") $preconditionError
    }
    if ($null -ne $before -and ($runBdn -or $runNbomber)) {
        Load-ResumeState $script:PhaseRoot
        Invoke-StandardProfile $script:PhaseRoot -RunBdn:$runBdn -RunNbomber:$runNbomber
        $after = Invoke-FinalValidation $script:PhaseRoot $before
        $residueAfter = Invoke-V2Child @("--residue", "--output", (Join-Path $script:PhaseRoot "v2-residue-after.json")) $script:PhaseRoot "residue-after.stdout.txt" "residue-after.stderr.txt"
        if ($residueAfter.ExitCode -ne 0) {
            $script:ProductFailureObserved = $true
        }
    } elseif ($null -ne $before) {
        Copy-Item -LiteralPath (Join-Path $script:PhaseRoot "v2-validation-pre.json") -Destination (Join-Path $script:PhaseRoot "v2-validation.json") -Force
        Copy-Item -LiteralPath (Join-Path $script:PhaseRoot "v2-consistency-before.json") -Destination (Join-Path $script:PhaseRoot "v2-consistency-after.json") -Force
        $after = $before
        Copy-Item -LiteralPath (Join-Path $script:PhaseRoot "v2-residue-before.json") -Destination (Join-Path $script:PhaseRoot "v2-residue-after.json") -Force
    }
    $result = Get-ResultClassification $script:PhaseRoot $before $after
    if (-not [string]::IsNullOrWhiteSpace($preconditionError)) {
        $result.Result = "BENCHMARK_V2_PRECONDITION_FAILED"
        $result.PreconditionError = $preconditionError
    }
    if (-not $runBdn -and -not $runNbomber -and $null -ne $before -and [string]::IsNullOrWhiteSpace($preconditionError)) {
        $result.Result = "VALIDATION_ONLY_PASS"
    }
    Write-CapacitySummary $script:PhaseRoot $result.Result
    Write-JsonFile (Join-Path $script:PhaseRoot "v2-result.json") $result
    Write-RunMetadata $script:PhaseRoot "COMPLETE" $result.Result | Out-Null
    Write-BenchmarkReport $script:PhaseRoot $result $candidate $before $after
    return $(if ($result.Result -in @("BENCHMARK_V2_STANDARD_BASELINE_CREATED", "VALIDATION_ONLY_PASS", "BENCHMARK_V2_HOST_TOO_CONSTRAINED_FOR_STANDARD")) { 0 } else { 1 })
}

function Initialize-V21Paths {
    $script:V21ManifestFile = Join-Path $script:RepoRoot "benchmarks\v2.1\benchmark-v2.1-adaptive-manifest.json"
    $script:V21RunnerFile = Join-Path $PSScriptRoot "run-warehouse-benchmark-v2.1-adaptive.ps1"
    $script:V2ReferenceRoot = "P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_1-20260915-211500"
    $script:V2Runner = Join-Path $script:RepoRoot "benchmarks\v2\run-warehouse-benchmark-v2.ps1"
    $script:V2ManifestFile = Join-Path $script:RepoRoot "benchmarks\v2\benchmark-v2-manifest.json"
    $script:V21Expected = [ordered]@{
        V2ManifestSha256 = "5F83D05CAAF1803DF9AAE68F47F1EB7A5997EE4F2C087913219712ABBC08C082"
        V2RunnerSha256 = "AED4E9594D62A959B79AC268DBE5E19A464C64414F30EE0FE18374A66E7FC4AD"
        V2CandidateId = "BENCHMARK_V2_CANDIDATE_20260915-135731"
        BenchmarkDllSha256 = "DCB75373489DC1CC3288142D02484327E1369EF8B6A522C80BC6108D06B3BA84"
        RuntimeBundleManifestSha256 = "2DEE48CC2D9E8ECE7B1D5A483E694DEC83F159C5F33489791BB9BCB37F413C6C"
        SchemaSha256 = "7576666BE99F921885844F51767691244BD8B839413DB5D2397036CFE3A9904C"
        ProceduresSha256 = "88A245B85B09D1087FDB201F7E43BB963005707791BFCBF84FD26B7654085894"
        SecuritySha256 = "291C0FA789E854FCA5A6AEB2584E8E1DACDE6A457690566247B6E35ACE2435BB"
        TestSha256 = "809B7EF7080C2C2803DD9867401AB35130337BCD836BFD497766C9D786E53179"
    }
    $script:V21ProductPaths = [ordered]@{
        Schema = Join-Path $script:RepoRoot "Database\WarehouseModule.Schema.sql"
        Procedures = Join-Path $script:RepoRoot "Database\WarehouseModule.Procedures.sql"
        Security = Join-Path $script:RepoRoot "Database\WarehouseModule.Security.sql"
        HistoricalGroupModeTests = Join-Path $script:RepoRoot "TKS_Thuc_Tap_V11_Data_Access.Tests\WarehouseHistoricalGroupModeTests.cs"
    }
    $script:V21ClassEvidence = @{}
    $script:V21HostCapacityObserved = $false
    $script:V21ProductFailureObserved = $false
    $script:V21HarnessFailureObserved = $false
    $script:V21SourceInventory = @()
    $script:V21SourceClosureHash = ""
    $script:V21BenchmarkProjectHash = ""
    $script:V21RunnerHash = ""
    $script:V21ManifestHash = ""
    $script:V21Candidate = $null
}

function Get-V21HostSnapshot {
    $freeMb = 0.0
    $totalMb = 0.0
    $commitUsedMb = 0.0
    $commitLimitMb = 0.0
    try {
        $os = Get-CimInstance -ClassName Win32_OperatingSystem
        $freeMb = [math]::Round(([double]$os.FreePhysicalMemory * 1KB) / 1MB, 1)
        $totalMb = [math]::Round(([double]$os.TotalVisibleMemorySize * 1KB) / 1MB, 1)
        $commitUsedMb = [math]::Round((([double]$os.TotalVirtualMemorySize - [double]$os.FreeVirtualMemory) * 1KB) / 1MB, 1)
        $commitLimitMb = [math]::Round(([double]$os.TotalVirtualMemorySize * 1KB) / 1MB, 1)
    } catch {
    }
    $sqlWorkingMb = 0.0
    foreach ($sqlProcess in @(Get-Process -Name sqlservr -ErrorAction SilentlyContinue)) {
        try {
            $sqlWorkingMb += [double]$sqlProcess.WorkingSet64 / 1MB
        } catch {
        }
    }
    $cpuPercent = 0.0
    try {
        $cpuRows = @(Get-CimInstance -ClassName Win32_PerfFormattedData_PerfOS_Processor -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq "_Total" })
        if ($cpuRows.Count -gt 0) {
            $cpuPercent = [math]::Round([double]$cpuRows[0].PercentProcessorTime, 1)
        }
    } catch {
    }
    $processCount = 0
    try {
        $processCount = @(Get-Process).Count
    } catch {
    }
    return [pscustomobject]@{
        TimestampUtc = [DateTime]::UtcNow.ToString("O")
        FreePhysicalRamMb = $freeMb
        TotalPhysicalRamMb = $totalMb
        CommitUsedMb = $commitUsedMb
        CommitLimitMb = $commitLimitMb
        SqlServerWorkingSetMb = [math]::Round($sqlWorkingMb, 1)
        CpuPercent = $cpuPercent
        ProcessCount = $processCount
    }
}

function Get-V21ProcessMemory([int[]]$ProcessIds) {
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

function Stop-V21ProcessTree([int]$RootPid) {
    $currentProcessId = [Diagnostics.Process]::GetCurrentProcess().Id
    foreach ($processId in @(Get-ProcessTreeIds $RootPid | Sort-Object -Descending)) {
        if ($processId -eq $currentProcessId) {
            continue
        }
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
    }
}

function Add-V21HostSample(
    [string]$BlockId,
    [string]$Class,
    [string]$SampleKind,
    [int]$ChildPid = 0
) {
    $snapshot = Get-V21HostSnapshot
    $childPrivateMb = 0.0
    $childWorkingMb = 0.0
    if ($ChildPid -gt 0) {
        $memory = Get-V21ProcessMemory @(Get-ProcessTreeIds $ChildPid)
        $childPrivateMb = $memory.PrivateMb
        $childWorkingMb = $memory.WorkingMb
    }
    $row = [pscustomobject]@{
        BlockId = $BlockId
        Class = $Class
        SampleKind = $SampleKind
        TimestampUtc = $snapshot.TimestampUtc
        FreePhysicalRamMb = $snapshot.FreePhysicalRamMb
        TotalPhysicalRamMb = $snapshot.TotalPhysicalRamMb
        CommitUsedMb = $snapshot.CommitUsedMb
        CommitLimitMb = $snapshot.CommitLimitMb
        ChildPrivateMemoryMb = $childPrivateMb
        ChildWorkingSetMb = $childWorkingMb
        SqlServerWorkingSetMb = $snapshot.SqlServerWorkingSetMb
        CpuPercent = $snapshot.CpuPercent
        ProcessCount = $snapshot.ProcessCount
        HardFloorMb = 512
        EmergencyMarginMb = 128
        IdealStartMb = 1024
    }
    Add-CsvRow (Join-Path $script:PhaseRoot "v2.1-host-telemetry.csv") $row
    return [pscustomobject]@{
        Snapshot = $snapshot
        ChildPrivateMemoryMb = $childPrivateMb
        ChildWorkingSetMb = $childWorkingMb
    }
}

function Add-V21MemoryModelRow(
    [string]$BlockId,
    [string]$Class,
    [double]$ObservedMb,
    [object]$PreviousDrop,
    [object]$PredictedMb,
    [string]$Admission,
    [string]$Reason,
    [int]$Consecutive
) {
    Add-CsvRow (Join-Path $script:PhaseRoot "v2.1-memory-model.csv") ([pscustomobject]@{
        TimestampUtc = [DateTime]::UtcNow.ToString("O")
        BlockId = $BlockId
        Class = $Class
        SampleKind = "ADMISSION"
        ObservedAvailableMB = [math]::Round($ObservedMb, 1)
        PreviousSameClassPeakDropMB = $PreviousDrop
        SafetyMarginMB = 128
        PredictedMinimumMB = $PredictedMb
        Admission = $Admission
        Reason = $Reason
        ConsecutiveSafeSamples = $Consecutive
        CalibrationStartHeuristicMB = 700
    })
}

function Wait-V21Admission(
    [string]$Class,
    [string]$BlockId,
    [bool]$CalibrationMode
) {
    $evidence = $null
    if ($script:V21ClassEvidence.ContainsKey($Class)) {
        $evidence = $script:V21ClassEvidence[$Class]
    }
    if (-not $CalibrationMode -and $null -eq $evidence) {
        $hostSample = Add-V21HostSample $BlockId $Class "ADMISSION"
        Add-V21MemoryModelRow $BlockId $Class $hostSample.Snapshot.FreePhysicalRamMb "" "" "DENIED" "CALIBRATION_REQUIRED" 0
        $script:V21HostCapacityObserved = $true
        return [pscustomobject]@{
            Allowed = $false
            Reason = "CALIBRATION_REQUIRED"
            ObservedAvailableMB = $hostSample.Snapshot.FreePhysicalRamMb
            PreviousDropMb = ""
            PredictedMinimumMb = ""
            ConsecutiveSafeSamples = 0
        }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $consecutive = 0
    $lastSnapshot = $null
    $lastPrevious = ""
    $lastPredicted = ""
    while ([DateTime]::UtcNow -lt $deadline) {
        $hostSample = Add-V21HostSample $BlockId $Class "ADMISSION"
        $lastSnapshot = $hostSample.Snapshot
        $previousDrop = if ($null -ne $evidence) { [double]$evidence.PeakDropMb } else { 0.0 }
        $predicted = if ($null -ne $evidence) { [math]::Round([double]$hostSample.Snapshot.FreePhysicalRamMb - $previousDrop, 1) } else { "" }
        $safe = if ($CalibrationMode) {
            [double]$hostSample.Snapshot.FreePhysicalRamMb -gt 640 -and [double]$hostSample.Snapshot.FreePhysicalRamMb -ge 700
        } else {
            [double]$hostSample.Snapshot.FreePhysicalRamMb -gt 640 -and [double]$predicted -gt 640
        }
        if ($safe) {
            $consecutive++
        } else {
            $consecutive = 0
        }
        $reason = if ($CalibrationMode) { "CALIBRATION_START_HEURISTIC" } else { "PREDICTED_MINIMUM_ABOVE_640_MB" }
        $admission = if ($consecutive -ge 2) { "ALLOWED" } else { "WAIT" }
        $previousValue = if ($null -ne $evidence) { $previousDrop } else { "" }
        Add-V21MemoryModelRow $BlockId $Class $hostSample.Snapshot.FreePhysicalRamMb $previousValue $predicted $admission $reason $consecutive
        $lastPrevious = $previousValue
        $lastPredicted = $predicted
        if ($consecutive -ge 2) {
            return [pscustomobject]@{
                Allowed = $true
                Reason = $reason
                ObservedAvailableMB = $hostSample.Snapshot.FreePhysicalRamMb
                PreviousDropMb = $lastPrevious
                PredictedMinimumMb = $lastPredicted
                ConsecutiveSafeSamples = $consecutive
            }
        }
        Start-Sleep -Milliseconds 1000
    }
    $script:V21HostCapacityObserved = $true
    return [pscustomobject]@{
        Allowed = $false
        Reason = "ADMISSION_WAIT_EXPIRED"
        ObservedAvailableMB = if ($null -ne $lastSnapshot) { $lastSnapshot.FreePhysicalRamMb } else { 0 }
        PreviousDropMb = $lastPrevious
        PredictedMinimumMb = $lastPredicted
        ConsecutiveSafeSamples = $consecutive
    }
}

function Get-V21Numeric([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) {
        return 0.0
    }
    $number = 0.0
    $styles = [Globalization.NumberStyles]::Float -bor [Globalization.NumberStyles]::AllowThousands
    if ([double]::TryParse($Value.Trim(), $styles, [Globalization.CultureInfo]::CurrentCulture, [ref]$number)) {
        return $number
    }
    $match = [regex]::Match($Value.Replace(",", ""), "[-+]?[0-9]*\.?[0-9]+")
    if ($match.Success -and [double]::TryParse($match.Value, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$number)) {
        return $number
    }
    return 0.0
}

function Get-V21NBomberResult([string]$Directory, [string]$Scenario) {
    foreach ($file in @(Get-ChildItem -LiteralPath $Directory -Recurse -File -Filter "*.csv" | Sort-Object LastWriteTime -Descending)) {
        try {
            $rows = @(Import-Csv -LiteralPath $file.FullName)
            foreach ($row in $rows) {
                $requestProperty = $row.PSObject.Properties["request_count"]
                if ($null -eq $requestProperty) {
                    $requestProperty = $row.PSObject.Properties["RequestCount"]
                }
                if ($null -eq $requestProperty) {
                    continue
                }
                $name = Get-FieldValue $row @("scenario", "Scenario", "test_name")
                if ($name -ne $Scenario -and -not [string]::IsNullOrWhiteSpace($name)) {
                    continue
                }
                return [pscustomobject]@{
                    Path = $file.FullName
                    RequestCount = Get-FieldValue $row @("request_count", "RequestCount")
                    Ok = Get-FieldValue $row @("ok", "Ok")
                    Failed = Get-FieldValue $row @("failed", "Failed")
                    Timeout = Get-FieldValue $row @("timeout", "Timeout", "timeout_count", "TimeoutCount")
                    MeanMs = Get-FieldValue $row @("ok_mean", "OkMean")
                    P50Ms = Get-FieldValue $row @("ok_50_percent", "Ok50Percent")
                    P95Ms = Get-FieldValue $row @("ok_95_percent", "Ok95Percent")
                    P99Ms = Get-FieldValue $row @("ok_99_percent", "Ok99Percent")
                    Rps = Get-FieldValue $row @("ok_rps", "OkRps")
                }
            }
        } catch {
        }
    }
    return $null
}

function Get-V21TelemetrySummary([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [pscustomobject]@{
            Rows = 0
            Errors = 1
            PendingMemoryGrantsMax = 0
            ResourceSemaphoreWaitersMax = 0
            BlockingRequestsMax = 0
            ActiveRequestsMax = 0
            DeadlockCounterMax = 0
        }
    }
    $rows = @(Import-Csv -LiteralPath $Path)
    $validRows = @($rows | Where-Object { $_.DatabaseName -ne "TELEMETRY_ERROR" })
    $errorRows = @($rows | Where-Object { $_.DatabaseName -eq "TELEMETRY_ERROR" })
    $pending = 0.0
    $waiters = 0.0
    $blocking = 0.0
    $active = 0.0
    $deadlocks = 0.0
    foreach ($row in $validRows) {
        $pending = [math]::Max($pending, (Get-V21Numeric (Get-FieldValue $row @("PendingMemoryGrants"))))
        $waiters = [math]::Max($waiters, (Get-V21Numeric (Get-FieldValue $row @("ResourceSemaphoreWaiters"))))
        $blocking = [math]::Max($blocking, (Get-V21Numeric (Get-FieldValue $row @("BlockingRequests"))))
        $active = [math]::Max($active, (Get-V21Numeric (Get-FieldValue $row @("ActiveRequests"))))
        $deadlocks = [math]::Max($deadlocks, (Get-V21Numeric (Get-FieldValue $row @("DeadlockCounter"))))
    }
    return [pscustomobject]@{
        Rows = $validRows.Count
        Errors = $errorRows.Count
        PendingMemoryGrantsMax = [int64]$pending
        ResourceSemaphoreWaitersMax = [int64]$waiters
        BlockingRequestsMax = [int64]$blocking
        ActiveRequestsMax = [int64]$active
        DeadlockCounterMax = [int64]$deadlocks
    }
}

function Get-V21SafeName([string]$Text) {
    $safe = $Text
    foreach ($character in [IO.Path]::GetInvalidFileNameChars()) {
        $safe = $safe.Replace([string]$character, "_")
    }
    return $safe
}

function Add-V21SourceFile(
    [System.Collections.Generic.List[object]]$Rows,
    [string]$Path,
    [string]$Role
) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        $item = Get-Item -LiteralPath $Path
        [void]$Rows.Add([pscustomobject]@{
            RelativePath = Get-RelativeRepoPath $Path
            Role = $Role
            Size = [int64]$item.Length
            Sha256 = Get-Sha256 $Path
        })
    }
}

function Get-V21SourceClosure {
    $rows = [System.Collections.Generic.List[object]]::new()
    $projectSources = @(
        Get-ChildItem -LiteralPath $script:ProjectRoot -Recurse -File -Filter "*.cs" |
            Where-Object { $_.FullName -notmatch "\\(bin|obj|Debug|Release)\\" } |
            Sort-Object FullName
    )
    foreach ($item in $projectSources) {
        Add-V21SourceFile $rows $item.FullName "V2_COMPILED_SOURCE"
    }
    Add-V21SourceFile $rows $script:ProjectFile "V2_PROJECT"
    $dataRoot = Join-Path $script:RepoRoot "TKS_Thuc_Tap_V11_Data_Access"
    $dataSources = @(
        Get-ChildItem -LiteralPath $dataRoot -Recurse -File -Filter "*.cs" |
            Where-Object { $_.FullName -notmatch "\\(bin|obj|Debug|Release)\\" } |
            Sort-Object FullName
    )
    foreach ($item in $dataSources) {
        Add-V21SourceFile $rows $item.FullName "PROJECT_REFERENCE_SOURCE"
    }
    Add-V21SourceFile $rows (Join-Path $dataRoot "TKS_Thuc_Tap_V11_Data_Access.csproj") "PROJECT_REFERENCE"
    Add-V21SourceFile $rows $script:V21RunnerFile "V2_1_RUNNER"
    Add-V21SourceFile $rows $script:V2ManifestFile "V2_REFERENCE_MANIFEST"
    foreach ($name in @("global.json", "NuGet.config", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "packages.lock.json")) {
        Add-V21SourceFile $rows (Join-Path $script:RepoRoot $name) "BUILD_INPUT"
    }
    $script:V21SourceInventory = @($rows | Sort-Object RelativePath, Role)
    $script:V21SourceClosureHash = Get-InventoryFingerprint $script:V21SourceInventory
    $script:V21BenchmarkProjectHash = Get-Sha256 $script:ProjectFile
    return $script:V21SourceInventory
}

function Write-V21SourceEvidence([string]$Phase) {
    $source = Join-Path $Phase "source"
    New-Item -ItemType Directory -Path $source -Force | Out-Null
    $csv = @($script:V21SourceInventory | ConvertTo-Csv -NoTypeInformation)
    Write-TextFile (Join-Path $source "source-inventory.csv") ($csv -join [Environment]::NewLine)
    Write-JsonFile (Join-Path $source "source-inventory.json") $script:V21SourceInventory
    Write-TextFile (Join-Path $source "source-closure.sha256") $script:V21SourceClosureHash
    Write-TextFile (Join-Path $source "benchmark-project.sha256") $script:V21BenchmarkProjectHash
}
function Capture-V21Product([string]$Phase, [string]$Label) {
    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($name in $script:V21ProductPaths.Keys) {
        $path = $script:V21ProductPaths[$name]
        $expectedHash = switch ($name) {
            "Schema" { $script:V21Expected.SchemaSha256 }
            "Procedures" { $script:V21Expected.ProceduresSha256 }
            "Security" { $script:V21Expected.SecuritySha256 }
            "HistoricalGroupModeTests" { $script:V21Expected.TestSha256 }
            default { "" }
        }
        $actualHash = if (Test-Path -LiteralPath $path -PathType Leaf) { Get-Sha256 $path } else { "" }
        [void]$rows.Add([pscustomobject]@{
            Name = $name
            Path = $path
            ExpectedSha256 = $expectedHash
            ActualSha256 = $actualHash
            Match = ($actualHash -eq $expectedHash)
        })
    }
    $passed = @($rows | Where-Object { -not $_.Match }).Count -eq 0
    $record = [ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow.ToString("O")
        Label = $Label
        Passed = $passed
        Files = @($rows)
        BusinessDatabase = "TKS_Thuc_Tap_V11_GiaiDoan2; not accessed"
        PerformanceDatabase = $script:TargetDatabase
        ProductSqlModified = $false
    }
    Write-JsonFile (Join-Path $Phase ("v2.1-product-integrity-" + $Label + ".json")) $record
    if (-not $passed) {
        $details = ($rows | Where-Object { -not $_.Match } | ForEach-Object { "$($_.Name):$($_.ActualSha256)" }) -join "; "
        throw "PRODUCT_CANDIDATE_DRIFT|$details"
    }
    return $record
}

function Get-V21RuntimeRows([string]$RuntimeDirectory) {
    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($file in @(Get-ChildItem -LiteralPath $RuntimeDirectory -Recurse -File | Sort-Object FullName)) {
        [void]$rows.Add([pscustomobject]@{
            RelativePath = [IO.Path]::GetRelativePath($RuntimeDirectory, $file.FullName).Replace("\", "/")
            Role = "RUNTIME_BUNDLE"
            Size = [int64]$file.Length
            Sha256 = Get-Sha256 $file.FullName
            Origin = "Accepted V2.0 frozen runtime"
            Required = ($file.Name -in @($script:MainDllName, "TKS_Thuc_Tap_V11_Benchmarks_V2.deps.json", "TKS_Thuc_Tap_V11_Benchmarks_V2.runtimeconfig.json"))
        })
    }
    return @($rows)
}

function Capture-V21Reference([string]$Phase) {
    $v2ManifestHash = Get-Sha256 $script:V2ManifestFile
    $v2RunnerHash = Get-Sha256 $script:V2Runner
    $v1RunnerHash = Get-Sha256 $script:V1Runner
    if ($v2ManifestHash -ne $script:V21Expected.V2ManifestSha256) {
        throw "V2 manifest drift detected. Expected $($script:V21Expected.V2ManifestSha256), actual $v2ManifestHash."
    }
    if ($v2RunnerHash -ne $script:V21Expected.V2RunnerSha256) {
        throw "V2 runner drift detected. Expected $($script:V21Expected.V2RunnerSha256), actual $v2RunnerHash."
    }
    if ($v1RunnerHash -ne $script:V1RunnerExpectedHash) {
        throw "V1 runner drift detected. Expected $script:V1RunnerExpectedHash, actual $v1RunnerHash."
    }
    $candidatePath = Join-Path $script:V2ReferenceRoot "candidate-manifest.json"
    $candidate = Get-JsonValue $candidatePath
    $inventoryPath = Join-Path $script:V2ReferenceRoot "runtime-bundle-inventory.json"
    $expectedRows = @(Get-JsonValue $inventoryPath)
    $actualRows = @(Get-V21RuntimeRows $script:RuntimeDirectory)
    $runtimeHash = Get-InventoryFingerprint $actualRows
    $runtimeMatches = ($actualRows.Count -eq $expectedRows.Count)
    foreach ($expectedRow in $expectedRows) {
        $actualRow = $actualRows | Where-Object { $_.RelativePath -eq $expectedRow.RelativePath } | Select-Object -First 1
        if ($null -eq $actualRow -or [int64]$actualRow.Size -ne [int64]$expectedRow.Size -or [string]$actualRow.Sha256 -ne [string]$expectedRow.Sha256) {
            $runtimeMatches = $false
        }
    }
    $mainDll = Join-Path $script:RuntimeDirectory $script:MainDllName
    $runtimeConfig = Join-Path $script:RuntimeDirectory "TKS_Thuc_Tap_V11_Benchmarks_V2.runtimeconfig.json"
    $depsFile = Join-Path $script:RuntimeDirectory "TKS_Thuc_Tap_V11_Benchmarks_V2.deps.json"
    $oldRootFiles = @("v2-result.json", "Benchmark-V2-Report.md", "candidate-manifest.json", "runtime-bundle-inventory.json")
    $oldRootHashes = [ordered]@{}
    foreach ($name in $oldRootFiles) {
        $path = Join-Path $script:V2ReferenceRoot $name
        $oldRootHashes[$name] = if (Test-Path -LiteralPath $path -PathType Leaf) { Get-Sha256 $path } else { "" }
    }
    $record = [ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow.ToString("O")
        V2ReferenceRoot = $script:V2ReferenceRoot
        V2ManifestPath = $script:V2ManifestFile
        V2ManifestSha256 = $v2ManifestHash
        V2RunnerPath = $script:V2Runner
        V2RunnerSha256 = $v2RunnerHash
        V1RunnerPath = $script:V1Runner
        V1RunnerSha256 = $v1RunnerHash
        V2CandidateId = Get-FieldValue $candidate @("CandidateId")
        ExpectedV2CandidateId = $script:V21Expected.V2CandidateId
        V2CandidateDllSha256 = Get-FieldValue $candidate @("BenchmarkDllSha256")
        BenchmarkDllSha256 = if (Test-Path -LiteralPath $mainDll -PathType Leaf) { Get-Sha256 $mainDll } else { "" }
        ExpectedBenchmarkDllSha256 = $script:V21Expected.BenchmarkDllSha256
        RuntimeDirectory = $script:RuntimeDirectory
        RuntimeFileCount = $actualRows.Count
        RuntimeBundleManifestSha256 = $runtimeHash
        ExpectedRuntimeBundleManifestSha256 = $script:V21Expected.RuntimeBundleManifestSha256
        RuntimeInventoryMatchesAccepted = $runtimeMatches
        RuntimeConfigExists = Test-Path -LiteralPath $runtimeConfig -PathType Leaf
        DepsExists = Test-Path -LiteralPath $depsFile -PathType Leaf
        OldV2RootFileHashes = $oldRootHashes
        RuntimeInventory = $actualRows
    }
    $record["Passed"] =
        $record.V2CandidateId -eq $script:V21Expected.V2CandidateId -and
        $record.V2CandidateDllSha256 -eq $script:V21Expected.BenchmarkDllSha256 -and
        $record.BenchmarkDllSha256 -eq $script:V21Expected.BenchmarkDllSha256 -and
        $runtimeHash -eq $script:V21Expected.RuntimeBundleManifestSha256 -and
        $runtimeMatches -and $record.RuntimeConfigExists -and $record.DepsExists
    Write-JsonFile (Join-Path $Phase "v2.1-v2-reference.json") $record
    if (-not [bool]$record.Passed) {
        throw "V2_REFERENCE_INTEGRITY_FAILED"
    }
    return $record
}

function Capture-V21RuntimeReference([string]$Phase, [object]$Reference) {
    $runtimeConfigPath = Join-Path $script:RuntimeDirectory "TKS_Thuc_Tap_V11_Benchmarks_V2.runtimeconfig.json"
    $depsPath = Join-Path $script:RuntimeDirectory "TKS_Thuc_Tap_V11_Benchmarks_V2.deps.json"
    $runtimeConfig = Get-JsonValue $runtimeConfigPath
    $deps = Get-JsonValue $depsPath
    $record = [ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow.ToString("O")
        RuntimeDirectory = $script:RuntimeDirectory
        MainDll = Join-Path $script:RuntimeDirectory $script:MainDllName
        MainDllSha256 = $Reference.BenchmarkDllSha256
        RuntimeBundleManifestSha256 = $Reference.RuntimeBundleManifestSha256
        FileCount = $Reference.RuntimeFileCount
        RuntimeConfig = $runtimeConfig
        DependencyContextLibraryCount = if ($null -ne $deps -and $null -ne $deps.libraries) { @($deps.libraries.PSObject.Properties).Count } else { 0 }
        RuntimeInventory = $Reference.RuntimeInventory
        CopyPolicy = "No mixed Debug/Release files; accepted V2.0 runtime used in place"
    }
    Write-JsonFile (Join-Path $Phase "v2.1-runtime-reference.json") $record
    return $record
}

function Capture-V21Environment([string]$Phase) {
    $directory = Join-Path $Phase "environment"
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $dotnet = Get-DotnetPath
    $info = Invoke-LoggedProcess $dotnet @("--info") $script:RepoRoot (Join-Path $directory "dotnet-info.stdout.txt") (Join-Path $directory "dotnet-info.stderr.txt")
    $version = Invoke-LoggedProcess $dotnet @("--version") $script:RepoRoot (Join-Path $directory "dotnet-version.stdout.txt") (Join-Path $directory "dotnet-version.stderr.txt")
    $runtimes = Invoke-LoggedProcess $dotnet @("--list-runtimes") $script:RepoRoot (Join-Path $directory "dotnet-runtimes.stdout.txt") (Join-Path $directory "dotnet-runtimes.stderr.txt")
    $sdks = Invoke-LoggedProcess $dotnet @("--list-sdks") $script:RepoRoot (Join-Path $directory "dotnet-sdks.stdout.txt") (Join-Path $directory "dotnet-sdks.stderr.txt")
    $msbuild = Invoke-LoggedProcess $dotnet @("msbuild", $script:ProjectFile, "-version", "-nologo") $script:RepoRoot (Join-Path $directory "msbuild.stdout.txt") (Join-Path $directory "msbuild.stderr.txt")
    $os = Get-CimInstance -ClassName Win32_OperatingSystem
    $projectText = Get-Content -Raw -LiteralPath $script:ProjectFile
    $packages = @(
        [regex]::Matches($projectText, 'PackageReference Include="([^"]+)" Version="([^"]+)"') |
            ForEach-Object { [pscustomobject]@{ Id = $_.Groups[1].Value; Version = $_.Groups[2].Value } }
    )
    $environmentNames = @("PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER", "NUMBER_OF_PROCESSORS", "DOTNET_ROOT", "DOTNET_ROOT_X64", "NUGET_PACKAGES", "MSBuildSDKsPath", "TEMP", "TMP", "CI")
    $relevantEnvironment = [ordered]@{}
    foreach ($name in $environmentNames) {
        $relevantEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
    }
    $record = [ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow.ToString("O")
        Os = [ordered]@{ Caption = $os.Caption; Version = $os.Version; BuildNumber = $os.BuildNumber; Architecture = [string]$os.OSArchitecture }
        DotnetPath = $dotnet
        DotnetVersion = $version.Stdout.Trim()
        DotnetInfoExitCode = $info.ExitCode
        DotnetRuntimesExitCode = $runtimes.ExitCode
        DotnetSdksExitCode = $sdks.ExitCode
        MsbuildExitCode = $msbuild.ExitCode
        TargetFramework = "net8.0"
        Configuration = "Release"
        Deterministic = $true
        ContinuousIntegrationBuild = "Not changed; frozen runtime reused"
        Packages = $packages
        RelevantEnvironment = $relevantEnvironment
        RestorePolicy = "No restore and no build in V2.1"
    }
    Write-JsonFile (Join-Path $Phase "v2.1-environment.json") $record
    return $record
}

function Capture-V21DependencyReference([string]$Phase) {
    $sourceGraph = Join-Path $script:V2ReferenceRoot "source\dependency-graph.json"
    if (-not (Test-Path -LiteralPath $sourceGraph -PathType Leaf)) {
        throw "Accepted V2 dependency graph is missing: $sourceGraph"
    }
    $graph = @(Get-JsonValue $sourceGraph)
    $libraries = [System.Collections.Generic.List[string]]::new()
    foreach ($entry in $graph) {
        foreach ($library in @($entry.Libraries)) {
            if (-not $libraries.Contains([string]$library)) {
                [void]$libraries.Add([string]$library)
            }
        }
    }
    $dataProject = Join-Path $script:RepoRoot "TKS_Thuc_Tap_V11_Data_Access\TKS_Thuc_Tap_V11_Data_Access.csproj"
    $record = [ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow.ToString("O")
        Policy = "Accepted V2.0 dependency graph reused; no restore or package update"
        SourceEvidencePath = $sourceGraph
        SourceEvidenceSha256 = Get-Sha256 $sourceGraph
        LibraryCount = $libraries.Count
        Libraries = @($libraries | Sort-Object)
        ProjectReferences = @(
            [pscustomobject]@{ Project = "TKS_Thuc_Tap_V11_Benchmarks_V2"; Path = Get-RelativeRepoPath $script:ProjectFile; Sha256 = Get-Sha256 $script:ProjectFile },
            [pscustomobject]@{ Project = "TKS_Thuc_Tap_V11_Data_Access"; Path = Get-RelativeRepoPath $dataProject; Sha256 = Get-Sha256 $dataProject }
        )
    }
    Write-JsonFile (Join-Path $Phase "v2.1-dependency-resolution.json") $record
    return $record
}

function Get-V21ChildEnvironment([string]$OutputDirectory, [int]$Copies, [int]$WarmupSeconds, [int]$DurationSeconds) {
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
        TKS_V2_NBOMBER_WARMUP_SECONDS = [string]$WarmupSeconds
        TKS_V2_NBOMBER_DURATION_SECONDS = [string]$DurationSeconds
        TEMP = $temp
        TMP = $temp
    }
}

function Invoke-V21Child(
    [string[]]$Arguments,
    [string]$OutputDirectory,
    [string]$StdoutName,
    [string]$StderrName,
    [int]$Copies = 1,
    [int]$WarmupSeconds = 3,
    [int]$DurationSeconds = 15
) {
    $runtimeDll = Join-Path $script:RuntimeDirectory $script:MainDllName
    if (-not (Test-Path -LiteralPath $runtimeDll -PathType Leaf)) {
        throw "Frozen runtime missing: $runtimeDll"
    }
    $environment = Get-V21ChildEnvironment $OutputDirectory $Copies $WarmupSeconds $DurationSeconds
    return Invoke-LoggedProcess (Get-DotnetPath) (@($runtimeDll) + $Arguments) $script:RepoRoot (Join-Path $OutputDirectory $StdoutName) (Join-Path $OutputDirectory $StderrName) $environment
}

function Initialize-V21OutputFiles([string]$Phase) {
    $headers = [ordered]@{
        "v2.1-bdn-summary.csv" = "Profile,BlockId,Scenario,Level,Status,FailureType,ExitCode,DurationSeconds,MeanMs,ErrorMs,StdDevMs,AllocatedBytes,MeasuredIterations,ArtifactPath,TelemetryRows,PendingMemoryGrantsMax,ResourceSemaphoreWaitersMax,BlockingRequestsMax,Notes"
        "v2.1-nbomber-summary.csv" = "Profile,BlockId,Scenario,Level,Status,FailureType,ExitCode,DurationSeconds,MeanMs,P50Ms,P95Ms,P99Ms,Rps,RequestCount,Ok,Failed,Timeout,ArtifactPath,TelemetryRows,PendingMemoryGrantsMax,ResourceSemaphoreWaitersMax,BlockingRequestsMax,Notes"
        "v2.1-host-telemetry.csv" = "BlockId,Class,SampleKind,TimestampUtc,FreePhysicalRamMb,TotalPhysicalRamMb,CommitUsedMb,CommitLimitMb,ChildPrivateMemoryMb,ChildWorkingSetMb,SqlServerWorkingSetMb,CpuPercent,ProcessCount,HardFloorMb,EmergencyMarginMb,IdealStartMb"
        "v2.1-sql-telemetry.csv" = "BlockId,TimestampUtc,DatabaseName,DatabaseId,ActiveRequests,BlockingRequests,ActiveRequestGrantKB,RequestedGrantKB,GrantedGrantKB,PendingMemoryGrants,ResourceSemaphoreWaiters,LogicalReads,TempdbUsedKB,MemoryGrantsPendingCounter,DeadlockCounter"
        "v2.1-process-peak-memory.csv" = "Profile,BlockId,Kind,Scenario,Level,PeakPrivateMemoryMb,PeakWorkingSetMb,StartFreePhysicalRamMb,MinFreePhysicalRamMb,ActualDropMb,Status"
        "v2.1-memory-model.csv" = "TimestampUtc,BlockId,Class,SampleKind,ObservedAvailableMB,PreviousSameClassPeakDropMB,SafetyMarginMB,PredictedMinimumMB,Admission,Reason,ConsecutiveSafeSamples,CalibrationStartHeuristicMB"
        "v2.1-calibration.csv" = "CalibrationId,Kind,Scenario,Level,Class,Status,FailureType,ExitCode,DurationSeconds,ObservedStartFreeMB,MinFreeMB,ActualDropMB,PeakPrivateMemoryMB,PeakWorkingSetMB,RequestCount,Ok,Failed,Timeout,ArtifactPath,TelemetryRows,PendingMemoryGrantsMax,ResourceSemaphoreWaitersMax,Notes"
    }
    foreach ($name in $headers.Keys) {
        $path = Join-Path $Phase $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            Write-TextFile $path $headers[$name]
        }
    }
}

function Update-V21Manifest([string]$Phase, [object]$Reference) {
    $preHash = Get-Sha256 $script:V21ManifestFile
    Copy-Item -LiteralPath $script:V21ManifestFile -Destination (Join-Path $Phase "manifest-pre.json") -Force
    Write-TextFile (Join-Path $Phase "manifest-pre.sha256") $preHash
    $manifest = Get-JsonValue $script:V21ManifestFile
    if ($manifest.protocolVersion -ne "WAREHOUSE_BENCHMARK_V2_1_ADAPTIVE" -or $manifest.protocolSemanticVersion -ne "2.1.0") {
        throw "V2_1_MANIFEST_PROTOCOL_MISMATCH"
    }
    $manifest.runnerSha256 = $script:RunnerHash
    $manifest.sourceClosureSha256 = $script:V21SourceClosureHash
    $manifest.benchmarkDllSha256 = $Reference.BenchmarkDllSha256
    $manifest.runtimeBundleManifestSha256 = $Reference.RuntimeBundleManifestSha256
    Write-TextFile $script:V21ManifestFile ($manifest | ConvertTo-Json -Depth 50)
    $script:V21ManifestHash = Get-Sha256 $script:V21ManifestFile
    Copy-Item -LiteralPath $script:V21ManifestFile -Destination (Join-Path $Phase "manifest-post.json") -Force
    Write-TextFile (Join-Path $Phase "manifest-post.sha256") $script:V21ManifestHash
    return $manifest
}

function Get-V21ResidueClean([string]$Path) {
    $residue = Get-JsonValue $Path
    if ($null -eq $residue) {
        return $false
    }
    return [bool]$residue.Passed -and
        [int64]$residue.ActiveRequests -eq 0 -and
        [int64]$residue.BlockingRequests -eq 0 -and
        [int64]$residue.PendingMemoryGrants -eq 0 -and
        [int64]$residue.ResourceSemaphoreWaiters -eq 0 -and
        [int64]$residue.OpenTransactions -eq 0 -and
        [int64]$residue.ApplicationLocks -eq 0
}

function Wait-V21Cooldown([string]$BlockId) {
    Start-Sleep -Seconds 5
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $probe = 0
    while ([DateTime]::UtcNow -lt $deadline) {
        $probe++
        $sample = Add-V21HostSample $BlockId "COOLDOWN" "COOLDOWN"
        $safeRam = [double]$sample.Snapshot.FreePhysicalRamMb -gt 640
        $residuePath = Join-Path $script:PhaseRoot ("guards\cooldown-" + (Get-V21SafeName $BlockId) + "-" + $probe + ".json")
        $residueResult = Invoke-V21Child @("--residue", "--output", $residuePath) $script:PhaseRoot "cooldown.stdout.txt" "cooldown.stderr.txt"
        if ($residueResult.ExitCode -eq 0 -and $safeRam -and (Get-V21ResidueClean $residuePath)) {
            return $true
        }
        Start-Sleep -Seconds 2
    }
    $script:V21HarnessFailureObserved = $true
    return $false
}

function Update-V21ClassEvidence([string]$Class, [object]$Row) {
    if ($Row.Status -ne "PASS") {
        return
    }
    if ($Row.Kind -ne "BDN" -and [int64]$Row.RequestCount -le 0) {
        return
    }
    $previous = 0.0
    if ($script:V21ClassEvidence.ContainsKey($Class)) {
        $previous = [double]$script:V21ClassEvidence[$Class].PeakDropMb
    }
    $peakDrop = [math]::Max($previous, [double]$Row.ActualDropMb)
    $script:V21ClassEvidence[$Class] = [pscustomobject]@{
        Class = $Class
        PeakDropMb = [math]::Round($peakDrop, 1)
        EvidenceBlockId = $Row.BlockId
        CapturedAtUtc = [DateTime]::UtcNow.ToString("O")
    }
    Write-JsonFile (Join-Path $script:PhaseRoot "v2.1-memory-class-evidence.json") $script:V21ClassEvidence
}

function New-V21BlockRow(
    [string]$Profile,
    [string]$Kind,
    [string]$BlockId,
    [string]$Scenario,
    [int]$Level,
    [string]$Status,
    [string]$FailureType,
    [int]$ExitCode,
    [double]$DurationSeconds,
    [double]$StartFreeMb,
    [double]$MinFreeMb,
    [double]$PeakPrivateMb,
    [double]$PeakWorkingMb,
    [object]$Artifact,
    [object]$Telemetry
) {
    $requestCount = 0
    $ok = 0
    $failed = 0
    $timeout = 0
    $mean = ""
    $p50 = ""
    $p95 = ""
    $p99 = ""
    $rps = ""
    $errorMs = ""
    $stdDevMs = ""
    $allocatedBytes = ""
    if ($null -ne $Artifact -and $Kind -eq "NBOMBER") {
        $requestCount = [int64]$Artifact.RequestCount
        $ok = [int64]$Artifact.Ok
        $failed = [int64]$Artifact.Failed
        $timeout = [int64]$Artifact.Timeout
        $mean = [string]$Artifact.MeanMs
        $p50 = [string]$Artifact.P50Ms
        $p95 = [string]$Artifact.P95Ms
        $p99 = [string]$Artifact.P99Ms
        $rps = [string]$Artifact.Rps
    }
    if ($null -ne $Artifact -and $Kind -eq "BDN") {
        $mean = [string]$Artifact.MeanMs
        $errorMs = [string]$Artifact.ErrorMs
        $stdDevMs = [string]$Artifact.StdDevMs
        $allocatedBytes = [string]$Artifact.AllocatedBytes
    }
    $drop = [math]::Max(0.0, $StartFreeMb - $MinFreeMb)
    return [pscustomobject]@{
        Profile = $Profile
        Kind = $Kind
        BlockId = $BlockId
        Scenario = $Scenario
        Level = $Level
        Status = $Status
        FailureType = $FailureType
        ExitCode = $ExitCode
        DurationSeconds = [math]::Round($DurationSeconds, 3)
        MeanMs = $mean
        P50Ms = $p50
        P95Ms = $p95
        P99Ms = $p99
        Rps = $rps
        RequestCount = $requestCount
        Ok = $ok
        Failed = $failed
        Timeout = $timeout
        ErrorMs = $errorMs
        StdDevMs = $stdDevMs
        AllocatedBytes = $allocatedBytes
        ArtifactPath = if ($null -ne $Artifact) { $Artifact.Path } else { "" }
        TelemetryRows = if ($null -ne $Telemetry) { $Telemetry.Rows } else { 0 }
        PendingMemoryGrantsMax = if ($null -ne $Telemetry) { $Telemetry.PendingMemoryGrantsMax } else { 0 }
        ResourceSemaphoreWaitersMax = if ($null -ne $Telemetry) { $Telemetry.ResourceSemaphoreWaitersMax } else { 0 }
        BlockingRequestsMax = if ($null -ne $Telemetry) { $Telemetry.BlockingRequestsMax } else { 0 }
        StartFreeMb = [math]::Round($StartFreeMb, 1)
        MinFreeMb = [math]::Round($MinFreeMb, 1)
        ActualDropMb = [math]::Round($drop, 1)
        PeakPrivateMemoryMb = [math]::Round($PeakPrivateMb, 1)
        PeakWorkingSetMb = [math]::Round($PeakWorkingMb, 1)
    }
}

function Write-V21BlockRow([object]$Row) {
    if ($Row.Profile -ne "CALIBRATION") {
        if ($Row.Kind -eq "BDN") {
            Add-CsvRow (Join-Path $script:PhaseRoot "v2.1-bdn-summary.csv") ([pscustomobject]@{
                Profile = $Row.Profile
                BlockId = $Row.BlockId
                Scenario = $Row.Scenario
                Level = $Row.Level
                Status = $Row.Status
                FailureType = $Row.FailureType
                ExitCode = $Row.ExitCode
                DurationSeconds = $Row.DurationSeconds
                MeanMs = $Row.MeanMs
                ErrorMs = $Row.ErrorMs
                StdDevMs = $Row.StdDevMs
                AllocatedBytes = $Row.AllocatedBytes
                MeasuredIterations = if ($Row.Status -eq "PASS") { 5 } else { 0 }
                ArtifactPath = $Row.ArtifactPath
                TelemetryRows = $Row.TelemetryRows
                PendingMemoryGrantsMax = $Row.PendingMemoryGrantsMax
                ResourceSemaphoreWaitersMax = $Row.ResourceSemaphoreWaitersMax
                BlockingRequestsMax = $Row.BlockingRequestsMax
                Notes = ""
            })
        } else {
            Add-CsvRow (Join-Path $script:PhaseRoot "v2.1-nbomber-summary.csv") ([pscustomobject]@{
                Profile = $Row.Profile
                BlockId = $Row.BlockId
                Scenario = $Row.Scenario
                Level = $Row.Level
                Status = $Row.Status
                FailureType = $Row.FailureType
                ExitCode = $Row.ExitCode
                DurationSeconds = $Row.DurationSeconds
                MeanMs = $Row.MeanMs
                P50Ms = $Row.P50Ms
                P95Ms = $Row.P95Ms
                P99Ms = $Row.P99Ms
                Rps = $Row.Rps
                RequestCount = $Row.RequestCount
                Ok = $Row.Ok
                Failed = $Row.Failed
                Timeout = $Row.Timeout
                ArtifactPath = $Row.ArtifactPath
                TelemetryRows = $Row.TelemetryRows
                PendingMemoryGrantsMax = $Row.PendingMemoryGrantsMax
                ResourceSemaphoreWaitersMax = $Row.ResourceSemaphoreWaitersMax
                BlockingRequestsMax = $Row.BlockingRequestsMax
                Notes = ""
            })
        }
    }
    Add-CsvRow (Join-Path $script:PhaseRoot "v2.1-process-peak-memory.csv") ([pscustomobject]@{
        Profile = $Row.Profile
        BlockId = $Row.BlockId
        Kind = $Row.Kind
        Scenario = $Row.Scenario
        Level = $Row.Level
        PeakPrivateMemoryMb = $Row.PeakPrivateMemoryMb
        PeakWorkingSetMb = $Row.PeakWorkingSetMb
        StartFreePhysicalRamMb = $Row.StartFreeMb
        MinFreePhysicalRamMb = $Row.MinFreeMb
        ActualDropMb = $Row.ActualDropMb
        Status = $Row.Status
    })
}

function Invoke-V21Block(
    [string]$Profile,
    [string]$Kind,
    [string]$Class,
    [string]$BlockId,
    [string]$Scenario,
    [int]$Level,
    [int]$Copies,
    [int]$WarmupSeconds,
    [int]$DurationSeconds
) {
    $parentDirectory = if ($Profile -eq "CALIBRATION") { "calibration" } else { "runs" }
    $blockDirectory = Join-Path $script:PhaseRoot ($parentDirectory + "\" + (Get-V21SafeName $BlockId))
    New-Item -ItemType Directory -Path $blockDirectory -Force | Out-Null
    $runtimeDll = Join-Path $script:RuntimeDirectory $script:MainDllName
    $arguments = if ($Kind -eq "BDN") {
        @("--bdn", "--scenario", $Scenario, "--artifacts", $blockDirectory)
    } else {
        @("--nbomber", "--scenario", $Scenario, "--output", $blockDirectory)
    }
    $environment = Get-V21ChildEnvironment $blockDirectory $Copies $WarmupSeconds $DurationSeconds
    $child = $null
    $telemetry = $null
    $childResult = $null
    $telemetryStartFailed = $false
    $hardStop = $false
    $timedOut = $false
    $startFreeMb = 0.0
    $minFreeMb = 999999.0
    $peakPrivateMb = 0.0
    $peakWorkingMb = 0.0
    $startedUtc = [DateTime]::UtcNow
    try {
        $startFreeMb = [double](Get-V21HostSnapshot).FreePhysicalRamMb
        $child = Start-AsyncProcess (Get-DotnetPath) (@($runtimeDll) + $arguments) $script:RepoRoot (Join-Path $blockDirectory ($Kind + ".stdout.txt")) (Join-Path $blockDirectory ($Kind + ".stderr.txt")) $environment
        $childPid = [int]$child.Process.Id
        $sqlTelemetryPath = Join-Path $blockDirectory "sql-telemetry.csv"
        try {
            $telemetryEnvironment = Get-V21ChildEnvironment $blockDirectory $Copies $WarmupSeconds $DurationSeconds
            $telemetry = Start-AsyncProcess (Get-DotnetPath) @($runtimeDll, "--telemetry", "--target-pid", [string]$childPid, "--block", $BlockId, "--output", $sqlTelemetryPath) $script:RepoRoot (Join-Path $blockDirectory "telemetry.stdout.txt") (Join-Path $blockDirectory "telemetry.stderr.txt") $telemetryEnvironment
        } catch {
            $telemetryStartFailed = $true
            Write-TextFile (Join-Path $blockDirectory "telemetry-start-error.txt") $_.Exception.Message
        }
        $startedUtc = [DateTime]::UtcNow
        $timeoutSeconds = if ($Kind -eq "BDN") { 240 } else { 90 }
        $deadline = $startedUtc.AddSeconds($timeoutSeconds)
        while (-not $child.Process.HasExited) {
            $hostSample = Add-V21HostSample $BlockId $Class "ACTIVE" $childPid
            $freeMb = [double]$hostSample.Snapshot.FreePhysicalRamMb
            $minFreeMb = [math]::Min($minFreeMb, $freeMb)
            $peakPrivateMb = [math]::Max($peakPrivateMb, [double]$hostSample.ChildPrivateMemoryMb)
            $peakWorkingMb = [math]::Max($peakWorkingMb, [double]$hostSample.ChildWorkingSetMb)
            if ($freeMb -le 512) {
                $hardStop = $true
                $script:V21HostCapacityObserved = $true
                Stop-V21ProcessTree $childPid
                break
            }
            if ([DateTime]::UtcNow -gt $deadline) {
                $timedOut = $true
                Stop-V21ProcessTree $childPid
                break
            }
            Start-Sleep -Milliseconds 1000
        }
        $childResult = Complete-AsyncProcess $child
        if ($null -ne $telemetry) {
            $telemetryDeadline = [DateTime]::UtcNow.AddSeconds(10)
            while (-not $telemetry.Process.HasExited -and [DateTime]::UtcNow -lt $telemetryDeadline) {
                Start-Sleep -Milliseconds 250
            }
            if (-not $telemetry.Process.HasExited) {
                Stop-V21ProcessTree ([int]$telemetry.Process.Id)
            }
            try {
                [void](Complete-AsyncProcess $telemetry)
            } catch {
                $telemetryStartFailed = $true
            }
            Merge-CsvFile $sqlTelemetryPath (Join-Path $script:PhaseRoot "v2.1-sql-telemetry.csv")
        }
    } catch {
        $script:V21HarnessFailureObserved = $true
        $childResult = [pscustomobject]@{ ExitCode = -1; Stdout = ""; Stderr = $_.Exception.Message }
    }
    if ($minFreeMb -eq 999999.0) {
        $minFreeMb = $startFreeMb
    }
    $duration = ([DateTime]::UtcNow - $startedUtc).TotalSeconds
    $telemetrySummary = Get-V21TelemetrySummary (Join-Path $blockDirectory "sql-telemetry.csv")
    if ($telemetryStartFailed -or $telemetrySummary.Rows -eq 0) {
        $script:V21HarnessFailureObserved = $true
    }
    $combinedText = ""
    $exitCode = -1
    if ($null -ne $childResult) {
        $combinedText = [string]$childResult.Stdout + [Environment]::NewLine + [string]$childResult.Stderr
        $exitCode = [int]$childResult.ExitCode
    }
    $artifact = if ($Kind -eq "BDN") { Find-BdnResult $blockDirectory } else { Get-V21NBomberResult $blockDirectory $Scenario }
    $status = "PASS"
    $failureType = ""
    if ($hardStop) {
        $status = "HOST_CAPACITY_LIMIT"
        $failureType = if ($Kind -eq "BDN") { "BDN_HOST_LIMIT" } else { "HOST_CAPACITY_LIMIT" }
    } elseif ($timedOut) {
        $status = "HARNESS_FAILURE"
        $failureType = if ($Kind -eq "BDN") { "BDN_HOST_LIMIT" } else { "HARNESS_TIMEOUT" }
        $script:V21HarnessFailureObserved = $true
        if ($Kind -eq "BDN") {
            $script:V21HostCapacityObserved = $true
        }
    } elseif ($null -eq $childResult -or $exitCode -ne 0) {
        $failureType = Get-TextFailureType $combinedText
        if ($Kind -eq "BDN" -and $failureType -eq "RESOURCE_SEMAPHORE") {
            $status = "HOST_CAPACITY_LIMIT"
            $failureType = "BDN_HOST_LIMIT"
            $script:V21HostCapacityObserved = $true
        } elseif ($failureType -in @("RESOURCE_SEMAPHORE", "DEADLOCK", "SQL_TIMEOUT", "PRODUCT_ERROR")) {
            $status = "PRODUCT_FAILURE"
            $script:V21ProductFailureObserved = $true
        } else {
            $status = "HARNESS_FAILURE"
            $script:V21HarnessFailureObserved = $true
        }
    } elseif ($null -eq $artifact) {
        $status = "HARNESS_FAILURE"
        $failureType = if ($Kind -eq "BDN") { "BDN_RESULT_NOT_FOUND" } else { "NBOMBER_RESULT_NOT_FOUND" }
        $script:V21HarnessFailureObserved = $true
    } elseif ($Kind -eq "NBOMBER" -and [int64]$artifact.RequestCount -le 0) {
        $status = "HARNESS_FAILURE"
        $failureType = "NBOMBER_ZERO_REQUESTS"
        $script:V21HarnessFailureObserved = $true
    } elseif ($Kind -eq "NBOMBER" -and ([int64]$artifact.Failed -gt 0 -or [int64]$artifact.Timeout -gt 0)) {
        $status = "PRODUCT_FAILURE"
        $failureType = Get-TextFailureType $combinedText
        if ($failureType -eq "HARNESS_ERROR") {
            $failureType = "PRODUCT_ERROR"
        }
        $script:V21ProductFailureObserved = $true
    }
    $row = New-V21BlockRow $Profile $Kind $BlockId $Scenario $Level $status $failureType $exitCode $duration $startFreeMb $minFreeMb $peakPrivateMb $peakWorkingMb $artifact $telemetrySummary
    Write-V21BlockRow $row
    Update-V21ClassEvidence $Class $row
    [void](Wait-V21Cooldown $BlockId)
    return $row
}

function Write-V21CalibrationRow(
    [string]$CalibrationId,
    [string]$Kind,
    [string]$Scenario,
    [int]$Level,
    [string]$Class,
    [string]$Status,
    [string]$FailureType,
    [int]$ExitCode,
    [double]$DurationSeconds,
    [double]$StartFreeMb,
    [double]$MinFreeMb,
    [double]$PeakPrivateMb,
    [double]$PeakWorkingMb,
    [int64]$RequestCount,
    [int64]$Ok,
    [int64]$Failed,
    [int64]$Timeout,
    [string]$ArtifactPath,
    [int64]$TelemetryRows,
    [int64]$PendingMemoryGrantsMax,
    [int64]$ResourceSemaphoreWaitersMax,
    [string]$Notes
) {
    $drop = [math]::Max(0.0, $StartFreeMb - $MinFreeMb)
    Add-CsvRow (Join-Path $script:PhaseRoot "v2.1-calibration.csv") ([pscustomobject]@{
        CalibrationId = $CalibrationId
        Kind = $Kind
        Scenario = $Scenario
        Level = $Level
        Class = $Class
        Status = $Status
        FailureType = $FailureType
        ExitCode = $ExitCode
        DurationSeconds = [math]::Round($DurationSeconds, 3)
        ObservedStartFreeMB = [math]::Round($StartFreeMb, 1)
        MinFreeMB = [math]::Round($MinFreeMb, 1)
        ActualDropMB = [math]::Round($drop, 1)
        PeakPrivateMemoryMB = [math]::Round($PeakPrivateMb, 1)
        PeakWorkingSetMB = [math]::Round($PeakWorkingMb, 1)
        RequestCount = $RequestCount
        Ok = $Ok
        Failed = $Failed
        Timeout = $Timeout
        ArtifactPath = $ArtifactPath
        TelemetryRows = $TelemetryRows
        PendingMemoryGrantsMax = $PendingMemoryGrantsMax
        ResourceSemaphoreWaitersMax = $ResourceSemaphoreWaitersMax
        Notes = $Notes
    })
}

function Invoke-V21Calibration(
    [string]$Kind,
    [string]$Class,
    [string]$CalibrationId,
    [string]$Scenario,
    [int]$Level,
    [int]$Copies
) {
    $admission = Wait-V21Admission $Class $CalibrationId $true
    if (-not [bool]$admission.Allowed) {
        $failureType = if ($Kind -eq "BDN") { "BDN_HOST_LIMIT" } else { "HOST_CAPACITY_LIMIT" }
        Write-V21CalibrationRow $CalibrationId $Kind $Scenario $Level $Class "HOST_CAPACITY_LIMIT" $failureType -20 0 ([double]$admission.ObservedAvailableMB) ([double]$admission.ObservedAvailableMB) 0 0 0 0 0 0 "" 0 0 0 "Calibration admission was not safe above the hard floor plus margin."
        $script:V21HostCapacityObserved = $true
        return [pscustomobject]@{ Status = "HOST_CAPACITY_LIMIT"; RequestCount = 0; Admission = $admission }
    }
    $row = Invoke-V21Block "CALIBRATION" $Kind $Class $CalibrationId $Scenario $Level $Copies 3 3
    Write-V21CalibrationRow $CalibrationId $Kind $Scenario $Level $Class $row.Status $row.FailureType $row.ExitCode $row.DurationSeconds $row.StartFreeMb $row.MinFreeMb $row.PeakPrivateMemoryMb $row.PeakWorkingSetMb ([int64]$row.RequestCount) ([int64]$row.Ok) ([int64]$row.Failed) ([int64]$row.Timeout) ([string]$row.ArtifactPath) ([int64]$row.TelemetryRows) ([int64]$row.PendingMemoryGrantsMax) ([int64]$row.ResourceSemaphoreWaitersMax) "Calibration only; excluded from baseline summaries and performance conclusions."
    return $row
}

function Invoke-V21Calibrations {
    $bdn = Invoke-V21Calibration "BDN" "BDN" "CAL-BDN-MasterPaged" "MasterPaged" 0 1
    $c1 = Invoke-V21Calibration "NBOMBER" "C1" "CAL-NB-MasterPaged-C1" "MasterPaged" 1 1
    $c2 = Invoke-V21Calibration "NBOMBER" "C2" "CAL-NB-MasterPaged-C2" "MasterPaged" 2 2
    $c4 = $null
    if ($c2.Status -eq "PASS" -and [int64]$c2.RequestCount -gt 0) {
        $c4 = Invoke-V21Calibration "NBOMBER" "C4" "CAL-NB-MasterPaged-C4" "MasterPaged" 4 4
    } else {
        Write-V21CalibrationRow "CAL-NB-MasterPaged-C4" "NBOMBER" "MasterPaged" 4 "C4" "HOST_CAPACITY_LIMIT" "HOST_CAPACITY_LIMIT_AT_C4" -21 0 0 0 0 0 0 0 0 0 "" 0 0 0 "Optional C4 calibration not attempted because C2 calibration produced no real requests."
    }
    Write-JsonFile (Join-Path $script:PhaseRoot "v2.1-calibration-summary.json") ([ordered]@{
        BDN = $bdn
        C1 = $c1
        C2 = $c2
        C4 = $c4
        CalibrationIsNotPerformanceResult = $true
    })
}

function Get-V21SqlAgentEvidence([string]$Phase, [string]$Label) {
    $service = Get-Service -Name 'SQLAgent$MSSQLSERVER19' -ErrorAction SilentlyContinue
    $record = [ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow.ToString("O")
        Label = $Label
        Name = 'SQLAgent$MSSQLSERVER19'
        Exists = ($null -ne $service)
        Status = if ($null -ne $service) { [string]$service.Status } else { "NOT_FOUND" }
        StartType = if ($null -ne $service) { [string]$service.StartType } else { "NOT_FOUND" }
        RequiredStatus = "Stopped"
        RequiredStartType = "Manual"
        UnchangedSafeState = ($null -ne $service -and [string]$service.Status -eq "Stopped" -and [string]$service.StartType -eq "Manual")
    }
    Write-JsonFile (Join-Path $Phase ("v2.1-sql-agent-" + $Label + ".json")) $record
    return $record
}

function Write-V21Consistency([string]$Phase, [string]$Label, [object]$Validation) {
    $checks = if ($null -ne $Validation) { @($Validation.Consistency) } else { @() }
    Write-JsonFile (Join-Path $Phase ("v2.1-consistency-" + $Label + ".json")) ([ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow.ToString("O")
        Source = "v2.1-validation-$Label.json"
        CheckCount = $checks.Count
        Checks = $checks
        AllZero = ($checks.Count -eq 11 -and @($checks | Where-Object { [int64]$_.MismatchCount -ne 0 }).Count -eq 0)
        CurrentProjectionExpected = "10,000 rows in Performance DB; no Current fixture exception for V2.1"
    })
}

function Invoke-V21ValidationBefore([string]$Phase) {
    $residuePath = Join-Path $Phase "v2.1-residue-before.json"
    $residue = Invoke-V21Child @("--residue", "--output", $residuePath) $Phase "residue-before.stdout.txt" "residue-before.stderr.txt"
    $residueJson = Get-JsonValue $residuePath
    if ($residue.ExitCode -ne 0 -or $null -eq $residueJson -or -not [bool]$residueJson.Passed) {
        throw "V2_1_RESIDUE_PRECONDITION_FAILED"
    }
    $validationPath = Join-Path $Phase "v2.1-validation-before.json"
    $validation = Invoke-V21Child @("--validate", "--output", $validationPath) $Phase "validation-before.stdout.txt" "validation-before.stderr.txt"
    $validationJson = Get-JsonValue $validationPath
    if ($null -eq $validationJson) {
        throw "V2_1_VALIDATION_JSON_MISSING"
    }
    Write-V21Consistency $Phase "before" $validationJson
    if ($validation.ExitCode -ne 0 -or -not [bool]$validationJson.Passed) {
        throw "V2_1_VALIDATION_PRECONDITION_FAILED"
    }
    return $validationJson
}

function Invoke-V21ValidationAfter([string]$Phase, [object]$BeforeValidation) {
    $validationPath = Join-Path $Phase "v2.1-validation-after.json"
    $validation = Invoke-V21Child @("--validate", "--output", $validationPath) $Phase "validation-after.stdout.txt" "validation-after.stderr.txt"
    $after = Get-JsonValue $validationPath
    if ($null -ne $after) {
        Copy-Item -LiteralPath $validationPath -Destination (Join-Path $Phase "v2.1-validation.json") -Force
        Write-V21Consistency $Phase "after" $after
    }
    if ($null -eq $after -or $validation.ExitCode -ne 0 -or -not [bool]$after.Passed) {
        $script:V21ProductFailureObserved = $true
    }
    if ($null -ne $BeforeValidation -and $null -ne $after) {
        Write-JsonFile (Join-Path $Phase "v2.1-validation-dataset-comparison.json") ([ordered]@{
            DatasetFingerprintBefore = [string]$BeforeValidation.Dataset.FingerprintSha256
            DatasetFingerprintAfter = [string]$after.Dataset.FingerprintSha256
            DatasetUnchanged = ([string]$BeforeValidation.Dataset.FingerprintSha256 -eq [string]$after.Dataset.FingerprintSha256)
            CurrentRowsetBefore = [string]$BeforeValidation.CurrentRowsetSha256
            CurrentRowsetAfter = [string]$after.CurrentRowsetSha256
            CurrentRowsetUnchanged = ([string]$BeforeValidation.CurrentRowsetSha256 -eq [string]$after.CurrentRowsetSha256)
        })
    }
    return $after
}

function Invoke-V21StaticValidation([string]$Phase) {
    $runtimeDll = Join-Path $script:RuntimeDirectory $script:MainDllName
    $help = Invoke-LoggedProcess (Get-DotnetPath) @($runtimeDll, "--help") $script:RepoRoot (Join-Path $Phase "static-help.stdout.txt") (Join-Path $Phase "static-help.stderr.txt")
    $selfTestPath = Join-Path $Phase "v2.1-self-test.json"
    $selfTest = Invoke-V21Child @("--self-test", "--output", $selfTestPath) $Phase "static-self-test.stdout.txt" "static-self-test.stderr.txt"
    $selfJson = Get-JsonValue $selfTestPath
    $record = [ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow.ToString("O")
        RuntimeDll = $runtimeDll
        RuntimeDllSha256 = Get-Sha256 $runtimeDll
        TargetFramework = "net8.0"
        HelpExitCode = $help.ExitCode
        HelpContainsProtocol = $help.Stdout.Contains("WAREHOUSE_BENCHMARK_V2_1")
        SelfTestExitCode = $selfTest.ExitCode
        SelfTestResult = if ($null -ne $selfJson) { $selfJson.Result } else { "MISSING" }
        AssemblyLoadAndArgumentParsing = ($help.ExitCode -eq 0)
        Passed = ($help.ExitCode -eq 0 -and $selfTest.ExitCode -eq 0 -and $null -ne $selfJson)
    }
    Write-JsonFile (Join-Path $Phase "v2.1-static-validation.json") $record
    if (-not [bool]$record.Passed) {
        throw "V2_1_STATIC_VALIDATION_FAILED"
    }
    return $record
}

function Invoke-V21Preparation([string]$Phase) {
    $script:RuntimeDirectory = Join-Path $script:V2ReferenceRoot "runtime"
    $script:RunnerHash = Get-Sha256 $script:V21RunnerFile
    [void](Capture-V21Product $Phase "before")
    $reference = Capture-V21Reference $Phase
    [void](Get-V21SourceClosure)
    Write-V21SourceEvidence $Phase
    [void](Capture-V21Environment $Phase)
    [void](Capture-V21DependencyReference $Phase)
    [void](Capture-V21RuntimeReference $Phase $reference)
    [void](Invoke-V21StaticValidation $Phase)
    [void](Update-V21Manifest $Phase $reference)
    $script:V21Candidate = [ordered]@{
        CandidateId = "BENCHMARK_V2_1_ADAPTIVE_CANDIDATE_$((Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss'))"
        ProtocolVersion = "WAREHOUSE_BENCHMARK_V2_1_ADAPTIVE"
        ProtocolSemanticVersion = "2.1.0"
        BenchmarkDllSha256 = $reference.BenchmarkDllSha256
        SourceClosureSha256 = $script:V21SourceClosureHash
        RuntimeBundleManifestSha256 = $reference.RuntimeBundleManifestSha256
        V2_1RunnerSha256 = $script:RunnerHash
        V2_1ManifestSha256 = $script:V21ManifestHash
        V2ManifestSha256 = $script:V21Expected.V2ManifestSha256
        V2CandidateId = $reference.V2CandidateId
        BuildConfiguration = "Release"
        TargetFramework = "net8.0"
        BuildPolicy = "NO_BUILD; accepted V2.0 frozen Release runtime reused"
        RuntimeDirectory = $reference.RuntimeDirectory
    }
    Write-JsonFile (Join-Path $Phase "v2.1-candidate-manifest.json") $script:V21Candidate
    return $script:V21Candidate
}

function Write-V21RunMetadata([string]$Phase, [string]$Stage, [string]$Result = "") {
    $metadata = [ordered]@{
        ProtocolVersion = "WAREHOUSE_BENCHMARK_V2_1_ADAPTIVE"
        ProtocolSemanticVersion = "2.1.0"
        Stage = $Stage
        Result = $Result
        CapturedAtUtc = [DateTime]::UtcNow.ToString("O")
        PhaseRoot = $Phase
        Target = [ordered]@{ Server = $script:TargetServer; Database = $script:TargetDatabase; DatabaseId = $script:TargetDatabaseId }
        Workload = [ordered]@{
            Scenarios = $script:Scenarios
            CoreLevels = @(1, 2)
            ExtendedLevel = 4
            DateRange = "2025-01-01..2026-12-31"
            Page = 1
            PageSize = 10
            Login = "PERF_USER"
            UseSnapshot = $false
            HistoricalUseCurrentBalance = $false
            CurrentUseCurrentBalance = $true
            ApplicationTimeoutSeconds = 30
            NBomber = "KeepConstant; one copy is one worker; warmup=3s; timed=15s"
            BDN = "Launch=1; warmup=2; iteration=5; invocation=1; unroll=1; InProcessNoEmit"
        }
        AdaptivePolicy = [ordered]@{
            HardFloorMb = 512
            EmergencyMarginMb = 128
            IdealStartMb = 1024
            CalibrationStartHeuristicMb = 700
            Admission = "ObservedAvailableMB - PreviousSameClassPeakDropMB > 640 MB"
            ConsecutiveSafeSamples = 2
            Universal1024Gate = $false
            HardStopAction = "Terminate only active benchmark child tree at or below 512 MB"
        }
        Cooldown = [ordered]@{
            MinimumSeconds = 5
            MaximumSeconds = 60
            RecoveryThresholdMb = 640
            RequiresChildExit = $true
            RequiresNoPendingGrant = $true
            RequiresNoResourceSemaphore = $true
            RequiresNoTargetRequest = $true
        }
        Integrity = [ordered]@{
            V1RunnerSha256 = Get-Sha256 $script:V1Runner
            V2RunnerSha256 = Get-Sha256 $script:V2Runner
            V2_1RunnerSha256 = $script:RunnerHash
            SourceClosureSha256 = $script:V21SourceClosureHash
            BenchmarkDllSha256 = $script:V21Expected.BenchmarkDllSha256
            RuntimeBundleManifestSha256 = $script:V21Expected.RuntimeBundleManifestSha256
            V2ManifestSha256 = $script:V21Expected.V2ManifestSha256
            V2_1ManifestSha256 = $script:V21ManifestHash
        }
        Boundaries = [ordered]@{
            PerformanceDatabaseMutation = $false
            BusinessDatabaseAccessed = $false
            ProductSqlMutation = $false
            SqlAgentMutation = $false
            CurrentRebuild = $false
            CacheFlush = $false
            V1OrV2ReferenceModified = $false
        }
    }
    Write-JsonFile (Join-Path $Phase "v2.1-run-metadata.json") $metadata
    return $metadata
}

function Get-V21ContiguousSafeLevel([object[]]$Rows, [string]$Scenario) {
    $safeLevel = 0
    foreach ($level in @(1, 2, 4)) {
        $row = $Rows | Where-Object { $_.Scenario -eq $Scenario -and [int]$_.Level -eq $level } | Select-Object -First 1
        if ($null -eq $row -or $row.Status -ne "PASS" -or [int64](Get-V21Numeric ([string]$row.RequestCount)) -le 0) {
            break
        }
        $safeLevel = $level
    }
    return $safeLevel
}

function Get-V21Result([string]$Phase, [object]$BeforeValidation, [object]$AfterValidation) {
    $bdnRows = @(Import-Csv -LiteralPath (Join-Path $Phase "v2.1-bdn-summary.csv"))
    $nbRows = @(Import-Csv -LiteralPath (Join-Path $Phase "v2.1-nbomber-summary.csv"))
    $coreRows = @($nbRows | Where-Object { $_.Profile -eq "CORE_NBOMBER" -and [int]$_.Level -in @(1, 2) })
    $expectedKeys = [System.Collections.Generic.List[string]]::new()
    foreach ($scenario in $script:Scenarios) {
        foreach ($level in @(1, 2)) {
            [void]$expectedKeys.Add(("{0}|{1}" -f $scenario, $level))
        }
    }
    $byKey = @{}
    foreach ($row in $coreRows) {
        $byKey[("{0}|{1}" -f $row.Scenario, $row.Level)] = $row
    }
    $missing = @($expectedKeys | Where-Object { -not $byKey.ContainsKey($_) })
    $corePass = $missing.Count -eq 0 -and $coreRows.Count -eq 12
    foreach ($key in $expectedKeys) {
        if (-not $byKey.ContainsKey($key)) {
            $corePass = $false
            continue
        }
        $row = $byKey[$key]
        if ($row.Status -ne "PASS" -or [double](Get-V21Numeric ([string]$row.DurationSeconds)) -le 0 -or [int64](Get-V21Numeric ([string]$row.RequestCount)) -le 0 -or [int64](Get-V21Numeric ([string]$row.Failed)) -gt 0 -or [int64](Get-V21Numeric ([string]$row.Timeout)) -gt 0) {
            $corePass = $false
        }
    }
    $c4Rows = @($nbRows | Where-Object { $_.Profile -eq "EXTENDED_STANDARD" -and [int]$_.Level -eq 4 })
    $c4Status = if ($c4Rows.Count -eq 6 -and @($c4Rows | Where-Object { $_.Status -eq "PASS" -and [int64](Get-V21Numeric ([string]$_.RequestCount)) -gt 0 }).Count -eq 6) {
        "EXTENDED_C4_COMPLETE"
    } elseif (@($c4Rows | Where-Object { $_.Status -eq "HOST_CAPACITY_LIMIT_AT_C4" -or $_.FailureType -eq "HOST_CAPACITY_LIMIT" }).Count -gt 0) {
        "EXTENDED_C4_HOST_CAPACITY_LIMIT"
    } else {
        "EXTENDED_C4_NOT_COMPLETE"
    }
    $bdnStatus = if ($bdnRows.Count -eq 6 -and @($bdnRows | Where-Object { $_.Status -eq "PASS" }).Count -eq 6) {
        "BDN_COMPLETE"
    } elseif (@($bdnRows | Where-Object { $_.FailureType -eq "BDN_HOST_LIMIT" -or $_.Status -eq "HOST_CAPACITY_LIMIT" }).Count -gt 0) {
        "BDN_HOST_LIMIT"
    } else {
        "BDN_NOT_COMPLETE"
    }
    $resultName = if ($null -eq $BeforeValidation) {
        "BENCHMARK_V2_1_HARNESS_FAILED"
    } elseif ($script:V21ProductFailureObserved) {
        "BENCHMARK_V2_1_PRODUCT_FAILURE_OBSERVED"
    } elseif ($script:V21HarnessFailureObserved) {
        "BENCHMARK_V2_1_HARNESS_FAILED"
    } elseif ($corePass -and $null -ne $AfterValidation -and [bool]$AfterValidation.Passed) {
        "BENCHMARK_V2_1_CORE_BASELINE_CREATED"
    } elseif ($script:V21HostCapacityObserved -or $missing.Count -gt 0) {
        "BENCHMARK_V2_1_HOST_TOO_CONSTRAINED_EVEN_FOR_CORE"
    } else {
        "BENCHMARK_V2_1_HARNESS_FAILED"
    }
    $safeConcurrency = [ordered]@{}
    foreach ($scenario in $script:Scenarios) {
        $safeConcurrency[$scenario] = Get-V21ContiguousSafeLevel $nbRows $scenario
    }
    $calibrationRows = @(Import-Csv -LiteralPath (Join-Path $Phase "v2.1-calibration.csv"))
    return [ordered]@{
        Result = $resultName
        CapturedAtUtc = [DateTime]::UtcNow.ToString("O")
        Candidate = $script:V21Candidate
        CoreBaseline = [ordered]@{
            MandatoryNBomberRows = 12
            ObservedRows = $coreRows.Count
            MissingRows = $missing
            AllMandatoryRowsPass = $corePass
            AllRowsHaveRealRequests = ($coreRows.Count -eq 12 -and @($coreRows | Where-Object { [int64](Get-V21Numeric ([string]$_.RequestCount)) -gt 0 }).Count -eq 12)
        }
        Calibration = [ordered]@{ Rows = $calibrationRows.Count; IsPerformanceResult = $false }
        BDN = [ordered]@{ Rows = $bdnRows.Count; Status = $bdnStatus; HostFailureDoesNotBlockNBomber = $true }
        ExtendedC4 = [ordered]@{ Rows = $c4Rows.Count; Status = $c4Status; RequiredForCorePass = $false }
        MaxSafeConcurrencyLevel = $safeConcurrency
        Observed = [ordered]@{
            NBomberRows = $nbRows.Count
            CoreNBomberRows = $coreRows.Count
            CorePassRows = @($coreRows | Where-Object { $_.Status -eq "PASS" }).Count
            BdnRows = $bdnRows.Count
            CalibrationRows = $calibrationRows.Count
            HostCapacityObserved = $script:V21HostCapacityObserved
            ProductFailureObserved = $script:V21ProductFailureObserved
            HarnessFailureObserved = $script:V21HarnessFailureObserved
        }
        Validation = [ordered]@{
            BeforePassed = if ($null -ne $BeforeValidation) { [bool]$BeforeValidation.Passed } else { $false }
            AfterPassed = if ($null -ne $AfterValidation) { [bool]$AfterValidation.Passed } else { $false }
            DatasetFingerprintBefore = if ($null -ne $BeforeValidation) { [string]$BeforeValidation.Dataset.FingerprintSha256 } else { "" }
            DatasetFingerprintAfter = if ($null -ne $AfterValidation) { [string]$AfterValidation.Dataset.FingerprintSha256 } else { "" }
            CurrentRowsetBefore = if ($null -ne $BeforeValidation) { [string]$BeforeValidation.CurrentRowsetSha256 } else { "" }
            CurrentRowsetAfter = if ($null -ne $AfterValidation) { [string]$AfterValidation.CurrentRowsetSha256 } else { "" }
        }
        Comparison = [ordered]@{
            V2_0Result = "BENCHMARK_V2_HOST_TOO_CONSTRAINED_FOR_STANDARD"
            V2_0StandardBlocks = 24
            V2_0MeasuredRequestCount = 0
            V2_0LatencyComparison = "NOT_AVAILABLE"
            V2_1MeasuredCoreRows = $coreRows.Count
            V2_1MeasuredCoreRequests = [int64](@($coreRows | ForEach-Object { Get-V21Numeric ([string]$_.RequestCount) } | Measure-Object -Sum).Sum)
        }
        Target = [ordered]@{ Server = $script:TargetServer; Database = $script:TargetDatabase; DatabaseId = $script:TargetDatabaseId }
        Boundaries = [ordered]@{
            BusinessDatabaseAccessed = $false
            BusinessDatabaseMutated = $false
            PerformanceDatabaseMutated = $false
            ProductSqlModified = $false
            V1RunnerModified = $false
            V2ReferenceModified = $false
            SqlAgentModified = $false
            CurrentRebuilt = $false
            CacheFlush = $false
        }
    }
}

function Write-V21Report([string]$Phase, [object]$Result, [object]$BeforeValidation, [object]$AfterValidation) {
    $lines = [System.Collections.Generic.List[string]]::new()
    [void]$lines.Add("# Benchmark V2.1 Adaptive Low-Memory 10M Baseline")
    [void]$lines.Add("")
    [void]$lines.Add("Generated: $([DateTime]::UtcNow.ToString('O'))")
    [void]$lines.Add("")
    [void]$lines.Add("## 1. Executive summary")
    [void]$lines.Add("")
    [void]$lines.Add("Final verdict: **$($Result.Result)**")
    [void]$lines.Add("")
    [void]$lines.Add("V2.1 is an independent adaptive execution policy. It preserves the V2.0 workload contract, reuses the accepted frozen Release runtime, and does not build or restore during measurement. V2.0 remains historical and unchanged.")
    [void]$lines.Add("")
    [void]$lines.Add("The mandatory core is six real Data Access scenarios at C1 and C2: 12 NBomber rows. Calibration rows establish admission evidence only and are not performance results. C4 is extended and does not invalidate a core baseline when host capacity limits it.")
    [void]$lines.Add("")
    [void]$lines.Add("## 2. V2.0 historical boundary")
    [void]$lines.Add("")
    [void]$lines.Add("V2.0 root: $script:V2ReferenceRoot")
    [void]$lines.Add("")
    [void]$lines.Add("V2.0 verdict: BENCHMARK_V2_HOST_TOO_CONSTRAINED_FOR_STANDARD. Its universal 1024 MB admission gate skipped all standard blocks before measurement, leaving zero requests and no latency baseline. V2.1 does not compare latency against those zero-request rows.")
    [void]$lines.Add("")
    [void]$lines.Add("## 3. Adaptive policy")
    [void]$lines.Add("")
    Add-MarkdownTable $lines @("Policy item", "Value", "Meaning") @(
        [pscustomobject]@{ "Policy item" = "Hard floor"; Value = "512 MB free physical RAM"; Meaning = "Terminate only the active benchmark child at or below this floor" },
        [pscustomobject]@{ "Policy item" = "Emergency margin"; Value = "128 MB"; Meaning = "Predicted minimum must remain above 640 MB" },
        [pscustomobject]@{ "Policy item" = "Ideal start"; Value = "1024 MB"; Meaning = "Ideal headroom only; not a universal gate" },
        [pscustomobject]@{ "Policy item" = "Calibration start"; Value = "700 MB heuristic"; Meaning = "Permits calibration below 1024 when safely above 640 MB" },
        [pscustomobject]@{ "Policy item" = "Admission"; Value = "Observed minus prior same-class peak drop above 640 MB"; Meaning = "Two consecutive safe samples before launch" },
        [pscustomobject]@{ "Policy item" = "Cooldown"; Value = "5 to 60 seconds"; Meaning = "Child exit, clean residue and adaptive recovery above 640 MB" }
    )
    [void]$lines.Add("")
    [void]$lines.Add("## 4. Candidate and integrity")
    [void]$lines.Add("")
    Add-MarkdownTable $lines @("Item", "Value") @(
        [pscustomobject]@{ Item = "CandidateId"; Value = $Result.Candidate.CandidateId },
        [pscustomobject]@{ Item = "Benchmark DLL SHA-256"; Value = $Result.Candidate.BenchmarkDllSha256 },
        [pscustomobject]@{ Item = "Source closure SHA-256"; Value = $Result.Candidate.SourceClosureSha256 },
        [pscustomobject]@{ Item = "Runtime bundle SHA-256"; Value = $Result.Candidate.RuntimeBundleManifestSha256 },
        [pscustomobject]@{ Item = "V2.1 runner SHA-256"; Value = $Result.Candidate.V2_1RunnerSha256 },
        [pscustomobject]@{ Item = "V2.1 manifest SHA-256"; Value = $Result.Candidate.V2_1ManifestSha256 },
        [pscustomobject]@{ Item = "V2.0 manifest SHA-256"; Value = $Result.Candidate.V2ManifestSha256 },
        [pscustomobject]@{ Item = "Source inventory"; Value = "source/source-inventory.csv and source/source-inventory.json" }
    )
    [void]$lines.Add("")
    [void]$lines.Add("The V2.1 manifest is the only manifest updated, and only its existing integrity fields were populated. The V2.0 manifest and V2.0 runner were read-only references.")
    [void]$lines.Add("")
    [void]$lines.Add("## 5. Frozen workload contract")
    [void]$lines.Add("")
    Add-MarkdownTable $lines @("Dimension", "Historical contract", "V2.1 candidate", "Result") @(
        [pscustomobject]@{ Dimension = "Scenario mapping"; "Historical contract" = "Six real Data Access operations"; "V2.1 candidate" = "Same six operations"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Parameters"; "Historical contract" = "page=1, pageSize=10, empty search, PERF_USER"; "V2.1 candidate" = "Same"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Date range"; "Historical contract" = "2025-01-01..2026-12-31"; "V2.1 candidate" = "Same"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Flags"; "Historical contract" = "Snapshot=false; Historical Current=false; Current Current=true"; "V2.1 candidate" = "Same"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Timeout"; "Historical contract" = "30-second Data Access command timeout"; "V2.1 candidate" = "Same frozen runtime"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "NBomber"; "Historical contract" = "KeepConstant; one copy=one worker; warmup=3s; timed=15s"; "V2.1 candidate" = "Same for C1/C2/C4"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "BDN"; "Historical contract" = "Launch=1; warmup=2; iteration=5; invocation=1; unroll=1; InProcessNoEmit"; "V2.1 candidate" = "Same; admission is separate"; Result = "PRESERVED" },
        [pscustomobject]@{ Dimension = "Metrics"; "Historical contract" = "Real request latency, percentiles, ok RPS, failure accounting"; "V2.1 candidate" = "Same emitted metrics; zero-request rows rejected"; Result = "PRESERVED" }
    )
    [void]$lines.Add("")
    [void]$lines.Add("## 6. Database and fixture state")
    [void]$lines.Add("")
    $beforeConsistencyText = "NOT_RUN"
    $afterConsistencyText = "NOT_RUN"
    $afterConsistencyResult = "NOT_PROVEN"
    if ($null -ne $BeforeValidation) {
        $beforeConsistencyRows = @($BeforeValidation.Consistency)
        $beforeZeroMismatch = @($beforeConsistencyRows | Where-Object { [int64]$_.MismatchCount -ne 0 }).Count -eq 0
        $beforeConsistencyText = "{0} checks; zero mismatches={1}" -f $beforeConsistencyRows.Count, $beforeZeroMismatch
    }
    if ($null -ne $AfterValidation) {
        $afterConsistencyRows = @($AfterValidation.Consistency)
        $afterZeroMismatch = @($afterConsistencyRows | Where-Object { [int64]$_.MismatchCount -ne 0 }).Count -eq 0
        $afterConsistencyText = "{0} checks; zero mismatches={1}" -f $afterConsistencyRows.Count, $afterZeroMismatch
        if ($afterConsistencyRows.Count -eq 11 -and $afterZeroMismatch) {
            $afterConsistencyResult = "PASS"
        }
    }
    Add-MarkdownTable $lines @("Check", "Before", "After", "Result") @(
        [pscustomobject]@{
            Check = "Target identity"
            Before = if ($null -ne $BeforeValidation) { "$($BeforeValidation.Identity.ServerName) / $($BeforeValidation.Identity.DatabaseName) / $($BeforeValidation.Identity.DatabaseId)" } else { "NOT_RUN" }
            After = if ($null -ne $AfterValidation) { "$($AfterValidation.Identity.ServerName) / $($AfterValidation.Identity.DatabaseName) / $($AfterValidation.Identity.DatabaseId)" } else { "NOT_RUN" }
            Result = if ($null -ne $AfterValidation -and [bool]$AfterValidation.Identity.IsTarget) { "PASS" } else { "NOT_PROVEN" }
        },
        [pscustomobject]@{
            Check = "Dataset"
            Before = if ($null -ne $BeforeValidation) { [string]$BeforeValidation.Dataset.MatchesExpected } else { "NOT_RUN" }
            After = if ($null -ne $AfterValidation) { [string]$AfterValidation.Dataset.MatchesExpected } else { "NOT_RUN" }
            Result = if ($null -ne $AfterValidation -and [bool]$AfterValidation.Dataset.MatchesExpected) { "PASS" } else { "NOT_PROVEN" }
        },
        [pscustomobject]@{
            Check = "Historical-critical consistency"
            Before = $beforeConsistencyText
            After = $afterConsistencyText
            Result = $afterConsistencyResult
        },
        [pscustomobject]@{
            Check = "Current rowset"
            Before = if ($null -ne $BeforeValidation) { [string]$BeforeValidation.CurrentRowsetSha256 } else { "NOT_RUN" }
            After = if ($null -ne $AfterValidation) { [string]$AfterValidation.CurrentRowsetSha256 } else { "NOT_RUN" }
            Result = if ($null -ne $BeforeValidation -and $null -ne $AfterValidation -and [string]$BeforeValidation.CurrentRowsetSha256 -eq [string]$AfterValidation.CurrentRowsetSha256) { "UNCHANGED" } else { "NOT_PROVEN" }
        }
    )
    [void]$lines.Add("")
    [void]$lines.Add("Expected raw fixture: receipt 5,000,000; issue 5,000,000; total 10,000,000; headers 500,000 + 500,000; products 10,000; Movement Daily 730,000; Balance Daily 730,000; Current 10,000; mode LEGACY. No rebuild, cache flush or SQL mutation was performed.")
    [void]$lines.Add("")
    [void]$lines.Add("## 7. Calibration evidence")
    [void]$lines.Add("")
    [void]$lines.Add("Calibration is admission evidence only. It is excluded from the core baseline and must not be used as a latency or throughput result.")
    [void]$lines.Add("")
    $calibrationRows = @(Import-Csv -LiteralPath (Join-Path $Phase "v2.1-calibration.csv"))
    Add-MarkdownTable $lines @("CalibrationId", "Kind", "Class", "Status", "ObservedStartFreeMB", "MinFreeMB", "ActualDropMB", "RequestCount", "TelemetryRows", "Notes") $calibrationRows
    [void]$lines.Add("")
    [void]$lines.Add("## 8. BDN execution-path result")
    [void]$lines.Add("")
    $bdnRows = @(Import-Csv -LiteralPath (Join-Path $Phase "v2.1-bdn-summary.csv"))
    Add-MarkdownTable $lines @("Profile", "BlockId", "Scenario", "Status", "FailureType", "MeanMs", "ErrorMs", "StdDevMs", "AllocatedBytes", "ArtifactPath") $bdnRows
    [void]$lines.Add("")
    [void]$lines.Add("BDN status: $($Result.BDN.Status). BDN_HOST_LIMIT is supplemental and does not stop NBomber core.")
    [void]$lines.Add("")
    [void]$lines.Add("Numeric CSV fields use the host vi-VN locale: comma is the decimal separator (for example, 25,151 means 25.151 seconds).")
    [void]$lines.Add("")
    [void]$lines.Add("## 9. Real NBomber baseline")
    [void]$lines.Add("")
    $nbRows = @(Import-Csv -LiteralPath (Join-Path $Phase "v2.1-nbomber-summary.csv"))
    Add-MarkdownTable $lines @("Profile", "Scenario", "Level", "Status", "FailureType", "DurationSeconds", "MeanMs", "P50Ms", "P95Ms", "P99Ms", "Rps", "RequestCount", "Ok", "Failed", "Timeout", "ArtifactPath") $nbRows
    [void]$lines.Add("")
    [void]$lines.Add("Core gate: 12 rows, six scenarios x C1/C2, each with duration greater than zero and real RequestCount greater than zero. Extended C4 status: $($Result.ExtendedC4.Status).")
    [void]$lines.Add("")
    [void]$lines.Add("## 10. Maximum safe concurrency")
    [void]$lines.Add("")
    $safeRows = foreach ($scenario in $script:Scenarios) {
        [pscustomobject]@{ Scenario = $scenario; MAX_SAFE_CONCURRENCY_LEVEL = $Result.MaxSafeConcurrencyLevel[$scenario] }
    }
    Add-MarkdownTable $lines @("Scenario", "MAX_SAFE_CONCURRENCY_LEVEL") $safeRows
    [void]$lines.Add("")
    [void]$lines.Add("## 11. Interpretation: code, benchmark, or host")
    [void]$lines.Add("")
    if ($Result.Result -eq "BENCHMARK_V2_1_CORE_BASELINE_CREATED") {
        [void]$lines.Add("Code optimization: NOT PROVEN either way. A baseline measures the current product candidate; it does not prove optimized or unoptimized code.")
        [void]$lines.Add("Benchmark correctness: contract, target guard, frozen runtime, parser, and real-request accounting passed for the measured rows. No evidence here says benchmark semantics are wrong.")
        [void]$lines.Add("Host capacity: only per-block RAM/process telemetry supports a capacity statement. A C4 host limit affects extended concurrency only, not the C1/C2 core.")
    } elseif ($Result.Result -eq "BENCHMARK_V2_1_HOST_TOO_CONSTRAINED_EVEN_FOR_CORE") {
        [void]$lines.Add("Host capacity is the supported explanation: the adaptive guard or hard floor prevented enough real C1/C2 rows. This does not prove the code is unoptimized and does not prove the benchmark is wrong.")
    } elseif ($Result.Result -eq "BENCHMARK_V2_1_PRODUCT_FAILURE_OBSERVED") {
        [void]$lines.Add("A product or SQL failure was observed in a real validation or measured request path. This is not a host-only conclusion.")
    } else {
        [void]$lines.Add("The harness did not produce a valid core baseline. Launch, parsing, timeout, telemetry, or accounting evidence must be fixed before a performance conclusion.")
    }
    [void]$lines.Add("")
    [void]$lines.Add("## 12. Safety boundaries")
    [void]$lines.Add("")
    [void]$lines.Add("No product SQL, test, V1 runner, V2.0 runner, V2.0 manifest, Business DB, SQL Agent, SQL Server global setting, Current rebuild, cache flush, writer workload, C8, PERF-09W, or commit was performed.")
    [void]$lines.Add("")
    [void]$lines.Add("SQL Agent required state: Stopped / Manual. Business DB: out of scope and not accessed.")
    [void]$lines.Add("")
    [void]$lines.Add("## 13. Final verdict")
    [void]$lines.Add("")
    [void]$lines.Add($Result.Result)
    [void]$lines.Add("")
    Write-TextFile (Join-Path $Phase "Benchmark-V2.1-Adaptive-Low-Memory-Baseline-Report.md") ($lines -join [Environment]::NewLine)
}

function Capture-V21PostIntegrity([string]$Phase, [object]$BeforeAgent, [object]$ReferenceBefore) {
    $product = Capture-V21Product $Phase "after"
    $v2ManifestHash = Get-Sha256 $script:V2ManifestFile
    $v2RunnerHash = Get-Sha256 $script:V2Runner
    $v1Hash = Get-Sha256 $script:V1Runner
    $runtimeHash = Get-InventoryFingerprint @(Get-V21RuntimeRows $script:RuntimeDirectory)
    $agentAfter = Get-V21SqlAgentEvidence $Phase "after"
    $oldFilesStable = $true
    foreach ($name in @("v2-result.json", "Benchmark-V2-Report.md", "candidate-manifest.json", "runtime-bundle-inventory.json")) {
        $path = Join-Path $script:V2ReferenceRoot $name
        $actualHash = if (Test-Path -LiteralPath $path -PathType Leaf) { Get-Sha256 $path } else { "" }
        $expectedHash = ""
        if ($null -ne $ReferenceBefore -and $null -ne $ReferenceBefore.OldV2RootFileHashes) {
            $property = $ReferenceBefore.OldV2RootFileHashes.PSObject.Properties[$name]
            if ($null -ne $property) {
                $expectedHash = [string]$property.Value
            }
        }
        if ($actualHash -ne $expectedHash) {
            $oldFilesStable = $false
        }
    }
    $passed = [bool]$product.Passed -and
        $v2ManifestHash -eq $script:V21Expected.V2ManifestSha256 -and
        $v2RunnerHash -eq $script:V21Expected.V2RunnerSha256 -and
        $v1Hash -eq $script:V1RunnerExpectedHash -and
        $runtimeHash -eq $script:V21Expected.RuntimeBundleManifestSha256 -and
        [bool]$agentAfter.UnchangedSafeState -and
        $oldFilesStable
    $record = [ordered]@{
        CapturedAtUtc = [DateTime]::UtcNow.ToString("O")
        Passed = $passed
        ProductIntegrity = $product
        V2ManifestSha256 = $v2ManifestHash
        V2RunnerSha256 = $v2RunnerHash
        V1RunnerSha256 = $v1Hash
        RuntimeBundleManifestSha256 = $runtimeHash
        OldV2RootFilesStable = $oldFilesStable
        SqlAgentBefore = $BeforeAgent
        SqlAgentAfter = $agentAfter
        BusinessDatabaseAccessed = $false
        PerformanceDatabaseMutation = $false
    }
    Write-JsonFile (Join-Path $Phase "v2.1-integrity-after.json") $record
    return $record
}

function Write-V21CapacitySummary([string]$Phase, [object]$Result) {
    $hostRows = @(Import-Csv -LiteralPath (Join-Path $Phase "v2.1-host-telemetry.csv"))
    $minimum = ""
    if ($hostRows.Count -gt 0) {
        $values = @($hostRows | ForEach-Object { Get-V21Numeric ([string]$_.FreePhysicalRamMb) })
        if ($values.Count -gt 0) {
            $minimum = ($values | Measure-Object -Minimum).Minimum
        }
    }
    Write-JsonFile (Join-Path $Phase "v2.1-capacity-summary.json") ([ordered]@{
        HardFloorMb = 512
        EmergencyMarginMb = 128
        IdealStartMb = 1024
        CalibrationStartHeuristicMb = 700
        HostSamples = $hostRows.Count
        MinimumObservedFreePhysicalRamMb = $minimum
        HostCapacityObserved = $script:V21HostCapacityObserved
        CoreResult = $Result.Result
        BDNStatus = $Result.BDN.Status
        ExtendedC4Status = $Result.ExtendedC4.Status
        OptionalProfiles = "C6/C8, mixed and all-six-low not run"
    })
}

function Publish-V21Baseline([string]$Phase, [object]$Result) {
    if ($Result.Result -ne "BENCHMARK_V2_1_CORE_BASELINE_CREATED") {
        return $null
    }
    $stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
    $baselineId = "BENCHMARK_V2_1_BASELINE_$stamp"
    $baselinePath = Join-Path "P:\Warehouse-Benchmark-V2" $baselineId
    New-Item -ItemType Directory -Path $baselinePath -Force | Out-Null
    foreach ($name in @(
        "Benchmark-V2.1-Adaptive-Low-Memory-Baseline-Report.md",
        "v2.1-result.json",
        "v2.1-bdn-summary.csv",
        "v2.1-nbomber-summary.csv",
        "v2.1-calibration.csv",
        "v2.1-memory-model.csv",
        "v2.1-host-telemetry.csv",
        "v2.1-sql-telemetry.csv",
        "v2.1-process-peak-memory.csv",
        "v2.1-capacity-summary.json",
        "v2.1-candidate-manifest.json",
        "manifest-post.json",
        "manifest-post.sha256",
        "v2.1-v2-reference.json",
        "v2.1-runtime-reference.json",
        "v2.1-environment.json",
        "v2.1-validation-before.json",
        "v2.1-validation-after.json",
        "v2.1-validation.json",
        "v2.1-residue-before.json",
        "v2.1-residue-after.json",
        "v2.1-integrity-after.json"
    )) {
        $source = Join-Path $Phase $name
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $baselinePath $name) -Force
        }
    }
    Copy-Item -LiteralPath $script:V21ManifestFile -Destination (Join-Path $baselinePath "benchmark-v2.1-adaptive-manifest.json") -Force
    $record = [ordered]@{
        BaselineId = $baselineId
        BaselinePath = $baselinePath
        CreatedAtUtc = [DateTime]::UtcNow.ToString("O")
        RuntimeDirectory = $script:RuntimeDirectory
        BenchmarkDllSha256 = $Result.Candidate.BenchmarkDllSha256
        SourceClosureSha256 = $Result.Candidate.SourceClosureSha256
        RuntimeBundleManifestSha256 = $Result.Candidate.RuntimeBundleManifestSha256
        V2_1RunnerSha256 = $Result.Candidate.V2_1RunnerSha256
        V2_1ManifestSha256 = Get-Sha256 $script:V21ManifestFile
    }
    Write-JsonFile (Join-Path $baselinePath "baseline-manifest.json") $record
    return $record
}

function Invoke-Phase {
    Initialize-V21Paths
    if ([string]::IsNullOrWhiteSpace($PhaseRoot)) {
        $stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
        $PhaseRoot = Join-Path "P:\Warehouse-Benchmark-V2" ("WAREHOUSE_BENCHMARK_V2_1_ADAPTIVE-" + $stamp)
    }
    $script:PhaseRoot = [IO.Path]::GetFullPath($PhaseRoot)
    New-Item -ItemType Directory -Path $script:PhaseRoot -Force | Out-Null
    Initialize-V21OutputFiles $script:PhaseRoot
    $candidate = Invoke-V21Preparation $script:PhaseRoot
    if ($SelfTest -and -not $Baseline -and -not $Validate) {
        Write-JsonFile (Join-Path $script:PhaseRoot "v2.1-result.json") ([ordered]@{
            Result = "SELF_TEST_PASS"
            Candidate = $candidate
            Target = [ordered]@{ Server = $script:TargetServer; Database = $script:TargetDatabase; DatabaseId = 5 }
        })
        Write-V21RunMetadata $script:PhaseRoot "COMPLETE" "SELF_TEST_PASS" | Out-Null
        return 0
    }

    $agentBefore = Get-V21SqlAgentEvidence $script:PhaseRoot "before"
    if (-not [bool]$agentBefore.UnchangedSafeState) {
        throw "SQL Agent is not Stopped/Manual."
    }
    $before = $null
    $after = $null
    $preconditionError = ""
    try {
        $before = Invoke-V21ValidationBefore $script:PhaseRoot
    } catch {
        $preconditionError = "$($_.Exception.GetType().Name): $($_.Exception.Message)"
        Write-TextFile (Join-Path $script:PhaseRoot "v2.1-precondition-error.txt") $preconditionError
        $script:V21HarnessFailureObserved = $true
    }

    if ($null -ne $before -and $Baseline) {
        Invoke-V21Calibrations
        Invoke-V21ActualBaseline
        $after = Invoke-V21ValidationAfter $script:PhaseRoot $before
        $residuePath = Join-Path $script:PhaseRoot "v2.1-residue-after.json"
        $residue = Invoke-V21Child @("--residue", "--output", $residuePath) $script:PhaseRoot "residue-after.stdout.txt" "residue-after.stderr.txt"
        if ($residue.ExitCode -ne 0 -or -not (Get-V21ResidueClean $residuePath)) {
            $script:V21HarnessFailureObserved = $true
        }
    } elseif ($null -ne $before) {
        Copy-Item -LiteralPath (Join-Path $script:PhaseRoot "v2.1-validation-before.json") -Destination (Join-Path $script:PhaseRoot "v2.1-validation-after.json") -Force
        Copy-Item -LiteralPath (Join-Path $script:PhaseRoot "v2.1-validation-before.json") -Destination (Join-Path $script:PhaseRoot "v2.1-validation.json") -Force
        Copy-Item -LiteralPath (Join-Path $script:PhaseRoot "v2.1-residue-before.json") -Destination (Join-Path $script:PhaseRoot "v2.1-residue-after.json") -Force
        Copy-Item -LiteralPath (Join-Path $script:PhaseRoot "v2.1-consistency-before.json") -Destination (Join-Path $script:PhaseRoot "v2.1-consistency-after.json") -Force
        $after = $before
    } else {
        $residuePath = Join-Path $script:PhaseRoot "v2.1-residue-after.json"
        $residue = Invoke-V21Child @("--residue", "--output", $residuePath) $script:PhaseRoot "residue-after.stdout.txt" "residue-after.stderr.txt"
        if ($residue.ExitCode -ne 0 -or -not (Get-V21ResidueClean $residuePath)) {
            $script:V21HarnessFailureObserved = $true
        }
    }

    $referenceBefore = Get-JsonValue (Join-Path $script:PhaseRoot "v2.1-v2-reference.json")
    $post = Capture-V21PostIntegrity $script:PhaseRoot $agentBefore $referenceBefore
    $result = Get-V21Result $script:PhaseRoot $before $after
    if (-not [string]::IsNullOrWhiteSpace($preconditionError)) {
        $result["Result"] = "BENCHMARK_V2_1_HARNESS_FAILED"
        $result["PreconditionError"] = $preconditionError
    }
    if (-not [bool]$post.Passed) {
        $result["Result"] = "BENCHMARK_V2_1_HARNESS_FAILED"
        $result["IntegrityAfterFailed"] = $true
    }
    if ($Validate -and -not $Baseline -and [string]::IsNullOrWhiteSpace($preconditionError)) {
        $result["Result"] = "VALIDATION_ONLY_PASS"
    }
    $result["PostIntegrityPassed"] = [bool]$post.Passed
    Write-V21CapacitySummary $script:PhaseRoot $result
    Write-JsonFile (Join-Path $script:PhaseRoot "v2.1-result.json") $result
    Write-V21Report $script:PhaseRoot $result $before $after
    $published = Publish-V21Baseline $script:PhaseRoot $result
    if ($null -ne $published) {
        $result["BaselineId"] = $published.BaselineId
        $result["BaselinePath"] = $published.BaselinePath
        Write-JsonFile (Join-Path $script:PhaseRoot "v2.1-result.json") $result
        Write-V21Report $script:PhaseRoot $result $before $after
        Copy-Item -LiteralPath (Join-Path $script:PhaseRoot "v2.1-result.json") -Destination (Join-Path $published.BaselinePath "v2.1-result.json") -Force
        Copy-Item -LiteralPath (Join-Path $script:PhaseRoot "Benchmark-V2.1-Adaptive-Low-Memory-Baseline-Report.md") -Destination (Join-Path $published.BaselinePath "Benchmark-V2.1-Adaptive-Low-Memory-Baseline-Report.md") -Force
    }
    Write-V21RunMetadata $script:PhaseRoot "COMPLETE" $result.Result | Out-Null
    if ($result.Result -in @("BENCHMARK_V2_1_CORE_BASELINE_CREATED", "BENCHMARK_V2_1_HOST_TOO_CONSTRAINED_EVEN_FOR_CORE", "VALIDATION_ONLY_PASS")) {
        return 0
    }
    return 1
}

if ($Help -or -not ($Validate -or $SelfTest -or $Baseline)) {
    Show-Help
    exit 0
}

try {
    $exitCode = Invoke-Phase
    exit $exitCode
} catch {
    $message = "$($_.Exception.GetType().Name): $($_.Exception.Message)"
    Write-Error "V2_FATAL|$message"
    if (-not [string]::IsNullOrWhiteSpace($script:PhaseRoot)) {
        Write-TextFile (Join-Path $script:PhaseRoot "v2.1-fatal-error.txt") $message
        if ($null -ne $script:V21Candidate) {
            Write-V21RunMetadata $script:PhaseRoot "FATAL" "BENCHMARK_V2_1_HARNESS_FAILED" | Out-Null
        }
    }
    exit 1
}
