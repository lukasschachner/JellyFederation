# Tasks: Performance and Scalability Remediation

**Input**: Design documents from `/specs/006-performance-scalability/`  
**Prerequisites**: plan.md, spec.md, contracts/performance-scalability.md

## Phase 1: Setup

- [X] T001 Choose pagination metadata contract in `specs/006-performance-scalability/contracts/performance-scalability.md`.
- [X] T002 Choose library sync strategy and migration execution model in `specs/006-performance-scalability/plan.md`.
- [X] T003 [P] Inventory current list endpoints, sync queries, migration startup code, and request-limit config.

## Phase 2: Foundational

- [X] T004 Add pagination helper DTOs or header utilities in `src/JellyFederation.Server/` or `src/JellyFederation.Shared/`.
- [X] T005 Update EF model indexes in `src/JellyFederation.Data/FederationDbContext.cs` for media uniqueness and list query ordering.
- [X] T006 Add sync generation field to `MediaItem` if selected in Phase 0.
- [X] T007 Generate SQLite migration in `src/JellyFederation.Migrations.Sqlite/`.
- [X] T008 Generate PostgreSQL migration in `src/JellyFederation.Migrations.PostgreSQL/`.
- [X] T009 [P] Add stable failure codes for pagination/request-size/duplicate media outcomes.

## Phase 3: User Story 1 - Page operational list endpoints (P1)

- [X] T010 [P] Add `FileRequestsController.List` pagination tests in `tests/JellyFederation.Server.Tests/`.
- [X] T011 [P] Add `InvitationsController.List` pagination tests in `tests/JellyFederation.Server.Tests/`.
- [X] T012 [P] Add `ServersController.List` pagination tests in `tests/JellyFederation.Server.Tests/`.
- [X] T013 Implement pagination, bounded page size, stable sorting, and total metadata in `FileRequestsController`.
- [X] T014 Implement pagination, bounded page size, stable sorting, and total metadata in `InvitationsController`.
- [X] T015 Implement pagination, bounded page size, stable sorting, and total metadata in `ServersController`.
- [X] T016 Update `src/JellyFederation.Web/src/` callers if a response envelope is selected.

## Phase 4: User Story 2 - Scale library sync (P1)

- [X] T017 [P] Add provider tests for `(ServerId, JellyfinItemId)` uniqueness.
- [X] T018 [P] Add replace-all sync tests covering stale deletion without materializing stale entities.
- [ ] T019 [P] Add cancellation/duplicate-input tests for `LibraryController.Sync`.
- [X] T020 Refactor `LibraryController.Sync` to process incoming items in bounded batches.
- [X] T021 Implement set-based stale deletion with `ExecuteDeleteAsync` or provider-specific equivalent.
- [X] T022 Add cancellation tokens to touched EF operations in `LibraryController` and related services.
- [ ] T023 Add sync metrics for counts, duration, provider, and outcome without sensitive media metadata.

## Phase 5: User Story 3 - Production migrations and request limits (P2)

- [ ] T024 [P] Add startup tests proving production web app does not run migrations.
- [ ] T025 [P] Add request-size integration tests for sync and non-sync endpoints.
- [X] T026 Remove or gate `db.Database.Migrate()` in `src/JellyFederation.Server/Program.cs` for production.
- [ ] T027 Add/document dedicated migration job/service/command for production deployments.
- [X] T028 Lower global request-body limit defaults and apply endpoint-specific limits to library sync/image preview paths.
- [ ] T029 Pass cancellation tokens through additional touched controller/hub database calls.

## Phase 6: Polish

- [ ] T030 Update docs/quickstart with pagination, migration, and request-size limit guidance.
- [X] T031 Run `dotnet build JellyFederation.slnx`.
- [X] T032 Run `dotnet test JellyFederation.slnx`.
- [X] T033 Run provider-specific migration/test validation.
- [X] T034 Run Slopwatch for substantial generated/refactored .NET code.
