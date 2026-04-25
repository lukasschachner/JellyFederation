namespace JellyFederation.Shared.Security;

public static class SecurityFailureCodes
{
    public const string ConfigurationInvalid = "security.configuration_invalid";
    public const string RegistrationAdminTokenRequired = "registration.admin_token_required";
    public const string ApiKeyInvalid = "auth.api_key_invalid";
    public const string ApiKeyThrottled = "auth.api_key_throttled";
    public const string SessionCreateThrottled = "session.create_throttled";
    public const string SignalRConnectThrottled = "signalr.connect_throttled";
    public const string ServerLookupUnauthorized = "server.lookup_unauthorized";
    public const string MediaSyncInvalidMetadata = "media_sync.invalid_metadata";
}

public static class SecurityTelemetryTags
{
    public const string AuthOutcome = "security.auth.outcome";
    public const string CredentialFingerprint = "security.credential.fingerprint";
    public const string ThrottlePolicy = "security.throttle.policy";
    public const string ValidationOutcome = "security.validation.outcome";
}
