[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackagePath)

$ErrorActionPreference = 'Stop'
$package = (Resolve-Path $PackagePath).Path
$testRoot = Join-Path ([IO.Path]::GetDirectoryName($package)) ('installer-test-' + [guid]::NewGuid().ToString('N'))
$local = Join-Path $testRoot 'LocalAppData'
$install = Join-Path $testRoot 'Programs\PostgreManagementStudio'
$extract = Join-Path $testRoot 'package'
$originalLocalAppData = $env:LOCALAPPDATA
try {
    New-Item -ItemType Directory -Path $extract -Force | Out-Null
    Expand-Archive -LiteralPath $package -DestinationPath $extract
    $env:LOCALAPPDATA = $local
    & (Join-Path $extract 'install.ps1') -InstallRoot $install
    if (-not (Test-Path (Join-Path $install 'PostgreManagementStudio.Desktop.exe'))) { throw 'Install executable missing.' }
    $data = Join-Path $local 'PostgreManagementStudio'
    New-Item -ItemType Directory -Path $data -Force | Out-Null
    'user-state' | Set-Content (Join-Path $data 'preserve.txt')
    & (Join-Path $extract 'install.ps1') -InstallRoot $install -Repair
    if (-not (Test-Path (Join-Path $data 'preserve.txt'))) { throw 'Repair lost user state.' }
    & (Join-Path $extract 'uninstall.ps1') -InstallRoot $install
    if (Test-Path $install) { throw 'Uninstall left application binaries.' }
    if (-not (Test-Path (Join-Path $data 'preserve.txt'))) { throw 'Uninstall removed user state.' }
    Write-Output 'INSTALL_REPAIR_UNINSTALL_PRESERVATION=PASS'
}
finally {
    $env:LOCALAPPDATA = $originalLocalAppData
    if (Test-Path $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
