using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenTranslator.Translation;

/// <summary>
/// Google Cloud Translation API (v2) provider.
/// Docs: https://cloud.google.com/translate/docs/reference/rest/v2/translate
/// </summary>
public sealed class GoogleTranslationProvider : ITranslationProvider
{
  private static readonly HttpClient Http = new()
  {
    Timeout = TimeSpan.FromSeconds(10),
  };

  private readonly string _endpoint;
  private readonly string _apiKey;

  public GoogleTranslationProvider(string? endpoint, string apiKey)
  {
    _endpoint = string.IsNullOrWhiteSpace(endpoint)
      ? "https://translation.googleapis.com/language/translate/v2"
      : endpoint.Trim();
    _apiKey = apiKey;
  }

  public string Id => "google";
  public string DisplayName => "Google Translate";

  public async Task<string> TranslateAsync(string text, string from, string to, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (string.IsNullOrWhiteSpace(text))
      return string.Empty;

    if (string.IsNullOrWhiteSpace(_apiKey))
      throw new InvalidOperationException("Google Translate is not configured. Set API Key in Settings.");

    var sourceLang = MapLanguage(from);
    var targetLang = MapLanguage(to);

    if (string.IsNullOrWhiteSpace(targetLang))
      targetLang = "zh-CN";

    var requestBody = new Dictionary<string, object>
    {
      ["q"] = text.Trim(),
      ["target"] = targetLang,
      ["format"] = "text",
    };

    // Source language is optional (auto-detect if not specified)
    if (!string.IsNullOrWhiteSpace(sourceLang) && !string.Equals(sourceLang, "auto", StringComparison.OrdinalIgnoreCase))
    {
      requestBody["source"] = sourceLang;
    }

    var json = JsonSerializer.Serialize(requestBody);
    using var content = new StringContent(json, Encoding.UTF8, "application/json");

    var url = $"{_endpoint}?key={_apiKey}";
    using var req = new HttpRequestMessage(HttpMethod.Post, url)
    {
      Content = content,
    };

    using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
    var responseJson = await resp.Content.ReadAsStringAsync(ct);

    if (!resp.IsSuccessStatusCode)
    {
      var errorMsg = TryParseGoogleError(responseJson) ?? responseJson;
      throw new InvalidOperationException($"Google Translate HTTP {(int)resp.StatusCode}: {errorMsg}");
    }

    var dto = JsonSerializer.Deserialize<GoogleResponse>(responseJson);
    var translations = dto?.Data?.Translations;
    if (translations is null || translations.Length == 0)
    {
      throw new InvalidOperationException($"Google Translate response parse failed. raw={responseJson}");
    }

    return translations[0].TranslatedText?.Trim() ?? string.Empty;
  }

  private static string? TryParseGoogleError(string json)
  {
    try
    {
      var doc = JsonDocument.Parse(json);
      if (doc.RootElement.TryGetProperty("error", out var error))
      {
        if (error.TryGetProperty("message", out var msg))
          return msg.GetString();
      }
    }
    catch
    {
      // ignore
    }
    return null;
  }

  private static string MapLanguage(string? lang)
  {
    if (string.IsNullOrWhiteSpace(lang))
      return "";

    lang = lang.Trim().ToLowerInvariant();

    return lang switch
    {
      "auto" => "",
      "zh" or "zh-hans" or "zh-cn" or "zh-chs" => "zh-CN",
      "zh-hant" or "zh-tw" or "zh-cht" => "zh-TW",
      "en" => "en",
      "ja" => "ja",
      "ko" => "ko",
      "de" => "de",
      "fr" => "fr",
      "es" => "es",
      "it" => "it",
      "ru" => "ru",
      "pt" => "pt",
      "th" => "th",
      "vi" => "vi",
      _ => lang,
    };
  }

  private sealed class GoogleResponse
  {
    [JsonPropertyName("data")]
    public GoogleData? Data { get; set; }
  }

  private sealed class GoogleData
  {
    [JsonPropertyName("translations")]
    public GoogleTranslation[]? Translations { get; set; }
  }

  private sealed class GoogleTranslation
  {
    [JsonPropertyName("translatedText")]
    public string? TranslatedText { get; set; }

    [JsonPropertyName("detectedSourceLanguage")]
    public string? DetectedSourceLanguage { get; set; }
  }
}
