using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenTranslator.Services;
using ScreenTranslator.Windows;
using Xunit;

namespace ScreenTranslator.Tests;

public sealed class ScreenshotOverlayWindowTests
{
  [Fact]
  public void ShouldAssignCapturedBackground_ReturnsFalse_WhenWindowIsClosed()
  {
    var bitmap = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);

    var shouldAssign = ScreenshotOverlayWindow.ShouldAssignCapturedBackground(isClosed: true, bitmap);

    Assert.False(shouldAssign);
  }

  [Fact]
  public void ShouldAssignCapturedBackground_ReturnsFalse_WhenBitmapIsMissing()
  {
    var shouldAssign = ScreenshotOverlayWindow.ShouldAssignCapturedBackground(isClosed: false, null);

    Assert.False(shouldAssign);
  }

  [Fact]
  public void ShouldAssignCapturedBackground_ReturnsTrue_WhenWindowIsOpenAndBitmapExists()
  {
    var bitmap = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);

    var shouldAssign = ScreenshotOverlayWindow.ShouldAssignCapturedBackground(isClosed: false, bitmap);

    Assert.True(shouldAssign);
  }

  [Theory]
  [InlineData(false, true, true)]
  [InlineData(false, false, false)]
  [InlineData(true, true, false)]
  public void ShouldHideOverlayDuringBackgroundCapture_ReturnsExpectedValue(
    bool isClosed,
    bool isVisible,
    bool expected)
  {
    var shouldHide = ScreenshotOverlayWindow.ShouldHideOverlayDuringBackgroundCapture(isClosed, isVisible);

    Assert.Equal(expected, shouldHide);
  }

  [Theory]
  [InlineData(true, false, true)]
  [InlineData(false, false, false)]
  [InlineData(true, true, false)]
  public void ShouldRestoreOverlayAfterBackgroundCapture_ReturnsExpectedValue(
    bool wasHiddenForCapture,
    bool isClosed,
    bool expected)
  {
    var shouldRestore = ScreenshotOverlayWindow.ShouldRestoreOverlayAfterBackgroundCapture(wasHiddenForCapture, isClosed);

    Assert.Equal(expected, shouldRestore);
  }

  [Fact]
  public void ShouldBeginBackgroundCapture_ReturnsFalse_WhenInitialBackgroundExists()
  {
    var bitmap = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);

    var shouldCapture = ScreenshotOverlayWindow.ShouldBeginBackgroundCapture(bitmap);

    Assert.False(shouldCapture);
  }

  [Fact]
  public void ShouldBeginBackgroundCapture_ReturnsTrue_WhenInitialBackgroundIsMissing()
  {
    var shouldCapture = ScreenshotOverlayWindow.ShouldBeginBackgroundCapture(null);

    Assert.True(shouldCapture);
  }

  [Fact]
  public void GetOutputImage_ReturnsBaseImage_WhenAnnotationSessionIsNull()
  {
    var baseImage = CreateSolidImage(20, 20, Colors.Navy);

    var result = ScreenshotOverlayWindow.GetOutputImage(baseImage, session: null);

    Assert.Same(baseImage, result);
  }

  [Fact]
  public void GetOutputImage_ReturnsCompositedImage_WhenAnnotationSessionExists()
  {
    var baseImage = CreateSolidImage(20, 20, Colors.Navy);
    var session = new ScreenshotAnnotationSession(
      new Size(20, 20),
      new RectangleGeometry(new Rect(0, 0, 20, 20)));

    session.SetActiveTool(ScreenshotAnnotationTool.Rectangle);
    session.CommitRectangle(new Rect(4, 4, 12, 12), Colors.Red, 3);

    var result = ScreenshotOverlayWindow.GetOutputImage(baseImage, session);

    Assert.NotSame(baseImage, result);
    Assert.Equal(20, result.PixelWidth);
    Assert.Equal(20, result.PixelHeight);
    Assert.NotEqual(GetPixel(baseImage, 10, 4), GetPixel(result, 10, 4));
    Assert.Equal(GetPixel(baseImage, 1, 1), GetPixel(result, 1, 1));
  }

  [Fact]
  public void CreateEditModeState_Initializes_EditMode_With_Brush_Tool_And_Empty_Session()
  {
    var selectedRegion = new System.Drawing.Rectangle(100, 120, 80, 60);
    var selectionBounds = new Rect(10, 20, 40, 30);

    var state = ScreenshotOverlayWindow.CreateEditModeState(selectedRegion, selectionBounds);

    Assert.True(state.IsEditMode);
    Assert.False(state.IsAnnotating);
    Assert.Equal(selectedRegion, state.SelectedRegion);
    Assert.Equal(selectionBounds, state.SelectionBounds);
    Assert.NotNull(state.AnnotationSession);
    Assert.Equal(ScreenshotAnnotationTool.Brush, state.AnnotationSession!.ActiveTool);
    Assert.Empty(state.AnnotationSession.Operations);
    Assert.Equal(80, state.AnnotationSession.CanvasSize.Width);
    Assert.Equal(60, state.AnnotationSession.CanvasSize.Height);
  }

  [Fact]
  public void ResetEditModeState_Clears_EditMode_Selection_And_Annotations()
  {
    var state = ScreenshotOverlayWindow.CreateEditModeState(
      new System.Drawing.Rectangle(100, 120, 80, 60),
      new Rect(10, 20, 40, 30));
    state.AnnotationSession!.CommitRectangle(new Rect(1, 2, 20, 10), Colors.Red, 3);

    var resetState = ScreenshotOverlayWindow.ResetEditModeState(state);

    Assert.False(resetState.IsEditMode);
    Assert.False(resetState.IsAnnotating);
    Assert.Equal(System.Drawing.Rectangle.Empty, resetState.SelectedRegion);
    Assert.Equal(Rect.Empty, resetState.SelectionBounds);
    Assert.Null(resetState.AnnotationSession);
  }

  [Fact]
  public void DiscardAnnotationsForLongScreenshot_Clears_Operations_And_Preserves_SelectedRegion()
  {
    var state = ScreenshotOverlayWindow.CreateEditModeState(
      new System.Drawing.Rectangle(100, 120, 80, 60),
      new Rect(10, 20, 40, 30));
    state.AnnotationSession!.CommitRectangle(new Rect(1, 2, 20, 10), Colors.Red, 3);

    var updatedState = ScreenshotOverlayWindow.DiscardAnnotationsForLongScreenshot(state);

    Assert.Equal(state.SelectedRegion, updatedState.SelectedRegion);
    Assert.NotNull(updatedState.AnnotationSession);
    Assert.Empty(updatedState.AnnotationSession!.Operations);
    Assert.True(updatedState.IsEditMode);
  }

  [Theory]
  [InlineData(false, true, ScreenshotAnnotationTool.Brush, false)]
  [InlineData(true, false, ScreenshotAnnotationTool.Brush, false)]
  [InlineData(true, true, ScreenshotAnnotationTool.None, false)]
  [InlineData(true, true, ScreenshotAnnotationTool.Rectangle, true)]
  public void CanBeginEditAnnotation_Only_Allows_Active_EditTool_In_EditSurface(
    bool isEditMode,
    bool isWithinEditSurface,
    ScreenshotAnnotationTool tool,
    bool expected)
  {
    ScreenshotAnnotationSession? session = null;
    if (tool != ScreenshotAnnotationTool.None || isEditMode)
    {
      session = new ScreenshotAnnotationSession(
        new Size(20, 20),
        new RectangleGeometry(new Rect(0, 0, 20, 20)));
      session.SetActiveTool(tool);
    }

    var canBegin = ScreenshotOverlayWindow.CanBeginEditAnnotation(isEditMode, session, isWithinEditSurface);

    Assert.Equal(expected, canBegin);
  }

  [Fact]
  public void GetToolbarButtonOrder_Returns_Configured_Screenshot_Tool_Order()
  {
    var order = ScreenshotOverlayWindow.GetToolbarButtonOrder();

    Assert.Equal(
      [
        "Save",
        "Copy",
        "LongScreenshot",
        "Gif",
        "Redraw",
        "Pin",
        "Brush",
        "Rectangle",
        "Mosaic",
        "Undo",
        "Cancel",
      ],
      order);
  }

  private static WriteableBitmap CreateSolidImage(int width, int height, Color color)
  {
    var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
    var pixels = new byte[width * height * 4];

    for (var index = 0; index < pixels.Length; index += 4)
    {
      pixels[index + 0] = color.B;
      pixels[index + 1] = color.G;
      pixels[index + 2] = color.R;
      pixels[index + 3] = color.A;
    }

    bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
    return bitmap;
  }

  private static uint GetPixel(BitmapSource source, int x, int y)
  {
    var pixels = new byte[4];
    source.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
    return BitConverter.ToUInt32(pixels, 0);
  }
}
