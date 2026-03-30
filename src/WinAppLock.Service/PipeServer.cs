using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Serilog;
using WinAppLock.Core.IPC;

namespace WinAppLock.Service;

/// <summary>
/// Named Pipe sunucusu. Service ile UI arasında çift yönlü iletişim sağlar.
/// 
/// İki ayrı pipe kullanır:
/// - SERVICE_TO_UI_PIPE: Service → UI yönünde mesaj gönderimi (LOCK_TRIGGERED vb.)
/// - UI_TO_SERVICE_PIPE: UI → Service yönünde mesaj alımı (AUTH_SUCCESS, APP_ADDED vb.)
/// 
/// Mesajlar JSON formatında serialize edilir.
/// </summary>
public class PipeServer : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    /// <summary>UI'dan mesaj alındığında tetiklenen event.</summary>
    public event Action<PipeMessage>? MessageReceived;

    /// <summary>
    /// Pipe sunucusunu başlatır ve UI'dan gelen mesajları dinlemeye başlar.
    /// </summary>
    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenForMessages(_cts.Token));
        Log.Information("Pipe sunucusu başlatıldı");
    }

    /// <summary>
    /// UI'ya LOCK_TRIGGERED mesajı gönderir (kilitli uygulama algılandığında).
    /// </summary>
    /// <param name="processId">Askıya alınan process ID'si</param>
    /// <param name="appName">Uygulama görüntü adı</param>
    /// <param name="appPath">Exe dosya yolu</param>
    public void SendLockTriggered(int processId, string appName, string? appPath)
    {
        var message = new PipeMessage
        {
            Type = PipeMessageType.LockTriggered,
            ProcessId = processId,
            AppName = appName,
            AppPath = appPath
        };

        SendMessage(PipeConstants.SERVICE_TO_UI_PIPE, message);
    }

    /// <summary>
    /// UI'ya tümü kilitlendi mesajı gönderir.
    /// </summary>
    public void SendAllLocked()
    {
        var message = new PipeMessage { Type = PipeMessageType.AllLocked };
        SendMessage(PipeConstants.SERVICE_TO_UI_PIPE, message);
    }

    /// <summary>
    /// Belirtilen pipe üzerinden mesaj gönderir.
    /// Bağlantı kurulamazsa (UI kapalı) sessizce log'a yazar.
    /// </summary>
    /// <param name="pipeName">Hedef pipe adı</param>
    /// <param name="message">Gönderilecek mesaj</param>
    private void SendMessage(string pipeName, PipeMessage message)
    {
        Task.Run(() =>
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                pipe.Connect(PipeConstants.CONNECTION_TIMEOUT_MS);

                var json = JsonSerializer.Serialize(message);
                var bytes = Encoding.UTF8.GetBytes(json);

                pipe.Write(BitConverter.GetBytes(bytes.Length), 0, 4); // Önce uzunluk
                pipe.Write(bytes, 0, bytes.Length);
                pipe.Flush();

                Log.Debug("Pipe mesajı gönderildi: {Type} → {Pipe}", message.Type, pipeName);
            }
            catch (TimeoutException)
            {
                Log.Debug("UI pipe bağlantısı zaman aşımı (UI kapalı olabilir): {Pipe}", pipeName);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Pipe mesajı gönderilemedi: {Type}", message.Type);
            }
        });
    }

    /// <summary>
    /// UI'dan gelen mesajları sürekli dinler.
    /// Her mesaj alındığında MessageReceived event'ini tetikler.
    /// </summary>
    private async Task ListenForMessages(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    PipeConstants.UI_TO_SERVICE_PIPE,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous
                );

                await pipe.WaitForConnectionAsync(ct);

                // Mesaj uzunluğunu oku (ilk 4 byte)
                var lengthBuffer = new byte[4];
                var bytesRead = await pipe.ReadAsync(lengthBuffer, ct);
                if (bytesRead < 4) continue;

                var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                if (messageLength <= 0 || messageLength > PipeConstants.BUFFER_SIZE) continue;

                // Mesaj içeriğini oku
                var messageBuffer = new byte[messageLength];
                bytesRead = await pipe.ReadAsync(messageBuffer, ct);
                if (bytesRead < messageLength) continue;

                var json = Encoding.UTF8.GetString(messageBuffer, 0, bytesRead);
                var message = JsonSerializer.Deserialize<PipeMessage>(json);

                if (message != null)
                {
                    Log.Debug("Pipe mesajı alındı: {Type}", message.Type);
                    MessageReceived?.Invoke(message);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Pipe dinleme hatası");
                await Task.Delay(1000, ct); // Hata sonrası kısa bekleme
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
