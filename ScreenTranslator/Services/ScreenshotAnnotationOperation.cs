using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Geometry = System.Windows.Media.Geometry;
using Color = System.Windows.Media.Color;

namespace ScreenTranslator.Services;

public abstract record ScreenshotAnnotationOperation;

public sealed record BrushStrokeAnnotationOperation(
  IReadOnlyList<IReadOnlyList<Point>> Segments,
  Color Color,
  double StrokeThickness,
  bool IsMosaic) : ScreenshotAnnotationOperation;

public sealed record RectangleAnnotationOperation(
  Rect Bounds,
  Geometry ClipMask,
  Color Color,
  double StrokeThickness) : ScreenshotAnnotationOperation;

public sealed record ArrowAnnotationOperation(
  Point StartPoint,
  Point EndPoint,
  Geometry ClipMask,
  Color Color,
  double StrokeThickness) : ScreenshotAnnotationOperation;

public sealed record TextAnnotationOperation(
  Point Location,
  Geometry ClipMask,
  string Text,
  Color Color,
  double FontSize) : ScreenshotAnnotationOperation;
