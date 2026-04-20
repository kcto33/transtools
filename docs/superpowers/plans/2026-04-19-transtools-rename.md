# transtools Rename Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the repository and app's external identity to `transtools` while preserving the internal `ScreenTranslator` code structure.

**Architecture:** Keep the existing WPF project layout and namespaces, but update assembly metadata, publish output names, UI branding, settings storage, and startup registration so users only see `transtools`. Add a compatibility read path for legacy settings and startup entries to avoid breaking upgrades.

**Tech Stack:** .NET 8, WPF, PowerShell publish script, GitHub repository settings

---

### Task 1: Update external branding and compatibility points

**Files:**
- Modify: `F:\yys\transtools\pot\ScreenTranslator\ScreenTranslator.csproj`
- Modify: `F:\yys\transtools\pot\ScreenTranslator\app.manifest`
- Modify: `F:\yys\transtools\pot\ScreenTranslator\Resources\Strings.en.xaml`
- Modify: `F:\yys\transtools\pot\ScreenTranslator\Resources\Strings.zh-CN.xaml`
- Modify: `F:\yys\transtools\pot\ScreenTranslator\Services\AutoStartService.cs`
- Modify: `F:\yys\transtools\pot\ScreenTranslator\Services\LocalizationService.cs`
- Modify: `F:\yys\transtools\pot\ScreenTranslator\Services\SettingsService.cs`
- Modify: `F:\yys\transtools\pot\ScreenTranslator\Services\TrayService.cs`
- Modify: `F:\yys\transtools\pot\ScreenTranslator\Windows\SettingsWindow.xaml.cs`
- Modify: `F:\yys\transtools\pot\ScreenTranslator\App.xaml.cs`
- Modify: `F:\yys\transtools\pot\scripts\Publish-OneFile.ps1`
- Modify: `F:\yys\transtools\pot\README.md`
- Modify: `F:\yys\transtools\pot\docs\user-guide.md`
- Modify: `F:\yys\transtools\pot\docs\user-guide.zh-CN.md`

- [ ] Change assembly metadata and publish script output to emit `transtools.exe` and `transtools-...` package names.
- [ ] Update pack URIs and visible UI strings to `transtools`.
- [ ] Add settings-path migration from `%AppData%\ScreenTranslator` to `%AppData%\transtools`.
- [ ] Add auto-start registry compatibility for legacy `ScreenTranslator` entries while writing `transtools`.
- [ ] Update user-facing docs to describe the new app name and settings path.

### Task 2: Verify build, tests, and publish outputs

**Files:**
- Verify: `F:\yys\transtools\pot\artifacts\packages`

- [ ] Run `dotnet build .\ScreenTranslator\ScreenTranslator.csproj -c Release` and confirm exit code `0`.
- [ ] Run `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release` and confirm exit code `0`.
- [ ] Run `.\scripts\Publish-OneFile.ps1 -VersionSuffix rename-check` and confirm the new package contains `transtools.exe`.

### Task 3: GitHub repository rename follow-through

**Files:**
- Verify: `F:\yys\transtools\pot\.git\config`

- [ ] Attempt GitHub repository rename if authenticated access is available.
- [ ] If authenticated rename is unavailable, document the exact manual GitHub steps and the local `git remote set-url origin` command.
