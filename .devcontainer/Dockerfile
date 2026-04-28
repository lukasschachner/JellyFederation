FROM mcr.microsoft.com/devcontainers/dotnet:1-10.0-bookworm

RUN apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client \
    && rm -rf /var/lib/apt/lists/*
