# Security Findings

Date: 2026-04-25

Scope: static review of server, plugin, shared DTOs/models, SignalR hub, EF model configuration, web client code, and dependency vulnerability reports.

## High priority

### API keys are stored and compared in plaintext

Evidence:

- `src/JellyFederation.Shared/Models/RegisteredServer.cs` stores `ApiKey` directly.
- `src/JellyFederation.Server/Filters/ApiKeyAuthFilter.cs` queries `s.ApiKey == apiKey`.
- `src/JellyFederation.Server/Hubs/FederationHub.cs` queries `s.ApiKey == apiKey`.
- `src/JellyFederation.Server/Services/WebSessionService.cs` embeds `server.ApiKey` in the protected session cookie payload.

Impact:

- Database compromise exposes all server API keys.
- Backups and database dumps become credential material.
- Raw API keys are also used in cache keys and protected cookie payloads.

Recommendations:

- Store only an API key hash, not the raw API key.
- Return the raw API key only once during registration.
- Add an `ApiKeyHash` column and migrate existing records carefully.
- Use `CryptographicOperations.FixedTimeEquals` when comparing hashes.
- Replace session-cookie payloads containing API keys with a server-side session identifier or a cookie payload containing only non-secret identifiers.
- Avoid raw secrets in memory-cache keys. Cache by hash/fingerprint.

### Registration is publicly open by default

Evidence:

- `src/JellyFederation.Server/appsettings.json` sets `"AdminToken": ""`.
- `src/JellyFederation.Server/Controllers/ServersController.cs` checks `X-Admin-Token` only when `AdminToken` is non-empty.

Impact:

- A production server deployed with defaults allows anyone to register and obtain an API key.
- This can enable resource exhaustion, spam registrations, and unauthorized federation presence.

Recommendations:

- Fail startup in production when `AdminToken` is missing or blank.
- Keep open registration only in development or in an explicit first-run setup mode.
- Consider manual approval/invitation for newly registered servers.

### Session and authentication endpoints need more brute-force protection

Evidence:

- `Program.cs` defines a rate limiter only for `register`.
- `SessionsController.Create` has no dedicated rate limiter.
- Failed API key authentication and SignalR connection attempts are not rate-limited.

Impact:

- Attackers can generate avoidable DB/cache load.
- Session creation can be brute-forced or flooded.

Recommendations:

- Add rate-limiting policies for:
  - `POST /api/sessions`
  - failed API key attempts
  - SignalR hub connection attempts
- Consider per-IP and per-credential-fingerprint throttles.
- Track and log repeated authentication failures without logging secrets.

## Medium priority

### SignalR supports API keys in query strings

Evidence:

- `FederationHub.ResolveApiKey` accepts `access_token` and legacy `apiKey` query parameters.
- `src/JellyFederation.Web/src/hooks/useSignalR.ts` uses `accessTokenFactory`.

Impact:

- Query-string credentials may appear in reverse-proxy logs, browser/network tooling, telemetry, and traces.
- `access_token` is sometimes necessary for browser SignalR/WebSockets, but `apiKey` is legacy and riskier.

Recommendations:

- Remove legacy `apiKey` query support or disable it by default.
- Prefer cookie auth for browser clients.
- Prefer `Authorization: Bearer` or `X-Api-Key` for plugin/non-browser clients.
- Ensure reverse proxies redact `access_token` from logs.

### Session cookie `Secure` depends on `request.IsHttps`

Evidence:

- `WebSessionService.CreateCookieOptions` uses `Secure = request.IsHttps`.
- `Program.cs` notes that HTTPS is terminated by Traefik.

Impact:

- If forwarded headers are not configured correctly, production may issue a non-secure session cookie.

Recommendations:

- Force `Secure = true` in production.
- Validate forwarded-header configuration in production deployments.
- Keep dynamic `request.IsHttps` behavior only for development if needed.

### Production CORS should not use development defaults

Evidence:

- `Program.cs` falls back to `http://localhost:5173` and `http://localhost:4173` when `AllowedOrigins` is absent.
- CORS allows credentials.

Impact:

- Production deployments without explicit `AllowedOrigins` still trust localhost origins.
- Credentialed CORS should be explicitly configured per deployment.

Recommendations:

- In production, require `AllowedOrigins` to be explicitly set.
- Use localhost fallback only in development.

### Public server lookup leaks metadata

Evidence:

- `ServersController.List` is protected by `ApiKeyAuthFilter`.
- `ServersController.Get(Guid id)` is not protected.

Impact:

- Anyone with a server ID can retrieve server name, owner user ID, online state, last-seen time, and media item count.

Recommendations:

- Protect `GET /api/servers/{id}` unless it is intentionally public.
- If it must be public, reduce returned fields and document the exposure.

### Remote image URLs are rendered directly in the browser

Evidence:

- `MediaItemSyncEntry.ImageUrl` accepts arbitrary strings.
- `LibraryController.Sync` stores the value directly.
- `Library.tsx` renders it with `<img src={item.imageUrl ?? undefined}>`.

Impact:

- Federated peers can cause browsers to fetch attacker-controlled URLs.
- This may leak browser IP, user agent, referrer/timing metadata, and can be used for tracking.

Recommendations:

- Validate allowed image URL schemes and lengths.
- Allow only `https:` in production plus `data:image/jpeg;base64` if embedded previews remain supported.
- Consider proxying/caching images through the federation server.
- Add a Content Security Policy that constrains `img-src`.

### Media sync accepts large/unbounded string fields

Evidence:

- `SyncMediaRequest.Items` allows up to 10,000 items.
- `Overview` and `ImageUrl` have no `StringLength` validation.
- `ServerLimits:MaxRequestBodySizeMb` defaults to 300 in appsettings.

Impact:

- Authorized or compromised servers can cause memory pressure and database bloat.
- Large data URLs can inflate request bodies and storage.

Recommendations:

- Add explicit string-length validation for `Overview` and `ImageUrl`.
- Validate `FileSizeBytes >= 0`.
- Use endpoint-specific request-size limits for sync.
- Consider smaller chunk sizes for image-preview sync.

## Hardening backlog

### Add security headers for the SPA and static files

Recommended headers:

- `Content-Security-Policy`
- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: strict-origin-when-cross-origin` or stricter
- `Permissions-Policy`
- `Strict-Transport-Security` when HTTPS is handled by the app or trusted proxy path

Candidate CSP starting point:

```http
default-src 'self';
script-src 'self';
style-src 'self' 'unsafe-inline';
img-src 'self' data: https:;
connect-src 'self' https: wss:;
object-src 'none';
base-uri 'self';
frame-ancestors 'none';
```

Tune this for the actual deployment and telemetry endpoints.

### Forwarded headers should be explicitly trusted in production

Development clears known proxy/network lists for convenience. Ensure production config lists trusted proxies so `X-Forwarded-*` headers cannot be spoofed by arbitrary clients.
