[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RepoRoot,
    [Parameter(Mandatory=$true)][string]$CandidateRoot,
    [Parameter(Mandatory=$true)][string]$CandidateId
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($RepoRoot).TrimEnd('\');$root=[IO.Path]::GetFullPath($CandidateRoot);$review=Join-Path $root 'Batch1-Review-Pack';$logs=Join-Path $root 'logs'
$manifestPath=Join-Path $review 'V22-Candidate-Manifest.json';$inventoryPath=Join-Path $review 'V22-Source-Inventory.json';$runtimeInventoryPath=Join-Path $review 'V22-Runtime-Inventory.json';$proofPath=Join-Path $review 'V21-Preservation-Proof.json';$guardPath=Join-Path $review 'V22-Candidate-Guard-Result.json';$guardLogPath=Join-Path $logs 'candidate-guard.log'
if(Test-Path -LiteralPath $guardPath){throw "Candidate guard result already exists; refusing overwrite: $guardPath"}
if(Test-Path -LiteralPath $guardLogPath){throw "Candidate guard log already exists; refusing overwrite: $guardLogPath"}
$logStream=[IO.File]::Open($guardLogPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$startText="Warehouse Benchmark 2.2 candidate guard`nCandidateId=$CandidateId`nStartedUtc=$([DateTime]::UtcNow.ToString('o'))`n";$startBytes=[Text.UTF8Encoding]::new($false).GetBytes($startText);$logStream.Write($startBytes,0,$startBytes.Length);$logStream.Flush($true)}finally{$logStream.Dispose()}
function Add-GuardLog([string]$Text){[IO.File]::AppendAllText($guardLogPath,$Text+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))}
function Get-Hash([string]$Path){return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Write-NewJson([string]$Path,[object]$Value){if(Test-Path -LiteralPath $Path){throw "Guard result already exists: $Path"};$json=$Value|ConvertTo-Json -Depth 12;$stream=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($json+[Environment]::NewLine);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}}
function Capture-Tree([string]$Path){
    if(-not(Test-Path -LiteralPath $Path)){throw "Immutable root missing after run: $Path"}
    $item=Get-Item -LiteralPath $Path -Force
    if($item.PSIsContainer){$files=@(Get-ChildItem -LiteralPath $Path -File -Recurse -Force);$base=$item.FullName.TrimEnd('\')+'\'}else{$files=@($item);$base=(Split-Path -Parent $item.FullName).TrimEnd('\')+'\'}
    $entries=[Collections.Generic.List[object]]::new()
    foreach($file in $files){$relative=$file.FullName.Substring($base.Length).Replace('\','/');$entries.Add([pscustomobject]@{RelativePath=$relative;Size=[long]$file.Length;SHA256=(Get-Hash $file.FullName)})}
    $sorted=@($entries|Sort-Object -Property RelativePath -CaseSensitive)
    $canonical=($sorted|ForEach-Object{"$($_.RelativePath)`t$($_.Size)`t$($_.SHA256)`n"})-join ''
    $tree=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical))).ToLowerInvariant()
    return [pscustomobject]@{Path=$item.FullName;Kind=$(if($item.PSIsContainer){'directory'}else{'file'});FileCount=$sorted.Count;ByteCount=[long](($sorted|Measure-Object Size -Sum).Sum);TreeSHA256=$tree;Entries=$sorted}
}
function Get-RuntimeSnapshot([string]$RuntimeRoot){
    $map=[Collections.Generic.SortedDictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach($file in Get-ChildItem -LiteralPath $RuntimeRoot -File -Recurse -Force){
        $relative=$file.FullName.Substring(([IO.Path]::GetFullPath($RuntimeRoot).TrimEnd('\')+'\').Length).Replace('\','/')
        $role=if($relative -ceq 'TKS_Thuc_Tap_V11_Benchmarks_V22.dll'){'benchmark-assembly'}elseif($relative -ceq 'TKS_Thuc_Tap_V11_Data_Access.dll'){'data-access-assembly'}elseif($file.Name -ceq 'TKS_Thuc_Tap_V11_Benchmarks_V22.deps.json'){'dependency-manifest'}elseif($file.Name -ceq 'TKS_Thuc_Tap_V11_Benchmarks_V22.runtimeconfig.json'){'runtime-config'}else{'runtime-dependency'}
        $classification='unknown';if($file.Extension -in @('.json','.config')){$classification='configuration'}elseif($file.Extension -in @('.dll','.exe')){try{[void][Reflection.AssemblyName]::GetAssemblyName($file.FullName);$classification='managed'}catch{$classification='native-or-unmanaged'}}
        $required=($role -in @('benchmark-assembly','data-access-assembly','dependency-manifest','runtime-config'))
        $map.Add($relative,[pscustomobject]@{RelativePath=$relative;Size=[long]$file.Length;SHA256=(Get-Hash $file.FullName);Role=$role;OriginBuild='A';Required=$required;Classification=$classification})
    }
    $entries=@($map.Values);$tab=[string][char]9;$canonical=(@($entries|ForEach-Object{[string]::Join($tab,@($_.RelativePath,[string]$_.Size,$_.SHA256,$_.Role,$_.OriginBuild,[string]$_.Required,$_.Classification))})-join [Environment]::NewLine)+[Environment]::NewLine
    $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical))).ToLowerInvariant()
    return [pscustomobject]@{EntryCount=$entries.Count;InventorySHA256=$hash;Entries=$entries}
}
$results=[Collections.Generic.List[object]]::new()
try{
    $manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
    $expectedManifestHash=(Get-Content -LiteralPath (Join-Path $review 'V22-Candidate-Manifest.sha256.txt') -Raw).Trim().Split(' ')[0].ToLowerInvariant()
    $actualManifestHash=Get-Hash $manifestPath
    $manifestPass=($actualManifestHash -ceq $expectedManifestHash -and $manifest.CandidateId -ceq $CandidateId -and $manifest.ProtocolVersion -ceq '2.2' -and $manifest.Status -ceq 'CANDIDATE_BUILT_AND_ATTESTED')
    $results.Add([pscustomobject]@{Check='CANDIDATE_MANIFEST_IMMUTABLE';Status=$(if($manifestPass){'PASS'}else{'FAIL'});ExpectedSHA256=$expectedManifestHash;ActualSHA256=$actualManifestHash})

    $attestationPath=[string]$manifest.Build.AttestationPath
    $attestation=Get-Content -LiteralPath $attestationPath -Raw|ConvertFrom-Json
    $attestationHash=Get-Hash $attestationPath
    $attestationPass=($attestationHash -ceq [string]$manifest.Build.AttestationSHA256 -and $attestation.Status -ceq 'BUILD_REPRODUCIBILITY_PASS' -and $attestation.BuildOutputComparison.Status -ceq 'PASS')
    $inputMismatches=[Collections.Generic.List[string]]::new()
    foreach($input in @($attestation.GeneratedCompileInputsA)+@($attestation.GeneratedCompileInputsB)){
        $inputPath=[string]$input.FullPath
        if([string]::IsNullOrWhiteSpace($inputPath) -or -not(Test-Path -LiteralPath $inputPath -PathType Leaf) -or (Get-Hash $inputPath) -cne [string]$input.SHA256){$inputMismatches.Add([string]$input.RelativePath)}
    }
    foreach($asset in @($attestation.PackageAssetsA)+@($attestation.PackageAssetsB)){
        if(-not(Test-Path -LiteralPath $asset.AssetsPath -PathType Leaf) -or (Get-Hash $asset.AssetsPath) -cne [string]$asset.AssetsSHA256){$inputMismatches.Add([string]$asset.AssetsPath)}
    }
    if($inputMismatches.Count -gt 0){$attestationPass=$false}
    $results.Add([pscustomobject]@{Check='BUILD_ATTESTATION_AND_INPUTS';Status=$(if($attestationPass){'PASS'}else{'FAIL'});ExpectedSHA256=$manifest.Build.AttestationSHA256;ActualSHA256=$attestationHash;BuildStatus=$attestation.Status;ReproducibilityStatus=$attestation.BuildOutputComparison.Status;CompileInputCountA=@($attestation.GeneratedCompileInputsA).Count;CompileInputCountB=@($attestation.GeneratedCompileInputsB).Count;PackageAssetCountA=@($attestation.PackageAssetsA).Count;PackageAssetCountB=@($attestation.PackageAssetsB).Count;InputMismatches=$inputMismatches.ToArray()})

    $freezeLogPath=[string]$manifest.Evidence.RuntimeFreezeLog
    $freezeLogHash=Get-Hash $freezeLogPath
    $freezeLogPass=($freezeLogHash -ceq [string]$manifest.Evidence.RuntimeFreezeLogSHA256)
    $results.Add([pscustomobject]@{Check='RUNTIME_FREEZE_LOG_INTEGRITY';Status=$(if($freezeLogPass){'PASS'}else{'FAIL'});Path=$freezeLogPath;ExpectedSHA256=$manifest.Evidence.RuntimeFreezeLogSHA256;ActualSHA256=$freezeLogHash})

    $currentInventoryPath=Join-Path $logs 'candidate-guard-source-current.json'
    & (Join-Path $PSScriptRoot 'New-V22CanonicalSourceInventory.ps1') -RepoRoot $repo -OutputPath $currentInventoryPath|Out-Null
    $currentInventory=Get-Content -LiteralPath $currentInventoryPath -Raw|ConvertFrom-Json
    $savedInventory=Get-Content -LiteralPath $inventoryPath -Raw|ConvertFrom-Json
    $sourcePass=($currentInventory.InventorySHA256 -ceq $manifest.Source.InventorySHA256 -and $currentInventory.InventorySHA256 -ceq $savedInventory.InventorySHA256 -and $currentInventory.EntryCount -eq $savedInventory.EntryCount -and @($currentInventory.MissingInputs).Count -eq 0)
    $snapshotRoot=[string]$manifest.Source.SnapshotRoot;$snapshotMismatch=[Collections.Generic.List[string]]::new()
    foreach($entry in $savedInventory.Entries){$path=Join-Path $snapshotRoot $entry.NormalizedRelativePath.Replace('/','\');if(-not(Test-Path -LiteralPath $path -PathType Leaf)){ $snapshotMismatch.Add($entry.NormalizedRelativePath);continue };if((Get-Hash $path) -cne $entry.SHA256){$snapshotMismatch.Add($entry.NormalizedRelativePath)}}
    if($snapshotMismatch.Count -gt 0){$sourcePass=$false}
    $results.Add([pscustomobject]@{Check='SOURCE_INVENTORY_AND_SNAPSHOT';Status=$(if($sourcePass){'PASS'}else{'FAIL'});ExpectedInventorySHA256=$manifest.Source.InventorySHA256;CurrentInventorySHA256=$currentInventory.InventorySHA256;SnapshotMismatchCount=$snapshotMismatch.Count;SnapshotMismatches=$snapshotMismatch.ToArray()})

    $runtime=Get-RuntimeSnapshot $manifest.Runtime.Root;$savedRuntime=Get-Content -LiteralPath $runtimeInventoryPath -Raw|ConvertFrom-Json
    $runtimeMismatch=[Collections.Generic.List[string]]::new();$expectedRows=@($savedRuntime.Entries);$actualRows=@($runtime.Entries)
    if($runtime.EntryCount -ne $savedRuntime.EntryCount -or $runtime.InventorySHA256 -cne $manifest.Runtime.InventorySHA256 -or $runtime.InventorySHA256 -cne $savedRuntime.InventorySHA256){$runtimeMismatch.Add('INVENTORY_DIGEST_OR_COUNT')}
    $expectedMap=@{};foreach($entry in $expectedRows){$expectedMap[$entry.RelativePath]=$entry.SHA256};foreach($entry in $actualRows){if(-not $expectedMap.ContainsKey($entry.RelativePath) -or $expectedMap[$entry.RelativePath] -cne $entry.SHA256){$runtimeMismatch.Add($entry.RelativePath)}}
    $runtimePass=($runtimeMismatch.Count -eq 0)
    $results.Add([pscustomobject]@{Check='FROZEN_RUNTIME_INVENTORY';Status=$(if($runtimePass){'PASS'}else{'FAIL'});ExpectedInventorySHA256=$savedRuntime.InventorySHA256;ActualInventorySHA256=$runtime.InventorySHA256;ExpectedEntryCount=$savedRuntime.EntryCount;ActualEntryCount=$runtime.EntryCount;Mismatches=$runtimeMismatch.ToArray()})

    $binaryChecks=[Collections.Generic.List[object]]::new()
    foreach($spec in @(@{Name='BenchmarkDll';Path='TKS_Thuc_Tap_V11_Benchmarks_V22.dll'},@{Name='DataAccessDll';Path='TKS_Thuc_Tap_V11_Data_Access.dll'},@{Name='DepsJson';Path='TKS_Thuc_Tap_V11_Benchmarks_V22.deps.json'},@{Name='RuntimeConfigJson';Path='TKS_Thuc_Tap_V11_Benchmarks_V22.runtimeconfig.json'})){
        $actual=Get-Hash (Join-Path $manifest.Runtime.Root $spec.Path);$expected=[string]$manifest.Binary.PSObject.Properties[$spec.Name].Value.SHA256
        $binaryChecks.Add([pscustomobject]@{Name=$spec.Name;ExpectedSHA256=$expected;ActualSHA256=$actual;Match=($actual -ceq $expected)})
    }
    $binaryPass=(@($binaryChecks|Where-Object{-not $_.Match}).Count -eq 0)
    $buildOutputMismatches=[Collections.Generic.List[string]]::new()
    foreach($build in @(@{Name='A';Rows=@($attestation.BuildOutputComparison.BuildAOutputs)},@{Name='B';Rows=@($attestation.BuildOutputComparison.BuildBOutputs)})){
        foreach($row in $build.Rows){if(-not(Test-Path -LiteralPath $row.FullPath -PathType Leaf) -or (Get-Hash $row.FullPath) -cne [string]$row.SHA256){$buildOutputMismatches.Add($build.Name+':'+[string]$row.RelativePath)}}
    }
    if($buildOutputMismatches.Count -gt 0){$attestationPass=$false}
    $results.Add([pscustomobject]@{Check='BUILD_OUTPUTS_RETAINED';Status=$(if($buildOutputMismatches.Count -eq 0){'PASS'}else{'FAIL'});ExpectedOutputCount=@($attestation.BuildOutputComparison.BuildAOutputs).Count;BuildBOutputCount=@($attestation.BuildOutputComparison.BuildBOutputs).Count;Mismatches=$buildOutputMismatches.ToArray()})
    $results.Add([pscustomobject]@{Check='BUILD_BINARY_TO_RUNTIME_HASHES';Status=$(if($binaryPass){'PASS'}else{'FAIL'});Files=$binaryChecks.ToArray()})

    $proof=Get-Content -LiteralPath $proofPath -Raw|ConvertFrom-Json
    $preservationMismatches=[Collections.Generic.List[object]]::new()
    foreach($immutable in $proof.ImmutableRoots){
        $post=Capture-Tree ([string]$immutable.Path)
        $same=($post.TreeSHA256 -ceq $immutable.TreeSHA256 -and $post.FileCount -eq $immutable.FileCount -and $post.ByteCount -eq $immutable.ByteCount)
        Add-Member -InputObject $immutable -NotePropertyName PostKind -NotePropertyValue $post.Kind
        Add-Member -InputObject $immutable -NotePropertyName PostFileCount -NotePropertyValue $post.FileCount
        Add-Member -InputObject $immutable -NotePropertyName PostByteCount -NotePropertyValue $post.ByteCount
        Add-Member -InputObject $immutable -NotePropertyName PostTreeSHA256 -NotePropertyValue $post.TreeSHA256
        Add-Member -InputObject $immutable -NotePropertyName Equal -NotePropertyValue ([bool]$same)
        Add-Member -InputObject $immutable -NotePropertyName PostEntries -NotePropertyValue $post.Entries
        if(-not $same){$preservationMismatches.Add([pscustomobject]@{Path=$immutable.Path;PreTreeSHA256=$immutable.TreeSHA256;PostTreeSHA256=$post.TreeSHA256;PreFileCount=$immutable.FileCount;PostFileCount=$post.FileCount})}
    }
    $proof.Status=if($preservationMismatches.Count -eq 0){'PASS_MATCH'}else{'FAIL_MISMATCH'}
    Add-Member -InputObject $proof -NotePropertyName PostCapturedUtc -NotePropertyValue ([DateTime]::UtcNow.ToString('o'))
    Add-Member -InputObject $proof -NotePropertyName Mismatches -NotePropertyValue $preservationMismatches.ToArray()
    $proofJson=$proof|ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText($proofPath,$proofJson+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
    $preservationPass=($proof.Status -ceq 'PASS_MATCH')
    $results.Add([pscustomobject]@{Check='V21_PRESERVATION_PRE_POST';Status=$(if($preservationPass){'PASS'}else{'FAIL'});ImmutableRootCount=$proof.ImmutableRoots.Count;ImmutableFileCount=($proof.ImmutableRoots|Measure-Object FileCount -Sum).Sum;Mismatches=$preservationMismatches.ToArray()})

    $overall=(@($results|Where-Object Status -ceq 'FAIL').Count -eq 0)
    $guard=[ordered]@{SchemaVersion='warehouse-benchmark-v22-candidate-guard/1';CandidateId=$CandidateId;ProtocolVersion='2.2';Status=$(if($overall){'PASS'}else{'FAIL'});CandidateStatus=$(if($overall){'CANDIDATE_BUILT_AND_ATTESTED'}else{'CANDIDATE_INVALID'});ManifestSHA256=$actualManifestHash;GuardLogPath=$guardLogPath;Checks=$results.ToArray();RecordedUtc=[DateTime]::UtcNow.ToString('o')}
    foreach($check in $results){Add-GuardLog ("Check={0};Status={1}" -f $check.Check,$check.Status)}
    Add-GuardLog ("GuardStatus={0};CandidateStatus={1};ManifestSHA256={2}" -f $guard.Status,$guard.CandidateStatus,$actualManifestHash)
    Write-NewJson $guardPath $guard
    [pscustomobject]@{CandidateId=$CandidateId;Status=$guard.Status;CandidateStatus=$guard.CandidateStatus;ManifestSHA256=$actualManifestHash;GuardLogPath=$guardLogPath}
    if(-not $overall){exit 1}
}catch{
    try{Add-GuardLog ("GuardStatus=FAIL;FailureStage=GUARD;Message={0}" -f ($_.Exception.Message -replace '[\r\n]+',' '))}catch{}
    $guard=[ordered]@{SchemaVersion='warehouse-benchmark-v22-candidate-guard/1';CandidateId=$CandidateId;ProtocolVersion='2.2';Status='FAIL';CandidateStatus='CANDIDATE_INVALID';FailureStage='GUARD';FailureMessage=($_.Exception.Message -replace '(?i)(password|pwd|token|secret)\s*=\s*[^;,\s]+','$1=[REDACTED]');Checks=$results.ToArray();RecordedUtc=[DateTime]::UtcNow.ToString('o')}
    if(-not(Test-Path -LiteralPath $guardPath)){Write-NewJson $guardPath $guard}
    throw
}
