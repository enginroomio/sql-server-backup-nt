# =====================================================================
#  SQL Yedekleme Aracı - Otomatik Kurulum (tekrar çalıştırmak güvenlidir)
#  Kullanım: setup.bat dosyasına çift tıklayın (veya: powershell -ExecutionPolicy Bypass -File setup.ps1)
# =====================================================================
param([switch]$AclOnly, [string]$Instance = 'SQLEXPRESS', [switch]$ForceBuild, [string]$ForUser = '')
$ErrorActionPreference = 'Stop'
$Root      = Split-Path -Parent $MyInvocation.MyCommand.Path
$Staging   = Join-Path $env:ProgramData 'SQLBackupTool\Staging'
$InstallDir= Join-Path $env:LOCALAPPDATA 'Programs\SQLBackupTool'
$ExeName   = 'SQLYedeklemeAraci.exe'
$TaskName  = 'SQLBackupTool_Gunluk'

function Bilgi($m){ Write-Host "    $m" -ForegroundColor Gray }
function Adim($n,$m){ Write-Host ""; Write-Host "==> Adim $n/5: $m" -ForegroundColor Cyan }
function Uyari($m){ Write-Host "[UYARI]   $m" -ForegroundColor Yellow }

# ---- Yönetici olarak çalışan alt adım: staging klasörü + SQL hizmet hesabı izni ----
if ($AclOnly) {
    New-Item -ItemType Directory -Force -Path $Staging | Out-Null
    $svc = if ($Instance -eq 'MSSQLSERVER') { 'NT Service\MSSQLSERVER' } else { "NT Service\MSSQL`$$Instance" }
    # Yalnizca: kurulumu yapan kullanici + SYSTEM + SQL Server hizmeti (BACKUP TO DISK icin zorunlu)
    if (-not $ForUser) { $ForUser = "$env:USERDOMAIN\$env:USERNAME" }
    & icacls $Staging /setowner "$ForUser" /T | Out-Null
    & icacls $Staging /inheritance:r /grant:r "${ForUser}:(OI)(CI)F" /grant:r "*S-1-5-18:(OI)(CI)F" /grant:r "${svc}:(OI)(CI)M" | Out-Null
    if ($LASTEXITCODE -ne 0) { exit 1 }
    Set-Content -Path (Join-Path $Staging '.acl_ok') -Value (Get-Date -Format s)
    # SYSTEM modu icin ayar kopyasi klasoru (kullanici + SYSTEM + Administrators)
    $svcDir = Join-Path $env:ProgramData 'SQLBackupTool\Service'
    New-Item -ItemType Directory -Force -Path $svcDir | Out-Null
    & icacls $svcDir /inheritance:r /grant:r "${ForUser}:(OI)(CI)M" /grant:r "*S-1-5-18:(OI)(CI)F" /grant:r "*S-1-5-32-544:(OI)(CI)F" | Out-Null
    # Windows Olay Gunlugu kaynagi (Application)
    try {
        if (-not [System.Diagnostics.EventLog]::SourceExists('SQLYedeklemeAraci')) {
            [System.Diagnostics.EventLog]::CreateEventSource('SQLYedeklemeAraci', 'Application')
            Write-Host "Olay Gunlugu kaynagi olusturuldu: SQLYedeklemeAraci"
        }
    } catch { Write-Host "Olay Gunlugu kaynagi olusturulamadi: $($_.Exception.Message)" }
    Write-Host "Staging izinleri ayarlandi: $Staging ($ForUser + SYSTEM + $svc)"
    exit 0
}

Write-Host "SQL Yedekleme Araci kurulumu basliyor. Lutfen pencereyi kapatmayin." -ForegroundColor Green
# Windows Server tespiti
$os = $null; try { $os = Get-CimInstance Win32_OperatingSystem } catch {}
$isServer = $os -and (($os.ProductType -ne 1) -or ($os.Caption -match 'Server'))
if ($isServer) {
    $ver = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction SilentlyContinue)
    Write-Host ""
    Write-Host "  [SUNUCU] Windows Server algilandi: $($os.Caption)" -ForegroundColor Magenta
    Write-Host "           Sunucuda Google Drive yerine yerel/ag klasoru kullanilabilir; uygulama icinden secebilirsiniz." -ForegroundColor Magenta
    Write-Host "           Oturum acilmadan / SYSTEM ile calisma modlari uygulamada 'Sunucu Modu' altinda (yonetici onayiyla)." -ForegroundColor Magenta
    if ($ver.InstallationType -eq 'Server Core') {
        Write-Host "           Server Core: arayuz (GUI) icin Desktop Experience gerekir. Kurulum yine calisir;" -ForegroundColor Yellow
        Write-Host "           yedekleme zamanlanmis gorevden '--backup' ile arayuzsuz calistirilabilir." -ForegroundColor Yellow
    }
}
Adim 1 "Guvenli gecici klasor ve Olay Gunlugu kaynagi hazirlaniyor..."

# 1) Staging klasörü izinleri (tek seferlik yönetici onayı istenir)
$needAcl = $true
$evOk = $false; try { $evOk = [System.Diagnostics.EventLog]::SourceExists('SQLYedeklemeAraci') } catch {}
if ((Test-Path $Staging) -and (Test-Path (Join-Path $Staging '.acl_ok')) -and $evOk) {
    $acl = (Get-Acl $Staging).Access | ForEach-Object { $_.IdentityReference.Value }
    if ($acl -match [regex]::Escape("MSSQL`$$Instance")) { $needAcl = $false }
}
if ($needAcl) {
    Bilgi "SQL Server hizmet hesabına staging klasörü yazma izni veriliyor (UAC onayı istenecek)..."
    try {
        $p = Start-Process powershell -Verb RunAs -Wait -PassThru -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -AclOnly -Instance $Instance -ForUser `"$env:USERDOMAIN\$env:USERNAME`""
        if ($p.ExitCode -ne 0) { Uyari "İzin adımı başarısız (kod $($p.ExitCode)). KURULUM_AYARLAR.md > GEREKLİ İZİNLER bölümüne bakın." }
    } catch { Uyari "Yönetici onayı verilmedi. Staging izinlerini daha sonra elle ayarlayın (KURULUM_AYARLAR.md)." }
} else { Bilgi "Staging izinleri zaten ayarlı." }

Adim 2 ".NET ve uygulama kontrol ediliyor..."
# 2) Uygulamayı hazırla: hazır exe varsa kullan, yoksa .NET 8 SDK ile derle
$exeSrc = Join-Path $Root "dist\$ExeName"
if ($ForceBuild -or -not (Test-Path $exeSrc)) {
    $hasSdk = $false
    try { $hasSdk = (& dotnet --list-sdks 2>$null) -match '^8\.' } catch {}
    if (-not $hasSdk) {
        Bilgi ".NET 8 SDK bulunamadı, winget ile kuruluyor..."
        winget install --id Microsoft.DotNet.SDK.8 -e --silent --accept-package-agreements --accept-source-agreements
        $env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
    }
    Bilgi "NuGet paketleri geri yükleniyor ve uygulama derleniyor (birkaç dakika sürebilir)..."
    Push-Location (Join-Path $Root 'src')
    try {
        dotnet restore
        dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $Root 'dist')
        if ($LASTEXITCODE -ne 0) { throw "Derleme başarısız (kod $LASTEXITCODE)." }
    } finally { Pop-Location }
} else { Bilgi "Hazır derlenmiş uygulama bulundu: $exeSrc" }

Adim 3 "Uygulama yukleniyor ve masaustu kisayolu olusturuluyor..."
# 3) Kopyala (çalışan örneği kapat)
Get-Process -Name 'SQLYedeklemeAraci' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item $exeSrc (Join-Path $InstallDir $ExeName) -Force
Copy-Item (Join-Path $Root 'backup.master.sql') $InstallDir -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $Root 'KURULUM_AYARLAR.md') $InstallDir -Force -ErrorAction SilentlyContinue
$Exe = Join-Path $InstallDir $ExeName
Bilgi "Uygulama kopyalandı: $Exe  (ayarlar %LOCALAPPDATA%\SQLBackupTool\config.json içinde korunur)"

# 4) Masaüstü kısayolu
$lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'SQL Yedekleme Aracı.lnk'
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut($lnk); $sc.TargetPath = $Exe; $sc.WorkingDirectory = $InstallDir
$sc.Description = 'SQL Server veritabanlarını Google Drive''a yedekler'; $sc.Save()
Bilgi "Masaüstü kısayolu oluşturuldu."

Adim 4 "Gunluk otomatik yedekleme (02:00) kaydediliyor..."
# 5) Zamanlanmış görev (mevcut kullanıcı, yönetici gerekmez). Ayarlardaki saat varsa onu kullan.
$time = '02:00'
$cfgFile = Join-Path $env:LOCALAPPDATA 'SQLBackupTool\config.json'
if (Test-Path $cfgFile) {
    try { $c = Get-Content $cfgFile -Raw | ConvertFrom-Json; if ($c.BackupTime) { $time = $c.BackupTime }
          if ($c.ScheduleEnabled -eq $false) { $time = $null } } catch {}
}
if ($time) {
    & schtasks.exe /Create /TN $TaskName /TR "`"$Exe`" --backup" /SC DAILY /ST $time /F | Out-Null
    if ($LASTEXITCODE -eq 0) { Bilgi "Zamanlanmış görev kaydedildi: her gün $time ($TaskName)" } else { Uyari "Zamanlanmış görev kaydedilemedi; uygulamadan 'Zamanlanmış Görevi Kaydet' ile tekrar deneyin." }
} else { Bilgi "Zamanlama ayarlarda kapalı; görev oluşturulmadı." }

Adim 5 "Google Drive for Desktop kontrol ediliyor..."
$driveCands = @("$env:USERPROFILE\My Drive", "$env:USERPROFILE\Google Drive", "$env:USERPROFILE\Drive'ım")
foreach ($l in [char[]]'DEFGHIJKLMNOPQRSTUVWXYZ') { $driveCands += "${l}:\My Drive"; $driveCands += "${l}:\Drive'ım" }
$driveFound = $driveCands | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ($driveFound) { Bilgi "Google Drive klasoru bulundu: $driveFound" }
else {
    Write-Host ""
    Write-Host "  [UYARI] Google Drive for Desktop bulunamadi." -ForegroundColor Yellow
    Write-Host "          Ucretsiz indirin: https://www.google.com/drive/download/" -ForegroundColor Yellow
    Write-Host "          Kurup KISISEL hesabinizla giris yapin; sonra uygulamada klasoru secin." -ForegroundColor Yellow
    Write-Host "          (Kurulum devam ediyor.)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Kurulum tamamlandi ✓ — uygulama aciliyor" -ForegroundColor Green
Start-Process $Exe
