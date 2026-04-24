using System.Windows.Media.Imaging;

using ScreenTranslator.Models;

using WinRect = System.Drawing.Rectangle;
using WpfApplication = System.Windows.Application;

namespace ScreenTranslator.Services;

internal interface IScreenshotOverlaySessionWindow
{
  event EventHandler Closed;
  void Show();
  bool Focus();
  void Close();
}

internal interface IScreenshotFreeformWindow
{
  event EventHandler Closed;
  void Show();
  bool Focus();
  void Close();
}

internal interface IScreenshotRegionSession : IDisposable
{
  event Action? Closed;
  void Start();
  void Focus();
}

/// <summary>
/// Manages screenshot flow and pin windows.
/// </summary>
public sealed class ScreenshotController
{
  private readonly AppSettings _settings;
  private readonly List<Windows.PinWindow> _pinWindows = [];
  private readonly Func<AppSettings, BitmapSource?, Action<BitmapSource, WinRect, double, double>, Action?, Action<WinRect, double, double>?, Action<WinRect, double, double>?, IScreenshotOverlaySessionWindow> _overlayFactory;
  private readonly Func<AppSettings, Action<BitmapSource, WinRect, double, double>, IScreenshotFreeformWindow> _freeformFactory;
  private readonly Func<AppSettings, WinRect, double, double, Action<BitmapSource, WinRect, double, double>, IScreenshotRegionSession> _longScreenshotFactory;
  private readonly Func<AppSettings, WinRect, double, double, Action<BitmapSource, WinRect, double, double>, IScreenshotRegionSession> _gifRecordingFactory;
  private readonly Func<Task<BitmapSource?>> _overlayBackgroundCaptureAsync;
  private IScreenshotOverlaySessionWindow? _overlayWindow;
  private IScreenshotFreeformWindow? _freeformWindow;
  private IScreenshotRegionSession? _longScreenshotSession;
  private IScreenshotRegionSession? _gifRecordingSession;
  private Task? _overlayStartTask;

  public ScreenshotController(AppSettings settings)
    : this(
        settings,
        (appSettings, initialCapturedScreen, onPinRequested, onFreeformRequested, onLongScreenshotRequested, onGifRecordingRequested) =>
          new Windows.ScreenshotOverlayWindow(
            appSettings,
            onPinRequested,
            onFreeformRequested,
            onLongScreenshotRequested,
            onGifRecordingRequested,
            initialCapturedScreen),
        (appSettings, onPinRequested) => new Windows.FreeformScreenshotWindow(appSettings, onPinRequested),
        (appSettings, region, dpiScaleX, dpiScaleY, onPinRequested) =>
          new LongScreenshotSessionCoordinator(appSettings, region, dpiScaleX, dpiScaleY, onPinRequested),
        (appSettings, region, dpiScaleX, dpiScaleY, _) =>
          new GifRecordingSessionCoordinator(appSettings, region, dpiScaleX, dpiScaleY),
        CaptureInitialOverlayBackgroundAsync)
  {
  }

  internal ScreenshotController(
    AppSettings settings,
    Func<AppSettings, BitmapSource?, Action<BitmapSource, WinRect, double, double>, Action?, Action<WinRect, double, double>?, Action<WinRect, double, double>?, IScreenshotOverlaySessionWindow> overlayFactory,
    Func<AppSettings, Action<BitmapSource, WinRect, double, double>, IScreenshotFreeformWindow> freeformFactory,
    Func<AppSettings, WinRect, double, double, Action<BitmapSource, WinRect, double, double>, IScreenshotRegionSession> longScreenshotFactory,
    Func<AppSettings, WinRect, double, double, Action<BitmapSource, WinRect, double, double>, IScreenshotRegionSession> gifRecordingFactory,
    Func<Task<BitmapSource?>> overlayBackgroundCaptureAsync)
  {
    _settings = settings;
    _overlayFactory = overlayFactory;
    _freeformFactory = freeformFactory;
    _longScreenshotFactory = longScreenshotFactory;
    _gifRecordingFactory = gifRecordingFactory;
    _overlayBackgroundCaptureAsync = overlayBackgroundCaptureAsync;
  }

  /// <summary>
  /// Start the screenshot selection process.
  /// </summary>
  public void StartScreenshot()
  {
    _ = StartScreenshotAsync();
  }

  public Task StartScreenshotAsync()
  {
    if (_longScreenshotSession is not null)
    {
      _longScreenshotSession.Focus();
      return Task.CompletedTask;
    }

    if (_gifRecordingSession is not null)
    {
      _gifRecordingSession.Focus();
      return Task.CompletedTask;
    }

    if (_overlayWindow != null)
    {
      _overlayWindow.Focus();
      return Task.CompletedTask;
    }

    if (_freeformWindow != null)
    {
      _freeformWindow.Focus();
      return Task.CompletedTask;
    }

    if (_overlayStartTask is not null && !_overlayStartTask.IsCompleted)
    {
      return _overlayStartTask;
    }

    _overlayStartTask = StartScreenshotCoreAsync();
    return _overlayStartTask;
  }

  /// <summary>
  /// Start the freeform screenshot selection process.
  /// </summary>
  public void StartFreeformScreenshot()
  {
    if (_longScreenshotSession is not null)
    {
      _longScreenshotSession.Focus();
      return;
    }

    if (_gifRecordingSession is not null)
    {
      _gifRecordingSession.Focus();
      return;
    }

    if (_freeformWindow != null)
    {
      _freeformWindow.Focus();
      return;
    }

    _overlayWindow?.Close();

    _freeformWindow = _freeformFactory(_settings, OnPinRequested);
    _freeformWindow.Closed += (_, _) => _freeformWindow = null;
    _freeformWindow.Show();
  }

  public void StartLongScreenshot(WinRect region, double dpiScaleX, double dpiScaleY)
  {
    if (_longScreenshotSession is not null)
    {
      _longScreenshotSession.Focus();
      return;
    }

    if (_gifRecordingSession is not null)
    {
      _gifRecordingSession.Focus();
      return;
    }

    _overlayWindow?.Close();

    _longScreenshotSession = _longScreenshotFactory(
      _settings,
      region,
      dpiScaleX,
      dpiScaleY,
      OnPinRequested);

    _longScreenshotSession.Closed += () =>
    {
      _longScreenshotSession?.Dispose();
      _longScreenshotSession = null;
    };

    _longScreenshotSession.Start();
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

    _gifRecordingSession = _gifRecordingFactory(
      _settings,
      region,
      dpiScaleX,
      dpiScaleY,
      OnPinRequested);

    _gifRecordingSession.Closed += () =>
    {
      _gifRecordingSession?.Dispose();
      _gifRecordingSession = null;
    };

    _gifRecordingSession.Start();
  }

  private void OnPinRequested(BitmapSource image, WinRect region, double dpiScaleX, double dpiScaleY)
  {
    WpfApplication.Current.Dispatcher.Invoke(() =>
    {
      var pinWindow = new Windows.PinWindow();
      pinWindow.SetImage(image, dpiScaleX, dpiScaleY);

      // Position at the selection location (convert physical pixels to DIPs)
      pinWindow.Left = region.X / dpiScaleX;
      pinWindow.Top = region.Y / dpiScaleY;

      pinWindow.Closed += (_, _) => _pinWindows.Remove(pinWindow);
      _pinWindows.Add(pinWindow);
      pinWindow.Show();
    });
  }

  /// <summary>
  /// Close all pin windows.
  /// </summary>
  public void CloseAllPinWindows()
  {
    _overlayWindow?.Close();
    _freeformWindow?.Close();
    _longScreenshotSession?.Dispose();
    _longScreenshotSession = null;
    _gifRecordingSession?.Dispose();
    _gifRecordingSession = null;

    foreach (var window in _pinWindows.ToList())
    {
      window.Close();
    }

    _pinWindows.Clear();
  }

  private async Task StartScreenshotCoreAsync()
  {
    try
    {
      BitmapSource? initialCapturedScreen = null;
      try
      {
        initialCapturedScreen = await _overlayBackgroundCaptureAsync();
      }
      catch
      {
        // Fall back to the overlay's legacy capture path if pre-capture fails.
      }

      if (_longScreenshotSession is not null)
      {
        _longScreenshotSession.Focus();
        return;
      }

      if (_gifRecordingSession is not null)
      {
        _gifRecordingSession.Focus();
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

      _overlayWindow = _overlayFactory(
        _settings,
        initialCapturedScreen,
        OnPinRequested,
        StartFreeformScreenshot,
        StartLongScreenshot,
        StartGifRecording);
      _overlayWindow.Closed += (_, _) => _overlayWindow = null;
      _overlayWindow.Show();
    }
    finally
    {
      _overlayStartTask = null;
    }
  }

  private static Task<BitmapSource?> CaptureInitialOverlayBackgroundAsync()
  {
    return Task.Run(() =>
    {
      var virtualScreen = ScreenMetricsService.GetVirtualScreenBoundsPx();
      using var bitmap = new System.Drawing.Bitmap(virtualScreen.Width, virtualScreen.Height);
      using var graphics = System.Drawing.Graphics.FromImage(bitmap);
      graphics.CopyFromScreen(
        virtualScreen.Left,
        virtualScreen.Top,
        0,
        0,
        new System.Drawing.Size(virtualScreen.Width, virtualScreen.Height));

      return (BitmapSource?)ConvertToBitmapSource(bitmap);
    });
  }

  private static BitmapSource ConvertToBitmapSource(System.Drawing.Bitmap bitmap)
  {
    const double ScreenCaptureDpi = 96.0;
    var bitmapData = bitmap.LockBits(
      new WinRect(0, 0, bitmap.Width, bitmap.Height),
      System.Drawing.Imaging.ImageLockMode.ReadOnly,
      bitmap.PixelFormat);

    try
    {
      var bitmapSource = BitmapSource.Create(
        bitmapData.Width,
        bitmapData.Height,
        ScreenCaptureDpi,
        ScreenCaptureDpi,
        System.Windows.Media.PixelFormats.Bgra32,
        null,
        bitmapData.Scan0,
        bitmapData.Stride * bitmapData.Height,
        bitmapData.Stride);

      bitmapSource.Freeze();
      return bitmapSource;
    }
    finally
    {
      bitmap.UnlockBits(bitmapData);
    }
  }
}
