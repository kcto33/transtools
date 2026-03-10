using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using ScreenTranslator.Interop;
using ScreenTranslator.Services;

using WpfClipboard = System.Windows.Clipboard;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace ScreenTranslator.Windows;

public partial class PinWindow : Window
{
  private const double ChromeSizeDip = 4;
  private const double MinZoom = 0.05;
  private const double MaxZoom = 100.0;

  private IntPtr _hwnd;
  private System.Windows.Point _dragStart;
  private bool _isDragging;
  private bool _isPanning;
  private System.Windows.Point _panStart;
  private double _panStartHorizontalOffset;
  private double _panStartVerticalOffset;

  private BitmapSource? _imageSource;
  private double _nativeContentWidthDip;
  private double _nativeContentHeightDip;
  private double _zoom = 1.0;
  private double _fitZoom = 1.0;

  public PinWindow()
  {
    InitializeComponent();

    MouseLeftButtonDown += OnMouseLeftButtonDown;
    MouseLeftButtonUp += OnMouseLeftButtonUp;
    MouseMove += OnMouseMove;
    Chrome.PreviewMouseLeftButtonDown += OnMouseLeftButtonDown;
    Chrome.PreviewMouseLeftButtonUp += OnMouseLeftButtonUp;
    Chrome.PreviewMouseMove += OnMouseMove;
    Chrome.PreviewMouseRightButtonDown += OnMouseRightButtonDown;
    Chrome.PreviewMouseRightButtonUp += OnMouseRightButtonUp;
    ImageScrollViewer.PreviewMouseWheel += OnMouseWheel;
    MouseEnter += (_, _) => CloseButton.Visibility = Visibility.Visible;
    MouseLeave += (_, _) => CloseButton.Visibility = Visibility.Collapsed;
    MouseWheel += OnMouseWheel;
    KeyDown += OnKeyDown;
    LostMouseCapture += OnLostMouseCapture;
  }

  private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
  {
    if (e.Key == Key.Escape)
    {
      Close();
    }
  }

  protected override void OnSourceInitialized(EventArgs e)
  {
    base.OnSourceInitialized(e);

    _hwnd = new WindowInteropHelper(this).Handle;
    var ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
    ex |= NativeMethods.WS_EX_TOOLWINDOW;
    NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
  }

  public void SetImage(BitmapSource bitmapSource, double dpiScaleX = 1.0, double dpiScaleY = 1.0)
  {
    _imageSource = bitmapSource;
    PinnedImage.Source = bitmapSource;

    var safeDpiX = dpiScaleX <= 0 ? 1.0 : dpiScaleX;
    var safeDpiY = dpiScaleY <= 0 ? 1.0 : dpiScaleY;

    _nativeContentWidthDip = Math.Max(1, bitmapSource.PixelWidth / safeDpiX);
    _nativeContentHeightDip = Math.Max(1, bitmapSource.PixelHeight / safeDpiY);

    PinnedImage.Width = _nativeContentWidthDip;
    PinnedImage.Height = _nativeContentHeightDip;

    _fitZoom = CalculateFitZoom();
    _zoom = _fitZoom;

    ApplyZoom(_zoom);
    UpdateWindowSizeForZoom(_zoom);

    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
    {
      ImageScrollViewer.ScrollToHorizontalOffset(0);
      ImageScrollViewer.ScrollToVerticalOffset(0);
    }));
  }

  private double CalculateFitZoom()
  {
    var maxContentWidth = Math.Max(260, SystemParameters.WorkArea.Width * 0.82) - ChromeSizeDip;
    var maxContentHeight = Math.Max(240, SystemParameters.WorkArea.Height * 0.82) - ChromeSizeDip;

    if (_nativeContentWidthDip <= 0 || _nativeContentHeightDip <= 0)
    {
      return 1.0;
    }

    var fit = Math.Min(maxContentWidth / _nativeContentWidthDip, maxContentHeight / _nativeContentHeightDip);
    return Math.Min(1.0, Math.Max(MinZoom, fit));
  }

  private void UpdateWindowSizeForZoom(double zoom)
  {
    var desiredContentWidth = _nativeContentWidthDip * zoom;
    var desiredContentHeight = _nativeContentHeightDip * zoom;

    var maxContentWidth = Math.Max(260, SystemParameters.WorkArea.Width * 0.90) - ChromeSizeDip;
    var maxContentHeight = Math.Max(240, SystemParameters.WorkArea.Height * 0.90) - ChromeSizeDip;

    // Resize each axis independently so very tall images don't lock width
    // as soon as height reaches its cap.
    var windowContentWidth = Math.Clamp(desiredContentWidth, 120, maxContentWidth);
    var windowContentHeight = Math.Clamp(desiredContentHeight, 100, maxContentHeight);

    Width = windowContentWidth + ChromeSizeDip;
    Height = windowContentHeight + ChromeSizeDip;
  }

  private void OnMouseWheel(object sender, MouseWheelEventArgs e)
  {
    if (_imageSource is null)
    {
      return;
    }

    var factor = e.Delta > 0 ? 1.1 : 0.9;
    var targetZoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
    if (Math.Abs(targetZoom - _zoom) < 0.0001)
    {
      return;
    }

    var mouseInViewer = e.GetPosition(ImageScrollViewer);
    var anchorContentX = (ImageScrollViewer.HorizontalOffset + mouseInViewer.X) / _zoom;
    var anchorContentY = (ImageScrollViewer.VerticalOffset + mouseInViewer.Y) / _zoom;

    _zoom = targetZoom;
    ApplyZoom(_zoom);
    // Hybrid zoom behavior:
    // 1) Grow window with content until reaching the viewport cap.
    // 2) Keep window size fixed afterwards, while content keeps zooming.
    UpdateWindowSizeForZoom(_zoom);

    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
    {
      var targetOffsetX = (anchorContentX * _zoom) - mouseInViewer.X;
      var targetOffsetY = (anchorContentY * _zoom) - mouseInViewer.Y;

      targetOffsetX = Math.Clamp(targetOffsetX, 0, ImageScrollViewer.ScrollableWidth);
      targetOffsetY = Math.Clamp(targetOffsetY, 0, ImageScrollViewer.ScrollableHeight);

      ImageScrollViewer.ScrollToHorizontalOffset(targetOffsetX);
      ImageScrollViewer.ScrollToVerticalOffset(targetOffsetY);
    }));

    e.Handled = true;
  }

  private void ApplyZoom(double zoom)
  {
    ImageScaleTransform.ScaleX = zoom;
    ImageScaleTransform.ScaleY = zoom;

    RenderOptions.SetBitmapScalingMode(
      PinnedImage,
      zoom > 1.0 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
  }

  private void CopyMenuItem_Click(object sender, RoutedEventArgs e)
  {
    if (_imageSource is null)
    {
      return;
    }

    try
    {
      WpfClipboard.SetImage(_imageSource);
    }
    catch
    {
      // Clipboard may be locked.
    }
  }

  private void SaveMenuItem_Click(object sender, RoutedEventArgs e)
  {
    if (_imageSource == null)
    {
      return;
    }

    var dialog = new WpfSaveFileDialog
    {
      Filter = LocalizationService.GetString("FileDialog_ImageFilter", "PNG Image|*.png|JPEG Image|*.jpg|BMP Image|*.bmp"),
      DefaultExt = ".png",
      FileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}",
    };

    if (dialog.ShowDialog() == true)
    {
      try
      {
        var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        BitmapEncoder encoder = ext switch
        {
          ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
          ".bmp" => new BmpBitmapEncoder(),
          _ => new PngBitmapEncoder(),
        };

        encoder.Frames.Add(BitmapFrame.Create(_imageSource));
        using var stream = File.Create(dialog.FileName);
        encoder.Save(stream);
      }
      catch
      {
        // Save failed.
      }
    }
  }

  private void CloseMenuItem_Click(object sender, RoutedEventArgs e)
  {
    Close();
  }

  private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
  {
    if (ReferenceEquals(e.OriginalSource, CloseButton))
    {
      return;
    }

    Focus();
    if (e.ClickCount == 2)
    {
      Close();
      return;
    }

    _isDragging = true;
    _dragStart = e.GetPosition(this);
    CaptureMouse();
    e.Handled = true;
  }

  private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
  {
    if (_isDragging)
    {
      _isDragging = false;
      EndCaptureIfIdle();
      e.Handled = true;
    }
  }

  private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
  {
    if (_isPanning)
    {
      if (e.RightButton != MouseButtonState.Pressed)
      {
        _isPanning = false;
        Cursor = System.Windows.Input.Cursors.Arrow;
        EndCaptureIfIdle();
        return;
      }

      var current = e.GetPosition(ImageScrollViewer);
      var dx = current.X - _panStart.X;
      var dy = current.Y - _panStart.Y;

      var targetX = Math.Clamp(_panStartHorizontalOffset - dx, 0, ImageScrollViewer.ScrollableWidth);
      var targetY = Math.Clamp(_panStartVerticalOffset - dy, 0, ImageScrollViewer.ScrollableHeight);

      ImageScrollViewer.ScrollToHorizontalOffset(targetX);
      ImageScrollViewer.ScrollToVerticalOffset(targetY);
      e.Handled = true;
      return;
    }

    if (!_isDragging)
    {
      return;
    }

    if (e.LeftButton != MouseButtonState.Pressed)
    {
      _isDragging = false;
      EndCaptureIfIdle();
      return;
    }

    var pos = e.GetPosition(this);
    var delta = pos - _dragStart;

    Left += delta.X;
    Top += delta.Y;
    e.Handled = true;
  }

  private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
  {
    if (ReferenceEquals(e.OriginalSource, CloseButton))
    {
      return;
    }

    if (!CanPanContent())
    {
      return;
    }

    _isPanning = true;
    _panStart = e.GetPosition(ImageScrollViewer);
    _panStartHorizontalOffset = ImageScrollViewer.HorizontalOffset;
    _panStartVerticalOffset = ImageScrollViewer.VerticalOffset;
    CaptureMouse();
    Cursor = System.Windows.Input.Cursors.ScrollAll;
    e.Handled = true;
  }

  private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
  {
    if (_isPanning)
    {
      _isPanning = false;
      Cursor = System.Windows.Input.Cursors.Arrow;
      EndCaptureIfIdle();
      e.Handled = true;
    }
  }

  private void OnLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
  {
    _isDragging = false;
    _isPanning = false;
    Cursor = System.Windows.Input.Cursors.Arrow;
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e)
  {
    Close();
  }

  private static bool IsDescendantOf<T>(DependencyObject? node)
    where T : DependencyObject
  {
    while (node is not null)
    {
      if (node is T)
      {
        return true;
      }

      node = VisualTreeHelper.GetParent(node);
    }

    return false;
  }

  private bool CanPanContent()
  {
    return _zoom > (_fitZoom + 0.01) &&
           (ImageScrollViewer.ScrollableWidth > 0.5 || ImageScrollViewer.ScrollableHeight > 0.5);
  }

  private void EndCaptureIfIdle()
  {
    if (!_isDragging && !_isPanning && IsMouseCaptured)
    {
      ReleaseMouseCapture();
    }
  }
}
