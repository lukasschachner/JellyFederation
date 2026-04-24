# JellyFederation

JellyFederation is a Jellyfin plugin + companion server for federated media discovery and transfer between Jellyfin instances.

## Repository layout

- `src/JellyFederation.Plugin`: Jellyfin plugin (configuration page, startup service, sync, transfer).
- `src/JellyFederation.Server`: federation API + SignalR hub.
- `src/JellyFederation.Shared`: contracts shared by plugin and server.
- `src/JellyFederation.Data`: EF Core DbContext and persistence configuration.
- `src/JellyFederation.Migrations.Sqlite`: SQLite provider migrations.
- `src/JellyFederation.Migrations.PostgreSQL`: PostgreSQL provider migrations.
- `src/JellyFederation.Web`: frontend for server registration and management.
- `tests/`: plugin and server test projects.

## Project constitution

Development is governed by `.specify/memory/constitution.md`. Feature specs and
plans must preserve stable federation contracts, result-based failures,
privacy-safe OpenTelemetry, provider-aware EF Core migrations, and independently
testable increments.

## Local development

Use `./dev.sh` to build/deploy the plugin and run the local Jellyfin + federation stack.

Sensible default refresh (no args):

```bash
./dev.sh
```

This runs a full refresh: rebuild plugin+server, restart local federation server, deploy plugin to local and remote Jellyfin, and seed local+remote plugin configs from federation DB.

## Local federation + LGTM stack (via `dev.sh`)

`docker-compose.yml` includes:

- `lgtm` (Grafana + OTLP) on `http://localhost:3000` with OTLP gRPC at `localhost:4317`

Jellyfin topology remains:

- local standalone container managed by existing `dev.sh` commands (`setup/start/deploy/...`)
- remote production/test Jellyfin at `192.168.2.192` managed via `./dev.sh deploy-test`
- local federation server via `dotnet run` (`./dev.sh server`, also started by `./dev.sh stack-up`)

Start the stack with:

```bash
./dev.sh stack-up
```

Useful stack commands:

```bash
./dev.sh stack-status
./dev.sh stack-logs server
./dev.sh stack-logs local
./dev.sh stack-logs lgtm
./dev.sh stack-restart
./dev.sh stack-deploy
./dev.sh stack-down
```

For a media-request test flow:

1. Start local observability/server stack with `./dev.sh stack-up`.
2. Ensure local Jellyfin is running (`./dev.sh start`) and deploy plugin updates (`./dev.sh deploy`).
3. Deploy to the remote Jellyfin (`./dev.sh deploy-test 192.168.2.192 root`).
4. Register local + remote servers in JellyFederation web (`:5264`) so each plugin receives `ServerId` + `ApiKey`.
5. Trigger a federation media request and inspect traces/metrics in Grafana (`:3000`).

## Telemetry configuration

Server (`src/JellyFederation.Server/appsettings.json`, override with env vars):

- `Telemetry__ServiceName` (default: `jellyfederation-server`)
- `Telemetry__OtlpEndpoint` (default: `http://localhost:4317`)
- `Telemetry__SamplingRatio` (default: `1.0`)
- `Telemetry__EnableTracing` (default: `true`)
- `Telemetry__EnableMetrics` (default: `true`)
- `Telemetry__EnableLogs` (default: `true`)
- `Telemetry__RedactionEnabled` (default: `true`)

Plugin (`PluginConfiguration`):

- `TelemetryServiceName` (default: `jellyfederation-plugin`)
- `TelemetryOtlpEndpoint` (default: `http://localhost:4317`)
- `TelemetrySamplingRatio` (default: `1.0`)
- `EnableTracing` / `EnableMetrics` / `EnableLogs` / `RedactionEnabled`

## Large-file transport selection

The plugin now advertises transfer transport capabilities during hole-punch readiness:

- `PreferQuicForLargeFiles` (default: `true`) enables QUIC preference for eligible large files.
- `LargeFileQuicThresholdBytes` (default: `536870912`) controls the size threshold used for QUIC selection.

Server-side mode selection is deterministic per request:

1. Select `Quic` only when both peers advertise QUIC support and file size meets threshold.
2. Otherwise use `ArqUdp`.
3. Record selected mode, selection reason, transfer progress bytes, and failure category in file request state.

## Reliability trend and incident triage

1. Capture a baseline for `operations.total`, `operation.duration`, `timeouts.total`, `retries.total`, and `inflight`.
2. Compare the same metric series by `operation`, `component`, and `release` dimensions between releases.
3. During incidents, pivot from high timeout/error series to traces with matching `correlation_id`.
4. Use `federation.outcome`, `error.type`, and sanitized `error.message` tags to identify the failing stage quickly.

## Result-based workflow outcomes

- Core plugin/server workflows now use `OperationOutcome<T>` + `FailureDescriptor` for expected failures.
- Boundary translation happens through:
  - `ErrorContractMapper` (HTTP API)
  - `SignalRErrorMapper` (SignalR payloads)
- External error payloads include stable `code`, `category`, and sanitized `message` fields.

## Contributor guidance: outcome/error conventions

1. Prefer returning `OperationOutcome<T>` for expected failures (validation, not-found, conflict, connectivity).
2. Use stable failure codes (for example: `file_request.invalid_state`) and safe messages.
3. Keep raw exception details in logs/telemetry, not in client-facing contracts.
4. At boundaries, map `FailureDescriptor` with mapper services instead of ad-hoc `NotFound("...")`/`Conflict("...")`.
5. Use shared composition helpers (`Map`, `Bind`, `Match`, async variants) to avoid repetitive branching.

## License

See `LICENSE`.
