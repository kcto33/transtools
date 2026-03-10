using System.Runtime.InteropServices;

using ScreenTranslator.Interop;

namespace ScreenTranslator.Services;

public sealed class LongScreenshotInputHookService : IDisposable
{
  private IntPtr _mouseHook;
  private IntPtr _keyboardHook;
  private NativeMethods.LowLevelMouseProc? _mouseProc;
  private NativeMethods.LowLevelKeyboardProc? _keyboardProc;

  public event Action? ScrollAttempted;
  public event Action? EscapePressed;

  public void Install()
  {
    if (_mouseHook != IntPtr.Zero || _keyboardHook != IntPtr.Zero)
    {
      return;
    }

    _mouseProc = MouseHook;
    _keyboardProc = KeyboardHook;

    _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, IntPtr.Zero, 0);
    _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, IntPtr.Zero, 0);
  }

  public void Uninstall()
  {
    if (_mouseHook != IntPtr.Zero)
    {
      try
      {
        NativeMethods.UnhookWindowsHookEx(_mouseHook);
      }
      catch
      {
        // best effort
      }

      _mouseHook = IntPtr.Zero;
    }

    if (_keyboardHook != IntPtr.Zero)
    {
      try
      {
        NativeMethods.UnhookWindowsHookEx(_keyboardHook);
      }
      catch
      {
        // best effort
      }

      _keyboardHook = IntPtr.Zero;
    }

    _mouseProc = null;
    _keyboardProc = null;
  }

  public void Dispose()
  {
    Uninstall();
  }

  private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
  {
    if (nCode >= 0)
    {
      var message = wParam.ToInt32();
      if (message == NativeMethods.WM_MOUSEWHEEL || message == NativeMethods.WM_MOUSEHWHEEL)
      {
        try
        {
          var hookData = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
          var delta = unchecked((short)((hookData.mouseData >> 16) & 0xFFFF));
          if (delta != 0)
          {
            ScrollAttempted?.Invoke();
          }
        }
        catch
        {
          ScrollAttempted?.Invoke();
        }
      }
    }

    return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
  }

  private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
  {
    if (nCode >= 0)
    {
      var message = wParam.ToInt32();
      if (message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN)
      {
        var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        if (data.vkCode == NativeMethods.VK_NEXT ||
            data.vkCode == NativeMethods.VK_PRIOR ||
            data.vkCode == NativeMethods.VK_SPACE ||
            data.vkCode == NativeMethods.VK_DOWN ||
            data.vkCode == NativeMethods.VK_UP)
        {
          ScrollAttempted?.Invoke();
        }
        else if (data.vkCode == NativeMethods.VK_ESCAPE)
        {
          EscapePressed?.Invoke();
        }
      }
    }

    return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
  }
}
