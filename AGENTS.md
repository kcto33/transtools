# Agent Guide (transtools)

This repository contains `ScreenTranslator/`, a .NET 8 WPF tray app for Windows 11 (22H2+, build 22621+).
Agents working here should prefer small, safe changes that keep UI responsive and avoid disrupting the tray workflow.

## Project Layout

- `ScreenTranslator/ScreenTranslator.csproj`: main project (WPF WinExe, `net8.0-windows10.0.22621.0`).
- `ScreenTranslator/Services/`: capture, OCR, translation, tray, settings, hotkey, flow orchestration.
- `ScreenTranslator/Windows/`: WPF windows (`OverlayWindow`, `BubbleWindow`, `SettingsWindow`).
- `ScreenTranslator/Translation/`: translation provider interface + implementations.
- `ScreenTranslator/Interop/`: P/Invoke and Win32 interop.
- Settings file: `%AppData%\transtools\settings.json` (created/updated by `SettingsService`).

## Commands (Build / Run / Publish)

All commands assume Windows + .NET 8 SDK.

- Restore
  - `dotnet restore .\ScreenTranslator\ScreenTranslator.csproj`
- Build (Release)
  - `dotnet build .\ScreenTranslator\ScreenTranslator.csproj -c Release`
- Build (Debug)
  - `dotnet build .\ScreenTranslator\ScreenTranslator.csproj -c Debug`
- Run
  - `dotnet run --project .\ScreenTranslator\ScreenTranslator.csproj`
- Publish (example)
  - `dotnet publish .\ScreenTranslator\ScreenTranslator.csproj -c Release -r win-x64 --self-contained false`

### Lint / Format

No repo-specific linter/formatter config is present (no `.editorconfig`, no StyleCop rulesets, no dotnet-tools manifest).

Use these as optional, local-only checks:

- Treat warnings as errors for a tighter build (may require fixing existing warnings)
  - `dotnet build .\ScreenTranslator\ScreenTranslator.csproj -c Release -warnaserror`
- Format (requires `dotnet format` available in your environment)
  - `dotnet format .\ScreenTranslator\ScreenTranslator.csproj`

## Tests

No test projects are present in this repo currently.

If/when tests are added:

- Run all tests
  - `dotnet test .\<test-project>.csproj -c Release`
- Run a single test (recommended pattern)
  - `dotnet test .\<test-project>.csproj -c Release --filter FullyQualifiedName~Namespace.ClassName.MethodName`
- Run tests by class
  - `dotnet test .\<test-project>.csproj --filter FullyQualifiedName~Namespace.ClassName`
- Run tests by name substring
  - `dotnet test .\<test-project>.csproj --filter Name~Hotkey`

## Cursor / Copilot Rules

- No Cursor rules found (`.cursorrules`, `.cursor/rules/`).
- No Copilot instructions found (`.github/copilot-instructions.md`).
- If these files are added later, they override guidance in this document.

## Code Style Guidelines (C# / WPF)

Match existing code conventions before introducing new patterns.

### Files, Namespaces, and Types

- Use file-scoped namespaces: `namespace ScreenTranslator.Services;`.
- Prefer `sealed` for concrete classes unless inheritance is intended.
- Keep types small and focused; new “service” classes belong in `ScreenTranslator/Services/`.
- Interop goes in `ScreenTranslator/Interop/` and is `internal` by default.

### Indentation and Formatting

- Indentation is 2 spaces (not tabs) in existing `.cs` files.
- Braces on next line (Allman style).
- Keep blank line between `using` block and `namespace`.
- Prefer early returns over deep nesting.

### Using Directives and Imports

- Group `using` directives roughly as:
  - `System.*` first
  - then project namespaces (`ScreenTranslator.*`)
  - then `using X = Y;` aliases (especially for WPF vs WinForms type name collisions)
- Use aliases when names collide (`Screen`, WPF `Color`, `Brush`, etc.).
- Avoid unnecessary `using` directives; rely on `ImplicitUsings` where appropriate, but keep explicit usings when clarity improves.

### Naming Conventions

- Types/methods/properties: `PascalCase`.
- Locals/parameters: `camelCase`.
- Private fields: `_camelCase`.
- Constants: `PascalCase` (existing code uses `private const int WM_HOTKEY = ...`).
- Async methods end with `Async`.
- Events use past-tense or “Requested” names (e.g. `StartSelectionRequested`).

### Nullable and Types

- Nullable reference types are enabled (`<Nullable>enable</Nullable>`):
  - Use `string?` and null checks intentionally.
  - Avoid `!` unless you can justify a proven invariant.
- Prefer `var` when the RHS makes the type obvious.
- Use `readonly` fields when values do not change.

### Async, Cancellation, and Responsiveness

- For async APIs, accept `CancellationToken ct` as the last parameter.
- Call `ct.ThrowIfCancellationRequested()` early in async methods.
- Handle `OperationCanceledException` explicitly and treat it as non-error.
- Avoid blocking the UI thread; prefer async work + UI updates through `Dispatcher`.
- Avoid `async void` except for event handlers.

### Error Handling and User Experience

- Prefer specific exceptions when reasonable; otherwise catch `Exception` at UI boundaries.
- Silent catches are acceptable only for best-effort UX operations (tray icon loading, settings save, clipboard, etc.).
- When surfacing an error to the user, use short messages (see `SelectionFlowController` truncation).
- Do not spam modal dialogs; use `MessageBox` only for actionable configuration failures.

### Security / Secrets

- API secrets are stored in `%AppData%\transtools\settings.json` and protected via DPAPI:
  - Use `SecretProtector.ProtectString` / `SecretProtector.UnprotectString`.
  - Never log or hardcode secrets.
  - Do not add example secrets to repo files or tests.

### WPF + Interop Patterns

- Be explicit about pixels vs DIPs:
  - Selection and placement math often uses pixels (`Rectangle`, `SetWindowPos`).
  - Convert using `VisualTreeHelper.GetDpi(this)` as in `OverlayWindow` / `BubbleWindow`.
- For non-activating UI elements:
  - `BubbleWindow` uses Win32 styles (`WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`).
- Dispose what you allocate:
  - `using var` for `Bitmap`, `Graphics`, `HttpRequestMessage`, etc.
  - Unhook native hooks in `OnClosed`.

## Editing + PR Hygiene

- Do not commit build outputs (`bin/`, `obj/`, `publish/` are ignored in `.gitignore`).
- Prefer changes that keep the app usable without external dependencies (the `mock` provider remains a safe fallback).
- When adding a new translation provider:
  - Implement `ITranslationProvider` in `ScreenTranslator/Translation/`.
  - Add selection logic in `ScreenTranslator/Services/TranslationService.cs`.
  - Add provider settings fields in `ScreenTranslator/Models/AppSettings.cs` (and keep them backward-compatible).

## Feature Specs

- History paste / clipboard picker: `docs/history-paste.md`
