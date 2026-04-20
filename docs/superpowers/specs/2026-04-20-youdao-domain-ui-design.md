# Youdao Domain UI Design

## Goal

Expose Youdao domain translation selection in the settings window so users can choose between general, computers, and game without editing JSON manually.

## Scope

- Add a Youdao-only domain selector in the API settings section.
- Support `general`, `computers`, and `game`.
- Normalize saved values so blank or unknown values fall back to `general`.
- Keep existing provider request behavior unchanged except for using the selected domain.

## Design

`SettingsWindow` gets a small list of supported Youdao domains and helper methods to normalize/load/save the selected value. The selector is shown only for the Youdao provider. The saved value goes into `ProviderSettings.Domain`, which the provider already uses.

## Testing

- Domain normalization accepts `computers` and `game`.
- Blank or unsupported values fall back to `general`.
