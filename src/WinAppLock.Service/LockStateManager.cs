using System.Collections.Concurrent;
using Serilog;
using WinAppLock.Core.Models;
using System.Diagnostics;
using WinAppLock.Core.Identification;
using System.Linq;

namespace WinAppLock.Service;

/// <summary>
/// Sistemdeki süreçleri zombi PID'lerden ayırmak için oluşturulma zamanını da tutan veri yapısı.
/// </summary>
public record ProcessNode(int ProcessId, int ParentProcessId, string ProcessName, string? ExecutablePath, DateTime CreationTime);

/// <summary>
/// Aynı kilit havuzuna (Aileye) dahil olan tüm süreçleri tutan ağaç yapısı.
/// Eğer kilit açıldıysa (IsUnlocked=true), bu ağaca giren hiç kimse şifre sormaz.
/// </summary>
public class LockTree
{
    public LockedApp RootApp { get; init; } = null!;
    public bool IsUnlocked { get; set; } = false;
    public Timer? RelockTimer { get; set; }
    public ConcurrentDictionary<int, ProcessNode> Nodes { get; } = new();

    public void AddNode(ProcessNode node) => Nodes[node.ProcessId] = node;
    public void RemoveNode(int processId) => Nodes.TryRemove(processId, out _);
    public bool IsEmpty => Nodes.IsEmpty;
}

public class LockStateManager
{
    // Orijinal App ID (LockedApp.Id) -> O Uygulamanın Aktif Ağacı
    private readonly ConcurrentDictionary<int, LockTree> _lockTrees = new();
    private readonly ConcurrentDictionary<int, DateTime> _lastTriggeredUi = new();
    private readonly object _syncRoot = new object();

    /// <summary>
    /// Yeni yakalanan bir sürecin herhangi bir ağaca ait olup olmadığını veya yeni bir kilit başlatıp başlatmayacağını belirler.
    /// Dönen Tuple: (ShouldSuspend, ShouldTriggerUi, MatchedAppName)
    /// </summary>
    public (bool ShouldSuspend, bool ShouldTriggerUi, string? TriggerName) ProcessNewArrivedNode(ProcessNode node, List<LockedApp> activeLockedApps)
    {
        lock (_syncRoot)
        {
            // 1. Ağaç Taraması: Bu süreç hali hazırda var olan bir ağacın parçası mı?
            foreach (var treeKp in _lockTrees)
            {
                var tree = treeKp.Value;

                // Parent'ımız bu ağaçta mı?
                bool isParentInTree = tree.Nodes.ContainsKey(node.ParentProcessId);

                // Kendimiz klasörde miyiz veya imzamız bu ağacın efendisine ait mi?
                bool isSelfMatch = false;
                if (!string.IsNullOrEmpty(node.ExecutablePath))
                {
                    try
                    {
                        var identity = AppIdentifier.CreateIdentity(node.ExecutablePath);
                        isSelfMatch = AppIdentifier.IsMatch(identity, tree.RootApp);
                    }
                    catch (Exception) { /* Ignored */ }
                }

                // KURAL 1 & KURAL 2 (Overreach Filtresi ve Updater'ları Koruma):
                // isSelfMatch: İmzası veya klasörü tutuyor, kesinlikle bizim ailemizden (Babasız-Öksüz olsa bile).
                // isParentInTree: Babası ağaçtan fırlattı, peki biz kimi çalıştırıyor?
                bool isTemporaryTool = node.ExecutablePath != null && 
                                      (node.ExecutablePath.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase) || 
                                       node.ExecutablePath.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase));

                if (isSelfMatch || (isParentInTree && isTemporaryTool))
                {
                    // Ağaca aitsin!
                    tree.AddNode(node);
                    Log.Information("[Tree] Düğüm Eklendi: {App} (PID: {PID}) -> Tree: {TreeName}", node.ProcessName, node.ProcessId, tree.RootApp.DisplayName);

                    if (tree.IsUnlocked)
                    {
                        // Şifre önceden girilmiş, ağaç serbest
                        return (false, false, null);
                    }
                    else
                    {
                        // Ağaç tutsak, bu süreç de askıya alınmalı
                        bool triggerUi = CheckDebounce(tree.RootApp.Id);
                        // KURAL 3: UI ekranında o anki yan programın adı gözüksün (node.ProcessName)
                        return (true, triggerUi, node.ProcessName); 
                    }
                }
                
                // NOT: Parent içerde ama çocuk başka yerde ve isTemporary değilse -> OVERREACH. (Örn: Steam'den çıkan Baldurs Gate). Aileye katılmıyor, yola devam.
            }

            // 2. Hiçbir mevcut ağaca uymadık. Peki kurucu bir süreç miyiz?
            foreach (var lockedApp in activeLockedApps)
            {
                if (!string.IsNullOrEmpty(node.ExecutablePath))
                {
                    try
                    {
                        var identity = AppIdentifier.CreateIdentity(node.ExecutablePath);
                        if (AppIdentifier.IsMatch(identity, lockedApp))
                        {
                            var newTree = new LockTree { RootApp = lockedApp };
                            newTree.AddNode(node);
                            _lockTrees[lockedApp.Id] = newTree;

                            Log.Information("[Tree] Yeni Kilit Ağacı Kuruldu: PID {PID}, Root: {App}", node.ProcessId, lockedApp.DisplayName);

                            return (true, CheckDebounce(lockedApp.Id), node.ProcessName); 
                        }
                    }
                    catch (Exception) { /* Ignored */ }
                }
                // Fallback (Sadece path alınamazsa isimle eşleştirme)
                else if (string.Equals(lockedApp.Identity.ExecutableName, node.ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                     var newTree = new LockTree { RootApp = lockedApp };
                     newTree.AddNode(node);
                     _lockTrees[lockedApp.Id] = newTree;
                     Log.Information("[Tree] Yeni Kilit Ağacı Kuruldu (Fallback): PID {PID}, Root: {App}", node.ProcessId, lockedApp.DisplayName);
                     return (true, CheckDebounce(lockedApp.Id), node.ProcessName);
                }
            }

            // Kilit listesinde yokuz, ailelere de uymuyoruz. Özgürüz.
            return (false, false, null);
        }
    }

    private bool CheckDebounce(int appId)
    {
        if (_lastTriggeredUi.TryGetValue(appId, out var lastTrigger) && (DateTime.UtcNow - lastTrigger).TotalSeconds < 2)
            return false;
        _lastTriggeredUi[appId] = DateTime.UtcNow;
        return true;
    }

    public void OnAuthSuccess(int processId)
    {
        lock (_syncRoot)
        {
            var targetTree = _lockTrees.Values.FirstOrDefault(t => t.Nodes.ContainsKey(processId));
            if (targetTree == null)
            {
                Log.Warning("AuthSuccess: PID {PID} ait olduğu ağaç bulunamadı", processId);
                return;
            }

            targetTree.IsUnlocked = true;

            foreach (var node in targetTree.Nodes.Values)
            {
                ProcessController.ResumeProcess(node.ProcessId);
                Log.Debug("[Tree] Resume: {PID}", node.ProcessId);
            }

            if (targetTree.RootApp.RelockBehavior == RelockBehavior.TimeBased)
            {
                StartRelockTimer(targetTree);
            }

            Log.Information("Ağaç Serbest Bırakıldı: {App}, Toplam Dal Sayısı: {Count}", targetTree.RootApp.DisplayName, targetTree.Nodes.Count);
        }
    }

    public void OnAuthCancelled(int processId)
    {
        lock (_syncRoot)
        {
            var targetTreeKv = _lockTrees.FirstOrDefault(t => t.Value.Nodes.ContainsKey(processId));
            if (targetTreeKv.Value == null) return;
            
            var targetTree = targetTreeKv.Value;

            foreach (var node in targetTree.Nodes.Values)
            {
                try
                {
                    // Şifre iptal edildiyse o ağaçtaki tüm kilitli/askıda kalan uygulamaları öldürüyoruz.
                    var process = Process.GetProcessById(node.ProcessId);
                    process.Kill(entireProcessTree: true); 
                    ProcessController.ResumeProcess(node.ProcessId); // Deadlock önleme
                }
                catch { /* Kapalı process */ }
            }
            
            _lockTrees.TryRemove(targetTreeKv.Key, out _);
            Log.Information("Ağaç İmha Edildi (Şifre İptal): {App}", targetTree.RootApp.DisplayName);
        }
    }

    /// <summary>
    /// Bir süreç (PID) kapandığında olaydan haberdar olup ağaçtan düşmesini sağlar.
    /// Zombi engelleme mantığı gereği gerekirse CreationTime eklenebilir.
    /// </summary>
    public void HandleProcessExited(int processId)
    {
        lock (_syncRoot)
        {
            foreach (var kvp in _lockTrees)
            {
                var tree = kvp.Value;
                if (tree.Nodes.TryRemove(processId, out var removedNode))
                {
                    Log.Debug("[Tree] Dal Koptu (Process Exited): {App} (PID: {PID}), Kalan: {Count}", removedNode.ProcessName, processId, tree.Nodes.Count);

                    if (tree.IsEmpty && tree.RootApp.RelockBehavior == RelockBehavior.OnClose)
                    {
                        if (_lockTrees.TryRemove(kvp.Key, out _))
                        {
                            tree.RelockTimer?.Dispose();
                            Log.Information("Ağaç Katlandı (Tüm Parçalar Kapandı -> Relock): {App}", tree.RootApp.DisplayName);
                        }
                    }
                }
            }
        }
    }

    public void LockAll()
    {
        lock (_syncRoot)
        {
            foreach (var kvp in _lockTrees)
            {
                var tree = kvp.Value;
                tree.RelockTimer?.Dispose();
                tree.IsUnlocked = false; 

                foreach (var node in tree.Nodes.Values)
                {
                    if (ProcessController.IsProcessRunning(node.ProcessId))
                    {
                        ProcessController.SuspendProcess(node.ProcessId);
                    }
                }
            }
            Log.Information("Tüm ağaçlar manuel olarak kilitlendi (LockAll)");
        }
    }

    private void StartRelockTimer(LockTree tree)
    {
        tree.RelockTimer = new Timer(_ =>
        {
            Log.Information("Otomatik Zamanlı Relock Tetiklendi: {App}", tree.RootApp.DisplayName);
            lock (_syncRoot)
            {
                tree.IsUnlocked = false;
                foreach (var node in tree.Nodes.Values)
                {
                    if (ProcessController.IsProcessRunning(node.ProcessId))
                    {
                        ProcessController.SuspendProcess(node.ProcessId);
                    }
                }
            }
            tree.RelockTimer?.Dispose();
        }, null, TimeSpan.FromMinutes(tree.RootApp.RelockTimeMinutes), Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Geriye dönük uyumluluk veya Heartbeat worker için askıdaki programların listesi (Anti-Bypass).
    /// IsUnlocked = false ise tüm ağaç kilitli kabul edilir.
    /// </summary>
    public IReadOnlyDictionary<int, LockedApp> GetSuspendedProcesses()
    {
        var dict = new Dictionary<int, LockedApp>();
        lock (_syncRoot)
        {
            foreach (var tree in _lockTrees.Values)
            {
                if (!tree.IsUnlocked)
                {
                    foreach(var n in tree.Nodes.Values) 
                    {
                        dict[n.ProcessId] = tree.RootApp;
                    }
                }
            }
        }
        return dict;
    }

    /// <summary>
    /// UI'daki Gözlemciye (WindowObserver) iletilecek "Takip Edilmesi Gereken" ağaçları döner.
    /// PreventBackgroundExecution = true olan ağaçların AppId -> ProcessId Listesini sağlar.
    /// </summary>
    public Dictionary<int, List<int>> GetTreesToTrack()
    {
        var dict = new Dictionary<int, List<int>>();
        lock (_syncRoot)
        {
            foreach (var kvp in _lockTrees)
            {
                if (kvp.Value.RootApp.PreventBackgroundExecution)
                {
                    dict[kvp.Key] = kvp.Value.Nodes.Keys.ToList();
                }
            }
        }
        return dict;
    }

    /// <summary>
    /// UI Sensörü bir uygulamanın görünür penceresi kalmadığında Auth süresini iptal (Invalidate) etmek için uyarır.
    /// </summary>
    public void InvalidateSession(int appId)
    {
        lock (_syncRoot)
        {
            if (_lockTrees.TryGetValue(appId, out var tree))
            {
                Log.Information("[Sensor] UI Gözlemcisi, {App} (ID:{ID}) için görünür pencere kalmadığını bildirdi. Oturum PUSU moduna (Kilide) alınıyor.", tree.RootApp.DisplayName, appId);
                tree.IsUnlocked = false; 
            }
        }
    }

    /// <summary>
    /// UI Sensörü, gizli/pusudaki uygulamanın yeniden tepsi veya başka yolla pencere fırlattığını iletince Suspend basar.
    /// </summary>
    public void SuspendAppProcesses(int appId)
    {
        lock (_syncRoot)
        {
            if (_lockTrees.TryGetValue(appId, out var tree))
            {
                Log.Warning("[Sensor] UI Gözlemcisi pencere dirilmesini (Resurrected) yakaladı! Ağaç {App} ACİL ASKIYA ALINIYOR.", tree.RootApp.DisplayName);

                tree.IsUnlocked = false;
                foreach (var pid in tree.Nodes.Keys)
                {
                    if (ProcessController.IsProcessRunning(pid))
                        ProcessController.SuspendProcess(pid);
                }
            }
        }
    }

    /// <summary>
    /// EĞER UI (SENSÖR) Görev Yöneticisinden veya hata sebebiyle kapanırsa (Pipe Connection Lost) Servis can güvenliğini alır.
    /// </summary>
    public void ExecuteDeadManSwitch()
    {
        lock (_syncRoot)
        {
            Log.Fatal("[SECURITY] SENSÖR UYGULAMASI (UI) ÇÖKTÜ VEYA ÖLDÜRÜLDÜ!");
            Log.Fatal("[SECURITY] DEAD-MAN'S SWITCH AKTİVASYONU: TÜM AĞAÇLAR DONDURULUYOR!");

            foreach (var kvp in _lockTrees)
            {
                var tree = kvp.Value;
                tree.IsUnlocked = false;
                
                foreach (var pid in tree.Nodes.Keys)
                {
                    if (ProcessController.IsProcessRunning(pid))
                        ProcessController.SuspendProcess(pid);
                }
            }
        }
    }
}
