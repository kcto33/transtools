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
  public void BackgroundImage_Uses_Fill_Stretch_To_Match_CurrentDpiOverlayBounds()
  {
    var xaml = File.ReadAllText(GetSourceFilePath("ScreenTranslator", "Windows", "ScreenshotOverlayWindow.xaml"));

    Assert.Contains("x:Name=\"BackgroundImage\"", xaml);
    Assert.Matches("<Image\\s+x:Name=\"BackgroundImage\"[^<]*Stretch=\"Fill\"[^<]*/>", xaml);
  }

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
  public void CreatePixelSelectionRegionFromSelectionBounds_Uses_DpiScale_For_Right_And_Bottom_Edges()
  {
    var region = ScreenshotOverlayWindow.CreatePixelSelectionRegionFromSelectionBounds(
      new Rect(10, 20, 100, 80),
      new System.Drawing.Rectangle(0, 0, 1920, 1080),
      dpiScaleX: 1.75,
      dpiScaleY: 1.75);

    Assert.Equal(new System.Drawing.Rectangle(17, 35, 176, 140), region);
  }

  [Fact]
  public void CreatePixelSelectionRegionFromSelectionBounds_Offsets_From_VirtualScreen_Origin()
  {
    var region = ScreenshotOverlayWindow.CreatePixelSelectionRegionFromSelectionBounds(
      new Rect(4, 6, 20, 10),
      new System.Drawing.Rectangle(-1920, 0, 3840, 1080),
      dpiScaleX: 1.5,
      dpiScaleY: 1.5);

    Assert.Equal(new System.Drawing.Rectangle(-1914, 9, 30, 15), region);
  }

  [Fact]
  public void CreatePixelSelectionRegionFromDisplayedBackground_Uses_Actual_Background_Display_Scale()
  {
    var region = ScreenshotOverlayWindow.CreatePixelSelectionRegionFromDisplayedBackground(
      new Rect(10, 5, 20, 10),
      new System.Drawing.Rectangle(100, 200, 200, 100),
      capturedPixelSize: new Size(200, 100),
      backgroundDisplaySizeDip: new Size(100, 50));

    Assert.Equal(new System.Drawing.Rectangle(120, 210, 40, 20), region);
  }

  [Fact]
  public void CreatePixelSelectionRegionFromDisplayedElement_Uses_Element_Origin_And_Display_Scale()
  {
    var region = ScreenshotOverlayWindow.CreatePixelSelectionRegionFromDisplayedElement(
      new Rect(15, 25, 20, 10),
      new Rect(5, 10, 100, 50),
      new System.Drawing.Rectangle(100, 200, 200, 100),
      capturedPixelSize: new Size(200, 100));

    Assert.Equal(new System.Drawing.Rectangle(120, 230, 40, 20), region);
  }

  [Fact]
  public void CreateSelectionBoundsFromScreenPixels_Converts_Physical_Cursor_Points_To_Overlay_Dips()
  {
    var bounds = ScreenshotOverlayWindow.CreateSelectionBoundsFromScreenPixels(
      new System.Drawing.Point(441, 263),
      new System.Drawing.Point(1131, 729),
      new System.Drawing.Rectangle(0, 0, 1920, 1080),
      dpiScaleX: 1.75,
      dpiScaleY: 1.75);

    Assert.Equal(252, bounds.X, precision: 6);
    Assert.Equal(150.285714285714, bounds.Y, precision: 6);
    Assert.Equal(394.285714285714, bounds.Width, precision: 6);
    Assert.Equal(266.285714285714, bounds.Height, precision: 6);
  }

  [Fact]
  public void CreatePixelSelectionRegionFromScreenPixels_Clamps_To_VirtualScreen()
  {
    var region = ScreenshotOverlayWindow.CreatePixelSelectionRegionFromScreenPixels(
      new System.Drawing.Point(-20, 100),
      new System.Drawing.Point(2000, 1200),
      new System.Drawing.Rectangle(0, 0, 1920, 1080));

    Assert.Equal(new System.Drawing.Rectangle(0, 100, 1920, 980), region);
  }

  [Fact]
  public void CreateBackgroundImageSizeDip_Converts_Captured_Physical_Size_To_Dips()
  {
    var bitmap = new WriteableBitmap(2560, 1440, 96, 96, PixelFormats.Bgra32, null);

    var size = ScreenshotOverlayWindow.CreateBackgroundImageSizeDip(
      bitmap,
      dpiScaleX: 1.75,
      dpiScaleY: 1.75);

    Assert.Equal(1462.857142857143, size.Width, precision: 6);
    Assert.Equal(822.857142857143, size.Height, precision: 6);
  }

  [Fact]
  public void CreateAnnotationImagePoint_Uses_Preview_Display_Size()
  {
    var point = ScreenshotOverlayWindow.CreateAnnotationImagePoint(
      new Point(50, 25),
      canvasSizePx: new Size(175, 100),
      previewDisplaySizeDip: new Size(100, 50));

    Assert.Equal(87.5, point.X, precision: 6);
    Assert.Equal(50, point.Y, precision: 6);
  }

  [Fact]
  public void CreateAnnotationImageBounds_Uses_Preview_Display_Size()
  {
    var bounds = ScreenshotOverlayWindow.CreateAnnotationImageBounds(
      new Rect(10, 5, 20, 10),
      canvasSizePx: new Size(175, 100),
      previewDisplaySizeDip: new Size(100, 50));

    Assert.Equal(17.5, bounds.X, precision: 6);
    Assert.Equal(10, bounds.Y, precision: 6);
    Assert.Equal(35, bounds.Width, precision: 6);
    Assert.Equal(20, bounds.Height, precision: 6);
  }

  [Fact]
  public void CreateOverlayBoundsDip_Converts_Physical_VirtualScreen_To_Dips()
  {
    var bounds = ScreenshotOverlayWindow.CreateOverlayBoundsDip(
      new System.Drawing.Rectangle(0, 0, 2560, 1440),
      dpiScaleX: 1.75,
      dpiScaleY: 1.75);

    Assert.Equal(0, bounds.Left);
    Assert.Equal(0, bounds.Top);
    Assert.Equal(1462.857142857143, bounds.Width, precision: 6);
    Assert.Equal(822.857142857143, bounds.Height, precision: 6);
  }

  [Fact]
  public void CreateOverlayPresentation_Uses_Matching_Window_And_Background_Dip_Bounds()
  {
    var presentation = ScreenshotOverlayWindow.CreateOverlayPresentation(
      new System.Drawing.Rectangle(-1280, 0, 3840, 1440),
      capturedPixelSize: new Size(3840, 1440),
      dpiScaleX: 1.25,
      dpiScaleY: 1.25);

    Assert.Equal(-1024, presentation.OverlayBoundsDip.Left, precision: 6);
    Assert.Equal(0, presentation.OverlayBoundsDip.Top, precision: 6);
    Assert.Equal(3072, presentation.OverlayBoundsDip.Width, precision: 6);
    Assert.Equal(1152, presentation.OverlayBoundsDip.Height, precision: 6);
    Assert.Equal(3072, presentation.BackgroundSizeDip.Width, precision: 6);
    Assert.Equal(1152, presentation.BackgroundSizeDip.Height, precision: 6);
  }

  [Theory]
  [InlineData(1.0, 1920, 1080)]
  [InlineData(1.75, 1097.142857142857, 617.142857142857)]
  public void CreateOverlayMetrics_Uses_OneScale_For_Window_And_Background(
    double dpiScale,
    double expectedWidthDip,
    double expectedHeightDip)
  {
    var metrics = ScreenshotOverlayWindow.CreateOverlayMetrics(
      new System.Drawing.Rectangle(0, 0, 1920, 1080),
      capturedPixelSize: new Size(1920, 1080),
      dpiScaleX: dpiScale,
      dpiScaleY: dpiScale);

    Assert.Equal(expectedWidthDip, metrics.OverlayBoundsDip.Width, precision: 6);
    Assert.Equal(expectedHeightDip, metrics.OverlayBoundsDip.Height, precision: 6);
    Assert.Equal(metrics.OverlayBoundsDip.Width, metrics.BackgroundSizeDip.Width, precision: 6);
    Assert.Equal(metrics.OverlayBoundsDip.Height, metrics.BackgroundSizeDip.Height, precision: 6);
  }

  [Fact]
  public void CreateSelectionDpiScale_Uses_SelectedPixels_And_CurrentSelectionBounds()
  {
    var scale = ScreenshotOverlayWindow.CreateSelectionDpiScale(
      new System.Drawing.Rectangle(100, 200, 300, 180),
      new Rect(20, 30, 200, 120),
      fallbackDpiScaleX: 1.25,
      fallbackDpiScaleY: 1.25);

    Assert.Equal(1.5, scale.DpiScaleX, precision: 6);
    Assert.Equal(1.5, scale.DpiScaleY, precision: 6);
  }

  [Fact]
  public void CreateSelectionDpiScale_FallsBack_WhenSelectionBoundsAreEmpty()
  {
    var scale = ScreenshotOverlayWindow.CreateSelectionDpiScale(
      new System.Drawing.Rectangle(100, 200, 300, 180),
      Rect.Empty,
      fallbackDpiScaleX: 1.25,
      fallbackDpiScaleY: 1.5);

    Assert.Equal(1.25, scale.DpiScaleX, precision: 6);
    Assert.Equal(1.5, scale.DpiScaleY, precision: 6);
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
        "Text",
        "Rectangle",
        "Mosaic",
        "Undo",
        "Cancel",
      ],
      order);
  }

  [Fact]
  public void GetAnnotationColorPalette_Returns_Common_Preset_Colors()
  {
    var palette = ScreenshotOverlayWindow.GetAnnotationColorPalette();

    Assert.Equal(
      [
        Colors.DeepSkyBlue,
        Colors.Red,
        Colors.Orange,
        Colors.Yellow,
        Colors.LimeGreen,
        Colors.White,
        Colors.Black,
        Colors.MediumPurple,
      ],
      palette);
  }

  [Fact]
  public void Freeform_And_Rectangle_Screenshot_Toolbars_Use_Same_Annotation_Color_Palette()
  {
    Assert.Equal(
      ScreenshotOverlayWindow.GetAnnotationColorPalette(),
      FreeformScreenshotWindow.GetAnnotationColorPalette());
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

  private static string GetSourceFilePath(params string[] relativeParts)
  {
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
      var candidate = Path.Combine([directory.FullName, .. relativeParts]);
      if (File.Exists(candidate))
      {
        return candidate;
      }

      directory = directory.Parent;
    }

    throw new FileNotFoundException($"Could not find source file: {Path.Combine(relativeParts)}");
  }
}
