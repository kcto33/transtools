using System.Windows.Media;
using System.Windows.Media.Imaging;

using ScreenTranslator.Models;
using ScreenTranslator.Services;

using Xunit;

using WinRect = System.Drawing.Rectangle;

namespace ScreenTranslator.Tests;

public sealed class ScreenshotControllerTests
{
  [Fact]
  public async Task StartScreenshotAsync_CapturesBackground_BeforeCreatingAndShowingOverlay()
  {
    var sequence = new List<string>();
    var overlay = new FakeOverlayWindow(() => sequence.Add("show"));
    var capturedBackground = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);

    var controller = CreateController(
      overlayFactory: (_, initialBackground, _, _, _, _) =>
      {
        Assert.Same(capturedBackground, initialBackground);
        Assert.Equal(["capture"], sequence);
        sequence.Add("factory");
        return overlay;
      },
      overlayBackgroundCaptureAsync: () =>
      {
        sequence.Add("capture");
        return Task.FromResult<BitmapSource?>(capturedBackground);
      });

    await controller.StartScreenshotAsync();

    Assert.Equal(["capture", "factory", "show"], sequence);
  }

  [Fact]
  public void StartScreenshot_WiresGifCallback_AndStartsGifSession_WhenOverlayRequestsIt()
  {
    var overlay = new FakeOverlayWindow();
    var gifSession = new FakeRegionSession();
    Action<WinRect, double, double>? gifRequested = null;

    var controller = CreateController(
      overlayFactory: (_, _, _, _, _, onGifRequested) =>
      {
        gifRequested = onGifRequested;
        return overlay;
      },
      gifFactory: (_, _, _, _, _) => gifSession);

    controller.StartScreenshot();

    Assert.NotNull(gifRequested);

    gifRequested!(new WinRect(10, 20, 120, 80), 1.25, 1.25);

    Assert.True(overlay.CloseCalled);
    Assert.True(gifSession.StartCalled);
  }

  [Fact]
  public void StartScreenshot_FocusesExistingGifSession_InsteadOfCreatingOverlay()
  {
    var overlayCreated = false;
    var gifSession = new FakeRegionSession();

    var controller = CreateController(
      overlayFactory: (_, _, _, _, _, _) =>
      {
        overlayCreated = true;
        return new FakeOverlayWindow();
      },
      gifFactory: (_, _, _, _, _) => gifSession);

    controller.StartGifRecording(new WinRect(0, 0, 64, 64), 1.0, 1.0);

    gifSession.StartCalled = false;
    controller.StartScreenshot();

    Assert.False(overlayCreated);
    Assert.True(gifSession.FocusCalled);
    Assert.False(gifSession.StartCalled);
  }

  [Fact]
  public void CloseAllPinWindows_DisposesGifSession()
  {
    var gifSession = new FakeRegionSession();
    var controller = CreateController(gifFactory: (_, _, _, _, _) => gifSession);

    controller.StartGifRecording(new WinRect(0, 0, 64, 64), 1.0, 1.0);
    controller.CloseAllPinWindows();

    Assert.True(gifSession.DisposeCalled);
  }

  [Fact]
  public void StartScreenshot_CreatesNewOverlay_AfterGifSessionCloses()
  {
    var overlayCreated = 0;
    var gifSession = new FakeRegionSession();

    var controller = CreateController(
      overlayFactory: (_, _, _, _, _, _) =>
      {
        overlayCreated++;
        return new FakeOverlayWindow();
      },
      gifFactory: (_, _, _, _, _) => gifSession);

    controller.StartGifRecording(new WinRect(0, 0, 64, 64), 1.0, 1.0);
    gifSession.RaiseClosed();

    controller.StartScreenshot();

    Assert.Equal(1, overlayCreated);
    Assert.True(gifSession.DisposeCalled);
  }

  private static ScreenshotController CreateController(
    Func<AppSettings, BitmapSource?, Action<BitmapSource, WinRect, double, double>, Action?, Action<WinRect, double, double>?, Action<WinRect, double, double>?, IScreenshotOverlaySessionWindow>? overlayFactory = null,
    Func<AppSettings, Action<BitmapSource, WinRect, double, double>, IScreenshotFreeformWindow>? freeformFactory = null,
    Func<AppSettings, WinRect, double, double, Action<BitmapSource, WinRect, double, double>, IScreenshotRegionSession>? longFactory = null,
    Func<AppSettings, WinRect, double, double, Action<BitmapSource, WinRect, double, double>, IScreenshotRegionSession>? gifFactory = null,
    Func<Task<BitmapSource?>>? overlayBackgroundCaptureAsync = null)
  {
    return new ScreenshotController(
      new AppSettings(),
      overlayFactory ?? ((_, _, _, _, _, _) => new FakeOverlayWindow()),
      freeformFactory ?? ((_, _) => new FakeFreeformWindow()),
      longFactory ?? ((_, _, _, _, _) => new FakeRegionSession()),
      gifFactory ?? ((_, _, _, _, _) => new FakeRegionSession()),
      overlayBackgroundCaptureAsync ?? (() => Task.FromResult<BitmapSource?>(null)));
  }

  private sealed class FakeOverlayWindow : IScreenshotOverlaySessionWindow
  {
    public event EventHandler? Closed;

    private readonly Action? _onShow;

    public bool ShowCalled { get; private set; }
    public bool FocusCalled { get; private set; }
    public bool CloseCalled { get; private set; }

    public FakeOverlayWindow(Action? onShow = null)
    {
      _onShow = onShow;
    }

    public void Show()
    {
      ShowCalled = true;
      _onShow?.Invoke();
    }

    public bool Focus()
    {
      FocusCalled = true;
      return true;
    }

    public void Close()
    {
      CloseCalled = true;
      Closed?.Invoke(this, EventArgs.Empty);
    }
  }

  private sealed class FakeFreeformWindow : IScreenshotFreeformWindow
  {
    public event EventHandler? Closed;

    public bool ShowCalled { get; private set; }
    public bool FocusCalled { get; private set; }
    public bool CloseCalled { get; private set; }

    public void Show()
    {
      ShowCalled = true;
    }

    public bool Focus()
    {
      FocusCalled = true;
      return true;
    }

    public void Close()
    {
      CloseCalled = true;
      Closed?.Invoke(this, EventArgs.Empty);
    }
  }

  private sealed class FakeRegionSession : IScreenshotRegionSession
  {
    public event Action? Closed;

    private bool _disposed;

    public bool StartCalled { get; set; }
    public bool FocusCalled { get; private set; }
    public bool DisposeCalled { get; private set; }

    public void Start()
    {
      StartCalled = true;
    }

    public void Focus()
    {
      FocusCalled = true;
    }

    public void Dispose()
    {
      if (_disposed)
      {
        return;
      }

      _disposed = true;
      DisposeCalled = true;
      Closed?.Invoke();
    }

    public void RaiseClosed()
    {
      Closed?.Invoke();
    }
  }
}
