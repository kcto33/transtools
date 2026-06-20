# Floating Notes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build multi-window floating sticky notes with hotkey creation, per-note pinning, RTF persistence, and a saved-note list.

**Architecture:** Add a storage service for `.rtf` files, a controller for note/window lifetime, a WPF editor window, and a WPF list window. Wire the feature through app startup, tray menu, hotkey registration, settings, and localization.

**Tech Stack:** .NET 8 WPF, `RichTextBox` RTF serialization, existing `SettingsService`, existing `HotkeyService`, xUnit tests.

---

### Task 1: Tests For Storage And State

**Files:**
- Create: `ScreenTranslator.Tests/FloatingNoteStorageServiceTests.cs`
- Create: `ScreenTranslator.Tests/FloatingNoteWindowStateTests.cs`

- [ ] Add tests that expect default note directory resolution, timestamped `.rtf` naming, modified-time ordering, and middle-button pin toggle helper behavior.
- [ ] Run `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FloatingNote` and confirm failures reference missing floating-note types.

### Task 2: Settings Model And Storage Service

**Files:**
- Modify: `ScreenTranslator/Models/AppSettings.cs`
- Create: `ScreenTranslator/Services/FloatingNoteStorageService.cs`

- [ ] Add `FloatingNoteHotkey` and `FloatingNoteSettings`.
- [ ] Implement explicit, non-recursive directory creation and `.rtf` file save/list/load helpers.
- [ ] Re-run the floating-note storage tests and confirm they pass.

### Task 3: Note Window And Controller

**Files:**
- Create: `ScreenTranslator/Windows/FloatingNoteWindow.xaml`
- Create: `ScreenTranslator/Windows/FloatingNoteWindow.xaml.cs`
- Create: `ScreenTranslator/Windows/FloatingNoteListWindow.xaml`
- Create: `ScreenTranslator/Windows/FloatingNoteListWindow.xaml.cs`
- Create: `ScreenTranslator/Services/FloatingNoteController.cs`

- [ ] Implement a borderless sticky-note editor with title buttons, fixed color menu, toolbar, focus chrome hiding, middle-button pin toggle, drag, and close-time save.
- [ ] Implement saved-note list open behavior.
- [ ] Implement controller methods `CreateNewNote`, `ShowList`, and `CloseAll`.

### Task 4: App, Tray, Settings, And Localization Wiring

**Files:**
- Modify: `ScreenTranslator/App.xaml.cs`
- Modify: `ScreenTranslator/Services/TrayService.cs`
- Modify: `ScreenTranslator/Services/SelectionFlowController.cs`
- Modify: `ScreenTranslator/Windows/SettingsWindow.xaml`
- Modify: `ScreenTranslator/Windows/SettingsWindow.xaml.cs`
- Modify: `ScreenTranslator/Resources/Strings.zh-CN.xaml`
- Modify: `ScreenTranslator/Resources/Strings.en.xaml`
- Modify: `.gitignore`

- [ ] Register and handle `FloatingNoteHotkey`.
- [ ] Add tray menu entry and input gesture text.
- [ ] Add settings controls for floating-note hotkey and save directory.
- [ ] Add localized labels and messages.
- [ ] Ignore `.superpowers/` companion artifacts.

### Task 5: Verification

**Files:**
- Modify if failures require it: files touched above

- [ ] Run `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release`.
- [ ] Run `dotnet build .\ScreenTranslator\ScreenTranslator.csproj -c Release`.
- [ ] If available, manually run the app and verify hotkey creation, multiple independent notes, middle-button pin toggle, focus chrome hiding, close-time `.rtf` save, and list reopen.

## Self-Review

The plan covers all confirmed requirements: hotkey creation, multiple notes, independent state, focus chrome hiding, middle-button pinning, RTF save, configured note directory, timestamp naming, note list, fixed colors, and settings/localization wiring. No arbitrary custom color chooser is included.
