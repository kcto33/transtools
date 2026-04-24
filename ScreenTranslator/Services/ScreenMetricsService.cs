using System.Drawing;
using System.Windows;
using System.Windows.Interop;

using ScreenTranslator.Interop;

using FormsSystemInformation = System.Windows.Forms.SystemInformation;

namespace ScreenTranslator.Services;

internal static class ScreenMetricsService
{
  private const double DefaultDpi = 96.0;

  internal static Rectangle GetVirtualScreenBoundsPx()
  {
    var left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
    var top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
    var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
    var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

    return width > 0 && height > 0
      ? new Rectangle(left, top, width, height)
      : FormsSystemInformation.VirtualScreen;
  }

  internal static DpiScale GetDpiScale(Window window, double fallbackScaleX = 1.0, double fallbackScaleY = 1.0)
  {
    var hwnd = new WindowInteropHelper(window).Handle;
    if (hwnd != IntPtr.Zero)
    {
      var dpi = NativeMethods.GetDpiForWindow(hwnd);
      if (dpi > 0)
      {
        var scale = dpi / DefaultDpi;
        return new DpiScale(scale, scale);
      }
    }

    var source = PresentationSource.FromVisual(window);
    if (source?.CompositionTarget is not null)
    {
      return new DpiScale(
        GetSafeScale(source.CompositionTarget.TransformToDevice.M11, fallbackScaleX),
        GetSafeScale(source.CompositionTarget.TransformToDevice.M22, fallbackScaleY));
    }

    return new DpiScale(GetSafeScale(fallbackScaleX, 1.0), GetSafeScale(fallbackScaleY, 1.0));
  }

  internal static DpiScale GetDpiScaleForBounds(Rectangle boundsPx, double fallbackScaleX = 1.0, double fallbackScaleY = 1.0)
  {
    var rect = new NativeMethods.RECT
    {
      Left = boundsPx.Left,
      Top = boundsPx.Top,
      Right = boundsPx.Right,
      Bottom = boundsPx.Bottom,
    };

    try
    {
      var monitor = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONEAREST);
      if (monitor != IntPtr.Zero &&
          NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY) == 0 &&
          dpiX > 0 &&
          dpiY > 0)
      {
        return new DpiScale(dpiX / DefaultDpi, dpiY / DefaultDpi);
      }
    }
    catch
    {
      // Fall through to the caller-provided fallback.
    }

    return new DpiScale(GetSafeScale(fallbackScaleX, 1.0), GetSafeScale(fallbackScaleY, 1.0));
  }

  internal static void PositionWindowAtPixelBounds(Window window, Rectangle boundsPx)
  {
    var hwnd = new WindowInteropHelper(window).Handle;
    if (hwnd == IntPtr.Zero || boundsPx.Width <= 0 || boundsPx.Height <= 0)
    {
      return;
    }

    NativeMethods.SetWindowPos(
      hwnd,
      IntPtr.Zero,
      boundsPx.Left,
      boundsPx.Top,
      boundsPx.Width,
      boundsPx.Height,
      NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
  }

  private static double GetSafeScale(double scale, double fallback)
  {
    if (double.IsFinite(scale) && scale > 0)
    {
      return scale;
    }

    return double.IsFinite(fallback) && fallback > 0 ? fallback : 1.0;
  }
}
