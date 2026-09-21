using System.Security.Cryptography;

namespace Axumera.LicenseManager.Core.Accounts;

/// <summary>
/// Local administrator password hashing. PBKDF2-HMAC-SHA256 (600,000
/// iterations, 128-bit random salt, 256-bit key) — built into the BCL, no
/// third-party packages. The hash is stored as
/// <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;salt-b64&gt;$&lt;hash-b64&gt;</c>; the
/// plaintext password is never persisted, logged or echoed.
/// </summary>
public static class PasswordHasher
{
    public const int Iterations = 600_000;
    public const int SaltSize = 16;
    public const int KeySize = 32;
    public const string Prefix = "pbkdf2-sha256";
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password must not be empty.", nameof(password));
        }

        if (password.Length > MaxLength)
        {
            throw new ArgumentException("Password is too long.", nameof(password));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string? password, string encodedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(encodedHash))
        {
            return false;
        }

        string[] parts = encodedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out int iterations) || iterations < 1 || iterations > 10_000_000)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}