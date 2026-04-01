using Microsoft.Win32;
using Serilog;
using System.Security.AccessControl;
using System.Security.Principal;
using WinAppLock.Core.Models;

namespace WinAppLock.Core.Registry;

/// <summary>
/// Windows IFEO (Image File Execution Options) registry anahtarlarını yönetir.
/// Kilitli uygulamalar için Debugger değeri yazarak, uygulama başlatıldığında
/// Gatekeeper.exe'nin devreye girmesini sağlar.
///
/// Registry yolu: HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{exe_name}
/// Değer: Debugger = "C:\ProgramData\WinAppLock\WinAppLock.Gatekeeper.exe"
///
/// DİKKAT: HKLM altına yazma yetkisi gerektirir (SYSTEM veya Admin).
/// </summary>
public static class IfeoRegistrar
{
    private const string IFEO_BASE_PATH = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

    /// <summary>WinAppLock'un kendi exe'leri — bunlara IFEO kaydı yazılmaz (sonsuz döngü engeli).</summary>
    private static readonly HashSet<string> PROTECTED_EXECUTABLES = new(StringComparer.OrdinalIgnoreCase)
    {
        "WinAppLock.UI.exe",
        "WinAppLock.Service.exe",
        "WinAppLock.Gatekeeper.exe"
    };

    /// <summary>
    /// Belirtilen exe için IFEO Debugger kaydı yazar.
    /// Bu kayıt sayesinde exe başlatıldığında Windows, Gatekeeper'ı devreye sokar.
    /// </summary>
    /// <param name="exeName">Çalıştırılabilir dosyanın adı (ör: "chrome.exe"). Sadece dosya adı, yol değil.</param>
    /// <param name="gatekeeperPath">Gatekeeper.exe'nin tam dosya yolu.</param>
    /// <returns>İşlem başarılı ise true, hata veya koruma ihlali ise false.</returns>
    public static bool RegisterApp(string exeName, string gatekeeperPath)
    {
        if (string.IsNullOrWhiteSpace(exeName))
        {
            Log.Warning("[IFEO] Boş exe adı ile kayıt denemesi reddedildi.");
            return false;
        }

        if (PROTECTED_EXECUTABLES.Contains(exeName))
        {
            Log.Warning("[IFEO] Korumalı exe ({ExeName}) için kayıt denemesi reddedildi — sonsuz döngü engeli.", exeName);
            return false;
        }

        try
        {
            // RegistryKeyPermissionCheck ayarı ile okuma/yazma (ve dolayısıyla ACL atama) haklarını istiyoruz
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                $@"{IFEO_BASE_PATH}\{exeName}", 
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryOptions.None);
                
            if (key == null) return false;

            key.SetValue("Debugger", $"\"{gatekeeperPath}\"", RegistryValueKind.String);

            // ACL Korumasını Aktifleştir
            ApplyAclProtection(key, exeName);

            Log.Information("[IFEO] Kayıt yazıldı ve korumaya alındı: {ExeName} → {GatekeeperPath}", exeName, gatekeeperPath);
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error(ex, "[IFEO] Yetki hatası — HKLM altına yazılamadı: {ExeName}", exeName);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[IFEO] Kayıt yazma hatası: {ExeName}", exeName);
            return false;
        }
    }

    /// <summary>
    /// Belirtilen exe için IFEO Debugger kaydını kaldırır.
    /// Uygulama artık doğrudan başlatılabilir hale gelir.
    /// </summary>
    /// <param name="exeName">Çalıştırılabilir dosyanın adı (ör: "chrome.exe").</param>
    /// <returns>İşlem başarılı ise true.</returns>
    public static bool UnregisterApp(string exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName)) return false;

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"{IFEO_BASE_PATH}\{exeName}", writable: true);
            if (key == null)
            {
                Log.Debug("[IFEO] Zaten kayıt yok: {ExeName}", exeName);
                return true;
            }

            key.DeleteValue("Debugger", throwOnMissingValue: false);

            // Alt anahtar boşsa temizle (başka değer yoksa)
            if (key.ValueCount == 0 && key.SubKeyCount == 0)
            {
                Microsoft.Win32.Registry.LocalMachine.DeleteSubKey($@"{IFEO_BASE_PATH}\{exeName}", throwOnMissingSubKey: false);
            }

            Log.Information("[IFEO] Kayıt silindi: {ExeName}", exeName);
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error(ex, "[IFEO] Yetki hatası — HKLM kaydı silinemedi: {ExeName}", exeName);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[IFEO] Kayıt silme hatası: {ExeName}", exeName);
            return false;
        }
    }

    /// <summary>
    /// Geçici olarak IFEO kaydını kaldırır. Gatekeeper'ın orijinal uygulamayı başlatabilmesi için
    /// sonsuz döngüyü kırmak amacıyla kullanılır.
    /// İşlem bitince <see cref="RestoreRegistration"/> ile geri eklenir.
    /// </summary>
    /// <param name="exeName">Geçici olarak kaydı kaldırılacak exe adı.</param>
    /// <param name="gatekeeperPath">Geri ekleme için Gatekeeper yolu.</param>
    /// <returns>Kayıt kaldırıldıysa true (geri ekleme gerekir), zaten yoksa false.</returns>
    public static bool TemporarilyUnregister(string exeName, string gatekeeperPath)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"{IFEO_BASE_PATH}\{exeName}", writable: true);
            if (key?.GetValue("Debugger") == null) return false;

            key.DeleteValue("Debugger", throwOnMissingValue: false);
            Log.Debug("[IFEO] Geçici kayıt kaldırma: {ExeName}", exeName);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[IFEO] Geçici kayıt kaldırma hatası: {ExeName}", exeName);
            return false;
        }
    }

    /// <summary>
    /// Geçici olarak kaldırılan IFEO kaydını geri ekler.
    /// </summary>
    /// <param name="exeName">Geri kaydedilecek exe adı.</param>
    /// <param name="gatekeeperPath">Gatekeeper.exe tam yolu.</param>
    public static void RestoreRegistration(string exeName, string gatekeeperPath)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                $@"{IFEO_BASE_PATH}\{exeName}", 
                RegistryKeyPermissionCheck.ReadWriteSubTree, 
                RegistryOptions.None);
                
            if (key == null) return;
            
            key.SetValue("Debugger", $"\"{gatekeeperPath}\"", RegistryValueKind.String);
            
            // Geri yüklemelerde de ACL korumasını aktifleştir
            ApplyAclProtection(key, exeName);
            
            Log.Debug("[IFEO] Kayıt geri eklendi ve korumaya alındı: {ExeName}", exeName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[IFEO] Kayıt geri ekleme BAŞARISIZ: {ExeName} — Uygulama korumasız kalabilir!", exeName);
        }
    }

    /// <summary>
    /// Belirtilen exe için IFEO Debugger kaydının mevcut olup olmadığını kontrol eder.
    /// </summary>
    /// <param name="exeName">Kontrol edilecek exe adı.</param>
    /// <returns>Kayıt varsa true.</returns>
    public static bool IsRegistered(string exeName)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"{IFEO_BASE_PATH}\{exeName}");
            return key?.GetValue("Debugger") != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Veritabanındaki kilitli uygulama listesiyle IFEO kayıtlarını senkronize eder.
    /// Eksik kayıtları ekler, fazla kayıtları siler.
    /// Service başlangıcında ve uygulama değişikliklerinde çağrılır.
    /// </summary>
    /// <param name="enabledApps">Aktif kilitli uygulama listesi.</param>
    /// <param name="gatekeeperPath">Gatekeeper.exe tam yolu.</param>
    public static void SyncRegistrations(List<LockedApp> enabledApps, string gatekeeperPath)
    {
        var expectedExeNames = enabledApps
            .Select(a => a.Identity.ExecutableName)
            .Where(n => !string.IsNullOrWhiteSpace(n) && !PROTECTED_EXECUTABLES.Contains(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Eksik IFEO kayıtlarını ekle
        foreach (var exeName in expectedExeNames)
        {
            if (!IsRegistered(exeName))
            {
                RegisterApp(exeName, gatekeeperPath);
            }
        }

        // Fazla/eski IFEO kayıtlarını temizle (bizim Gatekeeper'a işaret edenler)
        try
        {
            using var ifeoRoot = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(IFEO_BASE_PATH);
            if (ifeoRoot == null) return;

            foreach (var subKeyName in ifeoRoot.GetSubKeyNames())
            {
                if (PROTECTED_EXECUTABLES.Contains(subKeyName)) continue;

                using var subKey = ifeoRoot.OpenSubKey(subKeyName);
                var debuggerValue = subKey?.GetValue("Debugger")?.ToString();

                if (debuggerValue != null && debuggerValue.Contains("WinAppLock.Gatekeeper", StringComparison.OrdinalIgnoreCase))
                {
                    if (!expectedExeNames.Contains(subKeyName))
                    {
                        UnregisterApp(subKeyName);
                        Log.Information("[IFEO] Senkronizasyon: artık kilitli olmayan {ExeName} kaydı silindi.", subKeyName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[IFEO] Senkronizasyon sırasında hata oluştu.");
        }

        Log.Information("[IFEO] Senkronizasyon tamamlandı. {Count} uygulama korunuyor.", expectedExeNames.Count);
    }

    /// <summary>
    /// Tüm WinAppLock IFEO kayıtlarını temizler.
    /// Service durdurulduğunda veya kaldırma işleminde çağrılır.
    /// </summary>
    /// <param name="gatekeeperPath">Temizlenecek Gatekeeper yolu (doğrulama için).</param>
    public static void ClearAllRegistrations(string gatekeeperPath)
    {
        try
        {
            using var ifeoRoot = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(IFEO_BASE_PATH);
            if (ifeoRoot == null) return;

            foreach (var subKeyName in ifeoRoot.GetSubKeyNames())
            {
                using var subKey = ifeoRoot.OpenSubKey(subKeyName);
                var debuggerValue = subKey?.GetValue("Debugger")?.ToString();

                if (debuggerValue != null && debuggerValue.Contains("WinAppLock.Gatekeeper", StringComparison.OrdinalIgnoreCase))
                {
                    UnregisterApp(subKeyName);
                }
            }

            Log.Information("[IFEO] Tüm WinAppLock IFEO kayıtları temizlendi.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[IFEO] Toplu kayıt temizleme hatası.");
        }
    }

    /// <summary>
    /// Oluşturulan IFEO anahtarına ACL (Access Control List) politikasını uygular.
    /// SYSTEM: Tam Denetim (Yazma/Silme)
    /// Yöneticiler ve Kullanıcılar: Salt Okunur (Silme ve Değiştirme yasak)
    /// </summary>
    private static void ApplyAclProtection(RegistryKey key, string exeName)
    {
        try
        {
            var rs = key.GetAccessControl();
            
            // Üstteki klasörlerden gelen varsayılan mirasları (İzinleri) kes at.
            rs.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            // 1. SYSTEM HESABI (WinAppLock.Service olarak çalışıyor) -> TAM YETKİ
            rs.AddAccessRule(new RegistryAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                RegistryRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            // 2. ADMINISTRATORS (Yöneticiler) -> SİLME DAHİL TAM YETKİ
            rs.AddAccessRule(new RegistryAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                RegistryRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            // 3. USERS (Tüm standart kullanıcılar) -> SADECE OKUMA YETKİSİ
            rs.AddAccessRule(new RegistryAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                RegistryRights.ReadKey,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            key.SetAccessControl(rs);
            Log.Debug("[IFEO] ACL Koruması devrede: {ExeName}", exeName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[IFEO] ACL Koruması uygulanamadı: {ExeName}. Güvenlik uyarısı!", exeName);
        }
    }
}
