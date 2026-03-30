namespace WinAppLock.Core.Models;

/// <summary>
/// Kilit durumunu temsil eden enum.
/// Bir uygulamanın o anki koruma ve erişim durumunu belirtir.
/// </summary>
public enum LockState
{
    /// <summary>Uygulama kilitli, erişim için şifre gerekli.</summary>
    Locked,

    /// <summary>Uygulama kilidi açık, kullanıcı erişim sağladı.</summary>
    Unlocked,

    /// <summary>Process askıya alındı, şifre ekranı gösterilmeyi bekliyor.</summary>
    Suspended,

    /// <summary>Kilit geçici olarak devre dışı (kullanıcı toggle ile kapattı).</summary>
    Disabled
}

/// <summary>
/// Otomatik tekrar kilitleme davranışını belirleyen enum.
/// </summary>
public enum RelockBehavior
{
    /// <summary>Uygulama kapatıldığında tekrar kilitlensin (Default).</summary>
    OnClose,

    /// <summary>Belirli bir süre sonra otomatik tekrar kilitlensin.</summary>
    TimeBased
}

/// <summary>
/// Kimlik doğrulama yöntemini belirleyen enum.
/// </summary>
public enum AuthMethod
{
    /// <summary>Sayısal PIN kodu ile doğrulama.</summary>
    Pin,

    /// <summary>Alfanümerik şifre ile doğrulama.</summary>
    Password
}

/// <summary>
/// Kimlik doğrulama sonucunu temsil eden sınıf.
/// Doğrulama başarılı mı, kalan deneme hakkı kaç, hata mesajı nedir gibi bilgileri taşır.
/// </summary>
public class AuthResult
{
    /// <summary>Doğrulama başarılı mı?</summary>
    public bool IsSuccess { get; init; }

    /// <summary>Kalan deneme hakkı sayısı. -1 ise sınırsız.</summary>
    public int RemainingAttempts { get; init; } = -1;

    /// <summary>Geçici bloklama durumunda kalan bekleme süresi (saniye).</summary>
    public int CooldownSeconds { get; init; }

    /// <summary>Kullanıcıya gösterilecek hata veya bilgi mesajı.</summary>
    public string? Message { get; init; }

    /// <summary>Başarılı doğrulama sonucu oluşturur.</summary>
    public static AuthResult Success() => new() { IsSuccess = true };

    /// <summary>
    /// Başarısız doğrulama sonucu oluşturur.
    /// </summary>
    /// <param name="remainingAttempts">Kalan deneme hakkı</param>
    /// <param name="message">Hata mesajı</param>
    /// <param name="cooldownSeconds">Bekleme süresi (saniye)</param>
    /// <returns>Başarısız AuthResult nesnesi</returns>
    public static AuthResult Failure(int remainingAttempts, string? message = null, int cooldownSeconds = 0) =>
        new() { IsSuccess = false, RemainingAttempts = remainingAttempts, Message = message, CooldownSeconds = cooldownSeconds };
}
