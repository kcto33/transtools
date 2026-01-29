using System.Drawing;
using System.Drawing.Imaging;

namespace ScreenTranslator.Services;

public static class CaptureService
{
  public static Bitmap CaptureRegion(Rectangle regionPx)
  {
    if (regionPx.Width <= 0 || regionPx.Height <= 0)
      throw new ArgumentException("Invalid capture region.");

    var bmp = new Bitmap(regionPx.Width, regionPx.Height, PixelFormat.Format32bppPArgb);
    using var g = Graphics.FromImage(bmp);
    g.CopyFromScreen(regionPx.Left, regionPx.Top, 0, 0, regionPx.Size, CopyPixelOperation.SourceCopy);
    return bmp;
  }
}
