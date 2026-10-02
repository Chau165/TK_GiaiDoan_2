[CmdletBinding()]
param(
    [ValidateSet('Help','SelfTest','CandidateInfo')]
    [string]$Mode = 'Help',
    [string]$CandidateRoot
)
$ErrorActionPreference = 'Stop'
$harness = Join-Path $PSScriptRoot 'WarehouseBenchmarkV22.Harness.psm1'
Import-Module $harness -ErrorAction Stop
switch ($Mode) {
    'Help' { Write-Output 'Warehouse Benchmark 2.2 mixed protocol shell. Modes: Help, SelfTest, CandidateInfo. Batch 3 measured execution is coordinated by run-warehouse-benchmark-v2.2-performance.ps1 after the isolated gate.'; exit 0 }
    'SelfTest' {
        if ([string]::IsNullOrWhiteSpace($CandidateRoot)) { throw 'SelfTest requires a new -CandidateRoot.' }
        $evidencePath = Join-Path $CandidateRoot 'tests\offline-selftest.json'
        & (Join-Path $PSScriptRoot 'Invoke-V22OfflineSelfTest.ps1') -EvidencePath $evidencePath | Out-Null
        $selfTest = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
        if ($selfTest.Status -cne 'PASS') { throw 'V22_OFFLINE_SELFTEST_FAILED' }
        Write-Output $selfTest.Status
        exit 0
    }
    'CandidateInfo' {
        if ([string]::IsNullOrWhiteSpace($CandidateRoot)) { throw 'CandidateInfo requires -CandidateRoot.' }
        $manifestPath = Join-Path $CandidateRoot 'Batch1-Review-Pack\V22-Candidate-Manifest.json'
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'V22_CANDIDATE_MANIFEST_MISSING' }
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.ProtocolVersion -cne '2.2' -or $manifest.Status -cne 'CANDIDATE_BUILT_AND_ATTESTED') { throw 'V22_CANDIDATE_INVALID' }
        Write-Output $manifest.Status
        exit 0
    }
}

