using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

using ScreenTranslator.Models;
using ScreenTranslator.Windows;

using WinRect = System.Drawing.Rectangle;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace ScreenTranslator.Services;

public sealed class GifRecordingSessionCoordinator : IDisposable, IScreenshotRegionSession
{
  private readonly AppSettings _settings;
  private readonly WinRect _captureRegion;
  private readonly double _dpiScaleX;
  private readonly double _dpiScaleY;
  private readonly IGifRecordingRunner _recordingRunner;
  private readonly IGifEncodingRunner _encodingRunner;
  private readonly ISelectionFrameView _selectionFrameWindow;
  private readonly IGifRecordingControlView _controlWindow;
  private readonly IGifSaveDialogService _saveDialogService;
  private readonly IGifMessageService _messageService;
  private readonly IGifFileWriter _fileWriter;
  private readonly IUiDispatcher _uiDispatcher;

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
    : this(
        settings,
        captureRegion,
        dpiScaleX,
        dpiScaleY,
        new GifRecordingRunnerAdapter(recordingService ?? new GifRecordingService()),
        new GifEncodingRunnerAdapter(encodingService ?? new GifEncodingService()),
        new SelectionFrameWindowAdapter(new SelectionFrameWindow()),
        new GifRecordingControlWindowAdapter(new GifRecordingControlWindow()),
        new WpfGifSaveDialogService(),
        new WpfGifMessageService(),
        new GifFileWriter(),
        new WpfUiDispatcher())
  {
  }

  internal GifRecordingSessionCoordinator(
    AppSettings settings,
    WinRect captureRegion,
    double dpiScaleX,
    double dpiScaleY,
    IGifRecordingRunner recordingRunner,
    IGifEncodingRunner encodingRunner,
    ISelectionFrameView selectionFrameWindow,
    IGifRecordingControlView controlWindow,
    IGifSaveDialogService saveDialogService,
    IGifMessageService messageService,
    IGifFileWriter fileWriter,
    IUiDispatcher uiDispatcher)
  {
    _settings = settings;
    _captureRegion = captureRegion;
    _dpiScaleX = dpiScaleX;
    _dpiScaleY = dpiScaleY;
    _recordingRunner = recordingRunner;
    _encodingRunner = encodingRunner;
    _selectionFrameWindow = selectionFrameWindow;
    _controlWindow = controlWindow;
    _saveDialogService = saveDialogService;
    _messageService = messageService;
    _fileWriter = fileWriter;
    _uiDispatcher = uiDispatcher;

    _selectionFrameWindow.Initialize(captureRegion, dpiScaleX, dpiScaleY);

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

    ConfigureRunnerCaptureHooks(HideOverlaysForCapture, ShowOverlaysAfterCapture);
    _recordingRunner.ProgressChanged += OnProgressChanged;

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

    _recordingRunner.ProgressChanged -= OnProgressChanged;
    _recordingRunner.BeforeCapture = null;
    _recordingRunner.AfterCapture = null;

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

    _selectionFrameWindow.Close();
    _controlWindow.Close();

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

  private void ConfigureRunnerCaptureHooks(Action beforeCapture, Action afterCapture)
  {
    _recordingRunner.BeforeCapture = WrapCaptureHook(beforeCapture, callback => _uiDispatcher.Invoke(callback));
    _recordingRunner.AfterCapture = WrapCaptureHook(afterCapture, callback => _uiDispatcher.Invoke(callback));
  }

  private void OnStopRequested()
  {
    _controlWindow.SetStoppingHint();
    _recordingRunner.RequestStop();
  }

  private void OnCancelRequested()
  {
    _wasCanceled = true;
    Dispose();
  }

  private void OnProgressChanged(GifRecordingProgress progress)
  {
    _uiDispatcher.BeginInvoke(() =>
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
      result = await _recordingRunner.RecordAsync(_captureRegion, cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      _wasCanceled = true;
    }

    _uiDispatcher.BeginInvoke(() =>
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

      _ = CompleteRecordingAsync(result);
    });
  }

  private async Task CompleteRecordingAsync(GifCaptureResult result)
  {
    try
    {
      if (!ShouldEncodeResult(result, _wasCanceled))
      {
        if (!_wasCanceled && !string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
          _messageService.ShowWarning(
            result.ErrorMessage,
            LocalizationService.GetString("GifRecording_Title", "GIF Recording"));
        }

        return;
      }

      var savePath = _saveDialogService.ShowSaveDialog(
        GetSafeSaveDirectory(),
        BuildDefaultFileName(DateTime.Now),
        LocalizationService.GetString("FileDialog_GifFilter", "GIF Image|*.gif"));

      if (string.IsNullOrWhiteSpace(savePath))
      {
        return;
      }

      var bytes = await Task.Run(
        () => _encodingRunner.Encode(result.Frames, GifRecordingDefaults.FrameIntervalMs))
        .ConfigureAwait(false);

      try
      {
        await _fileWriter.WriteAllBytesAsync(savePath, bytes, CancellationToken.None).ConfigureAwait(false);
      }
      catch
      {
        _uiDispatcher.BeginInvoke(() =>
        {
          _messageService.ShowWarning(
            LocalizationService.GetString("GifRecording_Error_SaveFailed", "Failed to save GIF."),
            LocalizationService.GetString("GifRecording_Title", "GIF Recording"));
        });
      }
    }
    finally
    {
      _uiDispatcher.BeginInvoke(Dispose);
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

  internal interface IGifRecordingRunner
  {
    Action? BeforeCapture { get; set; }
    Action? AfterCapture { get; set; }
    event Action<GifRecordingProgress>? ProgressChanged;
    void RequestStop();
    Task<GifCaptureResult> RecordAsync(WinRect region, CancellationToken cancellationToken);
  }

  internal interface IGifEncodingRunner
  {
    byte[] Encode(IReadOnlyList<BitmapSource> frames, int frameIntervalMs);
  }

  internal interface ISelectionFrameView
  {
    bool IsVisible { get; }
    void Initialize(WinRect captureRegion, double dpiScaleX, double dpiScaleY);
    void SetLocked(bool locked);
    void Show();
    void Hide();
    void Close();
  }

  internal interface IGifRecordingControlView
  {
    event Action? StopRequested;
    event Action? CancelRequested;
    bool IsVisible { get; }
    bool PositionNearSelection(WinRect captureRegion, double dpiScaleX, double dpiScaleY);
    void UpdateProgress(TimeSpan elapsed);
    void SetStoppingHint();
    void Show();
    void Hide();
    void Close();
    void FocusWindow();
  }

  internal interface IGifSaveDialogService
  {
    string? ShowSaveDialog(string initialDirectory, string defaultFileName, string filter);
  }

  internal interface IGifMessageService
  {
    void ShowWarning(string message, string title);
  }

  internal interface IGifFileWriter
  {
    Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken);
  }

  internal interface IUiDispatcher
  {
    void Invoke(Action callback);
    void BeginInvoke(Action callback);
  }

  private sealed class GifRecordingRunnerAdapter : IGifRecordingRunner
  {
    private readonly GifRecordingService _service;

    public GifRecordingRunnerAdapter(GifRecordingService service)
    {
      _service = service;
    }

    public Action? BeforeCapture
    {
      get => _service.BeforeCapture;
      set => _service.BeforeCapture = value;
    }

    public Action? AfterCapture
    {
      get => _service.AfterCapture;
      set => _service.AfterCapture = value;
    }

    public event Action<GifRecordingProgress>? ProgressChanged
    {
      add => _service.ProgressChanged += value;
      remove => _service.ProgressChanged -= value;
    }

    public void RequestStop()
    {
      _service.RequestStop();
    }

    public Task<GifCaptureResult> RecordAsync(WinRect region, CancellationToken cancellationToken)
    {
      return _service.RecordAsync(region, cancellationToken);
    }
  }

  private sealed class GifEncodingRunnerAdapter : IGifEncodingRunner
  {
    private readonly GifEncodingService _service;

    public GifEncodingRunnerAdapter(GifEncodingService service)
    {
      _service = service;
    }

    public byte[] Encode(IReadOnlyList<BitmapSource> frames, int frameIntervalMs)
    {
      return _service.Encode(frames, frameIntervalMs);
    }
  }

  private sealed class SelectionFrameWindowAdapter : ISelectionFrameView
  {
    private readonly SelectionFrameWindow _window;

    public SelectionFrameWindowAdapter(SelectionFrameWindow window)
    {
      _window = window;
    }

    public bool IsVisible => _window.IsVisible;

    public void Initialize(WinRect captureRegion, double dpiScaleX, double dpiScaleY)
    {
      _window.Initialize(captureRegion, dpiScaleX, dpiScaleY);
    }

    public void SetLocked(bool locked)
    {
      _window.SetLocked(locked);
    }

    public void Show()
    {
      _window.Show();
    }

    public void Hide()
    {
      _window.Hide();
    }

    public void Close()
    {
      SafeClose(_window);
    }
  }

  private sealed class GifRecordingControlWindowAdapter : IGifRecordingControlView
  {
    private readonly GifRecordingControlWindow _window;

    public GifRecordingControlWindowAdapter(GifRecordingControlWindow window)
    {
      _window = window;
    }

    public event Action? StopRequested
    {
      add => _window.StopRequested += value;
      remove => _window.StopRequested -= value;
    }

    public event Action? CancelRequested
    {
      add => _window.CancelRequested += value;
      remove => _window.CancelRequested -= value;
    }

    public bool IsVisible => _window.IsVisible;

    public bool PositionNearSelection(WinRect captureRegion, double dpiScaleX, double dpiScaleY)
    {
      return _window.PositionNearSelection(captureRegion, dpiScaleX, dpiScaleY);
    }

    public void UpdateProgress(TimeSpan elapsed)
    {
      _window.UpdateProgress(elapsed);
    }

    public void SetStoppingHint()
    {
      _window.SetStoppingHint();
    }

    public void Show()
    {
      _window.Show();
    }

    public void Hide()
    {
      _window.Hide();
    }

    public void Close()
    {
      SafeClose(_window);
    }

    public void FocusWindow()
    {
      _window.FocusWindow();
    }
  }

  private sealed class WpfGifSaveDialogService : IGifSaveDialogService
  {
    public string? ShowSaveDialog(string initialDirectory, string defaultFileName, string filter)
    {
      var dialog = new WpfSaveFileDialog
      {
        Filter = filter,
        DefaultExt = ".gif",
        FileName = defaultFileName,
        InitialDirectory = initialDirectory,
        AddExtension = true,
        OverwritePrompt = true,
      };

      return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
  }

  private sealed class WpfGifMessageService : IGifMessageService
  {
    public void ShowWarning(string message, string title)
    {
      WpfMessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private sealed class GifFileWriter : IGifFileWriter
  {
    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
      return File.WriteAllBytesAsync(path, bytes, cancellationToken);
    }
  }

  private sealed class WpfUiDispatcher : IUiDispatcher
  {
    public void Invoke(Action callback)
    {
      var dispatcher = WpfApplication.Current?.Dispatcher;
      if (dispatcher is null)
      {
        callback();
        return;
      }

      dispatcher.Invoke(callback);
    }

    public void BeginInvoke(Action callback)
    {
      var dispatcher = WpfApplication.Current?.Dispatcher;
      if (dispatcher is null)
      {
        callback();
        return;
      }

      _ = dispatcher.BeginInvoke(callback);
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
