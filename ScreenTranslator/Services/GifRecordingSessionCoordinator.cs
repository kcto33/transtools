using System.Globalization;
using System.IO;
using System.Windows;

using Microsoft.Win32;

using ScreenTranslator.Models;
using ScreenTranslator.Windows;

using WinRect = System.Drawing.Rectangle;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
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

  private CancellationTokenSource? _cancellationTokenSource;
  private bool _disposed;
  private bool _wasCanceled;
  private bool _selectionFrameWasVisibleBeforeCapture;
  private bool _controlWindowWasVisibleBeforeCapture;

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

    _cancellationTokenSource = new CancellationTokenSource();

    ConfigureCaptureHooks(_recordingService, HideOverlaysForCapture, ShowOverlaysAfterCapture);
    _recordingService.ProgressChanged += OnProgressChanged;

    _selectionFrameWindow.SetLocked(true);
    _selectionFrameWindow.Show();

    _controlWindow.PositionNearSelection(_captureRegion, _dpiScaleX, _dpiScaleY);
    _controlWindow.UpdateProgress(TimeSpan.Zero);
    _controlWindow.Show();

    _ = RunAsync(_cancellationTokenSource.Token);
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
    _recordingService.BeforeCapture = null;
    _recordingService.AfterCapture = null;

    _controlWindow.StopRequested -= OnStopRequested;
    _controlWindow.CancelRequested -= OnCancelRequested;

    if (_cancellationTokenSource is not null)
    {
      try
      {
        _cancellationTokenSource.Cancel();
      }
      catch
      {
        // ignore
      }

      _cancellationTokenSource.Dispose();
      _cancellationTokenSource = null;
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
    WpfApplication.Current?.Dispatcher.BeginInvoke(() =>
    {
      if (_disposed)
      {
        return;
      }

      _controlWindow.UpdateProgress(progress.Elapsed);
    });
  }

  private async Task RunAsync(CancellationToken cancellationToken)
  {
    GifCaptureResult? result = null;

    try
    {
      result = await _recordingService.RecordAsync(_captureRegion, cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      _wasCanceled = true;
    }

    var dispatcher = WpfApplication.Current?.Dispatcher;
    if (dispatcher is null)
    {
      if (result is not null)
      {
        CompleteRecording(result);
      }
      else
      {
        Dispose();
      }

      return;
    }

    _ = dispatcher.BeginInvoke(() =>
    {
      if (_disposed)
      {
        return;
      }

      if (result is null)
      {
        Dispose();
        return;
      }

      CompleteRecording(result);
    });
  }

  private void CompleteRecording(GifCaptureResult result)
  {
    try
    {
      if (!ShouldEncodeResult(result, _wasCanceled))
      {
        if (!_wasCanceled && !string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
          WpfMessageBox.Show(
            result.ErrorMessage,
            LocalizationService.GetString("GifRecording_Title", "GIF Recording"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        }

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

      if (dialog.ShowDialog() != true)
      {
        return;
      }

      try
      {
        File.WriteAllBytes(dialog.FileName, bytes);
      }
      catch
      {
        WpfMessageBox.Show(
          LocalizationService.GetString("GifRecording_Error_SaveFailed", "Failed to save GIF."),
          LocalizationService.GetString("GifRecording_Title", "GIF Recording"),
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
      }
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
        // fallback below
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

    _selectionFrameWasVisibleBeforeCapture = _selectionFrameWindow.IsVisible;
    if (_selectionFrameWasVisibleBeforeCapture)
    {
      _selectionFrameWindow.Hide();
    }

    _controlWindowWasVisibleBeforeCapture = _controlWindow.IsVisible;
    if (_controlWindowWasVisibleBeforeCapture)
    {
      _controlWindow.Hide();
    }
  }

  private void ShowOverlaysAfterCapture()
  {
    if (_disposed)
    {
      return;
    }

    if (_selectionFrameWasVisibleBeforeCapture)
    {
      _selectionFrameWindow.Show();
      _selectionFrameWasVisibleBeforeCapture = false;
    }

    if (_controlWindowWasVisibleBeforeCapture)
    {
      _controlWindow.Show();
      _controlWindowWasVisibleBeforeCapture = false;
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
