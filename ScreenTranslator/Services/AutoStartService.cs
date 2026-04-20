using Microsoft.Win32;

namespace ScreenTranslator.Services;

public sealed class AutoStartService
{
  private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
  private const string ValueName = "transtools";
  private const string LegacyValueName = "ScreenTranslator";

  public bool IsEnabled()
  {
    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
    if (key is null)
      return false;

    return HasRunValue(key, ValueName) || HasRunValue(key, LegacyValueName);
  }

  public void Enable(string exePath)
  {
    using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
    var value = $"\"{exePath}\" --tray";
    key.SetValue(ValueName, value);
    key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
  }

  public void Disable()
  {
    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
    key?.DeleteValue(ValueName, throwOnMissingValue: false);
    key?.DeleteValue(LegacyValueName, throwOnMissingValue: false);
  }

  private static bool HasRunValue(RegistryKey key, string name)
  {
    return key.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value);
  }
}
