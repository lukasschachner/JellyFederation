# Performance Opportunities

Date: 2026-04-25

Scope: static review of EF Core access patterns, list endpoints, SignalR transfer routing, library sync, plugin image-preview generation, and startup behavior.

## Positive patterns already present

- Most read queries use `AsNoTracking`.
- Several endpoints project directly to DTOs instead of loading complete entity graphs.
- Library browsing has page/page-size validation.
- SignalR receive size and relay chunk size are bounded.
- Plugin library sync coalesces concurrent sync requests with a semaphore/pending flag.
- The plugin caps embedded-preview size per image and per sync.

## High-value opportunities

### Add pagination to non-library list endpoints

Evidence:

- `FileRequestsController.List()` returns all matching requests.
- `InvitationsController.List()` returns all matching invitations.
- `ServersController.List()` returns all servers.

Risk:

- As data grows, these endpoints can become slow and memory-heavy.
- The frontend may receive more data than it can reasonably render.

Recommendations:

- Add `page`/`pageSize` or cursor pagination.
- Return `X-Total-Count` or a typed pagination envelope.
- Keep page size bounded, e.g. 1-500 as used by library endpoints.

### Optimize server-side library sync for large Jellyfin libraries

Evidence:

- `LibraryController.Sync` loads all existing items for the current server into a tracking dictionary:
  - `ToDictionaryAsync(m => m.JellyfinItemId)`
- It then loops through all incoming items and optionally removes stale items in memory.

Risk:

- Large libraries may produce high memory usage and long EF change-tracking times.
- `ReplaceAll` deletes stale items by materializing them first.

Recommendations:

- Add a unique index on `(ServerId, JellyfinItemId)`.
- Consider a generation-based sync approach:
  1. Create a new sync generation ID/timestamp.
  2. Upsert incoming rows and mark them with the generation.
  3. Delete old rows with `ExecuteDeleteAsync` where generation is stale.
- Consider provider-specific bulk upsert paths for PostgreSQL.
- Keep current EF implementation as a simple fallback for SQLite/dev.

### Move migrations out of web application startup

Evidence:

- `Program.cs` runs `db.Database.Migrate();` during app startup.

Risk:

- Startup can block on DB availability or long migrations.
- Multiple app replicas can race migrations.
- Operational failures become web app startup failures.

Recommendations:

- Use a dedicated migration service/job for production.
- If a simple local/dev path remains, use `MigrateAsync` and limit it to development/single-instance deployments.

## Medium-value opportunities

### Add endpoint-specific request-size limits

Evidence:

- Global Kestrel request body limit defaults to 300 MiB in `appsettings.json`.
- Library sync is the only endpoint expected to need large payloads.

Recommendations:

- Lower the global limit.
- Use endpoint/controller-specific limits for library sync.
- Split base metadata sync and image-preview sync into separate endpoints with separate limits.

### Bound and index search workloads

Evidence:

- Library search uses `LIKE '%term%'`, which is not index-friendly for leading-wildcard search.

Recommendations:

- Keep page-size caps.
- Consider provider-specific full-text search for PostgreSQL if search becomes hot.
- Consider normalized/search-vector columns for title search.
- For SQLite, evaluate FTS5 if local/server deployments need large indexed search.

### Review DataChannel and relay backpressure continuously

Prior WebRTC review notes already identify transport-level concerns. Keep these performance themes tracked:

- DataChannel sender backpressure.
- Relay fallback throughput and memory pressure.
- Bounded queues for receiving DataChannel messages.
- Explicit timeouts for negotiation/open states.

## Low-cost improvements

### Avoid raw API keys as memory-cache keys

Although primarily a security concern, using long raw API keys directly as cache keys is unnecessary. Cache by stable fingerprint/hash instead.

### Add cancellation tokens to controller and hub database calls

Many methods do not accept/pass `CancellationToken`. Adding request-abort cancellation improves resource cleanup under disconnects and cancelled requests.

Pattern:

```csharp
public async Task<ActionResult<List<MediaItemDto>>> Browse(..., CancellationToken cancellationToken)
{
    var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
}
```

For controllers, use `HttpContext.RequestAborted` where method parameters are inconvenient.

### Keep query splitting in mind for future Include-heavy queries

Current read endpoints mostly use projections. If future endpoints add multiple collection `Include`s, use `AsSplitQuery` or projection to avoid cartesian explosions.
