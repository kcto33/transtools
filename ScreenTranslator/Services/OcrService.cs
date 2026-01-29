using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ScreenTranslator.Services;

public sealed class OcrService
{
  private readonly Dictionary<string, OcrEngine> _engines = new(StringComparer.OrdinalIgnoreCase);
  private OcrEngine? _fallback;
  private static readonly string[] AutoFallbackLanguages =
  [
    "zh-Hans",
    "zh-Hant",
    "ja-JP",
    "ko-KR",
    "en",
  ];

  public async Task<string> RecognizeAsync(Bitmap bitmap, string? languageTag, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();

    using var converted = EnsureBgra32(bitmap);
    var softwareBitmap = ToSoftwareBitmap(converted);

    ct.ThrowIfCancellationRequested();
    var tag = NormalizeLanguage(languageTag);
    if (IsAuto(tag))
      return await RecognizeAutoAsync(softwareBitmap, ct);

    var engine = GetEngine(tag);
    var result = await engine.RecognizeAsync(softwareBitmap);

    return (result.Text ?? string.Empty).Trim();
  }

  private async Task<string> RecognizeAutoAsync(SoftwareBitmap softwareBitmap, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();

    var primary = GetFallbackEngine();
    var primaryResult = await primary.RecognizeAsync(softwareBitmap);
    var primaryText = (primaryResult.Text ?? string.Empty).Trim();
    if (!string.IsNullOrWhiteSpace(primaryText))
      return primaryText;

    foreach (var lang in AutoFallbackLanguages)
    {
      ct.ThrowIfCancellationRequested();
      if (!TryGetEngine(lang, out var engine))
        continue;

      var result = await engine.RecognizeAsync(softwareBitmap);
      var text = (result.Text ?? string.Empty).Trim();
      if (!string.IsNullOrWhiteSpace(text))
        return text;
    }

    return string.Empty;
  }

  private OcrEngine GetEngine(string? languageTag)
  {
    var tag = NormalizeLanguage(languageTag);
    if (IsAuto(tag))
      return GetFallbackEngine();

    if (_engines.TryGetValue(tag, out var cached))
      return cached;

    var engine = OcrEngine.TryCreateFromLanguage(new Language(tag))
      ?? throw new InvalidOperationException($"Failed to create OCR engine for language '{tag}'.");

    _engines[tag] = engine;
    return engine;
  }

  private static string NormalizeLanguage(string? tag)
  {
    if (string.IsNullOrWhiteSpace(tag))
      return "auto";

    tag = tag.Trim();

    return tag switch
    {
      "zh" => "zh-Hans",
      "zh-CN" => "zh-Hans",
      "zh-Hans" => "zh-Hans",
      "zh-CHS" => "zh-Hans",
      "zh-TW" => "zh-Hant",
      "zh-Hant" => "zh-Hant",
      "zh-CHT" => "zh-Hant",
      "ja" => "ja-JP",
      "ko" => "ko-KR",
      "fr" => "fr-FR",
      "de" => "de-DE",
      "es" => "es-ES",
      "it" => "it-IT",
      "ru" => "ru-RU",
      "pt" => "pt-BR",
      "ar" => "ar-SA",
      "hi" => "hi-IN",
      "id" => "id-ID",
      "th" => "th-TH",
      "vi" => "vi-VN",
      _ => tag,
    };
  }

  private static bool IsAuto(string? tag) =>
    string.IsNullOrWhiteSpace(tag) || string.Equals(tag, "auto", StringComparison.OrdinalIgnoreCase);

  private OcrEngine GetFallbackEngine()
  {
    return _fallback ??= OcrEngine.TryCreateFromUserProfileLanguages()
      ?? OcrEngine.TryCreateFromLanguage(new Language("en"))
      ?? throw new InvalidOperationException("Failed to create OCR engine.");
  }

  private bool TryGetEngine(string? languageTag, out OcrEngine engine)
  {
    engine = default!;
    var tag = NormalizeLanguage(languageTag);
    if (IsAuto(tag))
    {
      engine = GetFallbackEngine();
      return true;
    }

    if (_engines.TryGetValue(tag, out var cached))
    {
      engine = cached;
      return true;
    }

    var created = OcrEngine.TryCreateFromLanguage(new Language(tag));
    if (created is null)
      return false;

    _engines[tag] = created;
    engine = created;
    return true;
  }

  private static Bitmap EnsureBgra32(Bitmap src)
  {
    if (src.PixelFormat == PixelFormat.Format32bppPArgb || src.PixelFormat == PixelFormat.Format32bppArgb)
      return (Bitmap)src.Clone();

    var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
    using var g = Graphics.FromImage(bmp);
    g.DrawImage(src, 0, 0, src.Width, src.Height);
    return bmp;
  }

  private static SoftwareBitmap ToSoftwareBitmap(Bitmap bmp)
  {
    var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
    var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
    try
    {
      int bytes = Math.Abs(data.Stride) * data.Height;
      var buffer = new byte[bytes];
      Marshal.Copy(data.Scan0, buffer, 0, bytes);

      var sb = new SoftwareBitmap(BitmapPixelFormat.Bgra8, bmp.Width, bmp.Height, BitmapAlphaMode.Premultiplied);
      sb.CopyFromBuffer(buffer.AsBuffer());
      return sb;
    }
    finally
    {
      bmp.UnlockBits(data);
    }
  }
}
