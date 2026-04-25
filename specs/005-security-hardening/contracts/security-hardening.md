# Security Hardening Contract Notes

## Changed boundaries

- Registration (`POST /api/servers/register` or current equivalent): continues to return the raw API key once on success. Persistent storage changes to hash/fingerprint only.
- API-key authentication for HTTP controllers: submitted key format should remain accepted for existing clients after migration/rotation, but comparison is against hash material.
- SignalR hub authentication: prefer `Authorization`/`X-Api-Key` for non-browser clients and cookie auth for browser clients. Legacy `apiKey` query parameter is removed or disabled by default; `access_token` may remain for browser SignalR where required.
- Web session cookie: payload contains only non-secret session or server identifiers and expiry metadata. No raw API keys.
- `GET /api/servers/{id}`: requires existing API-key or non-secret web-session authorization. The route keeps the current `ServerInfoDto` response shape for authenticated callers and returns `server.lookup_unauthorized` for unauthenticated callers. No reduced anonymous DTO is introduced for this feature.
- Library sync request validation: `Overview`, `ImageUrl`, and `FileSizeBytes` gain stricter validation. Validation failures use stable error responses.

## Compatibility rules

- Do not rename existing JSON properties unless a new DTO/route is introduced.
- Add read-side support before write-side changes where persistent data may be encountered during rolling deployments.
- Secrets must never appear in telemetry tags, logs, cache keys, exception messages, or client validation messages.
- Security config startup failures are intentionally breaking only for production deployments with insecure defaults.

## Failure outcomes to define/reuse

- `security.configuration_invalid`
- `registration.admin_token_required`
- `auth.api_key_invalid`
- `auth.api_key_throttled`
- `session.create_throttled`
- `signalr.connect_throttled`
- `server.lookup_unauthorized`
- `media_sync.invalid_metadata`
