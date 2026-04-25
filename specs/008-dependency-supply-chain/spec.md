# Feature Specification: Dependency and Supply-Chain Maintenance

**Feature Branch**: `008-dependency-supply-chain`  
**Created**: 2026-04-25  
**Status**: Draft  
**Input**: Repository audit report `docs/reviews/dependency-supply-chain.md`

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Remediate npm audit finding (Priority: P1)

A maintainer can update the web frontend dependency graph so the reported `postcss` vulnerability is fixed without breaking the frontend build.

**Why this priority**: The audit found one moderate npm vulnerability with a fix available.

**Independent Test**: Run `npm audit --audit-level=moderate` and `npm run build` in `src/JellyFederation.Web` after lockfile/package updates.

**Acceptance Scenarios**:

1. **Given** the current web lockfile, **When** dependencies are updated with the available fix, **Then** `postcss` resolves to a non-vulnerable version.
2. **Given** updated frontend dependencies, **When** `npm run build` executes, **Then** the production frontend build succeeds.

---

### User Story 2 - Add recurring dependency and vulnerability checks (Priority: P1)

A maintainer can rely on CI or documented local commands to detect vulnerable NuGet/npm dependencies before release.

**Why this priority**: Recurring checks prevent the same class of supply-chain issue from silently returning.

**Independent Test**: Run CI workflow or local script that executes NuGet vulnerability audit and npm audit/build commands with expected pass/fail behavior.

**Acceptance Scenarios**:

1. **Given** no known vulnerable NuGet packages, **When** the NuGet audit command runs, **Then** CI passes.
2. **Given** an npm moderate-or-higher vulnerability, **When** the npm audit step runs, **Then** CI fails or reports according to the configured threshold.

---

### User Story 3 - Improve container release reproducibility and scanning (Priority: P2)

A release operator can produce container images with pinned base image digests, regular rebuild guidance, optional SBOMs, and vulnerability scanning.

**Why this priority**: Floating base image tags reduce reproducibility and make release provenance harder to verify.

**Independent Test**: Build containers using pinned digest references or a documented release override, generate/validate an SBOM where configured, and run the selected container scanner in CI or release workflow.

**Acceptance Scenarios**:

1. **Given** a release Docker build, **When** the Dockerfile/base image configuration is inspected, **Then** base images are pinned by digest or resolved by a controlled release process.
2. **Given** a published image, **When** scanning runs, **Then** high/critical vulnerabilities fail the release or are documented with an approved exception.

---

### User Story 4 - Keep repository hygiene and secret scanning in place (Priority: P3)

A maintainer can prevent accidental commits of local databases, build outputs, generated docs output, editor settings, and secrets.

**Why this priority**: The audit did not find hygiene issues, but lightweight recurring checks reduce future risk.

**Independent Test**: Run repository hygiene and secret scanning commands locally/CI and verify intentionally ignored files are not tracked.

**Acceptance Scenarios**:

1. **Given** common generated/local files, **When** `git status` and hygiene checks run, **Then** ignored files remain untracked.
2. **Given** a test secret fixture, **When** secret scanning runs, **Then** the scanner detects it in test mode without blocking legitimate fixtures through approved allowlists.

---

### Edge Cases

- `npm audit fix` may update broad transitive dependencies; diffs must be inspected and the frontend build must pass.
- NuGet audit output can vary by SDK/source availability; CI should use stable SDK and package sources.
- Digest-pinned Dockerfiles require a process to refresh digests for security updates.
- Secret scanning must avoid false positives for documented test fixtures while still catching real credentials.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Web dependencies MUST be updated so `postcss <8.5.10` is no longer present in the resolved dependency graph.
- **FR-002**: Frontend build MUST succeed after dependency remediation.
- **FR-003**: Repository MUST provide recurring NuGet vulnerability audit and npm audit commands in CI or documented scripts.
- **FR-004**: CI SHOULD fail on high/critical vulnerabilities and at least warn or fail on moderate vulnerabilities according to documented policy.
- **FR-005**: Container release process SHOULD pin base image digests or document a controlled digest resolution/update process.
- **FR-006**: Release process SHOULD generate SBOMs for published images where practical.
- **FR-007**: CI/release workflow SHOULD include container image vulnerability scanning.
- **FR-008**: Repository hygiene checks SHOULD confirm local databases, build outputs, generated docs output, and editor-local settings are ignored.
- **FR-009**: Secret scanning SHOULD run for commits or pull requests with an allowlist process for intentional fixtures.

### Contract & Compatibility Requirements *(mandatory for federation/API/SignalR/storage changes)*

- **CR-001**: No federation HTTP/SignalR/storage contract changes are expected for dependency remediation.
- **CR-002**: Docker image tag/digest changes MUST preserve runtime behavior and documented deployment environment variables.
- **CR-003**: Package updates MUST not introduce browser bundle or API behavior changes without separate review.

### Failure & Error Requirements *(mandatory)*

- **ER-001**: Expected failures MUST map to stable CI job outcomes and actionable logs.
- **ER-002**: Vulnerability, build, scanner, SBOM, and hygiene failures MUST identify the failing package/image/path without exposing secrets.

### Observability & Privacy Requirements *(mandatory)*

- **OR-001**: CI artifacts/logs SHOULD include dependency audit summaries, scanner summaries, and SBOM locations.
- **OR-002**: Secret scanning output MUST redact detected secret values and show only fingerprints/locations as supported by the scanner.

### Data & Migration Requirements *(include if feature involves data)*

- **DR-001**: No application data or EF migrations are expected.
- **DR-002**: Lockfile changes are expected for npm remediation and must be reviewed.

### Key Entities *(include if feature involves data)*

- **Dependency Audit Workflow**: CI/local check that runs NuGet and npm vulnerability audits.
- **Container Release Workflow**: Build/scan/SBOM process for published images.
- **Repository Hygiene Check**: Guardrail for ignored/generated/local files and secret scanning.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: `cd src/JellyFederation.Web && npm audit --audit-level=moderate` reports no moderate-or-higher vulnerabilities.
- **SC-002**: `cd src/JellyFederation.Web && npm run build` succeeds after dependency updates.
- **SC-003**: `dotnet list JellyFederation.slnx package --vulnerable --include-transitive` is included in CI or documented recurring checks.
- **SC-004**: Container base image digest pinning or controlled update process is documented and validated in release workflow.
- **SC-005**: Secret scanning and hygiene checks are documented or automated with false-positive handling.

## Assumptions

- CI platform and release registry details may need confirmation before workflow implementation.
- The project may choose documented local recurring checks first if CI files are not present.
- Container digest pinning is most important for release pipelines; development Dockerfiles may retain convenience tags if documented.
