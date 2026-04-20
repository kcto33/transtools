using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

using ScreenTranslator.Interop;

namespace ScreenTranslator.Services;

public static class CaptureService
{
  internal enum OverlayCursorKind
  {
    Arrow,
    Hand,
    IBeam,
  }

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
  private static readonly IntPtr ArrowCursorHandle = NativeMethods.LoadCursor(IntPtr.Zero, new IntPtr(NativeMethods.IDC_ARROW));
  private static readonly IntPtr HandCursorHandle = NativeMethods.LoadCursor(IntPtr.Zero, new IntPtr(NativeMethods.IDC_HAND));
  private static readonly IntPtr IBeamCursorHandle = NativeMethods.LoadCursor(IntPtr.Zero, new IntPtr(NativeMethods.IDC_IBEAM));

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

  internal static OverlayCursorKind ResolveOverlayCursorKind(
    IntPtr cursorHandle,
    IntPtr arrowCursorHandle,
    IntPtr handCursorHandle,
    IntPtr iBeamCursorHandle)
  {
    if (cursorHandle == handCursorHandle)
    {
      return OverlayCursorKind.Hand;
    }

    if (cursorHandle == iBeamCursorHandle)
    {
      return OverlayCursorKind.IBeam;
    }

    return OverlayCursorKind.Arrow;
  }

  internal static Point GetOverlayCursorHotspot(OverlayCursorKind kind)
  {
    return kind switch
    {
      OverlayCursorKind.Hand => new Point(12, 3),
      OverlayCursorKind.IBeam => new Point(7, 22),
      _ => new Point(4, 2),
    };
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

    var kind = ResolveOverlayCursorKind(
      cursorInfo.hCursor,
      ArrowCursorHandle,
      HandCursorHandle,
      IBeamCursorHandle);
    var hotspot = GetOverlayCursorHotspot(kind);
    var drawLocation = GetCursorDrawLocation(captureRegion, cursorPosition, hotspot.X, hotspot.Y);

    using var graphics = Graphics.FromImage(bitmap);
    var previousSmoothing = graphics.SmoothingMode;
    graphics.SmoothingMode = SmoothingMode.AntiAlias;

    try
    {
      DrawEnhancedCursor(graphics, drawLocation, kind);
    }
    finally
    {
      graphics.SmoothingMode = previousSmoothing;
    }
  }

  private static void DrawEnhancedCursor(Graphics graphics, Point drawLocation, OverlayCursorKind kind)
  {
    switch (kind)
    {
      case OverlayCursorKind.Hand:
        DrawHighlightedPolygon(graphics, OffsetPoints(drawLocation, HandCursorPoints));
        break;
      case OverlayCursorKind.IBeam:
        DrawEnhancedIBeam(graphics, drawLocation);
        break;
      default:
        DrawHighlightedPolygon(graphics, OffsetPoints(drawLocation, ArrowCursorPoints));
        break;
    }
  }

  private static void DrawHighlightedPolygon(Graphics graphics, Point[] points)
  {
    using var glowPen = new Pen(Color.FromArgb(190, 255, 140, 0), 8f)
    {
      LineJoin = LineJoin.Round,
    };
    using var outlinePen = new Pen(Color.Black, 3.6f)
    {
      LineJoin = LineJoin.Round,
    };
    using var accentPen = new Pen(Color.FromArgb(255, 255, 164, 32), 1.6f)
    {
      LineJoin = LineJoin.Round,
    };
    using var fillBrush = new SolidBrush(Color.White);

    graphics.DrawPolygon(glowPen, points);
    graphics.FillPolygon(fillBrush, points);
    graphics.DrawPolygon(outlinePen, points);
    graphics.DrawPolygon(accentPen, points);
  }

  private static void DrawEnhancedIBeam(Graphics graphics, Point drawLocation)
  {
    var x = drawLocation.X;
    var y = drawLocation.Y;

    using var glowPen = new Pen(Color.FromArgb(190, 255, 140, 0), 8f);
    using var outlinePen = new Pen(Color.Black, 4.5f);
    using var innerPen = new Pen(Color.White, 2.5f);

    var top = new Point(x, y);
    var bottom = new Point(x, y + 44);
    var topLeft = new Point(x - 7, y);
    var topRight = new Point(x + 7, y);
    var bottomLeft = new Point(x - 7, y + 44);
    var bottomRight = new Point(x + 7, y + 44);

    graphics.DrawLine(glowPen, top, bottom);
    graphics.DrawLine(glowPen, topLeft, topRight);
    graphics.DrawLine(glowPen, bottomLeft, bottomRight);

    graphics.DrawLine(outlinePen, top, bottom);
    graphics.DrawLine(outlinePen, topLeft, topRight);
    graphics.DrawLine(outlinePen, bottomLeft, bottomRight);

    graphics.DrawLine(innerPen, top, bottom);
    graphics.DrawLine(innerPen, topLeft, topRight);
    graphics.DrawLine(innerPen, bottomLeft, bottomRight);
  }

  private static Point[] OffsetPoints(Point offset, Point[] source)
  {
    var points = new Point[source.Length];
    for (var index = 0; index < source.Length; index++)
    {
      points[index] = new Point(source[index].X + offset.X, source[index].Y + offset.Y);
    }

    return points;
  }

  private static readonly Point[] ArrowCursorPoints =
  [
    new Point(4, 2),
    new Point(4, 38),
    new Point(13, 29),
    new Point(18, 43),
    new Point(24, 40),
    new Point(18, 26),
    new Point(31, 26),
  ];

  private static readonly Point[] HandCursorPoints =
  [
    new Point(12, 3),
    new Point(15, 6),
    new Point(15, 18),
    new Point(18, 15),
    new Point(21, 16),
    new Point(21, 6),
    new Point(24, 8),
    new Point(24, 23),
    new Point(27, 21),
    new Point(27, 11),
    new Point(30, 13),
    new Point(30, 30),
    new Point(27, 35),
    new Point(22, 39),
    new Point(14, 39),
    new Point(8, 33),
    new Point(8, 20),
    new Point(5, 18),
    new Point(5, 10),
    new Point(8, 8),
    new Point(10, 10),
    new Point(10, 18),
  ];
}
