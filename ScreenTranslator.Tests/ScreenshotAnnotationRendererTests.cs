using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenTranslator.Services;
using Xunit;

namespace ScreenTranslator.Tests;

public sealed class ScreenshotAnnotationRendererTests
{
  [Fact]
  public void RenderComposite_Clips_Rectangle_Stroke_To_Clip_Mask()
  {
    var baseImage = CreateSolidImage(40, 40, Colors.Navy);
    var session = new ScreenshotAnnotationSession(
      new Size(40, 40),
      new EllipseGeometry(new Point(20, 20), 8, 8));

    session.SetActiveTool(ScreenshotAnnotationTool.Rectangle);
    session.CommitRectangle(new Rect(12, 12, 16, 16), Colors.Red, 4);

    var result = ScreenshotAnnotationRenderer.RenderComposite(baseImage, session);

    Assert.Equal(40, result.PixelWidth);
    Assert.Equal(40, result.PixelHeight);
    Assert.Equal(GetPixel(baseImage, 12, 12), GetPixel(result, 12, 12));
    Assert.NotEqual(GetPixel(baseImage, 20, 12), GetPixel(result, 20, 12));
  }

  [Fact]
  public void RenderComposite_Clips_Brush_Stroke_And_Preserves_Segmented_Gaps()
  {
    var baseImage = CreateSolidImage(40, 40, Colors.Navy);
    var session = new ScreenshotAnnotationSession(
      new Size(40, 40),
      new EllipseGeometry(new Point(20, 20), 12, 12));

    session.SetActiveTool(ScreenshotAnnotationTool.Brush);
    session.CommitStroke(
      [
        new Point(12, 12),
        new Point(28, 28)
      ],
      Colors.Red,
      strokeThickness: 8);

    var result = ScreenshotAnnotationRenderer.RenderComposite(baseImage, session);

    Assert.Equal(40, result.PixelWidth);
    Assert.Equal(40, result.PixelHeight);
    Assert.NotEqual(GetPixel(baseImage, 20, 20), GetPixel(result, 20, 20));
    Assert.Equal(GetPixel(baseImage, 8, 8), GetPixel(result, 8, 8));
  }

  [Fact]
  public void RenderComposite_Applies_Mosaic_To_Segmented_Stroke_Without_Bridging_Gaps()
  {
    var baseImage = CreateGradientImage(40, 40);
    var session = new ScreenshotAnnotationSession(
      new Size(40, 40),
      new EllipseGeometry(new Point(20, 20), 12, 12));

    session.SetActiveTool(ScreenshotAnnotationTool.Mosaic);
    session.CommitStroke(
      [
        new Point(12, 14),
        new Point(28, 14),
        new Point(30, 30),
        new Point(32, 32),
        new Point(12, 26),
        new Point(28, 26)
      ],
      Colors.Transparent,
      strokeThickness: 4);

    var result = ScreenshotAnnotationRenderer.RenderComposite(baseImage, session);

    Assert.Equal(40, result.PixelWidth);
    Assert.Equal(40, result.PixelHeight);
    Assert.NotEqual(GetPixel(baseImage, 16, 14), GetPixel(result, 16, 14));
    Assert.Equal(GetPixel(baseImage, 20, 20), GetPixel(result, 20, 20));
    Assert.Equal(GetPixel(baseImage, 0, 0), GetPixel(result, 0, 0));
  }

  [Fact]
  public void RenderComposite_Draws_Arrow_Line_And_Head()
  {
    var baseImage = CreateSolidImage(60, 40, Colors.Navy);
    var session = new ScreenshotAnnotationSession(
      new Size(60, 40),
      new RectangleGeometry(new Rect(0, 0, 60, 40)));

    session.SetActiveTool(ScreenshotAnnotationTool.Arrow);
    session.CommitArrow(new Point(10, 20), new Point(45, 20), Colors.Red, strokeThickness: 4);

    var result = ScreenshotAnnotationRenderer.RenderComposite(baseImage, session);

    Assert.NotEqual(GetPixel(baseImage, 25, 20), GetPixel(result, 25, 20));
    Assert.NotEqual(GetPixel(baseImage, 41, 16), GetPixel(result, 41, 16));
  }

  private static WriteableBitmap CreateGradientImage(int width, int height)
  {
    var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
    var pixels = new byte[width * height * 4];

    for (var y = 0; y < height; y++)
    {
      for (var x = 0; x < width; x++)
      {
        var index = (y * width + x) * 4;
        pixels[index + 0] = (byte)(x * 7);
        pixels[index + 1] = (byte)(y * 7);
        pixels[index + 2] = (byte)((x + y) * 4);
        pixels[index + 3] = 255;
      }
    }

    bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
    return bitmap;
  }

  private static WriteableBitmap CreateSolidImage(int width, int height, Color color)
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
    return bitmap;
  }

  private static uint GetPixel(BitmapSource source, int x, int y)
  {
    var pixels = new byte[4];
    source.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
    return BitConverter.ToUInt32(pixels, 0);
  }
}
