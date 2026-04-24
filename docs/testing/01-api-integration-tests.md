# API integration tests

Use ASP.NET Core integration tests as the default strategy for server behavior.

The existing `tests/JellyFederation.Server.Tests` project uses `WebApplicationFactory<Program>` against the real ASP.NET Core app and SQLite. This should remain our primary test style for server endpoints because it exercises:

- routing
- authentication filters
- model validation
- EF Core queries
- migrations
- JSON serialization
- SignalR hub behavior
- error envelopes

## Next coverage targets

- Registration rejects invalid payloads.
- Invitation lifecycle:
  - send
  - accept
  - reject
  - cancel
  - duplicate invitation prevention
- File request lifecycle:
  - create request only between invited servers
  - reject request for non-federated servers
  - cancel request by requester
  - deny cancel by unrelated servers
- Library sync:
  - replace-all removes stale items
  - differential sync updates existing items
  - pagination validation
  - search/type filtering
  - `IsRequestable` toggling
- SignalR hub security:
  - only participants can forward ICE signals
  - only authorized participants can forward relay chunks
  - unrelated servers cannot mark transfers complete
  - offline peers are handled safely
