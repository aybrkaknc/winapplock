using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinAppLock.Core.IPC;

namespace WinAppLock.UI.Services;

/// <summary>
/// Service tarafından iletilen kilitli ve pencereli olması gereken uygulamaları saniyede bir izler.
/// Arka planda çalışmasına rağmen penceresi olmayanları yakalar ve oturumlarını düşürür.
/// </summary>
public class WindowObserverService : IDisposable
{
    private readonly PipeClient _pipeClient;
    private Dictionary<int, List<int>> _trackingList = new();
    
    // AppId -> Göze çarpan penceresi en son var mıydı? (Varsayılan = var).
    private readonly Dictionary<int, bool> _hasVisibleWindow = new();
    
    // Geçici UI kayıpları için (Örn: Chrome loading ekranı) Debounce tablosu.
    // AppId -> (Pencerenin İlk Kaybolduğu An)
    private readonly Dictionary<int, DateTime> _windowLossTimers = new();

    private CancellationTokenSource? _cts;
    private Task? _observerTask;

    public WindowObserverService(PipeClient pipeClient)
    {
        _pipeClient = pipeClient;
        _pipeClient.MessageReceived += OnMessageReceived;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _observerTask = Task.Run(() => ObserveLoop(_cts.Token));
    }

    private void OnMessageReceived(PipeMessage message)
    {
        if (message.Type == PipeMessageType.TrackingListUpdated && !string.IsNullOrEmpty(message.Payload))
        {
            try
            {
                var newList = JsonSerializer.Deserialize<Dictionary<int, List<int>>>(message.Payload);
                if (newList != null)
                {
                    lock (_trackingList)
                    {
                        _trackingList = newList;
                    }
                }
            }
            catch { /* Parsing ignore */ }
        }
    }

    private async Task ObserveLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Dictionary<int, List<int>> currentList;
            lock (_trackingList) 
            { 
                currentList = new Dictionary<int, List<int>>(_trackingList); 
            }

            foreach (var kvp in currentList)
            {
                int appId = kvp.Key;
                List<int> pids = kvp.Value;

                bool isAnyWindowVisible = CheckIfAnyWindowVisible(pids);

                if (!_hasVisibleWindow.TryGetValue(appId, out bool previouslyVisible))
                {
                    previouslyVisible = true; 
                    _hasVisibleWindow[appId] = true;
                }

                if (previouslyVisible && !isAnyWindowVisible)
                {
                    // Yeni kayboldu, Debounce başlat veya kontrol et.
                    if (!_windowLossTimers.TryGetValue(appId, out DateTime lossTime))
                    {
                        _windowLossTimers[appId] = DateTime.UtcNow;
                    }
                    else if ((DateTime.UtcNow - lossTime).TotalSeconds >= 1.5) // ~1.5 - 2 sn debounce
                    {
                        // Kesin arka planda!
                        _hasVisibleWindow[appId] = false;
                        _windowLossTimers.Remove(appId);
                        
                        _pipeClient.SendSessionInvalidated(appId);
                    }
                }
                else if (!previouslyVisible && isAnyWindowVisible)
                {
                    // Aniden dirildi! Debounce yok ANINDA kilit vur!
                    _hasVisibleWindow[appId] = true;
                    _windowLossTimers.Remove(appId);

                    _pipeClient.SendWindowResurrected(appId);
                }
                else if (previouslyVisible && isAnyWindowVisible)
                {
                    // Görünür kalmaya devam ediyor
                    _windowLossTimers.Remove(appId);
                }
            }

            await Task.Delay(1000, ct); 
        }
    }

    private bool CheckIfAnyWindowVisible(List<int> pids)
    {
        foreach (var pid in pids)
        {
            try
            {
                var process = Process.GetProcessById(pid);
                // MainWindowHandle 0 değilse UI'ı vardır
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    return true;
                }
            }
            catch
            {
                // Process ölmüş
            }
        }
        return false;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _pipeClient.MessageReceived -= OnMessageReceived;
    }
}
