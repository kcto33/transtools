using System.Globalization;
using System.Windows;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;
using Vector = System.Windows.Vector;
using Brushes = System.Windows.Media.Brushes;
using Geometry = System.Windows.Media.Geometry;
using LineSegment = System.Windows.Media.LineSegment;
using PathFigure = System.Windows.Media.PathFigure;
using PathGeometry = System.Windows.Media.PathGeometry;
using Pen = System.Windows.Media.Pen;
using ToleranceType = System.Windows.Media.ToleranceType;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using FormattedText = System.Windows.Media.FormattedText;
using Typeface = System.Windows.Media.Typeface;
using WpfFlowDirection = System.Windows.FlowDirection;

namespace ScreenTranslator.Services;

public sealed class ScreenshotAnnotationSession
{
  private readonly List<ScreenshotAnnotationOperation> _operations = [];

  public ScreenshotAnnotationSession(Size canvasSize, Geometry editMask)
  {
    CanvasSize = canvasSize;
    EditMask = editMask.Clone();
    EditMask.Freeze();
  }

  public Size CanvasSize { get; }

  public Geometry EditMask { get; }

  public ScreenshotAnnotationTool ActiveTool { get; private set; }

  public Color CurrentColor { get; private set; } = Colors.DeepSkyBlue;

  public double CurrentSize { get; private set; } = 3;

  public IReadOnlyList<ScreenshotAnnotationOperation> Operations => _operations.AsReadOnly();

  public void SetActiveTool(ScreenshotAnnotationTool tool)
  {
    ActiveTool = tool;
  }

  public void SetAnnotationColor(Color color)
  {
    CurrentColor = color;
  }

  public void SetAnnotationSize(double size)
  {
    CurrentSize = Math.Clamp(size, 1, 10);
  }

  public void CommitStroke(IReadOnlyList<Point> points, Color color, double strokeThickness)
  {
    List<IReadOnlyList<Point>>? segments = null;
    List<Point>? currentSegment = null;

    foreach (var point in points)
    {
      if (EditMask.FillContains(point))
      {
        currentSegment ??= [];
        currentSegment.Add(point);
        continue;
      }

      if (currentSegment is not null && currentSegment.Count >= 2)
      {
        segments ??= [];
        segments.Add(currentSegment.ToArray());
      }

      currentSegment = null;
    }

    if (currentSegment is not null && currentSegment.Count >= 2)
    {
      segments ??= [];
      segments.Add(currentSegment.ToArray());
    }

    if (segments is null)
    {
      return;
    }

    _operations.Add(new BrushStrokeAnnotationOperation(
      segments,
      color,
      strokeThickness,
      ActiveTool == ScreenshotAnnotationTool.Mosaic));
  }

  public void CommitRectangle(Rect bounds, Color color, double strokeThickness)
  {
    if (bounds.Width <= 0 || bounds.Height <= 0)
    {
      return;
    }

    _operations.Add(new RectangleAnnotationOperation(bounds, EditMask, color, strokeThickness));
  }

  public void CommitArrow(Point startPoint, Point endPoint, Color color, double strokeThickness)
  {
    if ((endPoint - startPoint).Length < 1)
    {
      return;
    }

    _operations.Add(new ArrowAnnotationOperation(startPoint, endPoint, EditMask, color, strokeThickness));
  }

  public void CommitText(Point location, string text, Color color, double fontSize)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return;
    }

    _operations.Add(new TextAnnotationOperation(
      location,
      EditMask,
      text.Trim(),
      color,
      Math.Max(1, fontSize)));
  }

  internal int? FindAnnotationAt(Point point, double hitTolerance)
  {
    var safeTolerance = Math.Max(1, hitTolerance);
    for (var index = _operations.Count - 1; index >= 0; index--)
    {
      if (ContainsPoint(_operations[index], point, safeTolerance))
      {
        return index;
      }
    }

    return null;
  }

  internal bool MoveAnnotation(int operationIndex, Vector delta)
  {
    if (operationIndex < 0 || operationIndex >= _operations.Count)
    {
      return false;
    }

    _operations[operationIndex] = MoveOperation(_operations[operationIndex], delta);
    return true;
  }

  internal Color? GetAnnotationColor(int operationIndex)
  {
    if (operationIndex < 0 || operationIndex >= _operations.Count)
    {
      return null;
    }

    return _operations[operationIndex] switch
    {
      BrushStrokeAnnotationOperation brush => brush.Color,
      RectangleAnnotationOperation rectangle => rectangle.Color,
      ArrowAnnotationOperation arrow => arrow.Color,
      TextAnnotationOperation text => text.Color,
      _ => null,
    };
  }

  internal double? GetAnnotationSize(int operationIndex)
  {
    if (operationIndex < 0 || operationIndex >= _operations.Count)
    {
      return null;
    }

    return _operations[operationIndex] switch
    {
      BrushStrokeAnnotationOperation brush => brush.StrokeThickness,
      RectangleAnnotationOperation rectangle => rectangle.StrokeThickness,
      ArrowAnnotationOperation arrow => arrow.StrokeThickness,
      TextAnnotationOperation text => text.FontSize,
      _ => null,
    };
  }

  internal bool SetAnnotationColor(int operationIndex, Color color)
  {
    if (operationIndex < 0 || operationIndex >= _operations.Count)
    {
      return false;
    }

    _operations[operationIndex] = SetOperationColor(_operations[operationIndex], color);
    return true;
  }

  internal bool SetAnnotationSize(int operationIndex, double size)
  {
    if (operationIndex < 0 || operationIndex >= _operations.Count)
    {
      return false;
    }

    _operations[operationIndex] = SetOperationSize(_operations[operationIndex], Math.Max(1, size));
    return true;
  }

  public bool Undo()
  {
    if (_operations.Count == 0)
    {
      return false;
    }

    _operations.RemoveAt(_operations.Count - 1);
    return true;
  }

  public void ClearAnnotations()
  {
    _operations.Clear();
  }

  private bool ContainsPoint(ScreenshotAnnotationOperation operation, Point point, double hitTolerance)
  {
    return operation switch
    {
      BrushStrokeAnnotationOperation brush => EditMask.FillContains(point, hitTolerance, ToleranceType.Absolute) &&
                                              BrushContainsPoint(brush, point, hitTolerance),
      RectangleAnnotationOperation rectangle => rectangle.ClipMask.FillContains(point, hitTolerance, ToleranceType.Absolute) &&
                                                RectangleContainsPoint(rectangle, point, hitTolerance),
      ArrowAnnotationOperation arrow => arrow.ClipMask.FillContains(point, hitTolerance, ToleranceType.Absolute) &&
                                        ArrowContainsPoint(arrow, point, hitTolerance),
      TextAnnotationOperation text => text.ClipMask.FillContains(point, hitTolerance, ToleranceType.Absolute) &&
                                      TextContainsPoint(text, point, hitTolerance),
      _ => false,
    };
  }

  private static bool BrushContainsPoint(BrushStrokeAnnotationOperation brush, Point point, double hitTolerance)
  {
    var pen = new Pen(Brushes.Black, Math.Max(brush.StrokeThickness, hitTolerance * 2));
    foreach (var segment in brush.Segments)
    {
      if (segment.Count >= 2 &&
          BuildStrokeGeometry(segment).StrokeContains(pen, point, 0.5, ToleranceType.Absolute))
      {
        return true;
      }
    }

    return false;
  }

  private static bool RectangleContainsPoint(RectangleAnnotationOperation rectangle, Point point, double hitTolerance)
  {
    var bounds = rectangle.Bounds;
    bounds.Inflate(hitTolerance, hitTolerance);
    return bounds.Contains(point);
  }

  private static bool ArrowContainsPoint(ArrowAnnotationOperation arrow, Point point, double hitTolerance)
  {
    var geometry = ScreenshotAnnotationRenderer.BuildFilledArrowGeometry(
      arrow.StartPoint,
      arrow.EndPoint,
      arrow.StrokeThickness);
    return geometry.FillContains(point, hitTolerance, ToleranceType.Absolute);
  }

  private static bool TextContainsPoint(TextAnnotationOperation text, Point point, double hitTolerance)
  {
    var formattedText = new FormattedText(
      text.Text,
      CultureInfo.CurrentUICulture,
      WpfFlowDirection.LeftToRight,
      new Typeface("Segoe UI"),
      text.FontSize,
      Brushes.Black,
      pixelsPerDip: 1.0);

    var bounds = new Rect(
      text.Location,
      new Size(
        Math.Max(1, formattedText.WidthIncludingTrailingWhitespace),
        Math.Max(1, formattedText.Height)));
    bounds.Inflate(hitTolerance, hitTolerance);
    return bounds.Contains(point);
  }

  private static ScreenshotAnnotationOperation MoveOperation(
    ScreenshotAnnotationOperation operation,
    Vector delta)
  {
    return operation switch
    {
      BrushStrokeAnnotationOperation brush => brush with
      {
        Segments = brush.Segments
          .Select(segment => (IReadOnlyList<Point>)segment.Select(point => point + delta).ToArray())
          .ToArray()
      },
      RectangleAnnotationOperation rectangle => rectangle with
      {
        Bounds = new Rect(rectangle.Bounds.Location + delta, rectangle.Bounds.Size)
      },
      ArrowAnnotationOperation arrow => arrow with
      {
        StartPoint = arrow.StartPoint + delta,
        EndPoint = arrow.EndPoint + delta
      },
      TextAnnotationOperation text => text with
      {
        Location = text.Location + delta
      },
      _ => operation,
    };
  }

  private static ScreenshotAnnotationOperation SetOperationColor(
    ScreenshotAnnotationOperation operation,
    Color color)
  {
    return operation switch
    {
      BrushStrokeAnnotationOperation brush => brush with { Color = color },
      RectangleAnnotationOperation rectangle => rectangle with { Color = color },
      ArrowAnnotationOperation arrow => arrow with { Color = color },
      TextAnnotationOperation text => text with { Color = color },
      _ => operation,
    };
  }

  private static ScreenshotAnnotationOperation SetOperationSize(
    ScreenshotAnnotationOperation operation,
    double size)
  {
    return operation switch
    {
      BrushStrokeAnnotationOperation brush => brush with { StrokeThickness = size },
      RectangleAnnotationOperation rectangle => rectangle with { StrokeThickness = size },
      ArrowAnnotationOperation arrow => arrow with { StrokeThickness = size },
      TextAnnotationOperation text => text with { FontSize = size },
      _ => operation,
    };
  }

  private static PathGeometry BuildStrokeGeometry(IReadOnlyList<Point> points)
  {
    if (points.Count == 0)
    {
      return Geometry.Empty.GetFlattenedPathGeometry();
    }

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
}
