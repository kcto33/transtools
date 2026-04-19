using System.Globalization;
using System.Windows;
using System.Windows.Interop;

using ScreenTranslator.Interop;
using ScreenTranslator.Services;

using WinRect = System.Drawing.Rectangle;

namespace ScreenTranslator.Windows;

public sealed partial class LongScreenshotControlWindow : Window
{
  private static readonly TimeSpan StartupClickDebounce = TimeSpan.FromMilliseconds(250);
  private readonly DateTime _shownAtUtc = DateTime.UtcNow;

  public event Action? PauseResumeRequested;
  public event Action? StopRequested;
  public event Action? CancelRequested;
  public event Action? TogglePreviewRequested;
  public event Action? SkipRequested;
  public event Action? CopyRequested;
  public event Action? SaveRequested;
  public event Action? PinRequested;
  public event Action? CloseRequested;
  public event Action? AutoScrollRequested;

  public LongScreenshotControlWindow()
  {
    InitializeComponent();

    PauseResumeButton.Click += (_, _) =>
    {
      if (ShouldIgnoreStartupClick())
      {
        return;
      }

      PauseResumeRequested?.Invoke();
    };
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
    TogglePreviewButton.Click += (_, _) =>
    {
      if (ShouldIgnoreStartupClick())
      {
        return;
      }

      TogglePreviewRequested?.Invoke();
    };
    SkipButton.Click += (_, _) =>
    {
      if (ShouldIgnoreStartupClick())
      {
        return;
      }

      SkipRequested?.Invoke();
    };

    CopyButton.Click += (_, _) =>
    {
      if (ShouldIgnoreStartupClick())
      {
        return;
      }

      CopyRequested?.Invoke();
    };
    SaveButton.Click += (_, _) =>
    {
      if (ShouldIgnoreStartupClick())
      {
        return;
      }

      SaveRequested?.Invoke();
    };
    PinButton.Click += (_, _) =>
    {
      if (ShouldIgnoreStartupClick())
      {
        return;
      }

      PinRequested?.Invoke();
    };
    CloseButton.Click += (_, _) =>
    {
      if (ShouldIgnoreStartupClick())
      {
        return;
      }

      CloseRequested?.Invoke();
    };
    AutoScrollButton.Click += (_, _) =>
    {
      if (ShouldIgnoreStartupClick())
      {
        return;
      }

      AutoScrollRequested?.Invoke();
    };

    MouseLeftButtonDown += (_, e) =>
    {
      if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
      {
        try { DragMove(); } catch { }
      }
    };

    HintText.Text = LocalizationService.GetString("LongScreenshot_Hint_RunningManual", "Scroll the page manually to capture.");
  }

  internal static bool ShouldHandleStartupClick(DateTime shownAtUtc, DateTime nowUtc)
  {
    return nowUtc - shownAtUtc >= StartupClickDebounce;
  }

  private bool ShouldIgnoreStartupClick()
  {
    return !ShouldHandleStartupClick(_shownAtUtc, DateTime.UtcNow);
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

  public void FocusWindow()
  {
    if (!IsVisible)
    {
      return;
    }

    Topmost = true;
  }

  public void PositionNearSelection(WinRect captureRegion, double dpiScaleX, double dpiScaleY)
  {
    var leftDip = captureRegion.Left / dpiScaleX;
    var topDip = captureRegion.Top / dpiScaleY;
    var widthDip = captureRegion.Width / dpiScaleX;
    var heightDip = captureRegion.Height / dpiScaleY;

    const double gap = 10;
    var desiredLeft = leftDip;
    var desiredTop = topDip + heightDip + gap;

    if (desiredTop + Height > SystemParameters.WorkArea.Bottom)
    {
      desiredTop = topDip - Height - gap;
    }

    if (desiredLeft + Width > SystemParameters.WorkArea.Right)
    {
      desiredLeft = SystemParameters.WorkArea.Right - Width;
    }

    Left = Math.Max(SystemParameters.WorkArea.Left, desiredLeft);
    Top = Math.Max(SystemParameters.WorkArea.Top, desiredTop);
  }

  public void UpdateRunState(LongScreenshotRunState state, LongScreenshotProgress? progress, WinRect region)
  {
    if (progress is not null)
    {
      if (state == LongScreenshotRunState.PendingFix)
      {
        HintText.Text = LocalizationService.GetString(
          "LongScreenshot_Hint_PendingFix",
          "Match failed. Click stitched preview to choose seam or click Skip.");
      }
      else if (state == LongScreenshotRunState.Paused)
      {
        HintText.Text = LocalizationService.GetString(
          "LongScreenshot_Hint_Paused",
          "Paused. You can drag/resize the frame then click Continue.");
      }
      else if (state == LongScreenshotRunState.Running)
      {
        HintText.Text = progress.IsAutoScrolling
          ? LocalizationService.GetString(
              "LongScreenshot_Hint_AutoScroll",
              "Auto scrolling... Click Manual or Pause to stop.")
          : LocalizationService.GetString(
              "LongScreenshot_Hint_RunningManual",
              "Scroll the page manually to capture.");
      }
    }

    PauseResumeButton.Visibility = Visibility.Visible;
    AutoScrollButton.Visibility = state == LongScreenshotRunState.Running
      ? Visibility.Visible
      : Visibility.Collapsed;
    StopButton.Visibility = Visibility.Visible;
    CancelButton.Visibility = Visibility.Visible;
    TogglePreviewButton.Visibility = Visibility.Visible;

    CopyButton.Visibility = Visibility.Collapsed;
    SaveButton.Visibility = Visibility.Collapsed;
    PinButton.Visibility = Visibility.Collapsed;
    CloseButton.Visibility = Visibility.Collapsed;

    SkipButton.Visibility = state == LongScreenshotRunState.PendingFix ? Visibility.Visible : Visibility.Collapsed;

    PauseResumeButton.Content = state switch
    {
      LongScreenshotRunState.Running => LocalizationService.GetString("LongScreenshot_Btn_Pause", "Pause"),
      LongScreenshotRunState.Paused => LocalizationService.GetString("LongScreenshot_Btn_Resume", "Continue"),
      LongScreenshotRunState.PendingFix => LocalizationService.GetString("LongScreenshot_Btn_Skip", "Skip"),
      _ => LocalizationService.GetString("LongScreenshot_Btn_Pause", "Pause"),
    };
  }

  public void SetPreviewVisible(bool isVisible)
  {
    TogglePreviewButton.Content = isVisible
      ? LocalizationService.GetString("LongScreenshot_Btn_HidePreview", "Hide Preview")
      : LocalizationService.GetString("LongScreenshot_Btn_ShowPreview", "Show Preview");
  }

  public void SetAutoScrollState(bool enabled)
  {
    AutoScrollButton.Content = enabled
      ? LocalizationService.GetString("LongScreenshot_Btn_AutoScrollOff", "Manual")
      : LocalizationService.GetString("LongScreenshot_Btn_AutoScroll", "Auto Scroll");
  }

  public void SetHint(string message)
  {
    HintText.Text = message;
  }

  internal static string BuildResultHint(LongScreenshotResult result, string? autoSavedPath)
  {
    var reason = result.StopReason.ToString();
    return string.IsNullOrWhiteSpace(autoSavedPath)
      ? $"Long screenshot finished. reason={reason}, captured={result.CapturedFrames}, accepted={result.AcceptedFrames}"
      : $"Long screenshot finished. reason={reason}, captured={result.CapturedFrames}, accepted={result.AcceptedFrames}, saved={autoSavedPath}";
  }

  public void ShowResultState(LongScreenshotResult result, string? autoSavedPath, bool hasImage)
  {
    PauseResumeButton.Visibility = Visibility.Collapsed;
    AutoScrollButton.Visibility = Visibility.Collapsed;
    StopButton.Visibility = Visibility.Collapsed;
    CancelButton.Visibility = Visibility.Collapsed;
    TogglePreviewButton.Visibility = Visibility.Collapsed;
    SkipButton.Visibility = Visibility.Collapsed;

    CopyButton.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
    SaveButton.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
    PinButton.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
    CloseButton.Visibility = Visibility.Visible;

    HintText.Text = BuildResultHint(result, autoSavedPath);
  }
}
