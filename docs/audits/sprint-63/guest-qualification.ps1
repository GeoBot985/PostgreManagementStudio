[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagePath,

    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [string]$ExtractionRoot = 'C:\PMS-Sprint63'
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

function Invoke-CapturedCommand {
    param([string]$FilePath, [string[]]$Arguments)

    $output = & $FilePath @Arguments 2>&1
    [ordered]@{
        available = $true
        exitCode = $LASTEXITCODE
        output = @($output | ForEach-Object { [string]$_ })
    }
}

$timestamp = [DateTimeOffset]::UtcNow
$os = Get-CimInstance Win32_OperatingSystem
$computer = Get-CimInstance Win32_ComputerSystem
$processor = Get-CimInstance Win32_Processor | Select-Object -First 1
$video = Get-CimInstance Win32_VideoController | Select-Object -First 1
$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($currentIdentity)
$isAdministrator = $principal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

$dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
$dotnetSdks = if ($dotnet) {
    Invoke-CapturedCommand $dotnet.Source @('--list-sdks')
} else {
    [ordered]@{ available = $false; exitCode = $null; output = @() }
}
$dotnetRuntimes = if ($dotnet) {
    Invoke-CapturedCommand $dotnet.Source @('--list-runtimes')
} else {
    [ordered]@{ available = $false; exitCode = $null; output = @() }
}

$beforeRoots = @(
    (Join-Path $env:LOCALAPPDATA 'PostgreManagementStudio'),
    (Join-Path $env:APPDATA 'PostgreManagementStudio'),
    (Join-Path $env:LOCALAPPDATA 'Programs\PostgreManagementStudio')
)
$beforeState = $beforeRoots | ForEach-Object {
    [ordered]@{ path = $_; exists = Test-Path -LiteralPath $_ }
}

$package = Get-Item -LiteralPath $PackagePath
$packageHash = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash
$packageSignature = Get-AuthenticodeSignature -LiteralPath $package.FullName

if (Test-Path -LiteralPath $ExtractionRoot) {
    throw "Extraction root already exists: $ExtractionRoot"
}
Expand-Archive -LiteralPath $package.FullName -DestinationPath $ExtractionRoot

$allFiles = @(Get-ChildItem -LiteralPath $ExtractionRoot -File -Recurse)
$manifestPath = Join-Path $ExtractionRoot 'release-manifest.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$applicationPath = Join-Path $ExtractionRoot 'app\PostgreManagementStudio.Desktop.exe'
$applicationSignature = Get-AuthenticodeSignature -LiteralPath $applicationPath

$verificationSucceeded = $false
try {
    $verificationOutput = & (Join-Path $ExtractionRoot 'verify-package.ps1') `
        -PackagePath $package.FullName 2>&1
    $verificationSucceeded = $true
} catch {
    $verificationOutput = @($_.Exception.Message)
}
$verificationExitCode = if ($verificationSucceeded) { 0 } else { 1 }

$defender = $null
$mpStatus = Get-MpComputerStatus -ErrorAction SilentlyContinue
if ($mpStatus) {
    $defender = [ordered]@{
        antivirusEnabled = $mpStatus.AntivirusEnabled
        antispywareEnabled = $mpStatus.AntispywareEnabled
        realTimeProtectionEnabled = $mpStatus.RealTimeProtectionEnabled
        engineVersion = $mpStatus.AMEngineVersion
        signatureVersion = $mpStatus.AntivirusSignatureVersion
        signatureLastUpdated = $mpStatus.AntivirusSignatureLastUpdated
    }
    Start-MpScan -ScanType CustomScan -ScanPath $package.FullName
}

$evidence = [ordered]@{
    capturedAtUtc = $timestamp.ToString('o')
    machine = [ordered]@{
        computerName = $env:COMPUTERNAME
        manufacturer = $computer.Manufacturer
        model = $computer.Model
        machineType = 'Virtual machine'
        virtualisationPlatform = 'Oracle VirtualBox'
        osCaption = $os.Caption
        osVersion = $os.Version
        osBuild = $os.BuildNumber
        architecture = $os.OSArchitecture
        processor = $processor.Name
        memoryBytes = [long]$computer.TotalPhysicalMemory
        locale = (Get-Culture).Name
        uiCulture = (Get-UICulture).Name
        timezone = (Get-TimeZone).Id
        screenWidth = $video.CurrentHorizontalResolution
        screenHeight = $video.CurrentVerticalResolution
        user = $currentIdentity.Name
        administrator = $isAdministrator
    }
    dotnet = [ordered]@{
        sdks = $dotnetSdks
        runtimes = $dotnetRuntimes
    }
    isolationBeforeLaunch = $beforeState
    package = [ordered]@{
        path = $package.FullName
        size = $package.Length
        sha256 = $packageHash
        extractedPath = $ExtractionRoot
        extractedFileCount = $allFiles.Count
        manifest = [ordered]@{
            product = $manifest.product
            version = $manifest.version
            sourceRevision = $manifest.sourceRevision
            dirtyWorkingTree = $manifest.dirtyWorkingTree
            architecture = $manifest.architecture
            runtimeMode = $manifest.runtimeMode
            packageFile = $manifest.packageFile
            packageSha256 = $manifest.packageSha256
            signingStatus = $manifest.signingStatus
        }
        archiveSignature = [ordered]@{
            status = [string]$packageSignature.Status
            statusMessage = $packageSignature.StatusMessage
            signer = $packageSignature.SignerCertificate.Subject
        }
        executableSignature = [ordered]@{
            status = [string]$applicationSignature.Status
            statusMessage = $applicationSignature.StatusMessage
            signer = $applicationSignature.SignerCertificate.Subject
        }
        verificationExitCode = $verificationExitCode
        verificationOutput = @($verificationOutput | ForEach-Object { [string]$_ })
    }
    defender = $defender
    postExtractionUnexpectedSourceFiles = @(
        $allFiles |
            Where-Object { $_.Extension -in '.cs', '.csproj', '.sln', '.pdb', '.trx' } |
            ForEach-Object { $_.FullName.Substring($ExtractionRoot.Length + 1) }
    )
    relevantEnvironmentVariables = @(
        Get-ChildItem Env: |
            Where-Object { $_.Name -match '^(PMS_|PG|DOTNET_ROOT|CODEX)' } |
            ForEach-Object { $_.Name }
    )
}

$jsonPath = Join-Path $OutputDirectory 'clean-machine-evidence.json'
$textPath = Join-Path $OutputDirectory 'clean-machine-evidence.txt'
$evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
$evidence | Format-List | Out-String | Set-Content -LiteralPath $textPath -Encoding UTF8

Write-Output "Clean-machine evidence captured: $jsonPath"
