# PostgreSQL Testcontainers tests

SQLite is useful and fast, but production uses PostgreSQL. Add a second integration-test path using Testcontainers for provider-specific coverage.

## Goals

- Verify migrations apply cleanly on PostgreSQL.
- Verify performance indexes exist.
- Confirm EF queries involving enums, dates, and joins behave the same as SQLite.
- Catch SQLite-only query or schema assumptions.

## Suggested structure

```text
tests/JellyFederation.Server.Tests/
  SqliteServerTests.cs
  PostgreSqlServerTests.cs
  TestServerFactory.cs
  PostgreSqlTestServerFactory.cs
```

SQLite should remain the default fast path. PostgreSQL tests can be marked as an integration category if CI needs to run them separately.

## Notes

Use real infrastructure over mocks. Testcontainers should allocate random ports and clean up containers automatically. Prefer reusing a container within a test class when startup cost becomes significant, but keep data isolated between tests.
