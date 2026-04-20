using ScreenTranslator.Services;
using ScreenTranslator.Windows;
using Xunit;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
    var result = new GifCaptureResult(
      [CreateFrame()],
      1,
      false,
      null);

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
}
