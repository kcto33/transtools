using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ScreenTranslator.Interop;

namespace ScreenTranslator.Services;

public sealed record GifRecordingProgress(TimeSpan Elapsed, int CapturedFrames, int Attempts, int MaxAttempts);

public sealed record GifCaptureResult(
  IReadOnlyList<BitmapSource> Frames,
  int Attempts,
  bool HitDurationLimit,
  string? ErrorMessage);

public sealed class GifRecordingService
{
  private readonly Func<Rectangle, BitmapSource> _captureFrame;
  private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
  private int _stopRequested;

  public GifRecordingService(
    Func<Rectangle, BitmapSource>? captureFrame = null,
    Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
  {
    _captureFrame = captureFrame ?? DefaultCaptureFrame;
    _delayAsync = delayAsync ?? Task.Delay;
  }

  public Action? BeforeCapture { get; set; }

  public Action? AfterCapture { get; set; }

  public event Action<GifRecordingProgress>? ProgressChanged;

  public void RequestStop()
  {
    Interlocked.Exchange(ref _stopRequested, 1);
  }

  public async Task<GifCaptureResult> RecordAsync(Rectangle region, CancellationToken cancellationToken)
  {
    Interlocked.Exchange(ref _stopRequested, 0);

    var frames = new List<BitmapSource>();
    var attempts = 0;
    var consecutiveFailures = 0;

    while (true)
    {
      cancellationToken.ThrowIfCancellationRequested();

      if (ShouldStop(Volatile.Read(ref _stopRequested) != 0, attempts))
      {
        var stopRequested = Volatile.Read(ref _stopRequested) != 0;
        return new GifCaptureResult(frames, attempts, attempts >= GifRecordingDefaults.MaxCaptureAttempts && !stopRequested, null);
      }

      attempts++;

      Exception? captureException = null;

      try
      {
        BeforeCapture?.Invoke();

        try
        {
          var frame = _captureFrame(region) ?? throw new InvalidOperationException("captureFrame returned null.");
          frames.Add(frame);
          consecutiveFailures = 0;
        }
        catch (OperationCanceledException)
        {
          throw;
        }
        catch (Exception ex)
        {
          captureException = ex;
          consecutiveFailures++;
        }
      }
      finally
      {
        AfterCapture?.Invoke();
      }

      ProgressChanged?.Invoke(new GifRecordingProgress(
        GetElapsedForAttempts(attempts),
        frames.Count,
        attempts,
        GifRecordingDefaults.MaxCaptureAttempts));

      if (captureException is not null && ShouldAbortForConsecutiveFailures(consecutiveFailures))
      {
        return new GifCaptureResult(frames, attempts, false, captureException.Message);
      }

      if (ShouldStop(Volatile.Read(ref _stopRequested) != 0, attempts))
      {
        var stopRequested = Volatile.Read(ref _stopRequested) != 0;
        return new GifCaptureResult(frames, attempts, attempts >= GifRecordingDefaults.MaxCaptureAttempts && !stopRequested, null);
      }

      await _delayAsync(TimeSpan.FromMilliseconds(GifRecordingDefaults.FrameIntervalMs), cancellationToken).ConfigureAwait(false);
    }
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
    if (attempts <= 0)
    {
      return TimeSpan.Zero;
    }

    return TimeSpan.FromMilliseconds((long)attempts * GifRecordingDefaults.FrameIntervalMs);
  }

  private static BitmapSource DefaultCaptureFrame(Rectangle region)
  {
    using var bitmap = CaptureService.CaptureRegion(region, includeCursor: true);
    var hBitmap = bitmap.GetHbitmap();

    try
    {
      var source = Imaging.CreateBitmapSourceFromHBitmap(
        hBitmap,
        IntPtr.Zero,
        Int32Rect.Empty,
        BitmapSizeOptions.FromEmptyOptions());

      if (source.CanFreeze)
      {
        source.Freeze();
      }

      return source;
    }
    finally
    {
      NativeMethods.DeleteObject(hBitmap);
    }
  }
}
