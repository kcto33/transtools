using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;

using ScreenTranslator.Interop;
using ScreenTranslator.Services;

using WinRect = System.Drawing.Rectangle;
using WpfRect = System.Windows.Rect;

namespace ScreenTranslator.Windows;

public sealed partial class GifRecordingControlWindow : Window
{
  internal static readonly TimeSpan StartupClickDebounce = TimeSpan.FromMilliseconds(250);
  private readonly DateTime _shownAtUtc = DateTime.UtcNow;

  public event Action? StopRequested;
  public event Action? CancelRequested;

  public GifRecordingControlWindow()
  {
    InitializeComponent();

    StopButton.Content = LocalizationService.GetString("LongScreenshot_Btn_Stop", "Stop");
    CancelButton.Content = LocalizationService.GetString("LongScreenshot_Btn_Cancel", "Cancel");
    HintText.Text = BuildRecordingHint(TimeSpan.Zero);

    StopButton.Click += (_, _) =>
    {
      if (ShouldIgnoreStartupClick())
      {
        return;
      }

      StopRequested?.Invoke();
    };

    CancelButton.Click += (_, _) =>
    {
      if (ShouldIgnoreStartupClick())
      {
        return;
      }

      CancelRequested?.Invoke();
    };

    MouseLeftButtonDown += (_, e) =>
    {
      if (e.LeftButton == MouseButtonState.Pressed)
      {
        try { DragMove(); } catch { }
      }
    };
  }

  internal static bool ShouldHandleStartupClick(DateTime shownAtUtc, DateTime nowUtc)
  {
    return nowUtc - shownAtUtc >= StartupClickDebounce;
  }

  internal static string BuildRecordingHint(TimeSpan elapsed)
  {
    return string.Format(
      LocalizationService.GetString(
        "GifRecording_Hint_Recording",
        "Recording GIF {0:mm\\:ss} / {1:mm\\:ss} · {2} FPS"),
      elapsed,
      TimeSpan.FromSeconds(GifRecordingDefaults.MaxDurationSeconds),
      1000 / GifRecordingDefaults.FrameIntervalMs);
  }

  protected override void OnSourceInitialized(EventArgs e)
  {
    base.OnSourceInitialized(e);
    var hwnd = new WindowInteropHelper(this).Handle;
    var ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
    ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
    NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex);
    NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
  }

  public void FocusWindow()
  {
    if (!IsVisible)
    {
      return;
    }

    Topmost = true;
  }

  public bool PositionNearSelection(WinRect captureRegion, double dpiScaleX, double dpiScaleY)
  {
    const double gapDip = 12;
    var scaleX = dpiScaleX <= 0 ? 1.0 : dpiScaleX;
    var scaleY = dpiScaleY <= 0 ? 1.0 : dpiScaleY;

    var monitorWorkPx = Screen.FromRectangle(captureRegion).WorkingArea;
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

    var fallback = ClampToWorkArea(new WpfRect(selection.Left, selection.Top, Width, Height), work);
    Left = fallback.Left;
    Top = fallback.Top;
    return false;
  }

  public void UpdateProgress(TimeSpan elapsed)
  {
    HintText.Text = BuildRecordingHint(elapsed);
  }

  public void SetStoppingHint()
  {
    HintText.Text = LocalizationService.GetString("GifRecording_Hint_Stopping", "Stopping GIF recording...");
  }

  private bool ShouldIgnoreStartupClick()
  {
    return !ShouldHandleStartupClick(_shownAtUtc, DateTime.UtcNow);
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
    var maxX = Math.Max(work.Left, work.Right - candidate.Width);
    var maxY = Math.Max(work.Top, work.Bottom - candidate.Height);
    var x = Math.Clamp(candidate.Left, work.Left, maxX);
    var y = Math.Clamp(candidate.Top, work.Top, maxY);
    return new WpfRect(x, y, candidate.Width, candidate.Height);
  }
}
