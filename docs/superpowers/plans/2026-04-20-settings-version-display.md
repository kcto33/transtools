# Settings Version Display Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add explicit app version metadata and show the current version as `1.0.0` in the Settings window footer.

**Architecture:** Keep the version source in the project file, add a small formatting helper in the settings window code-behind, and expose the formatted string through a read-only footer label. Cover the formatting logic with a focused unit test so the UI display stays stable even when assembly metadata includes a fourth segment or build suffix.

**Tech Stack:** .NET 8, WPF, xUnit

---

### Task 1: Add a failing test for version formatting

**Files:**
- Create: `F:\yys\transtools\ScreenTranslator.Tests\SettingsWindowVersionTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ScreenTranslator.Windows;

namespace ScreenTranslator.Tests;

public sealed class SettingsWindowVersionTests
{
  [Theory]
  [InlineData("1.0.0", "1.0.0")]
  [InlineData("1.0.0.0", "1.0.0")]
  [InlineData("1.0.0+abc123", "1.0.0")]
  [InlineData("1.0.0-beta.1", "1.0.0")]
  [InlineData(null, "1.0.0")]
  [InlineData("", "1.0.0")]
  public void NormalizeDisplayVersion_ReturnsMajorMinorPatch(string? rawVersion, string expected)
  {
    var actual = SettingsWindow.NormalizeDisplayVersion(rawVersion);

    Assert.Equal(expected, actual);
  }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FullyQualifiedName~SettingsWindowVersionTests`
Expected: FAIL because `SettingsWindow.NormalizeDisplayVersion` does not exist yet.

### Task 2: Implement version metadata and footer display

**Files:**
- Modify: `F:\yys\transtools\ScreenTranslator\ScreenTranslator.csproj`
- Modify: `F:\yys\transtools\ScreenTranslator\Windows\SettingsWindow.xaml`
- Modify: `F:\yys\transtools\ScreenTranslator\Windows\SettingsWindow.xaml.cs`

- [ ] **Step 1: Add explicit project version metadata**

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net8.0-windows10.0.22621.0</TargetFramework>
  <WindowsSdkPackageVersion>10.0.22621.30</WindowsSdkPackageVersion>
  <UseWPF>true</UseWPF>
  <UseWindowsForms>true</UseWindowsForms>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <AssemblyName>transtools</AssemblyName>
  <Product>transtools</Product>
  <Title>transtools</Title>
  <Version>1.0.0</Version>
  <ApplicationManifest>app.manifest</ApplicationManifest>
</PropertyGroup>
```

- [ ] **Step 2: Add the footer text block to the Settings window**

```xml
<Border Grid.Row="1" BorderThickness="0,1,0,0" BorderBrush="#E0E0E0" Background="White" Padding="16">
  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="*" />
      <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>

    <TextBlock x:Name="VersionTextBlock"
               Grid.Column="0"
               VerticalAlignment="Center"
               Foreground="{StaticResource SecondaryTextBrush}"
               FontSize="12" />

    <StackPanel Grid.Column="1" Orientation="Horizontal" HorizontalAlignment="Right">
      <Button x:Name="SaveButton" Content="{DynamicResource Btn_SaveSettings}" Style="{StaticResource PrimaryButton}" />
    </StackPanel>
  </Grid>
</Border>
```

- [ ] **Step 3: Implement the minimal version helper and initialize the footer**

```csharp
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreenTranslator.Models;
using ScreenTranslator.Services;

namespace ScreenTranslator.Windows;

public partial class SettingsWindow : Window
{
  private const string DefaultDisplayVersion = "1.0.0";

  public SettingsWindow(
    SettingsService settings,
    Func<string, string?>? applyHotkey = null,
    Func<string, string?>? applyPasteHistoryHotkey = null,
    Func<string, string?>? applyScreenshotHotkey = null,
    Action<int>? updateClipboardHistoryMaxItems = null,
    Action? suspendHotkeys = null,
    Action? resumeHotkeys = null)
  {
    InitializeComponent();
    TrySetWindowIcon();
    _settings = settings;
    _applyHotkey = applyHotkey;
    _applyPasteHistoryHotkey = applyPasteHistoryHotkey;
    _applyScreenshotHotkey = applyScreenshotHotkey;
    _updateClipboardHistoryMaxItems = updateClipboardHistoryMaxItems;
    _suspendHotkeys = suspendHotkeys;
    _resumeHotkeys = resumeHotkeys;

    VersionTextBlock.Text = $"Version {GetDisplayVersion()}";

    InitializeUILanguageControls();
    InitializeProviderControls();
    InitializeLanguageControls();
    InitializeBubbleControls();
    InitializeHotkeyCapture();
    InitializeEventHandlers();

    LoadFromSettings();
  }

  internal static string NormalizeDisplayVersion(string? rawVersion)
  {
    if (string.IsNullOrWhiteSpace(rawVersion))
      return DefaultDisplayVersion;

    var core = rawVersion.Trim();
    var plusIndex = core.IndexOf('+');
    if (plusIndex >= 0)
      core = core[..plusIndex];

    var dashIndex = core.IndexOf('-');
    if (dashIndex >= 0)
      core = core[..dashIndex];

    if (Version.TryParse(core, out var parsed))
      return $"{parsed.Major}.{parsed.Minor}.{Math.Max(parsed.Build, 0)}";

    return DefaultDisplayVersion;
  }

  private static string GetDisplayVersion()
  {
    var assembly = typeof(SettingsWindow).Assembly;
    var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    if (!string.IsNullOrWhiteSpace(informational))
      return NormalizeDisplayVersion(informational);

    return NormalizeDisplayVersion(assembly.GetName().Version?.ToString());
  }
}
```

- [ ] **Step 4: Run the focused test to verify it passes**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FullyQualifiedName~SettingsWindowVersionTests`
Expected: PASS

### Task 3: Verify the integrated build and test suite

**Files:**
- Verify: `F:\yys\transtools\ScreenTranslator\obj`
- Verify: `F:\yys\transtools\ScreenTranslator.Tests\bin`

- [ ] **Step 1: Run the project build**

Run: `dotnet build .\ScreenTranslator\ScreenTranslator.csproj -c Release`
Expected: exit code `0`

- [ ] **Step 2: Run the full test project**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release`
Expected: exit code `0`

- [ ] **Step 3: Commit**

```bash
git add ScreenTranslator/ScreenTranslator.csproj ScreenTranslator/Windows/SettingsWindow.xaml ScreenTranslator/Windows/SettingsWindow.xaml.cs ScreenTranslator.Tests/SettingsWindowVersionTests.cs docs/superpowers/plans/2026-04-20-settings-version-display.md
git commit -m "feat: show app version in settings"
```
