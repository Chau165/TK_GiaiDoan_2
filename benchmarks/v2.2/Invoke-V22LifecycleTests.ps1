[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EvidenceRoot)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'WarehouseBenchmarkV22.Harness.psm1')
Import-Module (Join-Path $PSScriptRoot 'WarehouseBenchmarkV22.Finalizer.psm1')
if(Test-Path -LiteralPath $EvidenceRoot){throw 'V22_LIFECYCLE_TEST_ROOT_EXISTS'}
New-Item -ItemType Directory -Path $EvidenceRoot|Out-Null
$results=[Collections.Generic.List[object]]::new()
function Case([string]$Id,[object]$InputValue,[string]$Expected,[scriptblock]$Action){
    try{$actual=[string](& $Action)}catch{$actual='THREW:'+($_.Exception.Message -replace '\s+',' ')}
    $path=Join-Path $EvidenceRoot ($Id+'.json')
    $row=[ordered]@{TestId=$Id;Input=$InputValue;Expected=$Expected;Actual=$actual;Status=if($actual-ceq$Expected){'PASS'}else{'FAIL'};EvidencePath=$path}
    New-V22FinalizerJson $path $row;$results.Add([pscustomobject]$row)
}
function State([string]$Stage='SESSION_CREATED'){
    return [ordered]@{RunId='WHB22-LIFECYCLE-FIXTURE';SessionId='fixture-session';Stage=$Stage;CandidateId=$null;CandidateCreated=$false;BuildStarted=$false;DbPreflightStarted=$false;WorkloadStarted=$false;PerformanceRunRootCreated=$false}
}
function Disposition([object]$State){
    return [ordered]@{RunId=$State.RunId;CandidateId=$State.CandidateId;Verdict=if($State.WorkloadStarted){'BATCH3_PARTIAL'}else{'BATCH3_NOT_READY'};OriginalFailureStage=$State.Stage;OriginalFailureReason='SYNTHETIC';SafeExceptionMessage='synthetic';FinalizerStage='FINALIZATION';FinalizerFailure='NONE';CandidateCreated=$State.CandidateCreated;BuildStarted=$State.BuildStarted;DbPreflightStarted=$State.DbPreflightStarted;WorkloadStarted=$State.WorkloadStarted;CanonicalWorkloadAttemptConsumed=$State.WorkloadStarted;PerformanceRunRootCreated=$State.PerformanceRunRootCreated;RawArtifactState='NOT_CREATED_BEFORE_ABORT';CleanupStatus='NOT_APPLICABLE_PREWORKLOAD';OwnedProcessCount=0;PreservationStatus='NOT_RUN'}
}
$s=State
$runnerPath=Join-Path $PSScriptRoot 'run-warehouse-benchmark-v2.2-performance.ps1'
$tokens=$null;$parseErrors=$null;$runnerAst=[Management.Automation.Language.Parser]::ParseFile($runnerPath,[ref]$tokens,[ref]$parseErrors)
$deliveryIf=$runnerAst.FindAll({param($node) $node -is [Management.Automation.Language.IfStatementAst] -and $node.Extent.Text -like '*FINAL_REVIEW_DELIVERY_ARTIFACT_EXISTS*'},$true)[0]
$deliveryCondition=[scriptblock]::Create($deliveryIf.Clauses[0].Item1.Extent.Text)
$script:FinalReviewZipPath=Join-Path $EvidenceRoot 'absent.zip';$script:FinalReviewZipHashPath=Join-Path $EvidenceRoot 'absent.sha';$script:FinalReviewHashesPath=Join-Path $EvidenceRoot 'absent-hashes.json'
Case 'REGRESSION_OUTPUT_ABSENCE_REAL_AST' @{Paths='3 nonexistent paths';Source=$runnerPath} 'False' {[string](& $deliveryCondition)}
[IO.File]::WriteAllText($script:FinalReviewZipHashPath,'sentinel',[Text.UTF8Encoding]::new($false))
Case 'REGRESSION_EXISTING_OUTPUT_REAL_AST' @{Existing='hash sentinel';Source=$runnerPath} 'True' {[string](& $deliveryCondition)}
$preservationSource=Join-Path $PSScriptRoot 'Capture-V22PreservationPost.ps1'
Case 'REGRESSION_PRESERVATION_VARIABLE_ASSIGNMENTS' @{Source=$preservationSource} 'NO_ESCAPED_ASSIGNMENT' {if([IO.File]::ReadAllText($preservationSource)-notmatch'(?m)^`\$\w+\s*='){'NO_ESCAPED_ASSIGNMENT'}else{'BAD_ASSIGNMENT'}}
Case 'L01_ABORT_BEFORE_ROOT' $s 'NOT_CREATED_BEFORE_ABORT' {(Get-V22RawEvidenceState (Join-Path $EvidenceRoot 'absent')).Status}
Case 'L02_ABORT_BEFORE_CANDIDATE' $s 'NO_CANDIDATE_CREATED' {(Get-V22EarlyCleanupDisposition $s @()).CandidateState}
foreach($pair in @(@('L03_BEFORE_SNAPSHOT','SOURCE_SNAPSHOT'),@('L04_BEFORE_BUILD','BUILD_A'),@('L05_DURING_BUILD','BUILD_B'))){
    $state=State $pair[1];$state.BuildStarted=($pair[1]-ceq'BUILD_B')
    Case $pair[0] $state $pair[1] {(New-V22CrashFailure $state ([Exception]::new('synthetic')) 'SYNTHETIC').OriginalFailureStage}
}
Case 'L06_NULL_CANDIDATE_VALID' $s 'ACCEPTED' {[void](Assert-V22LifecycleState $s);'ACCEPTED'}
Case 'L07_NULL_RUN_ROOT' @{Root=$null} 'NOT_CREATED_BEFORE_ABORT' {(Get-V22RawEvidenceState $null).Status}
Case 'L08_ZERO_OWNED' $s 'NO_OWNED_PROCESS_STARTED' {(Get-V22EarlyCleanupDisposition $s @()).ResourceState}
Case 'L09_ZERO_RESOURCE_CLEANUP' $s 'CLEANUP_NOT_APPLICABLE_PREWORKLOAD' {(Get-V22EarlyCleanupDisposition $s @()).Status}
$failure=New-V22CrashFailure $s ([Exception]::new('primary-X')) 'PRIMARY'
$dual=Join-Path $EvidenceRoot 'dual';New-Item -ItemType Directory -Path $dual|Out-Null
New-V22FinalizerJson (Join-Path $dual 'Original-Failure.json') $failure
try{throw 'secondary-Y'}catch{New-V22FinalizerJson (Join-Path $dual 'Finalizer-Failure.json') @{Stage='FINALIZATION';Message=$_.Exception.Message}}
Case 'L10_PRIMARY_SECONDARY_PERSISTED' @{Primary='primary-X';Secondary='secondary-Y'} 'BOTH_PERSISTED' {$a=Get-Content (Join-Path $dual 'Original-Failure.json') -Raw|ConvertFrom-Json;$b=Get-Content (Join-Path $dual 'Finalizer-Failure.json') -Raw|ConvertFrom-Json;if($a.SafeExceptionMessage-ceq'primary-X'-and$b.Message-ceq'secondary-Y'){'BOTH_PERSISTED'}else{'FAIL'}}
Case 'L11_MISSING_ROOT_ENUMERATION' $s '0' {[string](Get-V22RawEvidenceState (Join-Path $EvidenceRoot 'missing')).Count}
Case 'L12_NO_CANDIDATE_PRESERVATION' $s 'NO_CANDIDATE_CREATED' {(Get-V22EarlyCleanupDisposition $s @()).CandidateState}
Case 'L13_ABORT_REPORT' $s 'PASS' {(Test-V22FinalReportLint (New-V22AbortReport (Disposition $s))).Status}
$acceptance=Disposition $s
New-V22FinalizerJson (Join-Path $EvidenceRoot 'absent-candidate-acceptance.json') $acceptance
Case 'L14_ABSENT_CANDIDATE_ACCEPTANCE' $s 'BATCH3_NOT_READY' {(Get-Content (Join-Path $EvidenceRoot 'absent-candidate-acceptance.json') -Raw|ConvertFrom-Json).Verdict}
Case 'L15_ORIGINAL_STAGE_IMMUTABLE' $s 'SESSION_CREATED' {$s.Stage='BUILD_A';$failure.OriginalFailureStage}
Case 'L16_FINALIZER_STAGE_SEPARATE' $s 'SESSION_CREATED|FINALIZATION' {$secondary=Get-Content (Join-Path $dual 'Finalizer-Failure.json') -Raw|ConvertFrom-Json;'{0}|{1}'-f$failure.OriginalFailureStage,$secondary.Stage}
$priorConnection=[Environment]::GetEnvironmentVariable('TKS_V22_CONNECTION_STRING','Process')
try{
    [Environment]::SetEnvironmentVariable('TKS_V22_CONNECTION_STRING','Server=SYNTHETIC;Database=SYNTHETIC;Password=fixture-secret;','Process')
    Case 'L17_SECRET_REDACTION' @{Secret='synthetic-only'} 'REDACTED' {$f=New-V22CrashFailure $s ([Exception]::new('Server=SYNTHETIC;Database=SYNTHETIC;Password=fixture-secret;')) 'SYNTHETIC';$j=$f|ConvertTo-Json;if($j-notmatch'fixture-secret|Server=SYNTHETIC'){'REDACTED'}else{'LEAK'}}
}finally{[Environment]::SetEnvironmentVariable('TKS_V22_CONNECTION_STRING',$priorConnection,'Process')}
Case 'L18_MALFORMED_STATE_FAIL_CLOSED' @{WorkloadStarted=$true;CandidateCreated=$false} 'REJECTED' {$bad=State;$bad.WorkloadStarted=$true;try{[void](Assert-V22LifecycleState $bad);'ACCEPTED'}catch{if($_.Exception.Message-like'V22_LIFECYCLE_INVALID|*'){'REJECTED'}else{throw}}}
foreach($stage in @('SOURCE_SNAPSHOT','BUILD_A','BUILD_B','CANDIDATE_FROZEN','DB_PREFLIGHT','ISOLATED_PERFORMANCE','ISOLATED_POST_CORRECTNESS','MIXED_PERFORMANCE','POST_MIXED','FINAL_CANDIDATE_GUARD_AND_PRESERVATION','FINALIZATION')){
    $state=State $stage
    if($stage-notin@('SOURCE_SNAPSHOT','BUILD_A','BUILD_B')){$state.CandidateCreated=$true;$state.CandidateId='fixture-candidate';$state.PerformanceRunRootCreated=$true;$state.BuildStarted=$true}
    if($stage-in@('DB_PREFLIGHT','ISOLATED_PERFORMANCE','ISOLATED_POST_CORRECTNESS','MIXED_PERFORMANCE','POST_MIXED','FINAL_CANDIDATE_GUARD_AND_PRESERVATION','FINALIZATION')){$state.DbPreflightStarted=$true}
    if($stage-in@('ISOLATED_PERFORMANCE','ISOLATED_POST_CORRECTNESS','MIXED_PERFORMANCE','POST_MIXED','FINAL_CANDIDATE_GUARD_AND_PRESERVATION','FINALIZATION')){$state.WorkloadStarted=$true}
    Case ('STATE_'+$stage) $state 'PASS' {$f=New-V22CrashFailure $state ([Exception]::new('synthetic')) 'SYNTHETIC';$p=Join-Path $EvidenceRoot ($stage+'-failure.json');New-V22FinalizerJson $p $f;(Test-V22FinalReportLint (New-V22AbortReport (Disposition $state))).Status}
}
foreach($kind in @('PASS','PREWORKLOAD_ABORT','CANDIDATE_INVALID','HOST_LIMIT','ISOLATED_FAIL','MIXED_FAIL','FINALIZER_FAIL','PARTIAL','CORRECTNESS_FAIL')){
    $values=Disposition (State);$values.OriginalFailureReason=$kind;$values.Verdict=if($kind-ceq'PASS'){'BATCH3_PASS'}elseif($kind-in@('PREWORKLOAD_ABORT','CANDIDATE_INVALID','CORRECTNESS_FAIL')){'BATCH3_NOT_READY'}else{'BATCH3_PARTIAL'}
    $report=New-V22AbortReport $values
    [IO.File]::WriteAllText((Join-Path $EvidenceRoot ($kind+'-fixture.md')),$report,[Text.UTF8Encoding]::new($false))
    Case ('REPORT_'+$kind) @{ReportType=$kind} 'PASS' {(Test-V22FinalReportLint $report).Status}
}
foreach($token in @('$RunId','$CandidateId','$manifestHash','$(','${','$script:','$_.','System.Object[]')){Case ('LINT_'+[Convert]::ToHexString([Text.Encoding]::UTF8.GetBytes($token))) @{Token=$token} 'FAIL' {(Test-V22FinalReportLint ('Unresolved: '+$token)).Status}}
# Execute the real entry-point failure path, without an existing repo/evidence root or connection.
$integration=Join-Path $EvidenceRoot 'real-runner-early-abort'
$integrationLog=Join-Path $EvidenceRoot 'real-runner-early-abort.log'
& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $PSScriptRoot 'run-warehouse-benchmark-v2.2-performance.ps1') -RepoRoot (Join-Path $EvidenceRoot 'nonexistent-repo') -BatchRoot $integration -PriorEvidenceRoot (Join-Path $EvidenceRoot 'nonexistent-prior') *> $integrationLog
$integrationExit=$LASTEXITCODE
$crashPath=Get-ChildItem -LiteralPath $integration -Filter 'Original-Failure.json' -File -Recurse|Select-Object -First 1 -ExpandProperty FullName
$acceptPath=Get-ChildItem -LiteralPath $integration -Filter 'Abort-Acceptance.json' -File -Recurse|Select-Object -First 1 -ExpandProperty FullName
Case 'INTEGRATION_REAL_RUNNER_EARLY_ABORT' @{Exit=$integrationExit;Log=$integrationLog} 'ORIGINAL_PERSISTED_NO_SECONDARY' {$crash=Get-Content -LiteralPath $crashPath -Raw|ConvertFrom-Json;$out=Get-Content -LiteralPath $acceptPath -Raw|ConvertFrom-Json;if($integrationExit-eq1-and$crash.OriginalFailureStage-ceq'INITIALIZE'-and$out.FinalizerFailure-ceq'NONE'-and-not$out.WorkloadStarted-and$out.CleanupStatus-ceq'CLEANUP_NOT_APPLICABLE_PREWORKLOAD'){'ORIGINAL_PERSISTED_NO_SECONDARY'}else{'FAIL'}}
Case 'INTEGRATION_ORIGINAL_STAGE_SURVIVES_FINALLY' @{Acceptance=$acceptPath} 'INITIALIZE|POST_PRESERVATION_CHECK' {$out=Get-Content -LiteralPath $acceptPath -Raw|ConvertFrom-Json;'{0}|{1}'-f$out.OriginalFailureStage,$out.FinalizerStage}
$dualRoot=Join-Path $EvidenceRoot 'real-runner-dual-failure'
& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $PSScriptRoot 'run-warehouse-benchmark-v2.2-performance.ps1') -BatchRoot $dualRoot -OfflineFailureFixture FINALIZER_AFTER_ORIGINAL *> (Join-Path $EvidenceRoot 'dual-integration.log')
$dualExit=$LASTEXITCODE
$dualAcceptance=Get-ChildItem -LiteralPath $dualRoot -Filter 'Abort-Acceptance.json' -File -Recurse|Select-Object -First 1 -ExpandProperty FullName
Case 'INTEGRATION_REAL_SECONDARY_FAILURE_ISOLATION' @{Exit=$dualExit;Path=$dualAcceptance} 'BOTH_PERSISTED_NO_WORKLOAD' {$a=Get-Content -LiteralPath $dualAcceptance -Raw|ConvertFrom-Json;$dir=Split-Path $dualAcceptance;if($dualExit-eq1-and$a.SafeExceptionMessage-ceq'OFFLINE_FIXTURE_ORIGINAL_FAILURE'-and$a.FinalizerFailure-ceq'OFFLINE_FIXTURE_SECONDARY_FINALIZER_FAILURE'-and-not$a.WorkloadStarted-and(Test-Path (Join-Path $dir 'Original-Failure.json'))-and(Test-Path (Join-Path $dir 'Finalizer-Failure.json'))){'BOTH_PERSISTED_NO_WORKLOAD'}else{'FAIL'}}
$failed=@($results|Where-Object Status -cne 'PASS').Count
$summary=[ordered]@{SchemaVersion='warehouse-benchmark-v22-lifecycle-tests/1';Status=if($failed){'FAIL'}else{'PASS'};Passed=$results.Count-$failed;TestCount=$results.Count;Failed=$failed;DatabaseAccess='NOT_USED';PerformanceLoad='NOT_RUN';Tests=$results.ToArray()}
New-V22FinalizerJson (Join-Path $EvidenceRoot 'V22-Lifecycle-Results.json') $summary
$summary|Select-Object Status,Passed,TestCount,Failed
if($failed){$results|Where-Object Status -cne 'PASS'|Select-Object TestId,Actual;exit 1}
