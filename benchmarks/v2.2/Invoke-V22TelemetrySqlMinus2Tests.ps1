[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EvidenceRoot,
    [Parameter(Mandatory)][string]$RuntimeDll,
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $EvidenceRoot) { throw 'Evidence root already exists; telemetry test evidence is never overwritten.' }
if (-not (Test-Path -LiteralPath $RuntimeDll -PathType Leaf)) { throw 'The diagnostic runtime DLL is missing.' }
New-Item -ItemType Directory -Path $EvidenceRoot | Out-Null
Import-Module (Join-Path $PSScriptRoot 'WarehouseBenchmarkV22.Harness.psm1') -Force

$script:TelemetryTests = [Collections.Generic.List[object]]::new()
$script:TelemetryHeader = @(Get-V22TelemetryCsvHeader)
$targetDatabase = 'TKS_Thuc_Tap_V11_Perf_10000000'
$runId = 'offline-telemetry-run'
$blockId = 'offline-telemetry-block'
$now = [DateTimeOffset]::UtcNow
$windowStart = $now.AddMinutes(-1)
$windowEnd = $now.AddMinutes(1)

function Add-TelemetryCase([string]$Id,[string]$Description,[string]$Expected,[string]$Actual,[bool]$Passed,[string]$Path) {
    $script:TelemetryTests.Add([ordered]@{TestId=$Id;Input=$Description;Expected=$Expected;Actual=$Actual;Status=if($Passed){'PASS'}else{'FAIL'};EvidencePath=$Path})
}

function New-TelemetryFixtureRow([DateTimeOffset]$SampleUtc=$now,[hashtable]$Overrides=@{}) {
    $row=[ordered]@{}
    foreach($name in $script:TelemetryHeader){$row[$name]=''}
    $row.RunId=$runId;$row.BlockId=$blockId;$row.TargetDatabase=$targetDatabase
    $row.SampleUtc=$SampleUtc.ToString('o');$row.Status='VALID';$row.FreeRamMb='2048';$row.CpuPercent='12.5';$row.DatabaseId='5'
    foreach($name in @('ActiveRequests','BlockingRequests','ActiveRequestGrantKB','RequestedGrantKB','GrantedGrantKB','PendingMemoryGrants','ResourceSemaphoreWaiters','ActiveRequestLogicalReadsDiagnostic','TempdbServerUsedKBDiagnostic','MemoryGrantsPendingCounterDiagnostic','DeadlockCounterDiagnostic')){$row[$name]='0'}
    $row.TelemetrySchemaVersion='warehouse-benchmark-v22-telemetry-csv/2';$row.SampleIndex='1'
    $row.SampleStartedUtc=$SampleUtc.ToString('o');$row.SampleCompletedUtc=$SampleUtc.AddMilliseconds(4).ToString('o');$row.ElapsedMs='4.000'
    $row.QueryId='SAMPLE_SQL_DMV_AGGREGATE/2';$row.QueryPhase='COMPLETE';$row.CommandTimeoutSeconds='5';$row.ConnectionState='Open'
    $row.IsTimeout='false';$row.CancellationRequested='false';$row.PreviousSampleStillRunning='false';$row.TelemetryProcessId=[string][Environment]::ProcessId;$row.TargetProcessIds='101;102'
    foreach($name in $Overrides.Keys){$row[$name]=[string]$Overrides[$name]}
    return [pscustomobject]$row
}

function Save-TelemetryFixture([string]$Name,[object]$Row) {
    $path=Join-Path $EvidenceRoot $Name
    $Row | Export-Csv -LiteralPath $path -NoTypeInformation -Encoding utf8
    return $path
}

function Get-TelemetryFixtureAssessment([string]$Path) {
    $file=Read-V22TelemetryCsvFile $Path
    $assessment=Get-V22TelemetryAssessment $file.Rows $targetDatabase $runId $blockId
    $window=Get-V22TelemetryWindowAssessment $file.Rows $windowStart $windowEnd 5
    return [pscustomobject]@{File=$file;Assessment=$assessment;Window=$window}
}

$validPath=Save-TelemetryFixture 'valid-sample.csv' (New-TelemetryFixtureRow)
$valid=Get-TelemetryFixtureAssessment $validPath
Add-TelemetryCase 'TELEMETRY_VALID_SAMPLE' 'one V2 valid row with matching identity, DB and window' 'PASS|TELEMETRY_VALID|PASS' "$($valid.File.Status)|$($valid.Assessment.Status)|$($valid.Window.Status)" ($valid.File.Status-eq'PASS'-and$valid.Assessment.Status-eq'TELEMETRY_VALID'-and$valid.Window.Status-eq'PASS') $validPath

$sqlTimeout=New-TelemetryFixtureRow -Overrides @{Status='TELEMETRY_ERROR';ErrorCode='SQL_-2';TelemetrySchemaVersion='warehouse-benchmark-v22-telemetry-csv/2';QueryPhase='EXECUTE_READER';ExceptionType='Microsoft.Data.SqlClient.SqlException';SqlErrorNumber='-2';SqlErrorState='0';SqlErrorClass='11';SafeErrorMessage='Execution Timeout Expired. Server=private-host;Password=private-value';IsTimeout='true'}
$sqlTimeoutPath=Save-TelemetryFixture 'sql-minus-2.csv' $sqlTimeout;$timeoutAssessment=Get-TelemetryFixtureAssessment $sqlTimeoutPath
Add-TelemetryCase 'TELEMETRY_SQL_MINUS_2' 'structured SQL timeout row' 'INVALID|1 error|0 metadata errors' "$($timeoutAssessment.Assessment.Status)|$($timeoutAssessment.Assessment.ErrorRows) error|$($timeoutAssessment.Assessment.DiagnosticMetadataErrors.Count) metadata errors" ($timeoutAssessment.Assessment.Status-eq'TELEMETRY_INVALID'-and$timeoutAssessment.Assessment.ErrorRows-eq1-and$timeoutAssessment.Assessment.DiagnosticMetadataErrors.Count-eq0) $sqlTimeoutPath

$wrongRunPath=Save-TelemetryFixture 'wrong-run.csv' (New-TelemetryFixtureRow -Overrides @{RunId='wrong-run'});$wrongRun=Get-TelemetryFixtureAssessment $wrongRunPath
Add-TelemetryCase 'TELEMETRY_WRONG_RUN' 'valid row with a different RunId' 'INVALID|rejected>0' "$($wrongRun.Assessment.Status)|rejected=$($wrongRun.Assessment.RejectedRows)" ($wrongRun.Assessment.Status-eq'TELEMETRY_INVALID'-and$wrongRun.Assessment.RejectedRows-gt0) $wrongRunPath

$wrongBlockPath=Save-TelemetryFixture 'wrong-block.csv' (New-TelemetryFixtureRow -Overrides @{BlockId='wrong-block'});$wrongBlock=Get-TelemetryFixtureAssessment $wrongBlockPath
Add-TelemetryCase 'TELEMETRY_WRONG_BLOCK' 'valid row with a different BlockId' 'INVALID|rejected>0' "$($wrongBlock.Assessment.Status)|rejected=$($wrongBlock.Assessment.RejectedRows)" ($wrongBlock.Assessment.Status-eq'TELEMETRY_INVALID'-and$wrongBlock.Assessment.RejectedRows-gt0) $wrongBlockPath

$wrongDbPath=Save-TelemetryFixture 'wrong-database.csv' (New-TelemetryFixtureRow -Overrides @{TargetDatabase='other-db'});$wrongDb=Get-TelemetryFixtureAssessment $wrongDbPath
Add-TelemetryCase 'TELEMETRY_WRONG_DATABASE' 'valid row for a different database name' 'INVALID|rejected>0' "$($wrongDb.Assessment.Status)|rejected=$($wrongDb.Assessment.RejectedRows)" ($wrongDb.Assessment.Status-eq'TELEMETRY_INVALID'-and$wrongDb.Assessment.RejectedRows-gt0) $wrongDbPath

$wrongIdPath=Save-TelemetryFixture 'wrong-database-id.csv' (New-TelemetryFixtureRow -Overrides @{DatabaseId='6'});$wrongId=Get-TelemetryFixtureAssessment $wrongIdPath
Add-TelemetryCase 'TELEMETRY_WRONG_DATABASE_ID' 'valid row with DatabaseId 6' 'INVALID|wrong DB count=1' "$($wrongId.Assessment.Status)|wrongDB=$($wrongId.Window.WrongDatabaseRows)" ($wrongId.Assessment.Status-eq'TELEMETRY_INVALID'-and$wrongId.Window.WrongDatabaseRows-eq1) $wrongIdPath

$outsideUtc=$now.AddHours(-2);$outsidePath=Save-TelemetryFixture 'outside-window.csv' (New-TelemetryFixtureRow -SampleUtc $outsideUtc);$outside=Get-TelemetryFixtureAssessment $outsidePath
Add-TelemetryCase 'TELEMETRY_OUTSIDE_WINDOW' 'valid row outside the measured block window' 'FAIL|outside=1' "$($outside.Window.Status)|outside=$($outside.Window.OutsideWindowRows)" ($outside.Window.Status-eq'FAIL'-and$outside.Window.OutsideWindowRows-eq1) $outsidePath

$overlapPath=Save-TelemetryFixture 'previous-sample-overlap.csv' (New-TelemetryFixtureRow -Overrides @{PreviousSampleStillRunning='true'});$overlap=Get-TelemetryFixtureAssessment $overlapPath
Add-TelemetryCase 'TELEMETRY_PREVIOUS_SAMPLE_OVERLAP' 'valid row marked as overlapping previous sample' 'INVALID|metadata error' "$($overlap.Assessment.Status)|metadata=$($overlap.Assessment.DiagnosticMetadataErrors.Count)" ($overlap.Assessment.Status-eq'TELEMETRY_INVALID'-and$overlap.Assessment.DiagnosticMetadataErrors.Count-gt0) $overlapPath

$cancelPath=Save-TelemetryFixture 'cancellation.csv' (New-TelemetryFixtureRow -Overrides @{Status='TELEMETRY_ERROR';ErrorCode='CANCELED';QueryPhase='EXECUTE_READER';ExceptionType='System.OperationCanceledException';SafeErrorMessage='Operation canceled';CancellationRequested='true'});$cancel=Get-TelemetryFixtureAssessment $cancelPath
Add-TelemetryCase 'TELEMETRY_CANCELLATION' 'canceled query attempt' 'INVALID|error=1|metadata=0' "$($cancel.Assessment.Status)|error=$($cancel.Assessment.ErrorRows)|metadata=$($cancel.Assessment.DiagnosticMetadataErrors.Count)" ($cancel.Assessment.Status-eq'TELEMETRY_INVALID'-and$cancel.Assessment.ErrorRows-eq1-and$cancel.Assessment.DiagnosticMetadataErrors.Count-eq0) $cancelPath

$connectionPath=Save-TelemetryFixture 'connection-failure.csv' (New-TelemetryFixtureRow -Overrides @{Status='TELEMETRY_ERROR';ErrorCode='IOException';QueryPhase='OPEN_CONNECTION';ConnectionState='Closed';ExceptionType='System.IO.IOException';SafeErrorMessage='connection refused'});$connection=Get-TelemetryFixtureAssessment $connectionPath
Add-TelemetryCase 'TELEMETRY_CONNECTION_FAILURE' 'connection open failure with recorded phase/type/message' 'INVALID|error=1|metadata=0' "$($connection.Assessment.Status)|error=$($connection.Assessment.ErrorRows)|metadata=$($connection.Assessment.DiagnosticMetadataErrors.Count)" ($connection.Assessment.Status-eq'TELEMETRY_INVALID'-and$connection.Assessment.ErrorRows-eq1-and$connection.Assessment.DiagnosticMetadataErrors.Count-eq0) $connectionPath

$nonTimeoutPath=Save-TelemetryFixture 'non-timeout-sql-error.csv' (New-TelemetryFixtureRow -Overrides @{Status='TELEMETRY_ERROR';ErrorCode='SQL_208';QueryPhase='EXECUTE_READER';ExceptionType='Microsoft.Data.SqlClient.SqlException';SqlErrorNumber='208';SqlErrorState='1';SqlErrorClass='16';SafeErrorMessage='Invalid object name';IsTimeout='false'});$nonTimeout=Get-TelemetryFixtureAssessment $nonTimeoutPath
Add-TelemetryCase 'TELEMETRY_NON_TIMEOUT_SQL_ERROR' 'SQL error 208 is not classified as timeout' 'INVALID|SQL_208|timeout=false' "$($nonTimeout.Assessment.Status)|$($nonTimeout.File.Rows[0].ErrorCode)|timeout=$($nonTimeout.File.Rows[0].IsTimeout)" ($nonTimeout.Assessment.Status-eq'TELEMETRY_INVALID'-and$nonTimeout.File.Rows[0].ErrorCode-eq'SQL_208'-and$nonTimeout.File.Rows[0].IsTimeout-eq'false') $nonTimeoutPath

$missingPath=Join-Path $EvidenceRoot 'missing-column.csv';$missingHeader=$script:TelemetryHeader[0..($script:TelemetryHeader.Count-2)];[IO.File]::WriteAllLines($missingPath,@(($missingHeader-join',')),[Text.UTF8Encoding]::new($false));$missing=Read-V22TelemetryCsvFile $missingPath
Add-TelemetryCase 'TELEMETRY_MISSING_COLUMN' 'V2 header missing the final required column' 'INVALID|INVALID_HEADER' "$($missing.Status)|$($missing.SchemaStatus)" ($missing.Status-eq'INVALID'-and$missing.SchemaStatus-eq'INVALID_HEADER') $missingPath

$extraPath=Join-Path $EvidenceRoot 'extra-field.csv';$simpleValues=@($script:TelemetryHeader|ForEach-Object{[string]$valid.File.Rows[0].PSObject.Properties[$_].Value});[IO.File]::WriteAllLines($extraPath,@(($script:TelemetryHeader-join','),(($simpleValues+@('unexpected'))-join',')),[Text.UTF8Encoding]::new($false));$extra=Read-V22TelemetryCsvFile $extraPath
Add-TelemetryCase 'TELEMETRY_EXTRA_FIELD' 'row contains one field beyond the fixed V2 header' 'INVALID|invalid rows=1' "$($extra.Status)|invalid rows=$($extra.InvalidRowCount)" ($extra.Status-eq'INVALID'-and$extra.InvalidRowCount-eq1) $extraPath

$malformedPath=Join-Path $EvidenceRoot 'malformed-quoted-field.csv';[IO.File]::WriteAllLines($malformedPath,@(($script:TelemetryHeader-join','),'"unclosed quote'),[Text.UTF8Encoding]::new($false));$malformed=Read-V22TelemetryCsvFile $malformedPath
Add-TelemetryCase 'TELEMETRY_MALFORMED_CSV' 'unterminated quoted CSV field' 'INVALID|parse error present' "$($malformed.Status)|parse error=$([bool]$malformed.Error)" ($malformed.Status-eq'INVALID'-and-not[string]::IsNullOrWhiteSpace([string]$malformed.Error)) $malformedPath

$selfTestOutput=& dotnet $RuntimeDll performance-telemetry-self-test 2>&1
$selfTestExit=$LASTEXITCODE;$selfTestJson=$null
try{$selfTestJson=($selfTestOutput -join [Environment]::NewLine)|ConvertFrom-Json}catch{}
$selfTestPath=Join-Path $EvidenceRoot 'V22-Performance-Telemetry-SelfTest.json'
@{ExitCode=$selfTestExit;Result=$selfTestJson;Output=if($null-eq$selfTestJson){@($selfTestOutput)}else{$null};Workload='NOT_RUN';DatabaseAccess='NOT_USED'}|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $selfTestPath -Encoding utf8NoBOM
$recoveryTest=@($selfTestJson.Tests|Where-Object TestId -eq 'TELEMETRY_TIMEOUT_THEN_SUCCESS'|Select-Object -First 1)
$recoveryPass=($recoveryTest.Count-eq1-and$recoveryTest[0].Status-eq'PASS')
Add-TelemetryCase 'TELEMETRY_TIMEOUT_THEN_SUCCESS' 'first telemetry sample times out; next sample runs and is valid' 'PASS|SQL_-2|VALID' $(if($recoveryTest.Count){$recoveryTest[0].Actual}else{'SELF_TEST_MISSING'}) ($selfTestExit-eq0-and$recoveryPass) $selfTestPath

$passed=@($script:TelemetryTests|Where-Object Status -eq 'PASS').Count
$result=[ordered]@{SchemaVersion='warehouse-benchmark-v22-telemetry-sql-minus-2-tests/1';Status=if($passed-eq$script:TelemetryTests.Count){'PASS'}else{'FAIL'};TestCount=$script:TelemetryTests.Count;Passed=$passed;Failed=$script:TelemetryTests.Count-$passed;DatabaseAccess='NOT_USED';PerformanceWorkload='NOT_RUN';RuntimeDll=$RuntimeDll;Tests=$script:TelemetryTests.ToArray()}
$resultPath=Join-Path $EvidenceRoot 'V22-Telemetry-SQLMinus2-Tests.json'
$result|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $resultPath -Encoding utf8NoBOM
$result|ConvertTo-Json -Depth 6
if($result.Status-ne'PASS'){exit 1}
