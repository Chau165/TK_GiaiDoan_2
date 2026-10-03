Set-StrictMode -Version Latest
$script:V22TrackedProcesses = [System.Collections.Generic.List[object]]::new()

function Throw-V22Failure([string]$Code, [string]$Message) {
    throw ("{0}|{1}" -f $Code, $Message)
}

function Get-V22Field([object]$InputObject, [string]$Name) {
    if ($null -eq $InputObject) { return $null }
    if ($InputObject -is [System.Collections.IDictionary]) {
        if ($InputObject.Contains($Name)) { return $InputObject[$Name] }
        return $null
    }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function ConvertTo-V22FiniteNumber([object]$Value, [string]$Field) {
    if ($null -eq $Value -or $Value -is [bool]) { Throw-V22Failure 'V22_INVALID_NUMBER' "$Field is missing or not numeric" }
    $text = [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture)
    if ([string]::IsNullOrWhiteSpace($text)) { Throw-V22Failure 'V22_INVALID_NUMBER' "$Field is empty" }
    $number = 0.0
    $ok = [double]::TryParse($text, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$number)
    if (-not $ok -or [double]::IsNaN($number) -or [double]::IsInfinity($number)) { Throw-V22Failure 'V22_INVALID_NUMBER' "$Field is malformed or non-finite" }
    return $number
}

function ConvertTo-V22QuantityNs([object]$Value, [object]$ExplicitUnit, [string]$Field) {
    $raw = [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture)
    if ([string]::IsNullOrWhiteSpace($raw)) { Throw-V22Failure 'V22_BDN_INVALID' "$Field is empty" }
    $match = [regex]::Match($raw, '^\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)\s*(ns|us|µs|μs|ms|s)?\s*$')
    if (-not $match.Success) { Throw-V22Failure 'V22_BDN_INVALID' "$Field is malformed" }
    $number = ConvertTo-V22FiniteNumber $match.Groups[1].Value $Field
    $unit = $match.Groups[2].Value
    if ([string]::IsNullOrWhiteSpace($unit)) { $unit = [string]$ExplicitUnit }
    if ([string]::IsNullOrWhiteSpace($unit)) { Throw-V22Failure 'V22_BDN_INVALID' "$Field unit is missing" }
    switch -CaseSensitive ($unit) {
        'ns' { $scale = 1.0 }
        'us' { $scale = 1000.0 }
        'µs' { $scale = 1000.0 }
        'μs' { $scale = 1000.0 }
        'ms' { $scale = 1000000.0 }
        's' { $scale = 1000000000.0 }
        default { Throw-V22Failure 'V22_BDN_INVALID' "$Field unit is unsupported" }
    }
    $result = $number * $scale
    if ([double]::IsNaN($result) -or [double]::IsInfinity($result) -or $result -lt 0) { Throw-V22Failure 'V22_BDN_INVALID' "$Field is negative or non-finite after unit conversion" }
    return $result
}

function Assert-V22BdnEvidence([string]$SummaryPath, [string]$MeasurementsPath) {
    foreach ($path in @($SummaryPath, $MeasurementsPath)) {
        if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { Throw-V22Failure 'V22_BDN_INVALID' 'required BDN artifact is missing' }
        if ((Get-Item -LiteralPath $path).Length -le 0) { Throw-V22Failure 'V22_BDN_INVALID' 'required BDN artifact is empty' }
    }
    try { $summaryRows = @(Import-Csv -LiteralPath $SummaryPath -Delimiter ','); $measurementRows = @(Import-Csv -LiteralPath $MeasurementsPath -Delimiter ',') }
    catch { Throw-V22Failure 'V22_BDN_INVALID' 'BDN CSV could not be parsed' }
    if ($summaryRows.Count -ne 1) { Throw-V22Failure 'V22_BDN_INVALID' 'BDN summary must contain exactly one result row' }
    $summary = $summaryRows[0]
    try {
        $meanNs = ConvertTo-V22QuantityNs (Get-V22Field $summary 'Mean') (Get-V22Field $summary 'Unit') 'Mean'
        $errorNs = ConvertTo-V22QuantityNs (Get-V22Field $summary 'Error') (Get-V22Field $summary 'Unit') 'Error'
        $stdDevNs = ConvertTo-V22QuantityNs (Get-V22Field $summary 'StdDev') (Get-V22Field $summary 'Unit') 'StdDev'
        $configured = ConvertTo-V22FiniteNumber (Get-V22Field $summary 'ConfiguredIterations') 'ConfiguredIterations'
        $reportedN = ConvertTo-V22FiniteNumber (Get-V22Field $summary 'ReportedN') 'ReportedN'
        $rawActual = ConvertTo-V22FiniteNumber (Get-V22Field $summary 'RawActualRows') 'RawActualRows'
        $rawResult = ConvertTo-V22FiniteNumber (Get-V22Field $summary 'RawResultRows') 'RawResultRows'
        $removed = ConvertTo-V22FiniteNumber (Get-V22Field $summary 'RemovedUpperOutliers') 'RemovedUpperOutliers'
        $upperFence = ConvertTo-V22FiniteNumber (Get-V22Field $summary 'UpperFenceNs') 'UpperFenceNs'
        $roundingTolerance = ConvertTo-V22FiniteNumber (Get-V22Field $summary 'MeanRoundingToleranceNs') 'MeanRoundingToleranceNs'
    } catch { Throw-V22Failure 'V22_BDN_INVALID' 'BDN summary contains missing, malformed, or non-finite required evidence' }
    if ($configured -lt 1 -or [Math]::Floor($configured) -ne $configured -or $reportedN -lt 1 -or [Math]::Floor($reportedN) -ne $reportedN -or $reportedN -gt $configured) { Throw-V22Failure 'V22_BDN_INVALID' 'configured iterations or reported N are invalid' }
    if ($rawActual -ne $configured -or $rawResult -ne $configured -or $removed -ne ($configured - $reportedN)) { Throw-V22Failure 'V22_BDN_INVALID' 'raw measurement counts do not reconcile with protocol and N' }
    $resultRows = @($measurementRows | Where-Object { ([string](Get-V22Field $_ 'IterationStage')).Trim() -ceq 'Result' })
    if ($resultRows.Count -ne $rawResult) { Throw-V22Failure 'V22_BDN_INVALID' 'WorkloadResult rows are missing or malformed' }
    $seen = [Collections.Generic.HashSet[int]]::new(); $included = [Collections.Generic.List[object]]::new()
    foreach ($row in $resultRows) {
        $iteration = 0
        if (-not [int]::TryParse([string](Get-V22Field $row 'Iteration'), [ref]$iteration) -or $iteration -lt 1 -or -not $seen.Add($iteration)) { Throw-V22Failure 'V22_BDN_INVALID' 'WorkloadResult iteration identity is malformed or duplicated' }
        $operations = ConvertTo-V22FiniteNumber (Get-V22Field $row 'Operations') 'Operations'
        $nanoseconds = ConvertTo-V22FiniteNumber (Get-V22Field $row 'Nanoseconds') 'Nanoseconds'
        $includeText = [string](Get-V22Field $row 'IncludedForMean'); $include = $false
        if ($operations -le 0 -or $nanoseconds -lt 0 -or -not [bool]::TryParse($includeText, [ref]$include)) { Throw-V22Failure 'V22_BDN_INVALID' 'WorkloadResult measurement is outside the metric contract' }
        if ($include -ne ($nanoseconds -le $upperFence)) { Throw-V22Failure 'V22_BDN_INVALID' 'upper-fence inclusion flag does not match measurement value' }
        if ($include) { $included.Add([pscustomobject]@{Nanoseconds=$nanoseconds}) }
    }
    if ($included.Count -ne $reportedN) { Throw-V22Failure 'V22_BDN_INVALID' 'included WorkloadResult rows do not equal statistical N' }
    $includedMean = ($included | Measure-Object -Property Nanoseconds -Average).Average
    if ([Math]::Abs($includedMean - $meanNs) -gt ($roundingTolerance + 0.001)) { Throw-V22Failure 'V22_BDN_INVALID' 'normalized Mean does not reconcile with included WorkloadResult values' }
    return [pscustomobject]@{ Status='BDN_COMPLETE'; MeanNs=$meanNs; ErrorNs=$errorNs; StdDevNs=$stdDevNs; ResultMeasurementRows=$resultRows.Count; ActualMeasurementRows=$rawActual; MeasuredIterations=$reportedN; RemovedUpperOutliers=$removed; SummaryPath=$SummaryPath; MeasurementsPath=$MeasurementsPath }
}
function Assert-V22NBomberMetrics([object]$Metrics, [Nullable[double]]$ExpectedConfiguredWindowMs) {
    $required = [ordered]@{ Requests='count'; Failed='count'; RPS='requests/s'; MeanMs='ms'; P50Ms='ms'; P95Ms='ms'; P99Ms='ms'; MaxMs='ms'; WindowMs='ms'; ConfiguredWindowMs='ms'; ObservedWindowMs='ms' }
    $units = Get-V22Field $Metrics 'Units'
    if ($null -eq $units) { Throw-V22Failure 'V22_NB_INVALID' 'metric units are missing' }
    $values = [ordered]@{}
    foreach ($name in $required.Keys) {
        $value = Get-V22Field $Metrics $name
        try { $number = ConvertTo-V22FiniteNumber $value $name } catch { Throw-V22Failure 'V22_NB_INVALID' ($name + ' is missing, malformed, or non-finite') }
        $unit = Get-V22Field $units $name
        if ([string]$unit -cne $required[$name]) { Throw-V22Failure 'V22_NB_INVALID' "$name unit does not match the protocol" }
        if ($number -lt 0) { Throw-V22Failure 'V22_NB_INVALID' "$name cannot be negative" }
        $values[$name] = $number
    }
    foreach ($name in @('Requests','Failed')) {
        if ([Math]::Floor($values[$name]) -ne $values[$name]) { Throw-V22Failure 'V22_NB_INVALID' "$name must be an integer count" }
    }
    if ($values.Failed -gt $values.Requests) { Throw-V22Failure 'V22_NB_INVALID' 'Failed exceeds Requests' }
    if ($values.Requests -le 0) { Throw-V22Failure 'V22_NB_INVALID' 'Requests must be positive for a measured result' }
    if ($values.ObservedWindowMs -le 0 -or $values.ConfiguredWindowMs -le 0) { Throw-V22Failure 'V22_NB_INVALID' 'configured and observed windows must be positive' }
    if ([Math]::Abs($values.WindowMs - $values.ObservedWindowMs) -gt 1.0) { Throw-V22Failure 'V22_NB_INVALID' 'WindowMs differs from ObservedWindowMs' }
    if ($null -ne $ExpectedConfiguredWindowMs -and [Math]::Abs($values.ConfiguredWindowMs - [double]$ExpectedConfiguredWindowMs) -gt 1.0) { Throw-V22Failure 'V22_NB_INVALID' 'configured window differs from run configuration' }
    # NBomber percentiles and Max are individually finite/nonnegative; no cross-field ordering gate is imposed.
    return [pscustomobject]@{ Status='NB_METRICS_VALID'; Metrics=$values; Units=$required }
}

function Assert-V22ReadOnlyIntegrity([object]$Before, [object]$After) {
    foreach ($field in @('DatasetHash','CurrentHash')) {
        $pre = [string](Get-V22Field $Before $field)
        $post = [string](Get-V22Field $After $field)
        if ([string]::IsNullOrWhiteSpace($pre) -or [string]::IsNullOrWhiteSpace($post)) { Throw-V22Failure 'V22_READ_ONLY_INTEGRITY_INVALID' "$field evidence is missing" }
        if ($pre -cne $post) { Throw-V22Failure 'V22_READ_ONLY_MUTATION' "$field changed during a read-only run" }
    }
    return [pscustomobject]@{ Status='READ_ONLY_INTEGRITY_PASS'; DatasetHash=[string](Get-V22Field $After 'DatasetHash'); CurrentHash=[string](Get-V22Field $After 'CurrentHash') }
}

function Get-V22TelemetryAssessment([object[]]$Rows, [string]$TargetDatabase, [string]$RunId, [string]$BlockId) {
    $valid = [System.Collections.Generic.List[object]]::new()
    $errors = [System.Collections.Generic.List[object]]::new()
    $rejected = [System.Collections.Generic.List[object]]::new()
    $metadataErrors = [System.Collections.Generic.List[object]]::new()
    foreach ($row in @($Rows)) {
        $status = [string](Get-V22Field $row 'Status')
        $identityMatches = ([string](Get-V22Field $row 'TargetDatabase') -ceq $TargetDatabase) -and ([string](Get-V22Field $row 'RunId') -ceq $RunId) -and ([string](Get-V22Field $row 'BlockId') -ceq $BlockId)
        if ($status -ceq 'TELEMETRY_ERROR') {
            $errors.Add($row)
            if (-not $identityMatches) { $rejected.Add([pscustomobject]@{Reason='ERROR_ROW_IDENTITY_MISMATCH';RunId=(Get-V22Field $row 'RunId');BlockId=(Get-V22Field $row 'BlockId');TargetDatabase=(Get-V22Field $row 'TargetDatabase')}) }
            $diagnostic = Test-V22TelemetryDiagnosticRow $row 'TELEMETRY_ERROR'
            foreach ($issue in $diagnostic.Errors) { $metadataErrors.Add([pscustomobject]@{SampleIndex=(Get-V22Field $row 'SampleIndex');Issue=$issue}) }
            if ($diagnostic.Status -ceq 'INVALID') { $rejected.Add($row) }
            continue
        }
        if (-not $identityMatches -or $status -cne 'VALID') { $rejected.Add($row); continue }
        try {
            $stamp = [DateTime]::Parse([string](Get-V22Field $row 'SampleUtc'), [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
            $ram = ConvertTo-V22FiniteNumber (Get-V22Field $row 'FreeRamMb') 'FreeRamMb'
            $cpu = ConvertTo-V22FiniteNumber (Get-V22Field $row 'CpuPercent') 'CpuPercent'
            if ($ram -lt 0 -or $cpu -lt 0 -or $cpu -gt 100) { throw 'telemetry values outside contract' }
            $diagnostic = Test-V22TelemetryDiagnosticRow $row 'VALID'
            foreach ($issue in $diagnostic.Errors) { $metadataErrors.Add([pscustomobject]@{SampleIndex=(Get-V22Field $row 'SampleIndex');Issue=$issue}) }
            if ($diagnostic.Status -ceq 'INVALID') { throw 'diagnostic telemetry metadata is invalid' }
            $valid.Add($row)
        } catch { $rejected.Add($row) }
    }
    $assessmentStatus = 'TELEMETRY_VALID'
    if ($valid.Count -lt 1 -or $errors.Count -gt 0 -or $rejected.Count -gt 0 -or $metadataErrors.Count -gt 0) { $assessmentStatus = 'TELEMETRY_INVALID' }
    return [pscustomobject]@{ Status=$assessmentStatus; ValidTargetRows=$valid.Count; ErrorRows=$errors.Count; RejectedRows=$rejected.Count; DiagnosticMetadataErrors=$metadataErrors.ToArray(); ConclusionAllowed=($assessmentStatus -ceq 'TELEMETRY_VALID'); TargetDatabase=$TargetDatabase; RunId=$RunId; BlockId=$BlockId }
}

function Get-V22TelemetryCsvHeader {
    return @('RunId','BlockId','TargetDatabase','SampleUtc','Status','FreeRamMb','CpuPercent','DatabaseId','ActiveRequests','BlockingRequests','ActiveRequestGrantKB','RequestedGrantKB','GrantedGrantKB','PendingMemoryGrants','ResourceSemaphoreWaiters','ActiveRequestLogicalReadsDiagnostic','TempdbServerUsedKBDiagnostic','MemoryGrantsPendingCounterDiagnostic','DeadlockCounterDiagnostic','ErrorCode','TelemetrySchemaVersion','SampleIndex','SampleStartedUtc','SampleCompletedUtc','ElapsedMs','QueryId','QueryPhase','CommandTimeoutSeconds','ConnectionState','ExceptionType','SqlErrorNumber','SqlErrorState','SqlErrorClass','SafeErrorMessage','IsTimeout','CancellationRequested','PreviousSampleStillRunning','TelemetryProcessId','TargetProcessIds')
}

function Test-V22TelemetryDiagnosticRow([object]$Row, [string]$Status) {
    $schema = [string](Get-V22Field $Row 'TelemetrySchemaVersion')
    if ([string]::IsNullOrWhiteSpace($schema)) { return [pscustomobject]@{Status='NOT_PRESENT';Errors=@()} }
    $errors = [Collections.Generic.List[string]]::new()
    if ($schema -cne 'warehouse-benchmark-v22-telemetry-csv/2') { $errors.Add('TELEMETRY_SCHEMA_VERSION_INVALID') }
    $sampleIndex = 0
    if (-not [int]::TryParse([string](Get-V22Field $Row 'SampleIndex'),[ref]$sampleIndex) -or $sampleIndex -le 0) { $errors.Add('SAMPLE_INDEX_INVALID') }
    $sampleStarted = [DateTimeOffset]::MinValue; $sampleCompleted = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse([string](Get-V22Field $Row 'SampleStartedUtc'),[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind,[ref]$sampleStarted)) { $errors.Add('SAMPLE_STARTED_UTC_INVALID') }
    if (-not [DateTimeOffset]::TryParse([string](Get-V22Field $Row 'SampleCompletedUtc'),[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind,[ref]$sampleCompleted)) { $errors.Add('SAMPLE_COMPLETED_UTC_INVALID') }
    if ($sampleCompleted -lt $sampleStarted) { $errors.Add('SAMPLE_TIME_ORDER_INVALID') }
    try { $elapsed=ConvertTo-V22FiniteNumber (Get-V22Field $Row 'ElapsedMs') 'ElapsedMs'; if($elapsed -lt 0){$errors.Add('ELAPSED_MS_INVALID')} } catch { $errors.Add('ELAPSED_MS_INVALID') }
    if ([string]::IsNullOrWhiteSpace([string](Get-V22Field $Row 'QueryId'))) { $errors.Add('QUERY_ID_MISSING') }
    if ([string]::IsNullOrWhiteSpace([string](Get-V22Field $Row 'QueryPhase'))) { $errors.Add('QUERY_PHASE_MISSING') }
    $timeout=0
    if (-not [int]::TryParse([string](Get-V22Field $Row 'CommandTimeoutSeconds'),[ref]$timeout) -or $timeout -ne 5) { $errors.Add('COMMAND_TIMEOUT_INVALID') }
    $telemetryPid=0
    if (-not [int]::TryParse([string](Get-V22Field $Row 'TelemetryProcessId'),[ref]$telemetryPid) -or $telemetryPid -le 0) { $errors.Add('TELEMETRY_PROCESS_ID_INVALID') }
    foreach($name in @('IsTimeout','CancellationRequested','PreviousSampleStillRunning')){$parsed=$false;if(-not[bool]::TryParse([string](Get-V22Field $Row $name),[ref]$parsed)){$errors.Add(($name.ToUpperInvariant()+'_INVALID'))}}
    if ($Status -ceq 'VALID') {
        $databaseId=0
        if (-not [int]::TryParse([string](Get-V22Field $Row 'DatabaseId'),[ref]$databaseId) -or $databaseId -ne 5) { $errors.Add('DATABASE_ID_INVALID') }
        if ([string](Get-V22Field $Row 'QueryPhase') -cne 'COMPLETE') { $errors.Add('VALID_QUERY_PHASE_INVALID') }
        if ([string](Get-V22Field $Row 'ConnectionState') -cne 'Open') { $errors.Add('VALID_CONNECTION_STATE_INVALID') }
        if ([string](Get-V22Field $Row 'ErrorCode') -or [string](Get-V22Field $Row 'ExceptionType') -or [string](Get-V22Field $Row 'SafeErrorMessage')) { $errors.Add('VALID_ROW_HAS_ERROR_DETAILS') }
        if ([string](Get-V22Field $Row 'PreviousSampleStillRunning') -cne 'false') { $errors.Add('SAMPLE_OVERLAP_REPORTED') }
        if ([string](Get-V22Field $Row 'CancellationRequested') -cne 'false') { $errors.Add('VALID_ROW_CANCELLED') }
    } else {
        foreach($name in @('ErrorCode','ExceptionType','SafeErrorMessage')){if([string]::IsNullOrWhiteSpace([string](Get-V22Field $Row $name))){$errors.Add(($name.ToUpperInvariant()+'_MISSING'))}}
        if ([string](Get-V22Field $Row 'ErrorCode') -ceq 'SQL_-2' -and [string](Get-V22Field $Row 'SqlErrorNumber') -cne '-2') { $errors.Add('SQL_TIMEOUT_NUMBER_MISMATCH') }
    }
    return [pscustomobject]@{Status=if($errors.Count){'INVALID'}else{'PASS'};Errors=$errors.ToArray()}
}

function Read-V22TelemetryCsvFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return [pscustomobject]@{Status='INVALID';SchemaStatus='MISSING';Rows=@();Header=@();InvalidRowCount=0;Error='FILE_MISSING'} }
    $header=@();$invalidRows=[Collections.Generic.List[int]]::new();$parseError=$null;$schemaStatus='INVALID_HEADER'
    try {
        Add-Type -AssemblyName Microsoft.VisualBasic -ErrorAction Stop | Out-Null
        $parser=[Microsoft.VisualBasic.FileIO.TextFieldParser]::new($Path,[Text.Encoding]::UTF8,$true)
        try {
            $parser.TextFieldType=[Microsoft.VisualBasic.FileIO.FieldType]::Delimited
            $parser.SetDelimiters(',');$parser.HasFieldsEnclosedInQuotes=$true;$parser.TrimWhiteSpace=$false
            $header=@($parser.ReadFields())
            $expected=Get-V22TelemetryCsvHeader
            $legacy=@('RunId','BlockId','TargetDatabase','SampleUtc','Status','FreeRamMb','CpuPercent','DatabaseId','ActiveRequests','BlockingRequests','ActiveRequestGrantKB','RequestedGrantKB','GrantedGrantKB','PendingMemoryGrants','ResourceSemaphoreWaiters','ActiveRequestLogicalReadsDiagnostic','TempdbServerUsedKBDiagnostic','MemoryGrantsPendingCounterDiagnostic','DeadlockCounterDiagnostic','ErrorCode')
            if (($header -join [char]0) -ceq ($expected -join [char]0)) { $schemaStatus='V2' }
            elseif (($header -join [char]0) -ceq ($legacy -join [char]0)) { $schemaStatus='LEGACY_V1' }
            elseif (@($header | Select-Object -Unique).Count -ne $header.Count) { $schemaStatus='DUPLICATE_HEADER' }
            $lineNumber=1
            while(-not $parser.EndOfData){$fields=$parser.ReadFields();$lineNumber++;if($null-eq$fields){continue};if($fields.Count-eq1-and[string]::IsNullOrWhiteSpace($fields[0])){continue};if($fields.Count-ne$header.Count){$invalidRows.Add($lineNumber)}}
        } finally { $parser.Dispose() }
    } catch { $parseError=$_.Exception.GetType().Name+': '+$_.Exception.Message }
    $rows=@();try{$rows=@(Import-Csv -LiteralPath $Path -Delimiter ',')}catch{if(-not$parseError){$parseError='Import-Csv: '+$_.Exception.GetType().Name}}
    $status=if($schemaStatus-eq'V2'-and$invalidRows.Count-eq0-and-not$parseError){'PASS'}else{'INVALID'}
    return [pscustomobject]@{Status=$status;SchemaStatus=$schemaStatus;Rows=$rows;Header=$header;InvalidRowCount=$invalidRows.Count;InvalidRowNumbers=$invalidRows.ToArray();Error=$parseError}
}

function Get-V22TelemetryWindowAssessment([object[]]$Rows,[DateTimeOffset]$WindowStart,[DateTimeOffset]$WindowEnd,[int]$ExpectedDatabaseId=5){
    $validRows=0;$within=0;$outside=0;$wrongDatabase=0;$invalidTimestamp=0
    foreach($row in @($Rows)){
        if([string](Get-V22Field $row 'Status')-cne'VALID'){continue}
        $stamp=[DateTimeOffset]::MinValue;$databaseId=0
        if(-not[DateTimeOffset]::TryParse([string](Get-V22Field $row 'SampleUtc'),[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind,[ref]$stamp)){$invalidTimestamp++;continue}
        if(-not[int]::TryParse([string](Get-V22Field $row 'DatabaseId'),[ref]$databaseId)-or$databaseId-ne$ExpectedDatabaseId){$wrongDatabase++;continue}
        $validRows++
        if($stamp-ge$WindowStart-and$stamp-le$WindowEnd){$within++}else{$outside++}
    }
    $status=if($validRows-gt0-and$within-gt0-and$outside-eq0-and$wrongDatabase-eq0-and$invalidTimestamp-eq0){'PASS'}else{'FAIL'}
    return [pscustomobject]@{Status=$status;ValidRows=$validRows;WithinWindowRows=$within;OutsideWindowRows=$outside;WrongDatabaseRows=$wrongDatabase;InvalidTimestampRows=$invalidTimestamp;WindowStartUtc=$WindowStart.ToString('o');WindowEndUtc=$WindowEnd.ToString('o');ExpectedDatabaseId=$ExpectedDatabaseId}
}

function Assert-V22TelemetryEvidence([object]$Assessment) {
    if ([string](Get-V22Field $Assessment 'Status') -cne 'TELEMETRY_VALID' -or [int](Get-V22Field $Assessment 'ValidTargetRows') -lt 1) { Throw-V22Failure 'V22_TELEMETRY_INVALID' 'telemetry does not contain valid target coverage' }
    return $Assessment
}

function Get-V22FinalClassification([bool]$PostIntegrityPassed, [bool]$ValidationPassed, [bool]$CooldownPassed, [bool]$PerformanceExecuted, [bool]$TelemetryRequired=$false, [bool]$TelemetryPassed=$false) {
    if (-not $PostIntegrityPassed) { return 'INVALID_READ_ONLY_MUTATION' }
    if (-not $CooldownPassed) { return 'COOLDOWN_BLOCKED' }
    if (-not $ValidationPassed) { return 'VALIDATION_FAILED' }
    if ($PerformanceExecuted -and $TelemetryRequired -and -not $TelemetryPassed) { return 'HARNESS_FAILED_TELEMETRY' }
    if (-not $PerformanceExecuted) { return 'VALIDATION_ONLY_COMPLETE' }
    return 'RUN_COMPLETE_PENDING_REVIEW'
}

function Invoke-V22BlockAfterCooldown([object]$CooldownEvidence, [scriptblock]$BlockAction) {
    $passed = Get-V22Field $CooldownEvidence 'Passed'
    if ($null -eq $passed -or -not [bool]$passed) { Throw-V22Failure 'V22_COOLDOWN_FAILED' 'next block was suppressed because cooldown did not pass' }
    return (& $BlockAction)
}

function New-V22RunRoot([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { Throw-V22Failure 'V22_INVALID_PHASE_ROOT' 'run root is required' }
    if (Test-Path -LiteralPath $Path) { Throw-V22Failure 'V22_EXISTING_PHASE_ROOT' 'run root already exists; resume is unsupported' }
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { Throw-V22Failure 'V22_INVALID_PHASE_ROOT' 'parent folder does not exist' }
    return (New-Item -ItemType Directory -Path $Path).FullName
}

function Register-V22TrackedProcess([object]$Process, [int]$ChildProcessId, [ValidateSet('CHILD','TELEMETRY')][string]$Role) {
    if ($ChildProcessId -le 0) { Throw-V22Failure 'V22_INVALID_PROCESS_ID' 'tracked child process id must be positive' }
    $script:V22TrackedProcesses.Add([pscustomobject]@{ Process=$Process; ChildProcessId=$ChildProcessId; Role=$Role; RegisteredUtc=[DateTime]::UtcNow.ToString('o') })
    return $script:V22TrackedProcesses[$script:V22TrackedProcesses.Count - 1]
}

function Start-V22TrackedProcess([string]$FilePath, [string[]]$ArgumentList, [string]$WorkingDirectory, [ValidateSet('CHILD','TELEMETRY')][string]$Role) {
    $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -WorkingDirectory $WorkingDirectory -PassThru -WindowStyle Hidden
    return (Register-V22TrackedProcess $process ([int]$process.Id) $Role)
}

function Get-V22TrackedProcesses { return @($script:V22TrackedProcesses.ToArray()) }

function Clear-V22TrackedProcesses {
    $results = [System.Collections.Generic.List[object]]::new()
    foreach ($record in @($script:V22TrackedProcesses.ToArray())) {
        $childProcessId = [int]$record.ChildProcessId
        try {
            if ($null -eq $record.Process -or $record.Process -isnot [System.Diagnostics.Process]) {
                $results.Add([pscustomobject]@{ ChildProcessId=$childProcessId; Role=$record.Role; Status='FAKE_OR_UNRESOLVED_NOT_KILLED'; Error=$null })
                continue
            }
            $process = [System.Diagnostics.Process]$record.Process
            if ($process.Id -ne $childProcessId) { throw 'tracked process id mismatch' }
            if (-not $process.HasExited) {
                $killTree = [Diagnostics.Process].GetMethod('Kill', [Type[]]@([bool]))
                if ($null -ne $killTree) { $process.Kill($true) }
                else {
                    $taskkill = Join-Path $env:SystemRoot 'System32\taskkill.exe'
                    & $taskkill /PID $childProcessId /T /F *> $null
                    if ($LASTEXITCODE -ne 0 -and -not $process.HasExited) { throw 'tracked process tree could not be terminated' }
                }
                if (-not $process.WaitForExit(5000)) { throw 'tracked process tree did not exit within timeout' }
            }
            $process.Dispose()
            $results.Add([pscustomobject]@{ ChildProcessId=$childProcessId; Role=$record.Role; Status='CLEANED'; Error=$null })
        } catch {
            $results.Add([pscustomobject]@{ ChildProcessId=$childProcessId; Role=$record.Role; Status='CLEANUP_FAILED'; Error=(ConvertTo-V22SafeMessage $_.Exception.Message) })
        }
    }
    $script:V22TrackedProcesses.Clear()
    return [pscustomobject]@{ Passed=(@($results | Where-Object Status -ceq 'CLEANUP_FAILED').Count -eq 0); Items=$results.ToArray() }
}

function ConvertTo-V22SafeMessage([string]$Message) {
    $safe = [regex]::Replace([string]$Message, '(?i)(password|pwd|token|secret|clientsecret)\s*=\s*[^;,\s]+', '$1=[REDACTED]')
    $safe = [regex]::Replace($safe, '(?i)Bearer\s+[^\s,;]+', 'Bearer [REDACTED]')
    if ($safe.Length -gt 2048) { $safe = $safe.Substring(0,2048) }
    return $safe
}

function Test-V22HostStopEvidence([object]$Evidence) {
    if ($null -eq $Evidence) { return $false }
    $reason = [string](Get-V22Field $Evidence 'Reason')
    $trigger = [string](Get-V22Field $Evidence 'TriggerName')
    $blockId = [string](Get-V22Field $Evidence 'BlockId')
    $childPid = 0
    $threshold = 0.0
    $observed = 0.0
    if ([string]::IsNullOrWhiteSpace($reason) -or [string]::IsNullOrWhiteSpace($blockId)) { return $false }
    if (-not [int]::TryParse([string](Get-V22Field $Evidence 'ChildPid'), [ref]$childPid) -or $childPid -le 0) { return $false }
    try {
        $threshold = ConvertTo-V22FiniteNumber (Get-V22Field $Evidence 'ConfiguredThresholdMB') 'ConfiguredThresholdMB'
        $observed = ConvertTo-V22FiniteNumber (Get-V22Field $Evidence 'ObservedFreeMB') 'ObservedFreeMB'
    } catch { return $false }
    $observedUtc = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse([string](Get-V22Field $Evidence 'ObservedUtc'), [ref]$observedUtc)) { return $false }
    if ((Get-V22Field $Evidence 'ActionRequested') -ne $true -or (Get-V22Field $Evidence 'ActionCompleted') -ne $true) { return $false }
    if ([string]::IsNullOrWhiteSpace([string](Get-V22Field $Evidence 'ProcessExitResult'))) { return $false }
    switch -CaseSensitive ($trigger) {
        'HOST_STOP_HARD_FLOOR' { return ($reason -ceq $trigger -and $threshold -eq 512 -and $observed -le $threshold) }
        'HOST_STOP_EMERGENCY_FLOOR' { return ($reason -ceq $trigger -and $threshold -eq 128 -and $observed -le $threshold) }
        default { return $false }
    }
}

function Get-V22WorkerTerminalState([bool]$ProcessExited, [object]$ExitCode, [bool]$CompletedMetadataPresent, [object]$HostStopEvidence) {
    if ($null -ne $HostStopEvidence) {
        if (Test-V22HostStopEvidence $HostStopEvidence) { return 'ABORTED_HOST_LIMIT' }
        return 'ABORTED_HARNESS'
    }
    if (-not $ProcessExited) { return 'ABORTED_HARNESS' }
    $exit = 0
    if (-not [int]::TryParse([string]$ExitCode, [ref]$exit)) { return 'ABORTED_HARNESS' }
    if ($exit -ne 0) { return 'ABORTED_CHILD_EXIT' }
    if (-not $CompletedMetadataPresent) { return 'ABORTED_HARNESS' }
    return 'COMPLETED'
}

function New-V22WorkerTerminalProjection([int]$ExpectedCopies, [int[]]$ObservedInstanceNumbers, [bool]$ProcessExited, [object]$ExitCode, [bool]$CompletedMetadataPresent, [object]$HostStopEvidence, [string]$RawEvidencePath) {
    $instances = @($ObservedInstanceNumbers | Sort-Object -Unique)
    $state = Get-V22WorkerTerminalState $ProcessExited $ExitCode $CompletedMetadataPresent $HostStopEvidence
    return [pscustomobject]@{
        Status = $state
        ExpectedCopies = $ExpectedCopies
        ObservedCopies = $instances.Count
        ObservedInstanceNumbers = $instances
        ProcessExitCode = $ExitCode
        StopReason = if ($null -ne $HostStopEvidence) { [string](Get-V22Field $HostStopEvidence 'Reason') } elseif ($state -eq 'ABORTED_CHILD_EXIT') { 'CHILD_EXIT_NONZERO' } else { $null }
        HostStopEvidence = $HostStopEvidence
        TimedWindowCompleted = ($state -ceq 'COMPLETED')
        RawEvidencePath = $RawEvidencePath
    }
}

function Get-V22ProcessCleanupAssessment([int[]]$OwnedProcessIds, [object[]]$ProbeResults) {
    $owned = @($OwnedProcessIds | Where-Object { $_ -gt 0 } | Sort-Object -Unique)
    $items = [Collections.Generic.List[object]]::new()
    foreach ($ownedPid in $owned) {
        $matches = @($ProbeResults | Where-Object { [int](Get-V22Field $_ 'TargetProcessId') -eq $ownedPid })
        if ($matches.Count -ne 1) {
            $items.Add([pscustomobject]@{ TargetProcessId=$ownedPid; Status='CLEANUP_UNVERIFIED'; Reason='exact target PID probe is missing or duplicated' })
            continue
        }
        $probe = $matches[0]
        $observedPid = 0
        $observedText = [string](Get-V22Field $probe 'ObservedProcessId')
        if (-not [string]::IsNullOrWhiteSpace($observedText) -and -not [int]::TryParse($observedText, [ref]$observedPid)) {
            $items.Add([pscustomobject]@{ TargetProcessId=$ownedPid; Status='CLEANUP_UNVERIFIED'; Reason='observed PID is malformed' })
            continue
        }
        if ($observedPid -gt 0 -and $observedPid -ne $ownedPid) {
            $items.Add([pscustomobject]@{ TargetProcessId=$ownedPid; ObservedProcessId=$observedPid; Status='CLEANUP_UNVERIFIED'; Reason='probe did not identify the exact owned PID' })
            continue
        }
        $probeStatus = [string](Get-V22Field $probe 'Status')
        $status = if ($probeStatus -ceq 'ABSENT' -and $observedPid -eq 0) { 'CLEAN' } elseif ($probeStatus -ceq 'PRESENT' -and $observedPid -eq $ownedPid) { 'ORPHAN_RUNNING' } else { 'CLEANUP_UNVERIFIED' }
        $items.Add([pscustomobject]@{ TargetProcessId=$ownedPid; ObservedProcessId=if($observedPid -gt 0){$observedPid}else{$null}; Status=$status; Reason=[string](Get-V22Field $probe 'Reason') })
    }
    $passed = ($owned.Count -gt 0 -and $items.Count -eq $owned.Count -and @($items | Where-Object Status -ne 'CLEAN').Count -eq 0)
    return [pscustomobject]@{ Status=if($passed){'PASS'}else{'FAIL'}; Passed=$passed; OwnedProcessIds=$owned; Items=$items.ToArray() }
}

function New-V22CorrectnessEvidenceProjection([int]$SemanticScenariosCompleted, [int]$TotalCountPassed, [string]$SemanticStatus, [string]$InventoryStatus, [string]$SecurityStatus, [string]$DatabaseStateStatus) {
    if ($SemanticScenariosCompleted -ne $TotalCountPassed) {
        Throw-V22Failure 'V22_CORRECTNESS_PROJECTION_MISMATCH' 'semantic scenario count and Total_Count count disagree'
    }
    $semanticText = '{0}/6' -f $SemanticScenariosCompleted
    $totalText = '{0}/6' -f $TotalCountPassed
    $pass = ($SemanticScenariosCompleted -eq 6 -and $TotalCountPassed -eq 6 -and $SemanticStatus -ceq 'PASS_6_OF_6' -and $InventoryStatus -ceq 'PASS_HISTORICAL_AND_CURRENT' -and $SecurityStatus -in @('NOT_APPLICABLE_BY_CONTRACT','EXISTING_RESTRICTED_USER') -and $DatabaseStateStatus -ceq 'PASS')
    return [pscustomobject]@{ Status=if($pass){'PASS'}else{'FAIL'}; SemanticStatus=$SemanticStatus; SemanticScenariosCompleted=$SemanticScenariosCompleted; SemanticScenarios=$semanticText; TotalCountPassed=$TotalCountPassed; TotalCount=$totalText; InventoryStatus=$InventoryStatus; SecurityStatus=$SecurityStatus; DatabaseStateStatus=$DatabaseStateStatus }
}

function Write-V22NewEvidenceFile([string]$Path, [string]$Text) {
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Text)
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    } finally { $stream.Dispose() }
}

function Write-V22FatalEvidence([string]$EvidenceRoot, [string]$Stage, [string]$Reason, [string]$ExceptionMessage, [object]$CleanupResult) {
    if (-not (Test-Path -LiteralPath $EvidenceRoot -PathType Container)) { Throw-V22Failure 'V22_FATAL_EVIDENCE_FAILED' 'fatal evidence root does not exist' }
    $safeMessage = ConvertTo-V22SafeMessage $ExceptionMessage
    $summary = [ordered]@{ SchemaVersion='warehouse-benchmark-v22-fatal/1'; Status='HARNESS_FAILED'; Stage=$Stage; FailureReason=$Reason; SafeExceptionMessage=$safeMessage; CleanupResult=$CleanupResult; RecordedUtc=[DateTime]::UtcNow.ToString('o') }
    $jsonPath = Join-Path $EvidenceRoot 'fatal-summary.json'
    $reasonPath = Join-Path $EvidenceRoot 'failure-reason.txt'
    Write-V22NewEvidenceFile $jsonPath (($summary | ConvertTo-Json -Depth 8) + [Environment]::NewLine)
    Write-V22NewEvidenceFile $reasonPath (("Stage={0}{1}Reason={2}{1}Exception={3}{1}" -f $Stage,[Environment]::NewLine,$Reason,$safeMessage))
    return [pscustomobject]@{ SummaryPath=$jsonPath; FailureReasonPath=$reasonPath; CleanupResult=$CleanupResult }
}

function Invoke-V22WithCleanup([scriptblock]$Work, [scriptblock]$Cleanup, [string]$EvidenceRoot, [string]$Stage) {
    $value = $null
    $primaryFailure = $null
    $cleanupResult = $null
    $cleanupFailure = $null
    try { $value = & $Work } catch { $primaryFailure = $_ }
    finally {
        try { $cleanupResult = & $Cleanup } catch { $cleanupFailure = $_ }
        $reportedCleanupPass = Get-V22Field $cleanupResult 'Passed'
        if ($null -ne $reportedCleanupPass -and -not [bool]$reportedCleanupPass -and $null -eq $cleanupFailure) { $cleanupFailure = [Management.Automation.ErrorRecord]::new([InvalidOperationException]::new('tracked process cleanup reported failure'), 'V22_CLEANUP_FAILED', [Management.Automation.ErrorCategory]::OperationStopped, $null) }
        if ($null -ne $primaryFailure -or $null -ne $cleanupFailure) {
            $reason = if ($null -ne $primaryFailure) { 'PRIMARY_FAILURE' } else { 'CLEANUP_FAILURE' }
            $message = if ($null -ne $primaryFailure) { $primaryFailure.Exception.Message } else { $cleanupFailure.Exception.Message }
            if ($null -ne $cleanupFailure) { $cleanupResult = [pscustomobject]@{ Passed=$false; PrimaryFailure=$message; CleanupFailure=(ConvertTo-V22SafeMessage $cleanupFailure.Exception.Message) } }
            try { [void](Write-V22FatalEvidence $EvidenceRoot $Stage $reason $message $cleanupResult) } catch { }
        }
    }
    if ($null -ne $primaryFailure) { throw $primaryFailure.Exception }
    if ($null -ne $cleanupFailure) { throw $cleanupFailure.Exception }
    if ($cleanupResult -is [System.Collections.IDictionary] -and $cleanupResult.Contains('Passed') -and -not [bool]$cleanupResult['Passed']) { Throw-V22Failure 'V22_CLEANUP_FAILED' 'cleanup reported failure' }
    return $value
}

Export-ModuleMember -Function @('Assert-V22BdnEvidence','Assert-V22NBomberMetrics','Assert-V22ReadOnlyIntegrity','Get-V22TelemetryAssessment','Get-V22TelemetryCsvHeader','Test-V22TelemetryDiagnosticRow','Read-V22TelemetryCsvFile','Get-V22TelemetryWindowAssessment','Assert-V22TelemetryEvidence','Get-V22FinalClassification','Invoke-V22BlockAfterCooldown','New-V22RunRoot','Register-V22TrackedProcess','Start-V22TrackedProcess','Get-V22TrackedProcesses','Clear-V22TrackedProcesses','Write-V22FatalEvidence','Invoke-V22WithCleanup','ConvertTo-V22SafeMessage','Get-V22Field','Test-V22HostStopEvidence','Get-V22WorkerTerminalState','New-V22WorkerTerminalProjection','Get-V22ProcessCleanupAssessment','New-V22CorrectnessEvidenceProjection')


