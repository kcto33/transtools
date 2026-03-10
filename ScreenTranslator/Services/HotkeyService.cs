using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace ScreenTranslator.Services;

public sealed class HotkeyService : IDisposable
{
  private const int WM_HOTKEY = 0x0312;
  public const int DefaultHotkeyId = 0xBEEF;

  private const uint MOD_ALT = 0x0001;
  private const uint MOD_CONTROL = 0x0002;
  private const uint MOD_SHIFT = 0x0004;
  private const uint MOD_WIN = 0x0008;
  private const uint MOD_NOREPEAT = 0x4000;

  public event EventHandler? HotkeyPressed;
  public event EventHandler<int>? HotkeyPressedById;

  private readonly HashSet<int> _registeredIds = new();

  public HotkeyService()
  {
    ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;
  }

  public void RegisterDefaultHotkey()
  {
    RegisterHotkey("Ctrl+Alt+T", DefaultHotkeyId);
  }

  public void RegisterHotkey(string? hotkey)
  {
    RegisterHotkey(hotkey, DefaultHotkeyId);
  }

  public void RegisterHotkey(string? hotkey, int id)
  {
    var value = string.IsNullOrWhiteSpace(hotkey) ? "Ctrl+Alt+T" : hotkey.Trim();
    if (!TryParseHotkey(value, out var mods, out var vk, out var error))
      throw new InvalidOperationException(error);

    Unregister(id);

    if (!RegisterHotKey(IntPtr.Zero, id, mods | MOD_NOREPEAT, vk))
      throw new InvalidOperationException($"Failed to register hotkey ({value}). It may already be in use.");

    _registeredIds.Add(id);
  }

  private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
  {
    if (msg.message == WM_HOTKEY)
    {
      var id = msg.wParam.ToInt32();
      if (_registeredIds.Contains(id))
      {
        handled = true;
        HotkeyPressedById?.Invoke(this, id);
        if (id == DefaultHotkeyId)
          HotkeyPressed?.Invoke(this, EventArgs.Empty);
      }
    }
  }

  public void Dispose()
  {
    UnregisterAll();
    ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage;
  }

  public void UnregisterAll()
  {
    if (_registeredIds.Count == 0)
      return;

    foreach (var id in _registeredIds.ToArray())
    {
      Unregister(id);
    }
  }

  public void Unregister(int id)
  {
    if (!_registeredIds.Contains(id))
      return;

    try { UnregisterHotKey(IntPtr.Zero, id); } catch { }
    _registeredIds.Remove(id);
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

  /// <summary>
  /// Tests if a hotkey can be registered (not in use by other applications).
  /// </summary>
  /// <param name="hotkey">The hotkey string to test (e.g., "Ctrl+Alt+T")</param>
  /// <param name="error">Error message if the hotkey cannot be registered</param>
  /// <returns>True if the hotkey is available, false otherwise</returns>
  public static bool TestHotkeyAvailable(string hotkey, out string error)
  {
    error = string.Empty;

    if (!TryParseHotkey(hotkey, out var mods, out var vk, out var parseError))
    {
      error = parseError;
      return false;
    }

    // Use a temporary ID for testing
    const int testId = 0x7FFF;

    // Try to register
    if (!RegisterHotKey(IntPtr.Zero, testId, mods | MOD_NOREPEAT, vk))
    {
      error = LocalizationService.GetString("Msg_HotkeyConflictOther", "This hotkey is already used by another application.");
      return false;
    }

    // Unregister immediately
    UnregisterHotKey(IntPtr.Zero, testId);
    return true;
  }

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
