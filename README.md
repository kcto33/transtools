# transtools

WPF tray app for Windows 11 22H2+ (build 22621+) that lets you drag-select a region, OCR it, and show a small non-activating translation bubble above the selection.

## Features

- Selected-text-first translation on the main hotkey, with fallback to region selection when no text is captured.
- Region screenshot editing with pin, copy, save, long screenshot, redraw, and annotation tools.
- Long screenshot capture with a fixed selection frame, live preview, and copy/save/pin actions.
- Non-activating translation bubble with configurable colors, font, spacing, and max width.

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

- `artifacts\packages\transtools-win-x64-onefile-<yyyyMMdd>\transtools.exe`
- `artifacts\packages\transtools-win-x64-onefile-<yyyyMMdd>.zip`

## Usage

- Hotkey: `Ctrl+Alt+T`
- Drag to select. Selection is clamped to the monitor where the gesture starts.
- A translation bubble appears above the selected region.
- Screenshot hotkey: `Ctrl+Alt+S`
- Screenshot edit toolbar order: `Save`, `Copy`, `Long Screenshot`, `Redraw`, `Pin`, `Brush`, `Rectangle`, `Mosaic`, `Undo`, `Cancel`

## Recent Fixes

- Increased the selected-text capture budget before falling back to screenshot mode, reducing accidental overlay launches when apps are slow to publish copied text.
- Reordered the screenshot toolbar and removed the unused clear-annotation action from the rectangular screenshot flow.
- Hardened long-screenshot startup and capture handling, including UI-thread-safe capture hooks and clearer completion diagnostics.
- Prevented the normal screenshot background capture from including the overlay mask by hiding the overlay while the frozen background is captured.

## User guide

- English: `docs/user-guide.md`
- ZH-CN: `docs/user-guide.zh-CN.md`

## Settings

- Tray icon menu: `Settings`
- Provider selection and API secrets are stored in `%AppData%\transtools\settings.json`

## Notes

- OCR uses Windows built-in OCR (`Windows.Media.Ocr`) and defaults to English recognition.
- Translation providers:
  - `mock`: returns original text
  - `youdao`: Youdao Text Translation API (requires AppId + Secret)
