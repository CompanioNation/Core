using System.Security.Cryptography;

namespace CompanioNationAPI;

/// <summary>
/// Provides password hashing and verification using PBKDF2.
/// Supports incremental migration from plaintext passwords to hashed passwords,
/// and future upgrades to stronger hash algorithms via versioning.
/// </summary>
internal static class PasswordHasher
{
    // Version 1: PBKDF2-SHA256, 100,000 iterations, 16-byte salt, 32-byte hash
    private const int V1SaltSize = 16;
    private const int V1HashSize = 32;
    private const int V1Iterations = 100_000;

    internal const int CurrentVersion = 1;

    /// <summary>
    /// Hashes a password using the current algorithm version.
    /// </summary>
    internal static (string hash, int version) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(V1SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, V1Iterations, HashAlgorithmName.SHA256, V1HashSize);

        var result = new byte[V1SaltSize + V1HashSize];
        salt.CopyTo(result, 0);
        hash.CopyTo(result, V1SaltSize);

        return (Convert.ToBase64String(result), CurrentVersion);
    }

    /// <summary>
    /// Verifies a password against a stored hash using the specified algorithm version.
    /// Returns false for unknown versions, allowing safe incremental upgrades.
    /// </summary>
    internal static bool VerifyPassword(string password, string storedHash, int version)
    {
        return version switch
        {
            1 => VerifyV1(password, storedHash),
            _ => false
        };
    }

    private static bool VerifyV1(string password, string storedHash)
    {
        byte[] hashBytes;
        try
        {
            hashBytes = Convert.FromBase64String(storedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        if (hashBytes.Length != V1SaltSize + V1HashSize) return false;

        var salt = hashBytes.AsSpan(0, V1SaltSize);
        var expectedHash = hashBytes.AsSpan(V1SaltSize, V1HashSize);

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, V1Iterations, HashAlgorithmName.SHA256, V1HashSize);
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}
