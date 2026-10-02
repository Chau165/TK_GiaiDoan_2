[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RepoRoot,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')
$baseUri = [Uri]($root + '\')
$entryMap = @{}
$projectQueue = [Collections.Generic.Queue[string]]::new()
$visitedProjects = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$projects = [Collections.Generic.List[object]]::new()
$missingInputs = [Collections.Generic.List[string]]::new()
$generatedCompileInputs = [Collections.Generic.List[object]]::new()

function Get-NormalizedRelativePath([string]$FullPath) {
    $full = [IO.Path]::GetFullPath($FullPath)
    $relative = [Uri]::UnescapeDataString($baseUri.MakeRelativeUri([Uri]$full).ToString()).Replace('/', '\')
    if ($relative.StartsWith('..\') -or [IO.Path]::IsPathRooted($relative)) { throw "External project input is not in repository root: $full" }
    return $relative.Normalize([Text.NormalizationForm]::FormC).Replace('\', '/')
}

function Test-GeneratedPath([string]$RelativePath) {
    return ($RelativePath -match '(^|/)(bin|obj|\.git|\.codegraph|\.vs|evidence|logs|runtime|build|artifacts|test-results|node_modules)(/|$)')
}

function Add-InventoryEntry([string]$FullPath, [string]$Role, [string]$ProjectPath, [string]$ItemType, [string]$ItemIdentity) {
    $relative = Get-NormalizedRelativePath $FullPath
    if (Test-GeneratedPath $relative) { return }
    $key = $relative.ToLowerInvariant()
    if (-not $entryMap.ContainsKey($key)) {
        $exists = Test-Path -LiteralPath $FullPath -PathType Leaf
        $size = $null
        $hash = $null
        if ($exists) {
            $item = Get-Item -LiteralPath $FullPath
            $size = [long]$item.Length
            $hash = (Get-FileHash -LiteralPath $FullPath -Algorithm SHA256).Hash.ToLowerInvariant()
        } else { $missingInputs.Add($relative) }
        $entryMap[$key] = [ordered]@{ NormalizedRelativePath=$relative; Roles=[Collections.Generic.List[string]]::new(); Size=$size; SHA256=$hash; Exists=[bool]$exists; Relationships=[Collections.Generic.List[object]]::new() }
    }
    $entry = $entryMap[$key]
    if (-not $entry.Roles.Contains($Role)) { $entry.Roles.Add($Role) }
    if ($ProjectPath) { $entry.Relationships.Add([pscustomobject]@{ Project=$ProjectPath; ItemType=$ItemType; ItemIdentity=$ItemIdentity }) }
}

function Resolve-ItemPath([string]$Identity, [string]$ProjectDirectory) {
    if ([IO.Path]::IsPathRooted($Identity)) { return [IO.Path]::GetFullPath($Identity) }
    return [IO.Path]::GetFullPath((Join-Path $ProjectDirectory $Identity))
}

$benchmarkProject = Join-Path $root 'TKS_Thuc_Tap_V11_Benchmarks_V22\TKS_Thuc_Tap_V11_Benchmarks_V22.csproj'
if (-not (Test-Path -LiteralPath $benchmarkProject -PathType Leaf)) { throw 'V22 benchmark project is missing' }
$projectQueue.Enqueue($benchmarkProject)

$protocolRoot = Join-Path $root 'benchmarks\v2.2'
$sourceExtensions = @('.ps1','.psm1','.psd1','.cs','.csproj','.props','.targets','.md','.json','.config')
foreach ($file in Get-ChildItem -LiteralPath $protocolRoot -File -Recurse -Force) {
    if ($sourceExtensions -contains $file.Extension.ToLowerInvariant()) { Add-InventoryEntry $file.FullName 'protocol-source' '' '' '' }
}

while ($projectQueue.Count -gt 0) {
    $projectPath = [IO.Path]::GetFullPath($projectQueue.Dequeue())
    if (-not $visitedProjects.Add($projectPath)) { continue }
    $projectRelative = Get-NormalizedRelativePath $projectPath
    Add-InventoryEntry $projectPath 'msbuild-project' $projectRelative 'Project' $projectRelative
    $query = @(& dotnet msbuild $projectPath '-getItem:Compile,ProjectReference,PackageReference,EmbeddedResource,Content,None' '-getProperty:TargetFramework,AssemblyName,OutputType')
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) { throw "MSBuild source inventory query failed for $projectRelative (exit $exitCode)" }
    try { $evaluated = ($query -join [Environment]::NewLine) | ConvertFrom-Json } catch { throw "MSBuild source inventory output is not JSON for $projectRelative" }
    $projectDirectory = Split-Path -Parent $projectPath
    $compileItems = @($evaluated.Items.Compile)
    $projectReferences = @($evaluated.Items.ProjectReference)
    $packageReferences = @($evaluated.Items.PackageReference)
    $copyItems = [Collections.Generic.List[object]]::new()
    $externalPackageInputs = [Collections.Generic.List[object]]::new()
    foreach ($itemType in @('EmbeddedResource','Content','None')) {
        foreach ($item in @($evaluated.Items.$itemType)) {
            $copyToOutput = [string]$item.CopyToOutputDirectory
            $packProperty = $item.PSObject.Properties['Pack']
            $pack = if ($null -ne $packProperty) { [string]$packProperty.Value } else { '' }
            $definingProject = [string]$item.DefiningProjectFullPath
            if ($itemType -ne 'None' -or $copyToOutput -or $pack) {
                $normalizedDefiningProject = $definingProject.Replace([char]47,[char]92)
                $nugetMarker = ([string][char]92)+'.nuget'+([string][char]92)+'packages'+([string][char]92)
                if ($normalizedDefiningProject.IndexOf($nugetMarker,[StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    $externalPath = Resolve-ItemPath ([string]$item.Identity) $projectDirectory
                    if (-not (Test-Path -LiteralPath $externalPath -PathType Leaf)) { throw "Resolved NuGet build asset is missing: $externalPath" }
                    $normalizedExternal = [IO.Path]::GetFullPath($externalPath).Replace([char]92,[char]47)
                    $assetMarker = '/.nuget/packages/'
                    $assetMarkerAt = $normalizedExternal.IndexOf($assetMarker,[StringComparison]::OrdinalIgnoreCase)
                    if ($assetMarkerAt -lt 0) { throw "NuGet build asset identity could not be normalized: $externalPath" }
                    $assetRelative = $normalizedExternal.Substring($assetMarkerAt + $assetMarker.Length)
                    $externalPackageInputs.Add([pscustomobject]@{ PackageAsset=$assetRelative; ItemType=$itemType; CopyToOutputDirectory=$copyToOutput; SHA256=(Get-FileHash -LiteralPath $externalPath -Algorithm SHA256).Hash.ToLowerInvariant(); Size=[long](Get-Item -LiteralPath $externalPath).Length; Origin='NuGet package cache; resolved asset identity is also captured by restore/build attestation' })
                } else {
                    $copyItems.Add([pscustomobject]@{ ItemType=$itemType; Identity=[string]$item.Identity })
                }
            }
        }
    }
    foreach ($item in $compileItems) {
        $identity = [string]$item.Identity
        $full = Resolve-ItemPath $identity $projectDirectory
        $relative = Get-NormalizedRelativePath $full
        if ($relative -match '(^|/)obj/') {
            $generatedCompileInputs.Add([pscustomobject]@{ Project=$projectRelative; Path=$relative; Exists=(Test-Path -LiteralPath $full -PathType Leaf); Role='generated-msbuild-compile-input' })
        } else { Add-InventoryEntry $full 'compile-input' $projectRelative 'Compile' $identity }
    }
    foreach ($item in $copyItems) { Add-InventoryEntry (Resolve-ItemPath $item.Identity $projectDirectory) 'build-content-input' $projectRelative $item.ItemType $item.Identity }
    foreach ($reference in $projectReferences) {
        $identity = [string]$reference.Identity
        $dependency = Resolve-ItemPath $identity $projectDirectory
        $projectQueue.Enqueue($dependency)
    }
    $projects.Add([pscustomobject]@{ Path=$projectRelative; TargetFramework=[string]$evaluated.Properties.TargetFramework; AssemblyName=[string]$evaluated.Properties.AssemblyName; OutputType=[string]$evaluated.Properties.OutputType; CompileItemCount=$compileItems.Count; ExternalPackageInputs=$externalPackageInputs.ToArray(); ProjectReferences=@($projectReferences | ForEach-Object { [pscustomobject]@{ Identity=[string]$_.Identity; ResolvedPath=(Get-NormalizedRelativePath (Resolve-ItemPath ([string]$_.Identity) $projectDirectory)) } }); PackageReferences=@($packageReferences | ForEach-Object { [pscustomobject]@{ Identity=[string]$_.Identity; Version=[string]$_.Version } }); ContentInputs=@($copyItems) })
    $directory = $projectDirectory
    while ($directory.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        foreach ($name in @('Directory.Build.props','Directory.Build.targets','Directory.Packages.props','global.json','NuGet.Config','nuget.config')) {
            $config = Join-Path $directory $name
            if (Test-Path -LiteralPath $config -PathType Leaf) { Add-InventoryEntry $config 'build-configuration' $projectRelative 'ImportedConfiguration' $name }
        }
        if ($directory -eq $root) { break }
        $directory = Split-Path -Parent $directory
    }
}

$sortedEntryMap = [Collections.Generic.SortedDictionary[string,object]]::new([StringComparer]::Ordinal)
foreach ($entry in $entryMap.Values) { $sortedEntryMap.Add([string]$entry.NormalizedRelativePath, $entry) }
$entries = @($sortedEntryMap.Values)
foreach ($entry in $entries) { $entry.Roles = @($entry.Roles | Sort-Object -CaseSensitive); $entry.Relationships = @($entry.Relationships | Sort-Object Project,ItemType,ItemIdentity) }
$tab = [string][char]9
$canonicalLines = foreach ($entry in $entries) { [string]::Join($tab, @([string]$entry.NormalizedRelativePath,([string]::Join(',',@($entry.Roles))),$(if($null -eq $entry.Size){''}else{[string]$entry.Size}),$(if($null -eq $entry.SHA256){''}else{[string]$entry.SHA256}),[string]$entry.Exists)) }
$canonicalText = ($canonicalLines -join [Environment]::NewLine) + [Environment]::NewLine
$inventoryHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonicalText))).ToLowerInvariant()
$inventory = [ordered]@{ SchemaVersion='warehouse-benchmark-v22-source-inventory/1'; RootIdentity='TKS_Thuc_Tap_11 repository root'; GeneratedUtc=[DateTime]::UtcNow.ToString('o'); HashAlgorithm='SHA-256'; PathNormalization='Unicode NFC; repository-relative slash separators; case-preserving; case-insensitive duplicate detection'; Ordering='ordinal normalized relative path'; EntryCount=$entries.Count; InventorySHA256=$inventoryHash; Projects=@($projects | Sort-Object Path -CaseSensitive); GeneratedCompileInputs=@($generatedCompileInputs); MissingInputs=@($missingInputs | Sort-Object -Unique -CaseSensitive); Entries=$entries }
$json = $inventory | ConvertTo-Json -Depth 12
$parent = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw 'Output directory does not exist' }
$stream = [IO.File]::Open($OutputPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try { $bytes=[Text.UTF8Encoding]::new($false).GetBytes($json + [Environment]::NewLine); $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
$inventory

