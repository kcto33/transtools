using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenTranslator.Translation;

/// <summary>
/// DeepL Translation API provider.
/// Docs: https://developers.deepl.com/docs/api-reference/translate
/// </summary>
public sealed class DeepLTranslationProvider : ITranslationProvider
{
  private static readonly HttpClient Http = new()
  {
    Timeout = TimeSpan.FromSeconds(10),
  };

  private readonly string _endpoint;
  private readonly string _apiKey;

  public DeepLTranslationProvider(string? endpoint, string apiKey)
  {
    // DeepL has two endpoints: free and pro
    // Free: https://api-free.deepl.com
    // Pro: https://api.deepl.com
    _endpoint = string.IsNullOrWhiteSpace(endpoint)
      ? "https://api-free.deepl.com/v2/translate"
      : endpoint.Trim().TrimEnd('/') + "/v2/translate";
    _apiKey = apiKey;
  }

  public string Id => "deepl";
  public string DisplayName => "DeepL";

  public async Task<string> TranslateAsync(string text, string from, string to, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (string.IsNullOrWhiteSpace(text))
      return string.Empty;

    if (string.IsNullOrWhiteSpace(_apiKey))
      throw new InvalidOperationException("DeepL is not configured. Set API Key in Settings.");

    var sourceLang = MapLanguage(from, isSource: true);
    var targetLang = MapLanguage(to, isSource: false);

    var requestBody = new Dictionary<string, object>
    {
      ["text"] = new[] { text.Trim() },
      ["target_lang"] = targetLang,
    };

    // Source language is optional for DeepL (auto-detect if not specified)
    if (!string.IsNullOrWhiteSpace(sourceLang) && !string.Equals(sourceLang, "auto", StringComparison.OrdinalIgnoreCase))
    {
      requestBody["source_lang"] = sourceLang;
    }

    var json = JsonSerializer.Serialize(requestBody);
    using var content = new StringContent(json, Encoding.UTF8, "application/json");

    using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint)
    {
      Content = content,
    };
    req.Headers.Add("Authorization", $"DeepL-Auth-Key {_apiKey}");

    using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
    var responseJson = await resp.Content.ReadAsStringAsync(ct);

    if (!resp.IsSuccessStatusCode)
    {
      throw new InvalidOperationException($"DeepL HTTP {(int)resp.StatusCode}: {responseJson}");
    }

    var dto = JsonSerializer.Deserialize<DeepLResponse>(responseJson);
    if (dto?.Translations is null || dto.Translations.Length == 0)
    {
      throw new InvalidOperationException($"DeepL response parse failed. raw={responseJson}");
    }

    return dto.Translations[0].Text?.Trim() ?? string.Empty;
  }

  private static string MapLanguage(string? lang, bool isSource)
  {
    if (string.IsNullOrWhiteSpace(lang))
      return isSource ? "" : "ZH";

    lang = lang.Trim().ToUpperInvariant();

    return lang switch
    {
      "AUTO" => "",
      "ZH" or "ZH-HANS" or "ZH-CN" or "ZH-CHS" => "ZH",
      "ZH-HANT" or "ZH-TW" or "ZH-CHT" => "ZH", // DeepL doesn't distinguish traditional Chinese in target
      "EN" => "EN",
      "JA" => "JA",
      "KO" => "KO",
      "DE" => "DE",
      "FR" => "FR",
      "ES" => "ES",
      "IT" => "IT",
      "NL" => "NL",
      "PL" => "PL",
      "PT" => "PT",
      "RU" => "RU",
      _ => lang,
    };
  }

  private sealed class DeepLResponse
  {
    [JsonPropertyName("translations")]
    public DeepLTranslation[]? Translations { get; set; }
  }

  private sealed class DeepLTranslation
  {
    [JsonPropertyName("detected_source_language")]
    public string? DetectedSourceLanguage { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
  }
}
