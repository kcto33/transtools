using System.Drawing;

using ScreenTranslator.Services;

using Xunit;

namespace ScreenTranslator.Tests;

public sealed class CaptureServiceTests
{
  [Fact]
  public void ShouldDrawCursor_ReturnsTrue_WhenCursorHotspotInsideCaptureRegion()
  {
    var shouldDraw = CaptureService.ShouldDrawCursor(
      new Rectangle(100, 200, 300, 150),
      new Point(250, 260));

    Assert.True(shouldDraw);
  }

  [Fact]
  public void ShouldDrawCursor_ReturnsFalse_WhenCursorHotspotOutsideCaptureRegion()
  {
    var shouldDraw = CaptureService.ShouldDrawCursor(
      new Rectangle(100, 200, 300, 150),
      new Point(450, 260));

    Assert.False(shouldDraw);
  }

  [Fact]
  public void GetCursorDrawLocation_TranslatesScreenPositionIntoCaptureRelativePosition()
  {
    var drawLocation = CaptureService.GetCursorDrawLocation(
      new Rectangle(100, 200, 300, 150),
      new Point(250, 260),
      hotspotX: 6,
      hotspotY: 4);

    Assert.Equal(new Point(144, 56), drawLocation);
  }
}
