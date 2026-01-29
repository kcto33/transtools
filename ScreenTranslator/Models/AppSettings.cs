namespace ScreenTranslator.Models;

public sealed class AppSettings
{
  public string ActiveProviderId { get; set; } = "mock";
  public string DefaultFrom { get; set; } = "en";
  public string DefaultTo { get; set; } = "zh-Hans";
  public string Hotkey { get; set; } = "Ctrl+Alt+T";

  public Dictionary<string, ProviderSettings> Providers { get; set; } = new();
}

public sealed class ProviderSettings
{
  // DPAPI-protected secret (base64) for the provider.
  public string? KeyProtected { get; set; }

  // Some providers (e.g. Youdao) use an app id/key + app secret.
  public string? AppId { get; set; }
  public string? AppSecretProtected { get; set; }

  public string? Endpoint { get; set; }
  public string? Region { get; set; }
}
