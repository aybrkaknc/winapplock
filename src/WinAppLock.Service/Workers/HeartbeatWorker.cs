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
    private static readonly TimeSpan CHECK_INTERVAL = TimeSpan.FromSeconds(5);

    /// <summary>Son IFEO doğrulama zamanı. Ağır işlem olduğu için her döngüde yapılmaz.</summary>
    private DateTime _lastIfeoCheck = DateTime.MinValue;
    private static readonly TimeSpan IFEO_CHECK_INTERVAL = TimeSpan.FromSeconds(30);

    public HeartbeatWorker(LockStateManager lockStateManager, PipeServer pipeServer, AppDatabase database)
    {
        _lockStateManager = lockStateManager;
        _pipeServer = pipeServer;
        _database = database;
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
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Heartbeat kontrol döngüsünde hata");
            }

            await Task.Delay(CHECK_INTERVAL, stoppingToken);
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
