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
