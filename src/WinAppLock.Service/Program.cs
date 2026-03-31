using Serilog;
using WinAppLock.Core.Data;
using WinAppLock.Core.IPC;
using WinAppLock.Core.Registry;
using WinAppLock.Service;
using WinAppLock.Service.Workers;

// ─── Serilog Yapılandırması ───
var appDataPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "WinAppLock", "Logs"
);
Directory.CreateDirectory(appDataPath);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        Path.Combine(appDataPath, "service-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
    )
    .CreateLogger();

try
{
    // Ciddi bir AppLocker yapabilmek için Service Process Priority Yüksek olmalı
    System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
    Log.Information("Process Priority => High olarak ayarlandı.");
}
catch (Exception ex)
{
    Log.Warning(ex, "Process Priority değiştirilemedi. Yetki eksiği olabilir.");
}

Log.Information("═══ WinAppLock Service başlatılıyor ═══");

try
{
    var builder = Host.CreateDefaultBuilder(args);

    // Windows Service olarak çalıştırma desteği
    builder.UseWindowsService(options =>
    {
        options.ServiceName = "WinAppLock";
    });

    builder.UseSerilog();

    builder.ConfigureServices(services =>
    {
        // ─── Singleton Servisler ───
        services.AddSingleton<AppDatabase>();
        services.AddSingleton<LockStateManager>();
        services.AddSingleton<PipeServer>();
        services.AddSingleton<GatekeeperPipeServer>();

        // ─── Background Worker'lar ───
        services.AddHostedService<HeartbeatWorker>();

        // ─── Pipe Sunucuları başlatma ve mesaj yönlendirme ───
        services.AddHostedService<PipeMessageRouter>();
    });

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "WinAppLock Service başlatılamadı");
}
finally
{
    Log.Information("═══ WinAppLock Service durduruluyor ═══");
    await Log.CloseAndFlushAsync();
}

/// <summary>
/// Pipe sunucularını başlatan ve gelen mesajları ilgili yöneticilere yönlendiren background service.
/// Hem UI pipe'ını hem Gatekeeper pipe'ını yönetir.
/// </summary>
internal class PipeMessageRouter : BackgroundService
{
    private readonly PipeServer _pipeServer;
    private readonly GatekeeperPipeServer _gatekeeperPipeServer;
    private readonly LockStateManager _lockStateManager;
    private readonly AppDatabase _database;

    public PipeMessageRouter(
        PipeServer pipeServer,
        GatekeeperPipeServer gatekeeperPipeServer,
        LockStateManager lockStateManager,
        AppDatabase database)
    {
        _pipeServer = pipeServer;
        _gatekeeperPipeServer = gatekeeperPipeServer;
        _lockStateManager = lockStateManager;
        _database = database;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // UI Pipe sunucusunu başlat
        _pipeServer.MessageReceived += OnMessageReceived;
        _pipeServer.ConnectionLost += OnConnectionLost;
        _pipeServer.Start();

        // Gatekeeper Pipe sunucusunu başlat
        _gatekeeperPipeServer.Start();

        // IFEO kayıtlarını veritabanıyla senkronize et
        SyncIfeoRegistrations();

        Log.Information("Tüm pipe sunucuları ve IFEO senkronizasyonu hazır.");
        return Task.CompletedTask;
    }

    private void OnConnectionLost()
    {
        _lockStateManager.ExecuteDeadManSwitch();
    }

    /// <summary>
    /// IFEO kayıtlarını veritabanındaki aktif kilitli uygulamalarla senkronize eder.
    /// Eksik kayıtları ekler, fazla kayıtları siler.
    /// </summary>
    private void SyncIfeoRegistrations()
    {
        var gatekeeperPath = PipeConstants.GATEKEEPER_DEPLOY_PATH;

        if (!File.Exists(gatekeeperPath))
        {
            Log.Warning("[IFEO] Gatekeeper.exe bulunamadı: {Path} — IFEO kayıtları yazılamıyor.", gatekeeperPath);
            return;
        }

        var enabledApps = _database.GetEnabledLockedApps();
        IfeoRegistrar.SyncRegistrations(enabledApps, gatekeeperPath);
    }

    /// <summary>
    /// UI'dan gelen mesajları ilgili yöneticilere yönlendirir.
    /// Auth mesajları Gatekeeper oturumunu kontrol eder.
    /// </summary>
    private void OnMessageReceived(PipeMessage message)
    {
        switch (message.Type)
        {
            case PipeMessageType.AuthSuccess:
                if (_gatekeeperPipeServer.IsGatekeeperSession(message.ProcessId))
                {
                    // IFEO Gatekeeper oturumu — Gatekeeper'a Allow gönder
                    _gatekeeperPipeServer.ResolveAuth(message.ProcessId, true);
                    _database.LogAccessAttempt(message.AppName ?? "Bilinmeyen", true);
                }
                else
                {
                    // Legacy oturum (geçiş dönemi uyumluluğu)
                    _lockStateManager.OnAuthSuccess(message.ProcessId);
                    _database.LogAccessAttempt(message.AppName ?? "Bilinmeyen", true);
                }
                break;

            case PipeMessageType.AuthCancelled:
                if (_gatekeeperPipeServer.IsGatekeeperSession(message.ProcessId))
                {
                    _gatekeeperPipeServer.ResolveAuth(message.ProcessId, false);
                }
                else
                {
                    _lockStateManager.OnAuthCancelled(message.ProcessId);
                }
                break;

            // --- Sensör (WindowObserver) Komutları ---
            case PipeMessageType.SessionInvalidated:
                _lockStateManager.InvalidateSession(message.ProcessId);
                break;

            case PipeMessageType.WindowResurrected:
                _lockStateManager.SuspendAppProcesses(message.ProcessId);
                break;

            // --- Uygulama Listesi Değişiklikleri ---
            case PipeMessageType.AppAdded:
            case PipeMessageType.AppRemoved:
            case PipeMessageType.AppToggled:
            case PipeMessageType.SettingsUpdated:
                SyncIfeoRegistrations();
                break;

            case PipeMessageType.LockAll:
                _lockStateManager.LockAll();
                _pipeServer.SendAllLocked();
                break;

            case PipeMessageType.UnlockAll:
                foreach (var (pid, _) in _lockStateManager.GetSuspendedProcesses())
                {
                    _lockStateManager.OnAuthSuccess(pid);
                }
                break;

            default:
                Log.Warning("Bilinmeyen mesaj tipi: {Type}", message.Type);
                break;
        }
    }
}
