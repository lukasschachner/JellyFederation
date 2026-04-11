using System.Security.Cryptography;

namespace JellyFederation.Server.Services;

public static class ApiKeyService
{
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
               .Replace('+', '-')
               .Replace('/', '_')
               .TrimEnd('=');
}
