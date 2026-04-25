# Implementation Plan: Performance and Scalability Remediation

**Branch**: `006-performance-scalability` | **Date**: 2026-04-25 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/006-performance-scalability/spec.md`

## Summary

Bound operational list endpoints, optimize library sync around unique media identities and set-based stale deletion, move production migrations out of web startup, lower global request-size defaults with endpoint-specific sync limits, and add cancellation/observability for touched database paths.

## Technical Context

**Language/Version**: C# on .NET 10 for server/tests; shared libraries `net9.0;net10.0`; plugin unchanged unless sync contract changes.  
**Primary Dependencies**: ASP.NET Core MVC, EF Core, SignalR, OpenTelemetry.  
**Storage**: EF Core SQLite/PostgreSQL; migrations required for unique/index/generation changes.  
**Testing**: xUnit v3 integration/provider tests; optional Testcontainers for PostgreSQL.  
**Target Platform**: Containerized federation server with PostgreSQL production and SQLite dev.  
**Project Type**: ASP.NET Core API + EF Core data model.  
**Performance Goals**: Bounded list responses; large sync avoids all-row tracked dictionaries and materialized stale deletes; startup not blocked by production migrations.  
**Constraints**: Additive API changes preferred, provider parity, async/cancellable I/O, privacy-safe telemetry.  
**Scale/Scope**: File request/invitation/server list endpoints, library sync, app startup migrations, Kestrel/controller request-size limits.

## Constitution Check

- **Contract-First Federation Boundaries**: PASS — pagination and request-limit behavior documented in contracts/performance-scalability.md.
- **Result-Oriented Failure Handling**: PASS — invalid pagination, duplicate media, and oversized request outcomes are stable.
- **Observable and Privacy-Safe Operations**: PASS — metrics use counts/durations/outcomes without media titles/URLs/secrets.
- **Provider-Aware Persistence and Migrations**: PASS — indexes/generation fields require provider-specific migrations and provider tests.
- **Incremental, Tested Delivery**: PASS — list pagination, sync optimization, and migrations/limits are independently testable.
- **Platform Standards**: PASS — follows EF Core no-tracking, ExecuteDelete, dedicated migration service/job, and cancellation token patterns.

## Project Structure

```text
specs/006-performance-scalability/
├── spec.md
├── plan.md
├── contracts/performance-scalability.md
└── tasks.md

src/JellyFederation.Server/                 # controllers, Program/startup, request limits, migration runner behavior
src/JellyFederation.Data/                   # EF indexes/configuration
src/JellyFederation.Migrations.Sqlite/      # migration
src/JellyFederation.Migrations.PostgreSQL/  # migration
tests/JellyFederation.Server.Tests/         # pagination, sync, migration/startup, request limit tests
```

**Structure Decision**: Keep contract changes additive in server controllers; implement persistence indexes and sync state in the shared data model; move migration execution to a dedicated production path while retaining documented dev behavior if needed.

## Action Plan

### Phase 0: Design decisions

1. Pagination contract: preserve existing raw-array JSON responses and add `X-Total-Count`, `X-Page`, `X-Page-Size`, and `X-Total-Pages` headers. Defaults are `page=1`, `pageSize=100`; maximum page size is `500`; invalid values return validation failures.
2. Library sync optimization approach: provider-neutral EF path first. Reject duplicate incoming `(ServerId, JellyfinItemId)` values, process incoming IDs in bounded batches, stamp processed rows with `IndexedAt = syncStartedAt`, and use `ExecuteDeleteAsync` for replace-all stale deletion where `IndexedAt < syncStartedAt`. Add a unique EF index on `(ServerId, JellyfinItemId)`; no new generation column is selected for this iteration.
3. Migration execution model: production web startup does not run `Database.Migrate()`. Development/testing may auto-migrate by default; production operators use `dotnet ef database update`/migration jobs with provider-specific migration projects.
4. Request-size limits: default global limit is 10 MiB (`ServerLimits:MaxRequestBodySizeMb`), with an explicit 100 MiB MVC request-size limit on the library sync endpoint.

### Phase 1: Foundational persistence and contracts

1. Add pagination request/response helpers or headers consistently.
2. Add EF unique index for `(ServerId, JellyfinItemId)` and any query-ordering indexes.
3. Add optional sync generation field and provider-specific migrations if chosen.
4. Add stable validation/failure codes for pagination, duplicate media, request-too-large, and migration-disabled states.

### Phase 2: Endpoint pagination

1. Update `FileRequestsController.List`, `InvitationsController.List`, and `ServersController.List` with page/pageSize/default sort.
2. Add integration tests for bounds, totals, filters, and sorting.
3. Update frontend callers if a response envelope is selected.

### Phase 3: Library sync scalability

1. Refactor `LibraryController.Sync` to avoid loading all existing rows into a tracked dictionary.
2. Upsert incoming rows in bounded batches and delete stale rows via `ExecuteDeleteAsync`/set-based provider paths.
3. Thread cancellation tokens through sync operations.
4. Add provider tests for uniqueness, replace-all behavior, cancellation, and duplicates.

### Phase 4: Startup migrations and request limits

1. Remove production `db.Database.Migrate()` from web startup.
2. Add/document a migration job/service/command with provider-specific usage.
3. Lower global request-body limits and add library-sync-specific limits.
4. Add startup/config integration tests.

### Phase 5: Verification

1. Run build/test/provider validations.
2. Review query plans where practical for new indexes.
3. Update docs/quickstart with pagination, migration, and request-limit behavior.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Optional provider-specific sync optimization | PostgreSQL production may need efficient upsert semantics | Pure EF fallback may remain too slow for very large libraries |
| Possible pagination envelope contract change | Typed metadata is clearer and testable | Raw-array compatibility may require headers or versioning if envelope breaks existing clients |
