using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace ScreenTranslator.Services;

public sealed class HotkeyService : IDisposable
{
  private const int WM_HOTKEY = 0x0312;
  private const int HOTKEY_ID = 0xBEEF;

  private const uint MOD_ALT = 0x0001;
  private const uint MOD_CONTROL = 0x0002;
  private const uint MOD_SHIFT = 0x0004;
  private const uint MOD_WIN = 0x0008;
  private const uint MOD_NOREPEAT = 0x4000;

  public event EventHandler? HotkeyPressed;
  private bool _registered;

  public HotkeyService()
  {
    ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;
  }

  public void RegisterDefaultHotkey()
  {
    RegisterHotkey("Ctrl+Alt+T");
  }

  public void RegisterHotkey(string? hotkey)
  {
    var value = string.IsNullOrWhiteSpace(hotkey) ? "Ctrl+Alt+T" : hotkey.Trim();
    if (!TryParseHotkey(value, out var mods, out var vk, out var error))
      throw new InvalidOperationException(error);

    Unregister();

    if (!RegisterHotKey(IntPtr.Zero, HOTKEY_ID, mods | MOD_NOREPEAT, vk))
      throw new InvalidOperationException($"Failed to register hotkey ({value}). It may already be in use.");

    _registered = true;
  }

  private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
  {
    if (msg.message == WM_HOTKEY && msg.wParam.ToInt32() == HOTKEY_ID)
    {
      handled = true;
      HotkeyPressed?.Invoke(this, EventArgs.Empty);
    }
  }

  public void Dispose()
  {
    Unregister();
    ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage;
  }

  private void Unregister()
  {
    if (!_registered)
      return;

    try { UnregisterHotKey(IntPtr.Zero, HOTKEY_ID); } catch { }
    _registered = false;
  }

  private static bool TryParseHotkey(string hotkey, out uint mods, out uint vk, out string error)
  {
    mods = 0;
    vk = 0;
    error = string.Empty;

    if (string.IsNullOrWhiteSpace(hotkey))
    {
      error = "Hotkey is empty.";
      return false;
    }

    var parts = hotkey
      .Split(['+', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
      .Where(p => !string.IsNullOrWhiteSpace(p))
      .ToArray();

    if (parts.Length == 0)
    {
      error = "Hotkey is empty.";
      return false;
    }

    string? keyToken = null;
    foreach (var part in parts)
    {
      if (part.Equals("ctrl", StringComparison.OrdinalIgnoreCase) ||
          part.Equals("control", StringComparison.OrdinalIgnoreCase))
      {
        mods |= MOD_CONTROL;
        continue;
      }

      if (part.Equals("alt", StringComparison.OrdinalIgnoreCase))
      {
        mods |= MOD_ALT;
        continue;
      }

      if (part.Equals("shift", StringComparison.OrdinalIgnoreCase))
      {
        mods |= MOD_SHIFT;
        continue;
      }

      if (part.Equals("win", StringComparison.OrdinalIgnoreCase) ||
          part.Equals("windows", StringComparison.OrdinalIgnoreCase))
      {
        mods |= MOD_WIN;
        continue;
      }

      if (keyToken is not null)
      {
        error = "Only one non-modifier key is allowed.";
        return false;
      }

      keyToken = part;
    }

    if (string.IsNullOrWhiteSpace(keyToken))
    {
      error = "Missing non-modifier key.";
      return false;
    }

    if (mods == 0)
    {
      error = "Hotkey must include at least one modifier (Ctrl/Alt/Shift/Win).";
      return false;
    }

    if (!TryParseKey(keyToken, out var key))
    {
      error = $"Unknown key: {keyToken}.";
      return false;
    }

    vk = (uint)KeyInterop.VirtualKeyFromKey(key);
    if (vk == 0)
    {
      error = $"Unsupported key: {keyToken}.";
      return false;
    }

    return true;
  }

  private static bool TryParseKey(string token, out Key key)
  {
    key = Key.None;
    if (string.IsNullOrWhiteSpace(token))
      return false;

    token = token.Trim();
    if (token.Length == 1)
    {
      var c = char.ToUpperInvariant(token[0]);
      if (c >= 'A' && c <= 'Z')
      {
        key = (Key)Enum.Parse(typeof(Key), c.ToString(), ignoreCase: true);
        return true;
      }

      if (c >= '0' && c <= '9')
      {
        key = (Key)Enum.Parse(typeof(Key), $"D{c}", ignoreCase: true);
        return true;
      }
    }

    try
    {
      var converter = new KeyConverter();
      if (converter.ConvertFromString(token) is Key converted && converted != Key.None)
      {
        key = converted;
        return true;
      }
    }
    catch
    {
      // ignored
    }

    return false;
  }

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
