# Getting started

## Repository layout

- `src/JellyFederation.Plugin`: Jellyfin plugin
- `src/JellyFederation.Server`: federation API + SignalR hub
- `src/JellyFederation.Shared`: shared contracts
- `src/JellyFederation.Data`: EF Core data model
- `src/JellyFederation.Web`: frontend app

## Run the stack locally

Use the helper script from the repository root:

```bash
./dev.sh
```

For observability + local federation stack:

```bash
./dev.sh stack-up
```

## Build docs locally

Install DocFX (if not already installed):

```bash
dotnet tool install -g docfx
```

Then build docs:

```bash
docfx docs/docfx.json
```

Or run with local server:

```bash
docfx docs/docfx.json --serve
```
