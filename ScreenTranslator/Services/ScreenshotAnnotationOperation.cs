using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Color = System.Windows.Media.Color;

namespace ScreenTranslator.Services;

public abstract record ScreenshotAnnotationOperation;

public sealed record BrushStrokeAnnotationOperation(
  IReadOnlyList<Point> Points,
  Color Color,
  double StrokeThickness,
  bool IsMosaic) : ScreenshotAnnotationOperation;

public sealed record RectangleAnnotationOperation(
  Rect Bounds,
  Color Color,
  double StrokeThickness) : ScreenshotAnnotationOperation;
