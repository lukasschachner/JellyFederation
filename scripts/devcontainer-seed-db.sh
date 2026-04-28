#!/usr/bin/env bash
set -euo pipefail

host="${POSTGRES_HOST:-postgres}"
port="${POSTGRES_PORT:-5432}"
database="${POSTGRES_DB:-jellyfederation}"
user="${POSTGRES_USER:-jellyfederation}"
export PGPASSWORD="${POSTGRES_PASSWORD:-jellyfederation}"

psql -h "$host" -p "$port" -U "$user" -d "$database" -v ON_ERROR_STOP=1 <<'SQL'
INSERT INTO "Servers" ("Id", "Name", "OwnerUserId", "ApiKey", "RegisteredAt", "LastSeenAt", "IsOnline") VALUES
  ('10000000-0000-0000-0000-000000000001', 'Local Dev Jellyfin', 'dev-local', 'dev-local-api-key', now(), now(), true),
  ('10000000-0000-0000-0000-000000000002', 'Remote Dev Jellyfin', 'dev-remote', 'dev-remote-api-key', now(), now(), false)
ON CONFLICT ("ApiKey") DO UPDATE SET
  "Name" = EXCLUDED."Name",
  "OwnerUserId" = EXCLUDED."OwnerUserId",
  "LastSeenAt" = EXCLUDED."LastSeenAt",
  "IsOnline" = EXCLUDED."IsOnline";

INSERT INTO "Invitations" ("Id", "FromServerId", "ToServerId", "Status", "CreatedAt", "RespondedAt") VALUES
  ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000002', 1, now(), now())
ON CONFLICT ("Id") DO UPDATE SET
  "Status" = EXCLUDED."Status",
  "RespondedAt" = EXCLUDED."RespondedAt";

INSERT INTO "MediaItems" ("Id", "ServerId", "JellyfinItemId", "Title", "Type", "Year", "Overview", "ImageUrl", "FileSizeBytes", "IsRequestable", "IndexedAt") VALUES
  ('30000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000002', 'dev-movie-001', 'Dev Movie', 0, 2026, 'Seeded devcontainer movie for PostgreSQL workflows.', null, 734003200, true, now()),
  ('30000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000002', 'dev-series-001', 'Dev Series', 1, 2026, 'Seeded devcontainer series for PostgreSQL workflows.', null, 0, true, now())
ON CONFLICT ("ServerId", "JellyfinItemId") DO UPDATE SET
  "Title" = EXCLUDED."Title",
  "Type" = EXCLUDED."Type",
  "Year" = EXCLUDED."Year",
  "Overview" = EXCLUDED."Overview",
  "FileSizeBytes" = EXCLUDED."FileSizeBytes",
  "IsRequestable" = EXCLUDED."IsRequestable",
  "IndexedAt" = EXCLUDED."IndexedAt";
SQL

echo "Seeded PostgreSQL database '$database'."
