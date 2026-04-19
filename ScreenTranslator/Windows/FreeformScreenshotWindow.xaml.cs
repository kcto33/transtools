using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

public sealed partial class FreeformScreenshotWindow : Window
{
  internal sealed record EditModeState(
    bool IsEditMode,
    bool IsAnnotating,
    ScreenshotAnnotationSession? AnnotationSession);

  internal sealed record PixelBounds(int X, int Y, int Width, int Height);

  private const double BrushPreviewThickness = 3;
  private const double RectanglePreviewThickness = 3;
  private const double MosaicPreviewThickness = 12;

  private readonly AppSettings _settings;
  private readonly Action<BitmapSource, WinRect, double, double> _onPinRequested;

  private readonly List<WpfPoint> _pathPoints = new();
  private readonly List<WpfPoint> _annotationPoints = new();
  private bool _isDrawing;
  private bool _isEditMode;
  private bool _isAnnotating;
  private BitmapSource? _capturedScreen;
  private BitmapSource? _selectedImage;
  private ScreenshotAnnotationSession? _annotationSession;
  private double _dpiScaleX = 1.0;
  private double _dpiScaleY = 1.0;
  private PathGeometry? _completedGeometry;
  private Rect _boundingRect;

  public FreeformScreenshotWindow(
    AppSettings settings,
    Action<BitmapSource, WinRect, double, double> onPinRequested)
  {
    _settings = settings;
    _onPinRequested = onPinRequested;
    InitializeComponent();

    Loaded += OnLoaded;
    MouseLeftButtonDown += OnMouseLeftButtonDown;
    MouseMove += OnMouseMove;
    MouseLeftButtonUp += OnMouseLeftButtonUp;
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

    CaptureAllScreens();

    Left = SystemParameters.VirtualScreenLeft;
    Top = SystemParameters.VirtualScreenTop;
    Width = SystemParameters.VirtualScreenWidth;
    Height = SystemParameters.VirtualScreenHeight;

    UpdateDarkOverlay(null);

    Focus();
    Cursor = WpfCursors.Pen;
  }

  private void CaptureAllScreens()
  {
    var left = (int)(SystemParameters.VirtualScreenLeft * _dpiScaleX);
    var top = (int)(SystemParameters.VirtualScreenTop * _dpiScaleY);
    var width = (int)(SystemParameters.VirtualScreenWidth * _dpiScaleX);
    var height = (int)(SystemParameters.VirtualScreenHeight * _dpiScaleY);

    using var bitmap = new System.Drawing.Bitmap(width, height);
    using var graphics = System.Drawing.Graphics.FromImage(bitmap);
    graphics.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height));

    _capturedScreen = ConvertToBitmapSource(bitmap);
    BackgroundImage.Source = _capturedScreen;
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
    if (Toolbar.Visibility == Visibility.Visible && IsDescendant(Toolbar, e.OriginalSource as DependencyObject))
    {
      return;
    }

    if (_isEditMode)
    {
      var isWithinEditSurface = IsDescendant(EditSurface, e.OriginalSource as DependencyObject);
      if (_annotationSession is null ||
          _annotationSession.ActiveTool == ScreenshotAnnotationTool.None ||
          !isWithinEditSurface ||
          !IsWithinEditableMask(e.GetPosition(this)))
      {
        return;
      }

      BeginAnnotation(e.GetPosition(this));
      return;
    }

    _isDrawing = true;
    _pathPoints.Clear();

    var point = e.GetPosition(this);
    _pathPoints.Add(point);

    HintBorder.Visibility = Visibility.Collapsed;
    CaptureMouse();

    UpdateFreeformPath();
  }

  private void OnMouseMove(object sender, WpfMouseEventArgs e)
  {
    if (_isAnnotating)
    {
      UpdateAnnotationPreview(e.GetPosition(this));
      return;
    }

    if (!_isDrawing)
    {
      return;
    }

    var point = e.GetPosition(this);
    if (_pathPoints.Count == 0 || (point - _pathPoints[^1]).Length > 3)
    {
      _pathPoints.Add(point);
      UpdateFreeformPath();
    }
  }

  private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
  {
    if (_isAnnotating)
    {
      CommitAnnotation(e.GetPosition(this));
      return;
    }

    if (!_isDrawing)
    {
      return;
    }

    _isDrawing = false;
    ReleaseMouseCapture();

    if (_pathPoints.Count < 3)
    {
      _pathPoints.Clear();
      UpdateFreeformPath();
      HintBorder.Visibility = Visibility.Visible;
      return;
    }

    _pathPoints.Add(_pathPoints[0]);
    UpdateFreeformPath();

    var figure = new PathFigure { StartPoint = _pathPoints[0], IsClosed = true, IsFilled = true };
    for (var index = 1; index < _pathPoints.Count; index++)
    {
      figure.Segments.Add(new LineSegment(_pathPoints[index], true));
    }

    _completedGeometry = new PathGeometry(new[] { figure });
    _boundingRect = _completedGeometry.Bounds;
    UpdateDarkOverlay(_completedGeometry);

    EnterEditMode();
  }

  private void OnKeyDown(object sender, WpfKeyEventArgs e)
  {
    if (e.Key == Key.Escape)
    {
      Close();
    }
  }

  private void UpdateFreeformPath()
  {
    if (_pathPoints.Count < 2)
    {
      FreeformPath.Data = null;
      return;
    }

    var figure = new PathFigure { StartPoint = _pathPoints[0] };
    for (var index = 1; index < _pathPoints.Count; index++)
    {
      figure.Segments.Add(new LineSegment(_pathPoints[index], true));
    }

    FreeformPath.Data = new PathGeometry(new[] { figure });
  }

  private void UpdateDarkOverlay(PathGeometry? selection)
  {
    var fullRect = new RectangleGeometry(new Rect(0, 0, Width, Height));

    if (selection != null)
    {
      DarkOverlay.Data = new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, selection);
    }
    else
    {
      DarkOverlay.Data = fullRect;
    }
  }

  private void ShowToolbar()
  {
    Toolbar.Visibility = Visibility.Visible;

    var toolbarY = _boundingRect.Bottom + 10;
    if (toolbarY + 40 > Height)
    {
      toolbarY = _boundingRect.Top - 50;
    }

    Canvas.SetLeft(Toolbar, Math.Max(10, _boundingRect.Left));
    Canvas.SetTop(Toolbar, toolbarY);
  }

  internal static BitmapSource GetOutputImage(BitmapSource baseImage, ScreenshotAnnotationSession? session)
  {
    return session is null
      ? baseImage
      : ScreenshotAnnotationRenderer.RenderComposite(baseImage, session);
  }

  internal static EditModeState CreateEditModeState(
    PathGeometry completedGeometry,
    Rect boundingRect,
    double dpiScaleX,
    double dpiScaleY)
  {
    var pixelBounds = GetPixelBounds(boundingRect, dpiScaleX, dpiScaleY);
    var annotationSession = new ScreenshotAnnotationSession(
      new WpfSize(pixelBounds.Width, pixelBounds.Height),
      CreateLocalMaskGeometry(
        completedGeometry,
        boundingRect,
        pixelBounds.Width / Math.Max(1, boundingRect.Width),
        pixelBounds.Height / Math.Max(1, boundingRect.Height)));
    annotationSession.SetActiveTool(ScreenshotAnnotationTool.Brush);

    return new EditModeState(
      IsEditMode: true,
      IsAnnotating: false,
      AnnotationSession: annotationSession);
  }

  internal static EditModeState ResetEditModeState()
  {
    return new EditModeState(
      IsEditMode: false,
      IsAnnotating: false,
      AnnotationSession: null);
  }

  private BitmapSource? CropFreeformSelection()
  {
    if (_capturedScreen == null || _completedGeometry == null)
    {
      return null;
    }

    var imageStartX = (int)(SystemParameters.VirtualScreenLeft * _dpiScaleX);
    var imageStartY = (int)(SystemParameters.VirtualScreenTop * _dpiScaleY);

    var pixelBounds = GetPixelBounds(_boundingRect, _dpiScaleX, _dpiScaleY);
    var cropX = pixelBounds.X - imageStartX;
    var cropY = pixelBounds.Y - imageStartY;
    var cropWidth = pixelBounds.Width;
    var cropHeight = pixelBounds.Height;

    cropX = Math.Max(0, cropX);
    cropY = Math.Max(0, cropY);
    cropWidth = Math.Min(cropWidth, _capturedScreen.PixelWidth - cropX);
    cropHeight = Math.Min(cropHeight, _capturedScreen.PixelHeight - cropY);

    if (cropWidth <= 0 || cropHeight <= 0)
    {
      return null;
    }

    var croppedBitmap = new CroppedBitmap(_capturedScreen, new Int32Rect(cropX, cropY, cropWidth, cropHeight));

    var drawingVisual = new DrawingVisual();
    using (var dc = drawingVisual.RenderOpen())
    {
      var transformedGeometry = _completedGeometry.Clone();
      transformedGeometry.Transform = new TranslateTransform(-_boundingRect.X, -_boundingRect.Y);

      dc.PushClip(transformedGeometry);
      dc.DrawImage(croppedBitmap, new Rect(0, 0, _boundingRect.Width, _boundingRect.Height));
      dc.Pop();
    }

    var renderTarget = new RenderTargetBitmap(
      cropWidth,
      cropHeight,
      _capturedScreen.DpiX,
      _capturedScreen.DpiY,
      PixelFormats.Pbgra32);

    renderTarget.Render(drawingVisual);
    renderTarget.Freeze();

    return renderTarget;
  }

  private BitmapSource? GetCurrentOutputImage()
  {
    var cropped = CropFreeformSelection();
    if (cropped == null)
    {
      return null;
    }

    _selectedImage = cropped;
    return GetOutputImage(cropped, _annotationSession);
  }

  private WinRect GetPhysicalBoundingRect()
  {
    var physicalX = (int)((_boundingRect.X + SystemParameters.VirtualScreenLeft) * _dpiScaleX);
    var physicalY = (int)((_boundingRect.Y + SystemParameters.VirtualScreenTop) * _dpiScaleY);
    var physicalWidth = (int)(_boundingRect.Width * _dpiScaleX);
    var physicalHeight = (int)(_boundingRect.Height * _dpiScaleY);

    return new WinRect(physicalX, physicalY, physicalWidth, physicalHeight);
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

  private void BtnCancel_Click(object sender, RoutedEventArgs e)
  {
    Close();
  }

  private void ResetSelection()
  {
    _pathPoints.Clear();
    _annotationPoints.Clear();
    _isDrawing = false;
    _selectedImage = null;
    ApplyEditModeState(ResetEditModeState());
    _completedGeometry = null;
    _boundingRect = Rect.Empty;

    ReleaseMouseCapture();
    FreeformPath.Data = null;
    EditSurface.Visibility = Visibility.Collapsed;
    EditSurface.Clip = null;
    SelectedImagePreview.Source = null;
    ClearAnnotationPreview();
    UpdateDarkOverlay(null);
    Toolbar.Visibility = Visibility.Collapsed;
    HintBorder.Visibility = Visibility.Visible;

    Cursor = WpfCursors.Pen;
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

    _onPinRequested(output, GetPhysicalBoundingRect(), _dpiScaleX, _dpiScaleY);
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

  private void EnterEditMode()
  {
    if (_completedGeometry is null || _boundingRect.IsEmpty)
    {
      return;
    }

    ApplyEditModeState(CreateEditModeState(_completedGeometry, _boundingRect, _dpiScaleX, _dpiScaleY));
    _selectedImage = null;

    Canvas.SetLeft(EditSurface, _boundingRect.X);
    Canvas.SetTop(EditSurface, _boundingRect.Y);
    EditSurface.Width = _boundingRect.Width;
    EditSurface.Height = _boundingRect.Height;
    EditSurface.Clip = CreateLocalMaskGeometry(_completedGeometry, _boundingRect, 1, 1);
    EditSurface.Visibility = Visibility.Visible;

    SelectedImagePreview.Width = _boundingRect.Width;
    SelectedImagePreview.Height = _boundingRect.Height;

    ClearAnnotationPreview();
    RefreshSelectedImagePreview();
    ShowToolbar();
    SetActiveAnnotationTool(ScreenshotAnnotationTool.Brush);
  }

  private void RefreshSelectedImagePreview()
  {
    _selectedImage = CropFreeformSelection();
    SelectedImagePreview.Source = _selectedImage is null
      ? null
      : GetOutputImage(_selectedImage, _annotationSession);
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

    AnnotationStrokePreview.Data = new PathGeometry(new[] { figure });
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

  private void ApplyEditModeState(EditModeState state)
  {
    _isEditMode = state.IsEditMode;
    _isAnnotating = state.IsAnnotating;
    _annotationSession = state.AnnotationSession;
  }

  private WpfPoint GetClampedEditSurfacePoint(WpfPoint point)
  {
    var editX = point.X - _boundingRect.X;
    var editY = point.Y - _boundingRect.Y;

    return new WpfPoint(
      Math.Clamp(editX, 0, Math.Max(0, _boundingRect.Width)),
      Math.Clamp(editY, 0, Math.Max(0, _boundingRect.Height)));
  }

  private bool IsWithinEditableMask(WpfPoint windowPoint)
  {
    return IsWithinEditableMask(_annotationSession, _boundingRect, windowPoint);
  }

  internal static bool IsWithinEditableMask(
    ScreenshotAnnotationSession? annotationSession,
    Rect boundingRect,
    WpfPoint windowPoint)
  {
    if (annotationSession is null ||
        windowPoint.X < boundingRect.X ||
        windowPoint.Y < boundingRect.Y ||
        windowPoint.X > boundingRect.Right ||
        windowPoint.Y > boundingRect.Bottom)
    {
      return false;
    }

    var clampedPoint = new WpfPoint(
      Math.Clamp(windowPoint.X - boundingRect.X, 0, Math.Max(0, boundingRect.Width)),
      Math.Clamp(windowPoint.Y - boundingRect.Y, 0, Math.Max(0, boundingRect.Height)));

    var scaleX = boundingRect.Width <= 0 ? 1 : annotationSession.CanvasSize.Width / boundingRect.Width;
    var scaleY = boundingRect.Height <= 0 ? 1 : annotationSession.CanvasSize.Height / boundingRect.Height;

    return annotationSession.EditMask.FillContains(ToImagePoint(clampedPoint, scaleX, scaleY));
  }

  private double GetEditScaleX()
  {
    return _boundingRect.Width <= 0 ? 1 : _annotationSession?.CanvasSize.Width / _boundingRect.Width ?? 1;
  }

  private double GetEditScaleY()
  {
    return _boundingRect.Height <= 0 ? 1 : _annotationSession?.CanvasSize.Height / _boundingRect.Height ?? 1;
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

  private static Geometry CreateLocalMaskGeometry(
    PathGeometry completedGeometry,
    Rect boundingRect,
    double scaleX,
    double scaleY)
  {
    var localGeometry = completedGeometry.Clone();
    var transformGroup = new TransformGroup();
    transformGroup.Children.Add(new TranslateTransform(-boundingRect.X, -boundingRect.Y));
    transformGroup.Children.Add(new ScaleTransform(scaleX, scaleY));
    localGeometry.Transform = transformGroup;
    localGeometry.Freeze();
    return localGeometry;
  }

  internal static PixelBounds GetPixelBounds(Rect boundingRect, double dpiScaleX, double dpiScaleY)
  {
    return new PixelBounds(
      X: (int)(boundingRect.X * dpiScaleX),
      Y: (int)(boundingRect.Y * dpiScaleY),
      Width: Math.Max(1, (int)(boundingRect.Width * dpiScaleX)),
      Height: Math.Max(1, (int)(boundingRect.Height * dpiScaleY)));
  }

  private static bool IsDescendant(DependencyObject ancestor, DependencyObject? child)
  {
    while (child is not null)
    {
      if (ReferenceEquals(child, ancestor))
      {
        return true;
      }

      child = VisualTreeHelper.GetParent(child);
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
      // Clipboard may be locked
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
          : _settings.ScreenshotSavePath
      };

      if (dialog.ShowDialog() != true)
      {
        return;
      }

      var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
      BitmapEncoder encoder = ext switch
      {
        ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
        ".bmp" => new BmpBitmapEncoder(),
        _ => new PngBitmapEncoder()
      };

      encoder.Frames.Add(BitmapFrame.Create(image));
      using var fileStream = new FileStream(dialog.FileName, FileMode.Create);
      encoder.Save(fileStream);
    }
    catch
    {
      // Save failed, ignore
    }
  }
}
