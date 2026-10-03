[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RepoRoot,
    [Parameter(Mandatory=$true)][string]$CandidateRoot,
    [Parameter(Mandatory=$true)][string]$ReviewRoot,
    [Parameter(Mandatory=$true)][string]$CandidateId,
    [Parameter(Mandatory=$true)][string]$StaticCheckPath,
    [Parameter(Mandatory=$true)][string]$SelfTestPath,
    [Parameter(Mandatory=$true)][string]$FailureInjectionPath,
    [Parameter(Mandatory=$true)][string]$ReportLintPath,
    [string]$NuGetPackagesRoot,
    [string]$NuGetCachePreInventoryPath
)
$ErrorActionPreference='Stop'
$stage='BATCH3_PERFORMANCE_CANDIDATE'
$repo=[IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')
$root=[IO.Path]::GetFullPath($CandidateRoot)
$review=[IO.Path]::GetFullPath($ReviewRoot)
$expectedReview=[IO.Path]::GetFullPath((Join-Path $root 'Batch3-Canonical-Run-Evidence'))
if($review -cne $expectedReview){throw 'ReviewRoot must be the unique canonical run evidence root under CandidateRoot'}
$logs=Join-Path $root 'logs'
$inventoryPath=Join-Path $review 'V22-Performance-Source-Inventory.json'
$attestationPath=Join-Path $review 'V22-Performance-Build-Attestation.json'
$manifestPath=Join-Path $review 'V22-Performance-Candidate-Manifest.json'
$runtimeInventoryPath=Join-Path $review 'V22-Performance-Runtime-Inventory.json'
$attestation=[ordered]@{SchemaVersion='warehouse-benchmark-v22-build-attestation/1';CandidateId=$CandidateId;ProtocolVersion='2.2';Status='IN_PROGRESS';Stage=$stage;Builds=@();BuildOutputComparison=$null;Outputs=@();Failure=$null}

function Write-NewJson([string]$Path,[object]$Value){
    if(Test-Path -LiteralPath $Path){throw "Evidence path already exists: $Path"}
    $json=$Value|ConvertTo-Json -Depth 16
    $stream=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($json+[Environment]::NewLine);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
}
function Write-NewText([string]$Path,[string]$Value){
    $stream=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($Value);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
}
function Append-Text([string]$Path,[string]$Value){
    if(-not(Test-Path -LiteralPath $Path -PathType Leaf)){throw "Append target missing: $Path"}
    $stream=[IO.File]::Open($Path,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($Value);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
}
function Get-Hash([string]$Path){return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Get-Relative([string]$Base,[string]$Path){$prefix=[IO.Path]::GetFullPath($Base).TrimEnd('\')+'\';return ([IO.Path]::GetFullPath($Path).Substring($prefix.Length).Replace('\','/'))}
function Get-DirtyState([string]$Repository){
    $commit=(& git -C $Repository rev-parse HEAD 2>$null | Out-String).Trim()
    if($LASTEXITCODE -ne 0){$commit=$null}
    $statusLines=@(& git -c core.quotePath=false -C $Repository status --porcelain=v1 --untracked-files=all 2>$null)
    if($LASTEXITCODE -ne 0){throw 'Git status could not be captured'}
    $rows=[Collections.Generic.List[object]]::new()
    foreach($line in $statusLines){
        if([string]::IsNullOrWhiteSpace($line) -or $line.Length -lt 4){continue}
        $status=$line.Substring(0,2);$rawPath=$line.Substring(3)
        $paths=@($rawPath)
        if($rawPath -match '^(.*?) -> (.*)$'){$paths=@($Matches[1],$Matches[2])}
        foreach($repoPath in $paths){
            $full=Join-Path $Repository $repoPath
            $exists=Test-Path -LiteralPath $full -PathType Leaf
            $hash=if($exists){Get-Hash $full}else{$null}
            $rows.Add([pscustomobject]@{Status=$status;Path=$repoPath.Replace('\','/').Normalize([Text.NormalizationForm]::FormC);Exists=[bool]$exists;SHA256=$hash})
        }
    }
    $sorted=[Collections.Generic.SortedDictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach($row in $rows){$key=$row.Path+'|'+$row.Status;if(-not $sorted.ContainsKey($key)){$sorted.Add($key,$row)}}
    $entries=@($sorted.Values);$tab=[string][char]9
    $canonical=(@($entries|ForEach-Object{[string]::Join($tab,@($_.Status,$_.Path,[string]$_.Exists,[string]$_.SHA256))}) -join [Environment]::NewLine)+[Environment]::NewLine
    $identity=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical))).ToLowerInvariant()
    return [pscustomobject]@{Commit=$commit;Dirty=($entries.Count -gt 0);DirtyStateIdentity=$identity;PathCount=$entries.Count;Entries=$entries}
}
function Invoke-Dotnet([string[]]$Arguments,[string]$LogPath,[string]$WorkingDirectory){
    if(Test-Path -LiteralPath $LogPath){throw "Build log already exists: $LogPath"}
    $start=[DateTime]::UtcNow
    Push-Location -LiteralPath $WorkingDirectory
    try{& dotnet @Arguments *> $LogPath;$code=$LASTEXITCODE}finally{Pop-Location}
    $end=[DateTime]::UtcNow
    $content=''
    if(Test-Path -LiteralPath $LogPath){$content=[IO.File]::ReadAllText($LogPath)}
    $warnings=[regex]::Matches($content,'(?im)\bwarning\s+[A-Z]{2,}\d+').Count
    $errors=[regex]::Matches($content,'(?im)\berror\s+[A-Z]{2,}\d+').Count
    return [pscustomobject]@{Command=('dotnet '+($Arguments -join ' '));WorkingDirectory=$WorkingDirectory;StartedUtc=$start.ToString('o');EndedUtc=$end.ToString('o');ExitCode=$code;WarningCount=$warnings;ErrorCount=$errors;LogPath=$LogPath;Succeeded=($code -eq 0 -and $errors -eq 0)}
}
function Get-BinOutputRows([string]$ArtifactsPath){
    $bin=Join-Path $ArtifactsPath 'bin'
    if(-not(Test-Path -LiteralPath $bin -PathType Container)){throw "Build output bin root missing: $bin"}
    $map=[Collections.Generic.SortedDictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach($file in Get-ChildItem -LiteralPath $bin -File -Recurse -Force){$relative=Get-Relative $bin $file.FullName;$map.Add($relative,[pscustomobject]@{RelativePath=$relative;Size=[long]$file.Length;SHA256=(Get-Hash $file.FullName);FullPath=$file.FullName})}
    return @($map.Values)
}
function Get-GeneratedCompileInputs([string]$ArtifactsPath){
    $obj=Join-Path $ArtifactsPath 'obj'
    if(-not(Test-Path -LiteralPath $obj -PathType Container)){return @()}
    $map=[Collections.Generic.SortedDictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach($file in Get-ChildItem -LiteralPath $obj -Filter '*.cs' -File -Recurse -Force){$relative=Get-Relative $ArtifactsPath $file.FullName;$map.Add($relative,[pscustomobject]@{RelativePath=$relative;FullPath=$file.FullName;Size=[long]$file.Length;SHA256=(Get-Hash $file.FullName);Origin='MSBuild-generated compile input'})}
    return @($map.Values)
}
function Find-OutputFile([object[]]$Rows,[string]$FileName,[string]$ProjectSegment){
    $found=@($Rows|Where-Object{([IO.Path]::GetFileName($_.FullPath) -ceq $FileName) -and $_.RelativePath.StartsWith($ProjectSegment,[StringComparison]::OrdinalIgnoreCase)})
    if($found.Count -ne 1){throw "Expected one $FileName under $ProjectSegment, found $($found.Count)"}
    return $found[0]
}
function Get-PackageAssets([string]$ArtifactsPath){
    $result=[Collections.Generic.List[object]]::new()
    foreach($file in Get-ChildItem -LiteralPath $ArtifactsPath -Filter 'project.assets.json' -File -Recurse -Force){
        $assets=Get-Content -LiteralPath $file.FullName -Raw|ConvertFrom-Json
        $packages=@($assets.libraries.PSObject.Properties|Where-Object{$_.Value.type -eq 'package'}|ForEach-Object{$_.Name}|Sort-Object -CaseSensitive)
        $result.Add([pscustomobject]@{AssetsPath=$file.FullName;AssetsSHA256=(Get-Hash $file.FullName);ResolvedPackages=$packages;PackageCount=$packages.Count})
    }
    return $result.ToArray()
}

try{
    if(-not(Test-Path -LiteralPath $repo -PathType Container) -or -not(Test-Path -LiteralPath $root -PathType Container)){throw 'Repository or candidate root does not exist'}
    if($CandidateId -notmatch '^WHB22-PERF-20261002-[A-F0-9]{8}$'){throw 'CandidateId format is invalid'}
    if(-not(Test-Path -LiteralPath $logs -PathType Container) -or -not(Test-Path -LiteralPath $review -PathType Container)){throw 'Candidate evidence folders are missing'}
    $proof=Get-Content -LiteralPath (Join-Path $review 'Previous-Evidence-Preservation-Prebuild.json') -Raw|ConvertFrom-Json
    $static=Get-Content -LiteralPath $StaticCheckPath -Raw|ConvertFrom-Json
$self=Get-Content -LiteralPath $SelfTestPath -Raw|ConvertFrom-Json
$failure=Get-Content -LiteralPath $FailureInjectionPath -Raw|ConvertFrom-Json
$reportLint=Get-Content -LiteralPath $ReportLintPath -Raw|ConvertFrom-Json
    if($proof.Status -cne 'PRE_CAPTURED_POST_PENDING'){throw '2.1 PRE preservation evidence is missing or invalid'}
if($static.Status -cne 'PASS' -or $self.Status -cne 'PASS' -or $failure.Status -cne 'PASS' -or $failure.Failed -ne 0 -or $reportLint.Status -cne 'PASS' -or $reportLint.IssueCount -ne 0 -or $self.ReportLintSHA256 -cne (Get-Hash $ReportLintPath)){throw 'Phase 1 gate failed; no candidate build is allowed'}
    if(-not(Test-Path -LiteralPath (Join-Path $review 'V22-Performance-Source-Inventory-Spec.md') -PathType Leaf)){throw 'Canonical source inventory rule is missing'}
    $reservedPaths=@('source-snapshot','build-a','build-b','runtime');if([string]::IsNullOrWhiteSpace($NuGetPackagesRoot)){$reservedPaths+=@('nuget-packages')}
    foreach($name in $reservedPaths){if(Test-Path -LiteralPath (Join-Path $root $name)){throw "Refusing to reuse existing Phase 2 path: $name"}}
    $sharedNuGet=$false;$nuGetPre=$null;$packages=Join-Path $root 'nuget-packages'
    if(-not[string]::IsNullOrWhiteSpace($NuGetPackagesRoot)){
        $packages=[IO.Path]::GetFullPath($NuGetPackagesRoot)
        if(-not(Test-Path -LiteralPath $packages -PathType Container)){throw 'External NuGet package cache is missing'}
        if($packages.StartsWith($root.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Shared package cache must be outside the candidate root'}
        if([string]::IsNullOrWhiteSpace($NuGetCachePreInventoryPath) -or -not(Test-Path -LiteralPath $NuGetCachePreInventoryPath -PathType Leaf)){throw 'External NuGet cache pre-inventory is missing'}
        $nuGetPre=Get-Content -LiteralPath $NuGetCachePreInventoryPath -Raw|ConvertFrom-Json
        if($nuGetPre.Status -cne 'PASS' -or [IO.Path]::GetFullPath($nuGetPre.PackageCacheRoot) -cne $packages){throw 'External NuGet cache pre-inventory is invalid or identifies a different root'}
        $sharedNuGet=$true
    }

    $stage='TOOLCHAIN_CAPTURE'
    $dotnetInfoLog=Join-Path $logs 'dotnet-info.log';$msbuildLog=Join-Path $logs 'msbuild-version.log'
    if((Test-Path $dotnetInfoLog) -or (Test-Path $msbuildLog)){throw 'Toolchain logs already exist'}
    & dotnet --info *> $dotnetInfoLog;$dotnetInfoExit=$LASTEXITCODE
    & dotnet msbuild -version -nologo *> $msbuildLog;$msbuildExit=$LASTEXITCODE
    if($dotnetInfoExit -ne 0 -or $msbuildExit -ne 0){throw 'Toolchain query failed'}
    $dotnetInfo=Get-Content -LiteralPath $dotnetInfoLog -Raw;$msbuildInfo=Get-Content -LiteralPath $msbuildLog -Raw
    $sdk=((& dotnet --version 2>$null)|Out-String).Trim()
    $toolchain=[ordered]@{SchemaVersion='warehouse-benchmark-v22-toolchain/1';CapturedUtc=[DateTime]::UtcNow.ToString('o');DotnetSdk=$sdk;DotnetInfo=$dotnetInfo;MSBuildVersionOutput=$msbuildInfo;OSDescription=[Runtime.InteropServices.RuntimeInformation]::OSDescription;OSArchitecture=[string][Runtime.InteropServices.RuntimeInformation]::OSArchitecture;ProcessArchitecture=[string][Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture;PowerShellVersion=[string]$PSVersionTable.PSVersion;PowerShellEdition=[string]$PSVersionTable.PSEdition;BuildConfiguration='Release';TargetFrameworks=@('net8.0','net6.0')}
    Write-NewJson (Join-Path $review 'V22-Toolchain.json') $toolchain

    $stage='SOURCE_INVENTORY'
    & (Join-Path $PSScriptRoot 'New-V22CanonicalSourceInventory.ps1') -RepoRoot $repo -OutputPath $inventoryPath | Out-Null
    $inventory=Get-Content -LiteralPath $inventoryPath -Raw|ConvertFrom-Json
    if(@($inventory.MissingInputs).Count -gt 0){throw 'Canonical inventory contains missing build inputs'}
    if(@($inventory.Projects|Where-Object Path -like '*Benchmarks_V22.csproj').Count -ne 1 -or @($inventory.Projects|Where-Object Path -like '*Data_Access.csproj').Count -ne 1){throw 'MSBuild project closure is incomplete'}
    $expectedCandidateId='WHB22-PERF-20261002-'+$inventory.InventorySHA256.Substring(0,8).ToUpperInvariant()
    if($CandidateId -cne $expectedCandidateId){throw 'CandidateId does not match the canonical source inventory identity'}
    $dirty=Get-DirtyState $repo
    Write-NewJson (Join-Path $review 'V22-Dirty-State.json') $dirty
    $snapshotId='V22SRC-'+$inventory.InventorySHA256.Substring(0,16).ToUpperInvariant()
    $snapshotPath=Join-Path $root 'source-snapshot';New-Item -ItemType Directory -Path $snapshotPath|Out-Null
    $copyRows=[Collections.Generic.List[object]]::new()
    foreach($entry in $inventory.Entries){
        if(-not $entry.Exists){throw "Source inventory input missing: $($entry.NormalizedRelativePath)"}
        $relative=$entry.NormalizedRelativePath.Replace('/','\')
        $source=Join-Path $repo $relative
        $destination=Join-Path $snapshotPath $relative
        $parent=Split-Path -Parent $destination
        if(-not(Test-Path -LiteralPath $parent -PathType Container)){New-Item -ItemType Directory -Path $parent|Out-Null}
        if(Test-Path -LiteralPath $destination){throw "Snapshot destination already exists: $relative"}
        Copy-Item -LiteralPath $source -Destination $destination
        $snapshotHash=Get-Hash $destination
        if($snapshotHash -cne $entry.SHA256){throw "Snapshot copy hash mismatch: $relative"}
        $copyRows.Add([pscustomobject]@{Path=$entry.NormalizedRelativePath;SourceSHA256=$entry.SHA256;SnapshotSHA256=$snapshotHash;Match=$true})
    }
    $snapshotEvidence=[ordered]@{SchemaVersion='warehouse-benchmark-v22-source-snapshot/1';SnapshotId=$snapshotId;CandidateId=$CandidateId;InventoryPath=$inventoryPath;InventorySHA256=$inventory.InventorySHA256;EntryCount=$inventory.EntryCount;GitCommit=$dirty.Commit;DirtyStateIdentity=$dirty.DirtyStateIdentity;Dirty=$dirty.Dirty;DirtyPathCount=$dirty.PathCount;SnapshotRoot=$snapshotPath;CopyEntryCount=$copyRows.Count;CopyRows=$copyRows.ToArray();CapturedUtc=[DateTime]::UtcNow.ToString('o')}
    Write-NewJson (Join-Path $review 'V22-Performance-Source-Snapshot.json') $snapshotEvidence

    $stage='BUILD_A_RESTORE'
    $projectRelative='TKS_Thuc_Tap_V11_Benchmarks_V22\TKS_Thuc_Tap_V11_Benchmarks_V22.csproj'
    $snapshotProject=Join-Path $snapshotPath $projectRelative
    if(-not $sharedNuGet){New-Item -ItemType Directory -Path $packages|Out-Null}
    $buildA=Join-Path $root 'build-a';New-Item -ItemType Directory -Path $buildA|Out-Null;$artifactsA=Join-Path $buildA 'artifacts'
    $buildB=Join-Path $root 'build-b';New-Item -ItemType Directory -Path $buildB|Out-Null;$artifactsB=Join-Path $buildB 'artifacts'
    $pathMapA="$snapshotPath=/__v22_source%2C$buildA=/__v22_build"
    $pathMapB="$snapshotPath=/__v22_source%2C$buildB=/__v22_build"
    $restoreA=Invoke-Dotnet @('restore',$snapshotProject,'--packages',$packages,'--artifacts-path',$artifactsA,'--verbosity','normal') (Join-Path $logs 'restore-a.log') $snapshotPath
    $attestation.Builds+=@([pscustomobject]@{BuildId='A-RESTORE';Kind='Restore';Result=$restoreA})
    if(-not $restoreA.Succeeded){throw 'Build A restore failed'}
    $stage='BUILD_B_RESTORE'
    $restoreB=Invoke-Dotnet @('restore',$snapshotProject,'--packages',$packages,'--artifacts-path',$artifactsB,'--verbosity','normal') (Join-Path $logs 'restore-b.log') $snapshotPath
    $attestation.Builds+=@([pscustomobject]@{BuildId='B-RESTORE';Kind='Restore';Result=$restoreB})
    if(-not $restoreB.Succeeded){throw 'Build B restore failed'}

    if($sharedNuGet){
        $knownPackages=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($package in $nuGetPre.Packages){[void]$knownPackages.Add(([string]$package.PackageId+'/'+[string]$package.Version))}
        foreach($assets in @((Get-PackageAssets $artifactsA)+(Get-PackageAssets $artifactsB))){foreach($packageIdentity in $assets.ResolvedPackages){if(-not $knownPackages.Contains([string]$packageIdentity)){throw "External NuGet cache pre-inventory does not contain required package identity: $packageIdentity"}}}
        $nuGetPostPath=Join-Path $logs 'V22-NuGet-External-Package-Inventory-Post.json'
        & (Join-Path $PSScriptRoot 'New-V22NuGetCacheInventory.ps1') -PackageCacheRoot $packages -OutputPath $nuGetPostPath | Out-Null
        $nuGetPost=Get-Content -LiteralPath $nuGetPostPath -Raw|ConvertFrom-Json
        if($nuGetPost.Status -cne 'PASS' -or $nuGetPost.InventorySHA256 -cne $nuGetPre.InventorySHA256 -or $nuGetPost.FileCount -ne $nuGetPre.FileCount -or $nuGetPost.PackageCount -ne $nuGetPre.PackageCount){throw 'EXTERNAL_NUGET_CACHE_MUTATED; candidate freeze is prohibited'}
        $attestation.ExternalNuGetCache=[ordered]@{Status='PASS_UNCHANGED';PackageCacheRoot=$packages;PreInventoryPath=$NuGetCachePreInventoryPath;PreInventorySHA256=(Get-Hash $NuGetCachePreInventoryPath);PreContentInventorySHA256=$nuGetPre.InventorySHA256;PostInventoryPath=$nuGetPostPath;PostInventorySHA256=(Get-Hash $nuGetPostPath);PostContentInventorySHA256=$nuGetPost.InventorySHA256;FileCount=$nuGetPost.FileCount;PackageCount=$nuGetPost.PackageCount;TotalBytes=$nuGetPost.TotalBytes;CopiedIntoCandidateRoot=$false;ResolvedPackagesA=(Get-PackageAssets $artifactsA);ResolvedPackagesB=(Get-PackageAssets $artifactsB)}
    }else{$attestation.ExternalNuGetCache=[ordered]@{Status='NOT_APPLICABLE_LOCAL_CACHE';PackageCacheRoot=$packages;CopiedIntoCandidateRoot=$true}}

    $stage='BUILD_A_RELEASE'
    $buildAResult=Invoke-Dotnet @('build',$snapshotProject,'--configuration','Release','--no-restore','--artifacts-path',$artifactsA,'/p:Deterministic=true',("/p:PathMap="+$pathMapA),'--verbosity','normal') (Join-Path $logs 'build-a-release.log') $snapshotPath
    $attestation.Builds+=@([pscustomobject]@{BuildId='A';Kind='ReleaseBuild';Configuration='Release';TargetFrameworks=@('net8.0','net6.0');Result=$buildAResult;GeneratedCompileInputs=@()})
    if(-not $buildAResult.Succeeded){throw 'Build A Release failed'}
    $generatedA=Get-GeneratedCompileInputs $artifactsA
    $attestation.Builds[-1].GeneratedCompileInputs=$generatedA

    $stage='BUILD_B_RELEASE'
    $buildBResult=Invoke-Dotnet @('build',$snapshotProject,'--configuration','Release','--no-restore','--artifacts-path',$artifactsB,'/p:Deterministic=true',("/p:PathMap="+$pathMapB),'--verbosity','normal') (Join-Path $logs 'build-b-release.log') $snapshotPath
    $attestation.Builds+=@([pscustomobject]@{BuildId='B';Kind='ReleaseBuild';Configuration='Release';TargetFrameworks=@('net8.0','net6.0');Result=$buildBResult;GeneratedCompileInputs=@()})
    if(-not $buildBResult.Succeeded){throw 'Build B Release failed'}
    $generatedB=Get-GeneratedCompileInputs $artifactsB
    $attestation.Builds[-1].GeneratedCompileInputs=$generatedB

    $stage='REPRODUCIBILITY_COMPARE'
    $rowsA=Get-BinOutputRows $artifactsA;$rowsB=Get-BinOutputRows $artifactsB
    $mapB=@{};foreach($row in $rowsB){$mapB[$row.RelativePath.ToLowerInvariant()]=$row.SHA256}
    $differences=[Collections.Generic.List[object]]::new()
    foreach($row in $rowsA){$key=$row.RelativePath.ToLowerInvariant();if(-not $mapB.ContainsKey($key)){$differences.Add([pscustomobject]@{Path=$row.RelativePath;BuildA=$row.SHA256;BuildB=$null;Reason='MISSING_IN_BUILD_B'})}elseif($mapB[$key] -cne $row.SHA256){$differences.Add([pscustomobject]@{Path=$row.RelativePath;BuildA=$row.SHA256;BuildB=$mapB[$key];Reason='HASH_MISMATCH'})}}
    foreach($row in $rowsB){if(-not $rowsA.RelativePath.Contains($row.RelativePath)){$differences.Add([pscustomobject]@{Path=$row.RelativePath;BuildA=$null;BuildB=$row.SHA256;Reason='EXTRA_IN_BUILD_B'})}}
    $generatedMapB=@{};foreach($row in $generatedB){$generatedMapB[$row.RelativePath.ToLowerInvariant()]=$row.SHA256}
    foreach($row in $generatedA){$key=$row.RelativePath.ToLowerInvariant();if(-not $generatedMapB.ContainsKey($key)){$differences.Add([pscustomobject]@{Path=$row.RelativePath;BuildA=$row.SHA256;BuildB=$null;Reason='GENERATED_COMPILE_INPUT_MISSING_IN_BUILD_B'})}elseif($generatedMapB[$key] -cne $row.SHA256){$differences.Add([pscustomobject]@{Path=$row.RelativePath;BuildA=$row.SHA256;BuildB=$generatedMapB[$key];Reason='GENERATED_COMPILE_INPUT_HASH_MISMATCH'})}}
    foreach($row in $generatedB){if(-not $generatedA.RelativePath.Contains($row.RelativePath)){$differences.Add([pscustomobject]@{Path=$row.RelativePath;BuildA=$null;BuildB=$row.SHA256;Reason='GENERATED_COMPILE_INPUT_EXTRA_IN_BUILD_B'})}}
    $reproStatus=if($differences.Count -eq 0 -and $rowsA.Count -eq $rowsB.Count -and $generatedA.Count -eq $generatedB.Count){'PASS'}else{'BUILD_REPRODUCIBILITY_FAILED'}
    $attestation.BuildOutputComparison=[pscustomobject]@{Status=$reproStatus;BuildAOutputCount=$rowsA.Count;BuildBOutputCount=$rowsB.Count;ComparedOutputCount=[Math]::Min($rowsA.Count,$rowsB.Count);GeneratedCompileInputCountA=$generatedA.Count;GeneratedCompileInputCountB=$generatedB.Count;GeneratedCompileInputsA=$generatedA;GeneratedCompileInputsB=$generatedB;Differences=$differences.ToArray();BuildAOutputs=$rowsA;BuildBOutputs=$rowsB}
    if($reproStatus -cne 'PASS'){$attestation.Status='BUILD_REPRODUCIBILITY_FAILED';$attestation.Stage=$stage;Write-NewJson $attestationPath $attestation;throw 'BUILD_REPRODUCIBILITY_FAILED; final candidate freeze is prohibited'}

    $stage='RUNTIME_FREEZE'
    $runtimeFreezeLog=Join-Path $logs 'runtime-freeze.log'
    Write-NewText $runtimeFreezeLog ("Warehouse Benchmark 2.2 runtime freeze`nCandidateId=$CandidateId`nStartedUtc=$([DateTime]::UtcNow.ToString('o'))`nStatus=IN_PROGRESS`n")
    $mainA=Find-OutputFile $rowsA 'TKS_Thuc_Tap_V11_Benchmarks_V22.dll' 'TKS_Thuc_Tap_V11_Benchmarks_V22/'
    $mainB=Find-OutputFile $rowsB 'TKS_Thuc_Tap_V11_Benchmarks_V22.dll' 'TKS_Thuc_Tap_V11_Benchmarks_V22/'
    $daAppA=Find-OutputFile $rowsA 'TKS_Thuc_Tap_V11_Data_Access.dll' 'TKS_Thuc_Tap_V11_Benchmarks_V22/'
    $daAppB=Find-OutputFile $rowsB 'TKS_Thuc_Tap_V11_Data_Access.dll' 'TKS_Thuc_Tap_V11_Benchmarks_V22/'
    $daProjectA=Find-OutputFile $rowsA 'TKS_Thuc_Tap_V11_Data_Access.dll' 'TKS_Thuc_Tap_V11_Data_Access/'
    $daProjectB=Find-OutputFile $rowsB 'TKS_Thuc_Tap_V11_Data_Access.dll' 'TKS_Thuc_Tap_V11_Data_Access/'
    $depsA=Find-OutputFile $rowsA 'TKS_Thuc_Tap_V11_Benchmarks_V22.deps.json' 'TKS_Thuc_Tap_V11_Benchmarks_V22/'
    $depsB=Find-OutputFile $rowsB 'TKS_Thuc_Tap_V11_Benchmarks_V22.deps.json' 'TKS_Thuc_Tap_V11_Benchmarks_V22/'
    $runtimeConfigA=Find-OutputFile $rowsA 'TKS_Thuc_Tap_V11_Benchmarks_V22.runtimeconfig.json' 'TKS_Thuc_Tap_V11_Benchmarks_V22/'
    $runtimeConfigB=Find-OutputFile $rowsB 'TKS_Thuc_Tap_V11_Benchmarks_V22.runtimeconfig.json' 'TKS_Thuc_Tap_V11_Benchmarks_V22/'
    $runtimeRoot=Join-Path $root 'runtime';New-Item -ItemType Directory -Path $runtimeRoot|Out-Null
    $appDir=Split-Path -Parent $mainA.FullPath
    foreach($file in Get-ChildItem -LiteralPath $appDir -File -Recurse -Force){$relative=Get-Relative $appDir $file.FullName;$destination=Join-Path $runtimeRoot $relative;$parent=Split-Path -Parent $destination;if(-not(Test-Path -LiteralPath $parent -PathType Container)){New-Item -ItemType Directory -Path $parent|Out-Null};if(Test-Path -LiteralPath $destination){throw "Runtime destination exists: $relative"};$sourceHash=Get-Hash $file.FullName;Copy-Item -LiteralPath $file.FullName -Destination $destination;$frozenHash=Get-Hash $destination;if($sourceHash -cne $frozenHash){throw "Runtime copy hash mismatch: $relative"};Append-Text $runtimeFreezeLog ("FILE`t{0}`t{1}`t{2}`t{3}`n" -f $relative,$file.Length,$sourceHash,$frozenHash)}
    $runtimeRows=[Collections.Generic.SortedDictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach($file in Get-ChildItem -LiteralPath $runtimeRoot -File -Recurse -Force){
        $relative=Get-Relative $runtimeRoot $file.FullName
        $role=if($relative -ceq 'TKS_Thuc_Tap_V11_Benchmarks_V22.dll'){'benchmark-assembly'}elseif($relative -ceq 'TKS_Thuc_Tap_V11_Data_Access.dll'){'data-access-assembly'}elseif($file.Name -ceq 'TKS_Thuc_Tap_V11_Benchmarks_V22.deps.json'){'dependency-manifest'}elseif($file.Name -ceq 'TKS_Thuc_Tap_V11_Benchmarks_V22.runtimeconfig.json'){'runtime-config'}else{'runtime-dependency'}
        $classification='unknown';if($file.Extension -in @('.json','.config')){$classification='configuration'}elseif($file.Extension -in @('.dll','.exe')){try{[void][Reflection.AssemblyName]::GetAssemblyName($file.FullName);$classification='managed'}catch{$classification='native-or-unmanaged'}}
        $required=($role -in @('benchmark-assembly','data-access-assembly','dependency-manifest','runtime-config'))
        $runtimeRows.Add($relative,[pscustomobject]@{RelativePath=$relative;Size=[long]$file.Length;SHA256=(Get-Hash $file.FullName);Role=$role;OriginBuild='A';Required=$required;Classification=$classification})
    }
    foreach($requiredName in @('TKS_Thuc_Tap_V11_Benchmarks_V22.dll','TKS_Thuc_Tap_V11_Data_Access.dll','TKS_Thuc_Tap_V11_Benchmarks_V22.deps.json','TKS_Thuc_Tap_V11_Benchmarks_V22.runtimeconfig.json')){if(-not(Test-Path -LiteralPath (Join-Path $runtimeRoot $requiredName) -PathType Leaf)){throw "Required runtime file missing: $requiredName"}}
    $runtimeEntries=@($runtimeRows.Values);$tab=[string][char]9;$runtimeCanonical=(@($runtimeEntries|ForEach-Object{[string]::Join($tab,@($_.RelativePath,[string]$_.Size,$_.SHA256,$_.Role,$_.OriginBuild,[string]$_.Required,$_.Classification))}) -join [Environment]::NewLine)+[Environment]::NewLine
    $runtimeHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($runtimeCanonical))).ToLowerInvariant()
    $runtimeInventory=[ordered]@{SchemaVersion='warehouse-benchmark-v22-runtime-inventory/1';RuntimeRoot=$runtimeRoot;OriginBuild='A';HashAlgorithm='SHA-256';EntryCount=$runtimeEntries.Count;InventorySHA256=$runtimeHash;Entries=$runtimeEntries}
    Write-NewJson $runtimeInventoryPath $runtimeInventory
    foreach($check in @(@{Source=$mainA;Runtime='TKS_Thuc_Tap_V11_Benchmarks_V22.dll'},@{Source=$daAppA;Runtime='TKS_Thuc_Tap_V11_Data_Access.dll'},@{Source=$depsA;Runtime='TKS_Thuc_Tap_V11_Benchmarks_V22.deps.json'},@{Source=$runtimeConfigA;Runtime='TKS_Thuc_Tap_V11_Benchmarks_V22.runtimeconfig.json'})){
        if((Get-Hash $check.Source.FullPath) -cne (Get-Hash (Join-Path $runtimeRoot $check.Runtime))){throw "Frozen runtime hash mismatch: $($check.Runtime)"}
    }
    $runtimeProbeLog=Join-Path $logs 'runtime-protocol-info.log';if(Test-Path $runtimeProbeLog){throw 'Runtime probe log exists'}
    & dotnet (Join-Path $runtimeRoot 'TKS_Thuc_Tap_V11_Benchmarks_V22.dll') --protocol-info *> $runtimeProbeLog
    $probeExit=$LASTEXITCODE
    if($probeExit -ne 0){throw 'Frozen runtime protocol-info probe failed'}
    $protocolInfo=Get-Content $runtimeProbeLog -Raw|ConvertFrom-Json
    if($protocolInfo.ProtocolVersion -cne '2.2' -or $protocolInfo.DataAccessAssembly -cne 'TKS_Thuc_Tap_V11_Data_Access' -or $protocolInfo.PerformanceExecutionEnabled -ne $true){throw 'Frozen runtime identity probe mismatch'}
    Append-Text $runtimeFreezeLog ("InventoryEntryCount=$($runtimeEntries.Count)`nInventorySHA256=$runtimeHash`nProtocolProbeExitCode=$probeExit`nProtocolVersion=$($protocolInfo.ProtocolVersion)`nDataAccessAssembly=$($protocolInfo.DataAccessAssembly)`nPerformanceExecutionEnabled=$($protocolInfo.PerformanceExecutionEnabled)`nCompletedUtc=$([DateTime]::UtcNow.ToString('o'))`nStatus=PASS`n")

    $stage='CANDIDATE_MANIFEST'
    $sourceFileMap=@{};foreach($entry in $inventory.Entries){$sourceFileMap[$entry.NormalizedRelativePath]=$entry.SHA256}
    $harness= [ordered]@{AdaptiveRunnerSHA256=$sourceFileMap['benchmarks/v2.2/run-warehouse-benchmark-v2.2-adaptive.ps1'];MixedRunnerSHA256=$sourceFileMap['benchmarks/v2.2/run-warehouse-benchmark-v2.2-mixed.ps1'];HarnessModuleSHA256=$sourceFileMap['benchmarks/v2.2/WarehouseBenchmarkV22.Harness.psm1'];FinalizerModuleSHA256=$sourceFileMap['benchmarks/v2.2/WarehouseBenchmarkV22.Finalizer.psm1'];SelfTestImplementationSHA256=$sourceFileMap['benchmarks/v2.2/Invoke-V22OfflineSelfTest.ps1'];FailureInjectionImplementationSHA256=$sourceFileMap['benchmarks/v2.2/Invoke-V22FailureInjection.ps1'];FailureInjectionResultsSHA256=(Get-Hash $FailureInjectionPath);OfflineSelfTestResultsSHA256=(Get-Hash $SelfTestPath);ReportTemplateLintSHA256=(Get-Hash $ReportLintPath)}
    $attestation.Status='BUILD_REPRODUCIBILITY_PASS';$attestation.Stage='COMPLETE';$attestation.SourceSnapshotId=$snapshotId;$attestation.SourceInventorySHA256=$inventory.InventorySHA256;$attestation.GitCommit=$dirty.Commit;$attestation.DirtyStateIdentity=$dirty.DirtyStateIdentity;$attestation.PackageAssetsA=Get-PackageAssets $artifactsA;$attestation.PackageAssetsB=Get-PackageAssets $artifactsB;$attestation.GeneratedCompileInputsA=$generatedA;$attestation.GeneratedCompileInputsB=$generatedB;$attestation.RuntimeInventorySHA256=$runtimeHash;$attestation.RuntimeInventoryEntryCount=$runtimeEntries.Count;$attestation.RuntimeRoot=$runtimeRoot;$attestation.RuntimeFreezeLogPath=$runtimeFreezeLog;$attestation.RuntimeFreezeLogSHA256=Get-Hash $runtimeFreezeLog;$attestation.ProtocolInfo=$protocolInfo
    $attestation.Outputs=@([pscustomobject]@{Role='BenchmarkDll';BuildA=$mainA;BuildB=$mainB;SHA256=$mainA.SHA256},[pscustomobject]@{Role='DataAccessDllInRuntime';BuildA=$daAppA;BuildB=$daAppB;SHA256=$daAppA.SHA256},[pscustomobject]@{Role='DataAccessProjectOutput';BuildA=$daProjectA;BuildB=$daProjectB;SHA256=$daProjectA.SHA256},[pscustomobject]@{Role='DepsJson';BuildA=$depsA;BuildB=$depsB;SHA256=$depsA.SHA256},[pscustomobject]@{Role='RuntimeConfigJson';BuildA=$runtimeConfigA;BuildB=$runtimeConfigB;SHA256=$runtimeConfigA.SHA256})
    Write-NewJson $attestationPath $attestation
    $attestationHash=Get-Hash $attestationPath
    $externalDependencies=[ordered]@{NuGetCache=$attestation.ExternalNuGetCache;SourceInventorySeparate=$true;ExternalPackageInventoryPath=if($sharedNuGet){$NuGetCachePreInventoryPath}else{$null}}
    $manifest=[ordered]@{SchemaVersion='warehouse-benchmark-v22-performance-candidate/2';CandidateId=$CandidateId;ProtocolVersion='2.2';Status='CANDIDATE_BUILT_AND_ATTESTED';CreatedUtc=[DateTime]::UtcNow.ToString('o');Source=[ordered]@{SnapshotId=$snapshotId;InventorySHA256=$inventory.InventorySHA256;InventoryEntryCount=$inventory.EntryCount;InventoryPath=$inventoryPath;SnapshotRoot=$snapshotPath;GitCommit=$dirty.Commit;Dirty=$dirty.Dirty;DirtyStateIdentity=$dirty.DirtyStateIdentity;DirtyPathCount=$dirty.PathCount};Build=[ordered]@{AttestationId=('V22BUILD-'+$inventory.InventorySHA256.Substring(0,12).ToUpperInvariant());AttestationPath=$attestationPath;AttestationSHA256=$attestationHash;BuildAId='A';BuildBId='B';ReproducibilityStatus=$reproStatus;Configuration='Release';BenchmarkTFM='net8.0';DataAccessTFM='net6.0';DotnetSdk=$sdk;MSBuildVersionOutput=$msbuildInfo.Trim();BuildAArtifacts=$rowsA;BuildBArtifacts=$rowsB;ExternalNuGetCacheStatus=$attestation.ExternalNuGetCache.Status};Binary=[ordered]@{BenchmarkDll=[ordered]@{Path='TKS_Thuc_Tap_V11_Benchmarks_V22.dll';SHA256=(Get-Hash $mainA.FullPath)};DataAccessDll=[ordered]@{Path='TKS_Thuc_Tap_V11_Data_Access.dll';SHA256=(Get-Hash $daAppA.FullPath)};DataAccessProjectOutput=[ordered]@{Path=$daProjectA.RelativePath;SHA256=$daProjectA.SHA256};DepsJson=[ordered]@{Path=$depsA.RelativePath;SHA256=$depsA.SHA256};RuntimeConfigJson=[ordered]@{Path=$runtimeConfigA.RelativePath;SHA256=$runtimeConfigA.SHA256}};Runtime=[ordered]@{Root=$runtimeRoot;InventoryPath=$runtimeInventoryPath;InventorySHA256=$runtimeHash;EntryCount=$runtimeEntries.Count;OriginBuild='A'};ExternalDependencies=$externalDependencies;Harness=$harness;HistoricalReference=[ordered]@{ProtocolVersion='2.1';Role='HistoricalComparator';ExecutableAsCandidateBinary=$false;FrozenPackage='P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_1-20260915-211500'};Evidence=[ordered]@{PreservationProof=(Join-Path $review 'Previous-Evidence-Preservation.json');SourceSnapshot=(Join-Path $review 'V22-Performance-Source-Snapshot.json');Toolchain=(Join-Path $review 'V22-Toolchain.json');FailureInjection=$FailureInjectionPath;OfflineSelfTest=$SelfTestPath;ReportTemplateLint=$ReportLintPath;RestoreA=(Join-Path $logs 'restore-a.log');BuildA=(Join-Path $logs 'build-a-release.log');RestoreB=(Join-Path $logs 'restore-b.log');BuildB=(Join-Path $logs 'build-b-release.log');ExternalNuGetPreInventory=$NuGetCachePreInventoryPath;ExternalNuGetPostInventory=if($sharedNuGet){$nuGetPostPath}else{$null};RuntimeProtocolInfo=$runtimeProbeLog;RuntimeFreezeLog=$runtimeFreezeLog;RuntimeFreezeLogSHA256=(Get-Hash $runtimeFreezeLog);CandidatePostFreezeGuard=(Join-Path $review 'V22-Performance-Candidate-Guard.json');CandidateGuardLog=(Join-Path $logs 'candidate-guard.log');RuntimeInventory=$runtimeInventoryPath;V21PreservationPost=(Join-Path $review 'Previous-Evidence-Preservation.json')}}
    $harness.Batch3PerformanceRunnerSHA256=$sourceFileMap['benchmarks/v2.2/run-warehouse-benchmark-v2.2-performance.ps1']
    $harness.PerformanceCandidateBuilderSHA256=$sourceFileMap['benchmarks/v2.2/Build-Freeze-V22PerformanceCandidate.ps1']
    $manifest.CandidateRole='PerformanceCandidate'
    $manifest.PerformanceExecutionEnabled=$true
    $manifest.Batch2FoundationCandidateId='WHB22-20261001-8F0CCE91'
    $manifest.PerformanceProtocolEvidencePath=(Join-Path $review 'V22-Performance-Protocol.json')
    $manifest.PerformanceProtocolEvidenceSHA256=(Get-Hash (Join-Path $review 'V22-Performance-Protocol.json'))
    $manifest.PreviousEvidencePreservationPrebuildSHA256=(Get-Hash (Join-Path $review 'Previous-Evidence-Preservation-Prebuild.json'))
    $manifest.PerformanceResultStatus='NOT_RUN'
    $manifest.PerformanceSlaStatus='NO_SLA_DEFINED'
    $manifest.WorkloadConfiguration=[ordered]@{Scenarios=@('MasterPaged','LookupPaged','DocumentPaged','DetailReportPaged','InventoryHistoricalReportPaged','InventoryCurrentBalancePaged');IsolatedOrder='Per scenario: BDN, C1, C2, C4';NBomberWarmupSeconds=3;NBomberTimedSeconds=15;MixedLevels=@(1,2,4,8);MixedTotalWorkers=@(6,12,24,48);MinimumCommonOverlapSeconds=10;BDN=[ordered]@{Toolchain='InProcessNoEmit';LaunchCount=1;WarmupCount=2;IterationCount=5;InvocationCount=1;UnrollFactor=1}}
    $manifest.HistoricalReference.Role='HistoricalComparatorOnly'
    $manifest.HistoricalReference.ExecutableAsCandidateBinary=$false
    Write-NewJson $manifestPath $manifest
    $manifestHash=Get-Hash $manifestPath
    $sidecar=Join-Path $review 'V22-Performance-Candidate-Manifest.sha256.txt';if(Test-Path $sidecar){throw 'Manifest hash sidecar already exists'};Set-Content -LiteralPath $sidecar -Value ($manifestHash+'  V22-Performance-Candidate-Manifest.json') -Encoding ascii


    [pscustomobject]@{CandidateId=$CandidateId;Status=$manifest.Status;SourceSnapshotId=$snapshotId;SourceInventoryCount=$inventory.EntryCount;BenchmarkDllSHA256=$mainA.SHA256;DataAccessDllSHA256=$daAppA.SHA256;RuntimeInventoryCount=$runtimeEntries.Count;RuntimeInventorySHA256=$runtimeHash;ManifestSHA256=$manifestHash;BuildReproducibility=$reproStatus}
}catch{
    $attestation.Status=if($stage -eq 'REPRODUCIBILITY_COMPARE'){'BUILD_REPRODUCIBILITY_FAILED'}else{'CANDIDATE_BUILD_FAILED'}
    $attestation.Stage=$stage
    $attestation.Failure=[ordered]@{Message=($_.Exception.Message -replace '(?i)(password|pwd|token|secret)\s*=\s*[^;,\s]+','$1=[REDACTED]');RecordedUtc=[DateTime]::UtcNow.ToString('o')}
    if(-not(Test-Path -LiteralPath $attestationPath)){try{Write-NewJson $attestationPath $attestation}catch{}}
    $failurePath=Join-Path $review 'V22-Build-Failure.json'
    if(-not(Test-Path -LiteralPath $failurePath)){try{Write-NewJson $failurePath ([ordered]@{Status=$attestation.Status;Stage=$stage;Message=$attestation.Failure.Message;RecordedUtc=[DateTime]::UtcNow.ToString('o')})}catch{}}
    throw
}



