# Plugin component tests

The Jellyfin plugin is harder to integration-test because it depends on Jellyfin runtime APIs. Focus first on pure or near-pure components.

## Good candidates

- Configuration defaults and parsing.
- Transfer selection thresholds.
- Telemetry redaction.
- Preview compression behavior with isolated image inputs.
- `ServerConnectionTracker` logic.
- Hole-punch staging logic.
- ICE candidacy staging logic.

## Guidance

Avoid mocking large Jellyfin interfaces unless the behavior is small and important. Prefer extracting pure helpers from Jellyfin-dependent services, then testing those helpers directly.

When service-level tests are needed, keep mocks narrow and verify observable outcomes rather than implementation details.
