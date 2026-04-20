# Youdao Domain Translation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add backward-compatible support for Youdao domain translation request parameters.

**Architecture:** Extend provider settings with optional domain fields, thread them through `TranslationService`, and update `YoudaoTranslationProvider` to conditionally include the new API parameters. Verify behavior with provider-focused tests that inspect the outgoing form payload.

**Tech Stack:** .NET 8, WPF app, xUnit

---

### Task 1: Lock request-shape behavior with tests

**Files:**
- Create: `F:\yys\transtools\ScreenTranslator.Tests\YoudaoTranslationProviderTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task TranslateAsync_DoesNotSendDomainFields_WhenNotConfigured()

[Fact]
public async Task TranslateAsync_SendsDomainFields_WhenConfigured()
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Debug --filter FullyQualifiedName~YoudaoTranslationProviderTests`
Expected: FAIL because the provider constructor and request composition do not yet support the tested behavior.

- [ ] **Step 3: Write minimal implementation**

```csharp
public YoudaoTranslationProvider(string endpoint, string appId, string appSecret, string? domain, bool? rejectFallback)
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Debug --filter FullyQualifiedName~YoudaoTranslationProviderTests`
Expected: PASS

### Task 2: Wire settings through TranslationService

**Files:**
- Modify: `F:\yys\transtools\ScreenTranslator\Models\AppSettings.cs`
- Modify: `F:\yys\transtools\ScreenTranslator\Services\TranslationService.cs`

- [ ] **Step 1: Write the failing test**

Add a service-level test proving configured Youdao settings produce a provider that uses domain translation parameters.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Debug --filter FullyQualifiedName~Youdao`
Expected: FAIL because settings are not threaded through yet.

- [ ] **Step 3: Write minimal implementation**

Add optional settings properties and pass them into the provider creation path.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Debug --filter FullyQualifiedName~Youdao`
Expected: PASS

### Task 3: Regression verification

**Files:**
- Verify only

- [ ] **Step 1: Run targeted translation tests**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Debug --filter FullyQualifiedName~TranslationServiceTests`
Expected: PASS

- [ ] **Step 2: Run full test project or build**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Debug`
Expected: PASS
