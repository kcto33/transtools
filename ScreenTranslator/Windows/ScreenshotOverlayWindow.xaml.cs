using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Forms;

using ScreenTranslator.Models;
using ScreenTranslator.Services;

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

public sealed partial class ScreenshotOverlayWindow : Window
{
  private const double BrushPreviewThickness = 3;
  private const double RectanglePreviewThickness = 3;
  private const double MosaicPreviewThickness = 12;

  private readonly AppSettings _settings;
  private readonly Action<BitmapSource, WinRect, double, double> _onPinRequested;
  private readonly Action? _onFreeformRequested;
  private readonly Action<WinRect, double, double>? _onLongScreenshotRequested;
  private readonly List<WpfPoint> _annotationPoints = [];

  private WpfPoint _startPoint;
  private bool _isSelecting;
  private bool _isEditMode;
  private bool _isAnnotating;
  private WinRect _selectedRegion;
  private Rect _selectionBounds = Rect.Empty;
  private BitmapSource? _capturedScreen;
  private BitmapSource? _selectedImage;
  private ScreenshotAnnotationSession? _annotationSession;
  private System.Drawing.Rectangle _virtualScreenPx;
  private double _dpiScaleX = 1.0;
  private double _dpiScaleY = 1.0;
  private bool _isClosed;

  public ScreenshotOverlayWindow(
    AppSettings settings,
    Action<BitmapSource, WinRect, double, double> onPinRequested,
    Action? onFreeformRequested = null,
    Action<WinRect, double, double>? onLongScreenshotRequested = null)
  {
    _settings = settings;
    _onPinRequested = onPinRequested;
    _onFreeformRequested = onFreeformRequested;
    _onLongScreenshotRequested = onLongScreenshotRequested;

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
    var source = PresentationSource.FromVisual(this);
    if (source?.CompositionTarget != null)
    {
      _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
      _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
    }

    _virtualScreenPx = SystemInformation.VirtualScreen;

    Left = SystemParameters.VirtualScreenLeft;
    Top = SystemParameters.VirtualScreenTop;
    Width = SystemParameters.VirtualScreenWidth;
    Height = SystemParameters.VirtualScreenHeight;

    UpdateDarkOverlay(null);

    Focus();
    Cursor = WpfCursors.Cross;

    _ = BeginCaptureAllScreensAsync();
  }

  protected override void OnClosed(EventArgs e)
  {
    _isClosed = true;
    base.OnClosed(e);
  }

  private async Task BeginCaptureAllScreensAsync()
  {
    BitmapSource? bitmap = null;
    try
    {
      bitmap = await Task.Run(CaptureAllScreensBitmapSource);
      await Dispatcher.InvokeAsync(() =>
      {
        if (!ShouldAssignCapturedBackground(_isClosed, bitmap))
        {
          return;
        }

        _capturedScreen = bitmap;
        BackgroundImage.Source = _capturedScreen;
        if (_isEditMode)
        {
          RefreshSelectedImagePreview();
        }
      });
    }
    catch
    {
      // Best effort only. The overlay can still function without a frozen background.
    }
  }

  private BitmapSource? CaptureAllScreensBitmapSource()
  {
    var left = _virtualScreenPx.Left;
    var top = _virtualScreenPx.Top;
    var width = _virtualScreenPx.Width;
    var height = _virtualScreenPx.Height;

    using var bitmap = new System.Drawing.Bitmap(width, height);
    using var graphics = System.Drawing.Graphics.FromImage(bitmap);
    graphics.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height));

    return ConvertToBitmapSource(bitmap);
  }

  internal static bool ShouldAssignCapturedBackground(bool isClosed, BitmapSource? bitmap)
  {
    return !isClosed && bitmap is not null;
  }

  internal static BitmapSource GetOutputImage(BitmapSource baseImage, ScreenshotAnnotationSession? session)
  {
    return session is null
      ? baseImage
      : ScreenshotAnnotationRenderer.RenderComposite(baseImage, session);
  }

  private static BitmapSource ConvertToBitmapSource(System.Drawing.Bitmap bitmap)
  {
    var bitmapData = bitmap.LockBits(
      new WinRect(0, 0, bitmap.Width, bitmap.Height),
      System.Drawing.Imaging.ImageLockMode.ReadOnly,
      bitmap.PixelFormat);

    var bitmapSource = BitmapSource.Create(
      bitmapData.Width,
      bitmapData.Height,
      bitmap.HorizontalResolution,
      bitmap.VerticalResolution,
      PixelFormats.Bgra32,
      null,
      bitmapData.Scan0,
      bitmapData.Stride * bitmapData.Height,
      bitmapData.Stride);

    bitmap.UnlockBits(bitmapData);
    bitmapSource.Freeze();
    return bitmapSource;
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
      if (_annotationSession is null ||
          !IsDescendant(EditSurface, e.OriginalSource as DependencyObject) ||
          _annotationSession.ActiveTool == ScreenshotAnnotationTool.None)
      {
        return;
      }

      BeginAnnotation(e.GetPosition(this));
      return;
    }

    _startPoint = e.GetPosition(this);
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
      UpdateAnnotationPreview(e.GetPosition(this));
      return;
    }

    if (!_isSelecting)
    {
      return;
    }

    var currentPoint = e.GetPosition(this);

    var x = Math.Min(_startPoint.X, currentPoint.X);
    var y = Math.Min(_startPoint.Y, currentPoint.Y);
    var width = Math.Abs(currentPoint.X - _startPoint.X);
    var height = Math.Abs(currentPoint.Y - _startPoint.Y);

    Canvas.SetLeft(SelectionRect, x);
    Canvas.SetTop(SelectionRect, y);
    SelectionRect.Width = width;
    SelectionRect.Height = height;

    UpdateDarkOverlay(new Rect(x, y, width, height));

    var topLeftPx = PointToScreen(new WpfPoint(x, y));
    var bottomRightPx = PointToScreen(new WpfPoint(x + width, y + height));
    var pixelWidth = Math.Max(1, (int)Math.Round(Math.Abs(bottomRightPx.X - topLeftPx.X)));
    var pixelHeight = Math.Max(1, (int)Math.Round(Math.Abs(bottomRightPx.Y - topLeftPx.Y)));
    SizeText.Text = $"{pixelWidth} x {pixelHeight}";
    SizeIndicator.Visibility = Visibility.Visible;

    Canvas.SetLeft(SizeIndicator, x);
    Canvas.SetTop(SizeIndicator, y - 28);
  }

  private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
  {
    if (_isAnnotating)
    {
      CommitAnnotation(e.GetPosition(this));
      return;
    }

    if (!_isSelecting)
    {
      return;
    }

    _isSelecting = false;
    ReleaseMouseCapture();

    var currentPoint = e.GetPosition(this);

    var x = Math.Min(_startPoint.X, currentPoint.X);
    var y = Math.Min(_startPoint.Y, currentPoint.Y);
    var width = Math.Abs(currentPoint.X - _startPoint.X);
    var height = Math.Abs(currentPoint.Y - _startPoint.Y);

    if (width < 10 || height < 10)
    {
      SelectionRect.Visibility = Visibility.Collapsed;
      SizeIndicator.Visibility = Visibility.Collapsed;
      UpdateDarkOverlay(null);
      return;
    }

    var topLeftPx = PointToScreen(new WpfPoint(x, y));
    var bottomRightPx = PointToScreen(new WpfPoint(x + width, y + height));
    var physicalX = (int)Math.Round(Math.Min(topLeftPx.X, bottomRightPx.X));
    var physicalY = (int)Math.Round(Math.Min(topLeftPx.Y, bottomRightPx.Y));
    var physicalWidth = Math.Max(1, (int)Math.Round(Math.Abs(bottomRightPx.X - topLeftPx.X)));
    var physicalHeight = Math.Max(1, (int)Math.Round(Math.Abs(bottomRightPx.Y - topLeftPx.Y)));

    _selectedRegion = new WinRect(physicalX, physicalY, physicalWidth, physicalHeight);
    EnterEditMode(new Rect(x, y, width, height));
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

  private BitmapSource? CropSelection()
  {
    if (_capturedScreen == null || _selectedRegion.Width <= 0)
    {
      return null;
    }

    var imageStartX = _virtualScreenPx.Left;
    var imageStartY = _virtualScreenPx.Top;

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
    Close();
    _onLongScreenshotRequested?.Invoke(_selectedRegion, _dpiScaleX, _dpiScaleY);
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

  private void BtnClear_Click(object sender, RoutedEventArgs e)
  {
    if (_annotationSession is null || _annotationSession.Operations.Count == 0)
    {
      return;
    }

    _annotationSession.ClearAnnotations();
    RefreshSelectedImagePreview();
    UpdateAnnotationToolbarState();
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

    _onPinRequested(output, _selectedRegion, _dpiScaleX, _dpiScaleY);
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
    _selectionBounds = selectionBounds;
    _isEditMode = true;
    _selectedImage = null;
    _annotationSession = CreateAnnotationSession(_selectedRegion);

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

  private static ScreenshotAnnotationSession CreateAnnotationSession(WinRect selectedRegion)
  {
    return new ScreenshotAnnotationSession(
      new WpfSize(selectedRegion.Width, selectedRegion.Height),
      new RectangleGeometry(new Rect(0, 0, selectedRegion.Width, selectedRegion.Height)));
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
    BtnClear.IsEnabled = _annotationSession.Operations.Count > 0;
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
    var scaleX = GetEditScaleX();
    var scaleY = GetEditScaleY();

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
            imagePoints[index] = ToImagePoint(_annotationPoints[index], scaleX, scaleY);
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
            new Rect(
              previewBounds.X * scaleX,
              previewBounds.Y * scaleY,
              previewBounds.Width * scaleX,
              previewBounds.Height * scaleY),
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

    _annotationSession.ClearAnnotations();
    _annotationPoints.Clear();
    ClearAnnotationPreview();
  }

  private void ResetSelection()
  {
    _isSelecting = false;
    _isEditMode = false;
    _isAnnotating = false;
    _annotationPoints.Clear();
    _annotationSession = null;
    _selectedImage = null;
    _selectedRegion = WinRect.Empty;
    _selectionBounds = Rect.Empty;

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

  private WpfPoint GetClampedEditSurfacePoint(WpfPoint point)
  {
    var editX = point.X - _selectionBounds.X;
    var editY = point.Y - _selectionBounds.Y;

    return new WpfPoint(
      Math.Clamp(editX, 0, Math.Max(0, _selectionBounds.Width)),
      Math.Clamp(editY, 0, Math.Max(0, _selectionBounds.Height)));
  }

  private double GetEditScaleX()
  {
    return _selectionBounds.Width <= 0 ? 1 : _selectedRegion.Width / _selectionBounds.Width;
  }

  private double GetEditScaleY()
  {
    return _selectionBounds.Height <= 0 ? 1 : _selectedRegion.Height / _selectionBounds.Height;
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
