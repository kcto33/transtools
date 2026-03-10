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
using WpfClipboard = System.Windows.Clipboard;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace ScreenTranslator.Windows;

public sealed partial class ScreenshotOverlayWindow : Window
{
  private readonly AppSettings _settings;
  private readonly Action<BitmapSource, WinRect, double, double> _onPinRequested;
  private readonly Action? _onFreeformRequested;
  private readonly Action<WinRect, double, double>? _onLongScreenshotRequested;

  private WpfPoint _startPoint;
  private bool _isSelecting;
  private WinRect _selectedRegion;
  private BitmapSource? _capturedScreen;
  private System.Drawing.Rectangle _virtualScreenPx;
  private double _dpiScaleX = 1.0;
  private double _dpiScaleY = 1.0;

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

    CaptureAllScreens();

    Left = SystemParameters.VirtualScreenLeft;
    Top = SystemParameters.VirtualScreenTop;
    Width = SystemParameters.VirtualScreenWidth;
    Height = SystemParameters.VirtualScreenHeight;

    UpdateDarkOverlay(null);

    Focus();
    Cursor = WpfCursors.Cross;
  }

  private void CaptureAllScreens()
  {
    _virtualScreenPx = SystemInformation.VirtualScreen;
    var left = _virtualScreenPx.Left;
    var top = _virtualScreenPx.Top;
    var width = _virtualScreenPx.Width;
    var height = _virtualScreenPx.Height;

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
    // Don't start a new selection if the user clicked on the toolbar.
    if (Toolbar.Visibility == Visibility.Visible && IsDescendant(Toolbar, e.OriginalSource as DependencyObject))
    {
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

    Toolbar.Visibility = Visibility.Visible;
    var toolbarY = y + height + 10;
    if (toolbarY + 40 > Height)
    {
      toolbarY = y - 50;
    }

    Canvas.SetLeft(Toolbar, x);
    Canvas.SetTop(Toolbar, toolbarY);

    Cursor = WpfCursors.Arrow;
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

    Close();
    _onLongScreenshotRequested?.Invoke(_selectedRegion, _dpiScaleX, _dpiScaleY);
  }

  private void PinSelection()
  {
    var cropped = CropSelection();
    if (cropped == null)
    {
      return;
    }

    if (_settings.ScreenshotAutoCopy)
    {
      CopyToClipboard(cropped);
    }

    if (_settings.ScreenshotAutoSave)
    {
      SaveToFile(cropped);
    }

    _onPinRequested(cropped, _selectedRegion, _dpiScaleX, _dpiScaleY);
    Close();
  }

  private void CopySelection()
  {
    var cropped = CropSelection();
    if (cropped == null)
    {
      return;
    }

    CopyToClipboard(cropped);

    if (_settings.ScreenshotAutoSave)
    {
      SaveToFile(cropped);
    }
  }

  private void SaveSelection()
  {
    var cropped = CropSelection();
    if (cropped == null)
    {
      return;
    }

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
