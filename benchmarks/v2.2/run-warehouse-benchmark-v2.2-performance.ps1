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
    [string]$BatchRoot='P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_2_BATCH3_20261001_01'
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

$script:RepoRoot=[IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')
$script:BatchRoot=[IO.Path]::GetFullPath($BatchRoot)
$script:ReviewRoot=Join-Path $script:BatchRoot 'Batch3-Review-Pack'
$script:LogsRoot=Join-Path $script:BatchRoot 'logs'
$script:ModulePath=Join-Path $PSScriptRoot 'WarehouseBenchmarkV22.Harness.psm1'
$script:CandidateBuilder=Join-Path $PSScriptRoot 'Build-Freeze-V22PerformanceCandidate.ps1'
$script:CandidateGuard=Join-Path $PSScriptRoot 'Test-V22PerformanceCandidateGuard.ps1'
$script:InventoryBuilder=Join-Path $PSScriptRoot 'New-V22CanonicalSourceInventory.ps1'
$script:OfflineSelfTest=Join-Path $PSScriptRoot 'Invoke-V22OfflineSelfTest.ps1'
$script:FailureInjection=Join-Path $PSScriptRoot 'Invoke-V22FailureInjection.ps1'
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
$script:CleanupRows=[Collections.Generic.List[object]]::new();$script:FinalCleanupProof=$null
$script:HostStopRows=[Collections.Generic.List[object]]::new()
$script:CurrentStage='INITIALIZE'
$script:OverallStatus='BATCH3_NOT_READY'
$script:StopReason=$null
$script:Failure=$null
$script:CandidateId=$null
$script:SourceSnapshotId=$null
$script:CandidateManifestHash=$null
$script:CandidateGuardInitialStatus='NOT_RUN'
$script:CandidateGuardCanonicalStatus='NOT_RUN'
$script:CandidateGuardFinalStatus='NOT_RUN'
$script:PreflightStatus='NOT_RUN'
$script:RecoveryDiagnosticStatus='NOT_RUN'
$script:RecoveryDiagnostic=$null
$script:ProbeStatus='NOT_RUN'
$script:ComparatorStatus='NOT_VERIFIED'
$script:PreservationStatus='NOT_RUN'
$script:IsolatedGate='NOT_RUN'
$script:MixedGate='NOT_RUN'
$script:PrePostStatus='NOT_RUN'
$script:RunId=('WHB22-PERF-RUN-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8).ToUpperInvariant())
$script:CanonicalRunId=$script:RunId
$script:PerformanceRunRoot=Join-Path $script:BatchRoot ('candidate-preflight-'+$script:CanonicalRunId)
$script:HostStopPath=Join-Path $script:PerformanceRunRoot 'host-stop-actions.jsonl'
$script:ProtocolInfo=[ordered]@{ProtocolVersion='2.2';RunId=$script:RunId;CandidateId=$null;Status='INITIALIZING';Scenarios=$script:Scenarios;IsolatedOrder='Per scenario: BDN, C1, C2, C4';NBomber=[ordered]@{Version='6.6.0';WarmupSeconds=3;TimedSeconds=15;LoadModel='KeepConstant';CopiesMeaning='One copy is one worker';ObservedCopiesEvidence='Unique ScenarioInfo.InstanceNumber values recorded by the scenario callback';ApplicationTimeoutSeconds=30;IndividualRequestRetry=$false};BDN=[ordered]@{Version='0.15.8';Toolchain='InProcessNoEmit';LaunchCount=1;WarmupCount=2;ConfiguredIterationCount=5;InvocationCount=1;UnrollFactor=1;OutlierMode='RemoveUpper, BenchmarkDotNet Job.Default';ActualMeasurements='Raw WorkloadActual rows reconciled with configured IterationCount, statistical N, upper fence, and Mean'};MixedLevels=@([ordered]@{Level='L1';CopiesPerScenario=1;TotalWorkers=6},[ordered]@{Level='L2';CopiesPerScenario=2;TotalWorkers=12},[ordered]@{Level='L4';CopiesPerScenario=4;TotalWorkers=24},[ordered]@{Level='L8';CopiesPerScenario=8;TotalWorkers=48});MinimumMixedCommonOverlapSeconds=10;HostPolicy=[ordered]@{HardFloorMB=512;EmergencyFloorMB=128;SafeMinimumMB=640;AdmissionSamples=2;AdmissionSampleIntervalSeconds=1;AdmissionWaitLimitSeconds=60;CooldownMinimumSeconds=5;CooldownMaximumSeconds=60};Database=[ordered]@{Name=$script:TargetDatabase;ExpectedDatabaseId=5;ReadOnly=$true;HistoricalReportMode='LEGACY'};PerformanceConclusion='DESCRIPTIVE_ONLY_NO_SLA_THRESHOLD'}

Import-Module $script:ModulePath -ErrorAction Stop

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
    $script:RunId=$RunId;$script:CanonicalRunId=$RunId
    $script:PerformanceRunRoot=Join-Path $script:BatchRoot ($RootPrefix+'-'+$RunId)
    [void](New-V22RunRoot $script:PerformanceRunRoot)
    $script:HostStopPath=Join-Path $script:PerformanceRunRoot 'host-stop-actions.jsonl'
    $script:HostLogPath=Join-Path $script:PerformanceRunRoot 'host-telemetry.jsonl'
    $script:ProtocolInfo.RunId=$RunId
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
    $resolved=(Get-Command $FilePath -ErrorAction Stop).Source
    $startInfo=[Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName=$resolved;$startInfo.WorkingDirectory=$WorkingDirectory;$startInfo.UseShellExecute=$false;$startInfo.CreateNoWindow=$true
    $startInfo.RedirectStandardOutput=$true;$startInfo.RedirectStandardError=$true
    if($Role -notin @('CHILD','TELEMETRY','DB_TOOL')){[void]$startInfo.Environment.Remove('TKS_V22_CONNECTION_STRING')}
    foreach($arg in $ArgumentVector){[void]$startInfo.ArgumentList.Add([string]$arg)}
    $process=[Diagnostics.Process]::Start($startInfo)
    if($null -eq $process){throw "Could not start owned process: $Label"}
    $stdoutTask=$process.StandardOutput.ReadToEndAsync();$stderrTask=$process.StandardError.ReadToEndAsync()
    $record=[pscustomobject]@{Label=$Label;Role=$Role;FilePath=$resolved;Arguments=$ArgumentVector;Process=$process;ChildProcessId=[int]$process.Id;StartedUtc=[DateTime]::UtcNow;StdoutTask=$stdoutTask;StderrTask=$stderrTask;LogDirectory=$LogDirectory;FinishedUtc=$null;ExitCode=$null;TimedOut=$false;StopReason=$null;HostStopEvidence=$null;CleanupStatus='TRACKED';CleanupProcessProof=$null;CleanupProofStatus='NOT_RUN';ProcessGone=$false;TerminalMetadataStatus='NOT_APPLICABLE';TerminalMetadataFailure=$null;Completed=$false;RunId=(Get-ArgumentValue $ArgumentVector '--run-id');BlockId=(Get-ArgumentValue $ArgumentVector '--block-id');Scenario=(Get-ArgumentValue $ArgumentVector '--scenario');ExpectedCopies=if((Get-ArgumentValue $ArgumentVector '--copies') -match '^\d+$'){[int](Get-ArgumentValue $ArgumentVector '--copies')}else{$null};ConfiguredDurationSeconds=if((Get-ArgumentValue $ArgumentVector '--duration-seconds') -match '^\d+$'){[int](Get-ArgumentValue $ArgumentVector '--duration-seconds')}else{$null};MetadataPath=(Get-ArgumentValue $ArgumentVector '--metadata-path');StdoutPath=(Join-Path $LogDirectory ($Label+'.stdout.txt'));StderrPath=(Join-Path $LogDirectory ($Label+'.stderr.txt'))}
    $script:AllOwnedProcesses.Add($record)
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
        $HostStopEvidence.ActionRequested=$true
        $HostStopEvidence.ActionCompleted=($status -ne 'CLEANUP_FAILED' -and $Record.Process.HasExited)
        $HostStopEvidence.KillIssued=$killIssued
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
function Get-BdnArtifact([string]$OutputDirectory,[string]$StdoutPath){
    $csv=@(Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse -Filter '*report.csv')
    if($csv.Count -ne 1){throw "V22_BDN_INVALID|expected one BDN summary CSV, found $($csv.Count)"}
    $rows=@(Import-Csv -LiteralPath $csv[0].FullName -Delimiter ',')|Where-Object Method -ceq 'Execute'
    if(@($rows).Count -ne 1){throw 'V22_BDN_INVALID|expected one Execute summary row'}
    $summary=$rows[0]
    $meanNs=Convert-BdnQuantityToNs ([string]$summary.Mean) 'Mean';$errorNs=Convert-BdnQuantityToNs ([string]$summary.Error) 'Error';$stdNs=Convert-BdnQuantityToNs ([string]$summary.StdDev) 'StdDev'
    $iterationConfigured=[int]$summary.IterationCount
    if($summary.Toolchain -cne 'InProcessNoEmitToolchain' -or [int]$summary.LaunchCount -ne 1 -or [int]$summary.WarmupCount -ne 2 -or [int]$summary.InvocationCount -ne 1 -or [int]$summary.UnrollFactor -ne 1 -or $iterationConfigured -lt 1){throw 'V22_BDN_INVALID|reported BDN profile differs from protocol'}
    $logText=[IO.File]::ReadAllText($StdoutPath)
    foreach($log in @(Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse -Filter '*.log')){$logText+=[Environment]::NewLine+[IO.File]::ReadAllText($log.FullName)}
    $measurementRows=[Collections.Generic.List[object]]::new()
    foreach($line in ($logText -split '\r?\n')){
        $measurementMatch=[regex]::Match($line,'^\s*WorkloadActual\s+\d+:\s*(?<ops>\d+)\s+op,\s*(?<elapsed>(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?)\s*(?<unit>ns|us|µs|μs|ms|s)(?:,|\s)')
        if($measurementMatch.Success){$operations=[double]$measurementMatch.Groups['ops'].Value;$elapsedNs=Convert-BdnQuantityToNs ($measurementMatch.Groups['elapsed'].Value+' '+$measurementMatch.Groups['unit'].Value) 'ActualMeasurement';$measurementRows.Add([pscustomobject]@{IterationStage='Actual';Operations=$operations;Nanoseconds=$elapsedNs})}
    }
    $meanMatch=[regex]::Match($logText,'(?im)^\s*Mean\s*=\s*(?<mean>[+-]?(?:(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d*)?|\.\d+))\s*(?<unit>ns|us|µs|μs|ms|s)\s*,.*?\bN\s*=\s*(?<n>\d+)')
    if(-not$meanMatch.Success){throw 'V22_BDN_INVALID|finite statistical Mean and N are not present in raw log'}
    $reportedN=[int]$meanMatch.Groups['n'].Value
    $statMeanNs=Convert-BdnQuantityToNs ($meanMatch.Groups['mean'].Value+' '+$meanMatch.Groups['unit'].Value) 'RawLogMean'
    $upperFenceMatch=[regex]::Match($logText,'(?im)^\s*IQR\s*=.*?\bUpperFence\s*=\s*(?<upper>[+-]?(?:(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d*)?|\.\d+))\s*(?<unit>ns|us|µs|μs|ms|s)')
    if(-not$upperFenceMatch.Success){throw 'V22_BDN_INVALID|BenchmarkDotNet upper-outlier fence is missing from raw log'}
    $upperFenceNs=Convert-BdnQuantityToNs ($upperFenceMatch.Groups['upper'].Value+' '+$upperFenceMatch.Groups['unit'].Value) 'UpperFence'
    if($measurementRows.Count -lt 1 -or $measurementRows.Count -ne $iterationConfigured -or $reportedN -lt 1 -or $reportedN -gt $measurementRows.Count){throw 'V22_BDN_INVALID|raw WorkloadActual count does not match configured iterations or statistical N is invalid'}
    $includedMeasurements=@($measurementRows|Where-Object{$_.Nanoseconds -le $upperFenceNs})
    $removedUpperOutliers=$measurementRows.Count-$includedMeasurements.Count
    if($includedMeasurements.Count -ne $reportedN){throw 'V22_BDN_INVALID|reported N does not reconcile with raw measurements and RemoveUpper fence'}
    $includedMeanNs=($includedMeasurements|Measure-Object -Property Nanoseconds -Average).Average
    $meanUnit=$meanMatch.Groups['unit'].Value
    $meanScale=switch -CaseSensitive ($meanUnit){'ns'{1.0}'us'{1000.0}'µs'{1000.0}'μs'{1000.0}'ms'{1000000.0}'s'{1000000000.0}default{throw 'V22_BDN_INVALID|raw Mean unit invalid'}}
    $meanText=$meanMatch.Groups['mean'].Value
    $meanDecimals=if($meanText.Contains('.')){$meanText.Length-$meanText.IndexOf('.')-1}else{0}
    $meanRoundingToleranceNs=$meanScale*[Math]::Pow(10,-$meanDecimals)
    if([Math]::Abs($includedMeanNs-$statMeanNs) -gt $meanRoundingToleranceNs){throw 'V22_BDN_INVALID|reported Mean does not reconcile with included raw WorkloadActual measurements'}
    $summaryNormalized=Join-Path $OutputDirectory 'bdn-summary-normalized.csv'
    $measurementsNormalized=Join-Path $OutputDirectory 'bdn-measurements-normalized.csv'
    Write-NewText $summaryNormalized ('Mean,Error,StdDev,Unit'+[Environment]::NewLine+($statMeanNs.ToString([Globalization.CultureInfo]::InvariantCulture)+','+$errorNs.ToString([Globalization.CultureInfo]::InvariantCulture)+','+$stdNs.ToString([Globalization.CultureInfo]::InvariantCulture)+',ns')+[Environment]::NewLine)
    $measureText='IterationStage,Operations,Nanoseconds'+[Environment]::NewLine+(@($measurementRows|ForEach-Object{'Actual,'+$_.Operations.ToString([Globalization.CultureInfo]::InvariantCulture)+','+$_.Nanoseconds.ToString([Globalization.CultureInfo]::InvariantCulture)}) -join [Environment]::NewLine)+[Environment]::NewLine
    Write-NewText $measurementsNormalized $measureText
    $validated=Assert-V22BdnEvidence $summaryNormalized $measurementsNormalized
    return [pscustomobject]@{Status='BDN_COMPLETE';RawSummaryPath=$csv[0].FullName;RawSummarySHA256=(Get-FileHashHex $csv[0].FullName);NormalizedSummaryPath=$summaryNormalized;NormalizedMeasurementsPath=$measurementsNormalized;MeanMs=$validated.MeanNs/1000000.0;ErrorMs=$validated.ErrorNs/1000000.0;StdDevMs=$validated.StdDevNs/1000000.0;ConfiguredIterations=$iterationConfigured;ReportedN=$reportedN;ActualMeasurementRows=$validated.ActualMeasurementRows;RemovedUpperOutliers=$removedUpperOutliers;OutlierMode='RemoveUpper, validated from raw UpperFence and N';MeasurementEvidence='Raw WorkloadActual rows equal configured IterationCount; statistical N reconciled to values at or below the raw-log UpperFence; Mean recomputed from included rows';Toolchain='InProcessNoEmitToolchain'}
}
function Get-TelemetryAssessment([string]$Path,[string]$BlockId,[DateTimeOffset]$BlockStarted,[DateTimeOffset]$BlockFinished,[double[]]$WindowBounds=@()){
    $rawRows=@();if(Test-Path -LiteralPath $Path){try{$rawRows=@(Import-Csv -LiteralPath $Path -Delimiter ',')}catch{}}
    $mapped=[Collections.Generic.List[object]]::new()
    foreach($row in $rawRows){
        $mapped.Add([pscustomobject]@{RunId=[string]$row.RunId;BlockId=[string]$row.BlockId;TargetDatabase=[string]$row.TargetDatabase;SampleUtc=[string]$row.SampleUtc;Status=[string]$row.Status;FreeRamMb=[string]$row.FreeRamMb;CpuPercent=[string]$row.CpuPercent;DatabaseId=[string]$row.DatabaseId;ActiveRequests=[string]$row.ActiveRequests;BlockingRequests=[string]$row.BlockingRequests;PendingMemoryGrants=[string]$row.PendingMemoryGrants;ResourceSemaphoreWaiters=[string]$row.ResourceSemaphoreWaiters;ErrorCode=[string]$row.ErrorCode})
    }
    $assessment=Get-V22TelemetryAssessment $mapped.ToArray() $script:TargetDatabase $script:RunId $BlockId
    $timeValid=$false;$dbIdentityValid=$false
    foreach($row in $mapped){if($row.Status -cne 'VALID'){continue};try{$stamp=[DateTimeOffset]::Parse($row.SampleUtc,[Globalization.CultureInfo]::InvariantCulture);$id=[int]$row.DatabaseId;if($id -ne 5){continue};$dbIdentityValid=$true;if($stamp -ge $BlockStarted -and $stamp -le $BlockFinished){$timeValid=$true}}catch{}}
    $valid=($assessment.Status -ceq 'TELEMETRY_VALID' -and $assessment.ValidTargetRows -gt 0 -and $dbIdentityValid -and $timeValid)
    $status=if($valid){'TELEMETRY_VALID'}else{'TELEMETRY_INVALID'}
    $result=[pscustomobject]@{Status=$status;RawPath=$Path;RawSHA256=if(Test-Path -LiteralPath $Path){Get-FileHashHex $Path}else{$null};RunId=$script:RunId;BlockId=$BlockId;TargetDatabase=$script:TargetDatabase;ExpectedDatabaseId=5;Rows=@($rawRows).Count;ValidTargetRows=$assessment.ValidTargetRows;ErrorRows=$assessment.ErrorRows;RejectedRows=$assessment.RejectedRows;TargetDatabaseIdentityVerified=$dbIdentityValid;WithinBlockWindowVerified=$timeValid;BlockStartedUtc=$BlockStarted.ToString('o');BlockFinishedUtc=$BlockFinished.ToString('o');SQLLogicalReads='DIAGNOSTIC_ONLY';TempdbCounters='DIAGNOSTIC_ONLY';DeadlockCounter='DIAGNOSTIC_ONLY'}
    $script:TelemetryAssessments.Add($result)
    return $result
}
function Get-Residue([string]$Label,[string]$Directory,[int]$TimeoutSeconds=20){
    $output=Join-Path $Directory ($Label+'.json')
    $arguments=@($script:RuntimeDll,'performance-residue','--target-database',$script:TargetDatabase,'--output',$output)
    $record=Invoke-Tool 'dotnet' $arguments $script:RuntimeRoot $Label $Directory $TimeoutSeconds -DatabaseProcess
    $result=$null
    if($record.ExitCode -eq 0 -and (Test-Path -LiteralPath $output)){$result=Get-Content -LiteralPath $output -Raw|ConvertFrom-Json}
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
    $usesNbomber=$BlockKind -in @('NBOMBER','C1','C2','C4')
    $blockDirectory=Join-Path $RawRoot $BlockId
    New-Item -ItemType Directory -Path $blockDirectory|Out-Null
    $config=[ordered]@{RunId=$script:RunId;BlockId=$BlockId;CandidateId=$script:CandidateId;ProtocolVersion='2.2';Profile=$Profile;BlockKind=$BlockKind;Scenario=$Scenario;Copies=$Copies;WarmupSeconds=3;ConfiguredTimedSeconds=15;TargetDatabase=$script:TargetDatabase;Page=1;PageSize=10;Search=if($Scenario -in @('MasterPaged','LookupPaged')){'SanPham / empty'}else{'empty'};Login=if($Scenario -in @('DocumentPaged','DetailReportPaged','InventoryHistoricalReportPaged','InventoryCurrentBalancePaged')){'PERF_USER'}else{'NOT_USED_BY_OPERATION'};Warehouse='null';FromDate=if($Scenario -in @('DetailReportPaged','InventoryHistoricalReportPaged','InventoryCurrentBalancePaged')){'2025-01-01'}else{'NOT_USED'};ToDate=if($Scenario -in @('DetailReportPaged','InventoryHistoricalReportPaged','InventoryCurrentBalancePaged')){'2026-12-31'}else{'NOT_USED'};HistoricalMode=if($Scenario -eq 'InventoryHistoricalReportPaged'){'LEGACY'}else{'NOT_APPLICABLE'};UseCurrentBalance=if($Scenario -eq 'InventoryHistoricalReportPaged'){$false}elseif($Scenario -eq 'InventoryCurrentBalancePaged'){$true}else{$null};IndividualRequestRetry=$false;CreatedUtc=[DateTime]::UtcNow.ToString('o')}
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
                $result.BdnEvidence=$bdn;if($result.FailureType -notin @('HOST_LIMIT','EMERGENCY_HOST_LIMIT','HOST_TELEMETRY_INVALID')){$result.Status=if($children[0].ExitCode -eq 0){'PASS'}else{'INVALID'};$result.FailureType=if($children[0].ExitCode -eq 0){$null}else{'BDN_PROCESS_FAILED'}};$result.MeanMs=$bdn.MeanMs;$result.ErrorMs=$bdn.ErrorMs;$result.StdDevMs=$bdn.StdDevMs;$result.MeasuredIterations=$bdn.ActualMeasurementRows
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

$script:Batch2Review='P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_2_BATCH2_FINAL_CLOSURE_20261001_01\Batch2-Final-Review-Pack'
$script:StaticChecksPath=$null;$script:SelfTestPath=$null;$script:FailureInjectionPath=$null;$script:PredictedInventoryPath=$null;$script:RuntimeRoot=$null;$script:RuntimeDll=$null;$script:CandidateManifestPath=$null;$script:InitialGuardPath=$null;$script:Preflight=$null;$script:AfterIsolated=$null;$script:AfterMixed=$null;$script:Comparator=$null;$script:ProbeEvidence=$null;$script:InitialPreservationSummaryHash=$null;$script:SourceChanges=@();$script:V21Rows=@();$script:BDNRows=@();$script:HostLogPath=Join-Path $script:PerformanceRunRoot 'host-telemetry.jsonl';$script:isolatedBeginSnapshot=$null;$manifest=$null;$buildAttestation=$null;$toolCompare=$null;$isolatedManifest=$null;$mixedManifest=$null;$candidateManifest=$null;$manifestPath=Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Manifest.json';$candidateHash=$null;$candidatePass=$false;$correctnessPass=$false;$telemetryPass=$false;$mixedPass=$false;$preservationPass=$false;$comparatorPass=$false;$c4Pass=$false;$bdnPass=$false;$isolatedCorePass=$false;$isolatedTelemetry=$false;$isolatedIntegrity=$false;$postPass=$false;$cleanupStatus='NOT_RUN';$isolatedTotal=[long]0;$isolatedFailed=[long]0;$mixedFailed=[long]0;$coreRows=@();$c4Rows=@();$bdnRows=@();$isolatedTotals=$null;$telemetryOut=$null;$bdnOut=$null;$mixedOut=$null;$cleanupOut=$null;$prepost=$null;$limitations=$null;$coreGate=$null;$artifactIndexPath=Join-Path $script:PerformanceRunRoot 'raw-artifact-hashes.json';$artifactIndexHash=$null;$zipHash=$null;$requestFiles=@()

try{
    if(-not(Test-Path -LiteralPath $script:RepoRoot -PathType Container) -or -not(Test-Path -LiteralPath $script:ReviewRoot -PathType Container) -or -not(Test-Path -LiteralPath $script:LogsRoot -PathType Container)){throw 'Required repository or Batch 3 evidence roots are missing'}
    if(Test-Path -LiteralPath $script:PerformanceRunRoot){throw 'BATCH3_OUTPUT_ROOT_EXISTS; refusing resume or overwrite'}
    if(-not(Test-Path -LiteralPath $script:Batch2Review -PathType Container)){throw 'Batch 2 final review pack is missing'}
    if([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('TKS_V22_CONNECTION_STRING','Process'))){throw 'TKS_V22_CONNECTION_STRING is missing in the current process; secret value is never logged'}
    [void](New-V22RunRoot $script:PerformanceRunRoot)
    $script:CurrentStage='PRESERVATION_PRE_GATE';Write-RunProgress $script:CurrentStage 'IN_PROGRESS'
    $preSummaryPath=Join-Path $script:ReviewRoot 'Previous-Evidence-Preservation.json';$archivedSummary=Join-Path $script:LogsRoot 'Previous-Evidence-Preservation-Prebatch3-Summary.json';$prebuildPath=Join-Path $script:ReviewRoot 'Previous-Evidence-Preservation-Prebuild.json';$preManifestPath=Join-Path $script:LogsRoot 'preservation-pre-files.json'
    if(-not(Test-Path -LiteralPath $preManifestPath -PathType Leaf)){throw 'Preservation PRE manifest is missing'}
    if(Test-Path -LiteralPath $archivedSummary){
        if((Test-Path -LiteralPath $preSummaryPath) -or -not(Test-Path -LiteralPath $prebuildPath)){throw 'Existing preservation PRE state is ambiguous'}
        $preSummary=Get-Content -LiteralPath $archivedSummary -Raw|ConvertFrom-Json;$prebuild=Get-Content -LiteralPath $prebuildPath -Raw|ConvertFrom-Json
        if($preSummary.Status -cne 'PRE_CAPTURED_PASS' -or $preSummary.V21MismatchCount -ne 0 -or @($preSummary.PreviousPacks|Where-Object Status -ne 'PASS').Count -gt 0 -or $prebuild.Status -cne 'PRE_CAPTURED_POST_PENDING' -or (Get-FileHashHex $archivedSummary) -cne $prebuild.PreSummarySHA256 -or (Get-FileHashHex $preManifestPath) -cne $prebuild.PreManifestSHA256){throw 'Existing preservation PRE evidence failed identity verification'}
        $script:InitialPreservationSummaryHash=$prebuild.PreSummarySHA256
    }else{
        if(-not(Test-Path -LiteralPath $preSummaryPath -PathType Leaf) -or (Test-Path -LiteralPath $prebuildPath)){throw 'Preservation PRE evidence is missing or ambiguous'}
        $preSummary=Get-Content -LiteralPath $preSummaryPath -Raw|ConvertFrom-Json
        if($preSummary.Status -cne 'PRE_CAPTURED_PASS' -or $preSummary.V21MismatchCount -ne 0 -or @($preSummary.PreviousPacks|Where-Object Status -ne 'PASS').Count -gt 0){throw 'Preservation PRE gate is not PASS'}
        $script:InitialPreservationSummaryHash=Get-FileHashHex $preSummaryPath
        Move-Item -LiteralPath $preSummaryPath -Destination $archivedSummary
        $preFilesHash=Get-FileHashHex $preManifestPath
        $prebuild=[ordered]@{SchemaVersion='warehouse-benchmark-v22-batch3-preservation-prebuild/1';Status='PRE_CAPTURED_POST_PENDING';PreSummaryPath=$archivedSummary;PreSummarySHA256=$script:InitialPreservationSummaryHash;PreManifestPath=$preManifestPath;PreManifestSHA256=$preFilesHash;V21RootCount=$preSummary.V21RootCount;V21FileCount=$preSummary.V21FileCount;V21MismatchCount=$preSummary.V21MismatchCount;PreviousPacks=$preSummary.PreviousPacks;Batch2FinalFileCount=$preSummary.Batch2FinalFileCount;Batch2FinalTreeSHA256=$preSummary.Batch2FinalTreeSHA256}
        Write-NewJson $prebuildPath $prebuild
    }

    $script:CurrentStage='STATIC_AND_OFFLINE_GATES';Write-RunProgress $script:CurrentStage 'IN_PROGRESS'
    $testRoot=Join-Path $script:PerformanceRunRoot 'tests';New-Item -ItemType Directory -Path $testRoot|Out-Null
    $parseRows=[Collections.Generic.List[object]]::new();$sourceFiles=@(Get-ChildItem -LiteralPath $PSScriptRoot -File -Recurse|Where-Object Extension -in @('.ps1','.psm1'))
    foreach($file in $sourceFiles){$tokens=$null;$parseErrors=$null;[Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$parseErrors)|Out-Null;$parseRows.Add([pscustomobject]@{Path=$file.FullName;Status=if($parseErrors.Count -eq 0){'PASS'}else{'FAIL'};ErrorCount=$parseErrors.Count;Errors=@($parseErrors|ForEach-Object{$_.Message})})}
    $pidAssignments=@($sourceFiles|ForEach-Object{Select-String -LiteralPath $_.FullName -Pattern '(?im)^\s*\$(?:pid)\s*=' -ErrorAction SilentlyContinue}|ForEach-Object{[pscustomobject]@{Path=$_.Path;Line=$_.LineNumber;Text=$_.Line.Trim()}})
    $staticStatus=if(@($parseRows|Where-Object Status -ne 'PASS').Count -eq 0 -and $pidAssignments.Count -eq 0){'PASS'}else{'FAIL'}
    $static=[ordered]@{SchemaVersion='warehouse-benchmark-v22-batch3-static-checks/1';Status=$staticStatus;PowerShellParseStatus=if(@($parseRows|Where-Object Status -ne 'PASS').Count -eq 0){'PASS'}else{'FAIL'};ParsedFileCount=$sourceFiles.Count;ParseRows=$parseRows.ToArray();AutomaticPidAssignments=$pidAssignments;ConnectionStringPersisted=$false;PerformanceLoadRun='NOT_RUN';CapturedUtc=[DateTime]::UtcNow.ToString('o')}
    $script:StaticChecksPath=Join-Path $script:LogsRoot ('performance-static-checks-'+$script:RunId+'.json');Write-NewJson $script:StaticChecksPath $static
    if($staticStatus -ne 'PASS'){throw 'PowerShell parse or automatic PID collision static check failed'}
    $script:SelfTestPath=Join-Path $testRoot 'offline-selftest.json';$selfArgs=@('-NoProfile','-File',$script:OfflineSelfTest,'-EvidencePath',$script:SelfTestPath);$selfRecord=Invoke-Tool (Join-Path $PSHOME 'pwsh.exe') $selfArgs $script:RepoRoot 'offline-selftest' $testRoot 120
    if($selfRecord.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $script:SelfTestPath)){throw 'Offline self-test failed; candidate build is blocked'}
    $selfResult=Get-Content -LiteralPath $script:SelfTestPath -Raw|ConvertFrom-Json
    if($selfResult.Status -cne 'PASS'){throw 'Offline self-test result is not PASS'}
    $script:FailureInjectionPath=Join-Path $testRoot 'failure-injection\V22-Failure-Injection-Results.json';$fiArgs=@('-NoProfile','-File',$script:FailureInjection,'-EvidenceRoot',(Split-Path -Parent $script:FailureInjectionPath));$fiRecord=Invoke-Tool (Join-Path $PSHOME 'pwsh.exe') $fiArgs $script:RepoRoot 'failure-injection' $testRoot 180
    if($fiRecord.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $script:FailureInjectionPath)){throw 'Mandatory offline failure injection failed; candidate build is blocked'}
    $fiResult=Get-Content -LiteralPath $script:FailureInjectionPath -Raw|ConvertFrom-Json
    if($fiResult.Status -cne 'PASS' -or $fiResult.Failed -ne 0){throw 'Mandatory offline failure injection did not PASS'}

    $specSource=Join-Path 'P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_2_BATCH1_WHB22-20260930-65EA9FB0\Batch1-Review-Pack' 'V22-Source-Inventory-Spec.md'
    if(-not(Test-Path -LiteralPath $specSource -PathType Leaf)){throw 'Batch 1 canonical source inventory specification is missing'}
    $specText=[IO.File]::ReadAllText($specSource)+"`n`n## Batch 3 performance candidate scope`n`nThe canonical rule above is applied to the current repository root. All files under benchmarks/v2.2 are included except generated/build/run evidence; MSBuild-evaluated compile and project-reference inputs are inventoried transitively for TKS_Thuc_Tap_V11_Benchmarks_V22 and Data_Access. This Batch 3 inventory includes the performance runner, candidate builder, guard, protocol, tests, and both evaluated project closures. Candidate identity is WHB22-PERF-20261001- plus the first eight uppercase characters of the canonical inventory SHA-256. The source snapshot is copied and hashed before Release Build A/B; any mismatch blocks freeze."
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

    $script:CurrentStage='CANDIDATE_BUILD_AND_ATTESTATION';Write-RunProgress $script:CurrentStage 'IN_PROGRESS'
    $builderArgs=@('-NoProfile','-File',$script:CandidateBuilder,'-RepoRoot',$script:RepoRoot,'-CandidateRoot',$script:BatchRoot,'-CandidateId',$script:CandidateId,'-StaticCheckPath',$script:StaticChecksPath,'-SelfTestPath',$script:SelfTestPath,'-FailureInjectionPath',$script:FailureInjectionPath,'-NuGetPackagesRoot',$script:NuGetCacheRoot,'-NuGetCachePreInventoryPath',$script:NuGetCachePrePath)
    $builderRecord=Invoke-Tool (Join-Path $PSHOME 'pwsh.exe') $builderArgs $script:RepoRoot 'performance-candidate-builder' $script:PerformanceRunRoot 1800
    if($builderRecord.ExitCode -ne 0){throw 'Performance candidate build/freeze did not complete successfully'}
    $reviewFiles=Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Manifest.json';$script:CandidateManifestPath=$reviewFiles
    $manifest=Get-Content -LiteralPath $script:CandidateManifestPath -Raw|ConvertFrom-Json
    $script:CandidateManifestHash=Get-FileHashHex $script:CandidateManifestPath;$script:SourceSnapshotId=$manifest.Source.SnapshotId
    if($manifest.CandidateId -cne $script:CandidateId -or $manifest.Source.InventorySHA256 -cne $predicted.InventorySHA256 -or $manifest.Status -cne 'CANDIDATE_BUILT_AND_ATTESTED' -or $manifest.Build.ReproducibilityStatus -cne 'PASS'){throw 'Built candidate identity or reproducibility failed'}
    $script:RuntimeRoot=[IO.Path]::GetFullPath([string]$manifest.Runtime.Root);$script:RuntimeDll=Join-Path $script:RuntimeRoot 'TKS_Thuc_Tap_V11_Benchmarks_V22.dll'
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

    $script:CurrentStage='RECOVERY_LOOKUPPAGED_C1_DIAGNOSTIC';Write-RunProgress $script:CurrentStage 'IN_PROGRESS'
    $diagnosticRunId='WHB22-RECOVERY-DIAG-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8).ToUpperInvariant()
    Set-ActiveRunContext $diagnosticRunId 'recovery-diagnostic'
    $script:RecoveryDiagnosticRoot=$script:PerformanceRunRoot
    $diagGuardPath=Join-Path $script:PerformanceRunRoot 'candidate-guard.json';$diagGuardInventory=Join-Path $script:PerformanceRunRoot 'candidate-inventory.json';$diagGuardLogs=Join-Path $script:PerformanceRunRoot 'guard-logs';New-Item -ItemType Directory -Path $diagGuardLogs|Out-Null
    $diagGuardArgs=@('-NoProfile','-File',$script:CandidateGuard,'-RepoRoot',$script:RepoRoot,'-CandidateRoot',$script:BatchRoot,'-CandidateManifestPath',$script:CandidateManifestPath,'-SourceInventoryPath',(Join-Path $script:ReviewRoot 'V22-Performance-Source-Inventory.json'),'-RuntimeInventoryPath',(Join-Path $script:ReviewRoot 'V22-Performance-Runtime-Inventory.json'),'-BuildAttestationPath',(Join-Path $script:ReviewRoot 'V22-Performance-Build-Attestation.json'),'-InventoryCheckPath',$diagGuardInventory,'-OutputPath',$diagGuardPath,'-ExpectedManifestSHA256',$script:CandidateManifestHash)
    $diagGuardProcess=Invoke-Tool (Join-Path $PSHOME 'pwsh.exe') $diagGuardArgs $script:RepoRoot 'recovery-diagnostic-candidate-guard' $diagGuardLogs 600
    $diagGuard=Get-Content -LiteralPath $diagGuardPath -Raw|ConvertFrom-Json
    if($diagGuardProcess.ExitCode -ne 0 -or $diagGuardProcess.CleanupStatus -ne 'PASS' -or $diagGuard.Status -cne 'PASS'){throw 'Recovery diagnostic candidate guard failed'}
    $script:ProtocolInfo.CandidateId=$script:CandidateId;$script:ProtocolInfo.Status='RECOVERY_DIAGNOSTIC'
    Write-NewJson (Join-Path $script:PerformanceRunRoot 'protocol.json') $script:ProtocolInfo
    $diagRawRoot=Join-Path $script:PerformanceRunRoot 'raw';New-Item -ItemType Directory -Path $diagRawRoot|Out-Null
    $diagBlockId=$diagnosticRunId+'-ISO-LookupPaged-C1'
    $diagRow=Invoke-MeasurementBlock 'LookupPaged' 1 'LEGACY_ISOLATED' $diagBlockId 'C1' 0.0 $diagRawRoot $script:HostLogPath 75
    $diagBlockRoot=Join-Path $diagRawRoot $diagBlockId;$diagTerminalPath=Join-Path $diagBlockRoot 'worker-metadata.json';$diagTerminal=$null
    if(Test-Path -LiteralPath $diagTerminalPath){try{$diagTerminal=Get-Content -LiteralPath $diagTerminalPath -Raw|ConvertFrom-Json}catch{}}
    $diagHostAfter=Get-HostSnapshot @($diagRow.ChildProcessIds)
    $diagGuardEvents=@($diagRow.HostStopActions);$hostGuardRecorded=[bool]$diagRow.Admission.Allowed
    if($diagGuardEvents.Count -gt 0){$hostGuardRecorded=($hostGuardRecorded -and @($diagGuardEvents|Where-Object{-not(Test-V22HostStopEvidence $_)}).Count -eq 0)}
    $diagPostAdmission=$null;$postDiagCorrectness=$null;$postDiagStateMatch=$false;$correctnessProcessEvidence=$null
    $safeForPostCheck=($diagRow.CleanupStatus -ceq 'PASS' -and $diagRow.FailureType -notin @('HOST_LIMIT','EMERGENCY_HOST_LIMIT') -and [double]$diagHostAfter.FreeRamMB -gt 512)
    if($safeForPostCheck){
        $diagPostAdmission=Get-Admission ($diagnosticRunId+'-POST-CORRECTNESS') 'LEGACY_ISOLATED' 0.0 $script:HostLogPath
        if($diagPostAdmission.Allowed){
            $processIndexBefore=$script:AllOwnedProcesses.Count;$correctnessHostAtLaunch=Get-HostSnapshot
            $postDiagCorrectness=Invoke-Correctness 'correctness-after-recovery-diagnostic'
            $correctnessHostAfter=Get-HostSnapshot
            $correctnessProcess=$null;if($script:AllOwnedProcesses.Count -gt $processIndexBefore){$correctnessProcess=$script:AllOwnedProcesses[$processIndexBefore]}
            $correctnessStderr='';if($correctnessProcess -and (Test-Path -LiteralPath $correctnessProcess.StderrPath)){try{$correctnessStderr=Get-SafeText ([IO.File]::ReadAllText($correctnessProcess.StderrPath))}catch{}}
            $clrState=if($postDiagCorrectness.Status -ceq 'PASS'){'REACHED_CORRECTNESS_EXECUTION'}elseif($correctnessStderr -match 'Failed to create CoreCLR|GC heap initialization failed'){'FAILED_BEFORE_CLR_INITIALIZATION'}else{'NOT_VERIFIED'}
            $dbState=if($postDiagCorrectness.OutputRoot -and (Test-Path -LiteralPath (Join-Path $postDiagCorrectness.OutputRoot 'V22-DB-PrePost-Evidence.json'))){'YES'}elseif($clrState -ceq 'FAILED_BEFORE_CLR_INITIALIZATION'){'NO'}else{'NOT_VERIFIED'}
            $correctnessProcessEvidence=[ordered]@{Status=$postDiagCorrectness.Status;LaunchUtc=if($correctnessProcess){$correctnessProcess.StartedUtc.ToString('o')}else{$null};FinishUtc=if($correctnessProcess){$correctnessProcess.FinishedUtc.ToString('o')}else{$null};ProcessId=if($correctnessProcess){$correctnessProcess.ChildProcessId}else{$null};ExitCode=if($correctnessProcess){$correctnessProcess.ExitCode}else{$null};CleanupStatus=if($correctnessProcess){$correctnessProcess.CleanupStatus}else{'PROCESS_NOT_CREATED'};CleanupProcessProof=if($correctnessProcess){$correctnessProcess.CleanupProcessProof}else{$null};FreeRamAtLaunchMB=$correctnessHostAtLaunch.FreeRamMB;FreeVirtualMemoryAtLaunchMB=$correctnessHostAtLaunch.FreeVirtualMemoryMB;TotalVirtualMemoryAtLaunchMB=$correctnessHostAtLaunch.TotalVirtualMemoryMB;FreeRamAfterMB=$correctnessHostAfter.FreeRamMB;FreeVirtualMemoryAfterMB=$correctnessHostAfter.FreeVirtualMemoryMB;TotalVirtualMemoryAfterMB=$correctnessHostAfter.TotalVirtualMemoryMB;CLRInitializationStatus=$clrState;DatabaseQueryStarted=$dbState;OutputRoot=$postDiagCorrectness.OutputRoot;StdoutPath=if($correctnessProcess){$correctnessProcess.StdoutPath}else{$null};StderrPath=if($correctnessProcess){$correctnessProcess.StderrPath}else{$null};SafeStderr=$correctnessStderr;Reason=(Get-V22Field $postDiagCorrectness 'Reason')}
            if($postDiagCorrectness.Status -ceq 'PASS'){$postDiagStateMatch=($postDiagCorrectness.DBEvidence.PrePostSha256Equal -eq $true -and $script:Preflight.DBEvidence.Post.SnapshotSha256 -ceq $postDiagCorrectness.DBEvidence.Pre.SnapshotSha256)}
        }
    }
    $diagBlockPass=($diagRow.Status -ceq 'PASS' -and $diagRow.Requests -gt 0 -and $diagRow.Failed -eq 0 -and $diagRow.ObservedCopies -eq 1 -and $diagRow.TelemetryStatus -ceq 'TELEMETRY_VALID' -and $diagRow.CleanupStatus -ceq 'PASS' -and $null -ne $diagTerminal -and $diagTerminal.Status -ceq 'COMPLETED' -and $diagTerminal.TimedWindowCompleted -eq $true -and $diagTerminal.ObservedCopies -eq 1 -and $hostGuardRecorded)
    $diagPostCorrectnessPass=($null -ne $postDiagCorrectness -and $postDiagCorrectness.Status -ceq 'PASS' -and $postDiagStateMatch -and $correctnessProcessEvidence.CleanupStatus -ceq 'PASS')
    $script:RecoveryDiagnosticStatus=if($diagBlockPass -and $diagPostCorrectnessPass){'PASS'}else{'RECOVERY_TARGETED_DIAGNOSTIC_FAILED'}
    $script:RecoveryDiagnostic=[ordered]@{SchemaVersion='warehouse-benchmark-v22-recovery-lookup-c1/1';Status=$script:RecoveryDiagnosticStatus;RunId=$diagnosticRunId;BlockId=$diagBlockId;CandidateId=$script:CandidateId;CandidateManifestSHA256=$script:CandidateManifestHash;Scenario='LookupPaged';Profile='LEGACY_ISOLATED';Copies=1;WarmupSeconds=3;TimedSeconds=15;RetryPolicy='No individual request retry';TargetDatabase=$script:TargetDatabase;RawRoot=$script:PerformanceRunRoot;BlockResult=$diagRow;TerminalWorkerMetadata=$diagTerminal;HostAfterChild=$diagHostAfter;HostAdmission=$diagRow.Admission;HostStopActions=$diagGuardEvents;HostGuardReasonFullyRecorded=$hostGuardRecorded;PostDiagnosticAdmission=$diagPostAdmission;PostDiagnosticCorrectness=$postDiagCorrectness;PostDiagnosticStateMatchesPreflight=$postDiagStateMatch;CorrectnessProcessLaunchEvidence=$correctnessProcessEvidence;IsPerformanceBaseline=$false;RecordedUtc=[DateTime]::UtcNow.ToString('o')}
    Write-NewJson (Join-Path $script:ReviewRoot 'V22-Recovery-LookupPaged-C1-Diagnostic.json') $script:RecoveryDiagnostic
    if($script:RecoveryDiagnosticStatus -ne 'PASS'){$script:StopReason=if($diagRow.FailureType){$diagRow.FailureType}else{'RECOVERY_TARGETED_DIAGNOSTIC_FAILED'};throw 'RECOVERY_TARGETED_DIAGNOSTIC_FAILED; canonical Batch 3 workload is blocked'}

    $canonicalRunId='WHB22-PERF-RUN-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8).ToUpperInvariant()
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
        else{$row=Invoke-MeasurementBlock $scenario $copies 'LEGACY_ISOLATED' $blockId $kind $previous (Join-Path $script:PerformanceRunRoot 'isolated') $script:HostLogPath 75}
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
        $mixedManifest=[ordered]@{SchemaVersion='warehouse-benchmark-v22-mixed-run-manifest/1';RunId=$script:RunId;CandidateId=$script:CandidateId;CandidateManifestSHA256=$script:CandidateManifestHash;Profile='MIXED_REGRESSION';Scenarios=$script:Scenarios;WarmupSeconds=3;TimedSeconds=15;ParentTimeoutSeconds=90;ChildProcessesPerLevel=6;CopiesMeaning='KeepConstant copies; one copy is one worker';RequiredCommonOverlapSeconds=10;Levels=@();StartedUtc=[DateTime]::UtcNow.ToString('o')}
        $previousMixedDrop=0.0;$stopMixed=$false
        foreach($workers in @(1,2,4,8)){
            $level='L'+$workers;$levelId=$script:RunId+'-MIXED-'+$level;$levelRoot=Join-Path (Join-Path $script:PerformanceRunRoot 'mixed') $levelId;New-Item -ItemType Directory -Path $levelRoot|Out-Null
            if($stopMixed){$levelResult=[pscustomobject]@{RunId=$script:RunId;Level=$level;LevelId=$levelId;TotalWorkers=6*$workers;WorkersPerScenario=$workers;Status='SKIPPED';FailureType='PRIOR_HOST_OR_COOLDOWN_GATE';WorkerRows=@();Failed=0;Requests=0;TelemetryStatus='SKIPPED';CleanupStatus='NO_CHILD_STARTED';CommonOverlapSeconds=0;Interpretation=if($workers -eq 8){'STANDALONE_MIXED_LOAD_LEVEL'}else{'MIXED_LEVEL'}};$script:MixedLevels.Add($levelResult);continue}
            $admission=Get-Admission $levelId 'MIXED_REGRESSION' $previousMixedDrop $script:HostLogPath
            if(-not$admission.Allowed){$levelResult=[pscustomobject]@{RunId=$script:RunId;Level=$level;LevelId=$levelId;TotalWorkers=6*$workers;WorkersPerScenario=$workers;Status='HOST_LIMIT';FailureType=$admission.Reason;Admission=$admission;WorkerRows=@();Failed=0;Requests=0;TelemetryStatus='SKIPPED';CleanupStatus='NO_CHILD_STARTED';CommonOverlapSeconds=0;Interpretation=if($workers -eq 8){'STANDALONE_MIXED_LOAD_LEVEL'}else{'MIXED_LEVEL'}};$script:MixedLevels.Add($levelResult);$stopMixed=$true;continue}
            $children=[Collections.Generic.List[object]]::new();$workerMetadata=[Collections.Generic.List[object]]::new();$levelStarted=[DateTimeOffset]::UtcNow
            try{
                foreach($scenario in $script:Scenarios){
                    $workerBlock=$levelId+'-'+$scenario;$report=Join-Path $levelRoot ($scenario+'\nbomber-report');$metadata=Join-Path $levelRoot ($scenario+'\worker-metadata.json')
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
                $workerRows=[Collections.Generic.List[object]]::new()
                foreach($meta in $workerMetadata){$child=$children|Where-Object ChildProcessId -eq $meta.ChildProcessId|Select-Object -First 1;$terminal=$null;try{$terminal=Get-Content -LiteralPath $meta.MetadataPath -Raw|ConvertFrom-Json}catch{};$workerRow=[ordered]@{Scenario=$meta.Scenario;BlockId=$meta.BlockId;Copies=$workers;Status='INVALID';FailureType=$null;Requests=$null;Success=$null;Failed=$null;RPS=$null;MeanMs=$null;P50Ms=$null;P95Ms=$null;P99Ms=$null;MaxMs=$null;MeasuredStartUtc=$null;MeasuredStopUtc=$null;LogWindowSeconds=$null;ObservedCopies=if($terminal){$terminal.ObservedCopies}else{$null};ObservedInstanceNumbers=if($terminal){@($terminal.ObservedInstanceNumbers)}else{@()};WorkerTerminalStatus=if($terminal){$terminal.Status}else{'MISSING'};WorkerTimedWindowCompleted=if($terminal){$terminal.TimedWindowCompleted}else{$false};ProcessExitCode=$child.ExitCode;RawArtifacts=@()}
                    try{$nb=Get-NBomberArtifact $meta.ReportDirectory $meta.Scenario $workers $child.StdoutPath $meta.MetadataPath $script:RunId $meta.BlockId 'MIXED_REGRESSION';$workerRow.Status=if($child.ExitCode -eq 0 -and $nb.Metrics.Failed -eq 0){'PASS'}else{'FAIL'};$workerRow.FailureType=if($workerRow.Status -eq 'PASS'){$null}else{'PRODUCT_ERROR'};$workerRow.Requests=$nb.Metrics.Requests;$workerRow.Success=$nb.Success;$workerRow.Failed=$nb.Metrics.Failed;$workerRow.RPS=$nb.Metrics.RPS;$workerRow.MeanMs=$nb.Metrics.MeanMs;$workerRow.P50Ms=$nb.Metrics.P50Ms;$workerRow.P95Ms=$nb.Metrics.P95Ms;$workerRow.P99Ms=$nb.Metrics.P99Ms;$workerRow.MaxMs=$nb.Metrics.MaxMs;$workerRow.MeasuredStartUtc=$nb.MeasuredStartUtc;$workerRow.MeasuredStopUtc=$nb.MeasuredStopUtc;$workerRow.LogWindowSeconds=$nb.LogWindowSeconds;$workerRow.ObservedCopies=$nb.ObservedCopies;$workerRow.ObservedInstanceNumbers=$nb.ObservedInstanceNumbers;$workerRow.NBomberEvidence=$nb}catch{$workerRow.Status='INVALID';$workerRow.FailureType='HARNESS_INVALID';$workerRow.Reason=Get-SafeText $_.Exception.Message}
                    $workerRoot=Join-Path $levelRoot $meta.Scenario;$workerRow.RawArtifacts=@(Get-ChildItem -LiteralPath $workerRoot -File -Recurse|ForEach-Object{[pscustomobject]@{Path=$_.FullName;Size=$_.Length;SHA256=(Get-FileHashHex $_.FullName)}});$workerRows.Add([pscustomobject]$workerRow);$combined=[pscustomobject]$workerRow;$combined|Add-Member -NotePropertyName Level -NotePropertyValue $level;$combined|Add-Member -NotePropertyName TotalWorkers -NotePropertyValue (6*$workers);$script:MixedRows.Add($combined)
                }
                $windows=@($workerRows|Where-Object{$_.MeasuredStartUtc -and $_.MeasuredStopUtc});$overlap=0.0
                if($windows.Count -eq 6){$latest=($windows|ForEach-Object{[DateTimeOffset]::Parse($_.MeasuredStartUtc)}|Sort-Object -Descending|Select-Object -First 1);$earliest=($windows|ForEach-Object{[DateTimeOffset]::Parse($_.MeasuredStopUtc)}|Sort-Object|Select-Object -First 1);$overlap=[Math]::Round(($earliest-$latest).TotalSeconds,3)}
                $blockEnd=[DateTimeOffset]::UtcNow;$telemetry=Get-TelemetryAssessment $telemetryCsv $levelId $levelStarted $blockEnd
                $allWorkersValid=($workerRows.Count -eq 6 -and @($workerRows|Where-Object Status -ne 'PASS').Count -eq 0 -and @($workerRows|Where-Object ObservedCopies -ne $workers).Count -eq 0)
                $overlapValid=($windows.Count -eq 6 -and $overlap -ge 10)
                $cleanupProof=Get-OwnedProcessCleanupProof (@($children)+@($telemetryRecord|Where-Object{$null -ne $_}))
                $terminalFailures=@($children|Where-Object TerminalMetadataStatus -ne 'WRITTEN').Count
                $cleanupPass=($cleanupProof.Passed -and $terminalFailures -eq 0 -and @($children|Where-Object{-not $_.Completed -or -not $_.ProcessGone}).Count -eq 0 -and $null -ne $telemetryRecord -and $telemetryRecord.Completed -and $telemetryRecord.ProcessGone)
                $levelStatus=if($wait.MemoryLimitReason){if($wait.EmergencyStop){'EMERGENCY_HOST_LIMIT'}else{'HOST_LIMIT'}}elseif($wait.MonitorFailure){'INVALID'}elseif($wait.TimedOut){'FAIL'}elseif(-not$allWorkersValid -or -not$overlapValid){'INVALID'}elseif($telemetry.Status -ne 'TELEMETRY_VALID' -or -not$cleanupPass){'INVALID'}else{'PASS'}
                $requests=[long](($workerRows|Measure-Object Requests -Sum).Sum);$failed=[long](($workerRows|Measure-Object Failed -Sum).Sum);$sumRps=[double](($workerRows|Measure-Object RPS -Sum).Sum)
                $minRam=$wait.MinRamMB;$drop=if($null -ne $minRam){[Math]::Max(0.0,[double]$admission.AdmissionSamples[-1].FreeRamMB-[double]$minRam)}else{0.0}
                $levelResult=[pscustomobject]@{RunId=$script:RunId;Level=$level;LevelId=$levelId;TotalWorkers=6*$workers;WorkersPerScenario=$workers;Status=$levelStatus;FailureType=if($levelStatus -eq 'PASS'){$null}elseif($wait.MemoryLimitReason){if($wait.EmergencyStop){'EMERGENCY_HOST_LIMIT'}else{'HOST_LIMIT'}}elseif($wait.MonitorFailure){'HOST_TELEMETRY_INVALID'}elseif(-not$overlapValid){'MIXED_LEVEL_INVALID'}elseif($telemetry.Status -ne 'TELEMETRY_VALID'){'TELEMETRY_INVALID'}else{'MIXED_WORKER_INVALID'};Admission=$admission;WorkerRows=$workerRows.ToArray();Requests=$requests;Failed=$failed;AggregateRPS=[Math]::Round($sumRps,3);AggregateRPSLabel='DESCRIPTIVE_SUM_OF_SCENARIO_RPS';WindowOverlapSeconds=$overlap;RequiredOverlapSeconds=10;OverlapValid=$overlapValid;Telemetry=$telemetry;TelemetryStatus=$telemetry.Status;CleanupStatus=if($cleanupPass){'PASS'}else{'FAIL'};CleanupProcessProof=$cleanupProof;HostStopActions=@($wait.HostStopActions);MinFreeRamMB=$minRam;ObservedDropMB=$drop;Interpretation=if($workers -eq 8){'STANDALONE_MIXED_LOAD_LEVEL'}else{'MIXED_LEVEL'};StartedUtc=$levelStarted.ToString('o');FinishedUtc=$blockEnd.ToString('o');ProcessExitCodes=@($children|ForEach-Object{[pscustomobject]@{ChildProcessId=$_.ChildProcessId;ExitCode=$_.ExitCode;TimedOut=$_.TimedOut;StopReason=$_.StopReason;TerminalMetadataStatus=$_.TerminalMetadataStatus}});RawTelemetryPath=$telemetryCsv;RawArtifacts=@(Get-ChildItem -LiteralPath $levelRoot -File -Recurse|ForEach-Object{[pscustomobject]@{Path=$_.FullName;Size=$_.Length;SHA256=(Get-FileHashHex $_.FullName)}})}
                $levelResultPath=Join-Path $levelRoot 'level-result.json';Write-NewJson $levelResultPath $levelResult
                $script:MixedLevels.Add($levelResult);$mixedManifest.Levels+=@([pscustomobject]@{Level=$level;TotalWorkers=6*$workers;WorkersPerScenario=$workers;BlockId=$levelId;Status=$levelStatus;CommonOverlapSeconds=$overlap;TelemetryStatus=$telemetry.Status;ChildProcessIds=@($children|ForEach-Object ChildProcessId)})
                if($levelStatus -ne 'PASS'){$stopMixed=$true}
                if($wait.MemoryLimitReason){$script:StopReason=if($wait.EmergencyStop){'EMERGENCY_HOST_LIMIT'}else{'HOST_LIMIT'};$stopMixed=$true}
                if($wait.MonitorFailure){$script:StopReason='HOST_TELEMETRY_INVALID';$stopMixed=$true}
                $cool=Wait-Cooldown $levelId (Join-Path $script:PerformanceRunRoot 'cooldown') $script:HostLogPath;$levelResult|Add-Member -NotePropertyName Cooldown -NotePropertyValue $cool
                if($cool.Status -ne 'PASS'){$levelResult.Status='INVALID';$levelResult.CleanupStatus='FAIL';$script:StopReason='COOLDOWN_FAILED';$stopMixed=$true}
                $previousMixedDrop=[Math]::Max(0.0,$drop)
            }catch{
                foreach($record in $children){if(-not$record.Completed){[void](Stop-OwnedProcess $record 'MIXED_LEVEL_EXCEPTION');[void](Complete-OwnedProcess $record 'MIXED_LEVEL_EXCEPTION')}}
                if($null -ne $telemetryRecord -and -not$telemetryRecord.Completed){if(-not$telemetryRecord.Process.HasExited){[void](Stop-OwnedProcess $telemetryRecord 'MIXED_LEVEL_EXCEPTION')};[void](Complete-OwnedProcess $telemetryRecord 'MIXED_LEVEL_EXCEPTION')}
                $levelResult=[pscustomobject]@{RunId=$script:RunId;Level=$level;LevelId=$levelId;TotalWorkers=6*$workers;WorkersPerScenario=$workers;Status='FAIL';FailureType='HARNESS_FAILED';Reason=Get-SafeText $_.Exception.Message;WorkerRows=@();Failed=0;Requests=0;TelemetryStatus='INVALID';CleanupStatus='CHECK_REQUIRED';CommonOverlapSeconds=0;Interpretation=if($workers -eq 8){'STANDALONE_MIXED_LOAD_LEVEL'}else{'MIXED_LEVEL'}}
                $script:MixedLevels.Add($levelResult);$stopMixed=$true
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
    $script:CurrentStage='FINAL_CANDIDATE_GUARD_AND_PRESERVATION';Write-RunProgress $script:CurrentStage 'IN_PROGRESS'
    $script:OverallStatus='BATCH3_PARTIAL'
}catch{
    $script:Failure=[ordered]@{Stage=$script:CurrentStage;Message=Get-SafeText $_.Exception.Message;RecordedUtc=[DateTime]::UtcNow.ToString('o')}
    $script:StopReason=if($script:StopReason){$script:StopReason}else{'STAGE_FAILED'}
    if(Test-Path -LiteralPath $script:PerformanceRunRoot){try{Write-NewJson (Join-Path $script:PerformanceRunRoot 'fatal-summary.json') ([ordered]@{SchemaVersion='warehouse-benchmark-v22-fatal/1';Status='HARNESS_FAILED';Stage=$script:CurrentStage;FailureReason=$script:StopReason;SafeExceptionMessage=$script:Failure.Message;CleanupResult='PENDING_FINALIZER';RecordedUtc=[DateTime]::UtcNow.ToString('o')})}catch{}}
}finally{
    foreach($record in @($script:AllOwnedProcesses|Where-Object{-not $_.Completed})){
        if(-not$record.Process.HasExited){[void](Stop-OwnedProcess $record 'FINAL_CLEANUP')}
        [void](Complete-OwnedProcess $record 'FINAL_CLEANUP')
    }
    $script:CurrentStage='POST_PRESERVATION_CHECK'
    if((Test-Path -LiteralPath $script:PreservationPost -PathType Leaf) -and -not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'Previous-Evidence-Preservation.json'))){
        try{$postArgs=@('-NoProfile','-File',$script:PreservationPost,'-BatchRoot',$script:BatchRoot);$postRecord=Invoke-Tool (Join-Path $PSHOME 'pwsh.exe') $postArgs $script:RepoRoot 'preservation-post' $script:LogsRoot 1800;$script:PreservationStatus=if($postRecord.ExitCode -eq 0){'PASS'}else{'FAIL'}}catch{$script:PreservationStatus='FAIL'}
    }else{
        $proof=Get-CanonicalPreservationProof
        $script:PreservationStatus=if($null -ne $proof){$proof.Status}else{'MISSING'}
    }
    if($script:CandidateManifestPath -and (Test-Path -LiteralPath $script:CandidateManifestPath -PathType Leaf) -and -not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Guard.json'))){
        try{$manifest=Get-Content -LiteralPath $script:CandidateManifestPath -Raw|ConvertFrom-Json;$finalGuard=Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Guard.json';$finalInventory=Join-Path $script:PerformanceRunRoot 'candidate-inventory-final.json';$guardDir=Join-Path $script:PerformanceRunRoot 'guard-logs';$expected=$script:CandidateManifestHash;$gargs=@('-NoProfile','-File',$script:CandidateGuard,'-RepoRoot',$script:RepoRoot,'-CandidateRoot',$script:BatchRoot,'-CandidateManifestPath',$script:CandidateManifestPath,'-SourceInventoryPath',(Join-Path $script:ReviewRoot 'V22-Performance-Source-Inventory.json'),'-RuntimeInventoryPath',(Join-Path $script:ReviewRoot 'V22-Performance-Runtime-Inventory.json'),'-BuildAttestationPath',(Join-Path $script:ReviewRoot 'V22-Performance-Build-Attestation.json'),'-InventoryCheckPath',$finalInventory,'-OutputPath',$finalGuard,'-ExpectedManifestSHA256',$expected);$finalGuardRecord=Invoke-Tool (Join-Path $PSHOME 'pwsh.exe') $gargs $script:RepoRoot 'candidate-guard-final' $guardDir 600;$finalObj=Get-Content -LiteralPath $finalGuard -Raw|ConvertFrom-Json;$script:CandidateGuardFinalStatus=$finalObj.Status}catch{$script:CandidateGuardFinalStatus='FAIL'}
    }
    $proof=Get-CanonicalPreservationProof
    if($null -ne $proof -and $script:PreservationStatus -eq 'NOT_RUN'){$script:PreservationStatus=$proof.Status}
    $coreRows=@($script:IsolatedRows|Where-Object BlockKind -in @('C1','C2'));$c4Rows=@($script:IsolatedRows|Where-Object BlockKind -eq 'C4');$bdnRows=@($script:IsolatedRows|Where-Object BlockKind -eq 'BDN')
    $script:IsolatedGate=if($coreRows.Count -eq 12 -and @($coreRows|Where-Object Status -ne 'PASS').Count -eq 0 -and $script:PrePostStatus -match 'PASS' -and @($coreRows|Where-Object TelemetryStatus -ne 'TELEMETRY_VALID').Count -eq 0){'PASS'}elseif($coreRows.Count -eq 0){'NOT_RUN'}else{'FAIL'}
    if($script:MixedGate -eq 'NOT_RUN'){$script:MixedGate=if($script:IsolatedGate -eq 'PASS'){'NOT_RUN'}else{'SKIPPED_ISOLATED_GATE_FAILED'}}
    $isolatedTotal=[long]0;$isolatedFailed=[long]0;foreach($row in $script:IsolatedRows|Where-Object BlockKind -in @('C1','C2','C4')){if($null -ne $row.Requests){$isolatedTotal+=[long]$row.Requests};if($null -ne $row.Failed){$isolatedFailed+=[long]$row.Failed}}
    $mixedFailed=[long]0;foreach($row in $script:MixedRows){if($null -ne $row.Failed){$mixedFailed+=[long]$row.Failed}}
    $script:FinalCleanupProof=Get-OwnedProcessCleanupProof @($script:AllOwnedProcesses);$allCleanup=($script:FinalCleanupProof.Passed -and @($script:CleanupRows|Where-Object Status -eq 'CLEANUP_FAILED').Count -eq 0 -and @($script:AllOwnedProcesses|Where-Object{-not $_.Completed -or -not $_.ProcessGone -or $_.CleanupProofStatus -ne 'CLEAN'}).Count -eq 0)
    if($allCleanup){$cleanupStatus='PASS'}else{$cleanupStatus='FAIL'}
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
    $postPass=($script:PrePostStatus -ceq 'PASS' -and $null -ne $script:AfterIsolated -and $script:AfterIsolated.Status -ceq 'PASS' -and ($null -eq $script:AfterMixed -or $script:AfterMixed.Status -ceq 'PASS'))
    $isolatedCorePass=($coreRows.Count -eq 12 -and @($coreRows|Where-Object Status -ne 'PASS').Count -eq 0 -and $isolatedTotal -gt 0 -and $isolatedFailed -eq 0)
    $artifactsBeforeIndex=@(Get-ChildItem -LiteralPath $script:PerformanceRunRoot -File -Recurse -Force|ForEach-Object{[pscustomobject]@{RelativePath=$_.FullName.Substring($script:PerformanceRunRoot.TrimEnd('\').Length+1).Replace('\','/');Size=[long]$_.Length;SHA256=(Get-FileHashHex $_.FullName)}})
    $artifactIndexPath=Join-Path $script:PerformanceRunRoot 'raw-artifact-hashes.json'
    if(-not(Test-Path -LiteralPath $artifactIndexPath)){try{Write-NewJson $artifactIndexPath ([ordered]@{SchemaVersion='warehouse-benchmark-v22-performance-raw-artifact-hashes/1';RunId=$script:RunId;Count=$artifactsBeforeIndex.Count;Entries=$artifactsBeforeIndex})}catch{}}
    $artifactIndexHash=if(Test-Path $artifactIndexPath){Get-FileHashHex $artifactIndexPath}else{$null}
    $script:OverallStatus=if($script:RecoveryDiagnosticStatus -eq 'RECOVERY_TARGETED_DIAGNOSTIC_FAILED'){'RECOVERY_TARGETED_DIAGNOSTIC_FAILED'}elseif($candidatePass -and $correctnessPass -and $script:Preflight.SemanticScenarios -ceq '6/6' -and $script:Preflight.TotalCount -ceq '6/6' -and $script:RecoveryDiagnosticStatus -ceq 'PASS' -and $isolatedCorePass -and $c4Pass -and $bdnPass -and $isolatedTelemetry -and $mixedPass -and $telemetryPass -and $postPass -and $allCleanup -and $preservationPass -and $comparatorPass){'BATCH3_PASS'}elseif($script:IsolatedRows.Count -gt 0 -or $script:MixedLevels.Count -gt 0){'BATCH3_PARTIAL'}else{'BATCH3_NOT_READY'}
    $isolatedTotals=[ordered]@{Status=if($isolatedCorePass -and $c4Pass -and $isolatedTelemetry){'PASS'}elseif($script:IsolatedRows.Count -gt 0){'PARTIAL'}else{'NOT_RUN'};RunId=$script:RunId;CandidateId=$script:CandidateId;CoreRowsExpected=12;CoreRowsValid=@($coreRows|Where-Object Status -eq 'PASS').Count;CoreRows=$coreRows;C4RowsExpected=6;C4RowsValid=@($c4Rows|Where-Object Status -eq 'PASS').Count;C4Rows=$c4Rows;TotalNBomberRequests=$isolatedTotal;TotalNBomberFailed=$isolatedFailed;TelemetryValid=($isolatedTelemetry -and $telemetryPass);PrePostStatus=$script:PrePostStatus;StartedUtc=if($null -ne $isolatedManifest){$isolatedManifest.StartedUtc}else{$null};FinishedUtc=if($null -ne $isolatedManifest){$isolatedManifest.FinishedUtc}else{$null};ArtifactsRoot=(Join-Path $script:PerformanceRunRoot 'isolated')}
    $bdnOut=[ordered]@{Status=if($bdnPass){'PASS'}elseif($bdnRows.Count -gt 0){'PARTIAL'}else{'NOT_RUN'};ExpectedScenarios=6;ValidScenarios=@($bdnRows|Where-Object Status -eq 'PASS').Count;Rows=$bdnRows;MeasurementCountSource='Counted actual WorkloadActual evidence and reconciled with reported N; no assumed measured-row count'}
    $mixedOut=[ordered]@{Status=if($mixedPass){'PASS'}elseif($script:MixedLevels.Count -gt 0){'PARTIAL'}else{'SKIPPED'};Levels=$script:MixedLevels.ToArray();Workers=$script:MixedRows.ToArray();FailedRequests=$mixedFailed;Interpretation='L8 is standalone mixed load; aggregate RPS is DESCRIPTIVE_SUM_OF_SCENARIO_RPS'}
    $telemetryOut=[ordered]@{Status=if($telemetryPass){'PASS'}elseif($script:TelemetryAssessments.Count -gt 0){'INVALID'}else{'NOT_RUN'};RequiredRule='At least one valid target sample; exact run/block/database/database-id; any error/rejected row invalidates the block';Assessments=$script:TelemetryAssessments.ToArray();DiagnosticOnly=@('ActiveRequestLogicalReadsDiagnostic','TempdbServerUsedKBDiagnostic','MemoryGrantsPendingCounterDiagnostic','DeadlockCounterDiagnostic')}
    $prepost=[ordered]@{Status=$script:PrePostStatus;CorrectnessPreflight=$script:Preflight;AfterIsolated=$script:AfterIsolated;AfterMixed=$script:AfterMixed;IsolatedPreSnapshot=$script:isolatedBeginSnapshot;IsolatedPostSnapshot=if($null -ne $script:AfterIsolated){$script:AfterIsolated.DBEvidence.Post.SnapshotSha256}else{$null};IsolatedTransitionEqual=($null -ne $script:AfterIsolated -and $script:isolatedBeginSnapshot -ceq $script:AfterIsolated.DBEvidence.Pre.SnapshotSha256);MixedTransitionEqual=($null -eq $script:AfterMixed -or ($null -ne $script:AfterIsolated -and $script:AfterIsolated.DBEvidence.Post.SnapshotSha256 -ceq $script:AfterMixed.DBEvidence.Pre.SnapshotSha256));FinalCorrectnessPass=$postPass;FullDatasetValueEquality='NOT_VERIFIED'}
    $cleanupOut=[ordered]@{Status=$cleanupStatus;ProcessCount=$script:AllOwnedProcesses.Count;FinalCleanupProcessProof=$script:FinalCleanupProof;OwnedProcesses=@($script:AllOwnedProcesses|ForEach-Object{[pscustomobject]@{Label=$_.Label;Role=$_.Role;ChildProcessId=$_.ChildProcessId;ExitCode=$_.ExitCode;TimedOut=$_.TimedOut;KilledForHostLimit=$_.KilledForHostLimit;CleanupStatus=$_.CleanupStatus;Completed=$_.Completed}});CleanupRows=$script:CleanupRows.ToArray();ResidueFinal='See final bounded correctness snapshot and per-block cooldown residue evidence';OrphanOwnedProcessCount=@($script:AllOwnedProcesses|Where-Object{-not $_.ProcessGone -or $_.CleanupProofStatus -ne 'CLEAN'}).Count;SQLSessionAttribution='NOT_VERIFIED; residue contract covers active target-database requests, grants and resource-semaphore waiters'}
    $limitations=[ordered]@{SchemaVersion='warehouse-benchmark-v22-performance-known-limitations/1';HistoricalTimeoutCause='NOT_VERIFIED';HistoricalTimeoutDetails='Earlier DocumentPaged/DetailReportPaged timeout cause is not identified by successful current execution.';FullDatasetValueEquality='NOT_VERIFIED';HistoricalComparison='SINGLE-WINDOW HISTORICAL COMPARISON';CausalAttribution='NOT_ESTABLISHED';PerformanceSla='NO_SLA_DEFINED';SqlServerCounterCausality='DIAGNOSTIC_ONLY';BoundedCorrectnessEquality='Does not establish full 10-million-row value equality';NewLimitations=@(if($script:Failure){$script:Failure.Message};if($script:StopReason){$script:StopReason})}
    $coreGate=[ordered]@{SchemaVersion='warehouse-benchmark-v22-core-evidence-gate/1';RunId=$script:RunId;Candidate=[ordered]@{Status=if($candidatePass){'PASS'}elseif($null -ne $manifest){'INVALID'}else{'MISSING'};CandidateId=$script:CandidateId;ManifestSHA256=$candidateHash;InitialGuard=$script:CandidateGuardInitialStatus;FinalGuard=$script:CandidateGuardFinalStatus};Correctness=[ordered]@{Status=if($correctnessPass){'PASS'}elseif($script:PreflightStatus -eq 'FAIL'){'FAIL'}else{'NOT_RUN'};Preflight=$script:Preflight};Isolated=[ordered]@{Status=if($isolatedCorePass -and $c4Pass -and $bdnPass){'PASS'}elseif($script:IsolatedRows.Count -gt 0){'PARTIAL'}else{'NOT_RUN'};CoreRowsValid=@($coreRows|Where-Object Status -eq 'PASS').Count;CoreRowsRequired=12;C4RowsValid=@($c4Rows|Where-Object Status -eq 'PASS').Count;C4RowsRequired=6;BdnRowsValid=@($bdnRows|Where-Object Status -eq 'PASS').Count;BdnRowsRequired=6};Mixed=[ordered]@{Status=$script:MixedGate;LevelStatuses=@($script:MixedLevels|ForEach-Object{[pscustomobject]@{Level=$_.Level;Status=$_.Status;OverlapValid=$_.OverlapValid;TelemetryStatus=$_.TelemetryStatus}})};Telemetry=[ordered]@{Status=if($telemetryPass){'PASS'}elseif($script:TelemetryAssessments.Count -gt 0){'INVALID'}else{'NOT_RUN'};ValidBlocks=@($script:TelemetryAssessments|Where-Object Status -eq 'TELEMETRY_VALID').Count;TotalAssessments=$script:TelemetryAssessments.Count};PostState=[ordered]@{Status=if($postPass){'PASS'}else{$script:PrePostStatus};FullDatasetValueEquality='NOT_VERIFIED'};Cleanup=[ordered]@{Status=$cleanupStatus;OwnedProcessCount=$script:AllOwnedProcesses.Count};Artifacts=[ordered]@{Status=if($artifactsBeforeIndex.Count -gt 0 -and $artifactIndexHash){'PASS'}else{'PARTIAL'};RawArtifactCount=$artifactsBeforeIndex.Count;RawArtifactIndexPath=$artifactIndexPath;RawArtifactIndexSHA256=$artifactIndexHash};Comparator=[ordered]@{Status=$script:ComparatorStatus;IdentityPath=(Join-Path $script:ReviewRoot 'V21-Comparator-Identity.json')};Preservation=[ordered]@{Status=$script:PreservationStatus;ProofPath=(Join-Path $script:ReviewRoot 'Previous-Evidence-Preservation.json')};OverallStatus=$script:OverallStatus;CoreEvidenceReady=($script:OverallStatus -eq 'BATCH3_PASS');ReadyForBatch4=($script:OverallStatus -eq 'BATCH3_PASS');SlaStatus='NO_SLA_DEFINED';RecordedUtc=[DateTime]::UtcNow.ToString('o')}
    $reviewMap=[ordered]@{
        'V22-Performance-Correctness-Preflight.json'=$(if($null -ne $script:Preflight){$script:Preflight}else{@{Status='SKIPPED';Reason=$script:Failure.Message}})
        'V22-Isolated-Run-Manifest.json'=$(if($null -ne $isolatedManifest){$isolatedManifest}else{@{Status='SKIPPED';Reason='Candidate/correctness gate stopped before isolated run'}})
        'V22-Isolated-Results.json'=$isolatedTotals
        'V22-Isolated-Telemetry-Summary.json'=$telemetryOut
        'V22-BDN-Results.json'=$bdnOut
        'V22-Mixed-Run-Manifest.json'=$mixedManifest
        'V22-Mixed-Results.json'=$mixedOut
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
    if(-not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'Batch3-Acceptance.json'))){try{Write-NewJson (Join-Path $script:ReviewRoot 'Batch3-Acceptance.json') $acceptance}catch{}}
    if(-not(Test-Path -LiteralPath (Join-Path $script:ReviewRoot 'Batch3-Changed-Files.txt'))){
        $lines=[Collections.Generic.List[string]]::new();$lines.Add('Warehouse Benchmark 2.2 Batch 3 source changes relative to the pre-Batch-3 inventory.');$preInventoryPath=Join-Path $script:LogsRoot 'source-inventory-prebatch3.json';$newInventoryPath=Join-Path $script:ReviewRoot 'V22-Performance-Source-Inventory.json'
        if((Test-Path $preInventoryPath) -and (Test-Path $newInventoryPath)){$before=Get-Content $preInventoryPath -Raw|ConvertFrom-Json;$after=Get-Content $newInventoryPath -Raw|ConvertFrom-Json;$old=@{};foreach($e in $before.Entries){$old[[string]$e.NormalizedRelativePath]=$e};$new=@{};foreach($e in $after.Entries){$new[[string]$e.NormalizedRelativePath]=$e};foreach($path in @($new.Keys|Sort-Object -CaseSensitive)){if(-not$old.ContainsKey($path)){$lines.Add("CREATED`t$($script:RepoRoot)\$($path.Replace('/','\'))")}elseif($old[$path].SHA256 -cne $new[$path].SHA256){$lines.Add("MODIFIED`t$($script:RepoRoot)\$($path.Replace('/','\'))")}};foreach($path in @($old.Keys|Sort-Object -CaseSensitive)){if(-not$new.ContainsKey($path)){$lines.Add("DELETED`t$($script:RepoRoot)\$($path.Replace('/','\'))")}}}else{$lines.Add('NOT_VERIFIED`tPre-Batch-3 inventory comparison unavailable')}
        $lines.Add("EVIDENCE_ROOT`t$($script:BatchRoot)");Write-NewText (Join-Path $script:ReviewRoot 'Batch3-Changed-Files.txt') ($lines -join [Environment]::NewLine)
    }
    $requestFiles=@('Batch3-Implementation-Report.md','Batch3-Acceptance.json','Batch3-Changed-Files.txt','Previous-Evidence-Preservation.json','V22-Prior-Attempt-Supersession.json','V22-Performance-Protocol.json','V22-Performance-Source-Inventory.json','V22-Performance-Source-Snapshot.json','V22-Performance-Build-Attestation.json','V22-Performance-Runtime-Inventory.json','V22-Performance-Candidate-Manifest.json','V22-Performance-Candidate-Guard.json','V22-Performance-Correctness-Preflight.json','V21-Comparator-Identity.json','V21-vs-V22-Toolchain-Comparison.json','V22-Isolated-Run-Manifest.json','V22-Isolated-Results.json','V22-Isolated-Telemetry-Summary.json','V21-vs-V22-Isolated-Comparison.json','V22-BDN-Results.json','V22-Mixed-Run-Manifest.json','V22-Mixed-Results.json','V22-Mixed-Telemetry-Summary.json','V22-Isolated-vs-Mixed-Comparison.json','V22-Performance-PrePost-Evidence.json','V22-Cleanup-Residue-Evidence.json','V22-Performance-Known-Limitations.json','V22-Core-Evidence-Gate.json','Batch3-Review-Index.md')
    foreach($name in $requestFiles|Where-Object{$_ -notin @('Batch3-Implementation-Report.md','Batch3-Review-Index.md')}){Ensure-ReviewArtifact $name ('No complete evidence was produced; current stage='+$script:CurrentStage)}
    $candidateManifest=if(Test-Path $manifestPath){Get-Content $manifestPath -Raw|ConvertFrom-Json}else{$null}
    $v21Nb=@();$v21Bdn=@();if($script:ComparatorStatus -eq 'PASS'){$v21Nb=Import-Csv 'P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_1_ADAPTIVE-20260916-FINAL\v2.1-nbomber-summary.csv' -Delimiter ',';$v21Bdn=Import-Csv 'P:\Warehouse-Benchmark-V2\WAREHOUSE_BENCHMARK_V2_1_ADAPTIVE-20260916-FINAL\v2.1-bdn-summary.csv' -Delimiter ','}
    $comparisonRows=[Collections.Generic.List[object]]::new()
    foreach($row in $script:IsolatedRows|Where-Object BlockKind -in @('C1','C2','C4')){
        $profile=if($row.Copies -eq 1){'CORE_NBOMBER'}elseif($row.Copies -eq 2){'CORE_NBOMBER'}else{'EXTENDED_STANDARD'}
        $old=$v21Nb|Where-Object{$_.Scenario -ceq $row.Scenario -and [int]$_.Level -eq $row.Copies -and $_.Profile -ceq $profile}|Select-Object -First 1
        $comparisonRows.Add([pscustomobject]@{Scenario=$row.Scenario;Copies=$row.Copies;HistoricalStatus=if($old){$old.Status}else{'NOT_VERIFIED'};HistoricalMeanMs=if($old){ConvertTo-FiniteDouble $old.MeanMs 'HistoricalMean'}else{$null};CurrentMeanMs=$row.MeanMs;MeanDeltaPct=if($old -and $row.MeanMs -ne $null){Get-Delta (ConvertTo-FiniteDouble $old.MeanMs 'HistoricalMean') ([double]$row.MeanMs)}else{$null};HistoricalP50Ms=if($old){ConvertTo-FiniteDouble $old.P50Ms 'HistoricalP50'}else{$null};CurrentP50Ms=$row.P50Ms;P50DeltaPct=if($old -and $row.P50Ms -ne $null){Get-Delta (ConvertTo-FiniteDouble $old.P50Ms 'HistoricalP50') ([double]$row.P50Ms)}else{$null};HistoricalP95Ms=if($old){ConvertTo-FiniteDouble $old.P95Ms 'HistoricalP95'}else{$null};CurrentP95Ms=$row.P95Ms;P95DeltaPct=if($old -and $row.P95Ms -ne $null){Get-Delta (ConvertTo-FiniteDouble $old.P95Ms 'HistoricalP95') ([double]$row.P95Ms)}else{$null};HistoricalP99Ms=if($old){ConvertTo-FiniteDouble $old.P99Ms 'HistoricalP99'}else{$null};CurrentP99Ms=$row.P99Ms;P99DeltaPct=if($old -and $row.P99Ms -ne $null){Get-Delta (ConvertTo-FiniteDouble $old.P99Ms 'HistoricalP99') ([double]$row.P99Ms)}else{$null};HistoricalRPS=if($old){ConvertTo-FiniteDouble $old.Rps 'HistoricalRPS'}else{$null};CurrentRPS=$row.RPS;RPSDeltaPct=if($old -and $row.RPS -ne $null){Get-Delta (ConvertTo-FiniteDouble $old.Rps 'HistoricalRPS') ([double]$row.RPS)}else{$null};Interpretation='DESCRIPTIVE_ONLY_SOURCE_AND_ENVIRONMENT_COMPARISON'})
    }
    $historicalComparison=[ordered]@{SchemaVersion='warehouse-benchmark-v21-vs-v22-isolated-comparison/1';Status=if($comparisonRows.Count -gt 0){'DESCRIPTIVE'}else{'NOT_AVAILABLE'};Rows=$comparisonRows.ToArray();NoSlaThreshold='NO_SLA_DEFINED';StatisticalLimitation='SINGLE-WINDOW HISTORICAL COMPARISON';Causality='NOT_ESTABLISHED'}
    if(-not(Test-Path (Join-Path $script:ReviewRoot 'V21-vs-V22-Isolated-Comparison.json'))){try{Write-NewJson (Join-Path $script:ReviewRoot 'V21-vs-V22-Isolated-Comparison.json') $historicalComparison}catch{}}
    $mixedComparison=[Collections.Generic.List[object]]::new()
    foreach($levelResult in $script:MixedLevels){$workers=[int]$levelResult.WorkersPerScenario;$isolatedCopies=if($workers -in @(1,2,4)){$workers}else{$null};foreach($worker in @($levelResult.WorkerRows)){$iso=$script:IsolatedRows|Where-Object{$_.BlockKind -in @('C1','C2','C4') -and $_.Scenario -ceq $worker.Scenario -and $_.Copies -eq $isolatedCopies}|Select-Object -First 1;$mixedComparison.Add([pscustomobject]@{Level=$levelResult.Level;Scenario=$worker.Scenario;MixedCopies=$workers;ComparisonProfile=if($workers -eq 8){'STANDALONE_MIXED_LOAD_LEVEL'}else{'MIXED_VS_ISOLATED_C'+$workers};IsolatedMeanMs=if($iso){$iso.MeanMs}else{$null};MixedMeanMs=$worker.MeanMs;MeanDeltaPct=if($iso -and $iso.MeanMs -and $worker.MeanMs){Get-Delta ([double]$iso.MeanMs) ([double]$worker.MeanMs)}else{$null};IsolatedP95Ms=if($iso){$iso.P95Ms}else{$null};MixedP95Ms=$worker.P95Ms;P95DeltaPct=if($iso -and $iso.P95Ms -and $worker.P95Ms){Get-Delta ([double]$iso.P95Ms) ([double]$worker.P95Ms)}else{$null};IsolatedP99Ms=if($iso){$iso.P99Ms}else{$null};MixedP99Ms=$worker.P99Ms;P99DeltaPct=if($iso -and $iso.P99Ms -and $worker.P99Ms){Get-Delta ([double]$iso.P99Ms) ([double]$worker.P99Ms)}else{$null};IsolatedRPS=if($iso){$iso.RPS}else{$null};MixedRPS=$worker.RPS;RPSDeltaPct=if($iso -and $iso.RPS -and $worker.RPS){Get-Delta ([double]$iso.RPS) ([double]$worker.RPS)}else{$null};Interpretation=if($workers -eq 8){'STANDALONE_MIXED_LOAD_LEVEL'}else{'DESCRIPTIVE_ONLY_NO_SLA_THRESHOLD'}})}}
    if(-not(Test-Path (Join-Path $script:ReviewRoot 'V22-Isolated-vs-Mixed-Comparison.json'))){try{Write-NewJson (Join-Path $script:ReviewRoot 'V22-Isolated-vs-Mixed-Comparison.json') ([ordered]@{SchemaVersion='warehouse-benchmark-v22-isolated-vs-mixed-comparison/1';Status=if($mixedComparison.Count -gt 0){'DESCRIPTIVE'}else{'NOT_AVAILABLE'};Rows=$mixedComparison.ToArray();AggregateRPS='DESCRIPTIVE_SUM_OF_SCENARIO_RPS';NoSlaThreshold='NO_SLA_DEFINED';Causality='NOT_ESTABLISHED'})}catch{}}
    $script:CurrentStage='FINAL_REPORT_AND_REVIEW_PACK';Write-RunProgress $script:CurrentStage $script:OverallStatus
    if($script:OverallStatus -eq 'BATCH3_PASS'){$headline='BATCH3_PASS; measurements are valid and descriptive. No performance SLA conclusion is made.'}elseif($script:OverallStatus -eq 'BATCH3_PARTIAL'){$headline='BATCH3_PARTIAL; valid rows are retained, but mandatory acceptance gates did not all pass.'}else{$headline='BATCH3_NOT_READY; the required candidate/correctness/performance gates did not complete.'}
    $report=[Collections.Generic.List[string]]::new();$report.Add('# Warehouse Benchmark 2.2 Batch 3 Implementation Report');$report.Add('');$report.Add("**Verdict:** $headline");$report.Add('');$report.Add('## 1. Executive summary');$report.Add('');$priorAttemptPath=Join-Path $script:ReviewRoot 'V22-Prior-Attempt-Supersession.json';if(Test-Path -LiteralPath $priorAttemptPath){$priorAttempt=Get-Content -LiteralPath $priorAttemptPath -Raw|ConvertFrom-Json;$priorAttemptRecords=if($priorAttempt.PSObject.Properties['Attempts']){@($priorAttempt.Attempts)}else{@()};if($priorAttemptRecords.Count -gt 0){foreach($priorRecord in $priorAttemptRecords){$priorRunIdText=if($priorRecord.PSObject.Properties['RunId']){[string]$priorRecord.RunId}else{'UNKNOWN'};$priorCandidateText=if($priorRecord.PSObject.Properties['CandidateId']){[string]$priorRecord.CandidateId}else{'UNKNOWN'};$priorStatusText=if($priorRecord.PSObject.Properties['Status']){[string]$priorRecord.Status}else{'UNKNOWN'};$priorFailureStageText=if($priorRecord.PSObject.Properties['FailureStage']){[string]$priorRecord.FailureStage}else{'NOT_RECORDED'};$report.Add("Prior attempt: run=$priorRunIdText; candidate=$priorCandidateText; status=$priorStatusText; stage=$priorFailureStageText.")}}elseif($priorAttempt.PSObject.Properties['RunId']){$report.Add("Prior attempt: run=$($priorAttempt.RunId); candidate=$($priorAttempt.SupersededCandidateId); status=$($priorAttempt.Status).")}else{$report.Add('Prior-attempt metadata has no structured attempt identity; see supersession evidence JSON.')};$priorRootText=if($priorAttempt.PSObject.Properties['CurrentContinuationRoot']){[string]$priorAttempt.CurrentContinuationRoot}elseif($priorAttempt.PSObject.Properties['PreviousAttemptRoot']){[string]$priorAttempt.PreviousAttemptRoot}else{'NOT_RECORDED'};$report.Add("Prior attempt evidence: $priorAttemptPath; continuation root: $priorRootText.");$report.Add('')}$report.Add("Run: ``$($script:RunId)``. Candidate: ``$($script:CandidateId)``. Source snapshot: ``$($script:SourceSnapshotId)``.");$report.Add('');$report.Add('The run uses the exact frozen 2.2 candidate, bounded correctness preflight, isolated C1/C2/C4 and supplemental BDN, followed by mixed L1/L2/L4/L8 only when the isolated core gate passes. Comparisons are descriptive; no SLA threshold or causal attribution is asserted.');$report.Add('');$report.Add('## 2. Performance candidate identity');$report.Add('');$report.Add("- Candidate manifest: ``$(Join-Path $script:ReviewRoot 'V22-Performance-Candidate-Manifest.json')``");$report.Add("- Candidate manifest SHA-256: ``$candidateHash``");$report.Add("- Benchmark DLL SHA-256: ``$(if($candidateManifest){$candidateManifest.Binary.BenchmarkDll.SHA256}else{'NOT_CREATED'})``");$report.Add("- Data Access DLL SHA-256: ``$(if($candidateManifest){$candidateManifest.Binary.DataAccessDll.SHA256}else{'NOT_CREATED'})``");$report.Add("- Source inventory entries/hash: ``$(if($candidateManifest){$candidateManifest.Source.InventoryEntryCount}else{0})`` / ``$(if($candidateManifest){$candidateManifest.Source.InventorySHA256}else{'NOT_CREATED'})``");$report.Add("- Runtime inventory entries/hash: ``$(if($candidateManifest){$candidateManifest.Runtime.EntryCount}else{0})`` / ``$(if($candidateManifest){$candidateManifest.Runtime.InventorySHA256}else{'NOT_CREATED'})``");$report.Add("- Build logs: ``$(Join-Path $script:LogsRoot 'restore-a.log')``, ``$(Join-Path $script:LogsRoot 'build-a-release.log')``, ``$(Join-Path $script:LogsRoot 'restore-b.log')``, ``$(Join-Path $script:LogsRoot 'build-b-release.log')``.");$report.Add('');$report.Add('## 3. Batch 2 preservation');$report.Add('');$report.Add("Preservation status: **$($script:PreservationStatus)**. Proof: ``$(Join-Path $script:ReviewRoot 'Previous-Evidence-Preservation.json')``. PRE detail: ``$(Join-Path $script:LogsRoot 'preservation-pre-files.json')``; POST detail: ``$(Join-Path $script:LogsRoot 'preservation-post-files.json')``.");$report.Add('');$report.Add('## 4. Legacy workload contract');$report.Add('');$report.Add('Six legacy read scenarios use the Batch 2 Data Access path and unchanged 2.1 parameters: page 1, size 10; PERF_USER for authenticated methods; null warehouse; report dates 2025-01-01 through 2026-12-31; Historical mode LEGACY; historical current-balance switch false; current-balance switch true. No direct SQL replaced the timed Data Access path.');$report.Add('');$report.Add('## 5. 2.1 comparator identity');$report.Add('');$report.Add("Comparator status: **$($script:ComparatorStatus)**. Identity: ``$(Join-Path $script:ReviewRoot 'V21-Comparator-Identity.json')``. Single-window historical limitation retained.");$report.Add('');$report.Add('## 6. Environment/toolchain comparison');$report.Add('');$report.Add("Evidence: ``$(Join-Path $script:ReviewRoot 'V21-vs-V22-Toolchain-Comparison.json')``. Unverified values remain marked NOT_VERIFIED; comparison means source plus recorded environment.");$report.Add('');$report.Add('## 7. Correctness preflight');$report.Add('');$report.Add("Status: **$($script:PreflightStatus)**; scenarios passed: $($script:Preflight.SemanticScenariosCompleted)/6; Total_Count: $($script:Preflight.TotalCountPassed)/6. Evidence: ``$(Join-Path $script:ReviewRoot 'V22-Performance-Correctness-Preflight.json')``. Exact performance runtime was used.");$report.Add('');$report.Add('## 8. Isolated methodology and results');$report.Add('');$report.Add('Order per scenario: BDN, C1, C2, C4. NBomber KeepConstant; 3-second warmup; 15-second measured duration; no per-request retry. C1/C2 form 12 core rows; C4 forms six supplemental rows.');$report.Add('');$report.Add("Core valid rows: $(@($coreRows|Where-Object Status -eq 'PASS').Count)/12. C4 valid: $(@($c4Rows|Where-Object Status -eq 'PASS').Count)/6. Requests: $isolatedTotal; failed: $isolatedFailed. Isolated output: ``$(Join-Path $script:ReviewRoot 'V22-Isolated-Results.json')``.");$report.Add('');foreach($row in $script:IsolatedRows){$report.Add("- $($row.BlockKind) $($row.Scenario) C$($row.Copies): $($row.Status); requests=$($row.Requests); failed=$($row.Failed); RPS=$($row.RPS); mean/P95/P99 ms=$($row.MeanMs)/$($row.P95Ms)/$($row.P99Ms); telemetry=$($row.TelemetryStatus); reason=$($row.Reason)")};$report.Add('');$report.Add('## 9. 2.1 versus 2.2 descriptive isolated comparison');$report.Add('');$report.Add("Evidence: ``$(Join-Path $script:ReviewRoot 'V21-vs-V22-Isolated-Comparison.json')``. Values are not labeled regression/improvement and do not establish cause.");$report.Add('');$report.Add('## 10. BDN results');$report.Add('');$report.Add("Valid scenarios: $(@($bdnRows|Where-Object Status -eq 'PASS').Count)/6. Actual measurement rows were read from WorkloadActual log lines and reconciled with reported N and the summary IterationCount. Evidence: ``$(Join-Path $script:ReviewRoot 'V22-BDN-Results.json')``.");$report.Add('');$report.Add('## 11. Mixed methodology and results');$report.Add('');$report.Add('Six scenario-specific child processes run concurrently at each level. L1/L2/L4/L8 configure 1/2/4/8 copies per scenario (6/12/24/48 total). Each level requires six valid worker windows and at least 10 seconds common overlap. L8 is standalone mixed evidence. Aggregate RPS is the descriptive sum of scenario RPS, not a mathematically aligned common-window rate.');$report.Add('');foreach($levelResult in $script:MixedLevels){$report.Add("- $($levelResult.Level): $($levelResult.Status); total workers=$($levelResult.TotalWorkers); requests=$($levelResult.Requests); failed=$($levelResult.Failed); overlap=$($levelResult.WindowOverlapSeconds)s; telemetry=$($levelResult.TelemetryStatus); cleanup=$($levelResult.CleanupStatus)")};$report.Add('');$report.Add("Mixed comparison: ``$(Join-Path $script:ReviewRoot 'V22-Isolated-vs-Mixed-Comparison.json')``.");$report.Add('');$report.Add('## 12. Telemetry validity');$report.Add('');$report.Add("Status: $($telemetryOut.Status); valid target samples are required, and any error/rejected row invalidates a block. SQL logical-read/tempdb/deadlock counters are diagnostic only. Raw telemetry is under ``$($script:PerformanceRunRoot)``.");$report.Add('');$report.Add('## 13. Host admission and cooldown');$report.Add('');$report.Add("Host floor=512 MB; emergency floor=128 MB; safe predicted minimum >640 MB; two consecutive one-second admission samples; cooldown 5–60 seconds with clean target DB residue. Raw host sampling: ``$($script:HostLogPath)``.");$report.Add('');$report.Add('## 14. PRE/POST state');$report.Add('');$report.Add("Status: $($script:PrePostStatus). Evidence: ``$(Join-Path $script:ReviewRoot 'V22-Performance-PrePost-Evidence.json')``. FULL DATASET VALUE EQUALITY: NOT_VERIFIED.");$report.Add('');$report.Add('## 15. Cleanup and residue');$report.Add('');$report.Add("Status: $cleanupStatus. Evidence: ``$(Join-Path $script:ReviewRoot 'V22-Cleanup-Residue-Evidence.json')``. Child process IDs are owned-runner IDs only; SQL session attribution: NOT_VERIFIED.");$report.Add('');$report.Add('## 16. Known limitations');$report.Add('');$report.Add('Historical DocumentPaged/DetailReportPaged timeout cause: NOT VERIFIED. Full 10-million-row value equality: NOT_VERIFIED. Historical comparison is single-window. No SLA threshold, confidence interval, or causal attribution is asserted.');$report.Add('');$report.Add('## 17. Phase 8A evidence gate');$report.Add('');$report.Add("Overall: $($script:OverallStatus). Machine gate: ``$(Join-Path $script:ReviewRoot 'V22-Core-Evidence-Gate.json')``. Raw artifact hash index: ``$artifactIndexPath`` (SHA-256 ``$artifactIndexHash``).");$report.Add('');$report.Add('## 18. Findings and deferred work');$report.Add('');$report.Add("Stop reason: $(if($script:StopReason){$script:StopReason}else{'NONE'}). Stage failure: $(if($script:Failure){$script:Failure.Message}else{'NONE'}). Batch 4 extended CRUD/write, posting, reservation, worker, and read/write contention remain out of scope. No business source, SQL source, or DB data was modified by this task.");$report.Add('');$report.Add('## 19. Final verdict and Batch 4 readiness');$report.Add('');$report.Add("- Verdict: **$($script:OverallStatus)**");$report.Add("- LEGACY_REGRESSION_MEASUREMENT_VALID: **$(if($isolatedCorePass -and $c4Pass -and $isolatedTelemetry -and $postPass){'YES'}else{'NO'})**");$report.Add("- MIXED_REGRESSION_MEASUREMENT_VALID: **$(if($mixedPass){'YES'}else{'NO'})**");$report.Add("- CORE_EVIDENCE_READY: **$(if($script:OverallStatus -eq 'BATCH3_PASS'){'YES'}else{'NO'})**");$report.Add("- READY_FOR_BATCH4: **$(if($script:OverallStatus -eq 'BATCH3_PASS'){'YES'}else{'NO'})**");$report.Add('');$report.Add("Failure evidence, if any: ``$(Join-Path $script:PerformanceRunRoot 'fatal-summary.json')``.")
    $reportPath=Join-Path $script:ReviewRoot 'Batch3-Implementation-Report.md';if(-not(Test-Path $reportPath)){try{Write-NewText $reportPath ($report -join [Environment]::NewLine)}catch{}}
    $indexPath=Join-Path $script:ReviewRoot 'Batch3-Review-Index.md'
    if(Test-Path $indexPath){$indexBackup=Join-Path $script:PerformanceRunRoot 'Batch3-Review-Index-preexisting.txt';try{Copy-Item -LiteralPath $indexPath -Destination $indexBackup}catch{}}else{
        $index=[Collections.Generic.List[string]]::new();$index.Add('# Batch 3 Review Index');$index.Add('');$index.Add("Verdict: **$($script:OverallStatus)**");$index.Add("Candidate ID: ``$($script:CandidateId)``");$index.Add("Source snapshot: ``$($script:SourceSnapshotId)``");$index.Add("Frozen runtime: ``$(if($candidateManifest){$candidateManifest.Runtime.Root}else{'NOT_CREATED'})``");$index.Add('');$index.Add('## Read first');$index.Add('');$index.Add('1. Batch3-Implementation-Report.md');$index.Add('2. Batch3-Acceptance.json');$index.Add('3. V22-Core-Evidence-Gate.json');$index.Add('4. V22-Performance-Candidate-Manifest.json and V22-Performance-Candidate-Guard.json');$index.Add('5. V22-Performance-Correctness-Preflight.json and V22-Performance-PrePost-Evidence.json');$index.Add('6. Isolated/Mixed/BDN results and comparator identities');$index.Add('');$index.Add('## Review artifacts and SHA-256');$index.Add('');foreach($name in $requestFiles|Where-Object{$_ -ne 'Batch3-Review-Index.md'}){$path=Join-Path $script:ReviewRoot $name;$hash=if(Test-Path $path){Get-FileHashHex $path}else{'MISSING'};$index.Add("- ``$name`` — ``$hash``")};$index.Add('');$index.Add('## Build and raw logs');$index.Add('');foreach($name in @('dotnet-info.log','msbuild-version.log','restore-a.log','build-a-release.log','restore-b.log','build-b-release.log','runtime-freeze.log','candidate-guard.log')){$index.Add("- ``$(Join-Path $script:LogsRoot $name)``")};$index.Add("- Raw run artifacts: ``$($script:PerformanceRunRoot)``");$index.Add("- Raw artifact index SHA-256: ``$artifactIndexHash``");$index.Add('');$index.Add('ZIP contains the required review artifacts plus prior-attempt supersession evidence; frozen runtime binaries are excluded.');Write-NewText $indexPath ($index -join [Environment]::NewLine)
    }
    $zipPath=Join-Path $script:BatchRoot 'Batch3-Review-Pack.zip';$zipHashPath=Join-Path $script:BatchRoot 'Batch3-Review-Pack-SHA256.txt'
    if(-not(Test-Path $zipPath)){$zipSources=@($requestFiles|ForEach-Object{Join-Path $script:ReviewRoot $_}|Where-Object{Test-Path -LiteralPath $_});Compress-Archive -LiteralPath $zipSources -DestinationPath $zipPath -CompressionLevel Optimal}
    if(-not(Test-Path $zipHashPath)){$zipHash=Get-FileHashHex $zipPath;Write-NewText $zipHashPath ($zipHash+'  Batch3-Review-Pack.zip'+[Environment]::NewLine)}else{$zipHash=(Get-Content $zipHashPath -TotalCount 1).Split(' ')[0]}
    if(Test-Path (Join-Path $script:PerformanceRunRoot 'progress.json')){Write-RunProgress 'COMPLETE' $script:OverallStatus $(if($script:Failure){$script:Failure.Message}else{$script:StopReason})}
}

[pscustomobject]@{Verdict=$script:OverallStatus;CandidateId=$script:CandidateId;SourceSnapshotId=$script:SourceSnapshotId;BenchmarkDllSHA256=if($manifest){$manifest.Binary.BenchmarkDll.SHA256}else{$null};DataAccessDllSHA256=if($manifest){$manifest.Binary.DataAccessDll.SHA256}else{$null};SourceInventoryCount=if($manifest){$manifest.Source.InventoryEntryCount}else{0};RuntimeInventoryCount=if($manifest){$manifest.Runtime.EntryCount}else{0};CorrectnessPass=if($null -ne $script:Preflight){"$($script:Preflight.SemanticScenariosCompleted)/6"}else{'0/6'};IsolatedCoreValid=@($coreRows|Where-Object Status -eq 'PASS').Count;C4Valid=@($c4Rows|Where-Object Status -eq 'PASS').Count;BdnValid=@($bdnRows|Where-Object Status -eq 'PASS').Count;IsolatedRequests=$isolatedTotal;IsolatedFailed=$isolatedFailed;MixedStatuses=(@($script:MixedLevels|ForEach-Object{"$($_.Level)=$($_.Status)"}) -join ', ');MixedFailed=$mixedFailed;TelemetryStatus=$telemetryOut.Status;PrePostStatus=$script:PrePostStatus;CleanupStatus=$cleanupStatus;PreservationStatus=$script:PreservationStatus;HistoricalTimeoutCause='NOT_VERIFIED';FullDatasetValueEquality='NOT_VERIFIED';CoreEvidenceReady=($script:OverallStatus -eq 'BATCH3_PASS');ReadyForBatch4=($script:OverallStatus -eq 'BATCH3_PASS');ReportPath=(Join-Path $script:ReviewRoot 'Batch3-Implementation-Report.md');ZipPath=(Join-Path $script:BatchRoot 'Batch3-Review-Pack.zip');ZipSHA256=$zipHash;Failure=$script:Failure}
