# Repository Audit Index

Date: 2026-04-25

This section captures static review notes for security, performance, refactoring, and dependency maintenance. The findings are based on a repository inspection and lightweight dependency audits, not on penetration testing or dynamic load testing.

## Review files

- [Security findings](security-findings.md) → Speckit spec: `specs/005-security-hardening/spec.md`
- [Performance opportunities](performance-opportunities.md) → Speckit spec: `specs/006-performance-scalability/spec.md`
- [Refactoring opportunities](refactoring-opportunities.md) → Speckit spec: `specs/007-refactoring-maintainability/spec.md`
- [Dependency and supply-chain notes](dependency-supply-chain.md) → Speckit spec: `specs/008-dependency-supply-chain/spec.md` → Runbook: [Supply-chain maintenance](../supply-chain-maintenance.md) (npm advisory remediated; recurring checks, image scan/SBOM, and secret scan added)

## Commands used during review

```bash
dotnet list JellyFederation.slnx package --vulnerable --include-transitive
cd src/JellyFederation.Web && npm audit --audit-level=moderate --json
```

Additional repository-wide static searches were performed for authentication, authorization, token handling, CORS, filesystem access, SignalR routing, SQL usage, and async/concurrency patterns.

## Suggested remediation order

1. Require an admin token in production and decide whether `GET /api/servers/{id}` should be public.
2. Hash API keys at rest and remove raw API keys from session payloads/cache keys.
3. Add rate limiting for session creation, failed authentication, and SignalR connection attempts.
4. Fix npm audit findings. (Completed for `postcss` in `008-dependency-supply-chain`.)
5. Tighten production CORS and cookie `Secure` behavior.
6. Validate/constrain media sync strings and remote image URLs.
7. Add EF model constraints and unique indexes, especially `(ServerId, JellyfinItemId)`.
8. Move migrations out of web application startup for production deployments.
9. Refactor custom auth into standard ASP.NET Core authentication/authorization.
10. Extract service layer logic from controllers and the SignalR hub.
