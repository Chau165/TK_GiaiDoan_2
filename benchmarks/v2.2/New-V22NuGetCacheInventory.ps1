[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PackageCacheRoot,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($PackageCacheRoot).TrimEnd('\')
if(-not(Test-Path -LiteralPath $root -PathType Container)){throw 'V22_NUGET_CACHE_MISSING|package cache root is missing'}
if(Test-Path -LiteralPath $OutputPath){throw 'V22_NUGET_CACHE_OUTPUT_EXISTS|inventory output is immutable'}
$prefix=$root+'\'
$sha=[Security.Cryptography.SHA256]::Create()
$files=[Collections.Generic.List[object]]::new()
foreach($full in [IO.Directory]::EnumerateFiles($root,'*',[IO.SearchOption]::AllDirectories)){
    $info=[IO.FileInfo]::new($full)
    $relative=$full.Substring($prefix.Length).Replace('\','/')
    $stream=[IO.File]::OpenRead($full)
    try{$hash=[Convert]::ToHexString($sha.ComputeHash($stream)).ToLowerInvariant()}finally{$stream.Dispose()}
    $files.Add([pscustomobject]@{RelativePath=$relative;Size=[long]$info.Length;SHA256=$hash})
}
$entries=$files.ToArray()
$paths=[string[]]@($entries|ForEach-Object RelativePath)
[Array]::Sort($paths,[StringComparer]::Ordinal)
$entryMap=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
foreach($entry in $entries){$entryMap.Add([string]$entry.RelativePath,$entry)}
$tree=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
try{
    foreach($relative in $paths){$entry=$entryMap[$relative];$canonical=$relative+[char]9+[string]$entry.Size+[char]9+$entry.SHA256+[char]10;$bytes=[Text.UTF8Encoding]::new($false).GetBytes($canonical);$tree.AppendData($bytes)}
    $inventoryHash=[Convert]::ToHexString($tree.GetHashAndReset()).ToLowerInvariant()
}finally{$tree.Dispose();$sha.Dispose()}
$packages=[Collections.Generic.List[object]]::new()
foreach($idDirectory in Get-ChildItem -LiteralPath $root -Directory|Sort-Object Name -CaseSensitive){
    foreach($versionDirectory in Get-ChildItem -LiteralPath $idDirectory.FullName -Directory|Sort-Object Name -CaseSensitive){
        $metadataPath=Join-Path $versionDirectory.FullName '.nupkg.metadata'
        $metadata=$null
        if(Test-Path -LiteralPath $metadataPath -PathType Leaf){try{$metadata=Get-Content -LiteralPath $metadataPath -Raw|ConvertFrom-Json}catch{}}
        $id=$idDirectory.Name.ToLowerInvariant();$version=$versionDirectory.Name.ToLowerInvariant()
        $shaPath=Join-Path $versionDirectory.FullName ($id+'.'+$version+'.nupkg.sha512')
        $declaredHash=if(Test-Path -LiteralPath $shaPath -PathType Leaf){(Get-Content -LiteralPath $shaPath -Raw).Trim()}else{$null}
        $source=[string]$metadata.source
        if($source -match '^https?://'){
            try{$uri=[Uri]$source;$source=$uri.GetLeftPart([UriPartial]::Path)}catch{$source='URL_SOURCE_REDACTED'}
        }elseif(-not[string]::IsNullOrWhiteSpace($source)){$source='LOCAL_SOURCE:'+([IO.Path]::GetFileName($source.TrimEnd([char]'\',[char]'/')))}
        $packages.Add([pscustomobject]@{PackageId=$id;Version=$version;DeclaredNupkgSHA512=$declaredHash;SourceIdentity=$source;MetadataPresent=($null -ne $metadata);PackagePath=$id+'/'+$version})
    }
}
$missing=@($packages|Where-Object{-not $_.MetadataPresent -or [string]::IsNullOrWhiteSpace($_.DeclaredNupkgSHA512)})
$status=if($entries.Count -gt 0 -and $packages.Count -gt 0 -and $missing.Count -eq 0){'PASS'}else{'INVALID'}
$result=[ordered]@{SchemaVersion='warehouse-benchmark-v22-nuget-cache-inventory/1';Status=$status;PackageCacheRoot=$root;HashAlgorithm='SHA-256';TreeHashAlgorithm='UTF-8 sorted ordinal rows: relative-path<TAB>size<TAB>file-sha256<LF>';InventorySHA256=$inventoryHash;FileCount=$entries.Count;PackageCount=$packages.Count;TotalBytes=[long](($entries|Measure-Object Size -Sum).Sum);MissingPackageMetadataCount=$missing.Count;Packages=$packages.ToArray();Entries=$entries;CapturedUtc=[DateTime]::UtcNow.ToString('o')}
$json=$result|ConvertTo-Json -Depth 8
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath),$json+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
[pscustomobject]@{Status=$status;InventorySHA256=$inventoryHash;PackageCount=$packages.Count;FileCount=$entries.Count;TotalBytes=$result.TotalBytes;MissingPackageMetadataCount=$missing.Count;OutputPath=[IO.Path]::GetFullPath($OutputPath)}
