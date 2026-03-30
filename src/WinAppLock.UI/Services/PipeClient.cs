using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using WinAppLock.Core.IPC;

namespace WinAppLock.UI.Services;

/// <summary>
/// Named Pipe istemcisi. UI tarafından Service'e mesaj gönderir
/// ve Service'ten gelen mesajları dinler.
/// </summary>
public class PipeClient : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    /// <summary>Service'ten mesaj alındığında tetiklenir.</summary>
    public event Action<PipeMessage>? MessageReceived;

    /// <summary>
    /// Service'ten gelen mesajları dinlemeye başlar.
    /// </summary>
    public void StartListening()
    {
        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenLoop(_cts.Token));
    }

    /// <summary>
    /// Service'e mesaj gönderir.
    /// </summary>
    /// <param name="message">Gönderilecek mesaj</param>
    public void SendToService(PipeMessage message)
    {
        Task.Run(() =>
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeConstants.UI_TO_SERVICE_PIPE, PipeDirection.Out);
                pipe.Connect(PipeConstants.CONNECTION_TIMEOUT_MS);

                var json = JsonSerializer.Serialize(message);
                var bytes = Encoding.UTF8.GetBytes(json);

                pipe.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
                pipe.Write(bytes, 0, bytes.Length);
                pipe.Flush();
            }
            catch (Exception)
            {
                // Service çalışmıyor olabilir — sessizce geç
            }
        });
    }

    /// <summary>AUTH_SUCCESS mesajı gönderir (process devam ettirilmeli).</summary>
    public void SendAuthSuccess(int processId, string? appName = null)
    {
        SendToService(new PipeMessage
        {
            Type = PipeMessageType.AuthSuccess,
            ProcessId = processId,
            AppName = appName
        });
    }

    /// <summary>AUTH_CANCELLED mesajı gönderir (process sonlandırılmalı).</summary>
    public void SendAuthCancelled(int processId)
    {
        SendToService(new PipeMessage
        {
            Type = PipeMessageType.AuthCancelled,
            ProcessId = processId
        });
    }

    /// <summary>Uygulama eklendiğini bildirir.</summary>
    public void SendAppAdded() =>
        SendToService(new PipeMessage { Type = PipeMessageType.AppAdded });

    /// <summary>Uygulama kaldırıldığını bildirir.</summary>
    public void SendAppRemoved() =>
        SendToService(new PipeMessage { Type = PipeMessageType.AppRemoved });

    /// <summary>Uygulama toggle edildiğini bildirir.</summary>
    public void SendAppToggled() =>
        SendToService(new PipeMessage { Type = PipeMessageType.AppToggled });

    /// <summary>Tümünü kilitle komutunu gönderir.</summary>
    public void SendLockAll() =>
        SendToService(new PipeMessage { Type = PipeMessageType.LockAll });

    /// <summary>Ayarlar güncellendiğini bildirir.</summary>
    public void SendSettingsUpdated() =>
        SendToService(new PipeMessage { Type = PipeMessageType.SettingsUpdated });

    /// <summary>Service'ten gelen mesajları sürekli dinler.</summary>
    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    PipeConstants.SERVICE_TO_UI_PIPE,
                    PipeDirection.In, 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct);

                var lengthBuffer = new byte[4];
                var bytesRead = await pipe.ReadAsync(lengthBuffer, ct);
                if (bytesRead < 4) continue;

                var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                if (messageLength <= 0 || messageLength > PipeConstants.BUFFER_SIZE) continue;

                var messageBuffer = new byte[messageLength];
                bytesRead = await pipe.ReadAsync(messageBuffer, ct);
                if (bytesRead < messageLength) continue;

                var json = Encoding.UTF8.GetString(messageBuffer, 0, bytesRead);
                var message = JsonSerializer.Deserialize<PipeMessage>(json);

                if (message != null)
                {
                    MessageReceived?.Invoke(message);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                await Task.Delay(1000, ct);
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
