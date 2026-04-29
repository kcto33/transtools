using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;
using Geometry = System.Windows.Media.Geometry;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

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
}
