# transtools Rename Design

## Goal

Rename the project's external identity from `pot` / `ScreenTranslator` to `transtools` without renaming the internal C# namespace tree or project directory structure.

## Scope

- Rename the GitHub repository from `pot` to `transtools`.
- Rename published package names and the published executable to `transtools`.
- Rename visible app branding to `transtools` in the tray icon text, dialog titles, and localized app title strings.
- Move persisted settings from `%AppData%\ScreenTranslator\settings.json` to `%AppData%\transtools\settings.json`.
- Keep backward compatibility by reading the old settings path when the new path does not exist yet.
- Rename the Windows auto-start registry value from `ScreenTranslator` to `transtools`.

## Non-Goals

- Do not rename the `ScreenTranslator/` source directory.
- Do not rename the `.csproj` file.
- Do not rename C# namespaces, XAML `x:Class` names, or test assembly names.
- Do not rewrite historical design/plan documents that intentionally describe previous work.

## Design

### Packaging and binary identity

The WPF project will keep its current file layout, but the assembly metadata will declare `transtools` as the external product name and executable name. The publish script will package output under `artifacts/packages/transtools-win-x64-onefile-<date>/transtools.exe`.

### Runtime branding

Localized strings and hard-coded window/tray titles will be updated to `transtools`. Resource pack URIs that currently name the assembly will be adjusted to the new assembly name so embedded icons and localization resources continue loading correctly after the executable rename.

### Settings migration

`SettingsService` will compute a new canonical path at `%AppData%\transtools\settings.json`. On load, if the new file does not exist but the legacy `%AppData%\ScreenTranslator\settings.json` file does, the service will read from the legacy path. Save operations will always write to the new path, migrating users forward without deleting the old file.

### Auto-start migration

`AutoStartService` will switch the registry value name to `transtools`. `IsEnabled` and `Disable` will treat either the new value or the legacy `ScreenTranslator` value as relevant so existing users can toggle startup cleanly after upgrading.

## Verification

- `dotnet build .\ScreenTranslator\ScreenTranslator.csproj -c Release`
- `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release`
- `.\scripts\Publish-OneFile.ps1 -VersionSuffix rename-check`
- Confirm the publish directory contains `transtools.exe`.

