# Refactoring Opportunities

Date: 2026-04-25

Scope: static design review of controllers, SignalR hub, authentication approach, shared DTOs, telemetry usage, EF model configuration, and frontend credential handling.

## Consolidate authentication and authorization

Current state:

- API key auth is implemented as `ApiKeyAuthFilter`.
- Hub auth is implemented manually in `FederationHub.AuthenticateAsync`.
- Browser session auth is implemented in `WebSessionService`.
- Authenticated controllers inherit `AuthenticatedController` and expect `HttpContext.Items` state.

Problems:

- Auth logic is duplicated across HTTP and SignalR paths.
- Authorization policy is implicit and harder to audit.
- Future roles/scopes will be harder to add.

Recommendation:

- Implement standard ASP.NET Core authentication handlers for:
  - API key authentication.
  - Web session cookie authentication.
- Use `[Authorize]` and named policies on controllers/hubs.
- Move current server identity into `ClaimsPrincipal` claims.

Benefits:

- Standard middleware ordering and diagnostics.
- Standard SignalR authorization.
- Cleaner test setup.
- Less custom per-controller plumbing.

## Extract business services from controllers and hub

Current controllers/hub mix:

- DB access.
- Business rules.
- Authorization checks.
- Telemetry.
- SignalR routing.
- DTO mapping.

High-value extraction targets:

- `ServerRegistrationService`
- `InvitationService`
- `LibrarySyncService` server-side component
- `FileRequestService`
- `TransferNegotiationService`
- `HubAuthenticationService` or replacement via ASP.NET auth
- `TransferRoutingService` for relay/ICE message routing

Benefits:

- Thinner controllers/hub methods.
- Easier unit testing of domain workflows.
- Reusable logic between HTTP and SignalR flows.
- Clearer boundaries for authorization decisions.

## Centralize telemetry boilerplate

Current state:

Many methods repeat:

- `Stopwatch.StartNew()`
- activity creation
- common tag assignment
- success/failure outcome recording
- metrics recording
- failure descriptor mapping

Recommendation:

- Add an operation runner/helper abstraction, for example:

```csharp
await _operationRunner.RunAsync(
    operationName: "file_request.create",
    component: "server",
    correlationId: CorrelationId,
    execute: async cancellationToken => { ... });
```

Benefits:

- Consistent metric dimensions.
- Less copy/paste.
- Lower chance of missing failure metrics.
- Easier privacy review for logged/telemetry fields.

## Make DTOs more idiomatic records where wire-compatible

Examples with manual constructors/deconstructors:

- `RegisterServerResponse`
- `ServerInfoDto`
- `MediaItemDto`
- `FileRequestDto`

Recommendation:

Use positional records when System.Text.Json compatibility and public contract requirements allow:

```csharp
public sealed record ServerInfoDto(
    Guid Id,
    string Name,
    string OwnerUserId,
    bool IsOnline,
    DateTime LastSeenAt,
    int MediaItemCount);
```

Benefits:

- Less code.
- Easier contract review.
- More idiomatic modern C#.

Caution:

- These are shared contracts. Keep property names stable and avoid breaking wire compatibility.
- Consider snapshot/contract tests when changing DTO shapes.

## Strengthen EF model configuration

Current state:

- Some indexes exist.
- Entity property lengths and uniqueness are mostly not enforced at the EF model level.

Recommendations:

- Add max lengths and required constraints matching DTO validation.
- Add unique index on `(ServerId, JellyfinItemId)` for `MediaItem`.
- Add suitable indexes for common query patterns:
  - invitations by participants/status
  - file requests by participants/status/created date
  - media items by server/type/title
- Consider provider-specific index improvements for PostgreSQL search later.

## Introduce API/session options with validation

Current state:

Configuration values are read directly in `Program.cs` or controllers.

Recommendation:

- Add strongly typed options:
  - `FederationSecurityOptions`
  - `CorsOptions`/`AllowedOriginsOptions`
  - `ServerLimitOptions`
  - `TelemetryOptions`
- Add `IValidateOptions<T>` or `ValidateOnStart()`.

Useful production validations:

- `AdminToken` is configured.
- `AllowedOrigins` is configured.
- Cookie secure policy is production-safe.
- Forwarded headers trusted proxies are configured when required.
- Request body limits are sane.

## Frontend credential handling cleanup

Current state:

- The frontend stores config in `localStorage`.
- Recent setup flow appears to save an empty API key after creating a cookie-backed session, but other paths and hooks still support using `apiKey` from config.

Recommendations:

- Prefer cookie-only browser auth after setup/session creation.
- Remove browser-side API key storage paths where possible.
- Make SignalR browser connections rely on cookies instead of `accessTokenFactory` if supported by the deployment flow.
- Keep plugin/non-browser API key support separate from browser session auth.

## Documentation and tracking

Recommended follow-up artifacts:

- Create issue checklist from `docs/reviews/security-findings.md`.
- Create migration design for hashed API keys.
- Create an auth refactor design before implementation to avoid breaking plugin/server compatibility.
- Add regression tests for each security behavior changed.
