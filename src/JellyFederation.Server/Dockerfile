ARG NODE_IMAGE=node:22-alpine
ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG DOTNET_ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0

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
