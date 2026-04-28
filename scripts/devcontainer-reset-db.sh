#!/usr/bin/env bash
set -euo pipefail

host="${POSTGRES_HOST:-postgres}"
port="${POSTGRES_PORT:-5432}"
database="${POSTGRES_DB:-jellyfederation}"
user="${POSTGRES_USER:-jellyfederation}"
export PGPASSWORD="${POSTGRES_PASSWORD:-jellyfederation}"

psql -h "$host" -p "$port" -U "$user" -d postgres \
  -v ON_ERROR_STOP=1 \
  -c "DROP DATABASE IF EXISTS \"$database\" WITH (FORCE);" \
  -c "CREATE DATABASE \"$database\" OWNER \"$user\";"

Database__Provider=PostgreSQL \
ConnectionStrings__Default="Host=$host;Port=$port;Database=$database;Username=$user;Password=$PGPASSWORD" \
dotnet dotnet-ef database update \
  --project src/JellyFederation.Migrations.PostgreSQL/JellyFederation.Migrations.PostgreSQL.csproj

echo "Reset and migrated PostgreSQL database '$database'."
