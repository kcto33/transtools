using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

using ScreenTranslator.Interop;

using WinRect = System.Drawing.Rectangle;

namespace ScreenTranslator.Windows;

public sealed partial class SelectionFrameWindow : Window
{
  private const double MinWidthDip = 80;
  private const double MinHeightDip = 80;
  private const double FrameInsetDip = 3;
  private const int LockedRingThicknessPx = 3;

  private double _dpiScaleX = 1.0;
  private double _dpiScaleY = 1.0;
  private bool _isLocked;
  private bool _isExcludedFromCapture;

  public event Action<WinRect>? RegionChanged;
  public bool IsExcludedFromCapture => _isExcludedFromCapture;

  public SelectionFrameWindow()
  {
    InitializeComponent();
    HookThumbEvents();
    SizeChanged += (_, _) => UpdateWindowRegion();
  }

  public void Initialize(WinRect regionPx, double dpiScaleX, double dpiScaleY)
  {
    _dpiScaleX = dpiScaleX <= 0 ? 1.0 : dpiScaleX;
    _dpiScaleY = dpiScaleY <= 0 ? 1.0 : dpiScaleY;

    Left = (regionPx.Left / _dpiScaleX) - FrameInsetDip;
    Top = (regionPx.Top / _dpiScaleY) - FrameInsetDip;
    Width = Math.Max(MinWidthDip + (FrameInsetDip * 2), (regionPx.Width / _dpiScaleX) + (FrameInsetDip * 2));
    Height = Math.Max(MinHeightDip + (FrameInsetDip * 2), (regionPx.Height / _dpiScaleY) + (FrameInsetDip * 2));
  }

  public void SetLocked(bool locked)
  {
    _isLocked = locked;
    FrameBorder.BorderBrush = locked
      ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(217, 48, 37))
      : new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 136, 229));

    var handlesVisibility = locked ? Visibility.Collapsed : Visibility.Visible;
    NorthThumb.Visibility = handlesVisibility;
    SouthThumb.Visibility = handlesVisibility;
    WestThumb.Visibility = handlesVisibility;
    EastThumb.Visibility = handlesVisibility;
    NorthWestThumb.Visibility = handlesVisibility;
    NorthEastThumb.Visibility = handlesVisibility;
    SouthWestThumb.Visibility = handlesVisibility;
    SouthEastThumb.Visibility = handlesVisibility;

    MoveThumb.IsHitTestVisible = !locked;
    UpdateWindowExStyle();
    UpdateWindowRegion();
  }

  protected override void OnSourceInitialized(EventArgs e)
  {
    base.OnSourceInitialized(e);
    UpdateWindowExStyle();
    UpdateWindowRegion();

    // Transparent WPF windows can render as white overlays on some systems
    // when display affinity exclusion is enabled.
    _isExcludedFromCapture = false;
  }

  private void HookThumbEvents()
  {
    MoveThumb.DragDelta += (_, e) =>
    {
      if (_isLocked)
      {
        return;
      }

      Left += e.HorizontalChange;
      Top += e.VerticalChange;
      ClampWithinVirtualScreen();
      RaiseRegionChanged();
    };

    NorthThumb.DragDelta += (_, e) => Resize(0, e.VerticalChange, 0, -e.VerticalChange);
    SouthThumb.DragDelta += (_, e) => Resize(0, 0, 0, e.VerticalChange);
    WestThumb.DragDelta += (_, e) => Resize(e.HorizontalChange, 0, -e.HorizontalChange, 0);
    EastThumb.DragDelta += (_, e) => Resize(0, 0, e.HorizontalChange, 0);

    NorthWestThumb.DragDelta += (_, e) => Resize(e.HorizontalChange, e.VerticalChange, -e.HorizontalChange, -e.VerticalChange);
    NorthEastThumb.DragDelta += (_, e) => Resize(0, e.VerticalChange, e.HorizontalChange, -e.VerticalChange);
    SouthWestThumb.DragDelta += (_, e) => Resize(e.HorizontalChange, 0, -e.HorizontalChange, e.VerticalChange);
    SouthEastThumb.DragDelta += (_, e) => Resize(0, 0, e.HorizontalChange, e.VerticalChange);
  }

  private void Resize(double deltaLeft, double deltaTop, double deltaWidth, double deltaHeight)
  {
    if (_isLocked)
    {
      return;
    }

    var minWindowWidthDip = MinWidthDip + (FrameInsetDip * 2);
    var minWindowHeightDip = MinHeightDip + (FrameInsetDip * 2);

    var newLeft = Left + deltaLeft;
    var newTop = Top + deltaTop;
    var newWidth = Width + deltaWidth;
    var newHeight = Height + deltaHeight;

    if (newWidth < minWindowWidthDip)
    {
      if (deltaLeft != 0)
      {
        newLeft = Left + (Width - minWindowWidthDip);
      }

      newWidth = minWindowWidthDip;
    }

    if (newHeight < minWindowHeightDip)
    {
      if (deltaTop != 0)
      {
        newTop = Top + (Height - minWindowHeightDip);
      }

      newHeight = minWindowHeightDip;
    }

    Left = newLeft;
    Top = newTop;
    Width = newWidth;
    Height = newHeight;
    ClampWithinVirtualScreen();
    RaiseRegionChanged();
  }

  private void ClampWithinVirtualScreen()
  {
    var leftBound = SystemParameters.VirtualScreenLeft - FrameInsetDip;
    var topBound = SystemParameters.VirtualScreenTop - FrameInsetDip;
    var rightBound = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + FrameInsetDip;
    var bottomBound = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight + FrameInsetDip;

    if (Left < leftBound)
    {
      Left = leftBound;
    }

    if (Top < topBound)
    {
      Top = topBound;
    }

    if (Left + Width > rightBound)
    {
      Left = rightBound - Width;
    }

    if (Top + Height > bottomBound)
    {
      Top = bottomBound - Height;
    }

    var maxWidth = rightBound - leftBound;
    var maxHeight = bottomBound - topBound;
    Width = Math.Min(Width, maxWidth);
    Height = Math.Min(Height, maxHeight);
  }

  private void RaiseRegionChanged()
  {
    var region = new WinRect(
      (int)Math.Round((Left + FrameInsetDip) * _dpiScaleX),
      (int)Math.Round((Top + FrameInsetDip) * _dpiScaleY),
      Math.Max(1, (int)Math.Round((Width - (FrameInsetDip * 2)) * _dpiScaleX)),
      Math.Max(1, (int)Math.Round((Height - (FrameInsetDip * 2)) * _dpiScaleY)));

    RegionChanged?.Invoke(region);
  }

  private void UpdateWindowExStyle()
  {
    var hwnd = new WindowInteropHelper(this).Handle;
    if (hwnd == IntPtr.Zero)
    {
      return;
    }

    var ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
    ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;

    if (_isLocked)
    {
      ex |= NativeMethods.WS_EX_TRANSPARENT;
    }
    else
    {
      ex &= ~NativeMethods.WS_EX_TRANSPARENT;
    }

    NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex);
  }

  private void UpdateWindowRegion()
  {
    var hwnd = new WindowInteropHelper(this).Handle;
    if (hwnd == IntPtr.Zero)
    {
      return;
    }

    if (!_isLocked)
    {
      NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, true);
      return;
    }

    if (!NativeMethods.GetWindowRect(hwnd, out var rect))
    {
      return;
    }

    var widthPx = Math.Max(1, rect.Right - rect.Left);
    var heightPx = Math.Max(1, rect.Bottom - rect.Top);
    if (widthPx <= (LockedRingThicknessPx * 2) || heightPx <= (LockedRingThicknessPx * 2))
    {
      NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, true);
      return;
    }

    var outer = NativeMethods.CreateRectRgn(0, 0, widthPx, heightPx);
    var inner = NativeMethods.CreateRectRgn(
      LockedRingThicknessPx,
      LockedRingThicknessPx,
      widthPx - LockedRingThicknessPx,
      heightPx - LockedRingThicknessPx);
    var ring = NativeMethods.CreateRectRgn(0, 0, 0, 0);

    if (outer == IntPtr.Zero || inner == IntPtr.Zero || ring == IntPtr.Zero)
    {
      if (outer != IntPtr.Zero) NativeMethods.DeleteObject(outer);
      if (inner != IntPtr.Zero) NativeMethods.DeleteObject(inner);
      if (ring != IntPtr.Zero) NativeMethods.DeleteObject(ring);
      return;
    }

    NativeMethods.CombineRgn(ring, outer, inner, NativeMethods.RGN_DIFF);
    NativeMethods.DeleteObject(outer);
    NativeMethods.DeleteObject(inner);

    var setResult = NativeMethods.SetWindowRgn(hwnd, ring, true);
    if (setResult == 0)
    {
      NativeMethods.DeleteObject(ring);
    }
  }
}
