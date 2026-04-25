# Feature Specification: Security Hardening Remediation

**Feature Branch**: `005-security-hardening`  
**Created**: 2026-04-25  
**Status**: Draft  
**Input**: Repository audit report `docs/reviews/security-findings.md`

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Protect server registration and API keys (Priority: P1)

An operator deploying JellyFederation in production can require administrative authorization for registration, store only non-reversible API-key material, and authenticate servers without exposing raw secrets in storage, cache keys, cookies, logs, or telemetry.

**Why this priority**: Plaintext API keys and open registration are the highest-impact audit findings because they can permit unauthorized federation registration and credential disclosure after database or backup compromise.

**Independent Test**: Run server integration tests against production-like configuration to verify startup fails without an admin token, registration requires `X-Admin-Token`, newly generated API keys are returned only once, stored rows contain only hashes/fingerprints, and API-key auth succeeds/fails through fixed-time hash comparison.

**Acceptance Scenarios**:

1. **Given** production environment and blank `AdminToken`, **When** the server starts, **Then** startup fails with a sanitized configuration validation error.
2. **Given** a server is registered successfully, **When** the registration response is returned, **Then** the raw API key is present only in that response and the database stores only a hash plus non-secret fingerprint.
3. **Given** a valid API key, **When** an HTTP API or SignalR hub authenticates, **Then** the matching server identity is resolved without querying or comparing plaintext API keys.

---

### User Story 2 - Harden browser sessions and endpoint throttling (Priority: P1)

A browser user or plugin client receives safe authentication behavior: session cookies do not embed API keys, secure cookie behavior is production-safe, and brute-force or flood attempts against sessions, API-key auth, and hubs are rate-limited.

**Why this priority**: The audit identified secret-bearing session cookies/cache keys plus missing brute-force protections for session creation and SignalR/API-key attempts.

**Independent Test**: Add integration tests for session payload redaction, cookie `Secure` behavior in production/forwarded-header scenarios, rate-limit responses for repeated session creation and failed API-key attempts, and SignalR connection throttling.

**Acceptance Scenarios**:

1. **Given** a web session is created, **When** the protected cookie payload is inspected through test hooks, **Then** it contains only non-secret identifiers/session data and no raw API key.
2. **Given** production configuration, **When** a session cookie is issued over a trusted forwarded HTTPS request, **Then** the cookie is `Secure`, `HttpOnly`, and `SameSite` as configured.
3. **Given** repeated failed API-key or session requests from the same source, **When** the configured threshold is exceeded, **Then** the server returns a stable throttling response without logging the submitted secret.

---

### User Story 3 - Constrain public surfaces and untrusted media metadata (Priority: P2)

An authenticated federation operator can rely on production CORS, public server lookup behavior, image URL handling, and media-sync validation being explicit and constrained.

**Why this priority**: Metadata disclosure, credentialed localhost CORS defaults, arbitrary remote images, and unbounded strings increase tracking, abuse, and data-bloat risk.

**Independent Test**: Verify production startup rejects missing `AllowedOrigins`, `GET /api/servers/{id}` is protected or returns a reduced documented public shape, media sync rejects invalid/oversized `Overview`/`ImageUrl`/negative file size values, and SPA responses include expected security headers/CSP.

**Acceptance Scenarios**:

1. **Given** production configuration with no allowed CORS origins, **When** the server starts, **Then** validation fails with instructions to configure explicit origins.
2. **Given** an unauthenticated request to `GET /api/servers/{id}`, **When** public lookup is disabled, **Then** the request is denied; when enabled, only the documented public fields are returned.
3. **Given** media sync with an `http:` image URL or oversized `Overview`, **When** the request is submitted, **Then** validation fails with a stable client error and no database write for that item.

---

### Edge Cases

- Existing rows with plaintext `ApiKey` must migrate without locking operators out; migration must support a cutover/compatibility path and document rollback limits.
- Authentication comparison must handle malformed, empty, extremely long, or wrong-prefix keys without timing leaks or excessive CPU/memory.
- Rate limiting must not make all users share a single reverse-proxy IP bucket when trusted forwarded headers are configured correctly.
- SignalR browser clients may require query-string `access_token`; legacy `apiKey` query support should be removed or explicitly opt-in with documented risk.
- CSP and image validation must preserve supported embedded previews if they remain in scope.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST validate production security configuration on startup, including non-blank admin token and explicit credentialed CORS origins.
- **FR-002**: System MUST store only API-key hashes and non-secret fingerprints at rest; raw API keys MUST be returned only once during registration or rotation.
- **FR-003**: System MUST authenticate HTTP API-key and SignalR clients using fixed-time comparison of derived hashes/fingerprints.
- **FR-004**: System MUST remove raw API keys from web session payloads, protected cookies, cache keys, structured logs, and telemetry.
- **FR-005**: System MUST add rate-limiting policies for session creation, failed API-key attempts, and SignalR connection attempts.
- **FR-006**: System MUST force secure cookie behavior in production and validate trusted forwarded-header configuration where applicable.
- **FR-007**: System MUST explicitly protect `GET /api/servers/{id}` or replace it with a documented reduced public response.
- **FR-008**: System MUST validate media-sync string lengths, image URL schemes/lengths, and non-negative file sizes before persistence.
- **FR-009**: System MUST add security headers for SPA/static responses, including CSP, `nosniff`, referrer policy, and permissions policy.

### Contract & Compatibility Requirements *(mandatory for federation/API/SignalR/storage changes)*

- **CR-001**: API-key registration and authentication routes MUST remain wire-compatible for clients that submit an API key after receiving it, but storage fields change from `ApiKey` to `ApiKeyHash`/fingerprint through migrations.
- **CR-002**: Legacy SignalR `apiKey` query-string support MUST be deprecated, disabled by default, or removed with migration notes; `access_token` compatibility for browser SignalR may remain if cookies are not viable.
- **CR-003**: Any public server lookup DTO change MUST be documented in `contracts/security-hardening.md` with field-level exposure notes.
- **CR-004**: Validation failures MUST use existing stable error envelopes/failure descriptors rather than ad-hoc response shapes.

### Failure & Error Requirements *(mandatory)*

- **ER-001**: Expected failures MUST map to stable failure codes/categories and sanitized user-facing messages.
- **ER-002**: Missing production security config, invalid admin token, invalid API key, throttled attempts, invalid media metadata, and unauthorized public lookup MUST have distinct stable outcomes.
- **ER-003**: Authentication and validation errors MUST never echo submitted API keys, tokens, full cookie payloads, or untrusted image URLs in logs or client messages.

### Observability & Privacy Requirements *(mandatory)*

- **OR-001**: Authentication, registration, session creation, throttling, media validation, and security-header middleware MUST emit privacy-safe logs/metrics with outcome tags and correlation IDs.
- **OR-002**: Telemetry MUST record only API-key fingerprints or server IDs where needed; raw keys, admin tokens, access tokens, and session payloads are forbidden.
- **OR-003**: Repeated authentication failures MUST be observable by source bucket and non-secret credential fingerprint when available.

### Data & Migration Requirements *(include if feature involves data)*

- **DR-001**: Add `ApiKeyHash` and optional non-secret `ApiKeyFingerprint` to `RegisteredServer`; remove or ignore plaintext `ApiKey` after migration.
- **DR-002**: SQLite and PostgreSQL migrations MUST be generated in their provider-specific migration projects.
- **DR-003**: Existing plaintext records MUST be migrated to hashed values using a documented operator-safe path; if impossible for already-running deployments, require key rotation with clear compatibility behavior.
- **DR-004**: Add EF max lengths and validation-aligned constraints for media metadata fields where changed.

### Key Entities *(include if feature involves data)*

- **RegisteredServer**: Federation peer identity with API-key hash/fingerprint, online state, owner metadata, and media relationship.
- **WebSession**: Server-side or identifier-backed browser session that contains non-secret identity and expiry data.
- **MediaItem**: Synchronized media metadata with bounded strings, image URL constraints, and non-negative file size.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Production startup rejects blank admin token and missing allowed origins in automated integration tests.
- **SC-002**: Database inspection after registration and migration finds zero raw API-key values in persisted `RegisteredServer` rows.
- **SC-003**: Repeated failed auth/session/hub attempts receive rate-limit responses at configured thresholds and expose no secrets in logs.
- **SC-004**: Media sync validation rejects oversized strings, unsupported image schemes, and negative file sizes before persistence.
- **SC-005**: SPA/static responses include documented security headers and tests verify CSP `img-src` matches accepted image policies.

## Assumptions

- Production deployments can provide admin token, CORS origins, and trusted proxy settings via environment variables or configuration providers.
- A one-time API-key rotation may be acceptable if lossless migration of unknown plaintext-to-hash semantics is not practical for every deployment.
- Browser session auth should move toward cookie-only behavior while plugin/non-browser clients continue to use API keys.
- Security hardening should be backward compatible where possible but may intentionally reject insecure production defaults.
