using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

using ScreenTranslator.Interop;
using ScreenTranslator.Models;

using WinRect = System.Drawing.Rectangle;
using WpfScaleTransform = System.Windows.Media.ScaleTransform;

namespace ScreenTranslator.Services;

public enum LongScreenshotRunState
{
  Running,
  Paused,
  PendingFix,
  Completed,
  Canceled,
}

public enum LongScreenshotMarkerStatus
{
  Matched,
  Failed,
  ManuallyResolved,
  Skipped,
}

public enum LongScreenshotStopReason
{
  CompletedByUser,
  AutoReachedBottom,
  MaxFramesReached,
  MaxHeightReached,
  TooManySkippedFrames,
  NoFrames,
  Canceled,
  Error,
}

public sealed record LongScreenshotMarker(int Index, LongScreenshotMarkerStatus Status, double OutputRatioY, double MadPercent);

public sealed class LongScreenshotProgress
{
  public int CapturedFrames { get; init; }
  public int AcceptedFrames { get; init; }
  public int TotalHeightPx { get; init; }
  public int ConsecutiveNoChange { get; init; }
  public int ConsecutiveSkips { get; init; }
  public double LastMadPercent { get; init; }
  public string StatusKey { get; init; } = "LongScreenshot_Status_Ready";
  public BitmapSource? CurrentFramePreview { get; init; }
  public BitmapSource? StitchedPreview { get; init; }
  public IReadOnlyList<LongScreenshotMarker> Markers { get; init; } = [];
  public bool HasPendingMismatch { get; init; }
  public bool IsAutoScrolling { get; init; }
  public LongScreenshotRunState RunState { get; init; } = LongScreenshotRunState.Paused;
}

public sealed class LongScreenshotResult
{
  public BitmapSource? Image { get; init; }
  public int CapturedFrames { get; init; }
  public int AcceptedFrames { get; init; }
  public int TotalHeightPx { get; init; }
  public LongScreenshotStopReason StopReason { get; init; }
  public IReadOnlyList<LongScreenshotMarker> Markers { get; init; } = [];
}

public sealed class LongScreenshotService
{
  public LongScreenshotSession CreateSession(WinRect region, LongScreenshotSettings settings)
  {
    return new LongScreenshotSession(region, settings);
  }
}

public sealed class LongScreenshotSession : IDisposable
{
  private const int MaxConsecutiveSkips = 5;
  private const double MatchThresholdPercent = 8.0;
  private const int SampleStride = 4;
  private const int MinOverlapPx = 32;
  private const double MinOverlapRatio = 0.2;
  // Allow very high overlap to support small manual scroll steps.
  // A lower ceiling (e.g. 85%) can force duplicated content seams.
  private const double MaxOverlapRatio = 0.97;

  private readonly object _syncRoot = new();
  private readonly LongScreenshotSettings _rawSettings;
  private readonly CancellationTokenSource _cts = new();
  private readonly List<Bitmap> _segments = [];
  private readonly List<LongScreenshotMarker> _markers = [];

  private Task? _loopTask;
  private WinRect _region;

  private bool _stopRequested;
  private bool _canceled;
  private bool _disposed;

  private int _capturedFrames;
  private int _acceptedFrames;
  private int _totalHeight;
  private int _noChangeCount;
  private int _skipCount;
  private double _lastMad;

  private long _scrollAttempts;
  private long _processedScrollAttempts;

  private Bitmap? _previousAcceptedBitmap;
  private GrayFrame? _previousAcceptedGray;
  private GrayFrame? _previousCapturedGray;
  private bool _autoScrollEnabled;

  private Bitmap? _pendingFrame;
  private GrayFrame? _pendingGray;
  private double _pendingMad;

  private LongScreenshotResult? _result;

  public event Action<LongScreenshotProgress>? ProgressChanged;
  public event Action<LongScreenshotResult>? Completed;

  /// <summary>
  /// Called synchronously before each screen capture so callers can hide
  /// overlay windows that would otherwise pollute the captured frame.
  /// </summary>
  public Action? BeforeCapture { get; set; }

  /// <summary>
  /// Called synchronously after each screen capture to restore any overlays
  /// hidden by <see cref="BeforeCapture"/>.
  /// </summary>
  public Action? AfterCapture { get; set; }

  public LongScreenshotSession(WinRect region, LongScreenshotSettings settings)
  {
    _region = region;
    _rawSettings = settings ?? new LongScreenshotSettings();
    RunState = LongScreenshotRunState.Paused;
  }

  public LongScreenshotRunState RunState { get; private set; }

  public void Start()
  {
    lock (_syncRoot)
    {
      if (_loopTask is not null)
      {
        return;
      }

      RunState = LongScreenshotRunState.Running;
      _loopTask = Task.Run(() => CaptureLoopAsync(_cts.Token));
    }
  }

  public void Pause()
  {
    lock (_syncRoot)
    {
      if (RunState == LongScreenshotRunState.Running)
      {
        _autoScrollEnabled = false;
        RunState = LongScreenshotRunState.Paused;
        RaiseProgressLocked("LongScreenshot_Status_Paused", null);
      }
    }
  }

  public void Resume()
  {
    lock (_syncRoot)
    {
      if (RunState == LongScreenshotRunState.Paused)
      {
        RunState = LongScreenshotRunState.Running;
        RaiseProgressLocked("LongScreenshot_Status_Running", null);
      }
    }
  }

  public void Stop()
  {
    lock (_syncRoot)
    {
      _stopRequested = true;
      _autoScrollEnabled = false;
    }
  }

  public void Cancel()
  {
    lock (_syncRoot)
    {
      _canceled = true;
      _autoScrollEnabled = false;
      RunState = LongScreenshotRunState.Canceled;
    }

    _cts.Cancel();
  }

  public void NotifyScrollAttempt()
  {
    Interlocked.Increment(ref _scrollAttempts);
  }

  public void UpdateRegion(WinRect region)
  {
    lock (_syncRoot)
    {
      _region = region;
    }
  }

  public void SetAutoScroll(bool enabled)
  {
    lock (_syncRoot)
    {
      _autoScrollEnabled = enabled;
    }
  }

  public bool TryResolvePendingMismatch(double ratioY)
  {
    lock (_syncRoot)
    {
      if (RunState != LongScreenshotRunState.PendingFix || _pendingFrame is null || _pendingGray is null)
      {
        return false;
      }

      var pending = _pendingFrame;
      var pendingGray = _pendingGray;
      var overlap = CalculateManualOverlap(pending.Height, ratioY);
      if (!TryAppendFrameLocked(pending, pendingGray, overlap, LongScreenshotMarkerStatus.ManuallyResolved, _pendingMad))
      {
        return false;
      }

      _pendingFrame = null;
      _pendingGray = null;
      _pendingMad = 0;
      _skipCount = 0;
      RunState = LongScreenshotRunState.Running;
      RaiseProgressLocked("LongScreenshot_Status_ManualResolved", pending);
      return true;
    }
  }

  public bool SkipPendingMismatch()
  {
    lock (_syncRoot)
    {
      if (RunState != LongScreenshotRunState.PendingFix || _pendingFrame is null)
      {
        return false;
      }

      AddMarkerLocked(LongScreenshotMarkerStatus.Skipped, _pendingMad);
      _pendingFrame.Dispose();
      _pendingFrame = null;
      _pendingGray = null;
      _pendingMad = 0;
      RunState = LongScreenshotRunState.Running;
      RaiseProgressLocked("LongScreenshot_Status_FrameSkipped", null);
      return true;
    }
  }

  public LongScreenshotResult? GetResult()
  {
    lock (_syncRoot)
    {
      return _result;
    }
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    _cts.Cancel();

    try
    {
      _loopTask?.Wait(500);
    }
    catch
    {
      // ignore
    }

    _cts.Dispose();

    lock (_syncRoot)
    {
      foreach (var segment in _segments)
      {
        segment.Dispose();
      }

      _segments.Clear();
      _previousAcceptedBitmap?.Dispose();
      _previousAcceptedBitmap = null;
      _pendingFrame?.Dispose();
      _pendingFrame = null;
    }
  }

  private async Task CaptureLoopAsync(CancellationToken ct)
  {
    var wheelNotches = Math.Clamp(_rawSettings.WheelNotchesPerStep, 1, 12);
    var frameIntervalMs = Math.Clamp(_rawSettings.FrameIntervalMs, 100, 2000);
    var maxFrames = Math.Clamp(_rawSettings.MaxFrames, 20, 500);
    var maxHeight = Math.Clamp(_rawSettings.MaxTotalHeightPx, 5000, 200000);
    var noChangeThreshold = Math.Clamp(_rawSettings.NoChangeDiffThresholdPercent, 0.1, 10.0);
    var noChangeRequired = Math.Clamp(_rawSettings.NoChangeConsecutiveCount, 2, 10);

    LongScreenshotStopReason stopReason;

    try
    {
      ct.ThrowIfCancellationRequested();

      Bitmap? firstFrame;
      WinRect captureRegion;
      lock (_syncRoot)
      {
        captureRegion = _region;
      }

      if (captureRegion.Width <= 1 || captureRegion.Height <= 1)
      {
        stopReason = LongScreenshotStopReason.NoFrames;
        FinishSession(stopReason);
        return;
      }

      // Let overlay/toolbar transitions settle before first baseline capture.
      await Task.Delay(220, ct);

      firstFrame = CaptureFrameWithHooks(captureRegion);
      if (firstFrame is null)
      {
        stopReason = LongScreenshotStopReason.NoFrames;
        FinishSession(stopReason);
        return;
      }

      lock (_syncRoot)
      {
        _segments.Add(firstFrame);
        _previousAcceptedBitmap = (Bitmap)firstFrame.Clone();
        _previousAcceptedGray = BuildGrayFrame(_previousAcceptedBitmap, SampleStride);
        _previousCapturedGray = BuildGrayFrame(firstFrame, SampleStride);

        _capturedFrames = 1;
        _acceptedFrames = 1;
        _totalHeight = firstFrame.Height;
        _noChangeCount = 0;
        _skipCount = 0;
        _lastMad = 0;

        RaiseProgressLocked("LongScreenshot_Status_Running", firstFrame);
      }

      stopReason = LongScreenshotStopReason.CompletedByUser;

      while (true)
      {
        ct.ThrowIfCancellationRequested();

        LongScreenshotRunState state;
        bool shouldStop;
        lock (_syncRoot)
        {
          state = RunState;
          shouldStop = _stopRequested;
        }

        if (shouldStop)
        {
          stopReason = LongScreenshotStopReason.CompletedByUser;
          break;
        }

        if (state == LongScreenshotRunState.Paused || state == LongScreenshotRunState.PendingFix)
        {
          await Task.Delay(60, ct);
          continue;
        }

        var shouldStopLoop = false;
        lock (_syncRoot)
        {
          if (_acceptedFrames >= maxFrames)
          {
            stopReason = LongScreenshotStopReason.MaxFramesReached;
            shouldStopLoop = true;
          }
          else if (_totalHeight >= maxHeight)
          {
            stopReason = LongScreenshotStopReason.MaxHeightReached;
            shouldStopLoop = true;
          }
        }

        if (shouldStopLoop)
        {
          break;
        }

        bool isAutoScroll;
        WinRect scrollTarget;
        lock (_syncRoot)
        {
          isAutoScroll = _autoScrollEnabled && RunState == LongScreenshotRunState.Running;
          scrollTarget = _region;
        }

        if (isAutoScroll)
        {
          SendAutoScrollInput(scrollTarget, wheelNotches);
          Interlocked.Increment(ref _scrollAttempts);
        }

        await Task.Delay(frameIntervalMs, ct);

        if (Interlocked.Read(ref _scrollAttempts) == _processedScrollAttempts)
        {
          continue;
        }

        _processedScrollAttempts = Interlocked.Read(ref _scrollAttempts);

        lock (_syncRoot)
        {
          captureRegion = _region;
        }

        using var currentFrame = CaptureFrameWithHooks(captureRegion);
        if (currentFrame is null)
        {
          stopReason = LongScreenshotStopReason.Error;
          break;
        }

        var currentGray = BuildGrayFrame(currentFrame, SampleStride);

        shouldStopLoop = false;
        lock (_syncRoot)
        {
          _capturedFrames++;

          var prevCaptured = _previousCapturedGray;
          if (prevCaptured is not null)
          {
            _lastMad = CalculateBottomStripMad(prevCaptured, currentGray);
            _noChangeCount = _lastMad <= noChangeThreshold ? _noChangeCount + 1 : 0;
          }

          _previousCapturedGray = currentGray;

          if (_noChangeCount >= noChangeRequired)
          {
            RaiseProgressLocked("LongScreenshot_Status_AutoStopNoChange", currentFrame);
            stopReason = LongScreenshotStopReason.AutoReachedBottom;
            shouldStopLoop = true;
          }
          else if (_previousAcceptedGray is null)
          {
            stopReason = LongScreenshotStopReason.Error;
            shouldStopLoop = true;
          }
          else
          {
            var overlap = FindBestOverlap(_previousAcceptedGray, currentGray, currentFrame.Height);
            _lastMad = overlap.MadPercent;

            if (!overlap.IsMatch)
            {
              _skipCount++;
              _pendingFrame?.Dispose();
              _pendingFrame = (Bitmap)currentFrame.Clone();
              _pendingGray = currentGray;
              _pendingMad = overlap.MadPercent;
              AddMarkerLocked(LongScreenshotMarkerStatus.Failed, overlap.MadPercent);
              RunState = LongScreenshotRunState.PendingFix;
              RaiseProgressLocked("LongScreenshot_Status_PendingFix", currentFrame);

              if (_skipCount > MaxConsecutiveSkips)
              {
                stopReason = LongScreenshotStopReason.TooManySkippedFrames;
                shouldStopLoop = true;
              }

              if (!shouldStopLoop)
              {
                continue;
              }
            }
            else
            {
              _skipCount = 0;
              TryAppendFrameLocked(currentFrame, currentGray, overlap.OverlapPx, LongScreenshotMarkerStatus.Matched, overlap.MadPercent);
              RaiseProgressLocked("LongScreenshot_Status_FrameAdded", currentFrame);
            }
          }
        }

        if (shouldStopLoop)
        {
          break;
        }
      }

      lock (_syncRoot)
      {
        if (_canceled)
        {
          stopReason = LongScreenshotStopReason.Canceled;
          RunState = LongScreenshotRunState.Canceled;
        }
      }

      FinishSession(stopReason);
    }
    catch (OperationCanceledException)
    {
      FinishSession(LongScreenshotStopReason.Canceled);
    }
    catch
    {
      FinishSession(LongScreenshotStopReason.Error);
    }
  }

  private void FinishSession(LongScreenshotStopReason stopReason)
  {
    LongScreenshotResult result;

    lock (_syncRoot)
    {
      if (_result is not null)
      {
        return;
      }

      if (stopReason != LongScreenshotStopReason.Canceled)
      {
        RunState = LongScreenshotRunState.Completed;
      }

      var stitched = BuildFinalBitmap(_segments);
      BitmapSource? source = null;
      if (stitched is not null)
      {
        source = ConvertToBitmapSource(stitched);
        stitched.Dispose();
      }

      result = new LongScreenshotResult
      {
        Image = source,
        CapturedFrames = _capturedFrames,
        AcceptedFrames = _acceptedFrames,
        TotalHeightPx = _totalHeight,
        StopReason = stopReason,
        Markers = _markers.ToArray(),
      };

      _result = result;
      RaiseProgressLocked(
        result.Image is null ? "LongScreenshot_Status_NoResult" : "LongScreenshot_Status_Completed",
        null);
    }

    Completed?.Invoke(result);
  }

  private bool TryAppendFrameLocked(
    Bitmap frame,
    GrayFrame frameGray,
    int overlapPx,
    LongScreenshotMarkerStatus markerStatus,
    double madPercent)
  {
    var appendHeight = frame.Height - overlapPx;
    if (appendHeight <= 0)
    {
      return false;
    }

    var appendRect = new WinRect(0, overlapPx, frame.Width, appendHeight);
    var appendedSegment = frame.Clone(appendRect, PixelFormat.Format32bppArgb);
    _segments.Add(appendedSegment);

    _totalHeight += appendHeight;
    _acceptedFrames++;

    _previousAcceptedBitmap?.Dispose();
    _previousAcceptedBitmap = (Bitmap)frame.Clone();
    _previousAcceptedGray = frameGray;

    AddMarkerLocked(markerStatus, madPercent);
    return true;
  }

  private void AddMarkerLocked(LongScreenshotMarkerStatus status, double madPercent)
  {
    var markerIndex = _markers.Count;
    var maxFrames = Math.Clamp(_rawSettings.MaxFrames, 20, 500);
    var ratio = maxFrames <= 1
      ? 0
      : Math.Clamp((double)Math.Max(0, _capturedFrames - 1) / (maxFrames - 1), 0, 1);

    _markers.Add(new LongScreenshotMarker(markerIndex, status, ratio, madPercent));
  }

  private void RaiseProgressLocked(string statusKey, Bitmap? currentFrame)
  {
    var currentPreview = currentFrame is null ? null : CreatePreview(currentFrame);
    var stitchedPreview = BuildTailPreview(_segments);

    var progress = new LongScreenshotProgress
    {
      CapturedFrames = _capturedFrames,
      AcceptedFrames = _acceptedFrames,
      TotalHeightPx = _totalHeight,
      ConsecutiveNoChange = _noChangeCount,
      ConsecutiveSkips = _skipCount,
      LastMadPercent = _lastMad,
      StatusKey = statusKey,
      CurrentFramePreview = currentPreview,
      StitchedPreview = stitchedPreview,
      Markers = _markers.ToArray(),
      HasPendingMismatch = RunState == LongScreenshotRunState.PendingFix,
      IsAutoScrolling = _autoScrollEnabled,
      RunState = RunState,
    };

    ProgressChanged?.Invoke(progress);
  }

  private static int CalculateManualOverlap(int frameHeight, double ratioY)
  {
    var min = Math.Max(MinOverlapPx, (int)(frameHeight * MinOverlapRatio));
    var max = Math.Min((int)(frameHeight * MaxOverlapRatio), frameHeight - 1);
    if (max <= min)
    {
      return min;
    }

    var clampedRatio = Math.Clamp(ratioY, 0, 1);
    return min + (int)Math.Round((max - min) * clampedRatio);
  }

  private static void SendAutoScrollInput(WinRect region, int wheelNotches)
  {
    try
    {
      var centerX = region.X + region.Width / 2;
      var centerY = region.Y + region.Height / 2;
      NativeMethods.SetCursorPos(centerX, centerY);

      var inputs = new[]
      {
        new NativeMethods.INPUT
        {
          type = NativeMethods.INPUT_MOUSE,
          u = new NativeMethods.INPUTUNION
          {
            mi = new NativeMethods.MOUSEINPUT
            {
              mouseData = unchecked((uint)(-120 * wheelNotches)),
              dwFlags = NativeMethods.MOUSEEVENTF_WHEEL,
            }
          }
        }
      };

      NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }
    catch
    {
      // best effort
    }
  }

  /// <summary>
  /// Invokes <see cref="BeforeCapture"/>, captures the screen region,
  /// then invokes <see cref="AfterCapture"/>.
  /// This allows the coordinator to temporarily hide overlay windows
  /// so they don't appear in the captured frame.
  /// </summary>
  private Bitmap? CaptureFrameWithHooks(WinRect region)
  {
    try
    {
      BeforeCapture?.Invoke();

      // Give DWM at least one composition cycle to remove hidden overlays
      // from the screen buffer before BitBlt captures the region.
      if (BeforeCapture is not null)
      {
        Thread.Sleep(50);
      }

      try
      {
        return CaptureFrame(region);
      }
      finally
      {
        AfterCapture?.Invoke();
      }
    }
    catch
    {
      return null;
    }
  }

  private static Bitmap? CaptureFrame(WinRect region)
  {
    try
    {
      var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
      using var graphics = Graphics.FromImage(bitmap);
      graphics.CopyFromScreen(region.X, region.Y, 0, 0, new Size(region.Width, region.Height));
      ForceOpaqueAlpha(bitmap);
      return bitmap;
    }
    catch
    {
      return null;
    }
  }

  private static Bitmap? BuildFinalBitmap(IReadOnlyList<Bitmap> segments)
  {
    if (segments.Count == 0)
    {
      return null;
    }

    var width = segments[0].Width;
    var totalHeight = segments.Sum(s => s.Height);

    if (width <= 0 || totalHeight <= 0)
    {
      return null;
    }

    var output = new Bitmap(width, totalHeight, PixelFormat.Format32bppArgb);
    using var graphics = Graphics.FromImage(output);
    graphics.CompositingMode = CompositingMode.SourceCopy;
    graphics.Clear(System.Drawing.Color.Transparent);

    var offsetY = 0;
    foreach (var segment in segments)
    {
      graphics.DrawImageUnscaled(segment, 0, offsetY);
      offsetY += segment.Height;
    }

    ForceOpaqueAlpha(output);

    return output;
  }

  private static BitmapSource BuildTailPreview(IReadOnlyList<Bitmap> segments)
  {
    if (segments.Count == 0)
    {
      return EmptyPreview();
    }

    var take = Math.Min(4, segments.Count);
    var tail = segments.Skip(segments.Count - take).ToArray();
    var width = tail.Max(s => s.Width);
    var totalHeight = tail.Sum(s => s.Height);

    using var bitmap = new Bitmap(width, totalHeight, PixelFormat.Format32bppArgb);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.Clear(System.Drawing.Color.Transparent);

    var y = 0;
    foreach (var item in tail)
    {
      graphics.DrawImage(item, 0, y, item.Width, item.Height);
      y += item.Height;
    }

    ForceOpaqueAlpha(bitmap);

    return CreatePreview(bitmap);
  }

  private static BitmapSource EmptyPreview()
  {
    using var bitmap = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
    return CreatePreview(bitmap);
  }

  private static BitmapSource ConvertToBitmapSource(Bitmap bitmap)
  {
    var bitmapData = bitmap.LockBits(
      new WinRect(0, 0, bitmap.Width, bitmap.Height),
      ImageLockMode.ReadOnly,
      bitmap.PixelFormat);

    try
    {
      var source = BitmapSource.Create(
        bitmapData.Width,
        bitmapData.Height,
        96,
        96,
        System.Windows.Media.PixelFormats.Bgra32,
        null,
        bitmapData.Scan0,
        bitmapData.Stride * bitmapData.Height,
        bitmapData.Stride);

      source.Freeze();
      return source;
    }
    finally
    {
      bitmap.UnlockBits(bitmapData);
    }
  }

  private static void ForceOpaqueAlpha(Bitmap bitmap)
  {
    if (bitmap.PixelFormat != PixelFormat.Format32bppArgb &&
        bitmap.PixelFormat != PixelFormat.Format32bppPArgb)
    {
      return;
    }

    var lockRect = new WinRect(0, 0, bitmap.Width, bitmap.Height);
    var bitmapData = bitmap.LockBits(lockRect, ImageLockMode.ReadWrite, bitmap.PixelFormat);
    try
    {
      var strideBytes = Math.Abs(bitmapData.Stride);
      var raw = new byte[strideBytes * bitmap.Height];
      Marshal.Copy(bitmapData.Scan0, raw, 0, raw.Length);

      for (var y = 0; y < bitmap.Height; y++)
      {
        var row = bitmapData.Stride > 0
          ? y * strideBytes
          : (bitmap.Height - 1 - y) * strideBytes;
        var rowEnd = row + (bitmap.Width * 4);
        for (var i = row + 3; i < rowEnd; i += 4)
        {
          raw[i] = 255;
        }
      }

      Marshal.Copy(raw, 0, bitmapData.Scan0, raw.Length);
    }
    finally
    {
      bitmap.UnlockBits(bitmapData);
    }
  }

  private static BitmapSource CreatePreview(Bitmap bitmap)
  {
    const int maxWidth = 240;
    const int maxHeight = 180;

    var source = ConvertToBitmapSource(bitmap);
    if (source.PixelWidth <= maxWidth && source.PixelHeight <= maxHeight)
    {
      return source;
    }

    var scale = Math.Min((double)maxWidth / source.PixelWidth, (double)maxHeight / source.PixelHeight);
    var transformed = new TransformedBitmap(source, new WpfScaleTransform(scale, scale));
    transformed.Freeze();
    return transformed;
  }

  private static GrayFrame BuildGrayFrame(Bitmap bitmap, int stride)
  {
    var sampledWidth = Math.Max(1, bitmap.Width / stride);
    var sampledHeight = Math.Max(1, bitmap.Height / stride);
    var data = new byte[sampledWidth * sampledHeight];

    var lockRect = new WinRect(0, 0, bitmap.Width, bitmap.Height);
    var bitmapData = bitmap.LockBits(lockRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    try
    {
      var strideBytes = Math.Abs(bitmapData.Stride);
      var raw = new byte[strideBytes * bitmap.Height];
      Marshal.Copy(bitmapData.Scan0, raw, 0, raw.Length);

      for (var y = 0; y < sampledHeight; y++)
      {
        var srcY = Math.Min(bitmap.Height - 1, y * stride);
        var srcRow = bitmapData.Stride > 0
          ? srcY * strideBytes
          : (bitmap.Height - 1 - srcY) * strideBytes;
        for (var x = 0; x < sampledWidth; x++)
        {
          var srcX = Math.Min(bitmap.Width - 1, x * stride);
          var offset = srcRow + (srcX * 4);
          var b = raw[offset];
          var g = raw[offset + 1];
          var r = raw[offset + 2];
          data[(y * sampledWidth) + x] = (byte)((77 * r + 150 * g + 29 * b) >> 8);
        }
      }
    }
    finally
    {
      bitmap.UnlockBits(bitmapData);
    }

    return new GrayFrame(sampledWidth, sampledHeight, data);
  }

  private static double CalculateBottomStripMad(GrayFrame previous, GrayFrame current)
  {
    var width = Math.Min(previous.Width, current.Width);
    var height = Math.Min(previous.Height, current.Height);
    if (width <= 0 || height <= 0)
    {
      return 100;
    }

    var stripHeight = Math.Min(96 / SampleStride, height / 4);
    stripHeight = Math.Max(1, stripHeight);
    var startA = previous.Height - stripHeight;
    var startB = current.Height - stripHeight;

    return CalculateMad(previous, startA, current, startB, stripHeight);
  }

  private static OverlapResult FindBestOverlap(GrayFrame previous, GrayFrame current, int currentFrameHeightPx)
  {
    var width = Math.Min(previous.Width, current.Width);
    var maxOverlapPx = Math.Min((int)(currentFrameHeightPx * MaxOverlapRatio), currentFrameHeightPx - 1);
    var minOverlapPx = Math.Max(MinOverlapPx, (int)(currentFrameHeightPx * MinOverlapRatio));
    var minOverlap = Math.Max(1, minOverlapPx / SampleStride);
    var maxOverlap = Math.Max(minOverlap, maxOverlapPx / SampleStride);

    var bestMad = 100.0;
    var bestOverlap = minOverlap;

    for (var overlap = minOverlap; overlap <= maxOverlap; overlap++)
    {
      if (overlap >= previous.Height || overlap >= current.Height || width <= 0)
      {
        continue;
      }

      var startA = previous.Height - overlap;
      var mad = CalculateMad(previous, startA, current, 0, overlap);
      if (mad < bestMad)
      {
        bestMad = mad;
        bestOverlap = overlap;
      }
    }

    return new OverlapResult
    {
      IsMatch = bestMad <= MatchThresholdPercent,
      OverlapPx = bestOverlap * SampleStride,
      MadPercent = bestMad,
    };
  }

  private static double CalculateMad(GrayFrame a, int startAY, GrayFrame b, int startBY, int rows)
  {
    var width = Math.Min(a.Width, b.Width);
    if (width <= 0 || rows <= 0)
    {
      return 100;
    }

    long diff = 0;
    long count = 0;
    for (var y = 0; y < rows; y++)
    {
      var rowA = (startAY + y) * a.Width;
      var rowB = (startBY + y) * b.Width;
      for (var x = 0; x < width; x++)
      {
        diff += Math.Abs(a.Data[rowA + x] - b.Data[rowB + x]);
        count++;
      }
    }

    if (count == 0)
    {
      return 100;
    }

    return (diff * 100.0) / (count * 255.0);
  }

  private sealed record GrayFrame(int Width, int Height, byte[] Data);

  private sealed class OverlapResult
  {
    public bool IsMatch { get; init; }
    public int OverlapPx { get; init; }
    public double MadPercent { get; init; }
  }
}
