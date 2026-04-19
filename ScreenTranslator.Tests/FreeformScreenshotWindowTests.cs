using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenTranslator.Services;
using ScreenTranslator.Windows;
using Xunit;

namespace ScreenTranslator.Tests;

public sealed class FreeformScreenshotWindowTests
{
  [Fact]
  public void GetOutputImage_ReturnsBaseImage_WhenAnnotationSessionIsNull()
  {
    var baseImage = CreateSolidImage(20, 20, Colors.Navy);

    var result = FreeformScreenshotWindow.GetOutputImage(baseImage, session: null);

    Assert.Same(baseImage, result);
  }

  [Fact]
  public void GetOutputImage_ReturnsCompositedImage_WhenAnnotationSessionExists()
  {
    var baseImage = CreateSolidImage(20, 20, Colors.Navy);
    var session = new ScreenshotAnnotationSession(
      new Size(20, 20),
      new RectangleGeometry(new Rect(0, 0, 20, 20)));

    session.SetActiveTool(ScreenshotAnnotationTool.Rectangle);
    session.CommitRectangle(new Rect(4, 4, 12, 12), Colors.Red, 3);

    var result = FreeformScreenshotWindow.GetOutputImage(baseImage, session);

    Assert.NotSame(baseImage, result);
    Assert.Equal(20, result.PixelWidth);
    Assert.Equal(20, result.PixelHeight);
    Assert.NotEqual(GetPixel(baseImage, 10, 4), GetPixel(result, 10, 4));
    Assert.Equal(GetPixel(baseImage, 1, 1), GetPixel(result, 1, 1));
  }

  [Fact]
  public void CreateEditModeState_Creates_Local_EditMask_From_Freeform_Geometry()
  {
    var geometry = new PathGeometry(
      new[]
      {
        new PathFigure(
          new Point(10, 10),
          new PathSegment[]
          {
            new LineSegment(new Point(30, 10), true),
            new LineSegment(new Point(30, 30), true),
            new LineSegment(new Point(10, 30), true)
          },
          true),
        new PathFigure(
          new Point(15, 15),
          new PathSegment[]
          {
            new LineSegment(new Point(25, 15), true),
            new LineSegment(new Point(25, 25), true),
            new LineSegment(new Point(15, 25), true)
          },
          true)
      });
    var boundingRect = new Rect(10, 10, 20, 20);

    var state = FreeformScreenshotWindow.CreateEditModeState(geometry, boundingRect, dpiScaleX: 1, dpiScaleY: 1);

    Assert.True(state.IsEditMode);
    Assert.False(state.IsAnnotating);
    Assert.NotNull(state.AnnotationSession);
    Assert.Equal(ScreenshotAnnotationTool.Brush, state.AnnotationSession!.ActiveTool);
    Assert.Equal(20, state.AnnotationSession.CanvasSize.Width);
    Assert.Equal(20, state.AnnotationSession.CanvasSize.Height);
    Assert.True(state.AnnotationSession.EditMask.FillContains(new Point(2, 2)));
    Assert.False(state.AnnotationSession.EditMask.FillContains(new Point(8, 8)));
    Assert.True(state.AnnotationSession.EditMask.FillContains(new Point(18, 18)));
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
