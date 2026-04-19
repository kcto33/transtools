using System.Windows;
using System.Windows.Media;
using ScreenTranslator.Services;
using Xunit;

namespace ScreenTranslator.Tests;

public sealed class ScreenshotAnnotationSessionTests
{
  [Fact]
  public void Undo_Removes_Most_Recent_Operation()
  {
    var session = new ScreenshotAnnotationSession(
      new Size(120, 80),
      Geometry.Parse("M0,0 L120,0 120,80 0,80 Z"));

    session.SetActiveTool(ScreenshotAnnotationTool.Brush);
    session.CommitStroke(
      [new Point(10, 10), new Point(30, 20)],
      Colors.Red,
      strokeThickness: 4);

    session.SetActiveTool(ScreenshotAnnotationTool.Rectangle);
    session.CommitRectangle(
      new Rect(40, 15, 50, 25),
      Colors.DeepSkyBlue,
      strokeThickness: 3);

    Assert.Equal(2, session.Operations.Count);

    var removed = session.Undo();

    Assert.True(removed);
    Assert.Single(session.Operations);
    Assert.IsType<BrushStrokeAnnotationOperation>(session.Operations[0]);
  }

  [Fact]
  public void ClearAnnotations_Removes_All_Operations_And_Preserves_Edit_Mask()
  {
    var editMask = Geometry.Parse("M0,0 L100,0 100,50 0,50 Z");
    var session = new ScreenshotAnnotationSession(new Size(100, 50), editMask);

    session.SetActiveTool(ScreenshotAnnotationTool.Mosaic);
    session.CommitStroke(
      [new Point(5, 5), new Point(20, 10), new Point(35, 18)],
      Colors.Transparent,
      strokeThickness: 12);

    session.ClearAnnotations();

    Assert.Empty(session.Operations);
    Assert.True(session.EditMask.FillContains(new Point(10, 10)));
  }

  [Fact]
  public void CommitStroke_Filters_Points_Outside_Freeform_Edit_Mask()
  {
    var session = new ScreenshotAnnotationSession(
      new Size(100, 100),
      new EllipseGeometry(new Point(50, 50), 30, 30));

    session.SetActiveTool(ScreenshotAnnotationTool.Brush);
    session.CommitStroke(
      [new Point(5, 5), new Point(45, 45), new Point(50, 50), new Point(95, 95)],
      Colors.Red,
      strokeThickness: 6);

    var operation = Assert.IsType<BrushStrokeAnnotationOperation>(Assert.Single(session.Operations));
    Assert.All(operation.Points, point => Assert.True(session.EditMask.FillContains(point)));
  }
}
