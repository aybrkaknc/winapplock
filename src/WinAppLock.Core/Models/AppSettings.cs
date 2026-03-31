namespace WinAppLock.Core.Models;

/// <summary>
/// Uygulama ayarlarını temsil eden model.
/// Veritabanında saklanır, kullanıcı tarafından düzenlenebilir.
/// </summary>
public class AppSettings
{
    /// <summary>Seçilen kimlik doğrulama yöntemi (PIN veya Şifre).</summary>
    public AuthMethod AuthMethod { get; set; } = AuthMethod.Pin;

    /// <summary>PIN uzunluğu (4-8 arası). Varsayılan: 6.</summary>
    public int PinLength { get; set; } = 6;

    /// <summary>Master password hash'i (Argon2id).</summary>
    public string MasterPasswordHash { get; set; } = string.Empty;

    /// <summary>Hatalı giriş deneme limiti. Varsayılan: 5.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Limit aşıldığında bekleme süresi (saniye). Varsayılan: 30.</summary>
    public int CooldownSeconds { get; set; } = 30;

    /// <summary>Arayüz dili ("tr" veya "en"). Varsayılan: "tr".</summary>
    public string Language { get; set; } = "tr";

    /// <summary>Ses efektleri aktif mi? Varsayılan: true.</summary>
    public bool SoundEnabled { get; set; } = true;

    /// <summary>Windows başlangıcında çalışsın mı? Varsayılan: true.</summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>
    /// WinAppLock arayüzü inaktiflik zaman aşımı (saniye).
    /// Bu süre boyunca işlem yapılmazsa UI tekrar şifre sorar.
    /// Varsayılan: 30 saniye.
    /// </summary>
    public int UiTimeoutSeconds { get; set; } = 30;

    /// <summary>Global kısayol tuşu (Tümünü Kilitle). Varsayılan: "Ctrl+Alt+L".</summary>
    public string GlobalHotkey { get; set; } = "Ctrl+Alt+L";

    /// <summary>İlk kurulum tamamlandı mı?</summary>
    public bool SetupCompleted { get; set; } = false;

    /// <summary>Recovery Key hash'i (kurtarma anahtarı doğrulaması için).</summary>
    public string RecoveryKeyHash { get; set; } = string.Empty;

    /// <summary>Güvenlik sorusu metni (opsiyonel).</summary>
    public string? SecurityQuestion { get; set; }

    /// <summary>Güvenlik sorusu cevabının hash'i (opsiyonel).</summary>
    public string? SecurityAnswerHash { get; set; }
}
