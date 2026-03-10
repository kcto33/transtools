using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenTranslator.Models;
using ScreenTranslator.Services;
using WinRect = System.Drawing.Rectangle;
using WpfPoint = System.Windows.Point;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfClipboard = System.Windows.Clipboard;
using WpfCursors = System.Windows.Input.Cursors;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace ScreenTranslator.Windows;

public sealed partial class FreeformScreenshotWindow : Window
{
  private readonly AppSettings _settings;
  private readonly Action<BitmapSource, WinRect, double, double> _onPinRequested;

  private readonly List<WpfPoint> _pathPoints = new();
  private bool _isDrawing;
  private BitmapSource? _capturedScreen;
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
    // Get DPI scaling
    var source = PresentationSource.FromVisual(this);
    if (source?.CompositionTarget != null)
    {
      _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
      _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
    }

    // Capture all screens
    CaptureAllScreens();

    // Set window to cover all screens
    Left = SystemParameters.VirtualScreenLeft;
    Top = SystemParameters.VirtualScreenTop;
    Width = SystemParameters.VirtualScreenWidth;
    Height = SystemParameters.VirtualScreenHeight;

    // Initialize dark overlay
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
    // Don't start drawing if the user clicked on the toolbar.
    if (Toolbar.Visibility == Visibility.Visible && IsDescendant(Toolbar, e.OriginalSource as DependencyObject))
    {
      return;
    }

    if (_completedGeometry != null)
    {
      // Already completed a selection, ignore new drawing
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
    if (!_isDrawing) return;

    var point = e.GetPosition(this);

    // Add point if it's far enough from the last point (avoid too many points)
    if (_pathPoints.Count == 0 || (point - _pathPoints[^1]).Length > 3)
    {
      _pathPoints.Add(point);
      UpdateFreeformPath();
    }
  }

  private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
  {
    if (!_isDrawing) return;

    _isDrawing = false;
    ReleaseMouseCapture();

    // Need at least 3 points to form a shape
    if (_pathPoints.Count < 3)
    {
      _pathPoints.Clear();
      UpdateFreeformPath();
      HintBorder.Visibility = Visibility.Visible;
      return;
    }

    // Close the path
    _pathPoints.Add(_pathPoints[0]);
    UpdateFreeformPath();

    // Create completed geometry
    var figure = new PathFigure { StartPoint = _pathPoints[0], IsClosed = true, IsFilled = true };
    for (int i = 1; i < _pathPoints.Count; i++)
    {
      figure.Segments.Add(new LineSegment(_pathPoints[i], true));
    }

    _completedGeometry = new PathGeometry(new[] { figure });
    _boundingRect = _completedGeometry.Bounds;

    // Update overlay with cutout
    UpdateDarkOverlay(_completedGeometry);

    // Show toolbar near the selection
    ShowToolbar();

    Cursor = WpfCursors.Arrow;
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
    for (int i = 1; i < _pathPoints.Count; i++)
    {
      figure.Segments.Add(new LineSegment(_pathPoints[i], true));
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

    // Position toolbar below the bounding box
    var toolbarY = _boundingRect.Bottom + 10;
    if (toolbarY + 40 > Height)
    {
      toolbarY = _boundingRect.Top - 50;
    }

    Canvas.SetLeft(Toolbar, Math.Max(10, _boundingRect.Left));
    Canvas.SetTop(Toolbar, toolbarY);
  }

  private BitmapSource? CropFreeformSelection()
  {
    if (_capturedScreen == null || _completedGeometry == null) return null;

    // Calculate crop rectangle in physical pixels
    var imageStartX = (int)(SystemParameters.VirtualScreenLeft * _dpiScaleX);
    var imageStartY = (int)(SystemParameters.VirtualScreenTop * _dpiScaleY);

    // Bounding rect is in DIPs, convert to physical pixels
    var cropX = (int)((_boundingRect.X + SystemParameters.VirtualScreenLeft) * _dpiScaleX) - imageStartX;
    var cropY = (int)((_boundingRect.Y + SystemParameters.VirtualScreenTop) * _dpiScaleY) - imageStartY;
    var cropWidth = (int)(_boundingRect.Width * _dpiScaleX);
    var cropHeight = (int)(_boundingRect.Height * _dpiScaleY);

    // Ensure bounds
    cropX = Math.Max(0, cropX);
    cropY = Math.Max(0, cropY);
    cropWidth = Math.Min(cropWidth, _capturedScreen.PixelWidth - cropX);
    cropHeight = Math.Min(cropHeight, _capturedScreen.PixelHeight - cropY);

    if (cropWidth <= 0 || cropHeight <= 0) return null;

    // Create a cropped version first
    var croppedBitmap = new CroppedBitmap(_capturedScreen, new Int32Rect(cropX, cropY, cropWidth, cropHeight));

    // Create a DrawingVisual to apply the freeform mask
    var drawingVisual = new DrawingVisual();
    using (var dc = drawingVisual.RenderOpen())
    {
      // Create a geometry that's offset to match the cropped region
      var offsetTransform = new TranslateTransform(-_boundingRect.X, -_boundingRect.Y);
      var transformedGeometry = _completedGeometry.Clone();
      transformedGeometry.Transform = offsetTransform;

      // Create a clip with the freeform shape
      dc.PushClip(transformedGeometry);

      // Draw the cropped image
      dc.DrawImage(croppedBitmap, new Rect(0, 0, _boundingRect.Width, _boundingRect.Height));

      dc.Pop();
    }

    // Render to bitmap with transparency
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

  private void BtnReset_Click(object sender, RoutedEventArgs e)
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
    _completedGeometry = null;
    _boundingRect = Rect.Empty;

    FreeformPath.Data = null;
    UpdateDarkOverlay(null);
    Toolbar.Visibility = Visibility.Collapsed;
    HintBorder.Visibility = Visibility.Visible;

    Cursor = WpfCursors.Pen;
  }

  private void PinSelection()
  {
    var cropped = CropFreeformSelection();
    if (cropped == null) return;

    if (_settings.ScreenshotAutoCopy)
    {
      CopyToClipboard(cropped);
    }

    if (_settings.ScreenshotAutoSave)
    {
      SaveToFile(cropped);
    }

    _onPinRequested(cropped, GetPhysicalBoundingRect(), _dpiScaleX, _dpiScaleY);
    Close();
  }

  private void CopySelection()
  {
    var cropped = CropFreeformSelection();
    if (cropped == null) return;

    CopyToClipboard(cropped);

    if (_settings.ScreenshotAutoSave)
    {
      SaveToFile(cropped);
    }
  }

  private void SaveSelection()
  {
    var cropped = CropFreeformSelection();
    if (cropped == null) return;

    if (_settings.ScreenshotAutoCopy)
    {
      CopyToClipboard(cropped);
    }

    SaveToFile(cropped);
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

      if (dialog.ShowDialog() != true) return;

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



