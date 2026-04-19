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

  [Fact]
  public void CreateEditModeState_Normalizes_CanvasSize_To_Cropped_Integer_Pixels()
  {
    var geometry = new PathGeometry(
      new[]
      {
        new PathFigure(
          new Point(10.2, 12.4),
          new PathSegment[]
          {
            new LineSegment(new Point(20.8, 12.4), true),
            new LineSegment(new Point(20.8, 18.8), true),
            new LineSegment(new Point(10.2, 18.8), true)
          },
          true)
      });
    var boundingRect = new Rect(10.2, 12.4, 10.6, 6.4);

    var state = FreeformScreenshotWindow.CreateEditModeState(geometry, boundingRect, dpiScaleX: 1.5, dpiScaleY: 1.5);

    Assert.Equal(15, state.AnnotationSession!.CanvasSize.Width);
    Assert.Equal(9, state.AnnotationSession.CanvasSize.Height);
  }

  [Fact]
  public void RenderFreeformSelection_Uses_PixelSized_Output_For_Non100Dpi_Crops()
  {
    var baseImage = CreateHorizontalGradientImage(15, 9);
    var geometry = new PathGeometry(
      new[]
      {
        new PathFigure(
          new Point(10.2, 12.4),
          new PathSegment[]
          {
            new LineSegment(new Point(20.8, 12.4), true),
            new LineSegment(new Point(20.8, 18.8), true),
            new LineSegment(new Point(10.2, 18.8), true)
          },
          true)
      });
    var boundingRect = new Rect(10.2, 12.4, 10.6, 6.4);
    var pixelBounds = FreeformScreenshotWindow.GetPixelBounds(boundingRect, dpiScaleX: 1.5, dpiScaleY: 1.5);

    var result = FreeformScreenshotWindow.RenderFreeformSelection(baseImage, geometry, boundingRect, pixelBounds);

    Assert.Equal(15, result.PixelWidth);
    Assert.Equal(9, result.PixelHeight);
    Assert.Equal(GetPixel(baseImage, 14, 4), GetPixel(result, 14, 4));
  }

  [Fact]
  public void ResetEditModeState_Clears_Freeform_EditMode_And_Annotations()
  {
    var state = FreeformScreenshotWindow.CreateEditModeState(
      new PathGeometry(
        new[]
        {
          new PathFigure(
            new Point(5, 5),
            new PathSegment[]
            {
              new LineSegment(new Point(25, 5), true),
              new LineSegment(new Point(25, 25), true),
              new LineSegment(new Point(5, 25), true)
            },
            true)
        }),
      new Rect(5, 5, 20, 20),
      dpiScaleX: 1,
      dpiScaleY: 1);
    state.AnnotationSession!.CommitRectangle(new Rect(2, 2, 5, 5), Colors.Red, 2);

    var resetState = FreeformScreenshotWindow.ResetEditModeState();

    Assert.False(resetState.IsEditMode);
    Assert.False(resetState.IsAnnotating);
    Assert.Null(resetState.AnnotationSession);
  }

  [Fact]
  public void IsWithinEditableMask_Rejects_WindowPoints_Outside_Translated_Freeform_Region()
  {
    var geometry = new PathGeometry(
      new[]
      {
        new PathFigure(
          new Point(20, 30),
          new PathSegment[]
          {
            new LineSegment(new Point(40, 30), true),
            new LineSegment(new Point(40, 50), true),
            new LineSegment(new Point(20, 50), true)
          },
          true)
      });
    var boundingRect = new Rect(20, 30, 20, 20);
    var state = FreeformScreenshotWindow.CreateEditModeState(geometry, boundingRect, dpiScaleX: 1, dpiScaleY: 1);

    var inside = FreeformScreenshotWindow.IsWithinEditableMask(
      state.AnnotationSession,
      boundingRect,
      new Point(25, 35));
    var outside = FreeformScreenshotWindow.IsWithinEditableMask(
      state.AnnotationSession,
      boundingRect,
      new Point(15, 35));

    Assert.True(inside);
    Assert.False(outside);
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

  private static WriteableBitmap CreateHorizontalGradientImage(int width, int height)
  {
    var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
    var pixels = new byte[width * height * 4];

    for (var y = 0; y < height; y++)
    {
      for (var x = 0; x < width; x++)
      {
        var offset = ((y * width) + x) * 4;
        var intensity = (byte)(x * 255 / Math.Max(1, width - 1));
        pixels[offset + 0] = intensity;
        pixels[offset + 1] = intensity;
        pixels[offset + 2] = intensity;
        pixels[offset + 3] = 255;
      }
    }

    bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
    return bitmap;
  }
}
