# jellyfederation Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-04-12

## Active Technologies
- C# on .NET 9 (plugin/server), with shared library multi-targeted net9.0/net10.0 + ASP.NET Core SignalR, EF Core (SQLite), OpenTelemetry, `System.Net.Quic` (BCL) (002-document-quic-support)
- SQLite via EF Core (`FileRequests` table + migration for transfer metadata) (002-document-quic-support)

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
- 002-document-quic-support: Added C# on .NET 9 (plugin/server), with shared library multi-targeted net9.0/net10.0 + ASP.NET Core SignalR, EF Core (SQLite), OpenTelemetry, `System.Net.Quic` (BCL)

- 001-run-speckit-feature: Added C# (.NET 9/10; `net9.0`, `net10.0`) + ASP.NET Core, SignalR, Entity Framework Core (SQLite), Jellyfin plugin APIs, OpenTelemetry .NET SDK

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
