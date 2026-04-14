FROM node:22-alpine AS frontend
WORKDIR /web
COPY src/JellyFederation.Web/package.json src/JellyFederation.Web/package-lock.json* ./
RUN npm ci
COPY src/JellyFederation.Web/ .
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/JellyFederation.Shared/JellyFederation.Shared.csproj src/JellyFederation.Shared/
COPY src/JellyFederation.Server/JellyFederation.Server.csproj src/JellyFederation.Server/
RUN dotnet restore src/JellyFederation.Server/JellyFederation.Server.csproj

COPY src/JellyFederation.Shared/ src/JellyFederation.Shared/
COPY src/JellyFederation.Server/ src/JellyFederation.Server/
COPY --from=frontend /web/dist/ src/JellyFederation.Server/wwwroot/
RUN dotnet publish src/JellyFederation.Server/JellyFederation.Server.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "JellyFederation.Server.dll"]
