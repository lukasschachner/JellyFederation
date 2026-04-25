# Dependency and Supply-Chain Contract Notes

## Application contract impact

No HTTP, SignalR, DTO, or EF storage contract changes are expected.

## Required checks

Local or CI commands:

```bash
dotnet restore JellyFederation.slnx
dotnet list JellyFederation.slnx package --vulnerable --include-transitive

cd src/JellyFederation.Web
npm audit --audit-level=moderate
npm run build
```

## Container release policy

- Release builds should use digest-pinned base images or a workflow that resolves and records immutable digests.
- High/critical image vulnerabilities should fail release unless an approved exception exists.
- SBOM generation should be enabled where practical.

## Privacy and output

- Scanner and secret-scan logs must not print secret values.
- CI artifacts should include summaries and SBOM locations, not credentials.
