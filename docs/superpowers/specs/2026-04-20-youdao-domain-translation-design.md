# Youdao Domain Translation Design

## Goal

Allow the existing Youdao text translation integration to opt into Youdao's domain translation mode without breaking current users or requiring a new endpoint.

## Scope

- Add backward-compatible provider settings for Youdao domain translation.
- Pass `domain` and `rejectFallback` to the existing `/api` request when configured.
- Parse the optional `isDomainSupport` response field for future use.
- Keep current UI behavior unchanged; settings can still be edited through the existing JSON-backed settings flow.

## Design

### Configuration

Extend `ProviderSettings` with:

- `Domain`: optional string, omitted when blank
- `RejectFallback`: optional bool, omitted when null

Existing settings files remain valid because both fields are optional.

### Service wiring

`TranslationService.CreateYoudao()` reads the new fields from the provider settings and passes them into `YoudaoTranslationProvider`.

### Provider behavior

`YoudaoTranslationProvider` continues using the same endpoint and v3 signature. When configured:

- include `domain` only when non-empty
- include `rejectFallback` as `true` or `false` when explicitly configured

The provider also deserializes `isDomainSupport` but does not change runtime behavior based on it yet.

## Testing

- Provider request does not include domain fields by default.
- Provider request includes configured domain fields when enabled.
- `TranslationService` passes provider settings through to the provider constructor path.
