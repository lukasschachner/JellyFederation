# WebRTC Remediation Tasks

Source review: `docs/webrtc-implementation-review.md`

## P0 — Correctness blockers

- [x] WRTC-001 Fix WebRTC session `CancellationTokenSource` ownership and disposal.
- [x] WRTC-002 Clean up sessions after successful send/receive, failure, cancellation, and terminal DataChannel close.
- [x] WRTC-003 Reduce offer/session race by dispatching answerer before offerer on the server.
- [x] WRTC-004 Buffer early ICE signals that arrive before a plugin session exists.
- [x] WRTC-005 Buffer ICE candidates until the remote SDP description has been applied.
- [x] WRTC-006 Add explicit sender-side DataChannel-open timeout and trigger relay fallback.

## P1 — Reliable transfers

- [x] WRTC-007 Replace ad-hoc EOF/header magic with explicit framed transfer protocol.
- [x] WRTC-008 Add DataChannel send backpressure / bounded send window.
- [x] WRTC-009 Replace unbounded receive channels with bounded queues.
- [x] WRTC-010 Send relay metadata/header before file chunks.
- [x] WRTC-011 Update persisted transport mode/reason when relay fallback is engaged.

## P2 — Streaming completion

- [x] WRTC-012 Track `fileRequestId -> stream URL` for active streams and implement `GetStreamUrl`.
- [x] WRTC-013 Make streaming pipe writes single-consumer and backpressure-aware.
- [x] WRTC-014 Decide MVP scope for HTTP range/seek support.
- [ ] WRTC-015 If seek support is required, redesign streaming protocol around range requests. See `docs/webrtc-streaming-range-design.md`.

## P3 — Hardening

- [x] WRTC-016 Enforce maximum SignalR payload sizes for relay chunks and ICE signals.
- [x] WRTC-017 Add optional TURN server configuration.
- [x] WRTC-018 Add plugin-side WebRTC unit/integration tests.
- [ ] WRTC-019 Add chaos tests for disconnects, slow receivers, dropped candidates, and duplicate state transitions.
