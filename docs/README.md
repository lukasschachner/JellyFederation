# Docs maintenance

This repository uses [DocFX](https://dotnet.github.io/docfx/) for conceptual + API documentation.

## Build locally

```bash
docfx docs/docfx.json
```

## Serve locally

```bash
docfx docs/docfx.json --serve
```

## CI-friendly validation command

```bash
docfx docs/docfx.json --disableGitFeatures
```

Optional strict mode:

```bash
docfx docs/docfx.json --warningsAsErrors --disableGitFeatures
```

Use strict mode once all warnings are resolved in CI/runtime.
