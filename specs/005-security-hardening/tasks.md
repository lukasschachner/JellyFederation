# Tasks: Security Hardening Remediation

**Input**: Design documents from `/specs/005-security-hardening/`  
**Prerequisites**: plan.md, spec.md, contracts/security-hardening.md

## Phase 1: Setup

- [X] T001 Confirm public server lookup decision in `specs/005-security-hardening/contracts/security-hardening.md`.
- [X] T002 [P] Inventory current API-key/session/auth paths in `src/JellyFederation.Server/Filters/`, `src/JellyFederation.Server/Hubs/`, `src/JellyFederation.Server/Services/`, and `src/JellyFederation.Web/src/`.
- [X] T003 [P] Define security option names and environment variable mappings in `specs/005-security-hardening/plan.md`.

## Phase 2: Foundational

- [X] T004 Add security/CORS/session/rate-limit options and validators in `src/JellyFederation.Server/Options/`.
- [X] T005 Add API-key hash/fingerprint helper service in `src/JellyFederation.Server/Services/` with fixed-time comparison tests.
- [ ] T006 Update `RegisteredServer` model and EF configuration in `src/JellyFederation.Shared/Models/RegisteredServer.cs` and `src/JellyFederation.Data/FederationDbContext.cs`.
- [ ] T007 Generate SQLite migration with `dotnet ef migrations add HashApiKeys --project src/JellyFederation.Migrations.Sqlite`.
- [ ] T008 Generate PostgreSQL migration with `dotnet ef migrations add HashApiKeys --project src/JellyFederation.Migrations.PostgreSQL`.
- [X] T009 [P] Add stable failure codes/telemetry constants for auth/config/throttling/media validation in `src/JellyFederation.Shared/`.
- [ ] T010 [P] Add provider migration tests or smoke coverage in `tests/JellyFederation.Server.Tests/`.

## Phase 3: User Story 1 - Protect server registration and API keys (P1)

- [ ] T011 [P] Add integration tests proving production startup fails with blank admin token and missing CORS origins.
- [ ] T012 [P] Add registration/auth tests proving raw API keys are returned once and not persisted.
- [ ] T013 Update `ServersController` registration flow to require admin token in production and store hash/fingerprint only.
- [ ] T014 Update `ApiKeyAuthFilter` and `FederationHub` authentication to compare hash/fingerprint without plaintext database predicates.
- [ ] T015 Add API-key rotation or migration compatibility workflow according to the Phase 0 decision.
- [ ] T016 Verify logs/telemetry for registration and auth contain no raw admin token/API key values.

## Phase 4: User Story 2 - Harden browser sessions and throttling (P1)

- [ ] T017 [P] Add tests for session cookie payload redaction and production `Secure` behavior.
- [ ] T018 [P] Add tests for session, failed API-key, and SignalR connection throttling.
- [ ] T019 Update `WebSessionService` to store non-secret session identifiers only and avoid raw API keys in cache keys.
- [ ] T020 Add rate-limiting policies in `src/JellyFederation.Server/Program.cs` or DI extensions and apply them to sessions/auth/hub paths.
- [ ] T021 Configure production-safe cookie options and forwarded-header validation.
- [ ] T022 Remove browser-side raw API-key session dependencies in `src/JellyFederation.Web/src/` where covered by the chosen design.

## Phase 5: User Story 3 - Constrain public surfaces and media metadata (P2)

- [ ] T023 [P] Add tests for protected/reduced `GET /api/servers/{id}` behavior.
- [ ] T024 [P] Add media-sync validation tests for oversized strings, invalid image URLs, and negative file sizes.
- [ ] T025 [P] Add security-header/CSP integration tests for SPA/static responses.
- [ ] T026 Implement the chosen server lookup protection/reduced DTO in `src/JellyFederation.Server/Controllers/ServersController.cs`.
- [ ] T027 Add media metadata validation attributes/service logic in shared DTOs and `LibraryController.Sync`.
- [ ] T028 Add CSP/security-header middleware and production configuration knobs.
- [ ] T029 Remove or disable legacy SignalR `apiKey` query-string support by default.

## Phase 6: Polish

- [ ] T030 Update deployment/security docs with admin token, CORS, cookie, forwarded-header, API-key rotation, and CSP guidance.
- [ ] T031 Run `dotnet build JellyFederation.slnx`.
- [ ] T032 Run `dotnet test JellyFederation.slnx`.
- [ ] T033 Run provider migration validation for SQLite and PostgreSQL.
- [ ] T034 Run Slopwatch for substantial generated/refactored .NET code.
- [ ] T035 Manually inspect logs/test output to confirm secrets are redacted.
