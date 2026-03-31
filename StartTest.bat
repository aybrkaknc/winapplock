@echo off
:: Yonetici (Admin) izni kontrolu
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Yonetici izinleri isteniyor... Lutfen onay verin.
    powershell -Command "Start-Process '%~dpnx0' -Verb RunAs"
    exit /b
)

echo ==============================================
echo WinAppLock: Restart ^& Run (Admin)
echo ==============================================
echo.

echo 1. Eski oturumlar ve IFEO kayitlari temizleniyor...
taskkill /F /IM WinAppLock.Service.exe /T >nul 2>&1
taskkill /F /IM WinAppLock.UI.exe /T >nul 2>&1
taskkill /F /IM WinAppLock.Gatekeeper.exe /T >nul 2>&1
dotnet build-server shutdown >nul 2>&1

:: IFEO Kayit Temizligi (WinAppLock'a ait olanlar)
for /f "tokens=*" %%k in ('reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options" 2^>nul ^| findstr /i "HKLM"') do (
    reg query "%%k" /v Debugger 2>nul | findstr /i "WinAppLock.Gatekeeper" >nul 2>&1
    if not errorlevel 1 (
        reg delete "%%k" /v Debugger /f >nul 2>&1
        echo    Temizlendi: %%k
    )
)
echo    Temizlik tamamlandi.

echo.
echo 2. Guncelemeler derleniyor...
cd /d %~dp0
dotnet build WinAppLock.sln -c Debug >nul 2>&1
if %errorLevel% neq 0 (
    echo [HATA] Derleme basarisiz! Detaylar icin "dotnet build" komutunu elle calistirin.
    pause
    exit /b
)
echo    Build basarili.

echo.
echo 3. Gatekeeper deploy ediliyor...
if not exist "C:\ProgramData\WinAppLock" mkdir "C:\ProgramData\WinAppLock"
xcopy "src\WinAppLock.Gatekeeper\bin\Debug\net8.0-windows\*.*" "C:\ProgramData\WinAppLock\" /Y /Q >nul 2>&1
echo    Gatekeeper guncellendi: C:\ProgramData\WinAppLock\WinAppLock.Gatekeeper.exe

echo.
echo 4. Service baslatiliyor...
start "WinAppLock Service (ADMIN)" cmd /c "color 04 && title SERVICE && cd /d %~dp0 && dotnet run --no-build --project src\WinAppLock.Service || pause"

echo.
echo 5. Service'in lk hazirliklari icin bekleniyor...
timeout /t 3 > nul

echo.
echo 6. UI baslatiliyor...
start "WinAppLock UI (ADMIN)" cmd /c "color 02 && title UI && cd /d %~dp0 && dotnet run --project src\WinAppLock.UI || pause"

echo.
echo ======= SISTEM AYAKTA =======
echo Her iki bilesen de Yonetici yetkileriyle yeniden baslatildi.
echo.
echo Test bittiginde StopTest.bat ile tamamen temizlik yapabilirsiniz.
echo Bu pencere 5 saniye icinde kapanacaktir.
timeout /t 5 > nul
