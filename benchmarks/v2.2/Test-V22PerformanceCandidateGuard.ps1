[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RepoRoot,
    [Parameter(Mandatory=$true)][string]$CandidateRoot,
    [Parameter(Mandatory=$true)][string]$CandidateManifestPath,
    [Parameter(Mandatory=$true)][string]$SourceInventoryPath,
    [Parameter(Mandatory=$true)][string]$RuntimeInventoryPath,
    [Parameter(Mandatory=$true)][string]$BuildAttestationPath,
    [Parameter(Mandatory=$true)][string]$InventoryCheckPath,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [string]$ExpectedManifestSHA256
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')
$root=[IO.Path]::GetFullPath($CandidateRoot)
$checks=[Collections.Generic.List[object]]::new()
function Add-Check([string]$Name,[bool]$Passed,[object]$Evidence){$checks.Add([pscustomobject]@{Name=$Name;Status=$(if($Passed){'PASS'}else{'FAIL'});Evidence=$Evidence})}
function Get-Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Get-Relative([string]$Base,[string]$Path){$prefix=[IO.Path]::GetFullPath($Base).TrimEnd('\')+'\';([IO.Path]::GetFullPath($Path).Substring($prefix.Length).Replace('\','/'))}
if(Test-Path -LiteralPath $OutputPath){throw 'Candidate guard output already exists'}
if(Test-Path -LiteralPath $InventoryCheckPath){throw 'Candidate inventory check output already exists'}
$manifest=Get-Content -LiteralPath $CandidateManifestPath -Raw|ConvertFrom-Json
$inventory=Get-Content -LiteralPath $SourceInventoryPath -Raw|ConvertFrom-Json
$runtimeInventory=Get-Content -LiteralPath $RuntimeInventoryPath -Raw|ConvertFrom-Json
$attestation=Get-Content -LiteralPath $BuildAttestationPath -Raw|ConvertFrom-Json
$manifestHash=Get-Hash $CandidateManifestPath
$sidecarPath=Join-Path (Split-Path -Parent $CandidateManifestPath) 'V22-Performance-Candidate-Manifest.sha256.txt'
$sidecarHash=if(Test-Path -LiteralPath $sidecarPath -PathType Leaf){([IO.File]::ReadAllText($sidecarPath).Trim() -split '\s+')[0].ToLowerInvariant()}else{''}
$checksManifest=($manifest.Status -ceq 'CANDIDATE_BUILT_AND_ATTESTED' -and $manifest.ProtocolVersion -ceq '2.2' -and $manifest.PerformanceExecutionEnabled -eq $true -and $manifest.CandidateRole -ceq 'PerformanceCandidate' -and $manifest.Source.InventorySHA256 -ceq $inventory.InventorySHA256 -and $manifest.Runtime.InventorySHA256 -ceq $runtimeInventory.InventorySHA256 -and $attestation.Status -ceq 'BUILD_REPRODUCIBILITY_PASS' -and $attestation.SourceInventorySHA256 -ceq $inventory.InventorySHA256 -and $attestation.SourceSnapshotId -ceq $manifest.Source.SnapshotId -and $manifest.Build.AttestationSHA256 -ceq (Get-Hash $BuildAttestationPath) -and $sidecarHash -ceq $manifestHash)
if($ExpectedManifestSHA256){$checksManifest=$checksManifest -and $manifestHash -ceq $ExpectedManifestSHA256.ToLowerInvariant()}
Add-Check 'CANDIDATE_MANIFEST_ATTESTATION_LINK' $checksManifest ([pscustomobject]@{CandidateId=$manifest.CandidateId;ManifestSHA256=$manifestHash;SidecarSHA256=$sidecarHash;ExpectedManifestSHA256=$ExpectedManifestSHA256;AttestationSHA256=(Get-Hash $BuildAttestationPath);Status=$manifest.Status})

& (Join-Path $PSScriptRoot 'New-V22CanonicalSourceInventory.ps1') -RepoRoot $repo -OutputPath $InventoryCheckPath | Out-Null
$currentInventory=Get-Content -LiteralPath $InventoryCheckPath -Raw|ConvertFrom-Json
$inventoryMatch=($currentInventory.InventorySHA256 -ceq $inventory.InventorySHA256 -and $currentInventory.EntryCount -eq $inventory.EntryCount -and @($currentInventory.MissingInputs).Count -eq 0)
$snapshotRoot=[IO.Path]::GetFullPath([string]$manifest.Source.SnapshotRoot)
$sourceDrift=[Collections.Generic.List[object]]::new()
$expectedSnapshotPaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($entry in $inventory.Entries){
    $relative=([string]$entry.NormalizedRelativePath).Replace('/','\')
    $source=Join-Path $repo $relative
    $snapshot=Join-Path $snapshotRoot $relative
    [void]$expectedSnapshotPaths.Add([IO.Path]::GetFullPath($snapshot))
    if(-not(Test-Path -LiteralPath $source -PathType Leaf) -or -not(Test-Path -LiteralPath $snapshot -PathType Leaf)){$sourceDrift.Add([pscustomobject]@{Path=$entry.NormalizedRelativePath;Reason='MISSING_SOURCE_OR_SNAPSHOT'});continue}
    $sourceHash=Get-Hash $source;$snapshotHash=Get-Hash $snapshot
    if($sourceHash -cne $entry.SHA256 -or $snapshotHash -cne $entry.SHA256){$sourceDrift.Add([pscustomobject]@{Path=$entry.NormalizedRelativePath;Expected=$entry.SHA256;Current=$sourceHash;Snapshot=$snapshotHash})}
}
$snapshotFiles=@(Get-ChildItem -LiteralPath $snapshotRoot -File -Recurse -Force)
foreach($file in $snapshotFiles){if(-not $expectedSnapshotPaths.Contains([IO.Path]::GetFullPath($file.FullName))){$sourceDrift.Add([pscustomobject]@{Path=(Get-Relative $snapshotRoot $file.FullName);Reason='UNLISTED_SNAPSHOT_FILE'})}}
$runtimeCanonical=(@($runtimeInventory.Entries|ForEach-Object{[string]::Join([string][char]9,@($_.RelativePath,[string]$_.Size,$_.SHA256,$_.Role,$_.OriginBuild,[string]$_.Required,$_.Classification))}) -join [Environment]::NewLine)+[Environment]::NewLine
$runtimeCanonicalHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($runtimeCanonical))).ToLowerInvariant()
$runtimeInventorySelfValid=($runtimeCanonicalHash -ceq [string]$runtimeInventory.InventorySHA256 -and @($runtimeInventory.Entries).Count -eq [int]$runtimeInventory.EntryCount)
$sourceMatch=$inventoryMatch -and $sourceDrift.Count -eq 0 -and $snapshotFiles.Count -eq $inventory.EntryCount
Add-Check 'CANONICAL_SOURCE_AND_SNAPSHOT' $sourceMatch ([pscustomobject]@{ExpectedInventorySHA256=$inventory.InventorySHA256;CurrentInventorySHA256=$currentInventory.InventorySHA256;EntryCount=$inventory.EntryCount;SnapshotFileCount=$snapshotFiles.Count;Drift=$sourceDrift.ToArray()})

$runtimeRoot=[IO.Path]::GetFullPath([string]$manifest.Runtime.Root)
$runtimeDrift=[Collections.Generic.List[object]]::new()
$expectedRuntime=@{};foreach($entry in $runtimeInventory.Entries){$expectedRuntime[[string]$entry.RelativePath]= $entry}
$actualFiles=@(Get-ChildItem -LiteralPath $runtimeRoot -File -Recurse -Force)
foreach($file in $actualFiles){
    $relative=Get-Relative $runtimeRoot $file.FullName
    if(-not $expectedRuntime.ContainsKey($relative)){$runtimeDrift.Add([pscustomobject]@{Path=$relative;Reason='UNLISTED_RUNTIME_FILE'});continue}
    $expected=$expectedRuntime[$relative];$actualHash=Get-Hash $file.FullName
    if([long]$file.Length -ne [long]$expected.Size -or $actualHash -cne [string]$expected.SHA256){$runtimeDrift.Add([pscustomobject]@{Path=$relative;ExpectedSHA256=$expected.SHA256;ActualSHA256=$actualHash;ExpectedSize=$expected.Size;ActualSize=$file.Length})}
    $expectedRuntime.Remove($relative)
}
foreach($missing in $expectedRuntime.Keys){$runtimeDrift.Add([pscustomobject]@{Path=$missing;Reason='MISSING_RUNTIME_FILE'})}
$runtimeCore=[ordered]@{}
foreach($name in @('TKS_Thuc_Tap_V11_Benchmarks_V22.dll','TKS_Thuc_Tap_V11_Data_Access.dll','TKS_Thuc_Tap_V11_Benchmarks_V22.deps.json','TKS_Thuc_Tap_V11_Benchmarks_V22.runtimeconfig.json')){
    $path=Join-Path $runtimeRoot $name
    $expectedHash=if($name -eq 'TKS_Thuc_Tap_V11_Benchmarks_V22.dll'){[string]$manifest.Binary.BenchmarkDll.SHA256}elseif($name -eq 'TKS_Thuc_Tap_V11_Data_Access.dll'){[string]$manifest.Binary.DataAccessDll.SHA256}elseif($name -like '*.deps.json'){[string]$manifest.Binary.DepsJson.SHA256}else{[string]$manifest.Binary.RuntimeConfigJson.SHA256}
    $actualHash=if(Test-Path -LiteralPath $path -PathType Leaf){Get-Hash $path}else{''}
    $runtimeCore[$name]=[pscustomobject]@{Expected=$expectedHash;Actual=$actualHash;Match=($actualHash -and $actualHash -ceq $expectedHash)}
}
$coreMatch=@($runtimeCore.Values|Where-Object{ -not $_.Match}).Count -eq 0
$runtimeMatch=$runtimeInventorySelfValid -and $runtimeDrift.Count -eq 0 -and $actualFiles.Count -eq $runtimeInventory.EntryCount -and $coreMatch
Add-Check 'FROZEN_RUNTIME_AND_BINARY_HASHES' $runtimeMatch ([pscustomobject]@{ExpectedRuntimeInventorySHA256=$manifest.Runtime.InventorySHA256;RecomputedRuntimeInventorySHA256=$runtimeCanonicalHash;RuntimeEntryCount=$runtimeInventory.EntryCount;ActualFileCount=$actualFiles.Count;CoreBinaryHashes=$runtimeCore;Drift=$runtimeDrift.ToArray()})

$outputHash=@{}
foreach($role in @('BenchmarkDll','DataAccessDll')){
    $needle=if($role -eq 'BenchmarkDll'){'TKS_Thuc_Tap_V11_Benchmarks_V22.dll'}else{'TKS_Thuc_Tap_V11_Data_Access.dll'}
    $a=@($manifest.Build.BuildAArtifacts|Where-Object{$_.RelativePath -like ('*TKS_Thuc_Tap_V11_Benchmarks_V22/*'+$needle)})|Select-Object -First 1
    $b=@($manifest.Build.BuildBArtifacts|Where-Object{$_.RelativePath -like ('*TKS_Thuc_Tap_V11_Benchmarks_V22/*'+$needle)})|Select-Object -First 1
    $outputHash[$role]=[pscustomobject]@{BuildA=$a.SHA256;BuildB=$b.SHA256;Manifest=$manifest.Binary.$role.SHA256;Reproducible=($null -ne $a -and $null -ne $b -and $a.SHA256 -ceq $b.SHA256 -and $a.SHA256 -ceq $manifest.Binary.$role.SHA256)}
}
$buildMatch=@($outputHash.Values|Where-Object{-not $_.Reproducible}).Count -eq 0
Add-Check 'BUILD_A_B_REPRODUCIBILITY' $buildMatch $outputHash

$status=if(@($checks|Where-Object Status -ne 'PASS').Count -eq 0){'PASS'}else{'INVALID'}
$result=[ordered]@{SchemaVersion='warehouse-benchmark-v22-performance-candidate-guard/1';CandidateId=$manifest.CandidateId;Status=$status;ManifestSHA256=$manifestHash;Checks=$checks.ToArray();CheckedUtc=[DateTime]::UtcNow.ToString('o')}
$json=$result|ConvertTo-Json -Depth 14
$stream=[IO.File]::Open($OutputPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($json+[Environment]::NewLine);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
$result
if($status -cne 'PASS'){exit 1}
