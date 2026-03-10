using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

using ScreenTranslator.Interop;
using ScreenTranslator.Models;

using Screen = System.Windows.Forms.Screen;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfFontFamily = System.Windows.Media.FontFamily;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using DpiScale = System.Windows.DpiScale;

namespace ScreenTranslator.Windows;

public partial class PasteHistoryWindow : Window
{
  private const int PreviewMaxChars = 60;

  private readonly BubbleSettings _bubbleSettings;
  private DpiScale _dpi;
  private IntPtr _hwnd;
  private IntPtr _mouseHook;
  private NativeMethods.LowLevelMouseProc? _mouseHookProc;
  private Screen? _screen;

  public PasteHistoryWindow(BubbleSettings? bubbleSettings = null)
  {
    InitializeComponent();

    _bubbleSettings = bubbleSettings ?? new BubbleSettings();
    ApplyBubbleStyle();

    ItemsList.DisplayMemberPath = nameof(HistoryItem.Preview);
  }

  public int SelectedIndex => ItemsList.SelectedIndex;

  public string? SelectedText => (ItemsList.SelectedItem as HistoryItem)?.Text;

  protected override void OnSourceInitialized(EventArgs e)
  {
    base.OnSourceInitialized(e);
    _dpi = VisualTreeHelper.GetDpi(this);

    _hwnd = new WindowInteropHelper(this).Handle;
    var ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
    ex |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
    NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);

    EnsureMouseHook();
  }

  protected override void OnClosed(EventArgs e)
  {
    RemoveMouseHook();
    base.OnClosed(e);
  }

  public void SetItems(IReadOnlyList<string> items)
  {
    var list = items
      .Where(s => !string.IsNullOrWhiteSpace(s))
      .Select(s => new HistoryItem(s, BuildPreview(s)))
      .ToArray();

    ItemsList.ItemsSource = list;
    ItemsList.SelectedIndex = list.Length > 0 ? 0 : -1;
  }

  public void MoveSelection(int delta)
  {
    var count = ItemsList.Items.Count;
    if (count <= 0)
      return;

    var idx = ItemsList.SelectedIndex;
    if (idx < 0)
      idx = 0;

    idx = (idx + delta) % count;
    if (idx < 0)
      idx += count;

    ItemsList.SelectedIndex = idx;
  }

  public void ShowAtCursor()
  {
    if (!NativeMethods.GetCursorPos(out var p))
      return;

    _screen = Screen.FromPoint(new System.Drawing.Point(p.X, p.Y));

    // 应用样式（包括基于屏幕的最大宽度）
    ApplyBubbleStyle();

    if (!IsVisible)
      Show();

    UpdateLayout();
    PlaceNear(p);
  }

  private void PlaceNear(NativeMethods.POINT cursorPx)
  {
    if (_screen is null)
      return;

    var work = _screen.WorkingArea;

    var windowWidthPx = (int)Math.Ceiling(ActualWidth * _dpi.DpiScaleX);
    var windowHeightPx = (int)Math.Ceiling(ActualHeight * _dpi.DpiScaleY);

    var x = cursorPx.X + 12;
    var y = cursorPx.Y + 12;

    if (x + windowWidthPx > work.Right)
      x = work.Right - windowWidthPx;
    if (y + windowHeightPx > work.Bottom)
      y = work.Bottom - windowHeightPx;

    if (x < work.Left)
      x = work.Left;
    if (y < work.Top)
      y = work.Top;

    NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, x, y, windowWidthPx, windowHeightPx,
      NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
  }

  private void ApplyBubbleStyle()
  {
    try
    {
      var bgColor = (WpfColor)WpfColorConverter.ConvertFromString(_bubbleSettings.BackgroundColor);
      Chrome.Background = new SolidColorBrush(bgColor);

      var borderColor = (WpfColor)WpfColorConverter.ConvertFromString(_bubbleSettings.BorderColor);
      Chrome.BorderBrush = new SolidColorBrush(borderColor);

      Chrome.CornerRadius = new CornerRadius(_bubbleSettings.CornerRadius);
      Chrome.Padding = new Thickness(_bubbleSettings.Padding);

      var textColor = (WpfColor)WpfColorConverter.ConvertFromString(_bubbleSettings.TextColor);
      ItemsList.Foreground = new SolidColorBrush(textColor);
      ItemsList.FontFamily = new WpfFontFamily(_bubbleSettings.FontFamily);
      ItemsList.FontSize = _bubbleSettings.FontSize;

      // 应用最大宽度比例
      if (_screen is not null)
      {
        var maxWidth = Math.Max(220, (int)(_screen.WorkingArea.Width * _bubbleSettings.MaxWidthRatio));
        ItemsList.MaxWidth = maxWidth - _bubbleSettings.Padding * 2 - 2; // 减去 padding 和 border
      }
    }
    catch
    {
      // ignore and fall back to defaults
    }
  }

  private static string BuildPreview(string text)
  {
    var normalized = text
      .Replace("\r\n", " ")
      .Replace("\n", " ")
      .Replace("\r", " ");

    normalized = string.Join(' ', normalized
      .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    if (normalized.Length <= PreviewMaxChars)
      return normalized;

    return normalized[..(PreviewMaxChars - 3)] + "...";
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

    if (!NativeMethods.GetWindowRect(_hwnd, out var r))
      return false;

    return pt.X >= r.Left && pt.X <= r.Right && pt.Y >= r.Top && pt.Y <= r.Bottom;
  }

  private sealed record HistoryItem(string Text, string Preview);
}
