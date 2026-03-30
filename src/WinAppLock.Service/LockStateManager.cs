using System.Collections.Concurrent;
using Serilog;
using WinAppLock.Core.Models;

namespace WinAppLock.Service;

/// <summary>
/// Askıya alınan process'lerin kilit/oturum durumunu yönetir.
/// 
/// Sorumlulukları:
/// - Askıya alınan process'lerin kaydını tutar
/// - Şifre sonrası oturum başlatır (aynı uygulama tekrar açılırsa şifre sorma)
/// - RelockBehavior'a göre otomatik tekrar kilitleme
/// - Process kapandığında oturumu temizler
/// </summary>
public class LockStateManager
{
    /// <summary>Askıya alınan process'ler: PID → LockedApp bilgisi.</summary>
    private readonly ConcurrentDictionary<int, SuspendedProcessInfo> _suspendedProcesses = new();

    /// <summary>Aktif oturumlar: exe adı (küçük harf) → oturum bilgisi.</summary>
    private readonly ConcurrentDictionary<string, SessionInfo> _activeSessions = new();

    /// <summary>
    /// Askıya alınan bir process'i kayıt altına alır.
    /// </summary>
    /// <param name="processId">Askıya alınan process'in ID'si</param>
    /// <param name="lockedApp">İlişkili kilitli uygulama bilgisi</param>
    public void RegisterSuspendedProcess(int processId, LockedApp lockedApp)
    {
        var info = new SuspendedProcessInfo
        {
            ProcessId = processId,
            LockedApp = lockedApp,
            SuspendedAt = DateTime.UtcNow
        };

        _suspendedProcesses[processId] = info;
        Log.Debug("Process kaydedildi: PID {PID}, Uygulama: {App}", processId, lockedApp.DisplayName);
    }

    /// <summary>
    /// Başarılı kimlik doğrulama sonrası process'i serbest bırakır ve oturum başlatır.
    /// </summary>
    /// <param name="processId">Serbest bırakılacak process ID'si</param>
    public void OnAuthSuccess(int processId)
    {
        if (!_suspendedProcesses.TryRemove(processId, out var info))
        {
            Log.Warning("AuthSuccess: PID {PID} kayıtlarda bulunamadı", processId);
            return;
        }

        // Process'i devam ettir
        ProcessController.ResumeProcess(processId);

        // Oturum başlat (aynı uygulama tekrar açılırsa şifre sorma)
        var sessionKey = info.LockedApp.Identity.ExecutableName.ToLowerInvariant();
        var session = new SessionInfo
        {
            AppName = sessionKey,
            LockedApp = info.LockedApp,
            StartedAt = DateTime.UtcNow,
            PrimaryProcessId = processId
        };

        _activeSessions[sessionKey] = session;

        // RelockBehavior.TimeBased ise zamanlayıcı başlat
        if (info.LockedApp.RelockBehavior == RelockBehavior.TimeBased)
        {
            StartRelockTimer(session, info.LockedApp.RelockTimeMinutes);
        }

        Log.Information("Oturum başlatıldı: {App}, Relock: {Behavior}",
            info.LockedApp.DisplayName, info.LockedApp.RelockBehavior);
    }

    /// <summary>
    /// Kullanıcı şifre ekranını iptal ettiğinde process'i sonlandırır.
    /// </summary>
    /// <param name="processId">Sonlandırılacak process ID'si</param>
    public void OnAuthCancelled(int processId)
    {
        _suspendedProcesses.TryRemove(processId, out _);

        try
        {
            var process = System.Diagnostics.Process.GetProcessById(processId);
            // Önce resume et (askıda olan process kill edilemeyebilir)
            ProcessController.ResumeProcess(processId);
            process.Kill();
            Log.Information("Process sonlandırıldı (kullanıcı iptal etti): PID {PID}", processId);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Process sonlandırılırken hata: PID {PID}", processId);
        }
    }

    /// <summary>
    /// Belirli bir uygulama için aktif oturum olup olmadığını kontrol eder.
    /// Aktif oturum varsa aynı uygulamanın yeni instance'larına şifre sorulmaz.
    /// </summary>
    /// <param name="executableName">Kontrol edilecek exe adı</param>
    /// <returns>Oturum aktifse true</returns>
    public bool IsSessionActive(string executableName)
    {
        var key = executableName.ToLowerInvariant();
        return _activeSessions.ContainsKey(key);
    }

    /// <summary>
    /// Process kapandığında çağrılır. OnClose davranışı varsa oturumu sonlandırır.
    /// </summary>
    /// <param name="processId">Kapanan process'in ID'si</param>
    /// <param name="processName">Kapanan process'in adı</param>
    public void OnProcessExited(int processId, string processName)
    {
        // Askıda olan bir process kapandıysa kaydını temizle
        _suspendedProcesses.TryRemove(processId, out _);

        var key = processName.ToLowerInvariant();
        if (!_activeSessions.TryGetValue(key, out var session))
            return;

        // İlk(ana) process kapandıysa ve davranış OnClose ise oturumu bitir
        if (session.PrimaryProcessId == processId &&
            session.LockedApp.RelockBehavior == RelockBehavior.OnClose)
        {
            _activeSessions.TryRemove(key, out _);
            session.RelockTimer?.Dispose();
            Log.Information("Oturum sonlandırıldı (uygulama kapandı): {App}", processName);
        }
    }

    /// <summary>
    /// Tüm oturumları sonlandırır (Ctrl+Alt+L veya "Tümünü Kilitle" komutu).
    /// Açık olan kilitli uygulamaları da askıya alır.
    /// </summary>
    public void LockAll()
    {
        foreach (var session in _activeSessions.Values)
        {
            session.RelockTimer?.Dispose();

            // Ana process hâlâ çalışıyorsa askıya al
            if (ProcessController.IsProcessRunning(session.PrimaryProcessId))
            {
                ProcessController.SuspendProcess(session.PrimaryProcessId);
                RegisterSuspendedProcess(session.PrimaryProcessId, session.LockedApp);
            }
        }

        _activeSessions.Clear();
        Log.Information("Tüm oturumlar sonlandırıldı, kilitler aktif");
    }

    /// <summary>
    /// Askıya alınmış process'lerin PID listesini döndürür.
    /// HeartbeatWorker bu listeyi kullanarak dışarıdan resume edilmiş process'leri kontrol eder.
    /// </summary>
    /// <returns>Askıda olan process ID ve bilgileri</returns>
    public IReadOnlyDictionary<int, SuspendedProcessInfo> GetSuspendedProcesses()
    {
        return _suspendedProcesses;
    }

    /// <summary>
    /// Süre bazlı tekrar kilitleme zamanlayıcısı başlatır.
    /// </summary>
    private void StartRelockTimer(SessionInfo session, int minutes)
    {
        session.RelockTimer = new Timer(_ =>
        {
            Log.Information("Otomatik relock: {App} ({Minutes} dk doldu)", session.AppName, minutes);

            // Oturumu sonlandır
            _activeSessions.TryRemove(session.AppName, out SessionInfo? _);

            // Process hâlâ çalışıyorsa askıya al
            if (ProcessController.IsProcessRunning(session.PrimaryProcessId))
            {
                ProcessController.SuspendProcess(session.PrimaryProcessId);
                RegisterSuspendedProcess(session.PrimaryProcessId, session.LockedApp);
            }

            session.RelockTimer?.Dispose();
        }, null, TimeSpan.FromMinutes(minutes), Timeout.InfiniteTimeSpan);
    }
}

/// <summary>
/// Askıya alınan process hakkında bilgi.
/// </summary>
public class SuspendedProcessInfo
{
    public int ProcessId { get; init; }
    public LockedApp LockedApp { get; init; } = null!;
    public DateTime SuspendedAt { get; init; }
}

/// <summary>
/// Aktif oturum bilgisi (şifre girilmiş, uygulama kullanılıyor).
/// </summary>
public class SessionInfo
{
    public string AppName { get; init; } = string.Empty;
    public LockedApp LockedApp { get; init; } = null!;
    public DateTime StartedAt { get; init; }
    public int PrimaryProcessId { get; init; }
    public Timer? RelockTimer { get; set; }
}
