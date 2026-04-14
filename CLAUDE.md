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
backend/
frontend/
tests/
```

## Commands

# Add commands for C# (.NET 9/10; `net9.0`, `net10.0`)

## Code Style

C# (.NET 9/10; `net9.0`, `net10.0`): Follow standard conventions

## Recent Changes
- 004-webrtc-datachannel: Added C# on .NET 9 (plugin), .NET 10 (server)
- 003-add-result-monads: Added C# on .NET 9/10 (`net9.0` for plugin/server, multi-targeted shared library) + ASP.NET Core MVC + SignalR, EF Core (SQLite), OpenTelemetry, Jellyfin plugin APIs
- 002-document-quic-support: Added C# on .NET 9 (plugin/server), with shared library multi-targeted net9.0/net10.0 + ASP.NET Core SignalR, EF Core (SQLite), OpenTelemetry, `System.Net.Quic` (BCL)


<!-- MANUAL ADDITIONS START -->

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
