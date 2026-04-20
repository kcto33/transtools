using System.Drawing;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenTranslator.Services;
using Xunit;

using WpfColor = System.Windows.Media.Color;

namespace ScreenTranslator.Tests;

public sealed class GifRecordingServiceTests
{
  [Fact]
  public async Task RecordAsync_Stops_When_Max_Attempts_Is_Reached_And_Reports_Calls()
  {
    var captureCalls = 0;
    var delayCalls = 0;
    var beforeCalls = 0;
    var afterCalls = 0;

    var service = new GifRecordingService(
      captureFrame: _ =>
      {
        captureCalls++;
        return CreateFrame(4, 4, Colors.Red);
      },
      delayAsync: (_, _) =>
      {
        delayCalls++;
        return Task.CompletedTask;
      })
    {
      BeforeCapture = () => beforeCalls++,
      AfterCapture = () => afterCalls++,
    };

    var result = await service.RecordAsync(new Rectangle(0, 0, 100, 100), CancellationToken.None);

    Assert.Equal(GifRecordingDefaults.MaxCaptureAttempts, captureCalls);
    Assert.Equal(GifRecordingDefaults.MaxCaptureAttempts, result.Attempts);
    Assert.Equal(GifRecordingDefaults.MaxCaptureAttempts, result.Frames.Count);
    Assert.True(result.HitDurationLimit);
    Assert.Null(result.ErrorMessage);
    Assert.Equal(GifRecordingDefaults.MaxCaptureAttempts, beforeCalls);
    Assert.Equal(GifRecordingDefaults.MaxCaptureAttempts, afterCalls);
    Assert.Equal(GifRecordingDefaults.MaxCaptureAttempts - 1, delayCalls);
  }

  [Fact]
  public async Task RecordAsync_Returns_False_HitDurationLimit_When_Stop_Is_Requested()
  {
    var captureCalls = 0;
    GifRecordingService? service = null;
    service = new GifRecordingService(
      captureFrame: _ =>
      {
        captureCalls++;
        service!.RequestStop();
        return CreateFrame(4, 4, Colors.Red);
      },
      delayAsync: (_, _) => Task.CompletedTask);

    var result = await service.RecordAsync(new Rectangle(0, 0, 100, 100), CancellationToken.None);

    Assert.Equal(1, captureCalls);
    Assert.Equal(1, result.Attempts);
    Assert.False(result.HitDurationLimit);
    Assert.Null(result.ErrorMessage);
  }

  [Fact]
  public async Task RecordAsync_Resets_Stop_State_Between_Runs()
  {
    var captureCalls = 0;
    GifRecordingService? service = null;
    service = new GifRecordingService(
      captureFrame: _ =>
      {
        captureCalls++;
        if (captureCalls == 1)
        {
          service!.RequestStop();
        }

        return CreateFrame(4, 4, Colors.Red);
      },
      delayAsync: (_, _) => Task.CompletedTask);

    var firstResult = await service.RecordAsync(new Rectangle(0, 0, 100, 100), CancellationToken.None);
    var secondResult = await service.RecordAsync(new Rectangle(0, 0, 100, 100), CancellationToken.None);

    Assert.Equal(1, firstResult.Attempts);
    Assert.False(firstResult.HitDurationLimit);
    Assert.True(secondResult.Attempts > 1);
    Assert.True(secondResult.HitDurationLimit);
  }

  [Fact]
  public async Task RecordAsync_Invokes_AfterCapture_When_BeforeCapture_Throws()
  {
    var afterCalls = 0;
    var service = new GifRecordingService(
      captureFrame: _ => CreateFrame(4, 4, Colors.Red),
      delayAsync: (_, _) => Task.CompletedTask)
    {
      BeforeCapture = () => throw new InvalidOperationException("before failed"),
      AfterCapture = () => afterCalls++,
    };

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
      service.RecordAsync(new Rectangle(0, 0, 100, 100), CancellationToken.None));

    Assert.Equal("before failed", exception.Message);
    Assert.Equal(1, afterCalls);
  }

  [Fact]
  public async Task RecordAsync_Resets_ConsecutiveFailures_After_Success()
  {
    var captureCalls = 0;
    GifRecordingService? service = null;
    service = new GifRecordingService(
      captureFrame: _ =>
      {
        captureCalls++;
        if (captureCalls == 1)
        {
          throw new InvalidOperationException("first failure");
        }

        if (captureCalls == 2)
        {
          return CreateFrame(4, 4, Colors.Green);
        }

        if (captureCalls == 3)
        {
          throw new InvalidOperationException("second failure");
        }

        service!.RequestStop();
        throw new InvalidOperationException("third failure");
      },
      delayAsync: (_, _) => Task.CompletedTask);

    var result = await service.RecordAsync(new Rectangle(0, 0, 100, 100), CancellationToken.None);

    Assert.Equal(4, captureCalls);
    Assert.Equal(4, result.Attempts);
    Assert.Single(result.Frames);
    Assert.Null(result.ErrorMessage);
    Assert.False(result.HitDurationLimit);
  }

  [Fact]
  public async Task RecordAsync_Raises_Correct_Progress_Payload_For_Each_Attempt()
  {
    var progress = new List<GifRecordingProgress>();
    var captureCalls = 0;
    GifRecordingService? service = null;
    service = new GifRecordingService(
      captureFrame: _ =>
      {
        captureCalls++;

        if (captureCalls == 2)
        {
          service!.RequestStop();
        }

        return CreateFrame(4, 4, Colors.Blue);
      },
      delayAsync: (_, _) => Task.CompletedTask);

    service.ProgressChanged += progress.Add;

    var result = await service.RecordAsync(new Rectangle(0, 0, 100, 100), CancellationToken.None);

    Assert.Equal(2, result.Attempts);
    Assert.Equal(2, progress.Count);

    Assert.Equal(TimeSpan.FromMilliseconds(GifRecordingDefaults.FrameIntervalMs), progress[0].Elapsed);
    Assert.Equal(1, progress[0].CapturedFrames);
    Assert.Equal(1, progress[0].Attempts);
    Assert.Equal(GifRecordingDefaults.MaxCaptureAttempts, progress[0].MaxAttempts);

    Assert.Equal(TimeSpan.FromMilliseconds(GifRecordingDefaults.FrameIntervalMs * 2), progress[1].Elapsed);
    Assert.Equal(2, progress[1].CapturedFrames);
    Assert.Equal(2, progress[1].Attempts);
    Assert.Equal(GifRecordingDefaults.MaxCaptureAttempts, progress[1].MaxAttempts);
  }

  [Fact]
  public void ShouldAbortForConsecutiveFailures_Returns_True_At_Threshold()
  {
    var shouldAbort = GifRecordingService.ShouldAbortForConsecutiveFailures(
      GifRecordingDefaults.MaxConsecutiveCaptureFailures);

    Assert.True(shouldAbort);
  }

  [Fact]
  public async Task RecordAsync_Returns_Error_After_Three_Consecutive_Capture_Failures()
  {
    var captureCalls = 0;
    var delayCalls = 0;

    var service = new GifRecordingService(
      captureFrame: _ =>
      {
        captureCalls++;
        throw new InvalidOperationException("capture failed");
      },
      delayAsync: (_, _) =>
      {
        delayCalls++;
        return Task.CompletedTask;
      });

    var result = await service.RecordAsync(new Rectangle(0, 0, 100, 100), CancellationToken.None);

    Assert.Equal(GifRecordingDefaults.MaxConsecutiveCaptureFailures, captureCalls);
    Assert.Equal(GifRecordingDefaults.MaxConsecutiveCaptureFailures, result.Attempts);
    Assert.Empty(result.Frames);
    Assert.False(result.HitDurationLimit);
    Assert.Contains("capture failed", result.ErrorMessage);
    Assert.Equal(GifRecordingDefaults.MaxConsecutiveCaptureFailures - 1, delayCalls);
  }

  [Fact]
  public void GetElapsedForAttempts_Returns_Thirty_Seconds_At_Max_Attempts()
  {
    var elapsed = GifRecordingService.GetElapsedForAttempts(GifRecordingDefaults.MaxCaptureAttempts);

    Assert.Equal(TimeSpan.FromSeconds(GifRecordingDefaults.MaxDurationSeconds), elapsed);
  }

  private static BitmapSource CreateFrame(int width, int height, WpfColor color)
  {
    var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
    var pixels = new byte[width * height * 4];

    for (var index = 0; index < pixels.Length; index += 4)
    {
      pixels[index + 0] = color.B;
      pixels[index + 1] = color.G;
      pixels[index + 2] = color.R;
      pixels[index + 3] = color.A;
    }

    bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
    bitmap.Freeze();
    return bitmap;
  }
}
