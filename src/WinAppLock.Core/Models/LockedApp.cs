namespace WinAppLock.Core.Models;

/// <summary>
/// Bir uygulamanın benzersiz kimliğini temsil eden sınıf.
/// Dosya adı, SHA-256 hash ve PE Header bilgilerini birlikte kullanarak
/// uygulamayı yeniden adlandırmaya karşı dayanıklı şekilde tanımlar.
/// </summary>
public class AppIdentity
{
    /// <summary>Çalıştırılabilir dosyanın adı (ör: "chrome.exe").</summary>
    public string ExecutableName { get; set; } = string.Empty;

    /// <summary>Çalıştırılabilir dosyanın tam yolu.</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Çalıştırılabilir dosyanın bulunduğu kök klasör.</summary>
    public string DirectoryPath { get; set; } = string.Empty;

    /// <summary>Dosyanın SHA-256 hash değeri (hex string).</summary>
    public string Sha256Hash { get; set; } = string.Empty;

    /// <summary>PE Header'dan okunan ürün adı (ör: "Google Chrome").</summary>
    public string? ProductName { get; set; }

    /// <summary>PE Header'dan okunan şirket adı (ör: "Google LLC").</summary>
    public string? CompanyName { get; set; }

    /// <summary>Dosya boyutu (byte cinsinden).</summary>
    public long FileSize { get; set; }

    /// <summary>Dosyanın orijinal sürüm bilgisi.</summary>
    public string? FileVersion { get; set; }
}

/// <summary>
/// Kilitli bir uygulamanın tüm bilgilerini temsil eden model.
/// Veritabanında saklanır ve hem Service hem UI tarafından kullanılır.
/// </summary>
public class LockedApp
{
    /// <summary>Benzersiz kayıt kimliği.</summary>
    public int Id { get; set; }

    /// <summary>Kullanıcının gördüğü uygulama adı (ör: "Google Chrome").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Uygulamanın tanımlama bilgileri (hash, PE header vb.).</summary>
    public AppIdentity Identity { get; set; } = new();

    /// <summary>Kilit aktif mi? (Kullanıcı toggle ile açıp kapatabilir.)</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Bu uygulamaya özel şifre hash'i.
    /// Null ise Master Password kullanılır.
    /// </summary>
    public string? CustomPasswordHash { get; set; }

    /// <summary>Otomatik tekrar kilitleme davranışı.</summary>
    public RelockBehavior RelockBehavior { get; set; } = RelockBehavior.OnClose;

    /// <summary>Uygulama arka planda (tepside veya görünmez) çalışmaya devam etmeye çalışırsa oturumu kapat/kilitle (Sensör yardımıyla).</summary>
    public bool PreventBackgroundExecution { get; set; } = true;

    /// <summary>
    /// Süre bazlı kilitleme seçildiğinde bekleme süresi (dakika).
    /// Varsayılan: 15 dakika.
    /// </summary>
    public int RelockTimeMinutes { get; set; } = 15;

    /// <summary>Uygulamanın bulunduğu klasörden fırlayan çocuk/update işlemleri kilitlemek için klasör eşleştirme (Blanket Lock).</summary>
    public bool LockChildProcesses { get; set; } = true;

    /// <summary>Uygulamanın ikonunun Base64 olarak saklanmış hali.</summary>
    public string? IconBase64 { get; set; }

    /// <summary>Kayıt oluşturulma tarihi.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
