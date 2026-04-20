using ScreenTranslator.Models;
using ScreenTranslator.Translation;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreenTranslator.Services;

public sealed class TranslationService
{
  private readonly SettingsService _settings;
  private readonly Func<ITranslationProvider>? _providerFactory;
  private static readonly Regex CompoundWordPattern = new(
    @"(?<=[a-z])(?=[A-Z])|[_\-]+|(?<=[A-Za-z])(?=\d)|(?<=\d)(?=[A-Za-z])",
    RegexOptions.Compiled);
  private static readonly Regex LineBreakPattern = new(@"(\r\n|\r|\n)", RegexOptions.Compiled);
  private static readonly Regex CommandStartPattern = new(
    @"^\s*(dotnet|npm|npx|yarn|pnpm|git|cargo|python|pip|node|go|java|javac|msbuild|pwsh|powershell|cmd)\b",
    RegexOptions.Compiled | RegexOptions.IgnoreCase);

  public TranslationService(SettingsService settings)
  {
    _settings = settings;
  }

  internal TranslationService(SettingsService settings, Func<ITranslationProvider> providerFactory)
  {
    _settings = settings;
    _providerFactory = providerFactory;
  }

  public ITranslationProvider CreateProvider()
  {
    if (_providerFactory is not null)
      return _providerFactory();

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

  public async Task<string> TranslateAsync(string text, string from, string to, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (string.IsNullOrWhiteSpace(text))
      return string.Empty;

    var provider = CreateProvider();
    var normalizedText = text.Trim();
    if (!LineBreakPattern.IsMatch(normalizedText))
      return await TranslateSingleSegmentAsync(provider, normalizedText, from, to, ct);

    var parts = LineBreakPattern.Split(normalizedText);
    var builder = new StringBuilder(normalizedText.Length);
    foreach (var part in parts)
    {
      if (string.IsNullOrEmpty(part))
        continue;

      if (IsLineBreakToken(part) || string.IsNullOrWhiteSpace(part))
      {
        builder.Append(part);
        continue;
      }

      builder.Append(await TranslateSingleSegmentAsync(provider, part, from, to, ct));
    }

    return builder.ToString();
  }

  internal static bool ShouldRetryWithSplitWords(string text, string translated)
  {
    if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(translated))
      return false;

    var normalizedText = text.Trim();
    var normalizedTranslated = translated.Trim();
    if (!string.Equals(normalizedText, normalizedTranslated, StringComparison.OrdinalIgnoreCase))
      return false;

    return HasCompoundWordBoundary(normalizedText);
  }

  internal static string SplitCompoundWords(string text)
  {
    if (string.IsNullOrWhiteSpace(text))
      return string.Empty;

    var normalized = CompoundWordPattern.Replace(text.Trim(), " ");
    return string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
  }

  private static bool HasCompoundWordBoundary(string text)
  {
    return CompoundWordPattern.IsMatch(text) && text.Any(char.IsLetter);
  }

  internal static bool ShouldPreserveOriginalSegment(string text)
  {
    if (string.IsNullOrWhiteSpace(text))
      return false;

    var trimmed = text.Trim();
    if (CommandStartPattern.IsMatch(trimmed))
      return true;

    if (trimmed.Contains('\\')
      || trimmed.Contains('/')
      || trimmed.Contains("--", StringComparison.Ordinal)
      || trimmed.Contains(".csproj", StringComparison.OrdinalIgnoreCase)
      || trimmed.Contains(".sln", StringComparison.OrdinalIgnoreCase)
      || trimmed.Contains(".dll", StringComparison.OrdinalIgnoreCase)
      || trimmed.Contains(".exe", StringComparison.OrdinalIgnoreCase)
      || trimmed.Contains(".json", StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    return false;
  }

  private static bool IsLineBreakToken(string text) =>
    string.Equals(text, "\r\n", StringComparison.Ordinal)
    || string.Equals(text, "\r", StringComparison.Ordinal)
    || string.Equals(text, "\n", StringComparison.Ordinal);

  private static async Task<string> TranslateSingleSegmentAsync(
    ITranslationProvider provider,
    string text,
    string from,
    string to,
    CancellationToken ct)
  {
    if (ShouldPreserveOriginalSegment(text))
      return text;

    string translated;
    try
    {
      translated = await provider.TranslateAsync(text, from, to, ct);
    }
    catch when (ShouldPreserveOriginalSegment(text))
    {
      return text;
    }

    if (!ShouldRetryWithSplitWords(text, translated))
      return translated;

    var splitText = SplitCompoundWords(text);
    if (string.Equals(splitText, text, StringComparison.Ordinal))
      return translated;

    var retried = await provider.TranslateAsync(splitText, from, to, ct);
    return string.IsNullOrWhiteSpace(retried) ? translated : retried;
  }

  private ITranslationProvider CreateYoudao()
  {
    _settings.Settings.Providers.TryGetValue("youdao", out var ps);
    ps ??= new ProviderSettings();

    var endpoint = ps.Endpoint;
    var appId = ps.AppId ?? string.Empty;
    var domain = ps.Domain;
    var rejectFallback = ps.RejectFallback;
    var secret = string.Empty;
    if (!string.IsNullOrWhiteSpace(ps.AppSecretProtected))
    {
      try { secret = SecretProtector.UnprotectString(ps.AppSecretProtected); }
      catch { secret = string.Empty; }
    }

    return new YoudaoTranslationProvider(
      endpoint ?? "https://openapi.youdao.com/api",
      appId,
      secret,
      domain,
      rejectFallback);
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
