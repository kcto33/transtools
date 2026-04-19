using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenTranslator.Services;
using Xunit;

namespace ScreenTranslator.Tests;

public sealed class ScreenshotAnnotationRendererTests
{
  [Fact]
  public void RenderComposite_Draws_Rectangle_Stroke_Over_Base_Image()
  {
    var baseImage = new WriteableBitmap(40, 40, 96, 96, PixelFormats.Bgra32, null);
    var session = new ScreenshotAnnotationSession(
      new Size(40, 40),
      Geometry.Parse("M0,0 L40,0 40,40 0,40 Z"));

    session.SetActiveTool(ScreenshotAnnotationTool.Rectangle);
    session.CommitRectangle(new Rect(5, 5, 20, 10), Colors.Red, 4);

    var result = ScreenshotAnnotationRenderer.RenderComposite(baseImage, session);

    Assert.Equal(40, result.PixelWidth);
    Assert.Equal(40, result.PixelHeight);
    Assert.NotSame(baseImage, result);
  }

  [Fact]
  public void RenderComposite_Applies_Mosaic_To_Selected_Region_Only()
  {
    var baseImage = new WriteableBitmap(32, 32, 96, 96, PixelFormats.Bgra32, null);
    var session = new ScreenshotAnnotationSession(
      new Size(32, 32),
      Geometry.Parse("M0,0 L32,0 32,32 0,32 Z"));

    session.SetActiveTool(ScreenshotAnnotationTool.Mosaic);
    session.CommitStroke(
      [
        new Point(4, 4),
        new Point(8, 8),
        new Point(12, 12)
      ],
      Colors.Transparent,
      strokeThickness: 10);

    var result = ScreenshotAnnotationRenderer.RenderComposite(baseImage, session);

    Assert.Equal(32, result.PixelWidth);
    Assert.Equal(32, result.PixelHeight);
    Assert.NotSame(baseImage, result);
  }
}
