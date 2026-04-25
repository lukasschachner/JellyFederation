using JellyFederation.Server.Services;
using Xunit;

namespace JellyFederation.Server.Tests;

public sealed class ApiKeyServiceTests
{
    [Fact]
    public void Hash_Returns_Verifiable_NonSecret_Material()
    {
        var apiKey = ApiKeyService.Generate();

        var hash = ApiKeyService.Hash(apiKey);
        var fingerprint = ApiKeyService.GetFingerprint(apiKey);

        Assert.NotEqual(apiKey, hash);
        Assert.DoesNotContain(apiKey, hash, StringComparison.Ordinal);
        Assert.NotEqual(apiKey, fingerprint);
        Assert.DoesNotContain(apiKey, fingerprint, StringComparison.Ordinal);
        Assert.True(ApiKeyService.Verify(apiKey, hash));
    }

    [Fact]
    public void Verify_Rejects_Wrong_Empty_And_Malformed_Keys()
    {
        var apiKey = ApiKeyService.Generate();
        var hash = ApiKeyService.Hash(apiKey);

        Assert.False(ApiKeyService.Verify(ApiKeyService.Generate(), hash));
        Assert.False(ApiKeyService.Verify(string.Empty, hash));
        Assert.False(ApiKeyService.Verify(apiKey, string.Empty));
        Assert.False(ApiKeyService.Verify(apiKey, "not-a-supported-hash"));
    }

    [Fact]
    public void Fingerprint_Is_Stable_And_Bounded()
    {
        var apiKey = ApiKeyService.Generate();

        var first = ApiKeyService.GetFingerprint(apiKey);
        var second = ApiKeyService.GetFingerprint(apiKey);

        Assert.Equal(first, second);
        Assert.StartsWith("akfp_", first, StringComparison.Ordinal);
        Assert.True(first.Length <= 32);
    }
}
