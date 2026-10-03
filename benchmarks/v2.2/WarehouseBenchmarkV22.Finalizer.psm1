Set-StrictMode -Version Latest

function Throw-V22FinalizerFailure([string]$Code,[string]$Message){throw ($Code+'|'+$Message)}
function Get-V22FinalizerField([object]$InputObject,[string]$Name){if($null-eq$InputObject){return [pscustomobject]@{Present=$false;Value=$null}};if($InputObject-is[System.Collections.IDictionary]){if($InputObject.Contains($Name)){return [pscustomobject]@{Present=$true;Value=$InputObject[$Name]}};return [pscustomobject]@{Present=$false;Value=$null}};$p=$InputObject.PSObject.Properties[$Name];if($null-eq$p){return [pscustomobject]@{Present=$false;Value=$null}};return [pscustomobject]@{Present=$true;Value=$p.Value}}
function Test-V22FinalizerBoolean([object]$Value){return ($Value-is[bool])}
function ConvertTo-V22FinalizerUtc([object]$Value,[string]$Field){$dt=[DateTimeOffset]::MinValue;if(-not[DateTimeOffset]::TryParse([string]$Value,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind,[ref]$dt)){Throw-V22FinalizerFailure 'V22_FINALIZER_TIMESTAMP_INVALID' ($Field+' is missing or malformed')};return $dt.ToUniversalTime().ToString('o')}
function New-V22FinalOwnedProcessInventory([object[]]$Records,[string]$CanonicalRunId,[string]$CandidateId){
    if([string]::IsNullOrWhiteSpace($CanonicalRunId)-or[string]::IsNullOrWhiteSpace($CandidateId)){Throw-V22FinalizerFailure 'V22_FINALIZER_IDENTITY_MISSING' 'canonical run or candidate identity is missing'}
    $rows=[Collections.Generic.List[object]]::new();$keys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($r in @($Records)){
        $type=Get-V22FinalizerField $r 'RecordType';if(-not$type.Present-or$type.Value-cne'OWNED_PROCESS'){Throw-V22FinalizerFailure 'V22_FINALIZER_RECORD_TYPE_INVALID' 'unknown or missing owned-process record type'}
        $version=Get-V22FinalizerField $r 'MetadataSchemaVersion';if(-not$version.Present-or[int]$version.Value-ne 1){Throw-V22FinalizerFailure 'V22_FINALIZER_SCHEMA_STALE' 'owned-process metadata schema version is unsupported'}
        $role=[string](Get-V22FinalizerField $r 'Role').Value;if($role-notin@('CHILD','TELEMETRY','TOOL','DB_TOOL')){Throw-V22FinalizerFailure 'V22_FINALIZER_RECORD_TYPE_INVALID' 'owned-process role is unsupported'}
        $pidValue=Get-V22FinalizerField $r 'ChildProcessId';$ownedProcessId=0;if(-not[int]::TryParse([string]$pidValue.Value,[ref]$ownedProcessId)-or$ownedProcessId-le 0){Throw-V22FinalizerFailure 'V22_FINALIZER_PID_INVALID' 'owned process PID is missing or invalid'}
        $start=Get-V22FinalizerField $r 'StartedUtc';if(-not$start.Present){Throw-V22FinalizerFailure 'V22_FINALIZER_PROCESS_FIELD_MISSING' 'StartedUtc is required'};$started=ConvertTo-V22FinalizerUtc $start.Value 'StartedUtc'
        $processStart=Get-V22FinalizerField $r 'ProcessStartUtc';if(-not$processStart.Present-or$null-eq$processStart.Value){Throw-V22FinalizerFailure 'V22_FINALIZER_PROCESS_FIELD_MISSING' 'ProcessStartUtc is required for PID ownership'};$processStartUtc=ConvertTo-V22FinalizerUtc $processStart.Value 'ProcessStartUtc'
        $recordRun=Get-V22FinalizerField $r 'RunId';if($recordRun.Present-and-not[string]::IsNullOrWhiteSpace([string]$recordRun.Value)-and[string]$recordRun.Value-cne$CanonicalRunId){Throw-V22FinalizerFailure 'V22_FINALIZER_RUN_ID_MISMATCH' 'owned process carries a stale RunId'}
        foreach($required in @('Completed','ProcessGone')){$f=Get-V22FinalizerField $r $required;if(-not$f.Present-or-not(Test-V22FinalizerBoolean $f.Value)){Throw-V22FinalizerFailure 'V22_FINALIZER_PROCESS_FIELD_MISSING' ($required+' must be a Boolean')}}
        $hostEvidence=(Get-V22FinalizerField $r 'HostStopEvidence').Value;$hostReason=[string](Get-V22FinalizerField $hostEvidence 'Reason').Value;$isHostLimit=$hostReason-in@('HOST_STOP_HARD_FLOOR','HOST_STOP_EMERGENCY_FLOOR')
        $app=Get-V22FinalizerField $r 'KilledForHostLimitApplicable';$killed=Get-V22FinalizerField $r 'KilledForHostLimit'
        if($app.Present-and$null-eq$app.Value){Throw-V22FinalizerFailure 'V22_FINALIZER_KILLED_FIELD_NULL' 'KilledForHostLimit applicability cannot be null'}
        if($app.Present-and-not(Test-V22FinalizerBoolean $app.Value)){Throw-V22FinalizerFailure 'V22_FINALIZER_KILLED_FIELD_MALFORMED' 'KilledForHostLimit applicability must be Boolean'}
        $applicable=if($app.Present){[bool]$app.Value}else{$isHostLimit}
        if($applicable-and-not$isHostLimit){Throw-V22FinalizerFailure 'V22_FINALIZER_KILLED_FIELD_INVALID' 'host-limit action is marked applicable without host-limit evidence'}
        if($isHostLimit-and-not$app.Present){Throw-V22FinalizerFailure 'V22_FINALIZER_KILLED_FIELD_REQUIRED' 'applicable host-limit field is missing'}
        if($killed.Present-and$null-eq$killed.Value){Throw-V22FinalizerFailure 'V22_FINALIZER_KILLED_FIELD_NULL' 'KilledForHostLimit cannot be null'}
        if($killed.Present-and-not(Test-V22FinalizerBoolean $killed.Value)){Throw-V22FinalizerFailure 'V22_FINALIZER_KILLED_FIELD_MALFORMED' 'KilledForHostLimit must be Boolean'}
        $killedValue=$null;$disposition='NOT_APPLICABLE'
        if($applicable){if(-not$killed.Present){Throw-V22FinalizerFailure 'V22_FINALIZER_KILLED_FIELD_REQUIRED' 'KilledForHostLimit is required for host-limit records'};$killIssued=Get-V22FinalizerField $hostEvidence 'KillIssued';if(-not$killIssued.Present-or-not(Test-V22FinalizerBoolean $killIssued.Value)){Throw-V22FinalizerFailure 'V22_FINALIZER_HOST_EVIDENCE_INVALID' 'host-limit KillIssued evidence is missing or malformed'};if([bool]$killIssued.Value-ne[bool]$killed.Value){Throw-V22FinalizerFailure 'V22_FINALIZER_HOST_EVIDENCE_INVALID' 'KilledForHostLimit does not match persisted stop action'};$killedValue=[bool]$killed.Value;$disposition=if($killedValue){'KILL_ISSUED'}else{'HOST_STOP_WITHOUT_KILL'}}elseif($killed.Present-and[bool]$killed.Value){Throw-V22FinalizerFailure 'V22_FINALIZER_KILLED_FIELD_INVALID' 'non-applicable process cannot be marked killed for host limit'}
        $block=[string](Get-V22FinalizerField $r 'BlockId').Value;if([string]::IsNullOrWhiteSpace($block)){$block=[string](Get-V22FinalizerField $r 'Label').Value};if([string]::IsNullOrWhiteSpace($block)){Throw-V22FinalizerFailure 'V22_FINALIZER_PROCESS_FIELD_MISSING' 'BlockId or process label is required'}
        $identity=if($processStartUtc){'{0}|{1}'-f$ownedProcessId,$processStartUtc}else{'{0}|{1}'-f$ownedProcessId,$started};if(-not$keys.Add($identity)){Throw-V22FinalizerFailure 'V22_FINALIZER_DUPLICATE_PID' 'duplicate PID and process-start identity in inventory'}
        $exit=Get-V22FinalizerField $r 'ExitCode';$exitCode=$null;if($exit.Present-and$null-ne$exit.Value){$parsed=0;if(-not[int]::TryParse([string]$exit.Value,[ref]$parsed)){Throw-V22FinalizerFailure 'V22_FINALIZER_PROCESS_FIELD_MALFORMED' 'ExitCode is malformed'};$exitCode=$parsed}
        $rows.Add([ordered]@{RecordType='OWNED_PROCESS';MetadataSchemaVersion=1;CanonicalRunId=$CanonicalRunId;CandidateId=$CandidateId;ProcessIdentity=$identity;BlockId=$block;Scenario=[string](Get-V22FinalizerField $r 'Scenario').Value;Level=[string](Get-V22FinalizerField $r 'Level').Value;ProcessRole=$role;PID=$ownedProcessId;ExecutablePath=[string](Get-V22FinalizerField $r 'FilePath').Value;StartedUtc=$started;ProcessStartUtc=$processStartUtc;ExitCode=$exitCode;Completed=[bool](Get-V22FinalizerField $r 'Completed').Value;ProcessGone=[bool](Get-V22FinalizerField $r 'ProcessGone').Value;HostStopped=($null-ne$hostEvidence);KilledForHostLimitApplicable=$applicable;KilledForHostLimit=$killedValue;HostLimitDisposition=$disposition;CleanupStatus=[string](Get-V22FinalizerField $r 'CleanupStatus').Value})
    }
    return [ordered]@{SchemaVersion='warehouse-benchmark-v22-final-owned-process-inventory/1';CanonicalRunId=$CanonicalRunId;CandidateId=$CandidateId;CapturedUtc=[DateTime]::UtcNow.ToString('o');ProcessCount=$rows.Count;ProcessRecords=$rows.ToArray()}
}
function Assert-V22CanonicalRunIdentity([string]$CanonicalRunId,[string]$ProtocolRunId,[string]$CandidateId,[string]$SourceSnapshotId,[string]$SessionId,[object[]]$Configurations){
    if([string]::IsNullOrWhiteSpace($CanonicalRunId)-or[string]::IsNullOrWhiteSpace($ProtocolRunId)-or$CanonicalRunId-cne$ProtocolRunId){Throw-V22FinalizerFailure 'CANONICAL_RUN_IDENTITY_INVALID' 'Protocol.RunId must equal CanonicalRunId'}
    if([string]::IsNullOrWhiteSpace($CandidateId)-or[string]::IsNullOrWhiteSpace($SourceSnapshotId)){Throw-V22FinalizerFailure 'CANONICAL_RUN_IDENTITY_INVALID' 'candidate/source identity is missing'}
    if(-not[string]::IsNullOrWhiteSpace($SessionId)-and$SessionId-ceq$CanonicalRunId){Throw-V22FinalizerFailure 'CANONICAL_RUN_IDENTITY_INVALID' 'SessionId must remain distinct from RunId'}
    if(@($Configurations).Count-eq 0){Throw-V22FinalizerFailure 'CANONICAL_RUN_IDENTITY_INVALID' 'no workload configurations were validated'}
    $blocks=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($c in @($Configurations)){if([string](Get-V22FinalizerField $c 'RunId').Value-cne$CanonicalRunId){Throw-V22FinalizerFailure 'CANONICAL_RUN_IDENTITY_INVALID' 'workload configuration has stale RunId'};if([string](Get-V22FinalizerField $c 'CandidateId').Value-cne$CandidateId){Throw-V22FinalizerFailure 'CANONICAL_RUN_IDENTITY_INVALID' 'workload configuration has stale CandidateId'};$block=[string](Get-V22FinalizerField $c 'BlockId').Value;if([string]::IsNullOrWhiteSpace($block)-or-not$blocks.Add($block)){Throw-V22FinalizerFailure 'CANONICAL_RUN_IDENTITY_INVALID' 'configuration BlockId is missing or duplicated'}}
    return [ordered]@{Status='PASS';CanonicalRunId=$CanonicalRunId;ProtocolRunId=$ProtocolRunId;CandidateId=$CandidateId;SourceSnapshotId=$SourceSnapshotId;SessionId=$SessionId;ConfigurationCount=@($Configurations).Count;ValidatedBlockIds=@($Configurations|ForEach-Object BlockId)}
}
function New-V22RepresentativeRunConfigurations([string]$CanonicalRunId,[string]$CandidateId,[string[]]$Scenarios){$rows=[Collections.Generic.List[object]]::new();foreach($kind in @('BDN','C1','C2','C4')){foreach($scenario in $Scenarios){$rows.Add([pscustomobject]@{RunId=$CanonicalRunId;CandidateId=$CandidateId;BlockId=($CanonicalRunId+'-IDENTITY-'+$kind+'-'+$scenario);Scenario=$scenario;Profile=$kind})}};foreach($workers in @(1,2,4,8)){foreach($scenario in $Scenarios){$level='L'+$workers;$rows.Add([pscustomobject]@{RunId=$CanonicalRunId;CandidateId=$CandidateId;BlockId=($CanonicalRunId+'-IDENTITY-'+$level+'-'+$scenario);Scenario=$scenario;Profile='MIXED_REGRESSION';Level=$level;CopiesPerScenario=$workers;ScenarioProcessCount=6;TotalLogicalCopies=6*$workers})}};return $rows.ToArray()}
function New-V22MixedSemantics([string]$Level,[int]$ScenarioProcessCount=6){if($Level-notmatch'^L(1|2|4|8)$'){Throw-V22FinalizerFailure 'V22_MIXED_LEVEL_INVALID' 'unsupported mixed level'};$copies=[int]$Matches[1];if($ScenarioProcessCount-ne 6){Throw-V22FinalizerFailure 'V22_MIXED_PROCESS_COUNT_INVALID' 'the protocol defines six scenario processes'};return [ordered]@{Level=$Level;ScenarioProcessCount=$ScenarioProcessCount;CopiesPerScenario=$copies;TotalLogicalCopies=$ScenarioProcessCount*$copies;TotalWorkers=$ScenarioProcessCount*$copies}}
function New-V22MixedWorkerConfiguration([string]$RunId,[string]$CandidateId,[string]$SourceSnapshotId,[string]$Level,[string]$BlockId,[string]$Scenario,[int]$WarmupSeconds=3,[int]$TimedSeconds=15){
    $sem=New-V22MixedSemantics $Level
    if([string]::IsNullOrWhiteSpace($RunId)-or[string]::IsNullOrWhiteSpace($CandidateId)-or[string]::IsNullOrWhiteSpace($SourceSnapshotId)-or[string]::IsNullOrWhiteSpace($BlockId)-or[string]::IsNullOrWhiteSpace($Scenario)){Throw-V22FinalizerFailure 'V22_MIXED_WORKER_IDENTITY_INVALID' 'worker configuration identity fields are required'}
    return [pscustomobject][ordered]@{SchemaVersion='warehouse-benchmark-v22-mixed-worker-config/1';RunId=$RunId;CandidateId=$CandidateId;SourceSnapshotId=$SourceSnapshotId;Profile='MIXED_REGRESSION';Level=$Level;BlockId=$BlockId;Scenario=$Scenario;ScenarioProcessCount=$sem.ScenarioProcessCount;CopiesPerScenario=$sem.CopiesPerScenario;TotalLogicalCopies=$sem.TotalLogicalCopies;WarmupSeconds=$WarmupSeconds;TimedSeconds=$TimedSeconds}
}
function New-V22MixedWorkerProjection([object]$WorkerRow,[string]$Level){
    $sem=New-V22MixedSemantics $Level;$source=[ordered]@{}
    if($WorkerRow-is[System.Collections.IDictionary]){foreach($key in $WorkerRow.Keys){$source[[string]$key]=$WorkerRow[$key]}}elseif($null-ne$WorkerRow){foreach($property in $WorkerRow.PSObject.Properties){$source[$property.Name]=$property.Value}}else{Throw-V22FinalizerFailure 'V22_MIXED_WORKER_ROW_MISSING' 'worker row is required'}
    foreach($field in @('ScenarioProcessCount','CopiesPerScenario','TotalLogicalCopies')){if(-not$source.Contains($field)){Throw-V22FinalizerFailure 'V22_MIXED_WORKER_COUNT_MISSING' ($field+' is required')};$expected=[int]$sem[$field];if([int]$source[$field]-ne$expected){Throw-V22FinalizerFailure 'V22_MIXED_WORKER_COUNT_MISMATCH' ($field+' differs from the protocol')}}
    $source['Level']=$Level;$source['MixedLevel']=$Level;$source['TotalWorkers']=$sem.TotalLogicalCopies
    return [pscustomobject]$source
}
function Get-V22MixedWorkerAggregate([string]$RunId,[string]$CandidateId,[string]$Level,[string[]]$ExpectedScenarios,[object[]]$WorkerRows,[int]$ObservedChildProcessCount,[double]$MinimumCommonOverlapSeconds=10){
    $sem=New-V22MixedSemantics $Level
    $rows=@($WorkerRows);$expected=@($ExpectedScenarios);$observations=[Collections.Generic.List[object]]::new();$errors=[Collections.Generic.List[string]]::new();$observedTotal=0;$allCopiesObserved=$true
    if($expected.Count-ne $sem.ScenarioProcessCount-or@($expected|Select-Object -Unique).Count-ne$expected.Count){$errors.Add('EXPECTED_SCENARIO_SET_INVALID')}
    if($ObservedChildProcessCount-lt 0){$errors.Add('OBSERVED_PROCESS_COUNT_INVALID')}
    foreach($scenario in $expected){
        $matches=@($rows|Where-Object{[string](Get-V22FinalizerField $_ 'Scenario').Value-ceq$scenario})
        if($matches.Count-ne 1){$errors.Add('WORKER_SCENARIO_ROW_COUNT_INVALID:'+ $scenario)}
        $row=if($matches.Count-eq 1){$matches[0]}else{$null};$observedField=Get-V22FinalizerField $row 'ObservedCopies';$observed=$null
        if($observedField.Present-and$null-ne$observedField.Value){$parsed=0;if([int]::TryParse([string]$observedField.Value,[ref]$parsed)-and$parsed-ge 0){$observed=$parsed}else{$errors.Add('OBSERVED_COPIES_MALFORMED:'+ $scenario)}}else{$allCopiesObserved=$false}
        if($null-ne$row){
            foreach($pair in @(@('RunId',$RunId),@('CandidateId',$CandidateId),@('Level',$Level))){$field=Get-V22FinalizerField $row $pair[0];if(-not$field.Present-or[string]$field.Value-cne[string]$pair[1]){$errors.Add('WORKER_IDENTITY_MISMATCH:'+ $scenario+':'+$pair[0])}}
            $instances=Get-V22FinalizerField $row 'ObservedInstanceNumbers';$unique=@();if($instances.Present){$unique=@($instances.Value|Select-Object -Unique)}
            $timed=Get-V22FinalizerField $row 'WorkerTimedWindowCompleted';$exit=Get-V22FinalizerField $row 'ProcessExitCode'
            if($null-ne$observed){$observedTotal+=$observed}
            if([string](Get-V22FinalizerField $row 'Status').Value-cne'PASS'){$errors.Add('WORKER_STATUS_NOT_PASS:'+ $scenario)}
            if(-not$timed.Present-or$timed.Value-isnot[bool]-or-not[bool]$timed.Value){$errors.Add('WORKER_TIMED_WINDOW_INVALID:'+ $scenario)}
            if(-not$exit.Present-or$null-eq$exit.Value-or[int]$exit.Value-ne 0){$errors.Add('WORKER_EXIT_INVALID:'+ $scenario)}
            if($null-ne$observed-and$unique.Count-ne$observed){$errors.Add('OBSERVED_INSTANCE_COUNT_MISMATCH:'+ $scenario)}
            $observations.Add([pscustomobject]@{Scenario=$scenario;ConfiguredCopies=$sem.CopiesPerScenario;ObservedCopies=$observed;ObservedInstanceNumbers=$unique;WorkerStatus=[string](Get-V22FinalizerField $row 'Status').Value;TimedWindowCompleted=($timed.Present-and$timed.Value-is[bool]-and[bool]$timed.Value);ProcessExitCode=if($exit.Present){$exit.Value}else{$null};IdentityValid=(@($errors|Where-Object{$_-like('WORKER_IDENTITY_MISMATCH:'+ $scenario+':*')}).Count-eq 0)})
        }else{$observations.Add([pscustomobject]@{Scenario=$scenario;ConfiguredCopies=$sem.CopiesPerScenario;ObservedCopies=$observed;ObservedInstanceNumbers=@();WorkerStatus='MISSING';TimedWindowCompleted=$false;ProcessExitCode=$null;IdentityValid=$false})}
        if($null-eq$observed-or$observed-ne$sem.CopiesPerScenario){$errors.Add('WORKER_COPY_COUNT_MISMATCH:'+ $scenario)}
    }
    if($rows.Count-ne$sem.ScenarioProcessCount-or$ObservedChildProcessCount-ne$sem.ScenarioProcessCount){$errors.Add('OBSERVED_SCENARIO_PROCESS_COUNT_MISMATCH')}
    $windows=[Collections.Generic.List[object]]::new()
    foreach($row in $rows){$start=Get-V22FinalizerField $row 'MeasuredStartUtc';$stop=Get-V22FinalizerField $row 'MeasuredStopUtc';$a=[DateTimeOffset]::MinValue;$b=[DateTimeOffset]::MinValue;if($start.Present-and$stop.Present-and[DateTimeOffset]::TryParse([string]$start.Value,[ref]$a)-and[DateTimeOffset]::TryParse([string]$stop.Value,[ref]$b)-and$b-gt$a){$windows.Add([pscustomobject]@{Start=$a;Stop=$b})}}
    $overlap=$null;$overlapValid=$null;$overlapStatus='NOT_CALCULATED'
    if($windows.Count-eq$sem.ScenarioProcessCount){$latest=($windows|ForEach-Object Start|Sort-Object -Descending|Select-Object -First 1);$earliest=($windows|ForEach-Object Stop|Sort-Object|Select-Object -First 1);$overlap=[Math]::Round(($earliest-$latest).TotalSeconds,3);$overlapValid=($overlap-ge$MinimumCommonOverlapSeconds);$overlapStatus=if($overlapValid){'PASS'}else{'FAIL'}}
    if(-not$allCopiesObserved){$observedTotal=$null}
    return [ordered]@{ScenarioProcessCount=$sem.ScenarioProcessCount;CopiesPerScenario=$sem.CopiesPerScenario;TotalLogicalCopies=$sem.TotalLogicalCopies;ObservedScenarioProcessCount=$ObservedChildProcessCount;ObservedCopiesPerScenario=$observations.ToArray();ObservedTotalLogicalCopies=$observedTotal;WorkerCountsValid=($errors.Count-eq 0);WorkerValidationErrors=$errors.ToArray();WindowOverlapSeconds=$overlap;OverlapValid=$overlapValid;OverlapStatus=$overlapStatus;OverlapApplicability=if($null-eq$overlapValid){'OPTIONAL'}else{'REQUIRED'};MinimumCommonOverlapSeconds=$MinimumCommonOverlapSeconds;WorkerWindowCount=$windows.Count}
}
function New-V22MixedResultRecord([object]$SourceRecord,[string]$RunId,[string]$CandidateId,[string]$Level){
    $sem=New-V22MixedSemantics $Level;$result=[ordered]@{}
    if($SourceRecord-is[System.Collections.IDictionary]){foreach($key in $SourceRecord.Keys){$result[[string]$key]=$SourceRecord[$key]}}elseif($null-ne$SourceRecord){foreach($property in $SourceRecord.PSObject.Properties){$result[$property.Name]=$property.Value}}
    $statusField=Get-V22FinalizerField $SourceRecord 'Status';$status=if($statusField.Present){[string]$statusField.Value}else{'INVALID'}
    $allowed=@('PASS','FAIL','INVALID','HOST_LIMIT','EMERGENCY_HOST_LIMIT','SKIPPED','PARTIAL')
    $failureType=Get-V22FinalizerField $SourceRecord 'FailureType';$reason=Get-V22FinalizerField $SourceRecord 'FailureReason';if(-not$reason.Present){$reason=Get-V22FinalizerField $SourceRecord 'Reason'}
    if($status-notin$allowed){$status='INVALID';$failureType=[pscustomobject]@{Present=$true;Value='MIXED_RESULT_SCHEMA_INVALID'};$reason=[pscustomobject]@{Present=$true;Value='Unknown mixed result status'}}
    $overlap=Get-V22FinalizerField $SourceRecord 'OverlapValid';$overlapSeconds=Get-V22FinalizerField $SourceRecord 'OverlapSeconds';if(-not$overlapSeconds.Present){$overlapSeconds=Get-V22FinalizerField $SourceRecord 'WindowOverlapSeconds'};if(-not$overlapSeconds.Present){$overlapSeconds=Get-V22FinalizerField $SourceRecord 'CommonOverlapSeconds'}
    $overlapApplicability=if($status-eq'PASS'){'REQUIRED'}elseif($status-eq'SKIPPED'-or$status-eq'HOST_LIMIT'-or$status-eq'EMERGENCY_HOST_LIMIT'){'NOT_APPLICABLE'}else{'OPTIONAL'}
    $overlapStatus=if($overlapApplicability-eq'NOT_APPLICABLE'){'NOT_APPLICABLE'}elseif(-not$overlap.Present-or$null-eq$overlap.Value){'NOT_CALCULATED'}elseif($overlap.Value-is[bool]-and[bool]$overlap.Value){'PASS'}elseif($overlap.Value-is[bool]){'FAIL'}else{'INVALID'}
    $started=Get-V22FinalizerField $SourceRecord 'StartedUtc';if(-not$started.Present){$started=Get-V22FinalizerField $SourceRecord 'Started'};$finished=Get-V22FinalizerField $SourceRecord 'FinishedUtc';if(-not$finished.Present){$finished=Get-V22FinalizerField $SourceRecord 'Finished'}
    $observedProcess=Get-V22FinalizerField $SourceRecord 'ObservedScenarioProcessCount';$workerRows=Get-V22FinalizerField $SourceRecord 'WorkerRows';if(-not$observedProcess.Present){$observedProcess=[pscustomobject]@{Present=$true;Value=if($workerRows.Present){@($workerRows.Value).Count}else{0}}}
    $copyRows=Get-V22FinalizerField $SourceRecord 'ObservedCopiesPerScenario';$observedTotal=Get-V22FinalizerField $SourceRecord 'ObservedTotalLogicalCopies'
    if(-not$copyRows.Present){$rows=[Collections.Generic.List[object]]::new();if($workerRows.Present){foreach($row in @($workerRows.Value)){$rows.Add([pscustomobject]@{Scenario=[string](Get-V22FinalizerField $row 'Scenario').Value;ConfiguredCopies=$sem.CopiesPerScenario;ObservedCopies=(Get-V22FinalizerField $row 'ObservedCopies').Value;ObservedInstanceNumbers=@((Get-V22FinalizerField $row 'ObservedInstanceNumbers').Value);WorkerStatus=[string](Get-V22FinalizerField $row 'Status').Value;TimedWindowCompleted=[bool](Get-V22FinalizerField $row 'WorkerTimedWindowCompleted').Value;ProcessExitCode=(Get-V22FinalizerField $row 'ProcessExitCode').Value;IdentityValid=$true})}};$copyRows=[pscustomobject]@{Present=$true;Value=$rows.ToArray()}}
    if(-not$observedTotal.Present){$sum=0;$known=$true;foreach($row in @($copyRows.Value)){$field=Get-V22FinalizerField $row 'ObservedCopies';$n=0;if(-not$field.Present-or$null-eq$field.Value-or-not[int]::TryParse([string]$field.Value,[ref]$n)){$known=$false}else{$sum+=$n}};$observedTotal=[pscustomobject]@{Present=$true;Value=if($known){$sum}else{$null}}}
    $result.SchemaVersion='warehouse-benchmark-v22-mixed-level-result/2';$result.RunId=$RunId;$result.CandidateId=$CandidateId;$result.MixedLevel=$Level;$result.Level=$Level;$result.Status=$status
    $result.FailureType=if($failureType.Present){$failureType.Value}else{$null};$result.FailureReason=if($reason.Present){$reason.Value}else{$null};$result.Reason=$result.FailureReason
    $result.StartedUtc=if($started.Present){$started.Value}else{$null};$result.FinishedUtc=if($finished.Present){$finished.Value}else{$null};$result.StartedApplicability=if($result.StartedUtc){'RECORDED'}elseif($status-eq'SKIPPED'-or$status-eq'HOST_LIMIT'-or$status-eq'EMERGENCY_HOST_LIMIT'){'NOT_APPLICABLE'}else{'OPTIONAL'}
    $result.ScenarioProcessCount=$sem.ScenarioProcessCount;$result.CopiesPerScenario=$sem.CopiesPerScenario;$result.TotalLogicalCopies=$sem.TotalLogicalCopies;$result.TotalWorkers=$sem.TotalLogicalCopies
    $result.ObservedScenarioProcessCount=$observedProcess.Value;$result.ObservedCopiesPerScenario=$copyRows.Value;$result.ObservedTotalLogicalCopies=$observedTotal.Value
    $result.OverlapSeconds=if($overlapSeconds.Present){$overlapSeconds.Value}else{$null};$result.WindowOverlapSeconds=$result.OverlapSeconds;$result.OverlapValid=if($overlap.Present){$overlap.Value}else{$null};$result.OverlapApplicability=$overlapApplicability;$result.OverlapStatus=$overlapStatus
    return [pscustomobject]$result
}
function Get-V22MixedResultGate([object]$Record,[double]$MinimumCommonOverlapSeconds=10){
    $errors=[Collections.Generic.List[string]]::new();$status=[string](Get-V22FinalizerField $Record 'Status').Value;$schema=[string](Get-V22FinalizerField $Record 'SchemaVersion').Value
    if($schema-cne'warehouse-benchmark-v22-mixed-level-result/2'){$errors.Add('SCHEMA_VERSION_INVALID')}
    foreach($field in @('RunId','CandidateId','MixedLevel','Status','FailureType','FailureReason','StartedUtc','FinishedUtc','ScenarioProcessCount','CopiesPerScenario','TotalLogicalCopies','ObservedScenarioProcessCount','ObservedCopiesPerScenario','ObservedTotalLogicalCopies','OverlapApplicability','OverlapStatus')){if(-not(Get-V22FinalizerField $Record $field).Present){$errors.Add('REQUIRED_FIELD_MISSING:'+ $field)}}
    $allowed=@('PASS','FAIL','INVALID','HOST_LIMIT','EMERGENCY_HOST_LIMIT','SKIPPED','PARTIAL');if($status-notin$allowed){$errors.Add('STATUS_INVALID')}
    if($status-ne'PASS'){foreach($fieldName in @('FailureType','FailureReason')){$field=Get-V22FinalizerField $Record $fieldName;if(-not$field.Present-or[string]::IsNullOrWhiteSpace([string]$field.Value)){$errors.Add('FAILURE_FIELD_MISSING:'+ $fieldName)}}}
    $app=[string](Get-V22FinalizerField $Record 'OverlapApplicability').Value;$valid=Get-V22FinalizerField $Record 'OverlapValid';$overlapStatus=[string](Get-V22FinalizerField $Record 'OverlapStatus').Value
    if($status-eq'PASS'){
        if($app-cne'REQUIRED'-or-not$valid.Present-or$valid.Value-isnot[bool]){$errors.Add('PASS_OVERLAP_REQUIRED_BOOLEAN_MISSING')}
        if($valid.Value-is[bool]-and-not[bool]$valid.Value){$errors.Add('PASS_OVERLAP_FALSE')}
        $seconds=Get-V22FinalizerField $Record 'OverlapSeconds';if(-not$seconds.Present){$seconds=Get-V22FinalizerField $Record 'WindowOverlapSeconds'};$parsed=0.0;if(-not$seconds.Present-or-not[double]::TryParse([string]$seconds.Value,[ref]$parsed)-or[double]::IsNaN($parsed)-or[double]::IsInfinity($parsed)-or$parsed-lt$MinimumCommonOverlapSeconds){$errors.Add('PASS_OVERLAP_SECONDS_INVALID')}
        foreach($fieldName in @('StartedUtc','FinishedUtc')){$field=Get-V22FinalizerField $Record $fieldName;$timestamp=[DateTimeOffset]::MinValue;if(-not$field.Present-or-not[DateTimeOffset]::TryParse([string]$field.Value,[ref]$timestamp)){$errors.Add('PASS_TIMESTAMP_INVALID:'+ $fieldName)}}
        $expected=0;$observed=0;$processExpected=0;$processObserved=0;$copies=0;$numbersValid=$true
        foreach($fieldName in @('TotalLogicalCopies','ObservedTotalLogicalCopies','ScenarioProcessCount','ObservedScenarioProcessCount','CopiesPerScenario')){$field=Get-V22FinalizerField $Record $fieldName;$parsedNumber=0;if(-not$field.Present-or-not[int]::TryParse([string]$field.Value,[ref]$parsedNumber)-or$parsedNumber-lt 0){$errors.Add('PASS_WORKER_COUNT_MALFORMED:'+ $fieldName);$numbersValid=$false}else{switch($fieldName){'TotalLogicalCopies'{$expected=$parsedNumber};'ObservedTotalLogicalCopies'{$observed=$parsedNumber};'ScenarioProcessCount'{$processExpected=$parsedNumber};'ObservedScenarioProcessCount'{$processObserved=$parsedNumber};'CopiesPerScenario'{$copies=$parsedNumber}}}}
        $copyField=Get-V22FinalizerField $Record 'ObservedCopiesPerScenario';$copyRows=if($copyField.Present){@($copyField.Value)}else{@()}
        if($numbersValid-and($processExpected-ne 6-or$processObserved-ne$processExpected-or$expected-ne($processExpected*$copies)-or$observed-ne$expected-or$copyRows.Count-ne$processExpected)){$errors.Add('PASS_WORKER_COUNT_MISMATCH')}
        $expectedScenarios=@('MasterPaged','LookupPaged','DocumentPaged','DetailReportPaged','InventoryHistoricalReportPaged','InventoryCurrentBalancePaged');$actualScenarios=[Collections.Generic.List[string]]::new()
        foreach($row in $copyRows){$n=0;$rowScenario=Get-V22FinalizerField $row 'Scenario';$rowCopies=Get-V22FinalizerField $row 'ObservedCopies';$rowStatus=Get-V22FinalizerField $row 'WorkerStatus';$rowTimed=Get-V22FinalizerField $row 'TimedWindowCompleted';$rowIdentity=Get-V22FinalizerField $row 'IdentityValid';$rowExit=Get-V22FinalizerField $row 'ProcessExitCode';$rowInstances=Get-V22FinalizerField $row 'ObservedInstanceNumbers';$instanceValues=[object[]]@();if($rowInstances.Present){$instanceValues=[object[]]@($rowInstances.Value|Select-Object -Unique)};$exitCode=0;$exitValid=$rowExit.Present-and[int]::TryParse([string]$rowExit.Value,[ref]$exitCode)-and$exitCode-eq 0;if($rowScenario.Present){$actualScenarios.Add([string]$rowScenario.Value)};if(-not$rowCopies.Present-or-not[int]::TryParse([string]$rowCopies.Value,[ref]$n)-or$n-ne$copies-or$instanceValues.Count-ne$n-or-not$exitValid-or-not$rowStatus.Present-or[string]$rowStatus.Value-cne'PASS'-or-not$rowTimed.Present-or$rowTimed.Value-isnot[bool]-or-not[bool]$rowTimed.Value-or-not$rowIdentity.Present-or$rowIdentity.Value-isnot[bool]-or-not[bool]$rowIdentity.Value){$errors.Add('PASS_WORKER_EVIDENCE_INVALID')}}
        $actualScenarioSet=(@($actualScenarios|Sort-Object -Unique)-join'|');$expectedScenarioSet=(@($expectedScenarios|Sort-Object -Unique)-join'|');if($actualScenarioSet-cne$expectedScenarioSet){$errors.Add('PASS_SCENARIO_SET_MISMATCH')}
        if($overlapStatus-cne'PASS'){$errors.Add('PASS_OVERLAP_STATUS_INVALID')}
    }elseif($status-eq'SKIPPED'-or$status-eq'HOST_LIMIT'-or$status-eq'EMERGENCY_HOST_LIMIT'){
        if($app-cne'NOT_APPLICABLE'-or$overlapStatus-cne'NOT_APPLICABLE'){$errors.Add('UNSTARTED_OVERLAP_MUST_BE_NOT_APPLICABLE')}
        if($valid.Present-and$null-ne$valid.Value){$errors.Add('UNSTARTED_OVERLAP_VALUE_MUST_BE_NULL')}
    }else{
        if($app-notin@('OPTIONAL','NOT_APPLICABLE')){$errors.Add('FAILURE_OVERLAP_APPLICABILITY_INVALID')}
        if($valid.Present-and$null-ne$valid.Value-and$valid.Value-isnot[bool]){$errors.Add('FAILURE_OVERLAP_TYPE_INVALID')}
        if(-not$valid.Present-or$null-eq$valid.Value){if($overlapStatus-cne'NOT_CALCULATED'){$errors.Add('MISSING_OVERLAP_STATUS_INVALID')}}elseif($valid.Value-is[bool]-and(($valid.Value-and$overlapStatus-cne'PASS')-or(-not$valid.Value-and$overlapStatus-cne'FAIL'))){$errors.Add('FAILURE_OVERLAP_STATUS_INVALID')}
    }
    $schemaStatus=if($errors.Count-eq0){'PASS'}else{'INVALID'};$measurement=if($status-eq'PASS'-and$schemaStatus-eq'PASS'){'PASS'}elseif($status-in@('SKIPPED','HOST_LIMIT','EMERGENCY_HOST_LIMIT')){'NOT_RUN'}else{'FAIL'}
    return [ordered]@{SchemaStatus=$schemaStatus;RecordStatus=$status;MeasurementStatus=$measurement;OverlapStatus=$overlapStatus;Errors=$errors.ToArray();IsMeasurementPass=($schemaStatus-eq'PASS'-and$measurement-eq'PASS')}
}
function Test-V22FinalCleanupEvidence([object]$Inventory,[object]$PerPidEvidence,[object]$TelemetryEvidence,[object]$SqlResidue,[string]$CanonicalRunId,[string]$CandidateId){
    if($null-eq$Inventory){Throw-V22FinalizerFailure 'V22_CLEANUP_INPUT_MISSING' 'owned-process inventory is missing'};if($null-eq$PerPidEvidence){Throw-V22FinalizerFailure 'V22_CLEANUP_PROBE_MISSING' 'final PID probe evidence is missing'};if($null-eq$TelemetryEvidence){Throw-V22FinalizerFailure 'V22_CLEANUP_PROBE_MISSING' 'telemetry/helper process probe evidence is missing'};if($null-eq$SqlResidue){Throw-V22FinalizerFailure 'V22_CLEANUP_INPUT_MISSING' 'final SQL residue evidence is missing'}
    foreach($e in @($Inventory,$PerPidEvidence,$TelemetryEvidence,$SqlResidue)){if([string](Get-V22FinalizerField $e 'CanonicalRunId').Value-cne$CanonicalRunId){Throw-V22FinalizerFailure 'V22_FINALIZER_RUN_ID_MISMATCH' 'cleanup evidence RunId mismatch'};if([string](Get-V22FinalizerField $e 'CandidateId').Value-cne$CandidateId){Throw-V22FinalizerFailure 'V22_FINALIZER_CANDIDATE_MISMATCH' 'cleanup evidence CandidateId mismatch'}}
    if([string]$Inventory.SchemaVersion-cne'warehouse-benchmark-v22-final-owned-process-inventory/1'-or[int]$Inventory.ProcessCount-ne@($Inventory.ProcessRecords).Count){Throw-V22FinalizerFailure 'V22_FINALIZER_SCHEMA_STALE' 'owned-process inventory schema/count is invalid'}
    $records=@($Inventory.ProcessRecords);$probeRows=@($PerPidEvidence.Probes);$helperRows=@($TelemetryEvidence.Probes);if($records.Count-ne$probeRows.Count){Throw-V22FinalizerFailure 'V22_CLEANUP_PROBE_MISSING' 'final PID probe count does not cover inventory'}
    $byIdentity=@{};foreach($p in $probeRows){$key=[string]$p.ProcessIdentity;if([string]::IsNullOrWhiteSpace($key)-or$byIdentity.ContainsKey($key)){Throw-V22FinalizerFailure 'V22_FINALIZER_DUPLICATE_PID' 'duplicate or missing process identity in PID probes'};$byIdentity[$key]=$p}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$failures=[Collections.Generic.List[object]]::new();foreach($r in $records){$id=[string]$r.ProcessIdentity;if(-not$seen.Add($id)-or-not$byIdentity.ContainsKey($id)){Throw-V22FinalizerFailure 'V22_CLEANUP_PROBE_MISSING' 'inventory process has no unique final PID probe'};$probe=$byIdentity[$id];if([string]$probe.CanonicalRunId-cne$CanonicalRunId-or[string]$probe.CandidateId-cne$CandidateId-or[int]$probe.PID-ne[int]$r.PID){Throw-V22FinalizerFailure 'V22_FINALIZER_RUN_ID_MISMATCH' 'PID probe identity does not match inventory'};$null=ConvertTo-V22FinalizerUtc $probe.ProbeUtc 'ProbeUtc';if($probe.Status-notin@('ABSENT','PID_REUSED_DIFFERENT_PROCESS','OWNED_PROCESS_PRESENT','IDENTITY_UNVERIFIED','UNKNOWN')){Throw-V22FinalizerFailure 'V22_FINALIZER_PROBE_STATUS_INVALID' 'unknown PID probe status'};if($probe.Status-notin@('ABSENT','PID_REUSED_DIFFERENT_PROCESS')-or-not[bool]$r.Completed-or-not[bool]$r.ProcessGone-or[string]$r.CleanupStatus-eq'CLEANUP_FAILED'){$failures.Add([pscustomobject]@{ProcessIdentity=$id;PID=$r.PID;ProbeStatus=$probe.Status;Completed=$r.Completed;ProcessGone=$r.ProcessGone;CleanupStatus=$r.CleanupStatus})}}
    $expectedHelpers=@($records|Where-Object{$_.ProcessRole -in @('TELEMETRY','TOOL','DB_TOOL')}|ForEach-Object ProcessIdentity|Sort-Object -Unique);$actualHelpers=@($helperRows|ForEach-Object ProcessIdentity|Sort-Object -Unique);if(($expectedHelpers-join'|')-cne($actualHelpers-join'|')){Throw-V22FinalizerFailure 'V22_CLEANUP_PROBE_MISSING' 'telemetry/helper process probe set differs from inventory'};foreach($h in $helperRows){if([string]$h.CanonicalRunId-cne$CanonicalRunId-or[string]$h.CandidateId-cne$CandidateId-or$h.Status-notin@('ABSENT','PID_REUSED_DIFFERENT_PROCESS')){$failures.Add([pscustomobject]@{ProcessIdentity=$h.ProcessIdentity;PID=$h.PID;ProbeStatus=$h.Status;Kind='HELPER'})}}
    $sqlStatus=[string](Get-V22FinalizerField $SqlResidue 'Status').Value;$sqlProcessExit=Get-V22FinalizerField $SqlResidue 'ProcessExitCode';$residue=$SqlResidue.Evidence;$residueStatus=[string](Get-V22FinalizerField $residue 'Status').Value;$target=[string](Get-V22FinalizerField $residue 'TargetDatabase').Value;$dbId=[int](Get-V22FinalizerField $residue 'DatabaseId').Value;$sqlProbe=Get-V22FinalizerField $SqlResidue 'ProbeProcess';$sqlProbeGood=$false;if($sqlProbe.Present-and$null-ne$sqlProbe.Value){$sp=$sqlProbe.Value;$null=ConvertTo-V22FinalizerUtc $sp.ProbeUtc 'SqlProbeProcessProbeUtc';$sqlProbeGood=([int]$sp.PID-gt 0-and[bool]$sp.ProcessGone-and[string]$sp.CleanupStatus-ceq'PASS'-and$sp.Status-in@('ABSENT','PID_REUSED_DIFFERENT_PROCESS'))};$sqlGood=($sqlStatus-eq'PASS'-and$sqlProcessExit.Present-and[int]$sqlProcessExit.Value-eq 0-and$residueStatus-ceq'RESIDUE_CLEAN'-and$target-ceq'TKS_Thuc_Tap_V11_Perf_10000000'-and$dbId-eq 5-and$sqlProbeGood);if(-not$sqlGood){$failures.Add([pscustomobject]@{Kind='SQL_RESIDUE';WrapperStatus=$sqlStatus;ProcessExitCode=$sqlProcessExit.Value;ResidueStatus=$residueStatus;TargetDatabase=$target;DatabaseId=$dbId})}
    return [ordered]@{SchemaVersion='warehouse-benchmark-v22-final-aggregate-cleanup/1';CanonicalRunId=$CanonicalRunId;CandidateId=$CandidateId;AggregatedUtc=[DateTime]::UtcNow.ToString('o');AggregationAlgorithm='V22_FINAL_CLEANUP_REDUCER/1; exact process identity coverage + persisted per-PID/helper probes + bounded SQL residue';OwnedProcessCount=$records.Count;UniqueProcessIdentityCount=$seen.Count;PerPidProbeCount=$probeRows.Count;TelemetryHelperProcessCount=$helperRows.Count;SqlResiduePass=$sqlGood;FailureCount=$failures.Count;Failures=$failures.ToArray();AggregateCleanupStatus=if($failures.Count-eq 0){'PASS'}else{'FAIL'};SQLSessionAttribution='NOT_VERIFIED beyond the persisted bounded performance-residue contract'}
}
function New-V22FinalizerJson([string]$Path,[object]$Value){if(Test-Path -LiteralPath $Path){Throw-V22FinalizerFailure 'V22_FINALIZER_OUTPUT_EXISTS' 'refusing to overwrite finalizer evidence'};$parent=Split-Path -Parent $Path;if(-not(Test-Path -LiteralPath $parent -PathType Container)){New-Item -ItemType Directory -Path $parent|Out-Null};$json=$Value|ConvertTo-Json -Depth 20;$stream=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($json+[Environment]::NewLine);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}}
function Get-V22ReportValue([object]$Values,[string]$Name,[string]$Fallback='NOT_RECORDED'){
    $field=Get-V22FinalizerField $Values $Name
    if(-not $field.Present -or $null -eq $field.Value){return $Fallback}
    if($field.Value -is [System.Array]){return (@($field.Value) -join ', ')}
    return ([string]$field.Value).Replace('$','&#36;')
}
function Get-V22OptionalNumericMetric([object]$Record,[string]$Name){
    if($null-eq$Record){return $null}
    $field=Get-V22FinalizerField $Record $Name
    if(-not$field.Present-or$null-eq$field.Value){return $null}
    $number=0.0;$text=[Convert]::ToString($field.Value,[Globalization.CultureInfo]::InvariantCulture)
    if(-not[double]::TryParse($text,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$number)-or[double]::IsNaN($number)-or[double]::IsInfinity($number)-or$number-lt 0){Throw-V22FinalizerFailure 'V22_FINALIZER_METRIC_INVALID' ($Name+' is malformed, non-finite, or negative')}
    return $number
}
function Get-V22MetricProjection([object]$Record,[string]$Kind='NB'){
    if($null-eq$Record){Throw-V22FinalizerFailure 'V22_FINALIZER_METRIC_RECORD_MISSING' 'metric record is required'}
    $statusField=Get-V22FinalizerField $Record 'Status'
    if(-not$statusField.Present-or[string]::IsNullOrWhiteSpace([string]$statusField.Value)){Throw-V22FinalizerFailure 'V22_FINALIZER_METRIC_STATUS_MISSING' 'record status is missing'}
    $status=[string]$statusField.Value
    if($status-notin@('PASS','FAIL','INVALID','HOST_LIMIT','EMERGENCY_HOST_LIMIT','SKIPPED','NOT_RUN')){Throw-V22FinalizerFailure 'V22_FINALIZER_METRIC_STATUS_INVALID' 'record status is unsupported'}
    $kind=$Kind.ToUpperInvariant()
    if($kind -in @('C1','C2','C4')){$kind='NB'}
    if($kind-notin@('BDN','NB')){Throw-V22FinalizerFailure 'V22_FINALIZER_METRIC_KIND_INVALID' 'metric kind is unsupported'}
    $names=@('Requests','Failed','RPS','MeanMs','P50Ms','P95Ms','P99Ms','MaxMs','ErrorMs','StdDevMs')
    $required=if($status-ne'PASS'){@()}elseif($kind-eq'BDN'){@('MeanMs','ErrorMs','StdDevMs')}else{@('Requests','Failed','RPS','MeanMs','P50Ms','P95Ms','P99Ms','MaxMs')}
    $values=[ordered]@{};$missing=[Collections.Generic.List[string]]::new();$invalid=[Collections.Generic.List[string]]::new();$available=0;$measuredAvailable=0
    foreach($name in $names){
        $field=Get-V22FinalizerField $Record $name
        if(-not$field.Present-or$null-eq$field.Value){$values[$name]=$null;if($name-in$required){$missing.Add($name)};continue}
        $number=0.0;$text=[Convert]::ToString($field.Value,[Globalization.CultureInfo]::InvariantCulture)
        if(-not[double]::TryParse($text,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$number)-or[double]::IsNaN($number)-or[double]::IsInfinity($number)-or$number-lt 0){$values[$name]=$null;$invalid.Add($name);continue}
        $values[$name]=$number;$available++;if($name-in@('RPS','MeanMs','P50Ms','P95Ms','P99Ms','MaxMs','ErrorMs','StdDevMs')){$measuredAvailable++}
    }
    if($missing.Count-gt 0){Throw-V22FinalizerFailure 'V22_FINALIZER_METRIC_REQUIRED_MISSING' ('PASS '+$kind+' row lacks required metrics: '+($missing -join ','))}
    if($status-eq'PASS'-and$invalid.Count-gt 0){Throw-V22FinalizerFailure 'V22_FINALIZER_METRIC_INVALID' ('PASS '+$kind+' row has malformed metrics: '+($invalid -join ','))}
    $metricState=if($invalid.Count-gt 0){'INVALID_PARTIAL'}elseif($status-eq'PASS'){'VALID'}elseif($measuredAvailable-gt 0){'PARTIAL_DIAGNOSTIC'}else{'NOT_MEASURED'}
    return [pscustomobject]@{Status=$status;Kind=$kind;Comparable=($status-eq'PASS');MetricState=$metricState;InvalidFields=$invalid.ToArray();Requests=$values.Requests;Failed=$values.Failed;RPS=$values.RPS;MeanMs=$values.MeanMs;P50Ms=$values.P50Ms;P95Ms=$values.P95Ms;P99Ms=$values.P99Ms;MaxMs=$values.MaxMs;ErrorMs=$values.ErrorMs;StdDevMs=$values.StdDevMs}
}
function Get-V22MetricDisplayValue([object]$Projection,[string]$Name){
    $field=Get-V22FinalizerField $Projection $Name
    if(-not$field.Present){return 'NOT_RECORDED'}
    if($null-eq$field.Value){if([string](Get-V22FinalizerField $Projection 'MetricState').Value-eq'INVALID_PARTIAL'){return 'INVALID'};return 'NOT_APPLICABLE'}
    return [Convert]::ToString($field.Value,[Globalization.CultureInfo]::InvariantCulture)
}
function New-V22FinalReport([object]$Values){
    $lines=[Collections.Generic.List[string]]::new()
    $lines.Add('# Warehouse Benchmark 2.2 - Final Canonical Batch 3')
    $lines.Add('')
    $lines.Add(('Verdict: {0}' -f (Get-V22ReportValue $Values 'Verdict')))
    $lines.Add('')
    $lines.Add('## Canonical identity')
    $lines.Add('')
    foreach($pair in @(@('Canonical Run ID','CanonicalRunId'),@('Protocol Run ID','ProtocolRunId'),@('Identity binding','IdentityStatus'),@('Candidate ID','CandidateId'),@('Source Snapshot ID','SourceSnapshotId'),@('Candidate manifest SHA-256','ManifestSHA256'))){$lines.Add(('- {0}: {1}' -f $pair[0],(Get-V22ReportValue $Values $pair[1])))}
    $lines.Add('')
    $lines.Add('## Candidate binaries and inventories')
    $lines.Add('')
    foreach($pair in @(@('Benchmark DLL SHA-256','BenchmarkDllSHA256'),@('Data Access DLL SHA-256','DataAccessDllSHA256'),@('Source inventory count','SourceInventoryCount'),@('Source inventory SHA-256','SourceInventorySHA256'),@('Runtime inventory count','RuntimeInventoryCount'),@('Runtime inventory SHA-256','RuntimeInventorySHA256'))){$lines.Add(('- {0}: {1}' -f $pair[0],(Get-V22ReportValue $Values $pair[1])))}
    $lines.Add('')
    $lines.Add('## Gates')
    $lines.Add('')
    foreach($key in @('Storage','Candidate','CanonicalRunIdentity','Preflight','TotalCount','IsolatedC1C2','C4','BDN','PostIsolatedCorrectness','MixedL1','MixedL2','MixedL4','MixedOverlap','MixedTelemetry','PostMixedCorrectness','AggregateCleanup','BoundedDbPrePost','CandidateGuard','Preservation','RawArtifactIntegrity','KnownLimitations','ReportLint','ReviewPackIntegrity','MixedHarnessOffline')){$value=Get-V22FinalizerField $Values.Gates $key;$status=if($value.Present){[string]$value.Value}else{'NOT_RUN'};$lines.Add(('- {0}: {1}' -f $key,$status))}
    $lines.Add(('- MixedL8: {0}' -f (Get-V22ReportValue $Values 'MixedL8')))
    $lines.Add('')
    $lines.Add('## Correctness and workload results')
    $lines.Add('')
    foreach($pair in @(@('Correctness preflight','CorrectnessPreflight'),@('Total_Count preflight','TotalCountPreflight'),@('Post-isolated correctness','PostIsolatedCorrectness'),@('Post-mixed correctness','PostMixedCorrectness'),@('Isolated C1/C2','IsolatedC1C2'),@('C4','C4'),@('BDN','BDN'),@('Mixed overlap','MixedOverlap'),@('Mixed telemetry','MixedTelemetry'),@('Isolated requests','IsolatedRequests'),@('Isolated failed requests','IsolatedFailed'),@('Mixed failed requests','MixedFailed'))){$lines.Add(('- {0}: {1}' -f $pair[0],(Get-V22ReportValue $Values $pair[1])))}
    $lines.Add(('- Mixed levels: L1={0}; L2={1}; L4={2}; L8={3}' -f (Get-V22ReportValue $Values 'MixedL1'),(Get-V22ReportValue $Values 'MixedL2'),(Get-V22ReportValue $Values 'MixedL4'),(Get-V22ReportValue $Values 'MixedL8')))
    $lines.Add('')
    $lines.Add('## Final cleanup and bounded state')
    $lines.Add('')
    foreach($pair in @(@('Owned process count','ProcessCount'),@('Final PID probe count','PidProbeCount'),@('Helper probe count','HelperProbeCount'),@('Aggregate cleanup','CleanupStatus'),@('Bounded DB PRE/POST','PrePostStatus'),@('Previous evidence preservation','PreservationStatus'),@('Raw artifact count','RawArtifactCount'),@('Raw artifact index SHA-256','RawArtifactIndexSHA256'))){$lines.Add(('- {0}: {1}' -f $pair[0],(Get-V22ReportValue $Values $pair[1])))}
    $lines.Add('')
    $lines.Add('## Workload execution')
    $lines.Add('')
    $lines.Add(('- Canonical workload attempts: {0}' -f (Get-V22ReportValue $Values 'CanonicalWorkloadAttempts' '0')))
    $lines.Add(('- Pre-canonical timed diagnostic: {0}' -f (Get-V22ReportValue $Values 'RecoveryDiagnosticStatus')))
    $lines.Add('- Additional full workload reruns: NO')
    $lines.Add('- Automatic retries: NO')
    $lines.Add('')
    $lines.Add('## Known limitations')
    $lines.Add('')
    foreach($pair in @(@('HistoricalTimeoutCause','HistoricalTimeoutCause'),@('FullDatasetValueEquality','FullDatasetValueEquality'),@('PerformanceSLA','PerformanceSLA'),@('CausalAttribution','CausalAttribution'),@('HistoricalHostLimitActionTimestamp','HistoricalHostLimitActionTimestamp'))){$lines.Add(('- {0}: {1}' -f $pair[0],(Get-V22ReportValue $Values $pair[1])))}
    $lines.Add('')
    $lines.Add('## Performance interpretation')
    $lines.Add('')
    $lines.Add('Reported comparisons are descriptive only. No regression, improvement, acceptable, or unacceptable performance verdict is made because no authoritative SLA is defined. L8 is standalone mixed evidence unless an isolated C8 comparator exists; no C8 is inferred.')
    $lines.Add('')
    $lines.Add('## Readiness')
    $lines.Add('')
    $lines.Add(('- CORE_EVIDENCE_READY: {0}' -f (Get-V22ReportValue $Values 'CoreEvidenceReady' 'NO')))
    $lines.Add(('- READY_FOR_BATCH4: {0}' -f (Get-V22ReportValue $Values 'ReadyForBatch4' 'NO')))
    $lines.Add(('- Stop reason: {0}' -f (Get-V22ReportValue $Values 'StopReason' 'NONE')))
    $lines.Add(('- Failure stage: {0}' -f (Get-V22ReportValue $Values 'FailureStage' 'NONE')))
    $lines.Add('')
    $lines.Add('## Evidence paths')
    $lines.Add('')
    foreach($path in @($Values.EvidencePaths)){$lines.Add(('- {0}' -f [string]$path))}
    $lines.Add('')
    return ($lines -join [Environment]::NewLine)+[Environment]::NewLine
}
function New-V22ReviewProjectionArtifact([string]$FinalRoot,[string]$Name,[string]$SourcePath,[string]$CanonicalRunId,[string]$CandidateId){
    if([IO.Path]::GetFileName($Name) -cne $Name -or [string]::IsNullOrWhiteSpace($Name)){Throw-V22FinalizerFailure 'V22_REVIEW_ARTIFACT_NAME_INVALID' 'projection must use a single safe filename'}
    $destination=Join-Path $FinalRoot $Name
    if(Test-Path -LiteralPath $destination){Throw-V22FinalizerFailure 'V22_FINALIZER_OUTPUT_EXISTS' ('refusing to overwrite '+$Name)}
    if(-not(Test-Path -LiteralPath $FinalRoot -PathType Container)){New-Item -ItemType Directory -Path $FinalRoot | Out-Null}
    if(-not [string]::IsNullOrWhiteSpace($SourcePath) -and (Test-Path -LiteralPath $SourcePath -PathType Leaf)){
        [IO.File]::Copy($SourcePath,$destination,$false)
        $sourceHash=(Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $targetHash=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
        if($sourceHash -cne $targetHash){Throw-V22FinalizerFailure 'V22_REVIEW_PROJECTION_HASH_MISMATCH' $Name}
        return [pscustomobject]@{Name=$Name;Status='COPIED';SourcePath=$SourcePath;SHA256=$targetHash;Size=[long](Get-Item -LiteralPath $destination).Length}
    }
    if([IO.Path]::GetExtension($Name) -ceq '.json'){
        New-V22FinalizerJson $destination ([ordered]@{SchemaVersion='warehouse-benchmark-v22-final-projection/1';Status='MISSING';CanonicalRunId=$CanonicalRunId;CandidateId=$CandidateId;MissingSourcePath=$SourcePath;Reason='Required source evidence was not persisted'})
    }else{
        $text='# '+$Name+[Environment]::NewLine+[Environment]::NewLine+'Status: MISSING'+[Environment]::NewLine+'Source: '+$SourcePath+[Environment]::NewLine
        [IO.File]::WriteAllText($destination,$text,[Text.UTF8Encoding]::new($false))
    }
    return [pscustomobject]@{Name=$Name;Status='MISSING';SourcePath=$SourcePath;SHA256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant();Size=[long](Get-Item -LiteralPath $destination).Length}
}
function Visit-V22ReviewIdentity([object]$Node,[string]$Path,[string]$CanonicalRunId,[string]$CandidateId,[Collections.Generic.List[object]]$Issues){
    if($null -eq $Node){return}
    if($Node -is [System.Array]){foreach($child in $Node){Visit-V22ReviewIdentity $child $Path $CanonicalRunId $CandidateId $Issues};return}
    $schema=Get-V22FinalizerField $Node 'SchemaVersion'
    $syntheticMixedHarness=$schema.Present -and [string]$schema.Value -ceq 'warehouse-benchmark-v22-mixed-harness-tests/1'
    foreach($property in $Node.PSObject.Properties){
        $value=$property.Value
        if($property.Name -in @('CanonicalRunId','RunId') -and $null -ne $value -and -not [string]::IsNullOrWhiteSpace([string]$value) -and [string]$value -cne $CanonicalRunId){$Issues.Add([pscustomobject]@{Code='STALE_RUN_ID';Path=$Path;Value=[string]$value})}
        if($property.Name -ceq 'CandidateId' -and $null -ne $value -and -not [string]::IsNullOrWhiteSpace([string]$value) -and [string]$value -cne $CandidateId){$Issues.Add([pscustomobject]@{Code='STALE_CANDIDATE_ID';Path=$Path;Value=[string]$value})}
        # The offline harness test document intentionally contains synthetic run/candidate IDs inside its test cases.
        if(-not $syntheticMixedHarness -and $null -ne $value -and $value -isnot [string] -and $value -isnot [ValueType]){Visit-V22ReviewIdentity $value $Path $CanonicalRunId $CandidateId $Issues}
    }
}
function Test-V22FinalCanonicalReviewPack([string]$Root,[string[]]$RequiredFiles,[string]$CanonicalRunId,[string]$CandidateId){
    $issues=[Collections.Generic.List[object]]::new();$required=@($RequiredFiles)
    foreach($name in @($required | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)){$issues.Add([pscustomobject]@{Code='DUPLICATE_LOGICAL_ARTIFACT';Path=$name})}
    if(-not(Test-Path -LiteralPath $Root -PathType Container)){return [ordered]@{Status='FAIL';IssueCount=1;Issues=@([pscustomobject]@{Code='PACK_ROOT_MISSING';Path=$Root});Entries=@()}}
    $actualFiles=@(Get-ChildItem -LiteralPath $Root -File -Force -Recurse)
    foreach($group in @($actualFiles | Group-Object Name | Where-Object Count -gt 1)){$issues.Add([pscustomobject]@{Code='DUPLICATE_FILE_NAME';Path=$group.Name})}
    foreach($name in $required){
        $path=Join-Path $Root $name
        if(-not(Test-Path -LiteralPath $path -PathType Leaf)){$issues.Add([pscustomobject]@{Code='REQUIRED_FILE_MISSING';Path=$name});continue}
        if([IO.Path]::GetExtension($name) -ceq '.json'){
            try{$json=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -Depth 40}catch{$issues.Add([pscustomobject]@{Code='INVALID_JSON';Path=$name;Message=$_.Exception.Message});continue}
            $statusField=Get-V22FinalizerField $json 'Status'
            if($statusField.Present -and [string]$statusField.Value -ceq 'MISSING'){$issues.Add([pscustomobject]@{Code='SOURCE_EVIDENCE_MISSING';Path=$name})}
            Visit-V22ReviewIdentity $json $name $CanonicalRunId $CandidateId $issues
        }
    }
    $rootFull=[IO.Path]::GetFullPath($Root).TrimEnd('\')
    $entries=@($actualFiles | Sort-Object FullName | ForEach-Object{[pscustomobject]@{RelativePath=$_.FullName.Substring($rootFull.Length+1).Replace('\','/');Size=[long]$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
    return [ordered]@{Status=if($issues.Count -eq 0){'PASS'}else{'FAIL'};IssueCount=$issues.Count;Issues=$issues.ToArray();FileCount=$actualFiles.Count;RequiredFileCount=$required.Count;Entries=$entries}
}
function Test-V22RawArtifactHashIndex([string]$IndexPath,[string]$ArtifactRoot,[string]$CanonicalRunId){
    $issues=[Collections.Generic.List[object]]::new()
    if(-not(Test-Path -LiteralPath $IndexPath -PathType Leaf)){return [ordered]@{Status='FAIL';IssueCount=1;Issues=@([pscustomobject]@{Code='INDEX_MISSING';Path=$IndexPath});VerifiedCount=0}}
    try{$index=Get-Content -LiteralPath $IndexPath -Raw | ConvertFrom-Json -Depth 40}catch{return [ordered]@{Status='FAIL';IssueCount=1;Issues=@([pscustomobject]@{Code='INDEX_INVALID_JSON';Path=$IndexPath});VerifiedCount=0}}
    if([string]$index.RunId -cne $CanonicalRunId){$issues.Add([pscustomobject]@{Code='STALE_RUN_ID';Path=$IndexPath})}
    $entries=@($index.Entries)
    if([long]$index.Count -ne $entries.Count){$issues.Add([pscustomobject]@{Code='ENTRY_COUNT_MISMATCH';Path=$IndexPath})}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($entry in $entries){
        $relative=[string]$entry.RelativePath
        if([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or $relative.Replace('\','/').Split('/') -contains '..'){$issues.Add([pscustomobject]@{Code='UNSAFE_RELATIVE_PATH';Path=$relative});continue}
        if(-not $seen.Add($relative)){$issues.Add([pscustomobject]@{Code='DUPLICATE_RAW_PATH';Path=$relative});continue}
        $path=Join-Path $ArtifactRoot ($relative.Replace('/',[IO.Path]::DirectorySeparatorChar))
        if(-not(Test-Path -LiteralPath $path -PathType Leaf)){$issues.Add([pscustomobject]@{Code='RAW_ARTIFACT_MISSING';Path=$relative});continue}
        $item=Get-Item -LiteralPath $path;$hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if([long]$item.Length -ne [long]$entry.Size -or $hash -cne [string]$entry.SHA256){$issues.Add([pscustomobject]@{Code='RAW_ARTIFACT_HASH_MISMATCH';Path=$relative})}
    }
    $exclusions=@($index.ExcludedMutableFiles | ForEach-Object{[string]$_})
    $rootFull=[IO.Path]::GetFullPath($ArtifactRoot).TrimEnd('\')
    $actual=@(Get-ChildItem -LiteralPath $ArtifactRoot -File -Force -Recurse | ForEach-Object{[string]$_.FullName.Substring($rootFull.Length+1).Replace('\','/')} | Where-Object{$_ -notin $exclusions -and $_ -cne 'raw-artifact-hashes.json'})
    $indexed=@($entries | ForEach-Object{[string]$_.RelativePath})
    foreach($path in @($actual | Where-Object{$_ -notin $indexed})){$issues.Add([pscustomobject]@{Code='RAW_ARTIFACT_UNINDEXED';Path=$path})}
    foreach($path in @($indexed | Where-Object{$_ -notin $actual})){$issues.Add([pscustomobject]@{Code='RAW_INDEX_STALE_ENTRY';Path=$path})}
    return [ordered]@{Status=if($issues.Count -eq 0){'PASS'}else{'FAIL'};IssueCount=$issues.Count;Issues=$issues.ToArray();VerifiedCount=$entries.Count;RunId=$index.RunId;ExcludedMutableFiles=$exclusions}
}
function Test-V22FinalCleanupReadback([string]$InventoryPath,[string]$PerPidPath,[string]$HelperPath,[string]$SqlResiduePath,[string]$AggregatePath,[string]$CanonicalRunId,[string]$CandidateId){
    $issues=[Collections.Generic.List[object]]::new();$paths=@($InventoryPath,$PerPidPath,$HelperPath,$SqlResiduePath)
    $loaded=@{}
    foreach($path in @($paths)+@($AggregatePath)){if(-not(Test-Path -LiteralPath $path -PathType Leaf)){$issues.Add([pscustomobject]@{Code='REQUIRED_FILE_MISSING';Path=$path});continue};try{$loaded[$path]=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json -Depth 40}catch{$issues.Add([pscustomobject]@{Code='INVALID_JSON';Path=$path;Message=$_.Exception.Message})}}
    foreach($path in $paths){if(-not $loaded.ContainsKey($path)){continue};$evidence=$loaded[$path];if([string]$evidence.CanonicalRunId -cne $CanonicalRunId -or [string]$evidence.CandidateId -cne $CandidateId){$issues.Add([pscustomobject]@{Code='IDENTITY_MISMATCH';Path=$path})}}
    if(@($issues|Where-Object Code -in @('REQUIRED_FILE_MISSING','INVALID_JSON')).Count -gt 0){return [ordered]@{Status='FAIL';IssueCount=$issues.Count;Issues=$issues.ToArray();AggregateSHA256=$null;InputEvidence=@()}}
    $inventory=$loaded[$InventoryPath];$pidEvidence=$loaded[$PerPidPath];$helperEvidence=$loaded[$HelperPath];$sqlEvidence=$loaded[$SqlResiduePath];$aggregate=$loaded[$AggregatePath]
    try{
        $computed=Test-V22FinalCleanupEvidence $inventory $pidEvidence $helperEvidence $sqlEvidence $CanonicalRunId $CandidateId
        if([string]$aggregate.AggregateCleanupStatus -cne [string]$computed.AggregateCleanupStatus){$issues.Add([pscustomobject]@{Code='AGGREGATE_STATUS_MISMATCH';Path=$AggregatePath})}
        foreach($pair in @(@('OwnedProcessCount',[long]$inventory.ProcessCount),@('PerPidProbeCount',[long]$pidEvidence.ProbeCount),@('TelemetryHelperProcessCount',[long]$helperEvidence.ProbeCount))){$field=Get-V22FinalizerField $aggregate $pair[0];if(-not $field.Present -or [long]$field.Value -ne [long]$pair[1]){$issues.Add([pscustomobject]@{Code='AGGREGATE_COUNT_MISMATCH';Field=$pair[0]})}}
        if(-not $inventory.CapturedUtc -or -not $pidEvidence.CapturedUtc -or -not $helperEvidence.CapturedUtc -or -not $sqlEvidence.CapturedUtc -or -not $aggregate.AggregatedUtc){$issues.Add([pscustomobject]@{Code='PERSISTED_TIMESTAMP_MISSING'})}
        if([long]$inventory.ProcessCount -ne @($inventory.ProcessRecords).Count -or [long]$pidEvidence.ProbeCount -ne @($pidEvidence.Probes).Count -or [long]$helperEvidence.ProbeCount -ne @($helperEvidence.Probes).Count){$issues.Add([pscustomobject]@{Code='PERSISTED_ROW_COUNT_MISMATCH'})}
        foreach($probe in @($pidEvidence.Probes)+@($helperEvidence.Probes)){$null=ConvertTo-V22FinalizerUtc $probe.ProbeUtc 'PersistedFinalProbeUtc'}
        if($sqlEvidence.ProbeProcess){$null=ConvertTo-V22FinalizerUtc $sqlEvidence.ProbeProcess.ProbeUtc 'PersistedSqlProbeUtc'}
        $sqlOutput=Get-V22FinalizerField $sqlEvidence 'OutputPath';if($sqlOutput.Present -and -not [string]::IsNullOrWhiteSpace([string]$sqlOutput.Value) -and -not(Test-Path -LiteralPath ([string]$sqlOutput.Value) -PathType Leaf)){$issues.Add([pscustomobject]@{Code='REFERENCED_SQL_PROBE_OUTPUT_MISSING';Path=[string]$sqlOutput.Value})}
        $persistedInputs=@($aggregate.InputEvidence);$expectedInputs=@($paths|ForEach-Object{[pscustomobject]@{Path=$_;SHA256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()}})
        if($persistedInputs.Count -ne $expectedInputs.Count){$issues.Add([pscustomobject]@{Code='AGGREGATE_INPUT_COUNT_MISMATCH'})}
        foreach($expected in $expectedInputs){$match=@($persistedInputs|Where-Object Path -CEQ $expected.Path);if($match.Count -ne 1 -or [string]$match[0].SHA256 -cne $expected.SHA256){$issues.Add([pscustomobject]@{Code='AGGREGATE_INPUT_HASH_MISMATCH';Path=$expected.Path})}}
        foreach($expected in $expectedInputs){if(-not(Test-Path -LiteralPath $expected.Path -PathType Leaf)){$issues.Add([pscustomobject]@{Code='AGGREGATE_REFERENCED_FILE_MISSING';Path=$expected.Path})}}
    }catch{$issues.Add([pscustomobject]@{Code='AGGREGATE_RECOMPUTE_FAILED';Message=$_.Exception.Message})}
    $hash=(Get-FileHash -LiteralPath $AggregatePath -Algorithm SHA256).Hash.ToLowerInvariant()
    return [ordered]@{Status=if($issues.Count -eq 0){'PASS'}else{'FAIL'};IssueCount=$issues.Count;Issues=$issues.ToArray();CanonicalRunId=$CanonicalRunId;CandidateId=$CandidateId;ProcessCount=[long]$inventory.ProcessCount;PerPidProbeCount=[long]$pidEvidence.ProbeCount;HelperProbeCount=[long]$helperEvidence.ProbeCount;AggregateCleanupStatus=[string]$aggregate.AggregateCleanupStatus;AggregateSHA256=$hash;InputEvidence=$expectedInputs}
}
function New-V22CleanupCountProjection([object]$OwnedProcessInventoryCount,[object]$PerPidProbeCount,[object]$AggregateCleanupExpectedCount,[object]$CoreGateCleanupProjection,[object]$InMemoryOwnedProcessCount=$null){
    $inputs=[ordered]@{OwnedProcessInventoryCount=$OwnedProcessInventoryCount;PerPidProbeCount=$PerPidProbeCount;AggregateCleanupExpectedCount=$AggregateCleanupExpectedCount;CoreGateCleanupProjection=$CoreGateCleanupProjection}
    $parsed=[ordered]@{};$issues=[Collections.Generic.List[string]]::new()
    foreach($name in $inputs.Keys){$count=0L;if($null-eq$inputs[$name]-or-not[long]::TryParse([string]$inputs[$name],[ref]$count)-or$count-lt 0){$issues.Add(($name+'_INVALID'));continue};$parsed[$name]=$count}
    if($issues.Count-eq0){$unique=@($parsed.Values|Select-Object -Unique);if($unique.Count-ne 1){$issues.Add('CLEANUP_COUNT_MISMATCH')}}
    $memoryCount=0L;$memoryPresent=$null-ne$InMemoryOwnedProcessCount-and[long]::TryParse([string]$InMemoryOwnedProcessCount,[ref]$memoryCount)
    $classification=if($issues.Count-eq0-and$memoryPresent-and$memoryCount-ne$parsed.OwnedProcessInventoryCount){'SCHEMA_PROJECTION_DEFECT'}elseif($issues.Count-eq0){'COUNT_PROJECTION_CONSISTENT'}else{'CLEANUP_COUNT_INVARIANT_FAILED'}
    return [ordered]@{SchemaVersion='warehouse-benchmark-v22-cleanup-count-projection/1';Status=if($issues.Count-eq0){'PASS'}else{'FAIL'};Classification=$classification;OwnedProcessInventoryCount=$parsed['OwnedProcessInventoryCount'];PerPidProbeCount=$parsed['PerPidProbeCount'];AggregateCleanupExpectedCount=$parsed['AggregateCleanupExpectedCount'];CoreGateCleanupProjection=$parsed['CoreGateCleanupProjection'];InMemoryOwnedProcessCount=if($memoryPresent){$memoryCount}else{$null};CoreGateProjectionSource='PERSISTED_FINAL_CLEANUP_READBACK_INVENTORY';Invariant='OwnedProcessInventoryCount == PerPidProbeCount == AggregateCleanupExpectedCount == CoreGateCleanupProjection';Issues=$issues.ToArray()}
}
function Test-V22FinalReportLint([string]$Text){
    $issues=[Collections.Generic.List[object]]::new()
    $patterns=@('(?<![A-Za-z0-9_])\$[A-Za-z_][A-Za-z0-9_]*\b','\$\(','\$\{','\{\{[^}]+\}\}','\[\[(?:TODO|PLACEHOLDER)[^]]*\]\]','(?im)^\s*@\{[^\r\n]*\}\s*$','System\.Object\[\]','System\.Management\.Automation\.PSObject','System\.Collections\.Generic\.(?:List|Dictionary)')
    foreach($pattern in $patterns){foreach($m in [regex]::Matches([string]$Text,$pattern)){$issues.Add([pscustomobject]@{Pattern=$pattern;Match=$m.Value;Index=$m.Index})}}
    return [ordered]@{Status=if($issues.Count -eq 0){'PASS'}else{'FAIL'};IssueCount=$issues.Count;Issues=$issues.ToArray();TextSHA256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes([string]$Text))).ToLowerInvariant()}
}
Export-ModuleMember -Function Throw-V22FinalizerFailure,Get-V22FinalizerField,Get-V22OptionalNumericMetric,Get-V22MetricProjection,Get-V22MetricDisplayValue,New-V22FinalOwnedProcessInventory,Assert-V22CanonicalRunIdentity,New-V22RepresentativeRunConfigurations,New-V22MixedSemantics,Test-V22FinalCleanupEvidence,Test-V22FinalCleanupReadback,New-V22CleanupCountProjection,New-V22FinalizerJson,New-V22FinalReport,Test-V22FinalReportLint,New-V22ReviewProjectionArtifact,Test-V22FinalCanonicalReviewPack,Test-V22RawArtifactHashIndex

function Assert-V22LifecycleState([object]$State){
    $stages=@('SESSION_CREATED','INITIALIZE','PRESERVATION_PRE_GATE','STATIC_AND_OFFLINE_GATES','SOURCE_SNAPSHOT','BUILD_A','BUILD_B','CANDIDATE_BUILD_AND_ATTESTATION','CANDIDATE_FROZEN','CANDIDATE_GUARD','COMPARATOR_AND_TOOLCHAIN','EXACT_CANDIDATE_CORRECTNESS_PREFLIGHT','DB_PREFLIGHT','HOST_ADMISSION','CANONICAL_PREWORKLOAD_GATES','CANONICAL_CORRECTNESS_PREFLIGHT','ISOLATED_PERFORMANCE','ISOLATED_POST_CORRECTNESS','MIXED_PERFORMANCE','POST_MIXED','FINAL_CANDIDATE_GUARD_AND_PRESERVATION','FINAL_CLEANUP','FINALIZATION','FINAL_REPORT_AND_REVIEW_PACK')
    if([string]::IsNullOrWhiteSpace([string]$State.RunId) -or $State.Stage -notin $stages){Throw-V22FinalizerFailure 'V22_LIFECYCLE_INVALID' 'RunId or stage invalid'}
    foreach($field in @('CandidateCreated','BuildStarted','DbPreflightStarted','WorkloadStarted','PerformanceRunRootCreated')){
        $value=Get-V22FinalizerField $State $field
        if(-not $value.Present -or $value.Value -isnot [bool]){Throw-V22FinalizerFailure 'V22_LIFECYCLE_INVALID' ($field+' must be Boolean')}
    }
    if($State.DbPreflightStarted -and -not $State.CandidateCreated){Throw-V22FinalizerFailure 'V22_LIFECYCLE_INVALID' 'DB activity without candidate'}
    if($State.WorkloadStarted -and (-not $State.DbPreflightStarted -or -not $State.PerformanceRunRootCreated)){Throw-V22FinalizerFailure 'V22_LIFECYCLE_INVALID' 'workload without preflight/run root'}
    if($State.CandidateCreated -and [string]::IsNullOrWhiteSpace([string]$State.CandidateId)){Throw-V22FinalizerFailure 'V22_LIFECYCLE_INVALID' 'created candidate lacks ID'}
    return $State
}
function New-V22CrashFailure([object]$State,[object]$Exception,[string]$Reason){
    [void](Assert-V22LifecycleState $State)
    $message=[string]$Exception.Message
    $connection=[Environment]::GetEnvironmentVariable('TKS_V22_CONNECTION_STRING','Process')
    if(-not[string]::IsNullOrEmpty($connection)){$message=$message.Replace($connection,'[REDACTED_CONNECTION_STRING]')}
    $message=ConvertTo-V22SafeMessage $message
    return [ordered]@{SchemaVersion='warehouse-benchmark-v22-crash-failure/1';RunId=$State.RunId;SessionId=$State.SessionId;OriginalFailureStage=$State.Stage;OriginalFailureReason=$Reason;SafeExceptionType=$Exception.GetType().FullName;SafeExceptionMessage=$message;RecordedUtc=[DateTime]::UtcNow.ToString('o');CandidateCreated=$State.CandidateCreated;BuildStarted=$State.BuildStarted;DbPreflightStarted=$State.DbPreflightStarted;WorkloadStarted=$State.WorkloadStarted;PerformanceRunRootCreated=$State.PerformanceRunRootCreated}
}
function Get-V22EarlyCleanupDisposition([object]$State,[object[]]$Records){
    [void](Assert-V22LifecycleState $State)
    if($State.WorkloadStarted -or $State.DbPreflightStarted){Throw-V22FinalizerFailure 'V22_LIFECYCLE_INVALID' 'early cleanup cannot prove workload/DB cleanup'}
    if(@($Records).Count -eq 0){return [ordered]@{Status='CLEANUP_NOT_APPLICABLE_PREWORKLOAD';CandidateState=if($State.CandidateCreated){'CANDIDATE_CREATED'}else{'NO_CANDIDATE_CREATED'};ResourceState='NO_OWNED_PROCESS_STARTED';OwnedProcessCount=0;DatabaseActivityOccurred=$false;PerformanceCleanupProven=$false}}
    return [ordered]@{Status='REQUIRES_OWNED_PROCESS_PROBES';OwnedProcessCount=@($Records).Count;PerformanceCleanupProven=$false}
}
function Get-V22RawEvidenceState([string]$Root){
    if([string]::IsNullOrWhiteSpace($Root) -or -not(Test-Path -LiteralPath $Root -PathType Container)){return [ordered]@{Status='NOT_CREATED_BEFORE_ABORT';Entries=@();Count=0}}
    return [ordered]@{Status='CREATED';Entries=@(Get-ChildItem -LiteralPath $Root -File -Recurse);Count=@(Get-ChildItem -LiteralPath $Root -File -Recurse).Count}
}
function New-V22AbortReport([object]$Values){
    foreach($field in @('RunId','Verdict','OriginalFailureStage','OriginalFailureReason','WorkloadStarted','CanonicalWorkloadAttemptConsumed','CleanupStatus','FinalizerStage','FinalizerFailure')){if(-not(Get-V22FinalizerField $Values $field).Present){Throw-V22FinalizerFailure 'V22_ABORT_REPORT_FIELD_MISSING' $field}}
    $lines=[Collections.Generic.List[string]]::new()
    $lines.Add('# Warehouse Benchmark 2.2 - Abort / Finalizer Disposition');$lines.Add('')
    foreach($field in @('Verdict','RunId','CandidateId','OriginalFailureStage','OriginalFailureReason','SafeExceptionMessage','FinalizerStage','FinalizerFailure','CandidateCreated','BuildStarted','DbPreflightStarted','WorkloadStarted','CanonicalWorkloadAttemptConsumed','PerformanceRunRootCreated','RawArtifactState','CleanupStatus','OwnedProcessCount','PreservationStatus')){$lines.Add(('- {0}: {1}' -f $field,(Get-V22ReportValue $Values $field)))}
    $lines.Add('- CORE_EVIDENCE_READY: NO');$lines.Add('- READY_FOR_BATCH4: NO');$lines.Add('- PerformanceSLA: NO_SLA_DEFINED');$lines.Add('- HistoricalTimeoutCause: NOT_VERIFIED');$lines.Add('- FullDatasetValueEquality: NOT_VERIFIED');$lines.Add('- CausalAttribution: NOT_ESTABLISHED')
    $report=($lines -join [Environment]::NewLine)+[Environment]::NewLine
    if((Test-V22FinalReportLint $report).Status -cne 'PASS'){Throw-V22FinalizerFailure 'V22_ABORT_REPORT_LINT_FAILED' 'unmaterialized report'}
    return $report
}
Export-ModuleMember -Function Assert-V22LifecycleState,New-V22CrashFailure,Get-V22EarlyCleanupDisposition,Get-V22RawEvidenceState,New-V22AbortReport
Export-ModuleMember -Function New-V22MixedWorkerConfiguration,New-V22MixedWorkerProjection,Get-V22MixedWorkerAggregate,New-V22MixedResultRecord,Get-V22MixedResultGate
function Get-V22MixedOverlapStatus([int]$LevelCount,[bool]$AllPass){
    if($LevelCount -eq 0){return 'NOT_RUN'}
    if($LevelCount -eq 4 -and $AllPass){return 'PASS'}
    return 'FAIL'
}
Export-ModuleMember -Function Get-V22MixedOverlapStatus
