namespace JellyFederation.Shared.Diagnostics;

public static class FederationLogScopes
{
    public const string FileRequestIdKey = "FileRequestId";
    public const string CorrelationIdKey = "CorrelationId";
    public const string RoleKey = "Role";
    public const string ServerIdKey = "ServerId";
    public const string PeerServerIdKey = "PeerServerId";
    public const string TransportModeKey = "TransportMode";
    public const string SignalTypeKey = "SignalType";

    public static Dictionary<string, object?> ForFileRequest(
        Guid fileRequestId,
        string? correlationId = null,
        string? role = null,
        Guid? serverId = null,
        Guid? peerServerId = null,
        string? transportMode = null,
        string? signalType = null)
    {
        var scope = new Dictionary<string, object?>(capacity: 7)
        {
            [FileRequestIdKey] = fileRequestId
        };

        AddIfNotEmpty(scope, CorrelationIdKey, correlationId);
        AddIfNotEmpty(scope, RoleKey, role);
        AddIfHasValue(scope, ServerIdKey, serverId);
        AddIfHasValue(scope, PeerServerIdKey, peerServerId);
        AddIfNotEmpty(scope, TransportModeKey, transportMode);
        AddIfNotEmpty(scope, SignalTypeKey, signalType);

        return scope;
    }

    private static void AddIfNotEmpty(Dictionary<string, object?> scope, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            scope[key] = value;
    }

    private static void AddIfHasValue(Dictionary<string, object?> scope, string key, Guid? value)
    {
        if (value.HasValue)
            scope[key] = value.Value;
    }
}
