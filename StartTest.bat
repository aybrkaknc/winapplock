@echo off
pushd "%~dp0"
:: Yonetici (Admin) izni kontrolu
>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"
if '%errorlevel%' NEQ '0' (
    echo Yonetici izinleri isteniyor... Lutfen onay verin.
    goto UACPrompt
) else ( goto gotAdmin )

:UACPrompt
    echo Set UAC = CreateObject^("Shell.Application"^) > "%temp%\getadmin.vbs"
    echo UAC.ShellExecute "%~s0", "", "", "runas", 1 >> "%temp%\getadmin.vbs"
    "%temp%\getadmin.vbs"
    exit /B

:gotAdmin
    if exist "%temp%\getadmin.vbs" ( del "%temp%\getadmin.vbs" )

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
echo 2. Guncelemeler derleniyor... (Lutfen bekleyin)
echo -------------------------------------------------------------------------
cd /d %~dp0
dotnet build WinAppLock.sln -c Debug
if %errorLevel% neq 0 (
    echo.
    echo [HATA] Derleme sirasinda kirmizi hatalar olustu! 
    echo Lutfen yukarıdaki hata mesajlarini inceleyin ve kodu duzetin.
    pause
    exit /b
)
echo -------------------------------------------------------------------------
echo    Build basarili.

echo.
echo 3. Gatekeeper yukleniyor (C:\ProgramData\WinAppLock)...
if not exist "C:\ProgramData\WinAppLock" mkdir "C:\ProgramData\WinAppLock"
xcopy "src\WinAppLock.Gatekeeper\bin\Debug\net8.0-windows\*.*" "C:\ProgramData\WinAppLock\" /Y /Q >nul 2>&1
echo    Gatekeeper guncellendi: WinAppLock.Gatekeeper.exe

echo.
echo 4. WinAppLock Servisleri baslatiliyor...
echo [INFO] Service ve UI loglari asagida birlesecektir.
echo -------------------------------------------------------------------------

:: Service'i arkaplanda baslat
start /B "" dotnet run --no-build --project src\WinAppLock.Service

:: Service'in pipe server'i hazirlamasi icin kisa bir es
timeout /t 2 > nul

:: UI'i arkaplanda baslat
start /B "" dotnet run --no-build --project src\WinAppLock.UI

echo.
echo ======= SISTEM AKTIF VE DINLEMEDE =======
echo Tum log akisi bu pencereye yonlendirildi. 
echo Sistemi durdurmak icin pencereyi kapatmaniz yeterlidir.
echo.
pause
