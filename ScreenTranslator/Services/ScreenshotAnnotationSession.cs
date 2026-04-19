using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;
using Geometry = System.Windows.Media.Geometry;
using Color = System.Windows.Media.Color;

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

  public IReadOnlyList<ScreenshotAnnotationOperation> Operations => _operations;

  public void SetActiveTool(ScreenshotAnnotationTool tool)
  {
    ActiveTool = tool;
  }

  public void CommitStroke(IReadOnlyList<Point> points, Color color, double strokeThickness)
  {
    var filteredPoints = points.Where(EditMask.FillContains).ToArray();
    if (filteredPoints.Length < 2)
    {
      return;
    }

    _operations.Add(new BrushStrokeAnnotationOperation(
      filteredPoints,
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

    _operations.Add(new RectangleAnnotationOperation(bounds, color, strokeThickness));
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
