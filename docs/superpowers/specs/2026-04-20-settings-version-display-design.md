# Settings Version Display Design

## Goal

Add lightweight application versioning so the app has an explicit release version and the Settings window shows the current version as `1.0.0`.

## Scope

- Define an explicit application version in `ScreenTranslator/ScreenTranslator.csproj`.
- Show the current application version in `ScreenTranslator/Windows/SettingsWindow.xaml`.
- Read the displayed version from assembly metadata in `ScreenTranslator/Windows/SettingsWindow.xaml.cs`.
- Keep the version display read-only and outside persisted user settings.

## Non-Goals

- Do not add editable version controls to the Settings UI.
- Do not store the version in `%AppData%\\transtools\\settings.json`.
- Do not introduce automatic semantic-version bumping, update checks, or release automation.
- Do not change tray behavior, startup behavior, translation flow, or other unrelated settings logic.

## Design

### Version source of truth

`ScreenTranslator.csproj` will declare `<Version>1.0.0</Version>`. This becomes the single source of truth for the app version so builds and UI display stay aligned. The implementation may also set related assembly/file version properties if needed, but the user-facing value remains `1.0.0`.

### Settings window display

The Settings window footer will show a read-only text label on the left side of the existing bottom action bar, opposite the Save button. The label text will render as `Version 1.0.0`. This placement keeps the value visible without adding noise to the main settings sections or changing the tray-first workflow.

### Runtime resolution and formatting

`SettingsWindow.xaml.cs` will resolve the current version from the executing assembly metadata during initialization. The display helper will prefer a normalized semantic version string and strip any extra build metadata or fourth version segment so the final UI always shows the requested `major.minor.patch` format. If metadata lookup fails, the helper will fall back to `1.0.0` to avoid breaking Settings window startup.

### Persistence and compatibility

The version is application metadata, not user configuration. No changes are needed in `AppSettings`, `SettingsService`, or the `%AppData%` settings file schema. Existing saved settings remain fully compatible.

## Verification

- Add or update a focused test in `ScreenTranslator.Tests` for the version formatting helper so inputs like `1.0.0`, `1.0.0.0`, and informational versions with suffixes still display as `1.0.0`.
- Run `dotnet build .\ScreenTranslator\ScreenTranslator.csproj -c Release`.
- Run `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release`.
