using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace WinAppLock.Core.Security;

/// <summary>
/// Argon2id algoritması ile şifre hash'leme ve doğrulama.
/// Plain text şifre asla saklanmaz. Argon2id, memory-hard olması
/// sayesinde GPU tabanlı brute-force saldırılarına dayanıklıdır.
/// 
/// Hash formatı: "$argon2id$salt_base64$hash_base64"
/// </summary>
public static class PasswordHasher
{
    private const int SALT_SIZE = 16;
    private const int HASH_SIZE = 32;
    private const int DEGREE_OF_PARALLELISM = 4;
    private const int MEMORY_SIZE_KB = 65536; // 64 MB
    private const int ITERATIONS = 3;

    /// <summary>
    /// Verilen şifre/PIN için rastgele salt ile Argon2id hash oluşturur.
    /// </summary>
    /// <param name="password">Hash'lenecek şifre veya PIN</param>
    /// <returns>
    /// "$argon2id$[salt_base64]$[hash_base64]" formatında hash string'i
    /// </returns>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SALT_SIZE);
        var hash = ComputeHash(password, salt);

        return $"$argon2id${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Girilen şifreyi saklanan hash ile karşılaştırır.
    /// </summary>
    /// <param name="password">Kullanıcının girdiği şifre</param>
    /// <param name="storedHash">Veritabanında saklanan hash string'i</param>
    /// <returns>Şifre doğru ise true, yanlış ise false</returns>
    public static bool Verify(string password, string storedHash)
    {
        try
        {
            var parts = storedHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || parts[0] != "argon2id")
                return false;

            var salt = Convert.FromBase64String(parts[1]);
            var expectedHash = Convert.FromBase64String(parts[2]);
            var computedHash = ComputeHash(password, salt);

            return CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Argon2id hash hesaplaması yapar.
    /// </summary>
    /// <param name="password">Hash'lenecek metin</param>
    /// <param name="salt">Rastgele salt değeri</param>
    /// <returns>Hash byte dizisi</returns>
    private static byte[] ComputeHash(string password, byte[] salt)
    {
        var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = DEGREE_OF_PARALLELISM,
            MemorySize = MEMORY_SIZE_KB,
            Iterations = ITERATIONS
        };

        return argon2.GetBytes(HASH_SIZE);
    }
}
