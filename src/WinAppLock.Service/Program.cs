using Serilog;
using WinAppLock.Core.Data;
using WinAppLock.Core.IPC;
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
    .WriteTo.File(
        Path.Combine(appDataPath, "service-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
    )
    .CreateLogger();

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

        // ─── Background Worker'lar ───
        services.AddHostedService<ProcessWatcherWorker>();
        services.AddHostedService<HeartbeatWorker>();

        // ─── Pipe Sunucusu başlatma ve mesaj yönlendirme ───
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
/// Pipe sunucusunu başlatan ve gelen mesajları ilgili yöneticilere yönlendiren background service.
/// </summary>
internal class PipeMessageRouter : BackgroundService
{
    private readonly PipeServer _pipeServer;
    private readonly LockStateManager _lockStateManager;
    private readonly ProcessWatcherWorker _processWatcher;
    private readonly AppDatabase _database;

    public PipeMessageRouter(
        PipeServer pipeServer,
        LockStateManager lockStateManager,
        IEnumerable<IHostedService> hostedServices,
        AppDatabase database)
    {
        _pipeServer = pipeServer;
        _lockStateManager = lockStateManager;
        _database = database;

        // ProcessWatcherWorker'ı hosted services içinden bul
        _processWatcher = hostedServices.OfType<ProcessWatcherWorker>().First();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _pipeServer.MessageReceived += OnMessageReceived;
        _pipeServer.Start();

        return Task.CompletedTask;
    }

    /// <summary>
    /// UI'dan gelen mesajları ilgili yöneticilere yönlendirir.
    /// </summary>
    private void OnMessageReceived(PipeMessage message)
    {
        switch (message.Type)
        {
            case PipeMessageType.AuthSuccess:
                _lockStateManager.OnAuthSuccess(message.ProcessId);
                _database.LogAccessAttempt(message.AppName ?? "Bilinmeyen", true);
                break;

            case PipeMessageType.AuthCancelled:
                _lockStateManager.OnAuthCancelled(message.ProcessId);
                break;

            case PipeMessageType.AppAdded:
            case PipeMessageType.AppRemoved:
            case PipeMessageType.AppToggled:
                _processWatcher.RefreshLockedAppsList();
                break;

            case PipeMessageType.SettingsUpdated:
                _processWatcher.RefreshLockedAppsList();
                break;

            case PipeMessageType.LockAll:
                _lockStateManager.LockAll();
                _pipeServer.SendAllLocked();
                break;

            case PipeMessageType.UnlockAll:
                // Tüm askıdaki process'leri serbest bırak
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
