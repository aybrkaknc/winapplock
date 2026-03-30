using System.Windows;
using WinAppLock.Core.Data;
using WinAppLock.Core.IPC;
using WinAppLock.UI.Services;
using WinAppLock.UI.Views;

namespace WinAppLock.UI;

/// <summary>
/// Uygulama giriş noktası.
/// Tek instance kontrolü, ilk kurulum yönlendirmesi ve
/// global servislerin (Tray, Pipe, Hotkey) başlatılması.
/// </summary>
public partial class App : Application
{
    private static Mutex? _mutex;
    private TrayIconService? _trayService;
    private PipeClient? _pipeClient;
    private GlobalHotkeyService? _hotkeyService;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Tek instance kontrolü — aynı anda birden fazla WinAppLock UI açılmasını engeller
        const string MUTEX_NAME = "WinAppLock_UI_SingleInstance";
        _mutex = new Mutex(true, MUTEX_NAME, out bool isNewInstance);

        if (!isNewInstance)
        {
            MessageBox.Show("WinAppLock zaten çalışıyor!", "WinAppLock",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Current.Shutdown();
            return;
        }

        // Global exception handler
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Beklenmeyen bir hata oluştu:\n{args.Exception.Message}",
                "WinAppLock Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);

        // Ayarları yükle ve dili ayarla
        var database = new AppDatabase();
        var settings = database.GetSettings();

        // Kayıtlı olan dili arayüze uygula
        LocalizationManager.LoadLanguageFromSettings();

        // İlk kurulum yapılmış mı kontrol et
        if (!settings.SetupCompleted)
        {
            // İlk kurulum sihirbazını göster
            var wizard = new SetupWizard();
            wizard.Show();
        }
        else
        {
            // Dashboard'u aç ve servisleri başlat
            var mainWindow = new MainWindow();
            mainWindow.Show();
            InitializeServices(mainWindow);
        }
    }

    /// <summary>
    /// Tray ikonu, Pipe istemcisi ve Global hotkey servislerini başlatır.
    /// </summary>
    /// <param name="mainWindow">Servislerin bağlanacağı ana pencere</param>
    public void InitializeServices(MainWindow mainWindow)
    {
        // ─── System Tray ───
        _trayService = new TrayIconService();
        _trayService.Initialize();
        _trayService.ShowRequested += () =>
        {
            mainWindow.Show();
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
        };
        _trayService.ExitRequested += () =>
        {
            _trayService.Dispose();
            Current.Shutdown();
        };
        _trayService.LockAllRequested += () =>
        {
            _pipeClient?.SendLockAll();
        };

        // ─── Named Pipe İstemcisi ───
        _pipeClient = new PipeClient();
        _pipeClient.StartListening();
        _pipeClient.MessageReceived += OnServiceMessage;

        // ─── Global Hotkey (Ctrl+Alt+L) ───
        _hotkeyService = new GlobalHotkeyService();
        mainWindow.Loaded += (_, _) =>
        {
            _hotkeyService.Register(mainWindow);
        };
        _hotkeyService.HotkeyPressed += () =>
        {
            _pipeClient?.SendLockAll();
            _trayService?.ShowBalloon("WinAppLock", "Tüm uygulamalar kilitlendi! 🔐");
        };
    }

    /// <summary>
    /// Service'ten gelen mesajları işler (LockTriggered → LockOverlay göster).
    /// </summary>
    private void OnServiceMessage(PipeMessage message)
    {
        Dispatcher.Invoke(() =>
        {
            switch (message.Type)
            {
                case PipeMessageType.LockTriggered:
                    ShowLockOverlay(message);
                    break;

                case PipeMessageType.AllLocked:
                    _trayService?.ShowBalloon("WinAppLock", "Tüm uygulamalar kilitlendi.");
                    break;
            }
        });
    }

    /// <summary>
    /// Kilitli uygulama algılandığında LockOverlay'i gösterir.
    /// </summary>
    private void ShowLockOverlay(PipeMessage message)
    {
        var database = new AppDatabase();

        // Uygulama ikonunu ve özel şifre hash'ini bul
        string? iconBase64 = null;
        string? customPasswordHash = null;

        var apps = database.GetAllLockedApps();
        var matchedApp = apps.FirstOrDefault(a =>
            a.Identity.ExecutableName.Equals(
                message.AppName, StringComparison.OrdinalIgnoreCase));

        if (matchedApp != null)
        {
            iconBase64 = matchedApp.IconBase64;
            customPasswordHash = matchedApp.CustomPasswordHash;
        }

        var overlay = new LockOverlay();

        overlay.AuthSuccess += processId =>
        {
            _pipeClient?.SendAuthSuccess(processId, message.AppName);
        };

        overlay.ShowForProcess(
            message.ProcessId,
            message.AppName ?? "Bilinmeyen Uygulama",
            iconBase64,
            customPasswordHash);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _pipeClient?.Dispose();
        _trayService?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
