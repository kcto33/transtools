using System.IO;
using System.Text.Json;
using ScreenTranslator.Models;

namespace ScreenTranslator.Services;

public sealed class SettingsService
{
  public AppSettings Settings { get; private set; } = new();

  public string SettingsPath { get; } = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "ScreenTranslator",
    "settings.json");

  public void Load()
  {
    try
    {
      if (!File.Exists(SettingsPath))
      {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        Save();
        return;
      }

      var json = File.ReadAllText(SettingsPath);
      var loaded = JsonSerializer.Deserialize<AppSettings>(json);
      if (loaded is not null)
        Settings = loaded;

      Settings.Providers ??= new();
    }
    catch
    {
      // If settings are corrupted, fall back to defaults.
      Settings = new();
    }
  }

  public void Save()
  {
    Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
    var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(SettingsPath, json);
  }
}
