---
description: "Task list template for JellyFederation feature implementation"
---

# Tasks: [FEATURE NAME]

**Input**: Design documents from `/specs/[###-feature-name]/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Include tests for every changed federation boundary, persistence behavior, transport workflow, telemetry/error contract, and independently testable user story.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- Server/API/SignalR: `src/JellyFederation.Server/`
- Jellyfin plugin: `src/JellyFederation.Plugin/`
- Shared contracts/models/telemetry: `src/JellyFederation.Shared/`
- EF Core data model: `src/JellyFederation.Data/`
- SQLite migrations: `src/JellyFederation.Migrations.Sqlite/`
- PostgreSQL migrations: `src/JellyFederation.Migrations.PostgreSQL/`
- Web frontend: `src/JellyFederation.Web/`
- Server tests: `tests/JellyFederation.Server.Tests/`
- Plugin tests: `tests/JellyFederation.Plugin.Tests/`
- Documentation: `README.md`, `docs/`, and `specs/[###-feature-name]/quickstart.md`

<!--
  The /speckit.tasks command MUST replace sample tasks with actual tasks based on:
  - User stories from spec.md, ordered by priority
  - Constitution Check results from plan.md
  - Contracts from contracts/
  - Entities and migration impacts from data-model.md
  - Observability, privacy, and failure requirements
-->

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare the feature branch, projects, contracts, and validation commands.

- [ ] T001 Confirm affected projects and validation commands in specs/[###-feature-name]/plan.md
- [ ] T002 [P] Create or update contract notes in specs/[###-feature-name]/contracts/
- [ ] T003 [P] Identify telemetry names, failure codes, and privacy redaction requirements in specs/[###-feature-name]/plan.md

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

Examples of foundational tasks (adjust based on the plan):

- [ ] T004 [P] Add or update shared DTOs/messages/models in src/JellyFederation.Shared/
- [ ] T005 [P] Add or update stable failure codes and boundary mapping in src/JellyFederation.Shared/Models/ and src/JellyFederation.Server/Services/
- [ ] T006 [P] Add telemetry constants/redaction helpers in src/JellyFederation.Shared/Telemetry/
- [ ] T007 Update EF Core model configuration in src/JellyFederation.Data/FederationDbContext.cs if storage changes
- [ ] T008 Generate SQLite migration in src/JellyFederation.Migrations.Sqlite/ if storage changes
- [ ] T009 Generate PostgreSQL migration in src/JellyFederation.Migrations.PostgreSQL/ if storage changes
- [ ] T010 Add foundational contract/provider tests in tests/JellyFederation.Server.Tests/ or tests/JellyFederation.Plugin.Tests/

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel.

---

## Phase 3: User Story 1 - [Title] (Priority: P1) 🎯 MVP

**Goal**: [Brief description of what this story delivers]

**Independent Test**: [How to verify this story works on its own]

### Tests for User Story 1

> **NOTE: Write these tests before or with implementation; they should fail against the old behavior when practical.**

- [ ] T011 [P] [US1] Add contract/API/SignalR test in tests/JellyFederation.Server.Tests/[Name]Tests.cs
- [ ] T012 [P] [US1] Add plugin/server workflow test in tests/JellyFederation.Plugin.Tests/ or tests/JellyFederation.Server.Tests/

### Implementation for User Story 1

- [ ] T013 [P] [US1] Implement shared contract/model changes in src/JellyFederation.Shared/
- [ ] T014 [US1] Implement server behavior in src/JellyFederation.Server/
- [ ] T015 [US1] Implement plugin behavior in src/JellyFederation.Plugin/ if required
- [ ] T016 [US1] Add result/failure mapping and privacy-safe error handling
- [ ] T017 [US1] Add OpenTelemetry spans, metrics, structured logs, and correlation propagation

**Checkpoint**: User Story 1 is fully functional and testable independently.

---

## Phase 4: User Story 2 - [Title] (Priority: P2)

**Goal**: [Brief description of what this story delivers]

**Independent Test**: [How to verify this story works on its own]

### Tests for User Story 2

- [ ] T018 [P] [US2] Add contract/API/SignalR test in tests/JellyFederation.Server.Tests/[Name]Tests.cs
- [ ] T019 [P] [US2] Add provider/telemetry/workflow test relevant to this story

### Implementation for User Story 2

- [ ] T020 [P] [US2] Implement shared contract/model changes in src/JellyFederation.Shared/
- [ ] T021 [US2] Implement server behavior in src/JellyFederation.Server/
- [ ] T022 [US2] Implement plugin behavior in src/JellyFederation.Plugin/ if required
- [ ] T023 [US2] Add result/failure mapping, telemetry, and redaction updates

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - [Title] (Priority: P3)

**Goal**: [Brief description of what this story delivers]

**Independent Test**: [How to verify this story works on its own]

### Tests for User Story 3

- [ ] T024 [P] [US3] Add contract/API/SignalR test in tests/JellyFederation.Server.Tests/[Name]Tests.cs
- [ ] T025 [P] [US3] Add provider/telemetry/workflow test relevant to this story

### Implementation for User Story 3

- [ ] T026 [P] [US3] Implement shared contract/model changes in src/JellyFederation.Shared/
- [ ] T027 [US3] Implement server behavior in src/JellyFederation.Server/
- [ ] T028 [US3] Implement plugin behavior in src/JellyFederation.Plugin/ if required
- [ ] T029 [US3] Add result/failure mapping, telemetry, and redaction updates

**Checkpoint**: All user stories are independently functional.

---

[Add more user story phases as needed, following the same pattern]

---

## Phase N: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories.

- [ ] TXXX [P] Update README.md, docs/, or quickstart documentation for changed behavior/configuration
- [ ] TXXX Run `dotnet build JellyFederation.slnx`
- [ ] TXXX Run `dotnet test JellyFederation.slnx`
- [ ] TXXX Run provider-specific migration/test validation if storage changed
- [ ] TXXX Review changed .NET code with applicable dotnet-skills guidance
- [ ] TXXX Run Slopwatch for substantial generated/refactored .NET code where applicable
- [ ] TXXX Validate no sensitive values appear in logs, telemetry, or client-facing errors

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3+)**: All depend on Foundational phase completion
  - User stories can proceed in parallel if they do not modify the same files or shared contracts incompatibly
  - Otherwise implement sequentially in priority order (P1 → P2 → P3)
- **Polish (Final Phase)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational phase - no dependencies on later stories
- **User Story 2 (P2)**: Can start after Foundational phase - may integrate with US1 but remains independently testable
- **User Story 3 (P3)**: Can start after Foundational phase - may integrate with US1/US2 but remains independently testable

### Within Each User Story

- Contract/test tasks before or alongside implementation
- Shared contracts before server/plugin boundary code
- Data model and migrations before persistence-dependent services
- Result/failure mapping before exposing boundary responses
- Telemetry/redaction before story completion
- Story complete before moving to the next priority unless explicitly parallelized

### Parallel Opportunities

- Tasks marked [P] can run in parallel when they touch different files and do not depend on the same contract decision
- Contract tests and provider tests can often run in parallel
- Plugin and server implementation can run in parallel after shared contracts are stable
- Documentation can run in parallel after behavior/configuration is known

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Build, test, and demonstrate User Story 1 independently
5. Deploy/demo if ready

### Incremental Delivery

1. Complete Setup + Foundational → foundation ready
2. Add User Story 1 → test independently → deploy/demo
3. Add User Story 2 → test independently → deploy/demo
4. Add User Story 3 → test independently → deploy/demo
5. Each story adds value without breaking previous stories

## Notes

- [P] tasks = different files and no unresolved contract dependency
- [Story] label maps task to a specific user story for traceability
- Exact repository paths are required in generated tasks
- Avoid vague tasks, same-file conflicts, unbounded queries, ad-hoc errors, and unredacted telemetry
- Commit after each task or logical group
