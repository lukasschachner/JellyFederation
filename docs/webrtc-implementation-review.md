# WebRTC Implementation Review

Date: 2026-04-24

## Scope

Reviewed the WebRTC/DataChannel implementation across:

- `src/JellyFederation.Plugin/Services/WebRtcTransportService.cs`
- `src/JellyFederation.Plugin/Services/FileTransferService.cs`
- `src/JellyFederation.Plugin/Services/LocalStreamEndpoint.cs`
- `src/JellyFederation.Server/Hubs/FederationHub.cs`
- `src/JellyFederation.Shared/SignalR/FederationMessages.cs`
- `specs/004-webrtc-datachannel/*`

Validation performed:

```bash
dotnet build JellyFederation.slnx
```

Result: build succeeds with no warnings or errors.

## Overall Assessment

The implementation is a solid first skeleton. It adds capability negotiation, SignalR-based ICE signaling, SIPSorcery peer connections, WebRTC DataChannel file transfer, and a relay fallback path. The server-side routing and participant checks are generally sensible, and old peers can still fall back to the existing transport path when `SupportsIce=false`.

However, the implementation is not yet production-ready. Several issues can cause dropped negotiations, unreliable fallback, memory growth, incorrect transfer reporting, incomplete streaming behavior, and fragile file framing under real-world network conditions.

The highest-risk areas are:

1. WebRTC session lifecycle and cancellation ownership.
2. Signaling races between offer, answerer session creation, and trickled candidates.
3. Lack of transfer backpressure.
4. Ad-hoc file framing over DataChannel.
5. Relay fallback metadata/status inaccuracies.
6. Incomplete direct-streaming support.

---

## Critical Issues

### 1. Session `CancellationTokenSource` Lifetime Is Incorrect

In `WebRtcTransportService.BeginAsOffererAsync`, `BeginAsAnswererAsync`, and `StartStreamingTransferAsync`, a CTS is created with `using var` and then stored in the session:

```csharp
using var cts = new CancellationTokenSource(IceTimeoutMs);
var session = new IceNegotiationSession(fileRequestId, pc, IceRole.Offerer, cts);
_sessions[fileRequestId] = session;
```

The WebRTC callbacks run after these methods return, but the CTS has already been disposed. Later callbacks still use `cts.Token` and `session.Cts.Cancel()`.

Potential effects:

- `ObjectDisposedException`
- transfer tasks receiving invalid/disposed cancellation state
- unreliable cleanup
- ineffective timeout behavior

Recommendation:

- Do not use `using var` for session-owned CTS instances.
- Let `IceNegotiationSession` own the CTS.
- Dispose the CTS in `CleanupSession`.
- Ideally make the session disposable.

Example direction:

```csharp
var cts = new CancellationTokenSource(IceTimeoutMs);
var session = new IceNegotiationSession(fileRequestId, pc, IceRole.Offerer, cts);
_sessions[fileRequestId] = session;
```

Then during cleanup:

```csharp
session.Cts.Cancel();
session.Cts.Dispose();
session.PeerConnection.Close("cleanup");
```

---

### 2. Offer Can Arrive Before the Answerer Session Exists

The server currently dispatches ICE negotiation start in this order:

```csharp
await Clients.Client(sender.ConnectionId).SendAsync("IceNegotiateStart", ... Offerer ...);
await Clients.Client(receiver.ConnectionId).SendAsync("IceNegotiateStart", ... Answerer ...);
```

The sender can create and forward the SDP offer before the receiver has created its `IceNegotiationSession`. If the offer arrives early, the receiver logs and drops it:

```csharp
ICE signal arrived but no session found — dropping
```

This can deadlock negotiation.

Recommendations:

- At minimum, notify the answerer first, then the offerer.
- Preferably buffer early ICE signals on the plugin side by `FileRequestId`.
- Best option: introduce a small readiness handshake where the answerer acknowledges it is ready before the offerer starts.

Minimum server-side improvement:

```csharp
await Clients.Client(receiver.ConnectionId).SendAsync(
    "IceNegotiateStart",
    new IceNegotiateStart(request.Id, IceRole.Answerer));

await Clients.Client(sender.ConnectionId).SendAsync(
    "IceNegotiateStart",
    new IceNegotiateStart(request.Id, IceRole.Offerer));
```

This reduces but does not fully eliminate the race.

---

### 3. ICE Candidates Are Not Buffered Before Remote Description Is Set

`HandleIceSignal` immediately applies incoming candidates:

```csharp
session.PeerConnection.addIceCandidate(...);
```

If candidates arrive before the remote SDP is applied, candidate addition may fail. The current implementation logs and drops failed candidates.

Recommendation:

- Track whether remote description has been applied.
- Queue candidates until after `setRemoteDescription` succeeds.
- Flush queued candidates after applying the remote description.

This is standard for robust trickle ICE implementations.

---

### 4. Offerer Timeout Does Not Reliably Trigger Relay Fallback

`IceTimeoutMs` exists, but the offerer path does not explicitly wait for the DataChannel to open. It sends the offer and returns. Fallback depends mostly on `onconnectionstatechange` reaching `failed` or `disconnected`.

Some failed network paths can remain in checking/connecting states longer than desired, and the disposed CTS issue further weakens timeout behavior.

Recommendation:

- Add an explicit DataChannel-open timeout per session.
- If the channel is not open within the timeout, trigger sender-side relay fallback.
- Mark the session as failed or relay before starting relay mode.

---

### 5. DataChannel Sending Has No Backpressure

`FileTransferService.SendDataChannelAsync` sends chunks in a tight loop:

```csharp
dc.send(buffer[..bytesRead]);
```

There is no pacing based on DataChannel buffer size and no application-level flow control. For large files or slow receivers, memory can grow inside the DataChannel/SCTP stack.

This conflicts with the requirement that transfers should not exceed bounded memory overhead per active session.

Recommendations:

- Use SIPSorcery/DataChannel buffered amount APIs if available.
- Configure a low buffered amount threshold if supported.
- Pause file reads while the DataChannel send buffer is above a threshold.
- If necessary, implement application-level ACK/windowing.

---

### 6. Receive Paths Use Unbounded Channels

Examples:

```csharp
Channel.CreateUnbounded<byte[]>(...)
Channel.CreateUnbounded<RelayChunk>(...)
```

A fast sender can enqueue unlimited data while the receiver writes slowly to disk or a pipe.

Recommendations:

- Use bounded channels.
- Size the bound according to a memory budget, not only message count.
- Pair bounded receive queues with sender-side backpressure where possible.

---

### 7. File Framing Is Fragile

The current DataChannel protocol is:

```text
header JSON -> binary chunks -> binary EOF magic
```

Problems:

- A real file chunk that exactly equals `EofMagic` is treated as EOF.
- Streaming drops any frame whose first byte is `{`, assuming it is the JSON header.
- There is no explicit frame type.
- There is no length prefix.
- There is no protocol version.
- There is no integrity verification.

Recommendation:

Replace magic-value framing with explicit frames.

Example:

```csharp
public enum TransferFrameType : byte
{
    Header = 1,
    Data = 2,
    End = 3,
    Error = 4
}
```

Frame format could be:

```text
byte Version
byte FrameType
int PayloadLength
byte[] Payload
```

Alternatively, use a schema-based envelope such as MessagePack or Protobuf.

---

### 8. Relay Receiver Expects a Header That Relay Sender Never Sends

`ReceiveRelayAsync` expects a special first header chunk:

```csharp
// Data = JSON bytes of FileHeader, ChunkIndex = -1
```

But `SendRelayAsync` starts sending file bytes at chunk index `0` and never sends the header:

```csharp
long chunkIndex = 0;

while (...)
{
    await connection.SendAsync("RelaySendChunk",
        new RelayChunk(fileRequestId, chunkIndex, false, buffer[..bytesRead]),
        ct);
    chunkIndex++;
}
```

Effects:

- Relay transfers lose original file name.
- Progress cannot know total size.
- Receiver falls back to `relay-{fileRequestId}`.

Recommendation:

Send a header chunk first:

```csharp
var header = JsonSerializer.SerializeToUtf8Bytes(
    new FileHeader(fileInfo.Name, fileInfo.Length));

await connection.SendAsync(
    "RelaySendChunk",
    new RelayChunk(fileRequestId, -1, false, header),
    ct);
```

Also validate the maximum relay chunk size server-side.

---

### 9. Relay Fallback Does Not Update Persistent Transfer Mode

The server records WebRTC when negotiation starts:

```csharp
request.SelectedTransportMode = TransferTransportMode.WebRtc;
request.TransportSelectionReason = TransferSelectionReason.IceNegotiated;
```

When ICE later fails and relay is used, the database is not updated to `Relay` / `IceFailed`. The UI can therefore report WebRTC even when the transfer used relay.

This conflicts with the feature requirement that the user can see the actual transport mode.

Recommendations:

- Add a hub/API method for transport mode updates, or
- Update the request when `ForwardRelayTransferStart` is accepted.

Example desired persisted state:

```csharp
request.SelectedTransportMode = TransferTransportMode.Relay;
request.TransportSelectionReason = TransferSelectionReason.IceFailed;
```

Then notify clients with the existing status notifier.

---

### 10. Successful Sessions Are Not Cleaned Up

`CleanupSession` is used for cancellation and timeout, but normal successful transfer completion does not remove sessions. `dc.onclose` only logs.

Effects:

- `_sessions` can grow indefinitely.
- Peer connections may remain referenced longer than needed.
- Future transfers with the same ID may behave unpredictably.

Recommendations:

- Cleanup after successful send/receive completion.
- Cleanup after EOF and completion reporting.
- Cleanup on DataChannel close after terminal state.
- Make cleanup idempotent.

---

## Streaming-Specific Gaps

### 11. `GetStreamUrl` Is a Stub

`WebRtcTransportService.GetStreamUrl` currently always returns null:

```csharp
return null;
```

So the task to expose active stream URLs is not functionally complete.

Recommendation:

- Store `fileRequestId -> streamUrl` or `fileRequestId -> token`.
- Remove the mapping when the stream completes or is cancelled.

---

### 12. Streaming Does Not Support Seeking

The spec requires seeking support:

> Given streaming is in progress, when the user seeks to a different position, playback resumes from the new position.

`LocalStreamEndpoint` serves a forward-only `PipeReader`. It does not parse or honor HTTP `Range` requests. Jellyfin and media players commonly use range requests for seeking and buffering.

Recommendation:

Decide whether seekable playback is required for MVP.

If yes, the design needs:

- HTTP `Range` support in `LocalStreamEndpoint`.
- A range request/control protocol over DataChannel.
- Sender-side file reads from requested offsets.
- Multiple request handling rather than a single forward-only pipe.

Current implementation supports only linear streaming.

---

### 13. Streaming Pipe Writes Are Fire-and-Forget

`ReceiveStreamingAsync` writes to a `PipeWriter` directly from the DataChannel callback:

```csharp
var mem = pipe.Writer.GetMemory(data.Length);
data.CopyTo(mem);
pipe.Writer.Advance(data.Length);
_ = pipe.Writer.FlushAsync(ct).AsTask();
```

Issues:

- Flush is not awaited.
- Exceptions are unobserved.
- Concurrent callbacks could corrupt `PipeWriter` usage.
- Backpressure is ignored.

Recommendation:

- Push DataChannel messages into a bounded channel.
- Have one async consumer write to the pipe and await `FlushAsync`.
- Complete the pipe on EOF, cancellation, or error.

---

### 14. Local Stream Endpoint Is Single-Consumer but Does Not Enforce It

A single `PipeReader` is registered per token. If Jellyfin probes, retries, or opens multiple HTTP requests for one stream, multiple consumers may try to read from the same pipe.

Recommendations:

- For non-seekable streaming, enforce single-use and return `409 Conflict` for additional readers.
- For seekable playback, redesign around range requests instead of a single shared pipe.

---

## Server-Side Concerns

### 15. Hub Does Not Enforce Payload Sizes

`RelayChunk` comments mention a max payload of 32 KB, but `FederationHub.RelaySendChunk` does not enforce it.

Recommendation:

Add validation:

```csharp
if (chunk.Data.Length > MaxRelayChunkBytes)
{
    // log/drop/fail
    return;
}
```

Also consider maximum payload limits for SDP and ICE candidate messages.

---

### 16. Relay Path Is Owner-to-Requester Only

`RelaySendChunk` enforces:

```csharp
if (senderId.Value != routing.OwningServerId)
    return;
```

This matches file download, but future seekable streaming will likely require requester-to-owner control messages.

Recommendation:

Do not overload `RelayChunk` for streaming control. Add separate range/control messages if seekable streaming is implemented.

---

### 17. Receiver Can Independently Enter Relay Receive Mode

The receiver currently may call `TriggerRelayReceiveAsync` when its ICE state fails/disconnects. But the sender may not have started relay yet.

This can leave the receiver waiting indefinitely.

Recommendation:

- Make sender/owner the authority for fallback.
- Receiver should enter relay receive mode only after receiving `RelayTransferStart`.
- Or add an explicit coordinated fallback negotiation.

---

## NAT Traversal and Deployment Concerns

### 18. STUN-Only WebRTC Will Fail in Some Common Topologies

The implementation uses STUN, but no TURN configuration. STUN-only ICE will fail in symmetric NAT, some mobile/carrier-grade NAT, and strict corporate networks.

The app-level relay helps, but WebRTC direct success rates may be lower than expected.

Recommendations:

- Add optional TURN configuration:
  - TURN URL
  - username
  - credential
- Keep federation-server relay as a fallback.
- Consider preferring TURN over federation-server relay if server bandwidth matters.

---

### 19. Transport Selection Is Recorded Too Early

The server marks WebRTC selected when negotiation starts, not when the DataChannel opens. If negotiation fails and relay is used, the persisted state can be wrong.

Recommendation:

Track attempted transport separately from actual transport, or update `SelectedTransportMode` on fallback.

Possible model:

- `AttemptedTransportMode = WebRtc`
- `SelectedTransportMode = Relay`
- `TransportSelectionReason = IceFailed`

If only one field is available, update it to actual mode.

---

## Testing Gaps

Existing tests cover some server-side routing/security. Plugin-side WebRTC behavior is mostly untested.

Recommended tests:

1. **Offer-before-answerer-session race**
   - Simulate an offer arriving before `BeginAsAnswererAsync` has registered a session.
   - Verify the offer is buffered or negotiation still succeeds.

2. **Candidate-before-remote-description**
   - Simulate candidates arriving before SDP is applied.
   - Verify they are queued and later applied.

3. **Relay header behavior**
   - Verify `SendRelayAsync` sends a header chunk first.
   - Verify `ReceiveRelayAsync` preserves file name and total size.

4. **EOF collision**
   - Send a file chunk equal to `EofMagic`.
   - Verify it is treated as data, not EOF.

5. **Bounded memory / slow receiver**
   - Simulate slow disk or slow stream consumer.
   - Verify queues and buffers remain bounded.

6. **Session cleanup**
   - After success, failure, and cancellation, verify `_sessions`, `_activeCts`, and relay queues are empty.

7. **Transport reporting**
   - Simulate ICE failure followed by relay success.
   - Verify the database reports `Relay` and `IceFailed`.

8. **Streaming URL lifecycle**
   - Verify `GetStreamUrl(fileRequestId)` returns a URL while active.
   - Verify the endpoint returns 404/410 after completion.

9. **Range/seek behavior**
   - If seeking is in scope, test HTTP `Range` requests explicitly.

---

## Suggested Priority Plan

### P0 — Correctness Blockers

1. Fix session CTS ownership and disposal.
2. Add cleanup after success, failure, and cancellation.
3. Reduce/fix signaling race by dispatching answerer first and buffering early signals.
4. Buffer candidates until remote description is set.
5. Add explicit sender-side DataChannel-open timeout and relay fallback.

### P1 — Reliable Transfers

6. Replace EOF/header magic with explicit frame protocol.
7. Add DataChannel send backpressure.
8. Replace unbounded receive channels with bounded queues.
9. Fix relay header and metadata transfer.
10. Update persisted transport mode on relay fallback.

### P2 — Streaming Completion

11. Implement `fileRequestId -> stream URL` tracking.
12. Make streaming pipe writes single-consumer and backpressure-aware.
13. Decide whether seek/range support is required for MVP.
14. If seeking is required, redesign streaming around range requests.

### P3 — Hardening

15. Enforce max payload sizes in the hub.
16. Add optional TURN configuration.
17. Add integration tests for two local SIPSorcery peer connections.
18. Add chaos tests for disconnects, slow receivers, dropped candidates, and duplicate state transitions.

---

## Bottom Line

The WebRTC implementation is a promising prototype and the solution builds cleanly. It is a good base for iterative development, but it is not yet robust enough for production federated transfers.

The most important fixes are:

- correct session lifetime management,
- signaling and candidate race handling,
- bounded memory/backpressure,
- explicit transfer framing,
- accurate relay fallback reporting,
- cleanup after terminal states,
- and completing or narrowing the streaming requirements.

Addressing these will make the WebRTC path much closer to the feature specification's goals of zero-configuration transfer, graceful fallback, and bounded-memory operation.
