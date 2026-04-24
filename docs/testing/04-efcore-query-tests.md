# EF Core query tests

Protect query behavior that relies on projections, no-tracking reads, joins, and provider translation.

## Coverage targets

- Listing file requests resolves media titles by `(OwningServerId, JellyfinItemId)` so duplicate Jellyfin IDs from different servers do not collide.
- Browsing only returns media from accepted invitations.
- Stale cleanup only fails pending, hole-punching, or transferring requests older than the threshold.
- Important indexes are represented in model metadata.

## Test style

Use a real EF Core context and seed data directly. SQLite is sufficient for fast query-shape coverage, while PostgreSQL Testcontainers should cover provider-specific behavior.

Mutation paths should explicitly verify that tracked queries save changes correctly when the application defaults to no-tracking reads.
