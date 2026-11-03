using System.Security.Cryptography;

namespace StagePass.Api.Security;

/// <summary>PBKDF2-SHA256 password hashing using only built-in System.Security.Cryptography.</summary>
public static class PasswordHasher
{
    private const int SaltSize = 32;
    private const int HashSize = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algo = HashAlgorithmName.SHA256;

    public static (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algo, HashSize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string password, string hash, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var hashBytes = Convert.FromBase64String(hash);
        var computed = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, Algo, HashSize);
        return CryptographicOperations.FixedTimeEquals(computed, hashBytes);
    }
}
