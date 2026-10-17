@echo off
chcp 65001 >nul
title SQL Yedekleme Araci - Kurulum
echo ==============================================
echo   SQL Yedekleme Araci - Kolay Kurulum
echo   Yonetici onayi (UAC) bir kez istenebilir: "Evet" deyin.
echo ==============================================
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" %*
if errorlevel 1 (
  echo.
  echo [HATA] Kurulum basarisiz oldu. Yukaridaki mesajlari kontrol edin.
) else (
  echo.
  echo Kurulum tamamlandi.
)
pause
