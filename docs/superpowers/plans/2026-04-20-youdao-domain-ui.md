# Youdao Domain UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a settings UI for selecting Youdao domain translation values.

**Architecture:** Add a Youdao-only domain combo box in the settings window, back it with a tiny normalized choice list in code-behind, and save the chosen value into provider settings. Keep tests focused on normalization logic and existing provider request behavior.

**Tech Stack:** .NET 8, WPF, xUnit

---

### Task 1: Lock domain normalization behavior with tests

**Files:**
- Modify: `F:\yys\transtools\ScreenTranslator.Tests\SettingsWindowVersionTests.cs`

- [ ] **Step 1: Write the failing test**
- [ ] **Step 2: Run `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Debug --filter FullyQualifiedName~SettingsWindowVersionTests` and verify it fails**
- [ ] **Step 3: Add minimal normalization helpers in `SettingsWindow.xaml.cs`**
- [ ] **Step 4: Re-run the same test and verify it passes**

### Task 2: Add the selector and wire save/load

**Files:**
- Modify: `F:\yys\transtools\ScreenTranslator\Windows\SettingsWindow.xaml`
- Modify: `F:\yys\transtools\ScreenTranslator\Windows\SettingsWindow.xaml.cs`
- Modify: `F:\yys\transtools\ScreenTranslator\Resources\Strings.en.xaml`
- Modify: `F:\yys\transtools\ScreenTranslator\Resources\Strings.zh-CN.xaml`

- [ ] **Step 1: Write the failing UI-related test if needed**
- [ ] **Step 2: Implement minimal combo box save/load behavior**
- [ ] **Step 3: Run targeted tests**

### Task 3: Verify

**Files:**
- Verify only

- [ ] **Step 1: Run `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Debug`**
- [ ] **Step 2: Run `dotnet build .\ScreenTranslator\ScreenTranslator.csproj -c Debug`**
