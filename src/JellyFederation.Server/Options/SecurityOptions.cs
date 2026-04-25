using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace JellyFederation.Server.Options;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public string AdminToken { get; set; } = string.Empty;
    public string ApiKeyPepper { get; set; } = string.Empty;
    public bool AllowPublicServerLookup { get; set; }
    public bool AllowLegacySignalRApiKeyQuery { get; set; }
}

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}

public sealed class WebSessionOptions
{
    public const string SectionName = "Session";

    public string CookieSecurePolicy { get; set; } = "SameAsRequest";
    public string SameSite { get; set; } = "Lax";
}

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    public RateLimitPolicyOptions Registration { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60, SegmentsPerWindow = 6 };
    public RateLimitPolicyOptions SessionCreation { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60, SegmentsPerWindow = 6 };
    public RateLimitPolicyOptions FailedApiKeyAuth { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60, SegmentsPerWindow = 6 };
    public RateLimitPolicyOptions SignalRConnections { get; set; } = new() { PermitLimit = 20, WindowSeconds = 60, SegmentsPerWindow = 6 };
}

public sealed class RateLimitPolicyOptions
{
    [Range(1, 10_000)]
    public int PermitLimit { get; set; }

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; }

    [Range(1, 1_000)]
    public int SegmentsPerWindow { get; set; } = 1;

    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);
}

public sealed class SecurityOptionsValidator : IValidateOptions<SecurityOptions>
{
    private readonly IHostEnvironment _environment;

    public SecurityOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, SecurityOptions options)
    {
        if (!_environment.IsProduction())
            return ValidateOptionsResult.Success;

        return string.IsNullOrWhiteSpace(options.AdminToken)
            ? ValidateOptionsResult.Fail("Security:AdminToken is required in production.")
            : ValidateOptionsResult.Success;
    }
}

public sealed class CorsOptionsValidator : IValidateOptions<CorsOptions>
{
    private readonly IHostEnvironment _environment;

    public CorsOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, CorsOptions options)
    {
        if (!_environment.IsProduction())
            return ValidateOptionsResult.Success;

        if (options.AllowedOrigins.Length == 0)
            return ValidateOptionsResult.Fail("Cors:AllowedOrigins must contain explicit production origins.");

        var hasInsecureLocalhost = options.AllowedOrigins.Any(origin =>
            origin.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            origin.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase));

        return hasInsecureLocalhost
            ? ValidateOptionsResult.Fail("Cors:AllowedOrigins must not rely on localhost origins in production.")
            : ValidateOptionsResult.Success;
    }
}

public sealed class WebSessionOptionsValidator : IValidateOptions<WebSessionOptions>
{
    private readonly IHostEnvironment _environment;

    public WebSessionOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, WebSessionOptions options)
    {
        var failures = new List<string>();
        if (!Enum.TryParse<CookieSecurePolicy>(options.CookieSecurePolicy, ignoreCase: true, out _))
            failures.Add("Session:CookieSecurePolicy must be Always, SameAsRequest, or None.");

        if (!Enum.TryParse<SameSiteMode>(options.SameSite, ignoreCase: true, out _))
            failures.Add("Session:SameSite must be Strict, Lax, None, or Unspecified.");

        if (_environment.IsProduction() && !options.CookieSecurePolicy.Equals("Always", StringComparison.OrdinalIgnoreCase))
            failures.Add("Session:CookieSecurePolicy must be Always in production.");

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
