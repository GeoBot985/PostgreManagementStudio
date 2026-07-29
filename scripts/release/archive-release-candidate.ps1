[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$RegressionSummaryPath,
    [string]$ArchiveRoot = (Join-Path (Get-Location) 'artifacts\release-candidates')
)

$ErrorActionPreference = 'Stop'
$package = (Resolve-Path $PackagePath).Path
$summary = (Resolve-Path $RegressionSummaryPath).Path
$releaseRoot = Split-Path $package -Parent
$manifest = Get-Content -Raw (Join-Path $releaseRoot 'release-manifest.json') | ConvertFrom-Json
$actualHash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $manifest.packageSha256) { throw 'Package checksum does not match the release manifest.' }
$destination = Join-Path ([IO.Path]::GetFullPath($ArchiveRoot)) $manifest.version
if (Test-Path $destination) { throw "Candidate archive already exists: $destination" }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath $package -Destination $destination
foreach ($file in @('release-manifest.json', 'checksums.sha256', 'package-inventory.json')) {
    Copy-Item -LiteralPath (Join-Path $releaseRoot $file) -Destination $destination
}
Copy-Item -LiteralPath $summary -Destination (Join-Path $destination 'release-regression-summary.json')
foreach ($file in @('LICENSE.txt', 'THIRD-PARTY-NOTICES.txt')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $destination
}
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
foreach ($file in @(
    "docs\release\$($manifest.version)-release-notes.md",
    "docs\release\$($manifest.version)-known-issues.md",
    'docs\release\installation-guide.md',
    'docs\release\sprint-47-rc-qualification-report.md')) {
    $source = Join-Path $repositoryRoot $file
    if (Test-Path $source) { Copy-Item -LiteralPath $source -Destination $destination }
}
[ordered]@{
    version = $manifest.version
    sourceRevision = $manifest.sourceRevision
    packageSha256 = $actualHash
    archivedAt = [DateTimeOffset]::UtcNow
    package = [IO.Path]::GetFileName($package)
    regressionSummary = [IO.Path]::GetFileName($summary)
} | ConvertTo-Json | Set-Content (Join-Path $destination 'archive-record.json') -Encoding UTF8
Write-Output "Archived immutable candidate bundle: $destination"
