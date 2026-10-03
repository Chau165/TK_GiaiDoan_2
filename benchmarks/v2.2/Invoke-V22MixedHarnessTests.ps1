[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EvidencePath)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'WarehouseBenchmarkV22.Finalizer.psm1') -Force
$parent=Split-Path -Parent $EvidencePath
if(-not(Test-Path -LiteralPath $parent -PathType Container)){if(Test-Path -LiteralPath $parent){throw 'MIXED_TEST_EVIDENCE_PARENT_NOT_DIRECTORY'};New-Item -ItemType Directory -Path $parent|Out-Null}
$fixtureRoot=Join-Path $parent 'Mixed-Harness-Fixtures'
if(Test-Path -LiteralPath $fixtureRoot){throw 'MIXED_FIXTURE_ROOT_EXISTS_NO_OVERWRITE'}
New-Item -ItemType Directory -Path $fixtureRoot|Out-Null
$checks=[Collections.Generic.List[object]]::new()
function Add-Case([string]$Id,[object]$InputValue,[string]$Expected,[scriptblock]$Action){
    try{$actual=[string](& $Action)}catch{$actual='THREW:'+ $_.Exception.Message}
    $checks.Add([pscustomobject]@{TestId=$Id;Input=$InputValue;Expected=$Expected;Actual=$actual;Status=if($actual-ceq$Expected){'PASS'}else{'FAIL'};EvidencePath=$EvidencePath})
}
function Get-PropertyMultiplicity([object]$Value,[string]$Name){@($Value.PSObject.Properties|Where-Object Name -CEQ $Name).Count}
$runId='WHB22-PERF-RUN-MIXED-FIXTURE-001';$candidateId='WHB22-PERF-MIXED-FIXTURE-001';$snapshotId='V22SRC-MIXED-FIXTURE-001'
$scenarios=@('MasterPaged','LookupPaged','DocumentPaged','DetailReportPaged','InventoryHistoricalReportPaged','InventoryCurrentBalancePaged')
$levelCopies=@{L1=1;L2=2;L4=4;L8=8}
$levelResults=[ordered]@{};$levelGates=[ordered]@{};$persistedResults=[ordered]@{};$persistedGates=[ordered]@{};$baseUtc=[DateTimeOffset]::Parse('2026-10-02T12:00:00Z')
foreach($level in @('L1','L2','L4','L8')){
    $copies=$levelCopies[$level];$sem=New-V22MixedSemantics $level
    $configs=[Collections.Generic.List[object]]::new()
    foreach($scenario in $scenarios){$block=$runId+'-MIXED-'+$level+'-'+$scenario;$configs.Add((New-V22MixedWorkerConfiguration $runId $candidateId $snapshotId $level $block $scenario 3 15))}
    $actualMultiplicity=0
    foreach($config in $configs){foreach($field in @('ScenarioProcessCount','CopiesPerScenario','TotalLogicalCopies')){$actualMultiplicity+=[Math]::Abs((Get-PropertyMultiplicity $config $field)-1)}}
    $expectedCounts='{0}/{1}/{2}'-f$sem.ScenarioProcessCount,$sem.CopiesPerScenario,$sem.TotalLogicalCopies
    $fixtureRows=[Collections.Generic.List[object]]::new()
    for($i=0;$i-lt$scenarios.Count;$i++){
        $config=$configs[$i];$start=$baseUtc.AddSeconds($i*0.2);$stop=$start.AddSeconds(15)
        $terminal=[pscustomobject]@{SchemaVersion='warehouse-benchmark-v22-worker-terminal/1';RunId=$runId;CandidateId=$candidateId;Level=$level;Scenario=$config.Scenario;BlockId=$config.BlockId;Status='COMPLETED';ObservedCopies=$copies;ObservedInstanceNumbers=@(1..$copies);TimedWindowCompleted=$true;ProcessExitCode=0;MeasuredStartUtc=$start.ToString('o');MeasuredStopUtc=$stop.ToString('o')}
        $worker=[ordered]@{RunId=$runId;CandidateId=$candidateId;SourceSnapshotId=$snapshotId;Level=$level;Scenario=$config.Scenario;BlockId=$config.BlockId;ScenarioProcessCount=$sem.ScenarioProcessCount;CopiesPerScenario=$sem.CopiesPerScenario;TotalLogicalCopies=$sem.TotalLogicalCopies;Status='PASS';ObservedCopies=$terminal.ObservedCopies;ObservedInstanceNumbers=$terminal.ObservedInstanceNumbers;WorkerTerminalStatus=$terminal.Status;WorkerTimedWindowCompleted=$terminal.TimedWindowCompleted;ProcessExitCode=$terminal.ProcessExitCode;MeasuredStartUtc=$terminal.MeasuredStartUtc;MeasuredStopUtc=$terminal.MeasuredStopUtc;Requests=10*$copies;Failed=0;RPS=20.0;MeanMs=1.0;P95Ms=2.0;P99Ms=3.0}
        $fixtureRows.Add((New-V22MixedWorkerProjection $worker $level))
    }
    $aggregate=Get-V22MixedWorkerAggregate $runId $candidateId $level $scenarios $fixtureRows.ToArray() $fixtureRows.Count 10
    $recordSource=[ordered]@{RunId=$runId;CandidateId=$candidateId;SourceSnapshotId=$snapshotId;Level=$level;LevelId=$runId+'-MIXED-'+$level;Status='PASS';FailureType=$null;FailureReason=$null;StartedUtc=$baseUtc.ToString('o');FinishedUtc=$baseUtc.AddSeconds(18).ToString('o');WorkerRows=$fixtureRows.ToArray();TelemetryStatus='TELEMETRY_VALID';CleanupStatus='PASS'}
    foreach($key in $aggregate.Keys){$recordSource[$key]=$aggregate[$key]}
    $record=New-V22MixedResultRecord $recordSource $runId $candidateId $level
    $gate=Get-V22MixedResultGate $record 10
    $levelResults[$level]=$record;$levelGates[$level]=$gate
    $levelEvidencePath=Join-Path $fixtureRoot ('integration-'+$level+'.json')
    New-V22FinalizerJson $levelEvidencePath $record
    $persistedRecord=Get-Content -LiteralPath $levelEvidencePath -Raw|ConvertFrom-Json
    $persistedGate=Get-V22MixedResultGate $persistedRecord 10
    $persistedResults[$level]=$persistedRecord;$persistedGates[$level]=$persistedGate
    $actualCounts='{0}/{1}/{2}'-f$record.ScenarioProcessCount,$record.CopiesPerScenario,$record.TotalLogicalCopies
    $observedCounts='{0}/{1}/{2}'-f$record.ObservedScenarioProcessCount,$record.ObservedTotalLogicalCopies,$record.OverlapStatus
    Add-Case ('META_'+$level) @{Level=$level;Expected=$expectedCounts} ($expectedCounts+'; multiplicity='+($configs.Count*3)) {if($actualMultiplicity-eq 0-and(Get-PropertyMultiplicity $record 'ScenarioProcessCount')-eq 1-and(Get-PropertyMultiplicity $record 'CopiesPerScenario')-eq 1-and(Get-PropertyMultiplicity $record 'TotalLogicalCopies')-eq 1){$actualCounts+'; multiplicity='+($configs.Count*3)}else{'FAIL'}}
    Add-Case ('INTEGRATION_'+$level) @{Level=$level;RunId=$runId;CandidateId=$candidateId;SixScenarioFixture=$true} ('PASS|'+$expectedCounts+'|6/'+$sem.TotalLogicalCopies+'|PASS|PASS') {if($gate.SchemaStatus-eq'PASS'-and$gate.IsMeasurementPass-and$record.RunId-ceq$runId-and$record.CandidateId-ceq$candidateId-and$record.ObservedScenarioProcessCount-eq 6-and$record.ObservedTotalLogicalCopies-eq$sem.TotalLogicalCopies){'PASS|'+$actualCounts+'|'+$record.ObservedScenarioProcessCount+'/'+$record.ObservedTotalLogicalCopies+'|'+$gate.OverlapStatus+'|'+$gate.SchemaStatus}else{'FAIL|'+$observedCounts+'|'+$gate.SchemaStatus+'|'+($gate.Errors -join ',')}}
    Add-Case ('FINALIZER_PERSISTENCE_'+$level) @{Level=$level;EvidencePath=$levelEvidencePath} 'PASS|PASS|6' {if($persistedGate.SchemaStatus-eq'PASS'-and$persistedGate.IsMeasurementPass-and$persistedRecord.WorkerRows.Count-eq 6){'PASS|PASS|6'}else{'FAIL|'+$persistedGate.SchemaStatus+'|'+$persistedRecord.WorkerRows.Count}}
}
$integratedProjection=[ordered]@{SchemaVersion='warehouse-benchmark-v22-mixed-fixture-projection/1';RunId=$runId;CandidateId=$candidateId;SourceSnapshotId=$snapshotId;Levels=$persistedResults}
$integratedProjectionPath=Join-Path $fixtureRoot 'integrated-mixed-projection.json'
New-V22FinalizerJson $integratedProjectionPath $integratedProjection
$integratedReadback=Get-Content -LiteralPath $integratedProjectionPath -Raw|ConvertFrom-Json
$integratedGates=[ordered]@{Storage='NOT_RUN';Candidate='NOT_RUN';CanonicalRunIdentity='PASS';Preflight='NOT_RUN';TotalCount='NOT_RUN';IsolatedC1C2='NOT_RUN';C4='NOT_RUN';BDN='NOT_RUN';PostIsolatedCorrectness='NOT_RUN';MixedL1=[string]$integratedReadback.Levels.L1.Status;MixedL2=[string]$integratedReadback.Levels.L2.Status;MixedL4=[string]$integratedReadback.Levels.L4.Status;MixedL8=[string]$integratedReadback.Levels.L8.Status;MixedOverlap='PASS';MixedTelemetry='NOT_RUN';PostMixedCorrectness='NOT_RUN';AggregateCleanup='NOT_RUN';BoundedDbPrePost='NOT_RUN';CandidateGuard='NOT_RUN';Preservation='NOT_RUN';RawArtifactIntegrity='NOT_RUN';KnownLimitations='PRESENT';ReportLint='NOT_RUN';ReviewPackIntegrity='NOT_RUN';MixedHarnessOffline='PASS'}
$integratedValues=[ordered]@{Verdict='BATCH3_NOT_READY';CanonicalRunId=$runId;ProtocolRunId=$runId;IdentityStatus='PASS';CandidateId=$candidateId;SourceSnapshotId=$snapshotId;ManifestSHA256=('a'*64);BenchmarkDllSHA256='NOT_BUILT';DataAccessDllSHA256='NOT_BUILT';SourceInventoryCount=0;SourceInventorySHA256='NOT_BUILT';RuntimeInventoryCount=0;RuntimeInventorySHA256='NOT_BUILT';Gates=$integratedGates;CorrectnessPreflight='NOT_RUN';TotalCountPreflight='NOT_RUN';PostIsolatedCorrectness='NOT_RUN';PostMixedCorrectness='NOT_RUN';IsolatedC1C2='NOT_RUN';C4='NOT_RUN';BDN='NOT_RUN';MixedL1=[string]$integratedReadback.Levels.L1.Status;MixedL2=[string]$integratedReadback.Levels.L2.Status;MixedL4=[string]$integratedReadback.Levels.L4.Status;MixedL8=[string]$integratedReadback.Levels.L8.Status;MixedOverlap='PASS';MixedTelemetry='NOT_RUN';ProcessCount=0;PidProbeCount=0;HelperProbeCount=0;CleanupStatus='NOT_RUN';PrePostStatus='NOT_RUN';PreservationStatus='NOT_RUN';RawArtifactIntegrity='NOT_RUN';CanonicalWorkloadAttempts=0;HistoricalTimeoutCause='NOT_VERIFIED';FullDatasetValueEquality='NOT_VERIFIED';PerformanceSLA='NO_SLA_DEFINED';CausalAttribution='NOT_ESTABLISHED';HistoricalHostLimitActionTimestamp='NOT_VERIFIED';CoreEvidenceReady='NO';ReadyForBatch4='NO';StopReason='OFFLINE_SYNTHETIC_FIXTURE_ONLY';FailureStage='MIXED_HARNESS_OFFLINE_TEST';EvidencePaths=@($integratedProjectionPath)}
$integratedReport=New-V22FinalReport $integratedValues
$integratedLint=Test-V22FinalReportLint $integratedReport
$integratedReportPath=Join-Path $fixtureRoot 'integrated-mixed-final-report.md'
[IO.File]::WriteAllText($integratedReportPath,$integratedReport,[Text.UTF8Encoding]::new($false))
Add-Case 'INTEGRATED_FINALIZER_REPORT' @{ProjectionPath=$integratedProjectionPath;ReportPath=$integratedReportPath} 'PASS|PASS|BATCH3_NOT_READY|0' {if($integratedLint.Status-eq'PASS'-and$integratedReport.Contains('Mixed levels: L1=PASS; L2=PASS; L4=PASS; L8=PASS')-and$integratedReport.Contains('Canonical workload attempts: 0')-and$integratedReport.Contains('MixedHarnessOffline: PASS')){'PASS|PASS|BATCH3_NOT_READY|0'}else{'FAIL|'+$integratedLint.Status+'|'+[string]$integratedValues.Verdict+'|'+[string]$integratedValues.CanonicalWorkloadAttempts}}
$validPass=$levelResults.L1
Add-Case 'PASS_OVERLAP_TRUE' @{Status='PASS';OverlapValid=$true} 'PASS|PASS' {$g=Get-V22MixedResultGate $validPass 10;'{0}|{1}'-f$g.MeasurementStatus,$g.OverlapStatus}
$passFalse=$validPass.PSObject.Copy();$passFalse.OverlapValid=$false;$passFalse.OverlapSeconds=9;$passFalse.WindowOverlapSeconds=9;$passFalse.OverlapStatus='FAIL'
Add-Case 'PASS_OVERLAP_FALSE' @{Status='PASS';OverlapValid=$false} 'FAIL|PASS_OVERLAP_FALSE' {$g=Get-V22MixedResultGate $passFalse 10;'{0}|{1}'-f$g.MeasurementStatus,($g.Errors|Where-Object{$_-ceq'PASS_OVERLAP_FALSE'}|Select-Object -First 1)}
$failBefore=New-V22MixedResultRecord ([ordered]@{Status='FAIL';FailureType='SYNTHETIC_FAILURE';FailureReason='Failure before overlap calculation'}) $runId $candidateId 'L1'
Add-Case 'FAIL_BEFORE_OVERLAP' @{Status='FAIL';OverlapApplicable='OPTIONAL'} 'PASS|FAIL|NOT_CALCULATED' {$g=Get-V22MixedResultGate $failBefore 10;'{0}|{1}|{2}'-f$g.SchemaStatus,$g.MeasurementStatus,$g.OverlapStatus}
$missingOptional=$failBefore.PSObject.Copy();$missingOptional.PSObject.Properties.Remove('OverlapValid')
Add-Case 'FAIL_MISSING_OPTIONAL_OVERLAP' @{Status='FAIL';OverlapValid='NOT_APPLICABLE'} 'PASS|FAIL|NOT_CALCULATED' {$g=Get-V22MixedResultGate $missingOptional 10;'{0}|{1}|{2}'-f$g.SchemaStatus,$g.MeasurementStatus,$g.OverlapStatus}
$failAfter=New-V22MixedResultRecord ([ordered]@{Status='FAIL';FailureType='SYNTHETIC_FAILURE';FailureReason='Overlap measured below floor';OverlapValid=$false;OverlapSeconds=4}) $runId $candidateId 'L1'
Add-Case 'FAIL_AFTER_OVERLAP' @{Status='FAIL';OverlapValid=$false} 'PASS|FAIL|FAIL' {$g=Get-V22MixedResultGate $failAfter 10;'{0}|{1}|{2}'-f$g.SchemaStatus,$g.MeasurementStatus,$g.OverlapStatus}
$invalid=New-V22MixedResultRecord ([ordered]@{Status='INVALID';FailureType='TELEMETRY_INVALID';FailureReason='Invalid telemetry'}) $runId $candidateId 'L1'
Add-Case 'INVALID_UNION_STATE' @{Status='INVALID'} 'PASS|FAIL|NOT_CALCULATED' {$g=Get-V22MixedResultGate $invalid 10;'{0}|{1}|{2}'-f$g.SchemaStatus,$g.MeasurementStatus,$g.OverlapStatus}
$hostLimit=New-V22MixedResultRecord ([ordered]@{Status='HOST_LIMIT';FailureType='HOST_LIMIT';FailureReason='Admission rejected'}) $runId $candidateId 'L1'
Add-Case 'HOST_LIMIT_UNION_STATE' @{Status='HOST_LIMIT'} 'PASS|NOT_RUN|NOT_APPLICABLE' {$g=Get-V22MixedResultGate $hostLimit 10;'{0}|{1}|{2}'-f$g.SchemaStatus,$g.MeasurementStatus,$g.OverlapStatus}
$skipped=New-V22MixedResultRecord ([ordered]@{Status='SKIPPED';FailureType='PRIOR_LEVEL_FAILED';FailureReason='Prior level failed'}) $runId $candidateId 'L2'
Add-Case 'SKIPPED_UNION_STATE' @{Status='SKIPPED'} 'PASS|NOT_RUN|NOT_APPLICABLE' {$g=Get-V22MixedResultGate $skipped 10;'{0}|{1}|{2}'-f$g.SchemaStatus,$g.MeasurementStatus,$g.OverlapStatus}
$partial=New-V22MixedResultRecord ([ordered]@{Status='PARTIAL';FailureType='PARTIAL_EVIDENCE';FailureReason='Partial metadata';OverlapValid=$true;OverlapSeconds=12}) $runId $candidateId 'L1'
Add-Case 'PARTIAL_UNION_STATE' @{Status='PARTIAL'} 'PASS|FAIL|PASS' {$g=Get-V22MixedResultGate $partial 10;'{0}|{1}|{2}'-f$g.SchemaStatus,$g.MeasurementStatus,$g.OverlapStatus}
$missingRequired=$validPass.PSObject.Copy();$missingRequired.PSObject.Properties.Remove('OverlapValid');$missingRequired.OverlapStatus='NOT_CALCULATED'
Add-Case 'PASS_MISSING_REQUIRED_OVERLAP' @{Status='PASS';OverlapValid='MISSING'} 'INVALID|FAIL' {$g=Get-V22MixedResultGate $missingRequired 10;'{0}|{1}'-f$g.SchemaStatus,$g.MeasurementStatus}
$malformed=$validPass.PSObject.Copy();$malformed.OverlapValid='true';$malformed.OverlapStatus='INVALID'
Add-Case 'MALFORMED_OVERLAP_TYPE' @{Status='PASS';OverlapValid='true'} 'INVALID|FAIL' {$g=Get-V22MixedResultGate $malformed 10;'{0}|{1}'-f$g.SchemaStatus,$g.MeasurementStatus}
$badCount=$validPass.PSObject.Copy();$badCount.ObservedScenarioProcessCount=5
Add-Case 'PASS_WORKER_COUNT_MISMATCH' @{Status='PASS';ObservedScenarioProcessCount=5} 'FAIL|PASS_WORKER_COUNT_MISMATCH' {$g=Get-V22MixedResultGate $badCount 10;'{0}|{1}'-f$g.MeasurementStatus,($g.Errors|Where-Object{$_-ceq'PASS_WORKER_COUNT_MISMATCH'}|Select-Object -First 1)}
$unknown=New-V22MixedResultRecord ([ordered]@{Status='UNRECOGNIZED';FailureType='SYNTHETIC';FailureReason='unknown'}) $runId $candidateId 'L1'
Add-Case 'UNKNOWN_STATUS_FAIL_CLOSED' @{Status='UNRECOGNIZED'} 'INVALID|FAIL' {$g=Get-V22MixedResultGate $unknown 10;'{0}|{1}'-f$unknown.Status,$g.MeasurementStatus}
function New-FixtureReport([string]$Name,[string]$L1,[string]$L2,[string]$L4,[string]$L8,[string]$Reason){
    $gates=[ordered]@{Storage='PASS';Candidate='PASS';CanonicalRunIdentity='PASS';Preflight='PASS';TotalCount='PASS';IsolatedC1C2='PASS';C4='PASS';BDN='PASS';PostIsolatedCorrectness='PASS';MixedL1=$L1;MixedL2=$L2;MixedL4=$L4;MixedL8=$L8;MixedOverlap='FAIL';MixedTelemetry='INVALID';PostMixedCorrectness='NOT_RUN';AggregateCleanup='PASS';BoundedDbPrePost='PASS';CandidateGuard='PASS';Preservation='PASS';RawArtifactIntegrity='PASS';KnownLimitations='PRESENT';ReportLint='PASS';ReviewPackIntegrity='PASS'}
    $values=[ordered]@{Verdict='BATCH3_PARTIAL';CanonicalRunId=$runId;ProtocolRunId=$runId;IdentityStatus='PASS';CandidateId=$candidateId;SourceSnapshotId=$snapshotId;ManifestSHA256=('a'*64);BenchmarkDllSHA256=('b'*64);DataAccessDllSHA256=('c'*64);SourceInventoryCount=10;SourceInventorySHA256=('d'*64);RuntimeInventoryCount=5;RuntimeInventorySHA256=('e'*64);Gates=$gates;CorrectnessPreflight='6/6';TotalCountPreflight='6/6';PostIsolatedCorrectness='6/6';PostMixedCorrectness='NOT_RUN';IsolatedC1C2='12/12';C4='6/6';BDN='6/6';MixedL1=$L1;MixedL2=$L2;MixedL4=$L4;MixedL8=$L8;MixedOverlap='FAIL';MixedTelemetry='INVALID';ProcessCount=0;PidProbeCount=0;HelperProbeCount=0;CleanupStatus='PASS';PrePostStatus='PASS';PreservationStatus='PASS';RawArtifactIntegrity='PASS';CanonicalWorkloadAttempts=1;HistoricalTimeoutCause='NOT_VERIFIED';FullDatasetValueEquality='NOT_VERIFIED';PerformanceSLA='NO_SLA_DEFINED';CausalAttribution='NOT_ESTABLISHED';HistoricalHostLimitActionTimestamp='NOT_VERIFIED';CoreEvidenceReady='NO';ReadyForBatch4='NO';StopReason=$Reason;FailureStage='MIXED_PERFORMANCE';EvidencePaths=@($fixtureRoot)}
    $text=New-V22FinalReport $values;$lint=Test-V22FinalReportLint $text;$out=Join-Path $fixtureRoot ($Name+'.md');[IO.File]::WriteAllText($out,$text,[Text.UTF8Encoding]::new($false));return [pscustomobject]@{Path=$out;Lint=$lint.Status;Status=if($lint.Status-eq'PASS'){'PASS'}else{'FAIL'}}
}
$reports=@()
$reports+=New-FixtureReport 'mixed-l1-fail-l234-skipped' 'FAIL' 'SKIPPED' 'SKIPPED' 'SKIPPED' 'L1 result projection failed'
$reports+=New-FixtureReport 'host-limit' 'PASS' 'HOST_LIMIT' 'SKIPPED' 'SKIPPED' 'Host admission rejected L2'
$reports+=New-FixtureReport 'overlap-invalid' 'INVALID' 'SKIPPED' 'SKIPPED' 'SKIPPED' 'Overlap below protocol floor'
$reports+=New-FixtureReport 'worker-count-mismatch' 'INVALID' 'SKIPPED' 'SKIPPED' 'SKIPPED' 'Observed worker count did not match protocol'
$reports+=New-FixtureReport 'mixed-fail' 'FAIL' 'SKIPPED' 'SKIPPED' 'SKIPPED' 'Mixed integration fixture failure'
foreach($report in $reports){Add-Case ('REPORT_'+[IO.Path]::GetFileNameWithoutExtension($report.Path)) @{Path=$report.Path} 'PASS' {if($report.Status-eq'PASS'-and$report.Lint-eq'PASS'){'PASS'}else{'FAIL'}}}
$failed=@($checks|Where-Object Status -cne 'PASS').Count
$result=[ordered]@{SchemaVersion='warehouse-benchmark-v22-mixed-harness-tests/1';Status=if($failed-eq 0){'PASS'}else{'FAIL'};Passed=$checks.Count-$failed;Failed=$failed;TestCount=$checks.Count;DatabaseAccess='NOT_USED';NBomberWorkload='NOT_RUN';BenchmarkDotNet='NOT_RUN';Levels=$levelResults;LevelGates=$levelGates;UnionSchema=[ordered]@{CommonRequired=@('SchemaVersion','RunId','CandidateId','MixedLevel','Status','FailureType','FailureReason','StartedUtc','FinishedUtc');CountsRequired=@('ScenarioProcessCount','CopiesPerScenario','TotalLogicalCopies','ObservedScenarioProcessCount','ObservedCopiesPerScenario','ObservedTotalLogicalCopies');OverlapApplicability=@{PASS='REQUIRED';FAIL='OPTIONAL';INVALID='OPTIONAL';HOST_LIMIT='NOT_APPLICABLE';EMERGENCY_HOST_LIMIT='NOT_APPLICABLE';SKIPPED='NOT_APPLICABLE';PARTIAL='OPTIONAL'}};Reports=$reports;Tests=$checks.ToArray();RecordedUtc=[DateTime]::UtcNow.ToString('o')}
if(Test-Path -LiteralPath $EvidencePath){throw 'MIXED_TEST_EVIDENCE_EXISTS_NO_OVERWRITE'}
[IO.File]::WriteAllText($EvidencePath,($result|ConvertTo-Json -Depth 18),[Text.UTF8Encoding]::new($false))
$result
if($failed-gt 0){exit 1}
