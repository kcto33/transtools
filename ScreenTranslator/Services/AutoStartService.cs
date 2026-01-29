using Microsoft.Win32;

namespace ScreenTranslator.Services;

public sealed class AutoStartService
{
  private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
  private const string ValueName = "ScreenTranslator";

  public bool IsEnabled()
  {
    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
    return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
  }

  public void Enable(string exePath)
  {
    using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
    var value = $"\"{exePath}\" --tray";
    key.SetValue(ValueName, value);
  }

  public void Disable()
  {
    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
    key?.DeleteValue(ValueName, throwOnMissingValue: false);
  }
}
