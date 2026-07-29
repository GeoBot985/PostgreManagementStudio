[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackagePath)
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path $PackagePath).Path
$temp = Join-Path ([IO.Path]::GetTempPath()) ('pms-package-' + [guid]::NewGuid().ToString('N'))
try {
    Expand-Archive -LiteralPath $package -DestinationPath $temp
    $files = Get-ChildItem -LiteralPath $temp -File -Recurse
    $names = $files.Name
    if ($names | Where-Object { $_ -match '\.(pdb|trx|coverage|bak)$' }) { throw 'Debug, test, coverage, or backup artefact found.' }
    $textFiles = $files | Where-Object { $_.Name -ne 'verify-package.ps1' -and $_.Extension -in '.json','.ps1','.txt','.config','.xml','.deps','.runtimeconfig' }
    if ($textFiles | Where-Object { (Get-Content -Raw $_.FullName -ErrorAction SilentlyContinue) -match 'qwerty123|Host=localhost;Port=5432;Database=' }) { throw 'Seeded credential or development connection found.' }
    $manifest = Get-Content -Raw (Join-Path $temp 'release-manifest.json') | ConvertFrom-Json
    if (-not $manifest.version -or -not $manifest.sourceRevision) { throw 'Manifest is incomplete.' }
    if (-not (Test-Path (Join-Path $temp 'app\PostgreManagementStudio.Desktop.exe'))) { throw 'Published executable is missing.' }
    if (-not (Test-Path (Join-Path $temp 'install.ps1'))) { throw 'Installer is missing.' }
    Write-Output "Package verification passed: $($manifest.product) $($manifest.version), $($files.Count) files."
} finally { if (Test-Path $temp) { Remove-Item -LiteralPath $temp -Recurse -Force } }
