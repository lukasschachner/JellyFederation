namespace JellyFederation.Shared.Telemetry;

public static class TelemetryRedaction
{
    private static readonly string[] SensitiveKeyFragments =
    [
        "api",
        "key",
        "token",
        "secret",
        "password",
        "userid",
        "user_id",
        "email"
    ];

    private static readonly HashSet<string> AllowedMetricDimensions =
    [
        "operation",
        "component",
        "outcome",
        "peer_server",
        "release"
    ];

    public static string? Redact(string? key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var k = key?.ToLowerInvariant() ?? string.Empty;
        if (SensitiveKeyFragments.Any(k.Contains))
            return "[REDACTED]";

        return value.Length > 256 ? value[..256] : value;
    }

    public static string SanitizeErrorMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        var sanitized = message;
        if (sanitized.Length > 256)
            sanitized = sanitized[..256];

        foreach (var fragment in SensitiveKeyFragments)
            sanitized = sanitized.Replace(fragment, "[REDACTED]", StringComparison.OrdinalIgnoreCase);

        return sanitized;
    }

    public static KeyValuePair<string, object?>[] BuildMetricTags(
        string operation,
        string component,
        string? outcome = null,
        string? peerServer = null,
        string? release = null)
    {
        var values = new List<KeyValuePair<string, object?>>
        {
            new("operation", SafeDimensionValue("operation", operation)),
            new("component", SafeDimensionValue("component", component))
        };

        if (!string.IsNullOrWhiteSpace(outcome))
            values.Add(new("outcome", SafeDimensionValue("outcome", outcome)));
        if (!string.IsNullOrWhiteSpace(peerServer))
            values.Add(new("peer_server", SafeDimensionValue("peer_server", peerServer)));
        if (!string.IsNullOrWhiteSpace(release))
            values.Add(new("release", SafeDimensionValue("release", release)));

        return values.ToArray();
    }

    private static string SafeDimensionValue(string key, string value)
    {
        if (!AllowedMetricDimensions.Contains(key))
            return "redacted";

        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var trimmed = value.Trim();
        if (trimmed.Length > 64)
            trimmed = trimmed[..64];

        return Redact(key, trimmed) ?? "unknown";
    }
}
