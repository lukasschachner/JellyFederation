# WebRTC Streaming Range/Seek Design

Date: 2026-04-24

## Decision

Seekable WebRTC streaming is required for a production-quality media playback path, but it should be implemented as a separate range-request protocol rather than extending the current forward-only pipe.

The current `LocalStreamEndpoint` can remain a linear streaming MVP, but user-visible playback should not claim seek support until the protocol below is implemented.

## Required Behavior

- `LocalStreamEndpoint` parses HTTP `Range` headers from Jellyfin/media clients.
- Each range request maps to an explicit request over the WebRTC DataChannel.
- The owning peer reads the requested byte range from the source file and sends bounded typed frames back.
- Multiple range requests must be independent; a browser/player retry must not consume the same single `PipeReader`.
- The endpoint returns correct HTTP status codes:
  - `200 OK` for full linear stream.
  - `206 Partial Content` for valid byte ranges.
  - `416 Range Not Satisfiable` for invalid ranges.

## Proposed Protocol Frames

Extend the DataChannel frame protocol with additional frame types:

```csharp
public enum TransferFrameType : byte
{
    Header = 1,
    Data = 2,
    End = 3,
    Error = 4,
    RangeRequest = 5,
    RangeHeader = 6,
    RangeData = 7,
    RangeEnd = 8
}
```

### RangeRequest payload

```text
Guid RequestId
long StartOffset
long? EndOffsetInclusive
```

### RangeHeader payload

```text
Guid RequestId
long TotalLength
long StartOffset
long EndOffsetInclusive
string ContentType
```

### RangeData payload

```text
Guid RequestId
byte[] Bytes
```

### RangeEnd payload

```text
Guid RequestId
```

## Local Endpoint Architecture

Replace the current `token -> PipeReader` model for seekable streams with a per-stream controller:

```text
fileRequestId -> StreamSessionController
StreamSessionController:
  - DataChannel
  - pending range requests keyed by request id
  - bounded response channels per range request
  - cancellation/lifetime owner
```

For each HTTP request:

1. Parse the `Range` header.
2. Allocate a new `RequestId`.
3. Send `RangeRequest` over the DataChannel.
4. Await `RangeHeader`.
5. Write HTTP headers and stream `RangeData` frames until `RangeEnd`.
6. Cancel only that range request if the HTTP client disconnects.

## Backpressure

- Reuse DataChannel buffered amount checks before every frame send.
- Use bounded channels for incoming range responses.
- Do not buffer full ranges in memory.

## Implementation Notes

- Keep the current forward-only `ReceiveStreamingAsync` path for simple MVP playback only.
- Gate UI/feature claims: seek support is not complete until this design is implemented.
- Add tests with single range, suffix range, invalid range, concurrent range requests, and disconnect during range.
