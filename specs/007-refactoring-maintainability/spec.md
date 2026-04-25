# Feature Specification: Refactoring and Maintainability Remediation

**Feature Branch**: `007-refactoring-maintainability`  
**Created**: 2026-04-25  
**Status**: Draft  
**Input**: Repository audit report `docs/reviews/refactoring-opportunities.md`

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Standardize authentication and authorization (Priority: P1)

A maintainer can audit and extend authentication/authorization through standard ASP.NET Core authentication handlers, claims, `[Authorize]`, and named policies rather than custom filters, hub code, and `HttpContext.Items` conventions.

**Why this priority**: Auth logic is duplicated across HTTP, SignalR, and browser session paths, making future roles/scopes harder and security behavior harder to verify.

**Independent Test**: Replace or wrap current auth flows with handlers and verify HTTP controllers, SignalR hubs, and web sessions resolve the same server identity/claims and enforce equivalent policies.

**Acceptance Scenarios**:

1. **Given** a valid API key, **When** an authenticated API endpoint is called, **Then** `ClaimsPrincipal` contains the server identity and the endpoint passes its named policy.
2. **Given** an unauthenticated SignalR connection, **When** it connects to a protected hub, **Then** standard authorization rejects it before hub business logic runs.

---

### User Story 2 - Extract business workflows from controllers and hub (Priority: P1)

A maintainer can test server registration, invitations, library sync, file requests, transfer negotiation, and transfer routing as service workflows without constructing full controllers/hubs.

**Why this priority**: Controllers and hub currently mix DB access, business rules, auth checks, telemetry, routing, and mapping, increasing change risk.

**Independent Test**: Add service-level tests for at least the first extracted workflow and verify controller/hub integration tests still pass with thin boundary adapters.

**Acceptance Scenarios**:

1. **Given** a request to create an invitation, **When** the controller receives it, **Then** it delegates workflow decisions to `InvitationService` and only maps HTTP boundary concerns.
2. **Given** transfer relay/ICE routing, **When** the hub receives a message, **Then** routing rules are delegated to `TransferRoutingService` or equivalent.

---

### User Story 3 - Centralize telemetry and configuration boilerplate (Priority: P2)

A maintainer can add or change operations with consistent metrics, spans, failure mapping, options validation, and DI registration patterns.

**Why this priority**: Repeated stopwatch/activity/metrics/configuration code increases inconsistency and privacy review burden.

**Independent Test**: Convert one representative operation to an operation-runner/helper and strongly typed options, then verify identical success/failure metrics and startup option validation.

**Acceptance Scenarios**:

1. **Given** a service operation succeeds or fails, **When** it runs through the operation helper, **Then** duration, outcome, correlation, and sanitized failure tags are recorded consistently.
2. **Given** invalid production configuration, **When** the app starts, **Then** strongly typed option validation fails before serving traffic.

---

### User Story 4 - Modernize DTOs and EF model constraints safely (Priority: P3)

A maintainer can review shared DTOs and EF constraints with stable contracts and snapshot/contract tests guarding accidental wire changes.

**Why this priority**: DTO boilerplate and weak EF constraints are lower urgency but improve correctness and long-term maintainability.

**Independent Test**: Convert one DTO family where wire-compatible, add snapshot/contract tests, and add EF max length/index constraints matching validation.

**Acceptance Scenarios**:

1. **Given** a DTO converted to a positional record, **When** serialized JSON is compared with the previous snapshot, **Then** property names and shapes remain stable.
2. **Given** invalid persisted data exceeding configured length, **When** saved through EF, **Then** provider constraints or validation reject it consistently.

---

### Edge Cases

- Auth refactor must preserve plugin/non-browser API-key support and browser session behavior during incremental rollout.
- Claims must avoid embedding raw secrets or overly broad metadata.
- Service extraction must not create anemic pass-through services; extract cohesive workflows with result-based failures.
- DTO record conversion must not break System.Text.Json constructor/property behavior or binary compatibility unnecessarily.
- EF constraints may require data cleanup migrations before enforcing stricter lengths/uniqueness.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST implement standard ASP.NET Core authentication handlers for API-key and web-session authentication.
- **FR-002**: System MUST express controller and hub authorization via `[Authorize]` and named policies where feasible.
- **FR-003**: System MUST move server identity into `ClaimsPrincipal` claims and remove controller dependence on `HttpContext.Items` for new code.
- **FR-004**: System MUST extract cohesive business services for high-value workflows: registration, invitations, library sync, file requests, transfer negotiation, and transfer routing.
- **FR-005**: System MUST introduce a telemetry operation runner/helper or equivalent abstraction for repeated operation instrumentation.
- **FR-006**: System MUST introduce strongly typed options with startup validation for security, CORS, server limits, telemetry, and related settings.
- **FR-007**: System SHOULD convert suitable shared DTOs to idiomatic records only when wire-compatible and covered by contract/snapshot tests.
- **FR-008**: System MUST strengthen EF model constraints and indexes to match validation and common query patterns.
- **FR-009**: Browser credential handling SHOULD prefer cookie-only auth after setup and remove raw API-key storage paths where possible.

### Contract & Compatibility Requirements *(mandatory for federation/API/SignalR/storage changes)*

- **CR-001**: Authentication refactor MUST preserve accepted client credential formats unless intentionally changed by the security-hardening spec.
- **CR-002**: Claims and policies are internal boundary abstractions but HTTP/SignalR response semantics must remain compatible or be documented.
- **CR-003**: DTO record conversion MUST preserve JSON property names, nullability expectations, and stable wire shape.
- **CR-004**: EF constraints/index changes require SQLite and PostgreSQL migrations and data-cleanup notes.

### Failure & Error Requirements *(mandatory)*

- **ER-001**: Expected failures MUST map to stable failure codes/categories and sanitized user-facing messages.
- **ER-002**: Service extraction MUST use result/outcome patterns for validation, not-found, conflict, timeout, cancellation, and connectivity failures.

### Observability & Privacy Requirements *(mandatory)*

- **OR-001**: Operation helper MUST standardize span names, metric dimensions, outcome tags, correlation IDs, and sanitized failure descriptors.
- **OR-002**: Auth claims, telemetry tags, and logs MUST not contain raw API keys, admin tokens, session cookies, media paths, or image URLs.

### Data & Migration Requirements *(include if feature involves data)*

- **DR-001**: Add or refine EF max lengths, required constraints, unique indexes, and common-query indexes for invitations, file requests, media items, and servers.
- **DR-002**: Generate provider-specific migrations for SQLite and PostgreSQL for any model changes.
- **DR-003**: Existing data cleanup/backfill requirements MUST be documented before constraints are enforced.

### Key Entities *(include if feature involves data)*

- **AuthenticatedServerPrincipal**: Claims-based representation of the current registered server and auth scheme.
- **Business Services**: Cohesive workflow services that return results/failure descriptors and hide persistence/routing details from controllers/hubs.
- **Operation Runner**: Cross-cutting helper for telemetry/failure mapping around service operations.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At least API-key HTTP auth and SignalR auth are enforced through ASP.NET Core authentication/authorization tests.
- **SC-002**: At least three high-value workflows have service-level tests independent of controllers/hubs.
- **SC-003**: Repeated telemetry boilerplate is reduced in representative controllers/hubs with no loss of metric/span coverage.
- **SC-004**: Strongly typed options fail startup for invalid production settings in tests.
- **SC-005**: DTO/EF refactors are covered by snapshot/contract/provider tests.

## Assumptions

- Security-hardening may run before or alongside this refactor; avoid conflicting auth changes by coordinating contracts.
- Refactoring should proceed incrementally by workflow to reduce risk.
- Public shared contracts require compatibility review before conversion to records.
