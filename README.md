# ScreenTranslator

WPF tray app for Windows 11 22H2+ (build 22621+) that lets you drag-select a region, OCR it, and show a small non-activating translation bubble above the selection.

## Project layout

- `ScreenTranslator/`: application source
- `docs/`: user-facing docs
- `scripts/`: local build and release scripts
- `artifacts/`: generated build and publish output

## Build and run

Requires Windows 11 and .NET 8 SDK.

```powershell
dotnet restore .\ScreenTranslator\ScreenTranslator.csproj
dotnet build .\ScreenTranslator\ScreenTranslator.csproj -c Release
dotnet run --project .\ScreenTranslator\ScreenTranslator.csproj
```

With `Directory.Build.props`, SDK outputs are centralized under `artifacts/` instead of scattering under the repo root.

## Release

Single-file self-contained release:

```powershell
.\scripts\Publish-OneFile.ps1
```

Optional arguments:

- `-Configuration Release`
- `-Runtime win-x64`
- `-VersionSuffix v1`
- `-NoZip`

Default output:

- `artifacts\packages\ScreenTranslator-win-x64-onefile-<yyyyMMdd>\ScreenTranslator.exe`
- `artifacts\packages\ScreenTranslator-win-x64-onefile-<yyyyMMdd>.zip`

## Usage

- Hotkey: `Ctrl+Alt+T`
- Drag to select. Selection is clamped to the monitor where the gesture starts.
- A translation bubble appears above the selected region.

## User guide

- English: `docs/user-guide.md`
- ZH-CN: `docs/user-guide.zh-CN.md`

## Settings

- Tray icon menu: `Settings`
- Provider selection and API secrets are stored in `%AppData%\ScreenTranslator\settings.json`

## Notes

- OCR uses Windows built-in OCR (`Windows.Media.Ocr`) and defaults to English recognition.
- Translation providers:
  - `mock`: returns original text
  - `youdao`: Youdao Text Translation API (requires AppId + Secret)
