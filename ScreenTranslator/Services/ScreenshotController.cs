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
  private readonly Func<AppSettings, Action<BitmapSource, WinRect, double, double>, Action?, Action<WinRect, double, double>?, Action<WinRect, double, double>?, IScreenshotOverlaySessionWindow> _overlayFactory;
  private readonly Func<AppSettings, Action<BitmapSource, WinRect, double, double>, IScreenshotFreeformWindow> _freeformFactory;
  private readonly Func<AppSettings, WinRect, double, double, Action<BitmapSource, WinRect, double, double>, IScreenshotRegionSession> _longScreenshotFactory;
  private readonly Func<AppSettings, WinRect, double, double, Action<BitmapSource, WinRect, double, double>, IScreenshotRegionSession> _gifRecordingFactory;
  private IScreenshotOverlaySessionWindow? _overlayWindow;
  private IScreenshotFreeformWindow? _freeformWindow;
  private IScreenshotRegionSession? _longScreenshotSession;
  private IScreenshotRegionSession? _gifRecordingSession;

  public ScreenshotController(AppSettings settings)
    : this(
        settings,
        (appSettings, onPinRequested, onFreeformRequested, onLongScreenshotRequested, onGifRecordingRequested) =>
          new Windows.ScreenshotOverlayWindow(
            appSettings,
            onPinRequested,
            onFreeformRequested,
            onLongScreenshotRequested,
            onGifRecordingRequested),
        (appSettings, onPinRequested) => new Windows.FreeformScreenshotWindow(appSettings, onPinRequested),
        (appSettings, region, dpiScaleX, dpiScaleY, onPinRequested) =>
          new LongScreenshotSessionCoordinator(appSettings, region, dpiScaleX, dpiScaleY, onPinRequested),
        (appSettings, region, dpiScaleX, dpiScaleY, _) =>
          new GifRecordingSessionCoordinator(appSettings, region, dpiScaleX, dpiScaleY))
  {
  }

  internal ScreenshotController(
    AppSettings settings,
    Func<AppSettings, Action<BitmapSource, WinRect, double, double>, Action?, Action<WinRect, double, double>?, Action<WinRect, double, double>?, IScreenshotOverlaySessionWindow> overlayFactory,
    Func<AppSettings, Action<BitmapSource, WinRect, double, double>, IScreenshotFreeformWindow> freeformFactory,
    Func<AppSettings, WinRect, double, double, Action<BitmapSource, WinRect, double, double>, IScreenshotRegionSession> longScreenshotFactory,
    Func<AppSettings, WinRect, double, double, Action<BitmapSource, WinRect, double, double>, IScreenshotRegionSession> gifRecordingFactory)
  {
    _settings = settings;
    _overlayFactory = overlayFactory;
    _freeformFactory = freeformFactory;
    _longScreenshotFactory = longScreenshotFactory;
    _gifRecordingFactory = gifRecordingFactory;
  }

  /// <summary>
  /// Start the screenshot selection process.
  /// </summary>
  public void StartScreenshot()
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

    // Only allow one overlay at a time
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

    _overlayWindow = _overlayFactory(_settings, OnPinRequested, StartFreeformScreenshot, StartLongScreenshot, StartGifRecording);
    _overlayWindow.Closed += (_, _) => _overlayWindow = null;
    _overlayWindow.Show();
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

    // Only allow one window at a time
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
}
