using Serilog;
using WinAppLock.Core.Data;
using WinAppLock.Core.IPC;
using WinAppLock.Core.Registry;

namespace WinAppLock.Service.Workers;

/// <summary>
/// IFEO kayıtlarının sağlığını ve legacy askı durumlarını periyodik olarak kontrol eder.
///
/// Sorumlulukları:
///   1. IFEO kayıtlarının kurcalanmadığını doğrula (anti-tamper)
///   2. Legacy askıdaki process'lerin dışarıdan resume edilmediğini kontrol et
///   3. UI Sensörüne izlenmesi gereken ağaçları bildir
///
/// Kontrol aralığı: 5 saniye.
/// </summary>
public class HeartbeatWorker : BackgroundService
{
    private readonly LockStateManager _lockStateManager;
    private readonly PipeServer _pipeServer;
    private readonly AppDatabase _database;
    private readonly GatekeeperPipeServer _gatekeeperPipeServer;
    private static readonly TimeSpan CHECK_INTERVAL = TimeSpan.FromSeconds(5);

    /// <summary>Son IFEO doğrulama zamanı. Ağır işlem olduğu için her döngüde yapılmaz.</summary>
    private DateTime _lastIfeoCheck = DateTime.MinValue;
    private static readonly TimeSpan IFEO_CHECK_INTERVAL = TimeSpan.FromSeconds(30);

    public HeartbeatWorker(LockStateManager lockStateManager, PipeServer pipeServer, AppDatabase database, GatekeeperPipeServer gatekeeperPipeServer)
    {
        _lockStateManager = lockStateManager;
        _pipeServer = pipeServer;
        _database = database;
        _gatekeeperPipeServer = gatekeeperPipeServer;
    }

    /// <summary>
    /// Periyodik kontrol döngüsü.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Information("Heartbeat kontrolü başlatıldı (aralık: {Interval}s, IFEO doğrulama: {IfeoInterval}s)",
            CHECK_INTERVAL.TotalSeconds, IFEO_CHECK_INTERVAL.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Legacy askıdaki process kontrolü (Suspend bypass tespiti)
                CheckSuspendedProcesses();

                // UI Sensörü için aktif takip listesini yolla
                var trackingList = _lockStateManager.GetTreesToTrack();
                if (trackingList.Count > 0)
                {
                    _pipeServer.SendTrackingListUpdate(trackingList);
                }

                // IFEO sağlık kontrolü (belirli aralıklarla)
                if ((DateTime.UtcNow - _lastIfeoCheck) >= IFEO_CHECK_INTERVAL)
                {
                    VerifyIfeoHealth();
                    _lastIfeoCheck = DateTime.UtcNow;
                }
                // Zamanlı Yeniden Kilitleme (Auto Relock) kontrolü
                CheckAutoRelock();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Heartbeat kontrol döngüsünde hata");
            }

            await Task.Delay(CHECK_INTERVAL, stoppingToken);
        }
    }

    /// <summary>
    /// Zamanlı kilitleme (Auto Relock) ayarını kontrol eder.
    /// Süresi dolan uygulamaların oturumlarını (Gatekeeper bypass) sonlandırır ve
    /// açık olan process'leri duraklatır (suspend).
    /// </summary>
    private void CheckAutoRelock()
    {
        var settings = _database.GetSettings();
        if (settings.AutoRelockMinutes <= 0) return;

        var timeoutSpan = TimeSpan.FromMinutes(settings.AutoRelockMinutes);
        var activeSessions = _gatekeeperPipeServer.GetActiveUnlockSessions();

        foreach (var (exeName, unlockTime) in activeSessions)
        {
            if (DateTime.UtcNow - unlockTime >= timeoutSpan)
            {
                Log.Information("[AutoRelock] {ExeName} için izin süresi doldu ({Minutes} dk). Oturum sonlandırılıyor...", 
                    exeName, settings.AutoRelockMinutes);

                // Ortak _authCache ve _unlockSessions yetkilerini temizle
                _gatekeeperPipeServer.LockAppSession(exeName);

                // Eğer aktif process'leri varsa onlara "LockTree" uygularız (Suspend)
                var processes = ProcessController.GetProcessesByName(exeName);
                if (processes.Count > 0)
                {
                    Log.Information("[AutoRelock] Açık olan {Count} adet {ExeName} işlemi donduruluyor.", processes.Count, exeName);
                    // _lockStateManager eski ağaç yöntemleriyle çalışabilir ama IFEOGatekeeper'da asıl kilit Pipe üzerinden gelir.
                    // Suspend işlemi son çare olarak uygulanır veya zorunlu kilit aktif edilir.
                    foreach(var p in processes)
                    {
                        ProcessController.SuspendTree(p.Id);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Legacy askıya alınmış process'leri kontrol eder.
    /// Dışarıdan resume edilmişleri tekrar askıya alır.
    /// </summary>
    private void CheckSuspendedProcesses()
    {
        var suspendedProcesses = _lockStateManager.GetSuspendedProcesses();

        foreach (var (processId, info) in suspendedProcesses)
        {
            if (!ProcessController.IsProcessRunning(processId))
            {
                Log.Debug("Askıdaki process artık çalışmıyor: PID {PID}", processId);
                continue;
            }

            if (ProcessController.IsProcessSuspended(processId))
                continue;

            Log.Warning("Process dışarıdan resume edildi, tekrar askıya alınıyor: PID {PID} ({App})",
                processId, info.DisplayName);

            ProcessController.SuspendProcess(processId);
        }
    }

    /// <summary>
    /// IFEO kayıtlarının kurcalanmadığını doğrular.
    /// Eksik veya bozuk kayıtları yeniden yazar (anti-tamper).
    /// </summary>
    private void VerifyIfeoHealth()
    {
        var gatekeeperPath = PipeConstants.GATEKEEPER_DEPLOY_PATH;

        if (!File.Exists(gatekeeperPath))
        {
            Log.Debug("[Heartbeat] Gatekeeper.exe bulunamadı, IFEO doğrulama atlanıyor.");
            return;
        }

        var enabledApps = _database.GetEnabledLockedApps();

        foreach (var app in enabledApps)
        {
            var exeName = app.Identity.ExecutableName;
            if (string.IsNullOrWhiteSpace(exeName)) continue;

            if (!IfeoRegistrar.IsRegistered(exeName))
            {
                Log.Warning("[Heartbeat] IFEO kaydı eksik/silinmiş tespit edildi: {ExeName}. Yeniden yazılıyor.", exeName);
                IfeoRegistrar.RegisterApp(exeName, gatekeeperPath);
            }
        }
    }
}
