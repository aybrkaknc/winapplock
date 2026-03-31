@echo off
:: Yonetici (Admin) izni kontrolu
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Yonetici izinleri isteniyor... Lutfen onay verin.
    powershell -Command "Start-Process '%~dpnx0' -Verb RunAs"
    exit /b
)

echo ==============================================
echo WinAppLock: IFEO Gatekeeper Test Ortami
echo ==============================================
echo.

echo 1. Arka plandaki eski veya takili kalan islemler temizleniyor...
taskkill /F /IM WinAppLock.Service.exe /T >nul 2>&1
taskkill /F /IM WinAppLock.UI.exe /T >nul 2>&1
taskkill /F /IM WinAppLock.Gatekeeper.exe /T >nul 2>&1
dotnet build-server shutdown >nul 2>&1

echo.
echo 2. Build ediliyor...
cd /d %~dp0
dotnet build WinAppLock.sln -c Debug >nul 2>&1
if %errorLevel% neq 0 (
    echo [HATA] Build basarisiz! Detaylar icin "dotnet build" komutunu elle calistirin.
    pause
    exit /b
)
echo    Build basarili.

echo.
echo 3. Gatekeeper deploy ediliyor...
if not exist "C:\ProgramData\WinAppLock" mkdir "C:\ProgramData\WinAppLock"
:: Gatekeeper + tum bagimliliklarini kopyala
xcopy "src\WinAppLock.Gatekeeper\bin\Debug\net8.0-windows\*.*" "C:\ProgramData\WinAppLock\" /Y /Q >nul 2>&1
echo    Gatekeeper deploy konumu: C:\ProgramData\WinAppLock\WinAppLock.Gatekeeper.exe

echo.
echo 4. Service penceresi baslatiliyor...
start "WinAppLock Service (ADMIN)" cmd /c "color 04 && cd /d %~dp0 && dotnet run --no-build --project src\WinAppLock.Service || pause"

echo.
echo 5. Service'in IFEO kayitlarini yazabilmesi icin bekleniyor...
timeout /t 4 > nul

echo.
echo 6. UI (Kullanici Arayuzu) baslatiliyor...
start "WinAppLock UI (ADMIN)" cmd /c "color 02 && cd /d %~dp0 && dotnet run --project src\WinAppLock.UI || pause"

echo.
echo ======= TEST ORTAMI HAZIR =======
echo Iki uygulama da Yonetici yetkileriyle calisyor.
echo.
echo IFEO Test Adimlari:
echo   1. UI'dan bir uygulama kilitle (ornegin notepad.exe)
echo   2. Kilitli uygulamayi calistirmayi dene
echo   3. Gatekeeper araya girecek ve sifre ekrani gosterilecek
echo   4. Service (kirmizi) penceresinde IFEO loglarini izle
echo.
echo DIKKAT: Test sonunda StopTest.bat calistirmayi unutma!
echo (IFEO kayitlari temizlenmezse uygulamalar acilmayabilir)
timeout /t 5 > nul
