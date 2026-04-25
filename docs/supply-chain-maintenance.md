# Supply-chain maintenance

Date: 2026-04-25

Use this runbook to keep NuGet, npm, container images, generated artifacts, and secrets within the repository maintenance policy.

## Dependency audit policy

Run the recurring dependency checks before release and for pull requests that change package manifests, lockfiles, Dockerfiles, or workflow files.

```bash
dotnet restore JellyFederation.slnx
dotnet list JellyFederation.slnx package --vulnerable --include-transitive

cd src/JellyFederation.Web
npm audit --audit-level=moderate
npm run build
```

Thresholds:

- NuGet: fail CI for any reported vulnerable package. Remediate or document an approved exception before release.
- npm: fail CI for moderate, high, or critical advisories through `npm audit --audit-level=moderate`.
- Containers: fail release image scans for high or critical vulnerabilities unless an approved exception exists.

When `npm audit fix` changes `package-lock.json`, inspect the diff and run the frontend build before committing.

## Container release reproducibility

Release workflows should build from immutable base image references. The GitHub Actions server-image workflow resolves these tags to digests at build time and passes the digest-qualified references into the Docker build:

- `node:22-alpine`
- `mcr.microsoft.com/dotnet/sdk:10.0`
- `mcr.microsoft.com/dotnet/aspnet:10.0`

Keep the human-readable tags in the Dockerfile defaults for local development, but use release workflow logs and image labels as the record of the resolved digest used for a published image.

Refresh process:

1. Rebuild the server image at least monthly and after upstream Node or .NET base image security notices.
2. Review the workflow's resolved base-image digests.
3. Run the Trivy image scan and review the SBOM artifact for the pushed image.
4. If a high/critical issue is unavoidable, document the CVE, affected image layer/package, reason for exception, owner, and expiry date in the release notes or issue tracker.

## SBOM and image scanning

The server image workflow publishes an SBOM artifact for the pushed image and runs Trivy with this release threshold:

```bash
trivy image --severity HIGH,CRITICAL --exit-code 1 ghcr.io/<owner>/jellyfederation-server:<tag>
```

Local operators may use equivalent scanners. Scanner output must identify package names, versions, and CVEs without printing credentials.

## Repository hygiene

Check that local databases, generated docs output, build outputs, editor settings, and frontend build artifacts are ignored or intentionally tracked:

```bash
git status --ignored --short
git ls-files | rg '(^|/)(bin|obj|node_modules|dist|coverage|TestResults|docs/_site|\.vs|\.vscode|\.idea)/|\.(db|sqlite|sqlite3)$|\.env'
```

If the second command prints files, confirm whether they are intentional fixtures. Remove accidental generated/local files with `git rm --cached <path>` after adding the appropriate ignore pattern.

## Secret scanning

Run secret scanning locally before release or when credential-handling code changes:

```bash
docker run --rm -v "$PWD:/repo" zricethezav/gitleaks:latest detect --source=/repo --redact --verbose
```

CI uses Gitleaks with redaction enabled. Intentional test fixtures require an allowlist entry with a short justification. Do not allowlist real credentials; rotate and remove them instead.
