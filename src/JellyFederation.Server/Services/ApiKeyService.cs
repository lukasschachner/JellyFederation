using System.Security.Cryptography;
using System.Text;

namespace JellyFederation.Server.Services;

public static class ApiKeyService
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Pbkdf2Iterations = 210_000;
    private const string HashPrefix = "akv1";
    private const string FingerprintPrefix = "akfp_";

    public static string Generate()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static string Hash(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        Span<byte> salt = stackalloc byte[SaltSizeBytes];
        RandomNumberGenerator.Fill(salt);

        Span<byte> hash = stackalloc byte[HashSizeBytes];
        Rfc2898DeriveBytes.Pbkdf2(
            apiKey,
            salt,
            hash,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256);

        return string.Join('.', HashPrefix, Pbkdf2Iterations.ToString(), Base64UrlEncode(salt), Base64UrlEncode(hash));
    }

    public static string GetFingerprint(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(apiKey), digest);
        return FingerprintPrefix + Base64UrlEncode(digest[..12]);
    }

    public static bool Verify(string apiKey, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(storedHash))
            return false;

        var parts = storedHash.Split('.', StringSplitOptions.TrimEntries);
        if (parts is not [HashPrefix, var iterationsValue, var saltValue, var hashValue] ||
            !int.TryParse(iterationsValue, out var iterations) ||
            iterations <= 0)
            return false;

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Base64UrlDecode(saltValue);
            expectedHash = Base64UrlDecode(hashValue);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length != SaltSizeBytes || expectedHash.Length != HashSizeBytes)
            return false;

        Span<byte> actualHash = stackalloc byte[HashSizeBytes];
        Rfc2898DeriveBytes.Pbkdf2(
            apiKey,
            salt,
            actualHash,
            iterations,
            HashAlgorithmName.SHA256);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes)
        .Replace('+', '-')
        .Replace('/', '_')
        .TrimEnd('=');

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        var padding = (4 - base64.Length % 4) % 4;
        return Convert.FromBase64String(base64.PadRight(base64.Length + padding, '='));
    }
}
