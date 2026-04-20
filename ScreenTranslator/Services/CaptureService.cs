using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

using ScreenTranslator.Interop;

namespace ScreenTranslator.Services;

public static class CaptureService
{
  [DllImport("user32.dll")]
  private static extern IntPtr GetDesktopWindow();

  [DllImport("user32.dll")]
  private static extern IntPtr GetWindowDC(IntPtr hWnd);

  [DllImport("user32.dll")]
  private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

  [DllImport("gdi32.dll")]
  private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

  [DllImport("gdi32.dll")]
  private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

  [DllImport("gdi32.dll")]
  private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

  [DllImport("gdi32.dll")]
  private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
    IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

  [DllImport("gdi32.dll")]
  private static extern bool DeleteObject(IntPtr hObject);

  [DllImport("gdi32.dll")]
  private static extern bool DeleteDC(IntPtr hdc);

  private const uint SRCCOPY = 0x00CC0020;
  private const uint CAPTUREBLT = 0x40000000;

  public static Bitmap CaptureRegion(Rectangle regionPx, bool includeCursor = false)
  {
    if (regionPx.Width <= 0 || regionPx.Height <= 0)
      throw new ArgumentException("Invalid capture region.");

    // Try using PrintWindow-style capture with CAPTUREBLT for layered windows
    var desktopHwnd = GetDesktopWindow();
    var desktopDC = GetWindowDC(desktopHwnd);
    
    try
    {
      var memDC = CreateCompatibleDC(desktopDC);
      var hBitmap = CreateCompatibleBitmap(desktopDC, regionPx.Width, regionPx.Height);
      var oldBitmap = SelectObject(memDC, hBitmap);

      try
      {
        // Use SRCCOPY | CAPTUREBLT to capture layered windows
        BitBlt(memDC, 0, 0, regionPx.Width, regionPx.Height,
          desktopDC, regionPx.Left, regionPx.Top, SRCCOPY | CAPTUREBLT);

        SelectObject(memDC, oldBitmap);

        var bmp = Image.FromHbitmap(hBitmap);
        if (includeCursor)
        {
          TryDrawCursor(bmp, regionPx);
        }

        return bmp;
      }
      finally
      {
        DeleteObject(hBitmap);
        DeleteDC(memDC);
      }
    }
    finally
    {
      ReleaseDC(desktopHwnd, desktopDC);
    }
  }

  internal static bool ShouldDrawCursor(Rectangle captureRegion, Point cursorPositionPx)
  {
    return captureRegion.Contains(cursorPositionPx);
  }

  internal static Point GetCursorDrawLocation(Rectangle captureRegion, Point cursorPositionPx, int hotspotX, int hotspotY)
  {
    return new Point(
      cursorPositionPx.X - captureRegion.Left - hotspotX,
      cursorPositionPx.Y - captureRegion.Top - hotspotY);
  }

  private static void TryDrawCursor(Bitmap bitmap, Rectangle captureRegion)
  {
    var cursorInfo = new NativeMethods.CURSORINFO
    {
      cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>(),
    };

    if (!NativeMethods.GetCursorInfo(ref cursorInfo) ||
        cursorInfo.flags != NativeMethods.CURSOR_SHOWING ||
        cursorInfo.hCursor == IntPtr.Zero)
    {
      return;
    }

    var cursorPosition = new Point(cursorInfo.ptScreenPos.X, cursorInfo.ptScreenPos.Y);
    if (!ShouldDrawCursor(captureRegion, cursorPosition))
    {
      return;
    }

    if (!NativeMethods.GetIconInfo(cursorInfo.hCursor, out var iconInfo))
    {
      return;
    }

    try
    {
      using var graphics = Graphics.FromImage(bitmap);
      var hdc = graphics.GetHdc();

      try
      {
        var drawLocation = GetCursorDrawLocation(
          captureRegion,
          cursorPosition,
          (int)iconInfo.xHotspot,
          (int)iconInfo.yHotspot);

        _ = NativeMethods.DrawIconEx(
          hdc,
          drawLocation.X,
          drawLocation.Y,
          cursorInfo.hCursor,
          0,
          0,
          0,
          IntPtr.Zero,
          NativeMethods.DI_NORMAL);
      }
      finally
      {
        graphics.ReleaseHdc(hdc);
      }
    }
    finally
    {
      if (iconInfo.hbmMask != IntPtr.Zero)
      {
        _ = NativeMethods.DeleteObject(iconInfo.hbmMask);
      }

      if (iconInfo.hbmColor != IntPtr.Zero)
      {
        _ = NativeMethods.DeleteObject(iconInfo.hbmColor);
      }
    }
  }
}
