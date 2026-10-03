[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EvidencePath,[string]$ReportLintPath)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'WarehouseBenchmarkV22.Harness.psm1')
Import-Module (Join-Path $PSScriptRoot 'WarehouseBenchmarkV22.Finalizer.psm1')
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
$canonicalRun='WHB22-PERF-RUN-SELFTEST-001';$candidate='WHB22-PERF-SELFTEST-001';$configs=New-V22RepresentativeRunConfigurations $canonicalRun $candidate @('MasterPaged','LookupPaged','DocumentPaged','DetailReportPaged','InventoryHistoricalReportPaged','InventoryCurrentBalancePaged');$identity=Assert-V22CanonicalRunIdentity $canonicalRun $canonicalRun $candidate 'V22SRC-SELFTEST' 'WHB22-SESSION-SELFTEST' $configs
Add-Check 'SELFTEST_CANONICAL_RUN_BINDING' @{Configurations=$configs.Count;RunId=$canonicalRun} 'PASS_48' ('{0}_{1}' -f $identity.Status,$configs.Count)
$mixed=New-V22MixedSemantics 'L8';Add-Check 'SELFTEST_MIXED_LOGICAL_COPIES' @{Level='L8';ScenarioProcessCount=6;CopiesPerScenario=8} '48' ([string]$mixed.TotalLogicalCopies)
$report=New-V22FinalReport ([ordered]@{Verdict='BATCH3_PARTIAL';CanonicalRunId=$canonicalRun;ProtocolRunId=$canonicalRun;IdentityStatus='PASS';CandidateId=$candidate;SourceSnapshotId='V22SRC-SELFTEST';ManifestSHA256=('a'*64);Gates=@{};CorrectnessPreflight='6/6';TotalCountPreflight='6/6';IsolatedC1C2='12/12';C4='6/6';BDN='6/6';PostIsolatedCorrectness='6/6';MixedL1='PASS';MixedL2='PASS';MixedL4='PASS';MixedL8='PASS';PostMixedCorrectness='6/6';MixedOverlap='PASS';MixedTelemetry='PASS';ProcessCount='0';PidProbeCount='0';CleanupStatus='NOT_RUN';CanonicalWorkloadAttempts=0;RecoveryDiagnosticStatus='NOT_RUN_NOT_IN_PROTOCOL';EvidencePaths=@('synthetic/evidence.json')});$reportLint=Test-V22FinalReportLint $report
Add-Check 'SELFTEST_FINAL_REPORT_LINT' @{ReportBytes=$report.Length} 'PASS' $reportLint.Status
$lintPath=if([string]::IsNullOrWhiteSpace($ReportLintPath)){Join-Path (Split-Path -Parent $EvidencePath) 'V22-Report-Template-Lint.json'}else{$ReportLintPath}
$lintEvidence=[ordered]@{SchemaVersion='warehouse-benchmark-v22-report-template-lint/1';Status=$reportLint.Status;Fixture='offline-materialized-final-report';ReportSHA256=$reportLint.TextSHA256;ReportText=$report;IssueCount=$reportLint.IssueCount;Issues=$reportLint.Issues;EvidencePath=$lintPath;DatabaseAccess='NOT_USED';PerformanceLoad='NOT_RUN';RecordedUtc=[DateTime]::UtcNow.ToString('o')}
New-V22FinalizerJson $lintPath $lintEvidence
$lintHash=(Get-FileHash -LiteralPath $lintPath -Algorithm SHA256).Hash.ToLowerInvariant()
$status=if(@($checks|Where-Object Status -ceq 'FAIL').Count -eq 0){'PASS'}else{'FAIL'}
$result=[ordered]@{SchemaVersion='warehouse-benchmark-v22-offline-selftest/1';ProtocolVersion='2.2';Status=$status;DatabaseAccess='NOT_USED';LoadExecution='NOT_USED';ReportLintStatus=$reportLint.Status;ReportLintPath=$lintPath;ReportLintSHA256=$lintHash;Checks=$checks.ToArray();RecordedUtc=[DateTime]::UtcNow.ToString('o')}
$json=$result|ConvertTo-Json -Depth 8
$stream=[IO.File]::Open($EvidencePath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($json+[Environment]::NewLine);$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
$result
if($status -cne 'PASS'){exit 1}
