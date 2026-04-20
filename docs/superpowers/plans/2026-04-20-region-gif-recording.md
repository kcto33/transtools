# Region GIF Recording Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a screenshot-toolbar GIF workflow that records a fixed rectangular region at 8 FPS for up to 30 seconds and saves the result as an animated `.gif`.

**Architecture:** Add a dedicated GIF stack parallel to long screenshot: a timed frame capture service, a GIF encoding service, and a session coordinator with a compact recording control window and locked selection frame. Integrate the session from the existing screenshot edit toolbar so users stay on the current screenshot hotkey and selection flow.

**Tech Stack:** .NET 8, WPF, WinForms screen APIs, `CaptureService`, `GifBitmapEncoder`, xUnit

---

## File Structure

- Create: `ScreenTranslator/Services/GifRecordingDefaults.cs`
  Stores V1 constants for frame interval, max duration, max attempts, and failure threshold.
- Create: `ScreenTranslator/Services/GifEncodingService.cs`
  Converts captured `BitmapSource` frames into animated GIF bytes and computes frame delays.
- Create: `ScreenTranslator/Services/GifRecordingService.cs`
  Runs the timed capture loop, raises progress, and reports stop/error conditions.
- Create: `ScreenTranslator/Services/GifRecordingSessionCoordinator.cs`
  Owns the end-to-end GIF session lifecycle: windows, capture hooks, encoding, save dialog, and cleanup.
- Create: `ScreenTranslator/Windows/GifRecordingControlWindow.xaml`
  Defines the compact recording control window UI.
- Create: `ScreenTranslator/Windows/GifRecordingControlWindow.xaml.cs`
  Positions the control window, applies non-activating styles, and raises stop/cancel events.
- Modify: `ScreenTranslator/Services/ScreenshotController.cs`
  Starts GIF sessions, focuses an existing GIF session, and cleans it up with the other screenshot flows.
- Modify: `ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml`
  Adds the `GIF` button to the screenshot toolbar after `Long Screenshot`.
- Modify: `ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml.cs`
  Wires the GIF toolbar action into the controller callback and updates toolbar ordering logic.
- Modify: `ScreenTranslator/Resources/Strings.en.xaml`
  Adds English strings for the toolbar button, tooltips, GIF recording window, and GIF save dialog filter.
- Modify: `ScreenTranslator/Resources/Strings.zh-CN.xaml`
  Adds Simplified Chinese strings for the same GIF UI.
- Create: `ScreenTranslator.Tests/GifEncodingServiceTests.cs`
  Verifies animated GIF bytes, frame count, dimensions, and per-frame delay metadata.
- Create: `ScreenTranslator.Tests/GifRecordingServiceTests.cs`
  Verifies attempt budgeting, capture cadence hooks, and repeated-failure handling.
- Create: `ScreenTranslator.Tests/GifRecordingCoordinatorTests.cs`
  Verifies capture hook wiring, cancel/no-save decisions, file naming, and startup debounce.
- Modify: `ScreenTranslator.Tests/ScreenshotOverlayWindowTests.cs`
  Updates the toolbar ordering expectation to include the `GIF` action.

### Task 1: Animated GIF Encoding Service

**Files:**
- Create: `ScreenTranslator/Services/GifRecordingDefaults.cs`
- Create: `ScreenTranslator/Services/GifEncodingService.cs`
- Test: `ScreenTranslator.Tests/GifEncodingServiceTests.cs`

- [ ] **Step 1: Write the failing encoding tests**

```csharp
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenTranslator.Services;
using Xunit;

namespace ScreenTranslator.Tests;

public sealed class GifEncodingServiceTests
{
  [Fact]
  public void Encode_Throws_When_Frame_Collection_Is_Empty()
  {
    var service = new GifEncodingService();

    var ex = Assert.Throws<InvalidOperationException>(() => service.Encode([], GifRecordingDefaults.FrameIntervalMs));

    Assert.Contains("frame", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void BuildFrameDelays_Alternates_Twelve_And_Thirteen_Centiseconds_For_EightFps()
  {
    var delays = GifEncodingService.BuildFrameDelays(GifRecordingDefaults.FrameIntervalMs, 4);

    Assert.Equal(new ushort[] { 12, 13, 12, 13 }, delays);
  }

  [Fact]
  public void Encode_Returns_Animated_Gif_With_Frame_Metadata()
  {
    var service = new GifEncodingService();
    var frames = new[]
    {
      CreateSolidImage(12, 10, Colors.Navy),
      CreateSolidImage(12, 10, Colors.OrangeRed),
    };

    var bytes = service.Encode(frames, GifRecordingDefaults.FrameIntervalMs);

    Assert.NotEmpty(bytes);

    using var stream = new MemoryStream(bytes);
    var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

    Assert.Equal(2, decoder.Frames.Count);
    Assert.Equal(12, decoder.Frames[0].PixelWidth);
    Assert.Equal(10, decoder.Frames[0].PixelHeight);

    var metadata = Assert.IsType<BitmapMetadata>(decoder.Frames[1].Metadata);
    Assert.Equal((ushort)13, metadata.GetQuery("/grctlext/Delay"));
  }

  private static WriteableBitmap CreateSolidImage(int width, int height, Color color)
  {
    var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
    var pixels = new byte[width * height * 4];

    for (var index = 0; index < pixels.Length; index += 4)
    {
      pixels[index + 0] = color.B;
      pixels[index + 1] = color.G;
      pixels[index + 2] = color.R;
      pixels[index + 3] = color.A;
    }

    bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
    return bitmap;
  }
}
```

- [ ] **Step 2: Run the encoding tests to verify they fail**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FullyQualifiedName~GifEncodingServiceTests`

Expected: FAIL with compile errors that `GifEncodingService` and `GifRecordingDefaults` do not exist.

- [ ] **Step 3: Write the minimal encoding implementation**

`ScreenTranslator/Services/GifRecordingDefaults.cs`

```csharp
namespace ScreenTranslator.Services;

public static class GifRecordingDefaults
{
  public const int FrameIntervalMs = 125;
  public const int MaxDurationSeconds = 30;
  public const int MaxCaptureAttempts = (MaxDurationSeconds * 1000) / FrameIntervalMs;
  public const int MaxConsecutiveCaptureFailures = 3;
}
```

`ScreenTranslator/Services/GifEncodingService.cs`

```csharp
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenTranslator.Services;

public sealed class GifEncodingService
{
  public byte[] Encode(IReadOnlyList<BitmapSource> frames, int frameIntervalMs)
  {
    ArgumentNullException.ThrowIfNull(frames);

    if (frames.Count == 0)
    {
      throw new InvalidOperationException("At least one frame is required to encode a GIF.");
    }

    var delays = BuildFrameDelays(frameIntervalMs, frames.Count);
    var encoder = new GifBitmapEncoder();

    for (var index = 0; index < frames.Count; index++)
    {
      var normalizedFrame = NormalizeFrame(frames[index]);
      var metadata = new BitmapMetadata("gif");
      metadata.SetQuery("/grctlext/Delay", delays[index]);
      metadata.SetQuery("/grctlext/Disposal", (byte)2);

      encoder.Frames.Add(BitmapFrame.Create(normalizedFrame, null, metadata, null));
    }

    using var stream = new MemoryStream();
    encoder.Save(stream);
    return stream.ToArray();
  }

  internal static IReadOnlyList<ushort> BuildFrameDelays(int frameIntervalMs, int frameCount)
  {
    if (frameIntervalMs <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(frameIntervalMs));
    }

    if (frameCount <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(frameCount));
    }

    var delays = new ushort[frameCount];
    var remainderMs = 0;

    for (var index = 0; index < frameCount; index++)
    {
      remainderMs += frameIntervalMs;
      var centiseconds = Math.Max(1, remainderMs / 10);
      remainderMs -= centiseconds * 10;
      delays[index] = (ushort)centiseconds;
    }

    return delays;
  }

  private static BitmapSource NormalizeFrame(BitmapSource source)
  {
    if (source.Format == PixelFormats.Bgra32)
    {
      if (!source.IsFrozen)
      {
        source.Freeze();
      }

      return source;
    }

    var converted = new FormatConvertedBitmap();
    converted.BeginInit();
    converted.Source = source;
    converted.DestinationFormat = PixelFormats.Bgra32;
    converted.EndInit();
    converted.Freeze();
    return converted;
  }
}
```

- [ ] **Step 4: Run the encoding tests to verify they pass**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FullyQualifiedName~GifEncodingServiceTests`

Expected: PASS with `3` tests passing.

- [ ] **Step 5: Commit the encoding service**

```bash
git add ScreenTranslator/Services/GifRecordingDefaults.cs ScreenTranslator/Services/GifEncodingService.cs ScreenTranslator.Tests/GifEncodingServiceTests.cs
git commit -m "feat: add GIF encoding service"
```

### Task 2: GIF Frame Capture Service

**Files:**
- Create: `ScreenTranslator/Services/GifRecordingService.cs`
- Test: `ScreenTranslator.Tests/GifRecordingServiceTests.cs`

- [ ] **Step 1: Write the failing capture-service tests**

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenTranslator.Services;
using Xunit;

using WinRect = System.Drawing.Rectangle;

namespace ScreenTranslator.Tests;

public sealed class GifRecordingServiceTests
{
  [Fact]
  public async Task RecordAsync_Stops_When_Max_Attempts_Is_Reached()
  {
    var captureCalls = 0;
    var delayCalls = 0;
    var service = new GifRecordingService(
      _ =>
      {
        captureCalls++;
        return CreateFrame(6, 4, Colors.CadetBlue);
      },
      (_, _) =>
      {
        delayCalls++;
        return Task.CompletedTask;
      });

    var result = await service.RecordAsync(new WinRect(0, 0, 6, 4), CancellationToken.None);

    Assert.True(result.HitDurationLimit);
    Assert.Equal(GifRecordingDefaults.MaxCaptureAttempts, result.Attempts);
    Assert.Equal(GifRecordingDefaults.MaxCaptureAttempts, captureCalls);
    Assert.Equal(GifRecordingDefaults.MaxCaptureAttempts - 1, delayCalls);
  }

  [Fact]
  public void ShouldAbortForConsecutiveFailures_Returns_True_At_Threshold()
  {
    var shouldAbort = GifRecordingService.ShouldAbortForConsecutiveFailures(GifRecordingDefaults.MaxConsecutiveCaptureFailures);

    Assert.True(shouldAbort);
  }

  [Fact]
  public async Task RecordAsync_Returns_Error_After_Three_Consecutive_Capture_Failures()
  {
    var service = new GifRecordingService(
      _ => throw new InvalidOperationException("capture failed"),
      (_, _) => Task.CompletedTask);

    var result = await service.RecordAsync(new WinRect(0, 0, 5, 5), CancellationToken.None);

    Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    Assert.Empty(result.Frames);
    Assert.False(result.HitDurationLimit);
  }

  [Fact]
  public void GetElapsedForAttempts_Returns_Thirty_Seconds_At_Max_Attempts()
  {
    var elapsed = GifRecordingService.GetElapsedForAttempts(GifRecordingDefaults.MaxCaptureAttempts);

    Assert.Equal(TimeSpan.FromSeconds(GifRecordingDefaults.MaxDurationSeconds), elapsed);
  }

  private static BitmapSource CreateFrame(int width, int height, Color color)
  {
    var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
    var pixels = new byte[width * height * 4];

    for (var index = 0; index < pixels.Length; index += 4)
    {
      pixels[index + 0] = color.B;
      pixels[index + 1] = color.G;
      pixels[index + 2] = color.R;
      pixels[index + 3] = color.A;
    }

    bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
    bitmap.Freeze();
    return bitmap;
  }
}
```

- [ ] **Step 2: Run the capture-service tests to verify they fail**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FullyQualifiedName~GifRecordingServiceTests`

Expected: FAIL with compile errors that `GifRecordingService` does not exist.

- [ ] **Step 3: Write the minimal capture service**

`ScreenTranslator/Services/GifRecordingService.cs`

```csharp
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

using ScreenTranslator.Interop;

using WinRect = System.Drawing.Rectangle;

namespace ScreenTranslator.Services;

public sealed record GifRecordingProgress(TimeSpan Elapsed, int CapturedFrames, int Attempts, int MaxAttempts);

public sealed record GifCaptureResult(IReadOnlyList<BitmapSource> Frames, int Attempts, bool HitDurationLimit, string? ErrorMessage);

public sealed class GifRecordingService
{
  private readonly Func<WinRect, BitmapSource> _captureFrame;
  private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
  private volatile bool _stopRequested;

  public GifRecordingService(
    Func<WinRect, BitmapSource>? captureFrame = null,
    Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
  {
    _captureFrame = captureFrame ?? CaptureFrame;
    _delayAsync = delayAsync ?? Task.Delay;
  }

  public Action? BeforeCapture { get; set; }
  public Action? AfterCapture { get; set; }

  public event Action<GifRecordingProgress>? ProgressChanged;

  public void RequestStop()
  {
    _stopRequested = true;
  }

  public async Task<GifCaptureResult> RecordAsync(WinRect region, CancellationToken cancellationToken)
  {
    var frames = new List<BitmapSource>();
    var attempts = 0;
    var consecutiveFailures = 0;

    while (!ShouldStop(_stopRequested, attempts))
    {
      cancellationToken.ThrowIfCancellationRequested();
      BeforeCapture?.Invoke();

      try
      {
        frames.Add(_captureFrame(region));
        consecutiveFailures = 0;
      }
      catch (Exception ex)
      {
        consecutiveFailures++;
        if (ShouldAbortForConsecutiveFailures(consecutiveFailures))
        {
          return new GifCaptureResult(frames, attempts, hitDurationLimit: false, ex.Message);
        }
      }
      finally
      {
        AfterCapture?.Invoke();
      }

      attempts++;
      ProgressChanged?.Invoke(new GifRecordingProgress(
        GetElapsedForAttempts(attempts),
        frames.Count,
        attempts,
        GifRecordingDefaults.MaxCaptureAttempts));

      if (ShouldStop(_stopRequested, attempts))
      {
        break;
      }

      await _delayAsync(TimeSpan.FromMilliseconds(GifRecordingDefaults.FrameIntervalMs), cancellationToken);
    }

    return new GifCaptureResult(
      frames,
      attempts,
      attempts >= GifRecordingDefaults.MaxCaptureAttempts && !_stopRequested,
      errorMessage: null);
  }

  internal static bool ShouldStop(bool stopRequested, int attempts)
  {
    return stopRequested || attempts >= GifRecordingDefaults.MaxCaptureAttempts;
  }

  internal static bool ShouldAbortForConsecutiveFailures(int consecutiveFailures)
  {
    return consecutiveFailures >= GifRecordingDefaults.MaxConsecutiveCaptureFailures;
  }

  internal static TimeSpan GetElapsedForAttempts(int attempts)
  {
    return TimeSpan.FromMilliseconds(attempts * GifRecordingDefaults.FrameIntervalMs);
  }

  private static BitmapSource CaptureFrame(WinRect region)
  {
    using var bitmap = CaptureService.CaptureRegion(region);
    var hBitmap = bitmap.GetHbitmap();

    try
    {
      var source = Imaging.CreateBitmapSourceFromHBitmap(
        hBitmap,
        IntPtr.Zero,
        Int32Rect.Empty,
        BitmapSizeOptions.FromEmptyOptions());

      source.Freeze();
      return source;
    }
    finally
    {
      NativeMethods.DeleteObject(hBitmap);
    }
  }
}
```

- [ ] **Step 4: Run the capture-service tests to verify they pass**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FullyQualifiedName~GifRecordingServiceTests`

Expected: PASS with `4` tests passing.

- [ ] **Step 5: Commit the capture service**

```bash
git add ScreenTranslator/Services/GifRecordingService.cs ScreenTranslator.Tests/GifRecordingServiceTests.cs
git commit -m "feat: add GIF recording capture service"
```

### Task 3: GIF Recording Session UI and Coordinator

**Files:**
- Create: `ScreenTranslator/Windows/GifRecordingControlWindow.xaml`
- Create: `ScreenTranslator/Windows/GifRecordingControlWindow.xaml.cs`
- Create: `ScreenTranslator/Services/GifRecordingSessionCoordinator.cs`
- Test: `ScreenTranslator.Tests/GifRecordingCoordinatorTests.cs`

- [ ] **Step 1: Write the failing coordinator tests**

```csharp
using System.Windows.Media.Imaging;
using ScreenTranslator.Services;
using ScreenTranslator.Windows;
using Xunit;

namespace ScreenTranslator.Tests;

public sealed class GifRecordingCoordinatorTests
{
  [Fact]
  public void ConfigureCaptureHooks_Assigns_Before_And_After_Capture_Callbacks()
  {
    var service = new GifRecordingService(
      _ => new WriteableBitmap(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null),
      (_, _) => Task.CompletedTask);
    var beforeCalled = false;
    var afterCalled = false;

    GifRecordingSessionCoordinator.ConfigureCaptureHooks(
      service,
      () => beforeCalled = true,
      () => afterCalled = true);

    service.BeforeCapture?.Invoke();
    service.AfterCapture?.Invoke();

    Assert.True(beforeCalled);
    Assert.True(afterCalled);
  }

  [Fact]
  public void ShouldEncodeResult_Returns_False_When_Capture_Was_Canceled()
  {
    var result = new GifCaptureResult([], 0, false, null);

    var shouldEncode = GifRecordingSessionCoordinator.ShouldEncodeResult(result, wasCanceled: true);

    Assert.False(shouldEncode);
  }

  [Fact]
  public void ShouldEncodeResult_Returns_True_When_Frames_Exist_And_No_Error()
  {
    var frame = new WriteableBitmap(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
    var result = new GifCaptureResult([frame], 1, false, null);

    var shouldEncode = GifRecordingSessionCoordinator.ShouldEncodeResult(result, wasCanceled: false);

    Assert.True(shouldEncode);
  }

  [Fact]
  public void BuildDefaultFileName_Appends_Gif_Extension()
  {
    var fileName = GifRecordingSessionCoordinator.BuildDefaultFileName(new DateTime(2026, 4, 20, 9, 30, 15));

    Assert.Equal("GifRecording_20260420_093015.gif", fileName);
  }

  [Fact]
  public void ShouldHandleStartupClick_Returns_False_Inside_Debounce_Window()
  {
    var shownAt = DateTime.UtcNow;

    var shouldHandle = GifRecordingControlWindow.ShouldHandleStartupClick(shownAt, shownAt.AddMilliseconds(120));

    Assert.False(shouldHandle);
  }
}
```

- [ ] **Step 2: Run the coordinator tests to verify they fail**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FullyQualifiedName~GifRecordingCoordinatorTests`

Expected: FAIL with compile errors that `GifRecordingSessionCoordinator` and `GifRecordingControlWindow` do not exist.

- [ ] **Step 3: Add the GIF recording control window XAML**

`ScreenTranslator/Windows/GifRecordingControlWindow.xaml`

```xml
<Window x:Class="ScreenTranslator.Windows.GifRecordingControlWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="{DynamicResource GifRecording_Title}"
        Width="360"
        Height="76"
        WindowStyle="None"
        ResizeMode="NoResize"
        ShowInTaskbar="False"
        WindowStartupLocation="Manual"
        Topmost="True"
        ShowActivated="False"
        Background="Transparent"
        AllowsTransparency="True">
  <Border Background="#EE1F1F1F" BorderBrush="#55333333" BorderThickness="1" CornerRadius="8" Padding="10,8">
    <Grid>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
      </Grid.ColumnDefinitions>

      <TextBlock Grid.Column="0"
                 x:Name="HintText"
                 Foreground="#D8FFFFFF"
                 VerticalAlignment="Center"
                 TextTrimming="CharacterEllipsis"
                 Margin="0,0,12,0" />

      <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">
        <Button x:Name="StopButton" Content="{DynamicResource GifRecording_Btn_Stop}" MinWidth="64" Margin="0,0,6,0" />
        <Button x:Name="CancelButton" Content="{DynamicResource GifRecording_Btn_Cancel}" MinWidth="64" />
      </StackPanel>
    </Grid>
  </Border>
</Window>
```

- [ ] **Step 4: Add the GIF recording control window code-behind**

`ScreenTranslator/Windows/GifRecordingControlWindow.xaml.cs`

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

using ScreenTranslator.Interop;
using ScreenTranslator.Services;

using WinRect = System.Drawing.Rectangle;
using WpfRect = System.Windows.Rect;

namespace ScreenTranslator.Windows;

public sealed partial class GifRecordingControlWindow : Window
{
  private static readonly TimeSpan StartupClickDebounce = TimeSpan.FromMilliseconds(250);
  private readonly DateTime _shownAtUtc = DateTime.UtcNow;

  public event Action? StopRequested;
  public event Action? CancelRequested;

  public GifRecordingControlWindow()
  {
    InitializeComponent();
    HintText.Text = BuildRecordingHint(TimeSpan.Zero);

    StopButton.Click += (_, _) =>
    {
      if (ShouldIgnoreStartupClick())
      {
        return;
      }

      StopRequested?.Invoke();
    };

    CancelButton.Click += (_, _) =>
    {
      if (ShouldIgnoreStartupClick())
      {
        return;
      }

      CancelRequested?.Invoke();
    };

    MouseLeftButtonDown += (_, e) =>
    {
      if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
      {
        try { DragMove(); } catch { }
      }
    };
  }

  internal static bool ShouldHandleStartupClick(DateTime shownAtUtc, DateTime nowUtc)
  {
    return nowUtc - shownAtUtc >= StartupClickDebounce;
  }

  internal static string BuildRecordingHint(TimeSpan elapsed)
  {
    return string.Format(
      CultureInfo.InvariantCulture,
      LocalizationService.GetString(
        "GifRecording_Hint_Recording",
        "Recording GIF {0:mm\\:ss} / {1:mm\\:ss} · {2} FPS"),
      elapsed,
      TimeSpan.FromSeconds(GifRecordingDefaults.MaxDurationSeconds),
      1000 / GifRecordingDefaults.FrameIntervalMs);
  }

  private bool ShouldIgnoreStartupClick()
  {
    return !ShouldHandleStartupClick(_shownAtUtc, DateTime.UtcNow);
  }

  protected override void OnSourceInitialized(EventArgs e)
  {
    base.OnSourceInitialized(e);
    var hwnd = new WindowInteropHelper(this).Handle;
    var ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
    ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
    NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex);
    NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
  }

  public void FocusWindow()
  {
    if (IsVisible)
    {
      Topmost = true;
    }
  }

  public bool PositionNearSelection(WinRect captureRegion, double dpiScaleX, double dpiScaleY)
  {
    const double gapDip = 12;
    var scaleX = dpiScaleX <= 0 ? 1.0 : dpiScaleX;
    var scaleY = dpiScaleY <= 0 ? 1.0 : dpiScaleY;

    var workPx = Screen.FromRectangle(captureRegion).WorkingArea;
    var work = new WpfRect(
      workPx.Left / scaleX,
      workPx.Top / scaleY,
      Math.Max(1, workPx.Width / scaleX),
      Math.Max(1, workPx.Height / scaleY));

    var selection = new WpfRect(
      captureRegion.Left / scaleX,
      captureRegion.Top / scaleY,
      Math.Max(1, captureRegion.Width / scaleX),
      Math.Max(1, captureRegion.Height / scaleY));

    var candidates = new[]
    {
      new WpfRect(selection.Right + gapDip, selection.Top, Width, Height),
      new WpfRect(selection.Left - Width - gapDip, selection.Top, Width, Height),
      new WpfRect(selection.Left, selection.Bottom + gapDip, Width, Height),
      new WpfRect(selection.Left, selection.Top - Height - gapDip, Width, Height),
    };

    foreach (var candidate in candidates)
    {
      if (Fits(work, candidate) && !candidate.IntersectsWith(selection))
      {
        Left = candidate.Left;
        Top = candidate.Top;
        return true;
      }
    }

    foreach (var candidate in candidates)
    {
      var clamped = ClampToWorkArea(candidate, work);
      if (!clamped.IntersectsWith(selection))
      {
        Left = clamped.Left;
        Top = clamped.Top;
        return true;
      }
    }

    return false;
  }

  public void UpdateProgress(TimeSpan elapsed)
  {
    HintText.Text = BuildRecordingHint(elapsed);
  }

  public void SetStoppingHint()
  {
      HintText.Text = LocalizationService.GetString("GifRecording_Hint_Stopping", "Stopping GIF recording.");
  }

  private static bool Fits(WpfRect work, WpfRect candidate)
  {
    return candidate.Left >= work.Left &&
           candidate.Top >= work.Top &&
           candidate.Right <= work.Right &&
           candidate.Bottom <= work.Bottom;
  }

  private static WpfRect ClampToWorkArea(WpfRect candidate, WpfRect work)
  {
    var x = Math.Clamp(candidate.Left, work.Left, work.Right - candidate.Width);
    var y = Math.Clamp(candidate.Top, work.Top, work.Bottom - candidate.Height);
    return new WpfRect(x, y, candidate.Width, candidate.Height);
  }
}
```

- [ ] **Step 5: Add the GIF session coordinator**

`ScreenTranslator/Services/GifRecordingSessionCoordinator.cs`

```csharp
using System.Globalization;
using System.IO;
using System.Windows;

using ScreenTranslator.Models;
using ScreenTranslator.Windows;

using WinRect = System.Drawing.Rectangle;
using WpfApplication = System.Windows.Application;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace ScreenTranslator.Services;

public sealed class GifRecordingSessionCoordinator : IDisposable
{
  private readonly AppSettings _settings;
  private readonly WinRect _captureRegion;
  private readonly double _dpiScaleX;
  private readonly double _dpiScaleY;
  private readonly GifRecordingService _recordingService;
  private readonly GifEncodingService _encodingService;
  private readonly SelectionFrameWindow _selectionFrameWindow;
  private readonly GifRecordingControlWindow _controlWindow;
  private readonly CancellationTokenSource _cts = new();

  private bool _disposed;
  private bool _wasCanceled;
  private bool _selectionFrameHiddenByCaptureHook;
  private bool _controlWindowHiddenByCaptureHook;

  public event Action? Closed;

  public GifRecordingSessionCoordinator(
    AppSettings settings,
    WinRect captureRegion,
    double dpiScaleX,
    double dpiScaleY,
    GifRecordingService? recordingService = null,
    GifEncodingService? encodingService = null)
  {
    _settings = settings;
    _captureRegion = captureRegion;
    _dpiScaleX = dpiScaleX;
    _dpiScaleY = dpiScaleY;
    _recordingService = recordingService ?? new GifRecordingService();
    _encodingService = encodingService ?? new GifEncodingService();

    _selectionFrameWindow = new SelectionFrameWindow();
    _selectionFrameWindow.Initialize(captureRegion, dpiScaleX, dpiScaleY);
    _selectionFrameWindow.SetLocked(true);

    _controlWindow = new GifRecordingControlWindow();
    _controlWindow.StopRequested += OnStopRequested;
    _controlWindow.CancelRequested += OnCancelRequested;
  }

  public void Start()
  {
    if (_disposed)
    {
      return;
    }

    ConfigureCaptureHooks(_recordingService, HideOverlaysForCapture, ShowOverlaysAfterCapture);
    _recordingService.ProgressChanged += OnProgressChanged;

    _selectionFrameWindow.Show();
    _controlWindow.PositionNearSelection(_captureRegion, _dpiScaleX, _dpiScaleY);
    _controlWindow.Show();
    _controlWindow.UpdateProgress(TimeSpan.Zero);

    _ = RunAsync();
  }

  public void Focus()
  {
    if (_disposed)
    {
      return;
    }

    _controlWindow.FocusWindow();
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    _recordingService.ProgressChanged -= OnProgressChanged;

    try
    {
      _cts.Cancel();
    }
    catch
    {
      // ignore
    }
    finally
    {
      _cts.Dispose();
    }

    SafeClose(_selectionFrameWindow);
    SafeClose(_controlWindow);

    Closed?.Invoke();
  }

  internal static void ConfigureCaptureHooks(GifRecordingService service, Action beforeCapture, Action afterCapture)
  {
    service.BeforeCapture = WrapCaptureHook(beforeCapture, callback => InvokeOnUiThreadOrInline(callback));
    service.AfterCapture = WrapCaptureHook(afterCapture, callback => InvokeOnUiThreadOrInline(callback));
  }

  internal static Action WrapCaptureHook(Action hook, Action<Action> invokeOnUiThread)
  {
    return () => invokeOnUiThread(hook);
  }

  internal static void InvokeOnUiThreadOrInline(Action callback)
  {
    var dispatcher = WpfApplication.Current?.Dispatcher;
    if (dispatcher is null)
    {
      callback();
      return;
    }

    dispatcher.Invoke(callback);
  }

  internal static bool ShouldEncodeResult(GifCaptureResult result, bool wasCanceled)
  {
    return !wasCanceled &&
           string.IsNullOrWhiteSpace(result.ErrorMessage) &&
           result.Frames.Count > 0;
  }

  internal static string BuildDefaultFileName(DateTime now)
  {
    return string.Format(CultureInfo.InvariantCulture, "GifRecording_{0:yyyyMMdd_HHmmss}.gif", now);
  }

  private async Task RunAsync()
  {
    try
    {
      var result = await _recordingService.RecordAsync(_captureRegion, _cts.Token);

      await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
      {
        if (_disposed)
        {
          return;
        }

        CompleteRecording(result);
      });
    }
    catch (OperationCanceledException)
    {
      if (!_disposed)
      {
        Dispose();
      }
    }
  }

  private void OnStopRequested()
  {
    _controlWindow.SetStoppingHint();
    _recordingService.RequestStop();
  }

  private void OnCancelRequested()
  {
    _wasCanceled = true;
    Dispose();
  }

  private void OnProgressChanged(GifRecordingProgress progress)
  {
    WpfApplication.Current.Dispatcher.BeginInvoke(() =>
    {
      if (_disposed)
      {
        return;
      }

      _controlWindow.UpdateProgress(progress.Elapsed);
    });
  }

  private void CompleteRecording(GifCaptureResult result)
  {
    if (!ShouldEncodeResult(result, _wasCanceled))
    {
      if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
      {
        MessageBox.Show(
          LocalizationService.GetString("GifRecording_Error_CaptureFailed", "GIF recording failed."),
          LocalizationService.GetString("AppName", "transtools"),
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
      }

      Dispose();
      return;
    }

    var bytes = _encodingService.Encode(result.Frames, GifRecordingDefaults.FrameIntervalMs);
    var dialog = new WpfSaveFileDialog
    {
      Filter = LocalizationService.GetString("FileDialog_GifFilter", "GIF Image|*.gif"),
      DefaultExt = ".gif",
      FileName = BuildDefaultFileName(DateTime.Now),
      InitialDirectory = GetSafeSaveDirectory(),
      AddExtension = true,
      OverwritePrompt = true,
    };

    try
    {
      if (dialog.ShowDialog() == true)
      {
        File.WriteAllBytes(dialog.FileName, bytes);
      }
    }
    catch
    {
      MessageBox.Show(
        LocalizationService.GetString("GifRecording_Error_SaveFailed", "Failed to save GIF."),
        LocalizationService.GetString("AppName", "transtools"),
        MessageBoxButton.OK,
        MessageBoxImage.Warning);
    }
    finally
    {
      Dispose();
    }
  }

  private string GetSafeSaveDirectory()
  {
    var preferred = _settings.ScreenshotSavePath?.Trim();
    if (!string.IsNullOrWhiteSpace(preferred))
    {
      try
      {
        Directory.CreateDirectory(preferred);
        return preferred;
      }
      catch
      {
        // fall through
      }
    }

    return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
  }

  private void HideOverlaysForCapture()
  {
    if (_disposed)
    {
      return;
    }

    _selectionFrameHiddenByCaptureHook = false;
    _controlWindowHiddenByCaptureHook = false;

    if (_selectionFrameWindow.IsVisible)
    {
      _selectionFrameWindow.Hide();
      _selectionFrameHiddenByCaptureHook = true;
    }

    if (_controlWindow.IsVisible)
    {
      _controlWindow.Hide();
      _controlWindowHiddenByCaptureHook = true;
    }
  }

  private void ShowOverlaysAfterCapture()
  {
    if (_disposed)
    {
      return;
    }

    if (_selectionFrameHiddenByCaptureHook)
    {
      _selectionFrameWindow.Show();
      _selectionFrameHiddenByCaptureHook = false;
    }

    if (_controlWindowHiddenByCaptureHook)
    {
      _controlWindow.PositionNearSelection(_captureRegion, _dpiScaleX, _dpiScaleY);
      _controlWindow.Show();
      _controlWindowHiddenByCaptureHook = false;
    }
  }

  private static void SafeClose(Window window)
  {
    try
    {
      window.Close();
    }
    catch
    {
      // ignore
    }
  }
}
```

- [ ] **Step 6: Run the coordinator tests to verify they pass**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FullyQualifiedName~GifRecordingCoordinatorTests`

Expected: PASS with `5` tests passing.

- [ ] **Step 7: Commit the session UI**

```bash
git add ScreenTranslator/Services/GifRecordingSessionCoordinator.cs ScreenTranslator/Windows/GifRecordingControlWindow.xaml ScreenTranslator/Windows/GifRecordingControlWindow.xaml.cs ScreenTranslator.Tests/GifRecordingCoordinatorTests.cs
git commit -m "feat: add GIF recording session UI"
```

### Task 4: Screenshot Flow Integration and Localization

**Files:**
- Modify: `ScreenTranslator/Services/ScreenshotController.cs`
- Modify: `ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml`
- Modify: `ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml.cs`
- Modify: `ScreenTranslator/Resources/Strings.en.xaml`
- Modify: `ScreenTranslator/Resources/Strings.zh-CN.xaml`
- Test: `ScreenTranslator.Tests/ScreenshotOverlayWindowTests.cs`

- [ ] **Step 1: Update the toolbar-order test to require `GIF`**

```csharp
[Fact]
public void GetToolbarButtonOrder_Returns_Configured_Screenshot_Tool_Order()
{
  var order = ScreenshotOverlayWindow.GetToolbarButtonOrder();

  Assert.Equal(
    [
      "Save",
      "Copy",
      "LongScreenshot",
      "GIF",
      "Redraw",
      "Pin",
      "Brush",
      "Rectangle",
      "Mosaic",
      "Undo",
      "Cancel",
    ],
    order);
}
```

- [ ] **Step 2: Run the screenshot overlay tests to verify they fail**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FullyQualifiedName~ScreenshotOverlayWindowTests`

Expected: FAIL because `GetToolbarButtonOrder()` still returns the old list without `GIF`.

- [ ] **Step 3: Add the `GIF` button to the screenshot overlay XAML**

`ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml`

```xml
<Border x:Name="Toolbar"
        Background="#EE333333"
        CornerRadius="4"
        Padding="4"
        Visibility="Collapsed">
  <StackPanel Orientation="Horizontal">
    <Button x:Name="BtnSave" Content="{DynamicResource Screenshot_Save}" Style="{StaticResource ToolbarButton}" Click="BtnSave_Click" ToolTip="{DynamicResource Screenshot_Tooltip_Save}" />
    <Button x:Name="BtnCopy" Content="{DynamicResource Screenshot_Copy}" Style="{StaticResource ToolbarButton}" Click="BtnCopy_Click" ToolTip="{DynamicResource Screenshot_Tooltip_Copy}" />
    <Button x:Name="BtnLongScreenshot" Content="{DynamicResource Screenshot_Long}" Style="{StaticResource ToolbarButton}" Click="BtnLongScreenshot_Click" ToolTip="{DynamicResource Screenshot_Tooltip_Long}" />
    <Button x:Name="BtnGif" Content="{DynamicResource Screenshot_Gif}" Style="{StaticResource ToolbarButton}" Click="BtnGif_Click" ToolTip="{DynamicResource Screenshot_Tooltip_Gif}" />
    <Button x:Name="BtnRedraw" Content="{DynamicResource Screenshot_Redraw}" Style="{StaticResource ToolbarButton}" Click="BtnRedraw_Click" ToolTip="{DynamicResource Screenshot_Tooltip_Redraw}" />
    <Button x:Name="BtnPin" Content="{DynamicResource Screenshot_Pin}" Style="{StaticResource ToolbarButton}" Click="BtnPin_Click" ToolTip="{DynamicResource Screenshot_Tooltip_Pin}" />
    <Button x:Name="BtnBrush" Content="{DynamicResource Screenshot_Annotate_Brush}" Style="{StaticResource ToolbarButton}" Click="BtnBrush_Click" ToolTip="{DynamicResource Screenshot_Tooltip_Annotate_Brush}" />
    <Button x:Name="BtnRectangle" Content="{DynamicResource Screenshot_Annotate_Rectangle}" Style="{StaticResource ToolbarButton}" Click="BtnRectangle_Click" ToolTip="{DynamicResource Screenshot_Tooltip_Annotate_Rectangle}" />
    <Button x:Name="BtnMosaic" Content="{DynamicResource Screenshot_Annotate_Mosaic}" Style="{StaticResource ToolbarButton}" Click="BtnMosaic_Click" ToolTip="{DynamicResource Screenshot_Tooltip_Annotate_Mosaic}" />
    <Button x:Name="BtnUndo" Content="{DynamicResource Screenshot_Annotate_Undo}" Style="{StaticResource ToolbarButton}" Click="BtnUndo_Click" ToolTip="{DynamicResource Screenshot_Tooltip_Annotate_Undo}" />
    <Button x:Name="BtnCancel" Content="{DynamicResource Screenshot_Cancel}" Style="{StaticResource ToolbarButton}" Click="BtnCancel_Click" ToolTip="{DynamicResource Screenshot_Tooltip_Cancel}" />
  </StackPanel>
</Border>
```

- [ ] **Step 4: Wire the `GIF` action in the screenshot overlay code-behind**

`ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml.cs`

```csharp
private readonly Action<WinRect, double, double>? _onGifRecordingRequested;

public ScreenshotOverlayWindow(
  AppSettings settings,
  Action<BitmapSource, WinRect, double, double> onPinRequested,
  Action? onFreeformRequested = null,
  Action<WinRect, double, double>? onLongScreenshotRequested = null,
  Action<WinRect, double, double>? onGifRecordingRequested = null)
{
  _settings = settings;
  _onPinRequested = onPinRequested;
  _onFreeformRequested = onFreeformRequested;
  _onLongScreenshotRequested = onLongScreenshotRequested;
  _onGifRecordingRequested = onGifRecordingRequested;

  InitializeComponent();

  Loaded += OnLoaded;
  MouseLeftButtonDown += OnMouseLeftButtonDown;
  MouseMove += OnMouseMove;
  MouseLeftButtonUp += OnMouseLeftButtonUp;
  MouseDown += OnMouseDown;
  KeyDown += OnKeyDown;
}

internal static string[] GetToolbarButtonOrder()
{
  return
  [
    "Save",
    "Copy",
    "LongScreenshot",
    "GIF",
    "Redraw",
    "Pin",
    "Brush",
    "Rectangle",
    "Mosaic",
    "Undo",
    "Cancel",
  ];
}

private void BtnGif_Click(object sender, RoutedEventArgs e)
{
  if (_selectedRegion.Width <= 0 || _selectedRegion.Height <= 0)
  {
    return;
  }

  DiscardAnnotations();
  Close();
  _onGifRecordingRequested?.Invoke(_selectedRegion, _dpiScaleX, _dpiScaleY);
}
```

- [ ] **Step 5: Integrate GIF sessions into the screenshot controller**

`ScreenTranslator/Services/ScreenshotController.cs`

```csharp
private GifRecordingSessionCoordinator? _gifRecordingSession;

public void StartScreenshot()
{
  if (_gifRecordingSession is not null)
  {
    _gifRecordingSession.Focus();
    return;
  }

  if (_longScreenshotSession is not null)
  {
    _longScreenshotSession.Focus();
    return;
  }

  if (_overlayWindow != null)
  {
    _overlayWindow.Focus();
    return;
  }

  if (_freeformWindow != null)
  {
    _freeformWindow.Focus();
    return;
  }

  _overlayWindow = new Windows.ScreenshotOverlayWindow(
    _settings,
    OnPinRequested,
    StartFreeformScreenshot,
    StartLongScreenshot,
    StartGifRecording);
  _overlayWindow.Closed += (_, _) => _overlayWindow = null;
  _overlayWindow.Show();
}

public void StartGifRecording(WinRect region, double dpiScaleX, double dpiScaleY)
{
  if (_gifRecordingSession is not null)
  {
    _gifRecordingSession.Focus();
    return;
  }

  if (_longScreenshotSession is not null)
  {
    _longScreenshotSession.Focus();
    return;
  }

  _overlayWindow?.Close();

  _gifRecordingSession = new GifRecordingSessionCoordinator(_settings, region, dpiScaleX, dpiScaleY);
  _gifRecordingSession.Closed += () =>
  {
    _gifRecordingSession?.Dispose();
    _gifRecordingSession = null;
  };

  _gifRecordingSession.Start();
}

public void CloseAllPinWindows()
{
  _overlayWindow?.Close();
  _freeformWindow?.Close();
  _gifRecordingSession?.Dispose();
  _gifRecordingSession = null;
  _longScreenshotSession?.Dispose();
  _longScreenshotSession = null;

  foreach (var window in _pinWindows.ToList())
  {
    window.Close();
  }

  _pinWindows.Clear();
}
```

- [ ] **Step 6: Add localized strings for the GIF UI**

`ScreenTranslator/Resources/Strings.en.xaml`

```xml
<sys:String x:Key="Screenshot_Gif">GIF</sys:String>
<sys:String x:Key="Screenshot_Tooltip_Gif">Record the selected region as an animated GIF</sys:String>

<sys:String x:Key="GifRecording_Title">GIF Recording</sys:String>
<sys:String x:Key="GifRecording_Btn_Stop">Stop</sys:String>
<sys:String x:Key="GifRecording_Btn_Cancel">Cancel</sys:String>
<sys:String x:Key="GifRecording_Hint_Recording">Recording GIF {0:mm\:ss} / {1:mm\:ss} · {2} FPS</sys:String>
<sys:String x:Key="GifRecording_Hint_Stopping">Stopping GIF recording.</sys:String>
<sys:String x:Key="GifRecording_Error_CaptureFailed">GIF recording failed.</sys:String>
<sys:String x:Key="GifRecording_Error_SaveFailed">Failed to save GIF.</sys:String>

<sys:String x:Key="FileDialog_GifFilter">GIF Image|*.gif</sys:String>
```

`ScreenTranslator/Resources/Strings.zh-CN.xaml`

```xml
<sys:String x:Key="Screenshot_Gif">GIF</sys:String>
<sys:String x:Key="Screenshot_Tooltip_Gif">将所选区域录制为 GIF 动图</sys:String>

<sys:String x:Key="GifRecording_Title">GIF 录制</sys:String>
<sys:String x:Key="GifRecording_Btn_Stop">停止</sys:String>
<sys:String x:Key="GifRecording_Btn_Cancel">取消</sys:String>
<sys:String x:Key="GifRecording_Hint_Recording">正在录制 GIF {0:mm\:ss} / {1:mm\:ss} · {2} FPS</sys:String>
<sys:String x:Key="GifRecording_Hint_Stopping">正在停止 GIF 录制。</sys:String>
<sys:String x:Key="GifRecording_Error_CaptureFailed">GIF 录制失败。</sys:String>
<sys:String x:Key="GifRecording_Error_SaveFailed">保存 GIF 失败。</sys:String>

<sys:String x:Key="FileDialog_GifFilter">GIF 动图|*.gif</sys:String>
```

- [ ] **Step 7: Run the integrated verification**

Run: `dotnet build .\ScreenTranslator\ScreenTranslator.csproj -c Release`
Expected: `Build succeeded.`

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release`
Expected: PASS with the existing suite plus the new GIF tests all green.

- [ ] **Step 8: Commit the screenshot integration**

```bash
git add ScreenTranslator/Services/ScreenshotController.cs ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml.cs ScreenTranslator/Resources/Strings.en.xaml ScreenTranslator/Resources/Strings.zh-CN.xaml ScreenTranslator.Tests/ScreenshotOverlayWindowTests.cs
git commit -m "feat: integrate region GIF recording"
```

## Final Manual Verification

- Trigger screenshot mode with `Ctrl+Alt+S`.
- Drag a rectangular region and confirm the toolbar order is `Save`, `Copy`, `Long Screenshot`, `GIF`, `Redraw`, `Pin`, `Brush`, `Rectangle`, `Mosaic`, `Undo`, `Cancel`.
- Click `GIF` and confirm the screenshot overlay closes before recording begins.
- Confirm the locked selection frame and compact GIF control window appear outside the capture region.
- Let one recording auto-stop at `30` seconds and save it as `.gif`.
- Stop one recording manually before `30` seconds and save it as `.gif`.
- Cancel one recording and confirm no save dialog appears.
- Open the saved GIFs and confirm the overlay UI was not captured into the animation.
