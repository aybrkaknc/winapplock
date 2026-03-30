using WinAppLock.Core.Models;

namespace WinAppLock.Core.Security;

/// <summary>
/// Kimlik doğrulama interface'i.
/// Tüm doğrulama yöntemleri (PIN, Şifre, ileride Windows Hello) bu arayüzü uygular.
/// Bu soyutlama sayesinde yeni kimlik doğrulama yöntemleri kolayca eklenebilir.
/// </summary>
public interface IAuthenticator
{
    /// <summary>Doğrulama yönteminin adı (ör: "PIN", "Password").</summary>
    AuthMethod Method { get; }

    /// <summary>
    /// Kullanıcının girişini doğrular.
    /// </summary>
    /// <param name="input">Kullanıcının girdiği değer (PIN veya şifre)</param>
    /// <param name="storedHash">Veritabanında saklanan hash değeri</param>
    /// <returns>Doğrulama sonucu</returns>
    bool Verify(string input, string storedHash);

    /// <summary>
    /// Yeni bir şifre/PIN için hash oluşturur.
    /// </summary>
    /// <param name="input">Hash'lenecek değer</param>
    /// <returns>Argon2id hash string'i</returns>
    string CreateHash(string input);

    /// <summary>
    /// Girişin format olarak geçerli olup olmadığını kontrol eder.
    /// Örnek: PIN için sadece rakam ve doğru uzunluk, şifre için minimum uzunluk.
    /// </summary>
    /// <param name="input">Doğrulanacak giriş</param>
    /// <param name="errorMessage">Hata durumunda açıklama mesajı</param>
    /// <returns>Geçerli ise true</returns>
    bool ValidateFormat(string input, out string? errorMessage);
}
