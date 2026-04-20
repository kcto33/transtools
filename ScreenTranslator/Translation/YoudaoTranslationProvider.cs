using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenTranslator.Translation;

// Youdao Text Translation API (signType=v3)
// Docs: https://ai.youdao.com/DOCSIRMA/html/trans/api/wbfy/index.html
public sealed class YoudaoTranslationProvider : ITranslationProvider
{
  private static readonly HttpClient SharedHttp = new()
  {
    Timeout = TimeSpan.FromSeconds(6),
  };

  private readonly string _endpoint;
  private readonly string _appId;
  private readonly string _appSecret;
  private readonly string? _domain;
  private readonly bool? _rejectFallback;
  private readonly HttpClient _httpClient;

  public YoudaoTranslationProvider(
    string endpoint,
    string appId,
    string appSecret,
    string? domain = null,
    bool? rejectFallback = null,
    HttpClient? httpClient = null)
  {
    _endpoint = string.IsNullOrWhiteSpace(endpoint) ? "https://openapi.youdao.com/api" : endpoint.Trim();
    _appId = appId;
    _appSecret = appSecret;
    _domain = string.IsNullOrWhiteSpace(domain) ? null : domain.Trim();
    _rejectFallback = rejectFallback;
    _httpClient = httpClient ?? SharedHttp;
  }

  public string Id => "youdao";
  public string DisplayName => "Youdao";

  public async Task<string> TranslateAsync(string text, string from, string to, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (string.IsNullOrWhiteSpace(text))
      return string.Empty;

    if (string.IsNullOrWhiteSpace(_appId) || string.IsNullOrWhiteSpace(_appSecret))
      throw new InvalidOperationException("Youdao is not configured. Set AppId/AppSecret in Settings.");

    var q = text.Trim();
    var salt = Guid.NewGuid().ToString("N");
    var curtime = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

    var ydFrom = MapLang(from, isFrom: true);
    var ydTo = MapLang(to, isFrom: false);

    var sign = ComputeV3Sign(_appId, _appSecret, q, salt, curtime);

    var form = new Dictionary<string, string>
    {
      ["q"] = q,
      ["from"] = ydFrom,
      ["to"] = ydTo,
      ["appKey"] = _appId,
      ["salt"] = salt,
      ["sign"] = sign,
      ["signType"] = "v3",
      ["curtime"] = curtime,
      ["strict"] = "true",
    };

    if (!string.IsNullOrWhiteSpace(_domain))
      form["domain"] = _domain;

    if (_rejectFallback.HasValue)
      form["rejectFallback"] = _rejectFallback.Value ? "true" : "false";

    using var content = new FormUrlEncodedContent(form);

    using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint)
    {
      Content = content,
    };

    using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
    var json = await resp.Content.ReadAsStringAsync(ct);

    if (!resp.IsSuccessStatusCode)
      throw new InvalidOperationException($"Youdao HTTP {(int)resp.StatusCode}: {json}");

    var dto = JsonSerializer.Deserialize<YoudaoResponse>(json);
    if (dto is null)
      throw new InvalidOperationException($"Youdao response parse failed. raw={NormalizeForError(json)}");

    if (!string.Equals(dto.ErrorCode, "0", StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException(BuildErrorMessage(dto, json));

    var translated = dto.Translation is { Length: > 0 }
      ? string.Join("\n", dto.Translation.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()))
      : string.Empty;

    return translated.Trim();
  }

  private static string MapLang(string lang, bool isFrom)
  {
    if (string.IsNullOrWhiteSpace(lang))
      return isFrom ? "auto" : "zh-CHS";

    lang = lang.Trim();

    // Map to Youdao API language codes
    // Reference: https://ai.youdao.com/DOCSIRMA/html/trans/api/wbfy/index.html
    return lang switch
    {
      // Chinese variants
      "zh" => "zh-CHS",
      "zh-Hans" => "zh-CHS",
      "zh-CN" => "zh-CHS",
      "zh-CHS" => "zh-CHS",

      "zh-Hant" => "zh-CHT",
      "zh-TW" => "zh-CHT",
      "zh-CHT" => "zh-CHT",

      // Common languages
      "en" => "en",
      "ja" => "ja",
      "ko" => "ko",
      "ru" => "ru",
      "fr" => "fr",
      "de" => "de",
      "es" => "es",
      "pt" => "pt",
      "it" => "it",
      "ar" => "ar",
      "th" => "th",
      "vi" => "vi",
      "id" => "id",
      "ms" => "ms",
      "nl" => "nl",
      "pl" => "pl",
      "tr" => "tr",
      "uk" => "uk",
      "cs" => "cs",
      "ro" => "ro",
      "hu" => "hu",
      "el" => "el",
      "bg" => "bg",
      "sv" => "sv",
      "da" => "da",
      "fi" => "fi",
      "no" => "no",
      "he" => "he",
      "hi" => "hi",
      "bn" => "bn",

      "auto" => "auto",

      _ => lang,
    };
  }

  private static string ComputeV3Sign(string appId, string appSecret, string q, string salt, string curtime)
  {
    var input = TruncateForV3(q);
    var raw = appId + input + salt + curtime + appSecret;
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
    return Convert.ToHexString(bytes).ToLowerInvariant();
  }

  private static string TruncateForV3(string q)
  {
    if (q.Length <= 20)
      return q;

    return string.Concat(q.AsSpan(0, 10), q.Length.ToString(), q.AsSpan(q.Length - 10));
  }

  private static string BuildErrorMessage(YoudaoResponse dto, string json)
  {
    var parts = new List<string>
    {
      $"Youdao error {dto.ErrorCode ?? "unknown"}",
    };

    if (!string.IsNullOrWhiteSpace(dto.Error))
      parts.Add(dto.Error.Trim());
    else if (!string.IsNullOrWhiteSpace(dto.Message))
      parts.Add(dto.Message.Trim());

    if (!string.IsNullOrWhiteSpace(dto.RequestId))
      parts.Add($"requestId={dto.RequestId.Trim()}");

    parts.Add($"raw={NormalizeForError(json)}");
    return string.Join(" | ", parts);
  }

  private static string NormalizeForError(string value)
  {
    if (string.IsNullOrWhiteSpace(value))
      return "(empty)";

    return value.Trim();
  }

  private sealed class YoudaoResponse
  {
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }
    [JsonPropertyName("error")]
    public string? Error { get; set; }
    [JsonPropertyName("msg")]
    public string? Message { get; set; }
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }
    [JsonPropertyName("translation")]
    public string[]? Translation { get; set; }
    [JsonPropertyName("isDomainSupport")]
    public bool? IsDomainSupport { get; set; }
  }
}
