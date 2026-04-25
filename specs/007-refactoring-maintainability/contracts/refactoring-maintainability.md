# Refactoring and Maintainability Contract Notes

## Auth schemes and claims

- Add an API-key authentication scheme for plugin/non-browser clients.
- Add a web-session/cookie authentication scheme for browser clients.
- Represent current registered server identity as claims, including server ID and auth scheme. Do not include raw API keys, admin tokens, or cookie payloads.
- Use named authorization policies on controllers and hubs.

## Service boundaries

Candidate services:

- `ServerRegistrationService`
- `InvitationService`
- `LibrarySyncService`
- `FileRequestService`
- `TransferNegotiationService`
- `TransferRoutingService`

Services should return result/outcome objects and leave HTTP/SignalR mapping to boundary layers.

## Telemetry operation runner

- Standardize operation name, component, correlation ID, outcome, duration, and sanitized failure descriptor.
- Avoid media titles, URLs, file paths, raw IDs where not necessary, and all secrets.

## DTO compatibility

- DTO record conversion is allowed only if JSON property names and shapes remain stable.
- Snapshot/contract tests must be updated intentionally.
- Prefer additive changes; do not remove or rename shared contract members without migration/versioning.

## EF constraints

- Add constraints/indexes only with provider migrations and data cleanup notes.
- Prioritize media uniqueness, invitations by participants/status, file requests by participants/status/created date, and media by server/type/title.
