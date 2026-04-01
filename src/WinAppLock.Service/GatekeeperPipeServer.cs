using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Serilog;
using WinAppLock.Core.Data;
using WinAppLock.Core.IPC;
using WinAppLock.Core.Identification;
using WinAppLock.Core.Models;
using WinAppLock.Core.Registry;

namespace WinAppLock.Service;

/// <summary>
/// Gatekeeper process'lerinden gelen başlatma isteklerini karşılayan duplex pipe sunucusu.
/// 
/// Mimari:
/// - Her Gatekeeper instance ayrı bir pipe bağlantısı açar
/// - İstekler bir kuyruğa alınır, tek tek işlenir (aynı anda tek şifre ekranı)
/// - Auth sonucu geldiğinde sıradaki Gatekeeper'a yanıt gönderilir
/// - IFEO kaydı geçici olarak kaldırılır (sonsuz döngü engeli), yanıt sonrası geri eklenir
/// 
/// Pipe: WinAppLock_Gatekeeper (duplex, çoklu bağlantı)
/// </summary>
public class GatekeeperPipeServer : IDisposable
{
    private readonly AppDatabase _database;
    private readonly PipeServer _uiPipeServer;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    /// <summary>Bekleyen Gatekeeper oturumlarının kuyruğu.</summary>
    private readonly ConcurrentQueue<GatekeeperSession> _sessionQueue = new();

    /// <summary>Şu anda şifre ekranı gösterilen aktif oturum.</summary>
    private GatekeeperSession? _activeSession;
    private readonly object _sessionLock = new();

    /// <summary>Gatekeeper.exe'nin deploy yolu (IFEO kayıtlarında kullanılır).</summary>
    private readonly string _gatekeeperPath;

    /// <summary>
    /// Eğer aynı exe için birden fazla peş peşe talep gelirse, Auth kargaşasını önlemek adına
    /// şifrenin geçerli sayıldığı (10 saniyelik grace period) bitiş zamanını saklar.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _authCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Auto-Relock (Zamanlı Yeniden Kilitleme) için şifrenin girildiği ilk anı saklar.
    /// Key: ExeName, Value: Şifrenin açıldığı UTC zamanı.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _unlockSessions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Başarılı şifre girişinden sonra çoklu işlemler için tanınan şifresiz geçiş süresi (30 saniye).</summary>
    private static readonly TimeSpan AUTH_GRACE_PERIOD = TimeSpan.FromSeconds(30);

    public GatekeeperPipeServer(AppDatabase database, PipeServer uiPipeServer)
    {
        _database = database;
        _uiPipeServer = uiPipeServer;
        _gatekeeperPath = PipeConstants.GATEKEEPER_DEPLOY_PATH;
    }

    /// <summary>
    /// Pipe sunucusunu başlatır ve Gatekeeper bağlantılarını dinlemeye başlar.
    /// </summary>
    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => AcceptConnections(_cts.Token));
        Log.Information("[GatekeeperPipe] Sunucu başlatıldı, bağlantı bekleniyor...");
    }

    /// <summary>
    /// Gatekeeper bağlantılarını kabul eden ana döngü.
    /// Her bağlantı ayrı bir görevde (task) işlenir.
    /// </summary>
    private async Task AcceptConnections(CancellationToken ct)
    {
        var pipeSecurity = CreatePipeSecurity();

        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                // Çok kısa bekleme
                await Task.Delay(50, ct);

                // ACL ile oluşturmayı dene
                pipe = NamedPipeServerStreamAcl.Create(
                    PipeConstants.GATEKEEPER_PIPE,
                    PipeDirection.InOut,
                    100, // Maksimum instance
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    PipeConstants.BUFFER_SIZE,
                    PipeConstants.BUFFER_SIZE,
                    pipeSecurity);

                await pipe.WaitForConnectionAsync(ct);

                // İşleme görevine devret
                _ = HandleGatekeeperConnection(pipe, ct);
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                break;
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Warning("[GatekeeperPipe] Yetki uyarısı (Hata yoksayılıp tekrar deneniyor): {Msg}", ex.Message);
                pipe?.Dispose();
                await Task.Delay(1000, ct);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[GatekeeperPipe] Bağlantı hatası");
                pipe?.Dispose();
                await Task.Delay(1000, ct);
            }
        }
    }

    /// <summary>
    /// Pipe için erişim kurallarını oluşturur.
    /// </summary>
    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        
        // Herkese (Everyone) tam yetki ver (Erişim engeli sorunlarını aşmak için en geniş ayar)
        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        security.AddAccessRule(new PipeAccessRule(
            everyoneSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return security;
    }

    /// <summary>
    /// Tek bir Gatekeeper bağlantısını işler.
    /// İsteği okur, kuyruğa alır, yanıt hazırlanana kadar bekler, yanıtı yazar.
    /// </summary>
    private async Task HandleGatekeeperConnection(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            // İsteği oku
            var request = await ReadRequest(pipe, ct);
            if (request == null)
            {
                pipe.Dispose();
                return;
            }

            Log.Information("[GatekeeperPipe] İstek alındı: {ExePath} (Gatekeeper PID: {Pid})",
                request.OriginalExePath, request.GatekeeperPid);

            // Uygulamanın kilitli olup olmadığını kontrol et
            var exeName = Path.GetFileName(request.OriginalExePath);
            var lockedApps = _database.GetEnabledLockedApps();
            var matchedApp = FindMatchingLockedApp(exeName, request.OriginalExePath, lockedApps);

            if (matchedApp == null)
            {
                // Uygulama kilitli değil — doğrudan izin ver
                Log.Information("[GatekeeperPipe] {ExeName} kilitli listede değil, izin veriliyor.", exeName);

                // IFEO geçici kaldır → başlat → geri ekle
                IfeoRegistrar.TemporarilyUnregister(exeName, _gatekeeperPath);
                await WriteResponse(pipe, new GatekeeperResponse { Verdict = GatekeeperVerdict.Allow });
                await Task.Delay(500, ct); // Başlatma için süre tanı
                IfeoRegistrar.RestoreRegistration(exeName, _gatekeeperPath);

                pipe.Dispose();
                return;
            }

            // --- GRACE PERIOD (YETKİ ÖN BELLEĞİ) KONTROLÜ ---
            // Eğer daha önceden (son 10 saniye içinde) şifreyle açılmışsa, otomatik ALLOW ver
            var cacheKey = matchedApp.Identity.ExecutableName;
            if (_authCache.TryGetValue(cacheKey, out var expiry) && DateTime.UtcNow < expiry)
            {
                Log.Information("[GatekeeperPipe] Grace Period devrede ({App}). Şifre sorulmadan başlatılıyor.", matchedApp.DisplayName);

                // Peş peşe gelen isteklerde izni tazeleyelim ki dalga bitene kadar izin sürsün
                _authCache[cacheKey] = DateTime.UtcNow.Add(AUTH_GRACE_PERIOD);

                // IFEO geçici kaldır → başlat → geri ekle
                IfeoRegistrar.TemporarilyUnregister(exeName, _gatekeeperPath);
                await WriteResponse(pipe, new GatekeeperResponse { Verdict = GatekeeperVerdict.Allow });
                await Task.Delay(500, ct); 
                IfeoRegistrar.RestoreRegistration(exeName, _gatekeeperPath);

                pipe.Dispose();
                return;
            }

            // Uygulama kilitli ve ön bellek süresi dolmuş/yok → kuyruk ve şifre süreci
            var session = new GatekeeperSession
            {
                Request = request,
                Pipe = pipe,
                MatchedApp = matchedApp,
                CompletionSource = new TaskCompletionSource<GatekeeperResponse>()
            };

            _sessionQueue.Enqueue(session);
            Log.Information("[GatekeeperPipe] Oturum kuyruğa eklendi: {App} (Kuyruk: {Count})",
                matchedApp.DisplayName, _sessionQueue.Count);

            // Sıradaki oturumu başlat (eğer aktif yoksa)
            TryProcessNextSession();

            // Yanıt gelene kadar bekle (şifre süreci boyunca bloklanır)
            var response = await session.CompletionSource.Task;

            if (response.Verdict == GatekeeperVerdict.Allow)
            {
                // IFEO geçici kaldır → Gatekeeper uygulamayı başlatsın → geri ekle
                IfeoRegistrar.TemporarilyUnregister(exeName, _gatekeeperPath);
                await WriteResponse(pipe, response);
                await Task.Delay(1000, ct); // Başlatma için süre tanı
                IfeoRegistrar.RestoreRegistration(exeName, _gatekeeperPath);
            }
            else
            {
                await WriteResponse(pipe, response);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[GatekeeperPipe] Bağlantı işleme hatası");
        }
        finally
        {
            pipe.Dispose();
        }
    }

    /// <summary>
    /// Kuyrukta bekleyen bir sonraki oturumu işlemeye alır.
    /// Aktif oturum varsa bekler (aynı anda tek şifre ekranı kuralı).
    /// </summary>
    private void TryProcessNextSession()
    {
        lock (_sessionLock)
        {
            if (_activeSession != null) return; // Zaten bir şifre ekranı açık

            if (!_sessionQueue.TryDequeue(out var session)) return;

            _activeSession = session;

            Log.Information("[GatekeeperPipe] Şifre ekranı tetikleniyor: {App} (PID: {Pid})",
                session.MatchedApp.DisplayName, session.Request.GatekeeperPid);

            // UI'ya LockTriggered gönder — processId olarak Gatekeeper PID'si kullanılır
            _uiPipeServer.SendLockTriggered(
                session.Request.GatekeeperPid,
                session.MatchedApp.Identity.ExecutableName,
                session.Request.OriginalExePath);
        }
    }

    /// <summary>
    /// UI'dan gelen doğrulama sonucunu işler.
    /// PipeMessageRouter tarafından AuthSuccess/AuthCancelled mesajlarında çağrılır.
    /// </summary>
    /// <param name="gatekeeperPid">Yanıtın ait olduğu Gatekeeper PID'si (LockOverlay'deki processId).</param>
    /// <param name="isSuccess">Doğrulama başarılı mı?</param>
    public void ResolveAuth(int gatekeeperPid, bool isSuccess)
    {
        lock (_sessionLock)
        {
            if (_activeSession == null)
            {
                Log.Warning("[GatekeeperPipe] Aktif oturum yokken auth sonucu geldi (PID: {Pid})", gatekeeperPid);
                return;
            }

            if (_activeSession.Request.GatekeeperPid != gatekeeperPid)
            {
                Log.Warning("[GatekeeperPipe] PID uyumsuzluğu: beklenen {Expected}, gelen {Actual}",
                    _activeSession.Request.GatekeeperPid, gatekeeperPid);
                return;
            }

            var response = new GatekeeperResponse
            {
                Verdict = isSuccess ? GatekeeperVerdict.Allow : GatekeeperVerdict.Deny,
                Message = isSuccess ? null : "Doğrulama iptal edildi."
            };

            if (isSuccess)
            {
                // Başarılı girişte 30 saniyelik şifresiz geçiş "dalga kalkanı"nı aktifleştir
                var cacheKey = _activeSession.MatchedApp.Identity.ExecutableName;
                _authCache[cacheKey] = DateTime.UtcNow.Add(AUTH_GRACE_PERIOD);
                
                // Zamanlı Relock için kilit açılma anını kaydet
                _unlockSessions[cacheKey] = DateTime.UtcNow;

                Log.Debug("[GatekeeperPipe] {App} için 30s Grace Period başlatıldı ve Oturum Kaydedildi.", _activeSession.MatchedApp.DisplayName);
            }

            _activeSession.CompletionSource.SetResult(response);

            Log.Information("[GatekeeperPipe] Auth sonucu: {Verdict} — {App}",
                response.Verdict, _activeSession.MatchedApp.DisplayName);

            _activeSession = null;

            // Sıradaki oturumu başlat
            TryProcessNextSession();
        }
    }

    /// <summary>
    /// Aktif bir Gatekeeper oturumu olup olmadığını kontrol eder.
    /// PipeMessageRouter, gelen AuthSuccess/AuthCancelled mesajının
    /// Gatekeeper'a mı yoksa eski WMI sistemine mi ait olduğunu ayırt etmek için kullanır.
    /// </summary>
    /// <param name="processId">Kontrol edilecek process ID'si.</param>
    /// <returns>Bu PID bir Gatekeeper oturumuna aitse true.</returns>
    public bool IsGatekeeperSession(int processId)
    {
        lock (_sessionLock)
        {
            return _activeSession?.Request.GatekeeperPid == processId;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Yardımcı Metodlar
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Kilitli uygulama listesinde exe adı veya yol ile eşleşen uygulamayı bulur.
    /// </summary>
    private LockedApp? FindMatchingLockedApp(string exeName, string exePath, List<LockedApp> lockedApps)
    {
        // Önce tam path ile AppIdentifier eşleştirmesi dene
        if (File.Exists(exePath))
        {
            try
            {
                var identity = AppIdentifier.CreateIdentity(exePath);
                foreach (var app in lockedApps)
                {
                    if (AppIdentifier.IsMatch(identity, app))
                        return app;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[GatekeeperPipe] Kimlik oluşturma hatası: {Path}", exePath);
            }
        }

        // Fallback: exe adı ile eşleştir
        return lockedApps.FirstOrDefault(a =>
            string.Equals(a.Identity.ExecutableName, exeName, StringComparison.OrdinalIgnoreCase));
    }



    /// <summary>Pipe'tan Gatekeeper isteğini okur (uzunluk ön ekli JSON).</summary>
    private async Task<GatekeeperRequest?> ReadRequest(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            var lengthBuffer = new byte[4];
            var bytesRead = await pipe.ReadAsync(lengthBuffer, ct);
            if (bytesRead < 4) return null;

            var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
            if (messageLength <= 0 || messageLength > PipeConstants.BUFFER_SIZE) return null;

            var messageBuffer = new byte[messageLength];
            bytesRead = await pipe.ReadAsync(messageBuffer, ct);
            if (bytesRead < messageLength) return null;

            var json = Encoding.UTF8.GetString(messageBuffer, 0, bytesRead);
            return JsonSerializer.Deserialize<GatekeeperRequest>(json);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[GatekeeperPipe] İstek okuma hatası");
            return null;
        }
    }

    /// <summary>Pipe'a Gatekeeper yanıtını yazar (uzunluk ön ekli JSON).</summary>
    private async Task WriteResponse(NamedPipeServerStream pipe, GatekeeperResponse response)
    {
        try
        {
            var json = JsonSerializer.Serialize(response);
            var bytes = Encoding.UTF8.GetBytes(json);

            await pipe.WriteAsync(BitConverter.GetBytes(bytes.Length));
            await pipe.WriteAsync(bytes);
            await pipe.FlushAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[GatekeeperPipe] Yanıt yazma hatası");
        }
    }

    /// <summary>Belirli bir uygulamanın yetkili oturumunu iptal eder. (Zamanlı kilitleme için)</summary>
    public void LockAppSession(string exeName)
    {
        _authCache.TryRemove(exeName, out _);
        _unlockSessions.TryRemove(exeName, out _);
    }

    /// <summary>Açık olan tüm kilit oturumlarını döndürür.</summary>
    public IReadOnlyDictionary<string, DateTime> GetActiveUnlockSessions()
    {
        return _unlockSessions;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Bir Gatekeeper bağlantısının oturum durumunu temsil eder.
/// Kuyrukta beklerken ve şifre doğrulama süresince aktif kalır.
/// </summary>
internal class GatekeeperSession
{
    /// <summary>Gatekeeper'dan gelen orijinal istek.</summary>
    public GatekeeperRequest Request { get; init; } = null!;

    /// <summary>Gatekeeper'ın bağlı olduğu pipe stream (yanıt yazımı için).</summary>
    public NamedPipeServerStream Pipe { get; init; } = null!;

    /// <summary>Eşleşen kilitli uygulama bilgisi.</summary>
    public LockedApp MatchedApp { get; init; } = null!;

    /// <summary>
    /// Auth sonucu gelene kadar bloklamak için kullanılan TaskCompletionSource.
    /// UI'dan AuthSuccess/AuthCancelled geldiğinde SetResult çağrılır.
    /// </summary>
    public TaskCompletionSource<GatekeeperResponse> CompletionSource { get; init; } = null!;
}
