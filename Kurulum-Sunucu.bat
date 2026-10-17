@echo off
chcp 65001 >nul
title SQL Yedekleme Araci - Windows Server Kurulumu
echo ==============================================
echo   SQL Yedekleme Araci - Windows Server
echo   Varsayilan: yerel klasor C:\SQLBackup, saat 02:00, saklama 7
echo   Yonetici onayi (UAC) bir kez istenebilir: "Evet" deyin.
echo ==============================================
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-server.ps1" %*
if errorlevel 1 (
  echo.
  echo [HATA] Kurulum basarisiz oldu. Yukaridaki mesajlari kontrol edin.
) else (
  echo.
  echo Sunucu kurulumu tamamlandi.
)
pause
