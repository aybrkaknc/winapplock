@echo off
:: Yonetici (Admin) izni kontrolu
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Yonetici izinleri isteniyor... Lutfen onay verin.
    powershell -Command "Start-Process '%~dpnx0' -Verb RunAs"
    exit /b
)

echo ==============================================
echo WinAppLock Test Ortami Kapatiliyor
echo ==============================================
echo.

echo 1. Arka plandaki Service, UI ve Gatekeeper islemleri sonlandiriliyor...
taskkill /F /IM WinAppLock.Service.exe /T 2>nul
taskkill /F /IM WinAppLock.UI.exe /T 2>nul
taskkill /F /IM WinAppLock.Gatekeeper.exe /T 2>nul
dotnet build-server shutdown >nul 2>&1

echo.
echo 2. IFEO kayitlari temizleniyor (WinAppLock'a ait olanlar)...
:: Registry'deki tum WinAppLock IFEO Debugger kayitlarini temizle
for /f "tokens=*" %%k in ('reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options" 2^>nul ^| findstr /i "HKLM"') do (
    reg query "%%k" /v Debugger 2>nul | findstr /i "WinAppLock.Gatekeeper" >nul 2>&1
    if not errorlevel 1 (
        reg delete "%%k" /v Debugger /f >nul 2>&1
        echo    Temizlendi: %%k
    )
)
echo    IFEO temizligi tamamlandi.

echo.
echo ======= ISLEM TAMAMLANDI =======
echo Tum WinAppLock islemleri ve IFEO kayitlari basariyla temizlendi.
echo Kilitli uygulamalar artik normal sekilde acilacaktir.
timeout /t 3 > nul
