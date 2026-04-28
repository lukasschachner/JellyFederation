# Test quality gates

Run quality gates after each meaningful test expansion.

## Standard command

Run the repository quality gates with the helper script:

```bash
scripts/quality-gates.sh
```

The script executes these gates with clear progress output:

```bash
dotnet build JellyFederation.slnx --no-restore
dotnet test --project tests/JellyFederation.Server.Tests/JellyFederation.Server.Tests.csproj --no-restore
slopwatch analyze --fail-on warning
```

Use `scripts/quality-gates.sh --dry-run` to print the commands without executing them.

## Additional checks

When adding complex code or tests, run coverage analysis and inspect high-risk untested paths:

```bash
scripts/coverage.sh
```

This collects MTP coverage (`Microsoft.Testing.Extensions.CodeCoverage`) in Cobertura format for both test projects, then generates merged HTML + text summaries under `coverage/`.

A line-coverage gate is enforced at **80%** by default.

## Commit strategy

Add tests in coherent commits so failures are easy to diagnose. Suggested order:

1. `test(server): cover invitation lifecycle`
2. `test(server): cover library sync and browse filters`
3. `test(server): cover file request authorization`
4. `test(server): cover SignalR routing security`
5. `test(data): verify PostgreSQL migrations`

Each commit should keep the repository in a runnable state.
