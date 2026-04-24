# Contract and DTO validation tests

Shared DTOs are part of the API contract, so validation and JSON compatibility should be tested explicitly.

## Coverage targets

- `RegisterServerRequest` requires `Name` and `OwnerUserId`.
- `CreateFileRequestDto` rejects empty `JellyfinItemId`.
- `SyncMediaRequest` rejects empty or excessive `Items`.
- Enum JSON values are serialized as strings.
- Error envelopes have a stable response shape.

## Test style

These tests can be either lightweight unit tests or API integration tests. Prefer API integration tests when the goal is to verify the actual HTTP response shape and model binding behavior.

For public API compatibility, consider snapshot tests with Verify once the contract stabilizes.
