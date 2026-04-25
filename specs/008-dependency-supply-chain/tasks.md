# Tasks: Dependency and Supply-Chain Maintenance

**Input**: Design documents from `/specs/008-dependency-supply-chain/`  
**Prerequisites**: plan.md, spec.md, contracts/dependency-supply-chain.md

## Phase 1: Setup

- [X] T001 Confirm CI system, workflow paths, container registry, and available scanner in `specs/008-dependency-supply-chain/plan.md`.
- [X] T002 Decide vulnerability thresholds for NuGet, npm, and container scanning.
- [X] T003 [P] Locate Dockerfiles and release workflow files.
- [X] T004 [P] Review current `.gitignore` and tracked generated/local files.

## Phase 2: User Story 1 - Remediate npm audit finding (P1)

- [X] T005 Run `cd src/JellyFederation.Web && npm audit fix` or targeted update to remediate `postcss <8.5.10`.
- [X] T006 Inspect `src/JellyFederation.Web/package*.json` diff for broad transitive changes.
- [X] T007 Run `cd src/JellyFederation.Web && npm audit --audit-level=moderate`.
- [X] T008 Run `cd src/JellyFederation.Web && npm run build`.

## Phase 3: User Story 2 - Recurring dependency checks (P1)

- [X] T009 Add or document `dotnet restore JellyFederation.slnx` and NuGet vulnerability audit recurring checks.
- [X] T010 Add or document npm audit/build recurring checks for `src/JellyFederation.Web`.
- [X] T011 Wire dependency checks into CI workflow if CI path is confirmed.
- [X] T012 Document failure thresholds and remediation expectations in `docs/` or CI comments.

## Phase 4: User Story 3 - Container reproducibility and scanning (P2)

- [X] T013 Pin Docker base images by digest or add release-time digest resolution process.
- [X] T014 Add/document scheduled base image refresh process.
- [X] T015 Add container vulnerability scanner to release workflow or document manual command.
- [X] T016 Add SBOM generation for published images where practical.
- [X] T017 Validate Docker build and scanner/SBOM commands.

## Phase 5: User Story 4 - Hygiene and secret scanning (P3)

- [X] T018 Update `.gitignore` for any missing local DB/build/generated/editor patterns.
- [X] T019 Add/document hygiene command to catch accidentally tracked local/generated files.
- [X] T020 Add/document secret scanning for commits/PRs with allowlist guidance.
- [X] T021 Validate scanner output redacts secret values or configure redaction/summary mode.

## Phase 6: Polish

- [X] T022 Update `docs/reviews/repo-audit-index.md` with remediation spec links/status.
- [X] T023 Run `dotnet list JellyFederation.slnx package --vulnerable --include-transitive`.
- [X] T024 Run `cd src/JellyFederation.Web && npm audit --audit-level=moderate && npm run build`.
- [X] T025 Run Docker/scanner/SBOM validation if configured.
