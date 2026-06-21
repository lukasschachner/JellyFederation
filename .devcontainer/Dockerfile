FROM mcr.microsoft.com/devcontainers/base:bookworm@sha256:9e35ebf156350ba25b117da6869907034b6e2437b1891e3a43395af0b7d3eda4

RUN apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client \
    && rm -rf /var/lib/apt/lists/*
