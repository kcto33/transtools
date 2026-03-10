using ScreenTranslator.Models;
using ScreenTranslator.Translation;

namespace ScreenTranslator.Services;

public sealed class TranslationService
{
  private readonly SettingsService _settings;

  public TranslationService(SettingsService settings)
  {
    _settings = settings;
  }

  public ITranslationProvider CreateProvider()
  {
    var id = _settings.Settings.ActiveProviderId?.Trim().ToLowerInvariant();

    return id switch
    {
      "youdao" => CreateYoudao(),
      "deepl" => CreateDeepL(),
      "google" => CreateGoogle(),
      "mock" or null or "" => new MockTranslationProvider(),
      _ => new MockTranslationProvider(),
    };
  }

  private ITranslationProvider CreateYoudao()
  {
    _settings.Settings.Providers.TryGetValue("youdao", out var ps);
    ps ??= new ProviderSettings();

    var endpoint = ps.Endpoint;
    var appId = ps.AppId ?? string.Empty;
    var secret = string.Empty;
    if (!string.IsNullOrWhiteSpace(ps.AppSecretProtected))
    {
      try { secret = SecretProtector.UnprotectString(ps.AppSecretProtected); }
      catch { secret = string.Empty; }
    }

    return new YoudaoTranslationProvider(endpoint ?? "https://openapi.youdao.com/api", appId, secret);
  }

  private ITranslationProvider CreateDeepL()
  {
    _settings.Settings.Providers.TryGetValue("deepl", out var ps);
    ps ??= new ProviderSettings();

    var endpoint = ps.Endpoint;
    var apiKey = string.Empty;
    if (!string.IsNullOrWhiteSpace(ps.KeyProtected))
    {
      try { apiKey = SecretProtector.UnprotectString(ps.KeyProtected); }
      catch { apiKey = string.Empty; }
    }

    return new DeepLTranslationProvider(endpoint, apiKey);
  }

  private ITranslationProvider CreateGoogle()
  {
    _settings.Settings.Providers.TryGetValue("google", out var ps);
    ps ??= new ProviderSettings();

    var endpoint = ps.Endpoint;
    var apiKey = string.Empty;
    if (!string.IsNullOrWhiteSpace(ps.KeyProtected))
    {
      try { apiKey = SecretProtector.UnprotectString(ps.KeyProtected); }
      catch { apiKey = string.Empty; }
    }

    return new GoogleTranslationProvider(endpoint, apiKey);
  }
}
