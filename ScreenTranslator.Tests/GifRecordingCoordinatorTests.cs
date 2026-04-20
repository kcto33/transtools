using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using ScreenTranslator.Models;
using ScreenTranslator.Services;
using ScreenTranslator.Windows;

using Xunit;

using WinRect = System.Drawing.Rectangle;

namespace ScreenTranslator.Tests;

public sealed class GifRecordingCoordinatorTests
{
  [Fact]
  public void ConfigureCaptureHooks_AssignsBeforeAndAfterCaptureCallbacks()
  {
    var service = new GifRecordingService();
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
  public void ShouldEncodeResult_ReturnsFalse_WhenCaptureWasCanceled()
  {
    var result = new GifCaptureResult([], 0, false, null);

    var shouldEncode = GifRecordingSessionCoordinator.ShouldEncodeResult(result, wasCanceled: true);

    Assert.False(shouldEncode);
  }

  [Fact]
  public void ShouldEncodeResult_ReturnsTrue_WhenFramesExist_And_NoError()
  {
    var result = new GifCaptureResult([CreateFrame()], 1, false, null);

    var shouldEncode = GifRecordingSessionCoordinator.ShouldEncodeResult(result, wasCanceled: false);

    Assert.True(shouldEncode);
  }

  [Fact]
  public void BuildDefaultFileName_ReturnsExpectedFormat()
  {
    var fileName = GifRecordingSessionCoordinator.BuildDefaultFileName(
      new DateTime(2026, 4, 20, 13, 14, 15, DateTimeKind.Local));

    Assert.Equal("GifRecording_20260420_131415.gif", fileName);
  }

  [Fact]
  public void ShouldHandleStartupClick_ReturnsFalse_InsideDebounceWindow()
  {
    var shownAt = DateTime.UtcNow;

    var shouldHandle = GifRecordingControlWindow.ShouldHandleStartupClick(
      shownAtUtc: shownAt,
      nowUtc: shownAt.AddMilliseconds(120));

    Assert.False(shouldHandle);
  }

  [Fact]
  public void Start_ShowsWindows_InitializesProgress_And_ConfiguresCaptureHooks()
  {
    var fixture = new CoordinatorFixture();

    fixture.Coordinator.Start();

    Assert.True(fixture.SelectionFrame.InitializeCalled);
    Assert.Contains(true, fixture.SelectionFrame.LockedStates);
    Assert.True(fixture.SelectionFrame.IsVisible);
    Assert.True(fixture.ControlWindow.IsVisible);
    Assert.Equal(TimeSpan.Zero, fixture.ControlWindow.ProgressUpdates.Single());
    Assert.Equal(1, fixture.ControlWindow.PositionCalls);
    Assert.NotNull(fixture.RecordingRunner.BeforeCapture);
    Assert.NotNull(fixture.RecordingRunner.AfterCapture);
  }

  [Fact]
  public void StopRequest_SetsStoppingHint_And_RequestsStop()
  {
    var fixture = new CoordinatorFixture();
    fixture.Coordinator.Start();

    fixture.ControlWindow.RaiseStopRequested();

    Assert.Equal(1, fixture.ControlWindow.StoppingHintCount);
    Assert.Equal(1, fixture.RecordingRunner.RequestStopCount);
  }

  [Fact]
  public void CancelRequest_DisposesCoordinator_RaisesClosed_AndCancelsRecording()
  {
    var fixture = new CoordinatorFixture();
    var closedCount = 0;
    fixture.Coordinator.Closed += () => closedCount++;

    fixture.Coordinator.Start();
    fixture.ControlWindow.RaiseCancelRequested();

    Assert.Equal(1, closedCount);
    Assert.True(fixture.SelectionFrame.CloseCalled);
    Assert.True(fixture.ControlWindow.CloseCalled);
    Assert.Null(fixture.RecordingRunner.BeforeCapture);
    Assert.Null(fixture.RecordingRunner.AfterCapture);
    Assert.True(fixture.RecordingRunner.CapturedCancellationToken.IsCancellationRequested);
  }

  [Fact]
  public void EscapePressed_DisposesCoordinator_RaisesClosed_AndCancelsRecording()
  {
    var fixture = new CoordinatorFixture();
    var closedCount = 0;
    fixture.Coordinator.Closed += () => closedCount++;

    fixture.Coordinator.Start();
    fixture.InputHooks.RaiseEscapePressed();

    Assert.Equal(1, closedCount);
    Assert.True(fixture.SelectionFrame.CloseCalled);
    Assert.True(fixture.ControlWindow.CloseCalled);
    Assert.True(fixture.InputHooks.DisposeCalled);
    Assert.True(fixture.RecordingRunner.CapturedCancellationToken.IsCancellationRequested);
  }

  [Fact]
  public void CaptureHooks_KeepSelectionFrameAndControlWindowVisible_DuringCapture()
  {
    var fixture = new CoordinatorFixture();
    fixture.Coordinator.Start();

    fixture.RecordingRunner.BeforeCapture?.Invoke();

    Assert.True(fixture.SelectionFrame.IsVisible);
    Assert.True(fixture.ControlWindow.IsVisible);

    fixture.RecordingRunner.AfterCapture?.Invoke();

    Assert.True(fixture.SelectionFrame.IsVisible);
    Assert.True(fixture.ControlWindow.IsVisible);
  }

  [Fact]
  public async Task Completion_UsesSaveDialogOnCallingThread_AndEncodesAndWritesOffUiThread()
  {
    var fixture = new CoordinatorFixture();
    var testThreadId = Environment.CurrentManagedThreadId;
    var closedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.Coordinator.Closed += () => closedTcs.TrySetResult();

    fixture.SaveDialog.ResultPath = "F:\\temp\\capture.gif";
    fixture.RecordingRunner.ResultTask = Task.FromResult(new GifCaptureResult([CreateFrame()], 1, false, null));

    fixture.Coordinator.Start();

    await closedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal(testThreadId, fixture.SaveDialog.CalledThreadId);
    Assert.NotEqual(testThreadId, fixture.EncodingRunner.CalledThreadId);
    Assert.NotEqual(testThreadId, fixture.FileWriter.CalledThreadId);
    Assert.Equal("F:\\temp\\capture.gif", fixture.FileWriter.Path);
    Assert.Equal(new byte[] { 1, 2, 3, 4 }, fixture.FileWriter.Bytes);
    Assert.True(fixture.SelectionFrame.CloseCalled);
    Assert.True(fixture.ControlWindow.CloseCalled);
  }

  [Fact]
  public async Task Completion_WithError_ShowsWarning_AndSkipsEncoding()
  {
    var fixture = new CoordinatorFixture();
    var closedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.Coordinator.Closed += () => closedTcs.TrySetResult();
    fixture.RecordingRunner.ResultTask = Task.FromResult(new GifCaptureResult([], 1, false, "capture failed"));

    fixture.Coordinator.Start();

    await closedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal("capture failed", fixture.MessageService.Messages.Single());
    Assert.Equal(0, fixture.EncodingRunner.CallCount);
    Assert.Equal(0, fixture.FileWriter.CallCount);
  }

  private static BitmapSource CreateFrame()
  {
    var bitmap = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null);
    var pixels = new byte[2 * 2 * 4];

    for (var index = 0; index < pixels.Length; index += 4)
    {
      pixels[index + 0] = Colors.Lime.B;
      pixels[index + 1] = Colors.Lime.G;
      pixels[index + 2] = Colors.Lime.R;
      pixels[index + 3] = Colors.Lime.A;
    }

    bitmap.WritePixels(new Int32Rect(0, 0, 2, 2), pixels, 2 * 4, 0);
    bitmap.Freeze();
    return bitmap;
  }

  private sealed class CoordinatorFixture
  {
    public CoordinatorFixture()
    {
      RecordingRunner = new FakeRecordingRunner();
      EncodingRunner = new FakeEncodingRunner();
      SelectionFrame = new FakeSelectionFrameView();
      ControlWindow = new FakeControlWindow();
      SaveDialog = new FakeSaveDialogService();
      MessageService = new FakeMessageService();
      FileWriter = new FakeFileWriter();
      UiDispatcher = new InlineUiDispatcher();
      InputHooks = new FakeInputHookService();

      Coordinator = new GifRecordingSessionCoordinator(
        new AppSettings(),
        new WinRect(10, 20, 100, 50),
        1.0,
        1.0,
        RecordingRunner,
        EncodingRunner,
        SelectionFrame,
        ControlWindow,
        SaveDialog,
        MessageService,
        FileWriter,
        UiDispatcher,
        InputHooks);
    }

    public GifRecordingSessionCoordinator Coordinator { get; }
    public FakeRecordingRunner RecordingRunner { get; }
    public FakeEncodingRunner EncodingRunner { get; }
    public FakeSelectionFrameView SelectionFrame { get; }
    public FakeControlWindow ControlWindow { get; }
    public FakeSaveDialogService SaveDialog { get; }
    public FakeMessageService MessageService { get; }
    public FakeFileWriter FileWriter { get; }
    public InlineUiDispatcher UiDispatcher { get; }
    public FakeInputHookService InputHooks { get; }
  }

  private sealed class FakeRecordingRunner : GifRecordingSessionCoordinator.IGifRecordingRunner
  {
    public Action? BeforeCapture { get; set; }
    public Action? AfterCapture { get; set; }
    public event Action<GifRecordingProgress>? ProgressChanged;
    public int RequestStopCount { get; private set; }
    public Task<GifCaptureResult> ResultTask { get; set; } = new TaskCompletionSource<GifCaptureResult>().Task;
    public CancellationToken CapturedCancellationToken { get; private set; }

    public void RequestStop()
    {
      RequestStopCount++;
    }

    public Task<GifCaptureResult> RecordAsync(WinRect region, CancellationToken cancellationToken)
    {
      CapturedCancellationToken = cancellationToken;
      return ResultTask;
    }

    public void RaiseProgress(TimeSpan elapsed)
    {
      ProgressChanged?.Invoke(new GifRecordingProgress(elapsed, 1, 1, GifRecordingDefaults.MaxCaptureAttempts));
    }
  }

  private sealed class FakeEncodingRunner : GifRecordingSessionCoordinator.IGifEncodingRunner
  {
    public int CallCount { get; private set; }
    public int CalledThreadId { get; private set; }

    public byte[] Encode(IReadOnlyList<BitmapSource> frames, int frameIntervalMs)
    {
      CallCount++;
      CalledThreadId = Environment.CurrentManagedThreadId;
      return [1, 2, 3, 4];
    }
  }

  private sealed class FakeSelectionFrameView : GifRecordingSessionCoordinator.ISelectionFrameView
  {
    public bool InitializeCalled { get; private set; }
    public List<bool> LockedStates { get; } = [];
    public bool IsVisible { get; private set; }
    public bool CloseCalled { get; private set; }

    public void Initialize(WinRect captureRegion, double dpiScaleX, double dpiScaleY)
    {
      InitializeCalled = true;
    }

    public void SetLocked(bool locked)
    {
      LockedStates.Add(locked);
    }

    public void Show()
    {
      IsVisible = true;
    }

    public void Hide()
    {
      IsVisible = false;
    }

    public void Close()
    {
      CloseCalled = true;
      IsVisible = false;
    }
  }

  private sealed class FakeControlWindow : GifRecordingSessionCoordinator.IGifRecordingControlView
  {
    public event Action? StopRequested;
    public event Action? CancelRequested;

    public bool IsVisible { get; private set; }
    public bool CloseCalled { get; private set; }
    public int PositionCalls { get; private set; }
    public int StoppingHintCount { get; private set; }
    public List<TimeSpan> ProgressUpdates { get; } = [];

    public bool PositionNearSelection(WinRect captureRegion, double dpiScaleX, double dpiScaleY)
    {
      PositionCalls++;
      return true;
    }

    public void UpdateProgress(TimeSpan elapsed)
    {
      ProgressUpdates.Add(elapsed);
    }

    public void SetStoppingHint()
    {
      StoppingHintCount++;
    }

    public void Show()
    {
      IsVisible = true;
    }

    public void Hide()
    {
      IsVisible = false;
    }

    public void Close()
    {
      CloseCalled = true;
      IsVisible = false;
    }

    public void FocusWindow()
    {
    }

    public void RaiseStopRequested()
    {
      StopRequested?.Invoke();
    }

    public void RaiseCancelRequested()
    {
      CancelRequested?.Invoke();
    }
  }

  private sealed class FakeSaveDialogService : GifRecordingSessionCoordinator.IGifSaveDialogService
  {
    public string? ResultPath { get; set; }
    public int CalledThreadId { get; private set; }

    public string? ShowSaveDialog(string initialDirectory, string defaultFileName, string filter)
    {
      CalledThreadId = Environment.CurrentManagedThreadId;
      return ResultPath;
    }
  }

  private sealed class FakeMessageService : GifRecordingSessionCoordinator.IGifMessageService
  {
    public List<string> Messages { get; } = [];

    public void ShowWarning(string message, string title)
    {
      Messages.Add(message);
    }
  }

  private sealed class FakeFileWriter : GifRecordingSessionCoordinator.IGifFileWriter
  {
    public int CallCount { get; private set; }
    public int CalledThreadId { get; private set; }
    public string? Path { get; private set; }
    public byte[]? Bytes { get; private set; }

    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
      CallCount++;
      CalledThreadId = Environment.CurrentManagedThreadId;
      Path = path;
      Bytes = bytes;
      return Task.CompletedTask;
    }
  }

  private sealed class InlineUiDispatcher : GifRecordingSessionCoordinator.IUiDispatcher
  {
    public void Invoke(Action callback)
    {
      callback();
    }

    public void BeginInvoke(Action callback)
    {
      callback();
    }
  }

  private sealed class FakeInputHookService : GifRecordingSessionCoordinator.IGifInputHookService
  {
    public event Action? EscapePressed;

    public bool InstallCalled { get; private set; }
    public bool UninstallCalled { get; private set; }
    public bool DisposeCalled { get; private set; }

    public void Install()
    {
      InstallCalled = true;
    }

    public void Uninstall()
    {
      UninstallCalled = true;
    }

    public void Dispose()
    {
      DisposeCalled = true;
    }

    public void RaiseEscapePressed()
    {
      EscapePressed?.Invoke();
    }
  }
}
