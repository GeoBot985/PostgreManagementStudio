[CmdletBinding()]
param(
    [string]$AdminConnectionString = $env:PMS_ADMIN_CONNECTION_STRING,
    [string]$PostgreSqlBin,
    [int]$Repeat = 1,
    [switch]$KeepDatabase,
    [switch]$SkipCoverage
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runId = [Guid]::NewGuid().ToString('N').Substring(0, 10)
$resultsDirectory = Join-Path (Join-Path $repositoryRoot 'TestResults') $runId
$testDatabase = "pms_regression_$runId"
$appRole = "pms_app_$runId"
$readonlyRole = "pms_readonly_$runId"
$restrictedRole = "pms_restricted_$runId"
$appPassword = [Guid]::NewGuid().ToString('N')
$readonlyPassword = [Guid]::NewGuid().ToString('N')
$restrictedPassword = [Guid]::NewGuid().ToString('N')
$environmentCreated = $false
$exitCode = 0
$startedAt = [DateTimeOffset]::UtcNow
$managedEnvironmentNames = @(
    'PGPASSWORD',
    'PMS_ADMIN_CONNECTION_STRING',
    'PMS_CONNECTION_STRING',
    'PMS_TEST_READONLY_CONNECTION_STRING',
    'PMS_TEST_RESTRICTED_CONNECTION_STRING',
    'PMS_TEST_DATABASE',
    'PMS_TEST_PG_BIN',
    'PMS_RUN_PERF'
)
$originalEnvironment = @{}
foreach ($name in $managedEnvironmentNames) {
    $item = Get-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
    $originalEnvironment[$name] = if ($null -eq $item) { $null } else { $item.Value }
}

function Get-ConnectionValue {
    param([System.Data.Common.DbConnectionStringBuilder]$Builder, [string[]]$Names, [string]$Default = '')
    foreach ($name in $Names) {
        $value = $null
        if ($Builder.TryGetValue($name, [ref]$value)) { return [string]$value }
    }
    return $Default
}

function Invoke-Psql {
    param([string]$Database, [string[]]$Arguments)
    $baseArguments = @('-X', '--no-psqlrc', '--no-password', '-v', 'ON_ERROR_STOP=1', '--host', $script:hostName, '--port', $script:port, '--username', $script:adminUser, '--dbname', $Database)
    & $script:psql @baseArguments @Arguments
    if ($LASTEXITCODE -ne 0) { throw "psql failed with exit code $LASTEXITCODE." }
}

if ([string]::IsNullOrWhiteSpace($AdminConnectionString)) {
    $AdminConnectionString = $env:PMS_CONNECTION_STRING
}
if ([string]::IsNullOrWhiteSpace($AdminConnectionString)) {
    throw 'Set PMS_ADMIN_CONNECTION_STRING (or PMS_CONNECTION_STRING) to an administrative PostgreSQL connection.'
}
if ($Repeat -lt 1) { throw 'Repeat must be at least 1.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet was not found on PATH.' }

$connectionBuilder = [System.Data.Common.DbConnectionStringBuilder]::new()
$connectionBuilder.set_ConnectionString($AdminConnectionString)
$hostName = Get-ConnectionValue $connectionBuilder @('Host', 'Server') 'localhost'
$port = Get-ConnectionValue $connectionBuilder @('Port') '5432'
$adminUser = Get-ConnectionValue $connectionBuilder @('Username', 'User ID', 'UserId') 'postgres'
$adminPassword = Get-ConnectionValue $connectionBuilder @('Password')
$adminDatabase = Get-ConnectionValue $connectionBuilder @('Database', 'Initial Catalog') 'postgres'

if ([string]::IsNullOrWhiteSpace($PostgreSqlBin)) {
    $candidate = Get-ChildItem 'C:\Program Files\PostgreSQL' -Recurse -Filter psql.exe -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($candidate) { $PostgreSqlBin = Split-Path -Parent $candidate.FullName }
}
$psql = if ($PostgreSqlBin) { Join-Path $PostgreSqlBin 'psql.exe' } else { (Get-Command psql -ErrorAction SilentlyContinue).Source }
$pgDump = if ($PostgreSqlBin) { Join-Path $PostgreSqlBin 'pg_dump.exe' } else { (Get-Command pg_dump -ErrorAction SilentlyContinue).Source }
$pgRestore = if ($PostgreSqlBin) { Join-Path $PostgreSqlBin 'pg_restore.exe' } else { (Get-Command pg_restore -ErrorAction SilentlyContinue).Source }
foreach ($tool in @($psql, $pgDump, $pgRestore)) {
    if ([string]::IsNullOrWhiteSpace($tool) -or -not (Test-Path -LiteralPath $tool)) { throw 'Required PostgreSQL tools (psql, pg_dump, pg_restore) were not found.' }
}

$env:PGPASSWORD = $adminPassword
New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null

try {
    Invoke-Psql $adminDatabase @('--command', 'SELECT current_setting(''server_version''), current_database(), current_user;')
    $createRoles = "CREATE ROLE `"$appRole`" LOGIN PASSWORD '$appPassword'; CREATE ROLE `"$readonlyRole`" LOGIN PASSWORD '$readonlyPassword'; CREATE ROLE `"$restrictedRole`" LOGIN PASSWORD '$restrictedPassword';"
    Invoke-Psql $adminDatabase @('--command', $createRoles)
    Invoke-Psql $adminDatabase @('--command', "CREATE DATABASE `"$testDatabase`" OWNER `"$appRole`";")
    $environmentCreated = $true

    Invoke-Psql $testDatabase @(
        '--set', "test_database=$testDatabase",
        '--set', "app_role=$appRole",
        '--set', "readonly_role=$readonlyRole",
        '--set', "restricted_role=$restrictedRole",
        '--file', (Join-Path $repositoryRoot 'scripts\testing\seed.sql')
    )

    $env:PMS_ADMIN_CONNECTION_STRING = "Host=$hostName;Port=$port;Database=$testDatabase;Username=$adminUser;Password=$adminPassword;Application Name=PostgreManagementStudio Regression Admin"
    $env:PMS_CONNECTION_STRING = "Host=$hostName;Port=$port;Database=$testDatabase;Username=$appRole;Password=$appPassword;Application Name=PostgreManagementStudio Regression"
    $env:PMS_TEST_READONLY_CONNECTION_STRING = "Host=$hostName;Port=$port;Database=$testDatabase;Username=$readonlyRole;Password=$readonlyPassword"
    $env:PMS_TEST_RESTRICTED_CONNECTION_STRING = "Host=$hostName;Port=$port;Database=$testDatabase;Username=$restrictedRole;Password=$restrictedPassword"
    $env:PMS_TEST_DATABASE = $testDatabase
    $env:PMS_TEST_PG_BIN = $PostgreSqlBin
    $env:PMS_RUN_PERF = '1'

    Push-Location $repositoryRoot
    try {
        dotnet restore PostgreManagementStudio.sln --force-evaluate
        if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
        dotnet build PostgreManagementStudio.sln --configuration Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

        for ($iteration = 1; $iteration -le $Repeat; $iteration++) {
            $testArguments = @(
                'test', 'PostgreManagementStudio.sln',
                '--configuration', 'Release',
                '--no-build', '--no-restore',
                '--logger', "trx;LogFilePrefix=release-$iteration",
                '--results-directory', $resultsDirectory
            )
            if (-not $SkipCoverage -and $iteration -eq 1) { $testArguments += @('--collect', 'XPlat Code Coverage') }
            & dotnet @testArguments
            if ($LASTEXITCODE -ne 0) { throw "Regression iteration $iteration failed." }
        }
    }
    finally {
        Pop-Location
    }
}
catch {
    $exitCode = 1
    Write-Error $_
}
finally {
    if ($environmentCreated -and -not $KeepDatabase) {
        try {
            Invoke-Psql $adminDatabase @('--command', "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='$testDatabase' AND pid <> pg_backend_pid();")
            Invoke-Psql $adminDatabase @('--command', "DROP DATABASE IF EXISTS `"$testDatabase`";")
            Invoke-Psql $adminDatabase @('--command', "DROP ROLE IF EXISTS `"$readonlyRole`"; DROP ROLE IF EXISTS `"$restrictedRole`"; DROP ROLE IF EXISTS `"$appRole`";")
            $environmentCreated = $false
        }
        catch {
            $exitCode = 1
            Write-Error "Test environment cleanup failed: $_"
        }
    }
    foreach ($name in $managedEnvironmentNames) {
        if ($null -eq $originalEnvironment[$name]) {
            Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
        }
        else {
            Set-Item -LiteralPath "Env:$name" -Value $originalEnvironment[$name]
        }
    }
    $passed = 0
    $failed = 0
    $skipped = 0
    foreach ($resultFile in Get-ChildItem $resultsDirectory -Recurse -Filter *.trx -ErrorAction SilentlyContinue) {
        [xml]$result = Get-Content -LiteralPath $resultFile.FullName
        $counters = $result.TestRun.ResultSummary.Counters
        $passed += [int]$counters.passed
        $failed += [int]$counters.failed
        $skipped += [int]$counters.notExecuted
    }
    $coverageLines = @{}
    foreach ($coverageFile in Get-ChildItem $resultsDirectory -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue) {
        [xml]$coverage = Get-Content -LiteralPath $coverageFile.FullName
        foreach ($class in $coverage.coverage.packages.package.classes.class) {
            foreach ($line in $class.lines.line) {
                $key = "$($class.filename):$($line.number)"
                $hits = [int]$line.hits
                if (-not $coverageLines.ContainsKey($key) -or $hits -gt $coverageLines[$key]) {
                    $coverageLines[$key] = $hits
                }
            }
        }
    }
    $linesValid = $coverageLines.Count
    $linesCovered = @($coverageLines.Values | Where-Object { $_ -gt 0 }).Count
    $summary = [ordered]@{
        runId = $runId
        startedAt = $startedAt
        completedAt = [DateTimeOffset]::UtcNow
        configuration = 'Release'
        repeat = $Repeat
        succeeded = ($exitCode -eq 0)
        cleanupSucceeded = (-not $environmentCreated)
        passed = $passed
        failed = $failed
        skipped = $skipped
        total = $passed + $failed + $skipped
        lineCoveragePercent = if ($linesValid -gt 0) { [Math]::Round(100 * $linesCovered / $linesValid, 2) } else { $null }
        testDatabase = if ($KeepDatabase) { $testDatabase } else { '[removed]' }
        resultsDirectory = $resultsDirectory
    }
    $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $resultsDirectory 'release-summary.json') -Encoding UTF8
}

exit $exitCode
