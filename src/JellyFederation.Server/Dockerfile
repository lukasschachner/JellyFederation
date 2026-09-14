ARG NODE_IMAGE=node:24.21.0-alpine@sha256:be80f76cf40ec8e42b9bec49f60a55e0660f30af58d3e5a25530785b30ea67e2
ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0@sha256:2fa828c68761b1b8c23d7662dc134421b9d3b59fe1425fdbc80804e390cdb24d
ARG DOTNET_ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0@sha256:6a94333d37514e385650a3c81a55e5350b67253dbe136e9cf17e499c35606a8c

FROM ${NODE_IMAGE} AS frontend
WORKDIR /web
COPY src/JellyFederation.Web/package.json src/JellyFederation.Web/package-lock.json* ./
RUN npm ci
COPY src/JellyFederation.Web/ .
RUN npm run build

FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /src

COPY global.json Directory.Packages.props ./
COPY src/JellyFederation.Shared/JellyFederation.Shared.csproj src/JellyFederation.Shared/
COPY src/JellyFederation.Data/JellyFederation.Data.csproj src/JellyFederation.Data/
COPY src/JellyFederation.Migrations.Sqlite/JellyFederation.Migrations.Sqlite.csproj src/JellyFederation.Migrations.Sqlite/
COPY src/JellyFederation.Migrations.PostgreSQL/JellyFederation.Migrations.PostgreSQL.csproj src/JellyFederation.Migrations.PostgreSQL/
COPY src/JellyFederation.Server/JellyFederation.Server.csproj src/JellyFederation.Server/
RUN dotnet restore src/JellyFederation.Server/JellyFederation.Server.csproj

COPY src/JellyFederation.Shared/ src/JellyFederation.Shared/
COPY src/JellyFederation.Data/ src/JellyFederation.Data/
COPY src/JellyFederation.Migrations.Sqlite/ src/JellyFederation.Migrations.Sqlite/
COPY src/JellyFederation.Migrations.PostgreSQL/ src/JellyFederation.Migrations.PostgreSQL/
COPY src/JellyFederation.Server/ src/JellyFederation.Server/
COPY --from=frontend /web/dist/ src/JellyFederation.Server/wwwroot/
RUN dotnet publish src/JellyFederation.Server/JellyFederation.Server.csproj \
    -c Release -o /app/publish

FROM ${DOTNET_ASPNET_IMAGE} AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "JellyFederation.Server.dll"]
