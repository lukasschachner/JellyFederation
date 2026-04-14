# jellyfederation Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-04-14

## Active Technologies
- C# on .NET 9 (plugin/server), with shared library multi-targeted net9.0/net10.0 + ASP.NET Core SignalR, EF Core (SQLite), OpenTelemetry, `System.Net.Quic` (BCL) (002-document-quic-support)
- SQLite via EF Core (`FileRequests` table + migration for transfer metadata) (002-document-quic-support)
- C# on .NET 9/10 (`net9.0` for plugin/server, multi-targeted shared library) + ASP.NET Core MVC + SignalR, EF Core (SQLite), OpenTelemetry, Jellyfin plugin APIs (003-add-result-monads)
- SQLite via EF Core (`FileRequest`, `Invitation`, `MediaItem`, `RegisteredServer`); in-memory state for active transfer sessions (003-add-result-monads)

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
- 003-add-result-monads: Added C# on .NET 9/10 (`net9.0` for plugin/server, multi-targeted shared library) + ASP.NET Core MVC + SignalR, EF Core (SQLite), OpenTelemetry, Jellyfin plugin APIs
- 002-document-quic-support: Added C# on .NET 9 (plugin/server), with shared library multi-targeted net9.0/net10.0 + ASP.NET Core SignalR, EF Core (SQLite), OpenTelemetry, `System.Net.Quic` (BCL)

- 001-run-speckit-feature: Added C# (.NET 9/10; `net9.0`, `net10.0`) + ASP.NET Core, SignalR, Entity Framework Core (SQLite), Jellyfin plugin APIs, OpenTelemetry .NET SDK

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
