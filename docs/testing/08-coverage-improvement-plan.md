# Coverage improvement plan (PR-sized backlog)

_Last updated: 2026-04-26_

## Current baseline

From `scripts/coverage.sh` (merged server + plugin):

- Total line coverage: **~34%**
- Branch coverage: **~26%**
- Main gap: **`JellyFederation.Plugin`** (several core services near 0%)

## Strategy

1. Prioritize high-ROI, low-friction test targets (pure/near-pure logic).
2. Keep PRs small and coherent (one subsystem per PR).
3. Ratchet quality gate progressively toward 80% while keeping CI usable.
4. Re-run `scripts/coverage.sh` in every coverage PR and capture before/after deltas.

---

## Milestone targets

- Milestone 1: 45%
- Milestone 2: 55%
- Milestone 3: 65%
- Milestone 4: 75%
- Milestone 5: 80%

---

## PR backlog (10–15 items)

## P1 — Plugin-focused (largest gains)

### 1) test(plugin): transfer frame + header parsing edge matrix
**Files:**
- `src/JellyFederation.Plugin/Services/FileTransferService.cs`
- `tests/JellyFederation.Plugin.Tests/WebRtcComponentTests.cs` (or new `FileTransferServiceTests.cs`)

**Add tests for:**
- malformed header length
- unexpected frame kind / EOF behavior
- truncated payloads
- boundary sizes (0, 1, max allowed)

**Estimated gain:** +2–4 points global

---

### 2) test(plugin): library sync conflict and dedup behavior
**Files:**
- `src/JellyFederation.Plugin/Services/LibrarySyncService.cs`
- new `tests/JellyFederation.Plugin.Tests/LibrarySyncServiceTests.cs`

**Add tests for:**
- duplicate IDs in payload
- replace-all removes stale rows
- partial sync updates existing + inserts missing
- invalid payload short-circuit

**Estimated gain:** +2–4 points global

---

### 3) test(plugin): hole punch state machine transitions
**Files:**
- `src/JellyFederation.Plugin/Services/HolePunchService.cs`
- new `tests/JellyFederation.Plugin.Tests/HolePunchServiceTests.cs`

**Add tests for:**
- initial -> staged -> completed transitions
- retry timeout / cancellation handling
- duplicate candidacy handling
- stale session eviction

**Estimated gain:** +2–3 points global

---

### 4) test(plugin): SignalR service reconnect and dispatch guards
**Files:**
- `src/JellyFederation.Plugin/Services/FederationSignalRService.cs`
- new `tests/JellyFederation.Plugin.Tests/FederationSignalRServiceTests.cs`

**Add tests for:**
- reconnect path with queued notifications
- unauthorized/invalid message ignored
- callback routing to correct transfer/session
- cancellation token respect

**Estimated gain:** +2–3 points global

---

### 5) test(plugin): startup orchestration and safe failure handling
**Files:**
- `src/JellyFederation.Plugin/Services/FederationStartupService.cs`
- new `tests/JellyFederation.Plugin.Tests/FederationStartupServiceTests.cs`

**Add tests for:**
- startup order dependencies
- partial startup failure cleanup
- idempotent start/stop

**Estimated gain:** +1–2 points global

---

### 6) test(plugin): stream endpoint range validation matrix
**Files:**
- `src/JellyFederation.Plugin/Services/LocalStreamEndpoint.cs`
- new `tests/JellyFederation.Plugin.Tests/LocalStreamEndpointTests.cs`

**Add tests for:**
- valid range, invalid range, open-ended range
- out-of-bounds handling
- response headers for seekability

**Estimated gain:** +1–2 points global

---

### 7) test(plugin): plugin root/registration smoke paths
**Files:**
- `src/JellyFederation.Plugin/Plugin.cs`
- `src/JellyFederation.Plugin/PluginServiceRegistrator.cs`
- existing `tests/JellyFederation.Plugin.Tests/PluginComponentTests.cs`

**Add tests for:**
- plugin metadata consistency
- required service registrations present
- safe defaults when optional settings missing

**Estimated gain:** +1–2 points global

---

## P2 — Server hotspot closure

### 8) test(server): transfer selection rules and edge conditions
**Files:**
- `src/JellyFederation.Server/Services/TransferSelection*.cs`
- new `tests/JellyFederation.Server.Tests/TransferSelectionTests.cs`

**Add tests for:**
- mode selection precedence
- threshold boundary conditions
- null/unknown capability handling

**Estimated gain:** +1–2 points global

---

### 9) test(server): stale request cleanup behavior matrix
**Files:**
- `src/JellyFederation.Server/Services/StaleRequestCleanupService.cs`
- extend `tests/JellyFederation.Server.Tests/EfCoreQueryTests.cs` or new dedicated file

**Add tests for:**
- pending/hole-punch/transferring older-than-threshold
- terminal states excluded
- mutation persistence on tracked entities

**Estimated gain:** +1–2 points global

---

### 10) test(server): auth handler failure paths
**Files:**
- `src/JellyFederation.Server/Auth/FederationApiKeyAuthenticationHandler.cs`
- new `tests/JellyFederation.Server.Tests/FederationApiKeyAuthenticationHandlerTests.cs`

**Add tests for:**
- missing header
- malformed key
- unknown fingerprint
- signature mismatch
- success principal claims shape

**Estimated gain:** +1 point global

---

### 11) test(server): session controller boundary and validation coverage
**Files:**
- `src/JellyFederation.Server/Controllers/SessionsController.cs`
- extend `tests/JellyFederation.Server.Tests/ApiIntegrationTests.cs`

**Add tests for:**
- invalid payload envelope
- expired/invalid session behavior
- authorized success shape and status codes

**Estimated gain:** +1 point global

---

### 12) test(server): federation hub unauthorized routing matrix
**Files:**
- `src/JellyFederation.Server/Hubs/FederationHub.cs`
- extend `tests/JellyFederation.Server.Tests/SignalRWorkflowTests.cs`

**Add tests for:**
- non-participant progress relay rejection
- wrong sender for relay start/chunk
- cancellation by unrelated peer ignored

**Estimated gain:** +1–2 points global

---

## P3 — Shared library low-coverage closures

### 13) test(shared): OperationOutcomeExtensions combinator coverage
**Files:**
- `src/JellyFederation.Shared/Models/OperationOutcomeExtensions*.cs`
- new `tests/JellyFederation.Server.Tests/OperationOutcomeExtensionsTests.cs` (or shared tests project later)

**Add tests for:**
- `Map`, `Bind`, async variants
- success/failure passthrough
- null guard and mapper exceptions where relevant

**Estimated gain:** +1–2 points global

---

### 14) test(shared): trace context propagation roundtrip
**Files:**
- `src/JellyFederation.Shared/Telemetry/TraceContextPropagation.cs`
- new `tests/JellyFederation.Server.Tests/TraceContextPropagationTests.cs`

**Add tests for:**
- inject/extract roundtrip
- missing keys fallback
- malformed context safe handling

**Estimated gain:** +0.5–1 point global

---

### 15) test(shared): SignalR DTO constructors and invariants
**Files:**
- `src/JellyFederation.Shared/SignalR/*.cs`
- extend `tests/JellyFederation.Server.Tests/ContractValidationTests.cs`

**Add tests for:**
- default ctor + property initialization
- invariant-preserving serialization/deserialization
- non-null fields and enum stability

**Estimated gain:** +0.5–1 point global

---

## CI / gate rollout recommendation

Because current baseline is far below 80%, apply a temporary ratchet while work lands:

1. Keep 80% as final target.
2. Temporarily gate at current baseline + 5% increments (for example 40 → 50 → 60 → 70 → 80).
3. Raise threshold only after milestone completion.

If hard 80% must stay immediately, expect all PRs to fail until large plugin test expansion lands.

---

## Definition of done per coverage PR

- Tests added for one backlog item only (or tightly related pair).
- `./dev.sh test all` passes.
- `scripts/coverage.sh --min-line-coverage <current-milestone-threshold>` passes.
- PR description includes:
  - files touched
  - scenario matrix covered
  - line/branch coverage delta from `coverage/Summary.txt`
