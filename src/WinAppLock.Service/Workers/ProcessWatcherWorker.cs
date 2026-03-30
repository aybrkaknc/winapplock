using System.Management;
using Serilog;
using WinAppLock.Core.Data;
using WinAppLock.Core.Identification;
using WinAppLock.Core.Models;

namespace WinAppLock.Service.Workers;

/// <summary>
/// WMI event subscription kullanarak yeni process oluşturulmasını izler.
/// Event-driven yaklaşım sayesinde CPU kullanımı minimumdur (polling yapmaz).
/// 
/// Kilitli listedeki bir uygulama başlatıldığında:
/// 1. Process'i SuspendThread ile askıya alır
/// 2. UI'ya Named Pipe üzerinden LOCK_TRIGGERED mesajı gönderir
/// </summary>
public class ProcessWatcherWorker : BackgroundService
{
    private readonly AppDatabase _database;
    private readonly LockStateManager _lockStateManager;
    private readonly PipeServer _pipeServer;
    private ManagementEventWatcher? _processStartWatcher;
    private ManagementEventWatcher? _processStopWatcher;

    /// <summary>Anlık olarak izlenen kilitli uygulama listesi (önbelleklenmiş).</summary>
    private List<LockedApp> _lockedApps = new();

    public ProcessWatcherWorker(
        AppDatabase database,
        LockStateManager lockStateManager,
        PipeServer pipeServer)
    {
        _database = database;
        _lockStateManager = lockStateManager;
        _pipeServer = pipeServer;
    }

    /// <summary>
    /// Servis başladığında WMI event watcher'ları oluşturur ve izlemeyi başlatır.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Information("ProcessWatcher başlatılıyor...");

        RefreshLockedAppsList();
        StartWatching();

        // Servis durdurulana kadar çalışmaya devam et
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            Log.Information("ProcessWatcher durduruluyor...");
        }
        finally
        {
            StopWatching();
        }
    }

    /// <summary>
    /// WMI event watcher'ları oluşturur ve başlatır.
    /// __InstanceCreationEvent ile yeni process oluşturulmasını,
    /// __InstanceDeletionEvent ile process kapanmasını izler.
    /// </summary>
    private void StartWatching()
    {
        try
        {
            // Process başlama izleme (100ms polling aralığı — WMI içsel)
            var startQuery = new WqlEventQuery(
                "__InstanceCreationEvent",
                new TimeSpan(0, 0, 0, 0, 100),
                "TargetInstance ISA 'Win32_Process'"
            );

            _processStartWatcher = new ManagementEventWatcher(startQuery);
            _processStartWatcher.EventArrived += OnProcessStarted;
            _processStartWatcher.Start();

            // Process kapanma izleme (relock davranışı için)
            var stopQuery = new WqlEventQuery(
                "__InstanceDeletionEvent",
                new TimeSpan(0, 0, 0, 0, 500),
                "TargetInstance ISA 'Win32_Process'"
            );

            _processStopWatcher = new ManagementEventWatcher(stopQuery);
            _processStopWatcher.EventArrived += OnProcessStopped;
            _processStopWatcher.Start();

            Log.Information("WMI process izleme aktif. {Count} uygulama izleniyor.", _lockedApps.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WMI watcher başlatılamadı");
        }
    }

    /// <summary>
    /// Yeni process başladığında tetiklenir.
    /// Kilitli listede olup olmadığını kontrol eder ve gerekirse askıya alır.
    /// </summary>
    private void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var processName = targetInstance["Name"]?.ToString();
            var processId = Convert.ToInt32(targetInstance["ProcessId"]);
            var executablePath = targetInstance["ExecutablePath"]?.ToString();

            if (string.IsNullOrEmpty(processName))
                return;

            // Kendi kendimizi kilitlemeyelim
            if (processName.Equals("WinAppLock.UI.exe", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("WinAppLock.Service.exe", StringComparison.OrdinalIgnoreCase))
                return;

            // Hızlı ön kontrol: ad ile kilitli listede var mı?
            var potentialMatches = _lockedApps
                .Where(app => app.IsEnabled)
                .Where(app => string.Equals(app.Identity.ExecutableName, processName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (potentialMatches.Count == 0)
                return;

            // Oturum kontrolü: bu uygulama zaten açılmış ve şifre girilmiş mi?
            if (_lockStateManager.IsSessionActive(processName))
            {
                Log.Debug("Oturum aktif, şifre sorulmayacak: {AppName}", processName);
                return;
            }

            // Detaylı tanımlama (hash + PE header)
            AppIdentity? processIdentity = null;
            if (!string.IsNullOrEmpty(executablePath))
            {
                try
                {
                    processIdentity = AppIdentifier.CreateIdentity(executablePath);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Process kimliği oluşturulamadı: {Path}", executablePath);
                }
            }

            foreach (var lockedApp in potentialMatches)
            {
                var isMatch = processIdentity != null
                    ? AppIdentifier.IsMatch(processIdentity, lockedApp)
                    : string.Equals(lockedApp.Identity.ExecutableName, processName, StringComparison.OrdinalIgnoreCase);

                if (!isMatch) continue;

                Log.Information("Kilitli uygulama algılandı: {AppName} (PID: {PID})", processName, processId);

                // 1. Process'i askıya al
                ProcessController.SuspendProcess(processId);

                // 2. Kilit durumunu kaydet
                _lockStateManager.RegisterSuspendedProcess(processId, lockedApp);

                // 3. UI'ya bildir
                _pipeServer.SendLockTriggered(processId, lockedApp.DisplayName, executablePath);

                break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Process başlama olayı işlenirken hata");
        }
    }

    /// <summary>
    /// Process kapandığında tetiklenir.
    /// RelockBehavior.OnClose ayarındaki uygulamalar için oturumu sonlandırır.
    /// </summary>
    private void OnProcessStopped(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var processName = targetInstance["Name"]?.ToString();
            var processId = Convert.ToInt32(targetInstance["ProcessId"]);

            if (string.IsNullOrEmpty(processName))
                return;

            _lockStateManager.OnProcessExited(processId, processName);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Process kapanma olayı işlenirken hata");
        }
    }

    /// <summary>
    /// Kilitli uygulamalar listesini veritabanından yeniden yükler.
    /// UI'dan uygulama eklendiğinde/çıkarıldığında çağrılır.
    /// </summary>
    public void RefreshLockedAppsList()
    {
        _lockedApps = _database.GetEnabledLockedApps();
        Log.Information("Kilitli uygulama listesi yenilendi: {Count} uygulama", _lockedApps.Count);
    }

    /// <summary>
    /// WMI watcher'ları durdurur ve kaynakları temizler.
    /// </summary>
    private void StopWatching()
    {
        _processStartWatcher?.Stop();
        _processStartWatcher?.Dispose();
        _processStopWatcher?.Stop();
        _processStopWatcher?.Dispose();

        Log.Information("WMI process izleme durduruldu.");
    }
}
