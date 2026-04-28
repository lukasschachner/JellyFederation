#!/usr/bin/env bash
set -euo pipefail

connection_string="${ConnectionStrings__Default:-Host=postgres;Port=5432;Database=jellyfederation;Username=jellyfederation;Password=jellyfederation}"

echo "Waiting for devcontainer PostgreSQL..."
until pg_isready -h postgres -p 5432 -U jellyfederation -d jellyfederation >/dev/null 2>&1; do
  sleep 1
done

echo "Applying PostgreSQL EF Core migrations..."
Database__Provider=PostgreSQL \
ConnectionStrings__Default="$connection_string" \
dotnet dotnet-ef database update \
  --project src/JellyFederation.Migrations.PostgreSQL/JellyFederation.Migrations.PostgreSQL.csproj

echo "Devcontainer PostgreSQL is ready and migrated."
