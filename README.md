ScreenTranslator

WPF (Windows 11 22H2+ / build 22621+) tray app that lets you drag-select a region, OCR it, and show a small non-activating bubble above the selection.

Build/run (on Windows 11 22H2+ with .NET 8 SDK):

1) Restore/build
   - `dotnet restore .\ScreenTranslator\ScreenTranslator.csproj`
   - `dotnet build .\ScreenTranslator\ScreenTranslator.csproj -c Release`

2) Run
   - `dotnet run --project .\ScreenTranslator\ScreenTranslator.csproj`

Usage
- Hotkey: `Ctrl+Alt+T`
- Drag to select (single monitor only; selection is clamped to the monitor you start on)
- A small bubble appears above the selection

Settings
- Tray icon menu: `Settings`
- Provider selection + API key fields are saved to `%AppData%\ScreenTranslator\settings.json`

Notes
- OCR uses Windows built-in OCR (`Windows.Media.Ocr`) and defaults to English recognition.
- Translation providers:
  - `mock`: returns original text
  - `youdao`: Youdao Text Translation API (requires AppId + Secret)
