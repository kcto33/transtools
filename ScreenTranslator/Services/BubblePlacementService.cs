using System.Drawing;

namespace ScreenTranslator.Services;

public static class BubblePlacementService
{
  public static Point Place(Rectangle selectionPx, Size bubbleSizePx, Rectangle workingAreaPx, int marginPx = 8, int padPx = 8)
  {
    int w = bubbleSizePx.Width;
    int h = bubbleSizePx.Height;

    bool FitsTop(int x, int y) => y + h <= selectionPx.Top - marginPx;
    bool FitsBottom(int x, int y) => y >= selectionPx.Bottom + marginPx;
    bool FitsRight(int x, int y) => x >= selectionPx.Right + marginPx;
    bool FitsLeft(int x, int y) => x + w <= selectionPx.Left - marginPx;

    int ClampX(int x) => Math.Clamp(x, workingAreaPx.Left + padPx, workingAreaPx.Right - padPx - w);
    int ClampY(int y) => Math.Clamp(y, workingAreaPx.Top + padPx, workingAreaPx.Bottom - padPx - h);

    // 1) Above (preferred)
    {
      int x = ClampX(selectionPx.Left);
      int y = selectionPx.Top - marginPx - h;
      if (FitsTop(x, y) && workingAreaPx.Top + padPx <= y)
        return new Point(x, y);
    }

    // 2) Right side
    {
      int x = selectionPx.Right + marginPx;
      int y = ClampY(selectionPx.Top - h);
      x = Math.Clamp(x, workingAreaPx.Left + padPx, workingAreaPx.Right - padPx - w);
      if (FitsRight(x, y))
        return new Point(x, y);
    }

    // 3) Left side
    {
      int x = selectionPx.Left - marginPx - w;
      int y = ClampY(selectionPx.Top - h);
      x = Math.Clamp(x, workingAreaPx.Left + padPx, workingAreaPx.Right - padPx - w);
      if (FitsLeft(x, y))
        return new Point(x, y);
    }

    // 4) Below (last resort, still non-overlapping)
    {
      int x = ClampX(selectionPx.Left);
      int y = selectionPx.Bottom + marginPx;
      y = Math.Clamp(y, workingAreaPx.Top + padPx, workingAreaPx.Bottom - padPx - h);
      if (FitsBottom(x, y))
        return new Point(x, y);
    }

    // If nothing fits, keep it inside the working area while minimizing overlap.
    return new Point(ClampX(selectionPx.Left), ClampY(selectionPx.Top - h));
  }
}
