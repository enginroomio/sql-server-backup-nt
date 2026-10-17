# =====================================================================
#  SQL Yedekleme Aracı - Windows Server tak-çalıştır kurulum
#  Varsayılanlar: yerel C:\SQLBackup, saklama 7, saat 02:00
#  Google Drive ZORUNLU DEĞİL. İsteğe bağlı: -BackupFolder, -Instance, -Time, -Retention
# =====================================================================
param(
    [string]$Instance = 'SQLEXPRESS',
    [string]$BackupFolder = 'C:\SQLBackup',
    [string]$Time = '02:00',
    [int]$Retention = 7,
    [string]$SimpleSettings = '',
    [switch]$ForceBuild,
    [switch]$SkipLaunch
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

# 1) Standart kurulum (staging ACL, exe kopya, görev iskeleti)
$setupArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Root 'setup.ps1'), '-Instance', $Instance)
if ($ForceBuild) { $setupArgs += '-ForceBuild' }
# setup.ps1 launches the GUI; we re-apply settings after. Patch: run setup then apply.
# Temporarily prevent double-launch noise by letting setup launch, then apply basit.
& powershell @setupArgs
if ($LASTEXITCODE -ne 0) { throw "setup.ps1 basarisiz (kod $LASTEXITCODE)" }

# 2) Basit ayarlar
$simple = if ($SimpleSettings -and (Test-Path -LiteralPath $SimpleSettings)) { $SimpleSettings } else { Join-Path $Root 'basit-ayarlar.json' }
if (-not (Test-Path -LiteralPath $simple)) {
    @{
        Sunucu       = if ($Instance -eq 'MSSQLSERVER') { '(local)' } else { ".\$Instance" }
        YedekKlasoru = $BackupFolder
        SaklamaAdedi = $Retention
        Saat         = $Time
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Root 'basit-ayarlar.json') -Encoding UTF8
    $simple = Join-Path $Root 'basit-ayarlar.json'
} else {
    # Override JSON with CLI params when provided (explicit Server path wins defaults)
    try {
        $j = Get-Content -LiteralPath $simple -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($PSBoundParameters.ContainsKey('BackupFolder')) { $j.YedekKlasoru = $BackupFolder }
        if ($PSBoundParameters.ContainsKey('Time')) { $j.Saat = $Time }
        if ($PSBoundParameters.ContainsKey('Retention')) { $j.SaklamaAdedi = $Retention }
        if ($Instance -eq 'MSSQLSERVER') { $j.Sunucu = '(local)' }
        elseif ($PSBoundParameters.ContainsKey('Instance') -or -not $j.Sunucu) { $j.Sunucu = ".\$Instance" }
        $j | ConvertTo-Json | Set-Content -LiteralPath $simple -Encoding UTF8
    } catch { Write-Host "[UYARI] basit-ayarlar.json guncellenemedi: $($_.Exception.Message)" -ForegroundColor Yellow }
}

New-Item -ItemType Directory -Force -Path $BackupFolder | Out-Null
$exe = Join-Path $env:LOCALAPPDATA 'Programs\SQLBackupTool\SQLYedeklemeAraci.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Kurulu exe bulunamadi: $exe" }

Write-Host "==> Basit ayarlar uygulanıyor: $simple" -ForegroundColor Cyan
& $exe --apply-basit $simple
$code = $LASTEXITCODE
if ($code -ne 0) { throw "basit ayar uygulanamadi (kod $code). GUI ile Ayarlar sekmesinden de kaydedebilirsiniz." }

Write-Host ""
Write-Host "Windows Server kurulumu tamamlandi." -ForegroundColor Green
Write-Host "  Sunucu/ornek : bakiniz basit-ayarlar.json"
Write-Host "  Yedek klasoru: $BackupFolder\SQLBackup"
Write-Host "  Saat / saklama: $Time / $Retention"
Write-Host "  Sonraki adim : Uygulamada 'Test Yedegi Al' veya: `"$exe`" --backup"
Write-Host "  Oturum kapali sunucu icin: uygulamada Sunucu Modu = 'Oturum acilmadan' veya SYSTEM."
if (-not $SkipLaunch) { Start-Process $exe }
