# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]
**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

[Extract from feature spec: primary requirement + technical approach from research]

## Technical Context

**Language/Version**: C# on .NET 10 for server/web/tests; .NET 9 for Jellyfin plugin; shared libraries target `net9.0;net10.0` or NEEDS CLARIFICATION
**Primary Dependencies**: ASP.NET Core MVC, SignalR, EF Core, OpenTelemetry, Jellyfin plugin APIs, SIPSorcery/WebRTC as applicable or NEEDS CLARIFICATION
**Storage**: EF Core with SQLite for local/dev and PostgreSQL for production; provider-specific migrations in `src/JellyFederation.Migrations.*` or N/A
**Testing**: xUnit v3 on Microsoft.Testing.Platform, ASP.NET Core integration tests, SignalR workflow tests, EF Core/provider tests, Testcontainers for PostgreSQL where applicable
**Target Platform**: Linux/containerized federation server plus Jellyfin plugin runtime
**Project Type**: Jellyfin plugin + ASP.NET Core federation server + shared contracts + web frontend
**Performance Goals**: [domain-specific goals such as transfer throughput, p95 API latency, query limits, startup time, or NEEDS CLARIFICATION]
**Constraints**: Stable federation contracts; privacy-safe telemetry; provider parity for SQLite/PostgreSQL; Jellyfin runtime assembly compatibility; async/cancellable I/O
**Scale/Scope**: [servers, media item count, transfer size, concurrent requests/transfers, or NEEDS CLARIFICATION]

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Contract-First Federation Boundaries**: Are HTTP DTOs, SignalR messages, persistent enums, and telemetry names defined in `src/JellyFederation.Shared` or `contracts/` with compatibility notes for breaking changes?
- **Result-Oriented Failure Handling**: Are expected failures modeled with `OperationOutcome<T>`/`FailureDescriptor` and translated through boundary mappers rather than ad-hoc payloads or leaked exceptions?
- **Observable and Privacy-Safe Operations**: Does the plan define spans, metrics, structured logs, correlation IDs, outcome tags, and redaction for new/changed workflows?
- **Provider-Aware Persistence and Migrations**: Are EF Core query tracking, indexes/limits, cancellation, and both SQLite/PostgreSQL migration impacts addressed?
- **Incremental, Tested Delivery**: Is each user story independently testable with contract/integration/provider coverage for changed boundaries, and are validation commands listed?
- **Platform Standards**: Does the design preserve .NET target frameworks, Central Package Management, Jellyfin plugin packaging constraints, DI/configuration conventions, and async cancellation?

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── JellyFederation.Plugin/                 # Jellyfin plugin (.NET 9)
├── JellyFederation.Server/                 # ASP.NET Core API + SignalR server (.NET 10)
├── JellyFederation.Shared/                 # Shared DTOs, models, SignalR contracts, telemetry (`net9.0;net10.0`)
├── JellyFederation.Data/                   # EF Core DbContext and persistence model configuration
├── JellyFederation.Migrations.Sqlite/      # SQLite provider migrations + design-time factory
├── JellyFederation.Migrations.PostgreSQL/  # PostgreSQL provider migrations + design-time factory
└── JellyFederation.Web/                    # Server management frontend

tests/
├── JellyFederation.Plugin.Tests/
└── JellyFederation.Server.Tests/

docs/
└── [DocFX conceptual/API documentation]
```

**Structure Decision**: [Document which projects/files this feature changes and why]

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., contract incompatibility] | [current need] | [compatibility alternative rejected because] |
| [e.g., provider-specific behavior] | [current need] | [provider-neutral alternative rejected because] |
