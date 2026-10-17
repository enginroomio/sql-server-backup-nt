# Basit 4 ayarı config.json'a yazar (GUI gerekmez / Server Core).
# Kullanım: powershell -ExecutionPolicy Bypass -File Uygula-Ayarlar.ps1
# Opsiyonel: -SettingsPath .\basit-ayarlar.json
param([string]$SettingsPath = '')
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $SettingsPath) { $SettingsPath = Join-Path $Root 'basit-ayarlar.json' }
if (-not (Test-Path -LiteralPath $SettingsPath)) { throw "Ayar dosyasi yok: $SettingsPath" }

$exe = Join-Path $env:LOCALAPPDATA 'Programs\SQLBackupTool\SQLYedeklemeAraci.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    $exe = Join-Path $Root 'dist\SQLYedeklemeAraci.exe'
}
if (-not (Test-Path -LiteralPath $exe)) { throw "SQLYedeklemeAraci.exe bulunamadi. Once Kurulum-Sunucu.bat veya setup.bat calistirin." }

Write-Host "Uygulaniyor: $SettingsPath -> $exe --apply-basit"
& $exe --apply-basit $SettingsPath
exit $LASTEXITCODE
