[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RepoRoot,
    [Parameter(Mandatory=$true)][string]$HistoricalBlockRoot,
    [Parameter(Mandatory=$true)][string]$EvidenceRoot
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $EvidenceRoot){throw 'V22_BDN_REPLAY_EVIDENCE_ROOT_EXISTS'}
$runnerPath=Join-Path $RepoRoot 'benchmarks\v2.2\run-warehouse-benchmark-v2.2-performance.ps1'
$harnessPath=Join-Path $RepoRoot 'benchmarks\v2.2\WarehouseBenchmarkV22.Harness.psm1'
$parserErrors=$null;$tokens=$null;$ast=[System.Management.Automation.Language.Parser]::ParseFile($runnerPath,[ref]$tokens,[ref]$parserErrors)
if($parserErrors.Count -gt 0){throw 'V22_BDN_REPLAY_RUNNER_PARSE_FAILED'}
$requiredFunctions=@('ConvertTo-FiniteDouble','Convert-BdnQuantityToNs','Get-BdnPrintedRoundingToleranceNs','Get-BdnArtifact','Get-FileHashHex','Write-NewText')
foreach($name in $requiredFunctions){
    $node=$ast.Find({param($n)$n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $name},$false)
    if($null -eq $node){throw ('V22_BDN_REPLAY_FUNCTION_MISSING:'+ $name)}
    Invoke-Expression $node.Extent.Text
}
Import-Module -Name $harnessPath -Force
$rawOutput=Join-Path $HistoricalBlockRoot 'bdn-output'
if(-not(Test-Path -LiteralPath $rawOutput -PathType Container)){throw 'V22_BDN_REPLAY_RAW_OUTPUT_MISSING'}
$sourceStdout=@(Get-ChildItem -LiteralPath $HistoricalBlockRoot -File -Filter '*-bdn.stdout.txt')
if($sourceStdout.Count -ne 1){throw 'V22_BDN_REPLAY_STDOUT_IDENTITY_INVALID'}
$sourceFiles=@(Get-ChildItem -LiteralPath $rawOutput -File -Recurse | Where-Object {$_.Extension -ceq '.log' -or $_.Name -like '*report.csv'})
if(@($sourceFiles|Where-Object Name -like '*report.csv').Count -ne 1 -or @($sourceFiles|Where-Object Extension -ceq '.log').Count -lt 1){throw 'V22_BDN_REPLAY_RAW_ARTIFACT_SET_INVALID'}
$sourceEvidence=@(foreach($file in @($sourceFiles)+@($sourceStdout)){[pscustomobject]@{Path=$file.FullName;Length=$file.Length;SHA256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
New-Item -ItemType Directory -Path $EvidenceRoot | Out-Null
function New-CaseFixture([string]$TestId){
    $caseRoot=Join-Path $EvidenceRoot $TestId
    $caseOutput=Join-Path $caseRoot 'bdn-output'
    New-Item -ItemType Directory -Path $caseOutput -Force | Out-Null
    foreach($file in $sourceFiles){
        $relative=[IO.Path]::GetRelativePath($rawOutput,$file.FullName)
        $destination=Join-Path $caseOutput $relative
        $parent=Split-Path -Parent $destination
        if(-not(Test-Path -LiteralPath $parent)){New-Item -ItemType Directory -Path $parent -Force|Out-Null}
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
    $stdout=Join-Path $caseRoot 'fixture-bdn.stdout.txt'
    Copy-Item -LiteralPath $sourceStdout[0].FullName -Destination $stdout
    return [pscustomobject]@{Root=$caseRoot;Output=$caseOutput;Stdout=$stdout;Log=(Get-ChildItem -LiteralPath $caseOutput -File -Recurse -Filter '*.log'|Select-Object -First 1).FullName;Summary=(Get-ChildItem -LiteralPath $caseOutput -File -Recurse -Filter '*report.csv'|Select-Object -First 1).FullName}
}
$results=[Collections.Generic.List[object]]::new()
function Invoke-ReplayCase([string]$TestId,[string]$Expected,[scriptblock]$Mutate){
    $fixture=New-CaseFixture $TestId
    if($null -ne $Mutate){& $Mutate $fixture}
    $actual='';$details=$null;$failureMessage=$null
    try{
        $evidence=Get-BdnArtifact $fixture.Output $fixture.Stdout
        $actual='{0}|N={1}|Actual={2}|Result={3}' -f $evidence.Status,$evidence.MeasuredIterations,$evidence.ActualMeasurementRows,$evidence.ResultMeasurementRows
        $details=$evidence
    }catch{
        $message=$_.Exception.Message;$failureMessage=($message -replace '\s+',' ')
        if($message -match '^(V22_BDN_INVALID)\|'){$actual=$Matches[1]}else{$actual='UNCLASSIFIED:'+($message -replace '\s+',' ')}
    }
    $status=if($actual-ceq$Expected){'PASS'}else{'FAIL'}
    $results.Add([pscustomobject]@{TestId=$TestId;Input=@{HistoricalRawBdnFixture=$HistoricalBlockRoot;Mutation=if($null-ne$Mutate){$TestId}else{'NONE'}};Expected=$Expected;Actual=$actual;Status=$status;EvidencePath=(Join-Path $fixture.Root 'case-evidence.json');Details=$details;FailureMessage=$failureMessage})
}
Invoke-ReplayCase 'BDN_HISTORICAL_DETAILREPORTPAGED_REPLAY' 'BDN_COMPLETE|N=5|Actual=5|Result=5' $null
Invoke-ReplayCase 'BDN_WORKLOADRESULT_MISSING' 'V22_BDN_INVALID' {
    param($f)
    $text=[IO.File]::ReadAllText($f.Log)
    $changed=[regex]::Replace($text,'(?m)^.*WorkloadResult\s+5:.*(?:\r?\n|$)','')
    if($changed-ceq$text){throw 'fixture mutation did not match WorkloadResult row'}
    [IO.File]::WriteAllText($f.Log,$changed,[Text.UTF8Encoding]::new($false))
}
Invoke-ReplayCase 'BDN_REPORTED_N_MISMATCH' 'V22_BDN_INVALID' {
    param($f)
    $text=[IO.File]::ReadAllText($f.Log)
    $changed=[regex]::Replace($text,'(?i)\bN\s*=\s*5\b','N = 4')
    if($changed-ceq$text){throw 'fixture mutation did not match raw N'}
    [IO.File]::WriteAllText($f.Log,$changed,[Text.UTF8Encoding]::new($false))
}
Invoke-ReplayCase 'BDN_ACTUAL_RESULT_ITERATION_MISMATCH' 'V22_BDN_INVALID' {
    param($f)
    $text=[IO.File]::ReadAllText($f.Log)
    $changed=$text.Replace('WorkloadResult   5: 1 op','WorkloadResult   5: 2 op')
    if($changed-ceq$text){throw 'fixture mutation did not match final result iteration'}
    [IO.File]::WriteAllText($f.Log,$changed,[Text.UTF8Encoding]::new($false))
}
Invoke-ReplayCase 'BDN_SUMMARY_MEAN_INVALID' 'V22_BDN_INVALID' {
    param($f)
    $text=[IO.File]::ReadAllText($f.Summary)
    $summaryRows=@(Import-Csv -LiteralPath $f.Summary)
    if($summaryRows.Count-ne 1 -or -not($summaryRows[0].PSObject.Properties.Name -contains 'Mean')){throw 'fixture summary Mean field is missing or ambiguous'}
    $mean=[string]$summaryRows[0].Mean
    if([string]::IsNullOrWhiteSpace($mean)){throw 'fixture summary Mean value is empty'}
    $changed=[regex]::Replace($text,','+[regex]::Escape($mean)+',',',NaN ms,',1)
    if($changed-ceq$text){throw 'fixture mutation did not match rounded summary Mean'}
    [IO.File]::WriteAllText($f.Summary,$changed,[Text.UTF8Encoding]::new($false))
}
Invoke-ReplayCase 'BDN_UPPER_FENCE_MISMATCH' 'V22_BDN_INVALID' {
    param($f)
    $text=[IO.File]::ReadAllText($f.Log)
    $changed=[regex]::Replace($text,'(?i)(UpperFence\s*=\s*)[-+]?(?:\d+(?:\.\d*)?|\.\d+)\s*ms','$1 0 ms')
    if($changed-ceq$text){throw 'fixture mutation did not match upper fence'}
    [IO.File]::WriteAllText($f.Log,$changed,[Text.UTF8Encoding]::new($false))
}
foreach($row in $results){$casePath=Join-Path $EvidenceRoot $row.TestId 'case-evidence.json';$json=$row|ConvertTo-Json -Depth 8;[IO.File]::WriteAllText($casePath,$json+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))}
$runnerHash=(Get-FileHash -LiteralPath $runnerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$summary=[ordered]@{SchemaVersion='warehouse-benchmark-v22-bdn-replay-tests/1';TestSuite='OFFLINE_RAW_BDN_REPLAY';DatabaseAccess='NOT_USED';PerformanceWorkload='NOT_RUN';HistoricalBlockRoot=$HistoricalBlockRoot;SourceArtifactHashes=$sourceEvidence;RunnerSHA256=$runnerHash;Passed=@($results|Where-Object Status -ceq 'PASS').Count;Failed=@($results|Where-Object Status -ceq 'FAIL').Count;TestCount=$results.Count;Status=if(@($results|Where-Object Status -cne 'PASS').Count-eq 0){'PASS'}else{'FAIL'};Results=$results.ToArray()}
$summaryPath=Join-Path $EvidenceRoot 'V22-BDN-Replay-Test-Results.json'
[IO.File]::WriteAllText($summaryPath,($summary|ConvertTo-Json -Depth 12)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
$summary
if($summary.Status-cne'PASS'){exit 1}
