<#+
.SYNOPSIS
Runs Warehouse Benchmark 2.2 Batch 3 after immutable-input and correctness gates.
.DESCRIPTION
Builds and attests a fresh performance candidate, runs bounded correctness, then the
historical isolated and mixed read-only profiles. Every output path is new and every
child process is owned and tracked by this runner.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot='P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11',
    [string]$BatchRoot='P:\Warehouse-Benchmark-V2\BATCH3_FINAL_CANONICAL_RERUN_20261002T080601Z',
    [string]$PriorEvidenceRoot='P:\Warehouse-Benchmark-V2\BATCH3_FINAL_CANONICAL_RERUN_20261002T080601Z',
    [ValidateSet('NONE','BEFORE_ROOT','FINALIZER_AFTER_ORIGINAL')][string]$OfflineFailureFixture='NONE'
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

$script:RepoRoot=[IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')
$script:BatchRoot=[IO.Path]::GetFullPath($BatchRoot)
$script:PriorReviewRoot=Join-Path ([IO.Path]::GetFullPath($PriorEvidenceRoot)) 'Batch3-Review-Pack'
$script:ReviewRoot=Join-Path $script:BatchRoot 'Batch3-Canonical-Run-Evidence'
$script:FinalReviewRoot=Join-Path $script:BatchRoot 'Batch3-Final-Canonical-Review-Pack'
$script:ReportLintPath=Join-Path $script:ReviewRoot 'V22-Report-Template-Lint-Prebuild.json'
$script:FinalReportLintPath=Join-Path $script:FinalReviewRoot 'V22-Report-Template-Lint.json'
$script:FinalReviewZipPath=Join-Path $script:BatchRoot 'Batch3-Final-Canonical-Review-Pack.zip'
$script:FinalReviewZipHashPath=Join-Path $script:BatchRoot 'Batch3-Final-Canonical-Review-Pack-SHA256.txt'
$script:FinalReviewHashesPath=Join-Path $script:BatchRoot 'Batch3-Final-Canonical-Review-Pack-File-Hashes.json'
$script:LogsRoot=Join-Path $script:BatchRoot 'logs'
$script:OneShotClaimPath=Join-Path $script:BatchRoot 'V22-Canonical-Workload-One-Shot-Claim.json'
$script:ModulePath=Join-Path $PSScriptRoot 'WarehouseBenchmarkV22.Harness.psm1'
$script:FinalizerModulePath=Join-Path $PSScriptRoot 'WarehouseBenchmarkV22.Finalizer.psm1'
$script:CandidateBuilder=Join-Path $PSScriptRoot 'Build-Freeze-V22PerformanceCandidate.ps1'
$script:CandidateGuard=Join-Path $PSScriptRoot 'Test-V22PerformanceCandidateGuard.ps1'
$script:InventoryBuilder=Join-Path $PSScriptRoot 'New-V22CanonicalSourceInventory.ps1'
$script:OfflineSelfTest=Join-Path $PSScriptRoot 'Invoke-V22OfflineSelfTest.ps1'
$script:FailureInjection=Join-Path $PSScriptRoot 'Invoke-V22FailureInjection.ps1'
$script:MixedHarnessTests=Join-Path $PSScriptRoot 'Invoke-V22MixedHarnessTests.ps1'
$script:PreservationPost=Join-Path $PSScriptRoot 'Capture-V22PreservationPost.ps1'
$script:NuGetCacheInventoryScript=Join-Path $PSScriptRoot 'New-V22NuGetCacheInventory.ps1'
$script:NuGetCacheRoot='C:\Users\Surface\.nuget\packages'
$script:NuGetCachePrePath=Join-Path $script:LogsRoot 'V22-NuGet-External-Package-Inventory-Pre.json'
$script:TargetDatabase='TKS_Thuc_Tap_V11_Perf_10000000'
$script:Scenarios=@('MasterPaged','LookupPaged','DocumentPaged','DetailReportPaged','InventoryHistoricalReportPaged','InventoryCurrentBalancePaged')
$script:AllOwnedProcesses=[Collections.Generic.List[object]]::new()
$script:IsolatedRows=[Collections.Generic.List[object]]::new()
$script:MixedLevels=[Collections.Generic.List[object]]::new()
$script:MixedRows=[Collections.Generic.List[object]]::new()
$script:TelemetryAssessments=[Collections.Generic.List[object]]::new()
$script:CleanupRows=[Collections.Generic.List[object]]::new();$script:FinalCleanupProof=$null;$script:FinalCleanupEvidence=$null;$script:PersistedOwnedInventory=$null;$script:CleanupCountProjection=$null
$script:HostStopRows=[Collections.Generic.List[object]]::new()
$script:CurrentStage='INITIALIZE'
$script:OverallStatus='BATCH3_NOT_READY'
$script:StopReason=$null
$script:Failure=$null
$script:MixedHarnessTestsPath=$null;$mixedTestResult=$null;$mixedHarnessOfflinePass=$false;$script:MixedOverlapStatus='NOT_RUN'
$script:CandidateId=$null
$script:SourceSnapshotId=$null
$script:CandidateManifestHash=$null
$script:CandidateGuardInitialStatus='NOT_RUN'
$script:CandidateGuardCanonicalStatus='NOT_RUN'
$script:CandidateGuardFinalStatus='NOT_RUN'
$script:CanonicalRunIdentityStatus='NOT_RUN'
$script:StorageAdmissionStatus='NOT_RUN'
$script:PreSummarySourcePath=$null
$script:DatabaseActivityStarted=$false
$script:CanonicalWorkloadStarted=$false
$script:PreflightStatus='NOT_RUN'
$script:RecoveryDiagnosticStatus='NOT_RUN_NOT_IN_PROTOCOL'
$script:RecoveryDiagnostic=$null
$script:ProbeStatus='NOT_RUN'
$script:ComparatorStatus='NOT_VERIFIED'
$script:PreservationStatus='NOT_RUN'
$script:IsolatedGate='NOT_RUN'
$script:MixedGate='NOT_RUN'
$script:PrePostStatus='NOT_RUN'
$script:RunId=('WHB22-PERF-RUN-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8).ToUpperInvariant())
$script:CanonicalRunId=$script:RunId
$script:SessionId=('WHB22-SESSION-'+[Guid]::NewGuid().ToString('N').Substring(0,12).ToUpperInvariant())
$script:PerformanceRunRoot=Join-Path $script:BatchRoot ('candidate-preflight-'+$script:CanonicalRunId)
$script:OneShotStartPath=Join-Path $script:PerformanceRunRoot 'canonical-workload-start.json'
$script:HostStopPath=Join-Path $script:PerformanceRunRoot 'host-stop-actions.jsonl'
$script:ProtocolInfo=[ordered]@{ProtocolVersion='2.2';RunId=$script:RunId;CandidateId=$null;Status='INITIALIZING';Scenarios=$script:Scenarios;IsolatedOrder='Per scenario: BDN, C1, C2, C4';NBomber=[ordered]@{Version='6.6.0';WarmupSeconds=3;TimedSeconds=15;LoadModel='KeepConstant';CopiesMeaning='One copy is one worker';ObservedCopiesEvidence='Unique ScenarioInfo.InstanceNumber values recorded by the scenario callback';ApplicationTimeoutSeconds=30;IndividualRequestRetry=$false};BDN=[ordered]@{Version='0.15.8';Toolchain='InProcessNoEmit';LaunchCount=1;WarmupCount=2;ConfiguredIterationCount=5;InvocationCount=1;UnrollFactor=1;OutlierMode='RemoveUpper, BenchmarkDotNet Job.Default';StatisticalMeasurements='WorkloadActual paired with WorkloadResult; configured iterations, statistical N, upper fence, and Mean reconciled from WorkloadResult'};MixedLevels=@([ordered]@{Level='L1';ScenarioProcessCount=6;CopiesPerScenario=1;TotalLogicalCopies=6;TotalWorkers=6},[ordered]@{Level='L2';ScenarioProcessCount=6;CopiesPerScenario=2;TotalLogicalCopies=12;TotalWorkers=12},[ordered]@{Level='L4';ScenarioProcessCount=6;CopiesPerScenario=4;TotalLogicalCopies=24;TotalWorkers=24},[ordered]@{Level='L8';ScenarioProcessCount=6;CopiesPerScenario=8;TotalLogicalCopies=48;TotalWorkers=48});MinimumMixedCommonOverlapSeconds=10;HostPolicy=[ordered]@{HardFloorMB=512;EmergencyFloorMB=128;SafeMinimumMB=640;AdmissionSamples=2;AdmissionSampleIntervalSeconds=1;AdmissionWaitLimitSeconds=60;CooldownMinimumSeconds=5;CooldownMaximumSeconds=60};Database=[ordered]@{Name=$script:TargetDatabase;ExpectedDatabaseId=5;ReadOnly=$true;HistoricalReportMode='LEGACY'};PerformanceConclusion='DESCRIPTIVE_ONLY_NO_SLA_THRESHOLD'}

$script:OriginalFailure=$null;$script:FinalizerFailure=$null;$script:FinalizerStage='NOT_STARTED'
$script:CandidateCreated=$false;$script:BuildStarted=$false
$script:FailureRoot=Join-Path $script:LogsRoot ('failures\'+$script:RunId)
if(-not(Test-Path -LiteralPath $script:LogsRoot -PathType Container)){New-Item -ItemType Directory -Path $script:LogsRoot|Out-Null}
New-Item -ItemType Directory -Path $script:FailureRoot|Out-Null
Import-Module $script:ModulePath -ErrorAction Stop
Import-Module $script:FinalizerModulePath -ErrorAction Stop

function Get-RunnerLifecycle{
    return [ordered]@{RunId=$script:RunId;SessionId=$script:SessionId;Stage=$script:CurrentStage;CandidateId=$script:CandidateId;CandidateCreated=[bool]$script:CandidateCreated;BuildStarted=[bool]$script:BuildStarted;DbPreflightStarted=[bool]$script:DatabaseActivityStarted;WorkloadStarted=[bool]$script:CanonicalWorkloadStarted;PerformanceRunRootCreated=([string]::IsNullOrWhiteSpace($script:PerformanceRunRoot)-eq$false -and (Test-Path -LiteralPath $script:PerformanceRunRoot -PathType Container))}
}
function Save-OriginalFailure([object]$ErrorRecord){
    if($null -ne $script:OriginalFailure){return}
    $script:OriginalFailure=New-V22CrashFailure (Get-RunnerLifecycle) $ErrorRecord.Exception 'STAGE_FAILED'
    $script:Failure=[ordered]@{Stage=$script:OriginalFailure.OriginalFailureStage;Message=$script:OriginalFailure.SafeExceptionMessage;RecordedUtc=$script:OriginalFailure.RecordedUtc}
    New-V22FinalizerJson (Join-Path $script:FailureRoot 'Original-Failure.json') $script:OriginalFailure
}
function Save-AbortDisposition{
    $state=Get-RunnerLifecycle
    $rawState=Get-V22RawEvidenceState $script:PerformanceRunRoot
    $cleanupStatus='NOT_VERIFIED'
    if(-not $state.DbPreflightStarted -and -not $state.WorkloadStarted){
        $early=Get-V22EarlyCleanupDisposition $state $script:AllOwnedProcesses.ToArray()
        if($early.Status -ceq 'REQUIRES_OWNED_PROCESS_PROBES'){
            $probes=@(foreach($record in $script:AllOwnedProcesses){$identity=[pscustomobject]@{PID=$record.ChildProcessId;ProcessIdentity=('{0}|{1}' -f $record.ChildProcessId,$record.ProcessStartUtc);ProcessStartUtc=$record.ProcessStartUtc};$probe=Get-FinalProcessProbe $identity;[ordered]@{RunId=$record.RunId;Label=$record.Label;PID=$record.ChildProcessId;Probe=$probe;Completed=$record.Completed;ProcessGone=$record.ProcessGone;CleanupStatus=$record.CleanupStatus}})
            $early.Probes=$probes
            $early.Status=if(@($probes|Where-Object{-not $_.Completed -or -not $_.ProcessGone -or $_.CleanupStatus -cne 'PASS' -or $_.Probe.Status -notin @('ABSENT','PID_REUSED_DIFFERENT_PROCESS')}).Count -eq 0){'PASS_RESOURCE_CLEANUP_PREWORKLOAD'}else{'CLEANUP_FAILED'}
        }
        $cleanupStatus=$early.Status
        if(-not(Test-Path -LiteralPath (Join-Path $script:FailureRoot 'Early-Cleanup.json'))){New-V22FinalizerJson (Join-Path $script:FailureRoot 'Early-Cleanup.json') $early}
    }elseif($null -ne $script:FinalCleanupProof){$cleanupStatus=$script:FinalCleanupProof.AggregateCleanupStatus}
    $value=[ordered]@{SchemaVersion='warehouse-benchmark-v22-abort-disposition/2';RunId=$script:RunId;CandidateId=$script:CandidateId;Verdict=if($state.WorkloadStarted){'BATCH3_PARTIAL'}else{'BATCH3_NOT_READY'};OriginalFailureStage=if($script:OriginalFailure){$script:OriginalFailure.OriginalFailureStage}else{'NONE'};OriginalFailureReason=if($script:OriginalFailure){$script:OriginalFailure.OriginalFailureReason}else{'NONE'};SafeExceptionMessage=if($script:OriginalFailure){$script:OriginalFailure.SafeExceptionMessage}else{'NONE'};FinalizerStage=$script:FinalizerStage;FinalizerFailure=if($script:FinalizerFailure){$script:FinalizerFailure.Message}else{'NONE'};CandidateCreated=$state.CandidateCreated;BuildStarted=$state.BuildStarted;DbPreflightStarted=$state.DbPreflightStarted;WorkloadStarted=$state.WorkloadStarted;CanonicalWorkloadAttemptConsumed=$state.WorkloadStarted;PerformanceRunRootCreated=$state.PerformanceRunRootCreated;RawArtifactState=$rawState.Status;CleanupStatus=$cleanupStatus;OwnedProcessCount=$script:AllOwnedProcesses.Count;PreservationStatus=$script:PreservationStatus;CoreEvidenceReady=$false;ReadyForBatch4=$false;RecordedUtc=[DateTime]::UtcNow.ToString('o')}
    $path=Join-Path $script:FailureRoot 'Abort-Acceptance.json'
    if(-not(Test-Path -LiteralPath $path)){
        $report=New-V22AbortReport $value
        New-V22FinalizerJson (Join-Path $script:FailureRoot 'Abort-Report-Lint.json') (Test-V22FinalReportLint $report)
        Write-NewText (Join-Path $script:FailureRoot 'Abort-Report.md') $report
        New-V22FinalizerJson $path $value
    }
    return $value
}
function Write-NewJson([string]$Path,[object]$Value){
    if(Test-Path -LiteralPath $Path){throw "Evidence path already exists: $Path"}
    $parent=Split-Path -Parent $Path
    if(-not(Test-Path -LiteralPath $parent -PathType Container)){New-Item -ItemType Directory -Path $parent|Out-Null}
    $text=($Value|ConvertTo-Json -Depth 24)+[Environment]::NewLine
    $stream=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($text);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
}
function Write-NewText([string]$Path,[string]$Text){
    if(Test-Path -LiteralPath $Path){throw "Evidence path already exists: $Path"}
    $parent=Split-Path -Parent $Path
    if(-not(Test-Path -LiteralPath $parent -PathType Container)){New-Item -ItemType Directory -Path $parent|Out-Null}
    $stream=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($Text);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
}
function Copy-NewReviewArtifact([string]$SourcePath,[string]$DestinationPath){
    if(-not(Test-Path -LiteralPath $SourcePath -PathType Leaf)){throw "Projection source is missing: $SourcePath"}
    if(Test-Path -LiteralPath $DestinationPath){throw "Projection target already exists: $DestinationPath"}
    $parent=Split-Path -Parent $DestinationPath
    if(-not(Test-Path -LiteralPath $parent -PathType Container)){New-Item -ItemType Directory -Path $parent|Out-Null}
    [IO.File]::Copy($SourcePath,$DestinationPath,$false)
    $sourceHash=Get-FileHashHex $SourcePath;$targetHash=Get-FileHashHex $DestinationPath
    if($sourceHash -cne $targetHash){throw "Projection hash mismatch: $DestinationPath"}
    return [pscustomobject]@{Source=$SourcePath;Destination=$DestinationPath;SHA256=$targetHash;Size=[long](Get-Item -LiteralPath $DestinationPath).Length;Status='PASS'}
}
function Read-OptionalJson([string]$Path){if(-not(Test-Path -LiteralPath $Path -PathType Leaf)){return $null};try{return (Get-Content -LiteralPath $Path -Raw|ConvertFrom-Json)}catch{return $null}}
function Get-FileHashHex([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Get-SafeText([string]$Text){
    $safe=[string]$Text
    $connection=[Environment]::GetEnvironmentVariable('TKS_V22_CONNECTION_STRING','Process')
    if(-not[string]::IsNullOrEmpty($connection)){$safe=$safe.Replace($connection,'[REDACTED_CONNECTION_STRING]')}
    return ConvertTo-V22SafeMessage $safe
}
function Write-RunProgress([string]$Stage,[string]$Status,[string]$Reason=$null){
    $progress=[ordered]@{SchemaVersion='warehouse-benchmark-v22-batch3-progress/1';RunId=$script:RunId;Stage=$Stage;Status=$Status;Reason=$Reason;UpdatedUtc=[DateTime]::UtcNow.ToString('o')}
    $path=Join-Path $script:PerformanceRunRoot 'progress.json'
    $text=$progress|ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText($path,$text+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
}
function Set-ActiveRunContext([string]$RunId,[string]$RootPrefix){
    if([string]::IsNullOrWhiteSpace($RunId) -or $RunId -cne $script:CanonicalRunId){throw 'CANONICAL_RUN_IDENTITY_INVALID|attempted to replace the single canonical RunId'}
    $script:RunId=$script:CanonicalRunId
    $script:PerformanceRunRoot=Join-Path $script:BatchRoot ($RootPrefix+'-'+$script:CanonicalRunId)
    [void](New-V22RunRoot $script:PerformanceRunRoot)
    $script:HostStopPath=Join-Path $script:PerformanceRunRoot 'host-stop-actions.jsonl'
    $script:HostLogPath=Join-Path $script:PerformanceRunRoot 'host-telemetry.jsonl'
    $script:ProtocolInfo.RunId=$script:CanonicalRunId
}
function Add-JsonLine([string]$Path,[object]$Value){[IO.File]::AppendAllText($Path,(($Value|ConvertTo-Json -Compress -Depth 10)+[Environment]::NewLine),[Text.UTF8Encoding]::new($false))}
function Get-HostSnapshot([int[]]$ChildProcessIds=@()){
    $os=Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
    $cpuRow=Get-CimInstance -ClassName Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'" -ErrorAction SilentlyContinue|Select-Object -First 1
    $childPrivate=0.0;$childWorking=0.0
    foreach($childProcessId in $ChildProcessIds){try{$proc=Get-Process -Id $childProcessId -ErrorAction Stop;$childPrivate+=$proc.PrivateMemorySize64/1MB;$childWorking+=$proc.WorkingSet64/1MB}catch{}}
    $sqlWorking=0.0
    foreach($sqlProc in @(Get-Process -Name 'sqlservr' -ErrorAction SilentlyContinue)){$sqlWorking+=$sqlProc.WorkingSet64/1MB}
    return [pscustomobject]@{CapturedUtc=[DateTime]::UtcNow.ToString('o');FreeRamMB=[Math]::Round(([double]$os.FreePhysicalMemory/1024),1);TotalRamMB=[Math]::Round(([double]$os.TotalVisibleMemorySize/1024),1);FreeVirtualMemoryMB=[Math]::Round(([double]$os.FreeVirtualMemory/1024),1);TotalVirtualMemoryMB=[Math]::Round(([double]$os.TotalVirtualMemorySize/1024),1);CpuPercent=if($null -ne $cpuRow){[double]$cpuRow.PercentProcessorTime}else{$null};ChildProcessIds=@($ChildProcessIds);ChildPrivateMemoryMB=[Math]::Round($childPrivate,1);ChildWorkingSetMB=[Math]::Round($childWorking,1);SqlServerWorkingSetMB=[Math]::Round($sqlWorking,1);ProcessId=[int]$PID}
}
function Get-ArgumentValue([string[]]$Arguments,[string]$Name){
    $index=[Array]::IndexOf($Arguments,$Name)
    if($index -ge 0 -and $index+1 -lt $Arguments.Count){return [string]$Arguments[$index+1]}
    return $null
}
function Write-WorkerTerminalMetadata([object]$Record){
    if($Record.Role -cne 'CHILD' -or [string]::IsNullOrWhiteSpace([string]$Record.MetadataPath)){return}
    $metadataPath=[string]$Record.MetadataPath
    $initialPath=$metadataPath+'.initial.json'
    $completedPath=$metadataPath+'.completed.json'
    $observedPath=$metadataPath+'.observed.jsonl'
    $initial=$null;$completed=$null
    if(Test-Path -LiteralPath $metadataPath -PathType Leaf){try{$initial=Get-Content -LiteralPath $metadataPath -Raw|ConvertFrom-Json}catch{}}
    if(Test-Path -LiteralPath $completedPath -PathType Leaf){try{$completed=Get-Content -LiteralPath $completedPath -Raw|ConvertFrom-Json}catch{}}
    $observed=@()
    if(Test-Path -LiteralPath $observedPath -PathType Leaf){
        foreach($line in Get-Content -LiteralPath $observedPath){try{$row=ConvertFrom-Json $line;if($row.RunId -ceq $Record.RunId -and $row.BlockId -ceq $Record.BlockId -and $row.Scenario -ceq $Record.Scenario -and [int]$row.ProcessId -eq $Record.ChildProcessId){$observed+=@($row)}}catch{}}
    }
    $instances=@($observed|ForEach-Object{[int]$_.InstanceNumber}|Sort-Object -Unique)
    $workerProjection=New-V22WorkerTerminalProjection -ExpectedCopies ([int]$Record.ExpectedCopies) -ObservedInstanceNumbers $instances -ProcessExited ([bool]$Record.ProcessGone) -ExitCode $Record.ExitCode -CompletedMetadataPresent ([bool]($null -ne $completed)) -HostStopEvidence $Record.HostStopEvidence -RawEvidencePath $Record.LogDirectory; $terminalState=$workerProjection.Status
    if(Test-Path -LiteralPath $metadataPath -PathType Leaf){
        if(Test-Path -LiteralPath $initialPath){$Record.TerminalMetadataStatus='FAILED';$Record.TerminalMetadataFailure='initial metadata preservation path already exists';return}
        Move-Item -LiteralPath $metadataPath -Destination $initialPath
    }
    $terminal=[ordered]@{SchemaVersion='warehouse-benchmark-v22-worker-terminal/1';Status=$terminalState;RunId=$Record.RunId;BlockId=$Record.BlockId;Scenario=$Record.Scenario;ChildProcessId=$Record.ChildProcessId;ProcessStartedUtc=$Record.StartedUtc.ToString('o');ProcessFinishedUtc=if($Record.FinishedUtc){$Record.FinishedUtc.ToString('o')}else{$null};ExpectedCopies=$workerProjection.ExpectedCopies;ObservedCopies=$workerProjection.ObservedCopies;ObservedInstanceNumbers=$workerProjection.ObservedInstanceNumbers;ObservedWorkerRows=$observed;ConfiguredDurationSeconds=$Record.ConfiguredDurationSeconds;ProcessExitCode=$workerProjection.ProcessExitCode;StopReason=if($Record.StopReason){$Record.StopReason}else{$workerProjection.StopReason};HostStopEvidence=$workerProjection.HostStopEvidence;TimedWindowCompleted=$workerProjection.TimedWindowCompleted;TimedWindowCompletionBasis=if($terminalState -ceq 'COMPLETED'){'NBomber completion metadata plus zero process exit; metrics/window externally validated'}else{'NOT_COMPLETED_OR_NOT_VERIFIED'};CompletedWorkerMetadataPresent=($null -ne $completed);CompletedWorkerMetadataPath=if($null -ne $completed){$completedPath}else{$null};ObservedWorkerMetadataPath=if(Test-Path -LiteralPath $observedPath){$observedPath}else{$null};InitialWorkerMetadataPath=if(Test-Path -LiteralPath $initialPath){$initialPath}else{$null};RawEvidencePath=$workerProjection.RawEvidencePath;TerminalCapturedUtc=[DateTime]::UtcNow.ToString('o')}
    try{Write-NewJson $metadataPath $terminal;$Record.TerminalMetadataStatus='WRITTEN'}catch{$Record.TerminalMetadataStatus='FAILED';$Record.TerminalMetadataFailure=Get-SafeText $_.Exception.Message}
}
function Start-OwnedProcess([string]$FilePath,[string[]]$ArgumentVector,[string]$WorkingDirectory,[string]$Label,[ValidateSet('CHILD','TELEMETRY','TOOL','DB_TOOL')][string]$Role,[string]$LogDirectory){
    $argumentRunId=Get-ArgumentValue $ArgumentVector '--run-id'
    if($Role -in @('CHILD','TELEMETRY')){
        if($script:CanonicalRunIdentityStatus -cne 'PASS' -or [string]::IsNullOrWhiteSpace($argumentRunId) -or $argumentRunId -cne $script:CanonicalRunId){throw 'CANONICAL_RUN_IDENTITY_INVALID|child process config is not bound to the canonical RunId'}
    }
    $resolved=(Get-Command $FilePath -ErrorAction Stop).Source
    $startInfo=[Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName=$resolved;$startInfo.WorkingDirectory=$WorkingDirectory;$startInfo.UseShellExecute=$false;$startInfo.CreateNoWindow=$true
    $startInfo.RedirectStandardOutput=$true;$startInfo.RedirectStandardError=$true
    if($Role -notin @('CHILD','TELEMETRY','DB_TOOL')){[void]$startInfo.Environment.Remove('TKS_V22_CONNECTION_STRING')}
    foreach($arg in $ArgumentVector){[void]$startInfo.ArgumentList.Add([string]$arg)}
    $process=[Diagnostics.Process]::Start($startInfo)
    if($null -eq $process){throw "Could not start owned process: $Label"}
    $stdoutTask=$process.StandardOutput.ReadToEndAsync();$stderrTask=$process.StandardError.ReadToEndAsync()
    $record=[pscustomobject]@{RecordType='OWNED_PROCESS';MetadataSchemaVersion=1;Label=$Label;Role=$Role;FilePath=$resolved;Arguments=$ArgumentVector;Process=$process;ChildProcessId=[int]$process.Id;StartedUtc=[DateTime]::UtcNow;ProcessStartUtc=$null;StdoutTask=$stdoutTask;StderrTask=$stderrTask;LogDirectory=$LogDirectory;FinishedUtc=$null;ExitCode=$null;TimedOut=$false;StopReason=$null;HostStopEvidence=$null;HostStopped=$false;KilledForHostLimitApplicable=$false;KilledForHostLimit=$false;CleanupStatus='TRACKED';CleanupProcessProof=$null;CleanupProofStatus='NOT_RUN';ProcessGone=$false;TerminalMetadataStatus='NOT_APPLICABLE';TerminalMetadataFailure=$null;Completed=$false;RunId=if(-not[string]::IsNullOrWhiteSpace($argumentRunId)){$argumentRunId}else{$script:CanonicalRunId};CandidateId=$script:CandidateId;BlockId=Get-ArgumentValue $ArgumentVector '--block-id';Scenario=Get-ArgumentValue $ArgumentVector '--scenario';Level=if((Get-ArgumentValue $ArgumentVector '--block-id') -match '-MIXED-(L[1248])(?:-|$)'){$Matches[1]}else{$null};ExpectedCopies=if((Get-ArgumentValue $ArgumentVector '--copies') -match '^\d+$'){[int](Get-ArgumentValue $ArgumentVector '--copies')}else{$null};ConfiguredDurationSeconds=if((Get-ArgumentValue $ArgumentVector '--duration-seconds') -match '^\d+$'){[int](Get-ArgumentValue $ArgumentVector '--duration-seconds')}else{$null};MetadataPath=(Get-ArgumentValue $ArgumentVector '--metadata-path');StdoutPath=(Join-Path $LogDirectory ($Label+'.stdout.txt'));StderrPath=(Join-Path $LogDirectory ($Label+'.stderr.txt'))}
    $script:AllOwnedProcesses.Add($record)
    try{$record.ProcessStartUtc=$process.StartTime.ToUniversalTime().ToString('o')}catch{[void](Stop-OwnedProcess $record 'PROCESS_START_IDENTITY_CAPTURE_FAILED');[void](Complete-OwnedProcess $record 'PROCESS_START_IDENTITY_CAPTURE_FAILED');throw 'OWNED_PROCESS_START_IDENTITY_UNVERIFIED'}
    return $record
}
function Stop-OwnedProcess([object]$Record,[string]$Reason,[object]$HostStopEvidence=$null){
    $status='ALREADY_EXITED';$failureMessage=$null;$killIssued=$false
    try{
        if(-not $Record.Process.HasExited){$killIssued=$true;$Record.Process.Kill($true);if(-not $Record.Process.WaitForExit(10000)){throw 'owned process tree did not exit'};$status='KILLED_OWNED_TREE'}
        $Record.CleanupStatus=$status
    }catch{$status='CLEANUP_FAILED';$failureMessage=Get-SafeText $_.Exception.Message;$Record.CleanupStatus=$status}
    $Record.StopReason=$Reason
    if($null -ne $HostStopEvidence){
        $Record.HostStopEvidence=$HostStopEvidence
        $Record.HostStopped=$true
        $HostStopEvidence.ActionRequested=$true
        $HostStopEvidence.ActionCompleted=($status -ne 'CLEANUP_FAILED' -and $Record.Process.HasExited)
        $HostStopEvidence.KillIssued=$killIssued
        $Record.KilledForHostLimitApplicable=($HostStopEvidence.Reason -in @('HOST_STOP_HARD_FLOOR','HOST_STOP_EMERGENCY_FLOOR'))
        $Record.KilledForHostLimit=if($Record.KilledForHostLimitApplicable){[bool]$killIssued}else{$null}
        try{$Record.ExitCode=$Record.Process.ExitCode;$HostStopEvidence.ProcessExitCode=$Record.ExitCode;$HostStopEvidence.ProcessExitResult='EXIT_CODE_'+[string]$Record.ExitCode}catch{$HostStopEvidence.ProcessExitCode=$null;$HostStopEvidence.ProcessExitResult='EXIT_UNVERIFIED'}
        $HostStopEvidence.CleanupActionStatus=$status
        $HostStopEvidence.CapturedUtc=[DateTime]::UtcNow.ToString('o')
        $script:HostStopRows.Add($HostStopEvidence)
        try{Add-JsonLine $script:HostStopPath $HostStopEvidence}catch{$HostStopEvidence.EvidenceWriteStatus='FAILED';$HostStopEvidence.EvidenceWriteFailure=Get-SafeText $_.Exception.Message}
    }
    $script:CleanupRows.Add([pscustomobject]@{ProcessLabel=$Record.Label;Role=$Record.Role;ChildProcessId=$Record.ChildProcessId;Reason=$Reason;Status=$status;Failure=$failureMessage;CapturedUtc=[DateTime]::UtcNow.ToString('o')})
    return [pscustomobject]@{Status=$status;Failure=$failureMessage;KillIssued=$killIssued;ProcessExited=[bool]$Record.Process.HasExited}
}
function Complete-OwnedProcess([object]$Record,[string]$Reason='NORMAL_EXIT'){
    if($Record.Completed){return $Record}
    if(-not $Record.Process.HasExited){[void](Stop-OwnedProcess $Record $Reason)}
    try{$Record.FinishedUtc=[DateTime]::UtcNow;$Record.ExitCode=$Record.Process.ExitCode}catch{$Record.ExitCode=-1}
    $stdout=Get-SafeText $Record.StdoutTask.GetAwaiter().GetResult();$stderr=Get-SafeText $Record.StderrTask.GetAwaiter().GetResult()
    Write-NewText $Record.StdoutPath $stdout;Write-NewText $Record.StderrPath $stderr
    $Record.ProcessGone=[bool]$Record.Process.HasExited
    $Record.Completed=$true
    Write-WorkerTerminalMetadata $Record
    try{$Record.Process.Dispose()}catch{}
    $script:CleanupRows.Add([pscustomobject]@{ProcessLabel=$Record.Label;Role=$Record.Role;ChildProcessId=$Record.ChildProcessId;Reason=$Reason;ExitCode=$Record.ExitCode;Status=$(if($Record.ExitCode -ge 0){'EXITED'}else{'EXIT_UNVERIFIED'});CapturedUtc=[DateTime]::UtcNow.ToString('o')})
    return $Record
}
function Get-OwnedProcessCleanupProof([object[]]$Records){
    $owned=@($Records|Where-Object Role -in @('CHILD','TELEMETRY','TOOL','DB_TOOL'))
    if($owned.Count -eq 0){return [pscustomobject]@{Status='PASS';Passed=$true;OwnedProcessIds=@();Items=@()}}
    $probes=[Collections.Generic.List[object]]::new()
    foreach($record in $owned){
        try{$found=Get-CimInstance -ClassName Win32_Process -Filter ('ProcessId = {0}' -f [int]$record.ChildProcessId) -ErrorAction Stop;if($null -eq $found){$probes.Add([pscustomobject]@{TargetProcessId=$record.ChildProcessId;ObservedProcessId=$null;Status='ABSENT';CapturedUtc=[DateTime]::UtcNow.ToString('o')})}else{$probes.Add([pscustomobject]@{TargetProcessId=$record.ChildProcessId;ObservedProcessId=[int]$found.ProcessId;Status='PRESENT';CapturedUtc=[DateTime]::UtcNow.ToString('o')})}}
        catch{$probes.Add([pscustomobject]@{TargetProcessId=$record.ChildProcessId;ObservedProcessId=$null;Status='UNKNOWN';Reason=Get-SafeText $_.Exception.Message;CapturedUtc=[DateTime]::UtcNow.ToString('o')})}
    }
    $assessment=Get-V22ProcessCleanupAssessment -OwnedProcessIds @($owned|ForEach-Object ChildProcessId) -ProbeResults $probes.ToArray(); foreach($record in $owned){$item=$assessment.Items|Where-Object TargetProcessId -eq $record.ChildProcessId|Select-Object -First 1;$record.CleanupProcessProof=$assessment;$record.CleanupProofStatus=if($null -ne $item){$item.Status}else{'CLEANUP_UNVERIFIED'}}; return $assessment
}
function Wait-OwnedGroup([object[]]$Records,[int]$TimeoutSeconds,[string]$HostLogPath,[bool]$MonitorHost,[string]$MonitorStage){
    $start=[DateTime]::UtcNow;$deadline=$start.AddSeconds($TimeoutSeconds);$lastSample=[DateTime]::MinValue;$hardStop=$false;$emergencyStop=$false;$timedOut=$false;$safetyStop=$false;$monitorFailure=$null;$minRam=[double]::PositiveInfinity;$memoryLimitReason=$null
    while(@($Records|Where-Object{-not $_.Process.HasExited}).Count -gt 0){
        $now=[DateTime]::UtcNow
        if($MonitorHost -and ($now-$lastSample).TotalSeconds -ge 1){
            $ids=@($Records|Where-Object{-not $_.Process.HasExited}|ForEach-Object{$_.ChildProcessId})
            $sample=$null;try{$sample=Get-HostSnapshot $ids;$minRam=[Math]::Min($minRam,[double]$sample.FreeRamMB);Add-JsonLine $HostLogPath ([ordered]@{BlockId=$MonitorStage;Stage=$MonitorStage;Sample=$sample})}catch{$monitorFailure='HOST_TELEMETRY_INVALID';$safetyStop=$true;$script:StopReason=$monitorFailure;foreach($record in @($Records|Where-Object{-not $_.Process.HasExited})){$safetyEvidence=[ordered]@{HostStopKind='SAFETY_STOP';Reason=$monitorFailure;TriggerName='HOST_TELEMETRY_FAILURE';ConfiguredThresholdMB=$null;ObservedFreeMB=$null;ObservedUtc=[DateTime]::UtcNow.ToString('o');BlockId=if($record.BlockId){$record.BlockId}else{$MonitorStage};ChildPid=[int]$record.ChildProcessId;ActionRequested=$true;ActionCompleted=$false;ProcessExitResult='PENDING';RunId=$script:RunId;Stage=$MonitorStage};[void](Stop-OwnedProcess $record $monitorFailure $safetyEvidence)}}
            $lastSample=$now
            if($null -ne $sample -and $sample.FreeRamMB -le [double]$script:ProtocolInfo.HostPolicy.EmergencyFloorMB){$emergencyStop=$true;$hardStop=$true;$memoryLimitReason='HOST_STOP_EMERGENCY_FLOOR'}
            elseif($null -ne $sample -and $sample.FreeRamMB -le [double]$script:ProtocolInfo.HostPolicy.HardFloorMB){$hardStop=$true;$memoryLimitReason='HOST_STOP_HARD_FLOOR'}
            if($hardStop){foreach($record in @($Records|Where-Object{-not $_.Process.HasExited})){$e=[ordered]@{Reason=$memoryLimitReason;TriggerName=$memoryLimitReason;ConfiguredThresholdMB=if($emergencyStop){[double]$script:ProtocolInfo.HostPolicy.EmergencyFloorMB}else{[double]$script:ProtocolInfo.HostPolicy.HardFloorMB};ObservedFreeMB=[double]$sample.FreeRamMB;ObservedUtc=$sample.CapturedUtc;BlockId=if($record.BlockId){$record.BlockId}else{$MonitorStage};ChildPid=[int]$record.ChildProcessId;ActionRequested=$true;ActionCompleted=$false;ProcessExitResult='PENDING';RunId=$script:RunId;Stage=$MonitorStage};[void](Stop-OwnedProcess $record $memoryLimitReason $e)}}
        }
        if($now -ge $deadline){$timedOut=$true;foreach($record in @($Records|Where-Object{-not $_.Process.HasExited})){$record.TimedOut=$true;[void](Stop-OwnedProcess $record 'PROCESS_TIMEOUT')};break}
        Start-Sleep -Milliseconds 300
    }
    foreach($record in $Records){[void](Complete-OwnedProcess $record $(if($record.StopReason){$record.StopReason}elseif($record.TimedOut){'PROCESS_TIMEOUT'}else{'NORMAL_EXIT'}))}
    return [pscustomobject]@{TimedOut=$timedOut;HardStop=$hardStop;EmergencyStop=$emergencyStop;MemoryLimitReason=$memoryLimitReason;SafetyStop=$safetyStop;MonitorFailure=$monitorFailure;HostStopActions=@($script:HostStopRows|Where-Object Stage -ceq $MonitorStage);MinRamMB=if([double]::IsPositiveInfinity($minRam)){$null}else{[Math]::Round($minRam,1)};StartedUtc=$start.ToString('o');FinishedUtc=[DateTime]::UtcNow.ToString('o')}
}
function Invoke-Tool([string]$FilePath,[string[]]$ArgumentVector,[string]$WorkingDirectory,[string]$Label,[string]$LogDirectory,[int]$TimeoutSeconds=300,[switch]$DatabaseProcess){
    if($DatabaseProcess){$script:DatabaseActivityStarted=$true}
    $role=if($DatabaseProcess){'DB_TOOL'}else{'TOOL'}
    $record=Start-OwnedProcess $FilePath $ArgumentVector $WorkingDirectory $Label $role $LogDirectory
    $wait=Wait-OwnedGroup @($record) $TimeoutSeconds (Join-Path $script:PerformanceRunRoot 'host-telemetry.jsonl') $false $Label
    $proof=Get-OwnedProcessCleanupProof @($record)
    $record.CleanupStatus=if($proof.Passed){'PASS'}else{'FAIL'}
    if(-not $proof.Passed -and -not $script:StopReason){$script:StopReason='CLEANUP_FAILED'}
    return $record
}
function ConvertTo-FiniteDouble([object]$Value,[string]$Name){
    $text=[Convert]::ToString($Value,[Globalization.CultureInfo]::InvariantCulture)
    if([string]::IsNullOrWhiteSpace($text)){throw "V22_NB_INVALID|$Name is empty"}
    $number=0.0
    $parsed=[double]::TryParse($text,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$number);if(-not$parsed -and $text.Contains(',') -and -not$text.Contains('.')){$parsed=[double]::TryParse($text,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::GetCultureInfo('vi-VN'),[ref]$number)};if(-not$parsed -or [double]::IsNaN($number) -or [double]::IsInfinity($number)){throw "V22_NB_INVALID|$Name is malformed or non-finite"}
    return $number
}
function Get-ObjectField([object]$Object,[string]$Name){$property=$Object.PSObject.Properties[$Name];if($null -eq $property){return $null};return $property.Value}
function Get-NBomberArtifact([string]$ReportDirectory,[string]$Scenario,[int]$ConfiguredCopies,[string]$StdoutPath,[string]$MetadataPath,[string]$ExpectedRunId,[string]$ExpectedBlockId,[string]$ExpectedProfile){
    $completedMetadataPath=$MetadataPath+'.completed.json'
    if(-not(Test-Path -LiteralPath $completedMetadataPath -PathType Leaf)){throw 'V22_NB_INVALID|completed worker metadata is missing'}
    try{$workerMetadata=Get-Content -LiteralPath $completedMetadataPath -Raw|ConvertFrom-Json}catch{throw 'V22_NB_INVALID|completed worker metadata is malformed'}
    if($workerMetadata.Status -cne 'PROCESS_COMPLETED_REPORT_REQUIRES_EXTERNAL_VALIDATION' -or $workerMetadata.RunId -cne $ExpectedRunId -or $workerMetadata.BlockId -cne $ExpectedBlockId -or $workerMetadata.Profile -cne $ExpectedProfile -or $workerMetadata.Scenario -cne $Scenario -or $workerMetadata.TargetDatabase -cne 'TKS_Thuc_Tap_V11_Perf_10000000'){throw 'V22_NB_INVALID|completed worker metadata identity mismatch'}
    $metadataCopies=ConvertTo-FiniteDouble $workerMetadata.Copies 'MetadataCopies'
    $observedCopies=ConvertTo-FiniteDouble $workerMetadata.ObservedCopies 'ObservedCopies'
    if([Math]::Floor($metadataCopies) -ne $metadataCopies -or [Math]::Floor($observedCopies) -ne $observedCopies -or $metadataCopies -ne $ConfiguredCopies -or $observedCopies -ne $ConfiguredCopies -or $workerMetadata.WarmupSeconds -ne 3 -or $workerMetadata.ConfiguredDurationSeconds -ne 15){throw 'V22_NB_INVALID|worker metadata configured/observed counts or window differ from protocol'}
    $observedInstanceNumbers=[Collections.Generic.List[int]]::new()
    foreach($rawInstanceNumber in @($workerMetadata.ObservedInstanceNumbers)){$instanceNumber=ConvertTo-FiniteDouble $rawInstanceNumber 'ObservedInstanceNumber';if([Math]::Floor($instanceNumber) -ne $instanceNumber -or $instanceNumber -lt 0 -or $instanceNumber -ge $ConfiguredCopies){throw 'V22_NB_INVALID|observed scenario instance number is outside configured worker range'};$observedInstanceNumbers.Add([int]$instanceNumber)}
    $uniqueInstanceNumbers=@($observedInstanceNumbers|Sort-Object -Unique)
    $expectedInstanceNumbers=@(0..($ConfiguredCopies-1))
    if($observedInstanceNumbers.Count -ne $ConfiguredCopies -or $uniqueInstanceNumbers.Count -ne $ConfiguredCopies -or (@($uniqueInstanceNumbers) -join ',') -cne (@($expectedInstanceNumbers) -join ',')){throw 'V22_NB_INVALID|observed scenario instances do not equal configured copies'}
    $csvFiles=@(Get-ChildItem -LiteralPath $ReportDirectory -File -Recurse -Filter '*.csv'|Sort-Object FullName)
    $found=[Collections.Generic.List[object]]::new()
    foreach($file in $csvFiles){
        try{$rows=@(Import-Csv -LiteralPath $file.FullName -Delimiter ',')}catch{continue}
        foreach($row in $rows){if(([string](Get-ObjectField $row 'scenario') -ceq $Scenario) -and ([string](Get-ObjectField $row 'step_name') -ceq 'global information')){$found.Add([pscustomobject]@{Path=$file.FullName;Row=$row})}}
    }
    if($found.Count -ne 1){throw "V22_NB_INVALID|expected exactly one global NBomber row for $Scenario, found $($found.Count)"}
    $item=$found[0];$row=$item.Row
    if(([string](Get-ObjectField $row 'test_suite')) -cne 'WAREHOUSE_BENCHMARK_V22_LEGACY'){throw 'V22_NB_INVALID|test suite identity mismatch'}
    if(([string](Get-ObjectField $row 'scenario')) -cne $Scenario){throw 'V22_NB_INVALID|scenario identity mismatch'}
    $duration=[TimeSpan]::Parse([string](Get-ObjectField $row 'duration'),[Globalization.CultureInfo]::InvariantCulture)
    $windowMs=$duration.TotalMilliseconds
    $metrics=[ordered]@{
        Requests=(ConvertTo-FiniteDouble (Get-ObjectField $row 'request_count') 'Requests')
        Failed=(ConvertTo-FiniteDouble (Get-ObjectField $row 'failed') 'Failed')
        RPS=(ConvertTo-FiniteDouble (Get-ObjectField $row 'ok_rps') 'RPS')
        MeanMs=(ConvertTo-FiniteDouble (Get-ObjectField $row 'ok_mean') 'Mean')
        P50Ms=(ConvertTo-FiniteDouble (Get-ObjectField $row 'ok_50_percent') 'P50')
        P95Ms=(ConvertTo-FiniteDouble (Get-ObjectField $row 'ok_95_percent') 'P95')
        P99Ms=(ConvertTo-FiniteDouble (Get-ObjectField $row 'ok_99_percent') 'P99')
        MaxMs=(ConvertTo-FiniteDouble (Get-ObjectField $row 'ok_max') 'Max')
        WindowMs=$windowMs;ConfiguredWindowMs=15000.0;ObservedWindowMs=$windowMs
        Units=[ordered]@{Requests='count';Failed='count';RPS='requests/s';MeanMs='ms';P50Ms='ms';P95Ms='ms';P99Ms='ms';MaxMs='ms';WindowMs='ms';ConfiguredWindowMs='ms';ObservedWindowMs='ms'}
    }
    $validated=Assert-V22NBomberMetrics $metrics 15000.0
    $success=ConvertTo-FiniteDouble (Get-ObjectField $row 'ok') 'Success'
    if([Math]::Floor($success) -ne $success -or $success+$validated.Metrics.Failed -ne $validated.Metrics.Requests){throw 'V22_NB_INVALID|request_count does not equal success plus failed'}
    if($duration.TotalSeconds -ne 15){throw 'V22_NB_INVALID|raw configured duration is not 15 seconds'}
    $logText=''
    $logs=@(Get-ChildItem -LiteralPath $ReportDirectory -File -Recurse -Filter 'nbomber-log-*.txt')
    foreach($log in $logs){$logText+=[IO.File]::ReadAllText($log.FullName)+[Environment]::NewLine}
    $stdoutPath=$StdoutPath
    if(Test-Path -LiteralPath $stdoutPath){$logText+=[IO.File]::ReadAllText($stdoutPath)}
    $startMatches=[regex]::Matches($logText,'(?m)^(?<stamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{1,7} [+-]\d{2}:\d{2}) \[INF\].*Starting bombing')
    $stopMatches=[regex]::Matches($logText,'(?m)^(?<stamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{1,7} [+-]\d{2}:\d{2}) \[INF\].*Stopping scenarios')
    if($startMatches.Count -lt 1 -or $stopMatches.Count -lt 1){throw 'V22_NB_INVALID|timed-window start/stop log evidence missing'}
    $start=[DateTimeOffset]::Parse($startMatches[0].Groups['stamp'].Value,[Globalization.CultureInfo]::InvariantCulture)
    $stop=[DateTimeOffset]::Parse($stopMatches[$stopMatches.Count-1].Groups['stamp'].Value,[Globalization.CultureInfo]::InvariantCulture)
    if($stop -le $start){throw 'V22_NB_INVALID|timed window is not ordered'}
    return [pscustomobject]@{Status='NB_METRICS_VALID';Scenario=$Scenario;ArtifactPath=$item.Path;Metrics=$validated.Metrics;Success=[long]$success;ObservedCopies=[int]$observedCopies;ConfiguredCopies=$ConfiguredCopies;ObservedInstanceNumbers=$uniqueInstanceNumbers;ObservedCopiesEvidence='Distinct ScenarioInfo.InstanceNumber values observed by worker callbacks';WorkerMetadataPath=$completedMetadataPath;WorkerMetadataSHA256=(Get-FileHashHex $completedMetadataPath);RawConfiguredDurationSeconds=$duration.TotalSeconds;MeasuredStartUtc=$start.ToUniversalTime().ToString('o');MeasuredStopUtc=$stop.ToUniversalTime().ToString('o');LogWindowSeconds=[Math]::Round(($stop-$start).TotalSeconds,3);RequestCountIdentityPass=$true;RawCsvSHA256=(Get-FileHashHex $item.Path)}
}
function Convert-BdnQuantityToNs([string]$Value,[string]$Field){
    $match=[regex]::Match($Value,'^\s*(?<n>[+-]?(?:(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)\s*(?<u>ns|us|µs|μs|ms|s)\s*$')
    if(-not$match.Success){throw "V22_BDN_INVALID|$Field is missing, malformed or has no supported unit"}
    $numberText=$match.Groups['n'].Value.Replace([string][char]44,[string]::Empty)
    $number=ConvertTo-FiniteDouble $numberText $Field
    $scale=switch -CaseSensitive ($match.Groups['u'].Value){'ns'{1.0}'us'{1000.0}'µs'{1000.0}'μs'{1000.0}'ms'{1000000.0}'s'{1000000000.0}default{throw "V22_BDN_INVALID|$Field unit invalid"}}
    $ns=$number*$scale
    if($ns -lt 0 -or [double]::IsInfinity($ns) -or [double]::IsNaN($ns)){throw "V22_BDN_INVALID|$Field is negative or non-finite"}
    return [double]$ns
}
function Get-BdnPrintedRoundingToleranceNs([string]$Value,[string]$Field){
    $match=[regex]::Match($Value,'^\s*(?<n>[+-]?(?:(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)\s*(?<u>ns|us|µs|μs|ms|s)\s*$')
    if(-not$match.Success){throw "V22_BDN_INVALID|$Field precision is missing or malformed"}
    $numberText=$match.Groups['n'].Value.Replace([string][char]44,[string]::Empty);$decimals=if($numberText.Contains('.')){$numberText.Length-$numberText.IndexOf('.')-1}else{0}
    $scale=switch -CaseSensitive ($match.Groups['u'].Value){'ns'{1.0}'us'{1000.0}'µs'{1000.0}'μs'{1000.0}'ms'{1000000.0}'s'{1000000000.0}default{throw "V22_BDN_INVALID|$Field unit invalid"}}
    return 0.5*$scale*[Math]::Pow(10,-$decimals)
}
function Get-BdnArtifact([string]$OutputDirectory,[string]$StdoutPath){
    $csv=@(Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse -Filter '*report.csv')
    if($csv.Count-ne 1){throw "V22_BDN_INVALID|expected one BDN summary CSV, found $($csv.Count)"}
    $rows=@(Import-Csv -LiteralPath $csv[0].FullName -Delimiter ','|Where-Object Method -ceq 'Execute')
    if($rows.Count-ne 1){throw 'V22_BDN_INVALID|expected one Execute summary row'}
    $summary=$rows[0];$summaryMeanNs=Convert-BdnQuantityToNs ([string]$summary.Mean) 'SummaryMean';$errorNs=Convert-BdnQuantityToNs ([string]$summary.Error) 'Error';$stdNs=Convert-BdnQuantityToNs ([string]$summary.StdDev) 'StdDev'
    $iterationConfigured=0;if(-not[int]::TryParse([string]$summary.IterationCount,[ref]$iterationConfigured)-or$iterationConfigured-lt 1){throw 'V22_BDN_INVALID|configured iteration count is missing or invalid'}
    if($summary.Toolchain-cne'InProcessNoEmitToolchain'-or[int]$summary.LaunchCount-ne 1-or[int]$summary.WarmupCount-ne 2-or[int]$summary.InvocationCount-ne 1-or[int]$summary.UnrollFactor-ne 1){throw 'V22_BDN_INVALID|reported BDN profile differs from protocol'}
    $logText=[IO.File]::ReadAllText($StdoutPath)
    foreach($log in @(Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse -Filter '*.log')){$logText+=[Environment]::NewLine+[IO.File]::ReadAllText($log.FullName)}
    $actualRows=[Collections.Generic.List[object]]::new();$resultRows=[Collections.Generic.List[object]]::new()
    $actualPattern='^\s*WorkloadActual\s+(?<iteration>\d+):\s*(?<ops>\d+)\s+op,\s*(?<elapsed>(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?)\s*(?<unit>ns|us|µs|μs|ms|s)(?:,|\s)'
    $resultPattern='^\s*WorkloadResult\s+(?<iteration>\d+):\s*(?<ops>\d+)\s+op,\s*(?<elapsed>(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?)\s*(?<unit>ns|us|µs|μs|ms|s)(?:,|\s)'
    foreach($line in ($logText -split '\r?\n')){
        $actualMatch=[regex]::Match($line,$actualPattern)
        if($actualMatch.Success){$ns=Convert-BdnQuantityToNs ($actualMatch.Groups['elapsed'].Value+' '+$actualMatch.Groups['unit'].Value) 'WorkloadActual';$actualRows.Add([pscustomobject]@{Iteration=[int]$actualMatch.Groups['iteration'].Value;Operations=[double]$actualMatch.Groups['ops'].Value;Nanoseconds=$ns});continue}
        $resultMatch=[regex]::Match($line,$resultPattern)
        if($resultMatch.Success){$ns=Convert-BdnQuantityToNs ($resultMatch.Groups['elapsed'].Value+' '+$resultMatch.Groups['unit'].Value) 'WorkloadResult';$resultRows.Add([pscustomobject]@{Iteration=[int]$resultMatch.Groups['iteration'].Value;Operations=[double]$resultMatch.Groups['ops'].Value;Nanoseconds=$ns})}
    }
    if($actualRows.Count-ne$iterationConfigured-or$resultRows.Count-ne$iterationConfigured){throw 'V22_BDN_INVALID|raw WorkloadActual/WorkloadResult counts do not match configured iterations'}
    if(@($actualRows.Iteration|Select-Object -Unique).Count-ne$actualRows.Count-or@($resultRows.Iteration|Select-Object -Unique).Count-ne$resultRows.Count){throw 'V22_BDN_INVALID|duplicate raw iteration identity'}
    foreach($actual in $actualRows){$matches=@($resultRows|Where-Object Iteration -eq $actual.Iteration);if($matches.Count-ne 1-or[double]$matches[0].Operations-ne[double]$actual.Operations){throw 'V22_BDN_INVALID|WorkloadActual and WorkloadResult iteration identities do not reconcile'}}
    $meanMatch=[regex]::Match($logText,'(?im)^\s*Mean\s*=\s*(?<mean>[+-]?(?:(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d*)?|\.\d+))\s*(?<unit>ns|us|µs|μs|ms|s)\s*,.*?\bN\s*=\s*(?<n>\d+)')
    if(-not$meanMatch.Success){throw 'V22_BDN_INVALID|finite statistical Mean and N are not present in raw log'}
    $reportedN=[int]$meanMatch.Groups['n'].Value;$statMeanNs=Convert-BdnQuantityToNs ($meanMatch.Groups['mean'].Value+' '+$meanMatch.Groups['unit'].Value) 'RawLogMean'
    $upperFenceMatch=[regex]::Match($logText,'(?im)^\s*IQR\s*=.*?\bUpperFence\s*=\s*(?<upper>[+-]?(?:(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d*)?|\.\d+))\s*(?<unit>ns|us|µs|μs|ms|s)')
    if(-not$upperFenceMatch.Success){throw 'V22_BDN_INVALID|BenchmarkDotNet upper-outlier fence is missing from raw log'}
    $upperFenceNs=Convert-BdnQuantityToNs ($upperFenceMatch.Groups['upper'].Value+' '+$upperFenceMatch.Groups['unit'].Value) 'UpperFence'
    if($reportedN-lt 1-or$reportedN-gt$resultRows.Count){throw 'V22_BDN_INVALID|statistical N is outside raw result count'}
    $included=@($resultRows|Where-Object{$_.Nanoseconds-le$upperFenceNs});$removed=@($resultRows|Where-Object{$_.Nanoseconds-gt$upperFenceNs})
    if($included.Count-ne$reportedN){throw 'V22_BDN_INVALID|reported N does not reconcile with WorkloadResult samples and RemoveUpper fence'}
    $includedMeanNs=($included|Measure-Object -Property Nanoseconds -Average).Average;$rawMeanText=$meanMatch.Groups['mean'].Value+' '+$meanMatch.Groups['unit'].Value;$rawTolerance=Get-BdnPrintedRoundingToleranceNs $rawMeanText 'RawLogMean'
    if([Math]::Abs($includedMeanNs-$statMeanNs)-gt($rawTolerance+0.001)){throw 'V22_BDN_INVALID|reported Mean does not reconcile with included raw WorkloadResult measurements'}
    $summaryTolerance=Get-BdnPrintedRoundingToleranceNs ([string]$summary.Mean) 'SummaryMean'
    if([Math]::Abs($summaryMeanNs-$statMeanNs)-gt($summaryTolerance+$rawTolerance+0.001)){throw 'V22_BDN_INVALID|summary CSV Mean does not reconcile with raw-log Mean'}
    $summaryNormalized=Join-Path $OutputDirectory 'bdn-summary-normalized.csv';$measurementsNormalized=Join-Path $OutputDirectory 'bdn-measurements-normalized.csv'
    Write-NewText $summaryNormalized ('Mean,Error,StdDev,Unit,ConfiguredIterations,ReportedN,RawActualRows,RawResultRows,RemovedUpperOutliers,UpperFenceNs,MeanRoundingToleranceNs'+[Environment]::NewLine+($statMeanNs.ToString([Globalization.CultureInfo]::InvariantCulture)+','+$errorNs.ToString([Globalization.CultureInfo]::InvariantCulture)+','+$stdNs.ToString([Globalization.CultureInfo]::InvariantCulture)+',ns,'+$iterationConfigured+','+$reportedN+','+$actualRows.Count+','+$resultRows.Count+','+$removed.Count+','+$upperFenceNs.ToString([Globalization.CultureInfo]::InvariantCulture)+','+$rawTolerance.ToString([Globalization.CultureInfo]::InvariantCulture))+[Environment]::NewLine)
    $measurementLines=[Collections.Generic.List[string]]::new();$measurementLines.Add('IterationStage,Iteration,Operations,Nanoseconds,IncludedForMean')
    foreach($row in $resultRows){$measurementLines.Add(('Result,{0},{1},{2},{3}'-f$row.Iteration,$row.Operations.ToString([Globalization.CultureInfo]::InvariantCulture),$row.Nanoseconds.ToString([Globalization.CultureInfo]::InvariantCulture),[bool]($row.Nanoseconds-le$upperFenceNs)))}
    Write-NewText $measurementsNormalized (($measurementLines-join[Environment]::NewLine)+[Environment]::NewLine)
    $validated=Assert-V22BdnEvidence $summaryNormalized $measurementsNormalized
    return [pscustomobject]@{Status='BDN_COMPLETE';RawSummaryPath=$csv[0].FullName;RawSummarySHA256=(Get-FileHashHex $csv[0].FullName);NormalizedSummaryPath=$summaryNormalized;NormalizedMeasurementsPath=$measurementsNormalized;MeanMs=$validated.MeanNs/1000000.0;ErrorMs=$validated.ErrorNs/1000000.0;StdDevMs=$validated.StdDevNs/1000000.0;ConfiguredIterations=$iterationConfigured;ReportedN=$reportedN;ActualMeasurementRows=$actualRows.Count;ResultMeasurementRows=$resultRows.Count;MeasuredIterations=$reportedN;RemovedUpperOutliers=$removed.Count;RemovedUpperOutlierIterations=@($removed|ForEach-Object{$_.Iteration});UpperFenceNs=$upperFenceNs;OutlierMode='RemoveUpper, validated from raw WorkloadResult and raw UpperFence';MeasurementEvidence='Raw WorkloadActual and WorkloadResult rows were paired by iteration; statistical N and Mean were recomputed from WorkloadResult rows at or below raw UpperFence';Toolchain='InProcessNoEmitToolchain'}
}
function Get-TelemetryAssessment([string]$Path,[string]$BlockId,[DateTimeOffset]$BlockStarted,[DateTimeOffset]$BlockFinished,[double[]]$WindowBounds=@()){
    $csv=Read-V22TelemetryCsvFile $Path;$rawRows=@($csv.Rows)
    $assessment=Get-V22TelemetryAssessment $rawRows $script:TargetDatabase $script:RunId $BlockId
    $window=Get-V22TelemetryWindowAssessment $rawRows $BlockStarted $BlockFinished 5
    $dbIdentityValid=($window.ValidRows -gt 0 -and $window.WrongDatabaseRows -eq 0)
    $timeValid=($window.Status -ceq 'PASS')
    $valid=($csv.Status -ceq 'PASS' -and $assessment.Status -ceq 'TELEMETRY_VALID' -and $assessment.ValidTargetRows -gt 0 -and $dbIdentityValid -and $timeValid)
    $status=if($valid){'TELEMETRY_VALID'}else{'TELEMETRY_INVALID'}
    $result=[pscustomobject]@{Status=$status;RawPath=$Path;RawSHA256=if(Test-Path -LiteralPath $Path){Get-FileHashHex $Path}else{$null};RunId=$script:RunId;BlockId=$BlockId;TargetDatabase=$script:TargetDatabase;ExpectedDatabaseId=5;Rows=@($rawRows).Count;FileSchemaStatus=$csv.SchemaStatus;InvalidCsvRowCount=$csv.InvalidRowCount;InvalidCsvRowNumbers=$csv.InvalidRowNumbers;CsvParseError=$csv.Error;ValidTargetRows=$assessment.ValidTargetRows;ErrorRows=$assessment.ErrorRows;RejectedRows=$assessment.RejectedRows;DiagnosticMetadataErrors=$assessment.DiagnosticMetadataErrors;TargetDatabaseIdentityVerified=$dbIdentityValid;WithinBlockWindowVerified=$timeValid;WindowAssessment=$window;BlockStartedUtc=$BlockStarted.ToString('o');BlockFinishedUtc=$BlockFinished.ToString('o');SQLLogicalReads='DIAGNOSTIC_ONLY';TempdbCounters='DIAGNOSTIC_ONLY';DeadlockCounter='DIAGNOSTIC_ONLY'}
    $script:TelemetryAssessments.Add($result)
    return $result
}
function Get-Residue([string]$Label,[string]$Directory,[int]$TimeoutSeconds=20){
    $output=Join-Path $Directory ($Label+'.json')
    $arguments=@($script:RuntimeDll,'performance-residue','--run-id',$script:RunId,'--block-id',$Label,'--target-database',$script:TargetDatabase,'--output',$output)
    $record=Invoke-Tool 'dotnet' $arguments $script:RuntimeRoot $Label $Directory $TimeoutSeconds -DatabaseProcess
    $result=$null
    if(Test-Path -LiteralPath $output){try{$result=Get-Content -LiteralPath $output -Raw|ConvertFrom-Json}catch{}}
    return [pscustomobject]@{OutputPath=$output;ProcessExitCode=$record.ExitCode;Evidence=$result;Passed=($null -ne $result -and $result.Status -ceq 'RESIDUE_CLEAN' -and $result.TargetDatabase -ceq $script:TargetDatabase -and [int]$result.DatabaseId -eq 5)}
}
function Get-Admission([string]$BlockId,[string]$Profile,[double]$PreviousDropMB,[string]$HostLogPath){
    $deadline=[DateTime]::UtcNow.AddSeconds(60);$samples=[Collections.Generic.List[object]]::new();$reason='ADMISSION_WAIT_EXPIRED';$predictedDrop=if($Profile -ceq 'MIXED_REGRESSION'){[Math]::Max($PreviousDropMB*1.25,$PreviousDropMB+64.0)}else{$PreviousDropMB}
    while([DateTime]::UtcNow -lt $deadline){
        $first=Get-HostSnapshot;Add-JsonLine $HostLogPath ([ordered]@{Stage='ADMISSION';BlockId=$BlockId;Sample=$first});$samples.Add($first)
        if($first.FreeRamMB -le 128){$reason='EMERGENCY_HOST_LIMIT';break};if($first.FreeRamMB -le 512){$reason='HOST_LIMIT';break}
        Start-Sleep -Seconds 1
        $second=Get-HostSnapshot;Add-JsonLine $HostLogPath ([ordered]@{Stage='ADMISSION';BlockId=$BlockId;Sample=$second});$samples.Add($second)
        if($second.FreeRamMB -le 128){$reason='EMERGENCY_HOST_LIMIT';break};if($second.FreeRamMB -le 512){$reason='HOST_LIMIT';break}
        $predictedMinimum=[Math]::Round($second.FreeRamMB-$predictedDrop,1)
        $bothSafe=(@($samples|Select-Object -Last 2|Where-Object{[double]$_.FreeRamMB -gt 640}).Count -eq 2)
        if($bothSafe -and $predictedMinimum -gt 640){$reason='ADMITTED';break}
        Start-Sleep -Milliseconds 500
    }
    $last=$samples|Select-Object -Last 1
    $predMin=if($null -ne $last){[Math]::Round([double]$last.FreeRamMB-$predictedDrop,1)}else{$null}
    $status=if($reason -ceq 'ADMITTED'){'PASS'}elseif($reason -like '*HOST_LIMIT*'){'HOST_LIMIT'}else{'HOST_LIMIT'}
    return [pscustomobject]@{Status=$status;Reason=$reason;BlockId=$BlockId;Profile=$Profile;AdmissionSampleCount=$samples.Count;AdmissionSamples=@($samples|Select-Object -Last 2);PreviousObservedDropMB=$PreviousDropMB;PredictedNextDropMB=[Math]::Round($predictedDrop,1);PredictedMinimumMB=$predMin;SafeMinimumMB=640;HardFloorMB=512;EmergencyFloorMB=128;AdmissionWaitLimitSeconds=60;Allowed=($reason -ceq 'ADMITTED');RecordedUtc=[DateTime]::UtcNow.ToString('o')}
}
function Wait-Cooldown([string]$BlockId,[string]$Directory,[string]$HostLogPath){
    $started=[DateTime]::UtcNow;Start-Sleep -Seconds 5;$deadline=$started.AddSeconds(60);$checks=[Collections.Generic.List[object]]::new();$probe=0
    do{
        $probe++;$snapshot=Get-HostSnapshot;Add-JsonLine $HostLogPath ([ordered]@{Stage='COOLDOWN';BlockId=$BlockId;Sample=$snapshot})
        $residue=Get-Residue ('residue-'+$BlockId+'-'+$probe) $Directory
        $liveOwned=@($script:AllOwnedProcesses|Where-Object{-not $_.Completed -and $_.Process -and -not $_.Process.HasExited}).Count
        $safe=($snapshot.FreeRamMB -gt 640 -and $residue.Passed -and $liveOwned -eq 0)
        $checks.Add([pscustomobject]@{Probe=$probe;CapturedUtc=$snapshot.CapturedUtc;FreeRamMB=$snapshot.FreeRamMB;ResiduePassed=$residue.Passed;Residue=$residue.Evidence;LiveOwnedProcessCount=$liveOwned;Passed=$safe})
        if($safe){break}
        if([DateTime]::UtcNow -ge $deadline){break}
        Start-Sleep -Seconds 2
    }while([DateTime]::UtcNow -lt $deadline)
    $result=[pscustomobject]@{BlockId=$BlockId;Status=if(@($checks|Where-Object Passed).Count -gt 0){'PASS'}else{'FAIL'};StartedUtc=$started.ToString('o');FinishedUtc=[DateTime]::UtcNow.ToString('o');MinimumWaitSeconds=5;MaximumWaitSeconds=60;Checks=$checks.ToArray()}
    return $result
}
function Invoke-MeasurementBlock([string]$Scenario,[int]$Copies,[string]$Profile,[string]$BlockId,[string]$BlockKind,[double]$PreviousDropMB,[string]$RawRoot,[string]$HostLogPath,[int]$TimeoutSeconds=75){
    $script:DatabaseActivityStarted=$true;if($script:CurrentStage -in @('ISOLATED_PERFORMANCE','MIXED_PERFORMANCE')){$script:CanonicalWorkloadStarted=$true}
    $usesNbomber=$BlockKind -in @('NBOMBER','C1','C2','C4')
    $blockDirectory=Join-Path $RawRoot $BlockId
    New-Item -ItemType Directory -Path $blockDirectory|Out-Null
    $config=[ordered]@{RunId=$script:RunId;BlockId=$BlockId;CandidateId=$script:CandidateId;ProtocolVersion='2.2';Profile=$Profile;BlockKind=$BlockKind;Scenario=$Scenario;Copies=$Copies;CopiesPerScenario=$Copies;ScenarioProcessCount=if($Profile -ceq 'MIXED_REGRESSION'){6}else{1};TotalLogicalCopies=if($Profile -ceq 'MIXED_REGRESSION'){6*$Copies}else{$Copies};SourceSnapshotId=$script:SourceSnapshotId;WarmupSeconds=3;ConfiguredTimedSeconds=15;TargetDatabase=$script:TargetDatabase;Page=1;PageSize=10;Search=if($Scenario -in @('MasterPaged','LookupPaged')){'SanPham / empty'}else{'empty'};Login=if($Scenario -in @('DocumentPaged','DetailReportPaged','InventoryHistoricalReportPaged','InventoryCurrentBalancePaged')){'PERF_USER'}else{'NOT_USED_BY_OPERATION'};Warehouse='null';FromDate=if($Scenario -in @('DetailReportPaged','InventoryHistoricalReportPaged','InventoryCurrentBalancePaged')){'2025-01-01'}else{'NOT_USED'};ToDate=if($Scenario -in @('DetailReportPaged','InventoryHistoricalReportPaged','InventoryCurrentBalancePaged')){'2026-12-31'}else{'NOT_USED'};HistoricalMode=if($Scenario -eq 'InventoryHistoricalReportPaged'){'LEGACY'}else{'NOT_APPLICABLE'};UseCurrentBalance=if($Scenario -eq 'InventoryHistoricalReportPaged'){$false}elseif($Scenario -eq 'InventoryCurrentBalancePaged'){$true}else{$null};IndividualRequestRetry=$false;CreatedUtc=[DateTime]::UtcNow.ToString('o')}
    $configIdentity=Assert-V22CanonicalRunIdentity $script:CanonicalRunId $config.RunId $script:CandidateId $script:SourceSnapshotId $script:SessionId @([pscustomobject]@{RunId=$config.RunId;CandidateId=$config.CandidateId;BlockId=$config.BlockId})
if($configIdentity.Status -cne 'PASS'){throw 'CANONICAL_RUN_IDENTITY_INVALID|measurement block config mismatch'}
if($Profile -ceq 'MIXED_REGRESSION'){$mixedContract=New-V22MixedSemantics ('L'+$Copies);if($config.ScenarioProcessCount -ne $mixedContract.ScenarioProcessCount -or $config.CopiesPerScenario -ne $mixedContract.CopiesPerScenario -or $config.TotalLogicalCopies -ne $mixedContract.TotalLogicalCopies){throw 'V22_MIXED_WORKER_SEMANTICS_INVALID'}}
Write-NewJson (Join-Path $blockDirectory 'block-config.json') $config
    $blockStarted=[DateTimeOffset]::UtcNow;$children=[Collections.Generic.List[object]]::new();$telemetryRecord=$null;$admission=Get-Admission $BlockId $Profile $PreviousDropMB $HostLogPath
    $result=[ordered]@{RunId=$script:RunId;BlockId=$BlockId;Profile=$Profile;BlockKind=$BlockKind;Scenario=$Scenario;Copies=$Copies;Status='NOT_RUN';FailureType=$null;Admission=$admission;Requests=$null;Success=$null;Failed=$null;ErrorRate=$null;RPS=$null;MeanMs=$null;P50Ms=$null;P95Ms=$null;P99Ms=$null;MaxMs=$null;ConfiguredWindowMs=15000;ObservedWindowMs=$null;MeasuredStartUtc=$null;MeasuredStopUtc=$null;LogWindowSeconds=$null;ObservedCopies=$null;ObservedInstanceNumbers=@();TelemetryStatus='NOT_RUN';CleanupStatus='NOT_RUN';CleanupProcessProof=$null;HostStopActions=@();MinFreeRamMB=$null;ChildProcessIds=@();RawArtifactRoot=$blockDirectory;RawArtifacts=@();ProcessExitCodes=@();Reason=$null}
    if(-not$admission.Allowed){$result.Status='HOST_LIMIT';$result.FailureType=$admission.Reason;$result.Reason=$admission.Reason;$result.CleanupStatus='NO_CHILD_STARTED';Write-NewJson (Join-Path $blockDirectory 'block-result.json') $result;return [pscustomobject]$result}
    try{
        $reportDirectory=Join-Path $blockDirectory 'nbomber-report';$metadataPath=Join-Path $blockDirectory 'worker-metadata.json'
        if($BlockKind -ceq 'BDN'){
            $reportDirectory=Join-Path $blockDirectory 'bdn-output'
            $args=@($script:RuntimeDll,'performance-bdn','--scenario',$Scenario,'--run-id',$script:RunId,'--block-id',$BlockId,'--output-directory',$reportDirectory,'--metadata-path',$metadataPath)
            $children.Add((Start-OwnedProcess 'dotnet' $args $script:RuntimeRoot ($BlockId+'-bdn') 'CHILD' $blockDirectory))
        }elseif($usesNbomber){
            $args=@($script:RuntimeDll,'performance-load','--scenario',$Scenario,'--copies',[string]$Copies,'--warmup-seconds','3','--duration-seconds','15','--profile',$Profile,'--run-id',$script:RunId,'--block-id',$BlockId,'--target-database',$script:TargetDatabase,'--report-directory',$reportDirectory,'--metadata-path',$metadataPath)
            $children.Add((Start-OwnedProcess 'dotnet' $args $script:RuntimeRoot ($BlockId+'-load') 'CHILD' $blockDirectory))
        }else{throw 'Unknown measured block kind'}
        $targetPids=(@($children|ForEach-Object{$_.ChildProcessId}) -join ',')
        $telemetryPath=Join-Path $blockDirectory 'sql-telemetry.csv'
        $telemetryArgs=@($script:RuntimeDll,'performance-telemetry','--run-id',$script:RunId,'--block-id',$BlockId,'--target-database',$script:TargetDatabase,'--target-process-ids',$targetPids,'--output',$telemetryPath)
        $telemetryRecord=Start-OwnedProcess 'dotnet' $telemetryArgs $script:RuntimeRoot ($BlockId+'-telemetry') 'TELEMETRY' $blockDirectory
        $wait=Wait-OwnedGroup $children.ToArray() $TimeoutSeconds $HostLogPath $true $BlockId
        if($wait.TimedOut -and $null -eq $script:StopReason){$result.FailureType='HARNESS_TIMEOUT';$result.Reason='Owned workload exceeded bounded process timeout'}
        if($wait.MemoryLimitReason){$result.FailureType=if($wait.EmergencyStop){'EMERGENCY_HOST_LIMIT'}else{'HOST_LIMIT'};$result.Status=$result.FailureType;$result.Reason=$wait.MemoryLimitReason;$script:StopReason=$result.FailureType}
        elseif($wait.MonitorFailure){$result.FailureType='HOST_TELEMETRY_INVALID';$result.Status='INVALID';$result.Reason='Host telemetry failed; the owned child was stopped for safety, not classified as a memory-floor event';$script:StopReason='HOST_TELEMETRY_INVALID'}
        $result.HostStopActions=@($wait.HostStopActions)
        if(-not $telemetryRecord.Process.HasExited){$telemetryDeadline=[DateTime]::UtcNow.AddSeconds(20);while(-not$telemetryRecord.Process.HasExited -and [DateTime]::UtcNow -lt $telemetryDeadline){Start-Sleep -Milliseconds 250}}
        if(-not $telemetryRecord.Process.HasExited){[void](Stop-OwnedProcess $telemetryRecord 'TELEMETRY_TIMEOUT')}
        [void](Complete-OwnedProcess $telemetryRecord 'TELEMETRY_FINISHED')
        foreach($child in $children){$result.ProcessExitCodes+=@([pscustomobject]@{ProcessLabel=$child.Label;ChildProcessId=$child.ChildProcessId;ExitCode=$child.ExitCode;TimedOut=$child.TimedOut;StopReason=$child.StopReason;HostStopped=($null -ne $child.HostStopEvidence);TerminalMetadataStatus=$child.TerminalMetadataStatus;TerminalMetadataFailure=$child.TerminalMetadataFailure})}
        $blockFinished=[DateTimeOffset]::UtcNow
        $result.ChildProcessIds=@($children|ForEach-Object{$_.ChildProcessId})
        $hostSamples=@(Get-Content -LiteralPath $HostLogPath -ErrorAction SilentlyContinue|ForEach-Object{try{$line=ConvertFrom-Json $_;if($line.BlockId -ceq $BlockId -and $line.Sample){$line.Sample}}catch{}})
        $ramSamples=@($hostSamples|ForEach-Object{[double]$_.FreeRamMB})
        if($ramSamples.Count -gt 0){$result.MinFreeRamMB=($ramSamples|Measure-Object -Minimum).Minimum}
        if($usesNbomber){
            try{
                $nb=Get-NBomberArtifact $reportDirectory $Scenario $Copies $children[0].StdoutPath $metadataPath $script:RunId $BlockId $Profile
                $result.Requests=$nb.Metrics.Requests;$result.Success=$nb.Success;$result.Failed=$nb.Metrics.Failed;$result.ErrorRate=if($nb.Metrics.Requests -gt 0){$nb.Metrics.Failed/$nb.Metrics.Requests}else{$null};$result.RPS=$nb.Metrics.RPS;$result.MeanMs=$nb.Metrics.MeanMs;$result.P50Ms=$nb.Metrics.P50Ms;$result.P95Ms=$nb.Metrics.P95Ms;$result.P99Ms=$nb.Metrics.P99Ms;$result.MaxMs=$nb.Metrics.MaxMs;$result.ObservedWindowMs=$nb.Metrics.ObservedWindowMs;$result.MeasuredStartUtc=$nb.MeasuredStartUtc;$result.MeasuredStopUtc=$nb.MeasuredStopUtc;$result.LogWindowSeconds=$nb.LogWindowSeconds;$result.ObservedCopies=$nb.ObservedCopies;$result.ObservedInstanceNumbers=$nb.ObservedInstanceNumbers;$result.NBomberEvidence=$nb
                if($result.FailureType -in @('HOST_LIMIT','EMERGENCY_HOST_LIMIT')){$result.Status=$result.FailureType}
                elseif($result.FailureType -eq 'HOST_TELEMETRY_INVALID'){$result.Status='INVALID'}
                elseif($children[0].ExitCode -ne 0){$result.Status='FAIL';$result.FailureType=if($result.FailureType){$result.FailureType}else{'PRODUCT_ERROR'};$result.Reason='NBomber worker process returned a nonzero exit code'}
                elseif($nb.Metrics.Failed -ne 0){$result.Status='FAIL';$result.FailureType='PRODUCT_ERROR';$result.Reason='Timed window contains failed requests'}
                elseif($result.FailureType -eq 'HARNESS_TIMEOUT'){$result.Status='HARNESS_TIMEOUT'}else{$result.Status='PASS'}
            }catch{if($result.FailureType -in @('HOST_LIMIT','EMERGENCY_HOST_LIMIT')){$result.Status=$result.FailureType}elseif($result.FailureType -eq 'HOST_TELEMETRY_INVALID'){$result.Status='INVALID'}else{$result.Status='INVALID';$result.FailureType='HARNESS_INVALID';$result.Reason=Get-SafeText $_.Exception.Message}}
            $telemetry=Get-TelemetryAssessment $telemetryPath $BlockId $blockStarted $blockFinished
            $result.TelemetryStatus=$telemetry.Status;$result.Telemetry=$telemetry
            if($Copies -le 4 -and $result.Status -ceq 'PASS' -and $telemetry.Status -cne 'TELEMETRY_VALID'){$result.Status='INVALID';$result.FailureType='TELEMETRY_INVALID';$result.Reason='Required telemetry failed target/run/time validation'}
            $result.RawArtifacts=@(Get-ChildItem -LiteralPath $blockDirectory -File -Recurse|ForEach-Object{[pscustomobject]@{Path=$_.FullName;Size=$_.Length;SHA256=(Get-FileHashHex $_.FullName)}})
        }else{
            try{
                $stdoutPath=$children[0].StdoutPath
                $bdn=Get-BdnArtifact $reportDirectory $stdoutPath
                $result.BdnEvidence=$bdn;if($result.FailureType -notin @('HOST_LIMIT','EMERGENCY_HOST_LIMIT','HOST_TELEMETRY_INVALID')){$result.Status=if($children[0].ExitCode -eq 0){'PASS'}else{'INVALID'};$result.FailureType=if($children[0].ExitCode -eq 0){$null}else{'BDN_PROCESS_FAILED'}};$result.MeanMs=$bdn.MeanMs;$result.ErrorMs=$bdn.ErrorMs;$result.StdDevMs=$bdn.StdDevMs;$result.MeasuredIterations=$bdn.MeasuredIterations
            }catch{if($result.FailureType -in @('HOST_LIMIT','EMERGENCY_HOST_LIMIT')){$result.Status=$result.FailureType}elseif($result.FailureType -eq 'HOST_TELEMETRY_INVALID'){$result.Status='INVALID'}else{$result.Status='INVALID';$result.FailureType='BDN_INVALID';$result.Reason=Get-SafeText $_.Exception.Message}}
            $telemetry=Get-TelemetryAssessment $telemetryPath $BlockId $blockStarted $blockFinished
            $result.TelemetryStatus=$telemetry.Status;$result.Telemetry=$telemetry
            $result.RawArtifacts=@(Get-ChildItem -LiteralPath $blockDirectory -File -Recurse|ForEach-Object{[pscustomobject]@{Path=$_.FullName;Size=$_.Length;SHA256=(Get-FileHashHex $_.FullName)}})
        }
    }catch{
        $result.Status='FAIL';$result.FailureType='HARNESS_FAILED';$result.Reason=Get-SafeText $_.Exception.Message
        try{Write-NewJson (Join-Path $blockDirectory 'fatal-summary.json') ([ordered]@{Status='HARNESS_FAILED';Stage=$BlockId;FailureReason=$result.FailureType;SafeExceptionMessage=$result.Reason;CapturedUtc=[DateTime]::UtcNow.ToString('o')})}catch{}
    }finally{
        foreach($record in $children){if(-not$record.Completed){if(-not$record.Process.HasExited){[void](Stop-OwnedProcess $record 'BLOCK_FINALLY')};[void](Complete-OwnedProcess $record 'BLOCK_FINALLY')}}
        if($null -ne $telemetryRecord -and -not$telemetryRecord.Completed){if(-not$telemetryRecord.Process.HasExited){[void](Stop-OwnedProcess $telemetryRecord 'BLOCK_FINALLY')};[void](Complete-OwnedProcess $telemetryRecord 'BLOCK_FINALLY')}
        $ownedRecords=@($children)+@($telemetryRecord|Where-Object{$null -ne $_})
        $cleanupProof=Get-OwnedProcessCleanupProof $ownedRecords
        $result.CleanupProcessProof=$cleanupProof
        $terminalFailures=@($children|Where-Object TerminalMetadataStatus -ne 'WRITTEN').Count
        $cleanupActionFailures=@($script:CleanupRows|Where-Object{($_.ProcessLabel -in @($ownedRecords|ForEach-Object Label)) -and $_.Status -eq 'CLEANUP_FAILED'}).Count
        $result.CleanupStatus=if($cleanupProof.Passed -and $terminalFailures -eq 0 -and $cleanupActionFailures -eq 0 -and @($ownedRecords|Where-Object{-not $_.Completed -or -not $_.ProcessGone}).Count -eq 0){'PASS'}else{'FAIL'}
        $result.BlockStartedUtc=$blockStarted.ToString('o');$result.BlockFinishedUtc=[DateTimeOffset]::UtcNow.ToString('o')
        if(-not(Test-Path -LiteralPath (Join-Path $blockDirectory 'block-result.json'))){try{Write-NewJson (Join-Path $blockDirectory 'block-result.json') $result}catch{}}
    }
    return [pscustomobject]$result
}
function Test-CorrectnessOutput([string]$OutputRoot){
    $semantic=Get-Content -LiteralPath (Join-Path $OutputRoot 'V22-Semantic-Correctness-Results.json') -Raw|ConvertFrom-Json
    $inventory=Get-Content -LiteralPath (Join-Path $OutputRoot 'V22-Inventory-Correctness-Results.json') -Raw|ConvertFrom-Json
    $security=Get-Content -LiteralPath (Join-Path $OutputRoot 'V22-Security-Correctness-Results.json') -Raw|ConvertFrom-Json
    $prepost=Get-Content -LiteralPath (Join-Path $OutputRoot 'V22-DB-PrePost-Evidence.json') -Raw|ConvertFrom-Json
    $projection=New-V22CorrectnessEvidenceProjection -SemanticScenariosCompleted ([int]$semantic.ScenariosCompleted) -TotalCountPassed ([int]$semantic.TotalCountPassed) -SemanticStatus ([string]$semantic.Status) -InventoryStatus ([string]$inventory.Status) -SecurityStatus ([string]$security.Disposition) -DatabaseStateStatus ([string]$prepost.Status)
    $passed=($projection.Status -ceq 'PASS' -and $security.PositiveCase.Status -eq 'PASS' -and $security.PositiveCase.ContentMatchedAllowedWarehouseMappings -eq $true -and $prepost.PrePostSha256Equal -eq $true -and $prepost.ExpectedActiveModuleHashesMatchPreAndPost -eq $true)
    return [pscustomobject]@{Status=if($passed){'PASS'}else{'FAIL'};SemanticStatus=$projection.SemanticStatus;SemanticScenariosCompleted=$projection.SemanticScenariosCompleted;SemanticScenarios=$projection.SemanticScenarios;TotalCountPassed=$projection.TotalCountPassed;TotalCount=$projection.TotalCount;InventoryStatus=$projection.InventoryStatus;SecurityStatus=$projection.SecurityStatus;SecurityPositiveStatus=$security.PositiveCase.Status;DbPrePostStatus=$prepost.Status;PrePostSha256Equal=$prepost.PrePostSha256Equal;ExpectedActiveModuleHashesMatchPreAndPost=$prepost.ExpectedActiveModuleHashesMatchPreAndPost;FullDatasetValueEquality='NOT_VERIFIED';OutputRoot=$OutputRoot;DBEvidence=$prepost}
}
function Invoke-Correctness([string]$Label){
    $script:DatabaseActivityStarted=$true
    $output=Join-Path $script:PerformanceRunRoot $Label
    if(Test-Path -LiteralPath $output){throw 'Correctness output root already exists'}
    $phase3=Join-Path $script:Batch2Review 'V22-Phase3-Acceptance.json';$closure=Join-Path $script:Batch2Review 'V22-SQL-ActiveLegacyClosure-Final.json';$parity=Join-Path $script:Batch2Review 'V22-SQL-Definition-Parity-Final.json';$security=Join-Path $script:Batch2Review 'V22-Security-User-Coverage.json'
    $arguments=@($script:RuntimeDll,'correctness','--output',$output,'--phase3',$phase3,'--closure',$closure,'--parity',$parity,'--security',$security)
    $record=Invoke-Tool 'dotnet' $arguments $script:RuntimeRoot ($Label+'-correctness') $script:PerformanceRunRoot 300 -DatabaseProcess
    if($record.ExitCode -ne 0){return [pscustomobject]@{Status='FAIL';ProcessExitCode=$record.ExitCode;OutputRoot=$output;Reason='Exact-candidate correctness process exited nonzero'}}
    try{$result=Test-CorrectnessOutput $output;$result|Add-Member -NotePropertyName ProcessExitCode -NotePropertyValue $record.ExitCode;return $result}catch{return [pscustomobject]@{Status='FAIL';ProcessExitCode=$record.ExitCode;OutputRoot=$output;Reason=Get-SafeText $_.Exception.Message}}
}
function Get-2_1Comparator{
    $adaptiveRoot='P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_1_ADAPTIVE-20260916-FINAL';$mixedRoot='P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_1_MIXED-20260916-040719';$frozenRoot='P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_1-20260915-211500'
    $resultPath=Join-Path $adaptiveRoot 'v2.1-result.json';$nbPath=Join-Path $adaptiveRoot 'v2.1-nbomber-summary.csv';$bdnPath=Join-Path $adaptiveRoot 'v2.1-bdn-summary.csv';$mixedResultPath=Join-Path $mixedRoot 'mixed-result.json';$mixedRowsPath=Join-Path $mixedRoot 'mixed-per-scenario.csv';$mixedLevelsPath=Join-Path $mixedRoot 'mixed-level-summary.csv'
    foreach($path in @($resultPath,$nbPath,$bdnPath,$mixedResultPath,$mixedRowsPath,$mixedLevelsPath)){if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Canonical 2.1 comparator artifact missing: $path"}}
    $adaptive=Get-Content -LiteralPath $resultPath -Raw|ConvertFrom-Json;$mix=Get-Content -LiteralPath $mixedResultPath -Raw|ConvertFrom-Json
    if($adaptive.Result -cne 'BENCHMARK_V2_1_CORE_BASELINE_CREATED' -or -not$adaptive.PostIntegrityPassed -or $adaptive.CoreBaseline.MandatoryNBomberRows -ne 12 -or -not$adaptive.CoreBaseline.AllMandatoryRowsPass -or $mix.Result -cne 'BENCHMARK_V2_1_ALL_SIX_MIXED_COMPLETE'){throw 'Canonical 2.1 comparator acceptance evidence is not valid'}
    $nbRows=@(Import-Csv -LiteralPath $nbPath -Delimiter ',');$bdnRows=@(Import-Csv -LiteralPath $bdnPath -Delimiter ',');$mixedRows=@(Import-Csv -LiteralPath $mixedRowsPath -Delimiter ',');$mixedLevels=@(Import-Csv -LiteralPath $mixedLevelsPath -Delimiter ',')
    $required=@($nbRows|Where-Object Profile -in @('CORE_NBOMBER','EXTENDED_STANDARD'))
    if(@($required|Where-Object Status -ne 'PASS').Count -gt 0 -or @($required|Where-Object RequestCount -le 0).Count -gt 0){throw '2.1 canonical NBomber rows failed identity/count validation'}
    $files=@($resultPath,$nbPath,$bdnPath,(Join-Path $adaptiveRoot 'v2.1-candidate-manifest.json'),(Join-Path $adaptiveRoot 'v2.1-environment.json'),(Join-Path $adaptiveRoot 'v2.1-memory-class-evidence.json'),$mixedResultPath,$mixedRowsPath,$mixedLevelsPath,(Join-Path $mixedRoot 'mixed-run-metadata.json'),(Join-Path $frozenRoot 'candidate-manifest.json'))|Where-Object{Test-Path -LiteralPath $_ -PathType Leaf}
    $identities=@($files|ForEach-Object{[pscustomobject]@{Path=$_;SHA256=(Get-FileHashHex $_);Size=(Get-Item -LiteralPath $_).Length}})
    return [pscustomobject]@{Status='PASS';ProtocolVersion='2.1.0 / 2.1.1';AdaptiveResultId=$adaptive.BaselineId;AdaptiveResult=$adaptive.Result;AdaptiveResultPath=$resultPath;AdaptiveCoreRows=$adaptive.CoreBaseline.ObservedRows;AdaptiveCoreRequests=$adaptive.Comparison.V2_1MeasuredCoreRequests;BDNStatus=$adaptive.BDN.Status;BDNRows=$adaptive.BDN.Rows;ExtendedC4Status=$adaptive.ExtendedC4.Status;MixedResult=$mix.Result;MixedRoot=$mixedRoot;MixedLevels=@($mixedLevels|ForEach-Object{[pscustomobject]@{Level=$_.Level;Status=$_.Result;TotalWorkers=$_.TotalWorkers;Requests=$_.TotalRequests;Failed=$_.Failed}});Workload=[ordered]@{Scenarios=$script:Scenarios;Page=1;PageSize=10;WarmupSeconds=3;TimedSeconds=15;Cores=@('C1','C2');Extended='C4';MixedLevels=@('L1','L2','L4','L8');MixedWorkerMeaning='one KeepConstant copy is one worker'};HostAndToolchainEvidence=[ordered]@{AdaptiveEnvironmentPath=(Join-Path $adaptiveRoot 'v2.1-environment.json');MixedEnvironmentPath=(Join-Path $mixedRoot 'mixed-environment.json');FrozenRuntimeRoot=$frozenRoot};Artifacts=$identities;StatisticalLimitation='SINGLE-WINDOW HISTORICAL COMPARISON'}
}
function Get-Delta([double]$Old,[double]$New){if($Old -eq 0){return $null};return [Math]::Round((($New-$Old)/$Old)*100.0,3)}
function Get-CanonicalPreservationProof{
    $path=Join-Path $script:ReviewRoot 'Previous-Evidence-Preservation.json'
    if(Test-Path -LiteralPath $path){return (Get-Content -LiteralPath $path -Raw|ConvertFrom-Json)}
    return $null
}
function Ensure-ReviewArtifact([string]$Name,[string]$Reason){
    $path=Join-Path $script:ReviewRoot $Name
    if(Test-Path -LiteralPath $path){return}
    if($Name.EndsWith('.md')){Write-NewText $path ("# $Name`n`nStatus: SKIPPED`n`nReason: $Reason`n")}
    else{Write-NewJson $path ([ordered]@{Status='SKIPPED';Reason=$Reason;RunId=$script:RunId;RecordedUtc=[DateTime]::UtcNow.ToString('o')})}
}
function Get-FinalProcessProbe([object]$Identity){
    $probeUtc=[DateTime]::UtcNow.ToString('o');$status='UNKNOWN';$observedStartUtc=$null;$errorMessage=$null
    try{
        $process=[Diagnostics.Process]::GetProcessById([int]$Identity.PID)
        try{
            $observedStartUtc=$process.StartTime.ToUniversalTime().ToString('o')
            $expectedTicks=([DateTimeOffset]::Parse([string]$Identity.ProcessStartUtc)).UtcDateTime.Ticks
            $observedTicks=([DateTimeOffset]::Parse($observedStartUtc)).UtcDateTime.Ticks
            $status=if($expectedTicks -eq $observedTicks){'OWNED_PROCESS_PRESENT'}else{'PID_REUSED_DIFFERENT_PROCESS'}
        }catch{$status='IDENTITY_UNVERIFIED';$errorMessage=Get-SafeText $_.Exception.Message}
        finally{$process.Dispose()}
    }catch [ArgumentException]{$status='ABSENT'}catch{$status='UNKNOWN';$errorMessage=Get-SafeText $_.Exception.Message}
    return [ordered]@{CanonicalRunId=$script:CanonicalRunId;CandidateId=$script:CandidateId;PID=[int]$Identity.PID;ProcessIdentity=[string]$Identity.ProcessIdentity;ExpectedProcessStartUtc=[string]$Identity.ProcessStartUtc;ObservedProcessStartUtc=$observedStartUtc;ProbeUtc=$probeUtc;ProcessExists=($status -notin @('ABSENT','PID_REUSED_DIFFERENT_PROCESS'));Status=$status;Error=$errorMessage}
}
function Invoke-PersistedFinalCleanup{
    $inventoryPath=Join-Path $script:ReviewRoot 'V22-Final-Owned-Process-Inventory.json'
    $pidPath=Join-Path $script:ReviewRoot 'V22-Final-PerPid-Process-Probes.json'
    $helperPath=Join-Path $script:ReviewRoot 'V22-Final-Telemetry-Process-Probes.json'
    $sqlPath=Join-Path $script:ReviewRoot 'V22-Final-Sql-Residue-Probe.json'
    $aggregatePath=Join-Path $script:ReviewRoot 'V22-Final-Aggregate-Cleanup.json'
    $failureMessage=$null
    try{
        if([string]::IsNullOrWhiteSpace($script:CanonicalRunId)-or[string]::IsNullOrWhiteSpace($script:CandidateId)){throw 'FINAL_CLEANUP_IDENTITY_MISSING'}
        $cleanupRoot=Join-Path $script:PerformanceRunRoot 'final-cleanup';if(-not(Test-Path -LiteralPath $cleanupRoot -PathType Container)){New-Item -ItemType Directory -Path $cleanupRoot|Out-Null}
        foreach($record in $script:AllOwnedProcesses){if($record.RunId -cne $script:CanonicalRunId){throw 'FINAL_CLEANUP_OWNED_PROCESS_RUN_ID_MISMATCH'};if([string]::IsNullOrWhiteSpace([string]$record.CandidateId)){$record.CandidateId=$script:CandidateId}elseif($record.CandidateId -cne $script:CandidateId){throw 'FINAL_CLEANUP_OWNED_PROCESS_CANDIDATE_MISMATCH'}}
        $inventory=New-V22FinalOwnedProcessInventory $script:AllOwnedProcesses.ToArray() $script:CanonicalRunId $script:CandidateId
        New-V22FinalizerJson $inventoryPath $inventory
        $inventory=Get-Content -LiteralPath $inventoryPath -Raw|ConvertFrom-Json
        $pidRows=[Collections.Generic.List[object]]::new()
        foreach($record in $inventory.ProcessRecords){$pidRows.Add((Get-FinalProcessProbe $record))}
        $pidEvidence=[ordered]@{SchemaVersion='warehouse-benchmark-v22-final-per-pid-probes/1';CanonicalRunId=$script:CanonicalRunId;CandidateId=$script:CandidateId;CapturedUtc=[DateTime]::UtcNow.ToString('o');ProbeCount=$pidRows.Count;Probes=$pidRows.ToArray()}
        New-V22FinalizerJson $pidPath $pidEvidence
        $helperRows=[Collections.Generic.List[object]]::new()
        foreach($record in $inventory.ProcessRecords|Where-Object ProcessRole -in @('TELEMETRY','TOOL','DB_TOOL')){$helperRows.Add(($pidRows|Where-Object ProcessIdentity -CEQ $record.ProcessIdentity|Select-Object -First 1))}
        $helperEvidence=[ordered]@{SchemaVersion='warehouse-benchmark-v22-final-helper-probes/1';CanonicalRunId=$script:CanonicalRunId;CandidateId=$script:CandidateId;CapturedUtc=[DateTime]::UtcNow.ToString('o');ProbeCount=$helperRows.Count;Probes=$helperRows.ToArray()}
        New-V22FinalizerJson $helperPath $helperEvidence
        if($script:DatabaseActivityStarted){
            $residue=Get-Residue 'final-residue' $cleanupRoot 60
            $sqlRecord=$script:AllOwnedProcesses|Where-Object Label -CEQ 'final-residue'|Select-Object -Last 1
            $sqlProbe=$null
            if($null -ne $sqlRecord){$identity=[pscustomobject]@{PID=$sqlRecord.ChildProcessId;ProcessIdentity=('{0}|{1}' -f $sqlRecord.ChildProcessId,$sqlRecord.ProcessStartUtc);ProcessStartUtc=$sqlRecord.ProcessStartUtc};$observed=Get-FinalProcessProbe $identity;$sqlProbe=[ordered]@{CanonicalRunId=$script:CanonicalRunId;CandidateId=$script:CandidateId;PID=$sqlRecord.ChildProcessId;ProcessIdentity=$identity.ProcessIdentity;ProcessStartUtc=$sqlRecord.ProcessStartUtc;ProbeUtc=$observed.ProbeUtc;Status=$observed.Status;ProcessGone=[bool]$sqlRecord.ProcessGone;CleanupStatus=$sqlRecord.CleanupStatus}}
            $sqlStatus=if($residue.Passed -and $null -ne $sqlProbe -and $sqlProbe.CleanupStatus -eq 'PASS' -and $sqlProbe.ProcessGone -and $sqlProbe.Status -in @('ABSENT','PID_REUSED_DIFFERENT_PROCESS')){'PASS'}else{'FAIL'}
            $sqlEvidence=[ordered]@{SchemaVersion='warehouse-benchmark-v22-final-sql-residue/1';CanonicalRunId=$script:CanonicalRunId;CandidateId=$script:CandidateId;Status=$sqlStatus;ProcessExitCode=$residue.ProcessExitCode;OutputPath=$residue.OutputPath;Evidence=$residue.Evidence;ProbeProcess=$sqlProbe;DatabaseActivityOccurred=$true;CapturedUtc=[DateTime]::UtcNow.ToString('o')}
        }else{
            $residue=[pscustomobject]@{Passed=$false;ProcessExitCode=$null;OutputPath=$null;Evidence=[ordered]@{Status='NOT_APPLICABLE_NO_DATABASE_ACTIVITY';TargetDatabase=$script:TargetDatabase;DatabaseId=5;Reason='No DB process was launched because earlier gates stopped the run'}}
            $sqlEvidence=[ordered]@{SchemaVersion='warehouse-benchmark-v22-final-sql-residue/1';CanonicalRunId=$script:CanonicalRunId;CandidateId=$script:CandidateId;Status='NOT_APPLICABLE_NO_DATABASE_ACTIVITY';ProcessExitCode=$null;OutputPath=$null;Evidence=$residue.Evidence;ProbeProcess=$null;DatabaseActivityOccurred=$false;CapturedUtc=[DateTime]::UtcNow.ToString('o')}
        }
        New-V22FinalizerJson $sqlPath $sqlEvidence
        $inventory=Get-Content -LiteralPath $inventoryPath -Raw|ConvertFrom-Json
        $pidEvidence=Get-Content -LiteralPath $pidPath -Raw|ConvertFrom-Json
        $helperEvidence=Get-Content -LiteralPath $helperPath -Raw|ConvertFrom-Json
        $sqlEvidence=Get-Content -LiteralPath $sqlPath -Raw|ConvertFrom-Json
        $aggregate=Test-V22FinalCleanupEvidence $inventory $pidEvidence $helperEvidence $sqlEvidence $script:CanonicalRunId $script:CandidateId
        $inputFiles=@($inventoryPath,$pidPath,$helperPath,$sqlPath)|ForEach-Object{[pscustomobject]@{Path=$_;SHA256=Get-FileHashHex $_}}
        $aggregate.InputEvidence=$inputFiles
        New-V22FinalizerJson $aggregatePath $aggregate
        $persistedAggregate=Get-Content -LiteralPath $aggregatePath -Raw|ConvertFrom-Json
        $readbackPath=Join-Path $script:ReviewRoot 'V22-Final-Cleanup-Persistence-Verification.json'
        $readback=Test-V22FinalCleanupReadback $inventoryPath $pidPath $helperPath $sqlPath $aggregatePath $script:CanonicalRunId $script:CandidateId
        New-V22FinalizerJson $readbackPath $readback
        if($readback.Status -cne 'PASS'){throw 'FINAL_CLEANUP_PERSISTENCE_FAILED; persisted evidence did not pass reread/count/hash validation'}
        $script:PersistedOwnedInventory=$inventory;$script:FinalCleanupProof=$persistedAggregate
        $script:FinalCleanupEvidence=[ordered]@{InventoryPath=$inventoryPath;PerPidPath=$pidPath;HelperPath=$helperPath;SqlResiduePath=$sqlPath;AggregatePath=$aggregatePath;AggregateSHA256=$readback.AggregateSHA256;ReadbackPath=$readbackPath;ReadbackSHA256=(Get-FileHashHex $readbackPath);AggregateStatus=$persistedAggregate.AggregateCleanupStatus;AggregateExpectedCount=$persistedAggregate.OwnedProcessCount;ProcessCount=$inventory.ProcessCount;PidProbeCount=$pidEvidence.ProbeCount;HelperProbeCount=$helperEvidence.ProbeCount;InputEvidence=$inputFiles}
        return [pscustomobject]@{Status=$persistedAggregate.AggregateCleanupStatus;Aggregate=$persistedAggregate;Evidence=$script:FinalCleanupEvidence}
    }catch{
        $failureMessage=Get-SafeText $_.Exception.Message;$script:FinalCleanupProof=[pscustomobject]@{AggregateCleanupStatus='FAIL';Failure=$failureMessage};$script:FinalCleanupEvidence=[ordered]@{Status='FAIL';Failure=$failureMessage;InventoryPath=$inventoryPath;PerPidPath=$pidPath;HelperPath=$helperPath;SqlResiduePath=$sqlPath;AggregatePath=$aggregatePath}
        $failureArtifacts=@(
            @{Path=$inventoryPath;Schema='warehouse-benchmark-v22-final-owned-process-inventory/1';Kind='OWNED_PROCESS_INVENTORY'},
            @{Path=$pidPath;Schema='warehouse-benchmark-v22-final-per-pid-probes/1';Kind='PER_PID_PROBES'},
            @{Path=$helperPath;Schema='warehouse-benchmark-v22-final-helper-probes/1';Kind='HELPER_PROBES'},
            @{Path=$sqlPath;Schema='warehouse-benchmark-v22-final-sql-residue/1';Kind='SQL_RESIDUE'},
            @{Path=$aggregatePath;Schema='warehouse-benchmark-v22-final-aggregate-cleanup/1';Kind='AGGREGATE_CLEANUP'},
            @{Path=(Join-Path $script:ReviewRoot 'V22-Final-Cleanup-Persistence-Verification.json');Schema='warehouse-benchmark-v22-final-cleanup-readback/1';Kind='CLEANUP_PERSISTENCE_VERIFICATION'}
        )
        foreach($item in $failureArtifacts){if(-not(Test-Path -LiteralPath $item.Path)){try{New-V22FinalizerJson $item.Path ([ordered]@{SchemaVersion=$item.Schema;CanonicalRunId=$script:CanonicalRunId;CandidateId=$script:CandidateId;Status='FAIL';AggregateCleanupStatus='FAIL';FailureStage=$script:CurrentStage;Failure=$failureMessage;EvidenceType=$item.Kind;RecordedUtc=[DateTime]::UtcNow.ToString('o')})}catch{}}}
        return [pscustomobject]@{Status='FAIL';Failure=$failureMessage;Evidence=$script:FinalCleanupEvidence}
    }
}

$script:Batch2Review='P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_2_BATCH2_FINAL_CLOSURE_20261001_01\Batch2-Final-Review-Pack'
$script:StaticChecksPath=$null;$script:SelfTestPath=$null;$script:FailureInjectionPath=$null;$script:PredictedInventoryPath=$null;$script:RuntimeRoot=$null;$script:RuntimeDll=$null;$script:CandidateManifestPath=$null;$script:InitialGuardPath=$null;$script:Preflight=$null;$script:AfterIsolated=$null;$script:AfterMixed=$null;$script:Comparator=$null;$script:ProbeEvidence=$null;$script:InitialPreservationSummaryHash=$null;$script:SourceChanges=@();$script:V21Rows=@();$script:BDNRows=@();$script:HostLogPath=Join-Path $script:PerformanceRunRoot 'host-telemetry.jsonl';$script:isolatedBeginSnapshot=$null;$manifest=$null;$buildAttestation=$null;$toolCompare=$null;$isolatedManifest=$null;$mixedManifest=$null;$candidateManifest=$null;$manifestPath=Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Manifest.json';$candidateHash=$null;$candidatePass=$false;$correctnessPass=$false;$telemetryPass=$false;$mixedPass=$false;$preservationPass=$false;$comparatorPass=$false;$c4Pass=$false;$bdnPass=$false;$isolatedCorePass=$false;$isolatedTelemetry=$false;$isolatedIntegrity=$false;$postPass=$false;$cleanupStatus='NOT_RUN';$isolatedTotal=[long]0;$isolatedFailed=[long]0;$mixedFailed=[long]0;$coreRows=@();$c4Rows=@();$bdnRows=@();$isolatedTotals=$null;$telemetryOut=$null;$bdnOut=$null;$mixedOut=$null;$cleanupOut=$null;$prepost=$null;$limitations=$null;$coreGate=$null;$artifactIndexPath=Join-Path $script:PerformanceRunRoot 'raw-artifact-hashes.json';$artifactIndexHash=$null;$zipHash=$null;$requestFiles=@()
$cleanupPersistencePass=$false;$cleanupReadback=$null;$cleanupReadbackPath=Join-Path $script:ReviewRoot 'V22-Final-Cleanup-Persistence-Verification.json';$finalLint=$null

try{
    # Explicit offline fixtures abort before tools, build, DB, or workload.
    if($OfflineFailureFixture -cne 'NONE'){throw 'OFFLINE_FIXTURE_ORIGINAL_FAILURE'}
    if(-not(Test-Path -LiteralPath $script:RepoRoot -PathType Container) -or -not(Test-Path -LiteralPath $script:PriorReviewRoot -PathType Container) -or -not(Test-Path -LiteralPath $script:LogsRoot -PathType Container)){throw 'Required repository, historical evidence, or Batch 3 logs root is missing'}
    if(Test-Path -LiteralPath $script:FinalReviewRoot){throw 'FINAL_REVIEW_PACK_ROOT_EXISTS; refusing overwrite'}
    if((Test-Path -LiteralPath $script:FinalReviewZipPath) -or (Test-Path -LiteralPath $script:FinalReviewZipHashPath) -or (Test-Path -LiteralPath $script:FinalReviewHashesPath)){throw 'FINAL_REVIEW_DELIVERY_ARTIFACT_EXISTS; refusing overwrite'}
    if(Test-Path -LiteralPath $script:ReviewRoot){throw 'CANONICAL_RUN_EVIDENCE_ROOT_EXISTS; refusing overwrite'}
    if(Test-Path -LiteralPath $script:PerformanceRunRoot){throw 'BATCH3_OUTPUT_ROOT_EXISTS; refusing resume or overwrite'}
    if(Test-Path -LiteralPath $script:OneShotClaimPath){throw 'CANONICAL_WORKLOAD_ALREADY_CLAIMED; a second canonical attempt is forbidden'}
    New-Item -ItemType Directory -Path $script:ReviewRoot|Out-Null
    if(-not(Test-Path -LiteralPath $script:Batch2Review -PathType Container)){throw 'Batch 2 final review pack is missing'}
    if([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('TKS_V22_CONNECTION_STRING','Process'))){throw 'TKS_V22_CONNECTION_STRING is missing in the current process; secret value is never logged'}
    [void](New-V22RunRoot $script:PerformanceRunRoot)
    $script:CurrentStage='PRESERVATION_PRE_GATE';Write-RunProgress $script:CurrentStage 'IN_PROGRESS'
    $preSummaryPath=Join-Path $script:PriorReviewRoot 'Previous-Evidence-Preservation.json'
    $archivedSummary=Join-Path $script:LogsRoot 'Previous-Evidence-Preservation-Prebatch3-Summary.json'
    $prebuildPath=Join-Path $script:ReviewRoot 'Previous-Evidence-Preservation-Prebuild.json'
    $preManifestPath=Join-Path $script:LogsRoot 'preservation-pre-files.json'
    if(-not(Test-Path -LiteralPath $preManifestPath -PathType Leaf)){throw 'Preservation PRE manifest is missing'}
    if(Test-Path -LiteralPath $prebuildPath){throw 'New-run preservation PRE evidence already exists; refusing overwrite'}
    if((Test-Path -LiteralPath $archivedSummary) -and (Test-Path -LiteralPath $preSummaryPath) -and (Get-FileHashHex $archivedSummary)-cne(Get-FileHashHex $preSummaryPath)){throw 'Historical preservation summary copies differ'}
    $script:PreSummarySourcePath=if(Test-Path -LiteralPath $archivedSummary){$archivedSummary}else{$preSummaryPath}
    if(-not(Test-Path -LiteralPath $script:PreSummarySourcePath -PathType Leaf)){throw 'Preservation PRE summary is missing'}
    $preSummary=Get-Content -LiteralPath $script:PreSummarySourcePath -Raw|ConvertFrom-Json
    $preManifestHash=Get-FileHashHex $preManifestPath
    if($preSummary.Status -cne 'PRE_CAPTURED_PASS' -or $preSummary.V21MismatchCount -ne 0 -or @($preSummary.PreviousPacks|Where-Object Status -ne 'PASS').Count -gt 0 -or $preSummary.PreManifestSHA256 -cne $preManifestHash){throw 'Existing preservation PRE evidence failed identity verification'}
    $script:InitialPreservationSummaryHash=Get-FileHashHex $script:PreSummarySourcePath
    $prebuild=[ordered]@{SchemaVersion='warehouse-benchmark-v22-batch3-preservation-prebuild/1';Status='PRE_CAPTURED_POST_PENDING';PreSummaryPath=$script:PreSummarySourcePath;PreSummarySHA256=$script:InitialPreservationSummaryHash;PreManifestPath=$preManifestPath;PreManifestSHA256=$preManifestHash;V21RootCount=$preSummary.V21RootCount;V21FileCount=$preSummary.V21FileCount;V21MismatchCount=$preSummary.V21MismatchCount;PreviousPacks=$preSummary.PreviousPacks;Batch2FinalFileCount=$preSummary.Batch2FinalFileCount;Batch2FinalTreeSHA256=$preSummary.Batch2FinalTreeSHA256;CapturedUtc=[DateTime]::UtcNow.ToString('o')}
    Write-NewJson $prebuildPath $prebuild
    $script:CurrentStage='STATIC_AND_OFFLINE_GATES';Write-RunProgress $script:CurrentStage 'IN_PROGRESS'
    $testRoot=Join-Path $script:PerformanceRunRoot 'tests';New-Item -ItemType Directory -Path $testRoot|Out-Null
    $parseRows=[Collections.Generic.List[object]]::new();$sourceFiles=@(Get-ChildItem -LiteralPath $PSScriptRoot -File -Recurse|Where-Object Extension -in @('.ps1','.psm1'))
    foreach($file in $sourceFiles){$tokens=$null;$parseErrors=$null;[Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$parseErrors)|Out-Null;$parseRows.Add([pscustomobject]@{Path=$file.FullName;Status=if($parseErrors.Count -eq 0){'PASS'}else{'FAIL'};ErrorCount=$parseErrors.Count;Errors=@($parseErrors|ForEach-Object{$_.Message})})}
    $pidAssignments=@($sourceFiles|ForEach-Object{Select-String -LiteralPath $_.FullName -Pattern '(?im)^\s*\$(?:pid)\s*=' -ErrorAction SilentlyContinue}|ForEach-Object{[pscustomobject]@{Path=$_.Path;Line=$_.LineNumber;Text=$_.Line.Trim()}})
    $staticStatus=if(@($parseRows|Where-Object Status -ne 'PASS').Count -eq 0 -and $pidAssignments.Count -eq 0){'PASS'}else{'FAIL'}
    $static=[ordered]@{SchemaVersion='warehouse-benchmark-v22-batch3-static-checks/1';Status=$staticStatus;PowerShellParseStatus=if(@($parseRows|Where-Object Status -ne 'PASS').Count -eq 0){'PASS'}else{'FAIL'};ParsedFileCount=$sourceFiles.Count;ParseRows=$parseRows.ToArray();AutomaticPidAssignments=$pidAssignments;ConnectionStringPersisted=$false;PerformanceLoadRun='NOT_RUN';CapturedUtc=[DateTime]::UtcNow.ToString('o')}
    $script:StaticChecksPath=Join-Path $script:LogsRoot ('performance-static-checks-'+$script:RunId+'.json');Write-NewJson $script:StaticChecksPath $static
    if($staticStatus -ne 'PASS'){throw 'PowerShell parse or automatic PID collision static check failed'}
    $script:SelfTestPath=Join-Path $testRoot 'offline-selftest.json';$selfArgs=@('-NoProfile','-File',$script:OfflineSelfTest,'-EvidencePath',$script:SelfTestPath,'-ReportLintPath',$script:ReportLintPath);$selfRecord=Invoke-Tool (Join-Path $PSHOME 'pwsh.exe') $selfArgs $script:RepoRoot 'offline-selftest' $testRoot 120
    if($selfRecord.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $script:SelfTestPath)){throw 'Offline self-test failed; candidate build is blocked'}
    $selfResult=Get-Content -LiteralPath $script:SelfTestPath -Raw|ConvertFrom-Json
    if($selfResult.Status -cne 'PASS'){throw 'Offline self-test result is not PASS'}
    $script:FailureInjectionPath=Join-Path $testRoot 'failure-injection\V22-Failure-Injection-Results.json';$fiArgs=@('-NoProfile','-File',$script:FailureInjection,'-EvidenceRoot',(Split-Path -Parent $script:FailureInjectionPath));$fiRecord=Invoke-Tool (Join-Path $PSHOME 'pwsh.exe') $fiArgs $script:RepoRoot 'failure-injection' $testRoot 180
    if($fiRecord.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $script:FailureInjectionPath)){throw 'Mandatory offline failure injection failed; candidate build is blocked'}
    $fiResult=Get-Content -LiteralPath $script:FailureInjectionPath -Raw|ConvertFrom-Json
    if($fiResult.Status -cne 'PASS' -or $fiResult.Failed -ne 0){throw 'Mandatory offline failure injection did not PASS'}
    $script:MixedHarnessTestsPath=Join-Path $testRoot 'mixed-harness\V22-Mixed-Harness-Tests.json';$mixedTestArgs=@('-NoProfile','-File',$script:MixedHarnessTests,'-EvidencePath',$script:MixedHarnessTestsPath);$mixedTestRecord=Invoke-Tool (Join-Path $PSHOME 'pwsh.exe') $mixedTestArgs $script:RepoRoot 'mixed-harness-tests' $testRoot 180
    if($mixedTestRecord.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $script:MixedHarnessTestsPath)){throw 'Mixed metadata/integration/finalizer tests failed; candidate build is blocked'}
    $mixedTestResult=Get-Content -LiteralPath $script:MixedHarnessTestsPath -Raw|ConvertFrom-Json
    if($mixedTestResult.Status -cne 'PASS' -or $mixedTestResult.Failed -ne 0){throw 'Mixed harness offline test result is not PASS'}
    $mixedHarnessOfflinePass=$true

    $specSource=Join-Path 'P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_2_BATCH1_WHB22-20260930-65EA9FB0\Batch1-Review-Pack' 'V22-Source-Inventory-Spec.md'
    if(-not(Test-Path -LiteralPath $specSource -PathType Leaf)){throw 'Batch 1 canonical source inventory specification is missing'}
    $specText=[IO.File]::ReadAllText($specSource)+"`n`n## Batch 3 performance candidate scope`n`nThe canonical rule above is applied to the current repository root. All files under benchmarks/v2.2 are included except generated/build/run evidence; MSBuild-evaluated compile and project-reference inputs are inventoried transitively for TKS_Thuc_Tap_V11_Benchmarks_V22 and Data_Access. This Batch 3 inventory includes the performance runner, candidate builder, guard, protocol, tests, and both evaluated project closures. Candidate identity is WHB22-PERF-20261002- plus the first eight uppercase characters of the canonical inventory SHA-256. The source snapshot is copied and hashed before Release Build A/B; any mismatch blocks freeze."
    $specPath=Join-Path $script:ReviewRoot 'V22-Performance-Source-Inventory-Spec.md';if(Test-Path -LiteralPath $specPath){if([IO.File]::ReadAllText($specPath) -cne $specText){throw 'Existing source inventory spec differs; refusing overwrite'}}else{Write-NewText $specPath $specText}
    $script:PredictedInventoryPath=Join-Path $script:LogsRoot ('performance-source-inventory-predicted-'+$script:RunId+'.json')
    & $script:InventoryBuilder -RepoRoot $script:RepoRoot -OutputPath $script:PredictedInventoryPath|Out-Null
    $predicted=Get-Content -LiteralPath $script:PredictedInventoryPath -Raw|ConvertFrom-Json
    if(@($predicted.MissingInputs).Count -gt 0){throw 'Canonical source inventory contains missing inputs'}
    $script:CandidateId='WHB22-PERF-20261002-'+$predicted.InventorySHA256.Substring(0,8).ToUpperInvariant()
    $script:ProtocolInfo.CandidateId=$script:CandidateId;$script:ProtocolInfo.SourceInventorySHA256=$predicted.InventorySHA256;$script:ProtocolInfo.Status='PERFORMANCE_CANDIDATE_BUILD_AUTHORIZED_BY_GATES'
    Write-NewJson (Join-Path $script:ReviewRoot 'V22-Performance-Protocol.json') $script:ProtocolInfo

    $nugetPre=$null
    if(Test-Path -LiteralPath $script:NuGetCachePrePath){throw 'External NuGet cache pre-inventory already exists'}
    & $script:NuGetCacheInventoryScript -PackageCacheRoot $script:NuGetCacheRoot -OutputPath $script:NuGetCachePrePath | Out-Null
    $nugetPre=Get-Content -LiteralPath $script:NuGetCachePrePath -Raw|ConvertFrom-Json
    if($nugetPre.Status -cne 'PASS'){throw 'External NuGet package cache inventory failed; candidate build is blocked'}
    $priorStoragePath=Join-Path $script:PriorReviewRoot 'V22-Final-Rerun-Storage-Admission.json'
    if(-not(Test-Path -LiteralPath $priorStoragePath -PathType Leaf)){throw 'Measured historical storage admission evidence is missing'}
    $priorStorage=Get-Content -LiteralPath $priorStoragePath -Raw|ConvertFrom-Json
    if($priorStorage.Status -cne 'PASS' -or [long]$priorStorage.HistoricalFootprint.RootTotalBytes -le 0){throw 'Historical storage footprint evidence is invalid'}
    $pDrive=Get-PSDrive -Name P -ErrorAction Stop;$cDrive=Get-PSDrive -Name C -ErrorAction Stop
    $requiredP=[long]$priorStorage.HistoricalFootprint.RootTotalBytes*2;$requiredC=[long]$nugetPre.TotalBytes*2
    $pagefileUsage=@(Get-CimInstance -ClassName Win32_PageFileUsage -ErrorAction SilentlyContinue|Select-Object Name,AllocatedBaseSize,CurrentUsage,PeakUsage)
    $pagefileSettings=@(Get-CimInstance -ClassName Win32_PageFileSetting -ErrorAction SilentlyContinue|Select-Object Name,InitialSize,MaximumSize)
    $storageAdmission=[ordered]@{SchemaVersion='warehouse-benchmark-v22-final-rerun-storage-admission/2';CapturedUtc=[DateTime]::UtcNow.ToString('o');Status=if([long]$pDrive.Free -ge $requiredP -and [long]$cDrive.Free -ge $requiredC){'PASS'}else{'FINAL_RERUN_STORAGE_NOT_READY'};Method='Measured current free space; output requirement is full measured prior Batch3 tree plus equal explicit headroom; cache requirement is measured NuGet cache bytes plus equal explicit headroom';Volumes=@([ordered]@{Volume='P:';Role='candidate/source/build/runtime/raw/review and pagefile volume where configured';FreeBytes=[long]$pDrive.Free;HistoricalTreeBytes=[long]$priorStorage.HistoricalFootprint.RootTotalBytes;ExplicitHeadroomBytes=[long]$priorStorage.HistoricalFootprint.RootTotalBytes;RequiredBytes=$requiredP;RemainingMarginBytes=([long]$pDrive.Free-$requiredP);Pass=([long]$pDrive.Free-ge$requiredP)},[ordered]@{Volume='C:';Role='system and NuGet package cache';FreeBytes=[long]$cDrive.Free;NuGetCacheRoot=$nugetPre.PackageCacheRoot;NuGetCacheBytes=[long]$nugetPre.TotalBytes;NuGetCacheFiles=[long]$nugetPre.FileCount;ExplicitHeadroomBytes=[long]$nugetPre.TotalBytes;RequiredBytes=$requiredC;RemainingMarginBytes=([long]$cDrive.Free-$requiredC);Pass=([long]$cDrive.Free-ge$requiredC)});HistoricalFootprint=$priorStorage.HistoricalFootprint;NuGetInventoryPath=$script:NuGetCachePrePath;NuGetInventorySHA256=Get-FileHashHex $script:NuGetCachePrePath;PageFileUsage=$pagefileUsage;PageFileSettings=$pagefileSettings;PageFileActiveStatus=if($pagefileUsage.Count -gt 0){'OBSERVED'}else{'NOT_VERIFIED'};BuildAStarted=$false;CanonicalWorkloadStarted=$false}
    $storageAdmissionPath=Join-Path $script:FinalReviewRoot 'V22-Final-Rerun-Storage-Admission.json'
    New-V22FinalizerJson $storageAdmissionPath $storageAdmission
    $script:StorageAdmissionStatus=$storageAdmission.Status
    if($script:StorageAdmissionStatus -cne 'PASS'){throw 'FINAL_RERUN_STORAGE_NOT_READY; stop before Build A'}

    $script:BuildStarted=$true;$script:CurrentStage='CANDIDATE_BUILD_AND_ATTESTATION';Write-RunProgress $script:CurrentStage 'IN_PROGRESS'
    $builderArgs=@('-NoProfile','-File',$script:CandidateBuilder,'-RepoRoot',$script:RepoRoot,'-CandidateRoot',$script:BatchRoot,'-ReviewRoot',$script:ReviewRoot,'-CandidateId',$script:CandidateId,'-StaticCheckPath',$script:StaticChecksPath,'-SelfTestPath',$script:SelfTestPath,'-FailureInjectionPath',$script:FailureInjectionPath,'-ReportLintPath',$script:ReportLintPath,'-NuGetPackagesRoot',$script:NuGetCacheRoot,'-NuGetCachePreInventoryPath',$script:NuGetCachePrePath)
    $builderRecord=Invoke-Tool (Join-Path $PSHOME 'pwsh.exe') $builderArgs $script:RepoRoot 'performance-candidate-builder' $script:PerformanceRunRoot 1800
    if($builderRecord.ExitCode -ne 0){throw 'Performance candidate build/freeze did not complete successfully'}
    $reviewFiles=Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Manifest.json';$script:CandidateManifestPath=$reviewFiles
    $manifest=Get-Content -LiteralPath $script:CandidateManifestPath -Raw|ConvertFrom-Json
    $script:CandidateManifestHash=Get-FileHashHex $script:CandidateManifestPath;$script:SourceSnapshotId=$manifest.Source.SnapshotId
    if($manifest.CandidateId -cne $script:CandidateId -or $manifest.Source.InventorySHA256 -cne $predicted.InventorySHA256 -or $manifest.Status -cne 'CANDIDATE_BUILT_AND_ATTESTED' -or $manifest.Build.ReproducibilityStatus -cne 'PASS'){throw 'Built candidate identity or reproducibility failed'}
    $script:CandidateCreated=$true;$script:RuntimeRoot=[IO.Path]::GetFullPath([string]$manifest.Runtime.Root);$script:RuntimeDll=Join-Path $script:RuntimeRoot 'TKS_Thuc_Tap_V11_Benchmarks_V22.dll'
    $script:InitialGuardPath=Join-Path $script:PerformanceRunRoot 'candidate-guard-initial.json'
    $guardInventory=Join-Path $script:PerformanceRunRoot 'candidate-inventory-initial.json'
    $guardLogDir=Join-Path $script:PerformanceRunRoot 'guard-logs';New-Item -ItemType Directory -Path $guardLogDir|Out-Null
    $guardArgs=@('-NoProfile','-File',$script:CandidateGuard,'-RepoRoot',$script:RepoRoot,'-CandidateRoot',$script:BatchRoot,'-CandidateManifestPath',$script:CandidateManifestPath,'-SourceInventoryPath',(Join-Path $script:ReviewRoot 'V22-Performance-Source-Inventory.json'),'-RuntimeInventoryPath',(Join-Path $script:ReviewRoot 'V22-Performance-Runtime-Inventory.json'),'-BuildAttestationPath',(Join-Path $script:ReviewRoot 'V22-Performance-Build-Attestation.json'),'-InventoryCheckPath',$guardInventory,'-OutputPath',$script:InitialGuardPath)
    $guardRecord=Invoke-Tool (Join-Path $PSHOME 'pwsh.exe') $guardArgs $script:RepoRoot 'candidate-guard-initial' $guardLogDir 600
    if($guardRecord.ExitCode -ne 0){throw 'Initial performance candidate integrity guard failed'}
    $guardInitial=Get-Content -LiteralPath $script:InitialGuardPath -Raw|ConvertFrom-Json;$script:CandidateGuardInitialStatus=$guardInitial.Status
    if($guardInitial.Status -cne 'PASS'){throw 'Initial performance candidate guard did not PASS'}

    $script:CurrentStage='COMPARATOR_AND_TOOLCHAIN';Write-RunProgress $script:CurrentStage 'IN_PROGRESS'
    try{$script:Comparator=Get-2_1Comparator;$script:ComparatorStatus=$script:Comparator.Status}catch{$script:Comparator=[pscustomobject]@{Status='INVALID';Reason=Get-SafeText $_.Exception.Message};$script:ComparatorStatus='INVALID'}
    $v21Dir='P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_1_ADAPTIVE-20260916-FINAL';$v21Env=Get-Content -LiteralPath (Join-Path $v21Dir 'v2.1-environment.json') -Raw|ConvertFrom-Json
    $v21MixEnv=Get-Content -LiteralPath 'P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_1_MIXED-20260916-040719\mixed-environment.json' -Raw|ConvertFrom-Json
    $tool=Get-Content -LiteralPath (Join-Path $script:ReviewRoot 'V22-Toolchain.json') -Raw|ConvertFrom-Json;$buildAttestation=Get-Content -LiteralPath (Join-Path $script:ReviewRoot 'V22-Performance-Build-Attestation.json') -Raw|ConvertFrom-Json
    $toolCompare=[ordered]@{SchemaVersion='warehouse-benchmark-v22-toolchain-comparison/1';Status='CAPTURED_WITH_EXPLICIT_UNKNOWN_FIELDS';Historical21=[ordered]@{OS=$v21Env.Os.Caption;OSVersion=$v21Env.Os.Version;DotnetSDK=$v21Env.DotnetVersion;BenchmarkTFM=$v21Env.TargetFramework;Configuration=$v21Env.Configuration;BenchmarkDotNet=@($v21Env.Packages|Where-Object Id -eq 'BenchmarkDotNet'|Select-Object -ExpandProperty Version);NBomber=@($v21Env.Packages|Where-Object Id -eq 'NBomber'|Select-Object -ExpandProperty Version);CPU=$v21MixEnv.Hardware.Processor;LogicalProcessors=$v21MixEnv.Hardware.LogicalProcessors;MemoryMB=$v21MixEnv.Hardware.TotalVisibleMemoryMb;SQLServerVersion='NOT_VERIFIED_IN_HISTORICAL_ENVIRONMENT';Database='TKS_Thuc_Tap_V11_Perf_10000000';DatabaseId=5};Current22=[ordered]@{OS=$tool.OSDescription;OSArchitecture=$tool.OSArchitecture;DotnetSDK=$tool.DotnetSdk;DotnetInfoPath=(Join-Path $script:ReviewRoot 'V22-Toolchain.json');BenchmarkTFM='net8.0';DataAccessTFM='net6.0';BenchmarkDotNet='0.15.8';NBomber='6.6.0';CPU=if($null -ne $v21MixEnv.Hardware.Processor){(Get-CimInstance Win32_Processor|Select-Object -First 1 -ExpandProperty Name)}else{'NOT_VERIFIED'};LogicalProcessors=[Environment]::ProcessorCount;MemoryMB=(Get-HostSnapshot).TotalRamMB;SqlClientPackage=(@($buildAttestation.PackageAssetsA|ForEach-Object{$_.ResolvedPackages}|ForEach-Object{$_}|Where-Object{$_ -match '^Microsoft.Data.SqlClient/'})|Select-Object -Unique);SQLServerVersion='PENDING_PROBE';Database=$script:TargetDatabase;DatabaseId='PENDING_PROBE'};Interpretation='SOURCE + TOOLCHAIN/ENVIRONMENT COMPARISON; no causal attribution; no SLA threshold defined.'}

    if($null -ne $script:Comparator){Write-NewJson (Join-Path $script:ReviewRoot 'V21-Comparator-Identity.json') $script:Comparator}else{Write-NewJson (Join-Path $script:ReviewRoot 'V21-Comparator-Identity.json') ([ordered]@{Status='INVALID';Reason='Comparator identity could not be verified'})}

    $script:CurrentStage='EXACT_CANDIDATE_CORRECTNESS_PREFLIGHT';Write-RunProgress $script:CurrentStage 'IN_PROGRESS'
    $script:Preflight=Invoke-Correctness 'correctness-preflight'
    $script:PreflightStatus=$script:Preflight.Status
    $script:ProbeStatus='NOT_RUN'
    if($script:Preflight.Status -cne 'PASS'){throw 'PERFORMANCE_CANDIDATE_CORRECTNESS_FAILED; no load is permitted'}
    $script:ProbeStatus='IN_PROGRESS';$probeDir=Join-Path $script:PerformanceRunRoot 'telemetry-preflight';New-Item -ItemType Directory -Path $probeDir|Out-Null
    $probeCsv=Join-Path $probeDir 'telemetry-probe.csv';$probeArgs=@($script:RuntimeDll,'performance-telemetry-probe','--run-id',$script:RunId,'--block-id','telemetry-probe','--target-database',$script:TargetDatabase,'--output',$probeCsv)
    $probeRecord=Invoke-Tool 'dotnet' $probeArgs $script:RuntimeRoot 'telemetry-probe' $probeDir 60 -DatabaseProcess
    $probeAssessment=Get-TelemetryAssessment $probeCsv 'telemetry-probe' ([DateTimeOffset]$probeRecord.StartedUtc) ([DateTimeOffset]$probeRecord.FinishedUtc)
    $residueProbe=Get-Residue 'residue-initial' $probeDir
    $script:ProbeEvidence=[pscustomobject]@{Telemetry=$probeAssessment;Residue=$residueProbe.Evidence;ResiduePass=$residueProbe.Passed;TelemetryProcessExit=$probeRecord.ExitCode;SqlServerVersion=if($null -ne $residueProbe.Evidence){$residueProbe.Evidence.SqlServerProductVersion}else{$null};DatabaseId=if($null -ne $residueProbe.Evidence){$residueProbe.Evidence.DatabaseId}else{$null}}
    $script:ProbeStatus=if($probeRecord.ExitCode -eq 0 -and $probeAssessment.Status -ceq 'TELEMETRY_VALID' -and $residueProbe.Passed){'PASS'}else{'FAIL'}
    if($script:ProbeStatus -ne 'PASS'){throw 'Telemetry/residue preflight failed; performance load is blocked'}
    $toolCompare.Current22.SQLServerVersion=$script:ProbeEvidence.SqlServerVersion;$toolCompare.Current22.DatabaseId=$script:ProbeEvidence.DatabaseId
    $toolCompare.Status='PASS';Write-NewJson (Join-Path $script:ReviewRoot 'V21-vs-V22-Toolchain-Comparison.json') $toolCompare

    $identityConfigs=New-V22RepresentativeRunConfigurations $script:CanonicalRunId $script:CandidateId $script:Scenarios
    $identityCheck=Assert-V22CanonicalRunIdentity $script:CanonicalRunId $script:ProtocolInfo.RunId $script:CandidateId $script:SourceSnapshotId $script:SessionId $identityConfigs
    if($identityCheck.Status -cne 'PASS' -or @($identityConfigs|Where-Object RunId -cne $script:CanonicalRunId).Count -gt 0){$script:CanonicalRunIdentityStatus='FAIL';throw 'CANONICAL_RUN_IDENTITY_INVALID|pre-workload configuration binding failed'}
    $script:CanonicalRunIdentityStatus='PASS'
    $identityEvidence=[ordered]@{SchemaVersion='warehouse-benchmark-v22-canonical-run-identity/1';CanonicalRunId=$script:CanonicalRunId;ProtocolRunId=$script:ProtocolInfo.RunId;CandidateId=$script:CandidateId;SourceSnapshotId=$script:SourceSnapshotId;SessionId=$script:SessionId;AuthorizationId=$null;CreatedUtc=[DateTime]::UtcNow.ToString('o');ValidationStatus='PASS';ConfigurationCount=$identityConfigs.Count;Profiles=@('BDN','C1','C2','C4','L1','L2','L4','L8');Configurations=$identityConfigs}
    New-V22FinalizerJson (Join-Path $script:ReviewRoot 'V22-Canonical-Run-Identity.json') $identityEvidence
    $script:CurrentStage='CANONICAL_PREWORKLOAD_GATES';Write-RunProgress $script:CurrentStage 'IN_PROGRESS'
    $script:RecoveryDiagnosticStatus='NOT_RUN_NOT_IN_PROTOCOL'
    $script:RecoveryDiagnostic=$null
    $canonicalRunId=$script:CanonicalRunId
    Set-ActiveRunContext $canonicalRunId 'canonical'
    $canonicalGuardPath=Join-Path $script:PerformanceRunRoot 'candidate-guard-pre-canonical.json';$canonicalGuardInventory=Join-Path $script:PerformanceRunRoot 'candidate-inventory-pre-canonical.json';$canonicalGuardLogs=Join-Path $script:PerformanceRunRoot 'guard-logs';New-Item -ItemType Directory -Path $canonicalGuardLogs|Out-Null
    $canonicalGuardArgs=@('-NoProfile','-File',$script:CandidateGuard,'-RepoRoot',$script:RepoRoot,'-CandidateRoot',$script:BatchRoot,'-CandidateManifestPath',$script:CandidateManifestPath,'-SourceInventoryPath',(Join-Path $script:ReviewRoot 'V22-Performance-Source-Inventory.json'),'-RuntimeInventoryPath',(Join-Path $script:ReviewRoot 'V22-Performance-Runtime-Inventory.json'),'-BuildAttestationPath',(Join-Path $script:ReviewRoot 'V22-Performance-Build-Attestation.json'),'-InventoryCheckPath',$canonicalGuardInventory,'-OutputPath',$canonicalGuardPath,'-ExpectedManifestSHA256',$script:CandidateManifestHash)
    $canonicalGuardProcess=Invoke-Tool (Join-Path $PSHOME 'pwsh.exe') $canonicalGuardArgs $script:RepoRoot 'canonical-candidate-guard' $canonicalGuardLogs 600
    $canonicalGuard=Get-Content -LiteralPath $canonicalGuardPath -Raw|ConvertFrom-Json
    $script:CandidateGuardCanonicalStatus=$canonicalGuard.Status
    if($canonicalGuardProcess.ExitCode -ne 0 -or $canonicalGuardProcess.CleanupStatus -ne 'PASS' -or $canonicalGuard.Status -cne 'PASS'){throw 'Canonical pre-run candidate guard failed'}
    $script:CurrentStage='CANONICAL_CORRECTNESS_PREFLIGHT';Write-RunProgress $script:CurrentStage 'IN_PROGRESS'
    $script:Preflight=Invoke-Correctness 'correctness-canonical-preflight';$script:PreflightStatus=$script:Preflight.Status
    if($script:Preflight.Status -cne 'PASS' -or $script:Preflight.TotalCount -cne '6/6' -or $script:Preflight.SemanticScenarios -cne '6/6'){throw 'Canonical exact-candidate correctness preflight failed'}
    $canonicalAdmission=Get-Admission ($canonicalRunId+'-PRE-RUN') 'LEGACY_ISOLATED' 0.0 $script:HostLogPath
    Write-NewJson (Join-Path $script:PerformanceRunRoot 'canonical-pre-run-admission.json') $canonicalAdmission
    if(-not $canonicalAdmission.Allowed){$script:StopReason=$canonicalAdmission.Reason;throw 'Canonical host admission failed; performance workload is blocked'}
    $script:CurrentStage='ISOLATED_PERFORMANCE';Write-RunProgress $script:CurrentStage 'IN_PROGRESS'
    $memoryEvidence=Get-Content -LiteralPath (Join-Path $v21Dir 'v2.1-memory-class-evidence.json') -Raw|ConvertFrom-Json
    $classDrop=@{BDN=[double]$memoryEvidence.BDN.PeakDropMb;C1=[double]$memoryEvidence.C1.PeakDropMb;C2=[double]$memoryEvidence.C2.PeakDropMb;C4=[double]$memoryEvidence.C4.PeakDropMb}
    $isolatedManifest=[ordered]@{SchemaVersion='warehouse-benchmark-v22-isolated-run-manifest/1';RunId=$script:RunId;CandidateId=$script:CandidateId;CandidateManifestSHA256=$script:CandidateManifestHash;SourceSnapshotId=$script:SourceSnapshotId;Profile='LEGACY_ISOLATED';Scenarios=$script:Scenarios;Schedule='Per scenario BDN, C1, C2, C4';WarmupSeconds=3;TimedSeconds=15;WorkersMeaning='KeepConstant copies; one copy is one worker';ApplicationTimeoutSeconds=30;BlockTimeoutSeconds=75;RetryPolicy='No individual timed request retry';MemoryClassEvidencePath=(Join-Path $v21Dir 'v2.1-memory-class-evidence.json');InitialClassDropsMB=$classDrop;StartedUtc=[DateTime]::UtcNow.ToString('o');BlockIds=@()}
    $script:isolatedBeginSnapshot=$script:Preflight.DBEvidence.Post.SnapshotSha256
    $previousDrops=@{BDN=$classDrop.BDN;C1=$classDrop.C1;C2=$classDrop.C2;C4=$classDrop.C4};$stopIsolated=$false
    foreach($scenario in $script:Scenarios){foreach($kind in @('BDN','C1','C2','C4')){
        $blockId=$script:RunId+'-ISO-'+$scenario+'-'+$kind;$copies=if($kind -eq 'C1'){1}elseif($kind -eq 'C2'){2}elseif($kind -eq 'C4'){4}else{0}
        $previous=if($kind -eq 'BDN'){$previousDrops.BDN}elseif($kind -eq 'C1'){$previousDrops.C1}elseif($kind -eq 'C2'){$previousDrops.C2}else{$previousDrops.C4}
        if($stopIsolated){$row=[pscustomobject]@{RunId=$script:RunId;BlockId=$blockId;Profile='LEGACY_ISOLATED';BlockKind=$kind;Scenario=$scenario;Copies=$copies;Status='SKIPPED';FailureType='PRIOR_HOST_OR_COOLDOWN_GATE';Reason='Prior admission/cooldown stopped isolated sequence';Requests=0;Failed=0;TelemetryStatus='SKIPPED';CleanupStatus='NO_CHILD_STARTED';RawArtifactRoot=$null}}
        else{
            if(-not $script:CanonicalWorkloadStarted){
                New-V22FinalizerJson $script:OneShotClaimPath ([ordered]@{SchemaVersion='warehouse-benchmark-v22-one-shot-workload-claim/1';Status='CLAIMED';CanonicalRunId=$script:CanonicalRunId;CandidateId=$script:CandidateId;SourceSnapshotId=$script:SourceSnapshotId;ClaimedUtc=[DateTime]::UtcNow.ToString('o');FullCanonicalAttemptLimit=1})
                New-V22FinalizerJson $script:OneShotStartPath ([ordered]@{SchemaVersion='warehouse-benchmark-v22-canonical-workload-start/1';Status='STARTED';CanonicalRunId=$script:CanonicalRunId;ProtocolRunId=$script:ProtocolInfo.RunId;CandidateId=$script:CandidateId;SourceSnapshotId=$script:SourceSnapshotId;FirstBlockId=$blockId;FirstBlockKind=$kind;StartedUtc=[DateTime]::UtcNow.ToString('o');AttemptNumber=1})
                $script:CanonicalWorkloadStarted=$true
            }
            $row=Invoke-MeasurementBlock $scenario $copies 'LEGACY_ISOLATED' $blockId $kind $previous (Join-Path $script:PerformanceRunRoot 'isolated') $script:HostLogPath 75
        }
        $script:IsolatedRows.Add($row);$isolatedManifest.BlockIds+=@($blockId)
        if($row.Status -in @('PASS','FAIL','INVALID') -and $kind -ne 'BDN' -and $row.MinFreeRamMB -ne $null){$drop=[Math]::Max(0.0,[double]$row.Admission.AdmissionSamples[0].FreeRamMB-[double]$row.MinFreeRamMB);$previousDrops[$kind]=[Math]::Max([double]$previousDrops[$kind],$drop)}
        if($row.Status -eq 'HOST_LIMIT' -or $row.FailureType -in @('HOST_LIMIT','EMERGENCY_HOST_LIMIT') -or $script:StopReason -or $row.CleanupStatus -ne 'PASS'){$stopIsolated=$true}
        if($row.Status -ne 'HOST_LIMIT' -and $row.CleanupStatus -eq 'PASS'){$cool=Wait-Cooldown $blockId (Join-Path $script:PerformanceRunRoot 'cooldown') $script:HostLogPath;$row|Add-Member -NotePropertyName Cooldown -NotePropertyValue $cool; if($cool.Status -ne 'PASS'){$stopIsolated=$true;$script:StopReason='COOLDOWN_FAILED'}}
    }}
    $isolatedManifest.FinishedUtc=[DateTime]::UtcNow.ToString('o');$isolatedManifest.Status=if($stopIsolated){'PARTIAL'}else{'COMPLETED'}
    $script:BDNRows=@($script:IsolatedRows|Where-Object BlockKind -eq 'BDN')
    $coreRows=@($script:IsolatedRows|Where-Object BlockKind -in @('C1','C2'))
    $c4Rows=@($script:IsolatedRows|Where-Object BlockKind -eq 'C4')
    $corePass=($coreRows.Count -eq 12 -and @($coreRows|Where-Object Status -ne 'PASS').Count -eq 0 -and @($coreRows|Where-Object Failed -ne 0).Count -eq 0 -and @($coreRows|Where-Object CleanupStatus -ne 'PASS').Count -eq 0)
    $c4Pass=($c4Rows.Count -eq 6 -and @($c4Rows|Where-Object Status -ne 'PASS').Count -eq 0)
    $bdnPass=($script:BDNRows.Count -eq 6 -and @($script:BDNRows|Where-Object Status -ne 'PASS').Count -eq 0)
    $script:CurrentStage='ISOLATED_POST_CORRECTNESS';Write-RunProgress $script:CurrentStage 'IN_PROGRESS'
    $script:AfterIsolated=Invoke-Correctness 'correctness-after-isolated'
    $isolatedIntegrity=($null -ne $script:AfterIsolated -and $script:AfterIsolated.Status -ceq 'PASS' -and $script:isolatedBeginSnapshot -ceq $script:AfterIsolated.DBEvidence.Pre.SnapshotSha256 -and $script:AfterIsolated.DBEvidence.PrePostSha256Equal -eq $true)
    $isolatedTelemetry=(@($coreRows|Where-Object TelemetryStatus -ne 'TELEMETRY_VALID').Count -eq 0)
    $script:IsolatedGate=if($corePass -and $isolatedIntegrity -and $isolatedTelemetry -and $script:CandidateGuardInitialStatus -ceq 'PASS'){'PASS'}else{'FAIL'}
    $script:PrePostStatus=if($isolatedIntegrity){'ISOLATED_PRE_POST_PASS'}else{'ISOLATED_PRE_POST_FAIL'}

    $script:CurrentStage='MIXED_PERFORMANCE';Write-RunProgress $script:CurrentStage $(if($script:IsolatedGate -eq 'PASS'){'IN_PROGRESS'}else{'SKIPPED'})
    if($script:IsolatedGate -eq 'PASS'){
        $mixedManifest=[ordered]@{SchemaVersion='warehouse-benchmark-v22-mixed-run-manifest/1';RunId=$script:RunId;CandidateId=$script:CandidateId;CandidateManifestSHA256=$script:CandidateManifestHash;Profile='MIXED_REGRESSION';Scenarios=$script:Scenarios;WarmupSeconds=3;TimedSeconds=15;ParentTimeoutSeconds=90;ScenarioProcessCount=6;ChildProcessesPerLevel=6;CopiesMeaning='CopiesPerScenario is the NBomber worker count in each of six scenario child processes';RequiredCommonOverlapSeconds=10;Levels=@();StartedUtc=[DateTime]::UtcNow.ToString('o')}
        $previousMixedDrop=0.0;$stopMixed=$false
        foreach($workers in @(1,2,4,8)){
            $level='L'+$workers;$levelId=$script:RunId+'-MIXED-'+$level;$levelRoot=Join-Path (Join-Path $script:PerformanceRunRoot 'mixed') $levelId;New-Item -ItemType Directory -Path $levelRoot|Out-Null
            if($stopMixed){$levelResult=New-V22MixedResultRecord ([ordered]@{RunId=$script:CanonicalRunId;CandidateId=$script:CandidateId;Level=$level;LevelId=$levelId;Status='SKIPPED';FailureType='PRIOR_HOST_OR_COOLDOWN_GATE';FailureReason='A prior Mixed level gate stopped this sequence';WorkerRows=@();Failed=0;Requests=0;TelemetryStatus='SKIPPED';CleanupStatus='NO_CHILD_STARTED';FinishedUtc=[DateTime]::UtcNow.ToString('o');Interpretation=if($workers -eq 8){'STANDALONE_MIXED_LOAD_LEVEL'}else{'MIXED_LEVEL'}}) $script:CanonicalRunId $script:CandidateId $level;$script:MixedLevels.Add($levelResult);Write-NewJson (Join-Path $levelRoot 'level-result.json') $levelResult;$mixedManifest.Levels+=@([pscustomobject]@{Level=$level;Status=$levelResult.Status;FailureType=$levelResult.FailureType;FailureReason=$levelResult.FailureReason;ScenarioProcessCount=$levelResult.ScenarioProcessCount;CopiesPerScenario=$levelResult.CopiesPerScenario;TotalLogicalCopies=$levelResult.TotalLogicalCopies;ObservedScenarioProcessCount=$levelResult.ObservedScenarioProcessCount;ObservedCopiesPerScenario=$levelResult.ObservedCopiesPerScenario;ObservedTotalLogicalCopies=$levelResult.ObservedTotalLogicalCopies;OverlapStatus=$levelResult.OverlapStatus});continue}
            $admission=Get-Admission $levelId 'MIXED_REGRESSION' $previousMixedDrop $script:HostLogPath
            if(-not$admission.Allowed){$levelResult=New-V22MixedResultRecord ([ordered]@{RunId=$script:CanonicalRunId;CandidateId=$script:CandidateId;Level=$level;LevelId=$levelId;Status='HOST_LIMIT';FailureType=$admission.Reason;FailureReason='Host admission did not allow this Mixed level';Admission=$admission;WorkerRows=@();Failed=0;Requests=0;TelemetryStatus='SKIPPED';CleanupStatus='NO_CHILD_STARTED';FinishedUtc=[DateTime]::UtcNow.ToString('o');Interpretation=if($workers -eq 8){'STANDALONE_MIXED_LOAD_LEVEL'}else{'MIXED_LEVEL'}}) $script:CanonicalRunId $script:CandidateId $level;$script:MixedLevels.Add($levelResult);Write-NewJson (Join-Path $levelRoot 'level-result.json') $levelResult;$mixedManifest.Levels+=@([pscustomobject]@{Level=$level;Status=$levelResult.Status;FailureType=$levelResult.FailureType;FailureReason=$levelResult.FailureReason;ScenarioProcessCount=$levelResult.ScenarioProcessCount;CopiesPerScenario=$levelResult.CopiesPerScenario;TotalLogicalCopies=$levelResult.TotalLogicalCopies;ObservedScenarioProcessCount=$levelResult.ObservedScenarioProcessCount;ObservedCopiesPerScenario=$levelResult.ObservedCopiesPerScenario;ObservedTotalLogicalCopies=$levelResult.ObservedTotalLogicalCopies;OverlapStatus=$levelResult.OverlapStatus});$stopMixed=$true;continue}
            $children=[Collections.Generic.List[object]]::new();$workerMetadata=[Collections.Generic.List[object]]::new();$levelStarted=[DateTimeOffset]::UtcNow;$workerRows=[Collections.Generic.List[object]]::new();$telemetryRecord=$null
            $mixedContract=New-V22MixedSemantics $level;$workerConfigs=[Collections.Generic.List[object]]::new()
            foreach($configScenario in $script:Scenarios){$configBlock=$levelId+'-'+$configScenario;$workerConfigs.Add((New-V22MixedWorkerConfiguration $script:CanonicalRunId $script:CandidateId $script:SourceSnapshotId $level $configBlock $configScenario 3 15))}
            $mixedIdentity=Assert-V22CanonicalRunIdentity $script:CanonicalRunId $script:ProtocolInfo.RunId $script:CandidateId $script:SourceSnapshotId $script:SessionId $workerConfigs.ToArray()
            if($workerConfigs.Count -ne 6 -or $mixedIdentity.Status -cne 'PASS' -or @($workerConfigs|Where-Object{$_.ScenarioProcessCount -ne 6 -or $_.CopiesPerScenario -ne $workers -or $_.TotalLogicalCopies -ne (6*$workers)}).Count -gt 0){throw 'CANONICAL_RUN_IDENTITY_INVALID|mixed level configuration gate failed'}
            Write-NewJson (Join-Path $levelRoot 'level-config.json') ([ordered]@{RunId=$script:CanonicalRunId;CandidateId=$script:CandidateId;Level=$level;ScenarioProcessCount=6;CopiesPerScenario=$workers;TotalLogicalCopies=6*$workers;WorkerConfigs=$workerConfigs.ToArray()})
            foreach($workerConfig in $workerConfigs){Write-NewJson (Join-Path $levelRoot ('worker-config-'+$workerConfig.Scenario+'.json')) $workerConfig}
            try{
                foreach($scenario in $script:Scenarios){
                    $workerBlock=$levelId+'-'+$scenario;$workerConfig=$workerConfigs|Where-Object Scenario -CEQ $scenario|Select-Object -First 1
                    if($null -eq $workerConfig -or $workerConfig.RunId -cne $script:CanonicalRunId -or $workerConfig.CandidateId -cne $script:CandidateId){throw 'CANONICAL_RUN_IDENTITY_INVALID|mixed worker config changed before spawn'}
                    $report=Join-Path $levelRoot ($scenario+'\nbomber-report');$metadata=Join-Path $levelRoot ($scenario+'\worker-metadata.json')
                    $args=@($script:RuntimeDll,'performance-load','--scenario',$scenario,'--copies',[string]$workers,'--warmup-seconds','3','--duration-seconds','15','--profile','MIXED_REGRESSION','--run-id',$script:RunId,'--block-id',$workerBlock,'--target-database',$script:TargetDatabase,'--report-directory',$report,'--metadata-path',$metadata)
                    $logDir=Join-Path $levelRoot $scenario;New-Item -ItemType Directory -Path $logDir|Out-Null
                    $children.Add((Start-OwnedProcess 'dotnet' $args $script:RuntimeRoot ($workerBlock+'-load') 'CHILD' $logDir));$workerMetadata.Add([pscustomobject]@{Scenario=$scenario;BlockId=$workerBlock;Copies=$workers;ChildProcessId=$children[$children.Count-1].ChildProcessId;ReportDirectory=$report;MetadataPath=$metadata;LaunchUtc=$children[$children.Count-1].StartedUtc.ToString('o')})
                }
                $targetPids=(@($children|ForEach-Object{$_.ChildProcessId}) -join ',');$telemetryCsv=Join-Path $levelRoot 'sql-telemetry.csv';$telemetryArgs=@($script:RuntimeDll,'performance-telemetry','--run-id',$script:RunId,'--block-id',$levelId,'--target-database',$script:TargetDatabase,'--target-process-ids',$targetPids,'--output',$telemetryCsv)
                $telemetryRecord=Start-OwnedProcess 'dotnet' $telemetryArgs $script:RuntimeRoot ($levelId+'-telemetry') 'TELEMETRY' $levelRoot
                $wait=Wait-OwnedGroup $children.ToArray() 90 $script:HostLogPath $true $levelId
                if(-not$telemetryRecord.Process.HasExited){$telemetryDeadline=[DateTime]::UtcNow.AddSeconds(20);while(-not$telemetryRecord.Process.HasExited -and [DateTime]::UtcNow -lt $telemetryDeadline){Start-Sleep -Milliseconds 250}}
                if(-not$telemetryRecord.Process.HasExited){[void](Stop-OwnedProcess $telemetryRecord 'MIXED_TELEMETRY_TIMEOUT')}
                [void](Complete-OwnedProcess $telemetryRecord 'MIXED_LEVEL_TELEMETRY_FINISHED')
                foreach($meta in $workerMetadata){$child=$children|Where-Object ChildProcessId -eq $meta.ChildProcessId|Select-Object -First 1;$terminal=$null;try{$terminal=Get-Content -LiteralPath $meta.MetadataPath -Raw|ConvertFrom-Json}catch{};$workerRow=[ordered]@{RunId=$script:CanonicalRunId;CandidateId=$script:CandidateId;Level=$level;MixedLevel=$level;Scenario=$meta.Scenario;BlockId=$meta.BlockId;ScenarioProcessCount=6;CopiesPerScenario=$workers;TotalLogicalCopies=6*$workers;Copies=$workers;Status='INVALID';FailureType=$null;Requests=$null;Success=$null;Failed=$null;RPS=$null;MeanMs=$null;P50Ms=$null;P95Ms=$null;P99Ms=$null;MaxMs=$null;MeasuredStartUtc=$null;MeasuredStopUtc=$null;LogWindowSeconds=$null;ObservedCopies=if($terminal){$terminal.ObservedCopies}else{$null};ObservedInstanceNumbers=if($terminal){@($terminal.ObservedInstanceNumbers)}else{@()};WorkerTerminalStatus=if($terminal){$terminal.Status}else{'MISSING'};WorkerTimedWindowCompleted=if($terminal){$terminal.TimedWindowCompleted}else{$false};ProcessExitCode=$child.ExitCode;RawArtifacts=@()}
                    try{$nb=Get-NBomberArtifact $meta.ReportDirectory $meta.Scenario $workers $child.StdoutPath $meta.MetadataPath $script:RunId $meta.BlockId 'MIXED_REGRESSION';$workerRow.Status=if($child.ExitCode -eq 0 -and $nb.Metrics.Failed -eq 0){'PASS'}else{'FAIL'};$workerRow.FailureType=if($workerRow.Status -eq 'PASS'){$null}else{'PRODUCT_ERROR'};$workerRow.Requests=$nb.Metrics.Requests;$workerRow.Success=$nb.Success;$workerRow.Failed=$nb.Metrics.Failed;$workerRow.RPS=$nb.Metrics.RPS;$workerRow.MeanMs=$nb.Metrics.MeanMs;$workerRow.P50Ms=$nb.Metrics.P50Ms;$workerRow.P95Ms=$nb.Metrics.P95Ms;$workerRow.P99Ms=$nb.Metrics.P99Ms;$workerRow.MaxMs=$nb.Metrics.MaxMs;$workerRow.MeasuredStartUtc=$nb.MeasuredStartUtc;$workerRow.MeasuredStopUtc=$nb.MeasuredStopUtc;$workerRow.LogWindowSeconds=$nb.LogWindowSeconds;$workerRow.ObservedCopies=$nb.ObservedCopies;$workerRow.ObservedInstanceNumbers=$nb.ObservedInstanceNumbers;$workerRow.NBomberEvidence=$nb}catch{$workerRow.Status='INVALID';$workerRow.FailureType='HARNESS_INVALID';$workerRow.Reason=Get-SafeText $_.Exception.Message}
                    $workerRoot=Join-Path $levelRoot $meta.Scenario;$workerRow.RawArtifacts=@(Get-ChildItem -LiteralPath $workerRoot -File -Recurse|ForEach-Object{[pscustomobject]@{Path=$_.FullName;Size=$_.Length;SHA256=(Get-FileHashHex $_.FullName)}});$workerRows.Add([pscustomobject]$workerRow);$combined=New-V22MixedWorkerProjection $workerRow $level;$script:MixedRows.Add($combined)
                }
                $blockEnd=[DateTimeOffset]::UtcNow;$telemetry=Get-TelemetryAssessment $telemetryCsv $levelId $levelStarted $blockEnd
                $workerAggregate=Get-V22MixedWorkerAggregate $script:CanonicalRunId $script:CandidateId $level $script:Scenarios $workerRows.ToArray() $children.Count $script:ProtocolInfo.MinimumMixedCommonOverlapSeconds
                $overlap=$workerAggregate.WindowOverlapSeconds;$overlapValid=$workerAggregate.OverlapValid;$allWorkersValid=$workerAggregate.WorkerCountsValid
                $cleanupProof=Get-OwnedProcessCleanupProof (@($children)+@($telemetryRecord|Where-Object{$null -ne $_}))
                $terminalFailures=@($children|Where-Object TerminalMetadataStatus -ne 'WRITTEN').Count
                $cleanupPass=($cleanupProof.Passed -and $terminalFailures -eq 0 -and @($children|Where-Object{-not $_.Completed -or -not $_.ProcessGone}).Count -eq 0 -and $null -ne $telemetryRecord -and $telemetryRecord.Completed -and $telemetryRecord.ProcessGone)
                $levelStatus=if($wait.MemoryLimitReason){if($wait.EmergencyStop){'EMERGENCY_HOST_LIMIT'}else{'HOST_LIMIT'}}elseif($wait.MonitorFailure){'INVALID'}elseif($wait.TimedOut){'FAIL'}elseif(-not$allWorkersValid -or -not$overlapValid){'INVALID'}elseif($telemetry.Status -ne 'TELEMETRY_VALID' -or -not$cleanupPass){'INVALID'}else{'PASS'}
                $requests=[long](($workerRows|Measure-Object Requests -Sum).Sum);$failed=[long](($workerRows|Measure-Object Failed -Sum).Sum);$sumRps=[double](($workerRows|Measure-Object RPS -Sum).Sum)
                $minRam=$wait.MinRamMB;$drop=if($null -ne $minRam){[Math]::Max(0.0,[double]$admission.AdmissionSamples[-1].FreeRamMB-[double]$minRam)}else{0.0}
$failureType=if($levelStatus -eq 'PASS'){$null}elseif($wait.MemoryLimitReason){if($wait.EmergencyStop){'EMERGENCY_HOST_LIMIT'}else{'HOST_LIMIT'}}elseif($wait.MonitorFailure){'HOST_TELEMETRY_INVALID'}elseif(-not$overlapValid){'MIXED_LEVEL_INVALID'}elseif($telemetry.Status -ne 'TELEMETRY_VALID'){'TELEMETRY_INVALID'}else{'MIXED_WORKER_INVALID'};$failureReason=if($levelStatus -eq 'PASS'){$null}elseif($wait.MemoryLimitReason){'Host resource guard stopped the level'}elseif($wait.MonitorFailure){'Host telemetry evidence was invalid'}elseif($wait.TimedOut){'Mixed scenario process timed out'}elseif(-not$allWorkersValid){'Observed worker count or identity did not meet protocol'}elseif(-not$overlapValid){'Common measured overlap did not meet protocol'}elseif($telemetry.Status -ne 'TELEMETRY_VALID'){'Required Mixed telemetry was invalid'}elseif(-not$cleanupPass){'Per-level cleanup proof failed'}else{'Mixed level failed validation'};$levelResult=New-V22MixedResultRecord ([ordered]@{RunId=$script:CanonicalRunId;CandidateId=$script:CandidateId;Level=$level;LevelId=$levelId;Status=$levelStatus;FailureType=$failureType;FailureReason=$failureReason;Admission=$admission;WorkerRows=$workerRows.ToArray();ObservedScenarioProcessCount=$workerAggregate.ObservedScenarioProcessCount;ObservedCopiesPerScenario=$workerAggregate.ObservedCopiesPerScenario;ObservedTotalLogicalCopies=$workerAggregate.ObservedTotalLogicalCopies;WorkerCountsValid=$workerAggregate.WorkerCountsValid;WorkerValidationErrors=$workerAggregate.WorkerValidationErrors;Requests=$requests;Failed=$failed;AggregateRPS=[Math]::Round($sumRps,3);AggregateRPSLabel='DESCRIPTIVE_SUM_OF_SCENARIO_RPS';WindowOverlapSeconds=$overlap;RequiredOverlapSeconds=$script:ProtocolInfo.MinimumMixedCommonOverlapSeconds;OverlapValid=$overlapValid;OverlapApplicability=$workerAggregate.OverlapApplicability;OverlapStatus=$workerAggregate.OverlapStatus;Telemetry=$telemetry;TelemetryStatus=$telemetry.Status;CleanupStatus=if($cleanupPass){'PASS'}else{'FAIL'};CleanupProcessProof=$cleanupProof;HostStopActions=@($wait.HostStopActions);MinFreeRamMB=$minRam;ObservedDropMB=$drop;Interpretation=if($workers -eq 8){'STANDALONE_MIXED_LOAD_LEVEL'}else{'MIXED_LEVEL'};StartedUtc=$levelStarted.ToString('o');FinishedUtc=$blockEnd.ToString('o');ProcessExitCodes=@($children|ForEach-Object{[pscustomobject]@{ChildProcessId=$_.ChildProcessId;ExitCode=$_.ExitCode;TimedOut=$_.TimedOut;StopReason=$_.StopReason;TerminalMetadataStatus=$_.TerminalMetadataStatus}});RawTelemetryPath=$telemetryCsv;RawArtifacts=@(Get-ChildItem -LiteralPath $levelRoot -File -Recurse|ForEach-Object{[pscustomobject]@{Path=$_.FullName;Size=$_.Length;SHA256=(Get-FileHashHex $_.FullName)}})}) $script:CanonicalRunId $script:CandidateId $level



                if($wait.MemoryLimitReason){$script:StopReason=if($wait.EmergencyStop){'EMERGENCY_HOST_LIMIT'}else{'HOST_LIMIT'};$stopMixed=$true}
                if($wait.MonitorFailure){$script:StopReason='HOST_TELEMETRY_INVALID';$stopMixed=$true}
$cool=Wait-Cooldown $levelId (Join-Path $script:PerformanceRunRoot 'cooldown') $script:HostLogPath;$levelResult|Add-Member -NotePropertyName Cooldown -NotePropertyValue $cool
if($cool.Status -ne 'PASS'){$levelResult.Status='INVALID';$levelResult.FailureType='COOLDOWN_FAILED';$levelResult.FailureReason='Per-level cooldown or residue gate failed';$levelResult.CleanupStatus='FAIL';$script:StopReason='COOLDOWN_FAILED';$stopMixed=$true}
                $levelResult=New-V22MixedResultRecord $levelResult $script:CanonicalRunId $script:CandidateId $level
                $levelGate=Get-V22MixedResultGate $levelResult $script:ProtocolInfo.MinimumMixedCommonOverlapSeconds;$levelResult|Add-Member -NotePropertyName ResultContract -NotePropertyValue $levelGate
                if($levelResult.Status -eq 'PASS' -and -not$levelGate.IsMeasurementPass){$levelResult.Status='INVALID';$levelResult.FailureType='MIXED_RESULT_CONTRACT_INVALID';$levelResult.FailureReason=($levelGate.Errors -join '; ');$script:StopReason='MIXED_RESULT_CONTRACT_INVALID';$stopMixed=$true;$levelResult=New-V22MixedResultRecord $levelResult $script:CanonicalRunId $script:CandidateId $level}
                Write-NewJson (Join-Path $levelRoot 'level-result.json') $levelResult;$script:MixedLevels.Add($levelResult);$mixedManifest.Levels+=@([pscustomobject]@{Level=$level;Status=$levelResult.Status;FailureType=$levelResult.FailureType;FailureReason=$levelResult.FailureReason;BlockId=$levelId;ScenarioProcessCount=$levelResult.ScenarioProcessCount;CopiesPerScenario=$levelResult.CopiesPerScenario;TotalLogicalCopies=$levelResult.TotalLogicalCopies;ObservedScenarioProcessCount=$levelResult.ObservedScenarioProcessCount;ObservedCopiesPerScenario=$levelResult.ObservedCopiesPerScenario;ObservedTotalLogicalCopies=$levelResult.ObservedTotalLogicalCopies;OverlapSeconds=$levelResult.OverlapSeconds;OverlapValid=$levelResult.OverlapValid;OverlapStatus=$levelResult.OverlapStatus;TelemetryStatus=$levelResult.TelemetryStatus;ChildProcessIds=@($children|ForEach-Object ChildProcessId)})
                if($levelResult.Status -ne 'PASS'){$stopMixed=$true}
                $previousMixedDrop=[Math]::Max(0.0,$drop)
            }catch{
                foreach($record in $children){if(-not$record.Completed){[void](Stop-OwnedProcess $record 'MIXED_LEVEL_EXCEPTION');[void](Complete-OwnedProcess $record 'MIXED_LEVEL_EXCEPTION')}}
                if($null -ne $telemetryRecord -and -not$telemetryRecord.Completed){if(-not$telemetryRecord.Process.HasExited){[void](Stop-OwnedProcess $telemetryRecord 'MIXED_LEVEL_EXCEPTION')};[void](Complete-OwnedProcess $telemetryRecord 'MIXED_LEVEL_EXCEPTION')}
$levelResult=New-V22MixedResultRecord ([ordered]@{RunId=$script:CanonicalRunId;CandidateId=$script:CandidateId;Level=$level;LevelId=$levelId;Status='FAIL';FailureType='HARNESS_FAILED';FailureReason=Get-SafeText $_.Exception.Message;WorkerRows=$workerRows.ToArray();ObservedScenarioProcessCount=$children.Count;Requests=0;Failed=0;TelemetryStatus='INVALID';CleanupStatus='CHECK_REQUIRED';StartedUtc=$levelStarted.ToString('o');FinishedUtc=[DateTime]::UtcNow.ToString('o');Interpretation=if($workers -eq 8){'STANDALONE_MIXED_LOAD_LEVEL'}else{'MIXED_LEVEL'}}) $script:CanonicalRunId $script:CandidateId $level
$levelGate=Get-V22MixedResultGate $levelResult $script:ProtocolInfo.MinimumMixedCommonOverlapSeconds;$levelResult|Add-Member -NotePropertyName ResultContract -NotePropertyValue $levelGate;$script:MixedLevels.Add($levelResult);$mixedManifest.Levels+=@([pscustomobject]@{Level=$level;Status=$levelResult.Status;FailureType=$levelResult.FailureType;FailureReason=$levelResult.FailureReason;BlockId=$levelId;ScenarioProcessCount=$levelResult.ScenarioProcessCount;CopiesPerScenario=$levelResult.CopiesPerScenario;TotalLogicalCopies=$levelResult.TotalLogicalCopies;ObservedScenarioProcessCount=$levelResult.ObservedScenarioProcessCount;ObservedCopiesPerScenario=$levelResult.ObservedCopiesPerScenario;ObservedTotalLogicalCopies=$levelResult.ObservedTotalLogicalCopies;OverlapSeconds=$levelResult.OverlapSeconds;OverlapValid=$levelResult.OverlapValid;OverlapStatus=$levelResult.OverlapStatus;TelemetryStatus=$levelResult.TelemetryStatus});$stopMixed=$true
                if(-not(Test-Path -LiteralPath (Join-Path $levelRoot 'level-failure.json'))){Write-NewJson (Join-Path $levelRoot 'level-failure.json') $levelResult}
            }
        }
        $mixedManifest.FinishedUtc=[DateTime]::UtcNow.ToString('o');$mixedManifest.Status=if(@($script:MixedLevels|Where-Object Status -ne 'PASS').Count -eq 0){'COMPLETED'}else{'PARTIAL'}
        $script:MixedGate=if($script:MixedLevels.Count -eq 4 -and @($script:MixedLevels|Where-Object Status -ne 'PASS').Count -eq 0){'PASS'}else{'FAIL'}
        $script:AfterMixed=if($script:MixedLevels.Count -gt 0){Invoke-Correctness 'correctness-after-mixed'}else{$null}
    }else{
        $script:MixedGate='SKIPPED_ISOLATED_GATE_FAILED'
        $mixedManifest=[ordered]@{SchemaVersion='warehouse-benchmark-v22-mixed-run-manifest/1';RunId=$script:RunId;CandidateId=$script:CandidateId;Status='SKIPPED';Reason='Isolated C1/C2 core gate did not pass';Levels=@();StartedUtc=$null;FinishedUtc=[DateTime]::UtcNow.ToString('o')}
    }
    if($null -ne $script:AfterMixed){$finalCorrectness=$script:AfterMixed}else{$finalCorrectness=$script:AfterIsolated}
    $finalPostMatches=($null -ne $finalCorrectness -and $finalCorrectness.Status -ceq 'PASS' -and $finalCorrectness.DBEvidence.PrePostSha256Equal -eq $true)
    $isolatedToMixedMatches=($null -eq $script:AfterMixed -or ($null -ne $script:AfterIsolated -and $script:AfterIsolated.DBEvidence.Post.SnapshotSha256 -ceq $script:AfterMixed.DBEvidence.Pre.SnapshotSha256))
    $script:PrePostStatus=if($isolatedIntegrity -and $finalPostMatches -and $isolatedToMixedMatches){'PASS'}else{'FAIL'}
    if($script:MixedGate -eq 'PASS' -and -not$isolatedToMixedMatches){$script:MixedGate='FAIL_PRE_POST_MISMATCH'}
    if($script:MixedGate -eq 'PASS' -and $null -ne $script:AfterMixed -and $script:AfterMixed.Status -ne 'PASS'){$script:MixedGate='FAIL_POST_CORRECTNESS'}
    $postIsolatedPath=Join-Path $script:ReviewRoot 'V22-Post-Isolated-Correctness.json';if(-not(Test-Path -LiteralPath $postIsolatedPath)){$postIsolatedValue=if($null -ne $script:AfterIsolated){$script:AfterIsolated}else{[ordered]@{Status='NOT_RUN';Reason='No post-isolated correctness execution'}};Write-NewJson $postIsolatedPath $postIsolatedValue}
    $postMixedPath=Join-Path $script:ReviewRoot 'V22-Post-Mixed-Correctness.json';if(-not(Test-Path -LiteralPath $postMixedPath)){$postMixedValue=if($null -ne $script:AfterMixed){$script:AfterMixed}else{[ordered]@{Status='NOT_RUN';Reason='No post-mixed correctness execution'}};Write-NewJson $postMixedPath $postMixedValue}
    $script:CurrentStage='FINAL_CANDIDATE_GUARD_AND_PRESERVATION';Write-RunProgress $script:CurrentStage 'IN_PROGRESS'
    $script:OverallStatus='BATCH3_PARTIAL'
}catch{
    Save-OriginalFailure $_
    $script:StopReason=if($script:StopReason){$script:StopReason}else{'STAGE_FAILED'}
    if(Test-Path -LiteralPath $script:PerformanceRunRoot){try{Write-NewJson (Join-Path $script:PerformanceRunRoot 'fatal-summary.json') ([ordered]@{SchemaVersion='warehouse-benchmark-v22-fatal/1';Status='HARNESS_FAILED';Stage=$script:CurrentStage;FailureReason=$script:StopReason;SafeExceptionMessage=$script:Failure.Message;CleanupResult='PENDING_FINALIZER';RecordedUtc=[DateTime]::UtcNow.ToString('o')})}catch{}}
}finally{
  try{
    $script:FinalizerStage='FINAL_CLEANUP';
    foreach($record in @($script:AllOwnedProcesses|Where-Object{-not $_.Completed})){
        try{
            if($null -ne $record.Process -and -not $record.Process.HasExited){[void](Stop-OwnedProcess $record 'FINAL_CLEANUP')}
            [void](Complete-OwnedProcess $record 'FINAL_CLEANUP')
        }catch{
            $cleanupError=Get-SafeText $_.Exception.Message
            try{$record.CleanupStatus='CLEANUP_FAILED';$record.FinalCleanupError=$cleanupError}catch{}
            try{$script:CleanupRows.Add([pscustomobject]@{ProcessLabel=$record.Label;ProcessId=$record.ChildProcessId;Status='CLEANUP_FAILED';Reason=$cleanupError;CapturedUtc=[DateTime]::UtcNow.ToString('o')})}catch{}
        }
    }    $script:FinalizerStage='POST_PRESERVATION_CHECK'
    if($OfflineFailureFixture -ceq 'FINALIZER_AFTER_ORIGINAL'){throw 'OFFLINE_FIXTURE_SECONDARY_FINALIZER_FAILURE'}
    if($script:PreSummarySourcePath -and (Test-Path -LiteralPath $script:PreservationPost -PathType Leaf) -and -not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'Previous-Evidence-Preservation.json'))){
        try{$postArgs=@('-NoProfile','-File',$script:PreservationPost,'-BatchRoot',$script:BatchRoot,'-ReviewRoot',$script:ReviewRoot,'-PreSummaryPath',$script:PreSummarySourcePath);$postRecord=Invoke-Tool (Join-Path $PSHOME 'pwsh.exe') $postArgs $script:RepoRoot 'preservation-post' $script:LogsRoot 1800;$script:PreservationStatus=if($postRecord.ExitCode -eq 0){'PASS'}else{'FAIL'}}catch{$script:PreservationStatus='FAIL'}
    }else{
        $proof=Get-CanonicalPreservationProof
        $script:PreservationStatus=if($null -ne $proof){$proof.Status}else{'MISSING'}
    }
    if($script:CandidateManifestPath -and (Test-Path -LiteralPath $script:CandidateManifestPath -PathType Leaf) -and -not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Guard.json'))){
        try{$manifest=Get-Content -LiteralPath $script:CandidateManifestPath -Raw|ConvertFrom-Json;$finalGuard=Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Guard.json';$finalInventory=Join-Path $script:PerformanceRunRoot 'candidate-inventory-final.json';$guardDir=Join-Path $script:PerformanceRunRoot 'guard-logs';$expected=$script:CandidateManifestHash;$gargs=@('-NoProfile','-File',$script:CandidateGuard,'-RepoRoot',$script:RepoRoot,'-CandidateRoot',$script:BatchRoot,'-CandidateManifestPath',$script:CandidateManifestPath,'-SourceInventoryPath',(Join-Path $script:ReviewRoot 'V22-Performance-Source-Inventory.json'),'-RuntimeInventoryPath',(Join-Path $script:ReviewRoot 'V22-Performance-Runtime-Inventory.json'),'-BuildAttestationPath',(Join-Path $script:ReviewRoot 'V22-Performance-Build-Attestation.json'),'-InventoryCheckPath',$finalInventory,'-OutputPath',$finalGuard,'-ExpectedManifestSHA256',$expected);$finalGuardRecord=Invoke-Tool (Join-Path $PSHOME 'pwsh.exe') $gargs $script:RepoRoot 'candidate-guard-final' $guardDir 600;$finalObj=Get-Content -LiteralPath $finalGuard -Raw|ConvertFrom-Json;$script:CandidateGuardFinalStatus=$finalObj.Status}catch{$script:CandidateGuardFinalStatus='FAIL'}
    }
    if(-not $script:CandidateCreated -or -not(Test-Path -LiteralPath $script:PerformanceRunRoot -PathType Container)){
        [void](Save-AbortDisposition)
    }else{
    $script:FinalizerStage='FINAL_CLEANUP'
    $finalCleanupResult=Invoke-PersistedFinalCleanup
    if($finalCleanupResult.Status -ne 'PASS' -and -not $script:StopReason){$script:StopReason='FINAL_AGGREGATE_CLEANUP_FAILED'}
    $cleanupReadbackPath=Join-Path $script:ReviewRoot 'V22-Final-Cleanup-Persistence-Verification.json'
    $cleanupReadback=Read-OptionalJson $cleanupReadbackPath
    $cleanupPersistencePass=($null -ne $cleanupReadback -and $cleanupReadback.Status -ceq 'PASS' -and $cleanupReadback.CanonicalRunId -ceq $script:CanonicalRunId -and $cleanupReadback.CandidateId -ceq $script:CandidateId -and -not [string]::IsNullOrWhiteSpace([string]$cleanupReadback.AggregateSHA256))
    if(-not $cleanupPersistencePass -and -not $script:StopReason){$script:StopReason='FINAL_CLEANUP_PERSISTENCE_FAILED'}
    $proof=Get-CanonicalPreservationProof
    if($null -ne $proof -and $script:PreservationStatus -eq 'NOT_RUN'){$script:PreservationStatus=$proof.Status}
    $coreRows=@($script:IsolatedRows|Where-Object BlockKind -in @('C1','C2'));$c4Rows=@($script:IsolatedRows|Where-Object BlockKind -eq 'C4');$bdnRows=@($script:IsolatedRows|Where-Object BlockKind -eq 'BDN')
    $script:IsolatedGate=if($coreRows.Count -eq 12 -and @($coreRows|Where-Object Status -ne 'PASS').Count -eq 0 -and $script:PrePostStatus -match 'PASS' -and @($coreRows|Where-Object TelemetryStatus -ne 'TELEMETRY_VALID').Count -eq 0){'PASS'}elseif($coreRows.Count -eq 0){'NOT_RUN'}else{'FAIL'}
    if($script:MixedGate -eq 'NOT_RUN'){$script:MixedGate=if($script:IsolatedGate -eq 'PASS'){'NOT_RUN'}else{'SKIPPED_ISOLATED_GATE_FAILED'}}
    $isolatedTotal=[long]0;$isolatedFailed=[long]0;foreach($row in $script:IsolatedRows|Where-Object BlockKind -in @('C1','C2','C4')){if($null -ne $row.Requests){$isolatedTotal+=[long]$row.Requests};if($null -ne $row.Failed){$isolatedFailed+=[long]$row.Failed}}
    $mixedFailed=[long]0;foreach($row in $script:MixedRows){if($null -ne $row.Failed){$mixedFailed+=[long]$row.Failed}}
    $allCleanup=($null -ne $script:FinalCleanupProof -and $script:FinalCleanupProof.AggregateCleanupStatus -ceq 'PASS' -and $cleanupPersistencePass -and @($script:CleanupRows|Where-Object Status -eq 'CLEANUP_FAILED').Count -eq 0)
    if($allCleanup){$cleanupStatus='PASS'}else{$cleanupStatus='FAIL'}
    $script:CleanupCountProjection=New-V22CleanupCountProjection `
        $(if($script:FinalCleanupEvidence){$script:FinalCleanupEvidence.ProcessCount}else{$null}) `
        $(if($script:FinalCleanupEvidence){$script:FinalCleanupEvidence.PidProbeCount}else{$null}) `
        $(if($script:FinalCleanupProof){$script:FinalCleanupProof.OwnedProcessCount}else{$null}) `
        $(if($script:FinalCleanupEvidence){$script:FinalCleanupEvidence.ProcessCount}else{$null}) `
        $script:AllOwnedProcesses.Count
    if($script:CleanupCountProjection.Status -cne 'PASS'){$allCleanup=$false;$cleanupStatus='FAIL';if(-not$script:StopReason){$script:StopReason='CLEANUP_COUNT_INVARIANT_FAILED'}}
    $manifestPath=Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Manifest.json'
    $candidateHash=if(Test-Path -LiteralPath $manifestPath){Get-FileHashHex $manifestPath}else{$null}
    $manifest=if(Test-Path -LiteralPath $manifestPath){Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json}else{$null}
    $script:CandidateId=if($null -ne $manifest){$manifest.CandidateId}else{$script:CandidateId};$script:SourceSnapshotId=if($null -ne $manifest){$manifest.Source.SnapshotId}else{$script:SourceSnapshotId}
    $candidatePass=($null -ne $manifest -and $manifest.Status -ceq 'CANDIDATE_BUILT_AND_ATTESTED' -and $manifest.Build.ReproducibilityStatus -ceq 'PASS' -and $script:CandidateGuardInitialStatus -ceq 'PASS' -and $script:CandidateGuardCanonicalStatus -ceq 'PASS' -and $script:CandidateGuardFinalStatus -ceq 'PASS')
    $correctnessPass=($script:PreflightStatus -ceq 'PASS')
    $telemetryPass=(@($script:TelemetryAssessments|Where-Object Status -ne 'TELEMETRY_VALID').Count -eq 0 -and @($script:TelemetryAssessments|Where-Object BlockId -like '*-ISO-*'|Where-Object ValidTargetRows -lt 1).Count -eq 0)
    $mixedPass=($script:MixedGate -eq 'PASS')
    $preservationPass=($script:PreservationStatus -ceq 'PASS')
    $comparatorPass=($script:ComparatorStatus -ceq 'PASS')
    $c4Pass=($c4Rows.Count -eq 6 -and @($c4Rows|Where-Object Status -ne 'PASS').Count -eq 0)
    $bdnPass=($bdnRows.Count -eq 6 -and @($bdnRows|Where-Object Status -ne 'PASS').Count -eq 0)
    $postIsolatedPass=($null -ne $script:AfterIsolated -and $script:AfterIsolated.Status -ceq 'PASS' -and $script:AfterIsolated.SemanticScenarios -ceq '6/6' -and $script:AfterIsolated.TotalCount -ceq '6/6')
    $postMixedPass=($null -ne $script:AfterMixed -and $script:AfterMixed.Status -ceq 'PASS' -and $script:AfterMixed.SemanticScenarios -ceq '6/6' -and $script:AfterMixed.TotalCount -ceq '6/6')
    $postPass=($script:PrePostStatus -ceq 'PASS' -and $postIsolatedPass -and $postMixedPass)
    $isolatedCorePass=($coreRows.Count -eq 12 -and @($coreRows|Where-Object Status -ne 'PASS').Count -eq 0 -and $isolatedTotal -gt 0 -and $isolatedFailed -eq 0)
    $artifactIndexPath=Join-Path $script:PerformanceRunRoot 'raw-artifact-hashes.json'
    $rawArtifactExclusions=@('progress.json','raw-artifact-hashes.json')
    $artifactsBeforeIndex=@(Get-ChildItem -LiteralPath $script:PerformanceRunRoot -File -Recurse -Force|ForEach-Object{$relative=$_.FullName.Substring($script:PerformanceRunRoot.TrimEnd('\').Length+1).Replace('\','/');if($relative -notin $rawArtifactExclusions){[pscustomobject]@{RelativePath=$relative;Size=[long]$_.Length;SHA256=(Get-FileHashHex $_.FullName)}}})
    if(Test-Path -LiteralPath $artifactIndexPath){throw 'RAW_ARTIFACT_INDEX_ALREADY_EXISTS; refusing overwrite'}
    Write-NewJson $artifactIndexPath ([ordered]@{SchemaVersion='warehouse-benchmark-v22-performance-raw-artifact-hashes/2';RunId=$script:CanonicalRunId;Count=$artifactsBeforeIndex.Count;Entries=$artifactsBeforeIndex;ExcludedMutableFiles=@('progress.json');ExcludedIndexFile='raw-artifact-hashes.json'})
    $artifactIndexHash=if(Test-Path $artifactIndexPath){Get-FileHashHex $artifactIndexPath}else{$null}
    $rawArtifactIntegrity=Test-V22RawArtifactHashIndex $artifactIndexPath $script:PerformanceRunRoot $script:CanonicalRunId
    $script:OverallStatus=if($script:StorageAdmissionStatus -ceq 'PASS' -and $script:CanonicalRunIdentityStatus -ceq 'PASS' -and $candidatePass -and $correctnessPass -and $script:Preflight.SemanticScenarios -ceq '6/6' -and $script:Preflight.TotalCount -ceq '6/6'  -and $isolatedCorePass -and $c4Pass -and $bdnPass -and $isolatedTelemetry -and $mixedPass -and $telemetryPass -and $postPass -and $allCleanup -and $preservationPass -and $comparatorPass -and $rawArtifactIntegrity.Status -ceq 'PASS'){'BATCH3_PASS'}elseif($script:CanonicalWorkloadStarted -or $script:IsolatedRows.Count -gt 0 -or $script:MixedLevels.Count -gt 0){'BATCH3_PARTIAL'}else{'BATCH3_NOT_READY'}
    $isolatedTotals=[ordered]@{Status=if($isolatedCorePass -and $c4Pass -and $isolatedTelemetry){'PASS'}elseif($script:IsolatedRows.Count -gt 0){'PARTIAL'}else{'NOT_RUN'};RunId=$script:RunId;CandidateId=$script:CandidateId;CoreRowsExpected=12;CoreRowsValid=@($coreRows|Where-Object Status -eq 'PASS').Count;CoreRows=$coreRows;C4RowsExpected=6;C4RowsValid=@($c4Rows|Where-Object Status -eq 'PASS').Count;C4Rows=$c4Rows;TotalNBomberRequests=$isolatedTotal;TotalNBomberFailed=$isolatedFailed;TelemetryValid=($isolatedTelemetry -and $telemetryPass);PrePostStatus=$script:PrePostStatus;StartedUtc=if($null -ne $isolatedManifest){$isolatedManifest.StartedUtc}else{$null};FinishedUtc=if($null -ne $isolatedManifest){$isolatedManifest.FinishedUtc}else{$null};ArtifactsRoot=(Join-Path $script:PerformanceRunRoot 'isolated')}
    $bdnOut=[ordered]@{Status=if($bdnPass){'PASS'}elseif($bdnRows.Count -gt 0){'PARTIAL'}else{'NOT_RUN'};ExpectedScenarios=6;ValidScenarios=@($bdnRows|Where-Object Status -eq 'PASS').Count;Rows=$bdnRows;MeasurementCountSource='Paired raw WorkloadActual and WorkloadResult iteration rows; statistical sample count reconciled with raw WorkloadResult, N, and upper fence'}
    $mixedOut=[ordered]@{Status=if($mixedPass){'PASS'}elseif($script:MixedLevels.Count -gt 0){'PARTIAL'}else{'SKIPPED'};Levels=$script:MixedLevels.ToArray();Workers=$script:MixedRows.ToArray();FailedRequests=$mixedFailed;Interpretation='L8 is standalone mixed load; aggregate RPS is DESCRIPTIVE_SUM_OF_SCENARIO_RPS'}
    $telemetryOut=[ordered]@{Status=if($telemetryPass){'PASS'}elseif($script:TelemetryAssessments.Count -gt 0){'INVALID'}else{'NOT_RUN'};RequiredRule='At least one valid target sample; exact run/block/database/database-id; any error/rejected row invalidates the block';Assessments=$script:TelemetryAssessments.ToArray();DiagnosticOnly=@('ActiveRequestLogicalReadsDiagnostic','TempdbServerUsedKBDiagnostic','MemoryGrantsPendingCounterDiagnostic','DeadlockCounterDiagnostic')}
    $prepost=[ordered]@{Status=$script:PrePostStatus;CorrectnessPreflight=$script:Preflight;AfterIsolated=$script:AfterIsolated;AfterMixed=$script:AfterMixed;IsolatedPreSnapshot=$script:isolatedBeginSnapshot;IsolatedPostSnapshot=if($null -ne $script:AfterIsolated){$script:AfterIsolated.DBEvidence.Post.SnapshotSha256}else{$null};IsolatedTransitionEqual=($null -ne $script:AfterIsolated -and $script:isolatedBeginSnapshot -ceq $script:AfterIsolated.DBEvidence.Pre.SnapshotSha256);MixedTransitionEqual=($null -eq $script:AfterMixed -or ($null -ne $script:AfterIsolated -and $script:AfterIsolated.DBEvidence.Post.SnapshotSha256 -ceq $script:AfterMixed.DBEvidence.Pre.SnapshotSha256));FinalCorrectnessPass=$postPass;FullDatasetValueEquality='NOT_VERIFIED'}
    $cleanupOut=[ordered]@{Status=$cleanupStatus;ProcessCount=if($script:CleanupCountProjection){$script:CleanupCountProjection.CoreGateCleanupProjection}else{$null};TrackedProcessRecordCount=$script:AllOwnedProcesses.Count;CleanupCountProjection=$script:CleanupCountProjection;FinalCleanupProcessProof=$script:FinalCleanupProof;OwnedProcessInventory=if($script:PersistedOwnedInventory){@($script:PersistedOwnedInventory.ProcessRecords)}else{@()};TrackedProcessRecords=@($script:AllOwnedProcesses|ForEach-Object{[pscustomobject]@{Label=$_.Label;Role=$_.Role;ChildProcessId=$_.ChildProcessId;ExitCode=$_.ExitCode;TimedOut=$_.TimedOut;KilledForHostLimit=$_.KilledForHostLimit;CleanupStatus=$_.CleanupStatus;Completed=$_.Completed}});CleanupRows=$script:CleanupRows.ToArray();ResidueFinal='See final bounded correctness snapshot and per-block cooldown residue evidence';OrphanOwnedProcessCount=@($script:AllOwnedProcesses|Where-Object{-not $_.ProcessGone -or $_.CleanupProofStatus -ne 'CLEAN'}).Count;SQLSessionAttribution='NOT_VERIFIED; residue contract covers active target-database requests, grants and resource-semaphore waiters'}
    $limitations=[ordered]@{SchemaVersion='warehouse-benchmark-v22-performance-known-limitations/1';HistoricalTimeoutCause='NOT_VERIFIED';HistoricalTimeoutDetails='Earlier DocumentPaged/DetailReportPaged timeout cause is not identified by successful current execution.';FullDatasetValueEquality='NOT_VERIFIED';HistoricalComparison='SINGLE-WINDOW HISTORICAL COMPARISON';CausalAttribution='NOT_ESTABLISHED';PerformanceSla='NO_SLA_DEFINED';SqlServerCounterCausality='DIAGNOSTIC_ONLY';BoundedCorrectnessEquality='Does not establish full 10-million-row value equality';NewLimitations=@(if($script:Failure){$script:Failure.Message};if($script:StopReason){$script:StopReason})}
    $coreGate=[ordered]@{SchemaVersion='warehouse-benchmark-v22-core-evidence-gate/1';RunId=$script:RunId;Candidate=[ordered]@{Status=if($candidatePass){'PASS'}elseif($null -ne $manifest){'INVALID'}else{'MISSING'};CandidateId=$script:CandidateId;ManifestSHA256=$candidateHash;InitialGuard=$script:CandidateGuardInitialStatus;FinalGuard=$script:CandidateGuardFinalStatus};Correctness=[ordered]@{Status=if($correctnessPass){'PASS'}elseif($script:PreflightStatus -eq 'FAIL'){'FAIL'}else{'NOT_RUN'};Preflight=$script:Preflight};Isolated=[ordered]@{Status=if($isolatedCorePass -and $c4Pass -and $bdnPass){'PASS'}elseif($script:IsolatedRows.Count -gt 0){'PARTIAL'}else{'NOT_RUN'};CoreRowsValid=@($coreRows|Where-Object Status -eq 'PASS').Count;CoreRowsRequired=12;C4RowsValid=@($c4Rows|Where-Object Status -eq 'PASS').Count;C4RowsRequired=6;BdnRowsValid=@($bdnRows|Where-Object Status -eq 'PASS').Count;BdnRowsRequired=6};Mixed=[ordered]@{Status=$script:MixedGate;LevelStatuses=@($script:MixedLevels|ForEach-Object{[pscustomobject]@{Level=$_.Level;Status=$_.Status;OverlapValid=$_.OverlapValid;TelemetryStatus=$_.TelemetryStatus}})};Telemetry=[ordered]@{Status=if($telemetryPass){'PASS'}elseif($script:TelemetryAssessments.Count -gt 0){'INVALID'}else{'NOT_RUN'};ValidBlocks=@($script:TelemetryAssessments|Where-Object Status -eq 'TELEMETRY_VALID').Count;TotalAssessments=$script:TelemetryAssessments.Count};PostState=[ordered]@{Status=if($postPass){'PASS'}else{$script:PrePostStatus};FullDatasetValueEquality='NOT_VERIFIED'};Cleanup=[ordered]@{Status=$cleanupStatus;OwnedProcessCount=if($script:CleanupCountProjection){$script:CleanupCountProjection.CoreGateCleanupProjection}else{$null};CountProjection=$script:CleanupCountProjection};Artifacts=[ordered]@{Status=if($artifactsBeforeIndex.Count -gt 0 -and $artifactIndexHash){'PASS'}else{'PARTIAL'};RawArtifactCount=$artifactsBeforeIndex.Count;RawArtifactIndexPath=$artifactIndexPath;RawArtifactIndexSHA256=$artifactIndexHash};Comparator=[ordered]@{Status=$script:ComparatorStatus;IdentityPath=(Join-Path $script:ReviewRoot 'V21-Comparator-Identity.json')};Preservation=[ordered]@{Status=$script:PreservationStatus;ProofPath=(Join-Path $script:ReviewRoot 'Previous-Evidence-Preservation.json')};OverallStatus=$script:OverallStatus;CoreEvidenceReady=($script:OverallStatus -eq 'BATCH3_PASS');ReadyForBatch4=($script:OverallStatus -eq 'BATCH3_PASS');SlaStatus='NO_SLA_DEFINED';RecordedUtc=[DateTime]::UtcNow.ToString('o')}
    $coreGate['MixedHarnessOffline']=[ordered]@{Status=if($mixedHarnessOfflinePass){'PASS'}elseif($null -ne $mixedTestResult){'FAIL'}else{'NOT_RUN'};Passed=if($null -ne $mixedTestResult){$mixedTestResult.Passed}else{$null};Failed=if($null -ne $mixedTestResult){$mixedTestResult.Failed}else{$null};TestCount=if($null -ne $mixedTestResult){$mixedTestResult.TestCount}else{$null};EvidencePath=$script:MixedHarnessTestsPath;DatabaseAccess='NOT_USED';Workload='NOT_RUN'}
    $coreGate.Artifacts.Status=$rawArtifactIntegrity.Status;$coreGate.Artifacts.VerifiedRawArtifactCount=$rawArtifactIntegrity.VerifiedCount
    $coreGate.Cleanup.PersistenceVerificationStatus=if($cleanupPersistencePass){'PASS'}else{'FAIL'};$coreGate.Cleanup.PersistenceVerificationPath=$cleanupReadbackPath
    $cleanupOut.PersistenceVerificationPath=$cleanupReadbackPath;$cleanupOut.PersistenceVerificationStatus=if($cleanupPersistencePass){'PASS'}else{'FAIL'};$cleanupOut.AggregateSHA256=if($cleanupReadback){$cleanupReadback.AggregateSHA256}else{$null}
    $reviewMap=[ordered]@{
        'V22-Performance-Correctness-Preflight.json'=$(if($null -ne $script:Preflight){$script:Preflight}else{@{Status='SKIPPED';Reason=$script:Failure.Message}})
        'V22-Isolated-Run-Manifest.json'=$(if($null -ne $isolatedManifest){$isolatedManifest}else{@{Status='SKIPPED';Reason='Candidate/correctness gate stopped before isolated run'}})
        'V22-Isolated-Results.json'=$isolatedTotals
        'V22-Isolated-Telemetry-Summary.json'=$telemetryOut
        'V22-BDN-Results.json'=$bdnOut
        'V22-Mixed-Run-Manifest.json'=$mixedManifest
        'V22-Mixed-Results.json'=$mixedOut
        'V22-Mixed-Harness-Tests.json'=$(if($null -ne $mixedTestResult){$mixedTestResult}else{@{Status='NOT_RUN';Reason='Offline Mixed harness tests were not reached';RunId=$script:RunId}})
        'V22-Mixed-Telemetry-Summary.json'=[ordered]@{Status=if($script:MixedGate -eq 'PASS'){'PASS'}elseif($script:MixedLevels.Count -gt 0){'PARTIAL'}else{'SKIPPED'};Levels=@($script:MixedLevels|ForEach-Object{[pscustomobject]@{Level=$_.Level;Status=$_.TelemetryStatus;ValidRows=$_.Telemetry.ValidTargetRows;ErrorRows=$_.Telemetry.ErrorRows;RejectedRows=$_.Telemetry.RejectedRows}})}
        'V22-Performance-PrePost-Evidence.json'=$prepost
        'V22-Cleanup-Residue-Evidence.json'=$cleanupOut
        'V22-Performance-Known-Limitations.json'=$limitations
        'V22-Core-Evidence-Gate.json'=$coreGate
    }
    foreach($name in $reviewMap.Keys){if(-not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot $name))){try{Write-NewJson (Join-Path $script:ReviewRoot $name) $reviewMap[$name]}catch{}}}
    if(-not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'V21-Comparator-Identity.json'))){try{Write-NewJson (Join-Path $script:ReviewRoot 'V21-Comparator-Identity.json') ([ordered]@{Status=$script:ComparatorStatus;Reason=if($script:Comparator){$script:Comparator.Reason}else{'Comparator not captured'}})}catch{}}
    if(-not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'V21-vs-V22-Toolchain-Comparison.json'))){try{Write-NewJson (Join-Path $script:ReviewRoot 'V21-vs-V22-Toolchain-Comparison.json') ([ordered]@{Status='NOT_VERIFIED';Reason='Build/toolchain gate did not complete'})}catch{}}
    if(-not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'V22-Performance-Protocol.json'))){try{Write-NewJson (Join-Path $script:ReviewRoot 'V22-Performance-Protocol.json') $script:ProtocolInfo}catch{}}
    if(-not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'V22-Performance-Source-Inventory.json'))){try{Write-NewJson (Join-Path $script:ReviewRoot 'V22-Performance-Source-Inventory.json') ([ordered]@{Status='SKIPPED';Reason='Candidate build gate stopped before source inventory'})}catch{}}
    if(-not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'V22-Performance-Source-Snapshot.json'))){try{Write-NewJson (Join-Path $script:ReviewRoot 'V22-Performance-Source-Snapshot.json') ([ordered]@{Status='SKIPPED';Reason='Candidate build gate stopped before source snapshot'})}catch{}}
    if(-not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'V22-Performance-Build-Attestation.json'))){try{Write-NewJson (Join-Path $script:ReviewRoot 'V22-Performance-Build-Attestation.json') ([ordered]@{Status='SKIPPED';Reason='Build attestation not created'})}catch{}}
    if(-not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'V22-Performance-Runtime-Inventory.json'))){try{Write-NewJson (Join-Path $script:ReviewRoot 'V22-Performance-Runtime-Inventory.json') ([ordered]@{Status='SKIPPED';Reason='Runtime freeze not created'})}catch{}}
    if(-not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Manifest.json'))){try{Write-NewJson (Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Manifest.json') ([ordered]@{Status='INVALID';Reason='No candidate manifest was created'})}catch{}}
    if(-not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Guard.json'))){try{Write-NewJson (Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Guard.json') ([ordered]@{Status='NOT_RUN';Reason='Final candidate integrity verification unavailable'})}catch{}}
    if(-not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'Previous-Evidence-Preservation.json'))){try{Write-NewJson (Join-Path $script:ReviewRoot 'Previous-Evidence-Preservation.json') ([ordered]@{Status='FAIL';Reason='POST preservation check did not produce proof';PreSummarySHA256=$script:InitialPreservationSummaryHash})}catch{}}
    if(-not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'V22-Performance-Source-Inventory-Spec.md'))){try{Write-NewText (Join-Path $script:ReviewRoot 'V22-Performance-Source-Inventory-Spec.md') '# Inventory specification unavailable; candidate build blocked.'}catch{}}
    $acceptance=[ordered]@{SchemaVersion='warehouse-benchmark-v22-batch3-acceptance/1';OverallStatus=$script:OverallStatus;CandidateId=$script:CandidateId;SourceSnapshotId=$script:SourceSnapshotId;Gates=[ordered]@{Preservation=[ordered]@{Status=$script:PreservationStatus;Evidence=(Join-Path $script:ReviewRoot 'Previous-Evidence-Preservation.json');Reason=if($script:PreservationStatus -eq 'PASS'){'PRE and POST immutable sets match'}else{'PRE/POST preservation not proven'}};Protocol2_2=[ordered]@{Status=if(Test-Path (Join-Path $script:ReviewRoot 'V22-Performance-Protocol.json')){'PASS'}else{'FAIL'};Evidence=(Join-Path $script:ReviewRoot 'V22-Performance-Protocol.json');Reason='Protocol identity 2.2'};StaticChecks=[ordered]@{Status=if($null -ne $static){$static.Status}else{'NOT_RUN'};Evidence=$script:StaticChecksPath;Reason='PowerShell parse and PID collision scan'};FailureInjection=[ordered]@{Status=if($null -ne $fiResult){$fiResult.Status}else{'NOT_RUN'};Evidence=$script:FailureInjectionPath;Reason=if($null -ne $fiResult){"$($fiResult.Passed)/$($fiResult.TestCount) passed"}else{'Not run'}};OfflineSelfTest=[ordered]@{Status=if($null -ne $selfResult){$selfResult.Status}else{'NOT_RUN'};Evidence=$script:SelfTestPath;Reason='Database/load not used'};SourceInventory=[ordered]@{Status=if(Test-Path (Join-Path $script:ReviewRoot 'V22-Performance-Source-Inventory.json')){'CAPTURED'}else{'NOT_RUN'};Evidence=(Join-Path $script:ReviewRoot 'V22-Performance-Source-Inventory.json');Reason='Canonical MSBuild evaluated inventory'};BuildA=[ordered]@{Status=if($null -ne $manifest){$manifest.Build.ReproducibilityStatus}else{'NOT_RUN'};Evidence=(Join-Path $script:ReviewRoot 'V22-Performance-Build-Attestation.json');Reason='Release from copied snapshot'};BuildB=[ordered]@{Status=if($null -ne $manifest){$manifest.Build.ReproducibilityStatus}else{'NOT_RUN'};Evidence=(Join-Path $script:ReviewRoot 'V22-Performance-Build-Attestation.json');Reason='Independent Release build'};Reproducibility=[ordered]@{Status=if($null -ne $manifest){$manifest.Build.ReproducibilityStatus}else{'NOT_RUN'};Evidence=(Join-Path $script:ReviewRoot 'V22-Performance-Build-Attestation.json');Reason='Build A/B binaries compared'};RuntimeFreeze=[ordered]@{Status=if($candidatePass){'PASS'}elseif($null -ne $manifest){'INVALID'}else{'NOT_RUN'};Evidence=(Join-Path $script:ReviewRoot 'V22-Performance-Runtime-Inventory.json');Reason='Frozen runtime and complete inventory'};CandidateManifest=[ordered]@{Status=if($candidatePass){'PASS'}else{'INVALID'};Evidence=(Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Manifest.json');Reason='Source/build/binary/runtime identity link'};CandidatePostFreezeGuard=[ordered]@{Status=$script:CandidateGuardFinalStatus;Evidence=(Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Guard.json');Reason='Manifest/source/binary/runtime reverified after run'};CorrectnessPreflight=[ordered]@{Status=$script:PreflightStatus;Evidence=(Join-Path $script:ReviewRoot 'V22-Performance-Correctness-Preflight.json');Reason='Exact performance candidate against Batch 2 contracts'};IsolatedCore=[ordered]@{Status=if($isolatedCorePass){'PASS'}elseif($script:IsolatedRows.Count -gt 0){'FAIL'}else{'NOT_RUN'};Evidence=(Join-Path $script:ReviewRoot 'V22-Isolated-Results.json');Reason="$(@($coreRows|Where-Object Status -eq 'PASS').Count)/12 valid C1/C2 rows"};C4=[ordered]@{Status=if($c4Pass){'PASS'}elseif($c4Rows.Count -gt 0){'PARTIAL'}else{'NOT_RUN'};Evidence=(Join-Path $script:ReviewRoot 'V22-Isolated-Results.json');Reason="$(@($c4Rows|Where-Object Status -eq 'PASS').Count)/6 valid C4 rows"};BDN=[ordered]@{Status=if($bdnPass){'PASS'}elseif($bdnRows.Count -gt 0){'PARTIAL'}else{'NOT_RUN'};Evidence=(Join-Path $script:ReviewRoot 'V22-BDN-Results.json');Reason="$(@($bdnRows|Where-Object Status -eq 'PASS').Count)/6 valid BDN scenarios"};Mixed=[ordered]@{Status=$script:MixedGate;Evidence=(Join-Path $script:ReviewRoot 'V22-Mixed-Results.json');Reason='L1/L2/L4/L8 each require six workers, zero errors, overlap and telemetry'};Telemetry=[ordered]@{Status=if($telemetryPass){'PASS'}elseif($script:TelemetryAssessments.Count -gt 0){'INVALID'}else{'NOT_RUN'};Evidence=(Join-Path $script:ReviewRoot 'V22-Isolated-Telemetry-Summary.json');Reason='Exact run/block/target and at least one valid target row; errors invalidate'};PostState=[ordered]@{Status=if($postPass){'PASS'}else{$script:PrePostStatus};Evidence=(Join-Path $script:ReviewRoot 'V22-Performance-PrePost-Evidence.json');Reason='Bounded before/after correctness snapshots'};Cleanup=[ordered]@{Status=$cleanupStatus;Evidence=(Join-Path $script:ReviewRoot 'V22-Cleanup-Residue-Evidence.json');Reason='Only owned process trees cleaned; residue checked'};Artifacts=[ordered]@{Status=if($artifactIndexHash){'PASS'}else{'FAIL'};Evidence=$artifactIndexPath;Reason='Raw run artifacts hashed before index creation'};Comparator=[ordered]@{Status=$script:ComparatorStatus;Evidence=(Join-Path $script:ReviewRoot 'V21-Comparator-Identity.json');Reason='Canonical 2.1 adaptive/mixed final artifacts'};ToolchainComparison=[ordered]@{Status=if(Test-Path (Join-Path $script:ReviewRoot 'V21-vs-V22-Toolchain-Comparison.json')){'CAPTURED'}else{'NOT_RUN'};Evidence=(Join-Path $script:ReviewRoot 'V21-vs-V22-Toolchain-Comparison.json');Reason='Unknown values remain explicit'};NoPerformanceSLA=[ordered]@{Status='NO_SLA_DEFINED';Evidence=(Join-Path $script:ReviewRoot 'V22-Performance-Known-Limitations.json');Reason='No threshold invented'}};Counts=[ordered]@{SourceInventory=if($manifest){$manifest.Source.InventoryEntryCount}else{0};RuntimeInventory=if($manifest){$manifest.Runtime.EntryCount}else{0};FailureInjectionPassed=if($null -ne $fiResult){$fiResult.Passed}else{0};FailureInjectionTotal=if($null -ne $fiResult){$fiResult.TestCount}else{0};CorrectnessPassed=if($null -ne $script:Preflight){$script:Preflight.SemanticScenariosCompleted}else{0};IsolatedCoreValid=@($coreRows|Where-Object Status -eq 'PASS').Count;C4Valid=@($c4Rows|Where-Object Status -eq 'PASS').Count;BdnValid=@($bdnRows|Where-Object Status -eq 'PASS').Count;IsolatedRequests=$isolatedTotal;IsolatedFailed=$isolatedFailed;MixedFailed=$mixedFailed};RecordedUtc=[DateTime]::UtcNow.ToString('o')}
    $acceptance.Gates['MixedHarnessOffline']=[ordered]@{Status=if($mixedHarnessOfflinePass){'PASS'}elseif($null -ne $mixedTestResult){'FAIL'}else{'NOT_RUN'};Evidence=$script:MixedHarnessTestsPath;Reason=if($null -ne $mixedTestResult){'{0}/{1} tests passed' -f $mixedTestResult.Passed,$mixedTestResult.TestCount}else{'Offline Mixed tests were not reached'}}
    if(-not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'Batch3-Acceptance.json'))){try{Write-NewJson (Join-Path $script:ReviewRoot 'Batch3-Acceptance.json') $acceptance}catch{}}
    if(-not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'Batch3-Changed-Files.txt'))){
        $lines=[Collections.Generic.List[string]]::new();$lines.Add('Warehouse Benchmark 2.2 Batch 3 source changes relative to the pre-Batch-3 inventory.');$preInventoryPath=Join-Path $script:LogsRoot 'source-inventory-prebatch3.json';$newInventoryPath=Join-Path $script:ReviewRoot 'V22-Performance-Source-Inventory.json'
        if((Test-Path $preInventoryPath) -and (Test-Path $newInventoryPath)){$before=Get-Content $preInventoryPath -Raw|ConvertFrom-Json;$after=Get-Content $newInventoryPath -Raw|ConvertFrom-Json;$old=@{};foreach($e in $before.Entries){$old[[string]$e.NormalizedRelativePath]=$e};$new=@{};foreach($e in $after.Entries){$new[[string]$e.NormalizedRelativePath]=$e};foreach($path in @($new.Keys|Sort-Object -CaseSensitive)){if(-not$old.ContainsKey($path)){$lines.Add("CREATED`t$($script:RepoRoot)\$($path.Replace('/','\'))")}elseif($old[$path].SHA256 -cne $new[$path].SHA256){$lines.Add("MODIFIED`t$($script:RepoRoot)\$($path.Replace('/','\'))")}};foreach($path in @($old.Keys|Sort-Object -CaseSensitive)){if(-not$new.ContainsKey($path)){$lines.Add("DELETED`t$($script:RepoRoot)\$($path.Replace('/','\'))")}}}else{$lines.Add('NOT_VERIFIED`tPre-Batch-3 inventory comparison unavailable')}
        $lines.Add("EVIDENCE_ROOT`t$($script:BatchRoot)");Write-NewText (Join-Path $script:ReviewRoot 'Batch3-Changed-Files.txt') ($lines -join [Environment]::NewLine)
    }
    $requestFiles=@('Batch3-Implementation-Report.md','Batch3-Acceptance.json','Batch3-Changed-Files.txt','Previous-Evidence-Preservation.json','V22-Prior-Attempt-Supersession.json','V22-Performance-Protocol.json','V22-Performance-Source-Inventory.json','V22-Performance-Source-Snapshot.json','V22-Performance-Build-Attestation.json','V22-Performance-Runtime-Inventory.json','V22-Performance-Candidate-Manifest.json','V22-Performance-Candidate-Guard.json','V22-Performance-Correctness-Preflight.json','V21-Comparator-Identity.json','V21-vs-V22-Toolchain-Comparison.json','V22-Isolated-Run-Manifest.json','V22-Isolated-Results.json','V22-Isolated-Telemetry-Summary.json','V21-vs-V22-Isolated-Comparison.json','V22-BDN-Results.json','V22-Mixed-Harness-Tests.json','V22-Mixed-Run-Manifest.json','V22-Mixed-Results.json','V22-Mixed-Telemetry-Summary.json','V22-Isolated-vs-Mixed-Comparison.json','V22-Performance-PrePost-Evidence.json','V22-Cleanup-Residue-Evidence.json','V22-Performance-Known-Limitations.json','V22-Core-Evidence-Gate.json','Batch3-Review-Index.md')
    foreach($name in $requestFiles|Where-Object{$_ -notin @('Batch3-Implementation-Report.md','Batch3-Review-Index.md')}){Ensure-ReviewArtifact $name ('No complete evidence was produced; current stage='+$script:CurrentStage)}
    $candidateManifest=if(Test-Path $manifestPath){Get-Content $manifestPath -Raw|ConvertFrom-Json}else{$null}
    $v21Nb=@();$v21Bdn=@();if($script:ComparatorStatus -eq 'PASS'){$v21Nb=Import-Csv 'P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_1_ADAPTIVE-20260916-FINAL\v2.1-nbomber-summary.csv' -Delimiter ',';$v21Bdn=Import-Csv 'P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_1_ADAPTIVE-20260916-FINAL\v2.1-bdn-summary.csv' -Delimiter ','}
    $comparisonRows=[Collections.Generic.List[object]]::new()
foreach($row in $script:IsolatedRows){
    $kind=if([string]$row.BlockKind-ceq'BDN'){'BDN'}else{'NB'}
    $current=Get-V22MetricProjection $row $kind
    if(-not$current.Comparable){continue}
    $profile=if($row.Copies-eq 1-or$row.Copies-eq 2){'CORE_NBOMBER'}else{'EXTENDED_STANDARD'}
    $old=$v21Nb|Where-Object{$_.Scenario-ceq$row.Scenario-and[int]$_.Level-eq$row.Copies-and$_.Profile-ceq$profile}|Select-Object -First 1
    $oldStatus=if($old){[string](Get-V22FinalizerField $old 'Status').Value}else{'NOT_VERIFIED'}
    $oldComparable=($null-ne$old-and$oldStatus-ceq'PASS')
    $oldMean=if($oldComparable){Get-V22OptionalNumericMetric $old 'MeanMs'}else{$null};$oldP50=if($oldComparable){Get-V22OptionalNumericMetric $old 'P50Ms'}else{$null};$oldP95=if($oldComparable){Get-V22OptionalNumericMetric $old 'P95Ms'}else{$null};$oldP99=if($oldComparable){Get-V22OptionalNumericMetric $old 'P99Ms'}else{$null};$oldRps=if($oldComparable){Get-V22OptionalNumericMetric $old 'Rps'}else{$null}
    $comparisonRows.Add([pscustomobject]@{Scenario=$row.Scenario;Copies=$row.Copies;HistoricalStatus=$oldStatus;HistoricalMeanMs=$oldMean;CurrentMeanMs=$current.MeanMs;MeanDeltaPct=if($null-ne$oldMean-and$null-ne$current.MeanMs){Get-Delta $oldMean $current.MeanMs}else{$null};HistoricalP50Ms=$oldP50;CurrentP50Ms=$current.P50Ms;P50DeltaPct=if($null-ne$oldP50-and$null-ne$current.P50Ms){Get-Delta $oldP50 $current.P50Ms}else{$null};HistoricalP95Ms=$oldP95;CurrentP95Ms=$current.P95Ms;P95DeltaPct=if($null-ne$oldP95-and$null-ne$current.P95Ms){Get-Delta $oldP95 $current.P95Ms}else{$null};HistoricalP99Ms=$oldP99;CurrentP99Ms=$current.P99Ms;P99DeltaPct=if($null-ne$oldP99-and$null-ne$current.P99Ms){Get-Delta $oldP99 $current.P99Ms}else{$null};HistoricalRPS=$oldRps;CurrentRPS=$current.RPS;RPSDeltaPct=if($null-ne$oldRps-and$null-ne$current.RPS){Get-Delta $oldRps $current.RPS}else{$null};Interpretation='DESCRIPTIVE_ONLY_SOURCE_AND_ENVIRONMENT_COMPARISON'})
}$historicalComparison=[ordered]@{SchemaVersion='warehouse-benchmark-v21-vs-v22-isolated-comparison/1';Status=if($comparisonRows.Count -gt 0){'DESCRIPTIVE'}else{'NOT_AVAILABLE'};Rows=$comparisonRows.ToArray();NoSlaThreshold='NO_SLA_DEFINED';StatisticalLimitation='SINGLE-WINDOW HISTORICAL COMPARISON';Causality='NOT_ESTABLISHED'}
    if(-not(Test-Path (Join-Path $script:ReviewRoot 'V21-vs-V22-Isolated-Comparison.json'))){try{Write-NewJson (Join-Path $script:ReviewRoot 'V21-vs-V22-Isolated-Comparison.json') $historicalComparison}catch{}}
    $mixedComparison=[Collections.Generic.List[object]]::new()
foreach($levelResult in $script:MixedLevels){
    $workersField=Get-V22FinalizerField $levelResult 'WorkersPerScenario';if(-not$workersField.Present-or$null-eq$workersField.Value){continue};$workers=[int]$workersField.Value
    $isolatedCopies=if($workers-in@(1,2,4)){$workers}else{$null};$workerRowsField=Get-V22FinalizerField $levelResult 'WorkerRows';if(-not$workerRowsField.Present){continue}
    foreach($worker in @($workerRowsField.Value)){
        $workerMetrics=Get-V22MetricProjection $worker 'NB';if(-not$workerMetrics.Comparable){continue}
        $iso=$script:IsolatedRows|Where-Object{$_.Status-ceq'PASS'-and$_.BlockKind-in@('C1','C2','C4')-and$_.Scenario-ceq$worker.Scenario-and$_.Copies-eq$isolatedCopies}|Select-Object -First 1
        $isoMetrics=if($null-ne$iso){Get-V22MetricProjection $iso 'NB'}else{$null}
        $mixedComparison.Add([pscustomobject]@{Level=$levelResult.Level;Scenario=$worker.Scenario;MixedCopies=$workers;ComparisonProfile=if($workers-eq 8){'STANDALONE_MIXED_LOAD_LEVEL'}else{'MIXED_VS_ISOLATED_C'+$workers};IsolatedMeanMs=if($isoMetrics){$isoMetrics.MeanMs}else{$null};MixedMeanMs=$workerMetrics.MeanMs;MeanDeltaPct=if($isoMetrics-and$null-ne$isoMetrics.MeanMs-and$null-ne$workerMetrics.MeanMs){Get-Delta $isoMetrics.MeanMs $workerMetrics.MeanMs}else{$null};IsolatedP95Ms=if($isoMetrics){$isoMetrics.P95Ms}else{$null};MixedP95Ms=$workerMetrics.P95Ms;P95DeltaPct=if($isoMetrics-and$null-ne$isoMetrics.P95Ms-and$null-ne$workerMetrics.P95Ms){Get-Delta $isoMetrics.P95Ms $workerMetrics.P95Ms}else{$null};IsolatedP99Ms=if($isoMetrics){$isoMetrics.P99Ms}else{$null};MixedP99Ms=$workerMetrics.P99Ms;P99DeltaPct=if($isoMetrics-and$null-ne$isoMetrics.P99Ms-and$null-ne$workerMetrics.P99Ms){Get-Delta $isoMetrics.P99Ms $workerMetrics.P99Ms}else{$null};IsolatedRPS=if($isoMetrics){$isoMetrics.RPS}else{$null};MixedRPS=$workerMetrics.RPS;RPSDeltaPct=if($isoMetrics-and$null-ne$isoMetrics.RPS-and$null-ne$workerMetrics.RPS){Get-Delta $isoMetrics.RPS $workerMetrics.RPS}else{$null};Interpretation=if($workers-eq 8){'STANDALONE_MIXED_LOAD_LEVEL'}else{'DESCRIPTIVE_ONLY_NO_SLA_THRESHOLD'}})
    }
}if(-not(Test-Path (Join-Path $script:ReviewRoot 'V22-Isolated-vs-Mixed-Comparison.json'))){try{Write-NewJson (Join-Path $script:ReviewRoot 'V22-Isolated-vs-Mixed-Comparison.json') ([ordered]@{SchemaVersion='warehouse-benchmark-v22-isolated-vs-mixed-comparison/1';Status=if($mixedComparison.Count -gt 0){'DESCRIPTIVE'}else{'NOT_AVAILABLE'};Rows=$mixedComparison.ToArray();AggregateRPS='DESCRIPTIVE_SUM_OF_SCENARIO_RPS';NoSlaThreshold='NO_SLA_DEFINED';Causality='NOT_ESTABLISHED'})}catch{}}
    $script:CurrentStage='FINAL_REPORT_AND_REVIEW_PACK';Write-RunProgress $script:CurrentStage $script:OverallStatus
    if($script:OverallStatus -eq 'BATCH3_PASS'){$headline='BATCH3_PASS; measurements are valid and descriptive. No performance SLA conclusion is made.'}elseif($script:OverallStatus -eq 'BATCH3_PARTIAL'){$headline='BATCH3_PARTIAL; valid rows are retained, but mandatory acceptance gates did not all pass.'}else{$headline='BATCH3_NOT_READY; the required candidate/correctness/performance gates did not complete.'}
    $report=[Collections.Generic.List[string]]::new();$report.Add('# Warehouse Benchmark 2.2 Batch 3 Implementation Report');$report.Add('');$report.Add("**Verdict:** $headline");$report.Add('');$report.Add('## 1. Executive summary');$report.Add('');$priorAttemptPath=Join-Path $script:PriorReviewRoot 'V22-Prior-Attempt-Supersession.json';if(Test-Path -LiteralPath $priorAttemptPath){$priorAttempt=Get-Content -LiteralPath $priorAttemptPath -Raw|ConvertFrom-Json;$priorAttemptRecords=if($priorAttempt.PSObject.Properties['Attempts']){@($priorAttempt.Attempts)}else{@()};if($priorAttemptRecords.Count -gt 0){foreach($priorRecord in $priorAttemptRecords){$priorRunIdText=if($priorRecord.PSObject.Properties['RunId']){[string]$priorRecord.RunId}else{'UNKNOWN'};$priorCandidateText=if($priorRecord.PSObject.Properties['CandidateId']){[string]$priorRecord.CandidateId}else{'UNKNOWN'};$priorStatusText=if($priorRecord.PSObject.Properties['Status']){[string]$priorRecord.Status}else{'UNKNOWN'};$priorFailureStageText=if($priorRecord.PSObject.Properties['FailureStage']){[string]$priorRecord.FailureStage}else{'NOT_RECORDED'};$report.Add("Prior attempt: run=$priorRunIdText; candidate=$priorCandidateText; status=$priorStatusText; stage=$priorFailureStageText.")}}elseif($priorAttempt.PSObject.Properties['RunId']){$report.Add("Prior attempt: run=$($priorAttempt.RunId); candidate=$($priorAttempt.SupersededCandidateId); status=$($priorAttempt.Status).")}else{$report.Add('Prior-attempt metadata has no structured attempt identity; see supersession evidence JSON.')};$priorRootText=if($priorAttempt.PSObject.Properties['CurrentContinuationRoot']){[string]$priorAttempt.CurrentContinuationRoot}elseif($priorAttempt.PSObject.Properties['PreviousAttemptRoot']){[string]$priorAttempt.PreviousAttemptRoot}else{'NOT_RECORDED'};$report.Add("Prior attempt evidence: $priorAttemptPath; continuation root: $priorRootText.");$report.Add('')}$report.Add("Run: ``$($script:RunId)``. Candidate: ``$($script:CandidateId)``. Source snapshot: ``$($script:SourceSnapshotId)``.");$report.Add('');$report.Add('The run uses the exact frozen 2.2 candidate, bounded correctness preflight, isolated C1/C2/C4 and supplemental BDN, followed by mixed L1/L2/L4/L8 only when the isolated core gate passes. Comparisons are descriptive; no SLA threshold or causal attribution is asserted.');$report.Add('');$report.Add('## 2. Performance candidate identity');$report.Add('');$report.Add("- Candidate manifest: ``$(Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Manifest.json')``");$report.Add("- Candidate manifest SHA-256: ``$candidateHash``");$report.Add("- Benchmark DLL SHA-256: ``$(if($candidateManifest){$candidateManifest.Binary.BenchmarkDll.SHA256}else{'NOT_CREATED'})``");$report.Add("- Data Access DLL SHA-256: ``$(if($candidateManifest){$candidateManifest.Binary.DataAccessDll.SHA256}else{'NOT_CREATED'})``");$report.Add("- Source inventory entries/hash: ``$(if($candidateManifest){$candidateManifest.Source.InventoryEntryCount}else{0})`` / ``$(if($candidateManifest){$candidateManifest.Source.InventorySHA256}else{'NOT_CREATED'})``");$report.Add("- Runtime inventory entries/hash: ``$(if($candidateManifest){$candidateManifest.Runtime.EntryCount}else{0})`` / ``$(if($candidateManifest){$candidateManifest.Runtime.InventorySHA256}else{'NOT_CREATED'})``");$report.Add("- Build logs: ``$(Join-Path $script:LogsRoot 'restore-a.log')``, ``$(Join-Path $script:LogsRoot 'build-a-release.log')``, ``$(Join-Path $script:LogsRoot 'restore-b.log')``, ``$(Join-Path $script:LogsRoot 'build-b-release.log')``.");$report.Add('');$report.Add('## 3. Batch 2 preservation');$report.Add('');$report.Add("Preservation status: **$($script:PreservationStatus)**. Proof: ``$(Join-Path $script:ReviewRoot 'Previous-Evidence-Preservation.json')``. PRE detail: ``$(Join-Path $script:LogsRoot 'preservation-pre-files.json')``; POST detail: ``$(Join-Path $script:LogsRoot 'preservation-post-files.json')``.");$report.Add('');$report.Add('## 4. Legacy workload contract');$report.Add('');$report.Add('Six legacy read scenarios use the Batch 2 Data Access path and unchanged 2.1 parameters: page 1, size 10; PERF_USER for authenticated methods; null warehouse; report dates 2025-01-01 through 2026-12-31; Historical mode LEGACY; historical current-balance switch false; current-balance switch true. No direct SQL replaced the timed Data Access path.');$report.Add('');$report.Add('## 5. 2.1 comparator identity');$report.Add('');$report.Add("Comparator status: **$($script:ComparatorStatus)**. Identity: ``$(Join-Path $script:ReviewRoot 'V21-Comparator-Identity.json')``. Single-window historical limitation retained.");$report.Add('');$report.Add('## 6. Environment/toolchain comparison');$report.Add('');$report.Add("Evidence: ``$(Join-Path $script:ReviewRoot 'V21-vs-V22-Toolchain-Comparison.json')``. Unverified values remain marked NOT_VERIFIED; comparison means source plus recorded environment.");$report.Add('');$report.Add('## 7. Correctness preflight');$report.Add('');$report.Add("Status: **$($script:PreflightStatus)**; scenarios passed: $($script:Preflight.SemanticScenariosCompleted)/6; Total_Count: $($script:Preflight.TotalCountPassed)/6. Evidence: ``$(Join-Path $script:ReviewRoot 'V22-Performance-Correctness-Preflight.json')``. Exact performance runtime was used.");$report.Add('');$report.Add('## 8. Isolated methodology and results');$report.Add('');$report.Add('Order per scenario: BDN, C1, C2, C4. NBomber KeepConstant; 3-second warmup; 15-second measured duration; no per-request retry. C1/C2 form 12 core rows; C4 forms six supplemental rows.');$report.Add('');$report.Add("Core valid rows: $(@($coreRows|Where-Object Status -eq 'PASS').Count)/12. C4 valid: $(@($c4Rows|Where-Object Status -eq 'PASS').Count)/6. Requests: $isolatedTotal; failed: $isolatedFailed. Isolated output: ``$(Join-Path $script:ReviewRoot 'V22-Isolated-Results.json')``.");$report.Add('');foreach($row in $script:IsolatedRows){$metricProjection=Get-V22MetricProjection $row ([string]$row.BlockKind);$report.Add("- $($row.BlockKind) $($row.Scenario) C$($row.Copies): $($row.Status); requests=$($row.Requests); failed=$($row.Failed); RPS=$(Get-V22MetricDisplayValue $metricProjection 'RPS'); mean/P95/P99 ms=$(Get-V22MetricDisplayValue $metricProjection 'MeanMs')/$(Get-V22MetricDisplayValue $metricProjection 'P95Ms')/$(Get-V22MetricDisplayValue $metricProjection 'P99Ms'); telemetry=$($row.TelemetryStatus); reason=$($row.Reason)")};$report.Add('');$report.Add('## 9. 2.1 versus 2.2 descriptive isolated comparison');$report.Add('');$report.Add("Evidence: ``$(Join-Path $script:ReviewRoot 'V21-vs-V22-Isolated-Comparison.json')``. Values are not labeled regression/improvement and do not establish cause.");$report.Add('');$report.Add('## 10. BDN results');$report.Add('');$report.Add("Valid scenarios: $(@($bdnRows|Where-Object Status -eq 'PASS').Count)/6. Statistical samples were read from WorkloadResult log lines paired with WorkloadActual iteration identities, then reconciled with N and the upper fence. Evidence: ``$(Join-Path $script:ReviewRoot 'V22-BDN-Results.json')``.");$report.Add('');$report.Add('## 11. Mixed methodology and results');$report.Add('');$report.Add('Six scenario-specific child processes run concurrently at each level. L1/L2/L4/L8 configure 1/2/4/8 copies per scenario (6/12/24/48 total). Each level requires six valid worker windows and at least 10 seconds common overlap. L8 is standalone mixed evidence. Aggregate RPS is the descriptive sum of scenario RPS, not a mathematically aligned common-window rate.');$report.Add('');foreach($levelResult in $script:MixedLevels){$report.Add("- $($levelResult.Level): $($levelResult.Status); total workers=$($levelResult.TotalWorkers); requests=$($levelResult.Requests); failed=$($levelResult.Failed); overlap=$($levelResult.WindowOverlapSeconds)s; telemetry=$($levelResult.TelemetryStatus); cleanup=$($levelResult.CleanupStatus)")};$report.Add('');$report.Add("Mixed comparison: ``$(Join-Path $script:ReviewRoot 'V22-Isolated-vs-Mixed-Comparison.json')``.");$report.Add('');$report.Add('## 12. Telemetry validity');$report.Add('');$report.Add("Status: $($telemetryOut.Status); valid target samples are required, and any error/rejected row invalidates a block. SQL logical-read/tempdb/deadlock counters are diagnostic only. Raw telemetry is under ``$($script:PerformanceRunRoot)``.");$report.Add('');$report.Add('## 13. Host admission and cooldown');$report.Add('');$report.Add("Host floor=512 MB; emergency floor=128 MB; safe predicted minimum >640 MB; two consecutive one-second admission samples; cooldown 5–60 seconds with clean target DB residue. Raw host sampling: ``$($script:HostLogPath)``.");$report.Add('');$report.Add('## 14. PRE/POST state');$report.Add('');$report.Add("Status: $($script:PrePostStatus). Evidence: ``$(Join-Path $script:ReviewRoot 'V22-Performance-PrePost-Evidence.json')``. FULL DATASET VALUE EQUALITY: NOT_VERIFIED.");$report.Add('');$report.Add('## 15. Cleanup and residue');$report.Add('');$report.Add("Status: $cleanupStatus. Evidence: ``$(Join-Path $script:ReviewRoot 'V22-Cleanup-Residue-Evidence.json')``. Child process IDs are owned-runner IDs only; SQL session attribution: NOT_VERIFIED.");$report.Add('');$report.Add('## 16. Known limitations');$report.Add('');$report.Add('Historical DocumentPaged/DetailReportPaged timeout cause: NOT VERIFIED. Full 10-million-row value equality: NOT_VERIFIED. Historical comparison is single-window. No SLA threshold, confidence interval, or causal attribution is asserted.');$report.Add('');$report.Add('## 17. Phase 8A evidence gate');$report.Add('');$report.Add("Overall: $($script:OverallStatus). Machine gate: ``$(Join-Path $script:ReviewRoot 'V22-Core-Evidence-Gate.json')``. Raw artifact hash index: ``$artifactIndexPath`` (SHA-256 ``$artifactIndexHash``).");$report.Add('');$report.Add('## 18. Findings and deferred work');$report.Add('');$report.Add("Stop reason: $(if($script:StopReason){$script:StopReason}else{'NONE'}). Stage failure: $(if($script:Failure){$script:Failure.Message}else{'NONE'}). Batch 4 extended CRUD/write, posting, reservation, worker, and read/write contention remain out of scope. No business source, SQL source, or DB data was modified by this task.");$report.Add('');$report.Add('## 19. Final verdict and Batch 4 readiness');$report.Add('');$report.Add("- Verdict: **$($script:OverallStatus)**");$report.Add("- LEGACY_REGRESSION_MEASUREMENT_VALID: **$(if($isolatedCorePass -and $c4Pass -and $isolatedTelemetry -and $postPass){'YES'}else{'NO'})**");$report.Add("- MIXED_REGRESSION_MEASUREMENT_VALID: **$(if($mixedPass){'YES'}else{'NO'})**");$report.Add("- CORE_EVIDENCE_READY: **$(if($script:OverallStatus -eq 'BATCH3_PASS'){'YES'}else{'NO'})**");$report.Add("- READY_FOR_BATCH4: **$(if($script:OverallStatus -eq 'BATCH3_PASS'){'YES'}else{'NO'})**");$report.Add('');$report.Add("Failure evidence, if any: ``$(Join-Path $script:PerformanceRunRoot 'fatal-summary.json')``.")
    $mixedTestStatus=if($mixedHarnessOfflinePass){'PASS'}elseif($null -ne $mixedTestResult){'FAIL'}else{'NOT_RUN'}
    $mixedTestPassed=if($null -ne $mixedTestResult){[string]$mixedTestResult.Passed}else{'0'}
    $mixedTestCount=if($null -ne $mixedTestResult){[string]$mixedTestResult.TestCount}else{'0'}
    $mixedTestFailed=if($null -ne $mixedTestResult){[string]$mixedTestResult.Failed}else{'0'}
    $mixedTestEvidence=if($script:MixedHarnessTestsPath){$script:MixedHarnessTestsPath}else{'NOT_CREATED'}
    $report.Add('## Mixed harness offline regression evidence');$report.Add('');$report.Add(('Status: {0}; passed: {1}/{2}; failed: {3}.' -f $mixedTestStatus,$mixedTestPassed,$mixedTestCount,$mixedTestFailed));$report.Add(('Evidence: {0}' -f $mixedTestEvidence));$report.Add('Database access: NOT_USED; performance workload: NOT_RUN by this test.');$report.Add('')
    $reportPath=Join-Path $script:ReviewRoot 'Batch3-Implementation-Report.md';if(-not(Test-Path $reportPath)){try{Write-NewText $reportPath ($report -join [Environment]::NewLine)}catch{}}
    $indexPath=Join-Path $script:ReviewRoot 'Batch3-Review-Index.md'
    if(Test-Path $indexPath){$indexBackup=Join-Path $script:PerformanceRunRoot 'Batch3-Review-Index-preexisting.txt';try{Copy-Item -LiteralPath $indexPath -Destination $indexBackup}catch{}}else{
        $index=[Collections.Generic.List[string]]::new();$index.Add('# Batch 3 Review Index');$index.Add('');$index.Add("Verdict: **$($script:OverallStatus)**");$index.Add("Candidate ID: ``$($script:CandidateId)``");$index.Add("Source snapshot: ``$($script:SourceSnapshotId)``");$index.Add("Frozen runtime: ``$(if($candidateManifest){$candidateManifest.Runtime.Root}else{'NOT_CREATED'})``");$index.Add('');$index.Add('## Read first');$index.Add('');$index.Add('1. Batch3-Implementation-Report.md');$index.Add('2. Batch3-Acceptance.json');$index.Add('3. V22-Core-Evidence-Gate.json');$index.Add('4. V22-Performance-Candidate-Manifest.json and V22-Performance-Candidate-Guard.json');$index.Add('5. V22-Performance-Correctness-Preflight.json and V22-Performance-PrePost-Evidence.json');$index.Add('6. Isolated/Mixed/BDN results and comparator identities');$index.Add('');$index.Add('## Review artifacts and SHA-256');$index.Add('');foreach($name in $requestFiles|Where-Object{$_ -ne 'Batch3-Review-Index.md'}){$path=Join-Path $script:ReviewRoot $name;$hash=if(Test-Path $path){Get-FileHashHex $path}else{'MISSING'};$index.Add("- ``$name`` — ``$hash``")};$index.Add('');$index.Add('## Build and raw logs');$index.Add('');foreach($name in @('dotnet-info.log','msbuild-version.log','restore-a.log','build-a-release.log','restore-b.log','build-b-release.log','runtime-freeze.log','candidate-guard.log')){$index.Add("- ``$(Join-Path $script:LogsRoot $name)``")};$index.Add("- Raw run artifacts: ``$($script:PerformanceRunRoot)``");$index.Add("- Raw artifact index SHA-256: ``$artifactIndexHash``");$index.Add('');$index.Add('ZIP contains the required review artifacts plus prior-attempt supersession evidence; frozen runtime binaries are excluded.');Write-NewText $indexPath ($index -join [Environment]::NewLine)
    }
    try{
        $projectionMap=[ordered]@{
            'Batch3-Final-Changed-Files.txt'=(Join-Path $script:ReviewRoot 'Batch3-Changed-Files.txt')
            'Previous-Evidence-Preservation.json'=(Join-Path $script:ReviewRoot 'Previous-Evidence-Preservation.json')
            'V22-Canonical-Run-Identity.json'=(Join-Path $script:ReviewRoot 'V22-Canonical-Run-Identity.json')
            'V22-Final-Source-Inventory.json'=(Join-Path $script:ReviewRoot 'V22-Performance-Source-Inventory.json')
            'V22-Final-Source-Snapshot.json'=(Join-Path $script:ReviewRoot 'V22-Performance-Source-Snapshot.json')
            'V22-Final-Build-Attestation.json'=(Join-Path $script:ReviewRoot 'V22-Performance-Build-Attestation.json')
            'V22-Final-Runtime-Inventory.json'=(Join-Path $script:ReviewRoot 'V22-Performance-Runtime-Inventory.json')
            'V22-Final-Candidate-Manifest.json'=(Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Manifest.json')
            'V22-Final-Candidate-Guard.json'=(Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Guard.json')
            'V22-Final-Correctness-Preflight.json'=(Join-Path $script:ReviewRoot 'V22-Performance-Correctness-Preflight.json')
            'V22-Final-Isolated-Run-Manifest.json'=(Join-Path $script:ReviewRoot 'V22-Isolated-Run-Manifest.json')
            'V22-Final-Isolated-Results.json'=(Join-Path $script:ReviewRoot 'V22-Isolated-Results.json')
            'V22-Final-BDN-Results.json'=(Join-Path $script:ReviewRoot 'V22-BDN-Results.json')
            'V22-Final-Isolated-Telemetry.json'=(Join-Path $script:ReviewRoot 'V22-Isolated-Telemetry-Summary.json')
            'V22-Final-Post-Isolated-Correctness.json'=(Join-Path $script:ReviewRoot 'V22-Post-Isolated-Correctness.json')
            'V22-Final-Mixed-Run-Manifest.json'=(Join-Path $script:ReviewRoot 'V22-Mixed-Run-Manifest.json')
            'V22-Final-Mixed-Results.json'=(Join-Path $script:ReviewRoot 'V22-Mixed-Results.json')
            'V22-Final-Mixed-Harness-Tests.json'=(Join-Path $script:ReviewRoot 'V22-Mixed-Harness-Tests.json')
            'V22-Final-Mixed-Telemetry.json'=(Join-Path $script:ReviewRoot 'V22-Mixed-Telemetry-Summary.json')
            'V22-Final-Post-Mixed-Correctness.json'=(Join-Path $script:ReviewRoot 'V22-Post-Mixed-Correctness.json')
            'V22-Final-Owned-Process-Inventory.json'=(Join-Path $script:ReviewRoot 'V22-Final-Owned-Process-Inventory.json')
            'V22-Final-PerPid-Process-Probes.json'=(Join-Path $script:ReviewRoot 'V22-Final-PerPid-Process-Probes.json')
            'V22-Final-Telemetry-Process-Probes.json'=(Join-Path $script:ReviewRoot 'V22-Final-Telemetry-Process-Probes.json')
            'V22-Final-Aggregate-Cleanup.json'=(Join-Path $script:ReviewRoot 'V22-Final-Aggregate-Cleanup.json')
            'V22-Final-Cleanup-Persistence-Verification.json'=(Join-Path $script:ReviewRoot 'V22-Final-Cleanup-Persistence-Verification.json')
            'V22-Final-Sql-Residue-Probe.json'=(Join-Path $script:ReviewRoot 'V22-Final-Sql-Residue-Probe.json')
            'V22-Final-Performance-PrePost.json'=(Join-Path $script:ReviewRoot 'V22-Performance-PrePost-Evidence.json')
            'V21-vs-V22-Final-Isolated-Comparison.json'=(Join-Path $script:ReviewRoot 'V21-vs-V22-Isolated-Comparison.json')
            'V22-Final-Isolated-vs-Mixed-Comparison.json'=(Join-Path $script:ReviewRoot 'V22-Isolated-vs-Mixed-Comparison.json')
            'V22-Final-Raw-Artifact-Hashes.json'=$artifactIndexPath
            'V22-Canonical-Workload-One-Shot-Claim.json'=$script:OneShotClaimPath
            'V22-Canonical-Workload-Start.json'=$script:OneShotStartPath
        }
        $projectionRows=[Collections.Generic.List[object]]::new()
        foreach($name in $projectionMap.Keys){$projectionRows.Add((New-V22ReviewProjectionArtifact $script:FinalReviewRoot $name $projectionMap[$name] $script:CanonicalRunId $script:CandidateId))}
        $storageAdmissionPath=Join-Path $script:FinalReviewRoot 'V22-Final-Rerun-Storage-Admission.json'
        if(-not(Test-Path -LiteralPath $storageAdmissionPath -PathType Leaf)){$projectionRows.Add((New-V22ReviewProjectionArtifact $script:FinalReviewRoot 'V22-Final-Rerun-Storage-Admission.json' $null $script:CanonicalRunId $script:CandidateId))}
        $storageFinal=Read-OptionalJson $storageAdmissionPath
        $rawIntegrityPath=Join-Path $script:FinalReviewRoot 'V22-Final-Raw-Artifact-Integrity.json'
        New-V22FinalizerJson $rawIntegrityPath $rawArtifactIntegrity
        $historicalTimestamp='NOT_VERIFIED'
        $priorSupersession=Read-OptionalJson (Join-Path $script:PriorReviewRoot 'V22-Prior-Attempt-Supersession.json')
        if($null -ne $priorSupersession){$timestampField=Get-V22FinalizerField $priorSupersession 'HistoricalHostLimitActionTimestamp';if($timestampField.Present -and [string]$timestampField.Value -in @('NOT_RECORDED','NOT_VERIFIED')){$historicalTimestamp=[string]$timestampField.Value}}
        $finalLimitations=[ordered]@{SchemaVersion='warehouse-benchmark-v22-final-known-limitations/1';CanonicalRunId=$script:CanonicalRunId;CandidateId=$script:CandidateId;HistoricalTimeoutCause='NOT_VERIFIED';FullDatasetValueEquality='NOT_VERIFIED';PerformanceSLA='NO_SLA_DEFINED';CausalAttribution='NOT_ESTABLISHED';HistoricalHostLimitActionTimestamp=$historicalTimestamp;HistoricalHostLimitClassification='HOST_LIMIT_HARD_FLOOR_TRIGGERED';HistoricalPrecision='Do not infer an action-completion timestamp absent persisted evidence';Present=$true}
        New-V22FinalizerJson (Join-Path $script:FinalReviewRoot 'V22-Final-Known-Limitations.json') $finalLimitations
        $baseRequired=@($projectionMap.Keys)+@('V22-Final-Rerun-Storage-Admission.json','V22-Final-Raw-Artifact-Integrity.json','V22-Final-Known-Limitations.json')
        $baseIntegrity=Test-V22FinalCanonicalReviewPack $script:FinalReviewRoot $baseRequired $script:CanonicalRunId $script:CandidateId
        $mixedLevelStatus=@{};foreach($levelName in @('L1','L2','L4','L8')){$levelRecord=$script:MixedLevels|Where-Object Level -CEQ $levelName|Select-Object -Last 1;$mixedLevelStatus[$levelName]=if($levelRecord){[string]$levelRecord.Status}else{'NOT_RUN'}}
        $mixedResultGates=@(foreach($mixedRecord in $script:MixedLevels){Get-V22MixedResultGate $mixedRecord $script:ProtocolInfo.MinimumMixedCommonOverlapSeconds})
        $mixedOverlapValidationPass=($script:MixedLevels.Count -eq 4 -and $mixedResultGates.Count -eq 4 -and @($mixedResultGates|Where-Object{$_.SchemaStatus -cne 'PASS' -or $_.MeasurementStatus -cne 'PASS' -or $_.OverlapStatus -cne 'PASS'}).Count -eq 0)
        $script:MixedOverlapStatus=Get-V22MixedOverlapStatus $script:MixedLevels.Count $mixedOverlapValidationPass
        $mixedTelemetryPass=($script:MixedLevels.Count -eq 4 -and @($script:MixedLevels|Where-Object TelemetryStatus -ne 'TELEMETRY_VALID').Count -eq 0)
        $preflightSemantic=if($script:Preflight){[string]$script:Preflight.SemanticScenarios}else{'0/6'}
        $preflightTotal=if($script:Preflight){[string]$script:Preflight.TotalCount}else{'0/6'}
        $postIsolatedText=if($script:AfterIsolated){[string]$script:AfterIsolated.SemanticScenarios}else{'0/6'}
        $postMixedText=if($script:AfterMixed){[string]$script:AfterMixed.SemanticScenarios}else{'0/6'}
        $isolatedC12Text=('{0}/12' -f @($coreRows|Where-Object Status -eq 'PASS').Count)
        $c4Text=('{0}/6' -f @($c4Rows|Where-Object Status -eq 'PASS').Count)
        $bdnText=('{0}/6' -f @($bdnRows|Where-Object Status -eq 'PASS').Count)
        $cleanupReadbackStatus=if($cleanupPersistencePass){'PASS'}else{'FAIL'}
        $storagePass=($script:StorageAdmissionStatus -ceq 'PASS' -and $storageFinal -and $storageFinal.Status -ceq 'PASS')
        $candidateManifestPath=Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Manifest.json'
        $candidateManifestFinal=Read-OptionalJson $candidateManifestPath
        $finalIdentityStatus=if($script:CanonicalRunIdentityStatus -ceq 'PASS' -and (Test-Path $script:OneShotStartPath)){'PASS'}else{'FAIL'}
        $finalGates=[ordered]@{
            Storage=if($storagePass){'PASS'}else{'FAIL'}
            Candidate=if($candidatePass){'PASS'}else{'FAIL'}
            CanonicalRunIdentity=$finalIdentityStatus
            Preflight=if($correctnessPass -and $preflightSemantic -ceq '6/6'){'PASS'}else{'FAIL'}
            TotalCount=if($preflightTotal -ceq '6/6'){'PASS'}else{'FAIL'}
            IsolatedC1C2=if($isolatedCorePass){'PASS'}else{'FAIL'}
            C4=if($c4Pass){'PASS'}else{'FAIL'}
            BDN=if($bdnPass){'PASS'}else{'FAIL'}
            PostIsolatedCorrectness=if($postIsolatedPass){'PASS'}else{'FAIL'}
            MixedL1=$mixedLevelStatus.L1
            MixedL2=$mixedLevelStatus.L2
            MixedL4=$mixedLevelStatus.L4
            MixedL8=$mixedLevelStatus.L8
            MixedHarnessOffline=if($mixedHarnessOfflinePass){'PASS'}else{'FAIL'}
            MixedOverlap=$script:MixedOverlapStatus
            MixedTelemetry=if($mixedTelemetryPass){'PASS'}else{'FAIL'}
            PostMixedCorrectness=if($postMixedPass){'PASS'}else{'FAIL'}
            AggregateCleanup=if($allCleanup){'PASS'}else{'FAIL'}
            CleanupPersistence=$cleanupReadbackStatus
            BoundedDbPrePost=if($postPass -and $script:PrePostStatus -ceq 'PASS'){'PASS'}else{'FAIL'}
            CandidateGuard=if($script:CandidateGuardFinalStatus -ceq 'PASS'){'PASS'}else{'FAIL'}
            Preservation=if($preservationPass){'PASS'}else{'FAIL'}
            RawArtifactIntegrity=[string]$rawArtifactIntegrity.Status
            KnownLimitations='PRESENT'
            ReportLint='NOT_RUN'
            ReviewPackIntegrity=[string]$baseIntegrity.Status
        }
        $allFinalGatesPass=($finalGates.Storage -ceq 'PASS' -and $finalGates.Candidate -ceq 'PASS' -and $finalGates.CanonicalRunIdentity -ceq 'PASS' -and $finalGates.Preflight -ceq 'PASS' -and $finalGates.TotalCount -ceq 'PASS' -and $finalGates.IsolatedC1C2 -ceq 'PASS' -and $finalGates.C4 -ceq 'PASS' -and $finalGates.BDN -ceq 'PASS' -and $finalGates.PostIsolatedCorrectness -ceq 'PASS' -and $finalGates.MixedL1 -ceq 'PASS' -and $finalGates.MixedL2 -ceq 'PASS' -and $finalGates.MixedL4 -ceq 'PASS' -and $finalGates.MixedL8 -ceq 'PASS' -and $finalGates.MixedHarnessOffline -ceq 'PASS' -and $finalGates.MixedOverlap -ceq 'PASS' -and $finalGates.MixedTelemetry -ceq 'PASS' -and $finalGates.PostMixedCorrectness -ceq 'PASS' -and $finalGates.AggregateCleanup -ceq 'PASS' -and $finalGates.CleanupPersistence -ceq 'PASS' -and $finalGates.BoundedDbPrePost -ceq 'PASS' -and $finalGates.CandidateGuard -ceq 'PASS' -and $finalGates.Preservation -ceq 'PASS' -and $finalGates.RawArtifactIntegrity -ceq 'PASS' -and $finalGates.KnownLimitations -ceq 'PRESENT' -and $finalGates.ReviewPackIntegrity -ceq 'PASS' -and $script:ComparatorStatus -ceq 'PASS' -and $script:CanonicalWorkloadStarted)
        $tentativeVerdict=if($allFinalGatesPass){'BATCH3_PASS'}elseif($script:CanonicalWorkloadStarted){'BATCH3_PARTIAL'}else{'BATCH3_NOT_READY'}
        $finalValues=[ordered]@{
            Verdict=$tentativeVerdict;CanonicalRunId=$script:CanonicalRunId;ProtocolRunId=$script:ProtocolInfo.RunId;IdentityStatus=$finalIdentityStatus;CandidateId=$script:CandidateId;SourceSnapshotId=$script:SourceSnapshotId;ManifestSHA256=if($candidateManifestFinal){$candidateHash}else{'NOT_CREATED'}
            BenchmarkDllSHA256=if($candidateManifestFinal){$candidateManifestFinal.Binary.BenchmarkDll.SHA256}else{'NOT_CREATED'}
            DataAccessDllSHA256=if($candidateManifestFinal){$candidateManifestFinal.Binary.DataAccessDll.SHA256}else{'NOT_CREATED'}
            SourceInventoryCount=if($candidateManifestFinal){$candidateManifestFinal.Source.InventoryEntryCount}else{0}
            SourceInventorySHA256=if($candidateManifestFinal){$candidateManifestFinal.Source.InventorySHA256}else{'NOT_CREATED'}
            RuntimeInventoryCount=if($candidateManifestFinal){$candidateManifestFinal.Runtime.EntryCount}else{0}
            RuntimeInventorySHA256=if($candidateManifestFinal){$candidateManifestFinal.Runtime.InventorySHA256}else{'NOT_CREATED'}
            Gates=$finalGates;CorrectnessPreflight=$preflightSemantic;TotalCountPreflight=$preflightTotal;PostIsolatedCorrectness=$postIsolatedText;PostMixedCorrectness=$postMixedText
            IsolatedC1C2=$isolatedC12Text;C4=$c4Text;BDN=$bdnText;MixedL1=$mixedLevelStatus.L1;MixedL2=$mixedLevelStatus.L2;MixedL4=$mixedLevelStatus.L4;MixedL8=$mixedLevelStatus.L8;MixedOverlap=$finalGates.MixedOverlap;MixedTelemetry=$finalGates.MixedTelemetry
            IsolatedRequests=$isolatedTotal;IsolatedFailed=$isolatedFailed;MixedFailed=$mixedFailed;ProcessCount=if($script:FinalCleanupEvidence){$script:FinalCleanupEvidence.ProcessCount}else{$script:AllOwnedProcesses.Count}
            PidProbeCount=if($script:FinalCleanupEvidence){$script:FinalCleanupEvidence.PidProbeCount}else{0};HelperProbeCount=if($script:FinalCleanupEvidence){$script:FinalCleanupEvidence.HelperProbeCount}else{0};CleanupStatus=$cleanupStatus;PrePostStatus=$script:PrePostStatus;PreservationStatus=$script:PreservationStatus
            RawArtifactCount=$rawArtifactIntegrity.VerifiedCount;RawArtifactIndexSHA256=$artifactIndexHash;CanonicalWorkloadAttempts=if($script:CanonicalWorkloadStarted){1}else{0}
            HistoricalTimeoutCause='NOT_VERIFIED';FullDatasetValueEquality='NOT_VERIFIED';PerformanceSLA='NO_SLA_DEFINED';CausalAttribution='NOT_ESTABLISHED';HistoricalHostLimitActionTimestamp=$historicalTimestamp
            CoreEvidenceReady=if($tentativeVerdict -eq 'BATCH3_PASS'){'YES'}else{'NO'};ReadyForBatch4=if($tentativeVerdict -eq 'BATCH3_PASS'){'YES'}else{'NO'}
            StopReason=if($script:StopReason){$script:StopReason}else{'NONE'};FailureStage=if($script:Failure){$script:Failure.Stage}else{'NONE'}
            EvidencePaths=@($script:FinalReviewRoot,(Join-Path $script:PerformanceRunRoot 'raw-artifact-hashes.json'),$cleanupReadbackPath,(Join-Path $script:FinalReviewRoot 'V22-Final-Core-Evidence-Gate.json'),(Join-Path $script:FinalReviewRoot 'V21-vs-V22-Final-Isolated-Comparison.json'),(Join-Path $script:FinalReviewRoot 'V22-Final-Isolated-vs-Mixed-Comparison.json'))
        }
        $finalValues['RecoveryDiagnosticStatus']=$script:RecoveryDiagnosticStatus
        $draftReport=New-V22FinalReport $finalValues;$draftLint=Test-V22FinalReportLint $draftReport
        $finalGates.ReportLint=[string]$draftLint.Status
        if($draftLint.Status -ne 'PASS'){$allFinalGatesPass=$false;$finalValues.Verdict=if($script:CanonicalWorkloadStarted){'FINAL_REPORT_INVALID'}else{'BATCH3_NOT_READY'};$script:StopReason='FINAL_REPORT_INVALID'}
        $script:OverallStatus=if($allFinalGatesPass -and $draftLint.Status -ceq 'PASS'){'BATCH3_PASS'}elseif($script:CanonicalWorkloadStarted){'BATCH3_PARTIAL'}else{'BATCH3_NOT_READY'}
        $finalValues.Verdict=$script:OverallStatus;$finalValues.CoreEvidenceReady=if($script:OverallStatus -eq 'BATCH3_PASS'){'YES'}else{'NO'};$finalValues.ReadyForBatch4=$finalValues.CoreEvidenceReady;$finalValues.StopReason=if($script:StopReason){$script:StopReason}else{'NONE'}
        $finalCoreGate=[ordered]@{SchemaVersion='warehouse-benchmark-v22-final-core-evidence-gate/1';CanonicalRunId=$script:CanonicalRunId;ProtocolRunId=$script:ProtocolInfo.RunId;IdentityStatus=$finalIdentityStatus;CandidateId=$script:CandidateId;SourceSnapshotId=$script:SourceSnapshotId;Gates=$finalGates;CorrectnessPreflight=$preflightSemantic;TotalCount=$preflightTotal;PostIsolatedCorrectness=$postIsolatedText;PostMixedCorrectness=$postMixedText;CanonicalWorkloadAttempts=if($script:CanonicalWorkloadStarted){1}else{0};OverallStatus=$script:OverallStatus;CoreEvidenceReady=($script:OverallStatus -eq 'BATCH3_PASS');ReadyForBatch4=($script:OverallStatus -eq 'BATCH3_PASS');RecordedUtc=[DateTime]::UtcNow.ToString('o')}
        $finalCoreGate['RecoveryDiagnosticStatus']=$script:RecoveryDiagnosticStatus
        New-V22FinalizerJson (Join-Path $script:FinalReviewRoot 'V22-Final-Core-Evidence-Gate.json') $finalCoreGate
        $finalReport=New-V22FinalReport $finalValues
        $finalLint=Test-V22FinalReportLint $finalReport
        $finalGates.ReportLint=[string]$finalLint.Status
        if($finalLint.Status -ne 'PASS'){$script:OverallStatus=if($script:CanonicalWorkloadStarted){'FINAL_REPORT_INVALID'}else{'BATCH3_NOT_READY'};$script:StopReason='FINAL_REPORT_INVALID';$finalValues.Verdict=$script:OverallStatus;$finalValues.CoreEvidenceReady='NO';$finalValues.ReadyForBatch4='NO';$finalReport=New-V22FinalReport $finalValues;$finalLint=Test-V22FinalReportLint $finalReport}
        Write-NewText (Join-Path $script:FinalReviewRoot 'Batch3-Final-Canonical-Report.md') $finalReport
        New-V22FinalizerJson (Join-Path $script:FinalReviewRoot 'V22-Report-Template-Lint.json') $finalLint
        $finalAcceptance=[ordered]@{SchemaVersion='warehouse-benchmark-v22-final-canonical-acceptance/1';OverallStatus=$script:OverallStatus;CanonicalRunId=$script:CanonicalRunId;ProtocolRunId=$script:ProtocolInfo.RunId;CanonicalRunIdentityStatus=$finalIdentityStatus;CandidateId=$script:CandidateId;SourceSnapshotId=$script:SourceSnapshotId;CanonicalWorkloadAttempts=if($script:CanonicalWorkloadStarted){1}else{0};AdditionalWorkloadReruns=0;AutomaticRetries=0;Gates=$finalGates;Counts=[ordered]@{CorrectnessPreflight=$preflightSemantic;TotalCount=$preflightTotal;PostIsolatedCorrectness=$postIsolatedText;PostMixedCorrectness=$postMixedText;IsolatedC1C2=$isolatedC12Text;C4=$c4Text;BDN=$bdnText;RawArtifacts=$rawArtifactIntegrity.VerifiedCount};CoreEvidenceReady=($script:OverallStatus -eq 'BATCH3_PASS');ReadyForBatch4=($script:OverallStatus -eq 'BATCH3_PASS');KnownLimitations=$finalLimitations;EvidencePaths=@($script:FinalReviewRoot,$script:PerformanceRunRoot);RecordedUtc=[DateTime]::UtcNow.ToString('o')}
        $finalAcceptance['RecoveryDiagnosticStatus']=$script:RecoveryDiagnosticStatus
        New-V22FinalizerJson (Join-Path $script:FinalReviewRoot 'Batch3-Final-Canonical-Acceptance.json') $finalAcceptance
        $finalIndexPath=Join-Path $script:FinalReviewRoot 'Batch3-Final-Canonical-Review-Index.md'
        $indexEvidenceFiles=@(Get-ChildItem -LiteralPath $script:FinalReviewRoot -File -Force|Where-Object Name -ne 'Batch3-Final-Canonical-Review-Index.md'|Sort-Object Name)
        $indexLines=[Collections.Generic.List[string]]::new();$indexLines.Add('# Batch 3 Final Canonical Review Index');$indexLines.Add('');$indexLines.Add(('Verdict: '+$script:OverallStatus));$indexLines.Add(('Canonical Run ID: '+$script:CanonicalRunId));$indexLines.Add(('Protocol Run ID: '+$script:ProtocolInfo.RunId));$indexLines.Add(('Candidate ID: '+[string]$script:CandidateId));$indexLines.Add(('Source Snapshot ID: '+[string]$script:SourceSnapshotId));$indexLines.Add(('Canonical workload attempts: '+[string]$(if($script:CanonicalWorkloadStarted){1}else{0})));$indexLines.Add('');$indexLines.Add('## Read first');$indexLines.Add('');$indexLines.Add('1. Batch3-Final-Canonical-Report.md');$indexLines.Add('2. Batch3-Final-Canonical-Acceptance.json');$indexLines.Add('3. V22-Final-Core-Evidence-Gate.json');$indexLines.Add('4. V22-Final-Aggregate-Cleanup.json and V22-Final-Cleanup-Persistence-Verification.json');$indexLines.Add('5. V22-Final-Correctness-Preflight.json, V22-Final-Post-Isolated-Correctness.json, and V22-Final-Post-Mixed-Correctness.json');$indexLines.Add('6. Isolated, BDN, mixed, telemetry, and comparison evidence');$indexLines.Add('');$indexLines.Add('## Files and SHA-256');$indexLines.Add('')
        foreach($entry in $indexEvidenceFiles){$indexLines.Add(('- {0} - {1}' -f $entry.Name,(Get-FileHashHex $entry.FullName)))}
        $indexLines.Add('');$indexLines.Add(('Index self-hash and every pack-file hash: '+$script:FinalReviewHashesPath));$indexLines.Add(('Canonical raw artifacts: '+$script:PerformanceRunRoot));$indexLines.Add(('Candidate runtime: '+[string]$(if($candidateManifestFinal){$candidateManifestFinal.Runtime.Root}else{'NOT_CREATED'})))
        Write-NewText $finalIndexPath ($indexLines -join [Environment]::NewLine)
        $finalRequiredFiles=@('Batch3-Final-Canonical-Report.md','Batch3-Final-Canonical-Acceptance.json','Batch3-Final-Changed-Files.txt','Previous-Evidence-Preservation.json','V22-Final-Rerun-Storage-Admission.json','V22-Canonical-Run-Identity.json','V22-Final-Source-Inventory.json','V22-Final-Source-Snapshot.json','V22-Final-Build-Attestation.json','V22-Final-Runtime-Inventory.json','V22-Final-Candidate-Manifest.json','V22-Final-Candidate-Guard.json','V22-Final-Correctness-Preflight.json','V22-Final-Isolated-Run-Manifest.json','V22-Final-Isolated-Results.json','V22-Final-BDN-Results.json','V22-Final-Isolated-Telemetry.json','V22-Final-Post-Isolated-Correctness.json','V22-Final-Mixed-Run-Manifest.json','V22-Final-Mixed-Results.json','V22-Final-Mixed-Telemetry.json','V22-Final-Post-Mixed-Correctness.json','V22-Final-Owned-Process-Inventory.json','V22-Final-PerPid-Process-Probes.json','V22-Final-Telemetry-Process-Probes.json','V22-Final-Aggregate-Cleanup.json','V22-Final-Performance-PrePost.json','V21-vs-V22-Final-Isolated-Comparison.json','V22-Final-Isolated-vs-Mixed-Comparison.json','V22-Final-Known-Limitations.json','V22-Final-Core-Evidence-Gate.json','V22-Report-Template-Lint.json','Batch3-Final-Canonical-Review-Index.md','V22-Final-Cleanup-Persistence-Verification.json','V22-Final-Sql-Residue-Probe.json','V22-Final-Raw-Artifact-Hashes.json','V22-Final-Raw-Artifact-Integrity.json','V22-Canonical-Workload-One-Shot-Claim.json','V22-Canonical-Workload-Start.json')
        $finalPackIntegrity=Test-V22FinalCanonicalReviewPack $script:FinalReviewRoot $finalRequiredFiles $script:CanonicalRunId $script:CandidateId
        if($finalPackIntegrity.Status -cne $baseIntegrity.Status){$script:StopReason='FINAL_REVIEW_PACK_INTEGRITY_CHANGED';if(-not $script:Failure){$script:Failure=[ordered]@{Stage='FINAL_REVIEW_PACK_INTEGRITY';Message=('Pre-projection='+$baseIntegrity.Status+'; final='+$finalPackIntegrity.Status);RecordedUtc=[DateTime]::UtcNow.ToString('o')}};if($script:OverallStatus -eq 'BATCH3_PASS'){$script:OverallStatus='BATCH3_PARTIAL'}}
        $externalHashEntries=$finalPackIntegrity.Entries
        if(Test-Path -LiteralPath $script:FinalReviewHashesPath){throw 'FINAL_REVIEW_HASH_MANIFEST_EXISTS; refusing overwrite'}
        New-V22FinalizerJson $script:FinalReviewHashesPath ([ordered]@{SchemaVersion='warehouse-benchmark-v22-final-review-pack-hashes/1';Root=$script:FinalReviewRoot;Status=$finalPackIntegrity.Status;FileCount=$finalPackIntegrity.FileCount;Entries=$externalHashEntries;ZipPath=$script:FinalReviewZipPath;RecordedUtc=[DateTime]::UtcNow.ToString('o')})
        if(Test-Path -LiteralPath $script:FinalReviewZipPath){throw 'FINAL_REVIEW_ZIP_EXISTS; refusing overwrite'}
        $zipSources=@(Get-ChildItem -LiteralPath $script:FinalReviewRoot -File -Force|Sort-Object Name|ForEach-Object FullName)
        Compress-Archive -LiteralPath $zipSources -DestinationPath $script:FinalReviewZipPath -CompressionLevel Optimal
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive=[IO.Compression.ZipFile]::OpenRead($script:FinalReviewZipPath)
        try{$zipEntries=@($archive.Entries);$expectedNames=@(Get-ChildItem -LiteralPath $script:FinalReviewRoot -File -Force|ForEach-Object Name|Sort-Object);$actualNames=@($zipEntries|ForEach-Object FullName|Sort-Object);if(@(Compare-Object -ReferenceObject $expectedNames -DifferenceObject $actualNames -CaseSensitive).Count -ne 0){throw 'FINAL_REVIEW_ZIP_ENTRY_SET_MISMATCH'};foreach($entry in $zipEntries){$expected=$finalPackIntegrity.Entries|Where-Object RelativePath -CEQ $entry.FullName|Select-Object -First 1;if($null -eq $expected -or [long]$expected.Size -ne [long]$entry.Length){throw 'FINAL_REVIEW_ZIP_ENTRY_SIZE_MISMATCH'};$stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create();try{$entryHash=[Convert]::ToHexString($sha.ComputeHash($stream)).ToLowerInvariant()}finally{$stream.Dispose();$sha.Dispose()};if($entryHash -cne [string]$expected.SHA256){throw 'FINAL_REVIEW_ZIP_ENTRY_HASH_MISMATCH'}}}finally{$archive.Dispose()}
        $zipHash=Get-FileHashHex $script:FinalReviewZipPath
        if(Test-Path -LiteralPath $script:FinalReviewZipHashPath){throw 'FINAL_REVIEW_ZIP_HASH_EXISTS; refusing overwrite'}
        Write-NewText $script:FinalReviewZipHashPath ($zipHash+'  Batch3-Final-Canonical-Review-Pack.zip'+[Environment]::NewLine)
        $hashManifest=Get-Content -LiteralPath $script:FinalReviewHashesPath -Raw|ConvertFrom-Json
        $hashManifest.ZipSHA256=$zipHash
        $hashManifest.ZipStatus='PASS'
        $hashManifest|ConvertTo-Json -Depth 20|Set-Content -LiteralPath (Join-Path $script:LogsRoot ('final-review-zip-verification-'+$script:RunId+'.json')) -Encoding utf8
    }catch{
        $script:FinalReviewPackStatus='FAIL'
        if($script:CanonicalWorkloadStarted -and $script:OverallStatus -eq 'BATCH3_PASS'){$script:OverallStatus='BATCH3_PARTIAL'}
        if(-not $script:StopReason){$script:StopReason='FINAL_REVIEW_PACK_OR_ZIP_FAILED'}
        $safePackFailure=Get-SafeText $_.Exception.Message
        $packFailurePath=Join-Path $script:LogsRoot ('final-review-pack-failure-'+$script:RunId+'.json')
        if(-not(Test-Path -LiteralPath $packFailurePath)){try{Write-NewJson $packFailurePath ([ordered]@{Status='FAIL';CanonicalRunId=$script:CanonicalRunId;CandidateId=$script:CandidateId;Stage='FINAL_REVIEW_PACK';Failure=$safePackFailure;RecordedUtc=[DateTime]::UtcNow.ToString('o')})}catch{}}
    }
    if(Test-Path (Join-Path $script:PerformanceRunRoot 'progress.json')){Write-RunProgress 'COMPLETE' $script:OverallStatus $(if($script:Failure){$script:Failure.Message}else{$script:StopReason})}    }
  }catch{
    $script:FinalizerFailure=[ordered]@{Stage=$script:FinalizerStage;Message=Get-SafeText $_.Exception.Message;SafeExceptionType=$_.Exception.GetType().FullName;RecordedUtc=[DateTime]::UtcNow.ToString('o')}
    New-V22FinalizerJson (Join-Path $script:FailureRoot 'Finalizer-Failure.json') $script:FinalizerFailure
    [void](Save-AbortDisposition)
  }
}

if($null -ne $script:OriginalFailure -or $null -ne $script:FinalizerFailure){
    Get-Content -LiteralPath (Join-Path $script:FailureRoot 'Abort-Acceptance.json') -Raw
    exit 1
}
[pscustomobject]@{
    Verdict=$script:OverallStatus
    CanonicalRunId=$script:CanonicalRunId
    ProtocolRunId=if($script:ProtocolInfo){[string]$script:ProtocolInfo.RunId}else{'NOT_CREATED'}
    RunIdentityBinding=$script:CanonicalRunIdentityStatus
    PreCanonicalTimedDiagnostic=$script:RecoveryDiagnosticStatus
    CandidateId=$script:CandidateId
    SourceSnapshotId=$script:SourceSnapshotId
    BenchmarkDllSHA256=if($manifest){$manifest.Binary.BenchmarkDll.SHA256}else{'NOT_CREATED'}
    DataAccessDllSHA256=if($manifest){$manifest.Binary.DataAccessDll.SHA256}else{'NOT_CREATED'}
    SourceInventoryCount=if($manifest){$manifest.Source.InventoryEntryCount}else{0}
    RuntimeInventoryCount=if($manifest){$manifest.Runtime.EntryCount}else{0}
    CorrectnessPreflight=if($script:Preflight){[string]$script:Preflight.SemanticScenarios}else{'0/6'}
    TotalCountPreflight=if($script:Preflight){[string]$script:Preflight.TotalCount}else{'0/6'}
    IsolatedC1C2=('{0}/12' -f @($coreRows|Where-Object Status -eq 'PASS').Count)
    C4=('{0}/6' -f @($c4Rows|Where-Object Status -eq 'PASS').Count)
    BDN=('{0}/6' -f @($bdnRows|Where-Object Status -eq 'PASS').Count)
    PostIsolatedCorrectness=if($script:AfterIsolated){[string]$script:AfterIsolated.SemanticScenarios}else{'0/6'}
    MixedL1=if($script:MixedLevels|Where-Object Level -CEQ 'L1'|Select-Object -Last 1){[string](($script:MixedLevels|Where-Object Level -CEQ 'L1'|Select-Object -Last 1).Status)}else{'NOT_RUN'}
    MixedL2=if($script:MixedLevels|Where-Object Level -CEQ 'L2'|Select-Object -Last 1){[string](($script:MixedLevels|Where-Object Level -CEQ 'L2'|Select-Object -Last 1).Status)}else{'NOT_RUN'}
    MixedL4=if($script:MixedLevels|Where-Object Level -CEQ 'L4'|Select-Object -Last 1){[string](($script:MixedLevels|Where-Object Level -CEQ 'L4'|Select-Object -Last 1).Status)}else{'NOT_RUN'}
    MixedL8=if($script:MixedLevels|Where-Object Level -CEQ 'L8'|Select-Object -Last 1){[string](($script:MixedLevels|Where-Object Level -CEQ 'L8'|Select-Object -Last 1).Status)}else{'NOT_RUN'}
    MixedOverlap=$script:MixedOverlapStatus
    MixedTelemetry=if($script:MixedLevels.Count -eq 4 -and @($script:MixedLevels|Where-Object TelemetryStatus -ne 'TELEMETRY_VALID').Count -eq 0){'PASS'}else{'FAIL'}
    PostMixedCorrectness=if($script:AfterMixed){[string]$script:AfterMixed.SemanticScenarios}else{'0/6'}
    FinalOwnedProcessCount=if($script:FinalCleanupEvidence){$script:FinalCleanupEvidence.ProcessCount}else{0}
    FinalPidProbes=if($script:FinalCleanupEvidence){$script:FinalCleanupEvidence.PidProbeCount}else{0}
    AggregateCleanup=$cleanupStatus
    CleanupPersistence=if($cleanupPersistencePass){'PASS'}else{'FAIL'}
    BoundedDbPrePost=$script:PrePostStatus
    ReportTemplateLint=if($finalLint){[string]$finalLint.Status}else{'NOT_RUN'}
    CandidateGuard=$script:CandidateGuardFinalStatus
    Preservation=$script:PreservationStatus
    HistoricalTimeoutCause='NOT_VERIFIED'
    FullDatasetValueEquality='NOT_VERIFIED'
    PerformanceSLA='NO_SLA_DEFINED'
    CausalAttribution='NOT_ESTABLISHED'
    CoreEvidenceReady=($script:OverallStatus -eq 'BATCH3_PASS')
    ReadyForBatch4=($script:OverallStatus -eq 'BATCH3_PASS')
    CanonicalWorkloadAttempts=if($script:CanonicalWorkloadStarted){1}else{0}
    AdditionalWorkloadReruns=0
    AutomaticRetries=0
    ReportPath=(Join-Path $script:FinalReviewRoot 'Batch3-Final-Canonical-Report.md')
    ReviewZipPath=$script:FinalReviewZipPath
    ReviewZipSHA256=$zipHash
    Failure=$script:Failure
} | ConvertTo-Json -Depth 8 -Compress
