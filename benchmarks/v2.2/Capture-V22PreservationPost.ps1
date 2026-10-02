[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$BatchRoot)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($BatchRoot)
$review=Join-Path $root 'Batch3-Review-Pack'
$logs=Join-Path $root 'logs'
$prePath=Join-Path $logs 'preservation-pre-files.json'
$preSummaryPath=Join-Path $logs 'Previous-Evidence-Preservation-Prebatch3-Summary.json'
$b1='P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_2_BATCH1_WHB22-20260930-65EA9FB0\Batch1-Review-Pack\V21-Preservation-Proof.json'
$prior='P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_2_BATCH2_RESTORED_CONTINUATION_20261001_01\Batch2-Restored-Continuation-Review-Pack\Previous-Evidence-Integrity.json'
$priorFinal=Join-Path 'P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_2_BATCH2_FINAL_CLOSURE_20261001_01' 'Batch2-Final-Review-Pack\Previous-Evidence-Integrity-Final.json'
$batch2Root=Split-Path -Parent $priorFinal
$postDetailPath=Join-Path $logs 'preservation-post-files.json'
$reviewPath=Join-Path $review 'Previous-Evidence-Preservation.json'
function Get-Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Get-Entries([string]$Path){
    $base=[IO.Path]::GetFullPath($Path)
    if(Test-Path -LiteralPath $base -PathType Leaf){$file=Get-Item -LiteralPath $base;return @([pscustomobject]@{RelativePath=$file.Name;Size=[long]$file.Length;SHA256=(Get-Hash $base)})}
    if(-not(Test-Path -LiteralPath $base -PathType Container)){return @()}
    $prefix=$base.TrimEnd('\')+'\'
    return @(Get-ChildItem -LiteralPath $base -File -Recurse -Force|Sort-Object FullName|ForEach-Object{$relative=$_.FullName.Substring($prefix.Length).Replace('\','/');[pscustomobject]@{RelativePath=$relative;Size=[long]$_.Length;SHA256=(Get-Hash $_.FullName)}})
}
function Get-TreeHash([object[]]$Entries){$tab=[string][char]9;$canonical=(@($Entries|Sort-Object RelativePath -CaseSensitive|ForEach-Object{[string]::Join($tab,@($_.RelativePath,[string]$_.Size,$_.SHA256))}) -join [Environment]::NewLine)+[Environment]::NewLine;[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical))).ToLowerInvariant()}
function Get-PackCheck([object]$Pack){
    $mismatches=[Collections.Generic.List[string]]::new()
    foreach($file in @($Pack.Files)){if(-not(Test-Path -LiteralPath $file.Path -PathType Leaf)){$mismatches.Add('MISSING:'+ $file.Path)}elseif((Get-Hash $file.Path) -cne ([string]$file.SHA256).ToLowerInvariant()){$mismatches.Add('HASH:'+ $file.Path)}}
    if($Pack.ZipPath){if(-not(Test-Path -LiteralPath $Pack.ZipPath -PathType Leaf)){$mismatches.Add('MISSING_ZIP:'+ $Pack.ZipPath)}elseif((Get-Hash $Pack.ZipPath) -cne ([string]$Pack.ZipSHA256).ToLowerInvariant()){$mismatches.Add('ZIP_HASH:'+ $Pack.ZipPath)}}
    return [pscustomobject]@{Name=$Pack.Name;PackRoot=$Pack.PackRoot;FilesExpected=@($Pack.Files).Count;FilesVerified=@($Pack.Files).Count-$mismatches.Count;ZipPath=$Pack.ZipPath;ExpectedZipSHA256=$Pack.ZipSHA256;Mismatches=$mismatches.ToArray();Status=$(if($mismatches.Count -eq 0){'PASS'}else{'MISMATCH'})}
}
if((Test-Path -LiteralPath $postDetailPath) -or (Test-Path -LiteralPath $reviewPath)){throw 'Post-preservation evidence path already exists'}
$pre=Get-Content -LiteralPath $prePath -Raw|ConvertFrom-Json
$preSummary=Get-Content -LiteralPath $preSummaryPath -Raw|ConvertFrom-Json
$previous=Get-Content -LiteralPath $prior -Raw|ConvertFrom-Json
$previousFinal=Get-Content -LiteralPath $priorFinal -Raw|ConvertFrom-Json
$b1Proof=Get-Content -LiteralPath $b1 -Raw|ConvertFrom-Json
$v21Rows=[Collections.Generic.List[object]]::new()
$v21Mismatches=[Collections.Generic.List[object]]::new()
foreach($expectedRoot in @($pre.V21Roots)){
    $actual=Get-Entries $expectedRoot.Path
    $expected=@($pre.V21Entries|Where-Object{$_.Group -eq 'V21' -and $_.Root -eq $expectedRoot.Path})
    $expectedMap=@{};foreach($entry in $expected){$expectedMap[$entry.RelativePath]=$entry}
    $actualMap=@{};foreach($entry in $actual){$actualMap[$entry.RelativePath]=$entry}
    foreach($entry in $expected){if(-not $actualMap.ContainsKey($entry.RelativePath)){$v21Mismatches.Add([pscustomobject]@{Root=$expectedRoot.Path;Path=$entry.RelativePath;Kind='MISSING'})}elseif($actualMap[$entry.RelativePath].Size -ne $entry.Size -or $actualMap[$entry.RelativePath].SHA256 -cne $entry.SHA256){$v21Mismatches.Add([pscustomobject]@{Root=$expectedRoot.Path;Path=$entry.RelativePath;Kind='HASH_OR_SIZE'})}}
    foreach($entry in $actual){if(-not $expectedMap.ContainsKey($entry.RelativePath)){$v21Mismatches.Add([pscustomobject]@{Root=$expectedRoot.Path;Path=$entry.RelativePath;Kind='UNEXPECTED'})}}
    $tree=Get-TreeHash $actual
    $rootMismatches=@($v21Mismatches|Where-Object Root -ceq $expectedRoot.Path)
    $equal=($expectedRoot.ExpectedFileCount -eq $actual.Count -and $rootMismatches.Count -eq 0)
    $v21Rows.Add([pscustomobject]@{Path=$expectedRoot.Path;ExpectedFileCount=$expectedRoot.ExpectedFileCount;ActualFileCount=$actual.Count;PreCapturedExpectedTreeSHA256=$expectedRoot.ExpectedTreeSHA256;PreCapturedActualTreeSHA256=$expectedRoot.ActualTreeSHA256;PostCalculatedTreeSHA256=$tree;TreeHashComparison='NOT_USED_UNVERIFIED_ALGORITHM';EqualityMethod='PER_FILE_RELATIVE_PATH_SIZE_SHA256';Equal=$equal;Mismatches=$rootMismatches})
}
$packRows=[Collections.Generic.List[object]]::new()
foreach($pack in @($previous.Packs)){$packRows.Add((Get-PackCheck $pack))}
if($previousFinal.RestoredContinuationPack){$restored=$previousFinal.RestoredContinuationPack;$packRows.Add((Get-PackCheck ([pscustomobject]@{Name='RestoredContinuation';PackRoot=$restored.PackRoot;Files=$restored.Files;ZipPath=$restored.ZipPath;ZipSHA256=$restored.ZipSHA256})))}
$batch2EntriesExpected=@($pre.Batch2FinalRoot.Entries)
$batch2EntriesActual=Get-Entries $pre.Batch2FinalRoot.Path
$batch2ExpectedMap=@{};foreach($entry in $batch2EntriesExpected){$batch2ExpectedMap[$entry.RelativePath]=$entry}
$batch2Mismatches=[Collections.Generic.List[object]]::new()
$batch2ActualMap=@{};foreach($entry in $batch2EntriesActual){$batch2ActualMap[$entry.RelativePath]=$entry;if(-not $batch2ExpectedMap.ContainsKey($entry.RelativePath)){$batch2Mismatches.Add([pscustomobject]@{Path=$entry.RelativePath;Kind='UNEXPECTED'})}elseif($entry.Size -ne $batch2ExpectedMap[$entry.RelativePath].Size -or $entry.SHA256 -cne $batch2ExpectedMap[$entry.RelativePath].SHA256){$batch2Mismatches.Add([pscustomobject]@{Path=$entry.RelativePath;Kind='HASH_OR_SIZE'})}}
foreach($entry in $batch2EntriesExpected){if(-not $batch2ActualMap.ContainsKey($entry.RelativePath)){$batch2Mismatches.Add([pscustomobject]@{Path=$entry.RelativePath;Kind='MISSING'})}}
$batch2Tree=Get-TreeHash $batch2EntriesActual
$batch2Equal=($batch2EntriesActual.Count -eq $pre.Batch2FinalRoot.FileCount -and $batch2Mismatches.Count -eq 0)
$priorHash=Get-Hash $prior;$priorFinalHash=Get-Hash $priorFinal
$preManifestHash=Get-Hash $prePath
$summaryHash=Get-Hash $preSummaryPath
$overall=($preSummary.Status -ceq 'PRE_CAPTURED_PASS' -and $preSummary.PreManifestSHA256 -ceq $preManifestHash -and $v21Mismatches.Count -eq 0 -and @($v21Rows|Where-Object{-not $_.Equal}).Count -eq 0 -and @($packRows|Where-Object Status -ne 'PASS').Count -eq 0 -and $batch2Equal -and $priorHash -ceq $pre.PriorIntegritySHA256 -and $priorFinalHash -ceq $pre.PriorFinalIntegritySHA256)
$detail=[ordered]@{SchemaVersion='warehouse-benchmark-v22-batch3-preservation-post/2';CapturedUtc=[DateTime]::UtcNow.ToString('o');IdentityComparisonMethod='PER_FILE_RELATIVE_PATH_SIZE_SHA256';TreeHashComparison='NOT_USED_UNVERIFIED_ALGORITHM';PreManifestPath=$prePath;PreManifestSHA256=$preManifestHash;PreSummaryPath=$preSummaryPath;PreSummarySHA256=$summaryHash;V21Roots=$v21Rows.ToArray();V21Mismatches=$v21Mismatches.ToArray();PreviousPacks=$packRows.ToArray();Batch2FinalRoot=[ordered]@{Path=$pre.Batch2FinalRoot.Path;ExpectedFileCount=$pre.Batch2FinalRoot.FileCount;ActualFileCount=$batch2EntriesActual.Count;PreCapturedTreeSHA256=$pre.Batch2FinalRoot.TreeSHA256;PostCalculatedTreeSHA256=$batch2Tree;TreeHashComparison='NOT_USED_UNVERIFIED_ALGORITHM';EqualityMethod='PER_FILE_RELATIVE_PATH_SIZE_SHA256';Equal=$batch2Equal;Mismatches=$batch2Mismatches.ToArray()};PriorIntegrityPath=$prior;PriorIntegrityExpectedSHA256=$pre.PriorIntegritySHA256;PriorIntegrityActualSHA256=$priorHash;PriorFinalIntegrityPath=$priorFinal;PriorFinalIntegrityExpectedSHA256=$pre.PriorFinalIntegritySHA256;PriorFinalIntegrityActualSHA256=$priorFinalHash;Status=$(if($overall){'POST_PRESERVATION_PASS'}else{'POST_PRESERVATION_MISMATCH'})}
$detailJson=$detail|ConvertTo-Json -Depth 10
[IO.File]::WriteAllText($postDetailPath,$detailJson,[Text.UTF8Encoding]::new($false))
$proof=[ordered]@{SchemaVersion='warehouse-benchmark-v22-batch3-preservation/1';Status=$(if($overall){'PASS'}else{'FAIL'});PRE=[ordered]@{CapturedUtc=$pre.CapturedUtc;ManifestPath=$prePath;ManifestSHA256=$preManifestHash;SummaryPath=$preSummaryPath;SummarySHA256=$summaryHash;V21RootCount=@($pre.V21Roots).Count;V21ExpectedEntryCount=@($pre.V21Entries|Where-Object Group -eq 'V21').Count;MismatchCount=@($pre.V21Mismatches).Count};POST=[ordered]@{CapturedUtc=$detail.CapturedUtc;ManifestPath=$postDetailPath;ManifestSHA256=(Get-Hash $postDetailPath);V21RootCount=$v21Rows.Count;V21EqualRootCount=@($v21Rows|Where-Object Equal).Count;V21MismatchCount=$v21Mismatches.Count;PreviousPackCount=$packRows.Count;PreviousPacksPass=(@($packRows|Where-Object Status -ne 'PASS').Count -eq 0);Batch2FinalRoot=$detail.Batch2FinalRoot};Immutable2_1Roots=$v21Rows.ToArray();PreviousEvidencePacks=$packRows.ToArray();PriorIntegrity=[ordered]@{Path=$prior;ExpectedSHA256=$pre.PriorIntegritySHA256;ActualSHA256=$priorHash;Equal=($priorHash -ceq $pre.PriorIntegritySHA256)};PriorFinalIntegrity=[ordered]@{Path=$priorFinal;ExpectedSHA256=$pre.PriorFinalIntegritySHA256;ActualSHA256=$priorFinalHash;Equal=($priorFinalHash -ceq $pre.PriorFinalIntegritySHA256)};DetailsPath=$postDetailPath;DetailsSHA256=(Get-Hash $postDetailPath);CompletedUtc=[DateTime]::UtcNow.ToString('o')}
$proofJson=$proof|ConvertTo-Json -Depth 8
$stream=[IO.File]::Open($reviewPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($proofJson+[Environment]::NewLine);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
$proof|Select-Object Status,@{n='V21Roots';e={$_.Immutable2_1Roots.Count}},@{n='V21Mismatches';e={$_.POST.V21MismatchCount}},@{n='PreviousPacksPass';e={$_.POST.PreviousPacksPass}},@{n='Batch2FinalTreeSHA256';e={$_.POST.Batch2FinalRoot.PostCalculatedTreeSHA256}}
if(-not $overall){exit 1}
