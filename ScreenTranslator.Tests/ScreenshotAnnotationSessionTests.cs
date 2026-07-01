using System.Windows;
using System.Windows.Media;
using ScreenTranslator.Services;
using Xunit;
using Vector = System.Windows.Vector;

namespace ScreenTranslator.Tests;

public sealed class ScreenshotAnnotationSessionTests
{
  [Fact]
  public void New_Session_Uses_DeepSkyBlue_As_Default_Annotation_Color()
  {
    var session = new ScreenshotAnnotationSession(
      new Size(120, 80),
      Geometry.Parse("M0,0 L120,0 120,80 0,80 Z"));

    Assert.Equal(Colors.DeepSkyBlue, session.CurrentColor);
  }

  [Fact]
  public void New_Session_Uses_Default_Annotation_Size()
  {
    var session = new ScreenshotAnnotationSession(
      new Size(120, 80),
      Geometry.Parse("M0,0 L120,0 120,80 0,80 Z"));

    Assert.Equal(3, session.CurrentSize);
  }

  [Fact]
  public void SetAnnotationColor_Updates_Color_For_Brush_And_Rectangle_Annotations()
  {
    var session = new ScreenshotAnnotationSession(
      new Size(120, 80),
      Geometry.Parse("M0,0 L120,0 120,80 0,80 Z"));

    session.SetAnnotationColor(Colors.Orange);

    session.SetActiveTool(ScreenshotAnnotationTool.Brush);
    session.CommitStroke(
      [new Point(10, 10), new Point(30, 20)],
      session.CurrentColor,
      strokeThickness: 4);

    session.SetActiveTool(ScreenshotAnnotationTool.Rectangle);
    session.CommitRectangle(
      new Rect(40, 15, 50, 25),
      session.CurrentColor,
      strokeThickness: 3);

    var stroke = Assert.IsType<BrushStrokeAnnotationOperation>(session.Operations[0]);
    var rectangle = Assert.IsType<RectangleAnnotationOperation>(session.Operations[1]);
    Assert.Equal(Colors.Orange, stroke.Color);
    Assert.Equal(Colors.Orange, rectangle.Color);
  }

  [Fact]
  public void SetAnnotationSize_Clamps_Size_For_Future_Annotations()
  {
    var session = new ScreenshotAnnotationSession(
      new Size(120, 80),
      Geometry.Parse("M0,0 L120,0 120,80 0,80 Z"));

    session.SetAnnotationSize(20);
    Assert.Equal(10, session.CurrentSize);

    session.SetAnnotationSize(0);
    Assert.Equal(1, session.CurrentSize);
  }

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
  public void CommitStroke_Preserves_Separate_Segments_Across_Masked_Gaps()
  {
    var session = new ScreenshotAnnotationSession(
      new Size(100, 100),
      new EllipseGeometry(new Point(50, 50), 30, 30));

    session.SetActiveTool(ScreenshotAnnotationTool.Brush);
    session.CommitStroke(
      [new Point(45, 45), new Point(50, 50), new Point(5, 5), new Point(52, 52), new Point(55, 55)],
      Colors.Red,
      strokeThickness: 6);

    var operation = Assert.IsType<BrushStrokeAnnotationOperation>(Assert.Single(session.Operations));
    Assert.Equal(2, operation.Segments.Count);
    Assert.All(operation.Segments, segment => Assert.All(segment, point => Assert.True(session.EditMask.FillContains(point))));
    Assert.Equal(new Point(45, 45), operation.Segments[0][0]);
    Assert.Equal(new Point(50, 50), operation.Segments[0][1]);
    Assert.Equal(new Point(52, 52), operation.Segments[1][0]);
    Assert.Equal(new Point(55, 55), operation.Segments[1][1]);
  }

  [Fact]
  public void CommitRectangle_Carries_Edit_Mask_For_Future_Clipping()
  {
    var session = new ScreenshotAnnotationSession(
      new Size(100, 100),
      new EllipseGeometry(new Point(50, 50), 30, 30));

    session.SetActiveTool(ScreenshotAnnotationTool.Rectangle);
    session.CommitRectangle(
      new Rect(10, 10, 70, 70),
      Colors.DeepSkyBlue,
      strokeThickness: 3);

    var operation = Assert.IsType<RectangleAnnotationOperation>(Assert.Single(session.Operations));
    Assert.True(operation.ClipMask.FillContains(new Point(50, 50)));
    Assert.False(operation.ClipMask.FillContains(new Point(5, 5)));
  }

  [Fact]
  public void CommitArrow_Adds_Arrow_Operation_With_Clip_Mask()
  {
    var session = new ScreenshotAnnotationSession(
      new Size(100, 100),
      new EllipseGeometry(new Point(50, 50), 30, 30));

    session.SetActiveTool(ScreenshotAnnotationTool.Arrow);
    session.CommitArrow(
      new Point(35, 45),
      new Point(65, 55),
      Colors.DeepSkyBlue,
      strokeThickness: 3);

    var operation = Assert.IsType<ArrowAnnotationOperation>(Assert.Single(session.Operations));
    Assert.Equal(new Point(35, 45), operation.StartPoint);
    Assert.Equal(new Point(65, 55), operation.EndPoint);
    Assert.True(operation.ClipMask.FillContains(new Point(50, 50)));
  }

  [Fact]
  public void CommitText_Adds_Text_Operation_With_Color_Size_And_Clip_Mask()
  {
    var session = new ScreenshotAnnotationSession(
      new Size(100, 80),
      new EllipseGeometry(new Point(50, 40), 25, 20));

    session.SetActiveTool(ScreenshotAnnotationTool.Text);
    session.CommitText(
      new Point(35, 30),
      "Label",
      Colors.Yellow,
      fontSize: 18);

    var operation = Assert.IsType<TextAnnotationOperation>(Assert.Single(session.Operations));
    Assert.Equal(new Point(35, 30), operation.Location);
    Assert.Equal("Label", operation.Text);
    Assert.Equal(Colors.Yellow, operation.Color);
    Assert.Equal(18, operation.FontSize);
    Assert.True(operation.ClipMask.FillContains(new Point(50, 40)));
    Assert.False(operation.ClipMask.FillContains(new Point(5, 5)));
  }

  [Fact]
  public void CommitText_Ignores_Empty_Text()
  {
    var session = new ScreenshotAnnotationSession(
      new Size(100, 80),
      new RectangleGeometry(new Rect(0, 0, 100, 80)));

    session.SetActiveTool(ScreenshotAnnotationTool.Text);
    session.CommitText(
      new Point(10, 10),
      "   ",
      Colors.White,
      fontSize: 14);

    Assert.Empty(session.Operations);
  }

  [Fact]
  public void FindAnnotationAt_Returns_Topmost_Hit_Operation()
  {
    var session = new ScreenshotAnnotationSession(
      new Size(100, 80),
      new RectangleGeometry(new Rect(0, 0, 100, 80)));

    session.SetActiveTool(ScreenshotAnnotationTool.Rectangle);
    session.CommitRectangle(new Rect(10, 10, 40, 30), Colors.Red, strokeThickness: 3);
    session.SetActiveTool(ScreenshotAnnotationTool.Text);
    session.CommitText(new Point(20, 18), "Top", Colors.White, fontSize: 18);

    var index = session.FindAnnotationAt(new Point(24, 24), hitTolerance: 4);

    Assert.Equal(1, index);
  }

  [Fact]
  public void MoveAnnotation_Offsets_Rectangle_Arrow_Text_And_Brush_Points()
  {
    var session = new ScreenshotAnnotationSession(
      new Size(160, 120),
      new RectangleGeometry(new Rect(0, 0, 160, 120)));

    session.SetActiveTool(ScreenshotAnnotationTool.Rectangle);
    session.CommitRectangle(new Rect(10, 15, 30, 20), Colors.Red, strokeThickness: 3);
    session.SetActiveTool(ScreenshotAnnotationTool.Arrow);
    session.CommitArrow(new Point(50, 20), new Point(80, 40), Colors.Yellow, strokeThickness: 5);
    session.SetActiveTool(ScreenshotAnnotationTool.Text);
    session.CommitText(new Point(90, 25), "Move", Colors.White, fontSize: 18);
    session.SetActiveTool(ScreenshotAnnotationTool.Brush);
    session.CommitStroke(
      [new Point(20, 80), new Point(35, 90), new Point(50, 95)],
      Colors.DeepSkyBlue,
      strokeThickness: 4);

    Assert.True(session.MoveAnnotation(0, new Vector(5, -3)));
    Assert.True(session.MoveAnnotation(1, new Vector(5, -3)));
    Assert.True(session.MoveAnnotation(2, new Vector(5, -3)));
    Assert.True(session.MoveAnnotation(3, new Vector(5, -3)));

    var rectangle = Assert.IsType<RectangleAnnotationOperation>(session.Operations[0]);
    var arrow = Assert.IsType<ArrowAnnotationOperation>(session.Operations[1]);
    var text = Assert.IsType<TextAnnotationOperation>(session.Operations[2]);
    var brush = Assert.IsType<BrushStrokeAnnotationOperation>(session.Operations[3]);

    Assert.Equal(new Rect(15, 12, 30, 20), rectangle.Bounds);
    Assert.Equal(new Point(55, 17), arrow.StartPoint);
    Assert.Equal(new Point(85, 37), arrow.EndPoint);
    Assert.Equal(new Point(95, 22), text.Location);
    Assert.Equal(new Point(25, 77), brush.Segments[0][0]);
    Assert.Equal(new Point(55, 92), brush.Segments[0][2]);
  }

  [Fact]
  public void SetAnnotationColor_Updates_Only_Selected_Operation_Color()
  {
    var session = new ScreenshotAnnotationSession(
      new Size(120, 80),
      new RectangleGeometry(new Rect(0, 0, 120, 80)));

    session.SetAnnotationColor(Colors.Orange);
    session.SetActiveTool(ScreenshotAnnotationTool.Rectangle);
    session.CommitRectangle(new Rect(10, 10, 30, 20), session.CurrentColor, strokeThickness: 3);
    session.CommitRectangle(new Rect(50, 10, 30, 20), session.CurrentColor, strokeThickness: 3);

    Assert.True(session.SetAnnotationColor(0, Colors.Red));

    var first = Assert.IsType<RectangleAnnotationOperation>(session.Operations[0]);
    var second = Assert.IsType<RectangleAnnotationOperation>(session.Operations[1]);
    Assert.Equal(Colors.Red, first.Color);
    Assert.Equal(Colors.Orange, second.Color);
    Assert.Equal(Colors.Orange, session.CurrentColor);
  }

  [Fact]
  public void SetAnnotationSize_Updates_Only_Selected_Operation_Size()
  {
    var session = new ScreenshotAnnotationSession(
      new Size(120, 80),
      new RectangleGeometry(new Rect(0, 0, 120, 80)));

    session.SetActiveTool(ScreenshotAnnotationTool.Arrow);
    session.CommitArrow(new Point(10, 20), new Point(40, 20), Colors.Red, strokeThickness: 3);
    session.CommitArrow(new Point(10, 50), new Point(40, 50), Colors.Red, strokeThickness: 3);

    Assert.True(session.SetAnnotationSize(0, 8));

    var first = Assert.IsType<ArrowAnnotationOperation>(session.Operations[0]);
    var second = Assert.IsType<ArrowAnnotationOperation>(session.Operations[1]);
    Assert.Equal(8, first.StrokeThickness);
    Assert.Equal(3, second.StrokeThickness);
    Assert.Equal(3, session.CurrentSize);
  }

  [Fact]
  public void GetAnnotationColor_And_Size_Read_Selected_Text_Style()
  {
    var session = new ScreenshotAnnotationSession(
      new Size(120, 80),
      new RectangleGeometry(new Rect(0, 0, 120, 80)));

    session.SetActiveTool(ScreenshotAnnotationTool.Text);
    session.CommitText(new Point(10, 10), "Label", Colors.White, fontSize: 18);

    Assert.Equal(Colors.White, session.GetAnnotationColor(0));
    Assert.Equal(18, session.GetAnnotationSize(0));
    Assert.Null(session.GetAnnotationColor(10));
    Assert.Null(session.GetAnnotationSize(10));
  }
}
