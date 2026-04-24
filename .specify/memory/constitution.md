<!--
Sync Impact Report
Version change: template → 1.0.0
Modified principles:
- Template principle 1 → I. Contract-First Federation Boundaries
- Template principle 2 → II. Result-Oriented Failure Handling
- Template principle 3 → III. Observable and Privacy-Safe Operations
- Template principle 4 → IV. Provider-Aware Persistence and Migrations
- Template principle 5 → V. Incremental, Tested Delivery
Added sections:
- Platform and Architecture Standards
- Development Workflow and Quality Gates
Removed sections:
- None
Templates requiring updates:
- ✅ .specify/templates/plan-template.md
- ✅ .specify/templates/spec-template.md
- ✅ .specify/templates/tasks-template.md
- ✅ .specify/templates/commands/*.md (not present)
Runtime guidance updated:
- ✅ README.md
- ✅ AGENTS.md
- ✅ CLAUDE.md
Follow-up TODOs: None
-->
# JellyFederation Constitution

## Core Principles

### I. Contract-First Federation Boundaries
All cross-process contracts MUST live in `src/JellyFederation.Shared` or a feature
contract document before implementation. HTTP DTOs, SignalR messages, telemetry
field names, and persistent enum values MUST be stable, version-aware, and safe
for both plugin and server consumers. Breaking wire or storage changes MUST include
a migration or compatibility plan in the feature plan.

Rationale: JellyFederation spans Jellyfin plugins, a companion server, SignalR,
HTTP APIs, and persisted state; contract drift breaks federation even when local
code compiles.

### II. Result-Oriented Failure Handling
Expected failures MUST be represented as `OperationOutcome<T>` with stable
`FailureDescriptor` codes, categories, correlation IDs, and sanitized messages.
Controllers, hubs, and plugin/server boundary code MUST translate failures through
mapper services such as `ErrorContractMapper` and `SignalRErrorMapper`; ad-hoc
string errors and raw exception details in client-facing payloads are prohibited.
Exceptions remain appropriate for programmer errors and unexpected infrastructure
failures, but MUST be logged and translated at boundaries.

Rationale: Federated workflows fail for normal reasons such as validation,
connectivity, conflicts, and missing media; callers need predictable outcomes
without leaking operational details.

### III. Observable and Privacy-Safe Operations
Every feature that adds or changes federation workflows, transfers, persistence,
or external communication MUST define tracing, metrics, structured logs, and
correlation behavior in its plan. OpenTelemetry spans and metrics MUST use the
shared taxonomy in `JellyFederation.Shared.Telemetry`, include release/taxonomy
metadata where available, and redact secrets, API keys, paths, and raw exception
messages before export. New operational behavior MUST document how to diagnose
success, timeout, cancellation, and failure paths.

Rationale: Distributed media transfer issues cannot be debugged reliably without
consistent telemetry, but federation metadata can be sensitive.

### IV. Provider-Aware Persistence and Migrations
Data access MUST use EF Core with query tracking disabled by default and explicit
tracking or update calls for mutation paths. Database changes MUST include both
SQLite and PostgreSQL migrations in the provider-specific migration projects and
MUST preserve design-time factory support without requiring an ASP.NET startup
project. Queries that can grow with libraries, requests, or servers MUST include
appropriate filters, indexes, limits, and asynchronous cancellation support.

Rationale: The project supports local SQLite development and PostgreSQL
production, and media libraries can grow large enough for accidental tracking or
unbounded queries to become operational defects.

### V. Incremental, Tested Delivery
Features MUST be sliced by independently testable user stories and MUST include
contract or integration coverage for changed HTTP, SignalR, EF Core, migration,
transport, and plugin/server interactions. Tests MUST be written before or with
the implementation for the behavior they protect, and plans MUST identify the
minimal validation command set, normally `dotnet build JellyFederation.slnx` and
`dotnet test JellyFederation.slnx`. Substantial generated or refactored .NET code
MUST be reviewed with the repository's .NET skill guidance and Slopwatch where
applicable.

Rationale: Federation behavior crosses process and network boundaries; small,
tested increments keep regressions localized and releasable.

## Platform and Architecture Standards

- Runtime targets are .NET 10 for server/web/tests, .NET 9 for the Jellyfin
  plugin, and `net9.0;net10.0` for shared libraries that are consumed by both.
- Nullable reference types and implicit usings MUST stay enabled for C# projects.
- Central Package Management in `Directory.Packages.props` MUST remain the source
  of NuGet versions; project files reference packages without inline versions.
- Jellyfin-provided assemblies in the plugin MUST remain compile-time only or be
  stripped from publish output to avoid runtime binding conflicts.
- Dependency injection MUST prefer composable service registrations and small,
  focused services over service-locator patterns or large procedural entrypoints.
- Configuration MUST use strongly named sections, environment-variable-friendly
  keys, and safe defaults suitable for local development.
- Async I/O MUST accept and propagate `CancellationToken` where a caller can
  cancel work; blocking waits over tasks are prohibited in request, hub, transfer,
  and hosted-service paths.
- Security-sensitive data such as API keys, correlation-bearing identifiers,
  file paths, and peer information MUST never be logged or returned without an
  explicit redaction decision.

## Development Workflow and Quality Gates

1. Specifications MUST state user-visible outcomes, edge cases, success criteria,
   contract changes, telemetry expectations, security/privacy considerations, and
   data/migration impacts.
2. Plans MUST pass the Constitution Check before research/design and again after
   design. Any violation MUST be recorded in Complexity Tracking with the simpler
   alternative that was rejected.
3. Tasks MUST be grouped by user story, include exact repository paths, and keep
   each story independently buildable and testable.
4. Provider-specific migrations MUST be generated with:
   - `dotnet ef migrations add <Name> --project src/JellyFederation.Migrations.Sqlite`
   - `dotnet ef migrations add <Name> --project src/JellyFederation.Migrations.PostgreSQL`
5. Before completion, contributors MUST run the validation commands identified in
   the plan or explain why an environment-dependent command could not run.
6. Reviews MUST verify constitution compliance, stable contracts, privacy-safe
   telemetry, provider-aware persistence, and appropriate test coverage before
   merging.
7. Documentation in `README.md`, `docs/`, feature quickstarts, or generated API
   docs MUST be updated when behavior, configuration, operations, or contracts
   change.

## Governance

This constitution supersedes conflicting local conventions, generated templates,
and feature plans. Amendments require a pull request or explicit maintainer
approval that describes the change, the reason, the migration impact, and any
required updates to templates or runtime guidance.

Versioning follows semantic versioning:

- MAJOR: Removes or redefines a principle or governance rule in a way that can
  invalidate previously compliant work.
- MINOR: Adds a new principle, section, or materially expands required practices.
- PATCH: Clarifies wording, fixes errors, or updates references without changing
  obligations.

Compliance review is mandatory for every feature plan and code review. If a
feature cannot comply, the plan MUST document the violation, the operational or
product need, the rejected simpler alternative, and a follow-up owner/date when
possible. Ratification and amendment dates use ISO `YYYY-MM-DD` format.

**Version**: 1.0.0 | **Ratified**: 2026-04-24 | **Last Amended**: 2026-04-24
