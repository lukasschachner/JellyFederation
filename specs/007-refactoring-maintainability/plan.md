# Implementation Plan: Refactoring and Maintainability Remediation

**Branch**: `007-refactoring-maintainability` | **Date**: 2026-04-25 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/007-refactoring-maintainability/spec.md`

## Summary

Refactor custom authentication into ASP.NET Core handlers/policies, extract cohesive services from controllers and the SignalR hub, centralize telemetry and option validation, then safely modernize DTOs and EF constraints under compatibility tests.

## Technical Context

**Language/Version**: C# on .NET 10 for server/tests; shared libraries `net9.0;net10.0`; plugin .NET 9 compatibility preserved.  
**Primary Dependencies**: ASP.NET Core authentication/authorization, SignalR, EF Core, OpenTelemetry, Microsoft.Extensions.Options.  
**Storage**: EF Core SQLite/PostgreSQL; migrations required for model constraint/index changes.  
**Testing**: xUnit v3 unit/integration tests, SignalR authorization tests, snapshot/contract tests where DTOs change, provider tests for EF constraints.  
**Target Platform**: Containerized federation server and Jellyfin plugin clients.  
**Project Type**: ASP.NET Core API + SignalR server + shared contracts.  
**Performance Goals**: Refactor must not introduce additional unbounded queries or unnecessary allocations; auth and telemetry helpers should be cheap per request.  
**Constraints**: Stable wire contracts, result-based failures, privacy-safe telemetry, provider-aware EF migrations, DI/configuration conventions.  
**Scale/Scope**: Authentication, controllers, SignalR hub, services, telemetry, options, DTOs, EF model constraints, frontend credential cleanup.

## Constitution Check

- **Contract-First Federation Boundaries**: PASS — DTO/auth/SignalR semantics documented in contracts/refactoring-maintainability.md.
- **Result-Oriented Failure Handling**: PASS — extracted services return stable outcomes/failure descriptors.
- **Observable and Privacy-Safe Operations**: PASS — operation runner standardizes telemetry and redaction.
- **Provider-Aware Persistence and Migrations**: PASS — EF constraints/indexes require provider migrations and data cleanup notes.
- **Incremental, Tested Delivery**: PASS — refactor is workflow-by-workflow with tests preserving behavior.
- **Platform Standards**: PASS — uses ASP.NET Core auth/options/DI patterns, modern C# only where compatible.

## Project Structure

```text
specs/007-refactoring-maintainability/
├── spec.md
├── plan.md
├── contracts/refactoring-maintainability.md
└── tasks.md

src/JellyFederation.Server/                 # auth handlers, policies, services, telemetry helpers, options, controllers, hub
src/JellyFederation.Shared/                 # DTOs/failure descriptors/telemetry constants if changed
src/JellyFederation.Data/                   # EF constraints/indexes
src/JellyFederation.Web/                    # browser credential cleanup
tests/JellyFederation.Server.Tests/         # unit/integration/snapshot/provider tests
```

**Structure Decision**: Introduce new services and auth handlers alongside existing code, migrate one boundary at a time, and delete old filters/items plumbing only after coverage proves behavior parity.

## Action Plan

### Phase 0: Sequencing and conflict check

1. Determine dependency ordering with `005-security-hardening` because both touch authentication/session/API-key paths.
2. List controllers/hub methods and rank extraction targets by risk/value.
3. Decide DTOs eligible for record conversion based on wire compatibility.
4. Identify EF constraints that require data cleanup before migration.

#### Phase 0 inventory decisions

- **Security sequencing/conflicts**: `005-security-hardening` owns API-key hashing/fingerprints, browser session secret removal, rate limiting, production CORS/session validation, and legacy SignalR query-token policy. This refactor should not replace auth/session plumbing until those contracts land; auth tasks are blocked except for non-invasive adapters/tests that preserve credential formats.
- **Extraction ranking**: P1 `InvitationService` (small cohesive workflow, broad controller coverage), P1 `FileRequestService` (create/cancel/complete authorization and notification workflow), P1 `TransferRoutingService` (ICE/relay routing authorization), P2 `LibrarySyncService` (bulk sync/query pagination), P2 `TransferNegotiationService` (transport choice and state updates), P3 `ServerRegistrationService` (blocked by security hardening API-key persistence changes).
- **DTO candidates/snapshots**: Snapshot first for `InvitationDto`, `FileRequestDto`, `MediaItemDto`, `ServerInfoDto`, `RegisterServerResponse`, `SessionStatusResponse`, and SignalR messages (`FileRequestNotification`, `IceSignal`, `RelayChunk`). Prefer no conversion for DTOs already positional records; only convert remaining mutable/request records with stable System.Text.Json shapes.
- **EF cleanup candidates**: Before stricter constraints, audit/backfill duplicate `MediaItem` rows by `(ServerId, JellyfinItemId)`, oversized `Title`/`Name`/`OwnerUserId`/`JellyfinItemId`/failure strings, invalid media URLs, and invitations/file requests with orphaned participant IDs. Add data cleanup notes before SQLite/PostgreSQL migrations.

### Phase 1: Authentication/authorization foundation

1. Add API-key and web-session authentication handlers.
2. Define claims, authentication schemes, and named authorization policies.
3. Add tests for HTTP and SignalR authorization parity.
4. Gradually replace `ApiKeyAuthFilter`, manual hub auth, and `AuthenticatedController` state usage.

### Phase 2: Service extraction

1. Extract `ServerRegistrationService`, `InvitationService`, `LibrarySyncService`, `FileRequestService`, `TransferNegotiationService`, and `TransferRoutingService` incrementally.
2. Make each service return result/outcome types with stable failure descriptors.
3. Keep controllers/hub responsible only for boundary mapping, auth policy, DTO mapping, and cancellation.
4. Add service-level tests before deleting duplicated controller/hub logic.

### Phase 3: Cross-cutting helpers and options

1. Add operation runner/helper for spans, metrics, outcomes, correlation, and redaction.
2. Convert representative operations, then expand after proving parity.
3. Add strongly typed options and validators; move direct `Program.cs`/controller config reads to options.
4. Organize registrations through `IServiceCollection` extension methods.

### Phase 4: Safe DTO/EF/frontend cleanup

1. Add snapshot/contract tests for DTO wire shapes.
2. Convert suitable DTOs to records only when snapshots remain stable.
3. Add EF constraints/indexes with provider migrations and cleanup notes.
4. Remove browser raw API-key storage paths where cookie/session flow supports it.

### Phase 5: Verification

1. Run build/test/provider validations.
2. Run API/snapshot approval review for changed contracts.
3. Run Slopwatch due to broad generated/refactored .NET code.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Temporary coexistence of old and new auth paths | Needed for incremental behavior-preserving rollout | Big-bang auth replacement would increase outage/security regression risk |
| DTO conversion may be skipped for some contracts | Stable wire/binary compatibility is higher priority | Mechanical conversion without snapshots could break clients |
