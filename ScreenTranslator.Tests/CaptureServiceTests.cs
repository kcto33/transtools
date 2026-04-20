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

  [Fact]
  public void ResolveOverlayCursorKind_ReturnsHand_WhenCursorMatchesStandardHand()
  {
    var kind = CaptureService.ResolveOverlayCursorKind(
      cursorHandle: new IntPtr(2),
      arrowCursorHandle: new IntPtr(1),
      handCursorHandle: new IntPtr(2),
      iBeamCursorHandle: new IntPtr(3));

    Assert.Equal(CaptureService.OverlayCursorKind.Hand, kind);
  }

  [Fact]
  public void ResolveOverlayCursorKind_ReturnsIBeam_WhenCursorMatchesStandardTextCursor()
  {
    var kind = CaptureService.ResolveOverlayCursorKind(
      cursorHandle: new IntPtr(3),
      arrowCursorHandle: new IntPtr(1),
      handCursorHandle: new IntPtr(2),
      iBeamCursorHandle: new IntPtr(3));

    Assert.Equal(CaptureService.OverlayCursorKind.IBeam, kind);
  }

  [Fact]
  public void ResolveOverlayCursorKind_FallsBackToArrow_WhenCursorIsUnknown()
  {
    var kind = CaptureService.ResolveOverlayCursorKind(
      cursorHandle: new IntPtr(99),
      arrowCursorHandle: new IntPtr(1),
      handCursorHandle: new IntPtr(2),
      iBeamCursorHandle: new IntPtr(3));

    Assert.Equal(CaptureService.OverlayCursorKind.Arrow, kind);
  }

  [Theory]
  [InlineData(0, 4, 2)]
  [InlineData(1, 12, 3)]
  [InlineData(2, 7, 22)]
  public void GetOverlayCursorHotspot_ReturnsEnhancedHotspot_ForKind(
    int kindValue,
    int expectedX,
    int expectedY)
  {
    var kind = (CaptureService.OverlayCursorKind)kindValue;
    var hotspot = CaptureService.GetOverlayCursorHotspot(kind);

    Assert.Equal(new Point(expectedX, expectedY), hotspot);
  }
}
