# Dependency and Supply-Chain Notes

Date: 2026-04-25

## NuGet vulnerability audit

Command:

```bash
dotnet list JellyFederation.slnx package --vulnerable --include-transitive
```

Result:

- No vulnerable NuGet packages were reported for the solution at review time.

Recommendation:

- Keep this command in CI if it is not already present.
- Consider failing CI on high/critical vulnerabilities and warning on moderate vulnerabilities.

## npm audit

Command:

```bash
cd src/JellyFederation.Web
npm audit --audit-level=moderate --json
```

Original result:

- 1 moderate vulnerability.

Finding remediated in `008-dependency-supply-chain`:

- Package: `postcss`
- Vulnerable range: `<8.5.10`
- Resolved version: `8.5.10`
- Advisory: `GHSA-qx2v-qp2m-jg93`
- Issue: XSS via unescaped `</style>` in CSS stringify output.

Validation:

```bash
cd src/JellyFederation.Web
npm audit --audit-level=moderate
npm run build
```

If future lockfile updates are broad, inspect the diff and run frontend lint/tests/build before committing.

## Docker supply-chain notes

Current Dockerfile uses floating base tags:

- `node:22-alpine`
- `mcr.microsoft.com/dotnet/sdk:10.0`
- `mcr.microsoft.com/dotnet/aspnet:10.0`

Implemented policy:

- The server image workflow resolves base image tags to immutable digests before Docker build.
- The Dockerfile accepts digest-qualified build arguments while retaining local-development defaults.
- Release images generate an SBOM artifact and run a high/critical vulnerability scan.
- See [Supply-chain maintenance](../supply-chain-maintenance.md) for refresh cadence and exception handling.

## Repository hygiene notes

No tracked `federation.db`, `bin/`, `obj/`, or common local settings files were detected by the quick `git ls-files` check used during review.

Recommendations:

- Keep local databases, generated docs output, build output, and editor-local settings ignored.
- Avoid committing generated API docs unless the project intentionally versions them.
- If generated docs are committed intentionally, consider a CI check that verifies they are up to date.

## Suggested recurring checks

```bash
dotnet restore JellyFederation.slnx
dotnet list JellyFederation.slnx package --vulnerable --include-transitive

cd src/JellyFederation.Web
npm audit --audit-level=moderate
npm run build
```

Additional recurring checks are documented in [Supply-chain maintenance](../supply-chain-maintenance.md), including container scanning, SBOM generation, repository hygiene checks, and redacted secret scanning.
