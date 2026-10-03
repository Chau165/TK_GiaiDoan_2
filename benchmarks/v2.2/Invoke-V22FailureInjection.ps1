[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EvidenceRoot)
$ErrorActionPreference='Stop'
$harnessPath=Join-Path $PSScriptRoot 'WarehouseBenchmarkV22.Harness.psm1'
Import-Module $harnessPath
Import-Module (Join-Path $PSScriptRoot 'WarehouseBenchmarkV22.Finalizer.psm1')
if(Test-Path -LiteralPath $EvidenceRoot){throw 'V22_TEST_EVIDENCE_ROOT_EXISTS'}
New-Item -ItemType Directory -Path $EvidenceRoot | Out-Null
$script:Results=[Collections.Generic.List[object]]::new()
function Invoke-Case([string]$TestId,[object]$InputValue,[string]$Expected,[scriptblock]$Action){
    $actual=''
    try{$actual=[string](& $Action)}catch{$actual='THREW:'+($_.Exception.Message -replace '\s+',' ')}
    $status=if($actual -ceq $Expected){'PASS'}else{'FAIL'}
    $path=Join-Path $EvidenceRoot ($TestId+'.json')
    $row=[ordered]@{TestId=$TestId;Input=$InputValue;Expected=$Expected;Actual=$actual;Status=$status;EvidencePath=$path}
    $json=$row|ConvertTo-Json -Depth 8
    $stream=[IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($json+[Environment]::NewLine);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
    $script:Results.Add([pscustomobject]$row)
}
function Get-RejectionCode([scriptblock]$Action){try{[void](& $Action);return 'ACCEPTED'}catch{$message=$_.Exception.Message;if($message -match '^((?:V22_[A-Z0-9_]+)|CANONICAL_RUN_IDENTITY_INVALID)\|'){return $Matches[1]};return 'UNCLASSIFIED_REJECTION'}}
function New-BdnFixture([string]$Dir,[string]$Mean){
    if(-not(Test-Path -LiteralPath $Dir)){New-Item -ItemType Directory -Path $Dir|Out-Null}
    $summary=Join-Path $Dir 'summary.csv';$measure=Join-Path $Dir 'measurements.csv'
    $summaryLines=@('Mean,Error,StdDev,Unit,ConfiguredIterations,ReportedN,RawActualRows,RawResultRows,RemovedUpperOutliers,UpperFenceNs,MeanRoundingToleranceNs',("{0},1 ns,2 ns,ns,2,2,2,2,0,1000,0.5" -f $Mean))
    $measurementLines=@('IterationStage,Iteration,Operations,Nanoseconds,IncludedForMean','Result,1,1,120,True','Result,2,1,140,True')
    Set-Content -LiteralPath $summary -Value $summaryLines -Encoding utf8
    Set-Content -LiteralPath $measure -Value $measurementLines -Encoding utf8
    return @{SummaryPath=$summary;MeasurementsPath=$measure}
}
function New-NbFixture([object]$Override){
    $m=[ordered]@{Requests=100;Failed=1;RPS=10.0;MeanMs=5.0;P50Ms=4.0;P95Ms=8.0;P99Ms=9.0;MaxMs=10.0;WindowMs=10000;ConfiguredWindowMs=10000;ObservedWindowMs=10000;Units=[ordered]@{Requests='count';Failed='count';RPS='requests/s';MeanMs='ms';P50Ms='ms';P95Ms='ms';P99Ms='ms';MaxMs='ms';WindowMs='ms';ConfiguredWindowMs='ms';ObservedWindowMs='ms'}}
    foreach($key in $Override.Keys){$m[$key]=$Override[$key]}
    return $m
}
function New-ValidTelemetry([string]$Run='run-1',[string]$Block='block-1'){@{Status='VALID';TargetDatabase='WarehousePerf';RunId=$Run;BlockId=$Block;SampleUtc=[DateTime]::UtcNow.ToString('o');FreeRamMb=3072;CpuPercent=12.5}}
function Get-ModuleResult([scriptblock]$Action){$null=& $Action;return 'ACCEPTED'}

foreach($case in @(@{Id='FI_BDN_MEAN_NA';Value='NA'},@{Id='FI_BDN_MEAN_EMPTY';Value=''},@{Id='FI_BDN_MEAN_NAN';Value='NaN'},@{Id='FI_BDN_MEAN_POS_INF';Value='Infinity'},@{Id='FI_BDN_MEAN_NEG_INF';Value='-Infinity'},@{Id='FI_BDN_MEAN_MALFORMED';Value='not-a-number'})){
    $dir=Join-Path $EvidenceRoot $case.Id;$fixture=New-BdnFixture $dir $case.Value
    Invoke-Case $case.Id @{Mean=$case.Value;SummaryPath=$fixture.SummaryPath;MeasurementsPath=$fixture.MeasurementsPath} 'V22_BDN_INVALID' {Get-RejectionCode {Assert-V22BdnEvidence $fixture.SummaryPath $fixture.MeasurementsPath}}
}
Invoke-Case 'FI_CURRENT_PRE_POST_MISMATCH' @{Before='current-a';After='current-b'} 'V22_READ_ONLY_MUTATION' {Get-RejectionCode {Assert-V22ReadOnlyIntegrity @{DatasetHash='d';CurrentHash='current-a'} @{DatasetHash='d';CurrentHash='current-b'}}}
Invoke-Case 'FI_DATASET_PRE_POST_MISMATCH' @{Before='dataset-a';After='dataset-b'} 'V22_READ_ONLY_MUTATION' {Get-RejectionCode {Assert-V22ReadOnlyIntegrity @{DatasetHash='dataset-a';CurrentHash='c'} @{DatasetHash='dataset-b';CurrentHash='c'}}}
$target='WarehousePerf';$run='run-1';$block='block-1'
$allError=@(@{Status='TELEMETRY_ERROR';TargetDatabase=$target;RunId=$run;BlockId=$block;Message='synthetic'})
Invoke-Case 'FI_TELEMETRY_ALL_ERROR' @{Rows=$allError;TargetDatabase=$target;RunId=$run;BlockId=$block} 'TELEMETRY_INVALID_VALID=0_ERRORS=1' {$a=Get-V22TelemetryAssessment $allError $target $run $block;'{0}_VALID={1}_ERRORS={2}' -f $a.Status,$a.ValidTargetRows,$a.ErrorRows}
$wrong=@((New-ValidTelemetry 'other-run' $block))
Invoke-Case 'FI_TELEMETRY_ZERO_VALID_TARGET' @{Rows=$wrong;ExpectedRunId=$run} 'TELEMETRY_INVALID_VALID=0_REJECTED=1' {$a=Get-V22TelemetryAssessment $wrong $target $run $block;'{0}_VALID={1}_REJECTED={2}' -f $a.Status,$a.ValidTargetRows,$a.RejectedRows}
$mixed=@((New-ValidTelemetry $run $block),@{Status='TELEMETRY_ERROR';TargetDatabase=$target;RunId=$run;BlockId=$block;Message='synthetic'})
Invoke-Case 'FI_TELEMETRY_MIXED_VALID_ERROR' @{Rows=2;ExpectedValid=1;ExpectedErrors=1} 'TELEMETRY_INVALID_VALID=1_ERRORS=1' {$a=Get-V22TelemetryAssessment $mixed $target $run $block;'{0}_VALID={1}_ERRORS={2}' -f $a.Status,$a.ValidTargetRows,$a.ErrorRows}
Invoke-Case 'FI_TELEMETRY_WRONG_RUN_REJECTED' @{ExpectedRun='run-1';RowRun='wrong'} 'TELEMETRY_INVALID_VALID=0' {$a=Get-V22TelemetryAssessment @((New-ValidTelemetry 'wrong' $block)) $target $run $block;'{0}_VALID={1}' -f $a.Status,$a.ValidTargetRows}
$moduleText=Get-Content -LiteralPath $harnessPath -Raw
$fake=[pscustomobject]@{Id=873421;HasExited=$true}
Invoke-Case 'FI_FAKE_PID_NO_COLLISION' @{FakeProcessId=873421;NoRealProcess=$true} 'NO_PID_COLLISION_NO_KILL' {$static=($moduleText -notmatch '(?im)^\s*\$(?:pid|PID)\s*=');Register-V22TrackedProcess $fake 873421 'CHILD'|Out-Null;$tracked=Get-V22TrackedProcesses;$cleanup=Clear-V22TrackedProcesses;if($static -and $tracked[0].ChildProcessId -eq 873421 -and $cleanup.Items[0].Status -eq 'FAKE_OR_UNRESOLVED_NOT_KILLED'){'NO_PID_COLLISION_NO_KILL'}else{'FAILED'}}
Invoke-Case 'FI_POST_INTEGRITY_FALSE_PRECEDENCE' @{PostIntegrityPassed=$false;ValidationPassed=$true} 'INVALID_READ_ONLY_MUTATION' {Get-V22FinalClassification $false $true $true $false}
Invoke-Case 'FI_REQUIRED_TELEMETRY_INVALID_BLOCKS_RUN' @{PerformanceExecuted=$true;TelemetryRequired=$true;TelemetryPassed=$false} 'HARNESS_FAILED_TELEMETRY' {Get-V22FinalClassification $true $true $true $true $true $false}
$ran=$false
Invoke-Case 'FI_COOLDOWN_FALSE_BLOCKS_NEXT' @{CooldownPassed=$false} 'V22_COOLDOWN_FAILED_BLOCK_NOT_RUN' {try{Invoke-V22BlockAfterCooldown @{Passed=$false} {$script:ran=$true}|Out-Null;'BLOCK_RAN'}catch{if($_.Exception.Message -match '^V22_COOLDOWN_FAILED\|' -and -not $script:ran){'V22_COOLDOWN_FAILED_BLOCK_NOT_RUN'}else{'FAILED'}}}
$fatalEvidenceDir=Join-Path $EvidenceRoot 'FI_FATAL_EVIDENCE_STOP';New-Item -ItemType Directory -Path $fatalEvidenceDir|Out-Null
$priorEap=$ErrorActionPreference;$ErrorActionPreference='Stop';$fatalCaught=$null
try{Invoke-V22WithCleanup {throw 'synthetic fatal root cause'} { [pscustomobject]@{Passed=$true;Items=@()} } $fatalEvidenceDir 'SYNTHETIC_STAGE'|Out-Null}catch{$fatalCaught=$_.Exception.Message}
finally{$ErrorActionPreference=$priorEap}
$fatalSummaryPath=Join-Path $fatalEvidenceDir 'fatal-summary.json';$fatal=$null
if(Test-Path -LiteralPath $fatalSummaryPath){$fatal=Get-Content -LiteralPath $fatalSummaryPath -Raw|ConvertFrom-Json}
Invoke-Case 'FI_FATAL_EVIDENCE_BEFORE_TERMINATING_FAILURE' @{ErrorActionPreference='Stop';Caught=$fatalCaught} 'FATAL_FILES_WRITTEN_ROOT_CAUSE_RETAINED' {if($null -ne $fatal -and $fatal.Status -eq 'HARNESS_FAILED' -and $fatal.Stage -eq 'SYNTHETIC_STAGE' -and $fatal.SafeExceptionMessage -eq 'synthetic fatal root cause' -and (Test-Path (Join-Path $fatalEvidenceDir 'failure-reason.txt'))){'FATAL_FILES_WRITTEN_ROOT_CAUSE_RETAINED'}else{'MISSING_FATAL_EVIDENCE'}}
$existing=Join-Path $EvidenceRoot 'FI_EXISTING_PHASE_ROOT';New-Item -ItemType Directory -Path $existing|Out-Null;$sentinel=Join-Path $existing 'sentinel.txt';Set-Content -LiteralPath $sentinel -Value 'preserve' -Encoding utf8;$beforeHash=(Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash
Invoke-Case 'FI_EXISTING_PHASE_ROOT_NO_OVERWRITE' @{ExistingRoot=$existing;SentinelSHA256=$beforeHash} 'V22_EXISTING_PHASE_ROOT_SENTINEL_UNCHANGED' { $code=Get-RejectionCode {New-V22RunRoot $existing|Out-Null};$after=(Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash;if($code -eq 'V22_EXISTING_PHASE_ROOT' -and $after -eq $beforeHash){'V22_EXISTING_PHASE_ROOT_SENTINEL_UNCHANGED'}else{'FAILED'} }
Invoke-Case 'FI_NB_MALFORMED_NUMERIC' @{Field='RPS';Value='NaN-ish'} 'V22_NB_INVALID' {Get-RejectionCode {Assert-V22NBomberMetrics (New-NbFixture @{RPS='NaN-ish'}) 10000|Out-Null}}
foreach($flag in @('Bdn','Mixed','Resume','NoBuild','SoftWaitSeconds')){
    $entry=Join-Path $PSScriptRoot 'run-warehouse-benchmark-v2.2-adaptive.ps1'
    Invoke-Case ('FI_UNSUPPORTED_FLAG_'+$flag.ToUpperInvariant()) @{Flag=$flag} 'REJECTED' {try{$flagArguments=@{};if($flag -ceq 'SoftWaitSeconds'){$flagArguments[$flag]=1}else{$flagArguments[$flag]=$true};& $entry @flagArguments|Out-Null;'ACCEPTED'}catch{'REJECTED'}}
}
foreach($role in @('CHILD','TELEMETRY')){
    $id='FI_'+$role+'_START_THEN_EXCEPTION';$dir=Join-Path $EvidenceRoot $id;New-Item -ItemType Directory -Path $dir|Out-Null
    $cleanupObserved=$false
    try{Invoke-V22WithCleanup {Register-V22TrackedProcess ([pscustomobject]@{Id=78123;HasExited=$true}) 78123 $role|Out-Null;throw ('synthetic '+$role+' primary')} { $records=Get-V22TrackedProcesses;$script:cleanupObserved=(@($records|Where-Object Role -ceq $role).Count -eq 1);Clear-V22TrackedProcesses } $dir $role|Out-Null}catch{}
    $fatalPath=Join-Path $dir 'fatal-summary.json';$f=Get-Content -LiteralPath $fatalPath -Raw|ConvertFrom-Json
    Invoke-Case $id @{Role=$role;FakeProcessId=78123} 'PRIMARY_EXCEPTION_CLEANUP_RAN_FATAL_CAPTURED' {if($script:cleanupObserved -and $f.SafeExceptionMessage -ceq ('synthetic '+$role+' primary')){'PRIMARY_EXCEPTION_CLEANUP_RAN_FATAL_CAPTURED'}else{'FAILED'}}
}
$missingPid=990001
Register-V22TrackedProcess $null $missingPid 'TELEMETRY'|Out-Null
$missingCleanup=Clear-V22TrackedProcesses
Invoke-Case 'FI_CLEANUP_NONEXISTENT_FAKE_PID' @{ChildProcessId=$missingPid;NoRealProcess=$true} 'UNRESOLVED_RECORDED_NOT_KILLED' {if($missingCleanup.Items[0].ChildProcessId -eq $missingPid -and $missingCleanup.Items[0].Status -eq 'FAKE_OR_UNRESOLVED_NOT_KILLED'){'UNRESOLVED_RECORDED_NOT_KILLED'}else{'FAILED'}}
$dir=Join-Path $EvidenceRoot 'FI_PRIMARY_AND_CLEANUP_FAILURE';New-Item -ItemType Directory -Path $dir|Out-Null
try{Invoke-V22WithCleanup {throw 'primary-root-cause'} {throw 'cleanup-secondary-error'} $dir 'BOTH_FAIL'|Out-Null}catch{$caught=$_.Exception.Message}
$both=Get-Content -LiteralPath (Join-Path $dir 'fatal-summary.json') -Raw|ConvertFrom-Json
Invoke-Case 'FI_PRIMARY_FAILURE_PLUS_CLEANUP_FAILURE' @{Primary='primary-root-cause';Cleanup='cleanup-secondary-error'} 'PRIMARY_RETAINED_CLEANUP_RECORDED' {if($caught -match 'primary-root-cause' -and $both.FailureReason -eq 'PRIMARY_FAILURE' -and $both.CleanupResult.CleanupFailure -match 'cleanup-secondary-error'){'PRIMARY_RETAINED_CLEANUP_RECORDED'}else{'FAILED'}}
$hardStop=[pscustomobject]@{Reason='HOST_STOP_HARD_FLOOR';TriggerName='HOST_STOP_HARD_FLOOR';ConfiguredThresholdMB=512;ObservedFreeMB=500.5;ObservedUtc=[DateTime]::UtcNow.ToString('o');BlockId='diag-LookupPaged-C1';ChildPid=873421;ActionRequested=$true;ActionCompleted=$true;ProcessExitResult='EXIT_CODE_-1'}
$emergencyStop=[pscustomobject]@{Reason='HOST_STOP_EMERGENCY_FLOOR';TriggerName='HOST_STOP_EMERGENCY_FLOOR';ConfiguredThresholdMB=128;ObservedFreeMB=100;ObservedUtc=[DateTime]::UtcNow.ToString('o');BlockId='diag-LookupPaged-C1';ChildPid=873422;ActionRequested=$true;ActionCompleted=$true;ProcessExitResult='EXIT_CODE_-1'}
Invoke-Case 'FI_WORKER_CHILD_EXIT_NORMAL' @{ProcessExited=$true;ExitCode=0;CompletedMetadataPresent=$true} 'COMPLETED' {Get-V22WorkerTerminalState $true 0 $true $null}
Invoke-Case 'FI_WORKER_CHILD_EXIT_NONZERO' @{ProcessExited=$true;ExitCode=17;CompletedMetadataPresent=$false} 'ABORTED_CHILD_EXIT' {Get-V22WorkerTerminalState $true 17 $false $null}
Invoke-Case 'FI_WORKER_HOST_HARD_STOP' @{BlockId=$hardStop.BlockId;ChildPid=$hardStop.ChildPid;ObservedFreeMB=$hardStop.ObservedFreeMB} 'ABORTED_HOST_LIMIT' {Get-V22WorkerTerminalState $true -1 $false $hardStop}
Invoke-Case 'FI_WORKER_HOST_EMERGENCY_STOP' @{BlockId=$emergencyStop.BlockId;ChildPid=$emergencyStop.ChildPid;ObservedFreeMB=$emergencyStop.ObservedFreeMB} 'ABORTED_HOST_LIMIT' {Get-V22WorkerTerminalState $true -1 $false $emergencyStop}
Invoke-Case 'FI_WORKER_METADATA_INCOMPLETE_AT_CRASH' @{Status='IN_PROGRESS';ProcessExited=$true;ExitCode=0;CompletedMetadataPresent=$false} 'ABORTED_HARNESS' {Get-V22WorkerTerminalState $true 0 $false $null}
$terminalProjection=New-V22WorkerTerminalProjection -ExpectedCopies 2 -ObservedInstanceNumbers @(0) -ProcessExited $true -ExitCode 17 -CompletedMetadataPresent $false -HostStopEvidence $null -RawEvidencePath 'synthetic/block'
Invoke-Case 'FI_WORKER_TERMINAL_ABORT_METADATA' @{ExpectedCopies=2;ObservedInstanceNumbers=@(0);ExitCode=17;RawEvidencePath='synthetic/block'} 'ABORTED_CHILD_EXIT|EXPECTED=2|OBSERVED=1|WINDOW=False|EXIT=17|RAW=synthetic/block' {'{0}|EXPECTED={1}|OBSERVED={2}|WINDOW={3}|EXIT={4}|RAW={5}' -f $terminalProjection.Status,$terminalProjection.ExpectedCopies,$terminalProjection.ObservedCopies,$terminalProjection.TimedWindowCompleted,$terminalProjection.ProcessExitCode,$terminalProjection.RawEvidencePath}
$orphan=Get-V22ProcessCleanupAssessment @(873421) @([pscustomobject]@{TargetProcessId=873421;ObservedProcessId=873421;Status='PRESENT';Reason='synthetic exact owned process'})
Invoke-Case 'FI_CLEANUP_ORPHAN_DETECTED' @{OwnedProcessId=873421;ObservedProcessId=873421;NoRealProcess=$true} 'FAIL|ORPHAN_RUNNING' {'{0}|{1}' -f $orphan.Status,$orphan.Items[0].Status}
$gone=Get-V22ProcessCleanupAssessment @(873421) @([pscustomobject]@{TargetProcessId=873421;ObservedProcessId=$null;Status='ABSENT';Reason='synthetic absent target PID'})
Invoke-Case 'FI_CLEANUP_TERMINATED_CHILD_GONE' @{OwnedProcessId=873421;ObservedProcessId=$null;NoRealProcess=$true} 'PASS|CLEAN' {'{0}|{1}' -f $gone.Status,$gone.Items[0].Status}
$wrongPid=Get-V22ProcessCleanupAssessment @(873421) @([pscustomobject]@{TargetProcessId=873421;ObservedProcessId=873422;Status='PRESENT';Reason='synthetic wrong PID'})
Invoke-Case 'FI_CLEANUP_WRONG_PID_REJECTED' @{OwnedProcessId=873421;ObservedProcessId=873422;NoRealProcess=$true} 'FAIL|CLEANUP_UNVERIFIED' {'{0}|{1}' -f $wrongPid.Status,$wrongPid.Items[0].Status}
$missingStopReason=[pscustomobject]@{Reason='';TriggerName='HOST_STOP_HARD_FLOOR';ConfiguredThresholdMB=512;ObservedFreeMB=500;ObservedUtc=[DateTime]::UtcNow.ToString('o');BlockId='synthetic-block';ChildPid=873421;ActionRequested=$true;ActionCompleted=$true;ProcessExitResult='EXIT_CODE_-1'}
Invoke-Case 'FI_HOST_STOP_REASON_MISSING_REJECTED' @{Reason='';TriggerName='HOST_STOP_HARD_FLOOR'} 'REJECTED|ABORTED_HARNESS' {$accepted=Test-V22HostStopEvidence $missingStopReason;'{0}|{1}' -f $(if($accepted){'ACCEPTED'}else{'REJECTED'}),(Get-V22WorkerTerminalState $true -1 $false $missingStopReason)}
$optionalReasonMissing=Get-V22Field ([pscustomobject]@{Status='PASS'}) 'Reason'
Invoke-Case 'FI_OPTIONAL_CORRECTNESS_REASON_ABSENT_SAFE' @{Status='PASS';ReasonPropertyPresent=$false} 'NULL' {if($null -eq $optionalReasonMissing){'NULL'}else{'VALUE'}}
$optionalReasonPresent=Get-V22Field ([pscustomobject]@{Status='FAIL';Reason='synthetic failure'}) 'Reason'
Invoke-Case 'FI_OPTIONAL_CORRECTNESS_REASON_PRESENT' @{Status='FAIL';ReasonPropertyPresent=$true} 'synthetic failure' {$optionalReasonPresent}
$projection=New-V22CorrectnessEvidenceProjection -SemanticScenariosCompleted 6 -TotalCountPassed 6 -SemanticStatus 'PASS_6_OF_6' -InventoryStatus 'PASS_HISTORICAL_AND_CURRENT' -SecurityStatus 'NOT_APPLICABLE_BY_CONTRACT' -DatabaseStateStatus 'PASS'
Invoke-Case 'FI_CORRECTNESS_PROJECTION_SIX_OF_SIX' @{SemanticScenariosCompleted=6;TotalCountPassed=6} 'PASS|SEMANTIC=6/6|TOTAL=6/6' {'{0}|SEMANTIC={1}|TOTAL={2}' -f $projection.Status,$projection.SemanticScenarios,$projection.TotalCount}
Invoke-Case 'FI_CORRECTNESS_PROJECTION_MISMATCH_REJECTED' @{SemanticScenariosCompleted=6;TotalCountPassed=5} 'V22_CORRECTNESS_PROJECTION_MISMATCH' {Get-RejectionCode {New-V22CorrectnessEvidenceProjection -SemanticScenariosCompleted 6 -TotalCountPassed 5 -SemanticStatus 'PASS_6_OF_6' -InventoryStatus 'PASS_HISTORICAL_AND_CURRENT' -SecurityStatus 'NOT_APPLICABLE_BY_CONTRACT' -DatabaseStateStatus 'PASS'}}
function New-FinalizerFixture([string]$Role='CHILD',[string]$Run='fi-run',[string]$Candidate='fi-candidate',[bool]$Completed=$true,[bool]$ProcessGone=$true,[string]$CleanupStatus='ALREADY_EXITED',[string]$ProbeStatus='ABSENT',[bool]$IncludeKilledField=$true,[object]$KilledValue=$false,[object]$ApplicableValue=$false,[switch]$HostLimit,[int]$ExitCode=0,[string]$Level=''){
    $ownedProcessId=873800;$now=[DateTime]::UtcNow.ToString('o');$record=[ordered]@{RecordType='OWNED_PROCESS';MetadataSchemaVersion=1;Role=$Role;ChildProcessId=$ownedProcessId;StartedUtc=$now;ProcessStartUtc=$now;RunId=$Run;BlockId='fi-block';Scenario='LookupPaged';Level=$Level;FilePath='synthetic.exe';ExitCode=$ExitCode;Completed=$Completed;ProcessGone=$ProcessGone;CleanupStatus=$CleanupStatus}
    if($HostLimit){$record.HostStopEvidence=[ordered]@{Reason='HOST_STOP_HARD_FLOOR';KillIssued=[bool]$KilledValue;ActionRequested=$true;ActionCompleted=$true};$record.KilledForHostLimitApplicable=$true}elseif($null-ne$ApplicableValue){$record.KilledForHostLimitApplicable=$ApplicableValue}
    if($IncludeKilledField){$record.KilledForHostLimit=$KilledValue}
    $inventory=New-V22FinalOwnedProcessInventory @([pscustomobject]$record) $Run $Candidate;$identity=$inventory.ProcessRecords[0].ProcessIdentity
    $probe=[ordered]@{CanonicalRunId=$Run;CandidateId=$Candidate;PID=$ownedProcessId;ProcessIdentity=$identity;ProbeUtc=[DateTime]::UtcNow.ToString('o');Status=$ProbeStatus;ProcessExists=($ProbeStatus-ne'ABSENT');OwnedProcessExists=($ProbeStatus-eq'OWNED_PROCESS_PRESENT')}
    $pidEvidence=[ordered]@{CanonicalRunId=$Run;CandidateId=$Candidate;Probes=@($probe)};$helpers=@();if($Role -in @('TELEMETRY','TOOL','DB_TOOL')){$helpers=@($probe)}
    $helperEvidence=[ordered]@{CanonicalRunId=$Run;CandidateId=$Candidate;Probes=$helpers};$sql=[ordered]@{CanonicalRunId=$Run;CandidateId=$Candidate;Status='PASS';ProcessExitCode=0;Evidence=[ordered]@{Status='RESIDUE_CLEAN';TargetDatabase='TKS_Thuc_Tap_V11_Perf_10000000';DatabaseId=5};ProbeProcess=[ordered]@{PID=873899;ProbeUtc=[DateTime]::UtcNow.ToString('o');ProcessGone=$true;CleanupStatus='PASS';Status='ABSENT'}}
    return [pscustomobject]@{Record=[pscustomobject]$record;Inventory=$inventory;PidEvidence=$pidEvidence;HelperEvidence=$helperEvidence;SqlResidue=$sql}
}
$fiHostTrue=New-FinalizerFixture -HostLimit -KilledValue $true -ApplicableValue $true
Invoke-Case 'FI_FINALIZER_KILLED_TRUE' @{KilledForHostLimit=$true;Applicable=$true} 'KILL_ISSUED' {$fiHostTrue.Inventory.ProcessRecords[0].HostLimitDisposition}
$fiHostFalse=New-FinalizerFixture -HostLimit -KilledValue $false -ApplicableValue $true
Invoke-Case 'FI_FINALIZER_KILLED_FALSE' @{KilledForHostLimit=$false;Applicable=$true} 'HOST_STOP_WITHOUT_KILL' {$fiHostFalse.Inventory.ProcessRecords[0].HostLimitDisposition}
$fiOptional=New-FinalizerFixture -IncludeKilledField:$false -ApplicableValue:$null
Invoke-Case 'FI_FINALIZER_OPTIONAL_FIELD_ABSENT' @{KilledForHostLimitPropertyPresent=$false;HostLimitApplicable=$false} 'NOT_APPLICABLE' {$fiOptional.Inventory.ProcessRecords[0].HostLimitDisposition}
Invoke-Case 'FI_FINALIZER_REQUIRED_FIELD_ABSENT' @{KilledForHostLimitPropertyPresent=$false;HostLimitApplicable=$true} 'V22_FINALIZER_KILLED_FIELD_REQUIRED' {Get-RejectionCode {New-FinalizerFixture -HostLimit -IncludeKilledField:$false -KilledValue $true -ApplicableValue $true}}
Invoke-Case 'FI_FINALIZER_KILLED_NULL' @{KilledForHostLimit=$null} 'V22_FINALIZER_KILLED_FIELD_NULL' {Get-RejectionCode {New-FinalizerFixture -KilledValue $null}}
Invoke-Case 'FI_FINALIZER_KILLED_MALFORMED' @{KilledForHostLimit='yes'} 'V22_FINALIZER_KILLED_FIELD_MALFORMED' {Get-RejectionCode {New-FinalizerFixture -KilledValue 'yes'}}
$fiNormal=New-FinalizerFixture
Invoke-Case 'FI_FINALIZER_NORMAL_COMPLETED_PROCESS' @{Completed=$true;ProcessGone=$true;ExitCode=0} 'PASS' {(Test-V22FinalCleanupEvidence $fiNormal.Inventory $fiNormal.PidEvidence $fiNormal.HelperEvidence $fiNormal.SqlResidue 'fi-run' 'fi-candidate').AggregateCleanupStatus}
$fiNonzero=New-FinalizerFixture -ExitCode 17
Invoke-Case 'FI_FINALIZER_NONZERO_EXIT_CLEANUP' @{Completed=$true;ProcessGone=$true;ExitCode=17} 'PASS' {(Test-V22FinalCleanupEvidence $fiNonzero.Inventory $fiNonzero.PidEvidence $fiNonzero.HelperEvidence $fiNonzero.SqlResidue 'fi-run' 'fi-candidate').AggregateCleanupStatus}
Invoke-Case 'FI_FINALIZER_HOST_LIMIT_RECORD' @{KilledForHostLimit=$true;HostStopEvidence=$true} 'KILL_ISSUED' {$fiHostTrue.Inventory.ProcessRecords[0].HostLimitDisposition}
Invoke-Case 'FI_FINALIZER_CLEANUP_PASS' @{ProbeStatus='ABSENT';Residue='RESIDUE_CLEAN'} 'PASS' {(Test-V22FinalCleanupEvidence $fiNormal.Inventory $fiNormal.PidEvidence $fiNormal.HelperEvidence $fiNormal.SqlResidue 'fi-run' 'fi-candidate').AggregateCleanupStatus}
$fiCleanupFail=New-FinalizerFixture -CleanupStatus 'CLEANUP_FAILED'
Invoke-Case 'FI_FINALIZER_CLEANUP_FAIL' @{CleanupStatus='CLEANUP_FAILED'} 'FAIL' {(Test-V22FinalCleanupEvidence $fiCleanupFail.Inventory $fiCleanupFail.PidEvidence $fiCleanupFail.HelperEvidence $fiCleanupFail.SqlResidue 'fi-run' 'fi-candidate').AggregateCleanupStatus}
$fiOrphan=New-FinalizerFixture -ProbeStatus 'OWNED_PROCESS_PRESENT'
Invoke-Case 'FI_FINALIZER_ORPHAN_PID' @{ProbeStatus='OWNED_PROCESS_PRESENT'} 'FAIL' {(Test-V22FinalCleanupEvidence $fiOrphan.Inventory $fiOrphan.PidEvidence $fiOrphan.HelperEvidence $fiOrphan.SqlResidue 'fi-run' 'fi-candidate').AggregateCleanupStatus}
$fiTelemetry=New-FinalizerFixture -Role TELEMETRY
Invoke-Case 'FI_FINALIZER_TELEMETRY_HELPER' @{ProcessRole='TELEMETRY';HelperProbeCount=1} 'PASS' {(Test-V22FinalCleanupEvidence $fiTelemetry.Inventory $fiTelemetry.PidEvidence $fiTelemetry.HelperEvidence $fiTelemetry.SqlResidue 'fi-run' 'fi-candidate').AggregateCleanupStatus}
$fiMixed=New-FinalizerFixture -Role CHILD -Level L4
Invoke-Case 'FI_FINALIZER_MIXED_CHILD' @{ProcessRole='CHILD';Level='L4'} 'L4' {$fiMixed.Inventory.ProcessRecords[0].Level}
$fiDuplicate=$fiNormal.Record
Invoke-Case 'FI_FINALIZER_DUPLICATE_PID_IDENTITY' @{PID=$fiDuplicate.ChildProcessId;Duplicate=$true} 'V22_FINALIZER_DUPLICATE_PID' {Get-RejectionCode {New-V22FinalOwnedProcessInventory @($fiDuplicate,$fiDuplicate) 'fi-run' 'fi-candidate'}}
$fiStale=[pscustomobject]@{RecordType='OWNED_PROCESS';MetadataSchemaVersion=9;Role='CHILD';ChildProcessId=873801;StartedUtc=[DateTime]::UtcNow.ToString('o');ProcessStartUtc=[DateTime]::UtcNow.ToString('o');Completed=$true;ProcessGone=$true}
Invoke-Case 'FI_FINALIZER_STALE_SCHEMA' @{MetadataSchemaVersion=9} 'V22_FINALIZER_SCHEMA_STALE' {Get-RejectionCode {New-V22FinalOwnedProcessInventory @($fiStale) 'fi-run' 'fi-candidate'}}
$fiUnknownType=[pscustomobject]@{RecordType='UNKNOWN';MetadataSchemaVersion=1;Role='CHILD';ChildProcessId=873802;StartedUtc=[DateTime]::UtcNow.ToString('o');ProcessStartUtc=[DateTime]::UtcNow.ToString('o');Completed=$true;ProcessGone=$true;RunId='fi-run'}
Invoke-Case 'FI_FINALIZER_UNKNOWN_RECORD_TYPE' @{RecordType='UNKNOWN'} 'V22_FINALIZER_RECORD_TYPE_INVALID' {Get-RejectionCode {New-V22FinalOwnedProcessInventory @($fiUnknownType) 'fi-run' 'fi-candidate'}}
Invoke-Case 'FI_FINALIZER_MISSING_AGGREGATE_INPUT' @{Inventory=$null} 'V22_CLEANUP_INPUT_MISSING' {Get-RejectionCode {Test-V22FinalCleanupEvidence $null $fiNormal.PidEvidence $fiNormal.HelperEvidence $fiNormal.SqlResidue 'fi-run' 'fi-candidate'}}
Invoke-Case 'FI_FINALIZER_MISSING_FINAL_PID_PROBE' @{PerPidEvidence=$null} 'V22_CLEANUP_PROBE_MISSING' {Get-RejectionCode {Test-V22FinalCleanupEvidence $fiNormal.Inventory $null $fiNormal.HelperEvidence $fiNormal.SqlResidue 'fi-run' 'fi-candidate'}}
Invoke-Case 'FI_FINALIZER_RUN_ID_MISMATCH' @{CanonicalRunId='fi-run';ProtocolRunId='stale-run'} 'CANONICAL_RUN_IDENTITY_INVALID' {Get-RejectionCode {Assert-V22CanonicalRunIdentity 'fi-run' 'stale-run' 'fi-candidate' 'fi-source' 'fi-session' @([pscustomobject]@{RunId='fi-run';CandidateId='fi-candidate';BlockId='block'})}}
$fiBadCandidate=$fiNormal.HelperEvidence;$fiBadCandidate.CandidateId='other-candidate'
Invoke-Case 'FI_FINALIZER_CANDIDATE_MISMATCH' @{Expected='fi-candidate';Actual='other-candidate'} 'V22_FINALIZER_CANDIDATE_MISMATCH' {Get-RejectionCode {Test-V22FinalCleanupEvidence $fiNormal.Inventory $fiNormal.PidEvidence $fiBadCandidate $fiNormal.SqlResidue 'fi-run' 'fi-candidate'}}
$cleanupPersistRoot=Join-Path $EvidenceRoot 'cleanup-readback-fixture';New-Item -ItemType Directory -Path $cleanupPersistRoot|Out-Null
$persistFixture=New-FinalizerFixture
$persistInventory=Join-Path $cleanupPersistRoot 'inventory.json';$persistPid=Join-Path $cleanupPersistRoot 'pid.json';$persistHelper=Join-Path $cleanupPersistRoot 'helper.json';$persistSql=Join-Path $cleanupPersistRoot 'sql.json';$persistAggregate=Join-Path $cleanupPersistRoot 'aggregate.json'
$persistPidValue=[ordered]@{SchemaVersion='warehouse-benchmark-v22-final-per-pid-probes/1';CanonicalRunId='fi-run';CandidateId='fi-candidate';CapturedUtc=[DateTime]::UtcNow.ToString('o');ProbeCount=1;Probes=$persistFixture.PidEvidence.Probes}
$persistHelperValue=[ordered]@{SchemaVersion='warehouse-benchmark-v22-final-helper-probes/1';CanonicalRunId='fi-run';CandidateId='fi-candidate';CapturedUtc=[DateTime]::UtcNow.ToString('o');ProbeCount=0;Probes=@()}
$persistSqlValue=$persistFixture.SqlResidue;$persistSqlValue.CapturedUtc=[DateTime]::UtcNow.ToString('o')
New-V22FinalizerJson $persistInventory $persistFixture.Inventory;New-V22FinalizerJson $persistPid $persistPidValue;New-V22FinalizerJson $persistHelper $persistHelperValue;New-V22FinalizerJson $persistSql $persistSqlValue
$persistedAggregate=Test-V22FinalCleanupEvidence (Get-Content $persistInventory -Raw|ConvertFrom-Json) (Get-Content $persistPid -Raw|ConvertFrom-Json) (Get-Content $persistHelper -Raw|ConvertFrom-Json) (Get-Content $persistSql -Raw|ConvertFrom-Json) 'fi-run' 'fi-candidate'
$persistInputs=@($persistInventory,$persistPid,$persistHelper,$persistSql);$persistedAggregate.InputEvidence=@($persistInputs|ForEach-Object{[pscustomobject]@{Path=$_;SHA256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()}})
New-V22FinalizerJson $persistAggregate $persistedAggregate
Invoke-Case 'FI_FINAL_CLEANUP_PERSIST_AND_READBACK' @{Inputs=4;AggregatePersisted=$true} 'PASS_1' {$r=Test-V22FinalCleanupReadback $persistInventory $persistPid $persistHelper $persistSql $persistAggregate 'fi-run' 'fi-candidate';'{0}_{1}'-f$r.Status,$r.ProcessCount}
$missingPersistRoot=Join-Path $EvidenceRoot 'cleanup-readback-missing-fixture';New-Item -ItemType Directory -Path $missingPersistRoot|Out-Null
Invoke-Case 'FI_FINAL_CLEANUP_MISSING_PERSISTED_PROBE' @{PidProbePersisted=$false} 'FAIL' {(Test-V22FinalCleanupReadback $persistInventory (Join-Path $missingPersistRoot 'missing-pid.json') $persistHelper $persistSql $persistAggregate 'fi-run' 'fi-candidate').Status}
Invoke-Case 'FI_CLEANUP_COUNT_PROJECTION_86_MATCH' @{Inventory=86;PidProbes=86;AggregateExpected=86;CoreProjection=86;InMemory=87} 'PASS|SCHEMA_PROJECTION_DEFECT' {$r=New-V22CleanupCountProjection 86 86 86 86 87;'{0}|{1}'-f$r.Status,$r.Classification}
Invoke-Case 'FI_CLEANUP_COUNT_PROJECTION_87_MISMATCH' @{Inventory=86;PidProbes=86;AggregateExpected=86;CoreProjection=87} 'FAIL|CLEANUP_COUNT_INVARIANT_FAILED' {$r=New-V22CleanupCountProjection 86 86 86 87;'{0}|{1}'-f$r.Status,$r.Classification}
Invoke-Case 'FI_CLEANUP_COUNT_PROJECTION_MISSING_COUNT' @{Inventory=$null;PidProbes=0;AggregateExpected=0;CoreProjection=0} 'FAIL' {(New-V22CleanupCountProjection $null 0 0 0).Status}
$fiRun='fi-run';$fiCand='fi-candidate';$fiConfigs=New-V22RepresentativeRunConfigurations $fiRun $fiCand @('MasterPaged','LookupPaged','DocumentPaged','DetailReportPaged','InventoryHistoricalReportPaged','InventoryCurrentBalancePaged')
Invoke-Case 'FI_CONFIG_IDENTITY_C1_C2_C4_L1_L2_L4_L8' @{ConfigurationCount=$fiConfigs.Count;Profiles=@('C1','C2','C4','L1','L2','L4','L8')} 'PASS_48_SAME_RUN' {if((Assert-V22CanonicalRunIdentity $fiRun $fiRun $fiCand 'fi-source' 'fi-session' $fiConfigs).Status-eq'PASS'-and@($fiConfigs|Where-Object{$_.Profile -in @('C1','C2','C4')}).Count-eq 18-and@($fiConfigs|Where-Object{$_.Profile-eq'MIXED_REGRESSION'}).Count-eq 24){'PASS_48_SAME_RUN'}else{'FAIL'}}
Invoke-Case 'FI_CONFIG_STALE_RUN_REJECTED' @{StaleRunId='old-run'} 'CANONICAL_RUN_IDENTITY_INVALID' {Get-RejectionCode {Assert-V22CanonicalRunIdentity $fiRun $fiRun $fiCand 'fi-source' 'fi-session' @([pscustomobject]@{RunId='old-run';CandidateId=$fiCand;BlockId='stale'})}}
$fiRunnerSource=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'run-warehouse-benchmark-v2.2-performance.ps1'))
$fiIdentityIndex=$fiRunnerSource.IndexOf('V22-Canonical-Run-Identity.json',[StringComparison]::Ordinal)
$fiClaimIndex=$fiRunnerSource.IndexOf('$script:CanonicalWorkloadStarted=$true',$fiIdentityIndex,[StringComparison]::Ordinal)
$fiPreClaimSection=if($fiIdentityIndex-ge 0-and$fiClaimIndex-gt$fiIdentityIndex){$fiRunnerSource.Substring($fiIdentityIndex,$fiClaimIndex-$fiIdentityIndex)}else{''}
$fiPreClaimCalls=[regex]::Matches($fiPreClaimSection,'(?i)\bInvoke-MeasurementBlock\b').Count
Invoke-Case 'FI_NO_UNCLAIMED_TIMED_MEASUREMENT' @{IdentityMarkerFound=($fiIdentityIndex-ge 0);OneShotStartMarkerFound=($fiClaimIndex-gt$fiIdentityIndex);MeasurementCallsBeforeClaim=$fiPreClaimCalls} 'NO_PRECLAIM_TIMED_BLOCK' {if($fiIdentityIndex-ge 0-and$fiClaimIndex-gt$fiIdentityIndex-and$fiPreClaimCalls-eq 0){'NO_PRECLAIM_TIMED_BLOCK'}else{'UNCLAIMED_TIMED_BLOCK_FOUND'}}
$fiMixedTotals=@();foreach($n in @(1,2,4,8)){$r=New-V22MixedSemantics ('L'+$n);$fiMixedTotals+=$r.TotalLogicalCopies}
Invoke-Case 'FI_MIXED_SEMANTICS_1_2_4_8' @{Levels=@('L1','L2','L4','L8')} '6,12,24,48' {($fiMixedTotals -join ',')}
$fiReport=New-V22FinalReport ([ordered]@{Verdict='BATCH3_PARTIAL';CanonicalRunId='fi-run';ProtocolRunId='fi-run';IdentityStatus='PASS';CandidateId='fi-candidate';SourceSnapshotId='fi-source';ManifestSHA256=('a'*64);Gates=@{};CorrectnessPreflight='6/6';TotalCountPreflight='6/6';IsolatedC1C2='12/12';C4='6/6';BDN='6/6';PostIsolatedCorrectness='6/6';MixedL1='PASS';MixedL2='PASS';MixedL4='PASS';MixedL8='PASS';PostMixedCorrectness='6/6';MixedOverlap='PASS';MixedTelemetry='PASS';ProcessCount='0';PidProbeCount='0';CleanupStatus='NOT_RUN';CanonicalWorkloadAttempts=0;RecoveryDiagnosticStatus='NOT_RUN_NOT_IN_PROTOCOL';EvidencePaths=@('synthetic/evidence.json')})
Invoke-Case 'FI_REPORT_TEMPLATE_LINT_PASS' @{RunId='fi-run';CandidateId='fi-candidate'} 'PASS' {(Test-V22FinalReportLint $fiReport).Status}
Invoke-Case 'FI_REPORT_TEMPLATE_LINT_REJECTS_PLACEHOLDER' @{Text='Run $RunId and $(projection)'} 'FAIL' {(Test-V22FinalReportLint 'Run $RunId and $(projection)').Status}
$projectionSource=Join-Path $EvidenceRoot 'projection-source.txt';[IO.File]::WriteAllText($projectionSource,'synthetic projection',[Text.UTF8Encoding]::new($false))
$projectionRoot=Join-Path $EvidenceRoot 'projection-pack';New-Item -ItemType Directory -Path $projectionRoot|Out-Null
Invoke-Case 'FI_REVIEW_PROJECTION_COPY_HASH' @{Source=$projectionSource;Name='copy.txt'} 'COPIED' {(New-V22ReviewProjectionArtifact $projectionRoot 'copy.txt' $projectionSource 'fi-run' 'fi-candidate').Status}
$missingProjection=New-V22ReviewProjectionArtifact $projectionRoot 'missing.json' (Join-Path $EvidenceRoot 'absent-source.json') 'fi-run' 'fi-candidate'
Invoke-Case 'FI_REVIEW_PROJECTION_MISSING_FAIL_CLOSED' @{Source='absent-source.json'} 'MISSING' {$j=Get-Content -LiteralPath (Join-Path $projectionRoot 'missing.json') -Raw|ConvertFrom-Json;if($j.Status-eq'MISSING'){'MISSING'}else{'FAIL'}}
Invoke-Case 'FI_REVIEW_PROJECTION_NO_OVERWRITE' @{Name='copy.txt'} 'V22_FINALIZER_OUTPUT_EXISTS' {Get-RejectionCode {New-V22ReviewProjectionArtifact $projectionRoot 'copy.txt' $projectionSource 'fi-run' 'fi-candidate'}}
$validPack=Join-Path $EvidenceRoot 'valid-review-pack';New-Item -ItemType Directory -Path $validPack|Out-Null
New-V22FinalizerJson (Join-Path $validPack 'identity.json') ([ordered]@{RunId='fi-run';CanonicalRunId='fi-run';CandidateId='fi-candidate';Status='PASS'})
[IO.File]::WriteAllText((Join-Path $validPack 'report.md'),'Verdict: BATCH3_PARTIAL',[Text.UTF8Encoding]::new($false))
Invoke-Case 'FI_REVIEW_PACK_INTEGRITY_PASS' @{Required=@('identity.json','report.md')} 'PASS_2' {$r=Test-V22FinalCanonicalReviewPack $validPack @('identity.json','report.md') 'fi-run' 'fi-candidate';'{0}_{1}'-f$r.Status,$r.FileCount}
$stalePack=Join-Path $EvidenceRoot 'stale-review-pack';New-Item -ItemType Directory -Path $stalePack|Out-Null
New-V22FinalizerJson (Join-Path $stalePack 'identity.json') ([ordered]@{RunId='old-run';CandidateId='fi-candidate';Status='PASS'})
[IO.File]::WriteAllText((Join-Path $stalePack 'report.md'),'Verdict: BATCH3_PARTIAL',[Text.UTF8Encoding]::new($false))
Invoke-Case 'FI_REVIEW_PACK_STALE_RUN_REJECTED' @{RunId='old-run';Expected='fi-run'} 'FAIL' {(Test-V22FinalCanonicalReviewPack $stalePack @('identity.json','report.md') 'fi-run' 'fi-candidate').Status}
$invalidPack=Join-Path $EvidenceRoot 'invalid-review-pack';New-Item -ItemType Directory -Path $invalidPack|Out-Null
[IO.File]::WriteAllText((Join-Path $invalidPack 'identity.json'),'{ invalid',[Text.UTF8Encoding]::new($false))
Invoke-Case 'FI_REVIEW_PACK_INVALID_JSON_REJECTED' @{File='identity.json'} 'FAIL' {(Test-V22FinalCanonicalReviewPack $invalidPack @('identity.json') 'fi-run' 'fi-candidate').Status}
$rawFixtureRoot=Join-Path $EvidenceRoot 'raw-index-fixture';New-Item -ItemType Directory -Path $rawFixtureRoot|Out-Null
$rawData=Join-Path $rawFixtureRoot 'block.txt';[IO.File]::WriteAllText($rawData,'persisted',[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $rawFixtureRoot 'progress.json'),'mutable',[Text.UTF8Encoding]::new($false))
$rawEntry=[pscustomobject]@{RelativePath='block.txt';Size=[long](Get-Item $rawData).Length;SHA256=(Get-FileHash -LiteralPath $rawData -Algorithm SHA256).Hash.ToLowerInvariant()}
$rawIndex=Join-Path $rawFixtureRoot 'raw-artifact-hashes.json';New-V22FinalizerJson $rawIndex ([ordered]@{RunId='fi-run';Count=1;Entries=@($rawEntry);ExcludedMutableFiles=@('progress.json')})
Invoke-Case 'FI_RAW_ARTIFACT_INDEX_COMPLETE' @{Indexed=1;Excluded=@('progress.json')} 'PASS_1' {$r=Test-V22RawArtifactHashIndex $rawIndex $rawFixtureRoot 'fi-run';'{0}_{1}'-f$r.Status,$r.VerifiedCount}
[IO.File]::WriteAllText((Join-Path $rawFixtureRoot 'unindexed.txt'),'unindexed',[Text.UTF8Encoding]::new($false))
Invoke-Case 'FI_RAW_ARTIFACT_UNINDEXED_REJECTED' @{File='unindexed.txt'} 'FAIL' {(Test-V22RawArtifactHashIndex $rawIndex $rawFixtureRoot 'fi-run').Status}
$projectionSkipped=Get-V22MetricProjection ([ordered]@{Status='SKIPPED';BlockKind='C4';Requests=0;Failed=0}) 'C4'
Invoke-Case 'FI_FINALIZER_SKIPPED_OPTIONAL_METRICS' @{Status='SKIPPED';MeanMs='ABSENT'} 'NOT_MEASURED|NOT_APPLICABLE|NOT_APPLICABLE' {'{0}|{1}|{2}' -f $projectionSkipped.MetricState,(Get-V22MetricDisplayValue $projectionSkipped 'MeanMs'),(Get-V22MetricDisplayValue $projectionSkipped 'P95Ms')}
$projectionHost=Get-V22MetricProjection ([ordered]@{Status='HOST_LIMIT';BlockKind='C4';FailureType='HOST_LIMIT'}) 'C4'
Invoke-Case 'FI_FINALIZER_HOST_LIMIT_OPTIONAL_METRICS' @{Status='HOST_LIMIT';P95Ms='ABSENT'} 'NOT_MEASURED|NOT_APPLICABLE' {'{0}|{1}' -f $projectionHost.MetricState,(Get-V22MetricDisplayValue $projectionHost 'P95Ms')}
$projectionBdn=Get-V22MetricProjection ([ordered]@{Status='PASS';MeanMs=10.0;ErrorMs=1.0;StdDevMs=2.0}) 'BDN'
Invoke-Case 'FI_FINALIZER_BDN_PERCENTILES_NOT_APPLICABLE' @{Status='PASS';Kind='BDN'} 'VALID|NOT_APPLICABLE' {'{0}|{1}' -f $projectionBdn.MetricState,(Get-V22MetricDisplayValue $projectionBdn 'P95Ms')}
Invoke-Case 'FI_FINALIZER_PASS_REQUIRED_METRIC_MISSING' @{Status='PASS';Kind='BDN';MeanMs=10.0} 'V22_FINALIZER_METRIC_REQUIRED_MISSING' {Get-RejectionCode {Get-V22MetricProjection @{Status='PASS';MeanMs=10.0} 'BDN'}}
Invoke-Case 'FI_FINALIZER_PASS_NONFINITE_METRIC_REJECTED' @{Status='PASS';Kind='C4';P95Ms='NaN'} 'V22_FINALIZER_METRIC_INVALID' {Get-RejectionCode {Get-V22MetricProjection @{Status='PASS';Requests=10;Failed=0;RPS=1;MeanMs=1;P50Ms=1;P95Ms='NaN';P99Ms=1;MaxMs=1} 'C4'}}
$validBdn=New-BdnFixture (Join-Path $EvidenceRoot 'FI_BDN_RESULT_SOURCE_VALID') '130'
Invoke-Case 'FI_BDN_RESULT_SOURCE_VALID' @{MeanNs=130;N=2;Stage='Result'} 'BDN_COMPLETE|N=2|Rows=2' {$b=Assert-V22BdnEvidence $validBdn.SummaryPath $validBdn.MeasurementsPath;'{0}|N={1}|Rows={2}' -f $b.Status,$b.MeasuredIterations,$b.ResultMeasurementRows}
$fiMaterialized=New-V22FinalReport ([ordered]@{Verdict='BATCH3_PARTIAL';CanonicalRunId='fi-run';ProtocolRunId='fi-run';IdentityStatus='PASS';CandidateId='fi-candidate';SourceSnapshotId='fi-source';ManifestSHA256=('a'*64);BenchmarkDllSHA256=('b'*64);DataAccessDllSHA256=('c'*64);SourceInventoryCount=181;SourceInventorySHA256=('d'*64);RuntimeInventoryCount=151;RuntimeInventorySHA256=('e'*64);Gates=@{Preflight='PASS';ReportLint='PASS'};CorrectnessPreflight='6/6';TotalCountPreflight='6/6';IsolatedC1C2='12/12';C4='6/6';BDN='6/6';PostIsolatedCorrectness='6/6';MixedL1='PASS';MixedL2='PASS';MixedL4='PASS';MixedL8='PASS';PostMixedCorrectness='6/6';MixedOverlap='PASS';MixedTelemetry='PASS';ProcessCount=3;PidProbeCount=3;HelperProbeCount=1;CleanupStatus='PASS';PrePostStatus='PASS';PreservationStatus='PASS';RawArtifactCount=55;RawArtifactIndexSHA256=('f'*64);CanonicalWorkloadAttempts=1;RecoveryDiagnosticStatus='NOT_RUN_NOT_IN_PROTOCOL';HistoricalTimeoutCause='NOT_VERIFIED';FullDatasetValueEquality='NOT_VERIFIED';PerformanceSLA='NO_SLA_DEFINED';CausalAttribution='NOT_ESTABLISHED';HistoricalHostLimitActionTimestamp='NOT_RECORDED';CoreEvidenceReady='NO';ReadyForBatch4='NO';StopReason='synthetic';FailureStage='synthetic';EvidencePaths=@('synthetic/evidence.json')})
Invoke-Case 'FI_REPORT_MATERIALIZES_ID_HASH_AND_COUNTS' @{RunId='fi-run';CandidateId='fi-candidate';SourceCount=181} 'MATERIALIZED' {if($fiMaterialized.Contains('fi-run')-and$fiMaterialized.Contains('fi-candidate')-and$fiMaterialized.Contains(('b'*64))-and$fiMaterialized.Contains('Source inventory count: 181')-and$fiMaterialized.Contains('Correctness preflight: 6/6')){'MATERIALIZED'}else{'FAIL'}}
$lifecycleRoot=Join-Path $EvidenceRoot 'lifecycle'
& (Join-Path $PSScriptRoot 'Invoke-V22LifecycleTests.ps1') -EvidenceRoot $lifecycleRoot | Out-Null
$lifecycle=Get-Content -LiteralPath (Join-Path $lifecycleRoot 'V22-Lifecycle-Results.json') -Raw|ConvertFrom-Json
foreach($test in $lifecycle.Tests){$script:Results.Add($test)}
$summaryPath=Join-Path $EvidenceRoot 'V22-Failure-Injection-Results.json'
$failed=@($script:Results|Where-Object Status -ceq 'FAIL').Count
$result=[ordered]@{SchemaVersion='warehouse-benchmark-v22-failure-injection/1';ProtocolVersion='2.2';DatabaseAccess='NOT_USED';PerformanceLoad='NOT_RUN';TestCount=$script:Results.Count;Passed=($script:Results.Count-$failed);Failed=$failed;Status=$(if($failed -eq 0){'PASS'}else{'FAIL'});Tests=$script:Results.ToArray();RecordedUtc=[DateTime]::UtcNow.ToString('o')}
$json=$result|ConvertTo-Json -Depth 12
$stream=[IO.File]::Open($summaryPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($json+[Environment]::NewLine);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
$result|Select-Object Status,TestCount,Passed,Failed
if($failed -gt 0){exit 1}




