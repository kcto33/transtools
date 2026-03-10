using System.Windows.Media.Imaging;

using ScreenTranslator.Models;

using WinRect = System.Drawing.Rectangle;
using WpfApplication = System.Windows.Application;

namespace ScreenTranslator.Services;

/// <summary>
/// Manages screenshot flow and pin windows.
/// </summary>
public sealed class ScreenshotController
{
  private readonly AppSettings _settings;
  private readonly List<Windows.PinWindow> _pinWindows = [];
  private Windows.ScreenshotOverlayWindow? _overlayWindow;
  private Windows.FreeformScreenshotWindow? _freeformWindow;
  private LongScreenshotSessionCoordinator? _longScreenshotSession;

  public ScreenshotController(AppSettings settings)
  {
    _settings = settings;
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

    _overlayWindow = new Windows.ScreenshotOverlayWindow(_settings, OnPinRequested, StartFreeformScreenshot, StartLongScreenshot);
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

    // Only allow one window at a time
    if (_freeformWindow != null)
    {
      _freeformWindow.Focus();
      return;
    }

    _overlayWindow?.Close();

    _freeformWindow = new Windows.FreeformScreenshotWindow(_settings, OnPinRequested);
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

    _overlayWindow?.Close();

    _longScreenshotSession = new LongScreenshotSessionCoordinator(
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

    foreach (var window in _pinWindows.ToList())
    {
      window.Close();
    }

    _pinWindows.Clear();
  }
}
