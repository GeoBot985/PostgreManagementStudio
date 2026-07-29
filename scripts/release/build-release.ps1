[CmdletBinding()]
param(
    [string]$OutputRoot = (Join-Path (Get-Location) 'artifacts\release'),
    [switch]$SkipTests,
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repo

function Invoke-Checked([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code $LASTEXITCODE." }
}

$props = Get-Content -Raw (Join-Path $repo 'Directory.Build.props')
$prefix = ([regex]::Match($props, '<VersionPrefix>([^<]+)</VersionPrefix>')).Groups[1].Value
$suffix = ([regex]::Match($props, '<VersionSuffix>([^<]+)</VersionSuffix>')).Groups[1].Value
if ([string]::IsNullOrWhiteSpace($prefix)) { throw 'VersionPrefix is missing.' }
$version = if ($suffix) { "$prefix-$suffix" } else { $prefix }
$revision = (& git rev-parse HEAD).Trim()
$dirty = @(& git status --porcelain --untracked-files=all | Where-Object { $_ -notmatch 'STATE_OF_THE_NATION\.md$' }).Count -gt 0
if ($dirty -and -not $AllowDirty) { throw 'Working tree is dirty. Commit the source or pass -AllowDirty for an explicitly marked build.' }

$root = [IO.Path]::GetFullPath($OutputRoot)
$stage = Join-Path $root "stage-$version"
$publish = Join-Path $stage 'app'
$package = Join-Path $root "PostgreManagementStudio-$version-win-x64.zip"
if (Test-Path $root) { Remove-Item -LiteralPath $root -Recurse -Force }
New-Item -ItemType Directory -Path $publish -Force | Out-Null

Invoke-Checked 'dotnet' @('restore', 'PostgreManagementStudio.sln', '--runtime', 'win-x64')
Invoke-Checked 'dotnet' @('build', 'PostgreManagementStudio.sln', '--configuration', 'Release', '--no-restore')
if (-not $SkipTests) { Invoke-Checked 'dotnet' @('test', 'PostgreManagementStudio.sln', '--configuration', 'Release', '--no-build', '--no-restore') }
Invoke-Checked 'dotnet' @('publish', 'src\PostgreManagementStudio.Desktop\PostgreManagementStudio.Desktop.csproj', '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'true', '--no-restore', '--output', $publish, '/p:PublishSingleFile=false', '/p:PublishTrimmed=false', '/p:PublishReadyToRun=false', '/p:DebugType=None', '/p:DebugSymbols=false')

Copy-Item (Join-Path $repo 'scripts\release\install.ps1') $stage
Copy-Item (Join-Path $repo 'scripts\release\uninstall.ps1') $stage
Copy-Item (Join-Path $repo 'scripts\release\verify-package.ps1') $stage
Copy-Item (Join-Path $repo 'scripts\release\LICENSE.txt') $stage
Copy-Item (Join-Path $repo 'scripts\release\THIRD-PARTY-NOTICES.txt') $stage

$files = Get-ChildItem -LiteralPath $publish -File -Recurse | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($stage.Length + 1).Replace('\','/'); size = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$manifest = [ordered]@{
    product = 'PostgreManagementStudio'; version = $version; sourceRevision = $revision
    dirtyWorkingTree = $dirty; buildConfiguration = 'Release'; architecture = 'win-x64'
    runtimeMode = 'self-contained'; installerType = 'controlled per-user PowerShell installer over offline ZIP'
    settingsSchemaVersion = 2; workspaceSchemaVersion = 1; supportedWindows = @('Windows 11 x64')
    supportedPostgreSql = @('PostgreSQL 14+', 'PostgreSQL 18.4 qualification passed')
    packageFile = [IO.Path]::GetFileName($package); packageSha256 = $null
    testStatus = if ($SkipTests) { 'not-run-by-request' } else { 'passed-by-release-build' }
    signingStatus = 'unsigned internal candidate; ready for Authenticode signing'
    files = @($files)
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $stage 'release-manifest.json') -Encoding UTF8
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $package -CompressionLevel Optimal
$packageHash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest.packageSha256 = $packageHash
$manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $root 'release-manifest.json') -Encoding UTF8
"$packageHash  $([IO.Path]::GetFileName($package))" | Set-Content (Join-Path $root 'checksums.sha256') -Encoding ASCII
$inventory = Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($stage.Length + 1).Replace('\','/'); size = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$inventory | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $root 'package-inventory.json') -Encoding UTF8
Write-Output "Created $package"
