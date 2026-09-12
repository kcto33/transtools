using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ScreenTranslator.Interop;
using ScreenTranslator.Models;
using ScreenTranslator.Services;
using Screen = System.Windows.Forms.Screen;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfBrush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using DpiScale = System.Windows.DpiScale;
using CornerRadius = System.Windows.CornerRadius;
using Thickness = System.Windows.Thickness;

namespace ScreenTranslator.Windows;

public partial class BubbleWindow : Window
{
  private const string SpeakGlyph = "\uE767";
  private const string StopGlyph = "\uE71A";
  private const string SpeakTooltipKey = "Bubble_SpeakTooltip";
  private const string StopSpeakingTooltipKey = "Bubble_StopSpeakTooltip";

  private static readonly WpfBrush SpeakIconBrush = new SolidColorBrush(WpfColor.FromRgb(0x8A, 0x8A, 0x8A));
  private static readonly WpfBrush SpeakingIconBrush = new SolidColorBrush(WpfColor.FromRgb(0xC7, 0x50, 0x50));

  private readonly Screen _screen;
  private readonly BubbleSettings _bubbleSettings;
  private DpiScale _dpi;
  private readonly DispatcherTimer _autoClose;
  private readonly SpeechService _speech;
  private string _translationDisplay = string.Empty;
  private string _translationCopyText = string.Empty;
  private string _originalCopyText = string.Empty;
  private string? _sourceLanguage;
  private bool _showingOriginal;
  private Rectangle _selectionPx;
  private IntPtr _hwnd;
  private IntPtr _mouseHook;
  private NativeMethods.LowLevelMouseProc? _mouseHookProc;

  public BubbleWindow(Screen screen, BubbleSettings? bubbleSettings = null)
  {
    InitializeComponent();
    _screen = screen;
    _bubbleSettings = bubbleSettings ?? new BubbleSettings();

    ApplyBubbleStyle();

    _speech = new SpeechService();
    _speech.SpeakCompleted += OnSpeechCompleted;

    _autoClose = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
    _autoClose.Tick += (_, _) => Close();

    MouseLeftButtonDown += OnBubbleLeftClick;
    MouseRightButtonDown += OnBubbleRightClick;
    SetSpeakingUi(false);

    // Size is driven by content (SizeToContent in XAML).
  }

  private void ApplyBubbleStyle()
  {
    try
    {
      // Apply background color
      var bgColor = (WpfColor)WpfColorConverter.ConvertFromString(_bubbleSettings.BackgroundColor);
      Chrome.Background = new SolidColorBrush(bgColor);

      // Apply border color
      var borderColor = (WpfColor)WpfColorConverter.ConvertFromString(_bubbleSettings.BorderColor);
      Chrome.BorderBrush = new SolidColorBrush(borderColor);

      // Apply corner radius
      Chrome.CornerRadius = new CornerRadius(_bubbleSettings.CornerRadius);

      // Apply padding
      Chrome.Padding = new Thickness(_bubbleSettings.Padding);

      // Apply text color
      var textColor = (WpfColor)WpfColorConverter.ConvertFromString(_bubbleSettings.TextColor);
      TranslationText.Foreground = new SolidColorBrush(textColor);

      // Apply font
      TranslationText.FontFamily = new WpfFontFamily(_bubbleSettings.FontFamily);
      TranslationText.FontSize = _bubbleSettings.FontSize;
    }
    catch
    {
      // If any style fails to apply, use defaults
    }
  }

  protected override void OnSourceInitialized(EventArgs e)
  {
    base.OnSourceInitialized(e);
    _dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);

    _hwnd = new WindowInteropHelper(this).Handle;
    var ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
    ex |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
    NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);

    EnsureMouseHook();
  }

  public void ShowPlaceholder(Rectangle selectionPx)
  {
    _selectionPx = selectionPx;
    _translationDisplay = "...";
    _translationCopyText = "...";
    _originalCopyText = string.Empty;
    _sourceLanguage = null;
    _showingOriginal = false;
    UpdateDisplayedText();
    ShowAndPlace(selectionPx);
  }

  public void SetTranslation(Rectangle selectionPx, string translated, string originalText, string? fullTranslation = null, string? sourceLanguage = null)
  {
    _selectionPx = selectionPx;
    _translationDisplay = string.IsNullOrWhiteSpace(translated) ? "(no text)" : translated;
    _translationCopyText = string.IsNullOrWhiteSpace(fullTranslation) ? _translationDisplay : fullTranslation;
    _originalCopyText = originalText ?? string.Empty;
    _sourceLanguage = sourceLanguage;
    _showingOriginal = false;
    UpdateDisplayedText();
    SpeakButton.Visibility = string.IsNullOrWhiteSpace(_originalCopyText) ? Visibility.Collapsed : Visibility.Visible;
    ShowAndPlace(selectionPx);
  }

  private void ShowAndPlace(Rectangle selectionPx)
  {
    if (!IsVisible)
      Show();

    _autoClose.Stop();
    _autoClose.Start();

    // Cap width relative to the working area using configured ratio
    var work = _screen.WorkingArea;
    var maxWidth = Math.Max(220, (int)(work.Width * _bubbleSettings.MaxWidthRatio));
    TranslationText.MaxWidth = maxWidth;

    // Force layout to measure actual size.
    UpdateLayout();

    var bubbleSizePx = new System.Drawing.Size(
      (int)Math.Ceiling(ActualWidth * _dpi.DpiScaleX),
      (int)Math.Ceiling(ActualHeight * _dpi.DpiScaleY));

    var pt = BubblePlacementService.Place(selectionPx, bubbleSizePx, work);

    NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, pt.X, pt.Y, bubbleSizePx.Width, bubbleSizePx.Height,
      NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
  }

  private void OnBubbleLeftClick(object? sender, MouseButtonEventArgs e)
  {
    if (!string.IsNullOrWhiteSpace(_originalCopyText))
    {
      _showingOriginal = !_showingOriginal;
      UpdateDisplayedText();
      ShowAndPlace(_selectionPx);
    }
  }

  private void OnBubbleRightClick(object? sender, MouseButtonEventArgs e)
  {
    var text = _showingOriginal ? _originalCopyText : _translationCopyText;
    if (!string.IsNullOrWhiteSpace(text))
    {
      try
      {
        System.Windows.Clipboard.SetText(text);
        ShowCopyStatus();
      }
      catch { }
    }
  }

  private async void ShowCopyStatus()
  {
    CopyStatusBadge.Visibility = Visibility.Visible;
    await Task.Delay(1200);
    if (CopyStatusBadge.IsVisible)
    {
      CopyStatusBadge.Visibility = Visibility.Collapsed;
    }
  }

  private async void OnSpeakButtonMouseDown(object sender, MouseButtonEventArgs e)
  {
    e.Handled = true;
    await ToggleSpeakAsync();
  }

  private async Task ToggleSpeakAsync()
  {
    if (string.IsNullOrWhiteSpace(_originalCopyText))
      return;

    if (_speech.IsSpeaking)
    {
      StopSpeaking(restartAutoClose: true);
      return;
    }

    SetSpeakingUi(true);
    _autoClose.Stop();
    try
    {
      await _speech.SpeakAsync(_originalCopyText, _sourceLanguage, CancellationToken.None);
    }
    catch
    {
      // Reading is best-effort; ignore synthesis failures.
    }

    if (!_speech.IsSpeaking)
    {
      SetSpeakingUi(false);
      RestartAutoClose();
    }
  }

  private void StopSpeaking(bool restartAutoClose)
  {
    _speech.Stop();
    SetSpeakingUi(false);
    if (restartAutoClose)
      RestartAutoClose();
  }

  private void OnSpeechCompleted(object? sender, EventArgs e)
  {
    Dispatcher.BeginInvoke(() =>
    {
      SetSpeakingUi(false);
      RestartAutoClose();
    });
  }

  private void RestartAutoClose()
  {
    if (!IsVisible)
      return;

    _autoClose.Stop();
    _autoClose.Start();
  }

  private void SetSpeakingUi(bool speaking)
  {
    SpeakIcon.Text = speaking ? StopGlyph : SpeakGlyph;
    SpeakIcon.Foreground = speaking ? SpeakingIconBrush : SpeakIconBrush;
    SpeakTooltipText.Text = LocalizationService.GetString(
      speaking ? StopSpeakingTooltipKey : SpeakTooltipKey,
      speaking ? "Stop reading" : "Read original aloud");
  }

  protected override void OnClosed(EventArgs e)
  {
    RemoveMouseHook();
    _speech.Dispose();
    base.OnClosed(e);
  }

  private void UpdateDisplayedText()
  {
    var text = _showingOriginal ? _originalCopyText : _translationDisplay;
    if (string.IsNullOrWhiteSpace(text))
      text = "(no text)";
    TranslationText.Text = text;
  }

  private void EnsureMouseHook()
  {
    if (_mouseHook != IntPtr.Zero)
      return;

    _mouseHookProc = LowLevelMouseHook;
    _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseHookProc, IntPtr.Zero, 0);
  }

  private void RemoveMouseHook()
  {
    if (_mouseHook == IntPtr.Zero)
      return;

    try { NativeMethods.UnhookWindowsHookEx(_mouseHook); } catch { }
    _mouseHook = IntPtr.Zero;
    _mouseHookProc = null;
  }

  private IntPtr LowLevelMouseHook(int nCode, IntPtr wParam, IntPtr lParam)
  {
    if (nCode >= 0 && IsVisible)
    {
      var msg = wParam.ToInt32();
      if (msg == NativeMethods.WM_LBUTTONDOWN || msg == NativeMethods.WM_RBUTTONDOWN)
      {
        var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        if (!IsPointInsideWindow(data.pt))
        {
          Dispatcher.BeginInvoke(() =>
          {
            if (IsVisible)
              Close();
          });
        }
      }
    }

    return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
  }

  private bool IsPointInsideWindow(NativeMethods.POINT pt)
  {
    if (_hwnd == IntPtr.Zero)
      return false;

    if (!NativeMethods.GetWindowRect(_hwnd, out var rect))
      return false;

    return pt.X >= rect.Left && pt.X <= rect.Right && pt.Y >= rect.Top && pt.Y <= rect.Bottom;
  }
}
