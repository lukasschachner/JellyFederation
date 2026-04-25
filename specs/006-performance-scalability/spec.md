# Feature Specification: Performance and Scalability Remediation

**Feature Branch**: `006-performance-scalability`  
**Created**: 2026-04-25  
**Status**: Draft  
**Input**: Repository audit report `docs/reviews/performance-opportunities.md`

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Page operational list endpoints (Priority: P1)

An operator or authenticated peer can list file requests, invitations, and registered servers without unbounded responses as repository data grows.

**Why this priority**: Unbounded list endpoints can become slow, memory-heavy, and difficult for the frontend to render.

**Independent Test**: Seed more records than the maximum page size and verify each endpoint returns only a bounded page, includes total/count metadata, preserves filters, and rejects invalid page/page-size values.

**Acceptance Scenarios**:

1. **Given** 1,000 file requests, **When** a client requests page 1 with page size 50, **Then** exactly 50 items and total metadata are returned.
2. **Given** an invalid page size of 10,000, **When** the request is submitted, **Then** the response uses a stable validation failure or clamps according to the documented contract.

---

### User Story 2 - Scale library sync for large media libraries (Priority: P1)

A server can synchronize large Jellyfin libraries without loading all existing media rows into a tracked dictionary or materializing stale rows before deletion.

**Why this priority**: The current sync path may produce high memory usage and long EF change-tracking times for large libraries.

**Independent Test**: Use provider-backed tests to sync large batches, verify `(ServerId, JellyfinItemId)` uniqueness, upsert behavior, stale deletion, cancellation support, and bounded memory/query behavior where measurable.

**Acceptance Scenarios**:

1. **Given** an existing library with stale rows, **When** a replace-all sync completes, **Then** incoming rows are upserted and stale rows are deleted via set-based operations.
2. **Given** duplicate `(ServerId, JellyfinItemId)` values, **When** sync runs, **Then** duplicates are rejected or resolved deterministically according to the documented policy.

---

### User Story 3 - Move production migrations and request limits out of risky defaults (Priority: P2)

An operator can deploy the web app without startup-blocking production migrations and with request-body limits sized for actual endpoints.

**Why this priority**: Running migrations in web startup can block or race across replicas, and a global 300 MiB request limit exposes endpoints that do not need large payloads.

**Independent Test**: Verify production startup does not call `Database.Migrate()`, development/single-instance migration behavior remains documented, and endpoint-specific request limits permit library sync while rejecting oversized non-sync requests.

**Acceptance Scenarios**:

1. **Given** production configuration, **When** the app starts, **Then** pending migrations are not applied by the web process and the operator is instructed to run the migration job/service.
2. **Given** a large request to a non-sync endpoint, **When** it exceeds the endpoint limit, **Then** it is rejected without affecting library sync limits.

---

### Edge Cases

- Pagination must remain deterministic under concurrent inserts; default sorting must be documented.
- Pagination changes must preserve existing clients through defaults or additive envelope/headers.
- Library sync must handle cancellation after partial batches without corrupting uniqueness or stale-generation state.
- SQLite/dev fallback may use simpler EF code while PostgreSQL can use provider-specific bulk paths later.
- Search with leading-wildcard `LIKE` remains bounded by page size until full-text search is explicitly designed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST add bounded pagination to file request, invitation, and server list endpoints.
- **FR-002**: System MUST expose total-count metadata via a typed pagination envelope or documented headers.
- **FR-003**: System MUST add a unique index on `(ServerId, JellyfinItemId)` for media items.
- **FR-004**: System MUST optimize replace-all library sync to avoid materializing all stale rows before deletion.
- **FR-005**: System MUST pass cancellation tokens through controller and hub database calls touched by this feature.
- **FR-006**: System MUST stop applying production migrations from web application startup and provide a dedicated migration path.
- **FR-007**: System MUST reduce global request-body limits and apply endpoint-specific limits for library sync/image preview payloads.
- **FR-008**: System MUST keep search workloads bounded by page-size caps and document future full-text search options.

### Contract & Compatibility Requirements *(mandatory for federation/API/SignalR/storage changes)*

- **CR-001**: Pagination parameters must be additive and must not break clients that omit them.
- **CR-002**: If a pagination envelope replaces raw arrays, a compatibility route/version or response-header-only approach must be selected before implementation.
- **CR-003**: EF uniqueness/index changes require SQLite and PostgreSQL migrations.
- **CR-004**: Request-size limit failures must use stable error semantics and not expose infrastructure details.

### Failure & Error Requirements *(mandatory)*

- **ER-001**: Expected failures MUST map to stable failure codes/categories and sanitized user-facing messages.
- **ER-002**: Invalid pagination, duplicate media identities, canceled syncs, request-too-large responses, and migration-job failures MUST be handled predictably.

### Observability & Privacy Requirements *(mandatory)*

- **OR-001**: List endpoints and library sync MUST emit metrics for item count, page size, duration, outcome, and provider where appropriate.
- **OR-002**: Sync telemetry MUST not include media titles, image URLs, Jellyfin item IDs, file paths, or API keys unless already approved as non-sensitive; prefer counts and hashes.

### Data & Migration Requirements *(include if feature involves data)*

- **DR-001**: Add unique index for `MediaItem(ServerId, JellyfinItemId)` and any supporting indexes for paged list query ordering.
- **DR-002**: Add generation/timestamp fields only if selected for sync optimization; otherwise document provider-neutral fallback.
- **DR-003**: Generate provider-specific migrations for SQLite and PostgreSQL.

### Key Entities *(include if feature involves data)*

- **MediaItem**: Synchronized media row identified per server by `JellyfinItemId` and optionally sync generation.
- **FileRequest**: Transfer request listed through paged operational endpoints.
- **Invitation**: Invitation row listed through paged operational endpoints.
- **RegisteredServer**: Server row listed through paged operational endpoints.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: List endpoints never return more than the configured maximum page size in integration tests.
- **SC-002**: Library replace-all sync deletes stale rows with set-based operations and passes provider tests for uniqueness.
- **SC-003**: Production startup no longer applies EF migrations from the web app.
- **SC-004**: Non-sync endpoints reject oversized bodies below the previous 300 MiB global limit, while sync endpoint limits are explicitly configured.
- **SC-005**: Touched controller/hub database calls accept/pass cancellation tokens.

## Assumptions

- Existing library browse endpoints already establish acceptable pagination semantics that can be reused.
- PostgreSQL is the production provider; SQLite remains supported for local/dev and tests.
- Bulk upsert can be phased: provider-neutral EF improvements first, PostgreSQL-specific optimizations later if needed.
