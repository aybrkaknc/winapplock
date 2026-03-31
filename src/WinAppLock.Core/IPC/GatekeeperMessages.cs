namespace WinAppLock.Core.IPC;

/// <summary>
/// Gatekeeper'ın Service'e gönderdiği başlatma isteği.
/// IFEO tarafından yakalanan uygulama bilgilerini taşır.
/// </summary>
public class GatekeeperRequest
{
    /// <summary>Orijinal çalıştırılabilir dosyanın tam yolu (IFEO tarafından iletilen).</summary>
    public string OriginalExePath { get; set; } = string.Empty;

    /// <summary>Orijinal komut satırı argümanları (varsa).</summary>
    public string? Arguments { get; set; }

    /// <summary>Gatekeeper process'inin kendi PID'si (callback için).</summary>
    public int GatekeeperPid { get; set; }

    /// <summary>İstek zaman damgası.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Service'in Gatekeeper'a döndüğü karar.
/// </summary>
public enum GatekeeperVerdict
{
    /// <summary>Uygulamayı başlat — şifre doğrulandı veya uygulama kilitli değil.</summary>
    Allow,

    /// <summary>Uygulamayı başlatma — şifre iptal edildi veya reddedildi.</summary>
    Deny
}

/// <summary>
/// Service'in Gatekeeper'a gönderdiği yanıt.
/// Duplex pipe üzerinden aynı bağlantıda geri yazılır.
/// </summary>
public class GatekeeperResponse
{
    /// <summary>Karar: Başlat veya Engelle.</summary>
    public GatekeeperVerdict Verdict { get; set; }

    /// <summary>Opsiyonel bilgi/hata mesajı.</summary>
    public string? Message { get; set; }
}
