using System.Security.Cryptography;

namespace WinAppLock.Core.Security;

/// <summary>
/// Şifre kurtarma yönetimi.
/// İki kurtarma yöntemi sunar:
/// 1. Recovery Key: Kurulumda üretilen tek seferlik alfanümerik anahtar
/// 2. Güvenlik Sorusu: Kullanıcının belirlediği soru ve cevap
/// 
/// Her iki yöntemin cevapları Argon2id ile hash'lenerek saklanır.
/// </summary>
public static class RecoveryManager
{
    private const string KEY_PREFIX = "WAL";
    private const int KEY_SEGMENT_COUNT = 4;
    private const int KEY_SEGMENT_LENGTH = 4;

    /// <summary>
    /// Yeni bir recovery key üretir.
    /// Format: "WAL-XXXX-XXXX-XXXX-XXXX" (alfanümerik, okunması kolay karakterler).
    /// 
    /// I, O, 0, 1 gibi karıştırılabilecek karakterler kullanılmaz.
    /// </summary>
    /// <returns>Üretilen recovery key string'i</returns>
    public static string GenerateRecoveryKey()
    {
        // Okunması kolay karakterler (I, O, 0, 1 hariç)
        const string ALLOWED_CHARS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        var segments = new string[KEY_SEGMENT_COUNT];
        for (var i = 0; i < KEY_SEGMENT_COUNT; i++)
        {
            var segment = new char[KEY_SEGMENT_LENGTH];
            for (var j = 0; j < KEY_SEGMENT_LENGTH; j++)
            {
                segment[j] = ALLOWED_CHARS[RandomNumberGenerator.GetInt32(ALLOWED_CHARS.Length)];
            }
            segments[i] = new string(segment);
        }

        return $"{KEY_PREFIX}-{string.Join("-", segments)}";
    }

    /// <summary>
    /// Recovery key'in hash'ini oluşturur (veritabanında saklamak için).
    /// Key karşılaştırma sırasında büyük/küçük harf ve tire farkını yok sayar.
    /// </summary>
    /// <param name="recoveryKey">Recovery key string'i</param>
    /// <returns>Argon2id hash</returns>
    public static string HashRecoveryKey(string recoveryKey)
    {
        var normalized = NormalizeKey(recoveryKey);
        return PasswordHasher.Hash(normalized);
    }

    /// <summary>
    /// Girilen recovery key ile saklanan hash'i karşılaştırır.
    /// </summary>
    /// <param name="inputKey">Kullanıcının girdiği key</param>
    /// <param name="storedHash">Veritabanındaki hash</param>
    /// <returns>Doğru ise true</returns>
    public static bool VerifyRecoveryKey(string inputKey, string storedHash)
    {
        var normalized = NormalizeKey(inputKey);
        return PasswordHasher.Verify(normalized, storedHash);
    }

    /// <summary>
    /// Güvenlik sorusunun cevabını hash'ler.
    /// Cevap normalize edilir (küçük harf, trim).
    /// </summary>
    /// <param name="answer">Güvenlik sorusu cevabı</param>
    /// <returns>Argon2id hash</returns>
    public static string HashSecurityAnswer(string answer)
    {
        var normalized = NormalizeAnswer(answer);
        return PasswordHasher.Hash(normalized);
    }

    /// <summary>
    /// Güvenlik sorusu cevabını doğrular.
    /// </summary>
    /// <param name="inputAnswer">Kullanıcının girdiği cevap</param>
    /// <param name="storedHash">Veritabanındaki hash</param>
    /// <returns>Doğru ise true</returns>
    public static bool VerifySecurityAnswer(string inputAnswer, string storedHash)
    {
        var normalized = NormalizeAnswer(inputAnswer);
        return PasswordHasher.Verify(normalized, storedHash);
    }

    /// <summary>
    /// Recovery key'i karşılaştırma için normalize eder.
    /// Tire ve boşlukları kaldırır, büyük harfe çevirir.
    /// </summary>
    private static string NormalizeKey(string key) =>
        key.Replace("-", "").Replace(" ", "").ToUpperInvariant().Trim();

    /// <summary>
    /// Güvenlik sorusu cevabını normalize eder.
    /// Küçük harfe çevirir ve trim uygular.
    /// </summary>
    private static string NormalizeAnswer(string answer) =>
        answer.Trim().ToLowerInvariant();
}
