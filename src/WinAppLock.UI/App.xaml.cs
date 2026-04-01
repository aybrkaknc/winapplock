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
    private WindowObserverService? _windowObserver;
    private GlobalHotkeyService? _hotkeyService;

    public bool IsExiting { get; set; } = false;

    /// <summary>
    /// ResourceDictionary'den lokalize metin çeker.
    /// </summary>
    private static string L(string key, string fallback = "")
    {
        return Application.Current.TryFindResource(key)?.ToString() ?? fallback;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // UI'ın şifre ekranını gecikmesiz açması için işlemci önceliğini yükseltiyoruz
        try { System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.High; } catch { }

        // Tek instance kontrolü — aynı anda birden fazla WinAppLock UI açılmasını engeller
        const string MUTEX_NAME = "WinAppLock_UI_SingleInstance";
        _mutex = new Mutex(true, MUTEX_NAME, out bool isNewInstance);

        if (!isNewInstance)
        {
            MessageBox.Show(L("Str_AlreadyRunning", "WinAppLock zaten çalışıyor!"), "WinAppLock",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Current.Shutdown();
            return;
        }

        // Global exception handler
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"{L("Str_UnexpectedError", "Beklenmeyen bir hata oluştu:")}\n{args.Exception.Message}",
                "WinAppLock", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);

        bool isHidden = e.Args.Contains("--hidden");

        // Service.exe kontrolü
        EnsureServiceIsRunning();

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
            if (!isHidden)
            {
                mainWindow.Show();
            }
            InitializeServices(mainWindow);
        }
    }

    /// <summary>
    /// Servisin arka planda çalışıp çalışmadığını denetler, kapalıysa başlatır.
    /// </summary>
    private void EnsureServiceIsRunning()
    {
        try
        {
            // Zaten çalışıyorsa geç
            var processes = System.Diagnostics.Process.GetProcessesByName("WinAppLock.Service");
            if (processes.Length > 0) return;

            // Çalışmıyorsa, EXE'yi bul ve başlat
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            var servicePath = System.IO.Path.Combine(basePath, "WinAppLock.Service.exe");

            if (System.IO.File.Exists(servicePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = servicePath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Service başlatılamadı: {ex.Message}");
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
            IsExiting = true;
            _trayService.Dispose();
            Current.Shutdown();
        };

        // ─── Named Pipe İstemcisi ───
        _pipeClient = new PipeClient();
        _pipeClient.StartListening();
        _pipeClient.MessageReceived += OnServiceMessage;
        
        // Window Observer Sensörünü başlat (PipeClient Message Received'a burada sonradan da abone olabilir)
        _windowObserver = new WindowObserverService(_pipeClient);
        _windowObserver.Start();

        // MainWindow'a PipeClient referansını bağla (TODO'ları aktifleştir)
        mainWindow.SetPipeClient(_pipeClient);

        // ─── Global Hotkey (Ctrl+Alt+L) ───
        _hotkeyService = new GlobalHotkeyService();
        mainWindow.Loaded += (_, _) =>
        {
            _hotkeyService.Register(mainWindow);
        };
        _hotkeyService.HotkeyPressed += () =>
        {
            _pipeClient?.SendLockAll();
            _trayService?.ShowBalloon("WinAppLock", L("Str_TrayAllLocked", "Tüm uygulamalar kilitlendi! 🔐"));
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
                    _trayService?.ShowBalloon("WinAppLock", L("Str_TrayAllLocked", "Tüm uygulamalar kilitlendi! 🔐"));
                    break;
            }
        });
    }

    private LockOverlay? _currentOverlay;

    /// <summary>
    /// Kilitli uygulama algılandığında LockOverlay'i gösterir.
    /// </summary>
    private void ShowLockOverlay(PipeMessage message)
    {
        try
        {
            // Eğer halihazırda bir overlay açıksa, yenisini açma (veya mevcut olanı güncelle?)
            // Şimdilik sadece bir tane gösteriyoruz ki sistem kilitlenmesin.
            if (_currentOverlay != null && _currentOverlay.IsVisible)
            {
                return;
            }

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

            _currentOverlay = new LockOverlay();

            _currentOverlay.AuthSuccess += processId =>
            {
                _pipeClient?.SendAuthSuccess(processId, message.AppName);
                _currentOverlay = null;
            };
            
            _currentOverlay.AuthCancelled += processId =>
            {
                _pipeClient?.SendAuthCancelled(processId);
                _currentOverlay = null;
            };

            // Kapanma durumunda referansı temizle
            _currentOverlay.Closed += (s, e) => { _currentOverlay = null; };

            _currentOverlay.ShowForProcess(
                message.ProcessId,
                message.AppName ?? L("Str_UnknownApp", "Bilinmeyen Uygulama"),
                iconBase64,
                customPasswordHash);

            // Pencereyi öne getir ve odakla
            _currentOverlay.Activate();
            _currentOverlay.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Şifre Ekranı Yüklenirken Hata Oluştu!\n\nSebeb: {ex.Message}\nHata Türü: {ex.GetType().FullName}", 
                "WinAppLock UI Debug", MessageBoxButton.OK, MessageBoxImage.Error);
                
            System.Diagnostics.Debug.WriteLine($"LockOverlay hatası: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _pipeClient?.Dispose();
        _windowObserver?.Dispose();
        _trayService?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
