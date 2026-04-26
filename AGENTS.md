# jellyfederation Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-04-15

## Active Technologies
- C# on .NET 9 (plugin/server), with shared library multi-targeted net9.0/net10.0 + ASP.NET Core SignalR, EF Core (SQLite), OpenTelemetry, `System.Net.Quic` (BCL) (002-document-quic-support)
- SQLite via EF Core (`FileRequests` table + migration for transfer metadata) (002-document-quic-support)
- C# on .NET 9/10 (`net9.0` for plugin/server, multi-targeted shared library) + ASP.NET Core MVC + SignalR, EF Core (SQLite), OpenTelemetry, Jellyfin plugin APIs (003-add-result-monads)
- SQLite via EF Core (`FileRequest`, `Invitation`, `MediaItem`, `RegisteredServer`); in-memory state for active transfer sessions (003-add-result-monads)
- C# on .NET 9 (plugin), .NET 10 (server) (004-webrtc-datachannel)
- No new storage. `FileRequest` EF entity gains a `TransportMode` column (already exists as `SelectedTransportMode`). (004-webrtc-datachannel)

- C# (.NET 9/10; `net9.0`, `net10.0`) + ASP.NET Core, SignalR, Entity Framework Core (SQLite), Jellyfin plugin APIs, OpenTelemetry .NET SDK (001-run-speckit-feature)

## Project Structure

```text
src/JellyFederation.Plugin/                 # Jellyfin plugin (.NET 9)
src/JellyFederation.Server/                 # ASP.NET Core API + SignalR server (.NET 10)
src/JellyFederation.Shared/                 # Shared DTOs/models/SignalR/telemetry (net9.0;net10.0)
src/JellyFederation.Data/                   # EF Core DbContext/model configuration
src/JellyFederation.Migrations.Sqlite/      # SQLite migrations + design-time factory
src/JellyFederation.Migrations.PostgreSQL/  # PostgreSQL migrations + design-time factory
src/JellyFederation.Web/                    # Server registration/management frontend
tests/JellyFederation.Plugin.Tests/
tests/JellyFederation.Server.Tests/
```

## Commands

```bash
dotnet build --solution JellyFederation.slnx
dotnet test --solution JellyFederation.slnx
./dev.sh build all
./dev.sh test all
./dev.sh stack-up
./dev.sh stack-status
```

## Code Style

Follow `.specify/memory/constitution.md`: stable shared contracts, result-based expected failures,
privacy-safe OpenTelemetry, provider-aware EF Core, async/cancellable I/O, nullable-enabled modern C#.

## Recent Changes
- 004-webrtc-datachannel: Added C# on .NET 9 (plugin), .NET 10 (server)
- 003-add-result-monads: Added C# on .NET 9/10 (`net9.0` for plugin/server, multi-targeted shared library) + ASP.NET Core MVC + SignalR, EF Core (SQLite), OpenTelemetry, Jellyfin plugin APIs
- 002-document-quic-support: Added C# on .NET 9 (plugin/server), with shared library multi-targeted net9.0/net10.0 + ASP.NET Core SignalR, EF Core (SQLite), OpenTelemetry, `System.Net.Quic` (BCL)


<!-- MANUAL ADDITIONS START -->
# Agent Guidance: dotnet-skills

IMPORTANT: Prefer retrieval-led reasoning over pretraining for any .NET work.
Workflow: skim repo patterns -> consult dotnet-skills by name -> implement smallest-change -> note conflicts.

Routing (invoke by name)
- C# / code quality: modern-csharp-coding-standards, csharp-concurrency-patterns, api-design, type-design-performance
- ASP.NET Core / Web (incl. Aspire): aspire-service-defaults, aspire-integration-testing, transactional-emails
- Data: efcore-patterns, database-performance
- DI / config: dependency-injection-patterns, microsoft-extensions-configuration
- Testing: testcontainers-integration-tests, playwright-blazor-testing, snapshot-testing

Quality gates (use when applicable)
- dotnet-slopwatch: after substantial new/refactor/LLM-authored code
- crap-analysis: after tests added/changed in complex code

Specialist agents
- dotnet-concurrency-specialist, dotnet-performance-analyst, dotnet-benchmark-designer, akka-net-specialist, docfx-specialist

## Release hygiene (plugin deploys)

For any plugin change that is intended for deployment (local or remote), do **all** of the following:

1. Bump plugin version in `src/JellyFederation.Plugin/JellyFederation.Plugin.csproj`:
   - `<Version>`
   - `<AssemblyVersion>`
   - `<FileVersion>`
2. Commit the version bump as a dedicated release/chore commit.
3. Create an annotated git tag for that version (for example `v1.3.1` or `v1.3.1.0`).
4. Deploy after tagging (`./dev.sh deploy` for local, `./dev.sh deploy-test ...` for remote).

Do not ship deploy-intended plugin changes without a version bump + tag.

## dev.sh: Build/Test usage (Microsoft Testing Platform aware)

`dev.sh` now includes explicit build/test entrypoints. Prefer these for local developer workflows and agent automation.

### Build commands

```bash
./dev.sh build                    # same as: ./dev.sh build all
./dev.sh build all               # dotnet build --solution JellyFederation.slnx
./dev.sh build server            # build server project only
./dev.sh build plugin            # build plugin project only
./dev.sh build web               # frontend build + copy dist to server wwwroot

# pass through additional dotnet build args
./dev.sh build all -c Release
./dev.sh build server -v minimal
```

### Test commands

```bash
./dev.sh test                    # same as: ./dev.sh test all
./dev.sh test all               # dotnet test --solution JellyFederation.slnx
./dev.sh test server            # server tests only
./dev.sh test plugin            # plugin tests only
```

### Important: Microsoft Testing Platform (MTP) CLI differences

This repository uses **Microsoft Testing Platform** (`xunit.v3.mtp-v2`).

1. Use `dotnet test --solution <slnx>` or `dotnet test --project <csproj>`.
   - Positional forms like `dotnet test JellyFederation.slnx` are not valid in this setup.
2. VSTest-era assumptions do not always apply; some runner arguments differ.
3. Keep two argument lanes in mind:
   - **dotnet test args** (before `--`), e.g. `--filter`, `--list-tests`, `-c Release`
   - **MTP runner args** (after `--`), e.g. `-- --max-parallel-test-modules 1`

### Test filtering differences (MTP vs VSTest)

`--filter` / `FullyQualifiedName~...` is a VSTest pattern and **does not work** here.
Use MTP/xUnit v3 filter options instead:

- `--filter-class "*SignalRWorkflowTests"`
- `--filter-method "*SignalRWorkflowTests*BothPeersConnectSuccessfully"`
- `--filter-trait "Category=Integration"`
- `--filter-query "/**/*/*/*[name~SignalRWorkflowTests]"` (xUnit query language)

Examples:

```bash
./dev.sh test server --list-tests
./dev.sh test server --filter-class "*SignalRWorkflowTests"
./dev.sh test server --filter-query "/**/*/*/*[name~SignalRWorkflowTests]"
./dev.sh test server -- --max-parallel-test-modules 1
```

When in doubt, prefer `./dev.sh test <target> ...` so command shape stays consistent.

## EF Core Migrations

Migrations are split into two provider-specific projects, each with an `IDesignTimeDbContextFactory`:
- `src/JellyFederation.Migrations.Sqlite/` — SQLite provider
- `src/JellyFederation.Migrations.PostgreSQL/` — Npgsql/PostgreSQL provider

Adding a migration:
```bash
dotnet ef migrations add <Name> --project src/JellyFederation.Migrations.Sqlite
dotnet ef migrations add <Name> --project src/JellyFederation.Migrations.PostgreSQL
```

`--startup-project` is **not needed** because both projects implement `IDesignTimeDbContextFactory`, which supplies the connection string and provider registration at design time without the ASP.NET Core host.

At runtime the provider is selected via `Database:Provider` config (`"Sqlite"` or `"PostgreSQL"`). Production sets `Database__Provider=PostgreSQL` and `ConnectionStrings__Default=...` as environment variables.

<!-- MANUAL ADDITIONS END -->

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
<!-- SPECKIT END -->
