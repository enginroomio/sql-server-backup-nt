#Requires -Version 5.1
<#
  Free-tier friendly Windows smoke:
  - Ensure SQL LocalDB is up (Windows auth — no passwords / no GitHub secrets)
  - Create a tiny user database
  - Publish framework-dependent win-x64 build (faster than self-contained)
  - Apply basit-ayarlar + run --backup
  - Assert a .zip appeared under the backup folder
#>
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $Root

function Find-SqlLocalDb {
    $candidates = @(
        "${env:ProgramFiles}\Microsoft SQL Server\170\Tools\Binn\SqlLocalDB.exe",
        "${env:ProgramFiles}\Microsoft SQL Server\160\Tools\Binn\SqlLocalDB.exe",
        "${env:ProgramFiles}\Microsoft SQL Server\150\Tools\Binn\SqlLocalDB.exe",
        "${env:ProgramFiles}\Microsoft SQL Server\140\Tools\Binn\SqlLocalDB.exe",
        "${env:ProgramFiles(x86)}\Microsoft SQL Server\170\Tools\Binn\SqlLocalDB.exe",
        "${env:ProgramFiles(x86)}\Microsoft SQL Server\160\Tools\Binn\SqlLocalDB.exe",
        "${env:ProgramFiles(x86)}\Microsoft SQL Server\150\Tools\Binn\SqlLocalDB.exe"
    )
    foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { return $c } }
    $cmd = Get-Command SqlLocalDB.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($root in @("${env:ProgramFiles}\Microsoft SQL Server", "${env:ProgramFiles(x86)}\Microsoft SQL Server")) {
        if (-not (Test-Path $root)) { continue }
        $hit = Get-ChildItem -Path $root -Filter SqlLocalDB.exe -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
        if ($hit) { return $hit }
    }
    return $null
}

function Find-SqlCmd {
    $cmd = Get-Command sqlcmd.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $roots = @(
        "${env:ProgramFiles}\Microsoft SQL Server",
        "${env:ProgramFiles(x86)}\Microsoft SQL Server"
    )
    foreach ($r in $roots) {
        if (-not (Test-Path $r)) { continue }
        $hit = Get-ChildItem -Path $r -Filter sqlcmd.exe -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
        if ($hit) { return $hit }
    }
    return $null
}

Write-Host "==> Package validation (Python)" -ForegroundColor Cyan
$env:PYTHONIOENCODING = 'utf-8'
$env:PYTHONUTF8 = '1'
python tests\validate_package.py
if ($LASTEXITCODE -ne 0) { throw "validate_package.py failed" }

Write-Host "==> Locate / start LocalDB" -ForegroundColor Cyan
$localdb = Find-SqlLocalDb
if (-not $localdb) {
    Write-Host "SqlLocalDB not found — installing via Chocolatey (sql-server-express localdb feature may be heavy; trying sqllocaldb package)..." -ForegroundColor Yellow
    choco install sqllocaldb -y --no-progress
    $localdb = Find-SqlLocalDb
}
if (-not $localdb) { throw "SqlLocalDB.exe still not found after install attempt." }

& $localdb info MSSQLLocalDB 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    & $localdb create MSSQLLocalDB -s | Out-Null
}
& $localdb start MSSQLLocalDB | Out-Null
Write-Host "LocalDB ready via $localdb"

$sqlcmd = Find-SqlCmd
if (-not $sqlcmd) { throw "sqlcmd.exe not found (needed to create CI test DB)." }

$instance = '(localdb)\MSSQLLocalDB'
Write-Host "==> Create CI database on $instance" -ForegroundColor Cyan
& $sqlcmd -S $instance -E -Q "IF DB_ID(N'CiBackupTest') IS NULL CREATE DATABASE [CiBackupTest];" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "CREATE DATABASE failed" }

Write-Host "==> dotnet publish (framework-dependent, not self-contained — saves CI minutes)" -ForegroundColor Cyan
Push-Location (Join-Path $Root 'src')
try {
    dotnet restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }
    $out = Join-Path $Root 'artifacts\ci-publish'
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    dotnet publish -c Release -r win-x64 --self-contained false -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
} finally { Pop-Location }

$exe = Join-Path $Root 'artifacts\ci-publish\SQLYedeklemeAraci.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Published exe missing: $exe" }

$backupRoot = Join-Path $env:RUNNER_TEMP 'SQLBackupCi'
if (-not $backupRoot) { $backupRoot = Join-Path $env:TEMP 'SQLBackupCi' }
New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
# Staging under ProgramData (app default). LocalDB runs as the current user — no
# NT Service\MSSQL$* account — so user+SYSTEM ACL is enough (see Staging.Ensure).
$staging = Join-Path $env:ProgramData 'SQLBackupTool\Staging'
New-Item -ItemType Directory -Force -Path $staging | Out-Null
Remove-Item -LiteralPath (Join-Path $staging '.acl_ok') -Force -ErrorAction SilentlyContinue

$simple = Join-Path $backupRoot 'basit-ayarlar.ci.json'
# CI: local destination only — no Google Drive / secrets. Production default is GoogleDrive.
@{
    Sunucu       = $instance
    Hedef        = 'Yerel'
    YedekKlasoru = $backupRoot
    SaklamaAdedi = 3
    Saat         = '03:30'
} | ConvertTo-Json | Set-Content -LiteralPath $simple -Encoding UTF8

# Seed config.json so staging path is CI-safe before first backup
$cfgDir = Join-Path $env:LOCALAPPDATA 'SQLBackupTool'
New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
$cfgPath = Join-Path $cfgDir 'config.json'
@{
    InstanceName     = $instance
    DriveFolder      = ''
    Destination      = 1
    LocalFolder      = $backupRoot
    Mode             = 0
    StagingFolder    = $staging
    RetentionDays    = 3
    BackupTime       = '03:30'
    ScheduleEnabled  = $false
    SetupCompleted   = $true
    EncryptEnabled   = $false
    EncryptedPassword = ''
    UseSqlAuth       = $false
    SqlUser          = ''
    SqlPasswordEnc   = ''
    SecretScope      = 'CurrentUser'
    LastResults      = @{}
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $cfgPath -Encoding UTF8

# WinExe (GUI subsystem): PowerShell `&` does NOT wait and may leave a stale
# $LASTEXITCODE. Always Start-Process -Wait -PassThru and read .ExitCode.
function Invoke-WinExe {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $ArgumentList
    )
    $p = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList `
        -Wait -PassThru -WindowStyle Hidden
    if ($null -eq $p) { throw "Start-Process returned null for $FilePath" }
    # ExitCode can be $null if process was not waited on; treat as failure.
    if ($null -eq $p.ExitCode) { throw "No ExitCode from $FilePath (WinExe wait failed)" }
    return [int]$p.ExitCode
}

Write-Host "==> Apply basit settings" -ForegroundColor Cyan
$applyExit = Invoke-WinExe -FilePath $exe -ArgumentList @('--apply-basit', $simple)
if ($applyExit -ne 0) { throw "--apply-basit failed with $applyExit" }

# Re-assert staging (apply may normalize) and ScheduleEnabled false for CI
$cfg = Get-Content -LiteralPath $cfgPath -Raw | ConvertFrom-Json
$cfg.StagingFolder = $staging
$cfg.ScheduleEnabled = $false
$cfg.Destination = 1
$cfg.LocalFolder = $backupRoot
$cfg.InstanceName = $instance
$cfg | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $cfgPath -Encoding UTF8

# Preflight: edition + direct BACKUP without COMPRESSION (surfaces real SQL errors, not only 3013)
Write-Host "==> sqlcmd LocalDB diagnostic" -ForegroundColor Cyan
$diagBak = Join-Path $staging ("CiDiag_" + [guid]::NewGuid().ToString('N').Substring(0, 8) + ".bak")
$diagSql = @"
SET NOCOUNT ON;
SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(128)) AS Edition,
       CAST(SERVERPROPERTY('EngineEdition') AS int) AS EngineEdition,
       SUSER_SNAME() AS LoginName;
BACKUP DATABASE [CiBackupTest] TO DISK = N'$($diagBak.Replace("'","''"))' WITH INIT, CHECKSUM;
"@
& $sqlcmd -S $instance -E -b -Q $diagSql | Out-Host
$diagExit = $LASTEXITCODE
Write-Host "sqlcmd diagnostic exit: $diagExit (bak=$diagBak)"
if (Test-Path -LiteralPath $diagBak) {
    Write-Host "diagnostic bak size: $((Get-Item -LiteralPath $diagBak).Length) bytes"
    Remove-Item -LiteralPath $diagBak -Force -ErrorAction SilentlyContinue
} elseif ($diagExit -ne 0) {
    Write-Host "WARNING: raw LocalDB BACKUP failed before app --backup (see sqlcmd output above)" -ForegroundColor Yellow
}

Write-Host "==> Run headless backup" -ForegroundColor Cyan
$backupExit = Invoke-WinExe -FilePath $exe -ArgumentList @('--backup')
Write-Host "backup exit code: $backupExit"

$zipDir = Join-Path $backupRoot 'SQLBackup'
$zips = @()
if (Test-Path -LiteralPath $zipDir) {
    $zips = @(Get-ChildItem -LiteralPath $zipDir -Filter 'CiBackupTest_*.zip' -File -ErrorAction SilentlyContinue)
}
Write-Host "Zips found: $($zips.Count) in $zipDir"
if ($backupExit -ne 0 -or $zips.Count -lt 1) {
    Write-Host "---- config ----"; Get-Content $cfgPath -ErrorAction SilentlyContinue
    Write-Host "---- staging listing ----"
    if (Test-Path -LiteralPath $staging) { Get-ChildItem -LiteralPath $staging -Force | Format-Table Name, Length, LastWriteTime | Out-String | Write-Host }
    Write-Host "---- logs ----"
    $logDir = Join-Path $cfgDir 'logs'
    if (Test-Path $logDir) { Get-ChildItem $logDir | ForEach-Object { Write-Host $_.FullName; Get-Content $_.FullName -Tail 80 } }
    throw "Smoke backup failed (exit=$backupExit, zips=$($zips.Count))"
}

Write-Host "SMOKE OK: $($zips[0].FullName) ($([math]::Round($zips[0].Length/1KB,1)) KB)" -ForegroundColor Green
exit 0
