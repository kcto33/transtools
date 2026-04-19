using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

using ScreenTranslator.Models;
using ScreenTranslator.Windows;

using WinRect = System.Drawing.Rectangle;
using WpfApplication = System.Windows.Application;
using WpfClipboard = System.Windows.Clipboard;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace ScreenTranslator.Services;

public sealed class LongScreenshotSessionCoordinator : IDisposable
{
  private readonly AppSettings _settings;
  private readonly Action<BitmapSource, WinRect, double, double> _onPinRequested;
  private readonly double _dpiScaleX;
  private readonly double _dpiScaleY;

  private readonly LongScreenshotService _serviceFactory = new();
  private readonly LongScreenshotInputHookService _inputHooks = new();

  private readonly SelectionFrameWindow _selectionFrameWindow;
  private readonly LongScreenshotControlWindow _controlWindow;
  private readonly LongScreenshotPreviewWindow _previewWindow;

  private LongScreenshotSession? _session;
  private BitmapSource? _resultImage;
  private WinRect _captureRegion;
  private bool _previewVisible = true;
  private bool _autoScrollEnabled;
  private bool _hideSelectionFrameForCapture = false;
  private bool _selectionFrameHiddenByCaptureHook;
  private bool _disposed;

  public event Action? Closed;

  public LongScreenshotSessionCoordinator(
    AppSettings settings,
    WinRect captureRegion,
    double dpiScaleX,
    double dpiScaleY,
    Action<BitmapSource, WinRect, double, double> onPinRequested)
  {
    _settings = settings;
    _captureRegion = captureRegion;
    _dpiScaleX = dpiScaleX;
    _dpiScaleY = dpiScaleY;
    _onPinRequested = onPinRequested;

    _selectionFrameWindow = new SelectionFrameWindow();
    _selectionFrameWindow.Initialize(captureRegion, dpiScaleX, dpiScaleY);
    _selectionFrameWindow.RegionChanged += OnSelectionRegionChanged;

    _controlWindow = new LongScreenshotControlWindow();
    _controlWindow.PauseResumeRequested += OnPauseResumeRequested;
    _controlWindow.StopRequested += OnStopRequested;
    _controlWindow.CancelRequested += OnCancelRequested;
    _controlWindow.TogglePreviewRequested += OnTogglePreviewRequested;
    _controlWindow.AutoScrollRequested += OnAutoScrollRequested;
    _controlWindow.SkipRequested += OnSkipRequested;
    _controlWindow.CopyRequested += OnCopyRequested;
    _controlWindow.SaveRequested += OnSaveRequested;
    _controlWindow.PinRequested += OnPinRequestedRequested;
    _controlWindow.CloseRequested += OnCloseRequested;
    _controlWindow.Closed += (_, _) => Dispose();

    _previewWindow = new LongScreenshotPreviewWindow();
    _previewWindow.StitchedPreviewClicked += OnStitchedPreviewClicked;

    _inputHooks.ScrollAttempted += OnScrollAttempted;
    _inputHooks.EscapePressed += OnEscapePressed;
  }

  public bool IsClosed => _disposed;

  public void Start()
  {
    if (_disposed)
    {
      return;
    }

    _session = _serviceFactory.CreateSession(_captureRegion, _settings.LongScreenshot ?? new LongScreenshotSettings());
    ConfigureCaptureHooks(_session, HideOverlaysForCapture, ShowOverlaysAfterCapture);
    _session.ProgressChanged += OnProgressChanged;
    _session.Completed += OnSessionCompleted;

    _selectionFrameWindow.SetLocked(true);
    _selectionFrameWindow.Show();
    _hideSelectionFrameForCapture = true;

    _controlWindow.PositionNearSelection(_captureRegion, _dpiScaleX, _dpiScaleY);
    _controlWindow.Show();

    _previewVisible = TryShowPreviewWindow();

    _controlWindow.UpdateRunState(LongScreenshotRunState.Running, null, _captureRegion);
    _controlWindow.SetPreviewVisible(_previewVisible);

    _inputHooks.Install();
    _session.Start();
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

    _autoScrollEnabled = false;
    _inputHooks.ScrollAttempted -= OnScrollAttempted;
    _inputHooks.EscapePressed -= OnEscapePressed;
    _inputHooks.Dispose();

    if (_session is not null)
    {
      _session.ProgressChanged -= OnProgressChanged;
      _session.Completed -= OnSessionCompleted;
      _session.Cancel();
      _session.Dispose();
      _session = null;
    }

    _selectionFrameWindow.RegionChanged -= OnSelectionRegionChanged;
    SafeClose(_selectionFrameWindow);
    SafeClose(_previewWindow);
    SafeClose(_controlWindow);

    Closed?.Invoke();
  }

  private void OnSelectionRegionChanged(WinRect region)
  {
    _captureRegion = region;
    _session?.UpdateRegion(region);
    _controlWindow.PositionNearSelection(region, _dpiScaleX, _dpiScaleY);

    if (_previewVisible && !_previewWindow.PositionNearSelection(region, _dpiScaleX, _dpiScaleY))
    {
      _previewVisible = false;
      _previewWindow.Hide();
      _controlWindow.SetPreviewVisible(false);
    }
  }

  private void OnPauseResumeRequested()
  {
    if (_session is null)
    {
      return;
    }

    if (_session.RunState == LongScreenshotRunState.Running)
    {
      _autoScrollEnabled = false;
      _session.SetAutoScroll(false);
      _controlWindow.SetAutoScrollState(false);
      _session.Pause();
      _selectionFrameWindow.SetLocked(false);
      _selectionFrameWindow.Show();
      return;
    }

    if (_session.RunState == LongScreenshotRunState.Paused)
    {
      _selectionFrameWindow.SetLocked(true);
      _selectionFrameWindow.Show();
      _session.Resume();
      return;
    }

    if (_session.RunState == LongScreenshotRunState.PendingFix)
    {
      _session.SkipPendingMismatch();
    }
  }

  private void OnStopRequested()
  {
    _autoScrollEnabled = false;
    _controlWindow.SetAutoScrollState(false);
    _session?.Stop();
  }

  private void OnCancelRequested()
  {
    _autoScrollEnabled = false;
    _controlWindow.SetAutoScrollState(false);
    _session?.Cancel();
  }

  private void OnTogglePreviewRequested()
  {
    if (_previewVisible)
    {
      _previewVisible = false;
      _previewWindow.Hide();
    }
    else
    {
      _previewVisible = TryShowPreviewWindow();
    }

    _controlWindow.SetPreviewVisible(_previewVisible);
  }

  private void OnAutoScrollRequested()
  {
    if (_session is null || _session.RunState != LongScreenshotRunState.Running)
    {
      return;
    }

    _autoScrollEnabled = !_autoScrollEnabled;
    _session.SetAutoScroll(_autoScrollEnabled);
    _controlWindow.SetAutoScrollState(_autoScrollEnabled);
  }

  private void OnSkipRequested()
  {
    _session?.SkipPendingMismatch();
  }

  private void OnCopyRequested()
  {
    if (_resultImage is null)
    {
      return;
    }

    try
    {
      WpfClipboard.SetImage(_resultImage);
      _controlWindow.SetHint(LocalizationService.GetString("LongScreenshot_Hint_Copied", "Copied to clipboard."));
    }
    catch
    {
      _controlWindow.SetHint(LocalizationService.GetString("LongScreenshot_Hint_CopyFailed", "Copy failed."));
    }
  }

  private void OnSaveRequested()
  {
    if (_resultImage is null)
    {
      return;
    }

    try
    {
      var dialog = new WpfSaveFileDialog
      {
        Filter = LocalizationService.GetString("FileDialog_ImageFilter", "PNG Image|*.png|JPEG Image|*.jpg|BMP Image|*.bmp"),
        DefaultExt = ".png",
        FileName = string.Format(CultureInfo.InvariantCulture, "LongScreenshot_{0:yyyyMMdd_HHmmss}", DateTime.Now),
        InitialDirectory = GetSafeSaveDirectory(),
      };

      if (dialog.ShowDialog() != true)
      {
        return;
      }

      SaveBitmapToPath(_resultImage, dialog.FileName);
      _controlWindow.SetHint(string.Format(
        LocalizationService.GetString("LongScreenshot_Hint_SavedTo", "Saved to: {0}"),
        dialog.FileName));
    }
    catch
    {
      _controlWindow.SetHint(LocalizationService.GetString("LongScreenshot_Hint_SaveFailed", "Save failed."));
    }
  }

  private void OnPinRequestedRequested()
  {
    if (_resultImage is null)
    {
      return;
    }

    var pinRegion = new WinRect(_captureRegion.X, _captureRegion.Y, _resultImage.PixelWidth, _resultImage.PixelHeight);
    _onPinRequested(_resultImage, pinRegion, _dpiScaleX, _dpiScaleY);
    _controlWindow.SetHint(LocalizationService.GetString("LongScreenshot_Hint_Pinned", "Pinned to screen."));
  }

  private void OnCloseRequested()
  {
    Dispose();
  }

  private void OnStitchedPreviewClicked(double ratioY)
  {
    _session?.TryResolvePendingMismatch(ratioY);
  }

  private void OnScrollAttempted()
  {
    if (_session is null)
    {
      return;
    }

    if (_session.RunState == LongScreenshotRunState.Running)
    {
      _session.NotifyScrollAttempt();
    }
  }

  private void OnEscapePressed()
  {
    WpfApplication.Current.Dispatcher.BeginInvoke(() =>
    {
      if (_disposed)
      {
        return;
      }

      Dispose();
    });
  }

  private void OnProgressChanged(LongScreenshotProgress progress)
  {
    // Use BeginInvoke (async) instead of Invoke (sync) to avoid deadlock:
    // the session raises ProgressChanged while holding _syncRoot, and
    // Dispatcher.Invoke would block the background thread waiting for
    // the UI thread — which may itself be blocked trying to acquire
    // _syncRoot from a button-click handler (Pause/Stop/etc.).
    WpfApplication.Current.Dispatcher.BeginInvoke(() =>
    {
      if (_disposed)
      {
        return;
      }

      _controlWindow.UpdateRunState(progress.RunState, progress, _captureRegion);
      _previewWindow.UpdateProgress(progress);

      if (progress.RunState == LongScreenshotRunState.Running)
      {
        _selectionFrameWindow.SetLocked(true);
        _selectionFrameWindow.Show();
      }
      else if (progress.RunState == LongScreenshotRunState.Paused)
      {
        _selectionFrameWindow.SetLocked(false);
        _selectionFrameWindow.Show();
      }
      else if (progress.RunState == LongScreenshotRunState.PendingFix)
      {
        _selectionFrameWindow.SetLocked(true);
        _selectionFrameWindow.Show();
      }
    });
  }

  private void OnSessionCompleted(LongScreenshotResult result)
  {
    // Use BeginInvoke to avoid blocking the background capture thread,
    // which could deadlock if the UI thread is waiting on the session.
    WpfApplication.Current.Dispatcher.BeginInvoke(() =>
    {
      if (_disposed)
      {
        return;
      }

      _inputHooks.Uninstall();
      _selectionFrameWindow.Hide();

      if (result.StopReason == LongScreenshotStopReason.Canceled)
      {
        Dispose();
        return;
      }

      _resultImage = result.Image;
      if (_resultImage is not null && ShouldAutoCopyResult(_settings))
      {
        AutoCopyResult(_resultImage);
      }

      var autoSavedPath = _resultImage is null ? null : AutoSaveResult(_resultImage);
      _controlWindow.ShowResultState(result, autoSavedPath, _resultImage is not null);

      if (_resultImage is not null)
      {
        _previewWindow.ShowResult(_resultImage, result.Markers);
      }
    });
  }

  private static void SafeClose(Window? window)
  {
    if (window is null)
    {
      return;
    }

    try
    {
      window.Close();
    }
    catch
    {
      // ignore
    }
  }

  private void AutoCopyResult(BitmapSource image)
  {
    try
    {
      WpfClipboard.SetImage(image);
    }
    catch
    {
      // keep best effort
    }
  }

  internal static void ConfigureCaptureHooks(LongScreenshotSession session, Action beforeCapture, Action afterCapture)
  {
    session.BeforeCapture = WrapCaptureHook(beforeCapture, callback => InvokeOnUiThreadOrInline(callback));
    session.AfterCapture = WrapCaptureHook(afterCapture, callback => InvokeOnUiThreadOrInline(callback));
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

  internal static bool ShouldAutoCopyResult(AppSettings settings)
  {
    return settings.ScreenshotAutoCopy;
  }

  private string? AutoSaveResult(BitmapSource image)
  {
    if (!_settings.ScreenshotAutoSave)
    {
      return null;
    }

    try
    {
      var baseDir = GetSafeSaveDirectory();
      var fileName = BuildSafeFileName(_settings.ScreenshotFileNameFormat);
      var fullPath = GetUniquePath(Path.Combine(baseDir, fileName));
      SaveBitmapToPath(image, fullPath);
      return fullPath;
    }
    catch
    {
      return null;
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
        // fallback below
      }
    }

    return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
  }

  private static string BuildSafeFileName(string? format)
  {
    var raw = string.IsNullOrWhiteSpace(format)
      ? "LongScreenshot_{0:yyyyMMdd_HHmmss}"
      : format;

    string candidate;
    try
    {
      candidate = string.Format(CultureInfo.InvariantCulture, raw, DateTime.Now);
    }
    catch
    {
      candidate = string.Format(CultureInfo.InvariantCulture, "LongScreenshot_{0:yyyyMMdd_HHmmss}", DateTime.Now);
    }

    var invalidChars = Path.GetInvalidFileNameChars();
    var safe = new string(candidate.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
    if (string.IsNullOrWhiteSpace(safe))
    {
      safe = string.Format(CultureInfo.InvariantCulture, "LongScreenshot_{0:yyyyMMdd_HHmmss}", DateTime.Now);
    }

    if (Path.GetExtension(safe).Length == 0)
    {
      safe += ".png";
    }

    return safe;
  }

  private static string GetUniquePath(string path)
  {
    if (!File.Exists(path))
    {
      return path;
    }

    var directory = Path.GetDirectoryName(path) ?? string.Empty;
    var name = Path.GetFileNameWithoutExtension(path);
    var ext = Path.GetExtension(path);

    for (var i = 1; i < 1000; i++)
    {
      var candidate = Path.Combine(directory, $"{name}_{i}{ext}");
      if (!File.Exists(candidate))
      {
        return candidate;
      }
    }

    return Path.Combine(directory, $"{name}_{Guid.NewGuid():N}{ext}");
  }

  private static void SaveBitmapToPath(BitmapSource image, string path)
  {
    var ext = Path.GetExtension(path).ToLowerInvariant();
    BitmapEncoder encoder = ext switch
    {
      ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
      ".bmp" => new BmpBitmapEncoder(),
      _ => new PngBitmapEncoder(),
    };

    encoder.Frames.Add(BitmapFrame.Create(image));
    using var stream = File.Create(path);
    encoder.Save(stream);
  }

  private bool TryShowPreviewWindow()
  {
    if (_previewWindow.PositionNearSelection(_captureRegion, _dpiScaleX, _dpiScaleY))
    {
      _previewWindow.Show();
      return true;
    }

    _previewWindow.Hide();
    return false;
  }

  /// <summary>
  /// Hide overlay windows that would be captured as white rectangles
  /// by CopyFromScreen / BitBlt.  Called on the UI thread from the
  /// capture-loop's <see cref="LongScreenshotSession.BeforeCapture"/>.
  /// </summary>
  private void HideOverlaysForCapture()
  {
    if (_disposed)
    {
      return;
    }

    _selectionFrameHiddenByCaptureHook = false;
    if (_hideSelectionFrameForCapture && _selectionFrameWindow.IsVisible)
    {
      _selectionFrameWindow.Hide();
      _selectionFrameHiddenByCaptureHook = true;
    }
  }

  /// <summary>
  /// Restore overlays after the screen frame has been captured.
  /// </summary>
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
  }
}
