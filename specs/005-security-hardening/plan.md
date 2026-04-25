# Implementation Plan: Security Hardening Remediation

**Branch**: `005-security-hardening` | **Date**: 2026-04-25 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/005-security-hardening/spec.md`

## Summary

Remediate the security audit by enforcing production-safe configuration, hashing API keys at rest, removing raw secrets from session payloads/cache keys/telemetry, adding targeted throttling, constraining credentialed CORS and cookies, protecting or reducing public server lookup, validating untrusted media metadata, and adding baseline security headers.

## Technical Context

**Language/Version**: C# on .NET 10 for server/web/tests; shared libraries `net9.0;net10.0`; plugin remains .NET 9.  
**Primary Dependencies**: ASP.NET Core MVC, SignalR, EF Core, OpenTelemetry, Data Protection, rate limiting middleware, Jellyfin plugin APIs.  
**Storage**: EF Core with SQLite and PostgreSQL; provider-specific migrations required for `RegisteredServer` and likely media constraints.  
**Testing**: xUnit v3, ASP.NET Core integration tests, SignalR tests, EF Core/provider migration tests.  
**Target Platform**: Linux/containerized federation server behind Traefik or equivalent reverse proxy plus Jellyfin plugin runtime.  
**Project Type**: ASP.NET Core federation server + shared contracts + web frontend + plugin compatibility.  
**Performance Goals**: Auth lookups remain indexed and bounded; rate-limited attempts avoid avoidable DB/cache load; security middleware adds negligible per-request latency.  
**Constraints**: Stable federation contracts, privacy-safe telemetry, provider parity for SQLite/PostgreSQL, async/cancellable I/O, no leaked secrets in diagnostics.  
**Scale/Scope**: Registration/session/authentication/hub entry points; media sync validation; startup configuration validation; SPA/static security headers.

## Constitution Check

- **Contract-First Federation Boundaries**: PASS — changed routes/SignalR auth behavior and public DTO exposure are documented in `contracts/security-hardening.md`.
- **Result-Oriented Failure Handling**: PASS — validation/auth/throttle/config failures use stable failure codes/categories and sanitized messages.
- **Observable and Privacy-Safe Operations**: PASS — telemetry is required but must use server IDs/fingerprints only, never raw credentials.
- **Provider-Aware Persistence and Migrations**: PASS — API-key hash/fingerprint and media constraints require SQLite/PostgreSQL migrations.
- **Incremental, Tested Delivery**: PASS — P1 stories split API-key/admin-token hardening from session/throttling; P2 story covers public surfaces/metadata.
- **Platform Standards**: PASS — use ASP.NET Core options/rate limiting/Data Protection, DI extension methods, cancellation tokens, and central package management.

## Project Structure

### Documentation (this feature)

```text
specs/005-security-hardening/
├── spec.md
├── plan.md
├── contracts/security-hardening.md
└── tasks.md
```

### Source Code (repository root)

```text
src/JellyFederation.Server/                 # auth filters/hub/controllers/options/middleware/rate limits
src/JellyFederation.Shared/                 # shared DTO/failure/telemetry constants if changed
src/JellyFederation.Data/                   # EF model configuration
src/JellyFederation.Migrations.Sqlite/      # SQLite migration
src/JellyFederation.Migrations.PostgreSQL/  # PostgreSQL migration
src/JellyFederation.Web/                    # remove browser API-key storage paths if included
tests/JellyFederation.Server.Tests/         # API, SignalR, provider, config validation tests
```

**Structure Decision**: Implement production validation and auth/session/rate-limit services in `JellyFederation.Server`; keep persistent model constraints in `JellyFederation.Data`; document any wire/public DTO behavior in `contracts/`; update web frontend only where browser API-key storage or SignalR token factories are affected.

## Action Plan

### Phase 0: Design confirmation

1. Decide API-key migration/rotation strategy for existing plaintext rows.
2. Decide public server lookup policy: authenticated-only versus reduced public DTO.
3. Decide whether browser SignalR can rely on cookies in supported deployments; otherwise keep `access_token` and remove only legacy `apiKey` query support.
4. Define configuration option types and validation rules for security, CORS, cookies, forwarded headers, and rate limits.

### Phase 0 Decisions

- **Public server lookup**: `GET /api/servers/{id}` is authenticated-only. It will reuse existing API-key/session authentication and keep `ServerInfoDto` for authorized callers; unauthenticated callers receive `server.lookup_unauthorized`.
- **API-key migration**: new registrations write `ApiKeyHash` and `ApiKeyFingerprint`. Compatibility read paths may temporarily recognize legacy plaintext `ApiKey` rows only to authenticate and opportunistically backfill hash/fingerprint, with deployment docs recommending immediate rotation; new writes must not persist raw keys.
- **SignalR credentials**: keep header and bearer-token support, keep `access_token` for browser/WebSocket compatibility, and disable legacy `apiKey` query-string support by default behind an explicit `Security:AllowLegacySignalRApiKeyQuery` opt-in.
- **Configuration options**: use strongly typed option classes validated on start. Environment variable mappings follow standard .NET double-underscore binding:
  - `Security:AdminToken` / `Security__AdminToken` (fallback read from legacy `AdminToken` during transition)
  - `Security:AllowPublicServerLookup` / `Security__AllowPublicServerLookup` (default `false`; documented for future reduced DTO only)
  - `Security:AllowLegacySignalRApiKeyQuery` / `Security__AllowLegacySignalRApiKeyQuery` (default `false`)
  - `Security:ApiKeyPepper` / `Security__ApiKeyPepper` (required in production when HMAC peppering is enabled)
  - `Cors:AllowedOrigins` / `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, ... (fallback read from legacy `AllowedOrigins`; explicit non-localhost origins required in production)
  - `Session:CookieSecurePolicy` / `Session__CookieSecurePolicy` (`Always` in production unless explicitly overridden for trusted test hosts)
  - `Session:SameSite` / `Session__SameSite` (default `Lax`)
  - `ForwardedHeaders:KnownProxies` / `ForwardedHeaders__KnownProxies__0`, ...
  - `RateLimits:Registration`, `RateLimits:SessionCreation`, `RateLimits:FailedApiKeyAuth`, and `RateLimits:SignalRConnections` sections with `PermitLimit`, `WindowSeconds`, and `SegmentsPerWindow`.

### Current auth/session inventory

- `src/JellyFederation.Server/Controllers/ServersController.cs`: registration reads legacy `AdminToken`, generates `RegisteredServer.ApiKey`, persists it, returns it, and exposes unauthenticated `GET /api/servers/{id}`.
- `src/JellyFederation.Server/Filters/ApiKeyAuthFilter.cs`: reads `X-Api-Key`, caches by raw key (`apikey:{apiKey}`), and queries `RegisteredServer.ApiKey` directly; falls back to `WebSessionService` cookie auth.
- `src/JellyFederation.Server/Hubs/FederationHub.cs`: resolves API keys from `X-Api-Key`, `Authorization: Bearer`, `access_token`, and legacy `apiKey`, then queries `RegisteredServer.ApiKey` directly.
- `src/JellyFederation.Server/Services/ApiKeyService.cs`: generates raw API keys only; no hashing/fingerprint abstraction yet.
- `src/JellyFederation.Server/Services/WebSessionService.cs`: protected cookie payload includes `ServerId`, raw `ApiKey`, and expiry, then rechecks `RegisteredServer.ApiKey`.
- `src/JellyFederation.Server/Program.cs`: configures permissive fallback CORS origins, registration rate limit only, forwarded headers, Data Protection, and session service registration.
- `src/JellyFederation.Web/src/`: browser session dependencies require review when server session payload/auth changes are implemented.

### Phase 1: Foundational security infrastructure

1. Add strongly typed security/CORS/session/rate-limit options with `ValidateOnStart()`.
2. Add API-key hash/fingerprint helpers using modern password/key derivation or HMAC-based server-side pepper strategy, with fixed-time comparison.
3. Add EF model changes and provider-specific migrations.
4. Add sanitized failure descriptors and telemetry constants for auth/config/throttle/validation outcomes.

### Phase 2: P1 implementation

1. Enforce production admin token and CORS validation.
2. Change registration to generate raw API key once and store only hash/fingerprint.
3. Update HTTP and SignalR authentication to resolve by fingerprint/hash without plaintext comparison.
4. Replace secret-bearing web session payload/cache keys with non-secret session IDs/fingerprints.
5. Add rate-limit policies for session creation, failed API-key auth, and SignalR connection attempts.
6. Force production-secure cookie behavior with trusted forwarded-header validation.

### Phase 3: P2 implementation

1. Protect or reduce `GET /api/servers/{id}` according to design decision.
2. Add media-sync validation for `Overview`, `ImageUrl`, and `FileSizeBytes`.
3. Add security headers and CSP for SPA/static responses.
4. Remove or gate legacy query-string API-key support.

### Phase 4: Verification and release notes

1. Run unit/integration/provider tests and inspect logs for secret redaction.
2. Run migration smoke tests on SQLite and PostgreSQL.
3. Update deployment docs with required production settings, rotation steps, rate-limit knobs, CSP/image policy, and SignalR credential guidance.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Possible intentional rejection of insecure production defaults | Required to prevent open registration/credentialed localhost CORS in production | Preserving permissive defaults would leave the reported vulnerability unresolved |
| Possible API-key rotation requirement for existing deployments | Plaintext-to-hash migration may need operator coordination and safe rollout | Continuing to store plaintext keys would leave database backups as credential material |
