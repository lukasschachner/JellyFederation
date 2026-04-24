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
        "release",
        "failure_category",
        "failure_code"
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
        string? release = null,
        string? failureCategory = null,
        string? failureCode = null)
    {
        // Pre-compute the exact array size to avoid List<T> + ToArray() allocation.
        var count = 2
                    + (string.IsNullOrWhiteSpace(outcome)        ? 0 : 1)
                    + (string.IsNullOrWhiteSpace(peerServer)     ? 0 : 1)
                    + (string.IsNullOrWhiteSpace(release)        ? 0 : 1)
                    + (string.IsNullOrWhiteSpace(failureCategory) ? 0 : 1)
                    + (string.IsNullOrWhiteSpace(failureCode)    ? 0 : 1);

        var tags = new KeyValuePair<string, object?>[count];
        var i = 0;
        tags[i++] = new("operation", SafeDimensionValue("operation", operation));
        tags[i++] = new("component", SafeDimensionValue("component", component));
        if (!string.IsNullOrWhiteSpace(outcome))        tags[i++] = new("outcome",         SafeDimensionValue("outcome",         outcome));
        if (!string.IsNullOrWhiteSpace(peerServer))     tags[i++] = new("peer_server",     SafeDimensionValue("peer_server",     peerServer));
        if (!string.IsNullOrWhiteSpace(release))        tags[i++] = new("release",          SafeDimensionValue("release",         release));
        if (!string.IsNullOrWhiteSpace(failureCategory)) tags[i++] = new("failure_category", SafeDimensionValue("failure_category", failureCategory));
        if (!string.IsNullOrWhiteSpace(failureCode))    tags[i]   = new("failure_code",     SafeDimensionValue("failure_code",    failureCode));
        return tags;
    }

    public static Models.FailureDescriptor SanitizeFailureDescriptor(Models.FailureDescriptor failure)
    {
        var details = failure.Details?
            .ToDictionary(
                entry => entry.Key,
                entry => Redact(entry.Key, entry.Value));

        return failure with
        {
            Message = SanitizeErrorMessage(failure.Message),
            Details = details
        };
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
