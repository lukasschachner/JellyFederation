# SignalR workflow tests

Expand hub coverage beyond security checks into end-to-end workflow behavior using real SignalR clients.

## Coverage targets

- Both peers connect successfully.
- File request notifications are re-sent on reconnect.
- ICE negotiation starts only when both peers advertise ICE support.
- Relay fallback start is forwarded only from the authorized sender to the receiver.
- Relay chunks are routed only between valid participants.
- Progress events are visible to both server groups.

## Why this matters

These tests provide high confidence because they exercise the real hub, authentication, EF Core state, and SignalR client behavior together.

Use these tests to prevent privilege escalation bugs, dropped workflow messages, and regressions in reconnect behavior.
