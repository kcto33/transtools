using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace ScreenTranslator.Services;

public static class ScreenshotAnnotationRenderer
{
  public static BitmapSource RenderComposite(BitmapSource baseImage, ScreenshotAnnotationSession session)
  {
    var visual = new DrawingVisual();
    using (var context = visual.RenderOpen())
    {
      context.DrawImage(baseImage, new Rect(0, 0, session.CanvasSize.Width, session.CanvasSize.Height));

      foreach (var operation in session.Operations)
      {
        switch (operation)
        {
          case RectangleAnnotationOperation rectangle:
            DrawRectangle(context, rectangle);
            break;
          case BrushStrokeAnnotationOperation brush when brush.IsMosaic:
            DrawMosaic(context, baseImage, rectangleMask: session.EditMask, brush);
            break;
          case BrushStrokeAnnotationOperation brush:
            DrawStroke(context, session.EditMask, brush);
            break;
        }
      }
    }

    var result = new RenderTargetBitmap(
      baseImage.PixelWidth,
      baseImage.PixelHeight,
      baseImage.DpiX,
      baseImage.DpiY,
      PixelFormats.Pbgra32);

    result.Render(visual);
    result.Freeze();
    return result;
  }

  private static void DrawRectangle(DrawingContext context, RectangleAnnotationOperation rectangle)
  {
    context.PushClip(rectangle.ClipMask);
    context.DrawRectangle(
      null,
      new Pen(new SolidColorBrush(rectangle.Color), rectangle.StrokeThickness),
      rectangle.Bounds);
    context.Pop();
  }

  private static void DrawStroke(DrawingContext context, Geometry editMask, BrushStrokeAnnotationOperation brush)
  {
    context.PushClip(editMask);

    foreach (var segment in brush.Segments)
    {
      context.DrawGeometry(
        null,
        new Pen(new SolidColorBrush(brush.Color), brush.StrokeThickness),
        BuildStrokeGeometry(segment));
    }

    context.Pop();
  }

  private static void DrawMosaic(
    DrawingContext context,
    BitmapSource baseImage,
    Geometry rectangleMask,
    BrushStrokeAnnotationOperation brush)
  {
    var strokeGeometry = CombineSegments(brush.Segments)
      .GetWidenedPathGeometry(new Pen(Brushes.Black, brush.StrokeThickness));
    var clip = Geometry.Combine(rectangleMask, strokeGeometry, GeometryCombineMode.Intersect, null);

    context.PushClip(clip);
    context.DrawImage(CreatePixelatedImage(baseImage, blockSize: 8), new Rect(0, 0, baseImage.Width, baseImage.Height));
    context.Pop();
  }

  private static PathGeometry BuildStrokeGeometry(IReadOnlyList<Point> points)
  {
    var figure = new PathFigure
    {
      StartPoint = points[0],
      IsClosed = false,
      IsFilled = false
    };

    foreach (var point in points.Skip(1))
    {
      figure.Segments.Add(new LineSegment(point, true));
    }

    return new PathGeometry([figure]);
  }

  private static Geometry CombineSegments(IReadOnlyList<IReadOnlyList<Point>> segments)
  {
    if (segments.Count == 0)
    {
      return Geometry.Empty;
    }

    if (segments.Count == 1)
    {
      return BuildStrokeGeometry(segments[0]);
    }

    var group = new GeometryGroup();
    foreach (var segment in segments)
    {
      group.Children.Add(BuildStrokeGeometry(segment));
    }

    group.Freeze();
    return group;
  }

  private static BitmapSource CreatePixelatedImage(BitmapSource source, int blockSize)
  {
    var downscaled = new TransformedBitmap(source, new ScaleTransform(1.0 / blockSize, 1.0 / blockSize));
    downscaled.Freeze();

    var upscaled = new TransformedBitmap(downscaled, new ScaleTransform(blockSize, blockSize));
    upscaled.Freeze();
    return upscaled;
  }
}
