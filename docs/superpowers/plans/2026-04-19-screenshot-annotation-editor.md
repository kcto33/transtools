# Screenshot Annotation Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add post-selection screenshot editing with brush, rectangle outline, mosaic brush, undo, and clear-annotations for both rectangular and freeform screenshot flows.

**Architecture:** Keep screenshot selection inside the existing windows, then hand off to a shared annotation session and shared composition renderer once the selection is complete. Both `ScreenshotOverlayWindow` and `FreeformScreenshotWindow` will render and export from the same annotation model so preview, copy, save, and pin all use the same composited output.

**Tech Stack:** .NET 8, WPF, System.Drawing, xUnit

---

### Task 1: Add Shared Annotation Session And Operation Tests

**Files:**
- Create: `F:/yys/transtools/pot/ScreenTranslator/Services/ScreenshotAnnotationTool.cs`
- Create: `F:/yys/transtools/pot/ScreenTranslator/Services/ScreenshotAnnotationSession.cs`
- Create: `F:/yys/transtools/pot/ScreenTranslator/Services/ScreenshotAnnotationOperation.cs`
- Create: `F:/yys/transtools/pot/ScreenTranslator.Tests/ScreenshotAnnotationSessionTests.cs`
- Test: `F:/yys/transtools/pot/ScreenTranslator.Tests/ScreenshotAnnotationSessionTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FullyQualifiedName~ScreenshotAnnotationSessionTests`
Expected: FAIL with missing `ScreenshotAnnotationSession`, `ScreenshotAnnotationTool`, and operation types.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Windows;
using System.Windows.Media;

namespace ScreenTranslator.Services;

public enum ScreenshotAnnotationTool
{
  None,
  Brush,
  Rectangle,
  Mosaic
}

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
    var filtered = points.Where(EditMask.FillContains).ToArray();
    if (filtered.Length < 2)
    {
      return;
    }

    _operations.Add(new BrushStrokeAnnotationOperation(
      filtered,
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FullyQualifiedName~ScreenshotAnnotationSessionTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add ScreenTranslator/Services/ScreenshotAnnotationTool.cs ScreenTranslator/Services/ScreenshotAnnotationOperation.cs ScreenTranslator/Services/ScreenshotAnnotationSession.cs ScreenTranslator.Tests/ScreenshotAnnotationSessionTests.cs
git commit -m "test: cover screenshot annotation session behavior"
```

### Task 2: Add Shared Annotation Renderer And Composition Tests

**Files:**
- Create: `F:/yys/transtools/pot/ScreenTranslator/Services/ScreenshotAnnotationRenderer.cs`
- Create: `F:/yys/transtools/pot/ScreenTranslator.Tests/ScreenshotAnnotationRendererTests.cs`
- Test: `F:/yys/transtools/pot/ScreenTranslator.Tests/ScreenshotAnnotationRendererTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenTranslator.Services;
using Xunit;

namespace ScreenTranslator.Tests;

public sealed class ScreenshotAnnotationRendererTests
{
  [Fact]
  public void RenderComposite_Draws_Rectangle_Stroke_Over_Base_Image()
  {
    var baseImage = new WriteableBitmap(40, 40, 96, 96, PixelFormats.Bgra32, null);
    var session = new ScreenshotAnnotationSession(
      new Size(40, 40),
      Geometry.Parse("M0,0 L40,0 40,40 0,40 Z"));

    session.SetActiveTool(ScreenshotAnnotationTool.Rectangle);
    session.CommitRectangle(new Rect(5, 5, 20, 10), Colors.Red, 4);

    var result = ScreenshotAnnotationRenderer.RenderComposite(baseImage, session);

    Assert.Equal(40, result.PixelWidth);
    Assert.Equal(40, result.PixelHeight);
    Assert.NotSame(baseImage, result);
  }

  [Fact]
  public void RenderComposite_Applies_Mosaic_To_Selected_Region_Only()
  {
    var baseImage = new WriteableBitmap(32, 32, 96, 96, PixelFormats.Bgra32, null);
    var session = new ScreenshotAnnotationSession(
      new Size(32, 32),
      Geometry.Parse("M0,0 L32,0 32,32 0,32 Z"));

    session.SetActiveTool(ScreenshotAnnotationTool.Mosaic);
    session.CommitStroke(
      [new Point(4, 4), new Point(8, 8), new Point(12, 12)],
      Colors.Transparent,
      strokeThickness: 10);

    var result = ScreenshotAnnotationRenderer.RenderComposite(baseImage, session);

    Assert.Equal(32, result.PixelWidth);
    Assert.Equal(32, result.PixelHeight);
  }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FullyQualifiedName~ScreenshotAnnotationRendererTests`
Expected: FAIL with missing `ScreenshotAnnotationRenderer`.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenTranslator.Services;

public static class ScreenshotAnnotationRenderer
{
  public static BitmapSource RenderComposite(BitmapSource baseImage, ScreenshotAnnotationSession session)
  {
    var visual = new DrawingVisual();
    using var context = visual.RenderOpen();

    context.DrawImage(baseImage, new Rect(0, 0, session.CanvasSize.Width, session.CanvasSize.Height));

    foreach (var operation in session.Operations)
    {
      switch (operation)
      {
        case RectangleAnnotationOperation rectangle:
          DrawRectangle(context, rectangle);
          break;
        case BrushStrokeAnnotationOperation brush when brush.IsMosaic:
          DrawMosaic(context, baseImage, session.EditMask, brush);
          break;
        case BrushStrokeAnnotationOperation brush:
          DrawStroke(context, brush);
          break;
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
    context.DrawRectangle(
      null,
      new Pen(new SolidColorBrush(rectangle.Color), rectangle.StrokeThickness),
      rectangle.Bounds);
  }

  private static void DrawStroke(DrawingContext context, BrushStrokeAnnotationOperation brush)
  {
    var geometry = BuildStrokeGeometry(brush.Points);
    context.DrawGeometry(null, new Pen(new SolidColorBrush(brush.Color), brush.StrokeThickness), geometry);
  }

  private static void DrawMosaic(
    DrawingContext context,
    BitmapSource baseImage,
    Geometry editMask,
    BrushStrokeAnnotationOperation brush)
  {
    var strokeGeometry = BuildStrokeGeometry(brush.Points).GetWidenedPathGeometry(new Pen(Brushes.Black, brush.StrokeThickness));
    var clip = Geometry.Combine(editMask, strokeGeometry, GeometryCombineMode.Intersect, null);
    context.PushClip(clip);
    context.DrawImage(CreatePixelatedImage(baseImage, blockSize: 8), new Rect(0, 0, baseImage.Width, baseImage.Height));
    context.Pop();
  }

  private static PathGeometry BuildStrokeGeometry(IReadOnlyList<Point> points)
  {
    var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
    foreach (var point in points.Skip(1))
    {
      figure.Segments.Add(new LineSegment(point, true));
    }

    return new PathGeometry([figure]);
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FullyQualifiedName~ScreenshotAnnotationRendererTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add ScreenTranslator/Services/ScreenshotAnnotationRenderer.cs ScreenTranslator.Tests/ScreenshotAnnotationRendererTests.cs
git commit -m "feat: add screenshot annotation composition renderer"
```

### Task 3: Integrate Edit Mode Into Rectangular Screenshot Window

**Files:**
- Modify: `F:/yys/transtools/pot/ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml`
- Modify: `F:/yys/transtools/pot/ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml.cs`
- Modify: `F:/yys/transtools/pot/ScreenTranslator/Resources/Strings.en.xaml`
- Modify: `F:/yys/transtools/pot/ScreenTranslator/Resources/Strings.zh-CN.xaml`
- Modify: `F:/yys/transtools/pot/ScreenTranslator.Tests/ScreenshotOverlayWindowTests.cs`
- Test: `F:/yys/transtools/pot/ScreenTranslator.Tests/ScreenshotOverlayWindowTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenTranslator.Services;
using ScreenTranslator.Windows;
using Xunit;

namespace ScreenTranslator.Tests;

public sealed partial class ScreenshotOverlayWindowTests
{
  [Fact]
  public void GetOutputImage_Returns_Composited_Image_When_Annotation_Session_Exists()
  {
    var baseImage = new WriteableBitmap(20, 20, 96, 96, PixelFormats.Bgra32, null);
    var session = new ScreenshotAnnotationSession(
      new System.Windows.Size(20, 20),
      Geometry.Parse("M0,0 L20,0 20,20 0,20 Z"));

    var output = ScreenshotOverlayWindow.GetOutputImage(baseImage, session);

    Assert.NotNull(output);
    Assert.Equal(20, output.PixelWidth);
  }

  [Fact]
  public void GetOutputImage_Returns_Base_Image_When_Annotation_Session_Is_Null()
  {
    var baseImage = new WriteableBitmap(20, 20, 96, 96, PixelFormats.Bgra32, null);

    var output = ScreenshotOverlayWindow.GetOutputImage(baseImage, null);

    Assert.Same(baseImage, output);
  }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FullyQualifiedName~ScreenshotOverlayWindowTests`
Expected: FAIL with missing `GetOutputImage`.

- [ ] **Step 3: Write minimal implementation**

```csharp
internal static BitmapSource? GetOutputImage(BitmapSource? baseImage, ScreenshotAnnotationSession? session)
{
  if (baseImage is null)
  {
    return null;
  }

  if (session is null)
  {
    return baseImage;
  }

  return ScreenshotAnnotationRenderer.RenderComposite(baseImage, session);
}
```

- [ ] **Step 4: Add edit-mode UI and event routing**

```xml
<Canvas x:Name="AnnotationCanvas" IsHitTestVisible="False">
  <Image x:Name="SelectionPreviewImage" Visibility="Collapsed" />
  <Path x:Name="AnnotationPreviewPath"
        Stroke="#FFFF5252"
        StrokeThickness="3"
        Fill="Transparent"
        Visibility="Collapsed" />
</Canvas>

<Button x:Name="BtnBrush" Content="{DynamicResource Screenshot_Brush}" ... />
<Button x:Name="BtnRectangle" Content="{DynamicResource Screenshot_Rectangle}" ... />
<Button x:Name="BtnMosaic" Content="{DynamicResource Screenshot_Mosaic}" ... />
<Button x:Name="BtnUndo" Content="{DynamicResource Screenshot_Undo}" ... />
<Button x:Name="BtnClearAnnotations" Content="{DynamicResource Screenshot_ClearAnnotations}" ... />
```

```csharp
private ScreenshotAnnotationSession? _annotationSession;
private BitmapSource? _selectedImage;
private bool _isEditMode;
private readonly List<Point> _pendingStrokePoints = [];
private Point? _pendingRectangleStart;

private void EnterEditMode()
{
  _selectedImage = CropSelection();
  if (_selectedImage is null)
  {
    return;
  }

  _annotationSession = new ScreenshotAnnotationSession(
    new System.Windows.Size(_selectedImage.Width, _selectedImage.Height),
    new RectangleGeometry(new Rect(0, 0, _selectedImage.Width, _selectedImage.Height)));

  _isEditMode = true;
  SelectionPreviewImage.Source = _selectedImage;
  SelectionPreviewImage.Visibility = Visibility.Visible;
  Cursor = Cursors.Arrow;
}

private void RefreshCompositePreview()
{
  if (_selectedImage is null)
  {
    return;
  }

  SelectionPreviewImage.Source = GetOutputImage(_selectedImage, _annotationSession);
}
```

- [ ] **Step 5: Update output actions to use composited image**

```csharp
private void PinSelection()
{
  var output = GetOutputImage(_selectedImage ?? CropSelection(), _annotationSession);
  if (output == null)
  {
    return;
  }

  if (_settings.ScreenshotAutoCopy)
  {
    CopyToClipboard(output);
  }

  if (_settings.ScreenshotAutoSave)
  {
    SaveToFile(output);
  }

  _onPinRequested(output, _selectedRegion, _dpiScaleX, _dpiScaleY);
  Close();
}
```

- [ ] **Step 6: Run focused tests**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FullyQualifiedName~ScreenshotOverlayWindowTests`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml.cs ScreenTranslator/Resources/Strings.en.xaml ScreenTranslator/Resources/Strings.zh-CN.xaml ScreenTranslator.Tests/ScreenshotOverlayWindowTests.cs
git commit -m "feat: add annotation edit mode to rectangular screenshots"
```

### Task 4: Integrate Shared Edit Mode Into Freeform Screenshot Window

**Files:**
- Modify: `F:/yys/transtools/pot/ScreenTranslator/Windows/FreeformScreenshotWindow.xaml`
- Modify: `F:/yys/transtools/pot/ScreenTranslator/Windows/FreeformScreenshotWindow.xaml.cs`
- Create: `F:/yys/transtools/pot/ScreenTranslator.Tests/FreeformScreenshotWindowTests.cs`
- Test: `F:/yys/transtools/pot/ScreenTranslator.Tests/FreeformScreenshotWindowTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenTranslator.Services;
using ScreenTranslator.Windows;
using Xunit;

namespace ScreenTranslator.Tests;

public sealed class FreeformScreenshotWindowTests
{
  [Fact]
  public void GetOutputImage_Returns_Composited_Image_When_Annotation_Session_Exists()
  {
    var baseImage = new WriteableBitmap(30, 30, 96, 96, PixelFormats.Pbgra32, null);
    var session = new ScreenshotAnnotationSession(
      new System.Windows.Size(30, 30),
      new EllipseGeometry(new System.Windows.Point(15, 15), 10, 10));

    var output = FreeformScreenshotWindow.GetOutputImage(baseImage, session);

    Assert.NotNull(output);
    Assert.Equal(30, output.PixelWidth);
  }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FullyQualifiedName~FreeformScreenshotWindowTests`
Expected: FAIL with missing `FreeformScreenshotWindowTests` target or missing `GetOutputImage`.

- [ ] **Step 3: Write minimal implementation**

```csharp
internal static BitmapSource? GetOutputImage(BitmapSource? baseImage, ScreenshotAnnotationSession? session)
{
  if (baseImage is null)
  {
    return null;
  }

  return session is null
    ? baseImage
    : ScreenshotAnnotationRenderer.RenderComposite(baseImage, session);
}
```

- [ ] **Step 4: Add annotation session creation and redraw reset**

```csharp
private ScreenshotAnnotationSession? _annotationSession;
private BitmapSource? _selectedImage;
private bool _isEditMode;

private void EnterEditMode()
{
  _selectedImage = CropFreeformSelection();
  if (_selectedImage is null || _completedGeometry is null)
  {
    return;
  }

  var localMask = _completedGeometry.Clone();
  localMask.Transform = new TranslateTransform(-_boundingRect.X, -_boundingRect.Y);

  _annotationSession = new ScreenshotAnnotationSession(
    new System.Windows.Size(_selectedImage.Width, _selectedImage.Height),
    localMask);

  _isEditMode = true;
  SelectionPreviewImage.Source = _selectedImage;
  SelectionPreviewImage.Visibility = Visibility.Visible;
}

private void ResetSelection()
{
  _annotationSession = null;
  _selectedImage = null;
  _isEditMode = false;
  SelectionPreviewImage.Source = null;
  SelectionPreviewImage.Visibility = Visibility.Collapsed;
  // existing freeform reset logic remains here
}
```

- [ ] **Step 5: Route copy/save/pin through composited freeform output**

```csharp
private void CopySelection()
{
  var output = GetOutputImage(_selectedImage ?? CropFreeformSelection(), _annotationSession);
  if (output == null)
  {
    return;
  }

  CopyToClipboard(output);

  if (_settings.ScreenshotAutoSave)
  {
    SaveToFile(output);
  }
}
```

- [ ] **Step 6: Run focused tests**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release --filter FullyQualifiedName~FreeformScreenshotWindowTests`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add ScreenTranslator/Windows/FreeformScreenshotWindow.xaml ScreenTranslator/Windows/FreeformScreenshotWindow.xaml.cs ScreenTranslator.Tests/FreeformScreenshotWindowTests.cs
git commit -m "feat: add annotation edit mode to freeform screenshots"
```

### Task 5: Add Window-Shared Tool Strings And Final Verification

**Files:**
- Modify: `F:/yys/transtools/pot/ScreenTranslator/Resources/Strings.en.xaml`
- Modify: `F:/yys/transtools/pot/ScreenTranslator/Resources/Strings.zh-CN.xaml`
- Verify: `F:/yys/transtools/pot/ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml`
- Verify: `F:/yys/transtools/pot/ScreenTranslator/Windows/FreeformScreenshotWindow.xaml`
- Verify: `F:/yys/transtools/pot/ScreenTranslator.Tests/ScreenshotAnnotationSessionTests.cs`
- Verify: `F:/yys/transtools/pot/ScreenTranslator.Tests/ScreenshotAnnotationRendererTests.cs`
- Verify: `F:/yys/transtools/pot/ScreenTranslator.Tests/ScreenshotOverlayWindowTests.cs`
- Verify: `F:/yys/transtools/pot/ScreenTranslator.Tests/FreeformScreenshotWindowTests.cs`

- [ ] **Step 1: Add localization strings for the annotation toolbar**

```xml
<sys:String x:Key="Screenshot_Brush">Brush</sys:String>
<sys:String x:Key="Screenshot_Rectangle">Rectangle</sys:String>
<sys:String x:Key="Screenshot_Mosaic">Mosaic</sys:String>
<sys:String x:Key="Screenshot_Undo">Undo</sys:String>
<sys:String x:Key="Screenshot_ClearAnnotations">Clear</sys:String>
<sys:String x:Key="Screenshot_Tooltip_Brush">Draw a freehand highlight</sys:String>
<sys:String x:Key="Screenshot_Tooltip_Rectangle">Draw a rectangle outline</sys:String>
<sys:String x:Key="Screenshot_Tooltip_Mosaic">Paint mosaic over sensitive areas</sys:String>
<sys:String x:Key="Screenshot_Tooltip_Undo">Undo the last annotation</sys:String>
<sys:String x:Key="Screenshot_Tooltip_ClearAnnotations">Clear all annotations</sys:String>
```

```xml
<sys:String x:Key="Screenshot_Brush">画笔</sys:String>
<sys:String x:Key="Screenshot_Rectangle">线框</sys:String>
<sys:String x:Key="Screenshot_Mosaic">马赛克</sys:String>
<sys:String x:Key="Screenshot_Undo">撤销</sys:String>
<sys:String x:Key="Screenshot_ClearAnnotations">清空标注</sys:String>
<sys:String x:Key="Screenshot_Tooltip_Brush">自由绘制标注</sys:String>
<sys:String x:Key="Screenshot_Tooltip_Rectangle">绘制矩形线框</sys:String>
<sys:String x:Key="Screenshot_Tooltip_Mosaic">在敏感区域涂抹马赛克</sys:String>
<sys:String x:Key="Screenshot_Tooltip_Undo">撤销上一步标注</sys:String>
<sys:String x:Key="Screenshot_Tooltip_ClearAnnotations">清空当前标注</sys:String>
```

- [ ] **Step 2: Run the full test project**

Run: `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Release`
Expected: PASS

- [ ] **Step 3: Run the application build**

Run: `dotnet build .\ScreenTranslator\ScreenTranslator.csproj -c Release`
Expected: PASS with 0 errors

- [ ] **Step 4: Manual verification**

Run: `dotnet run --project .\ScreenTranslator\ScreenTranslator.csproj`
Expected:
- rectangular screenshot enters edit mode after selection
- brush, rectangle, and mosaic render in preview
- undo removes the latest annotation
- clear annotations keeps the selection but removes markup
- pin, copy, and save output the annotated image
- freeform screenshot supports the same tools
- freeform redraw resets both selection and annotations
- long screenshot still starts from rectangular selection toolbar

- [ ] **Step 5: Commit**

```bash
git add ScreenTranslator/Resources/Strings.en.xaml ScreenTranslator/Resources/Strings.zh-CN.xaml ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml.cs ScreenTranslator/Windows/FreeformScreenshotWindow.xaml ScreenTranslator/Windows/FreeformScreenshotWindow.xaml.cs ScreenTranslator/Services/ScreenshotAnnotationTool.cs ScreenTranslator/Services/ScreenshotAnnotationOperation.cs ScreenTranslator/Services/ScreenshotAnnotationSession.cs ScreenTranslator/Services/ScreenshotAnnotationRenderer.cs ScreenTranslator.Tests/ScreenshotAnnotationSessionTests.cs ScreenTranslator.Tests/ScreenshotAnnotationRendererTests.cs ScreenTranslator.Tests/ScreenshotOverlayWindowTests.cs ScreenTranslator.Tests/FreeformScreenshotWindowTests.cs
git commit -m "chore: verify screenshot annotation editor flow"
```
