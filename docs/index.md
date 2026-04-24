# JellyFederation documentation

JellyFederation is a Jellyfin plugin and companion server for federated media discovery and transfer between Jellyfin instances.

## What you'll find here

- Setup and development basics for this repository
- High-level architecture and component boundaries
- Generated API reference for .NET projects under `src/`

## Quick links

- [Getting started](articles/getting-started.md)
- [Architecture](articles/architecture.md)
- [Testing strategy](testing/01-api-integration-tests.md)
- [API reference](api/index.md)

## Local docs commands

```bash
# Build docs (metadata + site)
docfx docs/docfx.json

# CI-friendly validation build
docfx docs/docfx.json --disableGitFeatures

# Optional strict mode (treat warnings as errors)
docfx docs/docfx.json --warningsAsErrors --disableGitFeatures

# Build and serve locally (http://localhost:8080 by default)
docfx docs/docfx.json --serve
```
