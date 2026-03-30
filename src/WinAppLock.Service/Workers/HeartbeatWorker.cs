using Serilog;

namespace WinAppLock.Service.Workers;

/// <summary>
/// Askıya alınan process'lerin dışarıdan (Task Manager vb.) resume edilip edilmediğini
/// periyodik olarak kontrol eder. Resume edildiyse tekrar askıya alır.
/// 
/// Kontrol aralığı: 1 saniye.
/// Bu mekanizma "meraklı aile bireyi/iş arkadaşı" seviyesindeki bypass girişimlerini engeller.
/// </summary>
public class HeartbeatWorker : BackgroundService
{
    private readonly LockStateManager _lockStateManager;
    private static readonly TimeSpan CHECK_INTERVAL = TimeSpan.FromSeconds(1);

    public HeartbeatWorker(LockStateManager lockStateManager)
    {
        _lockStateManager = lockStateManager;
    }

    /// <summary>
    /// Her saniye askıya alınan process'lerin durumunu kontrol eder.
    /// Eğer bir process beklenmedik şekilde resume edildiyse tekrar askıya alır.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Information("Heartbeat kontrolü başlatıldı (aralık: {Interval}s)", CHECK_INTERVAL.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                CheckSuspendedProcesses();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Heartbeat kontrol döngüsünde hata");
            }

            await Task.Delay(CHECK_INTERVAL, stoppingToken);
        }
    }

    /// <summary>
    /// Askıya alınmış tüm process'leri kontrol eder.
    /// Dışarıdan resume edilmiş olanları tekrar askıya alır.
    /// </summary>
    private void CheckSuspendedProcesses()
    {
        var suspendedProcesses = _lockStateManager.GetSuspendedProcesses();

        foreach (var (processId, info) in suspendedProcesses)
        {
            // Process artık çalışmıyorsa (kapatılmış/crash olmuş) kaydı temizle
            if (!ProcessController.IsProcessRunning(processId))
            {
                Log.Debug("Askıdaki process artık çalışmıyor: PID {PID}", processId);
                continue;
            }

            // Process hâlâ askıdaysa sorun yok
            if (ProcessController.IsProcessSuspended(processId))
                continue;

            // Process dışarıdan resume edilmiş! Tekrar askıya al.
            Log.Warning("Process dışarıdan resume edildi, tekrar askıya alınıyor: PID {PID} ({App})",
                processId, info.LockedApp.DisplayName);

            ProcessController.SuspendProcess(processId);
        }
    }
}
