using System.IO;
using System.Text.Json;
using ScreenTranslator.Models;

namespace ScreenTranslator.Services;

public sealed class SettingsService
{
  private const string AppFolderName = "transtools";
  private const string LegacyAppFolderName = "ScreenTranslator";
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
  };

  public AppSettings Settings { get; private set; } = new();

  public string SettingsPath { get; }
  private string LegacySettingsPath { get; }

  public SettingsService()
  {
    var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    SettingsPath = Path.Combine(appDataPath, AppFolderName, "settings.json");
    LegacySettingsPath = Path.Combine(appDataPath, LegacyAppFolderName, "settings.json");
  }

  public void Load()
  {
    try
    {
      var sourcePath = ResolveLoadPath();
      if (sourcePath is null)
      {
        Save();
        return;
      }

      var json = File.ReadAllText(sourcePath);
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

  private string? ResolveLoadPath()
  {
    if (File.Exists(SettingsPath))
      return SettingsPath;

    if (File.Exists(LegacySettingsPath))
      return LegacySettingsPath;

    return null;
  }
}
