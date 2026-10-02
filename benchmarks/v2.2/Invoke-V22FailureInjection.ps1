[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EvidenceRoot)
$ErrorActionPreference='Stop'
$harnessPath=Join-Path $PSScriptRoot 'WarehouseBenchmarkV22.Harness.psm1'
Import-Module $harnessPath
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
function Get-RejectionCode([scriptblock]$Action){try{[void](& $Action);return 'ACCEPTED'}catch{$message=$_.Exception.Message;if($message -match '^(V22_[A-Z0-9_]+)\|'){return $Matches[1]};return 'UNCLASSIFIED_REJECTION'}}
function New-BdnFixture([string]$Dir,[string]$Mean){
    if(-not(Test-Path -LiteralPath $Dir)){New-Item -ItemType Directory -Path $Dir|Out-Null}
    $summary=Join-Path $Dir 'summary.csv';$measure=Join-Path $Dir 'measurements.csv'
    Set-Content -LiteralPath $summary -Value ("Method,Mean,Error,StdDev,Unit`nSynthetic,{0},1 ns,2 ns,ns" -f $Mean) -Encoding utf8
    Set-Content -LiteralPath $measure -Value "IterationStage,Operations,Nanoseconds`nActual,1,120`nActual,1,140" -Encoding utf8
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
$summaryPath=Join-Path $EvidenceRoot 'V22-Failure-Injection-Results.json'
$failed=@($script:Results|Where-Object Status -ceq 'FAIL').Count
$result=[ordered]@{SchemaVersion='warehouse-benchmark-v22-failure-injection/1';ProtocolVersion='2.2';DatabaseAccess='NOT_USED';PerformanceLoad='NOT_RUN';TestCount=$script:Results.Count;Passed=($script:Results.Count-$failed);Failed=$failed;Status=$(if($failed -eq 0){'PASS'}else{'FAIL'});Tests=$script:Results.ToArray();RecordedUtc=[DateTime]::UtcNow.ToString('o')}
$json=$result|ConvertTo-Json -Depth 12
$stream=[IO.File]::Open($summaryPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($json+[Environment]::NewLine);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
$result|Select-Object Status,TestCount,Passed,Failed
if($failed -gt 0){exit 1}




