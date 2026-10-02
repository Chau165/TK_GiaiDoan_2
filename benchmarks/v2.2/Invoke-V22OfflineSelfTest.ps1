[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EvidencePath)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'WarehouseBenchmarkV22.Harness.psm1')
$parent=Split-Path -Parent $EvidencePath
if(-not(Test-Path -LiteralPath $parent -PathType Container)){throw 'Self-test evidence directory does not exist'}
$checks=[Collections.Generic.List[object]]::new()
function Add-Check([string]$Id,[object]$InputValue,[string]$Expected,[string]$Actual){$checks.Add([pscustomobject]@{TestId=$Id;Input=$InputValue;Expected=$Expected;Actual=$Actual;Status=$(if($Expected -ceq $Actual){'PASS'}else{'FAIL'});EvidencePath=$EvidencePath})}
$integrity=Assert-V22ReadOnlyIntegrity @{DatasetHash='d1';CurrentHash='c1'} @{DatasetHash='d1';CurrentHash='c1'}
Add-Check 'SELFTEST_READ_ONLY_MATCH' @{DatasetHash='d1';CurrentHash='c1'} 'READ_ONLY_INTEGRITY_PASS' $integrity.Status
$metrics=[ordered]@{Requests=100;Failed=0;RPS=10.0;MeanMs=5.0;P50Ms=4.0;P95Ms=8.0;P99Ms=9.0;MaxMs=10.0;WindowMs=10000;ConfiguredWindowMs=10000;ObservedWindowMs=10000;Units=[ordered]@{Requests='count';Failed='count';RPS='requests/s';MeanMs='ms';P50Ms='ms';P95Ms='ms';P99Ms='ms';MaxMs='ms';WindowMs='ms';ConfiguredWindowMs='ms';ObservedWindowMs='ms'}}
$nb=Assert-V22NBomberMetrics $metrics 10000
Add-Check 'SELFTEST_NB_FINITE_UNITS' @{Requests=100;WindowMs=10000} 'NB_METRICS_VALID' $nb.Status
$telemetry=Get-V22TelemetryAssessment @(@{Status='VALID';TargetDatabase='SyntheticDb';RunId='selftest';BlockId='block-1';SampleUtc=[DateTime]::UtcNow.ToString('o');FreeRamMb=2048;CpuPercent=10}) 'SyntheticDb' 'selftest' 'block-1'
Add-Check 'SELFTEST_TELEMETRY_TARGET' @{TargetDatabase='SyntheticDb';RunId='selftest';BlockId='block-1'} 'TELEMETRY_VALID' $telemetry.Status
$status=if(@($checks|Where-Object Status -ceq 'FAIL').Count -eq 0){'PASS'}else{'FAIL'}
$result=[ordered]@{SchemaVersion='warehouse-benchmark-v22-offline-selftest/1';ProtocolVersion='2.2';Status=$status;DatabaseAccess='NOT_USED';LoadExecution='NOT_USED';Checks=$checks.ToArray();RecordedUtc=[DateTime]::UtcNow.ToString('o')}
$json=$result|ConvertTo-Json -Depth 8
$stream=[IO.File]::Open($EvidencePath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($json+[Environment]::NewLine);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
$result
if($status -cne 'PASS'){exit 1}
