using System.IO;
using System.Text.Json;
using ScreenTranslator.Models;

namespace ScreenTranslator.Services;

public sealed class SettingsService
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
  };

  public AppSettings Settings { get; private set; } = new();

  public string SettingsPath { get; } = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "ScreenTranslator",
    "settings.json");

  public void Load()
  {
    try
    {
      var dir = Path.GetDirectoryName(SettingsPath)!;
      if (!Directory.Exists(dir))
      {
        Directory.CreateDirectory(dir);
        Save();
        return;
      }

      if (!File.Exists(SettingsPath))
      {
        Save();
        return;
      }

      var json = File.ReadAllText(SettingsPath);
      var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
      if (loaded is not null)
        Settings = loaded;

      Settings.Providers ??= new();
      Settings.LongScreenshot ??= new();
    }
    catch
    {
      // If settings are corrupted, fall back to defaults.
      Settings = new();
    }
  }

  public void Save()
  {
    try
    {
      var dir = Path.GetDirectoryName(SettingsPath)!;
      if (!Directory.Exists(dir))
        Directory.CreateDirectory(dir);

      var json = JsonSerializer.Serialize(Settings, JsonOptions);
      File.WriteAllText(SettingsPath, json);
    }
    catch
    {
      // Silently ignore save errors to avoid disrupting user experience.
    }
  }

  public async Task SaveAsync(CancellationToken ct = default)
  {
    try
    {
      var dir = Path.GetDirectoryName(SettingsPath)!;
      if (!Directory.Exists(dir))
        Directory.CreateDirectory(dir);

      var json = JsonSerializer.Serialize(Settings, JsonOptions);
      await File.WriteAllTextAsync(SettingsPath, json, ct);
    }
    catch
    {
      // Silently ignore save errors.
    }
  }
}
