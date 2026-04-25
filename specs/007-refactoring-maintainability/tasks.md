# Tasks: Refactoring and Maintainability Remediation

**Input**: Design documents from `/specs/007-refactoring-maintainability/`  
**Prerequisites**: plan.md, spec.md, contracts/refactoring-maintainability.md

## Phase 1: Setup

- [X] T001 Confirm sequencing/conflicts with `specs/005-security-hardening/` auth and session work.
- [X] T002 [P] Inventory controller/hub methods and rank extraction targets in `specs/007-refactoring-maintainability/plan.md`.
- [X] T003 [P] Identify DTO candidates for record conversion and required snapshots.
- [X] T004 [P] Identify EF constraints requiring data cleanup.

## Phase 2: Authentication foundation

- [ ] T005 Add API-key authentication handler in `src/JellyFederation.Server/Authentication/`.
- [ ] T006 Add web-session/cookie authentication handler or adapter in `src/JellyFederation.Server/Authentication/`.
- [ ] T007 Define claims and named authorization policies in `src/JellyFederation.Server/` DI configuration.
- [ ] T008 [P] Add HTTP auth policy tests in `tests/JellyFederation.Server.Tests/`.
- [ ] T009 [P] Add SignalR authorization tests in `tests/JellyFederation.Server.Tests/`.
- [ ] T010 Replace `ApiKeyAuthFilter` usage with `[Authorize]` policies where covered by tests.
- [ ] T011 Replace manual `FederationHub.AuthenticateAsync` gates with standard hub authorization where covered by tests.
- [ ] T012 Remove or isolate `AuthenticatedController`/`HttpContext.Items` dependencies for migrated controllers.

## Phase 3: Service extraction

- [ ] T013 [P] Add `ServerRegistrationService` tests and implementation in `src/JellyFederation.Server/Services/`.
- [X] T014 [P] Add `InvitationService` tests and implementation in `src/JellyFederation.Server/Services/`.
- [ ] T015 [P] Add `FileRequestService` tests and implementation in `src/JellyFederation.Server/Services/`.
- [ ] T016 [P] Add `LibrarySyncService` tests and implementation in `src/JellyFederation.Server/Services/`.
- [ ] T017 [P] Add `TransferNegotiationService` tests and implementation in `src/JellyFederation.Server/Services/`.
- [ ] T018 [P] Add `TransferRoutingService` tests and implementation in `src/JellyFederation.Server/Services/`.
- [ ] T019 Refactor controllers to delegate business workflows to services while preserving response contracts.
- [ ] T020 Refactor `FederationHub` to delegate routing/negotiation workflows to services.

## Phase 4: Telemetry, options, and DI cleanup

- [ ] T021 Add operation runner/helper in `src/JellyFederation.Server/Telemetry/` or shared telemetry project.
- [ ] T022 Convert one representative controller operation and one hub operation to the operation runner.
- [ ] T023 Add strongly typed options and validators for security/CORS/server limits/telemetry in `src/JellyFederation.Server/Options/`.
- [ ] T024 Move related service registrations into `IServiceCollection` extension methods.
- [ ] T025 Add startup option validation tests.
- [ ] T026 Verify telemetry parity and secret redaction in tests/log inspection.

## Phase 5: DTO, EF, and frontend cleanup

- [ ] T027 [P] Add DTO snapshot/contract tests for selected shared DTOs.
- [ ] T028 Convert eligible DTOs to records in `src/JellyFederation.Shared/` without changing JSON shape.
- [ ] T029 Add EF max lengths/required constraints/indexes in `src/JellyFederation.Data/FederationDbContext.cs`.
- [ ] T030 Generate SQLite migration for EF constraint/index changes.
- [ ] T031 Generate PostgreSQL migration for EF constraint/index changes.
- [ ] T032 Remove browser raw API-key storage paths in `src/JellyFederation.Web/src/` where session/cookie flow supports it.

## Phase 6: Polish

- [ ] T033 Update docs with auth schemes, policies, services, options, and DTO compatibility notes.
- [X] T034 Run `dotnet build JellyFederation.slnx`.
- [ ] T035 Run `dotnet test JellyFederation.slnx`.
- [ ] T036 Run provider migration validation.
- [X] T037 Run Slopwatch for broad refactoring.
