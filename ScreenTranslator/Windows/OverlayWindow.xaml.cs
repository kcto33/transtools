using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenTranslator.Interop;
using ScreenTranslator.Services;
using Screen = System.Windows.Forms.Screen;

namespace ScreenTranslator.Windows;

public partial class OverlayWindow : Window
{
  private readonly Screen _screen;
  private bool _dragging;
  private NativeMethods.POINT _startPx;
  private Rectangle _rectPx;
  private DpiScale _dpi;

  public event EventHandler<Rectangle>? SelectionCompleted;
  public event EventHandler? SelectionCancelled;

  public OverlayWindow(Screen screen)
  {
    InitializeComponent();
    _screen = screen;
  }

  protected override void OnSourceInitialized(EventArgs e)
  {
    base.OnSourceInitialized(e);
    _dpi = VisualTreeHelper.GetDpi(this);

    // Force exact pixel bounds using SetWindowPos.
    var hwnd = new WindowInteropHelper(this).Handle;
    var b = _screen.Bounds;
    NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, b.Left, b.Top, b.Width, b.Height,
      NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);

    SelectionRect.Visibility = Visibility.Collapsed;
    CaptureMouse();
    Keyboard.Focus(this);
  }

  protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
  {
    if (e.Key == Key.Escape)
    {
      e.Handled = true;
      try { ReleaseMouseCapture(); } catch { }
      SelectionCancelled?.Invoke(this, EventArgs.Empty);
      Close();
      return;
    }
    base.OnPreviewKeyDown(e);
  }

  protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
  {
    base.OnMouseLeftButtonDown(e);
    _dragging = true;

    NativeMethods.GetCursorPos(out _startPx);
    _rectPx = new Rectangle(_startPx.X, _startPx.Y, 0, 0);
    SelectionRect.Visibility = Visibility.Visible;
    UpdateSelectionVisual();
  }

  protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
  {
    base.OnMouseMove(e);
    if (!_dragging)
      return;

    if (!NativeMethods.GetCursorPos(out var p))
      return;

    // Screen.Bounds.Right/Bottom are exclusive.
    var maxX = _screen.Bounds.Right - 1;
    var maxY = _screen.Bounds.Bottom - 1;

    var x1 = Math.Clamp(_startPx.X, _screen.Bounds.Left, maxX);
    var y1 = Math.Clamp(_startPx.Y, _screen.Bounds.Top, maxY);
    var x2 = Math.Clamp(p.X, _screen.Bounds.Left, maxX);
    var y2 = Math.Clamp(p.Y, _screen.Bounds.Top, maxY);

    var left = Math.Min(x1, x2);
    var top = Math.Min(y1, y2);
    var right = Math.Max(x1, x2);
    var bottom = Math.Max(y1, y2);

    _rectPx = Rectangle.FromLTRB(left, top, right, bottom);
    UpdateSelectionVisual();
  }

  protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
  {
    base.OnMouseLeftButtonUp(e);
    if (!_dragging)
      return;

    _dragging = false;
    ReleaseMouseCapture();

    if (_rectPx.Width < 8 || _rectPx.Height < 8)
    {
      SelectionCancelled?.Invoke(this, EventArgs.Empty);
      Close();
      return;
    }

    SelectionCompleted?.Invoke(this, _rectPx);
    Close();
  }

  private void UpdateSelectionVisual()
  {
    var leftDip = (_rectPx.Left - _screen.Bounds.Left) / _dpi.DpiScaleX;
    var topDip = (_rectPx.Top - _screen.Bounds.Top) / _dpi.DpiScaleY;
    var widthDip = _rectPx.Width / _dpi.DpiScaleX;
    var heightDip = _rectPx.Height / _dpi.DpiScaleY;

    Canvas.SetLeft(SelectionRect, leftDip);
    Canvas.SetTop(SelectionRect, topDip);
    SelectionRect.Width = Math.Max(0, widthDip);
    SelectionRect.Height = Math.Max(0, heightDip);
  }
}
