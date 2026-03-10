using System.Runtime.InteropServices;

using ScreenTranslator.Interop;
using ScreenTranslator.Windows;

using WpfApplication = System.Windows.Application;

namespace ScreenTranslator.Services;

public sealed class PasteHistoryController : IDisposable
{
  private const uint VK_CONTROL = 0x11;
  private const uint VK_V = 0x56;

  private const uint VK_UP = 0x26;
  private const uint VK_DOWN = 0x28;
  private const uint VK_RETURN = 0x0D;
  private const uint VK_ESCAPE = 0x1B;

  private readonly SettingsService _settings;
  private readonly ClipboardHistoryService _history;

  private PasteHistoryWindow? _window;
  private IntPtr _keyboardHook;
  private NativeMethods.LowLevelKeyboardProc? _keyboardProc;

  public PasteHistoryController(SettingsService settings, ClipboardHistoryService history)
  {
    _settings = settings;
    _history = history;
  }

  public void ShowOrClose()
  {
    WpfApplication.Current.Dispatcher.Invoke(() =>
    {
      if (_window is not null && _window.IsVisible)
      {
        _window.Close();
        return;
      }

      var items = _history.GetRecent();
      if (items.Count == 0)
        return;

      _window ??= CreateWindow();
      _window.SetItems(items);
      _window.ShowAtCursor();

      EnsureKeyboardHook();
    });
  }

  public void Dispose()
  {
    RemoveKeyboardHook();
    if (_window is not null)
    {
      try { _window.Close(); } catch { }
      _window = null;
    }
  }

  private PasteHistoryWindow CreateWindow()
  {
    var w = new PasteHistoryWindow(_settings.Settings.PasteHistoryBubble ?? _settings.Settings.Bubble)
    {
      Topmost = true,
    };
    w.Closed += (_, _) =>
    {
      RemoveKeyboardHook();
      _window = null;
    };
    return w;
  }

  private void EnsureKeyboardHook()
  {
    if (_keyboardHook != IntPtr.Zero)
      return;

    _keyboardProc = LowLevelKeyboardHook;
    _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, IntPtr.Zero, 0);
  }

  private void RemoveKeyboardHook()
  {
    if (_keyboardHook == IntPtr.Zero)
      return;

    try { NativeMethods.UnhookWindowsHookEx(_keyboardHook); } catch { }
    _keyboardHook = IntPtr.Zero;
    _keyboardProc = null;
  }

  private IntPtr LowLevelKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
  {
    if (nCode >= 0 && _window is not null && _window.IsVisible)
    {
      var msg = wParam.ToInt32();
      if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
      {
        var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        var vk = data.vkCode;

        if (vk == VK_UP)
        {
          WpfApplication.Current.Dispatcher.BeginInvoke(() => _window?.MoveSelection(-1));
          return new IntPtr(1);
        }

        if (vk == VK_DOWN)
        {
          WpfApplication.Current.Dispatcher.BeginInvoke(() => _window?.MoveSelection(1));
          return new IntPtr(1);
        }

        if (vk == VK_ESCAPE)
        {
          WpfApplication.Current.Dispatcher.BeginInvoke(() => _window?.Close());
          return new IntPtr(1);
        }

        if (vk == VK_RETURN)
        {
          WpfApplication.Current.Dispatcher.BeginInvoke(async () =>
          {
            var text = _window?.SelectedText;
            if (string.IsNullOrWhiteSpace(text))
            {
              _window?.Close();
              return;
            }

            // Close window first to return focus to the original app
            _window?.Close();

            try
            {
              await _history.SetClipboardTextAsync(text);

              // Delay to ensure focus has returned to the original app
              await Task.Delay(100);

              SendCtrlV();
            }
            catch
            {
              // ignore
            }
          });
          return new IntPtr(1);
        }
      }
    }

    return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
  }

  private static void SendCtrlV()
  {
    var inputs = new NativeMethods.INPUT[4];

    // Ctrl down
    inputs[0].type = (uint)NativeMethods.INPUT_KEYBOARD;
    inputs[0].u.ki.wVk = (ushort)VK_CONTROL;
    inputs[0].u.ki.dwFlags = 0;

    // V down
    inputs[1].type = (uint)NativeMethods.INPUT_KEYBOARD;
    inputs[1].u.ki.wVk = (ushort)VK_V;
    inputs[1].u.ki.dwFlags = 0;

    // V up
    inputs[2].type = (uint)NativeMethods.INPUT_KEYBOARD;
    inputs[2].u.ki.wVk = (ushort)VK_V;
    inputs[2].u.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;

    // Ctrl up
    inputs[3].type = (uint)NativeMethods.INPUT_KEYBOARD;
    inputs[3].u.ki.wVk = (ushort)VK_CONTROL;
    inputs[3].u.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;

    NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
  }
}
