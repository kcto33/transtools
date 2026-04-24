using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using ScreenTranslator.Interop;
using ScreenTranslator.Models;
using ScreenTranslator.Services;

using DrawingPoint = System.Drawing.Point;
using WinRect = System.Drawing.Rectangle;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfClipboard = System.Windows.Clipboard;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfSize = System.Windows.Size;

namespace ScreenTranslator.Windows;

public sealed partial class ScreenshotOverlayWindow : Window, IScreenshotOverlaySessionWindow
{
  internal sealed record EditModeState(
    bool IsEditMode,
    bool IsAnnotating,
    WinRect SelectedRegion,
    Rect SelectionBounds,
    ScreenshotAnnotationSession? AnnotationSession);

  internal sealed record OverlayPresentation(Rect OverlayBoundsDip, WpfSize BackgroundSizeDip);

  internal sealed record OverlayMetrics(
    WinRect VirtualScreenPx,
    double DpiScaleX,
    double DpiScaleY,
    Rect OverlayBoundsDip,
    WpfSize BackgroundSizeDip);

  private const double BrushPreviewThickness = 3;
  private const double RectanglePreviewThickness = 3;
  private const double MosaicPreviewThickness = 12;

  private readonly AppSettings _settings;
  private readonly Action<BitmapSource, WinRect, double, double> _onPinRequested;
  private readonly Action? _onFreeformRequested;
  private readonly Action<WinRect, double, double>? _onLongScreenshotRequested;
  private readonly Action<WinRect, double, double>? _onGifRecordingRequested;
  private readonly List<WpfPoint> _annotationPoints = [];

  private WpfPoint _startPoint;
  private DrawingPoint _startPointPx;
  private bool _isSelecting;
  private bool _isEditMode;
  private bool _isAnnotating;
  private WinRect _selectedRegion;
  private Rect _selectionBounds = Rect.Empty;
  private BitmapSource? _capturedScreen;
  private BitmapSource? _selectedImage;
  private ScreenshotAnnotationSession? _annotationSession;
  private System.Drawing.Rectangle _virtualScreenPx;
  private OverlayMetrics _metrics = CreateOverlayMetrics(WinRect.Empty, new WpfSize(1, 1), 1.0, 1.0);
  private double _dpiScaleX = 1.0;
  private double _dpiScaleY = 1.0;
  private bool _isClosed;

  public ScreenshotOverlayWindow(
    AppSettings settings,
    Action<BitmapSource, WinRect, double, double> onPinRequested,
    Action? onFreeformRequested = null,
    Action<WinRect, double, double>? onLongScreenshotRequested = null,
    Action<WinRect, double, double>? onGifRecordingRequested = null,
    BitmapSource? initialCapturedScreen = null)
  {
    _settings = settings;
    _onPinRequested = onPinRequested;
    _onFreeformRequested = onFreeformRequested;
    _onLongScreenshotRequested = onLongScreenshotRequested;
    _onGifRecordingRequested = onGifRecordingRequested;
    _capturedScreen = initialCapturedScreen;

    InitializeComponent();

    Loaded += OnLoaded;
    MouseLeftButtonDown += OnMouseLeftButtonDown;
    MouseMove += OnMouseMove;
    MouseLeftButtonUp += OnMouseLeftButtonUp;
    MouseDown += OnMouseDown;
    KeyDown += OnKeyDown;
  }

  private void OnLoaded(object sender, RoutedEventArgs e)
  {
    InitializeOverlayMetrics(_capturedScreen);
    Left = _metrics.OverlayBoundsDip.Left;
    Top = _metrics.OverlayBoundsDip.Top;
    Width = _metrics.OverlayBoundsDip.Width;
    Height = _metrics.OverlayBoundsDip.Height;

    ApplyBackgroundImage(_capturedScreen);
    UpdateDarkOverlay(null);

    Focus();
    Cursor = WpfCursors.Cross;

    if (ShouldBeginBackgroundCapture(_capturedScreen))
    {
      _ = BeginCaptureAllScreensAsync();
    }
  }

  protected override void OnClosed(EventArgs e)
  {
    _isClosed = true;
    base.OnClosed(e);
  }

  private async Task BeginCaptureAllScreensAsync()
  {
    BitmapSource? bitmap = null;
    var hiddenForCapture = false;
    try
    {
      await Dispatcher.InvokeAsync(() =>
      {
        if (ShouldHideOverlayDuringBackgroundCapture(_isClosed, IsVisible))
        {
          hiddenForCapture = true;
          Hide();
        }
      });

      bitmap = await Task.Run(CaptureAllScreensBitmapSource);
      await Dispatcher.InvokeAsync(() =>
      {
        if (ShouldRestoreOverlayAfterBackgroundCapture(hiddenForCapture, _isClosed))
        {
          Show();
          Cursor = WpfCursors.Cross;
        }

        if (!ShouldAssignCapturedBackground(_isClosed, bitmap))
        {
          return;
        }

        _capturedScreen = bitmap;
        InitializeOverlayMetrics(_capturedScreen);
        Left = _metrics.OverlayBoundsDip.Left;
        Top = _metrics.OverlayBoundsDip.Top;
        Width = _metrics.OverlayBoundsDip.Width;
        Height = _metrics.OverlayBoundsDip.Height;
        ApplyBackgroundImage(_capturedScreen);
        if (_isSelecting)
        {
          UpdateDarkOverlay(new Rect(Canvas.GetLeft(SelectionRect), Canvas.GetTop(SelectionRect), SelectionRect.Width, SelectionRect.Height));
        }
        else if (_isEditMode && !_selectionBounds.IsEmpty)
        {
          UpdateDarkOverlay(_selectionBounds);
        }
        else
        {
          UpdateDarkOverlay(null);
        }
        if (_isEditMode)
        {
          RefreshSelectedImagePreview();
        }
      });
    }
    catch
    {
      // Best effort only. The overlay can still function without a frozen background.
      await Dispatcher.InvokeAsync(() =>
      {
        if (ShouldRestoreOverlayAfterBackgroundCapture(hiddenForCapture, _isClosed))
        {
          Show();
          Cursor = WpfCursors.Cross;
        }
      });
    }
  }

  private BitmapSource? CaptureAllScreensBitmapSource()
  {
    var virtualScreenPx = ScreenMetricsService.GetVirtualScreenBoundsPx();
    var left = virtualScreenPx.Left;
    var top = virtualScreenPx.Top;
    var width = virtualScreenPx.Width;
    var height = virtualScreenPx.Height;

    using var bitmap = new System.Drawing.Bitmap(width, height);
    using var graphics = System.Drawing.Graphics.FromImage(bitmap);
    graphics.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height));

    return ConvertToBitmapSource(bitmap);
  }

  internal static bool ShouldAssignCapturedBackground(bool isClosed, BitmapSource? bitmap)
  {
    return !isClosed && bitmap is not null;
  }

  internal static bool ShouldHideOverlayDuringBackgroundCapture(bool isClosed, bool isVisible)
  {
    return !isClosed && isVisible;
  }

  internal static bool ShouldRestoreOverlayAfterBackgroundCapture(bool wasHiddenForCapture, bool isClosed)
  {
    return wasHiddenForCapture && !isClosed;
  }

  internal static bool ShouldBeginBackgroundCapture(BitmapSource? initialCapturedScreen)
  {
    return initialCapturedScreen is null;
  }

  internal static string[] GetToolbarButtonOrder()
  {
    return
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
    ];
  }

  internal static BitmapSource GetOutputImage(BitmapSource baseImage, ScreenshotAnnotationSession? session)
  {
    return session is null
      ? baseImage
      : ScreenshotAnnotationRenderer.RenderComposite(baseImage, session);
  }

  internal static EditModeState CreateEditModeState(WinRect selectedRegion, Rect selectionBounds)
  {
    var annotationSession = new ScreenshotAnnotationSession(
      new WpfSize(selectedRegion.Width, selectedRegion.Height),
      new RectangleGeometry(new Rect(0, 0, selectedRegion.Width, selectedRegion.Height)));
    annotationSession.SetActiveTool(ScreenshotAnnotationTool.Brush);

    return new EditModeState(
      IsEditMode: true,
      IsAnnotating: false,
      SelectedRegion: selectedRegion,
      SelectionBounds: selectionBounds,
      AnnotationSession: annotationSession);
  }

  internal static EditModeState ResetEditModeState(EditModeState _)
  {
    return new EditModeState(
      IsEditMode: false,
      IsAnnotating: false,
      SelectedRegion: WinRect.Empty,
      SelectionBounds: Rect.Empty,
      AnnotationSession: null);
  }

  internal static EditModeState DiscardAnnotationsForLongScreenshot(EditModeState state)
  {
    state.AnnotationSession?.ClearAnnotations();
    return state with { IsAnnotating = false };
  }

  internal static bool CanBeginEditAnnotation(bool isEditMode, ScreenshotAnnotationSession? session, bool isWithinEditSurface)
  {
    return isEditMode &&
           isWithinEditSurface &&
           session is not null &&
           session.ActiveTool != ScreenshotAnnotationTool.None;
  }

  private static BitmapSource ConvertToBitmapSource(System.Drawing.Bitmap bitmap)
  {
    const double ScreenCaptureDpi = 96.0;
    var bitmapData = bitmap.LockBits(
      new WinRect(0, 0, bitmap.Width, bitmap.Height),
      System.Drawing.Imaging.ImageLockMode.ReadOnly,
      bitmap.PixelFormat);

    try
    {
      var bitmapSource = BitmapSource.Create(
        bitmapData.Width,
        bitmapData.Height,
        ScreenCaptureDpi,
        ScreenCaptureDpi,
        PixelFormats.Bgra32,
        null,
        bitmapData.Scan0,
        bitmapData.Stride * bitmapData.Height,
        bitmapData.Stride);

      bitmapSource.Freeze();
      return bitmapSource;
    }
    finally
    {
      bitmap.UnlockBits(bitmapData);
    }
  }

  private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
  {
    // Don't start a new selection if the user clicked on the toolbar.
    if (Toolbar.Visibility == Visibility.Visible && IsDescendant(Toolbar, e.OriginalSource as DependencyObject))
    {
      return;
    }

    if (_isEditMode)
    {
      var isWithinEditSurface = IsDescendant(EditSurface, e.OriginalSource as DependencyObject);
      if (!CanBeginEditAnnotation(_isEditMode, _annotationSession, isWithinEditSurface))
      {
        return;
      }

      BeginAnnotation(e.GetPosition(EditSurface));
      return;
    }

    _startPointPx = GetCursorPositionPx(e);
    _startPoint = CreateOverlayPointFromScreenPixel(_startPointPx);
    _isSelecting = true;

    SelectionRect.Visibility = Visibility.Visible;
    Toolbar.Visibility = Visibility.Collapsed;

    Canvas.SetLeft(SelectionRect, _startPoint.X);
    Canvas.SetTop(SelectionRect, _startPoint.Y);
    SelectionRect.Width = 0;
    SelectionRect.Height = 0;

    CaptureMouse();
  }

  private void OnMouseMove(object sender, WpfMouseEventArgs e)
  {
    if (_isAnnotating)
    {
      UpdateAnnotationPreview(e.GetPosition(EditSurface));
      return;
    }

    if (!_isSelecting)
    {
      return;
    }

    var currentPointPx = GetCursorPositionPx(e);
    var selectionBounds = CreateSelectionBoundsFromCursorPixels(_startPointPx, currentPointPx);
    var x = selectionBounds.X;
    var y = selectionBounds.Y;
    var width = selectionBounds.Width;
    var height = selectionBounds.Height;

    Canvas.SetLeft(SelectionRect, x);
    Canvas.SetTop(SelectionRect, y);
    SelectionRect.Width = width;
    SelectionRect.Height = height;

    UpdateDarkOverlay(new Rect(x, y, width, height));

    var previewRegion = CreatePixelSelectionRegion(selectionBounds);
    SizeText.Text = $"{previewRegion.Width} x {previewRegion.Height}";
    SizeIndicator.Visibility = Visibility.Visible;

    Canvas.SetLeft(SizeIndicator, x);
    Canvas.SetTop(SizeIndicator, y - 28);
  }

  private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
  {
    if (_isAnnotating)
    {
      CommitAnnotation(e.GetPosition(EditSurface));
      return;
    }

    if (!_isSelecting)
    {
      return;
    }

    _isSelecting = false;
    ReleaseMouseCapture();

    var currentPointPx = GetCursorPositionPx(e);
    var selectionBounds = CreateSelectionBoundsFromCursorPixels(_startPointPx, currentPointPx);
    var x = selectionBounds.X;
    var y = selectionBounds.Y;
    var width = selectionBounds.Width;
    var height = selectionBounds.Height;

    if (width < 10 || height < 10)
    {
      SelectionRect.Visibility = Visibility.Collapsed;
      SizeIndicator.Visibility = Visibility.Collapsed;
      UpdateDarkOverlay(null);
      return;
    }

    _selectedRegion = CreatePixelSelectionRegion(selectionBounds);
    EnterEditMode(selectionBounds);
  }

  private WinRect CreatePixelSelectionRegion(Rect selectionBounds)
  {
    var backgroundDisplayBounds = GetDisplayedElementBounds(BackgroundImage);
    if (_capturedScreen is not null && backgroundDisplayBounds.Width > 0 && backgroundDisplayBounds.Height > 0)
    {
      return CreatePixelSelectionRegionFromDisplayedElement(
        selectionBounds,
        backgroundDisplayBounds,
        _metrics.VirtualScreenPx,
        new WpfSize(_capturedScreen.PixelWidth, _capturedScreen.PixelHeight));
    }

    return CreatePixelSelectionRegionFromSelectionBounds(
      selectionBounds,
      _metrics.VirtualScreenPx,
      _metrics.DpiScaleX,
      _metrics.DpiScaleY);
  }

  internal static WinRect CreatePixelSelectionRegionFromSelectionBounds(
    Rect selectionBoundsDip,
    WinRect virtualScreenPx,
    double dpiScaleX,
    double dpiScaleY)
  {
    var safeDpiScaleX = dpiScaleX <= 0 ? 1.0 : dpiScaleX;
    var safeDpiScaleY = dpiScaleY <= 0 ? 1.0 : dpiScaleY;
    var left = virtualScreenPx.Left + (int)Math.Floor(selectionBoundsDip.Left * safeDpiScaleX);
    var top = virtualScreenPx.Top + (int)Math.Floor(selectionBoundsDip.Top * safeDpiScaleY);
    var right = virtualScreenPx.Left + (int)Math.Ceiling(selectionBoundsDip.Right * safeDpiScaleX);
    var bottom = virtualScreenPx.Top + (int)Math.Ceiling(selectionBoundsDip.Bottom * safeDpiScaleY);

    return new WinRect(
      left,
      top,
      Math.Max(1, right - left),
      Math.Max(1, bottom - top));
  }

  internal static WinRect CreatePixelSelectionRegionFromDisplayedBackground(
    Rect selectionBoundsDip,
    WinRect virtualScreenPx,
    WpfSize capturedPixelSize,
    WpfSize backgroundDisplaySizeDip)
  {
    return CreatePixelSelectionRegionFromDisplayedElement(
      selectionBoundsDip,
      new Rect(0, 0, backgroundDisplaySizeDip.Width, backgroundDisplaySizeDip.Height),
      virtualScreenPx,
      capturedPixelSize);
  }

  internal static WinRect CreatePixelSelectionRegionFromDisplayedElement(
    Rect selectionBoundsDip,
    Rect displayedElementBoundsDip,
    WinRect virtualScreenPx,
    WpfSize capturedPixelSize)
  {
    var safeDisplayWidth = displayedElementBoundsDip.Width <= 0 ? 1.0 : displayedElementBoundsDip.Width;
    var safeDisplayHeight = displayedElementBoundsDip.Height <= 0 ? 1.0 : displayedElementBoundsDip.Height;
    var scaleX = Math.Max(1, capturedPixelSize.Width) / safeDisplayWidth;
    var scaleY = Math.Max(1, capturedPixelSize.Height) / safeDisplayHeight;

    var localLeft = Math.Clamp(selectionBoundsDip.Left - displayedElementBoundsDip.Left, 0, safeDisplayWidth);
    var localTop = Math.Clamp(selectionBoundsDip.Top - displayedElementBoundsDip.Top, 0, safeDisplayHeight);
    var localRight = Math.Clamp(selectionBoundsDip.Right - displayedElementBoundsDip.Left, 0, safeDisplayWidth);
    var localBottom = Math.Clamp(selectionBoundsDip.Bottom - displayedElementBoundsDip.Top, 0, safeDisplayHeight);

    var left = virtualScreenPx.Left + (int)Math.Floor(localLeft * scaleX);
    var top = virtualScreenPx.Top + (int)Math.Floor(localTop * scaleY);
    var right = virtualScreenPx.Left + (int)Math.Ceiling(localRight * scaleX);
    var bottom = virtualScreenPx.Top + (int)Math.Ceiling(localBottom * scaleY);

    return new WinRect(
      left,
      top,
      Math.Max(1, right - left),
      Math.Max(1, bottom - top));
  }

  internal static Rect CreateSelectionBoundsFromScreenPixels(
    DrawingPoint startPx,
    DrawingPoint currentPx,
    WinRect virtualScreenPx,
    double dpiScaleX,
    double dpiScaleY)
  {
    var safeDpiScaleX = dpiScaleX <= 0 ? 1.0 : dpiScaleX;
    var safeDpiScaleY = dpiScaleY <= 0 ? 1.0 : dpiScaleY;
    var normalizedPx = CreatePixelSelectionRegionFromScreenPixels(startPx, currentPx, virtualScreenPx);

    return new Rect(
      (normalizedPx.Left - virtualScreenPx.Left) / safeDpiScaleX,
      (normalizedPx.Top - virtualScreenPx.Top) / safeDpiScaleY,
      normalizedPx.Width / safeDpiScaleX,
      normalizedPx.Height / safeDpiScaleY);
  }

  internal static WinRect CreatePixelSelectionRegionFromScreenPixels(
    DrawingPoint startPx,
    DrawingPoint currentPx,
    WinRect virtualScreenPx)
  {
    var leftBoundary = virtualScreenPx.Left;
    var topBoundary = virtualScreenPx.Top;
    var rightBoundary = virtualScreenPx.Right;
    var bottomBoundary = virtualScreenPx.Bottom;

    var startX = Math.Clamp(startPx.X, leftBoundary, rightBoundary);
    var startY = Math.Clamp(startPx.Y, topBoundary, bottomBoundary);
    var currentX = Math.Clamp(currentPx.X, leftBoundary, rightBoundary);
    var currentY = Math.Clamp(currentPx.Y, topBoundary, bottomBoundary);

    var left = Math.Min(startX, currentX);
    var top = Math.Min(startY, currentY);
    var right = Math.Max(startX, currentX);
    var bottom = Math.Max(startY, currentY);

    return new WinRect(
      left,
      top,
      Math.Max(1, right - left),
      Math.Max(1, bottom - top));
  }

  internal static WpfSize CreateBackgroundImageSizeDip(BitmapSource capturedScreen, double dpiScaleX, double dpiScaleY)
  {
    var safeDpiScaleX = dpiScaleX <= 0 ? 1.0 : dpiScaleX;
    var safeDpiScaleY = dpiScaleY <= 0 ? 1.0 : dpiScaleY;

    return new WpfSize(
      Math.Max(1, capturedScreen.PixelWidth / safeDpiScaleX),
      Math.Max(1, capturedScreen.PixelHeight / safeDpiScaleY));
  }

  internal static Rect CreateOverlayBoundsDip(WinRect virtualScreenPx, double dpiScaleX, double dpiScaleY)
  {
    var safeDpiScaleX = dpiScaleX <= 0 ? 1.0 : dpiScaleX;
    var safeDpiScaleY = dpiScaleY <= 0 ? 1.0 : dpiScaleY;

    return new Rect(
      virtualScreenPx.Left / safeDpiScaleX,
      virtualScreenPx.Top / safeDpiScaleY,
      Math.Max(1, virtualScreenPx.Width / safeDpiScaleX),
      Math.Max(1, virtualScreenPx.Height / safeDpiScaleY));
  }

  internal static WinRect CreateVirtualScreenPixelBounds(
    double leftDip,
    double topDip,
    double widthDip,
    double heightDip,
    double dpiScaleX,
    double dpiScaleY)
  {
    var safeDpiScaleX = dpiScaleX <= 0 ? 1.0 : dpiScaleX;
    var safeDpiScaleY = dpiScaleY <= 0 ? 1.0 : dpiScaleY;

    return new WinRect(
      (int)Math.Round(leftDip * safeDpiScaleX),
      (int)Math.Round(topDip * safeDpiScaleY),
      Math.Max(1, (int)Math.Round(widthDip * safeDpiScaleX)),
      Math.Max(1, (int)Math.Round(heightDip * safeDpiScaleY)));
  }

  internal static OverlayPresentation CreateOverlayPresentation(
    WinRect virtualScreenPx,
    WpfSize capturedPixelSize,
    double dpiScaleX,
    double dpiScaleY)
  {
    var safeDpiScaleX = dpiScaleX <= 0 ? 1.0 : dpiScaleX;
    var safeDpiScaleY = dpiScaleY <= 0 ? 1.0 : dpiScaleY;
    return new OverlayPresentation(
      CreateOverlayBoundsDip(virtualScreenPx, dpiScaleX, dpiScaleY),
      new WpfSize(
        Math.Max(1, capturedPixelSize.Width / safeDpiScaleX),
        Math.Max(1, capturedPixelSize.Height / safeDpiScaleY)));
  }

  internal static OverlayMetrics CreateOverlayMetrics(
    WinRect virtualScreenPx,
    WpfSize capturedPixelSize,
    double dpiScaleX,
    double dpiScaleY)
  {
    var presentation = CreateOverlayPresentation(virtualScreenPx, capturedPixelSize, dpiScaleX, dpiScaleY);
    var safeDpiScaleX = dpiScaleX <= 0 ? 1.0 : dpiScaleX;
    var safeDpiScaleY = dpiScaleY <= 0 ? 1.0 : dpiScaleY;

    return new OverlayMetrics(
      virtualScreenPx,
      safeDpiScaleX,
      safeDpiScaleY,
      presentation.OverlayBoundsDip,
      presentation.BackgroundSizeDip);
  }

  internal static DpiScale CreateSelectionDpiScale(
    WinRect selectedRegion,
    Rect selectionBoundsDip,
    double fallbackDpiScaleX,
    double fallbackDpiScaleY)
  {
    var safeFallbackX = fallbackDpiScaleX <= 0 ? 1.0 : fallbackDpiScaleX;
    var safeFallbackY = fallbackDpiScaleY <= 0 ? 1.0 : fallbackDpiScaleY;
    var scaleX = selectionBoundsDip.Width > 0
      ? selectedRegion.Width / selectionBoundsDip.Width
      : safeFallbackX;
    var scaleY = selectionBoundsDip.Height > 0
      ? selectedRegion.Height / selectionBoundsDip.Height
      : safeFallbackY;

    return new DpiScale(
      scaleX > 0 ? scaleX : safeFallbackX,
      scaleY > 0 ? scaleY : safeFallbackY);
  }

  private void InitializeOverlayMetrics(BitmapSource? capturedScreen)
  {
    _virtualScreenPx = ScreenMetricsService.GetVirtualScreenBoundsPx();
    var dpi = ScreenMetricsService.GetDpiScaleForBounds(_virtualScreenPx, _dpiScaleX, _dpiScaleY);
    var capturedPixelSize = capturedScreen is null
      ? new WpfSize(_virtualScreenPx.Width, _virtualScreenPx.Height)
      : new WpfSize(capturedScreen.PixelWidth, capturedScreen.PixelHeight);

    _metrics = CreateOverlayMetrics(_virtualScreenPx, capturedPixelSize, dpi.DpiScaleX, dpi.DpiScaleY);
    _dpiScaleX = _metrics.DpiScaleX;
    _dpiScaleY = _metrics.DpiScaleY;
  }

  private DpiScale GetCurrentSelectionDpiScale()
  {
    return CreateSelectionDpiScale(_selectedRegion, _selectionBounds, _metrics.DpiScaleX, _metrics.DpiScaleY);
  }

  private DrawingPoint GetCursorPositionPx(WpfMouseEventArgs e)
  {
    if (NativeMethods.GetCursorPos(out var point))
    {
      return new DrawingPoint(point.X, point.Y);
    }

    var overlayPoint = e.GetPosition(this);
    return new DrawingPoint(
      _metrics.VirtualScreenPx.Left + (int)Math.Round(overlayPoint.X * _metrics.DpiScaleX),
      _metrics.VirtualScreenPx.Top + (int)Math.Round(overlayPoint.Y * _metrics.DpiScaleY));
  }

  private WpfPoint CreateOverlayPointFromScreenPixel(DrawingPoint pointPx)
  {
    return new WpfPoint(
      (pointPx.X - _metrics.VirtualScreenPx.Left) / _metrics.DpiScaleX,
      (pointPx.Y - _metrics.VirtualScreenPx.Top) / _metrics.DpiScaleY);
  }

  private Rect CreateSelectionBoundsFromCursorPixels(DrawingPoint startPx, DrawingPoint currentPx)
  {
    return CreateSelectionBoundsFromScreenPixels(
      startPx,
      currentPx,
      _metrics.VirtualScreenPx,
      _metrics.DpiScaleX,
      _metrics.DpiScaleY);
  }

  private void OnMouseDown(object sender, MouseButtonEventArgs e)
  {
    if (e.ChangedButton == MouseButton.Middle && _selectedRegion.Width > 0)
    {
      PinSelection();
    }
  }

  private void OnKeyDown(object sender, WpfKeyEventArgs e)
  {
    if (e.Key == Key.Escape)
    {
      Close();
    }
    else if (e.Key == Key.Enter && _selectedRegion.Width > 0)
    {
      CopySelection();
      Close();
    }
  }

  private void UpdateDarkOverlay(Rect? selection)
  {
    var fullRect = new RectangleGeometry(new Rect(0, 0, Width, Height));

    if (selection.HasValue && selection.Value.Width > 0 && selection.Value.Height > 0)
    {
      var selectionRect = new RectangleGeometry(selection.Value);
      DarkOverlay.Data = new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, selectionRect);
    }
    else
    {
      DarkOverlay.Data = fullRect;
    }
  }

  private void ApplyBackgroundImage(BitmapSource? capturedScreen)
  {
    BackgroundImage.Source = capturedScreen;
    if (capturedScreen is null)
    {
      return;
    }

    BackgroundImage.Width = _metrics.BackgroundSizeDip.Width;
    BackgroundImage.Height = _metrics.BackgroundSizeDip.Height;
  }

  private BitmapSource? CropSelection()
  {
    if (_capturedScreen == null || _selectedRegion.Width <= 0)
    {
      return null;
    }

    var imageStartX = _metrics.VirtualScreenPx.Left;
    var imageStartY = _metrics.VirtualScreenPx.Top;

    var cropX = _selectedRegion.X - imageStartX;
    var cropY = _selectedRegion.Y - imageStartY;

    cropX = Math.Max(0, cropX);
    cropY = Math.Max(0, cropY);
    var cropWidth = Math.Min(_selectedRegion.Width, _capturedScreen.PixelWidth - cropX);
    var cropHeight = Math.Min(_selectedRegion.Height, _capturedScreen.PixelHeight - cropY);

    if (cropWidth <= 0 || cropHeight <= 0)
    {
      return null;
    }

    var croppedBitmap = new CroppedBitmap(_capturedScreen, new Int32Rect(cropX, cropY, cropWidth, cropHeight));
    croppedBitmap.Freeze();
    return croppedBitmap;
  }

  private void BtnPin_Click(object sender, RoutedEventArgs e)
  {
    PinSelection();
  }

  private void BtnCopy_Click(object sender, RoutedEventArgs e)
  {
    CopySelection();
    Close();
  }

  private void BtnSave_Click(object sender, RoutedEventArgs e)
  {
    SaveSelection();
    Close();
  }

  private void BtnCancel_Click(object sender, RoutedEventArgs e)
  {
    Close();
  }

  private void BtnFreeform_Click(object sender, RoutedEventArgs e)
  {
    Close();
    _onFreeformRequested?.Invoke();
  }

  private void BtnLongScreenshot_Click(object sender, RoutedEventArgs e)
  {
    if (_selectedRegion.Width <= 0 || _selectedRegion.Height <= 0)
    {
      return;
    }

    DiscardAnnotations();
    var dpiScale = GetCurrentSelectionDpiScale();
    Close();
    _onLongScreenshotRequested?.Invoke(_selectedRegion, dpiScale.DpiScaleX, dpiScale.DpiScaleY);
  }

  private void BtnGif_Click(object sender, RoutedEventArgs e)
  {
    if (_selectedRegion.Width <= 0 || _selectedRegion.Height <= 0)
    {
      return;
    }

    DiscardAnnotations();
    var dpiScale = GetCurrentSelectionDpiScale();
    Close();
    _onGifRecordingRequested?.Invoke(_selectedRegion, dpiScale.DpiScaleX, dpiScale.DpiScaleY);
  }

  private void BtnBrush_Click(object sender, RoutedEventArgs e)
  {
    SetActiveAnnotationTool(ScreenshotAnnotationTool.Brush);
  }

  private void BtnRectangle_Click(object sender, RoutedEventArgs e)
  {
    SetActiveAnnotationTool(ScreenshotAnnotationTool.Rectangle);
  }

  private void BtnMosaic_Click(object sender, RoutedEventArgs e)
  {
    SetActiveAnnotationTool(ScreenshotAnnotationTool.Mosaic);
  }

  private void BtnUndo_Click(object sender, RoutedEventArgs e)
  {
    if (_annotationSession?.Undo() == true)
    {
      RefreshSelectedImagePreview();
      UpdateAnnotationToolbarState();
    }
  }

  private void BtnRedraw_Click(object sender, RoutedEventArgs e)
  {
    ResetSelection();
  }

  private void PinSelection()
  {
    var output = GetCurrentOutputImage();
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

    var dpiScale = GetCurrentSelectionDpiScale();
    _onPinRequested(output, _selectedRegion, dpiScale.DpiScaleX, dpiScale.DpiScaleY);
    Close();
  }

  private void CopySelection()
  {
    var output = GetCurrentOutputImage();
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

  private void SaveSelection()
  {
    var output = GetCurrentOutputImage();
    if (output == null)
    {
      return;
    }

    if (_settings.ScreenshotAutoCopy)
    {
      CopyToClipboard(output);
    }

    SaveToFile(output);
  }

  private void EnterEditMode(Rect selectionBounds)
  {
    ApplyEditModeState(CreateEditModeState(_selectedRegion, selectionBounds));
    _selectedImage = null;

    Canvas.SetLeft(SelectionRect, selectionBounds.X);
    Canvas.SetTop(SelectionRect, selectionBounds.Y);
    SelectionRect.Width = selectionBounds.Width;
    SelectionRect.Height = selectionBounds.Height;
    SelectionRect.Visibility = Visibility.Visible;

    Canvas.SetLeft(EditSurface, selectionBounds.X);
    Canvas.SetTop(EditSurface, selectionBounds.Y);
    EditSurface.Width = selectionBounds.Width;
    EditSurface.Height = selectionBounds.Height;
    EditSurface.Visibility = Visibility.Visible;
    SelectedImagePreview.Width = selectionBounds.Width;
    SelectedImagePreview.Height = selectionBounds.Height;

    SizeIndicator.Visibility = Visibility.Collapsed;
    ClearAnnotationPreview();
    RefreshSelectedImagePreview();
    PositionToolbar(selectionBounds);
    Toolbar.Visibility = Visibility.Visible;

    SetActiveAnnotationTool(ScreenshotAnnotationTool.Brush);
  }

  private void PositionToolbar(Rect selectionBounds)
  {
    var toolbarY = selectionBounds.Bottom + 10;
    if (toolbarY + 40 > Height)
    {
      toolbarY = selectionBounds.Top - 50;
    }

    Canvas.SetLeft(Toolbar, selectionBounds.Left);
    Canvas.SetTop(Toolbar, toolbarY);
  }

  private void SetActiveAnnotationTool(ScreenshotAnnotationTool tool)
  {
    if (_annotationSession is null)
    {
      return;
    }

    _annotationSession.SetActiveTool(tool);
    UpdateAnnotationToolbarState();
    Cursor = tool switch
    {
      ScreenshotAnnotationTool.Brush or ScreenshotAnnotationTool.Mosaic => WpfCursors.Pen,
      ScreenshotAnnotationTool.Rectangle => WpfCursors.Cross,
      _ => WpfCursors.Arrow,
    };
  }

  private void UpdateAnnotationToolbarState()
  {
    if (_annotationSession is null)
    {
      return;
    }

    var selectedBackground = new SolidColorBrush(WpfColor.FromArgb(102, 0, 183, 255));
    var transparentBackground = WpfBrushes.Transparent;

    BtnBrush.Background = _annotationSession.ActiveTool == ScreenshotAnnotationTool.Brush ? selectedBackground : transparentBackground;
    BtnRectangle.Background = _annotationSession.ActiveTool == ScreenshotAnnotationTool.Rectangle ? selectedBackground : transparentBackground;
    BtnMosaic.Background = _annotationSession.ActiveTool == ScreenshotAnnotationTool.Mosaic ? selectedBackground : transparentBackground;
    BtnUndo.IsEnabled = _annotationSession.Operations.Count > 0;
  }

  private void BeginAnnotation(WpfPoint point)
  {
    if (_annotationSession is null)
    {
      return;
    }

    _isAnnotating = true;
    _annotationPoints.Clear();
    _annotationPoints.Add(GetClampedEditSurfacePoint(point));

    ClearAnnotationPreview();
    CaptureMouse();
  }

  private void UpdateAnnotationPreview(WpfPoint point)
  {
    if (_annotationSession is null || !_isAnnotating)
    {
      return;
    }

    var currentPoint = GetClampedEditSurfacePoint(point);

    if (_annotationSession.ActiveTool is ScreenshotAnnotationTool.Brush or ScreenshotAnnotationTool.Mosaic)
    {
      if (_annotationPoints.Count == 0 || (currentPoint - _annotationPoints[^1]).Length >= 1.5)
      {
        _annotationPoints.Add(currentPoint);
        UpdateStrokePreview();
      }

      return;
    }

    UpdateRectanglePreview(_annotationPoints[0], currentPoint);
  }

  private void CommitAnnotation(WpfPoint point)
  {
    if (_annotationSession is null || !_isAnnotating)
    {
      return;
    }

    _isAnnotating = false;
    ReleaseMouseCapture();

    var endPoint = GetClampedEditSurfacePoint(point);
    var previewDisplaySize = GetSelectedImagePreviewDisplaySize();

    switch (_annotationSession.ActiveTool)
    {
      case ScreenshotAnnotationTool.Brush:
      case ScreenshotAnnotationTool.Mosaic:
        if (_annotationPoints.Count == 1 && (_annotationPoints[0] - endPoint).Length >= 1.5)
        {
          _annotationPoints.Add(endPoint);
        }

        if (_annotationPoints.Count >= 2)
        {
          var imagePoints = new WpfPoint[_annotationPoints.Count];
          for (var index = 0; index < _annotationPoints.Count; index++)
          {
            imagePoints[index] = CreateAnnotationImagePoint(
              _annotationPoints[index],
              _annotationSession.CanvasSize,
              previewDisplaySize);
          }

          _annotationSession.CommitStroke(
            imagePoints,
            _annotationSession.ActiveTool == ScreenshotAnnotationTool.Mosaic ? Colors.Transparent : Colors.DeepSkyBlue,
            GetStrokeThickness(_annotationSession.ActiveTool));
        }
        break;

      case ScreenshotAnnotationTool.Rectangle:
        var previewBounds = CreateNormalizedRect(_annotationPoints[0], endPoint);
        if (previewBounds.Width >= 1 && previewBounds.Height >= 1)
        {
          _annotationSession.CommitRectangle(
            CreateAnnotationImageBounds(
              previewBounds,
              _annotationSession.CanvasSize,
              previewDisplaySize),
            Colors.DeepSkyBlue,
            GetStrokeThickness(ScreenshotAnnotationTool.Rectangle));
        }
        break;
    }

    _annotationPoints.Clear();
    ClearAnnotationPreview();
    RefreshSelectedImagePreview();
    UpdateAnnotationToolbarState();
  }

  private void UpdateStrokePreview()
  {
    if (_annotationPoints.Count < 2 || _annotationSession is null)
    {
      AnnotationStrokePreview.Visibility = Visibility.Collapsed;
      return;
    }

    var figure = new PathFigure { StartPoint = _annotationPoints[0], IsClosed = false, IsFilled = false };
    for (var index = 1; index < _annotationPoints.Count; index++)
    {
      figure.Segments.Add(new LineSegment(_annotationPoints[index], true));
    }

    AnnotationStrokePreview.Data = new PathGeometry([figure]);
    AnnotationStrokePreview.Stroke = _annotationSession.ActiveTool == ScreenshotAnnotationTool.Mosaic
      ? new SolidColorBrush(WpfColor.FromRgb(255, 170, 0))
      : new SolidColorBrush(Colors.DeepSkyBlue);
    AnnotationStrokePreview.StrokeThickness = _annotationSession.ActiveTool == ScreenshotAnnotationTool.Mosaic
      ? MosaicPreviewThickness
      : BrushPreviewThickness;
    AnnotationStrokePreview.Visibility = Visibility.Visible;
  }

  private void UpdateRectanglePreview(WpfPoint startPoint, WpfPoint endPoint)
  {
    var bounds = CreateNormalizedRect(startPoint, endPoint);
    AnnotationRectanglePreview.Stroke = new SolidColorBrush(Colors.DeepSkyBlue);
    AnnotationRectanglePreview.StrokeThickness = RectanglePreviewThickness;
    AnnotationRectanglePreview.Width = bounds.Width;
    AnnotationRectanglePreview.Height = bounds.Height;
    Canvas.SetLeft(AnnotationRectanglePreview, bounds.X);
    Canvas.SetTop(AnnotationRectanglePreview, bounds.Y);
    AnnotationRectanglePreview.Visibility = Visibility.Visible;
  }

  private void ClearAnnotationPreview()
  {
    AnnotationStrokePreview.Data = null;
    AnnotationStrokePreview.Visibility = Visibility.Collapsed;
    AnnotationRectanglePreview.Visibility = Visibility.Collapsed;
    AnnotationRectanglePreview.Width = 0;
    AnnotationRectanglePreview.Height = 0;
  }

  private void RefreshSelectedImagePreview()
  {
    _selectedImage = CropSelection();
    SelectedImagePreview.Source = _selectedImage is null
      ? null
      : GetOutputImage(_selectedImage, _annotationSession);
  }

  private BitmapSource? GetCurrentOutputImage()
  {
    var cropped = CropSelection();
    if (cropped == null)
    {
      return null;
    }

    _selectedImage = cropped;
    return GetOutputImage(cropped, _annotationSession);
  }

  private void DiscardAnnotations()
  {
    if (_annotationSession is null)
    {
      return;
    }

    ApplyEditModeState(DiscardAnnotationsForLongScreenshot(CaptureEditModeState()));
    _annotationPoints.Clear();
    ClearAnnotationPreview();
  }

  private void ResetSelection()
  {
    _isSelecting = false;
    _annotationPoints.Clear();
    ApplyEditModeState(ResetEditModeState(CaptureEditModeState()));
    _selectedImage = null;

    ReleaseMouseCapture();
    SelectionRect.Visibility = Visibility.Collapsed;
    EditSurface.Visibility = Visibility.Collapsed;
    Toolbar.Visibility = Visibility.Collapsed;
    SizeIndicator.Visibility = Visibility.Collapsed;
    SelectedImagePreview.Source = null;
    ClearAnnotationPreview();
    UpdateDarkOverlay(null);
    Cursor = WpfCursors.Cross;
  }

  private EditModeState CaptureEditModeState()
  {
    return new EditModeState(_isEditMode, _isAnnotating, _selectedRegion, _selectionBounds, _annotationSession);
  }

  private void ApplyEditModeState(EditModeState state)
  {
    _isEditMode = state.IsEditMode;
    _isAnnotating = state.IsAnnotating;
    _selectedRegion = state.SelectedRegion;
    _selectionBounds = state.SelectionBounds;
    _annotationSession = state.AnnotationSession;
  }

  private WpfPoint GetClampedEditSurfacePoint(WpfPoint point)
  {
    var previewDisplaySize = GetSelectedImagePreviewDisplaySize();
    var editX = point.X;
    var editY = point.Y;

    return new WpfPoint(
      Math.Clamp(editX, 0, Math.Max(0, previewDisplaySize.Width)),
      Math.Clamp(editY, 0, Math.Max(0, previewDisplaySize.Height)));
  }

  private double GetEditScaleX()
  {
    var previewDisplaySize = GetSelectedImagePreviewDisplaySize();
    return previewDisplaySize.Width <= 0 ? 1 : _annotationSession?.CanvasSize.Width / previewDisplaySize.Width ?? 1;
  }

  private double GetEditScaleY()
  {
    var previewDisplaySize = GetSelectedImagePreviewDisplaySize();
    return previewDisplaySize.Height <= 0 ? 1 : _annotationSession?.CanvasSize.Height / previewDisplaySize.Height ?? 1;
  }

  private double GetStrokeThickness(ScreenshotAnnotationTool tool)
  {
    var scale = (GetEditScaleX() + GetEditScaleY()) / 2.0;
    return tool switch
    {
      ScreenshotAnnotationTool.Mosaic => MosaicPreviewThickness * scale,
      ScreenshotAnnotationTool.Rectangle => RectanglePreviewThickness * scale,
      _ => BrushPreviewThickness * scale,
    };
  }

  private static Rect CreateNormalizedRect(WpfPoint startPoint, WpfPoint endPoint)
  {
    return new Rect(
      Math.Min(startPoint.X, endPoint.X),
      Math.Min(startPoint.Y, endPoint.Y),
      Math.Abs(endPoint.X - startPoint.X),
      Math.Abs(endPoint.Y - startPoint.Y));
  }

  private static WpfPoint ToImagePoint(WpfPoint editPoint, double scaleX, double scaleY)
  {
    return new WpfPoint(editPoint.X * scaleX, editPoint.Y * scaleY);
  }

  internal static WpfPoint CreateAnnotationImagePoint(
    WpfPoint editPoint,
    WpfSize canvasSizePx,
    WpfSize previewDisplaySizeDip)
  {
    var safePreviewWidth = previewDisplaySizeDip.Width <= 0 ? 1.0 : previewDisplaySizeDip.Width;
    var safePreviewHeight = previewDisplaySizeDip.Height <= 0 ? 1.0 : previewDisplaySizeDip.Height;
    var scaleX = canvasSizePx.Width / safePreviewWidth;
    var scaleY = canvasSizePx.Height / safePreviewHeight;
    return ToImagePoint(editPoint, scaleX, scaleY);
  }

  internal static Rect CreateAnnotationImageBounds(
    Rect previewBoundsDip,
    WpfSize canvasSizePx,
    WpfSize previewDisplaySizeDip)
  {
    var topLeft = CreateAnnotationImagePoint(
      new WpfPoint(previewBoundsDip.Left, previewBoundsDip.Top),
      canvasSizePx,
      previewDisplaySizeDip);
    var bottomRight = CreateAnnotationImagePoint(
      new WpfPoint(previewBoundsDip.Right, previewBoundsDip.Bottom),
      canvasSizePx,
      previewDisplaySizeDip);

    return new Rect(
      topLeft.X,
      topLeft.Y,
      Math.Max(0, bottomRight.X - topLeft.X),
      Math.Max(0, bottomRight.Y - topLeft.Y));
  }

  private Rect GetDisplayedElementBounds(FrameworkElement element)
  {
    var actualWidth = element.ActualWidth > 0 ? element.ActualWidth : element.Width;
    var actualHeight = element.ActualHeight > 0 ? element.ActualHeight : element.Height;
    if (actualWidth <= 0 || actualHeight <= 0)
    {
      return Rect.Empty;
    }

    var origin = element.TranslatePoint(new WpfPoint(0, 0), this);
    return new Rect(origin.X, origin.Y, actualWidth, actualHeight);
  }

  private WpfSize GetSelectedImagePreviewDisplaySize()
  {
    var actualWidth = SelectedImagePreview.ActualWidth > 0 ? SelectedImagePreview.ActualWidth : EditSurface.ActualWidth;
    var actualHeight = SelectedImagePreview.ActualHeight > 0 ? SelectedImagePreview.ActualHeight : EditSurface.ActualHeight;
    var width = actualWidth > 0 ? actualWidth : Math.Max(0, EditSurface.Width);
    var height = actualHeight > 0 ? actualHeight : Math.Max(0, EditSurface.Height);
    return new WpfSize(width, height);
  }

  private static bool IsDescendant(DependencyObject ancestor, DependencyObject? child)
  {
    while (child is not null)
    {
      if (ReferenceEquals(child, ancestor))
      {
        return true;
      }

      child = System.Windows.Media.VisualTreeHelper.GetParent(child);
    }

    return false;
  }

  private static void CopyToClipboard(BitmapSource image)
  {
    try
    {
      WpfClipboard.SetImage(image);
    }
    catch
    {
      // Clipboard may be locked.
    }
  }

  private void SaveToFile(BitmapSource image)
  {
    try
    {
      var dialog = new WpfSaveFileDialog
      {
        Filter = LocalizationService.GetString("FileDialog_ImageFilter", "PNG Image|*.png|JPEG Image|*.jpg|BMP Image|*.bmp"),
        DefaultExt = ".png",
        FileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}",
        InitialDirectory = string.IsNullOrWhiteSpace(_settings.ScreenshotSavePath)
          ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
          : _settings.ScreenshotSavePath,
      };

      if (dialog.ShowDialog() != true)
      {
        return;
      }

      var ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
      BitmapEncoder encoder = ext switch
      {
        ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
        ".bmp" => new BmpBitmapEncoder(),
        _ => new PngBitmapEncoder(),
      };

      encoder.Frames.Add(BitmapFrame.Create(image));
      using var fileStream = new FileStream(dialog.FileName, FileMode.Create);
      encoder.Save(fileStream);
    }
    catch
    {
      // Save failed.
    }
  }
}
