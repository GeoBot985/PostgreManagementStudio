[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs\PostgreManagementStudio'),
    [switch]$RemoveUserData
)

$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($InstallRoot)
if (Get-Process -Name 'PostgreManagementStudio.Desktop' -ErrorAction SilentlyContinue) { throw 'PostgreManagementStudio is running. Close it before uninstalling.' }
if (Test-Path $target) { Remove-Item -LiteralPath $target -Recurse -Force }
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\PostgreManagementStudio.lnk'
if (Test-Path $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
if ($RemoveUserData) {
    $dataRoot = Join-Path $env:LOCALAPPDATA 'PostgreManagementStudio'
    if (Test-Path $dataRoot) { Remove-Item -LiteralPath $dataRoot -Recurse -Force }
}
Write-Output "Application binaries and shortcut removed. User data was $([string]::Concat($(if ($RemoveUserData) {'removed by explicit request'} else {'preserved'})))."
