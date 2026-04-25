# Implementation Plan: Dependency and Supply-Chain Maintenance

**Branch**: `008-dependency-supply-chain` | **Date**: 2026-04-25 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/008-dependency-supply-chain/spec.md`

## Summary

Update frontend dependencies to remediate the reported `postcss` advisory, add recurring NuGet/npm vulnerability checks, improve container release reproducibility through digest pinning or controlled update process, add optional SBOM/scanning, and maintain repository hygiene/secret scanning guardrails.

## Technical Context

**Language/Version**: C# on .NET 9/10; Node 22 for web build.  
**Primary Dependencies**: NuGet packages via Central Package Management; npm/Vite frontend dependencies; Docker base images.  
**Storage**: N/A.  
**Testing**: `dotnet list ... --vulnerable`, npm audit/build, Docker build/scan, secret/hygiene checks.  
**Target Platform**: CI/release pipeline and containerized server images.  
**Project Type**: Repository maintenance and release engineering.  
**Performance Goals**: CI checks should be fast enough for PR use; heavier container scans/SBOM can run in release workflows.  
**Constraints**: Do not edit NuGet package XML manually; inspect lockfile diffs; avoid logging secrets; preserve runtime Docker behavior.  
**Scale/Scope**: Web dependency graph, CI scripts/workflows/docs, Dockerfile base images, repository hygiene.

## Constitution Check

- **Contract-First Federation Boundaries**: PASS — no app contract changes expected.
- **Result-Oriented Failure Handling**: PASS — CI failures are actionable and sanitized.
- **Observable and Privacy-Safe Operations**: PASS — audit/scanner output must redact secrets.
- **Provider-Aware Persistence and Migrations**: N/A — no data model changes.
- **Incremental, Tested Delivery**: PASS — npm remediation, recurring checks, container hardening, and hygiene are independently testable.
- **Platform Standards**: PASS — package changes use npm/dotnet CLI conventions and CI/release workflow documentation.

## Project Structure

```text
specs/008-dependency-supply-chain/
├── spec.md
├── plan.md
├── contracts/dependency-supply-chain.md
└── tasks.md

src/JellyFederation.Web/                    # package-lock/package update and build validation
Dockerfile / docker-related files           # base image pinning if present
.github/workflows/ or equivalent CI files   # dependency audit/scanning if present
docs/                                       # recurring checks and release process docs
```

**Structure Decision**: Treat npm remediation as the only code-adjacent change; place recurring checks in CI when workflow files exist, otherwise document scripts/commands until CI platform is confirmed.

## Action Plan

### Phase 0: Environment and policy decisions

1. Confirmed CI system: GitHub Actions in `.github/workflows/`.
2. Confirmed container registry: GHCR via `.github/workflows/docker.yaml` (`ghcr.io/${{ github.repository_owner }}/jellyfederation-server`).
3. Selected scanners/tools: NuGet vulnerability audit, npm audit, Trivy for container scanning, Anchore SBOM action for image SBOMs, and Gitleaks for secret scanning.
4. Decided thresholds: NuGet vulnerable package output fails dependency audit; npm fails at moderate or higher; container scans fail at high/critical unless an approved exception exists.
5. Decided Docker reproducibility approach: release workflow resolves base image tags to immutable digests and passes digest-qualified image references as Docker build args.
6. Decided SBOM retention: GitHub Actions artifact named `jellyfederation-server.spdx.json` for the published image.

### Phase 1: npm vulnerability remediation

1. Run `npm audit fix` or targeted npm update in `src/JellyFederation.Web`.
2. Inspect package and lockfile diffs for broad changes.
3. Run frontend audit and build.

### Phase 2: Recurring dependency checks

1. Add or document `dotnet restore`, NuGet vulnerability audit, npm audit, and npm build checks.
2. Wire checks into CI if workflow location is available.
3. Set thresholds and failure behavior.

### Phase 3: Container supply-chain improvements

1. Locate Dockerfiles and current base image tags.
2. Pin digests or add controlled digest resolution/update process.
3. Add container scanner and optional SBOM generation in release workflow.
4. Document rebuild cadence for base image updates.

### Phase 4: Repository hygiene and secret scanning

1. Review `.gitignore` for local DB/build/generated/editor patterns.
2. Add or document hygiene command using `git ls-files`/status checks.
3. Add or document secret scanning with allowlist guidance.

### Phase 5: Verification

1. Run all recurring checks locally or in CI.
2. Validate Docker build/scan/SBOM path where configured.
3. Update audit index/spec links if desired.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Digest pinning requires update process | Reproducibility and provenance require immutable image references | Floating tags are easier but do not satisfy release reproducibility |
| CI implementation may be deferred to docs | CI platform details may be absent | Adding incorrect workflow files could create nonfunctional automation |
