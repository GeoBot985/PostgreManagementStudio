[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs\PostgreManagementStudio'),
    [switch]$Repair
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path (Join-Path $PSScriptRoot 'app')).Path
$manifestPath = Join-Path $PSScriptRoot 'release-manifest.json'
$manifest = if (Test-Path $manifestPath) { Get-Content -Raw $manifestPath | ConvertFrom-Json } else { throw 'Release manifest is missing.' }
$target = [IO.Path]::GetFullPath($InstallRoot)
$dataRoot = Join-Path $env:LOCALAPPDATA 'PostgreManagementStudio'
$logRoot = Join-Path $dataRoot 'logs'
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
$log = Join-Path $logRoot 'installer.log'
"$(Get-Date -Format o) operation=$([string]::Join('', $(if ($Repair) {'repair'} else {'install'}))) version=$($manifest.version) architecture=$($manifest.architecture) target=$target" | Add-Content $log

$running = Get-Process -Name 'PostgreManagementStudio.Desktop' -ErrorAction SilentlyContinue
if ($running) { throw 'PostgreManagementStudio is running. Close it before installing or repairing.' }
$parent = Split-Path $target -Parent
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$temporary = Join-Path $parent ('.PostgreManagementStudio-install-' + [guid]::NewGuid().ToString('N'))
$backup = $null
try {
    New-Item -ItemType Directory -Path $temporary -Force | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $temporary -Recurse -Force
    if (Test-Path $target) {
        $backup = "$target.previous-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"
        Move-Item -LiteralPath $target -Destination $backup
    }
    Move-Item -LiteralPath $temporary -Destination $target
    $shell = New-Object -ComObject WScript.Shell
    $start = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\PostgreManagementStudio.lnk'
    $shortcut = $shell.CreateShortcut($start)
    $shortcut.TargetPath = Join-Path $target 'PostgreManagementStudio.Desktop.exe'
    $shortcut.WorkingDirectory = $target
    $shortcut.Description = "PostgreManagementStudio $($manifest.version)"
    $shortcut.Save()
    [ordered]@{ product = $manifest.product; version = $manifest.version; installedAt = (Get-Date).ToUniversalTime().ToString('o'); installRoot = $target; userDataRoot = $dataRoot; upgrade = [bool]$backup } | ConvertTo-Json | Set-Content (Join-Path $target 'install-record.json') -Encoding UTF8
    if ($backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
    "$(Get-Date -Format o) result=success" | Add-Content $log
} catch {
    if (Test-Path $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue }
    if ($backup -and (Test-Path $backup) -and -not (Test-Path $target)) { Move-Item -LiteralPath $backup -Destination $target -ErrorAction SilentlyContinue }
    "$(Get-Date -Format o) result=rollback error=$($_.Exception.GetType().Name)" | Add-Content $log
    throw
}
Write-Output "Installed PostgreManagementStudio $($manifest.version) to $target. User data remains at $dataRoot."
