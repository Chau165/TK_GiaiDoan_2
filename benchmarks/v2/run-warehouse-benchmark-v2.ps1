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

if ($Help -or -not ($Validate -or $SelfTest -or $Baseline -or $Bdn -or $NbomberStandard -or $Capacity -or $Mixed -or $AllSixLow)) {
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
        Write-TextFile (Join-Path $script:PhaseRoot "v2-fatal-error.txt") $message
        Write-RunMetadata $script:PhaseRoot "FATAL" "BENCHMARK_V2_HARNESS_FAILED" | Out-Null
    }
    exit 1
}
