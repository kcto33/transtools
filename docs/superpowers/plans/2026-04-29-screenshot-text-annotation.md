# Screenshot Text Annotation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add inline text annotations to rectangular and freeform screenshot editors.

**Architecture:** Extend the existing screenshot annotation model with a `Text` tool and a text operation. Both screenshot windows create an inline `TextBox` preview and commit text into `ScreenshotAnnotationSession`; `ScreenshotAnnotationRenderer` composites text into final copy/save/pin output.

**Tech Stack:** .NET 8, WPF, xUnit, `System.Windows.Media` drawing APIs.

---

## File Structure

- Modify `ScreenTranslator/Services/ScreenshotAnnotationTool.cs`: add `Text`.
- Modify `ScreenTranslator/Services/ScreenshotAnnotationOperation.cs`: add `TextAnnotationOperation`.
- Modify `ScreenTranslator/Services/ScreenshotAnnotationSession.cs`: add `CommitText`.
- Modify `ScreenTranslator/Services/ScreenshotAnnotationRenderer.cs`: render text operations.
- Modify `ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml`: add text button and inline text box.
- Modify `ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml.cs`: route text tool clicks into inline entry and commit.
- Modify `ScreenTranslator/Windows/FreeformScreenshotWindow.xaml`: add text button and inline text box.
- Modify `ScreenTranslator/Windows/FreeformScreenshotWindow.xaml.cs`: same behavior adapted to freeform coordinates and mask.
- Modify `ScreenTranslator/Resources/Strings.en.xaml`: add English text tool labels.
- Modify `ScreenTranslator/Resources/Strings.zh-CN.xaml`: add Chinese text tool labels.
- Modify `ScreenTranslator.Tests/ScreenshotAnnotationSessionTests.cs`: test text operation storage and empty text guard.
- Modify `ScreenTranslator.Tests/ScreenshotAnnotationRendererTests.cs`: test text rendering and clipping.
- Modify `ScreenTranslator.Tests/ScreenshotOverlayWindowTests.cs`: test toolbar order includes `Text`.

---

### Task 1: Annotation Model And Session

**Files:**
- Modify: `ScreenTranslator/Services/ScreenshotAnnotationTool.cs`
- Modify: `ScreenTranslator/Services/ScreenshotAnnotationOperation.cs`
- Modify: `ScreenTranslator/Services/ScreenshotAnnotationSession.cs`
- Test: `ScreenTranslator.Tests/ScreenshotAnnotationSessionTests.cs`

- [ ] **Step 1: Write failing tests for committed text**

Append these tests to `ScreenTranslator.Tests/ScreenshotAnnotationSessionTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Debug --filter "FullyQualifiedName~ScreenshotAnnotationSessionTests"
```

Expected: FAIL because `ScreenshotAnnotationTool.Text`, `CommitText`, and `TextAnnotationOperation` do not exist.

- [ ] **Step 3: Add the text tool enum value**

Change `ScreenTranslator/Services/ScreenshotAnnotationTool.cs` to:

```csharp
namespace ScreenTranslator.Services;

public enum ScreenshotAnnotationTool
{
  None,
  Brush,
  Text,
  Rectangle,
  Arrow,
  Mosaic
}
```

- [ ] **Step 4: Add the text operation record**

Append this record to `ScreenTranslator/Services/ScreenshotAnnotationOperation.cs`:

```csharp
public sealed record TextAnnotationOperation(
  Point Location,
  Geometry ClipMask,
  string Text,
  Color Color,
  double FontSize) : ScreenshotAnnotationOperation;
```

- [ ] **Step 5: Add `CommitText` to the session**

Add this method to `ScreenTranslator/Services/ScreenshotAnnotationSession.cs` after `CommitArrow`:

```csharp
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
```

- [ ] **Step 6: Run tests to verify Task 1 passes**

Run:

```powershell
dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Debug --filter "FullyQualifiedName~ScreenshotAnnotationSessionTests"
```

Expected: PASS.

---

### Task 2: Text Rendering

**Files:**
- Modify: `ScreenTranslator/Services/ScreenshotAnnotationRenderer.cs`
- Test: `ScreenTranslator.Tests/ScreenshotAnnotationRendererTests.cs`

- [ ] **Step 1: Write failing renderer test**

Append this test to `ScreenTranslator.Tests/ScreenshotAnnotationRendererTests.cs`:

```csharp
[Fact]
public void RenderComposite_Draws_Text_And_Clips_To_Mask()
{
  var baseImage = CreateSolidImage(80, 50, Colors.Navy);
  var session = new ScreenshotAnnotationSession(
    new Size(80, 50),
    new RectangleGeometry(new Rect(10, 10, 50, 25)));

  session.SetActiveTool(ScreenshotAnnotationTool.Text);
  session.CommitText(new Point(12, 12), "Hi", Colors.White, fontSize: 22);
  session.CommitText(new Point(65, 12), "X", Colors.Red, fontSize: 22);

  var result = ScreenshotAnnotationRenderer.RenderComposite(baseImage, session);

  Assert.Equal(80, result.PixelWidth);
  Assert.Equal(50, result.PixelHeight);
  Assert.NotEqual(GetPixel(baseImage, 16, 22), GetPixel(result, 16, 22));
  Assert.Equal(GetPixel(baseImage, 70, 22), GetPixel(result, 70, 22));
}
```

- [ ] **Step 2: Run renderer tests to verify failure**

Run:

```powershell
dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Debug --filter "FullyQualifiedName~ScreenshotAnnotationRendererTests"
```

Expected: FAIL because `RenderComposite` does not handle `TextAnnotationOperation`.

- [ ] **Step 3: Add text operation switch case**

In `ScreenshotAnnotationRenderer.RenderComposite`, add this case after arrow handling and before brush handling:

```csharp
case TextAnnotationOperation text:
  DrawText(context, text);
  break;
```

- [ ] **Step 4: Add text drawing helper**

Add this method to `ScreenTranslator/Services/ScreenshotAnnotationRenderer.cs`:

```csharp
private static void DrawText(DrawingContext context, TextAnnotationOperation text)
{
  context.PushClip(text.ClipMask);

  var formattedText = new FormattedText(
    text.Text,
    System.Globalization.CultureInfo.CurrentUICulture,
    FlowDirection.LeftToRight,
    new Typeface("Segoe UI"),
    text.FontSize,
    new SolidColorBrush(text.Color),
    pixelsPerDip: 1.0);

  context.DrawText(formattedText, text.Location);
  context.Pop();
}
```

- [ ] **Step 5: Add required using**

If needed, add this to `ScreenTranslator/Services/ScreenshotAnnotationRenderer.cs`:

```csharp
using System.Windows;
```

- [ ] **Step 6: Run renderer tests to verify Task 2 passes**

Run:

```powershell
dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Debug --filter "FullyQualifiedName~ScreenshotAnnotationRendererTests"
```

Expected: PASS.

---

### Task 3: Rectangular Screenshot Window UI

**Files:**
- Modify: `ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml`
- Modify: `ScreenTranslator/Windows/ScreenshotOverlayWindow.xaml.cs`
- Modify: `ScreenTranslator/Resources/Strings.en.xaml`
- Modify: `ScreenTranslator/Resources/Strings.zh-CN.xaml`
- Test: `ScreenTranslator.Tests/ScreenshotOverlayWindowTests.cs`

- [ ] **Step 1: Update toolbar order test first**

In `ScreenshotOverlayWindowTests.GetToolbarButtonOrder_Returns_Configured_Screenshot_Tool_Order`, update the expected list to include `Text` after `Brush`:

```csharp
Assert.Equal(
  [
    "Save",
    "Copy",
    "LongScreenshot",
    "Gif",
    "Redraw",
    "Pin",
    "Brush",
    "Text",
    "Rectangle",
    "Mosaic",
    "Undo",
    "Cancel",
  ],
  order);
```

- [ ] **Step 2: Run overlay tests to verify failure**

Run:

```powershell
dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Debug --filter "FullyQualifiedName~ScreenshotOverlayWindowTests.GetToolbarButtonOrder"
```

Expected: FAIL because `GetToolbarButtonOrder()` does not include `Text`.

- [ ] **Step 3: Add text resources**

Add to `ScreenTranslator/Resources/Strings.en.xaml` near other screenshot annotation strings:

```xml
<sys:String x:Key="Screenshot_Annotate_Text">Text</sys:String>
<sys:String x:Key="Screenshot_Tooltip_Annotate_Text">Place text on the selected screenshot</sys:String>
```

Add to `ScreenTranslator/Resources/Strings.zh-CN.xaml`:

```xml
<sys:String x:Key="Screenshot_Annotate_Text">文本</sys:String>
<sys:String x:Key="Screenshot_Tooltip_Annotate_Text">在所选截图上添加文本</sys:String>
```

- [ ] **Step 4: Add inline text box and toolbar button**

In `ScreenshotOverlayWindow.xaml`, inside `EditSurface` after `AnnotationRectanglePreview`, add:

```xml
<TextBox x:Name="AnnotationTextBox"
         MinWidth="80"
         Background="#CC000000"
         BorderBrush="#AAFFFFFF"
         BorderThickness="1"
         Foreground="White"
         Padding="4,2"
         Visibility="Collapsed"
         AcceptsReturn="False"
         KeyDown="AnnotationTextBox_KeyDown"
         LostFocus="AnnotationTextBox_LostFocus" />
```

Add this toolbar button immediately after `BtnBrush`:

```xml
<Button x:Name="BtnText" Content="{DynamicResource Screenshot_Annotate_Text}" Style="{StaticResource ToolbarButton}" Click="BtnText_Click" ToolTip="{DynamicResource Screenshot_Tooltip_Annotate_Text}" />
```

- [ ] **Step 5: Update toolbar order method**

In `ScreenshotOverlayWindow.GetToolbarButtonOrder()`, insert `"Text"` after `"Brush"`:

```csharp
"Brush",
"Text",
"Rectangle",
```

- [ ] **Step 6: Add text tool click handler**

Add to `ScreenshotOverlayWindow.xaml.cs` near the other toolbar handlers:

```csharp
private void BtnText_Click(object sender, RoutedEventArgs e)
{
  SetActiveAnnotationTool(ScreenshotAnnotationTool.Text);
}
```

- [ ] **Step 7: Route text placement in mouse down**

In `OnMouseLeftButtonDown`, after `CanBeginEditAnnotation(...)` succeeds and before `BeginAnnotation(...)`, add:

```csharp
if (_annotationSession?.ActiveTool == ScreenshotAnnotationTool.Text)
{
  BeginTextAnnotation(e.GetPosition(EditSurface));
  return;
}
```

- [ ] **Step 8: Add text box helpers**

Add these methods to `ScreenshotOverlayWindow.xaml.cs` near annotation helpers:

```csharp
private void BeginTextAnnotation(WpfPoint point)
{
  if (_annotationSession is null)
  {
    return;
  }

  ClearAnnotationPreview();
  var clampedPoint = GetClampedEditSurfacePoint(point);
  AnnotationTextBox.Text = string.Empty;
  AnnotationTextBox.Foreground = new SolidColorBrush(_annotationSession.CurrentColor);
  AnnotationTextBox.FontSize = _annotationSession.CurrentSize * 4;
  Canvas.SetLeft(AnnotationTextBox, clampedPoint.X);
  Canvas.SetTop(AnnotationTextBox, clampedPoint.Y);
  AnnotationTextBox.Visibility = Visibility.Visible;
  AnnotationTextBox.Focus();
}

private void CommitTextAnnotation()
{
  if (_annotationSession is null || AnnotationTextBox.Visibility != Visibility.Visible)
  {
    return;
  }

  var text = AnnotationTextBox.Text;
  var previewLocation = new WpfPoint(
    Canvas.GetLeft(AnnotationTextBox),
    Canvas.GetTop(AnnotationTextBox));
  var imageLocation = CreateAnnotationImagePoint(
    previewLocation,
    _annotationSession.CanvasSize,
    GetSelectedImagePreviewDisplaySize());
  var fontSize = (_annotationSession.CurrentSize * 4) * ((GetEditScaleX() + GetEditScaleY()) / 2.0);

  _annotationSession.CommitText(imageLocation, text, _annotationSession.CurrentColor, fontSize);
  CancelTextAnnotation();
  RefreshSelectedImagePreview();
  UpdateAnnotationToolbarState();
}

private void CancelTextAnnotation()
{
  AnnotationTextBox.Visibility = Visibility.Collapsed;
  AnnotationTextBox.Text = string.Empty;
}

private void AnnotationTextBox_KeyDown(object sender, WpfKeyEventArgs e)
{
  if (e.Key == Key.Enter)
  {
    CommitTextAnnotation();
    e.Handled = true;
    return;
  }

  if (e.Key == Key.Escape)
  {
    CancelTextAnnotation();
    e.Handled = true;
  }
}

private void AnnotationTextBox_LostFocus(object sender, RoutedEventArgs e)
{
  CommitTextAnnotation();
}
```

- [ ] **Step 9: Hide text box when previews reset**

At the end of `ClearAnnotationPreview()`, add:

```csharp
CancelTextAnnotation();
```

- [ ] **Step 10: Update active toolbar and cursor**

In `UpdateAnnotationToolbarState()`, add:

```csharp
BtnText.Background = _annotationSession.ActiveTool == ScreenshotAnnotationTool.Text ? selectedBackground : transparentBackground;
```

In `SetActiveAnnotationTool`, include text cursor:

```csharp
ScreenshotAnnotationTool.Text => WpfCursors.IBeam,
```

- [ ] **Step 11: Run overlay toolbar test**

Run:

```powershell
dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Debug --filter "FullyQualifiedName~ScreenshotOverlayWindowTests.GetToolbarButtonOrder"
```

Expected: PASS.

---

### Task 4: Freeform Screenshot Window UI

**Files:**
- Modify: `ScreenTranslator/Windows/FreeformScreenshotWindow.xaml`
- Modify: `ScreenTranslator/Windows/FreeformScreenshotWindow.xaml.cs`
- Test: existing freeform and annotation tests

- [ ] **Step 1: Add inline text box and button**

In `FreeformScreenshotWindow.xaml`, inside `EditSurface` after `AnnotationRectanglePreview`, add the same `AnnotationTextBox` block from Task 3.

Add this toolbar button immediately after `BtnBrush`:

```xml
<Button x:Name="BtnText" Content="{DynamicResource Screenshot_Annotate_Text}" Style="{StaticResource ToolbarButton}" Click="BtnText_Click" ToolTip="{DynamicResource Screenshot_Tooltip_Annotate_Text}" />
```

- [ ] **Step 2: Add text click handler**

Add to `FreeformScreenshotWindow.xaml.cs` near the other toolbar handlers:

```csharp
private void BtnText_Click(object sender, RoutedEventArgs e)
{
  SetActiveAnnotationTool(ScreenshotAnnotationTool.Text);
}
```

- [ ] **Step 3: Route text placement**

In `OnMouseLeftButtonDown`, after edit surface and mask checks pass and before `BeginAnnotation(e.GetPosition(this));`, add:

```csharp
if (_annotationSession?.ActiveTool == ScreenshotAnnotationTool.Text)
{
  BeginTextAnnotation(e.GetPosition(this));
  return;
}
```

- [ ] **Step 4: Add freeform text helpers**

Add these methods to `FreeformScreenshotWindow.xaml.cs` near annotation helpers:

```csharp
private void BeginTextAnnotation(WpfPoint windowPoint)
{
  if (_annotationSession is null)
  {
    return;
  }

  ClearAnnotationPreview();
  var clampedPoint = GetClampedEditSurfacePoint(windowPoint);
  AnnotationTextBox.Text = string.Empty;
  AnnotationTextBox.Foreground = new SolidColorBrush(_annotationSession.CurrentColor);
  AnnotationTextBox.FontSize = _annotationSession.CurrentSize * 4;
  Canvas.SetLeft(AnnotationTextBox, clampedPoint.X);
  Canvas.SetTop(AnnotationTextBox, clampedPoint.Y);
  AnnotationTextBox.Visibility = Visibility.Visible;
  AnnotationTextBox.Focus();
}

private void CommitTextAnnotation()
{
  if (_annotationSession is null || AnnotationTextBox.Visibility != Visibility.Visible)
  {
    return;
  }

  var previewLocation = new WpfPoint(
    Canvas.GetLeft(AnnotationTextBox),
    Canvas.GetTop(AnnotationTextBox));
  var scaleX = GetEditScaleX();
  var scaleY = GetEditScaleY();
  var fontSize = (_annotationSession.CurrentSize * 4) * ((scaleX + scaleY) / 2.0);

  _annotationSession.CommitText(
    ToImagePoint(previewLocation, scaleX, scaleY),
    AnnotationTextBox.Text,
    _annotationSession.CurrentColor,
    fontSize);

  CancelTextAnnotation();
  RefreshSelectedImagePreview();
  UpdateAnnotationToolbarState();
}

private void CancelTextAnnotation()
{
  AnnotationTextBox.Visibility = Visibility.Collapsed;
  AnnotationTextBox.Text = string.Empty;
}

private void AnnotationTextBox_KeyDown(object sender, WpfKeyEventArgs e)
{
  if (e.Key == Key.Enter)
  {
    CommitTextAnnotation();
    e.Handled = true;
    return;
  }

  if (e.Key == Key.Escape)
  {
    CancelTextAnnotation();
    e.Handled = true;
  }
}

private void AnnotationTextBox_LostFocus(object sender, RoutedEventArgs e)
{
  CommitTextAnnotation();
}
```

- [ ] **Step 5: Update active toolbar and cursor**

In `UpdateAnnotationToolbarState()`, add:

```csharp
BtnText.Background = _annotationSession.ActiveTool == ScreenshotAnnotationTool.Text ? selectedBackground : transparentBackground;
```

In `SetActiveAnnotationTool`, include text cursor:

```csharp
ScreenshotAnnotationTool.Text => WpfCursors.IBeam,
```

- [ ] **Step 6: Clear draft text on reset paths**

Call `CancelTextAnnotation()` from `ResetSelection()` and before changing active tools in `SetActiveAnnotationTool()`.

- [ ] **Step 7: Run freeform tests**

Run:

```powershell
dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Debug --filter "FullyQualifiedName~FreeformScreenshotWindowTests"
```

Expected: PASS.

---

### Task 5: Full Verification

**Files:**
- All files touched above.

- [ ] **Step 1: Run all tests**

Run:

```powershell
dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Debug
```

Expected: PASS.

- [ ] **Step 2: Build the app**

Run:

```powershell
dotnet build .\ScreenTranslator\ScreenTranslator.csproj -c Debug
```

Expected: Build succeeds with no new errors.

- [ ] **Step 3: Review changed files**

Run:

```powershell
git diff -- ScreenTranslator ScreenTranslator.Tests
```

Expected: Diff only contains text annotation feature changes and localization strings.

- [ ] **Step 4: Commit implementation**

Run:

```powershell
git add -- ScreenTranslator ScreenTranslator.Tests
git commit -m "Add screenshot text annotations"
```

Expected: Commit succeeds.

---

## Self-Review

- Spec coverage: the plan adds the text tool, inline click-to-type input, shared color and size controls, rectangular and freeform support, renderer compositing, undo/clear integration through session operations, localization, tests, and build verification.
- Unfinished marker scan: no undefined future work remains in the plan.
- Type consistency: `ScreenshotAnnotationTool.Text`, `TextAnnotationOperation`, and `CommitText(Point location, string text, Color color, double fontSize)` are used consistently across tests, model, renderer, and windows.
