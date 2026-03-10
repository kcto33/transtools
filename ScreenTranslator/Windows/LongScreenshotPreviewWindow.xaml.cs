using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using ScreenTranslator.Interop;
using ScreenTranslator.Services;

using WinRect = System.Drawing.Rectangle;
using WinFormsScreen = System.Windows.Forms.Screen;
using WpfRect = System.Windows.Rect;

namespace ScreenTranslator.Windows;

public sealed partial class LongScreenshotPreviewWindow : Window
{
  public event Action<double>? StitchedPreviewClicked;

  public LongScreenshotPreviewWindow()
  {
    InitializeComponent();
    HintText.Text = LocalizationService.GetString("LongScreenshot_Hint_PreviewIdle", "Green marker means matched, red marker means manual fix needed.");

    MouseLeftButtonDown += (_, e) =>
    {
      if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
      {
        try { DragMove(); } catch { }
      }
    };
  }

  protected override void OnSourceInitialized(EventArgs e)
  {
    base.OnSourceInitialized(e);
    var hwnd = new WindowInteropHelper(this).Handle;
    var ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
    ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
    NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex);

    // Exclude from screen capture so it doesn't appear in long-screenshot frames.
    NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
  }

  public bool PositionNearSelection(WinRect captureRegion, double dpiScaleX, double dpiScaleY)
  {
    const double gapDip = 12;
    var scaleX = dpiScaleX <= 0 ? 1.0 : dpiScaleX;
    var scaleY = dpiScaleY <= 0 ? 1.0 : dpiScaleY;

    var monitorWorkPx = WinFormsScreen.FromRectangle(captureRegion).WorkingArea;
    var work = new WpfRect(
      monitorWorkPx.Left / scaleX,
      monitorWorkPx.Top / scaleY,
      Math.Max(1, monitorWorkPx.Width / scaleX),
      Math.Max(1, monitorWorkPx.Height / scaleY));

    var selection = new WpfRect(
      captureRegion.Left / scaleX,
      captureRegion.Top / scaleY,
      Math.Max(1, captureRegion.Width / scaleX),
      Math.Max(1, captureRegion.Height / scaleY));

    var candidates = new[]
    {
      new WpfRect(selection.Right + gapDip, selection.Top, Width, Height),
      new WpfRect(selection.Left - Width - gapDip, selection.Top, Width, Height),
      new WpfRect(selection.Left, selection.Bottom + gapDip, Width, Height),
      new WpfRect(selection.Left, selection.Top - Height - gapDip, Width, Height),
    };

    foreach (var candidate in candidates)
    {
      if (Fits(work, candidate) && !candidate.IntersectsWith(selection))
      {
        Left = candidate.Left;
        Top = candidate.Top;
        return true;
      }
    }

    foreach (var candidate in candidates)
    {
      var clamped = ClampToWorkArea(candidate, work);
      if (!clamped.IntersectsWith(selection))
      {
        Left = clamped.Left;
        Top = clamped.Top;
        return true;
      }
    }

    return false;
  }

  public void UpdateProgress(LongScreenshotProgress progress)
  {
    if (progress.StitchedPreview is not null)
    {
      StitchedImage.Source = progress.StitchedPreview;
    }

    RenderMarkers(progress.Markers);

    HintText.Text = progress.RunState == LongScreenshotRunState.PendingFix
      ? LocalizationService.GetString("LongScreenshot_Hint_PreviewPending", "Red marker found. Click red marker or click stitched image to resolve.")
      : LocalizationService.GetString("LongScreenshot_Hint_PreviewIdle", "Green marker means matched, red marker means manual fix needed.");
  }

  public void ShowResult(BitmapSource image, IReadOnlyList<LongScreenshotMarker> markers)
  {
    StitchedImage.Source = image;
    RenderMarkers(markers);
    HintText.Text = LocalizationService.GetString("LongScreenshot_Hint_Done", "Capture finished. You can copy, save, or pin the result.");
  }

  private void StitchedImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
  {
    if (StitchedImage.ActualHeight <= 1)
    {
      return;
    }

    var pt = e.GetPosition(StitchedImage);
    var ratioY = Math.Clamp(pt.Y / StitchedImage.ActualHeight, 0, 1);
    StitchedPreviewClicked?.Invoke(ratioY);
    e.Handled = true;
  }

  private void RenderMarkers(IReadOnlyList<LongScreenshotMarker> markers)
  {
    MarkerCanvas.Children.Clear();
    var height = MarkerCanvas.ActualHeight <= 1 ? MarkerCanvas.Height : MarkerCanvas.ActualHeight;
    if (height <= 1)
    {
      height = 200;
    }

    foreach (var marker in markers)
    {
      var color = marker.Status switch
      {
        LongScreenshotMarkerStatus.Matched => System.Windows.Media.Color.FromRgb(59, 170, 79),
        LongScreenshotMarkerStatus.Failed => System.Windows.Media.Color.FromRgb(219, 68, 55),
        LongScreenshotMarkerStatus.ManuallyResolved => System.Windows.Media.Color.FromRgb(66, 133, 244),
        _ => System.Windows.Media.Color.FromRgb(133, 133, 133),
      };

      var y = Math.Clamp(marker.OutputRatioY, 0, 1) * (height - 8);
      var item = new Border
      {
        Width = 18,
        Height = 6,
        CornerRadius = new CornerRadius(2),
        Background = new SolidColorBrush(color),
        BorderBrush = System.Windows.Media.Brushes.White,
        BorderThickness = new Thickness(1),
        ToolTip = $"#{marker.Index + 1}  MAD {marker.MadPercent:F2}%",
        Cursor = marker.Status == LongScreenshotMarkerStatus.Failed
          ? System.Windows.Input.Cursors.Hand
          : System.Windows.Input.Cursors.Arrow,
        Tag = marker,
      };

      if (marker.Status == LongScreenshotMarkerStatus.Failed)
      {
        item.MouseLeftButtonDown += OnFailedMarkerClicked;
      }

      Canvas.SetLeft(item, 8);
      Canvas.SetTop(item, y);
      MarkerCanvas.Children.Add(item);
    }
  }

  private void OnFailedMarkerClicked(object sender, MouseButtonEventArgs e)
  {
    if (sender is not Border { Tag: LongScreenshotMarker marker })
    {
      return;
    }

    StitchedPreviewClicked?.Invoke(marker.OutputRatioY);
    HintText.Text = LocalizationService.GetString("LongScreenshot_Hint_PreviewResolve", "Applied marker position. Continue scrolling if needed.");
    e.Handled = true;
  }

  private static bool Fits(WpfRect work, WpfRect candidate)
  {
    return candidate.Left >= work.Left &&
           candidate.Top >= work.Top &&
           candidate.Right <= work.Right &&
           candidate.Bottom <= work.Bottom;
  }

  private static WpfRect ClampToWorkArea(WpfRect candidate, WpfRect work)
  {
    var x = Math.Clamp(candidate.Left, work.Left, work.Right - candidate.Width);
    var y = Math.Clamp(candidate.Top, work.Top, work.Bottom - candidate.Height);
    return new WpfRect(x, y, candidate.Width, candidate.Height);
  }
}
