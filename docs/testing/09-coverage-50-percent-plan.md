# Plan to reach 50% line coverage

_Last updated: 2026-04-26_

## Target

- Current: ~45.1% line coverage
- Goal: **50.0%+** line coverage
- Delta needed: ~4.9 points (roughly 265 additional covered lines at current denominator)

## Strategy

Prioritize highest-ROI branches in large workflow/controller files first, then unlock plugin runtime coverage with small testability seams.

---

## PR1 — Expand `FederationHub` workflow matrix (est. +1.2 to +1.8)

**Files**
- `tests/JellyFederation.Server.Tests/SignalRWorkflowTests.cs`
- `src/JellyFederation.Server/Hubs/FederationHub.cs`

**Add tests for**
- `ReportHolePunchReady`: unknown connection / not-found request / override IP parse path
- `ForwardIceSignal`: payload-too-large guard, peer-offline path
- `RelaySendChunk`: payload-too-large guard, request-not-found path
- `ForwardRelayTransferStart`: request-not-found path
- `ReportTransferProgress`: request-not-found and unauthorized participant paths

---

## PR2 — Complete `FileRequestsController` branch matrix (est. +0.8 to +1.2)

**Files**
- `tests/JellyFederation.Server.Tests/ApiIntegrationTests.cs`
- `src/JellyFederation.Server/Controllers/FileRequestsController.cs`

**Add tests for**
- `Create`: owning server not found
- `Cancel`: not found / non-participant / completed already terminal
- `MarkCompleted`: not found / wrong actor / invalid state / success path with status fanout assertion
- `List`: pagination validation envelope shape + title resolution fallback branch

---

## PR3 — Close remaining server service/controller hotspots (est. +0.7 to +1.0)

**Files**
- `tests/JellyFederation.Server.Tests/FederationApiKeyAuthenticationHandlerTests.cs`
- `tests/JellyFederation.Server.Tests/SessionsControllerTests.cs`
- `tests/JellyFederation.Server.Tests/ServersControllerTests.cs`

**Add tests for**
- auth query token branch (`access_token`, legacy `apiKey` toggle behavior)
- challenge/forbidden envelope exact shape
- server list invalid pagination branch
- sessions delete cookie behavior + repeated delete idempotence

---

## PR4 — Plugin executable paths with minimal refactor seams (est. +1.0 to +1.6)

**Files**
- `src/JellyFederation.Plugin/Services/FederationSignalRService.cs`
- `src/JellyFederation.Plugin/Services/FederationStartupService.cs`
- new plugin tests

**Small seam refactor (testability-only)**
- Extract hub connection creation behind injectable factory
- Extract reconnect/resync callback logic into internal methods

**Add tests for**
- missing config early return path
- reconnect callback triggers resync
- cancel message dispatch calls holepunch/webrtc cancel handlers
- startup start/stop idempotence + event subscribe/unsubscribe flow

---

## PR5 — Plugin transfer/service helper coverage expansion (est. +0.5 to +0.9)

**Files**
- `tests/JellyFederation.Plugin.Tests/FileTransferServiceTests.cs`
- `tests/JellyFederation.Plugin.Tests/HolePunchServiceTests.cs`
- `tests/JellyFederation.Plugin.Tests/LibrarySyncServiceTests.cs`

**Add tests for**
- additional stream helper failure modes (`ReadExactlyAsync` multi-read behavior)
- data channel frame parser malformed inputs matrix
- `HolePunchService` parse/timeout-related helper branches
- library sync chunking boundary edge cases (exact threshold, empty set)

---

## Execution order

1. PR1 (Hub)
2. PR2 (FileRequestsController)
3. PR3 (auth/sessions/servers cleanup)
4. PR4 (plugin seam + runtime tests)
5. PR5 (plugin helper edge matrix)

Expected cumulative gain: ~4.2 to 6.5 points → should land at **50%+**.

---

## Quality gates per PR

Run:

```bash
./dev.sh test all
scripts/coverage.sh --min-line-coverage 0
```

After PR3, ratchet thresholds:

```bash
scripts/coverage.sh --min-line-coverage 47
# then 48, 49, and finally 50
```

Also run:

```bash
slopwatch analyze
```
