# Performance and Scalability Contract Notes

## List pagination

Affected endpoints:

- `FileRequestsController.List`
- `InvitationsController.List`
- `ServersController.List`

Required behavior:

- Add `page` and `pageSize` query parameters with documented defaults and maximums.
- Preserve existing filters and authorization.
- Return total metadata using response headers to preserve the existing raw-array response contract:
  - `X-Total-Count`: total rows matching filters before pagination.
  - `X-Page`: effective 1-based page number.
  - `X-Page-Size`: effective page size.
  - `X-Total-Pages`: ceiling of total/page-size.
- Defaults: `page=1`, `pageSize=100`. Maximum page size: `500`. Invalid values return a stable validation failure instead of clamping.
- Use stable sorting to prevent duplicate/missing rows during pagination as much as possible. Operational lists sort by `CreatedAt DESC, Id ASC`; server lists sort by `RegisteredAt DESC, Id ASC`.

## Library sync

- Add uniqueness for `(ServerId, JellyfinItemId)`.
- Replace-all semantics remain: current incoming set becomes authoritative when requested.
- Provider-neutral sync uses bounded incoming batches, updates `IndexedAt` to the sync start timestamp for every processed incoming item, and deletes stale rows with a set-based `ExecuteDeleteAsync` predicate (`ServerId` + older `IndexedAt`). No additional generation column is required initially.
- Duplicate incoming item IDs are rejected with a stable validation failure code (`library.sync.duplicate_item`) rather than silently choosing a winner.

## Startup migrations

- Production web startup must not apply migrations automatically.
- A dedicated migration job/service/command is the supported production path.
- Development auto-migrate may remain behind explicit environment/config checks.

## Request-size limits

- Global request body limit defaults to 10 MiB unless configured.
- Library sync receives an explicit larger limit (100 MiB by default); no image preview upload endpoint exists in this server at implementation time.
- Request-too-large failures are stable and sanitized by ASP.NET Core's 413 response semantics; application validation failures use the standard failure contract.
