# Architecture

JellyFederation is organized around a plugin/server split:

1. **Plugin (`JellyFederation.Plugin`)**
   - Runs inside Jellyfin
   - Handles local server integration and transfer orchestration
2. **Server (`JellyFederation.Server`)**
   - Exposes federation APIs
   - Hosts SignalR endpoints for coordination
3. **Shared (`JellyFederation.Shared`)**
   - Shared DTOs/contracts and cross-cutting types
4. **Data (`JellyFederation.Data`)**
   - EF Core entities and DbContext abstractions

## Documentation model

- Conceptual docs live under `docs/articles/`
- API docs are generated into `docs/api/` from `src/` project files
- Site navigation is controlled by `docs/toc.yml`

## API xref usage

DocFX supports API cross-references with `@` syntax, for example:

- `@JellyFederation.Shared`
- `@JellyFederation.Server`

Use these in conceptual docs when linking to generated API pages.
