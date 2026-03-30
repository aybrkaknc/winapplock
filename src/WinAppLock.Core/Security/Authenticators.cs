using WinAppLock.Core.Models;

namespace WinAppLock.Core.Security;

/// <summary>
/// PIN tabanlı kimlik doğrulama.
/// 4-8 haneli sayısal kodları doğrular.
/// </summary>
public class PinAuthenticator : IAuthenticator
{
    private readonly int _pinLength;

    /// <summary>
    /// PinAuthenticator oluşturur.
    /// </summary>
    /// <param name="pinLength">PIN uzunluğu (4-8 arası). Varsayılan: 6</param>
    public PinAuthenticator(int pinLength = 6)
    {
        if (pinLength < 4 || pinLength > 8)
            throw new ArgumentOutOfRangeException(nameof(pinLength), "PIN uzunluğu 4 ile 8 arasında olmalıdır.");

        _pinLength = pinLength;
    }

    /// <inheritdoc />
    public AuthMethod Method => AuthMethod.Pin;

    /// <inheritdoc />
    public bool Verify(string input, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        return PasswordHasher.Verify(input, storedHash);
    }

    /// <inheritdoc />
    public string CreateHash(string input)
    {
        return PasswordHasher.Hash(input);
    }

    /// <inheritdoc />
    public bool ValidateFormat(string input, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            errorMessage = "PIN boş olamaz.";
            return false;
        }

        if (!input.All(char.IsDigit))
        {
            errorMessage = "PIN sadece rakamlardan oluşmalıdır.";
            return false;
        }

        if (input.Length != _pinLength)
        {
            errorMessage = $"PIN {_pinLength} haneli olmalıdır.";
            return false;
        }

        return true;
    }
}

/// <summary>
/// Alfanümerik şifre tabanlı kimlik doğrulama.
/// Minimum 6 karakter uzunluğunda şifreleri doğrular.
/// </summary>
public class PasswordAuthenticator : IAuthenticator
{
    private const int MIN_PASSWORD_LENGTH = 6;

    /// <inheritdoc />
    public AuthMethod Method => AuthMethod.Password;

    /// <inheritdoc />
    public bool Verify(string input, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        return PasswordHasher.Verify(input, storedHash);
    }

    /// <inheritdoc />
    public string CreateHash(string input)
    {
        return PasswordHasher.Hash(input);
    }

    /// <inheritdoc />
    public bool ValidateFormat(string input, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            errorMessage = "Şifre boş olamaz.";
            return false;
        }

        if (input.Length < MIN_PASSWORD_LENGTH)
        {
            errorMessage = $"Şifre en az {MIN_PASSWORD_LENGTH} karakter olmalıdır.";
            return false;
        }

        return true;
    }
}
